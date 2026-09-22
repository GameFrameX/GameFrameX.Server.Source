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


using GameFrameX.NetWork.RemoteMessaging.Routing;
using MongoDB.Driver;

namespace GameFrameX.NetWork.RemoteMessaging.Discovery;

/// <summary>
/// Mongo 发现层进程装配器（C143d D11–D15 落地接线）。
/// </summary>
/// <remarks>
/// The process-level wiring point for the Mongo discovery layer (C143d).
/// The launch flow calls <see cref="Activate"/> once the control database
/// (gameframex_control) is registered in MultiDbRegistry: it starts the watcher
/// (read side), starts the registry (write side — skipped when no advertise port
/// is configured, e.g. single-process local development), and re-installs
/// <see cref="RoleRouterHolder"/> with the real case 2/3 remote router in place of
/// the C143c placeholder. Activation is idempotent per process: the first call
/// wins, later calls (one per hosted role startup in a multi-role process) return
/// immediately. The local dispatcher stays null until C143e, per the C143c seam plan.
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
    /// 激活 Mongo 发现层并重装跨 Role 路由缝。
    /// </summary>
    /// <remarks>
    /// Activates the discovery layer. The watcher always starts (every process
    /// observes the topology); the registry starts only when a advertise identity
    /// exists (advertise port configured). <see cref="RoleRouterHolder"/> is then
    /// re-initialized over the hosted role names with the real remote router.
    /// Call this after the control database is registered; calling it more than
    /// once per process is a no-op.
    /// </remarks>
    /// <param name="controlDatabase">控制库（gameframex_control）/ The control database</param>
    /// <param name="hostedRoleNames">本进程承载的 Role 名全集（RoleSet 快照）/ The full hosted role-name set (the RoleSet snapshot)</param>
    /// <param name="playerRouteFastPath">Tier 1 玩家路由快路径提供方（apps 端 SessionManager 适配器；null 则跳过 Tier 1）/ Tier 1 fast path; null skips Tier 1</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="controlDatabase"/> 或 <paramref name="hostedRoleNames"/> 为 null 时抛出 / Thrown when controlDatabase or hostedRoleNames is null</exception>
    public static void Activate(IMongoDatabase controlDatabase, IEnumerable<string> hostedRoleNames, IPlayerRouteFastPath playerRouteFastPath = null)
    {
        ArgumentNullException.ThrowIfNull(controlDatabase, nameof(controlDatabase));
        ArgumentNullException.ThrowIfNull(hostedRoleNames, nameof(hostedRoleNames));

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

        RoleRouterHolder.Initialize(new InProcessRoleRouter(hostedRoles, null, new MongoDiscoveryRemoteRoleRouter(_watcher, new TcpEnvelopeForwarder())));
        // C143e D21：玩家路由层装配（建索引 + 装 SyncTarget）。在路由缝激活后追加；
        // 接收端 envelope 复投由 LocalEnvelopeDispatcher 承担，LocalEnvelopeDispatcher 在装配流程末尾（GameApp）按需注入，本类不直接重装 RoleRouterHolder。
        MongoPlayerRouteResolverBootstrap.Attach(controlDatabase, playerRouteFastPath).GetAwaiter().GetResult();
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
