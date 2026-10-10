// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
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
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository:  https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:   https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:     https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using GameFrameX.DataBase;
using GameFrameX.DataBase.Mongo;
using GameFrameX.DataBase.Mongo.Routing;
using GameFrameX.Discovery.Routing;
using GameFrameX.Foundation.Localization.Core;
using MongoDB.Driver;

namespace GameFrameX.DataBase.Mongo.Discovery;

/// <summary>
/// Mongo 发现层进程装配器（消费通用组件 + Mongo 存储适配）。
/// </summary>
/// <remarks>
/// The process-level wiring point for the Mongo discovery layer. The launch
/// flow calls <c>Activate(DiscoveryActivationOptions)</c> once the control
/// database (gameframex_control) is registered in MultiDbRegistry: it
/// constructs the Mongo stores (<see cref="MongoHeartbeatStore"/> /
/// <see cref="MongoPlayerRouteStore"/>), starts the generic
/// <see cref="DiscoveryWatcher"/> (read side) and the generic
/// <see cref="DiscoveryRegistry"/> (write side + TTL cleanup loop — skipped
/// for the write side when no advertise port is configured, e.g.
/// single-process local development), and attaches the generic player-route
/// bootstrap. Router wiring is NOT performed here — the composition side
/// calls <c>DiscoveryRoutingWire.Initialize(RoleSet.Current, MongoDiscoveryRuntime.TableProvider)</c>
/// right after. Activation is idempotent per process: the first call wins. The
/// started registry is published to the process-wide <see cref="ActiveDiscoveryRuntime"/>
/// slot so provider-agnostic startup flows flip Booting → Active without
/// provider dispatch.
/// The local dispatcher slot stays null here on purpose: the hotfix wiring
/// point later calls <c>AttachLocalDispatcher</c> to fill the case 1 slot.
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
    private static DiscoveryRegistry _registry;

    /// <summary>
    /// 已创建的心跳读侧。
    /// </summary>
    /// <remarks>
    /// The created heartbeat reader.
    /// </remarks>
    private static DiscoveryWatcher _watcher;

    /// <summary>
    /// 发现层路由表提供者（路由胶水装配移交组合侧 DiscoveryRoutingWire，本 Runtime 只暴露读侧实例）。
    /// </summary>
    /// <remarks>
    /// The discovery route table provider (router wiring moved to the
    /// composition-side <c>DiscoveryRoutingWire</c>; this runtime only exposes the reader instance).
    /// </remarks>
    public static IRoleRouteTableProvider TableProvider => _watcher;

    /// <summary>
    /// 激活 Mongo 发现层（读侧 watcher + 写侧心跳 + 玩家路由层装配）。
    /// </summary>
    /// <remarks>
    /// Activates the discovery layer. The watcher always starts (every process
    /// observes the topology); the registry's write side starts only when an
    /// advertise identity exists (advertise port configured); the player-route
    /// bootstrap attaches right after. Calling it more than once
    /// per process is a no-op. The control database is resolved through the
    /// unified <c>GameDb</c> entry by <see cref="DiscoveryActivationOptions.ConnectionName"/>
    /// (the carrier direct-injection path was removed in C185).
    /// </remarks>
    /// <param name="options">激活参数（ConnectionName 必填，HostedRoleNames 必填）/ Activation options (ConnectionName and HostedRoleNames required)</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="options"/> 或 <c>HostedRoleNames</c> 为 null 时抛出 / Thrown when options or HostedRoleNames is null</exception>
    /// <exception cref="ArgumentException">当 <c>ConnectionName</c> 未设置时抛出 / Thrown when ConnectionName is not set</exception>
    /// <exception cref="InvalidOperationException">当注册名未注册时抛出（由 MultiDbRegistry 经 GameDb.As 抛出）/ Thrown when the connection name is not registered (raised by MultiDbRegistry via GameDb.As)</exception>
    public static void Activate(DiscoveryActivationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options, nameof(options));

        if (string.IsNullOrWhiteSpace(options.ConnectionName))
        {
            // Localization: Database.Discovery.ConnectionNameRequired - 必须设置 ConnectionName。
            throw new ArgumentException(LocalizationService.GetString(Localization.Keys.Database.DiscoveryConnectionNameRequired), nameof(options));
        }

        var controlDatabase = GameDb.As<MongoDbService>(options.ConnectionName).CurrentDatabase;

        var hostedRoleNames = options.HostedRoleNames;
        ArgumentNullException.ThrowIfNull(hostedRoleNames, nameof(options.HostedRoleNames));

        if (Interlocked.CompareExchange(ref _activated, 1, 0) != 0)
        {
            return;
        }

        var heartbeatStore = new MongoHeartbeatStore(controlDatabase);
        var playerRouteStore = new MongoPlayerRouteStore(controlDatabase);

        var hostedRoles = hostedRoleNames as IReadOnlyCollection<string> ?? hostedRoleNames.ToList();
        _watcher = new DiscoveryWatcher(heartbeatStore);
        _watcher.StartAsync(CancellationToken.None).GetAwaiter().GetResult();

        // 写侧需要唯一的广播身份：未配置广播端口（单进程本地开发等）时跳过注册，仅观察拓扑。
        // ponytail: 心跳文档是单 Role 模型，多 Role 进程只广播首选 Role——目标形态（compose 单 Role 服务）下
        // 多 Role 进程仅剩 AllInOne 开发形态，其路由全走本地 case 1，无跨进程发现需求；若未来多 Role 常态化
        // 再扩展为每 Role 一份心跳文档。
        var primaryRoleName = hostedRoles.Count > 0 ? hostedRoles.First() : "unknown";
        var selfDescriptor = DiscoveryRegistry.CreateSelfDescriptorFromEnvironment(primaryRoleName);
        _registry = new DiscoveryRegistry(heartbeatStore, playerRouteStore, selfDescriptor, null, options.TtlCleanupInterval);
        _registry.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        // 激活完成即登记公共槽位：宿主就绪时经 ActiveDiscoveryRuntime.MarkActiveAsync 无分派切 Active。
        ActiveDiscoveryRuntime.Bind(_registry);
        if (selfDescriptor == null)
        {
            // Localization: Database.Mongo.DiscoveryNoAdvertisePort - [MongoDiscoveryRuntime] 未配置广播端口（{0}）；心跳写入侧被跳过，本进程仅观察拓扑
            LogHelper.Warning(LocalizationService.GetString(Localization.Keys.Database.Mongo.DiscoveryNoAdvertisePort, AdvertiseEndpointEnvironment.AdvertisePortEnvironmentVariable));
        }

        // 玩家路由层装配（建索引 + 装 SyncTarget），通用 Bootstrap 单例。
        PlayerRouteResolverBootstrap.Attach(playerRouteStore, options.PlayerRouteFastPath).GetAwaiter().GetResult();
    }
}
