using System.Reflection;
using GameFrameX.Core.Abstractions.Agent;
using GameFrameX.Core.Abstractions.Events;
using GameFrameX.Core.Abstractions.Hotfix;
using GameFrameX.Core.Components;
using GameFrameX.Core.Hotfix;
using GameFrameX.Core.Timer;
using GameFrameX.Core.Timer.Handler;
using GameFrameX.Network.Abstractions;
using GameFrameX.Network.HTTP;
using Quartz;
using Xunit;

namespace GameFrameX.Hotfix.Tests;

/// <summary>
/// QuartzTimer 调度结果契约回归测试（C330）：删除/暂停/恢复返回作业存在性，
/// 调度失败（永不触发的 Cron）异常必须传播给调用方，而不是随被丢弃的 ValueTask 丢失。
/// </summary>
[Collection(HotfixManagerStaticStateCollection.Name)]
public sealed class QuartzTimerSchedulerResultTests
{
    /// <summary>
    /// 测试用定时器处理器：位于测试程序集，经解析契约替身通过 GetJobDetail 的热更程序集校验。
    /// </summary>
    private sealed class ProbeTimerHandler : ITimerHandler
    {
        public Task InnerHandleTimerAsync(IComponentAgent agent, GameEventArgs gameEventArgs)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// 测试用解析契约替身：HotfixAssembly 指向测试程序集，其余成员返回空值（本测试不触发作业执行）。
    /// </summary>
    private sealed class TestAssemblyHotfixResolver : IHotfixResolver
    {
        public Assembly HotfixAssembly
        {
            get { return typeof(TestAssemblyHotfixResolver).Assembly; }
        }

        public bool InTransition
        {
            get { return false; }
        }

        public Type GetAgentType(Type componentType)
        {
            return null;
        }

        public Type GetComponentType(Type agentType)
        {
            return null;
        }

        public T GetAgent<T>(BaseComponent component, Type refAssemblyType) where T : class, IComponentAgent
        {
            return null;
        }

        public IMessageHandler GetRpcHandler(Type requestType)
        {
            return null;
        }

        public IMessageHandler GetMessageHandler(int messageId)
        {
            return null;
        }

        public BaseHttpHandler GetHttpHandler(string command)
        {
            return null;
        }

        public List<BaseHttpHandler> GetListHttpHandler()
        {
            return new List<BaseHttpHandler>();
        }

        public List<IEventListener> FindListeners(ushort actorType, Type eventArgsType)
        {
            return new List<IEventListener>();
        }

        public List<IEventListener> FindListeners(Type eventArgsType)
        {
            return new List<IEventListener>();
        }

        public T GetInstance<T>(string typeName, Type refAssemblyType = null)
        {
            return default;
        }
    }

    /// <summary>
    /// 永不触发的 Cron（年份已过）在 Quartz ScheduleJob 内抛 SchedulerException；
    /// 改前该异常随被丢弃的 ValueTask 丢失，调用方拿到"有效"ID。
    /// </summary>
    [Fact]
    public async Task WithCronExpressionAsync_NeverFiringCron_ThrowsSchedulerException()
    {
        await QuartzTimer.StartAsync();
        HotfixManager.SetResolverForTesting(new TestAssemblyHotfixResolver());
        try
        {
            await Assert.ThrowsAsync<SchedulerException>(() => QuartzTimer.WithCronExpressionAsync<ProbeTimerHandler>(1, "0 0 0 1 1 ? 2000"));
        }
        finally
        {
            HotfixManager.SetResolverForTesting(null);
        }
    }

    /// <summary>
    /// 删除存在的作业返回 true 且作业确实被删除；再次删除（作业不存在，正常现象）返回 false；非正 ID 返回 false。
    /// </summary>
    [Fact]
    public async Task RemoveAsync_ReturnsExistenceAndDeletesJob()
    {
        await QuartzTimer.StartAsync();
        HotfixManager.SetResolverForTesting(new TestAssemblyHotfixResolver());
        try
        {
            var scheduleId = await QuartzTimer.DelayAsync<ProbeTimerHandler>(1, TimeSpan.FromHours(1));
            Assert.True(await QuartzTimer.RemoveAsync(scheduleId));
            Assert.False(await QuartzTimer.RemoveAsync(scheduleId));
            Assert.False(await QuartzTimer.RemoveAsync(0));
        }
        finally
        {
            HotfixManager.SetResolverForTesting(null);
        }
    }

    /// <summary>
    /// 暂停/恢复存在的作业返回 true，作业不存在时返回 false（false 为正常结果，不应抛出或记 Warning）。
    /// </summary>
    [Fact]
    public async Task PauseAsyncAndResumeAsync_ReturnExistence()
    {
        await QuartzTimer.StartAsync();
        HotfixManager.SetResolverForTesting(new TestAssemblyHotfixResolver());
        try
        {
            var scheduleId = await QuartzTimer.DelayAsync<ProbeTimerHandler>(1, TimeSpan.FromHours(1));
            Assert.True(await QuartzTimer.PauseAsync(scheduleId));
            Assert.True(await QuartzTimer.ResumeAsync(scheduleId));
            await QuartzTimer.RemoveAsync(scheduleId);
            Assert.False(await QuartzTimer.PauseAsync(scheduleId));
            Assert.False(await QuartzTimer.ResumeAsync(scheduleId));
        }
        finally
        {
            HotfixManager.SetResolverForTesting(null);
        }
    }

    /// <summary>
    /// 批量删除对快照内全部作业生效；集合在 await 期间被清空不影响删除（快照拷贝）。
    /// </summary>
    [Fact]
    public async Task RemoveAsync_BatchDeletesEveryScheduledJob()
    {
        await QuartzTimer.StartAsync();
        HotfixManager.SetResolverForTesting(new TestAssemblyHotfixResolver());
        try
        {
            var firstScheduleId = await QuartzTimer.DelayAsync<ProbeTimerHandler>(1, TimeSpan.FromHours(1));
            var secondScheduleId = await QuartzTimer.DelayAsync<ProbeTimerHandler>(1, TimeSpan.FromHours(1));
            var scheduleIdSet = new HashSet<long> { firstScheduleId, secondScheduleId };

            await QuartzTimer.RemoveAsync(scheduleIdSet);
            scheduleIdSet.Clear();

            Assert.False(await QuartzTimer.RemoveAsync(firstScheduleId));
            Assert.False(await QuartzTimer.RemoveAsync(secondScheduleId));
        }
        finally
        {
            HotfixManager.SetResolverForTesting(null);
        }
    }
}
