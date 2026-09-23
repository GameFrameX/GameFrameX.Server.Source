//  ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Online.Social;

/// <summary>
/// 好友关系存储的单进程默认实现（持久化与多实例协调归 Server 仓运行时装配，X4）。
/// <para>
/// 维护约束：无向对唯一性与状态 CAS 都在同一临界区内完成——<see cref="SaveIfAbsentAsync"/> 的
/// 「查 + 写」不可拆分，否则并发添加会落出两条记录（VC-6.1 失效）。
/// </para>
/// <para>
/// 天花板（ponytail）：全局单锁 + 全表线性扫描，好友关系上万后需换分片锁 + 按玩家索引；
/// 当前量级下正确性优先。
/// </para>
/// </summary>
public sealed class InMemoryOnlineFriendStore : IOnlineFriendStore
{
    /// <summary>并发保护锁。</summary>
    private readonly object _syncRoot = new object();

    /// <summary>关系表（键 = 作用域 + 无向对）。</summary>
    private readonly Dictionary<string, OnlineFriendship> _friendshipsByPair = new Dictionary<string, OnlineFriendship>(StringComparer.Ordinal);

    /// <summary>关系标识索引。</summary>
    private readonly Dictionary<string, string> _pairKeyByFriendshipId = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// 以内存字典实现无向对的「不存在则创建」：键已存在时返回既有记录的副本且不写入；新建时存入入参的深拷贝并登记关系标识索引。
    /// </summary>
    /// <remarks>
    /// In-memory dictionary based create-if-absent for the undirected pair: returns a copy of the existing record without writing when the key exists; on creation stores a deep copy of the input and registers the friendship-id index.
    /// </remarks>
    /// <param name="friendship">待创建的关系（其无向对与作用域已由调用方规范化） / Friendship to create (undirected pair and scope already canonicalized by the caller)</param>
    /// <param name="cancellationToken">取消令牌（同步内存实现，忽略此参数） / Cancellation token (ignored by this synchronous in-memory implementation)</param>
    /// <returns>当前生效的关系记录副本（既有记录或刚入库的入参副本） / Copy of the effective friendship record (the existing one or the just-stored copy of the input)</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="friendship"/> 为 null 时抛出 / Thrown when <paramref name="friendship"/> is null</exception>
    public Task<OnlineFriendship> SaveIfAbsentAsync(OnlineFriendship friendship, CancellationToken cancellationToken = default)
    {
        if (friendship == null)
        {
            throw new ArgumentNullException(nameof(friendship));
        }

        var pairKey = BuildPairKey(friendship.TenantId, friendship.AppId, friendship.LowPlayerId, friendship.HighPlayerId);
        lock (_syncRoot)
        {
            OnlineFriendship existing;
            if (_friendshipsByPair.TryGetValue(pairKey, out existing))
            {
                return Task.FromResult(existing.Copy());
            }

            // 存档存副本而非调用方实例（调用方复用该实例不会回写库内记录）。
            var stored = friendship.Copy();
            _friendshipsByPair[pairKey] = stored;
            _pairKeyByFriendshipId[friendship.FriendshipId ?? string.Empty] = pairKey;
            return Task.FromResult(stored.Copy());
        }
    }

    /// <summary>
    /// 先把两侧玩家规范化为无向对，再按索引键在内存关系表中查找并返回副本（两个方向的入参命中同一条记录）。
    /// </summary>
    /// <remarks>
    /// Canonicalizes the two players into the undirected pair first, then looks up the in-memory friendship table by index key and returns a copy (either direction of the arguments hits the same record).
    /// </remarks>
    /// <param name="tenantId">租户标识 / Tenant id</param>
    /// <param name="appId">App 标识 / App id</param>
    /// <param name="leftPlayerId">一侧玩家标识（无需预先规范化） / One-side player id (no pre-canonicalization needed)</param>
    /// <param name="rightPlayerId">另一侧玩家标识（无需预先规范化） / Other-side player id (no pre-canonicalization needed)</param>
    /// <param name="cancellationToken">取消令牌（同步内存实现，忽略此参数） / Cancellation token (ignored by this synchronous in-memory implementation)</param>
    /// <returns>关系副本；不存在返回 null / Copy of the friendship; null if absent</returns>
    public Task<OnlineFriendship> FindAsync(long tenantId, long appId, long leftPlayerId, long rightPlayerId, CancellationToken cancellationToken = default)
    {
        long low;
        long high;
        OnlineFriendship.Canonicalize(leftPlayerId, rightPlayerId, out low, out high);
        var pairKey = BuildPairKey(tenantId, appId, low, high);
        lock (_syncRoot)
        {
            OnlineFriendship found;
            if (_friendshipsByPair.TryGetValue(pairKey, out found))
            {
                return Task.FromResult(found.Copy());
            }

            return Task.FromResult<OnlineFriendship>(null);
        }
    }

