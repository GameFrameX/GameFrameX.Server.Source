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
//   or to infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes and liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
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


using GameFrameX.DataBase.Abstractions;
using GameFrameX.DataBase.Mongo.Discovery;
using GameFrameX.DataBase.PostgreSql.Discovery;
using GameFrameX.Discovery;
using GameFrameX.Discovery.Routing;
using GameFrameX.NetWork.Abstractions;
using GameFrameX.NetWork.HTTP;
using GameFrameX.NetWork.Message;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Apps.Common.Event;
using GameFrameX.Apps.Common.EventData;
using GameFrameX.Core.Events;

namespace GameFrameX.Launcher.StartUp;

/// <summary>
/// 标准服务器启动骨架（Social 型）：直承 <see cref="AppStartUpBase"/>，自含「标准 Role」完整启动序列。
/// </summary>
/// <remarks>
/// Standard server startup skeleton (Social-shaped), inheriting <see cref="AppStartUpBase"/> directly.
/// 「标准 Role」= 自有网络监听 + 进程内热更的自治服务：aop 反射装配、控制库/发现层/业务库装配、
/// 组件注册、热更加载、网络启动、上下线事件与自停对所有标准 Role 完全同构，只有
/// <see cref="StartUpTagAttribute"/>（名称/优先级）与缺省配置（端口/环境变量）因 Role 而异，
/// 因此二者下放为抽象成员，具体 Role 只需声明标签与 <see cref="CreateDefaultSetting"/>。
/// The startup sequence (aop assembly, control/business database wiring, discovery activation,
/// component registration, hotfix loading, network start, online/offline events and self-stop)
/// is identical for every standard role; only the startup tag (name/priority) and the default
/// settings (ports/environment variables) differ per role.
/// </remarks>
internal abstract class AppStartUpStandardServerBase : AppStartUpBase
{
    /// <summary>
    /// 停机幂等护栏标志位（0 = 未停机，1 = 已进入停机）。
    /// </summary>
    /// <remarks>
    /// The idempotent stop guard flag (0 = running, 1 = stopping/stopped).
    /// </remarks>
    private int _stopped;

    /// <summary>
    /// aop 处理器装配结果（反射收集 <see cref="IHttpAopHandler"/> 实现并按 <see cref="IHttpAopHandler.Priority"/> 升序排序）。
    /// </summary>
    /// <remarks>
    /// The assembled aop handlers (collected by reflection and sorted ascending by
    /// <see cref="IHttpAopHandler.Priority"/>), passed to <c>StartServerAsync</c> so HTTP 拦截按声明优先级生效。
    /// 装配发生在 <see cref="StartAsync"/> 最前段，先于任何 DB/网络初始化。
    /// </remarks>
    /// <value>按优先级升序的 aop 处理器列表 / The aop handlers sorted ascending by priority</value>
    protected List<IHttpAopHandler> AopHandlerTypes { get; private set; }

    /// <summary>
    /// 启动标准服务器：装配 aop → 控制库 → 发现层 → 业务库 → 组件 → 热更 → 网络 → 上线事件 → 运行至退出 → 自停。
    /// </summary>
    /// <remarks>
    /// Starts the standard server: aop assembly, then (inside the fault barrier) control database,
    /// discovery activation, business database, component registration, hotfix loading, network start,
    /// <c>ServiceOnline</c> dispatch, the startup-ready barrier and the heartbeat Active switch,
    /// then waits on the exit token; any failure falls into the catch-all logger and the role self-stops.
    /// </remarks>
    public sealed override async Task StartAsync()
    {
        AopHandlerTypes = AssemblyHelper.GetRuntimeImplementTypeNamesInstance<IHttpAopHandler>();
        AopHandlerTypes.Sort((handlerX, handlerY) => handlerX.Priority.CompareTo(handlerY.Priority));
        string exitMessage = null;
        try
        {
            LogHelper.Debug(LocalizationService.GetString(Localization.Keys.Launcher.DatabaseServiceStartBegin));
            LogHelper.Debug(LocalizationService.GetString(Localization.Keys.Launcher.ActorLimitConfigBegin));
            ActorLimit.Init(ActorLimit.RuleType.None);
            LogHelper.Debug(LocalizationService.GetString(Localization.Keys.Launcher.ActorLimitConfigEnd));
            // 控制库先行于业务库，经 GameDb 统一入口判重/命名；
            // 实现类型按 DbOptions.Provider 由 GameDb 约定解析（Name 仅注册名，库由连接串决定）。
            // 控制库必须显式 IsDefault = false——门面默认库由业务库声明式提名（缺省 true），漏写即启动期冲突。
            if (!GameDb.Contains(GameDb.ControlDatabaseName))
            {
                var controlDatabaseInitResult = await GameDb.Init(new DbOptions { Provider = Setting.DatabaseProvider, ConnectionString = Setting.DataBaseUrl, Name = GameDb.ControlDatabaseName, IsDefault = false, IsUseTimeZone = Setting.IsUseTimeZone, });
                if (controlDatabaseInitResult == false)
                {
                    throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.Launcher.DatabaseServiceStartFailed));
                }
            }

