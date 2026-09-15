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

namespace GameFrameX.Online.Social;

/// <summary>
/// 群组聚合（vault:C7 S6.4：Group 是「成员集合 + 邀请集合 + 元数据」的唯一事实源）。
/// <para>
/// 维护约束（红线）：
/// ① **整条记录是一个 CAS 单元**——成员集合、邀请集合、角色、Metadata 的任何变更都必须经
/// <see cref="IOnlineGroupStore.ReplaceAsync"/> 以整条记录提交，禁止拆成独立写入：分类写入会让
/// 「成员数上限」与「角色唯一性」读到半成品（成员已加、邀请仍在待答复），且无法用一个版本号裁决并发；
/// ② <see cref="OwnerId"/> 必须始终是成员集合中的一员且角色为 <see cref="OnlineGroupRole.Owner"/>——
/// 群主不得退出、不得被踢出、不得被降级（群主转让不在本 change 范围，服务层不发明该行为）；
/// ③ <see cref="Revision"/> 只由存储层推进（CAS 成功即 +1），调用方不得自行改写；
/// ④ <see cref="State"/> 为 <see cref="OnlineGroupState.Disbanded"/> 后一切写操作关闭，成员与邀请集合冻结保留。
/// </para>
/// </summary>
public sealed class OnlineGroup
{
    /// <summary>
    /// 获取或设置群组标识。
    /// </summary>
    public string GroupId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置租户标识（作用域隔离键）。
    /// </summary>
    public long TenantId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置 App 标识（作用域隔离键）。
    /// </summary>
    public long AppId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置群主标识（建群者，群内唯一）。
    /// </summary>
    public long OwnerId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置群组名称。
    /// </summary>
    public string Name
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置自定义元数据（键值对，供业务侧扩展；键非空由服务层校验）。
    /// </summary>
    public Dictionary<string, string> Metadata
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置成员集合。
    /// </summary>
    public List<OnlineGroupMember> Members
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置邀请集合（含历史终态邀请；同一被邀请人同时刻至多一条待答复邀请）。
    /// </summary>
    public List<OnlineGroupInvite> Invites
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置成员数上限（建群时固定，不接受成员的请求改写）。
    /// </summary>
    public int MaxMembers
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置群组状态。
    /// </summary>
    public OnlineGroupState State
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置版本号（CAS 判据；仅存储层在提交成功时推进）。
    /// </summary>
    public int Revision
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置创建时刻（UTC 毫秒）。
    /// </summary>
    public long CreatedAtTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置最近变更时刻（UTC 毫秒）。
    /// </summary>
    public long UpdatedAtTime
    {
        get;
        set;
    }

    /// <summary>
    /// 深拷贝群记录（成员、邀请、元数据逐条复制）。
    /// <para>
    /// 维护约束：存储层与调用方**不得**共享可变引用——写路径一律「先 Copy 到副本再改，然后以快照的版本号
    /// CAS 提交」，就地改快照会让未提交的改动泄漏给其他读取方，CAS 也就形同虚设。
    /// </para>
    /// </summary>
    /// <returns>群记录副本。</returns>
    public OnlineGroup Copy()
    {
        var copy = new OnlineGroup
        {
            GroupId = GroupId,
            TenantId = TenantId,
            AppId = AppId,
            OwnerId = OwnerId,
            Name = Name,
            MaxMembers = MaxMembers,
            State = State,
            Revision = Revision,
            CreatedAtTime = CreatedAtTime,
            UpdatedAtTime = UpdatedAtTime,
            Metadata = new Dictionary<string, string>(StringComparer.Ordinal),
            Members = new List<OnlineGroupMember>(),
            Invites = new List<OnlineGroupInvite>(),
        };

        if (Metadata != null)
        {
            foreach (var entry in Metadata)
            {
                copy.Metadata[entry.Key] = entry.Value;
            }
        }

        if (Members != null)
        {
            foreach (var member in Members)
            {
                copy.Members.Add(member.Copy());
            }
        }

        if (Invites != null)
        {
            foreach (var invite in Invites)
            {
                copy.Invites.Add(invite.Copy());
            }
        }

        return copy;
    }

    /// <summary>
    /// 判定指定玩家是否为本群成员。
    /// </summary>
    /// <param name="playerId">玩家标识。</param>
    /// <returns>是成员返回 <c>true</c>。</returns>
    public bool Contains(long playerId)
    {
        if (Members == null)
        {
            return false;
        }

        foreach (var member in Members)
        {
            if (member.PlayerId == playerId)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 取指定玩家的成员角色。
    /// </summary>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="role">成员角色；非成员时为默认值 <see cref="OnlineGroupRole.Member"/>（调用方必须先判返回值）。</param>
    /// <returns>是成员返回 <c>true</c>；非成员返回 <c>false</c>。</returns>
    public bool TryGetRole(long playerId, out OnlineGroupRole role)
    {
        role = OnlineGroupRole.Member;
        if (Members == null)
        {
            return false;
        }

        foreach (var member in Members)
        {
            if (member.PlayerId == playerId)
            {
                role = member.Role;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 取出指定被邀请人的全部待答复邀请。
    /// </summary>
    /// <param name="inviteeId">被邀请人玩家标识。</param>
    /// <returns>该玩家的待答复邀请（新列表；元素是记录内邀请，仅供读取，写路径须先 <see cref="Copy"/> 群记录）。</returns>
    public List<OnlineGroupInvite> FindPendingInvitesFor(long inviteeId)
    {
        var result = new List<OnlineGroupInvite>();
        if (Invites == null)
        {
            return result;
        }

        foreach (var invite in Invites)
        {
            if (invite.InviteeId == inviteeId && invite.State == OnlineGroupInviteState.Pending)
            {
                result.Add(invite);
            }
        }

        return result;
    }
}
