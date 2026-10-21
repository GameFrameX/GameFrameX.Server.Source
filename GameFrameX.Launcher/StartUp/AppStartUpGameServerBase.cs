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


using GameFrameX.Apps.Common.EventData;
using GameFrameX.Core.Events;
using GameFrameX.DataBase.Abstractions;
using GameFrameX.Discovery;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Foundation.Utility;
using GameFrameX.Network.Abstractions;
using GameFrameX.Network.HTTP;
using GameFrameX.Network.Message;

namespace GameFrameX.Launcher.StartUp;

/// <summary>
/// 游戏服务器启动模板：固化「控制库 → 发现层 → 业务库 → 组件注册 → Role 服务」共享启动序列
/// 与退出兜底，并承载与启动序列无关的组合件——停机幂等护栏、缺省配置收口与三段数据库/发现层装配件；
/// 差异点全部经 hook 下放（<see cref="PrepareAsync"/> / <see cref="StartRoleServicesAsync"/> /
/// <see cref="OnStartupCompletedAsync"/> / <see cref="ShutdownAsync(string)"/>）。
/// </summary>
/// <remarks>
/// The single-layer game-server startup template: freezes the shared startup sequence
/// (control database → discovery layer → business database → component registration → role services)
/// together with the exit catch-all, and hosts the sequence-independent composable parts directly —
/// the idempotent stop guard, the default-setting funnel and the three database / discovery wiring
/// steps — pushing every role difference down to hooks. 序列方法 sealed 是刻意选择：
/// 防止装配序列在派生类再分叉；自拟序列的异形 Role（无库网关、自拟装配顺序）直承
/// <see cref="AppStartUpBase"/> 自行编排启动序列——护栏、缺省配置收口与装配件届时按需重新抽取
///（机械操作，非单向门）。
/// Sealing the sequence is deliberate — it prevents the assembly sequence from forking again in
/// derived classes; free-form roles (library-less gateways, custom assembly orders) derive
/// <see cref="AppStartUpBase"/> directly and orchestrate their own startup — the guard, the funnel
/// and the wiring parts would be re-extracted on demand at that point (a mechanical move, not a
/// one-way door).
/// 单进程多 Role（--AllInOne / 复数 --ServerType）与多进程单 Role 两形态兼容：共享装配步骤幂等
///（控制库/业务库 Contains 守卫、发现层 Activate 一次性守卫、默认库 set-once），
/// <see cref="AppStartUpBase.MarkStartUpReady"/> 屏障保持在各 Role 的 DB/组件/Role 服务全就绪之后
///（优先级链启动语义不变）；多进程形态下单 Role 进程的屏障置位无人等待、无副作用。
/// Compatible with both the single-process multi-role shape (--AllInOne / multiple --ServerType)
/// and the multi-process single-role shape: the shared wiring steps stay idempotent, the
/// startup-ready barrier is still set only after each role's databases, components and role
/// services are all up (the priority-chain startup semantics are unchanged), and in the
/// multi-process shape nobody awaits the barrier, which is harmless.
/// </remarks>
internal abstract class AppStartUpGameServerBase : AppStartUpBase
{
    /// <summary>
    /// 启动游戏服务器（sealed 模板序列）：启动日志 → Role 预备 → ActorLimit → 控制库 → 发现层 →
    /// 业务库 → 组件注册 → Role 服务 → 启动完成 → 主循环/就绪屏障 → 心跳 Active → 运行至退出令牌；
    /// 任何一步失败落入统一兜底（异常日志 + Fatal），随后退出清理 hook 收尾。
    /// </summary>
    /// <remarks>
    /// Starts the game server (the sealed template sequence): start logging → role preparation →
    /// ActorLimit → control database → discovery layer → business database → component
    /// registration → role services → startup completion → main-loop/ready-barrier → heartbeat
    /// Active → run until the exit token; any failure falls into the unified catch-all
    /// (exception log + Fatal) and the exit-cleanup hook closes the run.
    /// </remarks>
    /// <returns>表示异步启动操作的任务（运行至退出后完成）/ A task that completes after the run-until-exit cycle</returns>
    public sealed override async Task StartAsync()
    {
        string exitMessage = null;
        try
        {
            LogHelper.Info(LocalizationService.GetString(Localization.Keys.Launcher.ServerStartBegin, Setting.ServerType));
            await PrepareAsync();

            LogHelper.Debug(LocalizationService.GetString(Localization.Keys.Launcher.ActorLimitConfigBegin));
            ActorLimit.Init(ActorLimit.RuleType.None);
            LogHelper.Debug(LocalizationService.GetString(Localization.Keys.Launcher.ActorLimitConfigEnd));

            LogHelper.Debug(LocalizationService.GetString(Localization.Keys.Launcher.DatabaseServiceStartBegin));
            await InitializeControlDatabaseAsync();
            ActivateDiscoveryLayer();
            await InitializeBusinessDatabaseAsync();
            LogHelper.DebugConsole(LocalizationService.GetString(Localization.Keys.Launcher.DatabaseServiceStartEnd));

            LogHelper.DebugConsole(LocalizationService.GetString(Localization.Keys.Launcher.ComponentRegisterBegin));
            await ComponentRegister.Init(typeof(AppsHandler).Assembly);
            LogHelper.DebugConsole(LocalizationService.GetString(Localization.Keys.Launcher.ComponentRegisterEnd));

            await StartRoleServicesAsync();
            await OnStartupCompletedAsync();

            LogHelper.DebugConsole(LocalizationService.GetString(Localization.Keys.Launcher.EnterMainLoop));
            LogHelper.Info(LocalizationService.GetString(Localization.Keys.Launcher.ServerStartEnd, Setting.ServerType));
            // 启动阶段完成（DB/组件/Role 服务均已就绪），放行下一个 Role 的启动屏障
            MarkStartUpReady();
            // 启动阶段真正完成后才把心跳从 Booting 切到 Active，避免其他进程在服务就绪前发现本实例并投递流量；
            // 未激活发现层或无广播身份时为无害 no-op。公共槽位持有实际激活的 Provider Runtime 写侧，宿主无需按 DatabaseProvider 分派
            await ActiveDiscoveryRuntime.MarkActiveAsync();

            exitMessage = await AppExitToken;
        }
        catch (Exception e)
        {
            LogHelper.Info(LocalizationService.GetString(Localization.Keys.Launcher.ServerExecutionException, e));
            LogHelper.Fatal(e);
        }

        LogHelper.Info(LocalizationService.GetString(Localization.Keys.Launcher.ServerExitBegin));
        await ShutdownAsync(exitMessage);
        LogHelper.Info(LocalizationService.GetString(Localization.Keys.Launcher.ServerExitSuccess));
    }

