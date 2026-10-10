// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规及开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes and liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:  https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using GameFrameX.Core.Session;
using GameFrameX.Apps.Common.EventData;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Apps.Player.Player.Component;
using GameFrameX.Apps.Player.Player.Entity;
using GameFrameX.Hotfix.Logic.Game.Room;
using GameFrameX.Hotfix.Logic.Player.Attribute;
using GameFrameX.Hotfix.Logic.Player.Mail;
using GameFrameX.Hotfix.Logic.Server;
using GameFrameX.Core.Events;

namespace GameFrameX.Hotfix.Logic.Player.Login;

public class PlayerComponentAgent : StateComponentAgent<PlayerComponent, PlayerState>
{
    public async Task OnLogout()
    {
        //移除在线玩家
        var serverComp = await ActorManager.GetComponentAgent<ServerComponentAgent>();
        await serverComp.RemoveOnlinePlayer(ActorId);
        EventDispatcher.Dispatch(ActorId, new OnRoleOfflineEventArgs());
        //下线后会被自动回收
        SetAutoRecycle(true);
        QuartzTimer.Remove(ScheduleIdSet);
    }

    /// <summary>
    /// 使用角色ID登录
    /// </summary>
    /// <param name="workChannel"></param>
    /// <param name="playerState"></param>
    /// <param name="reqLoginUniqueId"></param>
    /// <param name="response"></param>
    public async Task OnPlayerLogin(INetworkChannel workChannel, PlayerState playerState, RespPlayerLogin response)
    {
        // 更新连接会话数据
        await PlayerSessionManager.Instance.UpdateSession(workChannel.GameAppSession.SessionId, playerState.Id, playerState.Id.ToString(), NotifyDuplicateLoginAsync);
        response.Code = playerState.State;
        response.CreateTime = playerState.CreatedTime;
        response.PlayerInfo = new PlayerInfo
        {
            Id = playerState.Id,
            Name = playerState.Name,
            Level = playerState.Level,
            State = playerState.State,
            Avatar = playerState.Avatar,
        };

        // 初始化玩家属性默认值（属性系统）
        var attributeComponentAgent = await ActorManager.GetComponentAgent<PlayerAttributeComponentAgent>(playerState.Id);
        await attributeComponentAgent.InitializeDefaultsSilent();

        //加入在线玩家
        var serverComp = await ActorManager.GetComponentAgent<ServerComponentAgent>();
        await serverComp.AddOnlinePlayer(ActorId);
        EventDispatcher.Dispatch(ActorId, new OnRoleOnlineEventArgs());

        // 房间断线重连标记（房间系统）
        var roomComp = await ActorManager.GetComponentAgent<RoomComponentAgent>();
        await roomComp.MarkPlayerReconnected(ActorId);

        // 推送属性快照 + 邮件懒同步（邮件系统）
        await workChannel.WriteAsync(attributeComponentAgent.BuildSyncSnapshot());
        var mailAgent = await ActorManager.GetComponentAgent<MailComponentAgent>(playerState.Id);
        await mailAgent.SyncAsync();
    }

    /// <summary>
    /// 顶号通知器：向被顶掉的旧会话发送"账号已在其他设备登录"提示（RespPrompt Type=5，本地化文案）。
    /// 顶号编排（通知 → 清数据 → 断开 → 移除）封装在 PlayerSessionManager 内，通知器须在 Close 前内联 await，
    /// RespPrompt 属游戏协议（Proto 在 Core 之上），故由本调用方注入构造逻辑。
    /// </summary>
    /// <param name="oldSession">被顶掉的旧会话</param>
    private static Task NotifyDuplicateLoginAsync(IPlayerSession oldSession)
    {
        var msg = new RespPrompt
        {
            Type = 5,
            Content = LocalizationService.GetString(Localization.Keys.Apps.SessionManager.AccountAlreadyLoggedIn),
        };
        return oldSession.WriteAsync(msg);
    }
}
