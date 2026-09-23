// ==========================================================================================
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
//   侵犯其他合法权益等法律法规所禁止的行为！
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
// ==========================================================================================

namespace GameFrameX.Online.Runtime;

using GameFrameX.Online.Social;

/// <summary>
/// 聊天审计检索条件（<see cref="OnlineChatAuditProjection.QueryAsync"/> 除作用域外的全部过滤维度，
/// 形态对齐统一审计的 <c>OnlineAuditQuery</c> 先例：可选过滤 + 不透明游标 + 页大小）。
/// <para>
/// 维护约束：全部过滤条件为合取（AND）；时间窗为闭区间 [<see cref="StartTime"/>, <see cref="EndTime"/>]；
/// 正文回读经 <see cref="IOnlineChatStore.FindMessageAsync"/> 受控通道（C7 脱敏红线）。
/// </para>
/// </summary>
public sealed class ChatAuditQuery
{
    /// <summary>
    /// 获取或设置频道类型过滤（null 表示不限）。
    /// </summary>
    /// <remarks>Gets or sets the channel kind filter (null = no filter).</remarks>
    public OnlineChatChannelKind? ChannelKind { get; init; }

    /// <summary>
    /// 获取或设置参与者过滤（频道成员或发送者；null 表示不限）。
    /// </summary>
    /// <remarks>Gets or sets the participant filter (channel member or sender; null = no filter).</remarks>
    public long? ParticipantPlayerId { get; init; }

    /// <summary>
    /// 获取或设置正文关键字过滤（null 或空表示不限）。
    /// </summary>
    /// <remarks>Gets or sets the body keyword filter (null or empty = no filter).</remarks>
    public string Keyword { get; init; }

    /// <summary>
    /// 获取或设置起始时刻（UTC 毫秒；null 表示不限）。
    /// </summary>
    /// <remarks>Gets or sets the start time (UTC milliseconds; null = no filter).</remarks>
    public long? StartTime { get; init; }

    /// <summary>
    /// 获取或设置结束时刻（UTC 毫秒；null 表示不限）。
    /// </summary>
    /// <remarks>Gets or sets the end time (UTC milliseconds; null = no filter).</remarks>
    public long? EndTime { get; init; }

    /// <summary>
    /// 获取或设置分页游标（<c>sentAtTime:sequence</c>；null 从头）。
    /// </summary>
    /// <remarks>Gets or sets the pagination cursor (sentAtTime:sequence; null = from the beginning).</remarks>
    public string Cursor { get; init; }

    /// <summary>
    /// 获取或设置页大小（1～100；0 或负数取默认 20）。
    /// </summary>
    /// <remarks>Gets or sets the page size (1–100; 0 or negative = default 20).</remarks>
    public int PageSize { get; init; }
}
