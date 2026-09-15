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

using GameFrameX.Online.Audit;
using GameFrameX.Online.Contracts;
using GameFrameX.Online.Scope;
using GameFrameX.Online.Timeline;

namespace GameFrameX.Online.Runtime.AdminApi;

/// <summary>
/// 运维台查询域 admin action（query_online_overview / query_player_timeline / query_online_audit_events）。
/// <para>
/// 维护约束：三个 action 均为只读查询，无幂等包裹；总览与时间线直接映射既有服务面；
/// 审计事件查询以统一审计面为单一事实源（Admin 的 CommandKind → 事件类型过滤，TargetPlayerId → 玩家位过滤）。
/// </para>
/// </summary>
public sealed class OnlineAdminConsoleHandlers
{
    /// <summary>
    /// 宿主。
    /// </summary>
    private readonly OnlineRuntimeHost _host;

    /// <summary>
    /// 初始化 <see cref="OnlineAdminConsoleHandlers"/>。
    /// </summary>
    /// <param name="host">宿主。</param>
    public OnlineAdminConsoleHandlers(OnlineRuntimeHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    /// <summary>
    /// 注册运维台查询域 action。
    /// </summary>
    /// <param name="dispatcher">调度器。</param>
    public void Register(OnlineAdminApiDispatcher dispatcher)
    {
        dispatcher.Register("query_online_overview", QueryOnlineOverviewAsync, false);
        dispatcher.Register("query_player_timeline", QueryPlayerTimelineAsync, false);
        dispatcher.Register("query_online_audit_events", QueryOnlineAuditEventsAsync, false);
    }

    /// <summary>
    /// query_online_overview：在线总览（在线人数 / 会话数 / 对局数 / 重连率 / 队列摘要）。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <param name="scope">作用域。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>在线总览。</returns>
    public async Task<object> QueryOnlineOverviewAsync(OnlineAdminApiRequest request, OnlineScope scope, CancellationToken cancellationToken)
    {
        var snapshot = OnlineAdminApiContract.Unwrap(await _host.Overview.ReviewAsync(scope, 0, cancellationToken).ConfigureAwait(false));
        var queues = new List<OverviewQueueResponse>();
        if (snapshot.QueueSummaries != null)
        {
            foreach (var queue in snapshot.QueueSummaries)
            {
                queues.Add(new OverviewQueueResponse
                {
                    Mode = queue.Mode.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Region = queue.Region.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    QueueDepth = queue.QueueDepth,
                    AverageWaitSeconds = queue.AverageWaitSeconds,
                    ThroughputPerMinute = queue.ThroughputPerMinute,
                });
            }
        }

        return new OnlineOverviewResponse
        {
            OnlinePlayerCount = snapshot.OnlinePlayerCount,
            SessionCount = snapshot.SessionCount,
            MatchCount = snapshot.MatchCount,
            ReconnectRate = snapshot.ReconnectRate,
            QueueSummaries = queues,
            ServerTime = snapshot.ObservedTime > 0 ? snapshot.ObservedTime : OnlineAdminApiContract.NowSeconds() * 1000,
        };
    }

    /// <summary>
    /// query_player_timeline：玩家时间线（事件组 / 时间过滤 + 游标分页）。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <param name="scope">作用域。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>时间线页。</returns>
    public async Task<object> QueryPlayerTimelineAsync(OnlineAdminApiRequest request, OnlineScope scope, CancellationToken cancellationToken)
    {
        var playerScope = OnlineAdminApiContract.ReadPlayerScope(request, scope);
        var query = new OnlinePlayerTimelineQuery
        {
            Group = OnlineAdminApiContract.ReadOptionalString(request, "EventGroup"),
            StartTime = OnlineAdminApiContract.ReadOptionalInt64(request, "StartTime"),
            EndTime = OnlineAdminApiContract.ReadOptionalInt64(request, "EndTime"),
            Cursor = OnlineAdminApiContract.ReadCursor(request),
            PageSize = OnlineAdminApiContract.ReadPageSize(request),
        };
        var page = OnlineAdminApiContract.Unwrap(await _host.Timeline.QueryAsync(playerScope, query, cancellationToken).ConfigureAwait(false));
        var events = new List<TimelineEventResponse>();
        if (page.Entries != null)
        {
            foreach (var entry in page.Entries)
            {
                events.Add(new TimelineEventResponse
                {
                    EventId = entry.EventId,
                    EventType = entry.EventType,
                    Group = entry.Group,
                    OccurredAt = entry.OccurredAt,
                    Source = entry.Source ?? string.Empty,
                    CorrelationId = entry.CorrelationId ?? string.Empty,
                    PayloadSummary = entry.PayloadSummary ?? string.Empty,
                });
            }
        }

        return new TimelineQueryResponse
        {
            Events = events,
            NextCursor = page.Page != null && page.Page.HasMore ? page.Page.Cursor : null,
            HasMore = page.Page != null && page.Page.HasMore,
        };
    }

    /// <summary>
    /// query_online_audit_events：运维审计事件查询（统一审计面；CommandKind → 事件类型，TargetPlayerId → 玩家位）。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <param name="scope">作用域。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>审计事件页。</returns>
    public async Task<object> QueryOnlineAuditEventsAsync(OnlineAdminApiRequest request, OnlineScope scope, CancellationToken cancellationToken)
    {
        var targetPlayerId = request.ReadNullableInt64("TargetPlayerId");
        var query = new OnlineAuditQuery
        {
            OperatorId = OnlineAdminApiContract.ReadOptionalString(request, "OperatorId"),
            PlayerId = targetPlayerId,
            EventType = OnlineAdminApiContract.ReadOptionalString(request, "CommandKind"),
            StartTime = OnlineAdminApiContract.ReadOptionalInt64(request, "StartTime"),
            EndTime = OnlineAdminApiContract.ReadOptionalInt64(request, "EndTime"),
            Cursor = OnlineAdminApiContract.ReadCursor(request),
            PageSize = OnlineAdminApiContract.ReadPageSize(request),
        };
        var page = OnlineAdminApiContract.Unwrap(await _host.Audit.QueryAsync(scope, query, cancellationToken).ConfigureAwait(false));
        var items = new List<AuditEventResponse>();
        if (page.Records != null)
        {
            foreach (var record in page.Records)
            {
                items.Add(new AuditEventResponse
                {
                    EventId = record.EventId,
                    CommandType = record.EventType ?? string.Empty,
                    OperatorId = record.OperatorId ?? string.Empty,
                    TargetPlayerId = record.PlayerId > 0 ? record.PlayerId.ToString(System.Globalization.CultureInfo.InvariantCulture) : null,
                    OccurredAt = record.OccurredTime,
                    Summary = record.Reason ?? string.Empty,
                    CorrelationId = record.CorrelationId,
                });
            }
        }

        return new AuditEventQueryResponse
        {
            Items = items,
            NextCursor = page.Cursor != null && page.Cursor.HasMore ? page.Cursor.Cursor : null,
            HasMore = page.Cursor != null && page.Cursor.HasMore,
        };
    }

    /// <summary>
    /// 总览队列摘要（对齐 Admin OnlineOverviewQueueSummaryResponse）。
    /// </summary>
    public sealed class OverviewQueueResponse
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

        /// <summary>获取或设置每分钟吞吐。</summary>
        public long ThroughputPerMinute
        {
            get;
            set;
        }
    }

