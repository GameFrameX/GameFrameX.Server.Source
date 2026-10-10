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

using GameFrameX.Apps.ServerRole.Friend.Component;
using GameFrameX.Apps.ServerRole.Friend.Entity;

namespace GameFrameX.Hotfix.Logic.ServerRole.Friend;

/// <summary>
/// 好友服务器（Friend Role，好友关系，域号 660）业务组件代理。
/// </summary>
/// <remarks>
/// Friend Role 独立业务逻辑系统（C197 垂直切片）的热更侧落点；与社交域好友消息（域号 120）无关。
/// 业务规则判定一律委托 <see cref="FriendRules"/>（纯函数，可单测）；
/// 本类只做状态编排：档案懒创建、申请双向登记、成友 / 解除双向清理、全量列表。
/// </remarks>
public class FriendComponentAgent : StateComponentAgent<FriendComponent, FriendState>
{
    /// <summary>
    /// 发送好友申请：约束校验（自己 / 已好友 / 已在申请）后双方建档并双向登记申请。
    /// </summary>
    /// <param name="playerId">申请发起玩家ID。</param>
    /// <param name="request">发送申请请求。</param>
    /// <param name="response">发送申请响应。</param>
    public Task OnSendRequestAsync(long playerId, ReqFriendSendRequest request, RespFriendSendRequest response)
    {
        var profile = GetOrCreateProfile(playerId);
        if (playerId == request.ToPlayerId)
        {
            response.ErrorCode = (int)FriendErrorCode.CannotSelf;
            return Task.CompletedTask;
        }

        if (FriendRules.IsFriend(profile, request.ToPlayerId))
        {
            response.ErrorCode = (int)FriendErrorCode.AlreadyFriends;
            return Task.CompletedTask;
        }

        if (FriendRules.HasOutgoingRequest(profile, request.ToPlayerId))
        {
            response.ErrorCode = (int)FriendErrorCode.RequestPending;
            return Task.CompletedTask;
        }

        var toProfile = GetOrCreateProfile(request.ToPlayerId);
        toProfile.Incoming.Add(playerId);
        profile.Outgoing.Add(request.ToPlayerId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 接受好友申请：要求来源玩家在自己的收到申请中；成友双向 + 清理双向申请。
    /// </summary>
    /// <param name="playerId">接受申请玩家ID。</param>
    /// <param name="request">接受申请请求。</param>
    /// <param name="response">接受申请响应。</param>
    public Task OnAcceptAsync(long playerId, ReqFriendAccept request, RespFriendAccept response)
    {
        var profile = GetOrCreateProfile(playerId);
        if (!profile.Incoming.Contains(request.FromPlayerId))
        {
            response.ErrorCode = (int)FriendErrorCode.RequestNotFound;
            return Task.CompletedTask;
        }

        var fromProfile = GetOrCreateProfile(request.FromPlayerId);
        FriendRules.Befriend(profile, fromProfile);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 拒绝好友申请：要求来源玩家在自己的收到申请中；清理双向申请。
    /// </summary>
    /// <param name="playerId">拒绝申请玩家ID。</param>
    /// <param name="request">拒绝申请请求。</param>
    /// <param name="response">拒绝申请响应。</param>
    public Task OnRejectAsync(long playerId, ReqFriendReject request, RespFriendReject response)
    {
        var profile = GetOrCreateProfile(playerId);
        if (!profile.Incoming.Contains(request.FromPlayerId))
        {
            response.ErrorCode = (int)FriendErrorCode.RequestNotFound;
            return Task.CompletedTask;
        }

        var fromProfile = GetOrCreateProfile(request.FromPlayerId);
        fromProfile.Outgoing.RemoveAll(item => item == playerId);
        profile.Incoming.RemoveAll(item => item == request.FromPlayerId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 解除好友关系：要求目标确为好友；双向解除 + 清理双向残留申请。
    /// </summary>
    /// <param name="playerId">发起解除玩家ID。</param>
    /// <param name="request">解除请求。</param>
    /// <param name="response">解除响应。</param>
    public Task OnRemoveAsync(long playerId, ReqFriendRemove request, RespFriendRemove response)
    {
        if (request.FriendPlayerId == playerId)
        {
            response.ErrorCode = (int)FriendErrorCode.CannotSelf;
            return Task.CompletedTask;
        }

        var profile = GetOrCreateProfile(playerId);
        if (!FriendRules.IsFriend(profile, request.FriendPlayerId))
        {
            response.ErrorCode = (int)FriendErrorCode.RequestNotFound;
            return Task.CompletedTask;
        }

        var friendProfile = GetOrCreateProfile(request.FriendPlayerId);
        FriendRules.Unfriend(profile, friendProfile);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 拉取自己的好友关系全量列表（好友 / 收到申请 / 发出申请）。
    /// </summary>
    /// <param name="playerId">玩家ID。</param>
    /// <param name="request">列表请求。</param>
    /// <param name="response">列表响应。</param>
    public Task OnListAsync(long playerId, ReqFriendRelationList request, RespFriendRelationList response)
    {
        if (State.Profiles.TryGetValue(playerId, out var profile))
        {
            response.Friends.AddRange(profile.Friends);
            response.IncomingRequests.AddRange(profile.Incoming);
            response.OutgoingRequests.AddRange(profile.Outgoing);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 查询玩家好友档案，不存在时懒创建。
    /// </summary>
    /// <param name="playerId">玩家ID。</param>
    /// <returns>好友档案。</returns>
    private FriendProfileState GetOrCreateProfile(long playerId)
    {
        if (!State.Profiles.TryGetValue(playerId, out var profile))
        {
            profile = new FriendProfileState { PlayerId = playerId };
            State.Profiles[playerId] = profile;
        }

        return profile;
    }
}
