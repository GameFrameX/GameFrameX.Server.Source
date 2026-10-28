// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes and liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
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


using System.Reflection;
using GameFrameX.StartUp;
using GameFrameX.StartUp.Abstractions;
using GameFrameX.Tests.Utility;
using GameFrameX.Utility.Setting;

namespace GameFrameX.Tests.StartUp;

/// <summary>
/// 退出钩子停机有界等待测试（GFX-1073 / C365）：幂等触发返回同一停机任务、等待核心有界返回、
/// 入口启动异常路径返回前等待已触发的停机任务。
/// </summary>
[Collection(GameAppRuntimeCollection.Name)]
public sealed class AppExitHandlerShutdownWaitTests
{
    private const BindingFlags StaticNonPublic = BindingFlags.Static | BindingFlags.NonPublic;

    /// <summary>
    /// HandleExit 幂等分支返回既有停机任务实例（等待锚点），而不是丢弃引用。
    /// </summary>
    [Fact]
    public async Task HandleExit_SecondInvocation_ReturnsSameExitTaskInstance()
    {
        var stopGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gameLoopGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var appStartUp = new GateHostStartUp(stopGate.Task);

        SetAppEnterField("_exitCalled", 0);
        SetAppEnterField("_exitTask", null);
        SetAppEnterField("_appStartUps", new List<IAppStartUp> { appStartUp });
        SetAppEnterField("_gameLoopTask", gameLoopGate.Task);

        try
        {
            var firstExitTask = InvokeHandleExit("first");
            var secondExitTask = InvokeHandleExit("second");

            Assert.NotNull(firstExitTask);
            Assert.Same(firstExitTask, secondExitTask);
            Assert.False(firstExitTask.IsCompleted);

            stopGate.SetResult();
            gameLoopGate.SetResult();
            await firstExitTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            SetAppEnterField("_exitCalled", 0);
            SetAppEnterField("_exitTask", null);
            SetAppEnterField("_appStartUps", null);
            SetAppEnterField("_gameLoopTask", null);
            SetAppExitHandlerField("_isKill", false);
        }
    }

    /// <summary>
    /// 等待核心对挂起的停机任务在超时上限内有界返回，不无限阻塞退出路径。
    /// </summary>
    [Fact]
    public async Task WaitForExitTaskCompletionAsync_HangingTask_ReturnsWithinBound()
    {
        var neverCompletes = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var waitTask = AppExitHandler.WaitForExitTaskCompletionAsync(neverCompletes.Task, TimeSpan.FromMilliseconds(200));

        await waitTask.WaitAsync(TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// 等待核心对已完成与已失败的任务立即返回且不抛出（停机任务异常已在别处观测）。
    /// </summary>
    [Fact]
    public async Task WaitForExitTaskCompletionAsync_CompletedTask_ReturnsWithoutWaiting()
    {
        await AppExitHandler.WaitForExitTaskCompletionAsync(null, TimeSpan.FromSeconds(30));
        await AppExitHandler.WaitForExitTaskCompletionAsync(Task.CompletedTask, TimeSpan.FromSeconds(30));

        var faulted = Task.FromException(new InvalidOperationException("shutdown failure already observed"));
        await AppExitHandler.WaitForExitTaskCompletionAsync(faulted, TimeSpan.FromSeconds(30));
    }

    /// <summary>
    /// 启动异常退栈路径在 EntryAsync 返回前有界等待已触发的停机任务：
    /// 预置挂起的停机任务未完成时入口不得提前返回，任务完成后才返回。
    /// </summary>
    [Fact]
    public async Task EntryAsync_StartupFailure_WaitsForPendingExitTaskBeforeReturn()
    {
        var pendingExitTask = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        SetAppEnterField("_exitCalled", 0);
        SetAppEnterField("_exitTask", pendingExitTask.Task);
        SetAppEnterField("_appStartUps", null);
        SetAppEnterField("_gameLoopTask", null);

        try
        {
            var entryTask = AppEnter.EntryAsync(new List<IAppStartUp> { new ThrowingStartUp() });

            // 启动异常已进 catch，但 finally 仍在等待预置的停机任务：入口不得提前返回
            Assert.False(entryTask.IsCompleted);

            pendingExitTask.SetResult();
            await entryTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            SetAppEnterField("_exitCalled", 0);
            SetAppEnterField("_exitTask", null);
            SetAppEnterField("_appStartUps", null);
            SetAppEnterField("_gameLoopTask", null);
            SetAppExitHandlerField("_isKill", false);
        }
    }

    private static Task InvokeHandleExit(string message)
    {
        var method = GetAppEnterType().GetMethod("HandleExit", StaticNonPublic);
        Assert.NotNull(method);
        return (Task)method.Invoke(null, new object[] { message });
    }

    private static void SetAppEnterField(string fieldName, object value)
    {
        GetField(GetAppEnterType(), fieldName).SetValue(null, value);
    }

    private static void SetAppExitHandlerField(string fieldName, object value)
    {
        GetField(GetAppExitHandlerType(), fieldName).SetValue(null, value);
    }

    private static FieldInfo GetField(Type type, string fieldName)
    {
        var field = type.GetField(fieldName, StaticNonPublic);
        Assert.NotNull(field);
        return field;
    }

    private static Type GetAppEnterType()
    {
        var type = typeof(IAppStartUp).Assembly.GetType("GameFrameX.StartUp.AppEnter");
        Assert.NotNull(type);
        return type;
    }

    private static Type GetAppExitHandlerType()
    {
        var type = typeof(IAppStartUp).Assembly.GetType("GameFrameX.StartUp.AppExitHandler");
        Assert.NotNull(type);
        return type;
    }

    /// <summary>
    /// StopAsync 阻塞在给定任务上的假宿主（让停机流程可控地挂起/放行）。
    /// </summary>
    private sealed class GateHostStartUp : IAppStartUp
    {
        private readonly Task _stopTask;

        public GateHostStartUp(Task stopTask)
        {
            _stopTask = stopTask;
        }

        public Task<string> AppExitToken { get; } = Task.FromResult("exit");

        public string ServerType => "test";

        public AppSetting Setting { get; } = new() { ServerType = "test" };

        public bool Init(string serverType, AppSetting setting, string[] args)
        {
            return true;
        }

        public Task StartAsync()
        {
            return Task.CompletedTask;
        }

        public Task StartUpReadyTask { get; } = Task.CompletedTask;

        public async Task StopAsync(string message = "")
        {
            await _stopTask;
        }
    }

    /// <summary>
    /// StartAsync 同步抛出异常的假宿主（触发入口启动异常退栈路径）。
    /// </summary>
    private sealed class ThrowingStartUp : IAppStartUp
    {
        public Task<string> AppExitToken { get; } = Task.FromResult("exit");

        public string ServerType => "test";

        public AppSetting Setting { get; } = new() { ServerType = "test" };

        public bool Init(string serverType, AppSetting setting, string[] args)
        {
            return true;
        }

        public Task StartAsync()
        {
            throw new InvalidOperationException("startup failure");
        }

        public Task StartUpReadyTask { get; } = Task.CompletedTask;

        public Task StopAsync(string message = "")
        {
            return Task.CompletedTask;
        }
    }
}
