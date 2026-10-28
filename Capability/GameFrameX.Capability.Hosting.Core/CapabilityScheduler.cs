// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
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
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.Diagnostics;
using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Foundation.Localization.Core;

namespace GameFrameX.Capability.Hosting.Core;

/// <summary>
/// 能力调度壳（注册制后台驱动：逐滴答 job 按装配序执行 + 间隔 job 独立周期）。
/// <para>
/// 维护约束（硬化红线）：单循环顺序驱动（不做多任务并发——单进程默认实现下并发 job 对同一存储集合操作会放大竞态）；
/// per-job 隔离——单个 job 异常记结构化日志（job 全名 + 异常）后继续当轮剩余 job，间隔 job 异常只影响自身本轮；
/// 壳级兜底——单轮逃逸异常经观测出口（<see cref="ICapabilitySchedulerObserver.OnLoopFaulted"/>）上报后循环继续；
/// 每轮耗时 / job 执行数 / 失败数经观测出口上报；时钟可注入（<see cref="ISchedulerClock"/>，缺省系统时钟）；
/// 一次性生命周期——停止（<see cref="StopAsync"/> / <see cref="Dispose"/>）后再 <see cref="Start"/> 抛
/// <see cref="InvalidOperationException"/>。
/// </para>
/// </summary>
public sealed class CapabilityScheduler : IDisposable
{
    /// <summary>
    /// 调度驱动间隔（毫秒，下限 100）。
    /// </summary>
    private readonly int _intervalMilliseconds;

    /// <summary>
    /// 逐滴答 job 集合（装配序）。
    /// </summary>
    private readonly IReadOnlyList<CapabilityTickJob> _tickJobs;

    /// <summary>
    /// 间隔 job 集合。
    /// </summary>
    private readonly IReadOnlyList<CapabilityIntervalJob> _intervalJobs;

    /// <summary>
    /// 可注入时钟（缺省系统时钟）。
    /// </summary>
    private readonly ISchedulerClock _clock;

    /// <summary>
    /// 观测出口（job 失败与每轮指标；缺省无观测）。
    /// </summary>
    private readonly ICapabilitySchedulerObserver _observer;

    /// <summary>
    /// 间隔 job 到期记账器。
    /// </summary>
    private readonly IntervalWatch _intervalWatch;

    /// <summary>
    /// 停止令牌。
    /// </summary>
    private readonly CancellationTokenSource _cancellation = new();

    /// <summary>
    /// 循环任务（启动后非空）。
    /// </summary>
    private Task _loopTask;

    /// <summary>
    /// 已停止标记（一次性生命周期）。
    /// </summary>
    private bool _stopped;

    /// <summary>
    /// 同步锁（Start/Stop 竞态保护）。
    /// </summary>
    private readonly object _sync = new();

    /// <summary>
    /// 初始化 <see cref="CapabilityScheduler"/>。
    /// </summary>
    /// <param name="intervalMilliseconds">驱动间隔毫秒（小于 100 按 100 钳制）。</param>
    /// <param name="tickJobs">逐滴答 job 集合（装配序即执行序）。</param>
    /// <param name="intervalJobs">间隔 job 集合。</param>
    /// <param name="clock">调度时钟（null = 系统时钟）。</param>
    /// <param name="observer">观测出口（null = 无观测）。</param>
    public CapabilityScheduler(int intervalMilliseconds, IReadOnlyList<CapabilityTickJob> tickJobs, IReadOnlyList<CapabilityIntervalJob> intervalJobs = null, ISchedulerClock clock = null, ICapabilitySchedulerObserver observer = null)
    {
        _intervalMilliseconds = Math.Max(100, intervalMilliseconds);
        _tickJobs = tickJobs ?? Array.Empty<CapabilityTickJob>();
        _intervalJobs = intervalJobs ?? Array.Empty<CapabilityIntervalJob>();
        _clock = clock ?? SystemSchedulerClock.Instance;
        _observer = observer;
        _intervalWatch = new IntervalWatch(_intervalJobs, _clock);
    }

    /// <summary>
    /// 启动调度循环（可重复调用：已启动时为空操作；停止后再启动抛 <see cref="InvalidOperationException"/>）。
    /// </summary>
    public void Start()
    {
        lock (_sync)
        {
            if (_stopped)
            {
                // Localization: Capability.Hosting.Scheduler.RestartAfterStop - 调度器停止后不可重新启动。
                throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.Capability.Hosting.SchedulerRestartAfterStop));
            }

