// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规及开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file at the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
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
using GameFrameX.Core.Abstractions.Agent;
using GameFrameX.Utility.Setting;
using GameFrameX.Core.Abstractions.Events;
using GameFrameX.Core.Abstractions.Hotfix;
using GameFrameX.Core.Actors;
using GameFrameX.Core.Components;
using GameFrameX.Core.Hotfix;
using GameFrameX.DataBase;
using GameFrameX.Network.Abstractions;
using GameFrameX.Network.HTTP;
using Xunit;

namespace GameFrameX.Hotfix.Tests;

/// <summary>
/// 状态字典生命周期键契约回归测试（C313 / GFX-1024）：
/// <see cref="StateComponent{TState}" /> 的静态 StateDic 登记、移除、回存派发必须统一以 ActorId 为键。
/// State.Id 与 ActorId 不同的状态在组件反激活后不得残留字典（内存泄漏根因），
/// 且登记键必须是 ActorId 本身（回存派发按 owner actor 查找的同一键）；同 Actor 重复读取原地替换。
/// </summary>
/// <remarks>
/// StateDic 为 private static，经反射读取；探针状态类型为测试私有类型，
/// 闭合泛型 <c>StateComponent&lt;StateDictionaryActorKeyProbeState&gt;</c> 的字典进程内独占，零跨测试干扰。
/// </remarks>
[Collection(HotfixManagerStaticStateCollection.Name)]
public sealed class StateComponentStateDictionaryActorKeyTests : IDisposable
{
    private readonly ConcurrentDictionary<long, StateDictionaryActorKeyProbeState> _stateDictionary = GetStateDictionary();

    /// <summary>
    /// 注入 <c>GetAgent</c> 返回 null 的最小解析替身，使 <c>base.InactiveAsync</c> 的代理段干净跳过；
    /// actor 队列路径（Actor 构造 Tell）依赖进程级设置，沿 ComponentAgentEventDispatchTests 先例做缺省初始化。
    /// </summary>
    public StateComponentStateDictionaryActorKeyTests()
    {
        if (GlobalSettings.CurrentSetting == null)
        {
            GlobalSettings.SetCurrentSetting(new AppSetting { ServerId = 1 });
        }

        HotfixManager.SetResolverForTesting(new NullAgentResolver());
    }

    /// <summary>
    /// 复原解析替身并清空探针字典，不污染同 Collection 的其他测试。
    /// </summary>
    public void Dispose()
    {
        HotfixManager.SetResolverForTesting(null);
        _stateDictionary.Clear();
    }

    /// <summary>
    /// State.Id != ActorId 时反激活必须移除登记：改前登记键为 State.Id、移除键为 ActorId，
    /// 删除落空导致条目永久驻留（本用例在修复前必败）。
    /// </summary>
    [Fact]
    public async Task StateWithIdentifierDifferentFromActor_AfterInactive_DictionaryIsEmpty()
    {
        const long actorIdentifier = 900001;
        const long stateIdentifier = 900002;
        var actor = new Actor(actorIdentifier, 1);
        var state = new StateDictionaryActorKeyProbeState { Id = stateIdentifier };
        var component = new StateDictionaryActorKeyProbeStateComponent(state) { Actor = actor };

        await component.ReadStateAsync();
        Assert.Single(_stateDictionary);

        await component.InactiveAsync();
        Assert.Empty(_stateDictionary);
    }

    /// <summary>
    /// 登记键必须是 ActorId（而非 State.Id）；同 Actor 重复读取以新实例原地替换，不产生第二条登记。
    /// </summary>
    [Fact]
    public async Task StateRegistration_IsKeyedByActorIdentifier_AndReplacesOnRepeatedRead()
    {
        const long actorIdentifier = 900011;
        const long stateIdentifier = 900012;
        var actor = new Actor(actorIdentifier, 1);
        var firstState = new StateDictionaryActorKeyProbeState { Id = stateIdentifier };
        var component = new StateDictionaryActorKeyProbeStateComponent(firstState) { Actor = actor };

        await component.ReadStateAsync();

        Assert.True(_stateDictionary.ContainsKey(actorIdentifier));
        Assert.False(_stateDictionary.ContainsKey(stateIdentifier));
        Assert.Same(firstState, _stateDictionary[actorIdentifier]);

        var secondState = new StateDictionaryActorKeyProbeState { Id = stateIdentifier };
        component.SetProbeState(secondState);
        await component.ReadStateAsync();

        Assert.Single(_stateDictionary);
        Assert.Same(secondState, _stateDictionary[actorIdentifier]);
    }

