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
/// 社交域事件工厂（vault:C7 S6.3/S6.4：好友、屏蔽、静音、举报、处罚、群组变更供下游与 Admin 消费）。
/// <para>
/// 维护约束：事件是事实不是状态——消费端不得回写社交状态；每个负载都携带 <c>Action</c> 区分
/// 「同一条记录的不同变更」（好友记录的建立与删除共用一条记录，只靠状态字段消费端无法区分「刚接受」与「刚删除」）。
/// 载荷只放标识与状态名，不放举报补充说明、屏蔽原因等自由文本（沿用 C93 <c>OnlineEventSanitizer</c> 脱敏要求）。
/// </para>
/// <para>
/// 维护约束（信封字段）：好友与屏蔽/静音记录携带发起时所在区服，信封 <c>ServerId</c> 取记录值；
/// 群组、举报、处罚是 **App 级事实**（不按区服切分），信封 <c>ServerId</c> 置 <c>0</c>，
/// 消费端不得据此判定「无区服」为异常。
/// </para>
/// </summary>
public static class OnlineSocialEvents
{
    /// <summary>好友关系变更。</summary>
    public const string FriendshipChanged = "Online.Social.FriendshipChanged";

    /// <summary>屏蔽变更。</summary>
    public const string BlockChanged = "Online.Social.BlockChanged";

    /// <summary>静音变更。</summary>
    public const string MuteChanged = "Online.Social.MuteChanged";

    /// <summary>举报案件变更。</summary>
    public const string ReportChanged = "Online.Social.ReportChanged";

    /// <summary>处罚变更。</summary>
    public const string PunishmentChanged = "Online.Social.PunishmentChanged";

    /// <summary>群组变更。</summary>
    public const string GroupChanged = "Online.Social.GroupChanged";

    /// <summary>事件来源标识。</summary>
    public const string Source = "online-social";

    /// <summary>好友变更动作：发起请求。</summary>
    public const string FriendshipRequested = "Requested";

    /// <summary>好友变更动作：接受请求。</summary>
    public const string FriendshipAccepted = "Accepted";

    /// <summary>好友变更动作：拒绝请求。</summary>
    public const string FriendshipRejected = "Rejected";

    /// <summary>好友变更动作：删除好友。</summary>
    public const string FriendshipRemoved = "Removed";

    /// <summary>好友变更动作：请求超期。</summary>
    public const string FriendshipExpired = "Expired";

    /// <summary>屏蔽变更动作：新增屏蔽。</summary>
    public const string BlockAdded = "Added";

    /// <summary>屏蔽变更动作：解除屏蔽。</summary>
    public const string BlockRemoved = "Removed";

    /// <summary>静音变更动作：新增静音。</summary>
    public const string MuteAdded = "Added";

    /// <summary>静音变更动作：解除静音。</summary>
    public const string MuteRemoved = "Removed";

    /// <summary>处罚变更动作：施加处罚（运行时消费该动作处置当前连接）。</summary>
    public const string PunishmentApplied = "Applied";

    /// <summary>处罚变更动作：撤销处罚。</summary>
    public const string PunishmentRevoked = "Revoked";

    /// <summary>群组变更动作：建群。</summary>
    public const string GroupCreated = "Created";

    /// <summary>群组变更动作：成员加入。</summary>
    public const string GroupMemberJoined = "MemberJoined";

    /// <summary>群组变更动作：成员退出或被踢出。</summary>
    public const string GroupMemberLeft = "MemberLeft";

    /// <summary>群组变更动作：角色变更。</summary>
    public const string GroupRoleChanged = "RoleChanged";

    /// <summary>群组变更动作：解散。</summary>
    public const string GroupDisbanded = "Disbanded";

    /// <summary>群组变更动作：邀请变更。</summary>
    public const string GroupInviteChanged = "InviteChanged";

    /// <summary>群组变更动作：元数据变更。</summary>
    public const string GroupMetadataUpdated = "MetadataUpdated";

