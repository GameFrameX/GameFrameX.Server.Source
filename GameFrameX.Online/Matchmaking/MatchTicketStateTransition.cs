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
//   please see the LICENSE file in the root directory of the source code.
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
/// 匹配票据状态 CAS 迁移载荷（<see cref="IOnlineMatchTicketStore.UpdateStateAsync"/> 的唯一提交形态；
/// 取消 / 过期 / 失败走此入口）。
/// <para>
/// 维护约束：期望状态不匹配即整体失败并返回 null，绝不部分应用——取消与匹配成功的竞态由此收敛：
/// 谁先改到终态，谁的结果生效。
/// </para>
/// </summary>
public sealed class MatchTicketStateTransition
{
    /// <summary>
    /// 获取或设置票据标识。
    /// </summary>
    /// <remarks>Gets or sets the ticket id.</remarks>
    public string TicketId { get; init; }

    /// <summary>
    /// 获取或设置期望的当前状态；不匹配即失败。
    /// </summary>
    /// <remarks>Gets or sets the expected current state; a mismatch fails the whole transition.</remarks>
    public OnlineMatchTicketState ExpectedState { get; init; }

    /// <summary>
    /// 获取或设置目标状态。
    /// </summary>
    /// <remarks>Gets or sets the target state.</remarks>
    public OnlineMatchTicketState NewState { get; init; }

    /// <summary>
    /// 获取或设置失败原因码（非失败转迁移填 <see cref="OnlineMatchFailureReason.None"/>）。
    /// </summary>
    /// <remarks>Gets or sets the failure reason (None for non-failure transitions).</remarks>
    public OnlineMatchFailureReason FailureReason { get; init; }

    /// <summary>
    /// 获取或设置关联的分配标识（无则空字符串）。
    /// </summary>
    /// <remarks>Gets or sets the related assignment id (empty when absent).</remarks>
    public string AssignmentId { get; init; }
}
