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

using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Online.Contracts;
using GameFrameX.Online.Events;
using GameFrameX.Online.Scope;

/// <summary>
/// 好友服务（vault:C7 S6.2：好友关系生命周期的唯一写者）。
/// <para>
/// 维护约束（红线）：
/// ① **同一对玩家在任意时刻只有一条关系记录、一个状态**——由存储层的无向对唯一键保证，
/// 服务层不得先查后建（那会退化成「查完再写」的竞态），重复请求一律走
/// <see cref="IOnlineFriendStore.SaveIfAbsentAsync"/> 收敛（VC-6.1）；
/// ② 状态迁移唯一判据是 <see cref="OnlineFriendshipStateMachine.TryTransition"/>，
/// 表外迁移映射 <see cref="OnlineErrorCode.StateOperationForbidden"/>（VC-6.2）；
/// ③ 答复权限只属于**被请求方**（<see cref="OnlineFriendship.AddresseeId"/>），非被请求方一律
/// <see cref="OnlineErrorCode.ResourceNotFound"/>（反预言，不泄露他人关系存在性）；
/// ④ 反向重复请求**不自动接受**——契约未定义「互相请求即成为好友」的语义，本服务不发明行为，
/// 一律返回既有待答复请求；
/// ⑤ 在线状态不入库（见 <see cref="IOnlineSocialPresenceProbe"/>）。
/// </para>
/// </summary>
public sealed class OnlineFriendService
{
    /// <summary>关系存储。</summary>
    private readonly IOnlineFriendStore _store;

    /// <summary>事件发布出口。</summary>
    private readonly IOnlineEventPublisher _eventPublisher;

    /// <summary>玩家目录（可空：未装配时搜索返回空、展示名回退为标识字面量）。</summary>
    private readonly IOnlinePlayerDirectory _directory;

    /// <summary>在线事实探针（可空：未装配时在线状态恒为「未知」）。</summary>
    private readonly IOnlineSocialPresenceProbe _presenceProbe;

    /// <summary>待答复请求存活时长（秒）。</summary>
    private readonly long _requestTimeToLiveSeconds;

    /// <summary>搜索返回条数上限。</summary>
    private readonly int _searchLimit;

    /// <summary>好友列表返回条数上限。</summary>
    private readonly int _friendLimit;

