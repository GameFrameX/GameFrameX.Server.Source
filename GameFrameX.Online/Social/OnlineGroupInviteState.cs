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
/// 群邀请状态（vault:C7 S6.4：邀请生命周期唯一，<see cref="Pending"/> 是唯一非终态）。
/// <para>
/// 维护约束：**同一被邀请人在同一群组内同时刻至多一条 <see cref="Pending"/> 邀请**——重复邀请幂等返回
/// 既有邀请，不新建也不报错（见 <c>OnlineGroupService.InviteAsync</c>）。
/// <see cref="Accepted"/><see cref="Rejected"/><see cref="Expired"/><see cref="Revoked"/> 均为终态且出边为空，
/// 终态邀请不可被改写；重新邀请须新建一条邀请条目，不复活旧邀请。
/// </para>
/// </summary>
public enum OnlineGroupInviteState
{
    /// <summary>
    /// 待答复（唯一非终态；同一被邀请人在同一群组内至多一条）。
    /// </summary>
    Pending = 0,

    /// <summary>
    /// 已接受（仅被邀请人本人可答复；接受即入群，角色为 <see cref="OnlineGroupRole.Member"/>）。
    /// </summary>
    Accepted = 1,

    /// <summary>
    /// 已拒绝（仅被邀请人本人可答复；拒绝不改变成员集合）。
    /// </summary>
    Rejected = 2,

    /// <summary>
    /// 已过期（到达有效期上限仍未答复，由扫描或答复路径置位；不计入「答复」语义）。
    /// </summary>
    Expired = 3,

    /// <summary>
    /// 已撤销（邀请发起人本人，或群主/管理员撤销）。
    /// </summary>
    Revoked = 4,
}
