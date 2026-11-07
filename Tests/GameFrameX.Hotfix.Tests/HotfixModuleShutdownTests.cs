using System.Collections;
using System.Collections.Concurrent;
using GameFrameX.Core.Events;
using GameFrameX.Core.Abstractions.Events;
using GameFrameX.Core.Abstractions.Hotfix;
using GameFrameX.Core.Hotfix;
using GameFrameX.Core.Session;
using GameFrameX.Core.Timer;
using System.Reflection;
using GameFrameX.Core.Actors;
using GameFrameX.Core.Components;
using GameFrameX.DataBase;
using GameFrameX.DataBase.Abstractions;
using GameFrameX.DataBase.Sqlite;
using GameFrameX.Hotfix.Lifecycle;
using GameFrameX.Utility;
using GameFrameX.Utility.Setting;
using Xunit;

namespace GameFrameX.Hotfix.Tests;

/// <summary>真实热更停止链必须等待回收写方，并共享一次最终回存与数据库关闭。</summary>
[Collection(HotfixManagerStaticStateCollection.Name)]
public sealed class HotfixModuleShutdownTests : IDisposable
{
    private readonly IHotfixResolver _previousResolver = HotfixManager.Resolver;

    public HotfixModuleShutdownTests()
    {
        var resolver = DispatchProxy.Create<IHotfixResolver, ShutdownResolverProxy>();
        ((ShutdownResolverProxy)resolver).PreviousResolver = _previousResolver;
        HotfixManager.SetResolverForTesting(resolver);
    }

    public void Dispose()
    {
        HotfixManager.SetResolverForTesting(_previousResolver);
    }

