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
//   禁止利用本项目实施危害国家安全、破坏社会秩序、
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
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.Collections.Concurrent;
using System.Reflection;
using GameFrameX.Core.Abstractions;
using GameFrameX.Core.Abstractions.Agent;
using GameFrameX.Core.Abstractions.Events;
using GameFrameX.Core.Abstractions.Hotfix;
using GameFrameX.Core.Actors;
using GameFrameX.Core.Components;
using GameFrameX.Core.Hotfix;
using GameFrameX.DataBase;
using GameFrameX.Network.Abstractions;
using GameFrameX.Network.HTTP;
using GameFrameX.Utility.Setting;
using Xunit;

namespace GameFrameX.Hotfix.Tests;

/// <summary>
/// 组件反激活故障隔离回归测试（C436 / GFX-1176 CR-H02）：代理反激活钩子抛异常时组件 <c>IsActive</c> 必须落为 false；
/// <see cref="Actor.InactiveAsync"/> 单个组件失败不得中断其余组件，状态登记不得残留，Actor 仍可被回收。
/// Regression tests for deactivation fault isolation (C436): a throwing agent hook still resets
/// <c>IsActive</c>, and one failing component never stops the rest from deactivating.
/// </summary>
[Collection(HotfixManagerStaticStateCollection.Name)]
public sealed class ActorDeactivationFailureIsolationTests : IDisposable
{
    private readonly ConcurrentDictionary<long, IsolationProbeState> _stateDictionary = GetStateDictionary();

    /// <summary>
    /// 初始化进程级设置并注入解析替身（抛异常组件返回抛异常代理，其余组件返回 null 代理）。
    /// </summary>
    public ActorDeactivationFailureIsolationTests()
    {
        if (GlobalSettings.CurrentSetting == null)
        {
            GlobalSettings.SetCurrentSetting(new AppSetting { ServerId = 1 });
        }

        HotfixManager.SetResolverForTesting(new ThrowingHookResolver());
    }

    /// <summary>
    /// 复原解析宿主并清空探针状态字典。
    /// </summary>
    public void Dispose()
    {
        HotfixManager.SetResolverForTesting(null);
        _stateDictionary.Clear();
    }

    /// <summary>
    /// 单个组件：代理钩子抛异常时异常继续上抛，但 IsActive 已落为 false（改前永久卡 true）。
    /// </summary>
    [Fact]
    public async Task ComponentInactive_AgentHookThrows_IsActiveResetAndExceptionPropagates()
    {
        var component = new BeforeHookThrowingComponent { Actor = new Actor(910001, 1) };
        await component.ActiveAsync();
        Assert.True(component.IsActive);

        await Assert.ThrowsAsync<InvalidOperationException>(() => component.InactiveAsync());

        Assert.False(component.IsActive);
    }

    /// <summary>
    /// 两个钩子抛异常的组件 + 一个状态组件：Actor 反激活不抛出、全部组件 IsActive == false、
    /// 状态登记无残留、Actor 可被回收（改前首个异常即中断循环并外抛）。
    /// </summary>
    [Fact]
    public async Task ActorInactive_ComponentsThrow_AllComponentsInactiveAndStateUnregistered()
    {
        const long actorIdentifier = 910011;
        var actor = new Actor(actorIdentifier, 1);
        var components = GetComponentsMap(actor);
        var beforeHookThrowing = new BeforeHookThrowingComponent { Actor = actor };
        var afterHookThrowing = new AfterHookThrowingComponent { Actor = actor };
        var stateComponent = new IsolationProbeStateComponent { Actor = actor };
        components[typeof(BeforeHookThrowingComponent)] = beforeHookThrowing;
        components[typeof(AfterHookThrowingComponent)] = afterHookThrowing;
        components[typeof(IsolationProbeStateComponent)] = stateComponent;
        await beforeHookThrowing.ActiveAsync();
        await afterHookThrowing.ActiveAsync();
        await stateComponent.ActiveAsync();
        Assert.True(_stateDictionary.ContainsKey(actorIdentifier));

        var exception = await Record.ExceptionAsync(() => actor.InactiveAsync());

        Assert.Null(exception);
        Assert.False(beforeHookThrowing.IsActive);
        Assert.False(afterHookThrowing.IsActive);
        Assert.False(stateComponent.IsActive);
        Assert.False(_stateDictionary.ContainsKey(actorIdentifier));
        Assert.True(actor.ReadyToDeActive);
    }

