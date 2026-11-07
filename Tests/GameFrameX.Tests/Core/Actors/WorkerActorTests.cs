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

using GameFrameX.Core.Abstractions;
using GameFrameX.Core.Actors.Impl;
using GameFrameX.Tests.Utility;
using GameFrameX.Utility.Setting;
using System.Reflection;
using System.Threading.Tasks.Dataflow;

namespace GameFrameX.Tests.Core.Actors;

[Collection(GameAppRuntimeCollection.Name)]
public sealed class WorkerActorTests
{
    private static readonly object SettingsLock = new();

    [Fact]
    public async Task SendAsync_WithCancelledToken_ShouldCompleteAsCancelled()
    {
        EnsureSettings();
        var worker = new WorkerActor(1);
        using (var cancellationTokenSource = new CancellationTokenSource())
        {
            cancellationTokenSource.Cancel();
            var executed = false;

            var task = worker.SendAsync(() => executed = true, cancellationToken: cancellationTokenSource.Token);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.WaitAsync(TimeSpan.FromSeconds(1)));
            Assert.False(executed);
        }
    }

    [Fact]
    public async Task SendAsync_WhenQueueIsCompleted_ShouldCompleteAsRejected()
    {
        EnsureSettings();
        var worker = new WorkerActor(2);
        var executed = false;
        var queue = GetQueue(worker);
        queue.Complete();
        await queue.Completion.WaitAsync(TimeSpan.FromSeconds(1));

        var task = worker.SendAsync(() => executed = true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => task.WaitAsync(TimeSpan.FromSeconds(1)));
        Assert.False(executed);
    }

    [Fact]
    public async Task SendAsync_ShouldRunAcceptedWorkInOrder()
    {
        EnsureSettings();
        var worker = new WorkerActor(3);
        var order = new List<int>();

        var first = worker.SendAsync(() => order.Add(1), timeOut: 5000);
        var second = worker.SendAsync(() => order.Add(2), timeOut: 5000);

        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { 1, 2 }, order);
    }

    [Fact]
    public async Task SendAsync_Timeout_ShouldReleaseWaiterWithTimeoutException()
    {
        EnsureSettings();
        var worker = new WorkerActor(101);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var task = worker.SendAsync(() => release.Task, 80, true);

        // 契约：等待方以 TimeoutException 观测超时，而非假成功（false/default）
        await Assert.ThrowsAsync<TimeoutException>(() => task.WaitAsync(TimeSpan.FromSeconds(5)));
        release.TrySetResult();
    }

    [Fact]
    public async Task SendAsync_Timeout_ShouldNotStartNextWorkWhileOrphanRuns()
    {
        EnsureSettings();
        var worker = new WorkerActor(102);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = false;

        var first = worker.SendAsync(() => release.Task, 80, true);
        var second = worker.SendAsync(() => secondStarted = true, timeOut: 5000);

        await Assert.ThrowsAsync<TimeoutException>(() => first);
        await Task.Delay(150);

        // 串行不变量：孤儿任务未结束，下一条消息不得开始（旧实现此处已并发进入）
        Assert.False(secondStarted);

        release.TrySetResult();
        await second.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(secondStarted);
    }

    [Fact]
    public async Task SendAsync_AfterTimedOutOrphan_ReentrantCallShouldRunInline()
    {
        EnsureSettings();
        var worker = new WorkerActor(103);
        var order = new List<int>();

        // 孤儿任务 400ms 后自然结束（80ms 已超时放行等待方）
        var first = worker.SendAsync(() => Task.Delay(400), 80, true);
        var second = worker.SendAsync(async () =>
        {
            order.Add(2);

            // 重入调用：链上下文未被孤儿误清时应内联执行；被误清则入队自等待 → 死锁超时
            await worker.SendAsync(() => order.Add(3), timeOut: 5000);
            order.Add(4);
        }, 5000, true);

        await Assert.ThrowsAsync<TimeoutException>(() => first);
        await second.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { 2, 3, 4 }, order);
    }

    [Fact]
    public async Task SendAsync_OrphanExceedsHardCap_ShouldQuarantineActorAndFailQueuedWork()
    {
        EnsureSettings();
        var worker = new WorkerActor(104);
        var secondStarted = false;

        // 永不结束的孤儿：80ms 超时 → 硬上限 800ms 后隔离
        var first = worker.SendAsync(() => new TaskCompletionSource().Task, 80, true);
        var second = worker.SendAsync(() => secondStarted = true, timeOut: 5000);

        await Assert.ThrowsAsync<TimeoutException>(() => first);
        await Assert.ThrowsAsync<InvalidOperationException>(() => second.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(worker.IsQuarantined);
        Assert.False(secondStarted);

        // 隔离后新工作单元快速失败
        var third = worker.SendAsync(() => secondStarted = true, timeOut: 5000);
        await Assert.ThrowsAsync<InvalidOperationException>(() => third.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.False(secondStarted);
    }

    [Fact]
    public async Task SendAsync_WorkThrows_ShouldPropagateExceptionToWaiter()
    {
        EnsureSettings();
        var worker = new WorkerActor(105);

        var task = worker.SendAsync((Func<int>)(() => throw new InvalidOperationException("work failed")), 5000);

        // 契约：工作异常传播给等待方，而非以 default(T) 假成功
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("work failed", exception.Message);
    }

    [Fact]
    public async Task SendAsync_Timeout_ShouldCancelExecutionTokenVisibleInRuntimeContext()
    {
        EnsureSettings();
        var worker = new WorkerActor(106);
        var observedCanceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken observedToken = CancellationToken.None;

        var task = worker.SendAsync(async () =>
        {
            observedToken = RuntimeContext.CurrentCancellationToken;
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), RuntimeContext.CurrentCancellationToken);
            }
            catch (OperationCanceledException)
            {
                observedCanceled.TrySetResult();
            }
        }, 80, true);

        // 等待方在窗口内被放行即可：CancelAfter 与 WaitAsync 同刻竞速，
        // 协作取消先令工作收尾时等待方为正常完成，否则为 TimeoutException——两种结果皆符合契约
        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException)
        {
        }

        await observedCanceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(observedToken.CanBeCanceled);
        Assert.True(observedToken.IsCancellationRequested);
    }

    [Fact]
    public async Task SendAsync_CallerCancel_ShouldCancelRunningWork()
    {
        EnsureSettings();
        var worker = new WorkerActor(107);
        using (var cancellationTokenSource = new CancellationTokenSource())
        {
            var workStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            var task = worker.SendAsync(async () =>
            {
                workStarted.TrySetResult();
                await Task.Delay(TimeSpan.FromSeconds(30), RuntimeContext.CurrentCancellationToken);
            }, 30000, true, cancellationTokenSource.Token);

            await workStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellationTokenSource.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.WaitAsync(TimeSpan.FromSeconds(5)));
        }
    }

    private static IDataflowBlock GetQueue(WorkerActor worker)
    {
        var property = typeof(WorkerActor).GetProperty("ActionBlock", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(property);
        return Assert.IsAssignableFrom<IDataflowBlock>(property!.GetValue(worker));
    }

    private static void EnsureSettings()
    {
        if (GlobalSettings.CurrentSetting != null)
        {
            return;
        }

        lock (SettingsLock)
        {
            if (GlobalSettings.CurrentSetting == null)
            {
                GlobalSettings.SetCurrentSetting(new AppSetting { ServerId = 1 });
            }
        }
    }
}
