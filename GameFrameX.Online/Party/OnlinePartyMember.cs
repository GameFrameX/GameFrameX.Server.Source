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
/// 队伍成员（vault:C5 S4.3：队伍成员条目）。
/// <para>
/// 维护约束：队长身份由 <see cref="OnlineParty.LeaderId"/> 单点持有，成员条目不重复记录 IsLeader——
/// 两处记录必然产生不一致。成员在线状态**不**在此存放：按 vault:C5 风险表，
/// 阶段的 Presence 是唯一在线事实源，Party 不额外镜像。
/// </para>
/// </summary>
public sealed class OnlinePartyMember
{
    /// <summary>
    /// 获取或设置玩家标识。
    /// </summary>
    public long PlayerId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置成员状态（已加入/已准备）。
    /// </summary>
    public OnlinePartyMemberState MemberState
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置入队时刻（UTC 毫秒；队长转移的确定性依据——转移给加入最早者）。
    /// </summary>
    public long JoinedAtTime
    {
        get;
        set;
    }

    /// <summary>
    /// 复制成员条目（存储层防御性深拷贝使用）。
    /// </summary>
    /// <returns>成员副本。</returns>
    public OnlinePartyMember Copy()
    {
        return new OnlinePartyMember
        {
            PlayerId = PlayerId,
            MemberState = MemberState,
            JoinedAtTime = JoinedAtTime,
        };
    }
}
