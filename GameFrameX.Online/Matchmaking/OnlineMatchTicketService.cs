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

namespace GameFrameX.Online.Matchmaking;

using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Online.Contracts;
using GameFrameX.Online.Events;
using GameFrameX.Online.Scope;

/// <summary>
/// 匹配票据服务（vault:C5 S4.5：入队、取消、查询；票据状态的**唯一裁决入口**）。
/// <para>
/// 维护约束（红线）：
/// ① 所有状态改写都经 <see cref="IOnlineMatchTicketStore"/> 的 CAS 入口，本类不做「先读后写」的两段式判断；
/// ② 取消已进入终态的票据不是失败——返回该票据的终态回执（VC-4.2：匹配已成立时取消不产生任何回滚），
/// 调用方以回执的 <see cref="OnlineMatchTicket.State"/> 为准，而不是以「取消接口是否报错」为准；
/// ③ 队伍票据的成员集合整队进整队出（VC-4.3），本类不提供按单个成员退出票据的入口——
/// 成员变动必须先回到 Party 域（<see cref="Party.OnlinePartyService.LeaveAsync"/> → 票据随队伍状态收敛）。
/// </para>
/// </summary>
public sealed class OnlineMatchTicketService
{
    /// <summary>票据存储（原子边界所在）。</summary>
    private readonly IOnlineMatchTicketStore _store;

    /// <summary>事件发布器。</summary>
    private readonly IOnlineEventPublisher _eventPublisher;

    /// <summary>匹配与限流可配置项。</summary>
    private readonly OnlineMatchmakerOptions _options;

    /// <summary>入队/取消限流器（VC-4.11）。</summary>
    private readonly OnlineMatchRateLimiter _rateLimiter;

    /// <summary>
    /// 初始化 <see cref="OnlineMatchTicketService"/>。
    /// </summary>
    /// <param name="store">票据存储。</param>
    /// <param name="eventPublisher">事件发布器。</param>
    /// <param name="options">匹配与限流可配置项（null 取默认值）。</param>
    public OnlineMatchTicketService(IOnlineMatchTicketStore store, IOnlineEventPublisher eventPublisher, OnlineMatchmakerOptions options = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        _options = options == null ? new OnlineMatchmakerOptions() : options.Copy();
        _rateLimiter = new OnlineMatchRateLimiter(_options.RateLimitMaxOperations, _options.RateLimitWindowSeconds);
    }

    /// <summary>
    /// 入队（VC-4.3：队伍成员整队进入或整队不进入）。
    /// <para>
    /// 重复排队被拒（VC-4.4 / VC-4.12）：同一队伍、或任一所携带玩家已有排队中票据，一律返回
    /// <see cref="OnlineErrorCode.DuplicateRequest"/>，不产生第二张票据。
    /// </para>
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="request">入队请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>入队后的票据。</returns>
    public async Task<OnlineResult<OnlineMatchTicket>> EnqueueAsync(OnlineScope scope, OnlineMatchTicketEnqueueRequest request, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope(scope);
        if (failure != null)
        {
            return failure;
        }

        if (request == null)
        {
            return OnlineResult<OnlineMatchTicket>.Fail(OnlineErrorCode.ParameterInvalid, "缺少入队请求");
        }

        if (request.TeamSize <= 0)
        {
            return OnlineResult<OnlineMatchTicket>.Fail(OnlineErrorCode.ParameterInvalid, "对局规模必须为正数");
        }

        if (request.SkillRange != null && request.SkillRange.Min > request.SkillRange.Max)
        {
            return OnlineResult<OnlineMatchTicket>.Fail(OnlineErrorCode.ParameterInvalid, "技术水平区间下界不得大于上界");
        }

        var members = ResolveMembers(scope.PlayerId, request.PlayerIds);
        if (members == null)
        {
            return OnlineResult<OnlineMatchTicket>.Fail(OnlineErrorCode.ParameterInvalid, "票据玩家集合非法");
        }

        if (!_rateLimiter.TryAcquire(scope.TenantId, scope.AppId, scope.PlayerId))
        {
            return OnlineResult<OnlineMatchTicket>.Fail(OnlineErrorCode.RateLimitExceeded, "操作过于频繁，请稍后重试");
        }

        var partyId = request.PartyId ?? string.Empty;
        if (partyId.Length > 0)
        {
            var existing = await _store.FindActiveByPartyAsync(scope.TenantId, scope.AppId, partyId, cancellationToken).ConfigureAwait(false);
            if (existing != null)
            {
                return OnlineResult<OnlineMatchTicket>.Fail(OnlineErrorCode.DuplicateRequest, "该队伍已在匹配队列中");
            }
        }

        foreach (var playerId in members)
        {
            var existing = await _store.FindActiveByPlayerAsync(scope.TenantId, scope.AppId, playerId, cancellationToken).ConfigureAwait(false);
            if (existing != null)
            {
                return OnlineResult<OnlineMatchTicket>.Fail(OnlineErrorCode.DuplicateRequest, "玩家已在匹配队列中");
            }
        }

        var now = Now();
        var ticket = new OnlineMatchTicket
        {
            TicketId = "tkt-" + Guid.NewGuid().ToString("N"),
            PartyId = partyId,
            PlayerIds = members,
            TenantId = scope.TenantId,
            AppId = scope.AppId,
            ServerId = scope.ServerId,
            Mode = request.Mode,
            Region = request.Region,
            SkillRange = request.SkillRange == null ? null : request.SkillRange.Copy(),
            TeamSize = request.TeamSize,
            LatencyRequirement = request.LatencyRequirement,
            CustomProperties = CopyProperties(request.CustomProperties),
            CreatedAtTime = now,
            ExpiresAtTime = now + (_options.TicketTimeToLiveSeconds * 1000L),
            State = OnlineMatchTicketState.Queued,
            FailureReason = OnlineMatchFailureReason.None,
            AssignmentId = string.Empty,
        };

        await _store.SaveAsync(ticket, cancellationToken).ConfigureAwait(false);

        return OnlineResult<OnlineMatchTicket>.Ok(ticket);
    }

