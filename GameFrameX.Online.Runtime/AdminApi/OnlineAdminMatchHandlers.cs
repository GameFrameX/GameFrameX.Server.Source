// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应开源许可证与规定。
//   Usage of this project must strictly comply with applicable open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目 实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   Any legal disputes or liabilities arising from secondary development based on this project
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://cnb.cool/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using GameFrameX.Online.Contracts;
using GameFrameX.Online.Matchmaking;
using GameFrameX.Online.Presence;
using GameFrameX.Online.Scope;
using GameFrameX.Online.Social;

namespace GameFrameX.Online.Runtime.AdminApi;

/// <summary>
/// 匹配域 admin 命令与查询（query_match_queue_overview / query_match_ticket_failures / query_match_abnormal_tickets /
/// cancel_match_ticket / end_abnormal_match / suspend_resume_player / match_isolation）。
/// <para>
/// 维护约束：cancel / suspend_resume / match_isolation 为 Admin 受控四命令（幂等键
/// <c>onlineControlled-{action}[-{apply|lift}]-{targetId}-{serverId}</c>）经调度器层幂等包裹；
/// end_abnormal_match 按变更 C122 决策⑧①固定 5003 拒绝（对局结束属游戏进程权威，admin 面无该命令通道）；
/// 队列吞吐为快照内已匹配票据计数代理值（升级路径：观察器接入滑动窗口计数）；
/// RepeatedCancel 判定 = 同队伍已取消票据数 ≥ 3（C122 决策⑧群扫最小语义）。
/// </para>
/// </summary>
public sealed class OnlineAdminMatchHandlers
{
    /// <summary>
    /// 宿主。
    /// </summary>
    private readonly OnlineRuntimeHost _host;

