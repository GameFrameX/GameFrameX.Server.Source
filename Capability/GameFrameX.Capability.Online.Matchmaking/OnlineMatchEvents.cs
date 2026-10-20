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

namespace GameFrameX.Capability.Online.Matchmaking;

using System.Text.Json;
using GameFrameX.Capability.Online.Abstractions.Events;

/// <summary>
/// 匹配域事件工厂（票据状态与对局分配事件供下游与 Admin 消费）。
/// <para>
/// 维护约束：事件是事实不是状态——消费端不得回写票据；票据事件必须携带 from/to/reason 三元组，
/// 分配事件必须携带 AssignmentId/MatchId 与消费的票据集合
/// （「重复 assignment = 0」的对账依据）。载荷只放标识与状态名，不放自定义匹配属性
/// （可能含玩法私有数据），沿用既有脱敏要求。
/// </para>
/// </summary>
public static class OnlineMatchEvents
{
    /// <summary>票据状态变更。</summary>
    public const string TicketChanged = "Online.Match.TicketChanged";

    /// <summary>对局分配产生。</summary>
    public const string AssignmentCreated = "Online.Match.AssignmentCreated";

    /// <summary>事件来源标识。</summary>
    public const string Source = "online-matchmaker";

    /// <summary>
    /// 构造票据状态变更事件。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="serverId">发起区服标识。</param>
    /// <param name="ticketId">票据标识（可空；载荷落空字符串）。</param>
    /// <param name="partyId">来源队伍标识（可空；载荷落空字符串）。</param>
    /// <param name="failureReason">失败原因名。</param>
    /// <param name="assignmentId">产出的分配标识（可空；载荷落空字符串）。</param>
    /// <param name="playerCount">票据携带玩家数（空集合为 0）。</param>
    /// <param name="firstPlayerId">首位玩家标识（无成员为 0）。</param>
    /// <param name="fromState">原状态名。</param>
    /// <param name="toState">目标状态名。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <returns>事件信封实例。</returns>
    public static OnlineEvent CreateTicketChanged(long tenantId, long appId, long serverId, string ticketId, string partyId, string failureReason, string assignmentId, int playerCount, long firstPlayerId, string fromState, string toState, string correlationId = null)
    {
        var payload = new TicketChangedPayload
        {
            TicketId = ticketId ?? string.Empty,
            PartyId = partyId ?? string.Empty,
            FromState = fromState,
            ToState = toState,
            FailureReason = failureReason,
            AssignmentId = assignmentId ?? string.Empty,
            PlayerCount = playerCount,
        };
        var auditFields = new Dictionary<string, string>
        {
            { "TicketId", ticketId ?? string.Empty },
            { "PartyId", partyId ?? string.Empty },
            { "FromState", fromState },
            { "ToState", toState },
            { "FailureReason", failureReason },
            { "AssignmentId", assignmentId ?? string.Empty },
            { "PlayerCount", playerCount.ToString(System.Globalization.CultureInfo.InvariantCulture) },
        };
        return new OnlineEvent
        {
            EventId = "evt-" + Guid.NewGuid().ToString("N"),
            EventType = TicketChanged,
            OccurredTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SchemaVersion = 1,
            TenantId = tenantId,
            AppId = appId,
            ServerId = serverId,
            PlayerId = firstPlayerId,
            Source = Source,
            CorrelationId = correlationId ?? string.Empty,
            Payload = JsonSerializer.SerializeToUtf8Bytes(payload),
            PayloadAuditFields = auditFields,
        };
    }

