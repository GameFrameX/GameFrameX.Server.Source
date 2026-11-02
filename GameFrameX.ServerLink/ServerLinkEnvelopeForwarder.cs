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
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes and liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using GameFrameX.Network.RemoteMessaging.Routing;

namespace GameFrameX.ServerLink;

/// <summary>
/// 服务器间信封转发薄门面：把一条跨 Role 消息交给进程级路由缝
/// （本进程 Role 走本地投递，跨进程跳由路由器打包 <see cref="RoleRouteEnvelopeMessage"/> 经共享传输池转发）。
/// </summary>
/// <remarks>
/// The thin envelope-forwarding facade for inter-server messaging: it hands one cross-role message
/// to the process-wide routing seam (roles hosted by this process go to local in-process delivery;
/// remote hops are packed as a <see cref="RoleRouteEnvelopeMessage"/> by the router and forwarded
/// over the shared transport pool).
/// </remarks>
public static class ServerLinkEnvelopeForwarder
{
    /// <summary>
    /// 路由缝是否已完成装配（启动流程初始化 <c>RoleRouterHolder</c> 后为 true）。
    /// </summary>
    /// <remarks>
    /// Whether the routing seam has been assembled (true after the launch flow initializes
    /// <c>RoleRouterHolder</c>).
    /// </remarks>
    /// <value>是否已装配 / Whether the seam is initialized</value>
    public static bool IsRoutingInitialized
    {
        get { return RoleRouterHolder.IsInitialized; }
    }

    /// <summary>
    /// 转发一封跨 Role 消息信封：目标 Role 由本进程承载时走本地投递，否则经远程转发（打包 RoleRouteEnvelopeMessage）。
    /// </summary>
    /// <remarks>
    /// Forwards one cross-role message envelope: when the target role is hosted by this process the
    /// message goes to local in-process delivery; otherwise it is forwarded remotely (packed as a
    /// RoleRouteEnvelopeMessage).
    /// </remarks>
    /// <param name="targetRole">目标 Role 的服务器类型名 / The target role server type name</param>
    /// <param name="message">要转发的消息 / The message to forward</param>
    /// <param name="targetActorId">本地投递目标 ActorId（跨进程跳时忽略）/ The local delivery target actor id (ignored on remote hops)</param>
    /// <param name="targetInstanceId">可选的目标实例 Id（非空定向已知实例，空取任意活跃实例）/ Optional target instance id (non-null selects a known instance, null selects any active instance)</param>
    /// <param name="cancellationToken">取消操作的令牌 / The cancellation token</param>
    /// <returns>实际命中的投递分支 / The delivery branch that was actually hit</returns>
    /// <exception cref="ArgumentNullException">当 message 为 null 时抛出 / Thrown when message is null</exception>
    /// <exception cref="InvalidOperationException">当路由缝尚未装配时抛出 / Thrown when the routing seam is not initialized</exception>
    public static Task<RoleRouteDelivery> ForwardAsync(string targetRole, GameFrameX.Network.Messages.MessageObject message, long targetActorId = 0, string targetInstanceId = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var envelope = new MessageEnvelope(targetRole, message, targetActorId, targetInstanceId);
        return RoleRouterHolder.Current.RouteAsync(envelope, cancellationToken);
    }
}