    /// <summary>
    /// Role 专属预备 hook：标准序列最前段（启动日志之后、ActorLimit 与一切装配之前）执行，默认空实现。
    /// </summary>
    /// <remarks>
    /// The role-specific preparation hook at the very front of the standard sequence (after the
    /// start log, before ActorLimit and any wiring); the default is a no-op. 只放纯 Role 专属的
    /// 前置动作；进程级职责（如热更目录确保）不下放 hook，由对应组件自身兜底。
    /// Only genuinely role-specific preparation belongs here; process-level concerns (such as
    /// ensuring the hotfix directory) are not hooks — the owning component covers them itself.
    /// </remarks>
    /// <returns>表示 hook 操作的异步任务 / A task representing the hook operation</returns>
    protected virtual Task PrepareAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Role 服务启动 hook（缺省 = 统一形态）：热更模块装载（激活热更业务生命周期）→ 网络监听启动。
    /// </summary>
    /// <remarks>
    /// The role-services hook (default = the unified shape): hotfix module loading (which activates the
    /// hotfix business lifecycle) followed by the host network listener startup. 装载走
    /// <see cref="HotfixManager.LoadHotfixModule(AppSetting, HotfixLoadOptions)"/>（异步入口）：装载成功经
    /// 桥接 <c>IHotfixBridge.OnLoadSuccess</c> 激活热更生命周期（配置表/定时器/全局组件），
    /// 监听所有权在宿主；同路径幂等守卫使 --AllInOne 多 Role 的重复装载短路返回。
    /// Loading goes through <see cref="HotfixManager.LoadHotfixModule(AppSetting, HotfixLoadOptions)"/>
    /// (the async entry): a successful load activates the hotfix lifecycle (config tables, timers, global
    /// components) through the <c>IHotfixBridge.OnLoadSuccess</c> callback while listener ownership stays
    /// with the host; the same-path idempotency guard short-circuits repeated loads by extra roles in
    /// --AllInOne. aop 装配自序列最前段挪入本 hook 首段：唯一消费方是 <c>StartServerAsync</c>，
    /// 纯反射收集无 DB/状态依赖。
    /// The aop assembly moved here from the very front of the sequence: its only consumer is
    /// <c>StartServerAsync</c>, it is pure reflection with no database or state dependency.
    /// </remarks>
    /// <returns>表示 hook 操作的异步任务 / A task representing the hook operation</returns>
    protected virtual async Task StartRoleServicesAsync()
    {
        await HotfixManager.LoadHotfixModule(Setting);
        await StartServerAsync<DefaultMessageDecoderHandler, DefaultMessageEncoderHandler>(new DefaultMessageCompressHandler(), new DefaultMessageDecompressHandler(), HotfixManager.GetListHttpHandler(), HotfixManager.GetHttpHandler);
    }

