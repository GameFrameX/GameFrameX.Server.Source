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
//   or to infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
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

using System.Reflection;
using GameFrameX.Core.Abstractions.Agent;
using GameFrameX.Core.Abstractions.Attribute;
using GameFrameX.Core.Abstractions.Events;
using GameFrameX.Core.Abstractions.Hotfix;
using GameFrameX.Core.Actors;
using GameFrameX.Core.Components;
using GameFrameX.Core.Hotfix;
using GameFrameX.Core.Hotfix.Agent;
using GameFrameX.Network.Abstractions;
using GameFrameX.Network.HTTP;
using GameFrameX.Utility;
using GameFrameX.Utility.Setting;
using Xunit;

namespace GameFrameX.Hotfix.Tests;

/// <summary>
/// 组件激活互斥单测（<see cref="Actor.GetComponentAgentAsync(Type, bool)" />）：
/// 激活流程（组件激活 + 代理三段激活回调）完成前，actor 外部调用方不得经快速路径提前拿到代理；
/// 排队的重复激活工作单元复检后空跑；激活过程中的内联嵌套取同一组件不递归激活、不自锁。
/// Component activation exclusion tests: callers outside the actor must not be released by the fast path before the
/// whole activation flow completes; queued duplicate activation units are skipped after the in-context recheck;
/// inline nested lookups of the same component during activation neither re-activate nor self-lock.
/// </summary>
[Collection(HotfixManagerStaticStateCollection.Name)]
public sealed class ActorComponentActivationTests : IDisposable
{
    /// <summary>
    /// 测试专用 Actor 类型（每实体多实例区间，构造时不自动激活组件；业务组件未使用该编号）。
    /// </summary>
    private const ushort TestActorType = 77;

    /// <summary>
    /// 等待异步信号的上限，防止缺陷回归时用例无限挂起。
    /// </summary>
    private static readonly TimeSpan SignalTimeout = TimeSpan.FromSeconds(10);

    private static readonly object RegisterLock = new object();
    private static bool _componentsRegistered;

    public ActorComponentActivationTests()
    {
        // actor 队列路径（SendAsync / ActorLimit）依赖进程级设置；沿 WorkerActorTests 先例做缺省初始化。
        if (GlobalSettings.CurrentSetting == null)
        {
            GlobalSettings.SetCurrentSetting(new AppSetting { ServerId = 1 });
        }

        lock (RegisterLock)
        {
            if (!_componentsRegistered)
            {
                ComponentRegister.InitAsync(typeof(GatedActivationComponent).Assembly).GetAwaiter().GetResult();
                _componentsRegistered = true;
            }
        }

        HotfixManager.SetResolverForTesting(new TestAgentResolver());
    }

    public void Dispose()
    {
        HotfixManager.SetResolverForTesting(null);
    }

    /// <summary>
    /// 激活进行中（组件 IsActive 已为 true、代理激活回调未结束）时，第二个外部调用方必须排队等待激活完成，
    /// 且代理激活回调只执行一次。
    /// </summary>
    [Fact]
    public async Task GetComponentAgentAsync_WhileActivationInProgress_WaitsForCompletionAndActivatesOnce()
    {
        GatedActivationComponentAgent.ActivationEntered = new TaskCompletionSource<GatedActivationComponent>(TaskCreationOptions.RunContinuationsAsynchronously);
        GatedActivationComponentAgent.ActivationGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var actor = new Actor(ActorIdGenerator.GetActorId(TestActorType, 1), TestActorType);

        try
        {
            var firstCall = actor.GetComponentAgentAsync(typeof(GatedActivationComponentAgent));
            var component = await GatedActivationComponentAgent.ActivationEntered.Task.WaitAsync(SignalTimeout);
            Assert.True(component.IsActive);
            Assert.False(component.IsActivationCompleted);

            // 激活进行中：actor 外部的第二个调用方不得经快速路径提前放行
            var secondCall = actor.GetComponentAgentAsync(typeof(GatedActivationComponentAgent));
            var completedFirst = await Task.WhenAny(secondCall, Task.Delay(TimeSpan.FromMilliseconds(300)));
            Assert.NotSame(secondCall, completedFirst);

            GatedActivationComponentAgent.ActivationGate.TrySetResult(true);

            var agent = await firstCall.WaitAsync(SignalTimeout);
            Assert.Same(component, agent.Owner);
            Assert.Same(agent, await secondCall.WaitAsync(SignalTimeout));
            Assert.True(component.IsActivationCompleted);
            Assert.Equal(1, Volatile.Read(ref component.AgentActivationInvocationCount));
        }
        finally
        {
            // 断言失败时也放行闸门，避免 actor 工作单元永久挂起
            GatedActivationComponentAgent.ActivationGate.TrySetResult(true);
        }
    }

