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
//   Any legal disputes and liabilities arising from secondary development based on this project
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


using GameFrameX.DataBase.Abstractions;
using GameFrameX.DataBase.Mongo.Discovery;
using GameFrameX.DataBase.PostgreSql.Discovery;
using GameFrameX.Discovery;
using GameFrameX.Foundation.Localization.Core;

namespace GameFrameX.Launcher.StartUp;

/// <summary>
/// Role 组合基类：承载与启动序列无关、但各类 Role 形态都需要的可组合件——
/// 停机幂等护栏、缺省配置收口与三段数据库/发现层装配件。
/// </summary>
/// <remarks>
/// Composable role base: hosts the pieces every role shape shares regardless of its startup
/// sequence — the idempotent stop guard, the default-setting funnel and the three database /
/// discovery wiring steps. 标准序列 Role 经 <see cref="AppStartUpGameServerBase"/> 继承本类获得全部组合件；
/// 自拟序列的异形 Role（无库网关、自拟装配顺序）直接继承本类，自行编排
/// <see cref="AppStartUpBase.StartAsync"/> 而不失去护栏与装配件——sealed 启动模板不再等于移除扩展性。
/// Standard-sequence roles inherit all of these through <see cref="AppStartUpGameServerBase"/>;
/// free-form roles derive this class directly and orchestrate their own startup without losing
/// the guard or the wiring parts — a sealed sequence template no longer means losing extensibility.
/// </remarks>
internal abstract class AppStartUpRoleBase : AppStartUpBase
{
    /// <summary>
    /// 停机幂等护栏标志位（0 = 未停机，1 = 已进入停机）。
    /// </summary>
    /// <remarks>
    /// The idempotent stop guard flag (0 = running, 1 = stopping/stopped).
    /// </remarks>
    private int _stopped;

    /// <summary>
    /// 停止 Role：幂等护栏保证首个调用生效——宿主逆序停机与 Role 自停两条路径竞争时，
    /// 只有第一次调用会执行 <see cref="OnStoppingAsync(string)"/> 与基类停机流程
    ///（停机日志、网络停机、运行时状态与退出令牌置位），后续调用直接返回。
    /// </summary>
    /// <remarks>
    /// Stops the role behind an idempotent guard: when the host's reverse-order shutdown races the
    /// role's own run-until-exit self-stop, only the first caller runs <see cref="OnStoppingAsync(string)"/>
    /// and the base stop flow (stop log, network shutdown, runtime state and exit-token completion);
    /// later calls return immediately. 双停机路径竞争是单进程多 Role 形态的固有现象
    ///（<see cref="AppEnter"/> 逆序停机与 Role 自停并发进入），护栏把两条路径收敛为恰好一次。
    /// The dual-path stop race is inherent to the single-process multi-role shape; the guard
    /// collapses both paths into exactly one stop.
    /// </remarks>
    /// <param name="message">终止原因 / Termination reason</param>
    /// <returns>表示异步停止操作的任务 / A task representing the asynchronous stop operation</returns>
    public sealed override async Task StopAsync(string message = "")
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
        {
            return;
        }

