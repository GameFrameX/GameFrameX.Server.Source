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
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes and liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository:  https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:  https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using GameFrameX.StartUp;
using GameFrameX.StartUp.Abstractions;
using GameFrameX.Utility.Setting;
using Xunit;

namespace GameFrameX.Tests.StartUp;

/// <summary>单宿主的本地就绪等待、异常与有界停止。</summary>
public class AppEnterLifecycleTests
{
    [Fact]
    public async Task WaitForReadyAsync_WaitsForLocalReadiness()
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new LifecycleStartUp(ready.Task);
        var wait = AppEnter.WaitForReadyAsync(host, running.Task);
        Assert.False(wait.IsCompleted);
        ready.SetResult();
        await wait.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(running.Task.IsCompleted);
    }

    [Fact]
    public async Task WaitForReadyAsync_RunFailureBeforeReadiness_Propagates()
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failure = new InvalidOperationException("startup failure");
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => AppEnter.WaitForReadyAsync(new LifecycleStartUp(ready.Task), Task.FromException(failure)));
        Assert.Same(failure, exception);
    }

    [Fact]
    public async Task WaitForReadyAsync_ReadyFailure_Propagates()
    {
        var failure = new InvalidOperationException("ready failure");
        var running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => AppEnter.WaitForReadyAsync(new LifecycleStartUp(Task.FromException(failure)), running.Task));
        Assert.Same(failure, exception);
    }

    [Fact]
    public async Task WaitForReadyAsync_RunEndsBeforeReadiness_DoesNotHang()
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await AppEnter.WaitForReadyAsync(new LifecycleStartUp(ready.Task), Task.CompletedTask).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(ready.Task.IsCompleted);
    }

    [Fact]
    public async Task StopHostWithTimeoutAsync_StopsHostOnceAndPassesReason()
    {
        var host = new LifecycleStartUp(Task.CompletedTask);
        Assert.True(await AppEnter.StopHostWithTimeoutAsync(host, "shutdown", TimeSpan.FromSeconds(5)));
        Assert.Equal(1, host.StopCount);
        Assert.Equal("shutdown", host.StopReason);
    }

    [Fact]
    public async Task StopHostWithTimeoutAsync_StopFailure_Propagates()
    {
        var failure = new InvalidOperationException("stop failure");
        var host = new LifecycleStartUp(Task.CompletedTask) { StopOperation = () => Task.FromException(failure) };
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => AppEnter.StopHostWithTimeoutAsync(host, "shutdown", TimeSpan.FromSeconds(5)));
        Assert.Same(failure, exception);
    }

    [Fact]
    public async Task StopHostWithTimeoutAsync_HangingStop_ReturnsFalse()
    {
        var stop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var host = new LifecycleStartUp(Task.CompletedTask) { StopOperation = () => stop.Task };
        Assert.False(await AppEnter.StopHostWithTimeoutAsync(host, "shutdown", TimeSpan.FromMilliseconds(100)));
        Assert.Equal(1, host.StopCount);
        stop.SetResult();
    }

    [Fact]
    public async Task StopHostWithTimeoutAsync_SynchronouslyBlockedStop_RemainsBounded()
    {
        using (var release = new ManualResetEventSlim())
        {
            var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var host = new LifecycleStartUp(Task.CompletedTask)
            {
                StopOperation = () =>
                {
                    release.Wait();
                    completed.SetResult();
                    return Task.CompletedTask;
                },
            };
            try
            {
                Assert.False(await AppEnter.StopHostWithTimeoutAsync(host, "shutdown", TimeSpan.FromMilliseconds(100)).WaitAsync(TimeSpan.FromSeconds(5)));
            }
            finally
            {
                release.Set();
                await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
    }

    private sealed class LifecycleStartUp : IAppStartUp
    {
        public LifecycleStartUp(Task readyTask)
        {
            StartUpReadyTask = readyTask;
        }

        public Task<string> AppExitToken { get; } = Task.FromResult("exit");
        public string ServerType => "Game";
        public AppSetting Setting { get; } = new() { ServerType = "Game" };
        public Task StartUpReadyTask { get; }
        public Func<Task> StopOperation { get; set; } = () => Task.CompletedTask;
        public int StopCount { get; private set; }
        public string StopReason { get; private set; }
        public bool Init(string serverType, AppSetting setting, string[] args = null)
        {
            return true;
        }

        public Task StartAsync()
        {
            return Task.CompletedTask;
        }

        public Task StopAsync(string message = "")
        {
            StopCount++;
            StopReason = message;
            return StopOperation();
        }
    }
}
