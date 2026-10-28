// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规及开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code.
//   禁止利用本项目实施任何危害国家安全、破坏公共秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt public order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.Reflection;
using GameFrameX.Core.Abstractions;
using GameFrameX.Core.Abstractions.Agent;
using GameFrameX.Core.Abstractions.Attribute;
using GameFrameX.Core.Abstractions.Events;
using GameFrameX.Core.Abstractions.Hotfix;
using GameFrameX.Core.Actors;
using GameFrameX.Core.Components;
using GameFrameX.Core.Hotfix;
using GameFrameX.Network.Abstractions;
using GameFrameX.Network.HTTP;
using GameFrameX.Utility;
using GameFrameX.Utility.Setting;
using Xunit;

namespace GameFrameX.Hotfix.Tests;

/// <summary>
/// 组件激活失败可见性回归测试（C341）：调试模式下激活失败必须以 <see cref="AggregateException"/> 显式抛出；
/// Agent 未实现的组件必须告警跳过而非吞掉无上下文 NRE；非调试模式保持记录后继续。
/// Component activation failure visibility regression tests (C341): in debug mode activation
/// failures must surface as an <see cref="AggregateException"/>; components without an Agent
/// implementation must be skipped with a warning instead of swallowing a context-free NRE;
/// non-debug mode keeps log-and-continue semantics.
/// </summary>
[Collection(HotfixManagerStaticStateCollection.Name)]
public sealed class ComponentActivationFailureVisibilityTests : IDisposable
{
    /// <summary>
    /// 探测组件绑定的 Actor 类型（0 &lt; 97 &lt; 128 每实体多实例区间，业务程序集未占用）。
    /// Actor type bound to the probe component (0 &lt; 97 &lt; 128 per-entity range, unused by
    /// business assemblies).
    /// </summary>
    private const ushort ProbeActorType = 97;

    private static readonly object ModuleLock = new object();
    private static bool _componentsRegistered;

    private readonly FakeActivationResolver _resolver = new FakeActivationResolver();
    private readonly bool _originalIsDebug;

    /// <summary>
    /// 注册测试程序集组件映射、初始化进程级设置并接管解析契约；Dispose 复原生产宿主。
    /// Registers the test-assembly component map, initializes process-level settings and takes
    /// over the resolution contract; Dispose restores the production host.
    /// </summary>
    public ComponentActivationFailureVisibilityTests()
    {
        if (GlobalSettings.CurrentSetting == null)
        {
            GlobalSettings.SetCurrentSetting(new AppSetting { ServerId = 1 });
        }

        lock (ModuleLock)
        {
            if (!_componentsRegistered)
            {
                ComponentRegister.InitAsync(typeof(ComponentActivationFailureVisibilityTests).Assembly).GetAwaiter().GetResult();
                _componentsRegistered = true;
            }
        }

        _originalIsDebug = GlobalSettings.CurrentSetting.IsDebug;
        HotfixManager.SetResolverForTesting(_resolver);
    }

    /// <summary>
    /// 复原调试开关与生产解析宿主。
    /// Restores the debug switch and the production resolution host.
    /// </summary>
    public void Dispose()
    {
        GlobalSettings.CurrentSetting.IsDebug = _originalIsDebug;
        HotfixManager.SetResolverForTesting(null);
    }

    /// <summary>
    /// 调试模式下代理激活抛异常：失败必须以 AggregateException 汇总抛出，内层异常为注入的探测异常。
    /// In debug mode an agent activation exception must surface as an AggregateException whose
    /// inner exception is the injected probe exception.
    /// </summary>
    [Fact]
    public async Task DebugMode_AgentActivationFailure_ThrowsAggregateExceptionWithProbe()
    {
        GlobalSettings.CurrentSetting.IsDebug = true;
        var actor = NewProbeActor(901);

        var aggregateException = await Assert.ThrowsAsync<AggregateException>(
            () => ComponentRegister.ActiveComponentsAsync(actor));

        Assert.Contains(aggregateException.InnerExceptions,
            exception => exception is InvalidOperationException && exception.Message == FakeActivationResolver.ProbeMessage);
    }

