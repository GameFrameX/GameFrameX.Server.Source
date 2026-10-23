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
using GameFrameX.Apps.Player.Player.Component;
using GameFrameX.Core.Abstractions.Agent;
using GameFrameX.Core.Abstractions.Events;
using GameFrameX.Utility.Setting;
using GameFrameX.Core.Abstractions.Hotfix;
using GameFrameX.Core.Actors;
using GameFrameX.Core.Components;
using GameFrameX.Core.Events;
using GameFrameX.Core.Hotfix;
using GameFrameX.Hotfix.Logic.Player.Login;
using GameFrameX.Hotfix.Logic.Server;
using GameFrameX.Network.Abstractions;
using GameFrameX.Network.HTTP;
using GameFrameX.Utility;
using Xunit;

namespace GameFrameX.Hotfix.Tests;

/// <summary>
/// 组件代理侧事件派发单测（C289 扩展下沉 Core 后的 <c>agent.Dispatch(...)</c> 链路）：
/// 派发只触发代理所属 actor 类型的监听器、监听器收到该 actor 的组件代理；
/// 绑定同一事件类型但属于其他 actor 类型的监听器不触发。
/// </summary>
[Collection(HotfixManagerStaticStateCollection.Name)]
public sealed class ComponentAgentEventDispatchTests : IDisposable
{
    private static readonly object ModuleLock = new object();
    private static HotfixModule _module;
    private static bool _componentsRegistered;

    public ComponentAgentEventDispatchTests()
    {
        // actor 队列路径（SendAsync / ActorLimit）依赖进程级设置；沿 WorkerActorTests 先例做缺省初始化。
        if (GlobalSettings.CurrentSetting == null)
        {
            GlobalSettings.SetCurrentSetting(new AppSetting { ServerId = 1 });
        }

        lock (ModuleLock)
        {
            if (!_componentsRegistered)
            {
                ComponentRegister.InitAsync(typeof(PlayerComponent).Assembly).GetAwaiter().GetResult();
                _componentsRegistered = true;
            }

            if (_module == null)
            {
                _module = new HotfixModule(typeof(SessionRemoveEventListener).Assembly);
                RegisterListener(typeof(PlayerActorDispatchProbeEventListener));
                RegisterListener(typeof(ServerActorDispatchProbeEventListener));
            }
        }

        // 接管解析契约：扩展派发链路经 HotfixManager.Resolver 查监听器与组件代理类型（Dispose 复原生产宿主）。
        HotfixManager.SetResolverForTesting(new ModuleResolverAdapter(_module));
    }

    public void Dispose()
    {
        HotfixManager.SetResolverForTesting(null);
    }

    /// <summary>
    /// 组件代理侧派发：只触发代理所属 actor 类型的监听器，且监听器收到该 actor 的同一组件代理；
    /// </summary>
    [Fact]
    public async Task ComponentAgentDispatch_TriggersOnlyOwnActorTypeListeners_WithOwnActorAgent()
    {
        var playerActorType = ComponentRegister.GetActorType(typeof(PlayerComponent));
        Assert.NotEqual((ushort)0, playerActorType);
        var serverActorType = ComponentRegister.GetActorType(typeof(GameFrameX.Apps.Server.Component.ServerComponent));
        Assert.NotEqual((ushort)0, serverActorType);
        Assert.NotEqual(playerActorType, serverActorType);

        var actorId = ActorIdGenerator.GetActorId(playerActorType, 1);
        var agent = await ActorManager.GetComponentAgentAsync<PlayerComponentAgent>(actorId);
        Assert.NotNull(agent);

        PlayerActorDispatchProbeEventListener.Reset();
        ServerActorDispatchProbeEventListener.Reset();

        agent.Dispatch(new ComponentAgentDispatchProbeEventArgs());
        await ActorManager.AllFinishAsync();

        var recordedAgent = PlayerActorDispatchProbeEventListener.ReceivedAgent;
        Assert.NotNull(recordedAgent);
        Assert.Same(agent, recordedAgent);
        Assert.Equal(actorId, recordedAgent.ActorId);
        Assert.False(ServerActorDispatchProbeEventListener.Triggered);
    }