        await OnStoppingAsync(message);
        await base.StopAsync(message);
    }

    /// <summary>
    /// 停机钩子：护栏放行后、基类停机流程前回调恰好一次，默认空实现。
    /// </summary>
    /// <remarks>
    /// The stopping hook invoked exactly once after the guard admits the first stop and before the
    /// base stop flow; the default implementation is a no-op. 派生类在此挂 Role 专属停机动作
    ///（如 Social 型的 <c>ServiceOffline</c> 事件分发）。
    /// Derived classes attach role-specific stop actions here (e.g. the Social-shape
    /// <c>ServiceOffline</c> event dispatch).
    /// </remarks>
    /// <param name="message">终止原因（首个 <see cref="StopAsync(string)"/> 调用透传）/ The termination reason passed through by the first stop call</param>
    /// <returns>表示钩子操作的异步任务 / A task representing the hook operation</returns>
    protected virtual Task OnStoppingAsync(string message)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// 初始化应用程序：未注入配置时回落到 <see cref="CreateDefaultSetting"/> 的 Role 级缺省配置。
    /// </summary>
    /// <remarks>
    /// Initializes the application: falls back to the role-level defaults from
    /// <see cref="CreateDefaultSetting"/> when no setting was injected (the launcher-options
    /// copy and the config-file section both count as injected settings).
    /// </remarks>
    protected override void Init()
    {
        Setting ??= CreateDefaultSetting();
        base.Init();
    }

    /// <summary>
    /// 创建该 Role 的缺省配置（类型/端口/数据库等因 Role 而异的全部缺省值）。
    /// </summary>
    /// <remarks>
    /// Creates the role-level default settings (every per-role default: server type, port,
    /// database wiring and debug switches). 具体 Role 只需实现本成员与
    /// <see cref="StartUpTagAttribute"/> 声明。
    /// Concrete roles implement only this member plus their
    /// <see cref="StartUpTagAttribute"/> declaration.
    /// </remarks>
    /// <returns>该 Role 的缺省配置 / The role-level default settings</returns>
    protected abstract AppSetting CreateDefaultSetting();

    /// <summary>
    /// 控制库先行于业务库（D-Single 缺省回落：与业务库共用同一实例连接串），
    /// 经 GameDb 统一入口判重/命名；实现类型按 DbOptions.Provider 由 GameDb 约定解析
    ///（PG 路径下 DbOptions.Name 仅作注册名，数据库由连接串 Database 决定）。
    /// 控制库必须显式 IsDefault = false——门面默认库由业务库声明式提名（缺省 true），
    /// 漏写会在业务库注册时抛 set-once 冲突（启动期 fail-fast）。
    /// </summary>
    /// <remarks>
    /// Initializes the control database ahead of the business database (the D-Single default
    /// fallback shares the business database's connection string), registered and deduplicated
    /// through the <see cref="GameDb"/> facade with the provider implementation resolved by
    /// convention from <c>DbOptions.Provider</c>. The control database must pass
    /// <c>IsDefault = false</c> explicitly — the facade's default database is nominated
    /// declaratively by the business database (default <c>true</c>), and omitting it fails fast
    /// with a set-once conflict when the business database registers.
    /// </remarks>
    /// <returns>表示异步操作的任务 / A task representing the asynchronous operation</returns>
    protected virtual async Task InitializeControlDatabaseAsync()
    {
        // Contains 判重护栏：多 Role 进程（--AllInOne）下首个 Role 注册控制库，后续 Role 直接复用
        if (!GameDb.Contains(GameDb.ControlDatabaseName))
        {
            var controlDatabaseInitResult = await GameDb.Init(new DbOptions { Provider = Setting.DatabaseProvider, ConnectionString = Setting.DataBaseUrl, Name = GameDb.ControlDatabaseName, IsDefault = false, IsUseTimeZone = Setting.IsUseTimeZone, });
            if (controlDatabaseInitResult == false)
            {
                throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.Launcher.DatabaseServiceStartFailed));
            }
        }
    }

    /// <summary>
    /// 控制库就绪后激活发现层——读侧 watcher + 写侧心跳（未配置广播端口时自动跳过）
    /// 并以真实 case 2/3 转发器重装跨 Role 路由缝。幂等：多 Role 进程首个调用生效。
    /// 再激活玩家路由层（建 player_route 索引 + 装 SyncTarget），Tier 1 fast-path 注入 SessionManager 适配器。
    /// 控制库句柄解析下沉到发现层内部，收敛为激活参数对象。
    /// Provider 装配分支——PostgreSql 时激活 PG 平行发现层（控制库 NpgsqlDataSource 经 GameDb 按名解析）。
    /// 路由胶水装配自 Runtime 拆至组合侧 DiscoveryRoutingWire（发现层 Runtime 不再引用消息胶水程序集）。
    /// </summary>
    /// <remarks>
    /// Activates the discovery layer once the control database is ready — the read-side watcher
    /// plus the write-side heartbeat (skipped automatically when no broadcast port is configured)
    /// and the cross-role routing seam re-wired with the real case 2/3 forwarders. Idempotent: in
    /// a multi-role process the first call wins. It then activates the player-routing layer
    /// (creates the player_route index and installs the sync target) and injects the Tier 1
    /// fast-path adapter into the session manager. The provider branch activates the PostgreSQL
    /// twin discovery layer when configured; the routing glue assembly lives in the composition-side
    /// DiscoveryRoutingWire instead of the runtime.
    /// </remarks>
    protected virtual void ActivateDiscoveryLayer()
    {
        if (Setting.DatabaseProvider == DatabaseProviderType.PostgreSql)
        {
            PostgreSqlDiscoveryRuntime.Activate(new DiscoveryActivationOptions
            {
                ConnectionName = GameDb.ControlDatabaseName,
                HostedRoleNames = RoleSet.Current,
                PlayerRouteFastPath = GameFrameX.Core.Session.PlayerSessionFastPathAdapter.Instance,
            });
        }
        else
        {
            MongoDiscoveryRuntime.Activate(new DiscoveryActivationOptions
            {
                ConnectionName = GameDb.ControlDatabaseName,
                HostedRoleNames = RoleSet.Current,
                PlayerRouteFastPath = GameFrameX.Core.Session.PlayerSessionFastPathAdapter.Instance,
            });
        }

        // 两分支取值相同（ResolverBootstrap.SyncTarget），提出到分支后统一装配（等值重排）
        GameFrameX.Core.Session.PlayerSessionManager.Instance.PlayerRouteSyncTarget = GameFrameX.Discovery.Routing.PlayerRouteResolverBootstrap.SyncTarget;
        // 路由胶水装配自 Runtime 拆至组合侧 DiscoveryRoutingWire（发现层 Runtime 不再引用消息胶水程序集）
        GameFrameX.Network.RemoteMessaging.Routing.DiscoveryRoutingWire.Initialize(RoleSet.Current, Setting.DatabaseProvider == DatabaseProviderType.PostgreSql ? PostgreSqlDiscoveryRuntime.TableProvider : MongoDiscoveryRuntime.TableProvider);
    }

    /// <summary>
    /// 业务库 Init，实现类型按 DbOptions.Provider 由 GameDb 约定解析；缺省 IsDefault = true——
    /// 注册成功即声明式提名门面默认库，全部经 GameDb 门面的业务读写必须落在业务库
    ///（控制库已显式 IsDefault = false，先注册不会抢占门面）；失败抛 <see cref="InvalidOperationException"/>。
    /// 同名判重护栏：多 Role 进程共享同名业务库时首个注册生效（GameDb.Init 拒绝同名重复注册），
    /// 与控制库 <c>Contains</c> 护卫同一多 Role 语义。
    /// </summary>
    /// <remarks>
    /// Initializes the business database through the <see cref="GameDb"/> facade with the provider
    /// implementation resolved by convention; <c>IsDefault = true</c> by default means a successful
    /// registration declaratively nominates the facade's default database (the control database
    /// registered earlier with <c>IsDefault = false</c> never steals it), and a failure throws
    /// <see cref="InvalidOperationException"/>. The same-name guard makes the first registration win
    /// when roles of a multi-role process share one business database — the same multi-role
    /// semantics as the control-database <c>Contains</c> guard.
    /// </remarks>
    /// <returns>表示异步操作的任务 / A task representing the asynchronous operation</returns>
    protected virtual async Task InitializeBusinessDatabaseAsync()
    {
        if (!GameDb.Contains(Setting.DataBaseName))
        {
            var initResult = await GameDb.Init(new DbOptions { Provider = Setting.DatabaseProvider, ConnectionString = Setting.DataBaseUrl, Name = Setting.DataBaseName, IsUseTimeZone = Setting.IsUseTimeZone, });
            if (initResult == false)
            {
                throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.Launcher.DatabaseServiceStartFailed));
            }
        }
    }
}
