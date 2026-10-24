// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the PRC and relevant international regulations.
//   使用本项目须严格遵守相关法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please refer to the LICENSE file at the root of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe the legal rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project,
//   本组织与贡献者概不承担任何责任。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository:  https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:   https://gitee.com/GameFrameX
//   CNB  仓库：https://www.cnb.cool/GameFrameX
//   CNB Repository:     https://www.cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Docs:      https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Hosting.Core;
using Xunit;

namespace GameFrameX.Tests.Online;

/// <summary>
/// 能力调度壳测试（C294：一次性生命周期与停机路径幂等、逐滴答 job 注册序执行与 per-job 隔离、
/// 可注入时钟驱动间隔 job 到期、观测出口上报失败与每轮指标；组合面 SchedulerClock / SchedulerObserver 注入生效）。
/// </summary>
[Collection(CapabilityRuntimeCollection.Name)]
public class CapabilitySchedulerTests
{
    /// <summary>调度驱动间隔（机制下限 100ms）。</summary>
    private const int LoopIntervalMilliseconds = 100;

    /// <summary>轮询等待上限（后台循环异步推进的断言超时）。</summary>
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(5);

    /// <summary>虚拟时钟起点。</summary>
    private static readonly DateTimeOffset StartInstant = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// 手动推进的调度时钟（测试注入：间隔 job 到期只由用例推进决定，不依赖真实等待）。
    /// </summary>
    private sealed class ManualSchedulerClock : ISchedulerClock
    {
        /// <summary>时刻读写锁（后台调度循环与用例线程并发访问）。</summary>
        private readonly object _sync = new();

        /// <summary>当前虚拟时刻。</summary>
        private DateTimeOffset _utcNow;

        /// <summary>
        /// 初始化手动时钟。
        /// </summary>
        /// <param name="start">起始时刻。</param>
        public ManualSchedulerClock(DateTimeOffset start)
        {
            _utcNow = start;
        }

        /// <summary>
        /// 在锁内读取手动维护的虚拟时刻；时刻只随测试显式推进而变化，不读取系统时钟。
        /// </summary>
        /// <remarks>
        /// Reads the manually maintained virtual time under a lock; it changes only when the test advances it explicitly and never reads the system clock.
        /// </remarks>
        /// <value>当前虚拟时刻 / Current virtual time</value>
        public DateTimeOffset UtcNow
        {
            get
            {
                lock (_sync)
                {
                    return _utcNow;
                }
            }
        }

        /// <summary>
        /// 推进虚拟时刻。
        /// </summary>
        /// <param name="delta">推进量。</param>
        public void Advance(TimeSpan delta)
        {
            lock (_sync)
            {
                _utcNow += delta;
            }
        }
    }

    /// <summary>
    /// job 失败观测记录（种类 + 全名 + 异常）。
    /// </summary>
    private sealed class JobFailureRecord
    {
        /// <summary>
        /// 初始化失败记录。
        /// </summary>
        /// <param name="kind">job 种类。</param>
        /// <param name="jobName">job 全名。</param>
        /// <param name="exception">job 异常。</param>
        public JobFailureRecord(CapabilitySchedulerJobKind kind, string jobName, Exception exception)
        {
            Kind = kind;
            JobName = jobName;
            Exception = exception;
        }

        /// <summary>获取 job 种类。</summary>
        public CapabilitySchedulerJobKind Kind { get; }

        /// <summary>获取 job 全名。</summary>
        public string JobName { get; }

        /// <summary>获取 job 异常。</summary>
        public Exception Exception { get; }
    }

    /// <summary>
    /// 每轮 tick 指标记录（执行数 + 失败数）。
    /// </summary>
    private sealed class TickRoundRecord
    {
        /// <summary>
        /// 初始化每轮记录。
        /// </summary>
        /// <param name="executedJobCount">执行 job 数。</param>
        /// <param name="failedJobCount">失败 job 数。</param>
        public TickRoundRecord(int executedJobCount, int failedJobCount)
        {
            ExecutedJobCount = executedJobCount;
            FailedJobCount = failedJobCount;
        }

        /// <summary>获取执行 job 数。</summary>
        public int ExecutedJobCount { get; }

        /// <summary>获取失败 job 数。</summary>
        public int FailedJobCount { get; }
    }

    /// <summary>
    /// 记录型调度观测出口（运维指标管道的测试替身：收集 job 失败与每轮指标）。
    /// </summary>
    private sealed class RecordingSchedulerObserver : ICapabilitySchedulerObserver
    {
        /// <summary>记录读写锁（后台调度循环与用例线程并发访问）。</summary>
        private readonly object _sync = new();

