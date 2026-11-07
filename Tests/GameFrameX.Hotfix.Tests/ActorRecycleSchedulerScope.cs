using System.Reflection;
using GameFrameX.Core.Timer;
using Quartz;

namespace GameFrameX.Hotfix.Tests;

/// <summary>回收测试使用独立真实 Quartz 实例，不重启或关闭其它用例的进程调度器。</summary>
internal sealed class ActorRecycleSchedulerScope : IAsyncDisposable
{
    private static readonly FieldInfo SchedulerField = typeof(QuartzTimer)
        .GetField("_scheduler", BindingFlags.Static | BindingFlags.NonPublic);
    private readonly object _previousScheduler;
    private readonly IScheduler _scheduler;
    private readonly StandaloneSchedulerFactory _factory;

    private ActorRecycleSchedulerScope(IScheduler scheduler, StandaloneSchedulerFactory factory)
    {
        _scheduler = scheduler;
        _factory = factory;
        _previousScheduler = SchedulerField.GetValue(null);
        SchedulerField.SetValue(null, scheduler);
    }

    public static async Task<ActorRecycleSchedulerScope> CreateAsync()
    {
        var factory = QuartzSchedulerBuilder.Create(builder =>
        {
            builder.ConfigureScheduler(options =>
            {
                options.InstanceName = "ActorRecycleScheduleCleanup-" + Guid.NewGuid().ToString("N");
            });
        }).Build();
        try
        {
            var scheduler = await factory.GetScheduler();
            await scheduler.Start();
            return new ActorRecycleSchedulerScope(scheduler, factory);
        }
        catch
        {
            await factory.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _scheduler.Shutdown(waitForJobsToComplete: true);
        }
        finally
        {
            try
            {
                await _factory.DisposeAsync();
            }
            finally
            {
                SchedulerField.SetValue(null, _previousScheduler);
            }
        }
    }
}
