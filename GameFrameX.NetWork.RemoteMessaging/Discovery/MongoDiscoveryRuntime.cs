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


using GameFrameX.DataBase;
using GameFrameX.DataBase.Mongo;
using GameFrameX.NetWork.RemoteMessaging.Routing;
using MongoDB.Driver;

namespace GameFrameX.NetWork.RemoteMessaging.Discovery;

/// <summary>
/// Mongo 发现层进程装配器（C143d D11–D15 落地接线）。
/// </summary>
/// <remarks>
/// The process-level wiring point for the Mongo discovery layer (C143d).
/// The launch flow calls <see cref="Activate(IMongoDatabase, IEnumerable{string}, IPlayerRouteFastPath)"/> once the control database
/// (gameframex_control) is registered in MultiDbRegistry: it starts the watcher
/// (read side), starts the registry (write side — skipped when no advertise port
/// is configured, e.g. single-process local development), and re-installs
/// <see cref="RoleRouterHolder"/> with the real case 2/3 remote router in place of
/// the C143c placeholder. Activation is idempotent per process: the first call
/// wins, later calls (one per hosted role startup in a multi-role process) return
/// immediately. The local dispatcher slot stays null here on purpose: the
/// Hotfix-backed dispatcher only exists after the hotfix module loads, so the
/// hotfix wiring point later calls <see cref="AttachLocalDispatcher"/> (C152) to
/// fill the case 1 slot without touching the remote chain.
/// </remarks>
public static class MongoDiscoveryRuntime
{
    /// <summary>
    /// 装配互斥标志（首调胜出）。
    /// </summary>
    /// <remarks>
    /// The idempotence flag (first call wins).
    /// </remarks>
    private static int _activated;

    /// <summary>
    /// 已创建的心跳写侧（进程生命周期持有，终态 Stopped 由其自身退出钩子负责）。
    /// </summary>
    /// <remarks>
    /// The created heartbeat writer, held for the process lifetime; its exit hooks own the terminal Stopped write.
    /// </remarks>
    private static MongoEndpointRegistry _registry;

    /// <summary>
    /// 已创建的心跳读侧。
    /// </summary>
    /// <remarks>
    /// The created heartbeat reader.
    /// </remarks>
    private static MongoEndpointWatcher _watcher;

    /// <summary>
    /// Activate 时捕获的本进程 Role 名快照（供 <see cref="AttachLocalDispatcher"/> 重建路由器）。
    /// </summary>
    /// <remarks>
    /// The hosted-role snapshot captured by <see cref="Activate(IMongoDatabase, IEnumerable{string}, IPlayerRouteFastPath)"/>; reused by
    /// <see cref="AttachLocalDispatcher"/> when rebuilding the router.
    /// </remarks>
    private static IReadOnlyCollection<string> _hostedRoles;

    /// <summary>
    /// Activate 时创建的远程转发器（挂接时原样复用，保证不动远程链）。
    /// </summary>
    /// <remarks>
    /// The remote router created by <see cref="Activate(DiscoveryActivationOptions)"/>; <see cref="AttachLocalDispatcher"/>
    /// reuses the exact instance so only the local slot ever changes.
    /// </remarks>
    private static IRemoteRoleRouter _remoteRouter;

    /// <summary>
    /// 本地投递缝挂接互斥标志（首调胜出）。
    /// </summary>
    /// <remarks>
    /// The local-dispatcher attach idempotence flag (first call wins).
    /// </remarks>
    private static int _localDispatcherAttached;