        /// <summary>失败记录。</summary>
        private readonly List<JobFailureRecord> _failures = new();

        /// <summary>每轮记录。</summary>
        private readonly List<TickRoundRecord> _tickRounds = new();

        /// <summary>获取失败记录快照。</summary>
        public JobFailureRecord[] Failures
        {
            get
            {
                lock (_sync)
                {
                    return _failures.ToArray();
                }
            }
        }

        /// <summary>获取每轮记录快照。</summary>
        public TickRoundRecord[] TickRounds
        {
            get
            {
                lock (_sync)
                {
                    return _tickRounds.ToArray();
                }
            }
        }

        /// <summary>
        /// 在锁内记录一条作业失败（类型、名称、异常），供测试断言调度器上报的失败。
        /// </summary>
        /// <remarks>
        /// Records a job failure (kind, name, exception) under a lock so tests can assert failures reported by the scheduler.
        /// </remarks>
        /// <param name="jobKind">失败作业类型 / Kind of the failed job</param>
        /// <param name="jobName">失败作业名称 / Name of the failed job</param>
        /// <param name="exception">作业抛出的异常 / Exception thrown by the job</param>
        public void OnJobFailed(CapabilitySchedulerJobKind jobKind, string jobName, Exception exception)
        {
            lock (_sync)
            {
                _failures.Add(new JobFailureRecord(jobKind, jobName, exception));
            }
        }

        /// <summary>
        /// 在锁内记录一轮 tick 的执行作业数与失败作业数，供测试断言调度轮次统计；耗时不记录。
        /// </summary>
        /// <remarks>
        /// Records the executed and failed job counts of a tick round under a lock so tests can assert round statistics; the elapsed time is not recorded.
        /// </remarks>
        /// <param name="elapsed">本轮耗时（本实现不记录） / Elapsed time of the round (not recorded by this implementation)</param>
        /// <param name="executedJobCount">本轮执行的作业数 / Number of jobs executed in the round</param>
        /// <param name="failedJobCount">本轮失败的作业数 / Number of jobs failed in the round</param>
        public void OnTickRoundCompleted(TimeSpan elapsed, int executedJobCount, int failedJobCount)
        {
            lock (_sync)
            {
                _tickRounds.Add(new TickRoundRecord(executedJobCount, failedJobCount));
            }
        }
    }

    /// <summary>
    /// 线程安全的调用计数器（job 执行次数断言用）。
    /// </summary>
    private sealed class InvocationCounter
    {
        /// <summary>计数值。</summary>
        private int _count;

        /// <summary>获取当前计数。</summary>
        public int Count
        {
            get { return Volatile.Read(ref _count); }
        }

        /// <summary>
        /// 计数加一。
        /// </summary>
        public void Increment()
        {
            Interlocked.Increment(ref _count);
        }
    }

    /// <summary>
    /// 构造只含一个计数 tick job 的调度器（最小驱动间隔）。
    /// </summary>
    /// <param name="counter">tick job 执行计数器。</param>
    /// <returns>待测调度器。</returns>
    private static CapabilityScheduler CreateCountingScheduler(InvocationCounter counter)
    {
        return new CapabilityScheduler(LoopIntervalMilliseconds, new[]
        {
            new CapabilityTickJob("a/count", _ =>
            {
                counter.Increment();
                return Task.CompletedTask;
            }),
        });
    }

