// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and applicable international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织不承担任何责任，开发者应自行承担。
//   The organization and contributors shall not be responsible; developers shall bear the responsibility themselves.
//  ==========================================================================================

using System.Collections.Generic;
using GameFrameX.Capability.Online.Abstractions.Bridges;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Scope;
using GameFrameX.Foundation.Localization.Core;

namespace GameFrameX.Hotfix.Logic.Player.Friend;

/// <summary>
/// 好友查询桥薄适配器（C243 首个 Hotfix→Capability 桥接）：显式开关开启时把
/// <c>ReqFriendList</c> 的查询经 <see cref="OnlineFriendBridgeHolder"/> 进程内转发到
/// Online 好友契约面；开关关闭（缺省）时不接管，旧链行为逐字节保持。
/// <para>
/// 维护约束（红线）：
/// ① 开关开启时桥路径独占——桥不可用（宿主未启 / 已停止 / 租约撤销）与服务失败一律映射为
/// <c>-(int)OnlineErrorCode</c> 段的显式错误码（≤ -1001，与旧链 -1..-99 段不重叠），
/// 绝不静默回落旧链、绝不返回成功空列表掩盖错库 / 错进程；
/// ② 可信身份：作用域三元组只取 lease 携带的组合根授权值，玩家位只取请求主体 ActorId，
/// 请求体字段一律不可影响作用域；
/// ③ 无缓存：逐请求读取 holder 当前租约，热更 / 宿主重启后旧引用不存活。
/// </para>
/// </summary>
/// <remarks>
/// The friend-query thin bridge adapter (the first Hotfix→Capability bridge, C243): when the
/// explicit switch is on it forwards the <c>ReqFriendList</c> query in-process to the Online
/// friend contract face through <see cref="OnlineFriendBridgeHolder"/>; when off (default) it does
/// not intercept and the legacy chain stays byte-identical. With the switch on the bridge path is
/// exclusive — unavailability (host not started / stopped / lease revoked) and service failures map
/// to explicit <c>-(int)OnlineErrorCode</c> codes (≤ -1001, disjoint from the legacy -1..-99 band),
/// never silently falling back to the legacy chain and never masking a wrong store or wrong process
/// with a successful empty list. The scope triple comes only from the lease's composition-root
/// authorized values and the player position only from the requesting actor id — request-body
/// fields can never influence the scope; nothing is cached — each request reads the holder's
/// current lease so no stale reference survives a hotfix swap or host restart.
/// </remarks>
public static class FriendQueryBridge
{
    /// <summary>
    /// 获取好友查询桥开关是否开启（读取进程设置 <c>Online.EnableFriendQueryBridge</c>；
    /// 只应配置在 Game 角色段）。
    /// </summary>
    public static bool IsBridgeEnabled
    {
        get { return GameFrameX.Utility.Setting.GlobalSettings.CurrentSetting?.Online?.EnableFriendQueryBridge == true; }
    }

    /// <summary>
    /// 尝试以桥路径处理好友列表查询。
    /// </summary>
    /// <param name="actorId">请求主体（玩家全局 actor）标识——作用域玩家位唯一来源。</param>
    /// <param name="response">协议响应（成功填充条目与 0；失败填充显式错误码与空列表）。</param>
    /// <returns>桥开关开启返回 <c>true</c>（本次请求已由桥路径裁决，含显式失败）；
    /// 开关关闭返回 <c>false</c>（调用方继续旧链，响应未被触碰）。</returns>
    public static async Task<bool> TryQueryFriendListAsync(long actorId, RespFriendList response)
    {
        if (!IsBridgeEnabled)
        {
            return false;
        }

        var lease = OnlineFriendBridgeHolder.Current;
        if (lease == null || !lease.IsActive)
        {
            response.Friends = new List<FriendInfo>();
            response.ErrorCode = -(int)OnlineErrorCode.DependencyUnavailable;
            // Localization: Hotfix.Friend.FriendListBridgeUnavailable - 好友查询桥不可用（未发布或已撤销）, ActorId: {0}, Code: {1}
            LogHelper.Error(LocalizationService.GetString(Localization.Keys.Hotfix.Friend.FriendListBridgeUnavailable, actorId, OnlineErrorCode.DependencyUnavailable));
            return true;
        }

        try
        {
            var scope = new OnlineScope(lease.AuthorizedScope.TenantId, lease.AuthorizedScope.AppId, lease.AuthorizedScope.ServerId, actorId);
            var result = await lease.ListFriendsAsync(scope);
            if (!result.IsSuccess)
            {
                response.Friends = new List<FriendInfo>();
                response.ErrorCode = -(int)result.Code;
                // Localization: Hotfix.Friend.FriendListBridgeQueryFailed - 好友查询桥返回失败, Code: {0}, Message: {1}, ActorId: {2}
                LogHelper.Error(LocalizationService.GetString(Localization.Keys.Hotfix.Friend.FriendListBridgeQueryFailed, result.Code, result.Message, actorId));
                return true;
            }

            response.Friends = MapFriendInfos(result.Data);
            response.ErrorCode = 0;
            return true;
        }
        catch (System.Exception exception)
        {
            response.Friends = new List<FriendInfo>();
            response.ErrorCode = -2;
            // Localization: Hotfix.Friend.FriendListBridgeException - 好友查询桥调用异常, ActorId: {0}
            LogHelper.Error(exception, LocalizationService.GetString(Localization.Keys.Hotfix.Friend.FriendListBridgeException, actorId));
            return true;
        }
    }

    /// <summary>
    /// 契约条目映射为协议条目（字段级转发，无语义衍生）。
    /// </summary>
    /// <param name="entries">契约条目。</param>
    /// <returns>协议好友条目。</returns>
    private static List<FriendInfo> MapFriendInfos(IReadOnlyList<OnlineFriendEntry> entries)
    {
        var friends = new List<FriendInfo>(entries.Count);
        foreach (var entry in entries)
        {
            friends.Add(new FriendInfo
            {
                PlayerId = entry.PlayerId,
                PlayerName = entry.Name,
            });
        }

        return friends;
    }
}