    /// <summary>
    /// 激活 Mongo 发现层并重装跨 Role 路由缝。
    /// </summary>
    /// <remarks>
    /// Activates the discovery layer. The watcher always starts (every process
    /// observes the topology); the registry starts only when a advertise identity
    /// exists (advertise port configured). <see cref="RoleRouterHolder"/> is then
    /// re-initialized over the hosted role names with the real remote router.
    /// Call this after the control database is registered; calling it more than
    /// once per process is a no-op. The control database comes from
    /// <see cref="DiscoveryActivationOptions.ControlDatabase"/> directly, or is
    /// resolved through the unified <c>GameDb</c> entry when only
    /// <see cref="DiscoveryActivationOptions.ConnectionName"/> is set (the C159
    /// name-based overload folded into this options shape by C154).
    /// </remarks>
    /// <param name="options">激活参数（ControlDatabase / ConnectionName 二选一，HostedRoleNames 必填）/ Activation options (either ControlDatabase or ConnectionName; HostedRoleNames required)</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="options"/> 或 <c>HostedRoleNames</c> 为 null 时抛出 / Thrown when options or HostedRoleNames is null</exception>
    /// <exception cref="ArgumentException">当 <c>ControlDatabase</c> 与 <c>ConnectionName</c> 均未设置时抛出 / Thrown when neither ControlDatabase nor ConnectionName is set</exception>
    /// <exception cref="InvalidOperationException">当注册名未注册时抛出（由 MultiDbRegistry 经 GameDb.As 抛出）/ Thrown when the connection name is not registered (raised by MultiDbRegistry via GameDb.As)</exception>
    public static void Activate(DiscoveryActivationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options, nameof(options));

        var controlDatabase = options.ControlDatabase;
        if (controlDatabase == null)
        {
            if (string.IsNullOrWhiteSpace(options.ConnectionName))
            {
                throw new ArgumentException("Either ControlDatabase or ConnectionName must be set.", nameof(options));
            }

            controlDatabase = GameDb.As<MongoDbService>(options.ConnectionName).CurrentDatabase;
        }

        var hostedRoleNames = options.HostedRoleNames;
        ArgumentNullException.ThrowIfNull(hostedRoleNames, nameof(options.HostedRoleNames));

        if (Interlocked.CompareExchange(ref _activated, 1, 0) != 0)
        {
            return;
        }

        var hostedRoles = hostedRoleNames as IReadOnlyCollection<string> ?? hostedRoleNames.ToList();
        _watcher = new MongoEndpointWatcher(controlDatabase);
        _watcher.StartAsync(CancellationToken.None).GetAwaiter().GetResult();

        // 写侧需要唯一的广播身份：未配置广播端口（单进程本地开发等）时跳过注册，仅观察拓扑。
        // ponytail: 心跳文档是单 Role 模型，多 Role 进程只广播首选 Role——目标形态（D13 compose 单 Role 服务）下
        // 多 Role 进程仅剩 AllInOne 开发形态，其路由全走本地 case 1，无跨进程发现需求；若未来多 Role 常态化
        // 再扩展为每 Role 一份心跳文档。
        var primaryRoleName = hostedRoles.Count > 0 ? hostedRoles.First() : "unknown";
        var selfDescriptor = MongoEndpointRegistry.CreateSelfDescriptorFromEnvironment(primaryRoleName);
        if (selfDescriptor != null)
        {
            _registry = new MongoEndpointRegistry(controlDatabase, selfDescriptor);
            _registry.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        else
        {
            LogHelper.Warning("[MongoDiscoveryRuntime] no advertise port configured ({environmentVariable}); the heartbeat write side is skipped and this process only observes the topology", MongoEndpointRegistry.AdvertisePortEnvironmentVariable);
        }

        _hostedRoles = hostedRoles;
        _remoteRouter = new MongoDiscoveryRemoteRoleRouter(_watcher, new TcpEnvelopeForwarder());
        RoleRouterHolder.Initialize(new InProcessRoleRouter(hostedRoles, null, _remoteRouter));
        // C143e D21：玩家路由层装配（建索引 + 装 SyncTarget）。在路由缝激活后追加；
        // 接收端 envelope 复投由 LocalEnvelopeDispatcher 承担：C152 起 Hotfix 装配点在热更加载后
        // 经 AttachLocalDispatcher 补装 local 槽（发现层装配时 Hotfix 组件尚不存在）。
        MongoPlayerRouteResolverBootstrap.Attach(controlDatabase, options.PlayerRouteFastPath).GetAwaiter().GetResult();
    }

