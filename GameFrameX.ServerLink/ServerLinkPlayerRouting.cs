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

using GameFrameX.Discovery;
using GameFrameX.Network.RemoteMessaging;
using GameFrameX.Network.RemoteMessaging.Contracts;
using GameFrameX.Network.RemoteMessaging.Routing;
using GameFrameX.Network.RemoteMessaging.Unified;

namespace GameFrameX.ServerLink;

/// <summary>
/// 玩家路由投递组合薄门面：以发现层玩家路由解析为内置默认，装配统一消息发送器与本地信封投递槽。
/// 跨服找人是合服后的刚需——玩家级投递（本服直发 / 跨服转发 / 离线策略）经
/// <see cref="UnifiedMessageSenderHolder"/> 的发送器完成，宿主无需自行拼接各组件。
/// </summary>
/// <remarks>
/// The thin composition facade for player-route delivery: it assembles the unified message sender
/// and the local envelope delivery slot with the discovery-layer player-route resolver as the
/// built-in default. Finding players across servers is essential after server merges — the
/// player-level delivery (local direct send / cross-server forward / offline strategy) is done by
/// the sender held in <see cref="UnifiedMessageSenderHolder"/>, so hosts do not wire the
/// components themselves.
/// </remarks>
public static class ServerLinkPlayerRouting
{
    /// <summary>
    /// 以发现层玩家路由解析器为内置默认，装配统一消息发送器与本地信封投递槽。
    /// 幂等装配由各 Holder/组合侧自行保证（重复调用覆盖为最新实例）。
    /// </summary>
    /// <remarks>
    /// Assembles the unified message sender and the local envelope delivery slot with the
    /// discovery-layer player-route resolver as the built-in default. Idempotence is delegated to
    /// the holders/composition side (repeated calls overwrite with the newest instances).
    /// </remarks>
    /// <param name="remoteMessageClient">远程消息客户端（建议经 <see cref="ServerLinkBuilder"/> 构建）/ The remote message client (preferably built via <see cref="ServerLinkBuilder"/>)</param>
    /// <param name="routeTableProvider">双视图路由表提供者（实例列表唯一来源）/ The dual-view route table provider (the single source of the instance list)</param>
    /// <param name="playerRouteResolver">发现层玩家路由解析器（控制库读侧）/ The discovery-layer player-route resolver (the control-database read side)</param>
    /// <param name="localPlayerSender">本服玩家发送器（本地投递出口）/ The local player sender (the local delivery exit)</param>
    /// <param name="localServerType">本服服务器类型（本服判定基准）/ The local server type (the local-server identity baseline)</param>
    /// <param name="localInstanceId">本进程发现层实例 Id（本服判定基准）/ This process's discovery instance id (the local-server identity baseline)</param>
    /// <returns>装配完成的统一消息发送器（同 <see cref="UnifiedMessageSenderHolder"/> 当前实例）/ The assembled unified message sender (the current instance of <see cref="UnifiedMessageSenderHolder"/>)</returns>
    /// <exception cref="ArgumentNullException">当 remoteMessageClient、routeTableProvider、playerRouteResolver 或 localPlayerSender 为 null 时抛出 / Thrown when remoteMessageClient, routeTableProvider, playerRouteResolver, or localPlayerSender is null</exception>
    public static IUnifiedMessageSender ComposeUnifiedSender(
        IRemoteMessageClient remoteMessageClient,
        IRoleRouteTableProvider routeTableProvider,
        GameFrameX.Discovery.Routing.IPlayerRouteResolver playerRouteResolver,
        IPlayerLocalSender localPlayerSender,
        string localServerType,
        string localInstanceId)
    {
        ArgumentNullException.ThrowIfNull(remoteMessageClient);
        ArgumentNullException.ThrowIfNull(routeTableProvider);
        ArgumentNullException.ThrowIfNull(playerRouteResolver);
        ArgumentNullException.ThrowIfNull(localPlayerSender);

        var routeResolver = CreateRouteResolver(playerRouteResolver);
        UnifiedMessageSenderHolder.InitializeWithDefaults(
            routeResolver,
            localPlayerSender,
            remoteMessageClient,
            routeTableProvider,
            localServerType,
            localInstanceId);

        // 本地信封投递槽与发送器共用同一 localSender，保证一条本地复投语义；
        // 仅在路由缝已由启动流程初始化后补装（未初始化时跳过，由启动流程装配各自的投递槽）
        if (DiscoveryRoutingWire.IsInitialized)
        {
            DiscoveryRoutingWire.AttachLocalDispatcher(new LocalEnvelopeDispatcher(localPlayerSender));
        }

        return UnifiedMessageSenderHolder.Sender;
    }

    /// <summary>
    /// 把发现层玩家路由解析器适配为 Unified 投递契约（在线路由 → Online，离线路由 → Offline，路由缺失 → null）。
    /// </summary>
    /// <remarks>
    /// Adapts a discovery-layer player-route resolver into the Unified delivery contract (online
    /// routes become Online, offline routes become Offline, a missing route becomes null).
    /// </remarks>
    /// <param name="playerRouteResolver">发现层玩家路由解析器 / The discovery-layer player-route resolver</param>
    /// <returns>Unified 契约的玩家路由解析器 / The Unified-contract player-route resolver</returns>
    /// <exception cref="ArgumentNullException">当 playerRouteResolver 为 null 时抛出 / Thrown when playerRouteResolver is null</exception>
    public static IPlayerRouteResolver CreateRouteResolver(GameFrameX.Discovery.Routing.IPlayerRouteResolver playerRouteResolver)
    {
        ArgumentNullException.ThrowIfNull(playerRouteResolver);

        return new RoutingPlayerRouteResolverAdapter(playerRouteResolver);
    }
}
