// ==========================================================================================
//  GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//  GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//  均受中华人民共和国及相关国际法律法规保护。
//  are protected by the laws of the People's Republic of China and relevant international regulations.
// 
//  使用本项目须严格遵守相应法律法规及开源许可证之规定。
//  Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
// 
//  本项目采用 MIT 许可证与 Apache License 2.0 双许可证分发，
//  This project is dual-licensed under the MIT License and Apache License 2.0,
//  完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//  please refer to the LICENSE file in the root directory of the source code for the full license text.
// 
//  禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//  It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//  侵犯他人合法权益等法律法规所禁止的行为！
//  or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//  因基于本项目二次开发所产生的一切法律纠纷与责任，
//  Any legal disputes and liabilities arising from secondary development based on this project
//  本项目组织与贡献者概不承担。
//  shall be borne solely by the developer; the project organization and contributors assume no responsibility.
// 
//  GitHub 仓库：https://github.com/GameFrameX
//  GitHub Repository: https://github.com/GameFrameX
//  Gitee  仓库：https://gitee.com/GameFrameX
//  Gitee Repository:  https://gitee.com/GameFrameX
//  官方文档：https://gameframex.doc.alianblank.com/
//  Official Documentation: https://gameframex.doc.alianblank.com/
// ==========================================================================================

namespace GameFrameX.Apps.ServerRole.Chat.Entity;

/// <summary>
/// 聊天服务器（Chat Role）服务端作用域状态。
/// </summary>
/// <remarks>
/// 承载全部聊天频道的成员关系与消息历史：生命周期由 StateComponent 管理
/// （激活读取 / 消息完成回写 / 定时与停机兜底回存）。
/// </remarks>
public sealed class ChatState : BaseCacheState
{
    /// <summary>
    /// 全部频道。Key: 频道ID。
    /// </summary>
    public Dictionary<long, ChatChannelState> Channels { get; set; } = new Dictionary<long, ChatChannelState>();
}

/// <summary>
/// 单个聊天频道状态。
/// </summary>
public sealed class ChatChannelState
{
    /// <summary>
    /// 频道ID。
    /// </summary>
    public long ChannelId { get; set; }

    /// <summary>
    /// 频道成员列表（玩家ID）。
    /// </summary>
    public List<long> Members { get; set; } = new List<long>();

    /// <summary>
    /// 消息环形历史（仅保留最近 <see cref="int"/> 上限条，见 ChatRules.MaxHistoryMessages）。
    /// </summary>
    public List<ChatMessageState> Messages { get; set; } = new List<ChatMessageState>();

    /// <summary>
    /// 下一个频道内消息序号（从 1 递增）。
    /// </summary>
    public long NextSeq { get; set; } = 1;
}

/// <summary>
/// 单条聊天消息状态。
/// </summary>
public sealed class ChatMessageState
{
    /// <summary>
    /// 频道内递增序号。
    /// </summary>
    public long Seq { get; set; }

    /// <summary>
    /// 发言玩家ID。
    /// </summary>
    public long PlayerId { get; set; }

    /// <summary>
    /// 消息文本。
    /// </summary>
    public string Text { get; set; }

    /// <summary>
    /// 发言 Unix 秒。
    /// </summary>
    public long UnixTime { get; set; }
}