    /// <summary>
    /// 在线总览（对齐 Admin OnlineOverviewResponse）。
    /// </summary>
    public sealed class OnlineOverviewResponse
    {
        /// <summary>获取或设置在线玩家数。</summary>
        public long OnlinePlayerCount
        {
            get;
            set;
        }

        /// <summary>获取或设置会话数。</summary>
        public long SessionCount
        {
            get;
            set;
        }

        /// <summary>获取或设置对局数。</summary>
        public long MatchCount
        {
            get;
            set;
        }

        /// <summary>获取或设置重连率（0～1）。</summary>
        public double ReconnectRate
        {
            get;
            set;
        }

        /// <summary>获取或设置队列摘要列表。</summary>
        public List<OverviewQueueResponse> QueueSummaries
        {
            get;
            set;
        }

        /// <summary>获取或设置服务端时刻（UTC 毫秒）。</summary>
        public long ServerTime
        {
            get;
            set;
        }
    }

    /// <summary>
    /// 时间线事件（对齐 Admin OnlineTimelineEventResponse）。
    /// </summary>
    public sealed class TimelineEventResponse
    {
        /// <summary>获取或设置事件标识。</summary>
        public string EventId
        {
            get;
            set;
        }

        /// <summary>获取或设置事件类型。</summary>
        public string EventType
        {
            get;
            set;
        }

        /// <summary>获取或设置事件组。</summary>
        public string Group
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

        /// <summary>获取或设置来源。</summary>
        public string Source
        {
            get;
            set;
        }

        /// <summary>获取或设置关联标识。</summary>
        public string CorrelationId
        {
            get;
            set;
        }

        /// <summary>获取或设置载荷摘要。</summary>
        public string PayloadSummary
        {
            get;
            set;
        }
    }

    /// <summary>
    /// 时间线查询页（对齐 Admin OnlineTimelineQueryResponse）。
    /// </summary>
    public sealed class TimelineQueryResponse
    {
        /// <summary>获取或设置事件列表。</summary>
        public List<TimelineEventResponse> Events
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
    /// 审计事件条目（对齐 Admin OnlineAuditEventResponse）。
    /// </summary>
    public sealed class AuditEventResponse
    {
        /// <summary>获取或设置事件标识。</summary>
        public string EventId
        {
            get;
            set;
        }

        /// <summary>获取或设置命令类型（事件类型名）。</summary>
        public string CommandType
        {
            get;
            set;
        }

        /// <summary>获取或设置操作者。</summary>
        public string OperatorId
        {
            get;
            set;
        }

        /// <summary>获取或设置目标玩家（字符串形态；无目标为 null）。</summary>
        public string TargetPlayerId
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

        /// <summary>获取或设置摘要。</summary>
        public string Summary
        {
            get;
            set;
        }

        /// <summary>获取或设置关联标识。</summary>
        public string CorrelationId
        {
            get;
            set;
        }
    }

    /// <summary>
    /// 审计事件查询页（对齐 Admin OnlineAuditEventQueryResponse）。
    /// </summary>
    public sealed class AuditEventQueryResponse
    {
        /// <summary>获取或设置事件列表。</summary>
        public List<AuditEventResponse> Items
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