    /// <summary>
    /// 构造好友关系变更事件。
    /// </summary>
    /// <param name="friendship">变更后的关系快照。</param>
    /// <param name="action">变更动作（取本类型的 <c>Friendship*</c> 常量）。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <returns>事件信封实例。</returns>
    public static OnlineEvent CreateFriendshipChanged(OnlineFriendship friendship, string action, string correlationId = null)
    {
        var payload = new FriendshipChangedPayload
        {
            FriendshipId = friendship.FriendshipId ?? string.Empty,
            RequesterId = friendship.RequesterId,
            AddresseeId = friendship.AddresseeId,
            State = friendship.State.ToString(),
            Action = action ?? string.Empty,
        };
        var auditFields = new Dictionary<string, string>
        {
            { "FriendshipId", payload.FriendshipId },
            { "RequesterId", payload.RequesterId.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            { "AddresseeId", payload.AddresseeId.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            { "State", payload.State },
            { "Action", payload.Action },
        };
        return new OnlineEvent
        {
            EventId = "evt-" + Guid.NewGuid().ToString("N"),
            EventType = FriendshipChanged,
            OccurredTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SchemaVersion = 1,
            TenantId = friendship.TenantId,
            AppId = friendship.AppId,
            ServerId = friendship.ServerId,
            // 关系是双边的，信封只放得下一个玩家位，取请求方（方向所有者）；
            // 双方标识都在载荷与审计字段中，消费端不得只看 PlayerId。
            PlayerId = friendship.RequesterId,
            Source = Source,
            CorrelationId = correlationId ?? string.Empty,
            Payload = JsonSerializer.SerializeToUtf8Bytes(payload),
            PayloadAuditFields = auditFields,
        };
    }

    /// <summary>
    /// 构造屏蔽变更事件。
    /// </summary>
    /// <param name="entry">变更涉及的屏蔽记录。</param>
    /// <param name="action">变更动作（取本文 <see cref="BlockAdded"/> / <see cref="BlockRemoved"/>）。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <returns>事件信封实例。</returns>
    public static OnlineEvent CreateBlockChanged(OnlineBlockEntry entry, string action, string correlationId = null)
    {
        var payload = new BlockChangedPayload
        {
            OwnerId = entry.OwnerId,
            BlockedPlayerId = entry.BlockedPlayerId,
            Action = action ?? string.Empty,
        };
        var auditFields = new Dictionary<string, string>
        {
            { "OwnerId", payload.OwnerId.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            { "BlockedPlayerId", payload.BlockedPlayerId.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            { "Action", payload.Action },
        };
        return new OnlineEvent
        {
            EventId = "evt-" + Guid.NewGuid().ToString("N"),
            EventType = BlockChanged,
            OccurredTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SchemaVersion = 1,
            TenantId = entry.TenantId,
            AppId = entry.AppId,
            ServerId = entry.ServerId,
            // 屏蔽是单向私密事实：事件只投递给屏蔽发起人，不通知被屏蔽方（否则等于告知「你被谁屏蔽了」）。
            PlayerId = entry.OwnerId,
            Source = Source,
            CorrelationId = correlationId ?? string.Empty,
            Payload = JsonSerializer.SerializeToUtf8Bytes(payload),
            PayloadAuditFields = auditFields,
        };
    }

    /// <summary>
    /// 构造静音变更事件。
    /// </summary>
    /// <param name="entry">变更涉及的静音记录。</param>
    /// <param name="action">变更动作（取本文 <see cref="MuteAdded"/> / <see cref="MuteRemoved"/>）。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <returns>事件信封实例。</returns>
    public static OnlineEvent CreateMuteChanged(OnlineMuteEntry entry, string action, string correlationId = null)
    {
        var payload = new MuteChangedPayload
        {
            OwnerId = entry.OwnerId,
            MutedPlayerId = entry.MutedPlayerId,
            ExpiresAtTime = entry.ExpiresAtTime,
            Action = action ?? string.Empty,
        };
        var auditFields = new Dictionary<string, string>
        {
            { "OwnerId", payload.OwnerId.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            { "MutedPlayerId", payload.MutedPlayerId.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            { "ExpiresAtTime", payload.ExpiresAtTime.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            { "Action", payload.Action },
        };
        return new OnlineEvent
        {
            EventId = "evt-" + Guid.NewGuid().ToString("N"),
            EventType = MuteChanged,
            OccurredTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SchemaVersion = 1,
            TenantId = entry.TenantId,
            AppId = entry.AppId,
            ServerId = entry.ServerId,
            PlayerId = entry.OwnerId,
            Source = Source,
            CorrelationId = correlationId ?? string.Empty,
            Payload = JsonSerializer.SerializeToUtf8Bytes(payload),
            PayloadAuditFields = auditFields,
        };
    }

    /// <summary>
    /// 构造举报案件变更事件。
    /// </summary>
    /// <param name="reportCase">变更后的案件快照。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <returns>事件信封实例。</returns>
    public static OnlineEvent CreateReportChanged(OnlineReportCase reportCase, string correlationId = null)
    {
        var payload = new ReportChangedPayload
        {
            ReportId = reportCase.ReportId ?? string.Empty,
            ReporterId = reportCase.ReporterId,
            ReportedPlayerId = reportCase.ReportedPlayerId,
            Scene = reportCase.Scene.ToString(),
            Reason = reportCase.Reason.ToString(),
            State = reportCase.State.ToString(),
            Resolution = reportCase.Resolution.ToString(),
            Action = reportCase.State.ToString(),
        };
        var auditFields = new Dictionary<string, string>
        {
            { "Action", payload.Action },
            { "ReportId", payload.ReportId },
            { "ReporterId", payload.ReporterId.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            { "ReportedPlayerId", payload.ReportedPlayerId.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            { "Scene", payload.Scene },
            { "Reason", payload.Reason },
            { "State", payload.State },
            { "Resolution", payload.Resolution },
        };
        return new OnlineEvent
        {
            EventId = "evt-" + Guid.NewGuid().ToString("N"),
            EventType = ReportChanged,
            OccurredTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SchemaVersion = 1,
            TenantId = reportCase.TenantId,
            AppId = reportCase.AppId,
            ServerId = 0,
            // 举报是 App 级事实，事件面向 Admin 待办队列聚合，不按区服切分。
            PlayerId = reportCase.ReporterId,
            Source = Source,
            CorrelationId = correlationId ?? string.Empty,
            Payload = JsonSerializer.SerializeToUtf8Bytes(payload),
            PayloadAuditFields = auditFields,
        };
    }

    /// <summary>
    /// 构造处罚变更事件（运行时消费 <see cref="PunishmentApplied"/> 处置被处罚玩家的当前连接）。
    /// </summary>
    /// <param name="punishment">变更后的处罚快照。</param>
    /// <param name="action">变更动作（取本文 <see cref="PunishmentApplied"/> / <see cref="PunishmentRevoked"/>）。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <returns>事件信封实例。</returns>
    public static OnlineEvent CreatePunishmentChanged(OnlinePunishment punishment, string action, string correlationId = null)
    {
        var payload = new PunishmentChangedPayload
        {
            PunishmentId = punishment.PunishmentId ?? string.Empty,
            PlayerId = punishment.PlayerId,
            Kind = punishment.Kind.ToString(),
            EffectiveAtTime = punishment.EffectiveAtTime,
            ExpiresAtTime = punishment.ExpiresAtTime,
            Action = action ?? string.Empty,
        };
        var auditFields = new Dictionary<string, string>
        {
            { "PunishmentId", payload.PunishmentId },
            { "PlayerId", payload.PlayerId.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            { "Kind", payload.Kind },
            { "EffectiveAtTime", payload.EffectiveAtTime.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            { "ExpiresAtTime", payload.ExpiresAtTime.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            { "Action", payload.Action },
        };
        return new OnlineEvent
        {
            EventId = "evt-" + Guid.NewGuid().ToString("N"),
            EventType = PunishmentChanged,
            OccurredTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SchemaVersion = 1,
            TenantId = punishment.TenantId,
            AppId = punishment.AppId,
            ServerId = 0,
            PlayerId = punishment.PlayerId,
            Source = Source,
            CorrelationId = correlationId ?? string.Empty,
            Payload = JsonSerializer.SerializeToUtf8Bytes(payload),
            PayloadAuditFields = auditFields,
        };
    }

    /// <summary>
    /// 构造群组变更事件。
    /// </summary>
    /// <param name="group">变更后的群组快照。</param>
    /// <param name="action">变更动作（取本类型的 <c>Group*</c> 常量）。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <returns>事件信封实例。</returns>
    public static OnlineEvent CreateGroupChanged(OnlineGroup group, string action, string correlationId = null)
    {
        var memberCount = group.Members == null ? 0 : group.Members.Count;
        var payload = new GroupChangedPayload
        {
            GroupId = group.GroupId ?? string.Empty,
            OwnerId = group.OwnerId,
            State = group.State.ToString(),
            Action = action ?? string.Empty,
            MemberCount = memberCount,
            Revision = group.Revision,
        };
        var auditFields = new Dictionary<string, string>
        {
            { "GroupId", payload.GroupId },
            { "OwnerId", payload.OwnerId.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            { "State", payload.State },
            { "Action", payload.Action },
            { "MemberCount", payload.MemberCount.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            { "Revision", payload.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture) },
        };
        return new OnlineEvent
        {
            EventId = "evt-" + Guid.NewGuid().ToString("N"),
            EventType = GroupChanged,
            OccurredTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SchemaVersion = 1,
            TenantId = group.TenantId,
            AppId = group.AppId,
            ServerId = 0,
            // 群组是 App 级事实（跨区服成立），信封 PlayerId 取群主作为归属锚点。
            PlayerId = group.OwnerId,
            Source = Source,
            CorrelationId = correlationId ?? string.Empty,
            Payload = JsonSerializer.SerializeToUtf8Bytes(payload),
            PayloadAuditFields = auditFields,
        };
    }

    /// <summary>
    /// 好友关系变更事件负载（SchemaVersion=1）。
    /// </summary>
    private sealed class FriendshipChangedPayload
    {
        /// <summary>关系标识。</summary>
        public string FriendshipId
        {
            get;
            set;
        }

        /// <summary>请求方。</summary>
        public long RequesterId
        {
            get;
            set;
        }

        /// <summary>被请求方。</summary>
        public long AddresseeId
        {
            get;
            set;
        }

        /// <summary>关系状态名。</summary>
        public string State
        {
            get;
            set;
        }

        /// <summary>变更动作名。</summary>
        public string Action
        {
            get;
            set;
        }
    }

    /// <summary>
    /// 屏蔽变更事件负载（SchemaVersion=1）。
    /// </summary>
    private sealed class BlockChangedPayload
    {
        /// <summary>屏蔽发起人。</summary>
        public long OwnerId
        {
            get;
            set;
        }

        /// <summary>被屏蔽玩家。</summary>
        public long BlockedPlayerId
        {
            get;
            set;
        }

        /// <summary>变更动作名。</summary>
        public string Action
        {
            get;
            set;
        }
    }

    /// <summary>
    /// 静音变更事件负载（SchemaVersion=1）。
    /// </summary>
    private sealed class MuteChangedPayload
    {
        /// <summary>静音发起人。</summary>
        public long OwnerId
        {
            get;
            set;
        }

        /// <summary>被静音玩家。</summary>
        public long MutedPlayerId
        {
            get;
            set;
        }

        /// <summary>静音失效时刻（UTC 毫秒；0 表示永久）。</summary>
        public long ExpiresAtTime
        {
            get;
            set;
        }

        /// <summary>变更动作名。</summary>
        public string Action
        {
            get;
            set;
        }
    }

    /// <summary>
    /// 举报案件变更事件负载（SchemaVersion=1；不含补充说明等自由文本）。
    /// </summary>
    private sealed class ReportChangedPayload
    {
        /// <summary>案件标识。</summary>
        public string ReportId
        {
            get;
            set;
        }

        /// <summary>举报人。</summary>
        public long ReporterId
        {
            get;
            set;
        }

        /// <summary>被举报人。</summary>
        public long ReportedPlayerId
        {
            get;
            set;
        }

        /// <summary>举报场景名。</summary>
        public string Scene
        {
            get;
            set;
        }

        /// <summary>举报原因名。</summary>
        public string Reason
        {
            get;
            set;
        }

        /// <summary>案件状态名。</summary>
        public string State
        {
            get;
            set;
        }

        /// <summary>处置结果名。</summary>
        public string Resolution
        {
            get;
            set;
        }

        /// <summary>变更动作名（举报案件的全部变更都是状态迁移，故动作即迁移后的状态名，与 <see cref="State"/> 同值）。</summary>
        public string Action
        {
            get;
            set;
        }
    }

    /// <summary>
    /// 处罚变更事件负载（SchemaVersion=1）。
    /// </summary>
    private sealed class PunishmentChangedPayload
    {
        /// <summary>处罚标识。</summary>
        public string PunishmentId
        {
            get;
            set;
        }

        /// <summary>被处罚玩家。</summary>
        public long PlayerId
        {
            get;
            set;
        }

        /// <summary>处罚种类名。</summary>
        public string Kind
        {
            get;
            set;
        }

        /// <summary>生效时刻（UTC 毫秒）。</summary>
        public long EffectiveAtTime
        {
            get;
            set;
        }

        /// <summary>失效时刻（UTC 毫秒；0 表示永久）。</summary>
        public long ExpiresAtTime
        {
            get;
            set;
        }

        /// <summary>变更动作名。</summary>
        public string Action
        {
            get;
            set;
        }
    }

    /// <summary>
    /// 群组变更事件负载（SchemaVersion=1）。
    /// </summary>
    private sealed class GroupChangedPayload
    {
        /// <summary>群组标识。</summary>
        public string GroupId
        {
            get;
            set;
        }

        /// <summary>群主标识。</summary>
        public long OwnerId
        {
            get;
            set;
        }

        /// <summary>群组状态名。</summary>
        public string State
        {
            get;
            set;
        }

        /// <summary>变更动作名。</summary>
        public string Action
        {
            get;
            set;
        }

        /// <summary>变更后成员数。</summary>
        public int MemberCount
        {
            get;
            set;
        }

        /// <summary>变更后群记录版本号（消费端据此判断是否漏事件）。</summary>
        public int Revision
        {
            get;
            set;
        }
    }
}