    /// <summary>
    /// 启动完成 hook（缺省 = 统一形态，全 Role 生效）：网络监听就绪后分发 <c>ServiceOnline</c> 事件
    ///（唯一分发点——热更侧不分发，C284 单源化）。
    /// </summary>
    /// <remarks>
    /// The startup-completed hook (default = the unified shape, effective for every role): dispatches the
    /// <c>ServiceOnline</c> event once the network listeners are up — the single dispatch point (the
    /// hotfix side no longer dispatches it, unified by C284).
    /// </remarks>
    /// <returns>表示 hook 操作的异步任务 / A task representing the hook operation</returns>
    protected virtual Task OnStartupCompletedAsync()
    {
        EventDispatcher.Dispatch(0, new ServiceOnlineEventArgs(Setting.ServerType, Setting.ServerInstanceId, DateTime.UtcNow));
        return Task.CompletedTask;
    }

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
    /// 停机钩子：护栏放行后、基类停机流程前回调恰好一次，缺省 = <c>ServiceOffline</c> 事件分发
    ///（唯一分发点——热更侧不分发，C284 单源化）。
    /// </summary>
    /// <remarks>
    /// The stopping hook invoked exactly once after the guard admits the first stop and before the
    /// base stop flow; the default dispatches the <c>ServiceOffline</c> event — the single dispatch
    /// point (the hotfix side no longer dispatches it, unified by C284).
    /// </remarks>
    /// <param name="message">终止原因（首个 <see cref="StopAsync(string)"/> 调用透传）/ The termination reason passed through by the first stop call</param>
    /// <returns>表示钩子操作的异步任务 / A task representing the hook operation</returns>
    protected virtual Task OnStoppingAsync(string message)
    {
        EventDispatcher.Dispatch(0, new ServiceOfflineEventArgs(Setting.ServerType, Setting.ServerInstanceId, "Stopped", DateTime.UtcNow));
        return Task.CompletedTask;
    }

