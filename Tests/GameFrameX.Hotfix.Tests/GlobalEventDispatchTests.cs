// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规及开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes and liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne by the developer; the project organization and contributors assume no responsibility.
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
using GameFrameX.Core.Abstractions;
using GameFrameX.Core.Abstractions.Agent;
using GameFrameX.Core.Abstractions.Events;
using GameFrameX.Core.Actors;
using GameFrameX.Core.Components;
using GameFrameX.Core.Events;
using GameFrameX.Core.Hotfix;
using GameFrameX.Hotfix.Logic.Player.Login;
using GameFrameX.Hotfix.Logic.Server;
using GameFrameX.Utility;
using GameFrameX.Utility.Setting;
using Xunit;

namespace GameFrameX.Hotfix.Tests;

/// <summary>
/// 全局事件派发单测（目标 Actor 不在 ActorMap 时的 <c>EventDispatcher.Dispatch(0, ...)</c> 链路）：
/// 全局 Actor 类型的监听器被投递到其所属全局 Actor 串行执行并收到该 Actor 的非空组件代理；
/// 玩家段 Actor 类型的监听器被跳过、不被以空代理调用。
/// </summary>
/// <remarks>
/// Global event dispatch tests (the <c>EventDispatcher.Dispatch(0, ...)</c> path when the target actor
/// is absent from the ActorMap): listeners of global actor types are posted to their owning global
/// actor for serialized execution and receive a non-null agent of that actor; player-segment listeners
/// are skipped and never invoked with a null agent.
/// </remarks>
[Collection(HotfixManagerStaticStateCollection.Name)]
public sealed class GlobalEventDispatchTests : IDisposable
{
    private static readonly object ModuleLock = new object();
    private static HotfixModule _module;
    private static bool _componentsRegistered;
    private static long _globalActorId;

