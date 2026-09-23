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

namespace GameFrameX.Online.Social;

/// <summary>
/// 举报案件受理 / 裁决载荷（<see cref="OnlineSocialDecisionService.TransitionReportAsync"/> 的唯一提交形态，Admin 命令面）。
/// <para>
/// 维护约束：进入 <see cref="OnlineReportState.Rejected"/> 必须携带
/// <see cref="OnlineReportResolution.NoViolation"/>、进入 <see cref="OnlineReportState.Actioned"/>
/// 必须携带非 <see cref="OnlineReportResolution.None"/> 的处置结果（守卫在服务内，不靠调用方自觉）。
/// </para>
/// </summary>
public sealed class OnlineReportTransition
{
    /// <summary>
    /// 获取或设置租户标识。
    /// </summary>
    /// <remarks>Gets or sets the tenant id.</remarks>
    public long TenantId { get; init; }

    /// <summary>
    /// 获取或设置 App 标识。
    /// </summary>
    /// <remarks>Gets or sets the app id.</remarks>
    public long AppId { get; init; }

    /// <summary>
    /// 获取或设置案件标识。
    /// </summary>
    /// <remarks>Gets or sets the report case id.</remarks>
    public string ReportId { get; init; }

    /// <summary>
    /// 获取或设置目标状态。
    /// </summary>
    /// <remarks>Gets or sets the target state.</remarks>
    public OnlineReportState TargetState { get; init; }

    /// <summary>
    /// 获取或设置处置结果（进入「已处置」须非 <see cref="OnlineReportResolution.None"/>）。
    /// </summary>
    /// <remarks>Gets or sets the resolution (must not be None when entering Actioned).</remarks>
    public OnlineReportResolution Resolution { get; init; }

    /// <summary>
    /// 获取或设置受理 Admin 标识。
    /// </summary>
    /// <remarks>Gets or sets the handling admin id.</remarks>
    public long HandlerAdminId { get; init; }

    /// <summary>
    /// 获取或设置 Admin 侧案件关联键（可空）。
    /// </summary>
    /// <remarks>Gets or sets the admin-side case key (optional).</remarks>
    public string AdminCaseId { get; init; }

    /// <summary>
    /// 获取或设置关联标识（可空）。
    /// </summary>
    /// <remarks>Gets or sets the correlation id (optional).</remarks>
    public string CorrelationId { get; init; }
}