    /// <summary>
    /// 取消排队中的票据（VC-4.2 / VC-4.9：取消后不得再产出该票据的结果）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="ticketId">票据标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>取消后的票据（或终态回执）。</returns>
    public async Task<OnlineResult<OnlineMatchTicket>> CancelAsync(OnlineScope scope, string ticketId, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope(scope);
        if (failure != null)
        {
            return failure;
        }

        if (string.IsNullOrEmpty(ticketId))
        {
            return OnlineResult<OnlineMatchTicket>.Fail(OnlineErrorCode.ParameterInvalid, "缺少票据标识");
        }

        var ticket = await _store.FindAsync(scope.TenantId, scope.AppId, ticketId, cancellationToken).ConfigureAwait(false);
        if (ticket == null || !IsReachable(scope, ticket) || !ContainsPlayer(ticket, scope.PlayerId))
        {
            return OnlineResult<OnlineMatchTicket>.Fail(OnlineErrorCode.ResourceNotFound, "票据不存在或不属于当前玩家");
        }

        if (ticket.State != OnlineMatchTicketState.Queued)
        {
            return OnlineResult<OnlineMatchTicket>.Ok(ticket);
        }

        if (!_rateLimiter.TryAcquire(scope.TenantId, scope.AppId, scope.PlayerId))
        {
            return OnlineResult<OnlineMatchTicket>.Fail(OnlineErrorCode.RateLimitExceeded, "操作过于频繁，请稍后重试");
        }

        var cancelled = await _store.UpdateStateAsync(scope.TenantId, scope.AppId, new MatchTicketStateTransition { TicketId = ticketId, ExpectedState = OnlineMatchTicketState.Queued, NewState = OnlineMatchTicketState.Cancelled, FailureReason = OnlineMatchFailureReason.CancelledByPlayer, AssignmentId = string.Empty }, cancellationToken).ConfigureAwait(false);
        if (cancelled == null)
        {
            var receipt = await _store.FindAsync(scope.TenantId, scope.AppId, ticketId, cancellationToken).ConfigureAwait(false);
            if (receipt == null)
            {
                return OnlineResult<OnlineMatchTicket>.Fail(OnlineErrorCode.ResourceNotFound, "票据不存在或不属于当前玩家");
            }

            return OnlineResult<OnlineMatchTicket>.Ok(receipt);
        }

        await _eventPublisher.PublishAsync(OnlineMatchEvents.CreateTicketChanged(cancelled, OnlineMatchTicketState.Queued, OnlineMatchTicketState.Cancelled), cancellationToken).ConfigureAwait(false);

        return OnlineResult<OnlineMatchTicket>.Ok(cancelled);
    }

    /// <summary>
    /// 按标识查询票据（含终态历史；跨作用域与跨玩家一律不可见）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="ticketId">票据标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>票据副本。</returns>
    public async Task<OnlineResult<OnlineMatchTicket>> GetAsync(OnlineScope scope, string ticketId, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope(scope);
        if (failure != null)
        {
            return failure;
        }

        if (string.IsNullOrEmpty(ticketId))
        {
            return OnlineResult<OnlineMatchTicket>.Fail(OnlineErrorCode.ParameterInvalid, "缺少票据标识");
        }

        var ticket = await _store.FindAsync(scope.TenantId, scope.AppId, ticketId, cancellationToken).ConfigureAwait(false);
        if (ticket == null || !IsReachable(scope, ticket) || !ContainsPlayer(ticket, scope.PlayerId))
        {
            return OnlineResult<OnlineMatchTicket>.Fail(OnlineErrorCode.ResourceNotFound, "票据不存在或不属于当前玩家");
        }

        return OnlineResult<OnlineMatchTicket>.Ok(ticket);
    }

