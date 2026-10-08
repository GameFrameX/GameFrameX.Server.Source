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
//   please refer to the LICENSE file at the root of the source code for the full license text.
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
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


namespace GameFrameX.NetWork.RemoteMessaging.Routing;

/// <summary>
/// 发现层路由胶水装配器（C166 依赖方向裁定第二轮：自 *DiscoveryRuntime 拆出的组合侧装配）。
/// </summary>
/// <remarks>
/// Composition-side wiring for the discovery routing fabric (C166 second dependency-direction ruling:
/// extracted from the database-implementation <c>*DiscoveryRuntime</c> so that neither the database
/// implementation assemblies nor RemoteMessaging reference each other). The launch flow calls
/// <see cref="Initialize"/> right after the provider runtime's <c>Activate</c> (passing its exposed
/// <c>IRoleRouteTableProvider</c>); the Hotfix wiring point later calls
/// <see cref="AttachLocalDispatcher"/> to fill the case-1 local slot (C152 semantics preserved verbatim:
/// pre-Initialize call throws, idempotent per process, remote chain untouched).
/// </remarks>
public static class DiscoveryRoutingWire
{
    private static IReadOnlyCollection<string> _hostedRoles;
    private static IRemoteRoleRouter _remoteRouter;
    private static int _initialized;
    private static int _localDispatcherAttached;

    /// <summary>
    /// 初始化路由缝：以发现层路由表装配跨 Role 路由器（case 2/3 真实转发器，替换占位）。
    /// </summary>
    /// <remarks>
    /// Initializes the routing seam with the discovery route table (the real case 2/3 forwarder in place
    /// of the placeholder). Single activation per process — a second call is a no-op (mirroring the
    /// provider runtime's <c>Activate</c> guard semantics for multi-role re-entry).
    /// </remarks>
    /// <param name="hostedRoleNames">本进程承载的 Role 名集合（构造期快照）/ Hosted role names (construct-time snapshot)</param>
    /// <param name="tableProvider">发现层路由表提供者（各 *DiscoveryRuntime.TableProvider）/ The discovery route table provider</param>
    public static void Initialize(IReadOnlyCollection<string> hostedRoleNames, IRoleRouteTableProvider tableProvider)
    {
        ArgumentNullException.ThrowIfNull(hostedRoleNames, nameof(hostedRoleNames));
        ArgumentNullException.ThrowIfNull(tableProvider, nameof(tableProvider));

        if (Interlocked.CompareExchange(ref _initialized, 1, 0) != 0)
        {
            return;
        }

        _hostedRoles = hostedRoleNames;
        _remoteRouter = new DiscoveryRemoteRoleRouter(tableProvider, new TcpEnvelopeForwarder());
        RoleRouterHolder.Initialize(new InProcessRoleRouter(hostedRoleNames, null, _remoteRouter));
    }

    /// <summary>
    /// 补装本地 envelope 投递缝（C152：只补 case 1 的 local 槽，不动远程链）。
    /// </summary>
    /// <remarks>
    /// Rebuilds the process router with the given dispatcher in the case 1 slot, reusing the
    /// hosted-role snapshot and the exact remote router instance created by <see cref="Initialize"/> —
    /// the remote chain (watcher, forwarder, heartbeat registry) is untouched. Timing premise: the
    /// production caller is the Hotfix <c>OnLoadSuccess</c> wiring point, which the launch flow orders
    /// strictly after <see cref="Initialize"/> (the hotfix module loads later in the same startup
    /// sequence, when the Hotfix-side sender components finally exist); a call before
    /// <see cref="Initialize"/> therefore throws instead of quietly degrading case 1 back to
    /// route-time <see cref="RouteNotFoundException"/>. Idempotent per process: the first call wins,
    /// later calls (multi-role re-entry) return immediately without rebuilding the router.
    /// </remarks>
    /// <param name="dispatcher">本地 envelope 复投器（Hotfix 装配点传入，与 UnifiedMessageSenderHolder 共用同一 IPlayerLocalSender）/ The local envelope dispatcher (sharing the same IPlayerLocalSender as UnifiedMessageSenderHolder)</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="dispatcher"/> 为 null 时抛出 / Thrown when dispatcher is null</exception>
    /// <exception cref="InvalidOperationException">当尚未调用 <see cref="Initialize"/> 时抛出 / Thrown when Initialize has not been called</exception>
    public static void AttachLocalDispatcher(ILocalRoleMessageDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher, nameof(dispatcher));

        if (_hostedRoles == null)
        {
            throw new InvalidOperationException("DiscoveryRoutingWire.AttachLocalDispatcher must be called after Initialize; the router rebuild needs the hosted-role snapshot and the remote router created by Initialize.");
        }

        if (Interlocked.CompareExchange(ref _localDispatcherAttached, 1, 0) != 0)
        {
            return;
        }

        RoleRouterHolder.Initialize(new InProcessRoleRouter(_hostedRoles, dispatcher, _remoteRouter));
    }

    /// <summary>
    /// 重置全部装配状态（仅测试隔离使用）。
    /// </summary>
    /// <remarks>
    /// Resets all wiring state for test isolation. Test-only: production code never calls this.
    /// </remarks>
    internal static void ResetForTest()
    {
        _initialized = 0;
        _localDispatcherAttached = 0;
        _hostedRoles = null;
        _remoteRouter = null;
    }

    /// <summary>
    /// 模拟已 Initialize 的装配状态（仅测试使用；不创建真实转发资源）。
    /// </summary>
    /// <remarks>
    /// Simulates the post-Initialize state without real resources so unit tests can exercise
    /// <see cref="AttachLocalDispatcher"/> semantics (attach, idempotence, remote-chain preservation)
    /// on top of stub components.
    /// </remarks>
    /// <param name="hostedRoleNames">模拟的本进程 Role 名快照 / The simulated hosted-role snapshot</param>
    /// <param name="remoteRouter">模拟的远程转发缝 / The simulated remote forwarding seam</param>
    internal static void SimulateInitializedForTest(IReadOnlyCollection<string> hostedRoleNames, IRemoteRoleRouter remoteRouter)
    {
        _initialized = 1;
        _hostedRoles = hostedRoleNames;
        _remoteRouter = remoteRouter;
    }
}