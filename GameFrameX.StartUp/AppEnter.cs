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
using System.Diagnostics;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Utility.Setting;
using GameFrameX.Utility.Runtime;

namespace GameFrameX.StartUp;

/// <summary>单宿主运行、就绪等待与幂等退出。</summary>
internal static class AppEnter
{
    private static readonly object ExitGate = new();
    private static volatile Task _gameLoopTask;
    private static volatile Task _exitTask;
    private static volatile IAppStartUp _appStartUp;

    internal static readonly TimeSpan GracefulShutdownTotalTimeout = TimeSpan.FromSeconds(30);

    /// <summary>运行唯一宿主，启动失败和正常结束都必须执行清理。</summary>
    internal static async Task EntryAsync(IAppStartUp appStartUp)
    {
        ArgumentNullException.ThrowIfNull(appStartUp);
        lock (ExitGate)
        {
            if (_appStartUp != null)
            {
                throw new InvalidOperationException("An application host is already running.");
            }

            _appStartUp = appStartUp;
            _exitTask = null;
        }

        try
        {
            AppExitHandler.Init(HandleExit, appStartUp.Setting);
            _gameLoopTask = appStartUp.StartAsync();
            await WaitForReadyAsync(appStartUp, _gameLoopTask);
            if (!_gameLoopTask.IsCompleted)
            {
                LogHelper.Info(LocalizationService.GetString(Localization.Keys.StartUp.StartupOver));
            }

            await _gameLoopTask;
        }
        catch (Exception exception)
        {
            LogHelper.Error(exception.ToString());
            throw;
        }
        finally
        {
            await AppExitHandler.WaitForExitTaskCompletionAsync(HandleExit("Application host completed"), AppExitHandler.ShutdownWaitTimeout);
            AppExitHandler.Release();
            lock (ExitGate)
            {
                _appStartUp = null;
                _gameLoopTask = null;
            }
        }
    }

    /// <summary>等待本宿主就绪；提前终止和失败均被观测，不留悬挂屏障。</summary>
    internal static async Task WaitForReadyAsync(IAppStartUp appStartUp, Task runTask)
    {
        ArgumentNullException.ThrowIfNull(appStartUp);
        ArgumentNullException.ThrowIfNull(runTask);
        var readyTask = appStartUp.StartUpReadyTask ?? throw new InvalidOperationException("Startup ready task is required.");
        var completed = await Task.WhenAny(readyTask, runTask);
        await completed;
    }

    private static Task HandleExit(string message)
    {
        lock (ExitGate)
        {
            return _exitTask ??= Task.Run(() => HandleExitAsync(message));
        }
    }

    private static async Task HandleExitAsync(string message)
    {
        var shutdownClock = Stopwatch.StartNew();
        try
        {
            GameAppRuntime.MarkStopping();
            if (_appStartUp != null)
            {
                try
                {
                    if (!await StopHostWithTimeoutAsync(_appStartUp, message, GracefulShutdownTotalTimeout))
                    {
                        LogHelper.Error($"Application shutdown timed out after {GracefulShutdownTotalTimeout.TotalSeconds} seconds.");
                        LogHelper.FlushAndSave();
                        Environment.Exit(1);
                    }
                }
                catch (Exception exception)
                {
                    LogHelper.Error(LocalizationService.GetString(Localization.Keys.StartUp.Application.AbnormalServerStop, exception));
                }
            }

            AppExitHandler.Kill();
            GameAppRuntime.MarkStopped(message);
            if (_gameLoopTask != null)
            {
                var remaining = GracefulShutdownTotalTimeout - shutdownClock.Elapsed;
                if (remaining <= TimeSpan.Zero && !_gameLoopTask.IsCompleted)
                {
                    throw new TimeoutException("Application shutdown exceeded its total timeout.");
                }

                await _gameLoopTask.WaitAsync(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
            }
        }
        finally
        {
            GameAppRuntime.MarkStopped(message);
            LogHelper.FlushAndSave();
        }
    }

    /// <summary>对单宿主停机执行总超时，保留清理失败的异常。</summary>
    internal static async Task<bool> StopHostWithTimeoutAsync(IAppStartUp appStartUp, string message, TimeSpan timeout)
    {
        ArgumentNullException.ThrowIfNull(appStartUp);
        // 即使 StopAsync 在返回 Task 前同步阻塞，停机也必须受总超时约束。
        var stopTask = Task.Run(() => appStartUp.StopAsync(message));
        try
        {
            await stopTask.WaitAsync(timeout);
            return true;
        }
        catch (TimeoutException) when (!stopTask.IsCompleted)
        {
            _ = stopTask.ContinueWith(completed => LogHelper.Error(completed.Exception.ToString()),
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            return false;
        }
    }
}
