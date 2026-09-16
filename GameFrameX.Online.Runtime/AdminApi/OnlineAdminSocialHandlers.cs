// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应开源许可证与规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
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

using GameFrameX.Foundation.Json;
using GameFrameX.Online.Contracts;
using GameFrameX.Online.Scope;
using GameFrameX.Online.Social;
using GameFrameX.Online.Tokens;

namespace GameFrameX.Online.Runtime.AdminApi;

/// <summary>
/// 社交域 admin 命令与查询（kick_player / revoke_token / mute_player / query_report_cases / update_report_case /
/// send_admin_notification / query_chat_messages）。
/// <para>
/// 维护约束：kick / revoke_token / mute 为 Admin 三族处罚命令（幂等键 <c>penalty-{penaltyId}-{kind}-{serverId}</c>），
/// 经调度器层幂等包裹；踢线 / 吊销按「该玩家全部活跃会话」逐会话执行（Admin 线缆无会话标识，玩家级语义）；
/// 举报状态映射固定（Admin Pending/Handling/Resolved/Rejected ↔ Online Submitted/Reviewing/Actioned/Rejected，
/// Withdrawn 回退映射 Rejected）；聊天审计查询走 <see cref="OnlineChatAuditProjection"/>（C122 决策⑧④）。
/// </para>
/// </summary>
public sealed class OnlineAdminSocialHandlers
{
    /// <summary>
    /// 宿主。
    /// </summary>
    private readonly OnlineRuntimeHost _host;

