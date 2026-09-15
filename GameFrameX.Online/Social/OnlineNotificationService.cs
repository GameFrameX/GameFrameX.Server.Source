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
/// 通知服务（vault:C7 S6.8/S6.9：通知队列、推送出口与离线补发的唯一写者）。
/// <para>
/// 维护约束（红线）：
/// ① **入队幂等由去重键保证**——同一去重键重复入队返回既有通知，不新建、不重发、不报错
/// （VC-6.12：推送重试与离线补发同时发生时业务只执行一次）；
/// ② 推送失败必须留痕（<see cref="OnlineNotification.LastError"/>）并转入重试或
/// <see cref="OnlineNotificationState.Failed"/> 终态，不得静默丢弃——排障与补发的唯一依据；
/// ③ 状态落定一律走 <see cref="IOnlineNotificationStore.ReplaceAsync"/> 的 CAS，CAS 失败即重读收敛，
/// 既不报错也不覆盖并发结果（收敛语义对齐 <see cref="OnlineFriendService"/>）；
/// ④ 推送出口未装配（<c>dispatcher == null</c>）等价于「推送失败且可重试」——通知留在队列里等
/// <see cref="BackfillAsync"/> / <see cref="RetryPendingAsync"/>，不得据此判定玩家离线或丢弃通知；
/// ⑤ 重复入队**不重发**：去重命中即返回，是否再推由补发与重试路径决定（否则去重形同虚设）。
/// </para>
/// </summary>
public sealed class OnlineNotificationService
{
    /// <summary>非终态集合（过期扫描的输入；终态不可再迁移，扫描它们只是空转）。</summary>
    private static readonly OnlineNotificationState[] NonTerminalStates = new OnlineNotificationState[]
    {
        OnlineNotificationState.Created,
        OnlineNotificationState.Queued,
        OnlineNotificationState.Delivered,
        OnlineNotificationState.Retrying,
        OnlineNotificationState.Failed,
    };

    /// <summary>通知存储。</summary>
    private readonly IOnlineNotificationStore _store;

    /// <summary>事件发布出口。</summary>
    private readonly IOnlineEventPublisher _eventPublisher;

    /// <summary>推送出口（可空：未装配时推送一律失败并走重试 / 补发路径）。</summary>
    private readonly IOnlineNotificationDispatcher _dispatcher;

    /// <summary>单条通知的最大推送尝试次数（含首次）。</summary>
    private readonly int _maxAttempts;

    /// <summary>默认有效期（秒；&lt;= 0 表示默认不设有效期）。</summary>
    private readonly long _defaultTimeToLiveSeconds;

