// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Online.Assets;
using GameFrameX.Online.Events;
using GameFrameX.Online.Match;
using GameFrameX.Online.Matchmaking;
using GameFrameX.Online.Session;
using GameFrameX.Online.Social;

namespace GameFrameX.Online.GameEvents;

/// <summary>
/// 六类游戏事件投影器（vault:C8 S7.6）：把权威领域对象投影为标准游戏事件（C93 信封）。
/// <para>
/// 维护约束（红线）：
/// (1) **入参是权威领域对象，不是兄弟事件的 JSON 载荷**——投影只读领域类型的强类型字段，
/// 绝不反序列化其它事件类型已发布的载荷：载荷格式的上游演进会让解析在本层静默失败，
/// 而强类型字段的变更在编译期即暴露（P1-5）；
/// (2) **投影是一次状态迁移一次调用**——调用方在其状态迁移点触发（运行时装配 X4 负责时序）；
/// 投影器不自行去重、不做时序判定，去重由 <see cref="OnlineGameEventIngestor"/> 按 EventId 兜底；
/// (3) **载荷 = 登记表声明的字段的扁平字符串投影**，同一份字段同时充当 <c>Payload</c> 与
/// <c>PayloadAuditFields</c>：因为游戏事件的字段集由登记表数据驱动（17 个事件名各有各的必需字段），
/// 逐事件名建强类型负载会得到 17 个几乎空壳的类，而 L0 校验只需读这一份投影——两处各写一份必然漂移；
/// (4) **只为已发生的事实投影**：未成功的资产交易、未开始的对局、非聊天场景的举报都不投影
/// （事件名是冻结的跨仓契约，把语义不符的事实塞进既有事件名会污染下游口径）。
/// </para>
/// </summary>
public sealed class OnlineGameEventProjector
{
    /// <summary>摄取器（投影结果统一经此校验落档；投影不绕过 L0）。</summary>
    private readonly OnlineGameEventIngestor _ingestor;

    /// <summary>
    /// 初始化 <see cref="OnlineGameEventProjector"/>。
    /// </summary>
    /// <param name="ingestor">事件摄取器（投影结果经其校验落档）。</param>
    public OnlineGameEventProjector(OnlineGameEventIngestor ingestor)
    {
        _ingestor = ingestor ?? throw new ArgumentNullException(nameof(ingestor));
    }

    /// <summary>
    /// 投影会话事件（登录 / 会话开始 / 会话结束三选一，由会话状态唯一确定）。
    /// <para>
    /// 状态映射：<see cref="OnlineSessionState.Authenticated"/> → <c>Login</c>（凭据校验通过）；
    /// <see cref="OnlineSessionState.Connected"/> / <see cref="OnlineSessionState.Active"/> /
    /// <see cref="OnlineSessionState.Reconnecting"/> → <c>SessionStart</c>（进入可游戏状态）；
    /// <see cref="OnlineSessionState.Closed"/> / <see cref="OnlineSessionState.Kicked"/> /
    /// <see cref="OnlineSessionState.Expired"/> → <c>SessionEnd</c>（结束原因取状态名）；
    /// <see cref="OnlineSessionState.Created"/> → 不投影（尚未发生可陈述的事实）。
    /// </para>
    /// </summary>
    /// <param name="session">会话领域对象。</param>
    /// <returns>投影出的事件列表（0 或 1 个）。</returns>
    public List<OnlineEvent> ProjectSessionEvents(OnlineSession session)
    {
        if (session == null)
        {
            throw new ArgumentNullException(nameof(session));
        }

        var events = new List<OnlineEvent>();
        var fields = new Dictionary<string, string>
        {
            { "SessionId", session.Id ?? string.Empty },
            { "PlayerId", FormatId(session.PlayerId) },
            { "DeviceId", session.DeviceId ?? string.Empty },
            { "State", session.State.ToString() },
        };

        switch (session.State)
        {
            case OnlineSessionState.Authenticated:
                events.Add(Build(OnlineGameEventName.Login, session.TenantId, session.AppId, session.ServerId, session.PlayerId, Fallback(session.AuthenticatedAtTime, session.CreatedAtTime), session.Id, fields));
                break;
            case OnlineSessionState.Connected:
            case OnlineSessionState.Active:
            case OnlineSessionState.Reconnecting:
                events.Add(Build(OnlineGameEventName.SessionStart, session.TenantId, session.AppId, session.ServerId, session.PlayerId, Fallback(session.ConnectedAtTime, session.AuthenticatedAtTime), session.Id, fields));
                break;
            case OnlineSessionState.Closed:
            case OnlineSessionState.Kicked:
            case OnlineSessionState.Expired:
                fields["EndReason"] = session.State.ToString();
                events.Add(Build(OnlineGameEventName.SessionEnd, session.TenantId, session.AppId, session.ServerId, session.PlayerId, Fallback(session.DisconnectAtTime, session.CreatedAtTime), session.Id, fields));
                break;
        }

        return events;
    }