    [Fact]
    public async Task StopAsync_ConcurrentCalls_RecyclesBeforeFinalSaveAndClosesOnce()
    {
        if (GlobalSettings.CurrentSetting == null)
        {
            GlobalSettings.SetCurrentSetting(new AppSetting { ServerId = 1 });
        }

        await using (var schedulerScope = await ActorRecycleSchedulerScope.CreateAsync())
        {
            var databaseName = "hotfix-shutdown-" + Guid.NewGuid().ToString("N");
            Assert.True(await GameDatabase.ProcessDefault.InitAsync<SqliteDbService>(new DbOptions
            {
                Name = databaseName,
                ConnectionString = "Data Source=:memory:",
                Provider = DatabaseProviderType.Sqlite,
            }));

            var actorIdentifier = ActorIdGenerator.GetActorId(96, 9751);
            var actor = await ActorManager.GetOrNewAsync(actorIdentifier);
            var recycledWithDatabaseOpen = false;
            actor.AddOnceRecycleCallback(() =>
            {
                recycledWithDatabaseOpen = GameDatabase.ProcessDefault.Contains(databaseName);
            });

            var releaseProducer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseActor = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var actorEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var loopField = typeof(GlobalTimer).GetField("_loopTask", BindingFlags.Static | BindingFlags.NonPublic);
            var previousLoop = loopField.GetValue(null);
            var previousWorking = GlobalTimer.IsWorking;
            loopField.SetValue(null, releaseProducer.Task);
            GlobalTimer.IsWorking = true;
            var session = new PlayerSession("shutdown-order-session", null);
            session.SetPlayerId(actorIdentifier);
            var sessions = (ConcurrentDictionary<string, IPlayerSession>)typeof(PlayerSessionManager)
                .GetField("SessionMap", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(PlayerSessionManager.Instance);
            sessions[session.SessionId] = session;
            var queuedWork = actor.SendAsync((Func<Task>)(async () =>
            {
                actorEntered.TrySetResult();
                await releaseActor.Task;
            }), checkLock: true);
            await actorEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));

            var finalSaveEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseFinalSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var finalSaveCount = 0;
            StateComponent.AddShutdownSaveFunc(typeof(ShutdownProbeState), async (shutdown, force) =>
            {
                Interlocked.Increment(ref finalSaveCount);
                finalSaveEntered.TrySetResult();
                await releaseFinalSave.Task;
            });

            var bridge = new HotfixModuleLifecycleBridge();
            Task firstStop = null;
            try
            {
                firstStop = bridge.StopAsync();
                var secondStop = bridge.StopAsync();
                Assert.Same(firstStop, secondStop);
                Assert.False(GlobalTimer.IsWorking);
                Assert.True(sessions.ContainsKey(session.SessionId));
                Assert.False(recycledWithDatabaseOpen);
                Assert.False(finalSaveEntered.Task.IsCompleted);

                releaseProducer.TrySetResult();
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (sessions.ContainsKey(session.SessionId) && DateTime.UtcNow < deadline)
                {
                    await Task.Delay(10);
                }

                Assert.False(sessions.ContainsKey(session.SessionId));
                Assert.False(await EventDispatcher.WaitAllDispatchedAsync(TimeSpan.FromMilliseconds(30)));
                Assert.False(finalSaveEntered.Task.IsCompleted);
                Assert.False(recycledWithDatabaseOpen);
                Assert.True(GameDatabase.ProcessDefault.Contains(databaseName));
                releaseActor.TrySetResult();
                await queuedWork;
                await finalSaveEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.True(await EventDispatcher.WaitAllDispatchedAsync(TimeSpan.FromSeconds(1)));

                Assert.Same(firstStop, secondStop);
                Assert.False(firstStop.IsCompleted);
                Assert.True(recycledWithDatabaseOpen);
                Assert.True(GameDatabase.ProcessDefault.Contains(databaseName));
                releaseFinalSave.TrySetResult();
                await firstStop.WaitAsync(TimeSpan.FromSeconds(10));

                Assert.Equal(1, finalSaveCount);
                Assert.False(GameDatabase.ProcessDefault.Contains(databaseName));
                await bridge.StopAsync();
                Assert.Equal(1, finalSaveCount);
            }
            finally
            {
                releaseProducer.TrySetResult();
                releaseActor.TrySetResult();
                releaseFinalSave.TrySetResult();
                try
                {
                    if (firstStop != null)
                    {
                        await firstStop;
                    }
                }
                finally
                {
                    loopField.SetValue(null, previousLoop);
                    GlobalTimer.IsWorking = previousWorking;
                    sessions.TryRemove(session.SessionId, out _);
                    var field = typeof(StateComponent).GetField("SaveFuncMap", BindingFlags.Static | BindingFlags.NonPublic);
                    var registrations = Assert.IsAssignableFrom<IDictionary>(field.GetValue(null));
                    registrations.Remove(typeof(ShutdownProbeState).Assembly.GetName().Name + ":" + typeof(ShutdownProbeState).FullName);
                    await ActorManager.RemoveAsync(actorIdentifier);
                    if (GameDatabase.ProcessDefault.Contains(databaseName))
                    {
                        await GameDatabase.ProcessDefault.CloseAsync();
                    }
                }
            }
        }
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public async Task StopAsync_Failures_StillSavesAndClosesOnceAndPreservesExceptions(
        bool failProducer, bool failFinalSave, bool failClose)
    {
        var previousSetting = GlobalSettings.CurrentSetting;
        GlobalSettings.SetCurrentSetting(new AppSetting { ServerId = 9753 });
        await using (var schedulerScope = await ActorRecycleSchedulerScope.CreateAsync())
        {
            var databaseName = await OpenShutdownDatabaseAsync();
            var producerFailure = new InvalidOperationException("producer failure");
            var saveFailure = new InvalidOperationException("final save failure");
            var synchronousSaveFailure = new InvalidOperationException("synchronous final save failure");
            var closeFailure = new InvalidOperationException("database close failure");
            var trackingDatabase = DispatchProxy.Create<IDatabaseService, CloseTrackingDatabaseProxy>();
            var closeTracker = (CloseTrackingDatabaseProxy)trackingDatabase;
            closeTracker.Failure = failClose ? closeFailure : null;
            MultiDbRegistry.Register(databaseName + "-close-probe", trackingDatabase);
            var loopField = typeof(GlobalTimer).GetField("_loopTask", BindingFlags.Static | BindingFlags.NonPublic);
            var previousLoop = loopField.GetValue(null);
            var previousWorking = GlobalTimer.IsWorking;
            loopField.SetValue(null, failProducer ? Task.FromException(producerFailure) : Task.CompletedTask);
            GlobalTimer.IsWorking = true;
            var finalSaveCount = 0;
            StateComponent.AddShutdownSaveFunc(typeof(ShutdownProbeState), (shutdown, force) =>
            {
                Interlocked.Increment(ref finalSaveCount);
                Assert.False(GlobalTimer.IsWorking);
                Assert.True(GameDatabase.ProcessDefault.Contains(databaseName));
                return failFinalSave ? Task.FromException(saveFailure) : Task.CompletedTask;
            });
            var additionalSaveCount = 0;
            StateComponent.AddShutdownSaveFunc(typeof(AdditionalShutdownProbeState), (shutdown, force) =>
            {
                Interlocked.Increment(ref additionalSaveCount);
                if (failFinalSave)
                {
                    throw synchronousSaveFailure;
                }

                return Task.CompletedTask;
            });
            var actorIdentifier = ActorIdGenerator.GetActorId(96, 9753);
            var actor = await ActorManager.GetOrNewAsync(actorIdentifier);
            var recycled = false;
            actor.AddOnceRecycleCallback(() => { recycled = true; });
            var sessions = GetSessions();
            var session = new PlayerSession("shutdown-failure-session", null);
            session.SetPlayerId(actorIdentifier);
            sessions[session.SessionId] = session;
            var bridge = new HotfixModuleLifecycleBridge();
            try
            {
                var firstStop = bridge.StopAsync();
                Assert.Same(firstStop, bridge.StopAsync());
                var failure = await Assert.ThrowsAsync<AggregateException>(() => firstStop);
                var failures = failure.Flatten().InnerExceptions;
                if (failProducer)
                {
                    Assert.Contains(producerFailure, failures);
                }

                if (failFinalSave)
                {
                    Assert.Contains(saveFailure, failures);
                    Assert.Contains(synchronousSaveFailure, failures);
                }

                if (failClose)
                {
                    Assert.Contains(closeFailure, failures);
                }

                Assert.False(sessions.ContainsKey(session.SessionId));
                Assert.True(recycled);
                Assert.Equal(1, finalSaveCount);
                Assert.Equal(1, additionalSaveCount);
                Assert.Equal(1, closeTracker.CloseCount);
                Assert.False(GameDatabase.ProcessDefault.Contains(databaseName));
                Assert.Same(firstStop, bridge.StopAsync());
                var repeatedFailure = await Assert.ThrowsAsync<AggregateException>(() => bridge.StopAsync());
                Assert.Same(failure, repeatedFailure);
                Assert.Equal(1, finalSaveCount);
                Assert.Equal(1, additionalSaveCount);
                Assert.Equal(1, closeTracker.CloseCount);
            }
            finally
            {
                loopField.SetValue(null, previousLoop);
                GlobalTimer.IsWorking = previousWorking;
                RemoveShutdownRegistration();
                RemoveShutdownRegistration(typeof(AdditionalShutdownProbeState));
                sessions.TryRemove(session.SessionId, out _);
                await ActorManager.RemoveAsync(actorIdentifier);
                if (GameDatabase.ProcessDefault.Contains(databaseName))
                {
                    closeTracker.Failure = null;
                    await GameDatabase.ProcessDefault.CloseAsync();
                }

                RestoreSetting(previousSetting);
            }
        }
    }

