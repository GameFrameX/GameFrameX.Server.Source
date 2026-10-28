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

using GameFrameX.StartUp.Abstractions;
using GameFrameX.Foundation.Logger;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Utility.Setting;
using GameFrameX.Utility.Runtime;

namespace GameFrameX.StartUp;

/// <summary>
/// 应用程序入口。
/// </summary>
/// <remarks>
/// App entry point.
/// Provides a unified entry and exit handling mechanism for the application, responsible for managing the application lifecycle.
/// </remarks>
internal static class AppEnter
{
    /// <summary>
    /// 指示是否已调用退出方法。
    /// </summary>
    /// <remarks>
    /// Indicates whether the exit method has been called.
    /// </remarks>
    /// <value>如果已调用退出则为 <c>true</c>；否则为 <c>false</c> / <c>true</c> if exit has been called; otherwise <c>false</c></value>
    private static int _exitCalled;

    /// <summary>
    /// 主游戏循环任务。
    /// </summary>
    /// <remarks>
    /// The main game loop task.
    /// </remarks>
    /// <value>表示游戏循环执行的任务 / The task representing the game loop execution</value>
    private static volatile Task _gameLoopTask;

    /// <summary>
    /// 应用程序退出任务。
    /// </summary>
    /// <remarks>
    /// Application exit task.
    /// </remarks>
    private static volatile Task _exitTask;

    /// <summary>
    /// 应用程序启动实例集合（按启动优先级排序；单 Role 为单元素集合）。
    /// </summary>
    /// <remarks>
    /// The application startup instances, ordered by launch priority (a single-role process holds a one-element list).
    /// </remarks>
    /// <value>用于管理应用程序生命周期的启动实例集合 / The startup instances used to manage application lifecycle</value>
    private static volatile IReadOnlyList<IAppStartUp> _appStartUps;

    /// <summary>
    /// 优雅停机总超时时间。
    /// </summary>
    /// <remarks>
    /// The total timeout for graceful shutdown.
    /// 兜底防止某个 Role 的 StopAsync 挂起导致进程永久卡在关闭流程（容器平台强杀前自主退出）；
    /// 建议保持略小于容器平台的 terminationGracePeriodSeconds。
    /// </remarks>
    internal static readonly TimeSpan GracefulShutdownTotalTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 应用程序启动入口点。
    /// </summary>
    /// <remarks>
    /// Application startup entry point.
    /// Initializes the application, sets up exit handlers, and starts the main game loop of every hosted role.
    /// Hosts are started sequentially in list order (higher priority first): the launcher awaits
    /// the current role's startup-ready signal before starting the next one, so a lower-priority role never
    /// initializes while a higher-priority role is still bringing up shared infrastructure (databases, components).
    /// All run-until-exit loops are then awaited together; exit stops the hosts in reverse order (lower priority first).
    /// Before returning (normal or failed startup), any already-triggered shutdown task is awaited with a bound
    /// so the process does not exit mid-shutdown and truncate in-flight writes or the log flush.
    /// </remarks>
    /// <param name="appStartUps">应用程序启动实例集合（启动优先级序）/ Application startup instances (launch priority order)</param>
    /// <returns>表示异步操作的任务 / A task representing the asynchronous operation</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="appStartUps"/> 为 null 时抛出 / Thrown when <paramref name="appStartUps"/> is null</exception>
    /// <exception cref="ArgumentException">当 <paramref name="appStartUps"/> 为空集合时抛出 / Thrown when <paramref name="appStartUps"/> is empty</exception>
    internal static async Task EntryAsync(IReadOnlyList<IAppStartUp> appStartUps)
    {
        ArgumentNullException.ThrowIfNull(appStartUps);
        if (appStartUps.Count == 0)
        {
// Localization: StartUp.Application.StartupInstanceRequired - 至少需要一个应用程序启动实例
            throw new ArgumentException(LocalizationService.GetString(Localization.Keys.StartUp.Application.StartupInstanceRequired), nameof(appStartUps));
        }

        try
        {
            _appStartUps = appStartUps;
            AppExitHandler.Init(HandleExit, appStartUps[0].Setting);
            var startUpTasks = await StartHostsInOrderAsync(appStartUps);

            _gameLoopTask = startUpTasks.Count == 1 ? startUpTasks[0] : Task.WhenAll(startUpTasks);
            await _gameLoopTask;
        }
        catch (Exception e)
        {
            string error;
            if (GameAppRuntime.IsRunning)
            {
                error = $"abnormal server runtime:{e}";
            }
            else
            {
                error = $"failed to start the server:{e}";
            }

            LogHelper.Error(error);
        }
        finally
        {
            // 无论正常退出还是启动异常退栈，都在返回前有界等待已触发的停机任务，
            // 防止进程在停机流程（含日志刷盘）完成前退出截断写操作（GFX-1073）
            await AppExitHandler.WaitForExitTaskCompletionAsync(_exitTask, AppExitHandler.ShutdownWaitTimeout);
        }
    }

