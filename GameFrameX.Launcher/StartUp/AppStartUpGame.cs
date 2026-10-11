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


using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Foundation.Utility;
using GameFrameX.Network.Abstractions;
using GameFrameX.Online.Runtime;
using GameFrameX.Online.Runtime.AdminApi;
using GameFrameX.Utility.Runtime;

namespace GameFrameX.Launcher.StartUp;

/// <summary>
/// 游戏服务器
/// </summary>
// 显式优先级修复与 Social 同为缺省 1000 的冲突——Game 为主服务先起（值越小优先级越高）
[StartUpTag(GameServerConst.Game.Name, 100)]
internal sealed class AppStartUpGame : AppStartUpGameServerBase
{
    /// <summary>
    /// Online Runtime 宿主（IsEnableOnlineAdmin 开启时装配；进程退出时随主流程停止）。
    /// </summary>
    /// <remarks>
    /// The Online runtime host (assembled when <c>IsEnableOnlineAdmin</c> is on; stopped with the
    /// main flow at process exit).
    /// </remarks>
    private OnlineRuntimeHost _onlineRuntime;

    /// <summary>
    /// Online admin HTTP 服务（与协议端口独立的 Kestrel 监听）。
    /// </summary>
    /// <remarks>
    /// The Online admin HTTP service (a Kestrel listener independent of the protocol ports).
    /// </remarks>
    private OnlineAdminApiServer _onlineAdminApi;

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
                // Localization: StartUp.TcpServer.MessageReceived - 接收到消息 - 服务器类型: [{0}], 消息内容: {1}
                LogHelper.Debug(LocalizationService.GetString(Localization.Keys.StartUp.TcpServer.MessageReceived, ServerType, message.ToFormatMessageString()));
            }

            return ValueTask.CompletedTask;
        }

        if (Setting.IsDebug && Setting.IsDebugReceive)
        {
            // Localization: StartUp.TcpServer.MessageReceived - 接收到消息 - 服务器类型: [{0}], 消息内容: {1}
            LogHelper.Debug(LocalizationService.GetString(Localization.Keys.StartUp.TcpServer.MessageReceived, ServerType, message.ToFormatMessageString()));
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Online admin 可选启动——仅在 <see cref="AppSetting.IsEnableOnlineAdmin"/> 为 true 时装配
    /// Online runtime 宿主与 Online admin HTTP 服务。
    /// </summary>
    /// <remarks>
    /// Starts the Online admin optionally — assembles the Online runtime host and the Online admin
    /// HTTP service only when <see cref="AppSetting.IsEnableOnlineAdmin"/> is true.
    /// </remarks>
    /// <returns>表示异步操作的任务 / A task representing the asynchronous operation</returns>
    private async Task StartOnlineAdminIfEnabledAsync()
    {
        if (Setting.IsEnableOnlineAdmin)
        {
            LogHelper.Info(LocalizationService.GetString(Localization.Keys.Launcher.OnlineAdminStartBegin, Setting.OnlineAdminPort));
            _onlineRuntime = new OnlineRuntimeHost(new OnlineRuntimeOptions
            {
                TenantId = Setting.OnlineTenantId,
                AppId = Setting.OnlineAppId,
                ServerId = Setting.ServerId,
                AdminPort = Setting.OnlineAdminPort,
                AdminApiPrefix = Setting.OnlineAdminApiPrefix,
            });
            await _onlineRuntime.StartAsync();
            _onlineAdminApi = new OnlineAdminApiServer(_onlineRuntime);
            await _onlineAdminApi.StartAsync();
            LogHelper.Info(LocalizationService.GetString(Localization.Keys.Launcher.OnlineAdminStartEnd, Setting.OnlineTenantId, Setting.OnlineAppId, Setting.ServerId));
        }
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
