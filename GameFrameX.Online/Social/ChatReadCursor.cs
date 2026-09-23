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
/// 聊天读取游标（<see cref="IOnlineChatStore.ReadAfterAsync"/> 与 <see cref="IOnlineChatStore.CountUnreadAsync"/>
/// 共用的位点载荷：收敛 <c>(AfterSentAtTime, AfterSequence)</c> 稳定排序键二元组 + 读取页大小）。
/// <para>
/// 维护约束：排序键固定为 <c>(SentAtTime, Sequence)</c> 升序，实现不得改用其他顺序——
/// 游标由服务层按同一键编码，换序即失效。
/// </para>
/// </summary>
public sealed class ChatReadCursor
{
    /// <summary>
    /// 获取或设置频道标识。
    /// </summary>
    /// <remarks>Gets or sets the channel id.</remarks>
    public string ChannelId { get; init; }

    /// <summary>
    /// 获取或设置游标位置的发送时刻（UTC 毫秒；从头读传 <c>0</c>）。
    /// </summary>
    /// <remarks>Gets or sets the sent-at time of the cursor position (UTC milliseconds; 0 = from the beginning).</remarks>
    public long AfterSentAtTime { get; init; }

    /// <summary>
    /// 获取或设置游标位置的频道内序号（从头读传 <c>0</c>）。
    /// </summary>
    /// <remarks>Gets or sets the in-channel sequence of the cursor position (0 = from the beginning).</remarks>
    public long AfterSequence { get; init; }

    /// <summary>
    /// 获取或设置最多返回条数（必须为正；<see cref="IOnlineChatStore.CountUnreadAsync"/> 不消费本字段）。
    /// </summary>
    /// <remarks>Gets or sets the maximum number of items to return (must be positive; not consumed by CountUnreadAsync).</remarks>
    public int Limit { get; init; }
}
