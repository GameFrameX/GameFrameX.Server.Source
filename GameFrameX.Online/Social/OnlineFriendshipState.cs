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
/// 好友关系状态（vault:C7 S6.2：关系状态唯一，删除后可重新添加）。
/// <para>
/// 维护约束：一条关系记录承载「一对玩家」的全部状态，状态迁移唯一判据是
/// <see cref="OnlineFriendshipStateMachine.TryTransition"/>——不存在「请求中」与「已是好友」并存的中间态
/// （VC-6.2：每步状态唯一、无中间态残留）。<see cref="Requested"/> 是唯一的非终态；
/// <see cref="Rejected"/><see cref="Expired"/><see cref="Removed"/> 均为「可重新发起」的静止态，
/// 重新发起时回到 <see cref="Requested"/>，不新建第二条关系记录。
/// </para>
/// </summary>
public enum OnlineFriendshipState
{
    /// <summary>
    /// 待答复（请求已发出、对方尚未处理）。同一对玩家至多一条处于本状态（VC-6.1）。
    /// </summary>
    Requested = 0,

    /// <summary>
    /// 已成为好友（双方关系成立）。
    /// </summary>
    Accepted = 1,

    /// <summary>
    /// 请求被拒绝（静止态；可重新发起）。
    /// </summary>
    Rejected = 2,

    /// <summary>
    /// 请求超期未答复（静止态；可重新发起）。
    /// </summary>
    Expired = 3,

    /// <summary>
    /// 关系已删除（静止态；可重新发起，VC-6.2 状态机末段）。
    /// </summary>
    Removed = 4,
}
