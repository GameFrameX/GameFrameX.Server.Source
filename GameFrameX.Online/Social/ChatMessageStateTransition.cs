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
/// 聊天消息状态 CAS 迁移载荷（<see cref="IOnlineChatStore.UpdateMessageStateAsync"/> 的唯一提交形态；当前只有撤回一种迁移）。
/// </summary>
public sealed class ChatMessageStateTransition
{
    /// <summary>
    /// 获取或设置频道标识。
    /// </summary>
    /// <remarks>Gets or sets the channel id.</remarks>
    public string ChannelId { get; init; }

    /// <summary>
    /// 获取或设置消息标识。
    /// </summary>
    /// <remarks>Gets or sets the message id.</remarks>
    public string MessageId { get; init; }

    /// <summary>
    /// 获取或设置期望的当前状态；不匹配即失败。
    /// </summary>
    /// <remarks>Gets or sets the expected current state; a mismatch fails the whole transition.</remarks>
    public OnlineChatMessageState ExpectedState { get; init; }

    /// <summary>
    /// 获取或设置目标状态。
    /// </summary>
    /// <remarks>Gets or sets the target state.</remarks>
    public OnlineChatMessageState NewState { get; init; }

    /// <summary>
    /// 获取或设置本次变更时刻（UTC 毫秒）。
    /// </summary>
    /// <remarks>Gets or sets the change time (UTC milliseconds).</remarks>
    public long NowUnixMilliseconds { get; init; }

    /// <summary>
    /// 获取或设置撤回人（非撤回迁移传 <c>0</c>）。
    /// </summary>
    /// <remarks>Gets or sets the recalling player id (0 for non-recall transitions).</remarks>
    public long RecalledByPlayerId { get; init; }
}
