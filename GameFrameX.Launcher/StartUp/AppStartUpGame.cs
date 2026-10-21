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


using System.Reflection;
using GameFrameX.Capability.Hosting.Core;
using GameFrameX.Capability.Hosting.AdminApi;
using GameFrameX.Core.Events;
using GameFrameX.Core.Session;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Foundation.Utility;
using GameFrameX.Network.Abstractions;
using GameFrameX.Launcher.Online;
using GameFrameX.Utility.Runtime;

namespace GameFrameX.Launcher.StartUp;

/// <summary>
/// 游戏服务器
/// </summary>
/// <remarks>
/// 组合根边界（C240 声明随 C243 交付）：本类所在启动模板序列（<see cref="AppStartUpGameServerBase"/>
/// 模板钩子 + 各 Role 差异覆写）负责基础设施装配——网络监听、GameDb 数据库、发现层、热更模块装载与退出编排；
/// 平台业务域服务（好友 / 资产 / 对局等）的唯一组合根是 <c>AddOnlineCapabilities</c> 单入口装配的
/// <c>CapabilityHost</c>（本类持有并按启停序编排）。两层边界经 C243 好友查询桥实证：
/// Hotfix 协议面薄适配器经 Online 契约面进程内消费平台服务，不经本地注册表绕行。
/// Composition-root boundary (declared by C240, delivered with C243): the startup template sequence
/// (the <see cref="AppStartUpGameServerBase"/> template hooks plus per-role overrides) assembles the
/// infrastructure — network listeners, the GameDb database, the discovery layer, hotfix module loading
/// and exit orchestration — while the sole composition root for platform domain services (friends,
/// assets, matches, ...) is the <c>CapabilityHost</c> assembled by the single
/// <c>AddOnlineCapabilities</c> entry (held and lifecycle-orchestrated by this class). The two-layer
/// boundary is proven by the C243 friend-query bridge: the Hotfix protocol thin adapter consumes the
/// platform services in-process through the Online contract face, never through the local registry.
/// </remarks>
// 显式优先级修复与 Social 同为缺省 1000 的冲突——Game 为主服务先起（值越小优先级越高）
[StartUpTag(GameServerConst.Game.Name, 100)]
internal sealed class AppStartUpGame : AppStartUpGameServerBase
{
    /// <summary>
    /// Online Runtime 过渡组合根（<see cref="AppStartUpGame"/> 经 AddOnlineCapabilities 单入口装配的产物；
    /// 业务闸关闭时为 null；进程退出时随主流程停止）。
    /// </summary>
    /// <remarks>
    /// The Online runtime host — the runtime face of the AddOnlineCapabilities composition (null
    /// when the business gate is off; stopped with the main flow at process exit).
    /// </remarks>
    private CapabilityHost _onlineRuntime;

    /// <summary>
    /// Online admin HTTP 服务（与协议端口独立的 Kestrel 监听；管理面闸开启时由单入口装配——
    /// 业务运行时在则全能力形态，不在则 admin-only 形态，全部能力 action 按未安装裁决）。
    /// </summary>
    /// <remarks>
    /// The Online admin HTTP service (a Kestrel listener independent of the protocol ports),
    /// composed by the single entry when the admin gate is on — full-capability shape with the
    /// business runtime, admin-only shape without it (every capability action answered as not
    /// installed).
    /// </remarks>
    private AdminApiServer _onlineAdminApi;

    /// <summary>
    /// Game 型 Role 服务：缺省统一形态（热更生命周期激活 + 宿主监听，经 base hook）+ Online 双闸附加启动
    ///（共享装配由模板序列承载）。
    /// </summary>
    /// <remarks>
    /// Game-shape role services: the unified default (hotfix lifecycle activation plus the host
    /// listener startup, both via the base hook) followed by the optional Online gates — the shared
    /// wiring (control database, discovery layer, business database, components) is carried by the
    /// template sequence. C284 起 Game 不再自拟启动序列：热更装载与监听均由缺省 hook 承载，
    /// 本覆写只追加 Online 面（Online 属范围外，装配必须保留）。
    /// Since C284 Game no longer rolls its own startup sequence — hotfix loading and listener
    /// ownership both live in the default hook; this override only appends the Online surface
    /// (Online is out of scope and its assembly must be preserved).
    /// </remarks>
    /// <returns>表示 hook 操作的异步任务 / A task representing the hook operation</returns>
    protected override async Task StartRoleServicesAsync()
    {
        await base.StartRoleServicesAsync();
        await StartOnlineIfEnabledAsync();
        await StartOnlineAdminIfEnabledAsync();
    }