    /// <summary>
    /// 初始化 <see cref="OnlineAdminSocialHandlers"/>。
    /// </summary>
    /// <param name="host">宿主。</param>
    public OnlineAdminSocialHandlers(OnlineRuntimeHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    /// <summary>
    /// 注册社交域 action。
    /// </summary>
    /// <param name="dispatcher">调度器。</param>
    public void Register(OnlineAdminApiDispatcher dispatcher)
    {
        dispatcher.Register("kick_player", KickPlayerAsync, true);
        dispatcher.Register("revoke_token", RevokeTokenAsync, true);
        dispatcher.Register("mute_player", MutePlayerAsync, true);
        dispatcher.Register("query_report_cases", QueryReportCasesAsync, false);
        dispatcher.Register("update_report_case", UpdateReportCaseAsync, false);
        dispatcher.Register("send_admin_notification", SendAdminNotificationAsync, false);
        dispatcher.Register("query_chat_messages", QueryChatMessagesAsync, false);
    }

    /// <summary>
    /// kick_player：踢下线（该玩家全部活跃会话逐会话关闭）。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <param name="scope">作用域。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>受理回执。</returns>
    public async Task<object> KickPlayerAsync(OnlineAdminApiRequest request, OnlineScope scope, CancellationToken cancellationToken)
    {
        var playerScope = OnlineAdminApiContract.ReadPlayerScope(request, scope);
        var reason = BuildPenaltyReason(request);
        var sessions = await _host.SessionStore.ListActiveByPlayerAsync(scope.TenantId, scope.AppId, playerScope.PlayerId, cancellationToken).ConfigureAwait(false);
        foreach (var session in sessions)
        {
            await _host.Tokens.KickAsync(new OnlineTokenKickRequest { Scope = playerScope, SessionId = session.Id, Reason = reason, }, cancellationToken).ConfigureAwait(false);
        }

        return new LinkCommandAckResponse { TokenRevokedConfirmed = true, };
    }

    /// <summary>
    /// revoke_token：吊销令牌（Ban 命令映射；该玩家全部活跃会话逐会话吊销）。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <param name="scope">作用域。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>受理回执。</returns>
    public async Task<object> RevokeTokenAsync(OnlineAdminApiRequest request, OnlineScope scope, CancellationToken cancellationToken)
    {
        var playerScope = OnlineAdminApiContract.ReadPlayerScope(request, scope);
        var reason = BuildPenaltyReason(request);
        var sessions = await _host.SessionStore.ListActiveByPlayerAsync(scope.TenantId, scope.AppId, playerScope.PlayerId, cancellationToken).ConfigureAwait(false);
        foreach (var session in sessions)
        {
            await _host.Tokens.RevokeAsync(new OnlineTokenRevokeRequest { Scope = playerScope, SessionId = session.Id, Reason = reason, }, cancellationToken).ConfigureAwait(false);
        }

        return new LinkCommandAckResponse { TokenRevokedConfirmed = true, };
    }

    /// <summary>
    /// mute_player：禁言（处罚事实源落 <see cref="OnlinePunishmentService"/>，Admin 线缆无期限 → 长期禁言直至解除）。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <param name="scope">作用域。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>受理回执。</returns>
    public async Task<object> MutePlayerAsync(OnlineAdminApiRequest request, OnlineScope scope, CancellationToken cancellationToken)
    {
        var playerScope = OnlineAdminApiContract.ReadPlayerScope(request, scope);
        var adminId = request.ReadNullableInt64("PenaltyId") ?? 0;
        OnlineAdminApiContract.Unwrap(await _host.Punishments.ApplyAsync(scope.TenantId, scope.AppId, playerScope.PlayerId,
            OnlinePunishmentKind.Mute, BuildPenaltyReason(request), 0, 0, adminId, null, request.ReadRequestId(), cancellationToken).ConfigureAwait(false));
        return new LinkCommandAckResponse { TokenRevokedConfirmed = true, };
    }

    /// <summary>
    /// query_report_cases：举报案件查询（状态 / 举报人过滤 + 时间过滤 + 新到旧分页）。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <param name="scope">作用域。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>案件页。</returns>
    public async Task<object> QueryReportCasesAsync(OnlineAdminApiRequest request, OnlineScope scope, CancellationToken cancellationToken)
    {
        var status = ReadAdminStatus(request);
        var reporterPlayerId = ReadOptionalPlayerId(request, "ReporterPlayerId");
        var startTime = OnlineAdminApiContract.ReadOptionalInt64(request, "StartTime");
        var endTime = OnlineAdminApiContract.ReadOptionalInt64(request, "EndTime");
        var pageSize = OnlineAdminApiContract.ReadPageSize(request);
        var cursor = OnlineAdminApiContract.ReadCursor(request);

        var cases = new List<OnlineReportCase>();
        if (status.HasValue)
        {
            cases.AddRange(await _host.ReportStore.ListByStateAsync(scope.TenantId, scope.AppId, status.Value, cancellationToken).ConfigureAwait(false));
        }
        else if (reporterPlayerId.HasValue)
        {
            cases.AddRange(await _host.ReportStore.ListByReporterAsync(scope.TenantId, scope.AppId, reporterPlayerId.Value, cancellationToken).ConfigureAwait(false));
        }
        else
        {
            foreach (OnlineReportState state in Enum.GetValues(typeof(OnlineReportState)))
            {
                cases.AddRange(await _host.ReportStore.ListByStateAsync(scope.TenantId, scope.AppId, state, cancellationToken).ConfigureAwait(false));
            }
        }

        var filtered = cases
            .Where(item => (!reporterPlayerId.HasValue || item.ReporterId == reporterPlayerId.Value)
                           && (!startTime.HasValue || item.CreatedAtTime >= startTime.Value)
                           && (!endTime.HasValue || item.CreatedAtTime <= endTime.Value))
            .OrderByDescending(item => item.CreatedAtTime)
            .ThenByDescending(item => item.ReportId, StringComparer.Ordinal)
            .ToList();
        if (!string.IsNullOrEmpty(cursor) && TryParseReportCursor(cursor, out var cursorTime, out var cursorId))
        {
            filtered = filtered.Where(item => item.CreatedAtTime < cursorTime
                                              || (item.CreatedAtTime == cursorTime && string.CompareOrdinal(item.ReportId, cursorId) < 0)).ToList();
        }

        var hasMore = filtered.Count > pageSize;
        var pageItems = hasMore ? filtered.Take(pageSize).ToList() : filtered;
        var items = new List<ReportCaseResponse>();
        foreach (var item in pageItems)
        {
            items.Add(new ReportCaseResponse
            {
                CaseId = item.ReportId,
                AppId = item.AppId,
                ServerId = scope.ServerId,
                ReporterPlayerId = item.ReporterId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ReportedPlayerId = item.ReportedPlayerId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Scene = item.Scene.ToString(),
                MatchId = item.MatchId,
                ChatMessageId = item.ChatMessageId,
                Reason = item.Reason.ToString(),
                Evidence = item.Evidence,
                Status = ToAdminStatus(item.State),
                HandleResult = item.Resolution.ToString(),
                CreatedAt = item.CreatedAtTime,
                UpdatedAt = item.UpdatedAtTime,
            });
        }

        string nextCursor = null;
        if (hasMore && pageItems.Count > 0)
        {
            var last = pageItems[pageItems.Count - 1];
            nextCursor = last.CreatedAtTime.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + last.ReportId;
        }

        return new ReportCaseQueryResponse
        {
            Items = items,
            NextCursor = nextCursor,
            HasMore = hasMore,
        };
    }

    /// <summary>
    /// update_report_case：举报案件状态迁移（Resolution 映射：Rejected→NoViolation，其余 None）。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <param name="scope">作用域。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>确认载荷（Admin 契约要求 Data 非 null）。</returns>
    public async Task<object> UpdateReportCaseAsync(OnlineAdminApiRequest request, OnlineScope scope, CancellationToken cancellationToken)
    {
        var caseId = OnlineAdminApiContract.RequireString(request, "CaseId");
        var status = ReadAdminStatus(request);
        if (!status.HasValue)
        {
            throw new OnlineServiceException(OnlineErrorCode.ParameterInvalid, "Status must be a number in [1,4] (Pending/Handling/Resolved/Rejected).");
        }

        var resolution = status.Value == OnlineReportState.Rejected ? OnlineReportResolution.NoViolation : OnlineReportResolution.None;
        OnlineAdminApiContract.Unwrap(await _host.SocialDecisions.TransitionReportAsync(scope.TenantId, scope.AppId, caseId, status.Value, resolution,
            0, null, request.ReadRequestId(), cancellationToken).ConfigureAwait(false));
        return new EmptyAckResponse();
    }

    /// <summary>
    /// send_admin_notification：Admin 公告通知（7 天有效期，载荷 = {Title, Content}）。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <param name="scope">作用域。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>确认载荷。</returns>
    public async Task<object> SendAdminNotificationAsync(OnlineAdminApiRequest request, OnlineScope scope, CancellationToken cancellationToken)
    {
        var playerScope = OnlineAdminApiContract.ReadPlayerScope(request, scope);
        var title = OnlineAdminApiContract.RequireString(request, "Title");
        var content = OnlineAdminApiContract.RequireString(request, "Content");
        var payload = JsonHelper.Serialize(new NotificationPayload { Title = title, Content = content, });
        OnlineAdminApiContract.Unwrap(await _host.Notifications.EnqueueAsync(playerScope, OnlineNotificationKind.Announcement, null,
            payload, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 604800000L, request.ReadRequestId(), cancellationToken).ConfigureAwait(false));
        return new EmptyAckResponse();
    }

    /// <summary>
    /// query_chat_messages：聊天审计查询（事件投影 + store 正文回读；C122 决策⑧④）。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <param name="scope">作用域。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>消息页。</returns>
    public async Task<object> QueryChatMessagesAsync(OnlineAdminApiRequest request, OnlineScope scope, CancellationToken cancellationToken)
    {
        var channelKind = ReadChannelKind(request);
        var participantPlayerId = ReadOptionalPlayerId(request, "ParticipantPlayerId");
        var keyword = OnlineAdminApiContract.ReadOptionalString(request, "Keyword");
        var startTime = OnlineAdminApiContract.ReadOptionalInt64(request, "StartTime");
        var endTime = OnlineAdminApiContract.ReadOptionalInt64(request, "EndTime");
        var pageSize = OnlineAdminApiContract.ReadPageSize(request);
        var cursor = OnlineAdminApiContract.ReadCursor(request);
        var page = await _host.ChatAudit.QueryAsync(scope.TenantId, scope.AppId, channelKind, participantPlayerId, keyword, startTime, endTime, cursor, pageSize).ConfigureAwait(false);
        var items = new List<ChatAuditMessageResponse>();
        foreach (var item in page.Items)
        {
            var participants = new List<string>();
            foreach (var participant in item.Participants)
            {
                participants.Add(participant.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            items.Add(new ChatAuditMessageResponse
            {
                MessageId = item.MessageId,
                ChannelKind = item.ChannelKind,
                ParticipantPlayerIds = participants,
                Content = item.Content,
                OccurredAt = item.SentAtTime,
            });
        }

        return new ChatAuditQueryResponse
        {
            Items = items,
            NextCursor = page.NextCursor,
            HasMore = page.HasMore,
        };
    }

    /// <summary>
    /// 构造处罚原因（Admin 处罚命令无线缆原因字段，以幂等键 + 处罚标识留痕）。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <returns>原因文本。</returns>
    private static string BuildPenaltyReason(OnlineAdminApiRequest request)
    {
        var penaltyId = request.ReadNullableInt64("PenaltyId");
        return "admin-penalty:" + (penaltyId.HasValue ? penaltyId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "unknown") +
               ":key=" + (request.ReadIdempotencyKey() ?? string.Empty);
    }

    /// <summary>
    /// 读取 Admin 举报状态（线缆数字 1～4 → Online 状态 0～3）。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <returns>Online 举报状态；缺失返回 null。</returns>
    private static OnlineReportState? ReadAdminStatus(OnlineAdminApiRequest request)
    {
        var value = request.ReadNullableInt32("Status");
        if (!value.HasValue)
        {
            return null;
        }

        switch (value.Value)
        {
            case 1:
                return OnlineReportState.Submitted;
            case 2:
                return OnlineReportState.Reviewing;
            case 3:
                return OnlineReportState.Actioned;
            case 4:
                return OnlineReportState.Rejected;
            default:
                return null;
        }
    }

    /// <summary>
    /// Online 举报状态 → Admin 状态码（Withdrawn 回退映射 Rejected）。
    /// </summary>
    /// <param name="state">Online 举报状态。</param>
    /// <returns>Admin 状态码。</returns>
    private static int ToAdminStatus(OnlineReportState state)
    {
        switch (state)
        {
            case OnlineReportState.Submitted:
                return 1;
            case OnlineReportState.Reviewing:
                return 2;
            case OnlineReportState.Actioned:
                return 3;
            default:
                return 4;
        }
    }

    /// <summary>
    /// 读取可选玩家标识（字符串 / 数字双形态）。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <param name="name">字段名。</param>
    /// <returns>玩家标识；缺失返回 null。</returns>
    private static long? ReadOptionalPlayerId(OnlineAdminApiRequest request, string name)
    {
        return request.ReadNullableInt64(name);
    }

    /// <summary>
    /// 读取频道类型过滤（线缆数字）。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <returns>频道类型；缺失返回 null。</returns>
    private static OnlineChatChannelKind? ReadChannelKind(OnlineAdminApiRequest request)
    {
        var value = request.ReadNullableInt32("ChannelKind");
        if (!value.HasValue)
        {
            return null;
        }

        return (OnlineChatChannelKind)value.Value;
    }

    /// <summary>
    /// 解析举报分页游标。
    /// </summary>
    /// <param name="cursor">游标原文。</param>
    /// <param name="createdAtTime">输出：创建时刻。</param>
    /// <param name="reportId">输出：举报标识。</param>
    /// <returns>解析成功返回 true。</returns>
    private static bool TryParseReportCursor(string cursor, out long createdAtTime, out string reportId)
    {
        createdAtTime = 0;
        reportId = null;
        var separatorIndex = cursor.LastIndexOf(':');
        if (separatorIndex <= 0 || separatorIndex >= cursor.Length - 1)
        {
            return false;
        }

        return long.TryParse(cursor.Substring(0, separatorIndex), out createdAtTime);
    }

    /// <summary>
    /// 处罚命令回执（对齐 Admin OnlineLinkCommandAckResponse）。
    /// </summary>
    public sealed class LinkCommandAckResponse
    {
        /// <summary>获取或设置令牌是否已确认吊销。</summary>
        public bool TokenRevokedConfirmed
        {
            get;
            set;
        }
    }

    /// <summary>
    /// 最小确认载荷（Admin 契约要求成功 Data 非 null）。
    /// </summary>
    public sealed class EmptyAckResponse
    {
        /// <summary>获取或设置确认标记。</summary>
        public bool Accepted
        {
            get;
            set;
        } = true;
    }

    /// <summary>
    /// 通知载荷。
    /// </summary>
    public sealed class NotificationPayload
    {
        /// <summary>获取或设置标题。</summary>
        public string Title
        {
            get;
            set;
        }

        /// <summary>获取或设置正文。</summary>
        public string Content
        {
            get;
            set;
        }
    }

    /// <summary>
    /// 举报案件（对齐 Admin OnlineReportCaseResponse）。
    /// </summary>
    public sealed class ReportCaseResponse
    {
        /// <summary>获取或设置案件标识。</summary>
        public string CaseId
        {
            get;
            set;
        }

        /// <summary>获取或设置 App 标识。</summary>
        public long AppId
        {
            get;
            set;
        }

        /// <summary>获取或设置区服标识。</summary>
        public long ServerId
        {
            get;
            set;
        }

        /// <summary>获取或设置举报人（字符串形态）。</summary>
        public string ReporterPlayerId
        {
            get;
            set;
        }

        /// <summary>获取或设置被举报人（字符串形态）。</summary>
        public string ReportedPlayerId
        {
            get;
            set;
        }

        /// <summary>获取或设置场景名。</summary>
        public string Scene
        {
            get;
            set;
        }

        /// <summary>获取或设置对局标识。</summary>
        public string MatchId
        {
            get;
            set;
        }

        /// <summary>获取或设置聊天消息标识。</summary>
        public string ChatMessageId
        {
            get;
            set;
        }

        /// <summary>获取或设置举报原因名。</summary>
        public string Reason
        {
            get;
            set;
        }

        /// <summary>获取或设置证据。</summary>
        public string Evidence
        {
            get;
            set;
        }

        /// <summary>获取或设置 Admin 状态码（1=Pending/2=Handling/3=Resolved/4=Rejected）。</summary>
        public int Status
        {
            get;
            set;
        }

        /// <summary>获取或设置处理结论名。</summary>
        public string HandleResult
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

        /// <summary>获取或设置更新时刻（UTC 毫秒）。</summary>
        public long UpdatedAt
        {
            get;
            set;
        }
    }

    /// <summary>
    /// 举报案件查询页（对齐 Admin OnlineReportCaseQueryResultResponse）。
    /// </summary>
    public sealed class ReportCaseQueryResponse
    {
        /// <summary>获取或设置案件列表。</summary>
        public List<ReportCaseResponse> Items
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
    /// 聊天审计消息（对齐 Admin OnlineMessageAuditMessageResponse）。
    /// </summary>
    public sealed class ChatAuditMessageResponse
    {
        /// <summary>获取或设置消息标识。</summary>
        public string MessageId
        {
            get;
            set;
        }

        /// <summary>获取或设置频道类型。</summary>
        public OnlineChatChannelKind ChannelKind
        {
            get;
            set;
        }

        /// <summary>获取或设置参与者（字符串形态）。</summary>
        public List<string> ParticipantPlayerIds
        {
            get;
            set;
        }

        /// <summary>获取或设置正文原文。</summary>
        public string Content
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
    /// 聊天审计查询页（对齐 Admin OnlineMessageAuditQueryResultResponse）。
    /// </summary>
    public sealed class ChatAuditQueryResponse
    {
        /// <summary>获取或设置消息列表。</summary>
        public List<ChatAuditMessageResponse> Items
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
}