    /// <summary>
    /// 激活过程中代理在本 actor 调用链上内联获取自身组件：直接返回同一代理，不递归激活、不自锁。
    /// </summary>
    [Fact]
    public async Task GetComponentAgentAsync_NestedInlineDuringActivation_ReturnsWithoutReactivation()
    {
        var actor = new Actor(ActorIdGenerator.GetActorId(TestActorType, 2), TestActorType);

        var agent = (ReentrantActivationComponentAgent)await actor.GetComponentAgentAsync(typeof(ReentrantActivationComponentAgent)).WaitAsync(SignalTimeout);
        var component = (ReentrantActivationComponent)agent.Owner;

        Assert.Same(agent, component.NestedResolvedAgent);
        Assert.True(component.IsActivationCompleted);
        Assert.Equal(1, Volatile.Read(ref component.AgentActivationInvocationCount));
    }

    /// <summary>
    /// 激活可被测试阻塞的组件（阻塞点在代理激活回调内）。
    /// </summary>
    [ComponentType(TestActorType)]
    private sealed class GatedActivationComponent : BaseComponent
    {
        public int AgentActivationInvocationCount;
    }

    /// <summary>
    /// 门控组件代理：记录激活回调调用次数，进入后发出信号并阻塞到测试放行。
    /// </summary>
    private sealed class GatedActivationComponentAgent : BaseComponentAgent<GatedActivationComponent>
    {
        /// <summary>
        /// 激活回调进入信号（携带所属组件），用例开始前重置。
        /// </summary>
        public static TaskCompletionSource<GatedActivationComponent> ActivationEntered;

        /// <summary>
        /// 激活放行闸门，用例开始前重置。
        /// </summary>
        public static TaskCompletionSource<bool> ActivationGate;

        public override async Task<bool> ActiveAsync()
        {
            Interlocked.Increment(ref OwnerComponent.AgentActivationInvocationCount);
            if (!await base.ActiveAsync())
            {
                return false;
            }

            ActivationEntered.TrySetResult(OwnerComponent);
            await ActivationGate.Task;
            return true;
        }
    }

    /// <summary>
    /// 激活中内联嵌套获取自身的组件。
    /// </summary>
    [ComponentType(TestActorType)]
    private sealed class ReentrantActivationComponent : BaseComponent
    {
        public int AgentActivationInvocationCount;

        public IComponentAgent NestedResolvedAgent { get; set; }
    }

    /// <summary>
    /// 嵌套代理：激活回调内经本 actor 调用链再次获取自身组件代理。
    /// </summary>
    private sealed class ReentrantActivationComponentAgent : BaseComponentAgent<ReentrantActivationComponent>
    {
        public override async Task<bool> ActiveAsync()
        {
            Interlocked.Increment(ref OwnerComponent.AgentActivationInvocationCount);
            if (!await base.ActiveAsync())
            {
                return false;
            }

            OwnerComponent.NestedResolvedAgent = await GetComponentAgentAsync<ReentrantActivationComponentAgent>();
            return true;
        }
    }

    /// <summary>
    /// 测试用解析契约：组件类型 → 本文件代理类型，每个组件实例缓存唯一代理。
    /// </summary>
    private sealed class TestAgentResolver : IHotfixResolver
    {
        private static readonly Dictionary<Type, Type> AgentTypeMap = new Dictionary<Type, Type>
        {
            { typeof(GatedActivationComponent), typeof(GatedActivationComponentAgent) },
            { typeof(ReentrantActivationComponent), typeof(ReentrantActivationComponentAgent) },
        };

        private readonly object _agentLock = new object();
        private readonly Dictionary<BaseComponent, IComponentAgent> _agents = new Dictionary<BaseComponent, IComponentAgent>();

        public Assembly HotfixAssembly
        {
            get { return typeof(TestAgentResolver).Assembly; }
        }

        public bool InTransition
        {
            get { return false; }
        }

        public Type GetAgentType(Type componentType)
        {
            AgentTypeMap.TryGetValue(componentType, out var agentType);
            return agentType;
        }

        public Type GetComponentType(Type agentType)
        {
            return agentType.BaseType?.GetGenericArguments()[0];
        }

        public T GetAgent<T>(BaseComponent component, Type refAssemblyType) where T : class, IComponentAgent
        {
            lock (_agentLock)
            {
                if (!_agents.TryGetValue(component, out var agent))
                {
                    agent = (IComponentAgent)Activator.CreateInstance(AgentTypeMap[component.GetType()], true);
                    agent.SetOwner(component);
                    _agents[component] = agent;
                }

                return (T)agent;
            }
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
}