            // 控制库就绪后激活发现层——读侧 watcher + 写侧心跳（未配置广播端口时自动跳过）
            // 并以真实 case 2/3 转发器重装跨 Role 路由缝。幂等：多 Role 进程首个调用生效。
            // 再激活玩家路由层（建 player_route 索引 + 装 SyncTarget），Tier 1 fast-path 注入 SessionManager 适配器。
            // 控制库句柄解析下沉到发现层内部，收敛为激活参数对象。
            // Provider 装配分支——PostgreSql 时激活 PG 平行发现层并接 PG SyncTarget。
            if (Setting.DatabaseProvider == DatabaseProviderType.PostgreSql)
            {
                PostgreSqlDiscoveryRuntime.Activate(new DiscoveryActivationOptions
                {
                    ConnectionName = GameDb.ControlDatabaseName,
                    HostedRoleNames = RoleSet.Current,
                    PlayerRouteFastPath = GameFrameX.Apps.Common.Session.SessionManagerFastPathAdapter.Instance,
                });
                GameFrameX.Apps.Common.Session.SessionManager.PlayerRouteSyncTarget = GameFrameX.Discovery.Routing.PlayerRouteResolverBootstrap.SyncTarget;
            }
            else
            {
                MongoDiscoveryRuntime.Activate(new DiscoveryActivationOptions
                {
                    ConnectionName = GameDb.ControlDatabaseName,
                    HostedRoleNames = RoleSet.Current,
                    PlayerRouteFastPath = GameFrameX.Apps.Common.Session.SessionManagerFastPathAdapter.Instance,
                });
                GameFrameX.Apps.Common.Session.SessionManager.PlayerRouteSyncTarget = GameFrameX.Discovery.Routing.PlayerRouteResolverBootstrap.SyncTarget;
            }
            // 路由胶水装配自 Runtime 拆至组合侧 DiscoveryRoutingWire（发现层 Runtime 不再引用消息胶水程序集）。
            GameFrameX.NetWork.RemoteMessaging.Routing.DiscoveryRoutingWire.Initialize(RoleSet.Current, Setting.DatabaseProvider == DatabaseProviderType.PostgreSql ? PostgreSqlDiscoveryRuntime.TableProvider : MongoDiscoveryRuntime.TableProvider);

