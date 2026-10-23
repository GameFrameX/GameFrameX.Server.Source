// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses!
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the full license text in the root directory's LICENSE file.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes and liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担任何责任。
//   shall be borne by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://www.cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Capability.Online.Persistence.Social;

using GameFrameX.Capability.Online.Social;
using GameFrameX.DataBase;

/// <summary>
/// 好友关系 GameDb 持久化适配。
/// <para>
/// 数据生命周期语义：行物理长存——适配层零删除调用（接受 / 拒绝 / 过期 / 删除 / 重新发起均以
/// 状态字段覆盖写表达，与 InMemory 全量驻留语义同构）；无向对唯一性由物化复合键单列唯一索引 +
/// <c>TryAddUniqueAsync</c> 承接。
/// </para>
/// <para>
/// 与 Identity store 的关键差异（store 契约红线）：<see cref="SaveIfAbsentAsync"/> 的唯一插入冲突
/// 兜底是**读回既有行返回副本、绝不覆盖**——「键已存在返回既有且不写入」，并发连发的好友请求里
/// 只有第一条能创建记录，其余拿到同一条既有记录（Identity 域的「落者覆盖胜者」语义在此不适用，
/// 好友关系的既有字段是方向与状态事实，后到者无权改写）。
/// </para>
/// <para>
/// CAS 语义（继承 store 契约）：<see cref="UpdateStateAsync"/> / <see cref="RenewRequestAsync"/>
/// 按「读行 → 校验 → 覆盖写」执行，期望状态不匹配即整体失败返回 null，绝不部分应用；
/// 多实例并发收敛依赖数据库唯一索引与行级主键覆盖写（先到临界区者生效）。
/// </para>
/// </summary>
public sealed class GameDbOnlineFriendStore : IOnlineFriendStore
{
    /// <summary>
    /// 好友关系行的行 Id 分配序列名（无自然主键实体；键控 = 状态类型名 + 序列名）。
    /// </summary>
    private const string FriendshipRowIdSequence = "row-id";

    /// <summary>
    /// 以无向对物化复合键做「不存在则创建」：键已存在时返回既有行的副本且**不写入**（store 契约红线，
    /// 与 Identity 域的冲突覆盖语义不同）。
    /// </summary>
    /// <remarks>
    /// Create-if-absent keyed by the materialized composite key of the undirected pair: when the key already exists, returns a copy of the existing row and never overwrites it (a store-contract red line, unlike the identity domain's conflict-overwrite semantics).
    /// </remarks>
    /// <param name="friendship">待创建的关系（其无向对与作用域已由调用方规范化） / Friendship to create (undirected pair and scope already canonicalized by the caller)</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>当前生效的关系记录副本（既有记录或刚落库的入参） / Copy of the effective friendship record (the existing one or the just-persisted input)</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="friendship"/> 为 null 时抛出 / Thrown when <paramref name="friendship"/> is null</exception>
    public async Task<OnlineFriendship> SaveIfAbsentAsync(OnlineFriendship friendship, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(friendship);

        var uniqueKey = BuildPairKey(friendship.TenantId, friendship.AppId, friendship.LowPlayerId, friendship.HighPlayerId);
        var existing = await FindRowAsync(uniqueKey, cancellationToken).ConfigureAwait(false);
        if (existing != null)
        {
            // 键已存在：返回既有行副本，绝不覆盖（后到者无权改写既有方向与状态事实）。
            return ToEntity(existing);
        }

        var rowId = await GameDb.NextSequenceAsync<OnlineFriendshipRowState>(FriendshipRowIdSequence, cancellationToken).ConfigureAwait(false);
        var row = ToState(friendship, uniqueKey, rowId);
        if (await GameDb.TryAddUniqueAsync(row, state => state.UniqueKey == uniqueKey, cancellationToken).ConfigureAwait(false))
        {
            return ToEntity(row);
        }

        // 唯一插入冲突（并发窗口的落者）：读回胜者行返回副本——与 Identity store 不同，此处不覆盖。
        var winner = await FindRowAsync(uniqueKey, cancellationToken).ConfigureAwait(false);
        if (winner == null)
        {
            // 唯一插入失败但读不到既有行 = 数据库状态自相矛盾（唯一索引可见性异常），fail fast 而非静默丢写。
            throw new InvalidOperationException("唯一插入冲突后未能读回既有行：" + uniqueKey);
        }

        return ToEntity(winner);
    }

    /// <summary>
    /// 先把两侧玩家规范化为无向对，再按物化复合键查找并返回副本（两个方向的入参命中同一条记录）。
    /// </summary>
    /// <remarks>
    /// Canonicalizes the two players into the undirected pair first, then looks up by the materialized composite key and returns a copy (either direction of the arguments hits the same record).
    /// </remarks>
    /// <param name="tenantId">租户标识 / Tenant id</param>
    /// <param name="appId">App 标识 / App id</param>
    /// <param name="leftPlayerId">一侧玩家标识（无需预先规范化） / One-side player id (no pre-canonicalization needed)</param>
    /// <param name="rightPlayerId">另一侧玩家标识（无需预先规范化） / Other-side player id (no pre-canonicalization needed)</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>关系副本；不存在返回 null / Copy of the friendship; null if absent</returns>
    public async Task<OnlineFriendship> FindAsync(long tenantId, long appId, long leftPlayerId, long rightPlayerId, CancellationToken cancellationToken = default)
    {
        long low;
        long high;
        OnlineFriendship.Canonicalize(leftPlayerId, rightPlayerId, out low, out high);
        var state = await FindRowAsync(BuildPairKey(tenantId, appId, low, high), cancellationToken).ConfigureAwait(false);
        return state == null ? null : ToEntity(state);
    }

