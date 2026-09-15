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

namespace GameFrameX.Online.Events;

using System.Text.Json;
using GameFrameX.Online.Social;

/// <summary>
/// 聊天域事件工厂（vault:C7 S6.5/S6.6/S6.7：消息发送、撤回、已读、审核失败供下游与 Admin 消费）。
/// <para>
/// 维护约束（脱敏，沿用 C93 <c>OnlineEventSanitizer</c> 要求）：事件载荷**绝不携带消息正文**——
/// 消息内容只在频道内按成员资格下发，事件会流向审计、Admin 与离线消费方，一旦带上正文
/// 就等于绕过了频道权限。消费端要正文请按 <c>ChannelId</c> + <c>MessageId</c> 走受权限保护的读取面。
/// </para>
/// <para>
/// 维护约束（信封字段）：频道是 App 级事实（全局频道跨区服、定向与队伍频道不按区服切分），
/// 信封 <c>ServerId</c> 固定置 <c>0</c>；<c>PlayerId</c> 是**发送者锚点**，
/// 频道内广播的投递目标是 <c>ChannelId</c> 对应的成员集合，消费端不得只看 PlayerId 投递。
/// </para>
/// </summary>
public static class OnlineChatEvents
{
    /// <summary>消息发送。</summary>
    public const string MessageSent = "Online.Chat.MessageSent";

    /// <summary>消息撤回。</summary>
    public const string MessageRecalled = "Online.Chat.MessageRecalled";

    /// <summary>已读位点更新。</summary>
    public const string ReadMarkUpdated = "Online.Chat.ReadMarkUpdated";

    /// <summary>内容审核插件执行失败（放行留痕）。</summary>
    public const string ModerationFailed = "Online.Chat.ModerationFailed";

    /// <summary>事件来源标识。</summary>
    public const string Source = "online-chat";

    /// <summary>
    /// 构造消息发送事件。
    /// </summary>
    /// <param name="message">已落库的消息快照。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <returns>事件信封实例。</returns>
    public static OnlineEvent CreateMessageSent(OnlineChatMessage message, string correlationId = null)
    {
        var payload = new MessageSentPayload
        {
            MessageId = message.MessageId ?? string.Empty,
            ChannelId = message.ChannelId ?? string.Empty,
            ChannelKind = message.ChannelKind.ToString(),
            SenderId = message.SenderId,
            SentAtTime = message.SentAtTime,
            Sequence = message.Sequence,
        };
        var auditFields = new Dictionary<string, string>
        {
            { "MessageId", payload.MessageId },
            { "ChannelId", payload.ChannelId },
            { "ChannelKind", payload.ChannelKind },
            { "SenderId", payload.SenderId.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            { "Sequence", payload.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture) },
        };
        return new OnlineEvent
        {
            EventId = "evt-" + Guid.NewGuid().ToString("N"),
            EventType = MessageSent,
            OccurredTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SchemaVersion = 1,
            TenantId = message.TenantId,
            AppId = message.AppId,
            ServerId = 0,
            PlayerId = message.SenderId,
            Source = Source,
            CorrelationId = correlationId ?? string.Empty,
            Payload = JsonSerializer.SerializeToUtf8Bytes(payload),
            PayloadAuditFields = auditFields,
        };
    }

