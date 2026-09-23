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
/// 举报提交载荷（<see cref="OnlineSocialDecisionService.SubmitReportAsync"/> 除作用域外的全部提交维度）。
/// <para>
/// 维护约束：证据字段照单全收（VC-6.16 证据链的写入点）；案件标识由服务生成，
/// 调用方不可指定。聊天场景（<see cref="OnlineReportScene.Chat"/>）必须携带 <see cref="ChannelId"/>。
/// </para>
/// </summary>
public sealed class OnlineReportSubmission
{
    /// <summary>
    /// 获取或设置被举报人标识。
    /// </summary>
    /// <remarks>Gets or sets the reported player id.</remarks>
    public long ReportedPlayerId { get; init; }

    /// <summary>
    /// 获取或设置举报场景。
    /// </summary>
    /// <remarks>Gets or sets the report scene.</remarks>
    public OnlineReportScene Scene { get; init; }

    /// <summary>
    /// 获取或设置举报原因。
    /// </summary>
    /// <remarks>Gets or sets the report reason.</remarks>
    public OnlineReportReason Reason { get; init; }

    /// <summary>
    /// 获取或设置对局标识（可空）。
    /// </summary>
    /// <remarks>Gets or sets the match id (optional).</remarks>
    public string MatchId { get; init; }

    /// <summary>
    /// 获取或设置被举报消息标识（可空）。
    /// </summary>
    /// <remarks>Gets or sets the reported chat message id (optional).</remarks>
    public string ChatMessageId { get; init; }

    /// <summary>
    /// 获取或设置频道标识（可空；聊天场景必填）。
    /// </summary>
    /// <remarks>Gets or sets the channel id (optional; required for the chat scene).</remarks>
    public string ChannelId { get; init; }

    /// <summary>
    /// 获取或设置补充说明（可空；原因选「其他」时必填）。
    /// </summary>
    /// <remarks>Gets or sets the evidence text (optional; required when the reason is Other).</remarks>
    public string Evidence { get; init; }

    /// <summary>
    /// 获取或设置关联标识（可空）。
    /// </summary>
    /// <remarks>Gets or sets the correlation id (optional).</remarks>
    public string CorrelationId { get; init; }
}
