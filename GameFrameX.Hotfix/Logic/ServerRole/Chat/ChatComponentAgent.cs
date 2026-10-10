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

using GameFrameX.Apps.ServerRole.Chat.Component;
using GameFrameX.Apps.ServerRole.Chat.Entity;
using GameFrameX.Foundation.Utility;

namespace GameFrameX.Hotfix.Logic.ServerRole.Chat;

/// <summary>
/// 聊天服务器（Chat Role）业务组件代理：频道成员关系与消息历史。
/// </summary>
/// <remarks>
/// Chat Role 独立业务逻辑系统（C197 垂直切片）的热更侧落点。
/// 业务规则判定一律委托 <see cref="ChatRules"/>（纯函数，可单测）；
/// 本类只做状态编排：频道懒创建、成员增删、消息落库与环形削顶、增量历史拉取。
/// </remarks>
public class ChatComponentAgent : StateComponentAgent<ChatComponent, ChatState>
{
    /// <summary>
    /// 加入频道：频道懒创建（首位加入者建频道），重复加入幂等（直接返回当前成员数）。
    /// </summary>
    /// <param name="playerId">玩家ID。</param>
    /// <param name="request">加入请求。</param>
    /// <param name="response">加入响应。</param>
    public Task OnJoinAsync(long playerId, ReqChatJoin request, RespChatJoin response)
    {
        var channel = GetOrCreateChannel(request.ChannelId);
        if (!channel.Members.Contains(playerId))
        {
            if (!ChatRules.CanJoin(channel.Members.Count))
            {
                response.ErrorCode = (int)ChatErrorCode.ChannelFull;
                return Task.CompletedTask;
            }

            channel.Members.Add(playerId);
        }

        response.MemberCount = channel.Members.Count;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 离开频道：未加入返回 <see cref="ChatErrorCode.NotJoined"/>；离开返回剩余成员数。
    /// </summary>
    /// <param name="playerId">玩家ID。</param>
    /// <param name="request">离开请求。</param>
    /// <param name="response">离开响应。</param>
    public Task OnLeaveAsync(long playerId, ReqChatLeave request, RespChatLeave response)
    {
        if (!TryGetChannel(request.ChannelId, out var channel))
        {
            response.ErrorCode = (int)ChatErrorCode.ChannelNotFound;
            return Task.CompletedTask;
        }

        if (!channel.Members.Remove(playerId))
        {
            response.ErrorCode = (int)ChatErrorCode.NotJoined;
            return Task.CompletedTask;
        }

        response.MemberCount = channel.Members.Count;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 频道发言：必须已加入且文本合法；消息按频道序号落库并维持环形历史。
    /// </summary>
    /// <param name="playerId">玩家ID。</param>
    /// <param name="request">发言请求。</param>
    /// <param name="response">发言响应。</param>
    public Task OnSpeakAsync(long playerId, ReqChatSpeak request, RespChatSpeak response)
    {
        if (!TryGetChannel(request.ChannelId, out var channel))
        {
            response.ErrorCode = (int)ChatErrorCode.ChannelNotFound;
            return Task.CompletedTask;
        }

        if (!channel.Members.Contains(playerId))
        {
            response.ErrorCode = (int)ChatErrorCode.NotJoined;
            return Task.CompletedTask;
        }

        if (!ChatRules.IsTextValid(request.Text))
        {
            response.ErrorCode = (int)ChatErrorCode.TextInvalid;
            return Task.CompletedTask;
        }

        var message = new ChatMessageState
        {
            Seq = channel.NextSeq++,
            PlayerId = playerId,
            Text = request.Text,
            UnixTime = TimerHelper.UnixTimeSeconds(),
        };
        ChatRules.AppendMessage(channel, message);
        response.Message = ToMessageInfo(message);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 增量拉取历史消息：仅已加入成员可拉取；返回序号大于 SinceSeq 的消息（升序、至多 Limit 条）。
    /// </summary>
    /// <param name="playerId">玩家ID。</param>
    /// <param name="request">拉取请求。</param>
    /// <param name="response">拉取响应。</param>
    public Task OnPullHistoryAsync(long playerId, ReqChatPullHistory request, RespChatPullHistory response)
    {
        if (!TryGetChannel(request.ChannelId, out var channel))
        {
            response.ErrorCode = (int)ChatErrorCode.ChannelNotFound;
            return Task.CompletedTask;
        }

        if (!channel.Members.Contains(playerId))
        {
            response.ErrorCode = (int)ChatErrorCode.NotJoined;
            return Task.CompletedTask;
        }

        var limit = ChatRules.ClampPullLimit(request.Limit);
        foreach (var message in ChatRules.SelectHistory(channel, request.SinceSeq, limit))
        {
            response.Messages.Add(ToMessageInfo(message));
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 查询频道状态，不存在时按需懒创建。
    /// </summary>
    /// <param name="channelId">频道ID。</param>
    /// <returns>频道状态。</returns>
    private ChatChannelState GetOrCreateChannel(long channelId)
    {
        if (!State.Channels.TryGetValue(channelId, out var channel))
        {
            channel = new ChatChannelState { ChannelId = channelId };
            State.Channels[channelId] = channel;
        }

        return channel;
    }

    /// <summary>
    /// 查询频道状态。
    /// </summary>
    /// <param name="channelId">频道ID。</param>
    /// <param name="channel">频道状态。</param>
    /// <returns>存在返回 true。</returns>
    private bool TryGetChannel(long channelId, out ChatChannelState channel)
    {
        return State.Channels.TryGetValue(channelId, out channel);
    }

    /// <summary>
    /// 状态消息 → 协议载荷。
    /// </summary>
    /// <param name="message">状态消息。</param>
    /// <returns>协议载荷。</returns>
    private static ChatMessageInfo ToMessageInfo(ChatMessageState message)
    {
        return new ChatMessageInfo
        {
            Seq = message.Seq,
            PlayerId = message.PlayerId,
            Text = message.Text,
            UnixTime = message.UnixTime,
        };
    }
}