    /// <summary>
    /// 经反射向模块追加注册监听器（沿 EventBindingTests 对非公共 AddEvent 的既有用法）。
    /// </summary>
    private static void RegisterListener(Type listenerType)
    {
        var addEvent = typeof(HotfixModule).GetMethod("AddEvent", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(addEvent);
        Assert.True((bool)addEvent.Invoke(_module, new object[] { listenerType }));
    }

    /// <summary>
    /// 组件代理侧派发探针事件（仅用于本测试的监听器命中断言，不绑定任何生产监听器）。
    /// </summary>
    private sealed class ComponentAgentDispatchProbeEventArgs : GameEventArgs
    {
    }

    /// <summary>
    /// 玩家 actor 段探针监听器：记录收到的组件代理，证明派发命中本 actor 类型监听器且代理归属正确。
    /// </summary>
    [Event(typeof(ComponentAgentDispatchProbeEventArgs))]
    internal sealed class PlayerActorDispatchProbeEventListener : EventListener<PlayerComponentAgent>
    {
        /// <summary>最近一次收到的组件代理（测试断言读取）。</summary>
        internal static IComponentAgent ReceivedAgent;

        /// <summary>重置记录状态。</summary>
        internal static void Reset()
        {
            ReceivedAgent = null;
        }

        /// <inheritdoc />
        protected override Task HandleEventAsync(PlayerComponentAgent agent, GameEventArgs gameEventArgs)
        {
            ReceivedAgent = agent;
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// 服务器 actor 段探针监听器：与玩家段绑定同一事件类型，用于断言其他 actor 类型的监听器不触发。
    /// </summary>
    [Event(typeof(ComponentAgentDispatchProbeEventArgs))]
    internal sealed class ServerActorDispatchProbeEventListener : EventListener<ServerComponentAgent>
    {
        /// <summary>是否被触发（测试断言读取）。</summary>
        internal static bool Triggered;

        /// <summary>重置记录状态。</summary>
        internal static void Reset()
        {
            Triggered = false;
        }

        /// <inheritdoc />
        protected override Task HandleEventAsync(ServerComponentAgent agent, GameEventArgs gameEventArgs)
        {
            Triggered = true;
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// 测试用解析契约适配器：把真实 <see cref="HotfixModule" /> 的解析结果接入
    /// <see cref="HotfixManager.SetResolverForTesting" />（HotfixModule 自身不实现 IHotfixResolver）。
    /// </summary>
    private sealed class ModuleResolverAdapter : IHotfixResolver
    {
        private readonly HotfixModule _module;

        public ModuleResolverAdapter(HotfixModule module)
        {
            _module = module;
        }

        public Assembly HotfixAssembly
        {
            get { return _module.HotfixAssembly; }
        }

        public bool InTransition
        {
            get { return false; }
        }

        public Type GetAgentType(Type componentType)
        {
            return _module.GetAgentType(componentType);
        }

        public Type GetComponentType(Type agentType)
        {
            return _module.GetComponentType(agentType);
        }

        public T GetAgent<T>(BaseComponent component, Type refAssemblyType) where T : class, IComponentAgent
        {
            return _module.GetAgent<T>(component);
        }

        public IMessageHandler GetRpcHandler(Type requestType)
        {
            return _module.GetRpcHandler(requestType);
        }

        public IMessageHandler GetMessageHandler(int messageId)
        {
            return _module.GetMessageHandler(messageId);
        }

        public BaseHttpHandler GetHttpHandler(string command)
        {
            return _module.GetHttpHandler(command);
        }

        public List<BaseHttpHandler> GetListHttpHandler()
        {
            return _module.GetListHttpHandler();
        }

        public List<IEventListener> FindListeners(ushort actorType, Type eventArgsType)
        {
            return _module.FindListeners(actorType, eventArgsType);
        }

        public List<IEventListener> FindListeners(Type eventArgsType)
        {
            return _module.FindListeners(eventArgsType);
        }

        public T GetInstance<T>(string typeName, Type refAssemblyType = null)
        {
            return _module.GetInstance<T>(typeName);
        }
    }
}
