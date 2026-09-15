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

namespace GameFrameX.Online.Party;

/// <summary>
/// 队伍聚合（vault:C5 S4.3：Party 是「希望一起行动的玩家集合」的唯一事实源）。
/// <para>
/// 维护约束（红线）：成员集合是队伍完整性的唯一依据——匹配阶段必须整队成功或整队失败，
/// 禁止把队伍拆开分别匹配（VC-4.3）。<see cref="LeaderId"/> 必须始终是成员集合中的一员
/// （队长退出即转移或解散，不存在孤儿队长）。成员在线状态不在此存放。
/// </para>
/// </summary>
public sealed class OnlineParty
{
    /// <summary>
    /// 获取或设置队伍标识。
    /// </summary>
    public string PartyId
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
    /// 获取或设置区服标识（作用域隔离键）。
    /// </summary>
    public long ServerId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置队长标识。
    /// </summary>
    public long LeaderId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置队伍状态。
    /// </summary>
    public OnlinePartyState State
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置成员集合。
    /// </summary>
    public List<OnlinePartyMember> Members
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置人数下限（低于该值即人数不足）。
    /// </summary>
    public int MinMembers
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置人数上限。
    /// </summary>
    public int MaxMembers
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
    /// 获取或设置空闲过期时刻（UTC 毫秒；无成员变动且未进入匹配则到期转 Expired）。
    /// </summary>
    public long ExpiresAtTime
    {
        get;
        set;
    }

    /// <summary>
    /// 复制队伍聚合（存储层防御性深拷贝使用；成员集合逐条复制，避免别名污染已存状态）。
    /// </summary>
    /// <returns>队伍副本。</returns>
    public OnlineParty Copy()
    {
        var members = new List<OnlinePartyMember>();
        if (Members != null)
        {
            foreach (var member in Members)
            {
                members.Add(member.Copy());
            }
        }

        return new OnlineParty
        {
            PartyId = PartyId,
            TenantId = TenantId,
            AppId = AppId,
            ServerId = ServerId,
            LeaderId = LeaderId,
            State = State,
            Members = members,
            MinMembers = MinMembers,
            MaxMembers = MaxMembers,
            CreatedAtTime = CreatedAtTime,
            UpdatedAtTime = UpdatedAtTime,
            ExpiresAtTime = ExpiresAtTime,
        };
    }
}