    /// <summary>
    /// Game 型退出清理（三段逆序编排）：Online 撤桥并停 admin/runtime → 宿主停机（护栏内
    /// <c>ServiceOffline</c> 分发 + 网络监听停止）→ 热更模块业务清理。
    /// </summary>
    /// <remarks>
    /// Game-shape exit cleanup (three reverse phases): Online bridge retraction with the admin and
    /// runtime stops, then the host stop (the guarded <c>ServiceOffline</c> dispatch plus the
    /// network listener shutdown), then the hotfix module's business cleanup. C284 起监听所有权
    /// 归宿主：宿主 <c>StopAsync</c> 停监听并翻转运行时状态，热更 <c>Stop</c> 只清 Actor/定时器/会话。
    /// Since C284 listener ownership stays with the host — the host <c>StopAsync</c> stops the
    /// listeners and flips the runtime state while the hotfix <c>Stop</c> only clears actors,
    /// timers and sessions.
    /// </remarks>
    /// <param name="exitMessage">退出原因（可能为 null——启动失败路径未获得退出消息）/ The exit reason (may be null on the startup-failure path)</param>
    /// <returns>表示 hook 操作的异步任务 / A task representing the hook operation</returns>
    protected override async Task ShutdownAsync(string exitMessage)
    {
        await ShutdownOnlineAdminAsync();
        await StopAsync(exitMessage ?? "");
        await HotfixManager.Stop(exitMessage);
    }

    /// <summary>
    /// 创建 Game 服务器的 KCP 会话首消息鉴权配置：放行登录流程消息，以角色登录为鉴权完成信号。
    /// </summary>
    /// <remarks>
    /// Creates the KCP session first-message authentication options for the game server.
    /// 白名单为现有 TCP 登录流全链路（账号登录 → 角色列表/角色创建 → 角色登录）；
    /// <see cref="ReqPlayerLogin"/> 是鉴权终态——业务侧 OnPlayerLogin 处理该消息并绑定 ActorId，此后会话消息全放行。
    /// C204 起 <see cref="ReqSessionResume"/>（会话恢复）与角色登录同为鉴权完成信号——恢复即绑定 ActorId。
    /// C284 起监听面归宿主，本覆写自热更侧 <c>AppStartUpHotfixGame</c> 上移，语句逐字保持。
    /// Since C284 the listener surface lives with the host; this override moved up from the
    /// hotfix-side <c>AppStartUpHotfixGame</c> with the statements kept verbatim.
    /// </remarks>
    /// <returns>KCP 会话鉴权配置 / The KCP session authentication options</returns>
    protected override SessionAuthenticationOptions CreateKcpSessionAuthenticationOptions()
    {
        var options = base.CreateKcpSessionAuthenticationOptions();
        options.AllowedMessageIds.Add(GetLoginMessageId(typeof(ReqLogin)));
        options.AllowedMessageIds.Add(GetLoginMessageId(typeof(ReqPlayerList)));
        options.AllowedMessageIds.Add(GetLoginMessageId(typeof(ReqPlayerCreate)));
        options.AllowedMessageIds.Add(GetLoginMessageId(typeof(ReqPlayerLogin)));
        options.AllowedMessageIds.Add(GetLoginMessageId(typeof(ReqSessionResume)));
        options.AuthenticatedByMessageIds.Add(GetLoginMessageId(typeof(ReqPlayerLogin)));
        options.AuthenticatedByMessageIds.Add(GetLoginMessageId(typeof(ReqSessionResume)));
        return options;
    }

