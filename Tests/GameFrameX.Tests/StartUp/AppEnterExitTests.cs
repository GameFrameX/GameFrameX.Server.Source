// ==========================================================================================
//  GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//  GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//  均受中华人民共和国及相关国际法律法规保护。
//  are protected by the laws of the People's Republic of China and relevant international regulations.
//  
//  使用本项目须严格遵守相应法律法规及开源许可证之规定。
//  Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//  
//  本项目采用 Apache License 2.0 单协议分发，
//  This project is licensed solely under the Apache License 2.0,
//  完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//  please refer to the LICENSE file in the root directory of the source code for the full license text.
//  
//  禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//  It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//  侵犯他人合法权益等法律法规所禁止的行为！
//  or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//  因基于本项目二次开发所产生的一切法律纠纷与责任，
//  Any legal disputes and liabilities arising from secondary development based on this project
//  本项目组织与贡献者概不承担。
//  shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//  
//  GitHub 仓库：https://github.com/GameFrameX
//  GitHub Repository: https://github.com/GameFrameX
//  Gitee  仓库：https://gitee.com/GameFrameX
//  Gitee Repository:  https://gitee.com/GameFrameX
//  官方文档：https://gameframex.doc.alianblank.com/
//  Official Documentation: https://gameframex.doc.alianblank.com/
// ==========================================================================================

using System.Reflection;
using GameFrameX.StartUp.Abstractions;
using GameFrameX.Tests.Utility;
using GameFrameX.Utility.Setting;

namespace GameFrameX.Tests.StartUp;

[Collection(GameAppRuntimeCollection.Name)]
public sealed class AppEnterExitTests
{
    private const BindingFlags StaticNonPublic = BindingFlags.Static | BindingFlags.NonPublic;

    [Fact]
    public async Task HandleExit_ShouldRunShutdownAsTaskAndRejectDuplicateSignals()
    {
        var stopGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gameLoopGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var appStartUp = new TestAppStartUp(stopGate.Task);

        SetAppEnterField("_exitCalled", 0);
        SetAppEnterField("_exitTask", null);
        SetAppEnterField("_appStartUp", appStartUp);
        SetAppEnterField("_gameLoopTask", gameLoopGate.Task);

        try
        {
            InvokeHandleExit("first");
            InvokeHandleExit("second");

            await appStartUp.WaitStopStartedAsync();
            Assert.Equal(1, appStartUp.StopCallCount);

            var exitTask = GetAppEnterField<Task>("_exitTask");
            Assert.False(exitTask.IsCompleted);

            stopGate.SetResult();
            Assert.False(exitTask.IsCompleted);

            gameLoopGate.SetResult();
            await exitTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            SetAppEnterField("_exitCalled", 0);
            SetAppEnterField("_exitTask", null);
            SetAppEnterField("_appStartUp", null);
            SetAppEnterField("_gameLoopTask", null);
            SetAppExitHandlerField("_isKill", false);
        }
    }

    private static void InvokeHandleExit(string message)
    {
        var method = GetAppEnterType().GetMethod("HandleExit", StaticNonPublic);
        Assert.NotNull(method);
        method.Invoke(null, new object[] { message });
    }

    private static void SetAppEnterField(string fieldName, object value)
    {
        GetField(GetAppEnterType(), fieldName).SetValue(null, value);
    }

    private static T GetAppEnterField<T>(string fieldName)
    {
        return (T)GetField(GetAppEnterType(), fieldName).GetValue(null);
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

    private sealed class TestAppStartUp : IAppStartUp
    {
        private readonly Task _stopTask;
        private readonly TaskCompletionSource _stopStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _stopCallCount;

        public TestAppStartUp(Task stopTask)
        {
            _stopTask = stopTask;
        }

        public int StopCallCount => _stopCallCount;

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

        public async Task StopAsync(string message = "")
        {
            Interlocked.Increment(ref _stopCallCount);
            _stopStarted.SetResult();
            await _stopTask;
        }

        public async Task WaitStopStartedAsync()
        {
            await _stopStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}