    /// <summary>
    /// 构造对局分配事件。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="serverId">发起区服标识。</param>
    /// <param name="assignmentId">分配标识（可空；载荷落空字符串）。</param>
    /// <param name="matchId">对局标识（可空；载荷落空字符串）。</param>
    /// <param name="mode">玩法模式。</param>
    /// <param name="region">区域。</param>
    /// <param name="playerCount">参与玩家数（空集合为 0）。</param>
    /// <param name="firstPlayerId">首位玩家标识（无成员为 0）。</param>
    /// <param name="ticketIds">消费的票据集合（可空；载荷落空集合）。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <returns>事件信封实例。</returns>
    public static OnlineEvent CreateAssignmentCreated(long tenantId, long appId, long serverId, string assignmentId, string matchId, int mode, int region, int playerCount, long firstPlayerId, IReadOnlyList<string> ticketIds, string correlationId = null)
    {
        var ticketCount = ticketIds == null ? 0 : ticketIds.Count;
        var payload = new AssignmentCreatedPayload
        {
            AssignmentId = assignmentId ?? string.Empty,
            MatchId = matchId ?? string.Empty,
            Mode = mode,
            Region = region,
            PlayerCount = playerCount,
            TicketIds = ticketIds == null ? new List<string>() : new List<string>(ticketIds),
        };
        var auditFields = new Dictionary<string, string>
        {
            { "AssignmentId", assignmentId ?? string.Empty },
            { "MatchId", matchId ?? string.Empty },
            { "Mode", mode.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            { "Region", region.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            { "PlayerCount", playerCount.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            { "TicketCount", ticketCount.ToString(System.Globalization.CultureInfo.InvariantCulture) },
        };
        return new OnlineEvent
        {
            EventId = "evt-" + Guid.NewGuid().ToString("N"),
            EventType = AssignmentCreated,
            OccurredTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SchemaVersion = 1,
            TenantId = tenantId,
            AppId = appId,
            ServerId = serverId,
            PlayerId = firstPlayerId,
            Source = Source,
            CorrelationId = correlationId ?? string.Empty,
            Payload = JsonSerializer.SerializeToUtf8Bytes(payload),
            PayloadAuditFields = auditFields,
        };
    }

    /// <summary>
    /// 只读解析对局分配事件负载（C246 通知推送方消费；类型不符或载荷不可解析返回 null，不抛异常）。
    /// </summary>
    /// <param name="onlineEvent">事件。</param>
    /// <returns>分配负载视图；非分配事件或解析失败返回 null。</returns>
    public static AssignmentCreatedPayload ReadAssignmentCreated(OnlineEvent onlineEvent)
    {
        if (onlineEvent == null || onlineEvent.EventType != AssignmentCreated || onlineEvent.Payload.IsEmpty)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<AssignmentCreatedPayload>(onlineEvent.Payload.Span);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 票据状态变更事件负载（SchemaVersion=1）。
    /// </summary>
    private sealed class TicketChangedPayload
    {
        /// <summary>票据标识。</summary>
        public string TicketId { get; set; }

        /// <summary>来源队伍标识（单人排队为空字符串）。</summary>
        public string PartyId { get; set; }

        /// <summary>原状态名。</summary>
        public string FromState { get; set; }

        /// <summary>目标状态名。</summary>
        public string ToState { get; set; }

        /// <summary>失败原因名。</summary>
        public string FailureReason { get; set; }

        /// <summary>产出的分配标识（未匹配为空字符串）。</summary>
        public string AssignmentId { get; set; }

        /// <summary>票据携带玩家数。</summary>
        public int PlayerCount { get; set; }
    }

    /// <summary>
    /// 对局分配事件负载（SchemaVersion=1；C246 起公共化供订阅方只读解析）。
    /// </summary>
    public sealed class AssignmentCreatedPayload
    {
        /// <summary>分配标识。</summary>
        public string AssignmentId { get; set; }

        /// <summary>对局标识。</summary>
        public string MatchId { get; set; }

        /// <summary>玩法模式。</summary>
        public int Mode { get; set; }

        /// <summary>区域。</summary>
        public int Region { get; set; }

        /// <summary>参与玩家数。</summary>
        public int PlayerCount { get; set; }

        /// <summary>消费的票据集合。</summary>
        public List<string> TicketIds { get; set; }
    }
}