    /// <summary>
    /// 以 CAS 语义在临界区内改写内存中的关系状态：作用域不符、当前状态与期望不符或记录不存在时不留写入痕迹并返回 null。
    /// </summary>
    /// <remarks>
    /// Rewrites the in-memory friendship state with CAS semantics inside the critical section: leaves no write trace and returns null on scope mismatch, expected-state mismatch or missing record.
    /// </remarks>
    /// <param name="tenantId">租户标识 / Tenant id</param>
    /// <param name="appId">App 标识 / App id</param>
    /// <param name="friendshipId">关系标识 / Friendship id</param>
    /// <param name="expectedState">期望的当前状态；不匹配即失败 / Expected current state; a mismatch fails the call</param>
    /// <param name="newState">目标状态 / Target state</param>
    /// <param name="nowUnixMilliseconds">本次变更时刻（UTC 毫秒） / Change time of this update (UTC milliseconds)</param>
    /// <param name="responded">本次变更是否构成一次答复（答复时回填答复时刻） / Whether this change counts as a response (backfills the responded-at time when true)</param>
    /// <param name="cancellationToken">取消令牌（同步内存实现，忽略此参数） / Cancellation token (ignored by this synchronous in-memory implementation)</param>
    /// <returns>更新后的关系副本；CAS 失败或记录不存在返回 null / Copy of the updated friendship; null on CAS failure or missing record</returns>
    public Task<OnlineFriendship> UpdateStateAsync(long tenantId, long appId, string friendshipId, OnlineFriendshipState expectedState, OnlineFriendshipState newState, long nowUnixMilliseconds, bool responded, CancellationToken cancellationToken = default)
    {
        var lookupKey = friendshipId ?? string.Empty;
        lock (_syncRoot)
        {
            string pairKey;
            if (!_pairKeyByFriendshipId.TryGetValue(lookupKey, out pairKey))
            {
                return Task.FromResult<OnlineFriendship>(null);
            }

            OnlineFriendship current;
            if (!_friendshipsByPair.TryGetValue(pairKey, out current))
            {
                return Task.FromResult<OnlineFriendship>(null);
            }

            if (current.TenantId != tenantId || current.AppId != appId)
            {
                return Task.FromResult<OnlineFriendship>(null);
            }

            if (current.State != expectedState)
            {
                // CAS 失败：不留任何写入痕迹，调用方据 null 判定「状态已被并发改写」。
                return Task.FromResult<OnlineFriendship>(null);
            }

            current.State = newState;
            current.UpdatedAtTime = nowUnixMilliseconds;
            if (responded)
            {
                current.RespondedAtTime = nowUnixMilliseconds;
            }

            return Task.FromResult(current.Copy());
        }
    }

    /// <summary>
    /// 以 CAS 语义在同一临界区内把静止态关系重新发起为待答复请求：状态、方向与有效期三项一并落到同一条记录。
    /// </summary>
    /// <remarks>
    /// Re-initiates a quiescent friendship as a pending request with CAS semantics, writing state, direction and expiry together onto the same record within one critical section.
    /// </remarks>
    /// <param name="tenantId">租户标识 / Tenant id</param>
    /// <param name="appId">App 标识 / App id</param>
    /// <param name="friendshipId">关系标识 / Friendship id</param>
    /// <param name="expectedState">期望的当前状态（静止态）；不匹配即失败 / Expected current state (quiescent); a mismatch fails the call</param>
    /// <param name="requesterId">本次发起人（写入为新的方向事实） / Initiator of this round (written as the new direction fact)</param>
    /// <param name="addresseeId">本次被请求方 / Addressee of this round</param>
    /// <param name="nowUnixMilliseconds">本次变更时刻（UTC 毫秒） / Change time of this update (UTC milliseconds)</param>
    /// <param name="expiresAtTime">重置后的请求失效时刻（UTC 毫秒） / Reset request expiry time (UTC milliseconds)</param>
    /// <param name="cancellationToken">取消令牌（同步内存实现，忽略此参数） / Cancellation token (ignored by this synchronous in-memory implementation)</param>
    /// <returns>更新后的关系副本；CAS 失败或记录不存在返回 null / Copy of the updated friendship; null on CAS failure or missing record</returns>
    public Task<OnlineFriendship> RenewRequestAsync(long tenantId, long appId, string friendshipId, OnlineFriendshipState expectedState, long requesterId, long addresseeId, long nowUnixMilliseconds, long expiresAtTime, CancellationToken cancellationToken = default)
    {
        var lookupKey = friendshipId ?? string.Empty;
        lock (_syncRoot)
        {
            string pairKey;
            if (!_pairKeyByFriendshipId.TryGetValue(lookupKey, out pairKey))
            {
                return Task.FromResult<OnlineFriendship>(null);
            }

            OnlineFriendship current;
            if (!_friendshipsByPair.TryGetValue(pairKey, out current))
            {
                return Task.FromResult<OnlineFriendship>(null);
            }

            if (current.TenantId != tenantId || current.AppId != appId || current.State != expectedState)
            {
                return Task.FromResult<OnlineFriendship>(null);
            }

            // 三项同临界区落定：状态 + 方向 + 有效期（拆开写会让权限判据读到半成品）。
            current.State = OnlineFriendshipState.Requested;
            current.RequesterId = requesterId;
            current.AddresseeId = addresseeId;
            current.UpdatedAtTime = nowUnixMilliseconds;
            current.ExpiresAtTime = expiresAtTime;
            return Task.FromResult(current.Copy());
        }
    }