    /// <summary>
    /// 退出清理 hook（缺省 = 统一形态）：自停——护栏内的 <see cref="StopAsync(string)"/>，
    /// 宿主逆序停机先到时本调用被护栏吸收，<c>ServiceOffline</c> 仍恰好一次。
    /// Game 型覆写为「Online 撤桥停 admin → 宿主停机 → 热更模块业务清理」三段编排。
    /// </summary>
    /// <remarks>
    /// The exit-cleanup hook (default = the unified shape): self-stop through the guarded
    /// <see cref="StopAsync(string)"/> — when the host's reverse-order stop wins
    /// the race, the guard absorbs this call and <c>ServiceOffline</c> still fires exactly once.
    /// The Game shape overrides it with the three-phase orchestration: Online bridge retraction and
    /// admin stop, the host stop, then the hotfix module's business cleanup.
    /// </remarks>
    /// <param name="exitMessage">退出原因（可能为 null——启动失败路径未获得退出消息）/ The exit reason (may be null on the startup-failure path)</param>
    /// <returns>表示 hook 操作的异步任务 / A task representing the hook operation</returns>
    protected virtual Task ShutdownAsync(string exitMessage)
    {
        return StopAsync(exitMessage ?? "");
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
    /// Provider 装配经 DiscoveryRuntimeResolver 约定解析——按 DiscoveryActivationOptions.Provider
    /// 反射定位并激活目标 Runtime（C213），组合根零分支。
    /// 路由胶水装配自 Runtime 拆至组合侧 DiscoveryRoutingWire（发现层 Runtime 不再引用消息胶水程序集），
    /// 路由表经 ActiveDiscoveryRuntime.RouteTableProvider 槽位无分派回读。
    /// </summary>
    /// <remarks>
    /// Activates the discovery layer once the control database is ready — the read-side watcher
    /// plus the write-side heartbeat (skipped automatically when no broadcast port is configured)
    /// and the cross-role routing seam re-wired with the real case 2/3 forwarders. Idempotent: in
    /// a multi-role process the first call wins. It then activates the player-routing layer
    /// (creates the player_route index and installs the sync target) and injects the Tier 1
    /// fast-path adapter into the session manager. The provider dispatch lives in the
    /// <c>DiscoveryRuntimeResolver</c> convention (reflecting the target runtime from
    /// <c>DiscoveryActivationOptions.Provider</c>, C213) so this composition root holds no
    /// provider branch; the routing glue assembly lives in the composition-side
    /// <c>DiscoveryRoutingWire</c>, its route table read back branch-free through the
    /// <c>ActiveDiscoveryRuntime.RouteTableProvider</c> slot.
    /// </remarks>
    protected virtual void ActivateDiscoveryLayer()
    {
        // 约定激活：Provider 元数据随 options 走，目标 Runtime 由 DiscoveryRuntimeResolver 反射定位（镜像 GameDb.Init 的 DbOptions.Provider）。
        DiscoveryRuntimeResolver.Activate(new DiscoveryActivationOptions
        {
            Provider = Setting.DatabaseProvider,
            ConnectionName = GameDb.ControlDatabaseName,
            HostedRoleNames = RoleSet.Current,
            PlayerRouteFastPath = GameFrameX.Core.Session.PlayerSessionFastPathAdapter.Instance,
        });

        // 玩家路由 SyncTarget 与 Provider 无关（ResolverBootstrap 单例），激活后统一装配。
        GameFrameX.Core.Session.PlayerSessionManager.Instance.PlayerRouteSyncTarget = GameFrameX.Discovery.Routing.PlayerRouteResolverBootstrap.SyncTarget;
        // 路由胶水装配自 Runtime 拆至组合侧 DiscoveryRoutingWire（发现层 Runtime 不再引用消息胶水程序集）；
        // 路由表经公共槽位无分派回读，未激活时 null 触发 Initialize 的 ArgumentNullException fail-fast（既有语义）。
        GameFrameX.Network.RemoteMessaging.Routing.DiscoveryRoutingWire.Initialize(RoleSet.Current, ActiveDiscoveryRuntime.RouteTableProvider);
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

    /// <summary>
    /// 消息处理器解析（统一形态，全 Role 生效）：接热更注册表按消息 ID 解析处理器。
    /// </summary>
    /// <remarks>
    /// The resolution hook of the base package funnel (the unified shape, effective for every role):
    /// bridges the hotfix message-handler registry (<c>HotfixManager.GetMessageHandler</c>).
    /// Messages without a registered handler return <c>null</c> and hit the funnel's
    /// "handler not found" error log.
    /// </remarks>
    /// <param name="messageId">消息ID / The message identifier</param>
    /// <returns>处理器实例；未注册时为 <c>null</c> / The handler instance, or <c>null</c> when unregistered</returns>
    protected override IMessageHandler ResolveMessageHandler(int messageId)
    {
        return HotfixManager.GetMessageHandler(messageId);
    }

    /// <summary>
    /// 回复心跳消息（统一形态，全 Role 生效）：回显请求 UniqueId 的池化 NotifyHeartBeat 保活回包。
    /// </summary>
    /// <remarks>
    /// The keepalive reply (the unified shape, effective for every role): echoes the request's
    /// UniqueId in a pooled <c>NotifyHeartBeat</c> carrying the server timestamp. The receive-time
    /// stamping happens in the base funnel before this hook.
    /// </remarks>
    /// <param name="networkChannel">会话网络通道 / The session network channel</param>
    /// <param name="messagePackage">心跳消息包 / The heartbeat message package</param>
    /// <returns>表示异步操作的任务 / A task representing the asynchronous operation</returns>
    protected override async ValueTask ReplyHeartBeatAsync(INetworkChannel networkChannel, INetworkMessagePackage messagePackage)
    {
        if (messagePackage.DeserializeMessageObject() is ReqHeartBeat request)
        {
            var notifyHeartBeat = MessageObjectPoolHelper.Get<NotifyHeartBeat>();
            try
            {
                notifyHeartBeat.Timestamp = TimerHelper.UnixTimeMilliseconds();
                notifyHeartBeat.UniqueId = request.UniqueId;
                await networkChannel.WriteAsync(notifyHeartBeat);
            }
            finally
            {
                // 池化消息用毕归还（WriteAsync 在首个 await 前完成编码消费，归还安全）
                MessageObjectPoolHelper.Return(notifyHeartBeat);
            }
        }
    }
}