    /// <summary>
    /// 真实排空超时后仍尝试回存/关库，但在执行业务保持未完成，停机必须保持原始失败。
    /// 最终保存被调用不等于未完成事件已持久化；重复 Stop 也不得把失败转为成功。
    /// </summary>
    [Fact]
    public async Task StopAsync_EventDrainTimesOut_StillSavesAndClosesOnce()
    {
        var previousSetting = GlobalSettings.CurrentSetting;
        GlobalSettings.SetCurrentSetting(new AppSetting { ServerId = 9754, ActorTimeOut = 60000 });
        await using (var schedulerScope = await ActorRecycleSchedulerScope.CreateAsync())
        {
            var databaseName = await OpenShutdownDatabaseAsync();
            var trackingDatabase = DispatchProxy.Create<IDatabaseService, CloseTrackingDatabaseProxy>();
            var closeTracker = (CloseTrackingDatabaseProxy)trackingDatabase;
            MultiDbRegistry.Register(databaseName + "-close-probe", trackingDatabase);
            var loopField = typeof(GlobalTimer).GetField("_loopTask", BindingFlags.Static | BindingFlags.NonPublic);
            var previousLoop = loopField.GetValue(null);
            var previousWorking = GlobalTimer.IsWorking;
            loopField.SetValue(null, Task.CompletedTask);
            GlobalTimer.IsWorking = true;
            var actorIdentifier = ActorIdGenerator.GetActorId(96, 9754);
            var actor = await ActorManager.GetOrNewAsync(actorIdentifier);
            var releaseActor = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var actorEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var queuedWork = actor.SendAsync((Func<Task>)(async () =>
            {
                actorEntered.TrySetResult();
                await releaseActor.Task;
            }), timeOut: 60000, checkLock: true);
            var sessions = GetSessions();
            var session = new PlayerSession("shutdown-timeout-session", null);
            session.SetPlayerId(actorIdentifier);
            sessions[session.SessionId] = session;
            var finalSaveCount = 0;
            StateComponent.AddShutdownSaveFunc(typeof(ShutdownProbeState), (shutdown, force) =>
            {
                Interlocked.Increment(ref finalSaveCount);
                Assert.False(GlobalTimer.IsWorking);
                Assert.False(sessions.ContainsKey(session.SessionId));
                Assert.False(queuedWork.IsCompleted);
                Assert.True(GameDatabase.ProcessDefault.Contains(databaseName));
                return Task.CompletedTask;
            });
            var bridge = new HotfixModuleLifecycleBridge();
            Task firstStop = null;
            try
            {
                await actorEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                firstStop = bridge.StopAsync();
                Assert.Same(firstStop, bridge.StopAsync());
                var failure = await Assert.ThrowsAsync<AggregateException>(() => firstStop.WaitAsync(TimeSpan.FromSeconds(30)));
                Assert.Contains(failure.Flatten().InnerExceptions, exception =>
                    exception is TimeoutException && exception.Message.Contains("pending business events", StringComparison.Ordinal));
                Assert.False(queuedWork.IsCompleted);
                Assert.True(firstStop.IsFaulted);
                Assert.Equal(1, finalSaveCount);
                Assert.Equal(1, closeTracker.CloseCount);
                Assert.False(GameDatabase.ProcessDefault.Contains(databaseName));
                Assert.Same(firstStop, bridge.StopAsync());
                var repeatedFailure = await Assert.ThrowsAsync<AggregateException>(() => bridge.StopAsync());
                Assert.Same(failure, repeatedFailure);
                Assert.True(firstStop.IsFaulted);
                Assert.Equal(1, finalSaveCount);
                Assert.Equal(1, closeTracker.CloseCount);
            }
            finally
            {
                releaseActor.TrySetResult();
                await queuedWork;
                Assert.True(await EventDispatcher.WaitAllDispatchedAsync(TimeSpan.FromSeconds(10)));
                if (firstStop != null)
                {
                    await Record.ExceptionAsync(() => firstStop);
                }

                loopField.SetValue(null, previousLoop);
                GlobalTimer.IsWorking = previousWorking;
                RemoveShutdownRegistration();
                sessions.TryRemove(session.SessionId, out _);
                await ActorManager.RemoveAsync(actorIdentifier);
                if (GameDatabase.ProcessDefault.Contains(databaseName))
                {
                    await GameDatabase.ProcessDefault.CloseAsync();
                }

                RestoreSetting(previousSetting);
            }
        }
    }