    /// <summary>
    /// 反射获取闭合泛型组件类型的私有静态 StateDic。
    /// </summary>
    /// <returns>探针状态类型的静态状态字典</returns>
    private static ConcurrentDictionary<long, StateDictionaryActorKeyProbeState> GetStateDictionary()
    {
        var field = typeof(StateComponent<StateDictionaryActorKeyProbeState>).GetField("StateDic", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.True(field != null, "StateComponent<TState>.StateDic 私有静态字段应存在");
        return (ConcurrentDictionary<long, StateDictionaryActorKeyProbeState>)field.GetValue(null);
    }

    /// <summary>
    /// 探针状态：主键 Id 由用例显式指定，用于构造 State.Id != ActorId 场景。
    /// </summary>
    private sealed class StateDictionaryActorKeyProbeState : BaseCacheState
    {
    }

    /// <summary>
    /// 探针状态组件：ActiveReadStateAsync 返回预设状态（绕开 GameDb 查询路径）。
    /// </summary>
    private sealed class StateDictionaryActorKeyProbeStateComponent : StateComponent<StateDictionaryActorKeyProbeState>
    {
        private StateDictionaryActorKeyProbeState _probeState;

        /// <summary>
        /// 以预设状态构造探针组件。
        /// </summary>
        /// <param name="probeState">预设状态实例</param>
        public StateDictionaryActorKeyProbeStateComponent(StateDictionaryActorKeyProbeState probeState)
        {
            _probeState = probeState;
        }

        /// <summary>
        /// 替换预设状态，用于验证重复读取的原地替换语义。
        /// </summary>
        /// <param name="probeState">新的预设状态实例</param>
        public void SetProbeState(StateDictionaryActorKeyProbeState probeState)
        {
            _probeState = probeState;
        }

        /// <summary>
        /// 返回预设状态，不触发 GameDb 读取。
        /// </summary>
        /// <returns>预设状态实例</returns>
        protected override Task<StateDictionaryActorKeyProbeState> ActiveReadStateAsync()
        {
            return Task.FromResult(_probeState);
        }
    }

    /// <summary>
    /// 最小解析替身：GetAgent 返回 null（BaseComponent.InactiveAsync 的代理段直接跳过），其余成员不被本测试路径消费。
    /// </summary>
    private sealed class NullAgentResolver : IHotfixResolver
    {
        /// <summary>当前热更程序集（测试路径不消费）</summary>
        public Assembly HotfixAssembly
        {
            get { return typeof(NullAgentResolver).Assembly; }
        }

        /// <summary>过渡态（测试路径不消费）</summary>
        public bool InTransition
        {
            get { return false; }
        }

        /// <summary>代理类型解析（测试路径不消费）</summary>
        public Type GetAgentType(Type componentType)
        {
            return null;
        }

        /// <summary>组件类型解析（测试路径不消费）</summary>
        public Type GetComponentType(Type agentType)
        {
            return null;
        }

        /// <summary>代理实例解析：返回 null 使 inactive 链路干净跳过代理段</summary>
        public T GetAgent<T>(BaseComponent component, Type refAssemblyType) where T : class, IComponentAgent
        {
            return null;
        }

        /// <summary>RPC 处理器解析（测试路径不消费）</summary>
        public IMessageHandler GetRpcHandler(Type requestType)
        {
            return null;
        }

        /// <summary>消息处理器解析（测试路径不消费）</summary>
        public IMessageHandler GetMessageHandler(int messageId)
        {
            return null;
        }

        /// <summary>HTTP 处理器解析（测试路径不消费）</summary>
        public BaseHttpHandler GetHttpHandler(string command)
        {
            return null;
        }

        /// <summary>HTTP 处理器列表（测试路径不消费）</summary>
        public List<BaseHttpHandler> GetListHttpHandler()
        {
            return new List<BaseHttpHandler>();
        }

        /// <summary>事件监听器查找（按 ActorType，测试路径不消费）</summary>
        public List<IEventListener> FindListeners(ushort actorType, Type eventArgsType)
        {
            return new List<IEventListener>();
        }

        /// <summary>事件监听器查找（按事件类型，测试路径不消费）</summary>
        public List<IEventListener> FindListeners(Type eventArgsType)
        {
            return new List<IEventListener>();
        }

        /// <summary>实例解析（测试路径不消费）</summary>
        public T GetInstance<T>(string typeName, Type refAssemblyType = null)
        {
            return default;
        }
    }
}
