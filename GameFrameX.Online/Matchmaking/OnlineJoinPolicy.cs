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

namespace GameFrameX.Online.Matchmaking;

/// <summary>
/// 加入策略（vault:C5 S4.4 冻结四策略）。
/// <para>
/// 维护约束：策略判定在 <c>OnlineMatchListingService.JoinAsync</c> 单点收敛，四个分支互斥且必须各有
/// 拒绝用例（VC-4.14：行为与策略一致、越权加入被拒）。第 5 种 <c>MatchmakerOnly</c> 待 vault:C5
/// backlog L4 设计评审判定，本 change 不预埋分支——未定稿的策略一旦预埋就会成为事实契约。
/// </para>
/// </summary>
public enum OnlineJoinPolicy
{
    /// <summary>公开：任何人可加入。</summary>
    Public = 0,

    /// <summary>仅队伍：必须整队（≥ 2 人）加入，单人请求被拒。</summary>
    PartyOnly = 1,

    /// <summary>仅邀请：必须携带有效邀请凭证。</summary>
    InviteOnly = 2,

    /// <summary>密码：必须携带匹配的密码。</summary>
    Password = 3,
}