    /// <summary>
    /// 按优先级顺序依次启动宿主，并在两个宿主之间等待启动就绪屏障。
    /// </summary>
    /// <remarks>
    /// Starts the hosts sequentially in list order, awaiting the current role's
    /// <see cref="IAppStartUp.StartUpReadyTask"/> before starting the next one (priority startup barrier),
    /// so Game's databases and components are fully up before Social starts initializing.
    /// A role that exits (or fails) before reporting readiness also releases the barrier,
    /// preventing a missing ready signal from deadlocking the chain; its failure still surfaces
    /// through the aggregated loop task. Returns the run-until-exit task of every host.
    /// </remarks>
    /// <param name="appStartUps">启动实例集合（启动优先级序）/ Application startup instances (launch priority order)</param>
    /// <returns>每个宿主的运行直到退出任务 / The run-until-exit task of every host</returns>
    internal static async Task<List<Task>> StartHostsInOrderAsync(IReadOnlyList<IAppStartUp> appStartUps)
    {
        var startUpTasks = new List<Task>(appStartUps.Count);
        foreach (var appStartUp in appStartUps)
        {
            var runTask = appStartUp.StartAsync();
            startUpTasks.Add(runTask);

            // 启动屏障：等待当前 Role 报告启动就绪（或其运行任务先行完成）后再启动下一个 Role；
            // 运行任务异常保留在聚合任务中，由 Entry 统一观测
            var startUpReadyTask = appStartUp.StartUpReadyTask;
            if (startUpReadyTask != null)
            {
                await Task.WhenAny(startUpReadyTask, runTask);
            }
        }

        return startUpTasks;
    }

    /// <summary>
    /// 处理应用程序退出。
    /// </summary>
    /// <remarks>
    /// Handles application exit.
    /// Ensures the application only exits once and performs necessary cleanup operations.
    /// Returns the shutdown task (a repeated invocation returns the already-running one) so exit
    /// hooks and the entry point can await graceful shutdown with a bound instead of fire-and-forget.
    /// </remarks>
    /// <param name="message">退出消息 / Exit message</param>
    /// <returns>停机任务（重复触发时返回既有任务）/ The shutdown task (the existing one on repeated triggers)</returns>
    private static Task HandleExit(string message)
    {
        if (Interlocked.Exchange(ref _exitCalled, 1) == 1)
        {
            return _exitTask;
        }

        _exitTask = Task.Run(() => HandleExitAsync(message));
        return _exitTask;
    }

    /// <summary>
    /// 异步处理应用程序退出。
    /// </summary>
    /// <remarks>
    /// Handles application exit asynchronously.
    /// Stops every hosted role in reverse launch order (lower priority first) under a total
    /// graceful-shutdown timeout; a role-level stop failure is recorded and the remaining roles
    /// are still stopped, so the exit procedure (kill and log flush) always runs to completion.
    /// If the stop sequence does not finish within <see cref="GracefulShutdownTotalTimeout"/>,
    /// logs the error, flushes the logs, and forces process exit before the container platform
    /// would hard-kill the process.
    /// </remarks>
    /// <param name="message">退出消息 / Exit message</param>
    private static async Task HandleExitAsync(string message)
    {
        // Localization: StartUp.Application.ListeningExitMessage - 监听程序退出消息
        LogHelper.Info(LocalizationService.GetString(Localization.Keys.StartUp.Application.ListeningExitMessage));
        try
        {
            GameAppRuntime.MarkStopping();
            var gracefulShutdownCompleted = true;
            if (_appStartUps != null)
            {
                try
                {
                    gracefulShutdownCompleted = await StopHostsInReverseOrderWithTimeoutAsync(_appStartUps, message, GracefulShutdownTotalTimeout);
                }
                catch (Exception e)
                {
                    // 逆序停机部分失败：全部失败已在 StopHostsInReverseOrderAsync 中逐个记录并聚合于此，
                    // 退出流程继续执行，确保 Kill 与日志冲刷仍然运行
                    // Localization: StartUp.Application.AbnormalServerStop - 服务器异常停止:{0}
                    LogHelper.Error(LocalizationService.GetString(Localization.Keys.StartUp.Application.AbnormalServerStop, e));
                }
            }

            if (!gracefulShutdownCompleted)
            {
                LogHelper.Error($"[AppEnter] graceful shutdown timed out after {GracefulShutdownTotalTimeout.TotalSeconds} seconds, forcing exit");
                LogHelper.FlushAndSave();
                Environment.Exit(1);
            }

            AppExitHandler.Kill();
            // Localization: StartUp.Application.ExecutingExitProcedure - 执行退出程序
            LogHelper.Info(LocalizationService.GetString(Localization.Keys.StartUp.Application.ExecutingExitProcedure));
            if (_gameLoopTask != null)
            {
                await _gameLoopTask;
            }
        }
        finally
        {
            LogHelper.FlushAndSave();
        }
    }