    private static ConcurrentDictionary<Type, BaseComponent> GetComponentsMap(Actor actor)
    {
        var field = typeof(Actor).GetField("_componentsMap", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (ConcurrentDictionary<Type, BaseComponent>)field.GetValue(actor);
    }

    private static ConcurrentDictionary<long, IsolationProbeState> GetStateDictionary()
    {
        var field = typeof(StateComponent<IsolationProbeState>).GetField("StateDic", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        return (ConcurrentDictionary<long, IsolationProbeState>)field.GetValue(null);
    }

    /// <summary>
    /// 代理 BeforeInActivationAsync 抛异常的探针组件。
    /// </summary>
    private sealed class BeforeHookThrowingComponent : BaseComponent
    {
    }

    /// <summary>
    /// 代理 AfterInActivationAsync 抛异常的探针组件。
    /// </summary>
    private sealed class AfterHookThrowingComponent : BaseComponent
    {
    }

    /// <summary>
    /// 探针状态。
    /// </summary>
    private sealed class IsolationProbeState : BaseCacheState
    {
    }

    /// <summary>
    /// 探针状态组件：ActiveReadStateAsync 返回内存状态（不经 GameDb），并提交脏检查基线，
    /// 使「可回收」判定只取决于组件是否卡在激活态，而非新建状态未落库。
    /// </summary>
    private sealed class IsolationProbeStateComponent : StateComponent<IsolationProbeState>
    {
        protected override Task<IsolationProbeState> ActiveReadStateAsync()
        {
            var state = new IsolationProbeState { Id = ActorId };
            state.LoadFromDbPostHandler();
            return Task.FromResult(state);
        }
    }

    /// <summary>
    /// 解析替身：两个抛异常组件分别返回对应钩子抛异常的代理，其余组件返回 null 代理。
    /// </summary>
    private sealed class ThrowingHookResolver : IHotfixResolver
    {
        public Assembly HotfixAssembly
        {
            get { return typeof(ThrowingHookResolver).Assembly; }
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
            if (component is BeforeHookThrowingComponent)
            {
                return (T)(IComponentAgent)new ThrowingHookAgent(throwBefore: true);
            }

            if (component is AfterHookThrowingComponent)
            {
                return (T)(IComponentAgent)new ThrowingHookAgent(throwBefore: false);
            }

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
    /// 探针代理：按构造参数在 BeforeInActivationAsync 或 AfterInActivationAsync 抛异常；反激活路径外成员不可达。
    /// </summary>
    private sealed class ThrowingHookAgent : IComponentAgent
    {
        private readonly bool _throwBefore;

        public ThrowingHookAgent(bool throwBefore)
        {
            _throwBefore = throwBefore;
        }

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
            throw new NotImplementedException();
        }

        public Task<bool> ActiveAsync()
        {
            throw new NotImplementedException();
        }

        public Task<bool> AfterActivationAsync()
        {
            throw new NotImplementedException();
        }

        public Task<bool> BeforeInActivationAsync()
        {
            return _throwBefore ? Task.FromException<bool>(new InvalidOperationException("before inactivation failed")) : Task.FromResult(true);
        }

        public Task InactiveAsync()
        {
            return Task.CompletedTask;
        }

        public Task<bool> AfterInActivationAsync()
        {
            return _throwBefore ? Task.FromResult(true) : Task.FromException<bool>(new InvalidOperationException("after inactivation failed"));
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