    /// <summary>
    /// 临界区内全表线性扫描列出作用域内包含指定玩家的全部关系记录（任意状态）。
    /// </summary>
    /// <remarks>
    /// Lists all friendship records in the scope containing the given player (any state) via a full-table linear scan inside the critical section.
    /// </remarks>
    /// <param name="tenantId">租户标识 / Tenant id</param>
    /// <param name="appId">App 标识 / App id</param>
    /// <param name="playerId">玩家标识 / Player id</param>
    /// <param name="cancellationToken">取消令牌（同步内存实现，忽略此参数） / Cancellation token (ignored by this synchronous in-memory implementation)</param>
    /// <returns>关系副本列表 / List of friendship copies</returns>
    public Task<IReadOnlyList<OnlineFriendship>> ListByPlayerAsync(long tenantId, long appId, long playerId, CancellationToken cancellationToken = default)
    {
        var result = new List<OnlineFriendship>();
        lock (_syncRoot)
        {
            foreach (var pair in _friendshipsByPair)
            {
                var friendship = pair.Value;
                if (friendship.TenantId != tenantId || friendship.AppId != appId)
                {
                    continue;
                }

                if (friendship.Contains(playerId))
                {
                    result.Add(friendship.Copy());
                }
            }
        }

        return Task.FromResult<IReadOnlyList<OnlineFriendship>>(result);
    }

    /// <summary>
    /// 临界区内全表线性扫描列出作用域内处于指定状态的全部关系记录。
    /// </summary>
    /// <remarks>
    /// Lists all friendship records in the scope with the given state via a full-table linear scan inside the critical section.
    /// </remarks>
    /// <param name="tenantId">租户标识 / Tenant id</param>
    /// <param name="appId">App 标识 / App id</param>
    /// <param name="state">关系状态 / Friendship state</param>
    /// <param name="cancellationToken">取消令牌（同步内存实现，忽略此参数） / Cancellation token (ignored by this synchronous in-memory implementation)</param>
    /// <returns>关系副本列表 / List of friendship copies</returns>
    public Task<IReadOnlyList<OnlineFriendship>> ListByStateAsync(long tenantId, long appId, OnlineFriendshipState state, CancellationToken cancellationToken = default)
    {
        var result = new List<OnlineFriendship>();
        lock (_syncRoot)
        {
            foreach (var pair in _friendshipsByPair)
            {
                var friendship = pair.Value;
                if (friendship.TenantId == tenantId && friendship.AppId == appId && friendship.State == state)
                {
                    result.Add(friendship.Copy());
                }
            }
        }

        return Task.FromResult<IReadOnlyList<OnlineFriendship>>(result);
    }

    /// <summary>
    /// 构造无向对索引键（作用域 + 规范化玩家对）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="lowPlayerId">低标识侧玩家。</param>
    /// <param name="highPlayerId">高标识侧玩家。</param>
    /// <returns>索引键。</returns>
    private static string BuildPairKey(long tenantId, long appId, long lowPlayerId, long highPlayerId)
    {
        return "friend:" + tenantId + ":" + appId + ":" + lowPlayerId + ":" + highPlayerId;
    }
}