    [Fact]
    public async Task SaveAllForShutdownAsync_TimeoutRetainsAlreadyFailedSave()
    {
        var previousTimeout = StateComponent.ShutdownSaveTimeout;
        var releaseSave = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var saveFailure = new InvalidOperationException("save failed before timeout");
        StateComponent.ShutdownSaveTimeout = TimeSpan.FromMilliseconds(100);
        StateComponent.AddShutdownSaveFunc(typeof(ShutdownProbeState), (_, _) => releaseSave.Task);
        StateComponent.AddShutdownSaveFunc(typeof(AdditionalShutdownProbeState), (_, _) => throw saveFailure);
        try
        {
            var failure = await Assert.ThrowsAsync<AggregateException>(() => StateComponent.SaveAllForShutdownAsync(true));
            Assert.Contains(saveFailure, failure.Flatten().InnerExceptions);
            Assert.Contains(failure.Flatten().InnerExceptions, exception => exception is TimeoutException);
            Assert.False(releaseSave.Task.IsCompleted);
        }
        finally
        {
            releaseSave.TrySetResult();
            StateComponent.ShutdownSaveTimeout = previousTimeout;
            RemoveShutdownRegistration();
            RemoveShutdownRegistration(typeof(AdditionalShutdownProbeState));
        }
    }