    /// <summary>
    /// 初始化 <see cref="OnlineFriendService"/>。
    /// </summary>
    /// <param name="store">关系存储。</param>
    /// <param name="eventPublisher">事件发布出口。</param>
    /// <param name="directory">玩家目录（可空）。</param>
    /// <param name="presenceProbe">在线事实探针（可空）。</param>
    /// <param name="requestTimeToLiveSeconds">待答复请求存活时长（秒；默认 604800 即 7 天）。</param>
    /// <param name="searchLimit">搜索返回条数上限（默认 20）。</param>
    /// <param name="friendLimit">好友列表返回条数上限（默认 200）。</param>
    public OnlineFriendService(
        IOnlineFriendStore store,
        IOnlineEventPublisher eventPublisher,
        IOnlinePlayerDirectory directory = null,
        IOnlineSocialPresenceProbe presenceProbe = null,
        long requestTimeToLiveSeconds = 604800,
        int searchLimit = 20,
        int friendLimit = 200)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        _directory = directory;
        _presenceProbe = presenceProbe;
        _requestTimeToLiveSeconds = requestTimeToLiveSeconds > 0 ? requestTimeToLiveSeconds : 604800;
        _searchLimit = searchLimit > 0 ? searchLimit : 20;
        _friendLimit = friendLimit > 0 ? friendLimit : 200;
    }

    /// <summary>
    /// 按名称关键字搜索玩家（不返回自己）。
    /// <para>
    /// 维护约束：搜索结果**只含目录最小字段集**（标识 / 展示名 / 归属区服），不含身份域字段；
    /// 未装配玩家目录时返回空列表而非报错——搜索不可用不应阻断其他社交能力。
    /// </para>
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="keyword">名称关键字。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>匹配的玩家目录条目。</returns>
    public async Task<OnlineResult<IReadOnlyList<OnlinePlayerDirectoryEntry>>> SearchAsync(OnlineScope scope, string keyword, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope<IReadOnlyList<OnlinePlayerDirectoryEntry>>(scope);
        if (failure != null)
        {
            return failure;
        }

        if (string.IsNullOrWhiteSpace(keyword))
        {
            return OnlineResult<IReadOnlyList<OnlinePlayerDirectoryEntry>>.Fail(OnlineErrorCode.ParameterInvalid, "搜索关键字不能为空");
        }

        if (_directory == null)
        {
            return OnlineResult<IReadOnlyList<OnlinePlayerDirectoryEntry>>.Ok(new List<OnlinePlayerDirectoryEntry>());
        }

        var found = await _directory.SearchByNameAsync(scope.TenantId, scope.AppId, keyword.Trim(), _searchLimit, cancellationToken).ConfigureAwait(false);
        var result = new List<OnlinePlayerDirectoryEntry>();
        foreach (var entry in found)
        {
            if (entry.PlayerId != scope.PlayerId)
            {
                result.Add(entry);
            }
        }

        return OnlineResult<IReadOnlyList<OnlinePlayerDirectoryEntry>>.Ok(result);
    }

    /// <summary>
    /// 发起好友请求（重复请求幂等：返回既有关系而非新建，也不报错）。
    /// <para>
    /// 收敛语义（VC-6.1 / VC-6.2）：
    /// ① 无记录 → 创建待答复请求（并发连发时只有第一条创建成功，其余拿到同一条）；
    /// ② 已有待答复请求 → 直接返回该请求（无论方向，避免「同一对玩家两条待答复」）；
    /// ③ 已是好友 → 返回既有关系（幂等，不降级为待答复）；
    /// ④ 处于静止态（拒绝 / 过期 / 已删除）→ CAS 重新发起，方向翻转为本次发起人。
    /// </para>
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="targetPlayerId">目标玩家标识。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>生效的关系记录。</returns>
    public async Task<OnlineResult<OnlineFriendship>> RequestAsync(OnlineScope scope, long targetPlayerId, string correlationId = null, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope<OnlineFriendship>(scope);
        if (failure != null)
        {
            return failure;
        }

        if (targetPlayerId <= 0)
        {
            return OnlineResult<OnlineFriendship>.Fail(OnlineErrorCode.ParameterInvalid, "目标玩家标识无效");
        }

        if (targetPlayerId == scope.PlayerId)
        {
            return OnlineResult<OnlineFriendship>.Fail(OnlineErrorCode.ParameterInvalid, "不能添加自己为好友");
        }

        var now = Now();
        var existing = await _store.FindAsync(scope.TenantId, scope.AppId, scope.PlayerId, targetPlayerId, cancellationToken).ConfigureAwait(false);
        if (existing != null)
        {
            return await ConvergeExistingAsync(existing, scope, correlationId, now, cancellationToken).ConfigureAwait(false);
        }

        long low;
        long high;
        OnlineFriendship.Canonicalize(scope.PlayerId, targetPlayerId, out low, out high);
        var prototype = new OnlineFriendship
        {
            FriendshipId = "frd-" + Guid.NewGuid().ToString("N"),
            TenantId = scope.TenantId,
            AppId = scope.AppId,
            ServerId = scope.ServerId,
            LowPlayerId = low,
            HighPlayerId = high,
            RequesterId = scope.PlayerId,
            AddresseeId = targetPlayerId,
            State = OnlineFriendshipState.Requested,
            CreatedAtTime = now,
            UpdatedAtTime = now,
            ExpiresAtTime = now + (_requestTimeToLiveSeconds * 1000),
        };

        // 并发连发的收敛点：先到者创建，后到者拿到同一条既有记录（存储层原子）。
        var effective = await _store.SaveIfAbsentAsync(prototype, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(effective.FriendshipId, prototype.FriendshipId, StringComparison.Ordinal))
        {
            return await ConvergeExistingAsync(effective, scope, correlationId, now, cancellationToken).ConfigureAwait(false);
        }

        await _eventPublisher.PublishAsync(OnlineSocialEvents.CreateFriendshipChanged(effective, OnlineSocialEvents.FriendshipRequested, correlationId), cancellationToken).ConfigureAwait(false);
        return OnlineResult<OnlineFriendship>.Ok(effective);
    }

    /// <summary>
    /// 接受好友请求（仅被请求方本人可答复）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="friendshipId">关系标识。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>接受后的关系。</returns>
    public Task<OnlineResult<OnlineFriendship>> AcceptAsync(OnlineScope scope, string friendshipId, string correlationId = null, CancellationToken cancellationToken = default)
    {
        return AnswerAsync(scope, friendshipId, true, correlationId, cancellationToken);
    }

    /// <summary>
    /// 拒绝好友请求（仅被请求方本人可答复；拒绝后该对玩家可重新发起）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="friendshipId">关系标识。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>拒绝后的关系。</returns>
    public Task<OnlineResult<OnlineFriendship>> RejectAsync(OnlineScope scope, string friendshipId, string correlationId = null, CancellationToken cancellationToken = default)
    {
        return AnswerAsync(scope, friendshipId, false, correlationId, cancellationToken);
    }

    /// <summary>
    /// 删除好友（关系双方均可发起；删除后该对玩家可重新添加，VC-6.2）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="targetPlayerId">对方玩家标识。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>删除后的关系。</returns>
    public async Task<OnlineResult<OnlineFriendship>> RemoveAsync(OnlineScope scope, long targetPlayerId, string correlationId = null, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope<OnlineFriendship>(scope);
        if (failure != null)
        {
            return failure;
        }

        var friendship = await _store.FindAsync(scope.TenantId, scope.AppId, scope.PlayerId, targetPlayerId, cancellationToken).ConfigureAwait(false);
        if (friendship == null || !friendship.Contains(scope.PlayerId))
        {
            return OnlineResult<OnlineFriendship>.Fail(OnlineErrorCode.ResourceNotFound, "好友关系不存在");
        }

        if (friendship.State == OnlineFriendshipState.Removed)
        {
            // 幂等：已删除返回既有终态快照。
            return OnlineResult<OnlineFriendship>.Ok(friendship);
        }

        if (!OnlineFriendshipStateMachine.TryTransition(friendship.State, OnlineFriendshipState.Removed))
        {
            return OnlineResult<OnlineFriendship>.Fail(OnlineErrorCode.StateOperationForbidden, "当前关系状态不允许删除：" + friendship.State);
        }

        var removed = await _store.UpdateStateAsync(scope.TenantId, scope.AppId, new FriendshipStateTransition { FriendshipId = friendship.FriendshipId, ExpectedState = friendship.State, NewState = OnlineFriendshipState.Removed, NowUnixMilliseconds = Now(), Responded = false }, cancellationToken).ConfigureAwait(false);
        if (removed == null)
        {
            // CAS 失败 = 状态被并发改写；重读后按当前事实返回，不报错。
            var current = await _store.FindAsync(scope.TenantId, scope.AppId, scope.PlayerId, targetPlayerId, cancellationToken).ConfigureAwait(false);
            if (current == null)
            {
                return OnlineResult<OnlineFriendship>.Fail(OnlineErrorCode.ResourceNotFound, "好友关系不存在");
            }

            return OnlineResult<OnlineFriendship>.Ok(current);
        }

        await _eventPublisher.PublishAsync(OnlineSocialEvents.CreateFriendshipChanged(removed, OnlineSocialEvents.FriendshipRemoved, correlationId), cancellationToken).ConfigureAwait(false);
        return OnlineResult<OnlineFriendship>.Ok(removed);
    }

    /// <summary>
    /// 列出好友列表（含展示名与在线状态；在线状态为读取时即时事实）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>好友列表条目。</returns>
    public async Task<OnlineResult<IReadOnlyList<OnlineFriendSummary>>> ListFriendsAsync(OnlineScope scope, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope<IReadOnlyList<OnlineFriendSummary>>(scope);
        if (failure != null)
        {
            return failure;
        }

        var all = await _store.ListByPlayerAsync(scope.TenantId, scope.AppId, scope.PlayerId, cancellationToken).ConfigureAwait(false);
        var friendIds = new List<long>();
        var establishedAtTimeByPlayer = new Dictionary<long, long>();
        CollectEstablishedFriends(all, scope.PlayerId, friendIds, establishedAtTimeByPlayer);

        var names = new Dictionary<long, string>();
        var serverIds = new Dictionary<long, long>();
        await ResolveFriendDirectoryAsync(scope, friendIds, names, serverIds, cancellationToken).ConfigureAwait(false);

        var result = await BuildFriendSummariesAsync(scope, friendIds, names, serverIds, establishedAtTimeByPlayer, cancellationToken).ConfigureAwait(false);
        return OnlineResult<IReadOnlyList<OnlineFriendSummary>>.Ok(result);
    }

    /// <summary>
    /// 从全量关系中筛出已建立的好友（填充好友标识列表与建立时刻映射）。
    /// <para>
    /// 筛选判据：状态已建立（<see cref="OnlineFriendshipStateMachine.IsEstablished"/>）且对端标识有效；
    /// 建立时刻优先取答复时刻，未答复过（0）则回退最后更新时刻。
    /// </para>
    /// </summary>
    /// <param name="all">玩家的全量关系列表。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="friendIds">输出：已建立好友的标识列表。</param>
    /// <param name="establishedAtTimeByPlayer">输出：好友标识 → 建立时刻映射。</param>
    private static void CollectEstablishedFriends(IReadOnlyList<OnlineFriendship> all, long playerId, List<long> friendIds, Dictionary<long, long> establishedAtTimeByPlayer)
    {
        foreach (var friendship in all)
        {
            if (!OnlineFriendshipStateMachine.IsEstablished(friendship.State))
            {
                continue;
            }

            var other = friendship.OtherOf(playerId);
            if (other <= 0)
            {
                continue;
            }

            friendIds.Add(other);
            establishedAtTimeByPlayer[other] = friendship.RespondedAtTime > 0 ? friendship.RespondedAtTime : friendship.UpdatedAtTime;
        }
    }

    /// <summary>
    /// 解析好友的展示名与归属区服（填充两个映射；未装配目录或无好友时直接返回，保持空映射）。
    /// </summary>
    /// <param name="scope">生效作用域。</param>
    /// <param name="friendIds">已建立好友的标识列表。</param>
    /// <param name="names">输出：好友标识 → 展示名映射。</param>
    /// <param name="serverIds">输出：好友标识 → 归属区服映射。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task ResolveFriendDirectoryAsync(OnlineScope scope, List<long> friendIds, Dictionary<long, string> names, Dictionary<long, long> serverIds, CancellationToken cancellationToken)
    {
        if (_directory == null || friendIds.Count == 0)
        {
            return;
        }

        var entries = await _directory.FindAsync(scope.TenantId, scope.AppId, friendIds, cancellationToken).ConfigureAwait(false);
        foreach (var entry in entries)
        {
            names[entry.PlayerId] = entry.Name;
            serverIds[entry.PlayerId] = entry.ServerId;
        }
    }

    /// <summary>
    /// 组装好友列表条目（展示名缺失或为空时回退为标识字面量；在线状态为读取时即时事实，探针未装配恒为离线）。
    /// </summary>
    /// <param name="scope">生效作用域。</param>
    /// <param name="friendIds">已建立好友的标识列表。</param>
    /// <param name="names">好友标识 → 展示名映射。</param>
    /// <param name="serverIds">好友标识 → 归属区服映射。</param>
    /// <param name="establishedAtTimeByPlayer">好友标识 → 建立时刻映射。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>好友列表条目。</returns>
    private async Task<List<OnlineFriendSummary>> BuildFriendSummariesAsync(OnlineScope scope, List<long> friendIds, Dictionary<long, string> names, Dictionary<long, long> serverIds, Dictionary<long, long> establishedAtTimeByPlayer, CancellationToken cancellationToken)
    {
        var result = new List<OnlineFriendSummary>();
        foreach (var friendId in friendIds)
        {
            var online = false;
            if (_presenceProbe != null)
            {
                online = await _presenceProbe.IsOnlineAsync(scope.TenantId, scope.AppId, friendId, cancellationToken).ConfigureAwait(false);
            }

            string name;
            if (!names.TryGetValue(friendId, out name) || string.IsNullOrEmpty(name))
            {
                name = friendId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            long serverId;
            serverIds.TryGetValue(friendId, out serverId);

            result.Add(new OnlineFriendSummary
            {
                PlayerId = friendId,
                Name = name,
                ServerId = serverId,
                Online = online,
                EstablishedAtTime = establishedAtTimeByPlayer[friendId],
            });
        }

        return result;
    }

    /// <summary>
    /// 列出待本人答复的好友请求（只含 <see cref="OnlineFriendship.AddresseeId"/> 为自己的待答复请求）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>待答复请求列表。</returns>
    public async Task<OnlineResult<IReadOnlyList<OnlineFriendship>>> ListPendingRequestsAsync(OnlineScope scope, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope<IReadOnlyList<OnlineFriendship>>(scope);
        if (failure != null)
        {
            return failure;
        }

        var all = await _store.ListByPlayerAsync(scope.TenantId, scope.AppId, scope.PlayerId, cancellationToken).ConfigureAwait(false);
        var result = new List<OnlineFriendship>();
        foreach (var friendship in all)
        {
            if (friendship.State == OnlineFriendshipState.Requested && friendship.AddresseeId == scope.PlayerId)
            {
                result.Add(friendship);
            }
        }

        return OnlineResult<IReadOnlyList<OnlineFriendship>>.Ok(result);
    }

    /// <summary>
    /// 扫描并终结超期未答复的好友请求（置 <see cref="OnlineFriendshipState.Expired"/>，可重新发起）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒；传 0 取系统时钟，测试可注入）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>本次终结的请求数。</returns>
    public async Task<OnlineResult<int>> SweepExpiredAsync(long tenantId, long appId, long nowUnixMilliseconds = 0, CancellationToken cancellationToken = default)
    {
        var now = nowUnixMilliseconds > 0 ? nowUnixMilliseconds : Now();
        var pending = await _store.ListByStateAsync(tenantId, appId, OnlineFriendshipState.Requested, cancellationToken).ConfigureAwait(false);
        var affected = 0;
        foreach (var friendship in pending)
        {
            if (friendship.ExpiresAtTime > now)
            {
                continue;
            }

            var expired = await _store.UpdateStateAsync(tenantId, appId, new FriendshipStateTransition { FriendshipId = friendship.FriendshipId, ExpectedState = OnlineFriendshipState.Requested, NewState = OnlineFriendshipState.Expired, NowUnixMilliseconds = now, Responded = false }, cancellationToken).ConfigureAwait(false);
            if (expired != null)
            {
                await _eventPublisher.PublishAsync(OnlineSocialEvents.CreateFriendshipChanged(expired, OnlineSocialEvents.FriendshipExpired, null), cancellationToken).ConfigureAwait(false);
                affected++;
            }
        }

        return OnlineResult<int>.Ok(affected);
    }

    /// <summary>
    /// 判断本条关系若被接受，是否会使**任一方**的好友数越过上限。
    /// <para>
    /// 维护约束（双向判据）：好友关系对双方各占一个名额，因此必须两侧都判——
    /// 只判接受方的话，一个已经加满好友的发起人可以持续向「名额还有空」的人堆积关系，
    /// 把自己的列表顶到无界。
    /// </para>
    /// <para>
    /// 天花板（ponytail）：这里对两侧各做一次全量列表扫描再本地计数，是 O(该玩家关系数)；
    /// 关系数上万后应在存储层加按玩家维度的已建立关系计数索引，把判据下沉为一次 O(1) 读。
    /// </para>
    /// </summary>
    /// <param name="scope">生效作用域。</param>
    /// <param name="friendship">待答复的关系。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>任一方已达上限返回 <c>true</c>。</returns>
    private async Task<bool> IsFriendLimitReachedAsync(OnlineScope scope, OnlineFriendship friendship, CancellationToken cancellationToken)
    {
        var requesterCount = await CountEstablishedAsync(scope, friendship.RequesterId, cancellationToken).ConfigureAwait(false);
        if (requesterCount >= _friendLimit)
        {
            return true;
        }

        var addresseeCount = await CountEstablishedAsync(scope, friendship.AddresseeId, cancellationToken).ConfigureAwait(false);
        return addresseeCount >= _friendLimit;
    }

    /// <summary>
    /// 统计某玩家已建立（<see cref="OnlineFriendshipStateMachine.IsEstablished"/>）的好友数。
    /// </summary>
    /// <param name="scope">生效作用域。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>已建立的好友关系数。</returns>
    private async Task<int> CountEstablishedAsync(OnlineScope scope, long playerId, CancellationToken cancellationToken)
    {
        var all = await _store.ListByPlayerAsync(scope.TenantId, scope.AppId, playerId, cancellationToken).ConfigureAwait(false);
        var count = 0;
        foreach (var candidate in all)
        {
            if (OnlineFriendshipStateMachine.IsEstablished(candidate.State))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// 答复好友请求的唯一实现点（接受 / 拒绝共用；权限判据只取被请求方）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="friendshipId">关系标识。</param>
    /// <param name="accept">接受传 <c>true</c>，拒绝传 <c>false</c>。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>答复后的关系。</returns>
    private async Task<OnlineResult<OnlineFriendship>> AnswerAsync(OnlineScope scope, string friendshipId, bool accept, string correlationId, CancellationToken cancellationToken)
    {
        var failure = ValidateScope<OnlineFriendship>(scope);
        if (failure != null)
        {
            return failure;
        }

        if (string.IsNullOrEmpty(friendshipId))
        {
            return OnlineResult<OnlineFriendship>.Fail(OnlineErrorCode.ParameterInvalid, "关系标识不能为空");
        }

        var friendship = await FindByIdAsync(scope, friendshipId, cancellationToken).ConfigureAwait(false);
        if (friendship == null)
        {
            return OnlineResult<OnlineFriendship>.Fail(OnlineErrorCode.ResourceNotFound, "好友请求不存在");
        }

        if (friendship.AddresseeId != scope.PlayerId)
        {
            // 反预言：非被请求方（含发起方本人）看不到这条请求的存在。
            return OnlineResult<OnlineFriendship>.Fail(OnlineErrorCode.ResourceNotFound, "好友请求不存在");
        }

        var target = accept ? OnlineFriendshipState.Accepted : OnlineFriendshipState.Rejected;
        if (!OnlineFriendshipStateMachine.TryTransition(friendship.State, target))
        {
            // 已答复 / 已过期等：返回既有状态而非报错（重复答复幂等）。
            // 这一判定必须排在好友上限之前：已 Accepted 的重放请求若先撞上限，会拿到
            // StateOperationForbidden（「已达上限」），而它其实早已成功——重放与首次答复必须同解。
            return OnlineResult<OnlineFriendship>.Ok(friendship);
        }

        if (accept && await IsFriendLimitReachedAsync(scope, friendship, cancellationToken).ConfigureAwait(false))
        {
            // 上限在**写侧**（关系建立的那一刻）拒绝，而不是在读取侧静默截断。
            // 静默截断是错的两个方向：被截掉的好友在列表里凭空消失（玩家视角就是好友丢了，
            // 且没有任何错误信号可用于申诉），而写入侧不设防又让关系数无界增长
            // （每多一个好友就多一路 Presence 扇出与关系变更通知）。
            // 计数与随后的写入不在同一临界区（跨记录无 CAS）：两个代办请求并发接受时可能各读到
            // 上限 -1，从而双双落库超限一格。ponytail：单机内存实现按「上限是软约束、写入侧强于读取侧」收口，
            // 需要硬约束时把计数与写入收进存储层同一临界区（届时换成带计数的 CAS）。
            return OnlineResult<OnlineFriendship>.Fail(OnlineErrorCode.StateOperationForbidden, "好友数量已达上限");
        }

        var now = Now();
        var updated = await _store.UpdateStateAsync(scope.TenantId, scope.AppId, new FriendshipStateTransition { FriendshipId = friendshipId, ExpectedState = friendship.State, NewState = target, NowUnixMilliseconds = now, Responded = true }, cancellationToken).ConfigureAwait(false);
        if (updated == null)
        {
            // CAS 失败 = 已被并发答复 / 超期扫描抢先；重读后按当前事实返回。
            var current = await FindByIdAsync(scope, friendshipId, cancellationToken).ConfigureAwait(false);
            if (current == null)
            {
                return OnlineResult<OnlineFriendship>.Fail(OnlineErrorCode.ResourceNotFound, "好友请求不存在");
            }

            return OnlineResult<OnlineFriendship>.Ok(current);
        }

        var eventKind = accept ? OnlineSocialEvents.FriendshipAccepted : OnlineSocialEvents.FriendshipRejected;
        await _eventPublisher.PublishAsync(OnlineSocialEvents.CreateFriendshipChanged(updated, eventKind, correlationId), cancellationToken).ConfigureAwait(false);
        return OnlineResult<OnlineFriendship>.Ok(updated);
    }

    /// <summary>
    /// 按关系标识查找（存储按无向对索引，这里先按玩家维定位再精确匹配标识）。
    /// </summary>
    /// <param name="scope">生效作用域。</param>
    /// <param name="friendshipId">关系标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>关系副本；不存在返回 null。</returns>
    private async Task<OnlineFriendship> FindByIdAsync(OnlineScope scope, string friendshipId, CancellationToken cancellationToken)
    {
        var all = await _store.ListByPlayerAsync(scope.TenantId, scope.AppId, scope.PlayerId, cancellationToken).ConfigureAwait(false);
        foreach (var friendship in all)
        {
            if (string.Equals(friendship.FriendshipId, friendshipId, StringComparison.Ordinal))
            {
                return friendship;
            }
        }

        return null;
    }

    /// <summary>
    /// 把「已存在的关系」收敛到本次请求的应答（幂等 / 重新发起的唯一分派点）。
    /// </summary>
    /// <param name="existing">已存在的关系。</param>
    /// <param name="scope">生效作用域。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>收敛后的关系。</returns>
    private async Task<OnlineResult<OnlineFriendship>> ConvergeExistingAsync(OnlineFriendship existing, OnlineScope scope, string correlationId, long nowUnixMilliseconds, CancellationToken cancellationToken)
    {
        var targetPlayerId = existing.OtherOf(scope.PlayerId);

        // 待答复 / 已是好友：直接返回既有状态（VC-6.1「其余返回已有状态」）。
        if (existing.State == OnlineFriendshipState.Requested || OnlineFriendshipStateMachine.IsEstablished(existing.State))
        {
            return OnlineResult<OnlineFriendship>.Ok(existing);
        }

        if (!OnlineFriendshipStateMachine.TryTransition(existing.State, OnlineFriendshipState.Requested))
        {
            return OnlineResult<OnlineFriendship>.Fail(OnlineErrorCode.StateOperationForbidden, "当前关系状态不允许发起请求：" + existing.State);
        }

        var renewed = await _store.RenewRequestAsync(
            scope.TenantId,
            scope.AppId,
            new FriendshipRenewal
            {
                FriendshipId = existing.FriendshipId,
                ExpectedState = existing.State,
                RequesterId = scope.PlayerId,
                AddresseeId = targetPlayerId,
                NowUnixMilliseconds = nowUnixMilliseconds,
                ExpiresAtTime = nowUnixMilliseconds + (_requestTimeToLiveSeconds * 1000),
            },
            cancellationToken).ConfigureAwait(false);

        if (renewed == null)
        {
            // CAS 失败 = 并发发起/答复抢先；重读后返回当前事实，保证收敛。
            var current = await _store.FindAsync(scope.TenantId, scope.AppId, scope.PlayerId, targetPlayerId, cancellationToken).ConfigureAwait(false);
            if (current == null)
            {
                return OnlineResult<OnlineFriendship>.Fail(OnlineErrorCode.DependencyUnavailable, "好友关系读取失败");
            }

            return OnlineResult<OnlineFriendship>.Ok(current);
        }

        await _eventPublisher.PublishAsync(OnlineSocialEvents.CreateFriendshipChanged(renewed, OnlineSocialEvents.FriendshipRequested, correlationId), cancellationToken).ConfigureAwait(false);
        return OnlineResult<OnlineFriendship>.Ok(renewed);
    }

    /// <summary>
    /// 校验作用域与玩家主体位（泛型形态）。
    /// </summary>
    /// <typeparam name="TData">接口成功负载类型。</typeparam>
    /// <param name="scope">生效作用域。</param>
    /// <returns>成功返回 null，失败返回错误结果。</returns>
    private static OnlineResult<TData> ValidateScope<TData>(OnlineScope scope)
    {
        if (scope == null)
        {
            return OnlineResult<TData>.Fail(OnlineErrorCode.ParameterInvalid, "缺少作用域");
        }

        if (scope.PlayerId <= 0)
        {
            return OnlineResult<TData>.Fail(OnlineErrorCode.ParameterInvalid, "好友操作必须具备玩家主体位");
        }

        return null;
    }

    /// <summary>取当前 UTC 毫秒时刻。</summary>
    /// <returns>UTC 毫秒时间戳。</returns>
    private static long Now()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}