    /// <summary>
    /// 查询当前玩家排队中的票据（VC-4.10：客户端可确定性获知「我在排队 / 我已被匹配」）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>排队中的票据；不在队列中返回 null。</returns>
    public async Task<OnlineResult<OnlineMatchTicket>> GetActiveAsync(OnlineScope scope, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope(scope);
        if (failure != null)
        {
            return failure;
        }

        var ticket = await _store.FindActiveByPlayerAsync(scope.TenantId, scope.AppId, scope.PlayerId, cancellationToken).ConfigureAwait(false);
        return OnlineResult<OnlineMatchTicket>.Ok(ticket);
    }

    /// <summary>
    /// 扫描并把超过存活时长的排队票据置为 <see cref="OnlineMatchTicketState.Expired"/>（VC-4.9）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒；传 0 取系统时钟）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>本次过期的票据数。</returns>
    public async Task<OnlineResult<int>> SweepExpiredAsync(long tenantId, long appId, long nowUnixMilliseconds = 0, CancellationToken cancellationToken = default)
    {
        var now = nowUnixMilliseconds > 0 ? nowUnixMilliseconds : Now();
        var queued = await _store.ListQueuedAsync(tenantId, appId, cancellationToken).ConfigureAwait(false);

        var expired = new List<OnlineMatchTicket>();
        foreach (var ticket in queued)
        {
            if (ticket.ExpiresAtTime <= 0 || ticket.ExpiresAtTime > now)
            {
                continue;
            }

            var updated = await _store.UpdateStateAsync(tenantId, appId, new MatchTicketStateTransition { TicketId = ticket.TicketId, ExpectedState = OnlineMatchTicketState.Queued, NewState = OnlineMatchTicketState.Expired, FailureReason = OnlineMatchFailureReason.WaitTimeout, AssignmentId = string.Empty }, cancellationToken).ConfigureAwait(false);
            if (updated != null)
            {
                expired.Add(updated);
            }
        }

        foreach (var ticket in expired)
        {
            await _eventPublisher.PublishAsync(OnlineMatchEvents.CreateTicketChanged(ticket, OnlineMatchTicketState.Queued, OnlineMatchTicketState.Expired), cancellationToken).ConfigureAwait(false);
        }

        return OnlineResult<int>.Ok(expired.Count);
    }

    /// <summary>
    /// 规整票据携带的玩家集合：去重、升序，并保证发起人一定在内。
    /// </summary>
    /// <param name="requesterId">发起玩家标识。</param>
    /// <param name="playerIds">请求携带的玩家集合。</param>
    /// <returns>规整后的玩家集合；含非法成员返回 null。</returns>
    private static List<long> ResolveMembers(long requesterId, List<long> playerIds)
    {
        var members = new SortedSet<long>();
        if (playerIds != null)
        {
            foreach (var playerId in playerIds)
            {
                if (playerId <= 0)
                {
                    return null;
                }

                members.Add(playerId);
            }
        }

        members.Add(requesterId);
        return new List<long>(members);
    }

    /// <summary>
    /// 复制自定义匹配属性。
    /// </summary>
    /// <param name="properties">源属性集合。</param>
    /// <returns>属性副本（非 null）。</returns>
    private static Dictionary<string, string> CopyProperties(Dictionary<string, string> properties)
    {
        var copy = new Dictionary<string, string>();
        if (properties == null)
        {
            return copy;
        }

        foreach (var pair in properties)
        {
            copy[pair.Key] = pair.Value;
        }

        return copy;
    }

    /// <summary>
    /// 判断票据是否属于调用方所在区服（<c>ServerId = 0</c> 的平台级作用域不受限）。
    /// </summary>
    /// <param name="scope">生效作用域。</param>
    /// <param name="ticket">票据。</param>
    /// <returns>可达返回 <c>true</c>。</returns>
    private static bool IsReachable(OnlineScope scope, OnlineMatchTicket ticket)
    {
        return scope.ServerId <= 0 || ticket.ServerId <= 0 || scope.ServerId == ticket.ServerId;
    }

    /// <summary>
    /// 判断票据是否携带指定玩家。
    /// </summary>
    /// <param name="ticket">票据。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <returns>携带返回 <c>true</c>。</returns>
    private static bool ContainsPlayer(OnlineMatchTicket ticket, long playerId)
    {
        return ticket.PlayerIds != null && ticket.PlayerIds.Contains(playerId);
    }

    /// <summary>
    /// 校验作用域与玩家主体位。
    /// </summary>
    /// <param name="scope">生效作用域。</param>
    /// <returns>成功返回 null，失败返回错误结果。</returns>
    private static OnlineResult<OnlineMatchTicket> ValidateScope(OnlineScope scope)
    {
        if (scope == null)
        {
            return OnlineResult<OnlineMatchTicket>.Fail(OnlineErrorCode.ParameterInvalid, "缺少作用域");
        }

        if (scope.PlayerId <= 0)
        {
            return OnlineResult<OnlineMatchTicket>.Fail(OnlineErrorCode.ParameterInvalid, "票据操作必须具备玩家主体位");
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