    private static async Task<string> OpenShutdownDatabaseAsync()
    {
        var databaseName = "hotfix-shutdown-" + Guid.NewGuid().ToString("N");
        Assert.True(await GameDatabase.ProcessDefault.InitAsync<SqliteDbService>(new DbOptions
        {
            Name = databaseName,
            ConnectionString = "Data Source=:memory:",
            Provider = DatabaseProviderType.Sqlite,
        }));
        return databaseName;
    }

    private static ConcurrentDictionary<string, IPlayerSession> GetSessions()
    {
        return (ConcurrentDictionary<string, IPlayerSession>)typeof(PlayerSessionManager)
            .GetField("SessionMap", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(PlayerSessionManager.Instance);
    }

    private static void RemoveShutdownRegistration(Type stateType = null)
    {
        stateType ??= typeof(ShutdownProbeState);
        var registrations = (IDictionary)typeof(StateComponent)
            .GetField("SaveFuncMap", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        registrations.Remove(stateType.Assembly.GetName().Name + ":" + stateType.FullName);
    }

    private static void RestoreSetting(AppSetting previousSetting)
    {
        if (previousSetting != null)
        {
            GlobalSettings.SetCurrentSetting(previousSetting);
        }
        else
        {
            typeof(GlobalSettings).GetMethod("ResetCurrentSetting", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
        }
    }

    // 本夹具验证会话事件的真实入队/排空，不加载业务热更；避免未初始化解析器制造无关异常。
    public class ShutdownResolverProxy : DispatchProxy
    {
        public IHotfixResolver PreviousResolver { get; set; }

        protected override object Invoke(MethodInfo targetMethod, object[] arguments)
        {
            if (targetMethod.Name == nameof(IHotfixResolver.FindListeners))
            {
                return new List<IEventListener>();
            }

            return targetMethod.Invoke(PreviousResolver, arguments);
        }
    }

    public class CloseTrackingDatabaseProxy : DispatchProxy
    {
        public Exception Failure { get; set; }
        public int CloseCount { get; private set; }

        protected override object Invoke(MethodInfo targetMethod, object[] arguments)
        {
            if (targetMethod.Name == nameof(IDatabaseService.CloseAsync))
            {
                CloseCount++;
                return Failure == null ? Task.CompletedTask : Task.FromException(Failure);
            }

            throw new NotSupportedException(targetMethod.Name);
        }
    }

    private sealed class AdditionalShutdownProbeState
    {
    }

    private sealed class ShutdownProbeState
    {
    }
}