    /// <summary>
    /// 轮询等待条件成立（后台调度循环异步推进；超时返回 false）。
    /// </summary>
    /// <param name="condition">条件。</param>
    /// <param name="timeout">等待上限。</param>
    /// <returns>条件在上限内成立返回 true。</returns>
    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(20);
        }

        return condition();
    }

    /// <summary>
    /// 生产停机路径：启动后停止应正常返回（循环被取消打断并退出，令牌就地释放）；
    /// 停机并释放后 TickAsync 仍能驱动注册 job（不依赖运行中循环）。
    /// </summary>
    [Fact]
    public async Task StartThenStopAsync_CompletesWithoutThrowing()
    {
        var counter = new InvocationCounter();
        var scheduler = CreateCountingScheduler(counter);

        scheduler.Start();
        await scheduler.StopAsync();
        scheduler.Dispose();

        var before = counter.Count;
        await scheduler.TickAsync();

        Assert.Equal(before + 1, counter.Count);
    }

    /// <summary>
    /// 停止幂等：StopAsync 重复调用与后续 Dispose 均不得抛异常（已释放令牌上的重入防护）；job 路径完好。
    /// </summary>
    [Fact]
    public async Task StopAsync_RepeatedAfterStop_DoesNotThrow()
    {
        var counter = new InvocationCounter();
        var scheduler = CreateCountingScheduler(counter);

        scheduler.Start();
        await scheduler.StopAsync();
        await scheduler.StopAsync();
        scheduler.Dispose();

        var before = counter.Count;
        await scheduler.TickAsync();

        Assert.Equal(before + 1, counter.Count);
    }

    /// <summary>
    /// 从未启动即停止：StopAsync 与 Dispose 直接走释放路径，不得抛异常；job 仍可经 TickAsync 驱动。
    /// </summary>
    [Fact]
    public async Task StopAsync_WithoutStart_DisposesWithoutThrowing()
    {
        var counter = new InvocationCounter();
        var scheduler = CreateCountingScheduler(counter);

        await scheduler.StopAsync();
        scheduler.Dispose();
        await scheduler.TickAsync();

        Assert.Equal(1, counter.Count);
    }

    /// <summary>
    /// 一次性生命周期：停止后再 Start 抛 <see cref="InvalidOperationException"/>（调度器不可复活）。
    /// </summary>
    [Fact]
    public async Task Start_AfterStopAsync_Throws()
    {
        var scheduler = CreateCountingScheduler(new InvocationCounter());

        scheduler.Start();
        await scheduler.StopAsync();

        Assert.Throws<InvalidOperationException>(() => scheduler.Start());
    }

    /// <summary>
    /// 逐滴答 job 按注册序顺序执行（装配序即执行序不变量）。
    /// </summary>
    [Fact]
    public async Task TickAsync_RunsTickJobsInRegistrationOrder()
    {
        var order = new List<string>();
        var scheduler = new CapabilityScheduler(LoopIntervalMilliseconds, new[]
        {
            new CapabilityTickJob("a/first", _ =>
            {
                order.Add("first");
                return Task.CompletedTask;
            }),
            new CapabilityTickJob("a/second", _ =>
            {
                order.Add("second");
                return Task.CompletedTask;
            }),
            new CapabilityTickJob("a/third", _ =>
            {
                order.Add("third");
                return Task.CompletedTask;
            }),
        });

        await scheduler.TickAsync();

        Assert.Equal(new[] { "first", "second", "third" }, order);
    }

    /// <summary>
    /// per-job 隔离：一个 tick job 抛异常不阻断当轮剩余 job；观测出口收到 Tick 种类 + job 全名的失败，
    /// 每轮指标执行数含失败 job（3 执行 / 1 失败）；异常不向 TickAsync 调用方传播。
    /// </summary>
    [Fact]
    public async Task TickAsync_ThrowingJob_DoesNotStopRemainingJobsAndIsReported()
    {
        var order = new List<string>();
        var observer = new RecordingSchedulerObserver();
        var scheduler = new CapabilityScheduler(LoopIntervalMilliseconds, new[]
        {
            new CapabilityTickJob("a/first", _ =>
            {
                order.Add("first");
                return Task.CompletedTask;
            }),
            new CapabilityTickJob("a/boom", _ => throw new InvalidOperationException("tick boom")),
            new CapabilityTickJob("a/third", _ =>
            {
                order.Add("third");
                return Task.CompletedTask;
            }),
        }, null, null, observer);

        await scheduler.TickAsync();

        Assert.Equal(new[] { "first", "third" }, order);
        var failure = Assert.Single(observer.Failures);
        Assert.Equal(CapabilitySchedulerJobKind.Tick, failure.Kind);
        Assert.Equal("a/boom", failure.JobName);
        Assert.Equal("tick boom", failure.Exception.Message);
        var round = Assert.Single(observer.TickRounds);
        Assert.Equal(3, round.ExecutedJobCount);
        Assert.Equal(1, round.FailedJobCount);
    }

    /// <summary>
    /// 时钟注入：1 分钟间隔 job 在虚拟时钟未越过到期点前不执行（后台循环照常推进）；
    /// 时钟推进越过到期点后下一轮执行且只执行一次（下一租期顺延一个周期）。
    /// </summary>
    [Fact]
    public async Task IntervalJob_RunsOnlyAfterInjectedClockPassesDueTime()
    {
        var clock = new ManualSchedulerClock(StartInstant);
        var observer = new RecordingSchedulerObserver();
        var runs = new InvocationCounter();
        var scheduler = new CapabilityScheduler(LoopIntervalMilliseconds, Array.Empty<CapabilityTickJob>(), new[]
        {
            new CapabilityIntervalJob("a/minute", TimeSpan.FromMinutes(1), _ =>
            {
                runs.Increment();
                return Task.CompletedTask;
            }),
        }, clock, observer);
        try
        {
            scheduler.Start();

            Assert.True(await WaitUntilAsync(() => observer.TickRounds.Length >= 3, PollTimeout));
            Assert.Equal(0, runs.Count);

            clock.Advance(TimeSpan.FromSeconds(61));

            Assert.True(await WaitUntilAsync(() => runs.Count >= 1, PollTimeout));
            var roundsAfterRun = observer.TickRounds.Length;
            Assert.True(await WaitUntilAsync(() => observer.TickRounds.Length >= roundsAfterRun + 3, PollTimeout));
            Assert.Equal(1, runs.Count);
        }
        finally
        {
            await scheduler.StopAsync();
        }
    }

    /// <summary>
    /// 间隔 job 异常只影响自身本轮：观测出口收到 Interval 种类 + job 全名的失败，同轮其它到期间隔 job 照常执行。
    /// </summary>
    [Fact]
    public async Task IntervalJob_Failure_ReportedAsIntervalKind()
    {
        var clock = new ManualSchedulerClock(StartInstant);
        var observer = new RecordingSchedulerObserver();
        var healthyRuns = new InvocationCounter();
        var scheduler = new CapabilityScheduler(LoopIntervalMilliseconds, Array.Empty<CapabilityTickJob>(), new[]
        {
            new CapabilityIntervalJob("a/faulty", TimeSpan.FromMinutes(1), _ => throw new InvalidOperationException("interval boom")),
            new CapabilityIntervalJob("a/healthy", TimeSpan.FromMinutes(1), _ =>
            {
                healthyRuns.Increment();
                return Task.CompletedTask;
            }),
        }, clock, observer);
        try
        {
            scheduler.Start();
            clock.Advance(TimeSpan.FromSeconds(61));

            Assert.True(await WaitUntilAsync(() => observer.Failures.Length >= 1 && healthyRuns.Count >= 1, PollTimeout));
            var failure = Assert.Single(observer.Failures);
            Assert.Equal(CapabilitySchedulerJobKind.Interval, failure.Kind);
            Assert.Equal("a/faulty", failure.JobName);
            Assert.Equal("interval boom", failure.Exception.Message);
            Assert.Equal(1, healthyRuns.Count);
        }
        finally
        {
            await scheduler.StopAsync();
        }
    }

    /// <summary>
    /// 组合面注入：<see cref="CapabilityCompositionOptions.SchedulerClock"/> / <see cref="CapabilityCompositionOptions.SchedulerObserver"/>
    /// 被宿主调度器采用——槽位 AddIntervalJob 在 StartAsync 后、虚拟时钟推进前不执行，推进后执行；
    /// 注入的观测出口收到每轮指标与能力限定全名（<c>mech-first/faulty</c>）的 Interval 失败。
    /// </summary>
    [Fact]
    public async Task Compose_SchedulerClockAndObserver_AreHonored()
    {
        CapabilityRuntime runtime = null;
        try
        {
            var clock = new ManualSchedulerClock(StartInstant);
            var observer = new RecordingSchedulerObserver();
            var pulses = new InvocationCounter();
            MechanismScenario.First.Register = context =>
            {
                context.AddIntervalJob("pulse", TimeSpan.FromMinutes(1), _ =>
                {
                    pulses.Increment();
                    return Task.CompletedTask;
                });
                context.AddIntervalJob("faulty", TimeSpan.FromMinutes(1), _ => throw new InvalidOperationException("slot interval boom"));
            };
            var options = new CapabilityCompositionOptions(default, new CollectingCapabilityAssemblyLog())
            {
                HostOptions = new CapabilityHostOptions { TenantId = 1, AppId = 1, ServerId = 1, SchedulerIntervalMilliseconds = LoopIntervalMilliseconds },
                Filter = new CapabilityAssemblyFilter(new[] { "mech-first" }),
                SchedulerClock = clock,
                SchedulerObserver = observer,
            };

            runtime = CapabilityComposition.Compose(new MechanismTestFamily(), options);
            await runtime.StartAsync();

            Assert.True(await WaitUntilAsync(() => observer.TickRounds.Length >= 3, PollTimeout));
            Assert.Equal(0, pulses.Count);
            Assert.Empty(observer.Failures);

            clock.Advance(TimeSpan.FromSeconds(61));

            Assert.True(await WaitUntilAsync(() => pulses.Count >= 1 && observer.Failures.Length >= 1, PollTimeout));
            var failure = observer.Failures.Single();
            Assert.Equal(CapabilitySchedulerJobKind.Interval, failure.Kind);
            Assert.Equal("mech-first/faulty", failure.JobName);
            Assert.Equal(1, pulses.Count);
        }
        finally
        {
            if (runtime != null)
            {
                await runtime.StopAsync();
            }

            MechanismScenario.Reset();
        }
    }
}
