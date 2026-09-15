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
using GameFrameX.Online.Party;

/// <summary>
/// 队伍域事件工厂（vault:C5 S4.3/S4.7：队伍状态与邀请变更事件供下游与 Admin 消费）。
/// <para>
/// 维护约束：事件是事实不是状态——消费端不得回写队伍状态；队伍状态迁移事件必须携带
/// from/to/reason 三元组与成员数快照（VC-4.6/VC-4.7 断言依据）；
/// 载荷只放标识与状态名，不放邀请密码等敏感字段（沿用 C93 脱敏要求）。
/// </para>
/// </summary>
public static class OnlinePartyEvents
{
    /// <summary>队伍状态变更。</summary>
    public const string PartyChanged = "Online.Party.Changed";

    /// <summary>邀请状态变更。</summary>
    public const string PartyInviteChanged = "Online.Party.InviteChanged";

    /// <summary>事件来源标识。</summary>
    public const string Source = "online-party";

    /// <summary>
    /// 构造队伍状态变更事件。
    /// </summary>
    /// <param name="party">变更后的队伍快照。</param>
    /// <param name="fromState">原状态。</param>
    /// <param name="toState">目标状态。</param>
    /// <param name="reason">变更原因。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <returns>事件信封实例。</returns>
    public static OnlineEvent CreatePartyChanged(OnlineParty party, OnlinePartyState fromState, OnlinePartyState toState, OnlinePartyLeaveReason reason, string correlationId = null)
    {
        var payload = new PartyChangedPayload
        {
            PartyId = party.PartyId ?? string.Empty,
            LeaderId = party.LeaderId,
            FromState = fromState.ToString(),
            ToState = toState.ToString(),
            Reason = reason.ToString(),
            MemberCount = party.Members == null ? 0 : party.Members.Count,
        };
        var auditFields = new Dictionary<string, string>
        {
            { "PartyId", party.PartyId ?? string.Empty },
            { "LeaderId", party.LeaderId.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            { "FromState", fromState.ToString() },
            { "ToState", toState.ToString() },
            { "Reason", reason.ToString() },
            { "MemberCount", payload.MemberCount.ToString(System.Globalization.CultureInfo.InvariantCulture) },
        };
        return new OnlineEvent
        {
            EventId = "evt-" + Guid.NewGuid().ToString("N"),
            EventType = PartyChanged,
            OccurredTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SchemaVersion = 1,
            TenantId = party.TenantId,
            AppId = party.AppId,
            ServerId = party.ServerId,
            PlayerId = party.LeaderId,
            Source = Source,
            CorrelationId = correlationId ?? string.Empty,
            Payload = JsonSerializer.SerializeToUtf8Bytes(payload),
            PayloadAuditFields = auditFields,
        };
    }

    /// <summary>
    /// 构造邀请状态变更事件。
    /// </summary>
    /// <param name="invite">变更后的邀请快照。</param>
    /// <param name="serverId">区服标识。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <returns>事件信封实例。</returns>
    public static OnlineEvent CreateInviteChanged(OnlinePartyInvite invite, long serverId, string correlationId = null)
    {
        var payload = new PartyInviteChangedPayload
        {
            InviteId = invite.InviteId ?? string.Empty,
            PartyId = invite.PartyId ?? string.Empty,
            InviterId = invite.InviterId,
            InviteeId = invite.InviteeId,
            State = invite.State.ToString(),
        };
        var auditFields = new Dictionary<string, string>
        {
            { "InviteId", invite.InviteId ?? string.Empty },
            { "PartyId", invite.PartyId ?? string.Empty },
            { "InviterId", invite.InviterId.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            { "InviteeId", invite.InviteeId.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            { "State", invite.State.ToString() },
        };
        return new OnlineEvent
        {
            EventId = "evt-" + Guid.NewGuid().ToString("N"),
            EventType = PartyInviteChanged,
            OccurredTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SchemaVersion = 1,
            TenantId = invite.TenantId,
            AppId = invite.AppId,
            ServerId = serverId,
            PlayerId = invite.InviteeId,
            Source = Source,
            CorrelationId = correlationId ?? string.Empty,
            Payload = JsonSerializer.SerializeToUtf8Bytes(payload),
            PayloadAuditFields = auditFields,
        };
    }

    /// <summary>
    /// 队伍状态变更事件负载（SchemaVersion=1）。
    /// </summary>
    private sealed class PartyChangedPayload
    {
        /// <summary>队伍标识。</summary>
        public string PartyId
        {
            get;
            set;
        }

        /// <summary>队长标识。</summary>
        public long LeaderId
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

        /// <summary>变更原因名。</summary>
        public string Reason
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
    }

    /// <summary>
    /// 邀请状态变更事件负载（SchemaVersion=1）。
    /// </summary>
    private sealed class PartyInviteChangedPayload
    {
        /// <summary>邀请标识。</summary>
        public string InviteId
        {
            get;
            set;
        }

        /// <summary>队伍标识。</summary>
        public string PartyId
        {
            get;
            set;
        }

        /// <summary>邀请发起人。</summary>
        public long InviterId
        {
            get;
            set;
        }

        /// <summary>被邀请玩家。</summary>
        public long InviteeId
        {
            get;
            set;
        }

        /// <summary>邀请状态名。</summary>
        public string State
        {
            get;
            set;
        }
    }
}
