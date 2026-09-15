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
/// 举报案件状态（vault:C7 S6.3）。
/// <para>
/// 维护约束：状态迁移唯一判据是 <see cref="OnlineReportStateMachine.TryTransition"/>；
/// <see cref="Submitted"/> 是玩家可撤回窗口，一旦进入 <see cref="Reviewing"/> 即由 Admin 接管，
/// 玩家不能再撤回（否则处置中途证据消失）。
/// </para>
/// </summary>
public enum OnlineReportState
{
    /// <summary>
    /// 已提交（待 Admin 受理；玩家可撤回）。
    /// </summary>
    Submitted = 0,

    /// <summary>
    /// 受理中（Admin 已接单，玩家不可再撤回）。
    /// </summary>
    Reviewing = 1,

    /// <summary>
    /// 已处置（Admin 已作出裁决，处置结果见 <see cref="OnlineReportCase.Resolution"/>）。
    /// </summary>
    Actioned = 2,

    /// <summary>
    /// 已驳回（判定无违规，不代表玩家恶意举报）。
    /// </summary>
    Rejected = 3,

    /// <summary>
    /// 玩家已撤回。
    /// </summary>
    Withdrawn = 4,
}