    /// <summary>
    /// 以 CAS 语义改写关系状态：按关系标识定位行，作用域或当前状态与期望不符（或记录不存在）时不留写入痕迹并返回 null。
    /// </summary>
    /// <remarks>
    /// Rewrites the friendship state with CAS semantics: locates the row by friendship id and leaves no write trace, returning null on scope mismatch, expected-state mismatch or missing record.
    /// </remarks>
    /// <param name="tenantId">租户标识 / Tenant id</param>
    /// <param name="appId">App 标识 / App id</param>
    /// <param name="transition">状态迁移载荷（关系定位 + 期望/目标状态 + 变更时刻与答复标记） / State transition payload (friendship locator + expected/target states + change time and responded flag)</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>更新后的关系副本；CAS 失败或记录不存在返回 null / Copy of the updated friendship; null on CAS failure or missing record</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="transition"/> 为 null 时抛出 / Thrown when <paramref name="transition"/> is null</exception>
    public async Task<OnlineFriendship> UpdateStateAsync(long tenantId, long appId, FriendshipStateTransition transition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transition);

        var current = await FindRowByIdAsync(transition.FriendshipId ?? string.Empty, cancellationToken).ConfigureAwait(false);
        if (current == null)
        {
            return null;
        }

        if (current.TenantId != tenantId || current.AppId != appId)
        {
            return null;
        }

        if ((OnlineFriendshipState)current.State != transition.ExpectedState)
        {
            // CAS 失败：不留任何写入痕迹，调用方据 null 判定「状态已被并发改写」。
            return null;
        }

        current.State = (int)transition.NewState;
        current.UpdatedAtTime = transition.NowUnixMilliseconds;
        if (transition.Responded)
        {
            current.RespondedAtTime = transition.NowUnixMilliseconds;
        }