    public GlobalEventDispatchTests()
    {
        // actor 队列路径（SendAsync / ActorLimit）依赖进程级设置；沿 ComponentAgentEventDispatchTests 先例做缺省初始化。
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
                RegisterListener(typeof(GlobalActorDispatchProbeEventListener));
                RegisterListener(typeof(GlobalDispatchPlayerProbeEventListener));
            }
        }

        // 接管解析契约：全局派发链路经 HotfixManager.Resolver 聚合监听器并反查代理的组件类型（Dispose 复原生产宿主）。
        HotfixManager.SetResolverForTesting(new HotfixModuleResolverAdapter(_module));
    }

    public void Dispose()
    {
        HotfixManager.SetResolverForTesting(null);
    }

    /// <summary>
    /// 全局派发：全局 Actor 类型监听器在其所属全局 Actor 上下文执行（RuntimeContext 命中该 Actor）并收到非空代理。
    /// </summary>
    [Fact]
    public async Task GlobalDispatch_RunsGlobalActorListener_InOwnerActorContext_WithOwnerAgent()
    {
        var serverActorType = ComponentRegister.GetActorType(typeof(GameFrameX.Apps.Server.Component.ServerComponent));
        Assert.True(serverActorType > GlobalConst.ActorTypeSeparator);

        // 全局 Actor 与生产启动期同 ID（默认服务器ID）；不存在则创建
        _globalActorId = ActorIdGenerator.GetActorId(serverActorType);
        var globalActor = await ActorManager.GetOrNewAsync(_globalActorId);
        Assert.NotNull(globalActor);

        GlobalActorDispatchProbeEventListener.Reset();
        GlobalDispatchPlayerProbeEventListener.Reset();

        EventDispatcher.Dispatch(0, new GlobalDispatchProbeEventArgs());
        await ActorManager.AllFinishAsync();

        Assert.True(GlobalActorDispatchProbeEventListener.Triggered);
        Assert.Equal(_globalActorId, GlobalActorDispatchProbeEventListener.RecordedCurrentActorId);
        var recordedAgent = GlobalActorDispatchProbeEventListener.ReceivedAgent;
        Assert.NotNull(recordedAgent);
        Assert.Equal(_globalActorId, recordedAgent.ActorId);
    }

    /// <summary>
    /// 全局派发：玩家段 Actor 类型监听器被跳过，不被调用（不再以空代理执行）。
    /// </summary>
    [Fact]
    public async Task GlobalDispatch_SkipsPlayerSegmentListeners()
    {
        var playerActorType = ComponentRegister.GetActorType(typeof(PlayerComponent));
        Assert.True(playerActorType < GlobalConst.ActorTypeSeparator);

        GlobalActorDispatchProbeEventListener.Reset();
        GlobalDispatchPlayerProbeEventListener.Reset();

        EventDispatcher.Dispatch(0, new GlobalDispatchProbeEventArgs());
        await ActorManager.AllFinishAsync();

        Assert.False(GlobalDispatchPlayerProbeEventListener.Triggered);
        Assert.Null(GlobalDispatchPlayerProbeEventListener.ReceivedAgent);
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
    /// 全局派发探针事件（仅用于本测试的监听器命中断言，不绑定任何生产监听器）。
    /// </summary>
    private sealed class GlobalDispatchProbeEventArgs : GameEventArgs
    {
    }

    /// <summary>
    /// 全局 Actor 段探针监听器：记录执行时的 RuntimeContext Actor 与收到的代理，证明监听器在所属全局 Actor 上下文执行且代理非空。
    /// </summary>
    [Event(typeof(GlobalDispatchProbeEventArgs))]
    internal sealed class GlobalActorDispatchProbeEventListener : EventListener<ServerComponentAgent>
    {
        /// <summary>是否被触发（测试断言读取）。</summary>
        internal static bool Triggered;

        /// <summary>执行时 <c>RuntimeContext.CurrentActor</c>（测试断言读取）。</summary>
        internal static long RecordedCurrentActorId;

        /// <summary>最近一次收到的组件代理（测试断言读取）。</summary>
        internal static IComponentAgent ReceivedAgent;

        /// <summary>重置记录状态。</summary>
        internal static void Reset()
        {
            Triggered = false;
            RecordedCurrentActorId = 0;
            ReceivedAgent = null;
        }

        /// <summary>
        /// 记录执行上下文 Actor 与收到的代理到静态字段，供测试断言监听器在所属全局 Actor 上下文执行且代理非空；不做其他处理。
        /// </summary>
        /// <remarks>
        /// Records the executing actor context and the received agent into static fields so tests can
        /// assert the listener ran on its owning global actor with a non-null agent; performs no other work.
        /// </remarks>
        /// <param name="agent">收到事件的服务器组件代理 / The server component agent that received the event</param>
        /// <param name="gameEventArgs">探针事件参数（本实现不读取） / Probe event arguments (not read by this implementation)</param>
        /// <returns>记录完成后立即返回的已完成任务 / Completed task returned immediately after recording</returns>
        protected override Task HandleEventAsync(ServerComponentAgent agent, GameEventArgs gameEventArgs)
        {
            Triggered = true;
            RecordedCurrentActorId = RuntimeContext.CurrentActor;
            ReceivedAgent = agent;
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// 玩家段探针监听器：与全局段绑定同一事件类型，用于断言玩家段监听器在全局派发下被跳过、不被调用。
    /// </summary>
    [Event(typeof(GlobalDispatchProbeEventArgs))]
    internal sealed class GlobalDispatchPlayerProbeEventListener : EventListener<PlayerComponentAgent>
    {
        /// <summary>是否被触发（测试断言读取）。</summary>
        internal static bool Triggered;

        /// <summary>最近一次收到的组件代理（测试断言读取；被跳过时保持 null）。</summary>
        internal static IComponentAgent ReceivedAgent;

        /// <summary>重置记录状态。</summary>
        internal static void Reset()
        {
            Triggered = false;
            ReceivedAgent = null;
        }

        /// <summary>
        /// 记录触发与收到的代理到静态字段，供测试断言玩家段监听器未被全局派发命中；不做其他处理。
        /// </summary>
        /// <remarks>
        /// Records the triggered flag and received agent into static fields so tests can assert the
        /// player-segment listener is not hit by the global dispatch; performs no other work.
        /// </remarks>
        /// <param name="agent">收到事件的玩家组件代理（本实现不读取） / Player component agent that received the event (not read)</param>
        /// <param name="gameEventArgs">探针事件参数（本实现不读取） / Probe event arguments (not read by this implementation)</param>
        /// <returns>记录完成后立即返回的已完成任务 / Completed task returned immediately after recording</returns>
        protected override Task HandleEventAsync(PlayerComponentAgent agent, GameEventArgs gameEventArgs)
        {
            Triggered = true;
            ReceivedAgent = agent;
            return Task.CompletedTask;
        }
    }
}