            // 业务库注册即声明式提名门面默认库（DbOptions.IsDefault 缺省 true）——
            // 全部经 GameDb 门面的业务读写必须落在业务库（控制库已显式 IsDefault = false，先注册不会抢占门面）。
            // 同名判重护栏：多 Role 进程共享同名业务库时首个注册生效（GameDb.Init 拒绝同名重复注册），
            // 与上方控制库 Contains 护卫同一多 Role 语义。
            if (!GameDb.Contains(Setting.DataBaseName))
            {
                var initResult = await GameDb.Init(new DbOptions { Provider = Setting.DatabaseProvider, ConnectionString = Setting.DataBaseUrl, Name = Setting.DataBaseName, IsUseTimeZone = Setting.IsUseTimeZone, });
                if (initResult == false)
                {
                    throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.Launcher.DatabaseServiceStartFailed));
                }
            }

            await ComponentRegister.Init(typeof(AppsHandler).Assembly);
            HotfixManager.LoadHotfix(Setting);
            await StartServerAsync<DefaultMessageDecoderHandler, DefaultMessageEncoderHandler>(new DefaultMessageCompressHandler(), new DefaultMessageDecompressHandler(), HotfixManager.GetListHttpHandler(), HotfixManager.GetHttpHandler, AopHandlerTypes);
            EventDispatcher.Dispatch(0, (int)EventId.ServiceOnline, new ServiceOnlineEventArgs(Setting.ServerType, Setting.ServerInstanceId, DateTime.UtcNow));

            // 启动阶段完成（DB/组件/网络监听均已就绪），放行下一个 Role 的启动屏障
            MarkStartUpReady();
            // 启动阶段真正完成（DB/组件/网络监听均已就绪）后才把心跳从 Booting 切到 Active，
            // 避免其他进程在本 Role TCP listener 就绪前发现本实例并投递流量。
            // 公共槽位持有实际激活的 Provider Runtime 写侧，宿主无需按 DatabaseProvider 分派。
            await ActiveDiscoveryRuntime.MarkActiveAsync();

            exitMessage = await AppExitToken;
        }
        catch (Exception e)
        {
            LogHelper.Info(LocalizationService.GetString(Localization.Keys.Launcher.ServerExecutionException, e));
            LogHelper.Fatal(e);
        }

        await StopAsync(exitMessage ?? "");
    }

    /// <summary>
    /// 停止标准服务器：幂等护栏保证首个调用生效，<c>ServiceOffline</c> 事件恰好分发一次。
    /// </summary>
    /// <remarks>
    /// Stops the standard server behind an idempotent guard: the first caller wins and the
    /// rest are no-ops. <see cref="AppEnter"/> stops hosts in reverse launch order while the
    /// role's own run-until-exit loop self-stops on the exit token — both paths race into this
    /// method, and the guard collapses them so the <c>ServiceOffline</c> dispatch happens exactly
    /// once and the base stop flow (log, <c>StopServerAsync</c>, runtime state) runs only once.
    /// 停机护栏语义：宿主逆序停机与自停竞争时首个调用生效，后续调用直接返回。
    /// </remarks>
    /// <param name="message">终止原因 / Termination reason</param>
    /// <returns>表示异步停止操作的任务 / A task representing the asynchronous stop operation</returns>
    public sealed override async Task StopAsync(string message = "")
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
        {
            return;
        }

        await OnStoppingAsync();
        await base.StopAsync(message);
    }

    /// <summary>
    /// 停机钩子：进入停机时回调一次，默认分发 <see cref="ServiceOffline"/> 事件。
    /// </summary>
    /// <remarks>
    /// The stopping hook invoked exactly once when the role stops; the default implementation
    /// dispatches the <c>ServiceOffline</c> event. Derived skeletons may override it to extend
    /// the offline sequence (the base dispatch must then be preserved or replaced deliberately).
    /// </remarks>
    /// <returns>表示钩子操作的异步任务 / A task representing the hook operation</returns>
    protected virtual Task OnStoppingAsync()
    {
        EventDispatcher.Dispatch(0, (int)EventId.ServiceOffline, new ServiceOfflineEventArgs(Setting.ServerType, Setting.ServerInstanceId, "Stopped", DateTime.UtcNow));
        return Task.CompletedTask;
    }

    /// <summary>
    /// 网络消息处理：基类处理后，将 <see cref="INetworkMessagePackage"/> 交由热更 TCP handler 分发。
    /// </summary>
    /// <remarks>
    /// Network package handling: after the base handling, dispatches <see cref="INetworkMessagePackage"/>
    /// messages to the hotfix TCP handler resolved by <c>HotfixManager.GetTcpHandler</c>;
    /// a message without a registered handler hits the existing "handler not found" logging path.
    /// </remarks>
    /// <param name="session">会话对象 / The session</param>
    /// <param name="message">网络消息 / The network message</param>
    /// <returns>表示异步操作的任务 / A task representing the asynchronous operation</returns>
    protected override async ValueTask PackageHandler(IAppSession session, IMessage message)
    {
        await base.PackageHandler(session, message);
        if (message is INetworkMessagePackage messageObject)
        {
            var handler = HotfixManager.GetTcpHandler(messageObject.Header.MessageId);
            await InvokeMessageHandler(handler, messageObject.DeserializeMessageObject(), new DefaultNetWorkChannel(session, Setting));
        }
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
    /// database wiring and debug switches). Concrete roles implement only this member plus
    /// their <see cref="StartUpTagAttribute"/> declaration.
    /// </remarks>
    /// <returns>该 Role 的缺省配置 / The role-level default settings</returns>
    protected abstract AppSetting CreateDefaultSetting();
}
