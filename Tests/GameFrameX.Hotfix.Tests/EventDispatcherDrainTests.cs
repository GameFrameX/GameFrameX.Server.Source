// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the PRC and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file at the root of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or to infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目的二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project,
//   本组织与贡献者概不承担任何责任。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.Reflection;
using GameFrameX.Core.Abstractions.Agent;
using GameFrameX.Core.Actors;
using GameFrameX.Core.Actors.Impl;
using GameFrameX.Core.Abstractions.Events;
using GameFrameX.Core.Abstractions.Hotfix;
using GameFrameX.Core.Components;
using GameFrameX.Core.Events;
using GameFrameX.Core.Hotfix;
using GameFrameX.Network.Abstractions;
using GameFrameX.Utility.Setting;
using GameFrameX.Network.HTTP;
using Xunit;

namespace GameFrameX.Hotfix.Tests;

/// <summary>
/// 停机派发排空单测（C367 / GFX-1075）：全局派发（Task.Run fire-and-forget）纳入在飞计数后，
/// <see cref="EventDispatcher.WaitAllDispatchedAsync"/> 在派发完成前超时返回 false、完成后返回 true；
/// 无在飞派发时立即返回 true。
/// </summary>
[Collection(HotfixManagerStaticStateCollection.Name)]
public sealed class EventDispatcherDrainTests : IDisposable
{
    public EventDispatcherDrainTests()
    {
        HotfixManager.SetResolverForTesting(new BlockingResolverAdapter());
    }

    public void Dispose()
    {
        // 复原生产解析器，且保证在飞计数归零不泄漏给后续用例
        HotfixManager.SetResolverForTesting(null);
        Assert.True(EventDispatcher.WaitAllDispatchedAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult());
    }

    [Fact]
    public async Task WaitAllDispatched_NoInFlight_ShouldReturnTrueImmediately()
    {
        Assert.True(await EventDispatcher.WaitAllDispatchedAsync(TimeSpan.FromSeconds(5)));
    }

    /// <summary>
    /// 验证派发到已停邮箱的 Actor 时在飞计数不泄漏：工作单元被拒（不会执行）时，
    /// 计数必须回落，否则停机排空每次都空等满超时。
    /// </summary>
    [Fact]
    public async Task Dispatch_ToStoppedMailboxActor_ShouldNotLeakInFlightCount()
    {
        const long actorId = 987654321L;
        if (GlobalSettings.CurrentSetting == null)
        {
            GlobalSettings.SetCurrentSetting(new AppSetting());
        }

        var actor = await ActorManager.GetOrNewAsync(actorId);
        try
        {
            var workerActor = (WorkerActor)actor.WorkerActor;
            var actionBlockProperty = typeof(WorkerActor).GetProperty("ActionBlock", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(actionBlockProperty);
            var actionBlock = actionBlockProperty.GetValue(workerActor);
            actionBlockProperty.PropertyType.GetMethod("Complete").Invoke(actionBlock, null);

            EventDispatcher.Dispatch(actorId, new DrainProbeEventArgs());

            Assert.True(await EventDispatcher.WaitAllDispatchedAsync(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            // 邮箱已被本用例关闭：RemoveAsync 把 Actor 移出表后返回"队列拒绝工作"的异常任务（C436：不再静默丢弃），
            // 清理只需出表，忽略该预期异常。
            await Record.ExceptionAsync(() => ActorManager.RemoveAsync(actorId));
        }
    }

    [Fact]
    public async Task WaitAllDispatched_BlockedGlobalDispatch_ShouldTimeoutThenDrain()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        BlockingEventListener.Release = release;

        // actorId 0 无对应 Actor → 全局路径 Task.Run fire-and-forget
        EventDispatcher.Dispatch(0, new DrainProbeEventArgs());

        // 派发被监听器阻塞：短超时必须 false
        Assert.False(await EventDispatcher.WaitAllDispatchedAsync(TimeSpan.FromMilliseconds(200)));

        // 放行后：排空必须返回 true（且监听器确实执行过）
        release.SetResult();
        Assert.True(await EventDispatcher.WaitAllDispatchedAsync(TimeSpan.FromSeconds(10)));
        Assert.True(BlockingEventListener.Executed);
    }

    /// <summary>
    /// 排空探针事件参数（无载荷标记类）。
    /// </summary>
    private sealed class DrainProbeEventArgs : GameEventArgs
    {
    }

    /// <summary>
    /// 阻塞型全局监听器：等待外部 TaskCompletionSource 放行。
    /// </summary>
    private sealed class BlockingEventListener : IEventListener
    {
        public static volatile bool Executed;
        public static TaskCompletionSource Release;

        public Type AgentType
        {
            get { return null; }
        }

        public Task HandleEventAsync(IComponentAgent agent, GameEventArgs gameEventArgs)
        {
            return HandleEventAsync(gameEventArgs);
        }

        public async Task HandleEventAsync(GameEventArgs gameEventArgs)
        {
            Executed = true;
            var release = Release;
            if (release != null)
            {
                await release.Task;
            }
        }
    }

    /// <summary>
    /// 仅支撑全局 FindListeners 的最小解析替身（其余成员不可达）。
    /// </summary>
    private sealed class BlockingResolverAdapter : IHotfixResolver
    {
        public static readonly BlockingEventListener BlockingListener = new BlockingEventListener();

        public Assembly HotfixAssembly
        {
            get { return null; }
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
            return eventArgsType == typeof(DrainProbeEventArgs) ? new List<IEventListener> { BlockingListener } : new List<IEventListener>();
        }

        public T GetInstance<T>(string typeName, Type refAssemblyType = null)
        {
            return default;
        }
    }
}
