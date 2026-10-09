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
using GameFrameX.DataBase.PostgreSql;
using GameFrameX.NetWork.RemoteMessaging.Routing;
using Npgsql;

namespace GameFrameX.NetWork.RemoteMessaging.Discovery;

/// <summary>
/// PostgreSQL 发现层激活参数对象（控制库载体为 NpgsqlDataSource）。
/// </summary>
/// <remarks>
/// Parameter object for PostgreSQL discovery activation: the same shape as
/// <c>DiscoveryActivationOptions</c> with the control-database carrier swapped
/// to <see cref="NpgsqlDataSource"/>. Set <see cref="DataSource"/> for direct
/// package consumers, or <see cref="ConnectionName"/> for launch flows that
/// resolve the control database through the unified <c>GameDb</c> entry
/// (<c>GameDb.As&lt;PostgreSqlDbService&gt;(name).DataSource</c>); when both are
/// null activation throws <see cref="ArgumentException"/>.
/// </remarks>
public sealed class PostgreSqlDiscoveryActivationOptions
{
    /// <summary>
    /// 获取或设置控制库数据源；与 <see cref="ConnectionName"/> 二选一。
    /// </summary>
    /// <remarks>
    /// Gets or sets the control-database data source; mutually exclusive with <see cref="ConnectionName"/>.
    /// </remarks>
    public NpgsqlDataSource DataSource { get; init; }

    /// <summary>
    /// 获取或设置控制库注册名（经统一入口 GameDb 解析）；与 <see cref="DataSource"/> 二选一。
    /// </summary>
    /// <remarks>
    /// Gets or sets the control-database registry name (resolved through the unified GameDb entry); mutually exclusive with <see cref="DataSource"/>.
    /// </remarks>
    public string ConnectionName { get; init; }

    /// <summary>
    /// 获取或设置本进程承载的 Role 名全集（RoleSet 快照）。
    /// </summary>
    /// <remarks>
    /// Gets or sets the full hosted role-name set (the RoleSet snapshot).
    /// </remarks>
    public IEnumerable<string> HostedRoleNames { get; init; }

    /// <summary>
    /// 获取或设置 Tier 1 玩家路由快路径提供方（apps 端 SessionManager 适配器）；null 则跳过 Tier 1。
    /// </summary>
    /// <remarks>
    /// Gets or sets the Tier 1 player-route fast-path provider (the apps-side SessionManager adapter); null skips Tier 1.
    /// </remarks>
    public IPlayerRouteFastPath PlayerRouteFastPath { get; init; }

    /// <summary>
    /// 获取或设置 TTL 清理周期（缺省 5s；测试可收缩）。
    /// </summary>
    /// <remarks>
    /// Gets or sets the TTL cleanup period (defaults to 5 s; shrinkable in tests).
    /// Drives the generic registry's cleanup loop over both stores.
    /// </remarks>
    public TimeSpan? TtlCleanupInterval { get; init; }
}

/// <summary>
/// PostgreSQL 发现层进程装配器（C167 起消费通用组件 + PostgreSQL 存储适配）。
/// </summary>
/// <remarks>
/// The process-level wiring point for the PostgreSQL discovery layer. The
/// launch flow calls <see cref="Activate"/> once the control database is
/// registered: it first ensures the heartbeat schema (PostgreSQL, unlike
/// MongoDB, never creates missing relations lazily, and the watcher's
/// immediate first poll would otherwise throw 42P01), constructs the
/// PostgreSQL stores (<see cref="PostgreSqlHeartbeatStore"/> /
/// <see cref="PostgreSqlPlayerRouteStore"/>), starts the generic
/// <see cref="DiscoveryWatcher"/> and <see cref="DiscoveryRegistry"/> (the
/// registry's cleanup loop replaces the former standalone TTL cleanup job —
/// the AC-4 equivalent of the Mongo TTL index), then attaches the generic
/// player-route bootstrap. Router wiring is NOT performed here — the
/// composition side calls
/// <c>DiscoveryRoutingWire.Initialize(RoleSet.Current, PostgreSqlDiscoveryRuntime.TableProvider)</c>
/// right after. Activation is idempotent per process; the hotfix wiring point
/// later calls <c>DiscoveryRoutingWire.AttachLocalDispatcher</c> to fill the
/// case 1 slot.
/// </remarks>
public static class PostgreSqlDiscoveryRuntime
{
    /// <summary>
    /// 装配互斥标志（首调胜出）。
    /// </summary>
    /// <remarks>
    /// The idempotence flag (first call wins).
    /// </remarks>
    private static int _activated;

    /// <summary>
    /// 已创建的心跳写侧（含 TTL 清理循环，替代原独立清理 job）。
    /// </summary>
    /// <remarks>
    /// The created heartbeat writer (owning the TTL cleanup loop that replaces the former standalone job).
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
    /// 发现层路由表提供者（C166 依赖纠偏第二轮：路由胶水装配移交组合侧 DiscoveryRoutingWire，
    /// 本 Runtime 只暴露读侧实例）。
    /// </summary>
    /// <remarks>
    /// The discovery route table provider (C166 second dependency fix: router wiring moved to the
    /// composition-side <c>DiscoveryRoutingWire</c>; this runtime only exposes the reader instance).
    /// </remarks>
    public static IRoleRouteTableProvider TableProvider
    {
        get { return _watcher; }
    }