    /// <summary>
    /// 投影进入匹配队列事件（<c>MatchQueue</c>）。
    /// </summary>
    /// <param name="ticket">匹配票据领域对象。</param>
    /// <returns>投影出的事件列表（0 或 1 个）。</returns>
    public List<OnlineEvent> ProjectMatchQueueEvents(OnlineMatchTicket ticket)
    {
        if (ticket == null)
        {
            throw new ArgumentNullException(nameof(ticket));
        }

        var fields = new Dictionary<string, string>
        {
            { "TicketId", ticket.TicketId ?? string.Empty },
            { "Mode", FormatId(ticket.Mode) },
            { "Region", FormatId(ticket.Region) },
            { "PlayerCount", FormatId(ticket.PlayerIds == null ? 0 : ticket.PlayerIds.Count) },
            { "State", ticket.State.ToString() },
        };

        var events = new List<OnlineEvent>
        {
            Build(OnlineGameEventName.MatchQueue, ticket.TenantId, ticket.AppId, ticket.ServerId, 0, ticket.CreatedAtTime, ticket.TicketId, fields),
        };
        return events;
    }

    /// <summary>
    /// 投影对局开始事件（<c>MatchStart</c>；仅当对局**确已开始**——<see cref="OnlineMatchState.Running"/> 及其之后
    /// 的正常终态；未开始即取消 / 失败的对局不投影）。
    /// </summary>
    /// <param name="match">对局领域对象。</param>
    /// <returns>投影出的事件列表（0 或 1 个）。</returns>
    public List<OnlineEvent> ProjectMatchEvents(OnlineMatch match)
    {
        if (match == null)
        {
            throw new ArgumentNullException(nameof(match));
        }

        var events = new List<OnlineEvent>();
        if (!HasStarted(match.State))
        {
            return events;
        }

        var fields = new Dictionary<string, string>
        {
            { "MatchId", match.MatchId ?? string.Empty },
            { "Mode", FormatId(match.Mode) },
            { "Region", FormatId(match.Region) },
            { "State", match.State.ToString() },
        };

        events.Add(Build(OnlineGameEventName.MatchStart, match.TenantId, match.AppId, match.ServerId, 0, Fallback(match.StateChangedTime, match.CreatedTime), match.MatchId, fields));
        return events;
    }

