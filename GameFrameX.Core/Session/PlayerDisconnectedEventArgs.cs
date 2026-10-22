// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository:  https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:    https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using GameFrameX.Core.Abstractions.Events;

namespace GameFrameX.Core.Session;

/// <summary>
/// 玩家物理连接断开事件参数（C284 宿主→热更断线业务桥接契约）。
/// </summary>
/// <remarks>
/// 宿主持监听形态下，宿主会话断开钩子完成会话摘除后以本事件把「已绑定玩家的物理断开」
/// 通知热更域——热更侧全局监听器承接房间断线标记与会话恢复通知（原内联在热更侧
/// <c>AppStartUpHotfixGame.OnDisconnected</c> 的业务段；原名，C285 更名为 HotfixModuleLifecycleBridge）。事件绑定键即本类型本身；
/// 分发为全局（actorId = 0）fire-and-forget，与 <see cref="SessionRemovedEventArgs"/>
/// 登出清理同形态。携带断开的物理会话标识供热更侧旧回调迟到守卫使用。
/// Under the host-owned-listener shape, the host's session-disconnected hook removes the
/// session and then uses this event to notify the hotfix domain that a bound player's
/// physical connection dropped — the hotfix-side global listener carries the room
/// disconnected-marking and the session-resume notification (the business block formerly
/// inlined in the hotfix-side <c>AppStartUpHotfixGame.OnDisconnected</c>; renamed
/// <c>HotfixModuleLifecycleBridge</c> in C285). The event binding
/// key is the type itself; dispatch is global (actorId = 0) and fire-and-forget, the same
/// shape as the <see cref="SessionRemovedEventArgs"/> logout cleanup. The disconnected
/// physical session identifier is carried for the hotfix-side stale-callback guard.
/// </remarks>
public sealed class PlayerDisconnectedEventArgs : GameEventArgs
{
    /// <summary>
    /// 初始化玩家物理连接断开事件参数。
    /// </summary>
    /// <param name="playerId">断线玩家标识 / The disconnected player identifier</param>
    /// <param name="sessionId">断开的物理连接会话标识 / The disconnected physical session identifier</param>
    public PlayerDisconnectedEventArgs(long playerId, string sessionId)
    {
        PlayerId = playerId;
        SessionId = sessionId;
    }

    /// <summary>
    /// 断线玩家标识。
    /// </summary>
    /// <remarks>
    /// The disconnected player identifier.
    /// </remarks>
    public long PlayerId { get; }

    /// <summary>
    /// 断开的物理连接会话标识。
    /// </summary>
    /// <remarks>
    /// The disconnected physical session identifier.
    /// </remarks>
    public string SessionId { get; }
}
