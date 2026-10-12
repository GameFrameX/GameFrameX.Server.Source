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

namespace GameFrameX.Capability.Online.Social;

using System.Text.Json;
using GameFrameX.Capability.Online.Events;

/// <summary>
/// 通知域事件工厂（通知状态变更事件供下游与 Admin 消费）。
/// <para>
/// 维护约束：事件是事实不是状态——消费端不得回写通知状态；载荷只放标识与状态名，
/// **不放通知正文**（<see cref="OnlineNotification.Payload"/> 可能含敏感信息，沿用既有脱敏要求），
/// 也不放去重键（去重键是通知域内部收敛依据，无对外语义）；
/// 通知状态变更事件是「离线期间的必要通知已补发」的对外证据链（补发顺序与过期丢弃依据）。
/// </para>
/// </summary>
public static class OnlineNotificationEvents
{
    /// <summary>通知状态变更。</summary>
    public const string NotificationChanged = "Online.Notification.Changed";

    /// <summary>事件来源标识。</summary>
    public const string Source = "online-notification";

    /// <summary>
    /// 构造通知状态变更事件。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="serverId">来源区服标识。</param>
    /// <param name="playerId">接收者玩家标识。</param>
    /// <param name="notificationId">通知标识（可空；载荷落空字符串）。</param>
    /// <param name="kind">通知来源分类名。</param>
    /// <param name="state">通知状态名。</param>
    /// <param name="attemptCount">累计推送尝试次数。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <returns>事件信封实例。</returns>
    public static OnlineEvent CreateNotificationChanged(long tenantId, long appId, long serverId, long playerId, string notificationId, string kind, string state, int attemptCount, string correlationId = null)
    {
        var payload = new NotificationChangedPayload
        {
            NotificationId = notificationId ?? string.Empty,
            PlayerId = playerId,
            Kind = kind,
            State = state,
            AttemptCount = attemptCount,
        };
        var auditFields = new Dictionary<string, string>
        {
            { "NotificationId", payload.NotificationId },
            { "PlayerId", playerId.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            { "Kind", kind },
            { "State", state },
            { "AttemptCount", attemptCount.ToString(System.Globalization.CultureInfo.InvariantCulture) },
        };
        return new OnlineEvent
        {
            EventId = "evt-" + Guid.NewGuid().ToString("N"),
            EventType = NotificationChanged,
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
    /// 通知状态变更事件负载（SchemaVersion=1）。
    /// </summary>
    private sealed class NotificationChangedPayload
    {
        /// <summary>通知标识。</summary>
        public string NotificationId { get; set; }

        /// <summary>接收者玩家标识（通知的唯一属主）。</summary>
        public long PlayerId { get; set; }

        /// <summary>通知来源分类名。</summary>
        public string Kind { get; set; }

        /// <summary>通知状态名。</summary>
        public string State { get; set; }

        /// <summary>累计推送尝试次数。</summary>
        public int AttemptCount { get; set; }
    }
}