    /// <summary>
    /// 调试模式下组件未实现 Agent：告警跳过，不产生失败清单、不抛异常（旧代码为吞掉的无上下文 NRE）。
    /// In debug mode, when the component has no Agent implementation, it is skipped with a
    /// warning; no failure list is built and nothing throws (the old code swallowed a
    /// context-free NRE instead).
    /// </summary>
    [Fact]
    public async Task DebugMode_AgentNotImplemented_SkipsWithoutThrowing()
    {
        GlobalSettings.CurrentSetting.IsDebug = true;
        _resolver.NoAgentMode = true;
        var actor = NewProbeActor(902);

        var aggregateException = await Record.ExceptionAsync(() => ComponentRegister.ActiveComponentsAsync(actor));

        Assert.Null(aggregateException);
    }

    /// <summary>
    /// 非调试模式下同一激活失败：逐条 Fatal 记录后继续，不向上抛出（保持既有行为）。
    /// In non-debug mode the same activation failure is logged per-component and swallowed;
    /// nothing propagates (existing behavior preserved).
    /// </summary>
    [Fact]
    public async Task NonDebugMode_AgentActivationFailure_DoesNotThrow()
    {
        GlobalSettings.CurrentSetting.IsDebug = false;
        var actor = NewProbeActor(903);

        var aggregateException = await Record.ExceptionAsync(() => ComponentRegister.ActiveComponentsAsync(actor));

        Assert.Null(aggregateException);
    }

    /// <summary>
    /// 调试模式下代理生命周期全部成功：不产生失败清单、不抛异常。
    /// In debug mode, when the agent lifecycle succeeds entirely, no failure list is built and
    /// nothing throws.
    /// </summary>
    [Fact]
    public async Task DebugMode_BenignAgentLifecycle_DoesNotThrow()
    {
        GlobalSettings.CurrentSetting.IsDebug = true;
        _resolver.BenignMode = true;
        var actor = NewProbeActor(904);

        var aggregateException = await Record.ExceptionAsync(() => ComponentRegister.ActiveComponentsAsync(actor));

        Assert.Null(aggregateException);
    }

    /// <summary>
    /// 构造探测 Actor 并断言探测组件已注册到该 Actor 类型。
    /// Creates the probe actor and asserts the probe component is registered for that actor type.
    /// </summary>
    private static Actor NewProbeActor(int instanceId)
    {
        var actorType = ComponentRegister.GetActorType(typeof(ActivationProbeComponent));
        Assert.Equal(ProbeActorType, actorType);
        return new Actor(ActorIdGenerator.GetActorId(actorType, instanceId), actorType);
    }

    /// <summary>
    /// 探测组件：无状态、无 IO，仅用于走真实组件创建与激活链路。
    /// Probe component: stateless and IO-free, existing only to exercise the real component
    /// creation and activation chain.
    /// </summary>
    [ComponentType(ProbeActorType)]
    private sealed class ActivationProbeComponent : BaseComponent
    {
    }

    /// <summary>
    /// 探测代理类型壳：仅提供 <c>BaseType.GetGenericArguments()[0] == ActivationProbeComponent</c>
    /// 的代理类型形态（实例由解析替身返回，不实例化本类型）。
    /// Probe agent type shell: only provides the agent-type shape where
    /// <c>BaseType.GetGenericArguments()[0] == ActivationProbeComponent</c> (instances come from
    /// the resolver substitute; this type is never instantiated).
    /// </summary>
    private abstract class ActivationProbeAgentBase<TComponent>
    {
    }

    private sealed class ActivationProbeAgent : ActivationProbeAgentBase<ActivationProbeComponent>
    {
    }