            if (_loopTask != null)
            {
                return;
            }

            _loopTask = Task.Run(() => RunAsync(_cancellation.Token));
        }
    }

    /// <summary>
    /// 停止调度循环（等待当轮完成后退出并释放停止令牌；可重复调用；停止后不可重启）。
    /// </summary>
    /// <returns>异步任务。</returns>
    public async Task StopAsync()
    {
        Task loopTask;
        lock (_sync)
        {
            // 已取消说明此前已停止（令牌可能已释放），不再 Cancel 以免 ObjectDisposedException。
            if (!_cancellation.IsCancellationRequested)
            {
                _cancellation.Cancel();
            }

            _stopped = true;
            loopTask = _loopTask;
        }

        if (loopTask != null)
        {
            try
            {
                await loopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 等待周期被取消打断属预期退出路径。
            }
        }

        // 循环已退出（或从未启动），停止令牌不再需要，就地释放（Dispose 幂等，重复调用安全）。
        _cancellation.Dispose();
    }

    /// <summary>
    /// 释放资源（兜底停止；常规停机走 <see cref="StopAsync"/> 以等待当轮完成）。
    /// </summary>
    public void Dispose()
    {
        lock (_sync)
        {
            _stopped = true;
            if (!_cancellation.IsCancellationRequested)
            {
                _cancellation.Cancel();
            }

            _cancellation.Dispose();
        }
    }

    /// <summary>
    /// 调度循环主体（逐滴答 job + 到期间隔 job 顺序驱动）。
    /// </summary>
    /// <param name="cancellationToken">停止令牌。</param>
    /// <returns>异步任务。</returns>
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        using (var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_intervalMilliseconds)))
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false);
                    await TickAsync(cancellationToken).ConfigureAwait(false);
                    await TickDueIntervalJobsAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception exception)
                {
                    // 单轮失败不终止循环（调度壳自身异常兜底；job 异常已在 per-job 隔离内消化）。
                    ReportLoopFaulted(exception);
                }
            }
        }
    }

    /// <summary>
    /// 执行单轮逐滴答调度（按装配序顺序驱动；单个 job 异常上报观测出口后继续当轮剩余 job；
    /// 每轮耗时 / 执行数 / 失败数上报观测出口）。
    /// </summary>
    /// <param name="cancellationToken">停止令牌。</param>
    /// <returns>异步任务。</returns>
    public async Task TickAsync(CancellationToken cancellationToken = default)
    {
        var executedJobCount = 0;
        var failedJobCount = 0;
        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < _tickJobs.Count; i++)
        {
            var job = _tickJobs[i];
            try
            {
                await job.Run(cancellationToken).ConfigureAwait(false);
                executedJobCount++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                // per-job 隔离：坏 job 不拖垮当轮其余 job，失败上报观测出口（结构化日志 / 指标）。
                failedJobCount++;
                executedJobCount++;
                ReportJobFailure(CapabilitySchedulerJobKind.Tick, job.Name, exception);
            }
        }

        stopwatch.Stop();
        ReportTickRoundCompleted(stopwatch.Elapsed, executedJobCount, failedJobCount);
    }

    /// <summary>
    /// 执行到期的间隔 job（各自独立隔离；到期判定含本轮补偿）。
    /// </summary>
    /// <param name="cancellationToken">停止令牌。</param>
    /// <returns>异步任务。</returns>
    private async Task TickDueIntervalJobsAsync(CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        for (var i = 0; i < _intervalJobs.Count; i++)
        {
            var job = _intervalJobs[i];
            if (!_intervalWatch.TryConsumeDue(job, now))
            {
                continue;
            }

            try
            {
                await job.Run(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                // 间隔 job 异常只影响自身本轮，失败上报观测出口。
                ReportJobFailure(CapabilitySchedulerJobKind.Interval, job.Name, exception);
            }
        }
    }

    /// <summary>
    /// 上报 job 失败（观测出口异常吞掉——观测不得阻断调度循环）。
    /// </summary>
    /// <param name="jobKind">job 种类。</param>
    /// <param name="jobName">job 全名。</param>
    /// <param name="exception">job 异常。</param>
    private void ReportJobFailure(CapabilitySchedulerJobKind jobKind, string jobName, Exception exception)
    {
        try
        {
            _observer?.OnJobFailed(jobKind, jobName, exception);
        }
        catch (Exception)
        {
            // 观测出口自身异常不影响调度。
        }
    }

    /// <summary>
    /// 上报一轮 tick 指标（观测出口异常吞掉）。
    /// </summary>
    /// <param name="elapsed">本轮耗时。</param>
    /// <param name="executedJobCount">执行 job 数。</param>
    /// <param name="failedJobCount">失败 job 数。</param>
    private void ReportTickRoundCompleted(TimeSpan elapsed, int executedJobCount, int failedJobCount)
    {
        try
        {
            _observer?.OnTickRoundCompleted(elapsed, executedJobCount, failedJobCount);
        }
        catch (Exception)
        {
            // 观测出口自身异常不影响调度。
        }
    }

    /// <summary>
    /// 上报调度壳单轮兜底异常（观测出口异常吞掉——观测不得阻断调度循环；循环继续不变量保持）。
    /// </summary>
    /// <param name="exception">逃逸出单轮调度的壳级异常。</param>
    private void ReportLoopFaulted(Exception exception)
    {
        try
        {
            _observer?.OnLoopFaulted(exception);
        }
        catch (Exception)
        {
            // 观测出口自身异常不影响调度。
        }
    }

    /// <summary>
    /// 系统时钟（缺省实现）。
    /// </summary>
    private sealed class SystemSchedulerClock : ISchedulerClock
    {
        /// <summary>
        /// 单例。
        /// </summary>
        public static readonly SystemSchedulerClock Instance = new SystemSchedulerClock();

        /// <summary>
        /// 初始化 <see cref="SystemSchedulerClock"/>。
        /// </summary>
        private SystemSchedulerClock()
        {
        }

        /// <summary>
        /// 获取当前 UTC 时刻。
        /// </summary>
        public DateTimeOffset UtcNow
        {
            get { return DateTimeOffset.UtcNow; }
        }
    }

    /// <summary>
    /// 间隔 job 到期记账（首次按周期对齐，其后按上次执行时刻累加；时钟可注入）。
    /// </summary>
    private sealed class IntervalWatch
    {
        /// <summary>
        /// job 全名 → 下次到期时刻。
        /// </summary>
        private readonly Dictionary<string, DateTimeOffset> _nextDueByName = new(StringComparer.Ordinal);

        /// <summary>
        /// 初始化记账器（把首租期设为「当前时刻 + 周期」）。
        /// </summary>
        /// <param name="jobs">间隔 job 集合。</param>
        /// <param name="clock">时钟。</param>
        public IntervalWatch(IReadOnlyList<CapabilityIntervalJob> jobs, ISchedulerClock clock)
        {
            var now = clock.UtcNow;
            for (var i = 0; i < jobs.Count; i++)
            {
                _nextDueByName[jobs[i].Name] = now + ToInterval(jobs[i].Interval);
            }
        }

        /// <summary>
        /// 判定并消费到期（未到期返回 false）。
        /// </summary>
        /// <param name="job">间隔 job。</param>
        /// <param name="now">当前时刻。</param>
        /// <returns>到期返回 true 并顺延下一租期。</returns>
        public bool TryConsumeDue(CapabilityIntervalJob job, DateTimeOffset now)
        {
            if (!_nextDueByName.TryGetValue(job.Name, out var nextDue))
            {
                _nextDueByName[job.Name] = now + ToInterval(job.Interval);
                return false;
            }

            if (now < nextDue)
            {
                return false;
            }

            _nextDueByName[job.Name] = nextDue + ToInterval(job.Interval);
            return true;
        }

        /// <summary>
        /// 周期钳制（下限 100ms，与驱动间隔下限一致）。
        /// </summary>
        /// <param name="interval">原始周期。</param>
        /// <returns>钳制后周期。</returns>
        private static TimeSpan ToInterval(TimeSpan interval)
        {
            var milliseconds = Math.Max(100, interval.TotalMilliseconds);
            return TimeSpan.FromMilliseconds(milliseconds);
        }
    }
}
