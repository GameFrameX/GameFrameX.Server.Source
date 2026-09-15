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
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Online.Events;

using System.Text.Json;
using GameFrameX.Online.Presence;

/// <summary>
/// 在线状态域事件工厂（vault:C3 S2.5/S2.8：Presence 状态转换事件供下游与 Admin 消费）。
/// <para>
/// 维护约束：状态转换事件必须携带 from/to/reason 三元组（VC-2.5 状态机断言依据）；
/// Presence 只表达玩家在线事实——Admin 管理员连接不产生 Presence 记录，也不产生本类事件（X6/VC-2.6）；
/// 事件是事实不是状态，消费端不得回写 Presence。
/// </para>
/// </summary>
public static class OnlinePresenceEvents
{
    /// <summary>在线状态变更。</summary>
    public const string PresenceChanged = "Online.Presence.Changed";

    /// <summary>事件来源标识。</summary>
    public const string Source = "online-presence";

    /// <summary>
    /// 构造在线状态变更事件。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">应用标识。</param>
    /// <param name="serverId">区服标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="sessionId">关联会话标识。</param>
    /// <param name="fromState">原状态。</param>
    /// <param name="toState">目标状态。</param>
    /// <param name="reason">变更原因描述。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <returns>事件信封实例。</returns>
    public static OnlineEvent Create(long tenantId, long appId, long serverId, long playerId, string sessionId, OnlinePresenceState fromState, OnlinePresenceState toState, string reason, string correlationId = null)
    {
        var payload = new PresenceEventPayload
        {
            SessionId = sessionId ?? string.Empty,
            PlayerId = playerId,
            FromState = fromState.ToString(),
            ToState = toState.ToString(),
            Reason = reason ?? string.Empty,
        };
        var auditFields = new Dictionary<string, string>
        {
            { "SessionId", sessionId ?? string.Empty },
            { "PlayerId", playerId.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            { "FromState", fromState.ToString() },
            { "ToState", toState.ToString() },
            { "Reason", reason ?? string.Empty },
        };
        return new OnlineEvent
        {
            EventId = "evt-" + Guid.NewGuid().ToString("N"),
            EventType = PresenceChanged,
            OccurredTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SchemaVersion = 1,
            TenantId = tenantId,
            AppId = appId,
            ServerId = serverId,
            PlayerId = playerId,
            Source = Source,
            CorrelationId = correlationId ?? string.Empty,
            Payload = JsonSerializer.SerializeToUtf8Bytes(payload),
            PayloadAuditFields = auditFields,
        };
    }

    /// <summary>
    /// 在线状态事件负载（SchemaVersion=1）。
    /// </summary>
    private sealed class PresenceEventPayload
    {
        /// <summary>关联会话标识。</summary>
        public string SessionId
        {
            get;
            set;
        }

        /// <summary>玩家标识。</summary>
        public long PlayerId
        {
            get;
            set;
        }

        /// <summary>原状态名。</summary>
        public string FromState
        {
            get;
            set;
        }

        /// <summary>目标状态名。</summary>
        public string ToState
        {
            get;
            set;
        }

        /// <summary>变更原因描述。</summary>
        public string Reason
        {
            get;
            set;
        }
    }
}
