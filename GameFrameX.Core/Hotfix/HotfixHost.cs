// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规之开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights or interests of others, as prohibited by laws and regulations.
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
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

using System.Reflection;
using GameFrameX.Core.Abstractions.Agent;
using GameFrameX.Core.Abstractions.Events;
using GameFrameX.Core.Abstractions.Hotfix;
using GameFrameX.Core.Components;
using GameFrameX.Foundation.Utility;
using GameFrameX.Network.Abstractions;
using GameFrameX.Network.HTTP;
using GameFrameX.Utility.Hotload;
using GameFrameX.Utility.Setting;

namespace GameFrameX.Core.Hotfix;

/// <summary>
/// 热更宿主：<see cref="IHotfixResolver"/> 的生产实现——持有当前模块与退役链，承载全部解析路由与重载编排。
/// Hotfix host: the production implementation of <see cref="IHotfixResolver"/> — owns the current module and the
/// retirement chain, carrying all resolution routing and reload orchestration.
/// </summary>
/// <remarks>
/// 历史上这些逻辑散在 <c>HotfixManager</c> 静态字段里（<c>_module</c> / <c>OldModuleMap</c> / <c>DoingHotfix</c>），
/// 四个解析方法各自重复「按程序集身份回退退役模块」循环；现收敛为 <see cref="FindRetiredOwner"/> 单点，
/// 新增查找方法不会再漏写退役路由。
/// Historically this logic lived in <c>HotfixManager</c> static fields (<c>_module</c> / <c>OldModuleMap</c> /
/// <c>DoingHotfix</c>) with the assembly-identity fallback loop duplicated across four resolution methods; it now
/// converges into the single <see cref="FindRetiredOwner"/> point, so new lookups can no longer miss retirement routing.
/// </remarks>
internal sealed class HotfixHost : IHotfixResolver
{
    /// <summary>
    /// 退役排空宽限期（策略常量归 Core；机制由 <see cref="CollectibleAssemblyHost{TModule}"/> 提供，不携带默认值）。
    /// The retirement drain grace period (the policy constant stays in Core; the mechanism is provided by
    /// <see cref="CollectibleAssemblyHost{TModule}"/>, which carries no default).
    /// </summary>
    internal static readonly TimeSpan RetirementGracePeriod = TimeSpan.FromMinutes(10);

    /// <summary>
    /// 模块链：当前指针 + 退役编排（自 <c>HotfixManager</c> 的 OldModuleMap / DoingHotfix / 宽限 Task 下沉）。
    /// The module chain: current pointer plus retirement orchestration (sunk from <c>HotfixManager</c>'s
    /// OldModuleMap / DoingHotfix / grace task).
    /// </summary>
    private readonly CollectibleAssemblyHost<HotfixModule> _moduleChain = new();

    /// <summary>
    /// 空的事件监听器列表，用于在找不到监听器时返回（语义同历史 EmptyListenerList）。
    /// The empty listener list returned when no listeners are found (same semantics as the historical EmptyListenerList).
    /// </summary>
    private readonly List<IEventListener> _emptyListenerList = new();

    /// <summary>
    /// 获取当前热更模块。
    /// Gets the current hotfix module.
    /// </summary>
    internal HotfixModule CurrentModule
    {
        get;
        private set;
    }

    /// <summary>
    /// 获取最近一次热更新重载的时间。
    /// Gets the time of the most recent hot reload.
    /// </summary>
    internal DateTime ReloadTime
    {
        get;
        private set;
    }

    /// <summary>
    /// 获取当前热更程序集（未装载过为 <c>null</c>）。
    /// Gets the current hotfix assembly (<c>null</c> when never loaded).
    /// </summary>
    public Assembly HotfixAssembly
    {
        get { return CurrentModule?.HotfixAssembly; }
    }

    /// <summary>
    /// 获取是否处于热更过渡期（退役链未排空；期间常驻缓存必须视为失效）。
    /// Gets whether a hot transition is in progress (retirement chain not drained; resident caches must be treated as invalid meanwhile).
    /// </summary>
    public bool InTransition
    {
        get { return _moduleChain.InTransition; }
    }

    /// <summary>
    /// 判断当前模块是否已从指定路径装载（同路径幂等判重；--AllInOne 多 Role 重复装载在此短路）。
    /// Tells whether the current module was already loaded from the given path (the same-path idempotency check;
    /// repeated loads by extra roles in --AllInOne short-circuit here).
    /// </summary>
    /// <param name="path">热更程序集文件完整路径 / The full path of the hotfix assembly file.</param>
    /// <returns>已从该路径装载返回 <c>true</c> / <c>true</c> when already loaded from this path.</returns>
    internal bool IsSamePathLoaded(string path)
    {
        return CurrentModule != null && string.Equals(CurrentModule.DllPath, path, StringComparison.Ordinal);
    }