    /// <summary>
    /// 初始化 <see cref="OnlineNotificationService"/>。
    /// </summary>
    /// <param name="store">通知存储。</param>
    /// <param name="eventPublisher">事件发布出口。</param>
    /// <param name="dispatcher">推送出口（可空）。</param>
    /// <param name="maxAttempts">最大推送尝试次数（默认 5；&lt;= 0 时回退默认值）。</param>
    /// <param name="defaultTimeToLiveSeconds">默认有效期（秒；默认 604800 即 7 天；&lt;= 0 表示不设有效期）。</param>
    public OnlineNotificationService(
        IOnlineNotificationStore store,
        IOnlineEventPublisher eventPublisher,
        IOnlineNotificationDispatcher dispatcher = null,
        int maxAttempts = 5,
        long defaultTimeToLiveSeconds = 604800)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        _dispatcher = dispatcher;
        _maxAttempts = maxAttempts > 0 ? maxAttempts : 5;
        _defaultTimeToLiveSeconds = defaultTimeToLiveSeconds;
    }

    /// <summary>
    /// 入队一条通知（去重键幂等：重复入队返回既有通知，不新建、不重发、不报错）。
    /// <para>
    /// 收敛语义（VC-6.12）：新建成功的事件在落库后立即发布；随后**尽力尝试一次推送**，
    /// 推送失败不影响入队成功——通知已在队列里，由离线补发路径兜底，调用方不应据此重试入队。
    /// </para>
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位，接收者即作用域玩家）。</param>
    /// <param name="kind">通知来源分类。</param>
    /// <param name="dedupeKey">去重键（来源域给出的业务幂等键，不能为空）。</param>
    /// <param name="payload">载荷 JSON 文本（来源域序列化，通知域视为不透明）。</param>
    /// <param name="expiresAtTime">失效时刻（UTC 毫秒；&lt;= 0 时按默认 TTL 兜底）。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>当前生效的通知（新建的或去重命中的既有记录）。</returns>
    public async Task<OnlineResult<OnlineNotification>> EnqueueAsync(OnlineScope scope, OnlineNotificationKind kind, string dedupeKey, string payload, long expiresAtTime = 0, string correlationId = null, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope<OnlineNotification>(scope);
        if (failure != null)
        {
            return failure;
        }

        if (string.IsNullOrWhiteSpace(dedupeKey))
        {
            return OnlineResult<OnlineNotification>.Fail(OnlineErrorCode.ParameterInvalid, "去重键不能为空");
        }

        var now = Now();
        var prototype = new OnlineNotification
        {
            NotificationId = "ntf-" + Guid.NewGuid().ToString("N"),
            TenantId = scope.TenantId,
            AppId = scope.AppId,
            ServerId = scope.ServerId,
            PlayerId = scope.PlayerId,
            Kind = kind,
            DedupeKey = dedupeKey.Trim(),
            Payload = payload ?? string.Empty,
            ExpiresAtTime = ResolveExpiresAtTime(expiresAtTime, now),
            State = OnlineNotificationState.Created,
            AttemptCount = 0,
            LastError = string.Empty,
            CreatedAtTime = now,
            UpdatedAtTime = now,
        };

        // 并发连发的收敛点：先到者创建，后到者拿到同一条既有记录（存储层原子）。
        var effective = await _store.SaveIfAbsentAsync(prototype, cancellationToken).ConfigureAwait(false);
        if (effective == null)
        {
            return OnlineResult<OnlineNotification>.Fail(OnlineErrorCode.DependencyUnavailable, "通知入队写入失败");
        }

        if (!string.Equals(effective.NotificationId, prototype.NotificationId, StringComparison.Ordinal))
        {
            // 去重命中：同一去重键已有通知在途 / 已投递，直接返回既有记录，不新建、不重发。
            return OnlineResult<OnlineNotification>.Ok(effective);
        }

        await _eventPublisher.PublishAsync(OnlineNotificationEvents.CreateNotificationChanged(effective, correlationId), cancellationToken).ConfigureAwait(false);

        var dispatched = await TryDispatchAsync(scope.TenantId, scope.AppId, scope.PlayerId, effective.NotificationId, cancellationToken).ConfigureAwait(false);
        if (dispatched.IsSuccess && dispatched.Data != null)
        {
            // 推送已尝试：返回落定后的状态（Delivered / Retrying / Failed），入队本身仍是成功的。
            return OnlineResult<OnlineNotification>.Ok(dispatched.Data);
        }

        return OnlineResult<OnlineNotification>.Ok(effective);
    }

    /// <summary>
    /// 尝试推送一条通知（首次推送、离线补发与重试扫描共用的唯一实现点）。
    /// <para>
    /// 流程：过期兜底 → 落「在途」标记 → 调推送出口 → 按回执落定状态，两次落定都用 CAS。
    /// 终态（已读 / 已过期）幂等返回当前记录，不重复推送——这是「补发与重试并发不重复消费」的守卫点；
    /// 已投递但未读的记录同样不重投（重复推送等同重复消费），按状态机表外迁移返回
    /// <see cref="OnlineErrorCode.StateOperationForbidden"/>。
    /// </para>
    /// <para>
    /// 「不重复推送」的判定落在<b>状态机</b>上而非调用方自觉：已在途（<see cref="OnlineNotificationState.Queued"/>）
    /// 的记录同样被拒（自环非合法边）。在途意味着另一个调用方正停在推送出口内，放行就是玩家收到两条同样的推送。
    /// 代价（天花板）：若推送出口在回执前随进程一起消失，这条在途记录无人敢再投，只能等 TTL 到期被扫描轮终结——
    /// 用「宁可漏推、不可重复消费」换的确定性；跨进程的在途回收（带超时的租约）归运行时的持久化存储层。
    /// </para>
    /// <para>
    /// 过期兜底：<see cref="SweepExpiredAsync"/> 是**周期性**扫描，扫描间隔内到期的通知不会被它拦住；
    /// 推送入口因此自己按 <see cref="OnlineNotification.ExpiresAtTime"/> 判一次，到期就地终结为
    /// <see cref="OnlineNotificationState.Expired"/> 并**原样返回、绝不推给玩家**（VC-6.13：过期丢弃、不误导玩家）。
    /// </para>
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="playerId">接收者玩家标识。</param>
    /// <param name="notificationId">通知标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>推送尝试后的通知记录。</returns>
    public async Task<OnlineResult<OnlineNotification>> TryDispatchAsync(long tenantId, long appId, long playerId, string notificationId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(notificationId))
        {
            return OnlineResult<OnlineNotification>.Fail(OnlineErrorCode.ParameterInvalid, "通知标识不能为空");
        }

        var current = await _store.FindAsync(tenantId, appId, playerId, notificationId, cancellationToken).ConfigureAwait(false);
        if (current == null)
        {
            return OnlineResult<OnlineNotification>.Fail(OnlineErrorCode.ResourceNotFound, "通知不存在");
        }

        if (OnlineNotificationStateMachine.IsTerminal(current.State))
        {
            // 终态幂等：已读 / 已过期不再推送（VC-6.12「不重复消费」）。
            return OnlineResult<OnlineNotification>.Ok(current);
        }

        var now = Now();
        if (current.ExpiresAtTime > 0 && current.ExpiresAtTime <= now)
        {
            // 扫描轮还没跑到：到期即终结，不推给玩家（VC-6.13）。
            var expired = current.Copy();
            expired.State = OnlineNotificationState.Expired;
            expired.UpdatedAtTime = now;
            var terminated = await _store.ReplaceAsync(expired, current.State, current.AttemptCount, cancellationToken).ConfigureAwait(false);
            if (terminated == null)
            {
                return await ConvergeAsync(tenantId, appId, playerId, notificationId, cancellationToken).ConfigureAwait(false);
            }

            await _eventPublisher.PublishAsync(OnlineNotificationEvents.CreateNotificationChanged(terminated, null), cancellationToken).ConfigureAwait(false);
            return OnlineResult<OnlineNotification>.Ok(terminated);
        }

        // 在途标记：先落 Queued 再推送。已在途（Queued）的记录**拒绝重投**——「在途」说明另一个调用方
        // 正持有这条记录，放行就是一次真实的重复推送（VC-6.12 不重复消费）。自环不是合法边，不得豁免。
        if (!OnlineNotificationStateMachine.TryTransition(current.State, OnlineNotificationState.Queued))
        {
            return OnlineResult<OnlineNotification>.Fail(OnlineErrorCode.StateOperationForbidden, "当前通知状态不允许推送：" + current.State);
        }

        var attempt = current.Copy();
        attempt.State = OnlineNotificationState.Queued;
        attempt.AttemptCount = current.AttemptCount + 1;
        attempt.UpdatedAtTime = now;
        // 期望值含尝试次数：并发第二次推送读到的是同一份旧快照，此处必然 CAS 失败，不会重复调用推送出口。
        var queued = await _store.ReplaceAsync(attempt, current.State, current.AttemptCount, cancellationToken).ConfigureAwait(false);
        if (queued == null)
        {
            // CAS 失败 = 状态被并发改写（如过期扫描抢先）；重读后按当前事实返回，不报错。
            return await ConvergeAsync(tenantId, appId, playerId, notificationId, cancellationToken).ConfigureAwait(false);
        }

        await _eventPublisher.PublishAsync(OnlineNotificationEvents.CreateNotificationChanged(queued, null), cancellationToken).ConfigureAwait(false);

        var outcome = await DispatchAsync(queued, cancellationToken).ConfigureAwait(false);

        var settled = queued.Copy();
        settled.UpdatedAtTime = Now();
        if (outcome.Delivered)
        {
            settled.State = OnlineNotificationState.Delivered;
            settled.DeliveredAtTime = settled.UpdatedAtTime;
            settled.LastError = string.Empty;
        }
        else
        {
            settled.LastError = outcome.FailureReason ?? string.Empty;
            if (outcome.Retryable && settled.AttemptCount < _maxAttempts)
            {
                settled.State = OnlineNotificationState.Retrying;
            }
            else
            {
                // 不可重试（如接收者不存在）或重试次数耗尽：落 Failed 终态，不再占用重试预算。
                settled.State = OnlineNotificationState.Failed;
            }
        }

        if (!OnlineNotificationStateMachine.TryTransition(OnlineNotificationState.Queued, settled.State))
        {
            return OnlineResult<OnlineNotification>.Fail(OnlineErrorCode.StateOperationForbidden, "当前通知状态不允许落定：" + settled.State);
        }

        var result = await _store.ReplaceAsync(settled, OnlineNotificationState.Queued, queued.AttemptCount, cancellationToken).ConfigureAwait(false);
        if (result == null)
        {
            return await ConvergeAsync(tenantId, appId, playerId, notificationId, cancellationToken).ConfigureAwait(false);
        }

        await _eventPublisher.PublishAsync(OnlineNotificationEvents.CreateNotificationChanged(result, null), cancellationToken).ConfigureAwait(false);
        return OnlineResult<OnlineNotification>.Ok(result);
    }

    /// <summary>
    /// 标记通知已读（仅接收者本人可读；已读幂等）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位，且必须是通知接收者）。</param>
    /// <param name="notificationId">通知标识。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>已读后的通知记录。</returns>
    public async Task<OnlineResult<OnlineNotification>> MarkReadAsync(OnlineScope scope, string notificationId, string correlationId = null, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope<OnlineNotification>(scope);
        if (failure != null)
        {
            return failure;
        }

        if (string.IsNullOrEmpty(notificationId))
        {
            return OnlineResult<OnlineNotification>.Fail(OnlineErrorCode.ParameterInvalid, "通知标识不能为空");
        }

        var notification = await _store.FindAsync(scope.TenantId, scope.AppId, scope.PlayerId, notificationId, cancellationToken).ConfigureAwait(false);
        if (notification == null || notification.PlayerId != scope.PlayerId)
        {
            // 反预言：非接收者（含越权猜标识）看不到这条通知的存在，不泄露他人通知规模。
            return OnlineResult<OnlineNotification>.Fail(OnlineErrorCode.ResourceNotFound, "通知不存在");
        }

        if (notification.State == OnlineNotificationState.Read)
        {
            // 幂等：重复标记已读返回既有终态快照。
            return OnlineResult<OnlineNotification>.Ok(notification);
        }

        if (notification.State == OnlineNotificationState.Expired)
        {
            return OnlineResult<OnlineNotification>.Fail(OnlineErrorCode.StateOperationForbidden, "通知已过期，不能标记已读");
        }

        if (notification.State != OnlineNotificationState.Delivered)
        {
            return OnlineResult<OnlineNotification>.Fail(OnlineErrorCode.StateNotReady, "通知尚未投递，不能标记已读：" + notification.State);
        }

        var now = Now();
        var read = notification.Copy();
        read.State = OnlineNotificationState.Read;
        read.ReadAtTime = now;
        read.UpdatedAtTime = now;
        var updated = await _store.ReplaceAsync(read, OnlineNotificationState.Delivered, notification.AttemptCount, cancellationToken).ConfigureAwait(false);
        if (updated == null)
        {
            return await ConvergeAsync(scope.TenantId, scope.AppId, scope.PlayerId, notificationId, cancellationToken).ConfigureAwait(false);
        }

        await _eventPublisher.PublishAsync(OnlineNotificationEvents.CreateNotificationChanged(updated, correlationId), cancellationToken).ConfigureAwait(false);
        return OnlineResult<OnlineNotification>.Ok(updated);
    }

    /// <summary>
    /// 列出本接收者的通知（默认不含过期记录）。
    /// <para>
    /// 维护约束：过期通知一律不返回给玩家（VC-6.13：丢弃、不误导玩家），仅在
    /// <paramref name="includeTerminal"/> 为 <c>true</c> 时作为历史返回；已读属玩家可见历史，始终保留。
    /// </para>
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="includeTerminal">是否包含过期通知（已读的通知始终包含）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>通知列表。</returns>
    public async Task<OnlineResult<IReadOnlyList<OnlineNotification>>> ListAsync(OnlineScope scope, bool includeTerminal = false, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope<IReadOnlyList<OnlineNotification>>(scope);
        if (failure != null)
        {
            return failure;
        }

        var all = await _store.ListByPlayerAsync(scope.TenantId, scope.AppId, scope.PlayerId, cancellationToken).ConfigureAwait(false);
        var result = new List<OnlineNotification>();
        foreach (var notification in all)
        {
            if (notification.State == OnlineNotificationState.Expired && !includeTerminal)
            {
                continue;
            }

            result.Add(notification);
        }

        return OnlineResult<IReadOnlyList<OnlineNotification>>.Ok(result);
    }

    /// <summary>
    /// 离线补发（vault:C7 VC-6.11 的落点）：把该接收者全部未投递的通知逐条重投一次。
    /// <para>
    /// 补发范围是「未达终态、未投递且不在途」（<see cref="OnlineNotificationState.Created"/> /
    /// <see cref="OnlineNotificationState.Retrying"/> / <see cref="OnlineNotificationState.Failed"/>）——已投递 / 已读的
    /// 不重复推送，已过期的不补发，在途（<see cref="OnlineNotificationState.Queued"/>）的跳过：
    /// 补发的本意是「投递没发生」，而在途说明投递正在进行，跟一发就是重复推送（VC-6.12）。
    /// 逐条独立推进：单条失败不影响其余记录，返回的是**补发尝试后**的副本列表（在途记录原样返回，不被改写）。
    /// </para>
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>补发尝试后的通知记录列表。</returns>
    public async Task<OnlineResult<IReadOnlyList<OnlineNotification>>> BackfillAsync(OnlineScope scope, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope<IReadOnlyList<OnlineNotification>>(scope);
        if (failure != null)
        {
            return failure;
        }

        var all = await _store.ListByPlayerAsync(scope.TenantId, scope.AppId, scope.PlayerId, cancellationToken).ConfigureAwait(false);
        var result = new List<OnlineNotification>();
        foreach (var notification in all)
        {
            if (notification.State == OnlineNotificationState.Delivered || OnlineNotificationStateMachine.IsTerminal(notification.State))
            {
                continue;
            }

            var dispatched = await TryDispatchAsync(scope.TenantId, scope.AppId, scope.PlayerId, notification.NotificationId, cancellationToken).ConfigureAwait(false);
            if (dispatched.IsSuccess && dispatched.Data != null)
            {
                result.Add(dispatched.Data);
                continue;
            }

            result.Add(notification);
        }

        return OnlineResult<IReadOnlyList<OnlineNotification>>.Ok(result);
    }

    /// <summary>
    /// 扫描并终结超期通知（置 <see cref="OnlineNotificationState.Expired"/>，VC-6.13）。
    /// <para>
    /// 只处理「非终态 + 设了有效期 + 已到期」的记录；<see cref="OnlineNotification.ExpiresAtTime"/> 为 0 的
    /// 记录永不失效，不参与扫描。终态记录不可再迁移，扫描时直接跳过。
    /// </para>
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒；传 0 取系统时钟，测试可注入）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>本次终结的通知条数。</returns>
    public async Task<OnlineResult<int>> SweepExpiredAsync(long tenantId, long appId, long nowUnixMilliseconds = 0, CancellationToken cancellationToken = default)
    {
        var now = nowUnixMilliseconds > 0 ? nowUnixMilliseconds : Now();
        var affected = 0;
        foreach (var state in NonTerminalStates)
        {
            var pending = await _store.ListByStateAsync(tenantId, appId, state, cancellationToken).ConfigureAwait(false);
            foreach (var notification in pending)
            {
                if (notification.ExpiresAtTime <= 0 || notification.ExpiresAtTime > now)
                {
                    continue;
                }

                var expired = notification.Copy();
                expired.State = OnlineNotificationState.Expired;
                expired.UpdatedAtTime = now;
                var updated = await _store.ReplaceAsync(expired, state, notification.AttemptCount, cancellationToken).ConfigureAwait(false);
                if (updated == null)
                {
                    // CAS 失败 = 该条已被并发推进（如刚投递成功）；本轮不计入，留给下一轮事实判定。
                    continue;
                }

                await _eventPublisher.PublishAsync(OnlineNotificationEvents.CreateNotificationChanged(updated, null), cancellationToken).ConfigureAwait(false);
                affected++;
            }
        }

        return OnlineResult<int>.Ok(affected);
    }

    /// <summary>
    /// 重投待重试与已失败的通知（重试扫描；按批次上限截断，避免单轮扫描拖垮推送出口）。
    /// <para>
    /// 维护约束：只取 <see cref="OnlineNotificationState.Retrying"/> 与
    /// <see cref="OnlineNotificationState.Failed"/> 两种状态，且**合计**不超过
    /// <paramref name="maxCount"/>；重投复用 <see cref="TryDispatchAsync"/> 的 CAS 路径，
    /// 与离线补发并发时不会重复投递。
    /// </para>
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="maxCount">本轮最多尝试条数（&lt;= 0 时不重投）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>本轮实际尝试条数。</returns>
    public async Task<OnlineResult<int>> RetryPendingAsync(long tenantId, long appId, int maxCount = 100, CancellationToken cancellationToken = default)
    {
        var limit = maxCount > 0 ? maxCount : 0;
        var attempted = 0;
        if (limit > 0)
        {
            // 本轮已推送过的通知不得在**同一轮**内再推一次：Retrying 批次推送失败后其状态正好变成
            // Failed，第二批会立刻把它们当成「历史失败」重捞一遍，等于一条通知一口气烧掉两次重试预算
            // （实测尝试数会从 1 直接跳到 3），重试预算形同虚设。故按本轮游标去重。
            var attemptedIds = new HashSet<string>(StringComparer.Ordinal);
            // 待重试优先于已失败：前者还差一次推送，后者可能已不可达。
            attempted += await RetryBatchAsync(tenantId, appId, OnlineNotificationState.Retrying, limit - attempted, attemptedIds, cancellationToken).ConfigureAwait(false);
            attempted += await RetryBatchAsync(tenantId, appId, OnlineNotificationState.Failed, limit - attempted, attemptedIds, cancellationToken).ConfigureAwait(false);
        }

        return OnlineResult<int>.Ok(attempted);
    }

    /// <summary>
    /// 把推送出口的调用收敛为回执（未装配 / 抛异常 / 返回 null 一律转为「失败且可重试」）。
    /// </summary>
    /// <param name="notification">待推送的通知快照。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>推送回执。</returns>
    private async Task<OnlineNotificationDispatchOutcome> DispatchAsync(OnlineNotification notification, CancellationToken cancellationToken)
    {
        if (_dispatcher == null)
        {
            // 未装配推送出口不等于玩家离线：通知留在队列等补发，不得据此丢弃。
            return OnlineNotificationDispatchOutcome.Fail("未装配推送出口", true);
        }

        try
        {
            var outcome = await _dispatcher.DispatchAsync(notification, cancellationToken).ConfigureAwait(false);
            if (outcome == null)
            {
                // 返回 null 是出口的契约违约（必须显式表达失败），按可重试失败处理而非崩溃。
                return OnlineNotificationDispatchOutcome.Fail("推送出口未返回结果", true);
            }

            return outcome;
        }
        catch (Exception ex)
        {
            // 异常即传输失败：转为可重试失败，交由重试 / 补发路径驱动。
            return OnlineNotificationDispatchOutcome.Fail(ex.Message, true);
        }
    }

    /// <summary>
    /// CAS 失败后的收敛点：重读当前事实并按其返回（不报错、不覆盖并发结果）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="playerId">接收者玩家标识。</param>
    /// <param name="notificationId">通知标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>当前生效的通知记录。</returns>
    private async Task<OnlineResult<OnlineNotification>> ConvergeAsync(long tenantId, long appId, long playerId, string notificationId, CancellationToken cancellationToken)
    {
        var current = await _store.FindAsync(tenantId, appId, playerId, notificationId, cancellationToken).ConfigureAwait(false);
        if (current == null)
        {
            return OnlineResult<OnlineNotification>.Fail(OnlineErrorCode.ResourceNotFound, "通知不存在");
        }

        return OnlineResult<OnlineNotification>.Ok(current);
    }

    /// <summary>
    /// 重投指定状态的一批通知（受剩余批次配额约束）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="state">目标状态。</param>
    /// <param name="limit">本轮剩余配额。</param>
    /// <param name="attemptedIds">本轮已处理的通知标识游标（同一轮内不得重复推送，跨批次去重）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>本轮实际尝试条数。</returns>
    private async Task<int> RetryBatchAsync(long tenantId, long appId, OnlineNotificationState state, int limit, HashSet<string> attemptedIds, CancellationToken cancellationToken)
    {
        if (limit <= 0)
        {
            return 0;
        }

        var batch = await _store.ListByStateAsync(tenantId, appId, state, cancellationToken).ConfigureAwait(false);
        var attempted = 0;
        foreach (var notification in batch)
        {
            if (attempted >= limit)
            {
                break;
            }

            if (!attemptedIds.Add(notification.NotificationId))
            {
                // 本轮已处理过（例如刚从 Retrying 落入 Failed），跳过以免重复烧预算。
                continue;
            }

            attempted++;
            await TryDispatchAsync(tenantId, appId, notification.PlayerId, notification.NotificationId, cancellationToken).ConfigureAwait(false);
        }

        return attempted;
    }

    /// <summary>
    /// 解析通知失效时刻：入参有效则原样采用，否则按默认 TTL 兜底。
    /// </summary>
    /// <param name="expiresAtTime">调用方给出的失效时刻（UTC 毫秒；&lt;= 0 表示未指定）。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒）。</param>
    /// <returns>失效时刻；0 表示永不失效。</returns>
    private long ResolveExpiresAtTime(long expiresAtTime, long nowUnixMilliseconds)
    {
        if (expiresAtTime > 0)
        {
            return expiresAtTime;
        }

        if (_defaultTimeToLiveSeconds <= 0)
        {
            return 0;
        }

        return nowUnixMilliseconds + (_defaultTimeToLiveSeconds * 1000);
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
            return OnlineResult<TData>.Fail(OnlineErrorCode.ParameterInvalid, "通知操作必须具备玩家主体位");
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