    /// <summary>
    /// 初始化 <see cref="OnlineAdminMatchHandlers"/>。
    /// </summary>
    /// <param name="host">宿主。</param>
    public OnlineAdminMatchHandlers(OnlineRuntimeHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    /// <summary>
    /// 注册匹配域 action。
    /// </summary>
    /// <param name="dispatcher">调度器。</param>
    public void Register(OnlineAdminApiDispatcher dispatcher)
    {
        dispatcher.Register("query_match_queue_overview", QueryMatchQueueOverviewAsync, false);
        dispatcher.Register("query_match_ticket_failures", QueryMatchTicketFailuresAsync, false);
        dispatcher.Register("query_match_abnormal_tickets", QueryMatchAbnormalTicketsAsync, false);
        dispatcher.Register("cancel_match_ticket", CancelMatchTicketAsync, true);
        dispatcher.Register("end_abnormal_match", EndAbnormalMatchAsync, false);
        dispatcher.Register("suspend_resume_player", SuspendResumePlayerAsync, true);
        dispatcher.Register("match_isolation", MatchIsolationAsync, true);
    }

    /// <summary>
    /// query_match_queue_overview：匹配队列概况（快照内按 模式 × 区域 分组）。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <param name="scope">作用域。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>队列概况。</returns>
    public async Task<object> QueryMatchQueueOverviewAsync(OnlineAdminApiRequest request, OnlineScope scope, CancellationToken cancellationToken)
    {
        var modeFilter = OnlineAdminApiContract.ReadOptionalString(request, "Mode");
        var regionFilter = OnlineAdminApiContract.ReadOptionalString(request, "Region");
        var snapshot = OnlineAdminApiContract.Unwrap(await _host.MatchQueueObserver.ObserveAsync(scope.TenantId, scope.AppId, 0, cancellationToken).ConfigureAwait(false));
        var nowMilliseconds = OnlineAdminApiContract.NowSeconds() * 1000;
        var queues = new List<QueueSummaryResponse>();
        if (snapshot.Tickets != null)
        {
            foreach (var group in snapshot.Tickets.GroupBy(ticket => new { Mode = ticket.Mode, Region = ticket.Region }))
            {
                var modeText = group.Key.Mode.ToString(System.Globalization.CultureInfo.InvariantCulture);
                var regionText = group.Key.Region.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (!string.IsNullOrEmpty(modeFilter) && !string.Equals(modeText, modeFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(regionFilter) && !string.Equals(regionText, regionFilter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var queued = group.Where(ticket => ticket.State == OnlineMatchTicketState.Queued).ToList();
                queues.Add(new QueueSummaryResponse
                {
                    Mode = modeText,
                    Region = regionText,
                    QueueDepth = queued.Count,
                    AverageWaitSeconds = queued.Count == 0 ? 0 : queued.Sum(ticket => ticket.WaitSeconds) / queued.Count,
                    ThroughputPerMinute = group.Count(ticket => ticket.State == OnlineMatchTicketState.Matched),
                });
            }
        }

        return new MatchQueueOverviewResponse
        {
            ServerTime = nowMilliseconds,
            Queues = queues,
        };
    }

    /// <summary>
    /// query_match_ticket_failures：失败 / 过期 / 取消票据查询（含全量原因汇总）。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <param name="scope">作用域。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>失败票据页。</returns>
    public async Task<object> QueryMatchTicketFailuresAsync(OnlineAdminApiRequest request, OnlineScope scope, CancellationToken cancellationToken)
    {
        var reasonCodeFilter = OnlineAdminApiContract.ReadOptionalString(request, "ReasonCode");
        var startTime = OnlineAdminApiContract.ReadOptionalInt64(request, "StartTime");
        var endTime = OnlineAdminApiContract.ReadOptionalInt64(request, "EndTime");
        var pageSize = OnlineAdminApiContract.ReadPageSize(request);
        var cursor = OnlineAdminApiContract.ReadCursor(request);
        var tickets = await _host.MatchTicketStore.ListAllAsync(scope.TenantId, scope.AppId, cancellationToken).ConfigureAwait(false);
        var failed = tickets
            .Where(ticket => ticket.FailureReason != OnlineMatchFailureReason.None
                             || ticket.State == OnlineMatchTicketState.Expired
                             || ticket.State == OnlineMatchTicketState.Cancelled
                             || ticket.State == OnlineMatchTicketState.Failed)
            .Select(ticket => new { Ticket = ticket, ReasonCode = ToReasonCode(ticket.FailureReason), OccurredAt = ticket.ExpiresAtTime > 0 ? ticket.ExpiresAtTime : ticket.CreatedAtTime, })
            .Where(item => (string.IsNullOrEmpty(reasonCodeFilter) || string.Equals(item.ReasonCode, reasonCodeFilter, StringComparison.OrdinalIgnoreCase))
                           && (!startTime.HasValue || item.OccurredAt >= startTime.Value)
                           && (!endTime.HasValue || item.OccurredAt <= endTime.Value))
            .OrderByDescending(item => item.OccurredAt)
            .ThenByDescending(item => item.Ticket.TicketId, StringComparer.Ordinal)
            .ToList();
        if (!string.IsNullOrEmpty(cursor) && TryParseCursor(cursor, out var cursorTime, out var cursorId))
        {
            failed = failed.Where(item => item.OccurredAt < cursorTime
                                          || (item.OccurredAt == cursorTime && string.CompareOrdinal(item.Ticket.TicketId, cursorId) < 0)).ToList();
        }

        var reasonSummary = failed
            .GroupBy(item => item.ReasonCode)
            .Select(group => new ReasonCountResponse { ReasonCode = group.Key, Count = group.Count(), })
            .OrderByDescending(item => item.Count)
            .ToList();
        var hasMore = failed.Count > pageSize;
        var pageItems = hasMore ? failed.Take(pageSize).ToList() : failed;
        var items = new List<TicketFailureResponse>();
        foreach (var item in pageItems)
        {
            items.Add(new TicketFailureResponse
            {
                TicketId = item.Ticket.TicketId,
                PartyId = item.Ticket.PartyId,
                ReasonCode = item.ReasonCode,
                ReasonText = DescribeFailure(item.Ticket.FailureReason),
                OccurredAt = item.OccurredAt,
            });
        }

        string nextCursor = null;
        if (hasMore && pageItems.Count > 0)
        {
            var last = pageItems[pageItems.Count - 1];
            nextCursor = last.OccurredAt.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + last.Ticket.TicketId;
        }

        return new TicketFailureQueryResponse
        {
            ServerTime = OnlineAdminApiContract.NowSeconds() * 1000,
            Items = items,
            ReasonSummary = reasonSummary,
            NextCursor = nextCursor,
            HasMore = hasMore,
        };
    }

    /// <summary>
    /// query_match_abnormal_tickets：异常票据查询（LongWaiting=1 / RepeatedCancel=2 / Orphan=3）。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <param name="scope">作用域。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异常票据页。</returns>
    public async Task<object> QueryMatchAbnormalTicketsAsync(OnlineAdminApiRequest request, OnlineScope scope, CancellationToken cancellationToken)
    {
        var abnormalKind = request.ReadNullableInt32("AbnormalKind");
        if (!abnormalKind.HasValue || abnormalKind.Value < 1 || abnormalKind.Value > 3)
        {
            throw new OnlineServiceException(OnlineErrorCode.ParameterInvalid, "AbnormalKind must be a number in [1,3] (LongWaiting/RepeatedCancel/Orphan).");
        }

        var pageSize = OnlineAdminApiContract.ReadPageSize(request);
        var cursor = OnlineAdminApiContract.ReadCursor(request);
        var tickets = await _host.MatchTicketStore.ListAllAsync(scope.TenantId, scope.AppId, cancellationToken).ConfigureAwait(false);
        var nowMilliseconds = OnlineAdminApiContract.NowSeconds() * 1000;
        List<AbnormalTicketView> abnormal;
        if (abnormalKind.Value == 2)
        {
            abnormal = BuildRepeatedCancelViews(tickets);
        }
        else if (abnormalKind.Value == 1)
        {
            abnormal = CollectLongWaitingViews(tickets, nowMilliseconds);
        }
        else
        {
            abnormal = await CollectOrphanViewsAsync(tickets, scope, cancellationToken).ConfigureAwait(false);
        }

        var ordered = abnormal
            .OrderByDescending(view => view.Ticket.CreatedAtTime)
            .ThenByDescending(view => view.Ticket.TicketId, StringComparer.Ordinal)
            .ToList();
        ordered = ApplyAbnormalCursor(ordered, cursor);

        var hasMore = ordered.Count > pageSize;
        var pageItems = hasMore ? ordered.Take(pageSize).ToList() : ordered;
        var items = new List<AbnormalTicketResponse>();
        foreach (var view in pageItems)
        {
            items.Add(new AbnormalTicketResponse
            {
                TicketId = view.Ticket.TicketId,
                PartyId = view.Ticket.PartyId,
                Mode = view.Ticket.Mode.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Region = view.Ticket.Region.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Status = view.Ticket.State.ToString(),
                CreatedAt = view.Ticket.CreatedAtTime,
                ExpiresAt = view.Ticket.ExpiresAtTime > 0 ? view.Ticket.ExpiresAtTime : (long?)null,
                WaitingSeconds = view.WaitingSeconds,
                CancelCount = view.CancelCount,
            });
        }

        string nextCursor = null;
        if (hasMore && pageItems.Count > 0)
        {
            var last = pageItems[pageItems.Count - 1];
            nextCursor = last.Ticket.CreatedAtTime.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + last.Ticket.TicketId;
        }

        return new AbnormalTicketQueryResponse
        {
            ServerTime = nowMilliseconds,
            Items = items,
            NextCursor = nextCursor,
            HasMore = hasMore,
        };
    }

    /// <summary>
    /// cancel_match_ticket：受控取消排队票据（票据不存在 → 4002；已终态由票据服务语义拒绝）。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <param name="scope">作用域。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>受控命令回执。</returns>
    public async Task<object> CancelMatchTicketAsync(OnlineAdminApiRequest request, OnlineScope scope, CancellationToken cancellationToken)
    {
        var ticketId = OnlineAdminApiContract.RequireString(request, "TicketId");
        var reason = OnlineAdminApiContract.RequireString(request, "Reason");
        var ticket = await _host.MatchTicketStore.FindAsync(scope.TenantId, scope.AppId, ticketId, cancellationToken).ConfigureAwait(false);
        if (ticket == null)
        {
            throw new OnlineServiceException(OnlineErrorCode.ResourceNotFound, "Match ticket not found: " + ticketId);
        }

        var ticketScope = ticket.PlayerIds != null && ticket.PlayerIds.Count > 0
            ? new OnlineScope(scope.TenantId, scope.AppId, scope.ServerId, ticket.PlayerIds[0])
            : scope;
        OnlineAdminApiContract.Unwrap(await _host.MatchTickets.CancelAsync(ticketScope, ticketId, cancellationToken).ConfigureAwait(false));
        return BuildControlledAck(request, ticketId, "Ticket cancelled. Reason: " + reason);
    }

    /// <summary>
    /// end_abnormal_match：固定拒绝（C122 决策⑧①——对局结束属游戏进程权威，admin 面无通道）。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <param name="scope">作用域。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>不返回（恒抛 5003）。</returns>
    public Task<object> EndAbnormalMatchAsync(OnlineAdminApiRequest request, OnlineScope scope, CancellationToken cancellationToken)
    {
        throw new OnlineServiceException(OnlineErrorCode.StateOperationForbidden,
            "Ending an abnormal match is not available on the admin plane; the game process owns match lifecycle (change C122 gap #1).");
    }

    /// <summary>
    /// suspend_resume_player：暂停 / 恢复匹配资格（在线状态阻断位，Apply / Lift 双向）。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <param name="scope">作用域。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>受控命令回执。</returns>
    public async Task<object> SuspendResumePlayerAsync(OnlineAdminApiRequest request, OnlineScope scope, CancellationToken cancellationToken)
    {
        var playerScope = OnlineAdminApiContract.ReadPlayerScope(request, scope);
        var direction = ReadDirection(request);
        var reason = OnlineAdminApiContract.RequireString(request, "Reason");
        if (direction == "Apply")
        {
            OnlineAdminApiContract.Unwrap(await _host.Presence.MarkBlockedAsync(playerScope, request.ReadRequestId(), cancellationToken).ConfigureAwait(false));
        }
        else
        {
            OnlineAdminApiContract.Unwrap(await _host.Presence.ReleaseBlockedAsync(playerScope, request.ReadRequestId(), cancellationToken).ConfigureAwait(false));
        }

        return BuildControlledAck(request, playerScope.PlayerId.ToString(System.Globalization.CultureInfo.InvariantCulture), "Match eligibility " + (direction == "Apply" ? "suspended" : "resumed") + ". Reason: " + reason);
    }

    /// <summary>
    /// match_isolation：对局隔离（Apply = 落 Ban 处罚并阻断匹配；Lift = 撤销在效 Ban）。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <param name="scope">作用域。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>受控命令回执。</returns>
    public async Task<object> MatchIsolationAsync(OnlineAdminApiRequest request, OnlineScope scope, CancellationToken cancellationToken)
    {
        var playerScope = OnlineAdminApiContract.ReadPlayerScope(request, scope);
        var direction = ReadDirection(request);
        var reason = OnlineAdminApiContract.RequireString(request, "Reason");
        var adminId = ReadReviewerAdminId(request);
        if (direction == "Apply")
        {
            OnlineAdminApiContract.Unwrap(await _host.Punishments.ApplyAsync(scope.TenantId, scope.AppId, playerScope.PlayerId, OnlinePunishmentKind.Ban,
                "match-isolation:" + reason, 0, 0, adminId, null, request.ReadRequestId(), cancellationToken).ConfigureAwait(false));
        }
        else
        {
            var active = OnlineAdminApiContract.Unwrap(await _host.Punishments.ListActiveAsync(scope.TenantId, scope.AppId, playerScope.PlayerId, 0, cancellationToken).ConfigureAwait(false));
            var ban = active.FirstOrDefault(punishment => punishment.Kind == OnlinePunishmentKind.Ban && !punishment.Revoked);
            if (ban != null)
            {
                OnlineAdminApiContract.Unwrap(await _host.Punishments.RevokeAsync(scope.TenantId, scope.AppId, ban.PunishmentId, adminId, request.ReadRequestId(), cancellationToken).ConfigureAwait(false));
            }
        }

        return BuildControlledAck(request, playerScope.PlayerId.ToString(System.Globalization.CultureInfo.InvariantCulture), "Match isolation " + (direction == "Apply" ? "applied" : "lifted") + ". Reason: " + reason);
    }

    /// <summary>
    /// 构造受控命令回执（Command 回显线缆值，缺失回退 action 名）。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <param name="targetIdentifier">目标标识。</param>
    /// <param name="summary">摘要文本。</param>
    /// <returns>受控命令回执。</returns>
    private static ControlledCommandAckResponse BuildControlledAck(OnlineAdminApiRequest request, string targetIdentifier, string summary)
    {
        return new ControlledCommandAckResponse
        {
            Command = request.ReadString("Command") ?? request.Action,
            TargetIdentifier = targetIdentifier,
            IdempotencyKey = request.ReadIdempotencyKey() ?? string.Empty,
            Status = "Executed",
            ExecutedAt = OnlineAdminApiContract.NowSeconds() * 1000,
            Summary = summary,
        };
    }

    /// <summary>
    /// 解析审核操作者的数值管理员标识（处罚服务审计红线要求非 0：ReviewerOperatorId 稳定散射为正 long，缺省 1 = admin 面系统位）。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <returns>正数管理员标识。</returns>
    private static long ReadReviewerAdminId(OnlineAdminApiRequest request)
    {
        var reviewer = OnlineAdminApiContract.ReadOptionalString(request, "ReviewerOperatorId");
        var value = 1L;
        if (!string.IsNullOrEmpty(reviewer))
        {
            foreach (var character in reviewer)
            {
                unchecked
                {
                    value = value * 31 + character;
                }
            }

            if (value <= 0)
            {
                value = -value;
            }

            if (value == 0)
            {
                value = 1;
            }
        }

        return value;
    }

    /// <summary>
    /// 读取方向（Apply / Lift；受控双向命令必填）。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <returns>方向文本。</returns>
    private static string ReadDirection(OnlineAdminApiRequest request)
    {
        var direction = OnlineAdminApiContract.ReadOptionalString(request, "Direction");
        if (string.Equals(direction, "Apply", StringComparison.OrdinalIgnoreCase) || string.Equals(direction, "Lift", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(direction, "Apply", StringComparison.OrdinalIgnoreCase) ? "Apply" : "Lift";
        }

        throw new OnlineServiceException(OnlineErrorCode.ParameterInvalid, "Direction must be 'Apply' or 'Lift'.");
    }

    /// <summary>
    /// 失败原因 → Admin 线缆原因码（Timeout / CancelRace / RateLimited / DuplicateTicket）。
    /// </summary>
    /// <param name="reason">失败原因。</param>
    /// <returns>线缆原因码。</returns>
    private static string ToReasonCode(OnlineMatchFailureReason reason)
    {
        switch (reason)
        {
            case OnlineMatchFailureReason.WaitTimeout:
                return "Timeout";
            case OnlineMatchFailureReason.CancelledByPlayer:
                return "CancelRace";
            case OnlineMatchFailureReason.RateLimited:
                return "RateLimited";
            case OnlineMatchFailureReason.DuplicateEnqueue:
                return "DuplicateTicket";
            default:
                return reason.ToString();
        }
    }

    /// <summary>
    /// 失败原因可读描述。
    /// </summary>
    /// <param name="reason">失败原因。</param>
    /// <returns>描述文本。</returns>
    private static string DescribeFailure(OnlineMatchFailureReason reason)
    {
        return "Match ticket failed with reason " + reason + ".";
    }

    /// <summary>
    /// 构造 RepeatedCancel 视图（同队伍已取消票据数 ≥ 3 的取消票据集合）。
    /// </summary>
    /// <param name="tickets">全量票据。</param>
    /// <returns>异常视图列表。</returns>
    private static List<AbnormalTicketView> BuildRepeatedCancelViews(IReadOnlyList<OnlineMatchTicket> tickets)
    {
        var views = new List<AbnormalTicketView>();
        var cancelledByParty = tickets
            .Where(ticket => ticket.State == OnlineMatchTicketState.Cancelled)
            .GroupBy(ticket => ticket.PartyId ?? string.Empty)
            .ToDictionary(group => group.Key, group => group.Count());
        foreach (var ticket in tickets)
        {
            if (ticket.State != OnlineMatchTicketState.Cancelled)
            {
                continue;
            }

            var partyKey = ticket.PartyId ?? string.Empty;
            if (cancelledByParty.TryGetValue(partyKey, out var cancelCount) && cancelCount >= 3)
            {
                views.Add(new AbnormalTicketView(ticket, null, cancelCount));
            }
        }

        return views;
    }

    /// <summary>
    /// 构造 LongWaiting 视图（排队中且等待超 60 秒的票据集合）。
    /// <para>
    /// 纯函数式扫描：WaitingSeconds = 当前时刻与创建时刻毫秒差 / 1000，CancelCount 恒为 null；判定阈值与数值口径为既有契约。
    /// </para>
    /// </summary>
    /// <param name="tickets">全量票据。</param>
    /// <param name="nowMilliseconds">当前时刻（UTC 毫秒）。</param>
    /// <returns>异常视图列表。</returns>
    private static List<AbnormalTicketView> CollectLongWaitingViews(IReadOnlyList<OnlineMatchTicket> tickets, long nowMilliseconds)
    {
        var views = new List<AbnormalTicketView>();
        foreach (var ticket in tickets)
        {
            if (ticket.State == OnlineMatchTicketState.Queued && nowMilliseconds - ticket.CreatedAtTime > 60000L)
            {
                views.Add(new AbnormalTicketView(ticket, (nowMilliseconds - ticket.CreatedAtTime) / 1000L, null));
            }
        }

        return views;
    }

    /// <summary>
    /// 构造 Orphan 视图（已匹配、派给标识为空且查无派给记录的票据集合）。
    /// <para>
    /// 跳过非 Matched 或带派给标识的票据；仅 Matched 且派给标识为空的候选按存储返回顺序
    /// 逐票串行查询派给记录，查无派给才入列；两附加值（等待秒数 / 取消计数）均为 null。
    /// </para>
    /// </summary>
    /// <param name="tickets">全量票据。</param>
    /// <param name="scope">作用域。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异常视图列表。</returns>
    private async Task<List<AbnormalTicketView>> CollectOrphanViewsAsync(IReadOnlyList<OnlineMatchTicket> tickets, OnlineScope scope, CancellationToken cancellationToken)
    {
        var views = new List<AbnormalTicketView>();
        foreach (var ticket in tickets)
        {
            if (ticket.State != OnlineMatchTicketState.Matched || !string.IsNullOrEmpty(ticket.AssignmentId))
            {
                continue;
            }

            var assignment = await _host.MatchTicketStore.FindAssignmentAsync(scope.TenantId, scope.AppId, ticket.AssignmentId, cancellationToken).ConfigureAwait(false);
            if (assignment == null)
            {
                views.Add(new AbnormalTicketView(ticket, null, null));
            }
        }

        return views;
    }

    /// <summary>
    /// 按「时刻:标识」复合游标过滤异常视图（创建时刻严格小于游标时刻，或相等且标识序小于游标标识）。
    /// <para>
    /// 游标为空或不可解析时原样返回，过滤语义为既有契约。
    /// </para>
    /// </summary>
    /// <param name="views">排序后的异常视图。</param>
    /// <param name="cursor">游标原文。</param>
    /// <returns>过滤后的视图列表。</returns>
    private static List<AbnormalTicketView> ApplyAbnormalCursor(List<AbnormalTicketView> views, string cursor)
    {
        if (!string.IsNullOrEmpty(cursor) && TryParseCursor(cursor, out var cursorTime, out var cursorId))
        {
            return views.Where(view => view.Ticket.CreatedAtTime < cursorTime
                                       || (view.Ticket.CreatedAtTime == cursorTime && string.CompareOrdinal(view.Ticket.TicketId, cursorId) < 0)).ToList();
        }

        return views;
    }

    /// <summary>
    /// 解析「时刻:标识」复合游标。
    /// </summary>
    /// <param name="cursor">游标原文。</param>
    /// <param name="time">输出：时刻。</param>
    /// <param name="id">输出：标识。</param>
    /// <returns>解析成功返回 true。</returns>
    private static bool TryParseCursor(string cursor, out long time, out string id)
    {
        time = 0;
        id = null;
        var separatorIndex = cursor.LastIndexOf(':');
        if (separatorIndex <= 0 || separatorIndex >= cursor.Length - 1)
        {
            return false;
        }

        id = cursor.Substring(separatorIndex + 1);
        return long.TryParse(cursor.Substring(0, separatorIndex), out time);
    }

    /// <summary>
    /// 异常票据中间视图。
    /// </summary>
    private sealed class AbnormalTicketView
    {
        /// <summary>
        /// 票据。
        /// </summary>
        public OnlineMatchTicket Ticket
        {
            get;
        }

        /// <summary>
        /// 等待秒数（LongWaiting 专属）。
        /// </summary>
        public long? WaitingSeconds
        {
            get;
        }

        /// <summary>
        /// 队伍取消计数（RepeatedCancel 专属）。
        /// </summary>
        public long? CancelCount
        {
            get;
        }

        /// <summary>
        /// 初始化视图。
        /// </summary>
        /// <param name="ticket">票据。</param>
        /// <param name="waitingSeconds">等待秒数。</param>
        /// <param name="cancelCount">取消计数。</param>
        public AbnormalTicketView(OnlineMatchTicket ticket, long? waitingSeconds, long? cancelCount)
        {
            Ticket = ticket;
            WaitingSeconds = waitingSeconds;
            CancelCount = cancelCount;
        }
    }

    /// <summary>
    /// 队列摘要（对齐 Admin OnlineMatchQueueSummaryResponse）。
    /// </summary>
    public sealed class QueueSummaryResponse
    {
        /// <summary>获取或设置模式（数字字符串）。</summary>
        public string Mode
        {
            get;
            set;
        }

        /// <summary>获取或设置区域（数字字符串）。</summary>
        public string Region
        {
            get;
            set;
        }

        /// <summary>获取或设置队列深度。</summary>
        public long QueueDepth
        {
            get;
            set;
        }

        /// <summary>获取或设置平均等待秒数。</summary>
        public long AverageWaitSeconds
        {
            get;
            set;
        }

        /// <summary>获取或设置每分钟吞吐（快照内已匹配计数代理值）。</summary>
        public long ThroughputPerMinute
        {
            get;
            set;
        }
    }

    /// <summary>
    /// 队列概况（对齐 Admin OnlineMatchQueueOverviewResponse）。
    /// </summary>
    public sealed class MatchQueueOverviewResponse
    {
        /// <summary>获取或设置服务端时刻（UTC 毫秒）。</summary>
        public long ServerTime
        {
            get;
            set;
        }

        /// <summary>获取或设置队列摘要列表。</summary>
        public List<QueueSummaryResponse> Queues
        {
            get;
            set;
        }
    }

    /// <summary>
    /// 失败票据条目（对齐 Admin OnlineMatchTicketFailureResponse）。
    /// </summary>
    public sealed class TicketFailureResponse
    {
        /// <summary>获取或设置票据标识。</summary>
        public string TicketId
        {
            get;
            set;
        }

        /// <summary>获取或设置队伍标识。</summary>
        public string PartyId
        {
            get;
            set;
        }

        /// <summary>获取或设置原因码（Timeout / CancelRace / RateLimited / DuplicateTicket）。</summary>
        public string ReasonCode
        {
            get;
            set;
        }

        /// <summary>获取或设置原因描述。</summary>
        public string ReasonText
        {
            get;
            set;
        }

        /// <summary>获取或设置发生时刻（UTC 毫秒）。</summary>
        public long OccurredAt
        {
            get;
            set;
        }
    }

    /// <summary>
    /// 原因计数（对齐 Admin OnlineMatchReasonCountResponse）。
    /// </summary>
    public sealed class ReasonCountResponse
    {
        /// <summary>获取或设置原因码。</summary>
        public string ReasonCode
        {
            get;
            set;
        }

        /// <summary>获取或设置计数。</summary>
        public long Count
        {
            get;
            set;
        }
    }

    /// <summary>
    /// 失败票据查询页（对齐 Admin OnlineMatchTicketFailureQueryResponse）。
    /// </summary>
    public sealed class TicketFailureQueryResponse
    {
        /// <summary>获取或设置服务端时刻（UTC 毫秒）。</summary>
        public long ServerTime
        {
            get;
            set;
        }

        /// <summary>获取或设置失败票据列表。</summary>
        public List<TicketFailureResponse> Items
        {
            get;
            set;
        }

        /// <summary>获取或设置原因汇总（全量过滤集）。</summary>
        public List<ReasonCountResponse> ReasonSummary
        {
            get;
            set;
        }

        /// <summary>获取或设置下一页游标。</summary>
        public string NextCursor
        {
            get;
            set;
        }

        /// <summary>获取或设置是否还有更多。</summary>
        public bool HasMore
        {
            get;
            set;
        }
    }

    /// <summary>
    /// 异常票据条目（对齐 Admin OnlineMatchAbnormalTicketResponse）。
    /// </summary>
    public sealed class AbnormalTicketResponse
    {
        /// <summary>获取或设置票据标识。</summary>
        public string TicketId
        {
            get;
            set;
        }

        /// <summary>获取或设置队伍标识。</summary>
        public string PartyId
        {
            get;
            set;
        }

        /// <summary>获取或设置模式（数字字符串）。</summary>
        public string Mode
        {
            get;
            set;
        }

        /// <summary>获取或设置区域（数字字符串）。</summary>
        public string Region
        {
            get;
            set;
        }

        /// <summary>获取或设置状态名（Queued / Matching / Matched / Cancelled / Expired / Failed）。</summary>
        public string Status
        {
            get;
            set;
        }

        /// <summary>获取或设置创建时刻（UTC 毫秒）。</summary>
        public long CreatedAt
        {
            get;
            set;
        }

        /// <summary>获取或设置过期时刻（UTC 毫秒）。</summary>
        public long? ExpiresAt
        {
            get;
            set;
        }

        /// <summary>获取或设置等待秒数（LongWaiting 专属）。</summary>
        public long? WaitingSeconds
        {
            get;
            set;
        }

        /// <summary>获取或设置队伍取消计数（RepeatedCancel 专属）。</summary>
        public long? CancelCount
        {
            get;
            set;
        }
    }

    /// <summary>
    /// 异常票据查询页（对齐 Admin OnlineMatchAbnormalTicketQueryResponse）。
    /// </summary>
    public sealed class AbnormalTicketQueryResponse
    {
        /// <summary>获取或设置服务端时刻（UTC 毫秒）。</summary>
        public long ServerTime
        {
            get;
            set;
        }

        /// <summary>获取或设置异常票据列表。</summary>
        public List<AbnormalTicketResponse> Items
        {
            get;
            set;
        }

        /// <summary>获取或设置下一页游标。</summary>
        public string NextCursor
        {
            get;
            set;
        }

        /// <summary>获取或设置是否还有更多。</summary>
        public bool HasMore
        {
            get;
            set;
        }
    }

    /// <summary>
    /// 受控命令回执（对齐 Admin OnlineControlledCommandAckResponse）。
    /// </summary>
    public sealed class ControlledCommandAckResponse
    {
        /// <summary>获取或设置命令回显。</summary>
        public string Command
        {
            get;
            set;
        }

        /// <summary>获取或设置目标标识。</summary>
        public string TargetIdentifier
        {
            get;
            set;
        }

        /// <summary>获取或设置幂等键回显。</summary>
        public string IdempotencyKey
        {
            get;
            set;
        }

        /// <summary>获取或设置执行状态（Accepted / Executed）。</summary>
        public string Status
        {
            get;
            set;
        }

        /// <summary>获取或设置执行时刻（UTC 毫秒）。</summary>
        public long ExecutedAt
        {
            get;
            set;
        }

        /// <summary>获取或设置摘要。</summary>
        public string Summary
        {
            get;
            set;
        }
    }
}
