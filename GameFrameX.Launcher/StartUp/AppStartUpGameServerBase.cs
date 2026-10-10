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


using GameFrameX.Discovery;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Foundation.Utility;

namespace GameFrameX.Launcher.StartUp;

/// <summary>
/// 游戏服务器标准序列模板：固化「控制库 → 发现层 → 业务库 → 组件注册 → Role 服务」共享启动序列
/// 与退出兜底，差异点全部经 hook 下放（<see cref="PrepareAsync"/> / <see cref="StartRoleServicesAsync"/> /
/// <see cref="OnStartupCompletedAsync"/> / <see cref="ShutdownAsync(string)"/>）。
/// </summary>
/// <remarks>
/// The standard game-server sequence template: freezes the shared startup sequence
/// (control database → discovery layer → business database → component registration → role services)
/// together with the exit catch-all, pushing every difference down to hooks. 序列方法 sealed 是刻意选择：
/// 防止装配序列在派生类再分叉；自拟序列的异形 Role 直承 <see cref="AppStartUpRoleBase"/> 复用护栏、
/// 缺省配置收口与装配件自写序列。
/// Sealing the sequence is deliberate — it prevents the assembly sequence from forking again in
/// derived classes; free-form roles derive <see cref="AppStartUpRoleBase"/> instead and reuse the
/// stop guard, the default-setting funnel and the wiring parts with their own sequence.
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
internal abstract class AppStartUpGameServerBase : AppStartUpRoleBase
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
    /// Role 服务启动 hook：共享装配（控制库/发现层/业务库/组件）就绪后执行本 Role 的专属服务装配。
    /// Game 型在此装载热更模块与可选 Online admin；Social 型在此装载进程内热更并启动网络监听。
    /// </summary>
    /// <remarks>
    /// The role-services hook: runs after the shared wiring (control database, discovery layer,
    /// business database, components) is ready and assembles this role's own services. The Game
    /// shape loads the hotfix module and the optional Online admin here; the Social shape loads
    /// the in-process hotfix and starts its network listeners here.
    /// </remarks>
    /// <returns>表示 hook 操作的异步任务 / A task representing the hook operation</returns>
    protected abstract Task StartRoleServicesAsync();

    /// <summary>
    /// 启动完成 hook：Role 服务就绪后调用，默认空实现——Game 型在此标记进程运行时已启动
    ///（<c>GameAppRuntime.MarkStarted</c>）；Social 型在此分发 <c>ServiceOnline</c> 事件。
    /// </summary>
    /// <remarks>
    /// The startup-completed hook: invoked once this role's services are up; the default is a
    /// no-op. The Game shape marks the process runtime started (<c>GameAppRuntime.MarkStarted</c>)
    /// here; the Social shape dispatches the <c>ServiceOnline</c> event here.
    /// </remarks>
    /// <returns>表示 hook 操作的异步任务 / A task representing the hook operation</returns>
    protected virtual Task OnStartupCompletedAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// 退出清理 hook：退出兜底日志之后、退出完成日志之前调用——Game 型停 Online admin 与热更模块；
    /// Social 型自停（护栏内恰好一次）。
    /// </summary>
    /// <remarks>
    /// The exit-cleanup hook: called between the exit-begin and exit-success logs. The Game shape
    /// stops the Online admin and the hotfix module here; the Social shape self-stops here
    /// (exactly once behind the stop guard).
    /// </remarks>
    /// <param name="exitMessage">退出原因（可能为 null——启动失败路径未获得退出消息）/ The exit reason (may be null on the startup-failure path)</param>
    /// <returns>表示 hook 操作的异步任务 / A task representing the hook operation</returns>
    protected abstract Task ShutdownAsync(string exitMessage);
}