    /// <summary>
    /// 解析替身：只对探测组件返回代理类型（NoAgentMode 下全部返回 null）；代理实例在激活性命周期
    /// 抛注入异常（BenignMode 下全部成功），其余解析成员返回空结果。
    /// Resolver substitute: returns the agent type only for the probe component (null for
    /// everything, or everything in NoAgentMode); the agent instance throws the injected
    /// exception during the activation lifecycle (all members succeed in BenignMode); the
    /// remaining resolution members return empty results.
    /// </summary>
    private sealed class FakeActivationResolver : IHotfixResolver
    {
        /// <summary>
        /// 注入异常的标记消息。
        /// Marker message of the injected exception.
        /// </summary>
        public const string ProbeMessage = "C341 activation probe failure";

        /// <summary>
        /// 全部组件视为未实现 Agent（GetAgentType 恒返回 null）。
        /// Treat every component as having no Agent implementation (GetAgentType always null).
        /// </summary>
        public bool NoAgentMode;

        /// <summary>
        /// 良性模式：代理生命周期全部成功，不抛异常。
        /// Benign mode: every agent lifecycle member succeeds; nothing throws.
        /// </summary>
        public bool BenignMode;

        public Assembly HotfixAssembly
        {
            get { return typeof(FakeActivationResolver).Assembly; }
        }

        public bool InTransition
        {
            get { return false; }
        }

        public Type GetAgentType(Type componentType)
        {
            if (NoAgentMode || componentType != typeof(ActivationProbeComponent))
            {
                return null;
            }

            return typeof(ActivationProbeAgent);
        }

        public Type GetComponentType(Type agentType)
        {
            return null;
        }

        public T GetAgent<T>(BaseComponent component, Type refAssemblyType) where T : class, IComponentAgent
        {
            return (T)(IComponentAgent)new ProbeComponentAgent { Benign = BenignMode };
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
    /// 探测代理：<see cref="BeforeActivationAsync"/> 抛注入异常（良性模式下全部成功）；
    /// 激活路径外成员不可达，抛 <see cref="NotImplementedException"/> 兜底。
    /// Probe agent: <see cref="BeforeActivationAsync"/> throws the injected exception (all
    /// members succeed in benign mode); members off the activation path are unreachable and
    /// throw <see cref="NotImplementedException"/> as a guard.
    /// </summary>
    private sealed class ProbeComponentAgent : IComponentAgent
    {
        public bool Benign;

        public long ActorId
        {
            get { throw new NotImplementedException(); }
        }

        public IComponent Owner
        {
            get { throw new NotImplementedException(); }
        }

        public ushort OwnerType
        {
            get { throw new NotImplementedException(); }
        }

        public void SetOwner(IComponent owner)
        {
            throw new NotImplementedException();
        }

        public Task<bool> BeforeActivationAsync()
        {
            return Benign ? Task.FromResult(true) : Task.FromException<bool>(new InvalidOperationException(FakeActivationResolver.ProbeMessage));
        }

        public Task<bool> ActiveAsync()
        {
            return Task.FromResult(true);
        }

        public Task<bool> AfterActivationAsync()
        {
            return Task.FromResult(true);
        }

        public Task<bool> BeforeInActivationAsync()
        {
            throw new NotImplementedException();
        }

        public Task InactiveAsync()
        {
            throw new NotImplementedException();
        }

        public Task<bool> AfterInActivationAsync()
        {
            throw new NotImplementedException();
        }

        public void Tell(Action work, int timeOut = -1, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public void Tell(Func<Task> work, int timeOut = -1, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task SendAsync(Action work, int timeOut = -1, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<T> SendAsync<T>(Func<T> work, int timeOut = -1, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task SendAsync(Func<Task> work, int timeOut = -1, bool checkLock = true, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<T> SendAsync<T>(Func<Task<T>> work, int timeOut = -1, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<IComponentAgent> GetComponentAgentAsync(Type agentType, bool isNew = true)
        {
            throw new NotImplementedException();
        }

        public Task<T> GetComponentAgentAsync<T>(bool isNew = true) where T : IComponentAgent
        {
            throw new NotImplementedException();
        }
    }
}