    /// <summary>
    /// 从消息类型的 <see cref="MessageTypeHandlerAttribute"/> 读取消息码（编译期固定值，不依赖运行时协议注册表初始化）。
    /// </summary>
    /// <param name="messageType">登录流程消息类型 / The login-flow message type</param>
    /// <returns>消息码 / The message id</returns>
    private static int GetLoginMessageId(Type messageType)
    {
        return messageType.GetCustomAttribute<MessageTypeHandlerAttribute>()?.MessageId
               // Localization: Hotfix.Startup.LoginMessageTypeMissingAttribute - 登录消息类型 {0} 缺少 {1}。
               ?? throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.Hotfix.LoginMessageTypeMissingAttribute, messageType.FullName, nameof(MessageTypeHandlerAttribute)));
    }

    /// <summary>
    /// Game 型会话建立钩子：达到最大在线数即拒绝并关闭，否则建立 <see cref="PlayerSession"/>
    ///（语句自热更侧 <c>AppStartUpHotfixGame.OnConnected</c> 上移，C284）。
    /// </summary>
    /// <remarks>
    /// The Game-shape session-established hook: rejects and closes the connection once the maximum
    /// client count is exceeded, otherwise registers the <see cref="PlayerSession"/> (moved up
    /// verbatim from the hotfix-side <c>AppStartUpHotfixGame.OnConnected</c>, C284).
    /// </remarks>
    /// <param name="appSession">新建立的会话对象 / Newly established session object</param>
    /// <returns>表示异步操作的任务 / A task representing the asynchronous operation</returns>
    protected override async ValueTask OnConnected(IAppSession appSession)
    {
        // Localization: StartUp.Server.NewClientConnection - 新客户端连接 - 会话ID: {0}, 远程终端: {1}
        LogHelper.Info(LocalizationService.GetString(Localization.Keys.StartUp.Server.NewClientConnection, appSession.SessionId, appSession.RemoteEndPoint));
        var netChannel = new DefaultNetworkChannel(appSession, Setting);
        var count = PlayerSessionManager.Instance.Count();
        if (count > Setting.MaxClientCount)
        {
            // 达到最大在线人数限制
            await netChannel.WriteAsync(new NotifyServerFullyLoaded(), (int)OperationStatusCode.ServerFullyLoaded);
            netChannel.Close();
            return;
        }

        var session = new PlayerSession(appSession.SessionId, netChannel);
        PlayerSessionManager.Instance.Add(session);
    }

    /// <summary>
    /// Game 型会话断开钩子：摘除会话并对已绑定玩家分发 <see cref="PlayerDisconnectedEventArgs"/>
    ///——房间断线标记与会话恢复通知由热更侧全局监听器承接（C284 事件桥接契约，
    /// 与消息漏斗的热更注册表桥接同形态）。
    /// </summary>
    /// <remarks>
    /// The Game-shape session-disconnected hook: removes the session and, for a bound player,
    /// dispatches <see cref="PlayerDisconnectedEventArgs"/> — the hotfix-side global listener
    /// carries the room disconnected-marking and the session-resume notification (the C284
    /// event-bridging contract, mirroring the hotfix-registry bridging of the message funnel).
    /// The dispatch is fire-and-forget, the same shape as the <c>SessionRemovedEventArgs</c>
    /// logout cleanup dispatched inside <c>PlayerSessionManager.Remove</c>.
    /// </summary>
    /// <param name="appSession">断开连接的会话对象 / Session object that disconnected</param>
    /// <param name="disconnectEventArgs">断开连接的相关参数 / Parameters related to disconnection</param>
    /// <returns>表示异步操作的任务 / A task representing the asynchronous operation</returns>
    protected override ValueTask OnDisconnected(IAppSession appSession, CloseEventArgs disconnectEventArgs)
    {
        // Localization: Hotfix.StartUp.ClientDisconnected - 客户端断开连接 - 会话ID: {0}, 断开原因: {1}
        LogHelper.Info(LocalizationService.GetString(Localization.Keys.Hotfix.StartUpModule.ClientDisconnected, appSession.SessionId, disconnectEventArgs.Reason));
        var session = PlayerSessionManager.Instance.Remove(appSession.SessionId);
        if (session != null && session.PlayerId > 0)
        {
            // 房间断线标记 + 会话恢复通知（C204 两层协同）经事件桥接由热更侧监听器承接
            EventDispatcher.Dispatch(0, new PlayerDisconnectedEventArgs(session.PlayerId, appSession.SessionId));
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// 经单入口装配 Online 能力（双闸任一开启即组合；<see cref="AppSetting.IsEnableOnline"/> 关且
    /// <see cref="AppSetting.IsEnableOnlineAdmin"/> 关时零装配零日志直接返回——现状等价）。
    /// <para>业务运行时与管理面一次回调编排装配（作用域三元组 / 调度间隔缺省 / 双闸 / 管理面监听 /
    /// "Online" 配置段过滤器）；本方法只启动业务面，管理面启动由
    /// <see cref="StartOnlineAdminIfEnabledAsync"/> 按闸独立决定。
    /// <para>
    /// C244 好友域接管：业务运行时启动后无条件发布好友域桥租约（旧 120/660 链已退役，无回落目标），
    /// 按配置执行旧好友数据一次性迁移（<c>Online.MigrateLegacyFriendData</c>）与验证种子
    ///（<c>Online.FriendBridgeSeedPlayerIds</c>）；撤销由退出路径 <see cref="ShutdownOnlineAdminAsync"/> 承担。
    /// </para>
    /// </summary>
    /// <remarks>
    /// Composes the Online capabilities through the single entry point (composition runs when either
    /// gate is on; when both <see cref="AppSetting.IsEnableOnline"/> and
    /// <see cref="AppSetting.IsEnableOnlineAdmin"/> are off nothing is assembled and no log fires —
    /// behaviour-equivalent to the pre-C229 shape). The runtime and the admin plane are orchestrated
    /// by one callback (scope triple / default scheduler interval / both gates / admin listening /
    /// the "Online" config-section filter); this method only starts the business plane — whether the
    /// admin plane starts is still decided independently by
    /// <see cref="StartOnlineAdminIfEnabledAsync"/>.
    /// </remarks>
    /// <returns>表示异步操作的任务 / A task representing the asynchronous operation</returns>
    private async Task StartOnlineIfEnabledAsync()
    {
        if (!Setting.IsEnableOnline && !Setting.IsEnableOnlineAdmin)
        {
            OnlineMatchBridgeWiring.EnsureMatchGameBridgeConfigured(Setting);
            return;
        }

        // C243 桥接 fail-fast：开关开启而拓扑不支持（业务运行时关/作用域缺失）在装配前终止。
        OnlineMatchBridgeWiring.EnsureMatchGameBridgeConfigured(Setting);

        var composition = OnlineCapabilityComposition.AddOnlineCapabilities(options =>
        {
            options.TenantId = Setting.OnlineTenantId;
            options.AppId = Setting.OnlineAppId;
            options.ServerId = Setting.ServerId;
            options.EnableRuntime = Setting.IsEnableOnline;
            options.EnableAdminApi = Setting.IsEnableOnlineAdmin;
            options.AdminApi = new AdminApiOptions
            {
                AdminPort = Setting.OnlineAdminPort,
                AdminApiPrefix = Setting.OnlineAdminApiPrefix,
            };
            options.Capabilities = Setting.Online?.Capabilities;
            options.Disable = Setting.Online?.Disable;
            options.UseGameDbStores = Setting.Online?.UseGameDbStores ?? false;
            options.MatchBridgeLeaderboardId = Setting.Online?.MatchBridgeLeaderboardId;
        });
        _onlineRuntime = composition.Runtime;
        _onlineAdminApi = composition.AdminApi;

        if (_onlineRuntime != null)
        {
            await _onlineRuntime.StartAsync();

            // C244 好友域接管：业务运行时启动后发布好友域桥租约（无回落目标），按配置执行旧数据迁移与验证种子。
            OnlineFriendDomainBridgeWiring.PublishFriendDomainBridge(_onlineRuntime);
            if (Setting.Online?.MigrateLegacyFriendData == true)
            {
                await OnlineFriendLegacyMigration.MigrateAsync(_onlineRuntime);
            }

            await OnlineFriendDomainBridgeWiring.SeedFriendDomainBridgeVerificationAsync(_onlineRuntime, Setting.Online?.FriendBridgeSeedPlayerIds);

            // C245 桥接：业务运行时启动后发布对局桥租约（开关开启时），并确保写榜目标榜单存在。
            if (Setting.Online?.EnableMatchGameBridge == true)
            {
                OnlineMatchBridgeWiring.PublishMatchGameBridge(_onlineRuntime);
                await OnlineMatchBridgeWiring.EnsureMatchLeaderboardBoardAsync(_onlineRuntime, Setting.Online?.MatchBridgeLeaderboardId);
            }

            // C246 匹配域接管：发布匹配域桥租约（420 族唯一转发面，无回落目标）并订阅撮合分配事件推送匹配成功通知。
            OnlineMatchDomainBridgeWiring.PublishMatchDomainBridge(_onlineRuntime);
            OnlineMatchDomainBridgeWiring.SubscribeMatchMadeNotifications(_onlineRuntime);

            // C204 两层会话协同：发布会话域桥租约（登录凭据签发 / 会话恢复 / 断线开窗的唯一转发面）。
            OnlineSessionDomainBridgeWiring.PublishSessionDomainBridge(_onlineRuntime);
        }
    }

    /// <summary>
    /// 可选启动 Online admin HTTP 服务（<see cref="AppSetting.IsEnableOnlineAdmin"/> 为 true 时监听；
    /// 业务运行时在则全能力形态，不在则 admin-only 形态——组合已由单入口完成，此处只负责启动与日志）。
    /// </summary>
    /// <remarks>
    /// Starts the Online admin HTTP service optionally — listens when
    /// <see cref="AppSetting.IsEnableOnlineAdmin"/> is true; full-capability shape when the business
    /// runtime is running, admin-only shape otherwise. The composition itself already happened in
    /// the single entry call; this method only starts the listener and emits the boundary logs.
    /// </remarks>
    /// <returns>表示异步操作的任务 / A task representing the asynchronous operation</returns>
    private async Task StartOnlineAdminIfEnabledAsync()
    {
        if (!Setting.IsEnableOnlineAdmin)
        {
            return;
        }

        LogHelper.Info(LocalizationService.GetString(Localization.Keys.Launcher.OnlineAdminStartBegin, Setting.OnlineAdminPort));
        await _onlineAdminApi.StartAsync();
        LogHelper.Info(LocalizationService.GetString(Localization.Keys.Launcher.OnlineAdminStartEnd, Setting.OnlineTenantId, Setting.OnlineAppId, Setting.ServerId));
    }

    /// <summary>
    /// 退出期按反向顺序停掉 Online admin HTTP 服务与 Online runtime 宿主；
    /// 未启动（<see cref="AppSetting.IsEnableOnlineAdmin"/> 为 false）时为空判跳过。
    /// </summary>
    /// <remarks>
    /// Stops the Online admin HTTP service and the Online runtime host in reverse order at exit;
    /// skipped by null checks when they were never started (<see cref="AppSetting.IsEnableOnlineAdmin"/> false).
    /// </remarks>
    /// <returns>表示异步操作的任务 / A task representing the asynchronous operation</returns>
    private async Task ShutdownOnlineAdminAsync()
    {
        // C244 好友域接管：先撤销好友域桥租约（旧引用显式失效），再停 Online admin 与 runtime 宿主。
        // C245 桥接：先撤销对局桥租约（旧引用显式失效），顺序同上（撤桥 → 停 admin → 停 runtime）。
        OnlineFriendDomainBridgeWiring.RetractFriendDomainBridge();
        OnlineMatchBridgeWiring.RetractMatchGameBridge();
        OnlineMatchDomainBridgeWiring.RetractMatchDomainBridge();

        // C204 两层会话协同：撤销会话域桥租约（恢复请求自此显式失败，不残留旧引用）。
        OnlineSessionDomainBridgeWiring.RetractSessionDomainBridge();

        if (_onlineAdminApi != null)
        {
            await _onlineAdminApi.StopAsync();
        }

        if (_onlineRuntime != null)
        {
            await _onlineRuntime.StopAsync();
        }
    }

    /// <summary>
    /// 创建 Game 服务器的缺省配置。
    /// </summary>
    /// <remarks>
    /// Creates the Game role-level default settings.
    /// 数据库连接地址优先从环境变量读取，避免在源码中硬编码凭证（消除 Sonar csharpsquid:S2068）。
    /// The database connection URL is read from the environment variable first to avoid
    /// hard-coded credentials in source (Sonar csharpsquid:S2068).
    /// </remarks>
    /// <returns>Game 缺省配置 / The Game default settings</returns>
    protected override AppSetting CreateDefaultSetting()
    {
        return new AppSetting
        {
            ServerId = GameServerConst.Game.Id,
            ServerType = GameServerConst.Game.Name,
            InnerPort = 29100,
            MetricsPort = 29090,
            HttpPort = 28080,
            WsPort = 29110,
            MinModuleId = 10,
            HttpIsDevelopment = true,
            MaxModuleId = 9999,
            TagName = "GameFrameX",
            // 数据库连接地址优先从环境变量读取，避免在源码中硬编码凭证（消除 Sonar csharpsquid:S2068）
            DataBaseUrl = Environment.GetEnvironmentVariable("GAMEFRAMEX_GAME_DB_URL") ?? "mongodb://127.0.0.1:27017/?authSource=admin",
            DataBaseName = "gameframex",
        };
    }
}
