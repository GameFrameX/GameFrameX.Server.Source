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


using GameFrameX.Capability.Hosting.Core;
using GameFrameX.Capability.Hosting.AdminApi;
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
    /// Game 型 Role 服务：热更模块装载 + Online admin 可选启动（共享装配由模板序列承载）。
    /// </summary>
    /// <remarks>
    /// Game-shape role services: hotfix module loading plus the optional Online admin; the shared
    /// wiring (control database, discovery layer, business database, components) is carried by the
    /// template sequence. 热更目录确保由 <c>HotfixManager</c> 装载入口自身兜底，非 Game 型差异。
    /// The hotfix-directory ensuring is covered by the <c>HotfixManager</c> load entry itself and
    /// is not a Game-shape difference.
    /// </remarks>
    /// <returns>表示 hook 操作的异步任务 / A task representing the hook operation</returns>
    protected override async Task StartRoleServicesAsync()
    {
        LogHelper.DebugConsole(LocalizationService.GetString(Localization.Keys.Launcher.HotfixModuleLoadBegin));
        await HotfixManager.LoadHotfixModule(Setting);
        LogHelper.DebugConsole(LocalizationService.GetString(Localization.Keys.Launcher.HotfixModuleLoadEnd));

        await StartOnlineIfEnabledAsync();
        await StartOnlineAdminIfEnabledAsync();
    }

    /// <summary>
    /// Game 型启动完成：业务装配全就绪，标记进程运行时已启动（退出令牌随之复位，进入运行至退出周期）。
    /// </summary>
    /// <remarks>
    /// Game-shape startup completion: with every assembly step done, the process runtime is marked
    /// started (which also resets the exit token for a fresh run-until-exit cycle).
    /// </remarks>
    /// <returns>表示 hook 操作的异步任务 / A task representing the hook operation</returns>
    protected override Task OnStartupCompletedAsync()
    {
        GameAppRuntime.MarkStarted(TimerHelper.GetNowWithUtc());
        return Task.CompletedTask;
    }

    /// <summary>
    /// Game 型退出清理：按反向顺序停 Online admin HTTP 服务与 Online runtime 宿主，再停热更模块。
    /// </summary>
    /// <remarks>
    /// Game-shape exit cleanup: stops the Online admin HTTP service and the Online runtime host in
    /// reverse order, then stops the hotfix module.
    /// </remarks>
    /// <param name="exitMessage">退出原因（可能为 null——启动失败路径未获得退出消息）/ The exit reason (may be null on the startup-failure path)</param>
    /// <returns>表示 hook 操作的异步任务 / A task representing the hook operation</returns>
    protected override async Task ShutdownAsync(string exitMessage)
    {
        await ShutdownOnlineAdminAsync();
        await HotfixManager.Stop(exitMessage);
    }

    /// <summary>
    /// Game 型中性化覆写：空实现——不参与 <c>ServiceOffline</c> 分发（HEAD 行为，Game 无上下线事件面；
    /// 模板缺省面向 Social 型多数形态）。
    /// </summary>
    /// <remarks>
    /// Game-shape neutralizing override: a no-op — Game does not join the <c>ServiceOffline</c>
    /// dispatch (the HEAD behaviour; Game has no online/offline event surface, while the template
    /// default follows the Social-shape majority).
    /// </remarks>
    /// <param name="message">终止原因（护栏透传）/ The termination reason passed through by the guard</param>
    /// <returns>表示钩子操作的异步任务 / A task representing the hook operation</returns>
    protected override Task OnStoppingAsync(string message)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// Game 型中性化覆写：仅保留基类调试日志行为，不做 TCP 热更分发——Game 的网络监听由
    /// Hotfix 模块实例（自持 PackageHandler 的 <c>AppStartUpHotfixGame</c>）承载，宿主实例无监听面。
    /// 心跳日志门控与基类漏斗对齐：在接收调试之上额外要求 <c>IsDebugReceiveHeartBeat</c> 专用开关。
    /// </summary>
    /// <remarks>
    /// Game-shape neutralizing override: keeps only the base debug-logging behaviour without the
    /// funnel dispatch — Game's network listeners live inside the hotfix module instance
    /// (<c>AppStartUpHotfixGame</c> with its own <c>PackageHandler</c>), so the host instance has
    /// no listener surface. The heartbeat log gating mirrors the base funnel: an extra
    /// <c>IsDebugReceiveHeartBeat</c> flag on top of receive debugging.
    /// </remarks>
    /// <param name="session">会话对象 / The session</param>
    /// <param name="message">网络消息 / The network message</param>
    /// <returns>表示异步操作的任务 / A task representing the asynchronous operation</returns>
    protected override ValueTask PackageHandler(IAppSession session, IMessage message)
    {
        // 心跳是框架级保活语义，日志门控与基类漏斗对齐：额外要求 IsDebugReceiveHeartBeat 专用开关
        if (message is INetworkMessagePackage messagePackage && MessageProtoHelper.IsHeartbeat(messagePackage.MessageType))
        {
            if (Setting.IsDebug && Setting.IsDebugReceive && Setting.IsDebugReceiveHeartBeat)
            {
                // Localization: StartUp.Server.MessageReceived - 接收到消息 - 服务器类型: [{0}], 消息内容: {1}
                LogHelper.Debug(LocalizationService.GetString(Localization.Keys.StartUp.Server.MessageReceived, ServerType, message.ToFormatMessageString()));
            }

            return ValueTask.CompletedTask;
        }

        if (Setting.IsDebug && Setting.IsDebugReceive)
        {
            // Localization: StartUp.Server.MessageReceived - 接收到消息 - 服务器类型: [{0}], 消息内容: {1}
            LogHelper.Debug(LocalizationService.GetString(Localization.Keys.StartUp.Server.MessageReceived, ServerType, message.ToFormatMessageString()));
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
    /// C243 桥接：<c>Online.EnableFriendQueryBridge</c> 开启时，装配前先做拓扑 fail-fast，
    /// 业务运行时启动后发布好友查询桥租约并按配置建立验证种子；撤销由退出路径
    /// <see cref="ShutdownOnlineAdminAsync"/> 承担。
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

            // C243 桥接：业务运行时启动后发布好友查询桥租约（开关开启时），并按配置建立验证种子。
            if (Setting.Online?.EnableFriendQueryBridge == true)
            {
                OnlineFriendBridgeWiring.PublishFriendQueryBridge(_onlineRuntime);
                await OnlineFriendBridgeWiring.SeedFriendQueryBridgeVerificationAsync(_onlineRuntime, Setting.Online?.FriendQueryBridgeSeedPlayerIds);
            }

            // C245 桥接：业务运行时启动后发布对局桥租约（开关开启时），并确保写榜目标榜单存在。
            if (Setting.Online?.EnableMatchGameBridge == true)
            {
                OnlineMatchBridgeWiring.PublishMatchGameBridge(_onlineRuntime);
                await OnlineMatchBridgeWiring.EnsureMatchLeaderboardBoardAsync(_onlineRuntime, Setting.Online?.MatchBridgeLeaderboardId);
            }
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
        // C243 桥接：先撤销好友查询桥租约（旧引用显式失效），再停 Online admin 与 runtime 宿主。
        // C245 桥接：先撤销对局桥租约（旧引用显式失效），顺序同上（撤桥 → 停 admin → 停 runtime）。
        OnlineFriendBridgeWiring.RetractFriendQueryBridge();
        OnlineMatchBridgeWiring.RetractMatchGameBridge();

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
