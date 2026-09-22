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

/// <summary>
/// AppEnter 逆序停机与按优先级启动屏障测试（C143b D7：启动优先级序拉起、就绪屏障、低优先级先停）。
/// </summary>
public class AppEnterShutdownOrderTests
{
    /// <summary>
    /// 多宿主按启动顺序的逆序停机（最后启动的先停）。
    /// </summary>
    [Fact]
    public async Task StopHostsInReverseOrderAsync_MultipleHosts_StopsInReverseLaunchOrder()
    {
        var stopOrder = new List<string>();
        var hosts = new List<IAppStartUp>
        {
            new RecordingStartUp("Alpha", stopOrder),
            new RecordingStartUp("Beta", stopOrder),
            new RecordingStartUp("Gamma", stopOrder),
        };

        await AppEnter.StopHostsInReverseOrderAsync(hosts, "unit-test");

        Assert.Equal(new[] { "Gamma", "Beta", "Alpha" }, stopOrder);
    }

    /// <summary>
    /// 单宿主（现状形态）只停自身。
    /// </summary>
    [Fact]
    public async Task StopHostsInReverseOrderAsync_SingleHost_StopsTheHost()
    {
        var stopOrder = new List<string>();
        var hosts = new List<IAppStartUp> { new RecordingStartUp("Alpha", stopOrder) };

        await AppEnter.StopHostsInReverseOrderAsync(hosts, "unit-test");

        Assert.Equal(new[] { "Alpha" }, stopOrder);
    }

    /// <summary>
    /// 集合中的 null 宿主被跳过，不影响其余宿主的停机顺序。
    /// </summary>
    [Fact]
    public async Task StopHostsInReverseOrderAsync_NullEntry_Skipped()
    {
        var stopOrder = new List<string>();
        var hosts = new List<IAppStartUp>
        {
            new RecordingStartUp("Alpha", stopOrder),
            null,
            new RecordingStartUp("Gamma", stopOrder),
        };

        await AppEnter.StopHostsInReverseOrderAsync(hosts, "unit-test");

        Assert.Equal(new[] { "Gamma", "Alpha" }, stopOrder);
    }

    /// <summary>
    /// 单个 Role 停机失败不阻断其余 Role：全部宿主仍按逆序停机，失败以 AggregateException 聚合抛出。
    /// </summary>
    [Fact]
    public async Task StopHostsInReverseOrderAsync_FailingHost_StillStopsRemainingHostsAndThrowsAggregate()
    {
        var stopOrder = new List<string>();
        var hosts = new List<IAppStartUp>
        {
            new RecordingStartUp("Alpha", stopOrder),
            new ThrowingStopStartUp("Beta"),
            new RecordingStartUp("Gamma", stopOrder),
        };

        var aggregateException = await Assert.ThrowsAsync<AggregateException>(() => AppEnter.StopHostsInReverseOrderAsync(hosts, "unit-test"));

        Assert.Equal(new[] { "Gamma", "Alpha" }, stopOrder);
        var failure = Assert.Single(aggregateException.InnerExceptions);
        Assert.IsType<InvalidOperationException>(failure);
    }

    /// <summary>
    /// 启动就绪屏障：下一个 Role 在前一个 Role 报告启动就绪前不得启动。
    /// </summary>
    [Fact]
    public async Task StartHostsInOrderAsync_NextRoleWaitsForPreviousStartUpReady()
    {
        var hostAlpha = new BarrierStartUp("Alpha");
        var hostBeta = new BarrierStartUp("Beta");
        var hosts = new List<IAppStartUp> { hostAlpha, hostBeta };

        var startTask = Task.Run(() => AppEnter.StartHostsInOrderAsync(hosts));

        // Alpha 已启动但未报告就绪：Beta 不得启动
        await hostAlpha.WaitStartedAsync();
        Assert.Equal(0, hostBeta.StartCallCount);

        hostAlpha.MarkReady();
        await hostBeta.WaitStartedAsync();

        // 收尾：放行两个宿主的运行任务并确认聚合结果包含两项
        hostAlpha.MarkReady();
        hostBeta.MarkReady();
        hostAlpha.CompleteRun();
        hostBeta.CompleteRun();
        var startUpTasks = await startTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, startUpTasks.Count);
    }

    /// <summary>
    /// 就绪信号缺失不导致死锁：Role 提前退出（运行任务完成）同样放行启动屏障。
    /// </summary>
    [Fact]
    public async Task StartHostsInOrderAsync_RoleExitsBeforeReady_ReleasesBarrier()
    {
        var hostAlpha = new BarrierStartUp("Alpha");
        var hostBeta = new BarrierStartUp("Beta");
        var hosts = new List<IAppStartUp> { hostAlpha, hostBeta };

        var startTask = Task.Run(() => AppEnter.StartHostsInOrderAsync(hosts));

        await hostAlpha.WaitStartedAsync();
        // Alpha 从不 MarkReady，直接完成运行任务：屏障应放行
        hostAlpha.CompleteRun();
        await hostBeta.WaitStartedAsync();

        hostBeta.MarkReady();
        hostBeta.CompleteRun();
        var startUpTasks = await startTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, startUpTasks.Count);
    }

    /// <summary>
    /// 记录停机顺序的假宿主。
    /// </summary>
    private sealed class RecordingStartUp : IAppStartUp
    {
        private readonly List<string> _stopOrder;

        public RecordingStartUp(string serverType, List<string> stopOrder)
        {
            ServerType = serverType;
            _stopOrder = stopOrder;
        }

        public Task<string> AppExitToken
        {
            get { return Task.FromResult<string>(null); }
        }

        public string ServerType { get; }

        public AppSetting Setting { get; } = new AppSetting();

        public Task StartUpReadyTask { get; } = Task.CompletedTask;

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
            _stopOrder.Add(ServerType);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// StopAsync 必抛异常的假宿主。
    /// </summary>
    private sealed class ThrowingStopStartUp : IAppStartUp
    {
        public ThrowingStopStartUp(string serverType)
        {
            ServerType = serverType;
        }

        public Task<string> AppExitToken
        {
            get { return Task.FromResult<string>(null); }
        }

        public string ServerType { get; }

        public AppSetting Setting { get; } = new AppSetting();

        public Task StartUpReadyTask { get; } = Task.CompletedTask;

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
            throw new InvalidOperationException($"stop failed for {ServerType}");
        }
    }

    /// <summary>
    /// 可控就绪信号与运行任务的假宿主（启动屏障测试）。
    /// </summary>
    private sealed class BarrierStartUp : IAppStartUp
    {
        private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _runCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _startCallCount;

        public BarrierStartUp(string serverType)
        {
            ServerType = serverType;
        }

        public Task<string> AppExitToken
        {
            get { return Task.FromResult<string>(null); }
        }

        public string ServerType { get; }

        public AppSetting Setting { get; } = new AppSetting();

        public Task StartUpReadyTask
        {
            get { return _ready.Task; }
        }

        public int StartCallCount
        {
            get { return Volatile.Read(ref _startCallCount); }
        }

        public bool Init(string serverType, AppSetting setting, string[] args = null)
        {
            return true;
        }

        public Task StartAsync()
        {
            Interlocked.Increment(ref _startCallCount);
            _started.TrySetResult();
            return _runCompletion.Task;
        }

        public Task StopAsync(string message = "")
        {
            return Task.CompletedTask;
        }

        public void MarkReady()
        {
            _ready.TrySetResult();
        }

        public void CompleteRun()
        {
            _runCompletion.TrySetResult();
        }

        public async Task WaitStartedAsync()
        {
            await _started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }
}