    /// <summary>
    /// 注册当前模块（同步装载路径专用：保持历史行为——不退役旧模块、不回调桥接；差异观察项见 change C216 Review Notes）。
    /// Registers the current module (for the synchronous load path only: preserves historical behavior — no
    /// retirement, no bridge callback; the asymmetry observation lives in change C216 Review Notes).
    /// </summary>
    /// <param name="module">新装载的模块 / The newly loaded module.</param>
    internal void RegisterCurrent(HotfixModule module)
    {
        CurrentModule = module;
        _moduleChain.Register(module);
    }

    /// <summary>
    /// 重载装载：旧模块入退役链（宽限排空后由模块链卸载并验证），切换当前指针，回调桥接 <c>OnLoadSuccess</c>。
    /// Reload load: the old module joins the retirement chain (drained, unloaded and verified by the module chain
    /// after grace), the current pointer swaps, and the bridge's <c>OnLoadSuccess</c> is awaited.
    /// </summary>
    /// <param name="newModule">新的热更模块 / The new hotfix module.</param>
    /// <param name="setting">应用程序配置 / The application setting.</param>
    /// <param name="reload">是否为重新加载 / Whether this is a reload.</param>
    /// <returns>装载是否成功 / Whether the load succeeded.</returns>
    internal async Task<bool> Load(HotfixModule newModule, AppSetting setting, bool reload)
    {
        ReloadTime = TimerHelper.GetNowWithUtc();
        if (reload)
        {
            // 旧模块退役：InTransition 同步置位，宽限排空 + 卸载 GC 验证由模块链承担（时序不变量见其类备注）
            // Retire the old module: InTransition flips on synchronously; grace drain plus unload GC verification
            // belong to the module chain (timing invariant in its class remarks)
            _moduleChain.Retire(CurrentModule, RetirementGracePeriod);
        }

        CurrentModule = newModule;
        _moduleChain.Register(newModule);
        if (newModule.HotfixBridge != null)
        {
            return await newModule.HotfixBridge.OnLoadSuccess(setting, reload);
        }

        return true;
    }

    /// <summary>
    /// 停止当前模块（委托桥接 <c>Stop</c>；未装载或无桥接时为已完成任务）。
    /// Stops the current module (delegates to the bridge's <c>Stop</c>; a completed task when unloaded or bridge-less).
    /// </summary>
    /// <param name="message">停止原因 / The stop reason.</param>
    /// <returns>停止任务 / The stop task.</returns>
    internal Task Stop(string message)
    {
        return CurrentModule?.HotfixBridge?.Stop(message) ?? Task.CompletedTask;
    }

    /// <summary>
    /// 按程序集身份定位退役模块（宽限期内 in-flight 请求归属原模块）；未命中返回 <c>null</c>。
    /// Locates a retired module by assembly identity (in-flight requests during grace belong to their original
    /// module); returns <c>null</c> on miss.
    /// </summary>
    /// <param name="assembly">程序集身份 / The assembly identity.</param>
    /// <returns>退役模块或 <c>null</c> / The retired module, or <c>null</c>.</returns>
    private HotfixModule FindRetiredOwner(Assembly assembly)
    {
        return _moduleChain.TryGetOwner(assembly, out var module) ? module : null;
    }

    /// <summary>
    /// 获取组件对应的代理类型（退役期路由语义见契约备注）。
    /// Gets the agent type for a component (retirement routing semantics in the contract remarks).
    /// </summary>
    /// <param name="componentType">组件类型 / The component type.</param>
    /// <returns>代理类型 / The agent type.</returns>
    public Type GetAgentType(Type componentType)
    {
        return (FindRetiredOwner(componentType.Assembly) ?? CurrentModule).GetAgentType(componentType);
    }

    /// <summary>
    /// 获取代理对应的组件类型（退役期路由语义见契约备注）。
    /// Gets the component type for an agent (retirement routing semantics in the contract remarks).
    /// </summary>
    /// <param name="agentType">代理类型 / The agent type.</param>
    /// <returns>组件类型 / The component type.</returns>
    public Type GetComponentType(Type agentType)
    {
        return (FindRetiredOwner(agentType.Assembly) ?? CurrentModule).GetComponentType(agentType);
    }