    /// <summary>
    /// 带总超时执行逆序停机：超时返回 false，交由调用方记录、刷盘并强制退出。
    /// </summary>
    /// <remarks>
    /// Runs the reverse-order stop sequence under a total timeout: returns <c>false</c> when the
    /// sequence does not complete within <paramref name="timeout"/> (e.g. a hanging StopAsync),
    /// so the caller can log, flush, and force-exit instead of blocking forever. On success the
    /// stop task is awaited to surface its failures.
    /// </remarks>
    /// <param name="appStartUps">启动实例集合（启动优先级序）/ Application startup instances (launch priority order)</param>
    /// <param name="message">退出消息 / Exit message</param>
    /// <param name="timeout">总超时时间 / Total timeout</param>
    /// <returns>停机序列是否在超时内完成 / Whether the stop sequence completed within the timeout</returns>
    internal static async Task<bool> StopHostsInReverseOrderWithTimeoutAsync(IReadOnlyList<IAppStartUp> appStartUps, string message, TimeSpan timeout)
    {
        var stopTask = StopHostsInReverseOrderAsync(appStartUps, message);
        var timeoutTask = Task.Delay(timeout);
        if (await Task.WhenAny(stopTask, timeoutTask) == timeoutTask)
        {
            return false;
        }

        await stopTask;
        return true;
    }
    /// <summary>
    /// 按启动顺序的逆序依次停机（低优先级先停）。
    /// </summary>
    /// <remarks>
    /// Stops the hosts sequentially in reverse launch order (lower priority stops first),
    /// mirroring the start order: the last started role is the first stopped one.
    /// A role-level stop failure is caught, logged, and does not abort the loop — the remaining
    /// (higher priority) roles are still stopped; after all roles have been processed the recorded
    /// failures are rethrown as one <see cref="AggregateException"/> so the caller can observe them.
    /// </remarks>
    /// <param name="appStartUps">启动实例集合（启动优先级序）/ Application startup instances (launch priority order)</param>
    /// <param name="message">退出消息 / Exit message</param>
    /// <exception cref="AggregateException">当存在 Role 停机失败时抛出（在全部 Role 处理完毕后）/ Thrown when any role failed to stop (after every role has been processed)</exception>
    internal static async Task StopHostsInReverseOrderAsync(IReadOnlyList<IAppStartUp> appStartUps, string message)
    {
        List<Exception> stopFailures = null;
        for (var index = appStartUps.Count - 1; index >= 0; index--)
        {
            var appStartUp = appStartUps[index];
            if (appStartUp == null)
            {
                continue;
            }

            try
            {
                await appStartUp.StopAsync(message);
            }
            catch (Exception e)
            {
                // 单个 Role 停机失败不阻断其余 Role 的逆序停机：记录后继续，最后统一聚合抛出
                // Localization: StartUp.Application.StopServerError - 停止服务器角色 '{0}' 时出错:{1}
                LogHelper.Error(LocalizationService.GetString(Localization.Keys.StartUp.Application.StopServerError, appStartUp.ServerType, e));
                (stopFailures ??= new List<Exception>()).Add(e);
            }
        }

        if (stopFailures != null)
        {
            throw new AggregateException(stopFailures);
        }
    }
}