    /// <summary>
    /// 激活 PostgreSQL 发现层（schema 保障 + 读写侧通用组件 + 玩家路由层装配）。
    /// </summary>
    /// <remarks>
    /// Activates the discovery layer. The heartbeat schema is ensured before
    /// the one-shot activation guard (a failed create can then be fully
    /// retried by a process restart instead of locking Activate into a
    /// permanent no-op). The watcher always starts; the registry starts with
    /// its write side only when an advertise identity exists (its cleanup
    /// loop always runs); the player-route bootstrap attaches before the
    /// cleanup loop's first pass can touch player_route (the schema is
    /// ensured inside the bootstrap). Calling it more than once per process
    /// is a no-op.
    /// </remarks>
    /// <param name="options">激活参数（DataSource / ConnectionName 二选一，HostedRoleNames 必填）/ Activation options (either DataSource or ConnectionName; HostedRoleNames required)</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="options"/> 或 <c>HostedRoleNames</c> 为 null 时抛出 / Thrown when options or HostedRoleNames is null</exception>
    /// <exception cref="ArgumentException">当 <c>DataSource</c> 与 <c>ConnectionName</c> 均未设置时抛出 / Thrown when neither DataSource nor ConnectionName is set</exception>
    /// <exception cref="InvalidOperationException">当注册名未注册时抛出（由 MultiDbRegistry 经 GameDb.As 抛出）/ Thrown when the connection name is not registered</exception>
    public static void Activate(PostgreSqlDiscoveryActivationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options, nameof(options));

        var dataSource = options.DataSource;
        if (dataSource == null)
        {
            if (string.IsNullOrWhiteSpace(options.ConnectionName))
            {
                throw new ArgumentException("Either DataSource or ConnectionName must be set.", nameof(options));
            }

            dataSource = GameDb.As<PostgreSqlDbService>(options.ConnectionName).DataSource;
        }

        var hostedRoleNames = options.HostedRoleNames;
        ArgumentNullException.ThrowIfNull(hostedRoleNames, nameof(options.HostedRoleNames));

        // server_heartbeat 必须先于读侧立即轮询存在（且先于一次性激活守卫——
        // 建表失败时进程重启可完整重试，不会把 Activate 永久锁成 no-op）。
        var heartbeatStore = new PostgreSqlHeartbeatStore(dataSource);
        heartbeatStore.EnsureSchemaAsync(CancellationToken.None).GetAwaiter().GetResult();

        if (Interlocked.CompareExchange(ref _activated, 1, 0) != 0)
        {
            return;
        }

        var playerRouteStore = new PostgreSqlPlayerRouteStore(dataSource);

        var hostedRoles = hostedRoleNames as IReadOnlyCollection<string> ?? hostedRoleNames.ToList();
        _watcher = new DiscoveryWatcher(heartbeatStore);
        _watcher.StartAsync(CancellationToken.None).GetAwaiter().GetResult();

        // 写侧需要唯一的广播身份：未配置广播端口（单进程本地开发等）时跳过注册，仅观察拓扑；
        // 清理循环始终运行（写侧跳过时由 Bootstrap 先行建表，避免首轮 DELETE 缺表回滚）。
        var primaryRoleName = hostedRoles.Count > 0 ? hostedRoles.First() : "unknown";
        var selfDescriptor = DiscoveryRegistry.CreateSelfDescriptorFromEnvironment(primaryRoleName);

        // 玩家路由层装配（建表 / 建索引 + 装 SyncTarget）先于 Registry 启动：清理循环首轮要求 player_route 已建。
        PlayerRouteResolverBootstrap.Attach(playerRouteStore, options.PlayerRouteFastPath).GetAwaiter().GetResult();

        _registry = new DiscoveryRegistry(heartbeatStore, playerRouteStore, selfDescriptor, null, options.TtlCleanupInterval);
        _registry.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        if (selfDescriptor == null)
        {
            LogHelper.Warning("[PostgreSqlDiscoveryRuntime] no advertise port configured ({environmentVariable}); the heartbeat write side is skipped and this process only observes the topology", AdvertiseEndpointEnvironment.AdvertisePortEnvironmentVariable);
        }
    }

    /// <summary>
    /// 把本进程心跳从 Booting 切换为 Active（启动阶段真正完成、服务就绪后调用）。
    /// </summary>
    /// <remarks>
    /// Flips this process's announced status from Booting to Active. No-op when
    /// the discovery layer was not activated or the write side was skipped.
    /// </remarks>
    public static void MarkActive()
    {
        _registry?.MarkActiveAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>
    /// 重置全部装配状态（仅测试隔离使用）。
    /// </summary>
    /// <remarks>
    /// Resets all wiring state for test isolation. Test-only: production code never calls this.
    /// </remarks>
    internal static void ResetForTest()
    {
        _activated = 0;

        // 与 Activate 相反顺序停机：读侧 → 写侧（尽力写 Stopped 终态）。
        _watcher?.Dispose();
        _watcher = null;
        _registry?.Dispose();
        _registry = null;
    }
}