    /// <summary>
    /// 投影对局结果事件（<c>MatchEnd</c> 一例 + 逐玩家 <c>Win</c> / <c>Lose</c> / <c>Abandon</c>）。
    /// <para>
    /// 逐玩家归属规则：结果 <c>Outcome</c> 为 <see cref="OnlineMatchState.Aborted"/> 时全员记 <c>Abandon</c>
    /// （对局中断，无胜负归属）；其余终态按 <see cref="OnlineMatchResultEntry.IsWinner"/> 记 <c>Win</c> / <c>Lose</c>。
    /// </para>
    /// </summary>
    /// <param name="result">对局结果领域对象（权威结算事实）。</param>
    /// <returns>投影出的事件列表（恒含 1 个 <c>MatchEnd</c>，另按条目追加玩家维度事件）。</returns>
    public List<OnlineEvent> ProjectMatchResultEvents(OnlineMatchResult result)
    {
        if (result == null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        var occurredTime = Fallback(result.SettledTime, 0);
        var matchFields = new Dictionary<string, string>
        {
            { "MatchId", result.MatchId ?? string.Empty },
            { "MatchResultId", result.MatchResultId ?? string.Empty },
            { "Outcome", result.Outcome.ToString() },
            { "EntryCount", FormatId(result.Entries == null ? 0 : result.Entries.Count) },
        };

        var events = new List<OnlineEvent>
        {
            Build(OnlineGameEventName.MatchEnd, result.TenantId, result.AppId, result.ServerId, 0, occurredTime, result.MatchResultId, matchFields),
        };

        if (result.Entries == null)
        {
            return events;
        }

        var aborted = result.Outcome == OnlineMatchState.Aborted;
        foreach (var entry in result.Entries)
        {
            if (entry == null)
            {
                continue;
            }

            var name = aborted ? OnlineGameEventName.Abandon : (entry.IsWinner ? OnlineGameEventName.Win : OnlineGameEventName.Lose);
            var playerFields = new Dictionary<string, string>
            {
                { "MatchId", result.MatchId ?? string.Empty },
                { "MatchResultId", result.MatchResultId ?? string.Empty },
                { "PlayerId", FormatId(entry.PlayerId) },
                { "Rank", FormatId(entry.Rank) },
                { "Score", FormatId(entry.Score) },
            };

            events.Add(Build(name, result.TenantId, result.AppId, result.ServerId, entry.PlayerId, occurredTime, result.MatchResultId, playerFields));
        }

        return events;
    }

    /// <summary>
    /// 投影资产交易事件（付费 / 奖励发放 / 货币消耗三选一）。
    /// <para>
    /// 归属规则：来源为 <see cref="OnlineAssetChangeSource.PaymentConfirm"/> → <c>Purchase</c>
    /// （付费确认是唯一「用户付钱」来源，必须与运营发放区分）；否则操作为
    /// <see cref="OnlineGrantOperation.Deduct"/> / <see cref="OnlineGrantOperation.Revoke"/> → <c>CurrencySpend</c>，
    /// 其余操作（Grant / Reissue / Adjust）→ <c>RewardGrant</c>。
    /// **仅投影已成功（<see cref="OnlineAssetTransactionState.Succeeded"/>）的交易**——执行中与失败交易尚未产生资产事实。
    /// </para>
    /// </summary>
    /// <param name="transaction">资产交易领域对象。</param>
    /// <returns>投影出的事件列表（0 或 1 个）。</returns>
    public List<OnlineEvent> ProjectAssetTransactionEvents(OnlineAssetTransaction transaction)
    {
        if (transaction == null)
        {
            throw new ArgumentNullException(nameof(transaction));
        }

        var events = new List<OnlineEvent>();
        if (transaction.State != OnlineAssetTransactionState.Succeeded)
        {
            return events;
        }

        var isPayment = transaction.Source == OnlineAssetChangeSource.PaymentConfirm;
        string name;
        if (isPayment)
        {
            name = OnlineGameEventName.Purchase;
        }
        else if (transaction.Operation == OnlineGrantOperation.Deduct || transaction.Operation == OnlineGrantOperation.Revoke)
        {
            name = OnlineGameEventName.CurrencySpend;
        }
        else
        {
            name = OnlineGameEventName.RewardGrant;
        }

        var fields = new Dictionary<string, string>
        {
            { "TransactionId", transaction.TransactionId ?? string.Empty },
            { "PlayerId", FormatId(transaction.PlayerId) },
            { "ChangeSource", transaction.Source.ToString() },
            { "Operation", transaction.Operation.ToString() },
            { "BusinessOrderId", transaction.BusinessOrderId ?? string.Empty },
            { "ChangeCount", FormatId(transaction.ExpectedChangeCount) },
        };

        events.Add(Build(name, transaction.TenantId, transaction.AppId, transaction.HomeServerId, transaction.PlayerId, Fallback(transaction.SettledTime, transaction.CreatedTime), transaction.TransactionId, fields));
        return events;
    }

    /// <summary>
    /// 投影聊天举报受理事件（<c>ChatReport</c>；**仅聊天场景**——事件名是冻结的跨仓契约，
    /// 其它场景（组队 / 资料 / 好友等）没有对应事件名，不投影也不冒用本名）。
    /// </summary>
    /// <param name="reportCase">举报工单领域对象。</param>
    /// <returns>投影出的事件列表（0 或 1 个）。</returns>
    public List<OnlineEvent> ProjectReportCaseEvents(OnlineReportCase reportCase)
    {
        if (reportCase == null)
        {
            throw new ArgumentNullException(nameof(reportCase));
        }

        var events = new List<OnlineEvent>();
        if (reportCase.Scene != OnlineReportScene.Chat)
        {
            return events;
        }

        var fields = new Dictionary<string, string>
        {
            { "ReportId", reportCase.ReportId ?? string.Empty },
            { "ReporterId", FormatId(reportCase.ReporterId) },
            { "ReportedPlayerId", FormatId(reportCase.ReportedPlayerId) },
            { "Scene", reportCase.Scene.ToString() },
            { "Reason", reportCase.Reason.ToString() },
            { "State", reportCase.State.ToString() },
            { "MatchId", reportCase.MatchId ?? string.Empty },
            { "ChatMessageId", reportCase.ChatMessageId ?? string.Empty },
        };

        events.Add(Build(OnlineGameEventName.ChatReport, reportCase.TenantId, reportCase.AppId, 0, reportCase.ReporterId, Fallback(reportCase.CreatedAtTime, reportCase.UpdatedAtTime), reportCase.ReportId, fields));
        return events;
    }

    /// <summary>
    /// 投影处罚生效事件（<c>Penalty</c>；撤销的处罚单独发撤销事件不在本 change 范围，故**仅投影未撤销的处罚**）。
    /// </summary>
    /// <param name="punishment">处罚领域对象。</param>
    /// <returns>投影出的事件列表（0 或 1 个）。</returns>
    public List<OnlineEvent> ProjectPunishmentEvents(OnlinePunishment punishment)
    {
        if (punishment == null)
        {
            throw new ArgumentNullException(nameof(punishment));
        }

        var events = new List<OnlineEvent>();
        if (punishment.Revoked)
        {
            return events;
        }

        var fields = new Dictionary<string, string>
        {
            { "PunishmentId", punishment.PunishmentId ?? string.Empty },
            { "PlayerId", FormatId(punishment.PlayerId) },
            { "Kind", punishment.Kind.ToString() },
            { "Reason", punishment.Reason ?? string.Empty },
            { "EffectiveAtTime", FormatId(punishment.EffectiveAtTime) },
            { "ExpiresAtTime", FormatId(punishment.ExpiresAtTime) },
        };

        events.Add(Build(OnlineGameEventName.Penalty, punishment.TenantId, punishment.AppId, 0, punishment.PlayerId, Fallback(punishment.EffectiveAtTime, punishment.CreatedAtTime), punishment.PunishmentId, fields));
        return events;
    }

    /// <summary>
    /// 把投影出的事件送入摄取器（校验 → 去重 → 存储 / 死信），逐事件返回回执。
    /// </summary>
    /// <param name="events">投影出的事件列表。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒；传 0 取系统时钟，测试可注入）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>逐事件摄取回执（与入参同序）。</returns>
    public async Task<List<OnlineGameEventIngestOutcome>> ProjectAsync(IReadOnlyList<OnlineEvent> events, long nowUnixMilliseconds = 0, CancellationToken cancellationToken = default)
    {
        if (events == null)
        {
            throw new ArgumentNullException(nameof(events));
        }

        var outcomes = new List<OnlineGameEventIngestOutcome>();
        foreach (var onlineEvent in events)
        {
            outcomes.Add(await _ingestor.IngestAsync(onlineEvent, nowUnixMilliseconds, cancellationToken).ConfigureAwait(false));
        }

        return outcomes;
    }

    /// <summary>
    /// 构造标准游戏事件信封（载荷与载荷审计字段取同一份字段投影）。
    /// </summary>
    /// <param name="eventName">登记表内的事件名。</param>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="serverId">区服标识（无归属服传 0）。</param>
    /// <param name="playerId">玩家标识（聚合事件传 0）。</param>
    /// <param name="occurredTime">事件发生时刻（UTC 毫秒；小于等于 0 时取当前时刻）。</param>
    /// <param name="correlationId">关联领域标识。</param>
    /// <param name="fields">载荷字段投影（同时充当 <c>PayloadAuditFields</c>）。</param>
    /// <returns>事件信封实例。</returns>
    private static OnlineEvent Build(string eventName, long tenantId, long appId, long serverId, long playerId, long occurredTime, string correlationId, Dictionary<string, string> fields)
    {
        return new OnlineEvent
        {
            EventId = "evt-" + Guid.NewGuid().ToString("N"),
            EventType = eventName,
            OccurredTime = occurredTime > 0 ? occurredTime : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SchemaVersion = 1,
            TenantId = tenantId,
            AppId = appId,
            ServerId = serverId,
            PlayerId = playerId,
            Source = OnlineGameEventSchema.Source,
            CorrelationId = correlationId ?? string.Empty,
            Payload = JsonSerializer.SerializeToUtf8Bytes(fields),
            PayloadAuditFields = fields,
        };
    }

    /// <summary>
    /// 判定对局是否「确已开始」（<see cref="OnlineMatchState.Running"/> 及其之后的正常终态）。
    /// </summary>
    /// <param name="state">对局状态。</param>
    /// <returns>已开始返回 <c>true</c>。</returns>
    private static bool HasStarted(OnlineMatchState state)
    {
        return state == OnlineMatchState.Running
               || state == OnlineMatchState.Settling
               || state == OnlineMatchState.Completed
               || state == OnlineMatchState.Closed;
    }

    /// <summary>
    /// 取首个正数的时刻（领域时间戳缺省为 0——按语义优先级回退，避免事件落到 0 时刻而无法进入任何时间窗）。
    /// </summary>
    /// <param name="preferred">首选时刻。</param>
    /// <param name="fallback">回退时刻。</param>
    /// <returns>可用时刻（两者皆非正时返回 0，由 <see cref="Build"/> 取当前时刻）。</returns>
    private static long Fallback(long preferred, long fallback)
    {
        return preferred > 0 ? preferred : fallback;
    }

    /// <summary>
    /// 把标识与数值格式化为载荷字段的规范字符串形式（不变文化，跨区域结果一致）。
    /// </summary>
    /// <param name="value">数值。</param>
    /// <returns>不变文化字符串。</returns>
    private static string FormatId(long value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }
}