    /// <summary>
    /// 获取组件的代理实例（退役期按代理/引用程序集身份路由，见契约备注）。
    /// Gets the agent instance for a component (routed by agent/reference assembly identity during retirement, see contract remarks).
    /// </summary>
    /// <param name="component">组件实例 / The component instance.</param>
    /// <param name="refAssemblyType">引用程序集类型 / A type from the caller's hotfix assembly.</param>
    /// <typeparam name="T">代理类型 / The agent type.</typeparam>
    /// <returns>代理实例 / The agent instance.</returns>
    public T GetAgent<T>(BaseComponent component, Type refAssemblyType) where T : class, IComponentAgent
    {
        var owner = FindRetiredOwner(typeof(T).Assembly) ?? FindRetiredOwner(refAssemblyType?.Assembly);
        return (owner ?? CurrentModule).GetAgent<T>(component);
    }

    /// <summary>
    /// 获取RPC处理器实例。
    /// Gets the RPC handler instance.
    /// </summary>
    /// <param name="requestType">请求消息类型 / The request message type.</param>
    /// <returns>处理器实例 / The handler instance.</returns>
    public IMessageHandler GetRpcHandler(Type requestType)
    {
        return CurrentModule.GetRpcHandler(requestType);
    }

    /// <summary>
    /// 获取消息处理器。
    /// Gets the message handler.
    /// </summary>
    /// <param name="messageId">消息ID / The message identifier.</param>
    /// <returns>处理器实例 / The handler instance.</returns>
    public IMessageHandler GetMessageHandler(int messageId)
    {
        return CurrentModule.GetMessageHandler(messageId);
    }

    /// <summary>
    /// 获取HTTP处理器。
    /// Gets the HTTP handler.
    /// </summary>
    /// <param name="command">HTTP命令 / The HTTP command.</param>
    /// <returns>处理器实例 / The handler instance.</returns>
    public BaseHttpHandler GetHttpHandler(string command)
    {
        return CurrentModule.GetHttpHandler(command);
    }

    /// <summary>
    /// 获取所有HTTP处理器列表（去重后）。
    /// Gets the deduplicated list of all HTTP handlers.
    /// </summary>
    /// <returns>处理器列表 / The handler list.</returns>
    public List<BaseHttpHandler> GetListHttpHandler()
    {
        return CurrentModule.GetListHttpHandler();
    }

    /// <summary>
    /// 获取指定Actor类型和事件参数类型的事件监听器列表。
    /// Gets event listeners for the given actor type and event-args type.
    /// </summary>
    /// <param name="actorType">Actor类型 / The actor type.</param>
    /// <param name="eventArgsType">事件参数类型（事件绑定键） / The event-args type (the event binding key).</param>
    /// <returns>监听器列表，如果没有则空列表 / The listener list; empty when none.</returns>
    public List<IEventListener> FindListeners(ushort actorType, Type eventArgsType)
    {
        return CurrentModule.FindListeners(actorType, eventArgsType) ?? _emptyListenerList;
    }

    /// <summary>
    /// 获取指定事件参数类型的事件监听器列表（全局聚合）。
    /// Gets event listeners for the given event-args type (globally aggregated).
    /// </summary>
    /// <param name="eventArgsType">事件参数类型（事件绑定键） / The event-args type (the event binding key).</param>
    /// <returns>监听器列表，如果没有则空列表 / The listener list; empty when none.</returns>
    public List<IEventListener> FindListeners(Type eventArgsType)
    {
        return CurrentModule.FindListeners(eventArgsType) ?? _emptyListenerList;
    }

    /// <summary>
    /// 获取指定类型的实例（主要用于获取Event, Timer, Schedule的Handler实例；退役期按引用程序集身份路由）。
    /// Gets an instance of the given type (mainly Event / Timer / Schedule handler instances; routed by reference
    /// assembly identity during retirement).
    /// </summary>
    /// <typeparam name="T">实例类型 / The instance type.</typeparam>
    /// <param name="typeName">类型名称 / The type name.</param>
    /// <param name="refAssemblyType">引用程序集类型 / A type from the caller's hotfix assembly.</param>
    /// <returns>指定类型的实例，类型名称为空则返回默认值 / The instance, or default for an empty type name.</returns>
    public T GetInstance<T>(string typeName, Type refAssemblyType = null)
    {
        if (string.IsNullOrEmpty(typeName))
        {
            return default;
        }

        var owner = FindRetiredOwner(refAssemblyType?.Assembly) ?? CurrentModule;
        return owner.GetInstance<T>(typeName);
    }
}