        await GameDb.AddOrUpdateAsync(current, cancellationToken).ConfigureAwait(false);
        return ToEntity(current);
    }

    /// <summary>
    /// 以 CAS 语义把静止态关系重新发起为待答复请求：状态、方向与有效期三项一并落到同一行。
    /// </summary>
    /// <remarks>
    /// Re-initiates a quiescent friendship as a pending request with CAS semantics, writing state, direction and expiry together onto the same row.
    /// </remarks>
    /// <param name="tenantId">租户标识 / Tenant id</param>
    /// <param name="appId">App 标识 / App id</param>
    /// <param name="renewal">重新发起载荷（关系标识、期望状态、发起人、被请求方、变更时刻、失效时刻） / Renewal payload (friendship id, expected state, requester, addressee, change time, expiry)</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>更新后的关系副本；CAS 失败或记录不存在返回 null / Copy of the updated friendship; null on CAS failure or missing record</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="renewal"/> 为 null 时抛出 / Thrown when <paramref name="renewal"/> is null</exception>
    public async Task<OnlineFriendship> RenewRequestAsync(long tenantId, long appId, FriendshipRenewal renewal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(renewal);

        var current = await FindRowByIdAsync(renewal.FriendshipId ?? string.Empty, cancellationToken).ConfigureAwait(false);
        if (current == null)
        {
            return null;
        }

        if (current.TenantId != tenantId || current.AppId != appId || (OnlineFriendshipState)current.State != renewal.ExpectedState)
        {
            return null;
        }

        // 三项同临界区落定：状态 + 方向 + 有效期（拆开写会让权限判据读到半成品）。
        current.State = (int)OnlineFriendshipState.Requested;
        current.RequesterId = renewal.RequesterId;
        current.AddresseeId = renewal.AddresseeId;
        current.UpdatedAtTime = renewal.NowUnixMilliseconds;
        current.ExpiresAtTime = renewal.ExpiresAtTime;
        await GameDb.AddOrUpdateAsync(current, cancellationToken).ConfigureAwait(false);
        return ToEntity(current);
    }

    /// <summary>
    /// 列出作用域内包含指定玩家的全部关系记录（任意状态；双侧等值匹配 + Tenant/App 过滤）。
    /// </summary>
    /// <remarks>
    /// Lists all friendship records in the scope containing the given player (any state; both-side equality plus tenant/app filtering).
    /// </remarks>
    /// <param name="tenantId">租户标识 / Tenant id</param>
    /// <param name="appId">App 标识 / App id</param>
    /// <param name="playerId">玩家标识 / Player id</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>关系副本列表 / List of friendship copies</returns>
    public async Task<IReadOnlyList<OnlineFriendship>> ListByPlayerAsync(long tenantId, long appId, long playerId, CancellationToken cancellationToken = default)
    {
        var states = await GameDb.FindListAsync<OnlineFriendshipRowState>(
            state => state.TenantId == tenantId && state.AppId == appId && (state.LowPlayerId == playerId || state.HighPlayerId == playerId),
            cancellationToken).ConfigureAwait(false);
        return states.Select(ToEntity).ToList();
    }

    /// <summary>
    /// 列出作用域内处于指定状态的全部关系记录（状态 + Tenant/App 过滤）。
    /// </summary>
    /// <remarks>
    /// Lists all friendship records in the scope with the given state (state plus tenant/app filtering).
    /// </remarks>
    /// <param name="tenantId">租户标识 / Tenant id</param>
    /// <param name="appId">App 标识 / App id</param>
    /// <param name="state">关系状态 / Friendship state</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>关系副本列表 / List of friendship copies</returns>
    public async Task<IReadOnlyList<OnlineFriendship>> ListByStateAsync(long tenantId, long appId, OnlineFriendshipState state, CancellationToken cancellationToken = default)
    {
        var stateValue = (int)state;
        var states = await GameDb.FindListAsync<OnlineFriendshipRowState>(
            state => state.TenantId == tenantId && state.AppId == appId && state.State == stateValue,
            cancellationToken).ConfigureAwait(false);
        return states.Select(ToEntity).ToList();
    }

    /// <summary>
    /// 按物化复合键查找好友关系行。
    /// </summary>
    /// <param name="uniqueKey">物化复合键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>关系行；不存在返回 null。</returns>
    private static Task<OnlineFriendshipRowState> FindRowAsync(string uniqueKey, CancellationToken cancellationToken)
    {
        return GameDb.FindAsync<OnlineFriendshipRowState>(state => state.UniqueKey == uniqueKey, isCreateIfNotExists: false, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// 按关系标识查找好友关系行。
    /// </summary>
    /// <param name="friendshipId">关系标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>关系行；不存在返回 null。</returns>
    private static Task<OnlineFriendshipRowState> FindRowByIdAsync(string friendshipId, CancellationToken cancellationToken)
    {
        return GameDb.FindAsync<OnlineFriendshipRowState>(state => state.FriendshipId == friendshipId, isCreateIfNotExists: false, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// 构造无向对唯一键（与 InMemory BuildPairKey 同构）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="lowPlayerId">低标识侧玩家。</param>
    /// <param name="highPlayerId">高标识侧玩家。</param>
    /// <returns>复合键字符串。</returns>
    private static string BuildPairKey(long tenantId, long appId, long lowPlayerId, long highPlayerId)
    {
        return "friend:" + tenantId + ":" + appId + ":" + lowPlayerId + ":" + highPlayerId;
    }

    /// <summary>
    /// 实体 → 行（指定行 Id；全字段物化，唯一键由字段派生）。
    /// </summary>
    /// <param name="friendship">关系实体。</param>
    /// <param name="uniqueKey">物化复合键。</param>
    /// <param name="rowId">行 Id。</param>
    /// <returns>关系行。</returns>
    private static OnlineFriendshipRowState ToState(OnlineFriendship friendship, string uniqueKey, long rowId)
    {
        return new OnlineFriendshipRowState
        {
            Id = rowId,
            UniqueKey = uniqueKey,
            FriendshipId = friendship.FriendshipId ?? string.Empty,
            TenantId = friendship.TenantId,
            AppId = friendship.AppId,
            ServerId = friendship.ServerId,
            LowPlayerId = friendship.LowPlayerId,
            HighPlayerId = friendship.HighPlayerId,
            RequesterId = friendship.RequesterId,
            AddresseeId = friendship.AddresseeId,
            State = (int)friendship.State,
            CreatedAtTime = friendship.CreatedAtTime,
            UpdatedAtTime = friendship.UpdatedAtTime,
            ExpiresAtTime = friendship.ExpiresAtTime,
            RespondedAtTime = friendship.RespondedAtTime,
        };
    }

    /// <summary>
    /// 行 → 实体（防御性拷贝：行是引用对象，返回前复制值，调用方改动不回写存储）。
    /// </summary>
    /// <param name="state">关系行。</param>
    /// <returns>关系实体副本。</returns>
    private static OnlineFriendship ToEntity(OnlineFriendshipRowState state)
    {
        return new OnlineFriendship
        {
            FriendshipId = state.FriendshipId,
            TenantId = state.TenantId,
            AppId = state.AppId,
            ServerId = state.ServerId,
            LowPlayerId = state.LowPlayerId,
            HighPlayerId = state.HighPlayerId,
            RequesterId = state.RequesterId,
            AddresseeId = state.AddresseeId,
            State = (OnlineFriendshipState)state.State,
            CreatedAtTime = state.CreatedAtTime,
            UpdatedAtTime = state.UpdatedAtTime,
            ExpiresAtTime = state.ExpiresAtTime,
            RespondedAtTime = state.RespondedAtTime,
        };
    }
}
