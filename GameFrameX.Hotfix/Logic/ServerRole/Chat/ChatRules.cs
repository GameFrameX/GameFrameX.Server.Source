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

using System;
using System.Collections.Generic;
using System.Linq;
using GameFrameX.Apps.ServerRole.Chat.Entity;

namespace GameFrameX.Hotfix.Logic.ServerRole.Chat;

/// <summary>
/// 聊天业务纯规则（可脱离 Actor 基础设施单测）。
/// </summary>
/// <remarks>
/// 承载与存储无关的频道规则：文本合法性、成员容量、历史环削、增量拉取选择。
/// Agent 只做状态编排，规则判定一律走本类（<c>MailCampaignFilter</c> 先例）。
/// </remarks>
internal static class ChatRules
{
    /// <summary>
    /// 单条消息文本长度上限（字符）。
    /// </summary>
    public const int MaxTextLength = 200;

    /// <summary>
    /// 单频道成员数上限。
    /// </summary>
    public const int MaxChannelMembers = 200;

    /// <summary>
    /// 单频道保留的历史消息条数上限（环形削顶）。
    /// </summary>
    public const int MaxHistoryMessages = 100;

    /// <summary>
    /// 单次拉取条数上限。
    /// </summary>
    public const int MaxPullLimit = 100;

    /// <summary>
    /// 判定发言文本是否合法：非空白且不超过长度上限。
    /// </summary>
    /// <param name="text">发言文本。</param>
    /// <returns>合法返回 true。</returns>
    public static bool IsTextValid(string text)
    {
        return !string.IsNullOrWhiteSpace(text) && text.Length <= MaxTextLength;
    }

    /// <summary>
    /// 判定频道是否可再加入一名成员。
    /// </summary>
    /// <param name="memberCount">当前成员数。</param>
    /// <returns>可加入返回 true。</returns>
    public static bool CanJoin(int memberCount)
    {
        return memberCount < MaxChannelMembers;
    }

    /// <summary>
    /// 归一化拉取条数：&lt;=0 或超上限按上限处理。
    /// </summary>
    /// <param name="limit">请求条数。</param>
    /// <returns>归一化后的条数。</returns>
    public static int ClampPullLimit(int limit)
    {
        return limit <= 0 ? MaxPullLimit : Math.Min(limit, MaxPullLimit);
    }

    /// <summary>
    /// 增量选择历史消息：仅返回序号大于 <paramref name="sinceSeq"/> 的消息，按序号升序，至多 <paramref name="limit"/> 条。
    /// </summary>
    /// <param name="channel">频道状态。</param>
    /// <param name="sinceSeq">已同步到的最大序号。</param>
    /// <param name="limit">条数上限（调用前先归一化）。</param>
    /// <returns>增量消息列表。</returns>
    public static List<ChatMessageState> SelectHistory(ChatChannelState channel, long sinceSeq, int limit)
    {
        return channel.Messages.Where(message => message.Seq > sinceSeq).OrderBy(message => message.Seq).Take(limit).ToList();
    }

    /// <summary>
    /// 追加消息并维持环形历史：超过保留上限时淘汰最旧消息。
    /// </summary>
    /// <param name="channel">频道状态。</param>
    /// <param name="message">新消息（序号已由调用方从 NextSeq 取号）。</param>
    public static void AppendMessage(ChatChannelState channel, ChatMessageState message)
    {
        channel.Messages.Add(message);
        while (channel.Messages.Count > MaxHistoryMessages)
        {
            channel.Messages.RemoveAt(0);
        }
    }
}