    /// <summary>
    /// 补装本地 envelope 投递缝（C152：只补 case 1 的 local 槽，不动远程链）。
    /// </summary>
    /// <remarks>
    /// Rebuilds the process router with the given dispatcher in the case 1 slot,
    /// reusing the hosted-role snapshot and the exact remote router instance
    /// created by <see cref="Activate(DiscoveryActivationOptions)"/> — the remote chain (watcher, forwarder,
    /// heartbeat registry) is untouched. Timing premise: the production caller is
    /// the Hotfix <c>OnLoadSuccess</c> wiring point, which the launch flow orders
    /// strictly after <see cref="Activate(DiscoveryActivationOptions)"/> (the hotfix module loads later in the
    /// same startup sequence, when the Hotfix-side sender components finally
    /// exist); a call before <see cref="Activate(DiscoveryActivationOptions)"/> therefore throws instead of
    /// quietly degrading case 1 back to route-time <see cref="RouteNotFoundException"/>.
    /// Idempotent per process: the first call wins, later calls (multi-role
    /// re-entry) return immediately without rebuilding the router.
    /// </remarks>
    /// <param name="dispatcher">本地 envelope 复投器（Hotfix 装配点传入，与 UnifiedMessageSenderHolder 共用同一 IPlayerLocalSender）/ The local envelope dispatcher (sharing the same IPlayerLocalSender as UnifiedMessageSenderHolder)</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="dispatcher"/> 为 null 时抛出 / Thrown when dispatcher is null</exception>
    /// <exception cref="InvalidOperationException">当尚未调用 <see cref="Activate(DiscoveryActivationOptions)"/> 时抛出 / Thrown when Activate has not been called</exception>
    public static void AttachLocalDispatcher(ILocalRoleMessageDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(dispatcher, nameof(dispatcher));

        if (_hostedRoles == null)
        {
            throw new InvalidOperationException("MongoDiscoveryRuntime.AttachLocalDispatcher must be called after Activate; the router rebuild needs the hosted-role snapshot and the remote router created by Activate.");
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
    /// Resets all wiring state for test isolation (precedent:
    /// <c>MailCampaignRegistry.ResetForTest</c>). Test-only: production code never calls this.
    /// </remarks>
    internal static void ResetForTest()
    {
        _activated = 0;
        _localDispatcherAttached = 0;
        _registry = null;
        _watcher = null;
        _hostedRoles = null;
        _remoteRouter = null;
    }

    /// <summary>
    /// 模拟已 Activate 的装配状态（仅测试使用；不创建 watcher / registry 等真实 Mongo 资源）。
    /// </summary>
    /// <remarks>
    /// Simulates the post-Activate state without real Mongo resources so unit tests
    /// can exercise <see cref="AttachLocalDispatcher"/> semantics (attach, idempotence,
    /// remote-chain preservation) on top of stub components.
    /// </remarks>
    /// <param name="hostedRoleNames">模拟的本进程 Role 名快照 / The simulated hosted-role snapshot</param>
    /// <param name="remoteRouter">模拟的远程转发缝 / The simulated remote forwarding seam</param>
    internal static void SimulateActivatedForTest(IReadOnlyCollection<string> hostedRoleNames, IRemoteRoleRouter remoteRouter)
    {
        _hostedRoles = hostedRoleNames;
        _remoteRouter = remoteRouter;
    }

    /// <summary>
    /// 把本进程心跳从 Booting 切换为 Active（启动阶段真正完成、服务就绪后调用）。
    /// </summary>
    /// <remarks>
    /// Flips this process's announced status from Booting to Active. The startup
    /// flows call this right after their readiness point (<c>MarkStartUpReady</c>:
    /// databases, components, and listeners up) so other processes never discover
    /// and route traffic to a not-yet-ready instance. No-op when the discovery
    /// layer was not activated or the write side was skipped (no advertise identity).
    /// </remarks>
    public static void MarkActive()
    {
        _registry?.MarkActiveAsync(CancellationToken.None).GetAwaiter().GetResult();
    }
}