    /// <summary>
    /// 构造消息撤回事件。
    /// </summary>
    /// <param name="message">撤回后的消息快照。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <returns>事件信封实例。</returns>
    public static OnlineEvent CreateMessageRecalled(OnlineChatMessage message, string correlationId = null)
    {
        var payload = new MessageRecalledPayload
        {
            MessageId = message.MessageId ?? string.Empty,
            ChannelId = message.ChannelId ?? string.Empty,
            SenderId = message.SenderId,
            RecalledByPlayerId = message.RecalledByPlayerId,
            RecalledAtTime = message.RecalledAtTime,
        };
        var auditFields = new Dictionary<string, string>
        {
            { "MessageId", payload.MessageId },
            { "ChannelId", payload.ChannelId },
            { "SenderId", payload.SenderId.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            { "RecalledByPlayerId", payload.RecalledByPlayerId.ToString(System.Globalization.CultureInfo.InvariantCulture) },
        };
        return new OnlineEvent
        {
            EventId = "evt-" + Guid.NewGuid().ToString("N"),
            EventType = MessageRecalled,
            OccurredTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SchemaVersion = 1,
            TenantId = message.TenantId,
            AppId = message.AppId,
            ServerId = 0,
            PlayerId = message.RecalledByPlayerId,
            Source = Source,
            CorrelationId = correlationId ?? string.Empty,
            Payload = JsonSerializer.SerializeToUtf8Bytes(payload),
            PayloadAuditFields = auditFields,
        };
    }

    /// <summary>
    /// 构造已读位点更新事件（消费端据此清红点）。
    /// </summary>
    /// <param name="readMark">更新后的已读位点。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <returns>事件信封实例。</returns>
    public static OnlineEvent CreateReadMarkUpdated(OnlineChatReadMark readMark, string correlationId = null)
    {
        var payload = new ReadMarkUpdatedPayload
        {
            ChannelId = readMark.ChannelId ?? string.Empty,
            PlayerId = readMark.PlayerId,
            LastReadMessageId = readMark.LastReadMessageId ?? string.Empty,
            LastReadSentAtTime = readMark.LastReadSentAtTime,
            LastReadSequence = readMark.LastReadSequence,
        };
        var auditFields = new Dictionary<string, string>
        {
            { "ChannelId", payload.ChannelId },
            { "PlayerId", payload.PlayerId.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            { "LastReadMessageId", payload.LastReadMessageId },
            { "LastReadSequence", payload.LastReadSequence.ToString(System.Globalization.CultureInfo.InvariantCulture) },
        };
        return new OnlineEvent
        {
            EventId = "evt-" + Guid.NewGuid().ToString("N"),
            EventType = ReadMarkUpdated,
            OccurredTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SchemaVersion = 1,
            TenantId = readMark.TenantId,
            AppId = readMark.AppId,
            ServerId = 0,
            PlayerId = readMark.PlayerId,
            Source = Source,
            CorrelationId = correlationId ?? string.Empty,
            Payload = JsonSerializer.SerializeToUtf8Bytes(payload),
            PayloadAuditFields = auditFields,
        };
    }

    /// <summary>
    /// 构造审核插件执行失败事件（放行留痕：这条事件标记出了「未经审核即放行」的时间窗）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="playerId">发送者。</param>
    /// <param name="channelId">频道标识。</param>
    /// <param name="reason">插件失败原因（异常消息；供排障）。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <returns>事件信封实例。</returns>
    public static OnlineEvent CreateModerationFailed(long tenantId, long appId, long playerId, string channelId, string reason, string correlationId = null)
    {
        var payload = new ModerationFailedPayload
        {
            ChannelId = channelId ?? string.Empty,
            PlayerId = playerId,
            Reason = reason ?? string.Empty,
        };
        var auditFields = new Dictionary<string, string>
        {
            { "ChannelId", payload.ChannelId },
            { "PlayerId", payload.PlayerId.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            { "Reason", payload.Reason },
        };
        return new OnlineEvent
        {
            EventId = "evt-" + Guid.NewGuid().ToString("N"),
            EventType = ModerationFailed,
            OccurredTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SchemaVersion = 1,
            TenantId = tenantId,
            AppId = appId,
            ServerId = 0,
            PlayerId = playerId,
            Source = Source,
            CorrelationId = correlationId ?? string.Empty,
            Payload = JsonSerializer.SerializeToUtf8Bytes(payload),
            PayloadAuditFields = auditFields,
        };
    }

    /// <summary>
    /// 消息发送事件负载（SchemaVersion=1；不含消息正文）。
    /// </summary>
    private sealed class MessageSentPayload
    {
        /// <summary>消息标识。</summary>
        public string MessageId
        {
            get;
            set;
        }

        /// <summary>频道标识。</summary>
        public string ChannelId
        {
            get;
            set;
        }

        /// <summary>频道类型名。</summary>
        public string ChannelKind
        {
            get;
            set;
        }

        /// <summary>发送者。</summary>
        public long SenderId
        {
            get;
            set;
        }

        /// <summary>发送时刻（UTC 毫秒）。</summary>
        public long SentAtTime
        {
            get;
            set;
        }

        /// <summary>频道内序号。</summary>
        public long Sequence
        {
            get;
            set;
        }
    }

    /// <summary>
    /// 消息撤回事件负载（SchemaVersion=1）。
    /// </summary>
    private sealed class MessageRecalledPayload
    {
        /// <summary>消息标识。</summary>
        public string MessageId
        {
            get;
            set;
        }

        /// <summary>频道标识。</summary>
        public string ChannelId
        {
            get;
            set;
        }

        /// <summary>原发送者。</summary>
        public long SenderId
        {
            get;
            set;
        }

        /// <summary>撤回人。</summary>
        public long RecalledByPlayerId
        {
            get;
            set;
        }

        /// <summary>撤回时刻（UTC 毫秒）。</summary>
        public long RecalledAtTime
        {
            get;
            set;
        }
    }

    /// <summary>
    /// 已读位点更新事件负载（SchemaVersion=1）。
    /// </summary>
    private sealed class ReadMarkUpdatedPayload
    {
        /// <summary>频道标识。</summary>
        public string ChannelId
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

        /// <summary>最后已读消息标识。</summary>
        public string LastReadMessageId
        {
            get;
            set;
        }

        /// <summary>最后已读位置的发送时刻（UTC 毫秒）。</summary>
        public long LastReadSentAtTime
        {
            get;
            set;
        }

        /// <summary>最后已读位置的频道内序号。</summary>
        public long LastReadSequence
        {
            get;
            set;
        }
    }

    /// <summary>
    /// 审核插件失败事件负载（SchemaVersion=1）。
    /// </summary>
    private sealed class ModerationFailedPayload
    {
        /// <summary>频道标识。</summary>
        public string ChannelId
        {
            get;
            set;
        }

        /// <summary>发送者。</summary>
        public long PlayerId
        {
            get;
            set;
        }

        /// <summary>插件失败原因。</summary>
        public string Reason
        {
            get;
            set;
        }
    }
}
