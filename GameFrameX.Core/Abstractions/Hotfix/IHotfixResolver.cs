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
using GameFrameX.Core.Components;
using GameFrameX.Network.Abstractions;
using GameFrameX.Network.HTTP;

namespace GameFrameX.Core.Abstractions.Hotfix;

/// <summary>
/// 热更解析契约：代理 / 消息处理器 / HTTP 处理器 / 事件监听器的统一解析面。
/// Hotfix resolution contract: the unified resolution surface for agents, message handlers, HTTP handlers and event listeners.
/// </summary>
/// <remarks>
/// Core 各运行时子系统（Actor / 组件 / 事件 / 定时器）依赖本契约而非 <c>HotfixManager</c> 静态实现，
/// 使解析面可注入测试替身；生产装配由 <c>HotfixManager.Resolver</c> 提供唯一实例。
/// Core runtime subsystems (actors / components / events / timers) depend on this contract instead of the
/// <c>HotfixManager</c> static implementation, making the resolution surface substitutable in tests; production
/// wiring provides the single instance via <c>HotfixManager.Resolver</c>.
/// <para>
/// <b>退役期路由语义</b>：类型/实例类查找（<see cref="GetAgentType"/>、<see cref="GetComponentType"/>、
/// <see cref="GetAgent{T}"/>、<see cref="GetInstance{T}"/>）在热更过渡期内按程序集身份优先解析到<b>原属
/// （已退役）模块</b>，宽限排空窗口内旧代码的 in-flight 请求行为不变；未命中回落当前模块。处理器 /
/// 监听器类查找（<see cref="GetMessageHandler"/> 等）始终解析当前模块（与历史行为一致）。
/// <b>Retirement routing semantics</b>: type/instance lookups (<see cref="GetAgentType"/>,
/// <see cref="GetComponentType"/>, <see cref="GetAgent{T}"/>, <see cref="GetInstance{T}"/>) prefer the
/// <b>original (retired) module</b> by assembly identity during a hot transition, keeping in-flight requests on
/// old code within the drain window; on miss they fall back to the current module. Handler/listener lookups
/// (<see cref="GetMessageHandler"/> etc.) always resolve the current module (matching historical behavior).
/// </para>
/// <para>
/// <b><see cref="InTransition"/> 含义</b>：退役链未排空（存在宽限未到或未完成卸载的旧模块）。期间常驻缓存
/// （如组件代理缓存）必须视为失效——缓存实例可能来自即将卸载的程序集。
/// <b><see cref="InTransition"/> meaning</b>: the retirement chain has not drained (an old module is still within
/// grace or unloading). Resident caches (e.g. component agent caches) must be treated as invalid during it —
/// cached instances may come from an assembly about to unload.
/// </para>
/// </remarks>
public interface IHotfixResolver
{
    /// <summary>
    /// 获取当前热更程序集（未装载过为 <c>null</c>）。
    /// Gets the current hotfix assembly (<c>null</c> when never loaded).
    /// </summary>
    Assembly HotfixAssembly
    {
        get;
    }

    /// <summary>
    /// 获取是否处于热更过渡期（退役链未排空；语义见类备注）。
    /// Gets whether a hot transition is in progress (retirement chain not drained; semantics in class remarks).
    /// </summary>
    bool InTransition
    {
        get;
    }

    /// <summary>
    /// 获取组件对应的代理类型（退役期按组件程序集身份路由，见类备注）。
    /// Gets the agent type for a component (routed by assembly identity during retirement, see remarks).
    /// </summary>
    /// <param name="componentType">组件类型 / The component type.</param>
    /// <returns>代理类型 / The agent type.</returns>
    Type GetAgentType(Type componentType);

    /// <summary>
    /// 获取代理对应的组件类型（退役期按代理程序集身份路由，见类备注）。
    /// Gets the component type for an agent (routed by assembly identity during retirement, see remarks).
    /// </summary>
    /// <param name="agentType">代理类型 / The agent type.</param>
    /// <returns>组件类型 / The component type.</returns>
    Type GetComponentType(Type agentType);

    /// <summary>
    /// 获取组件的代理实例（退役期按代理/引用程序集身份路由，见类备注）。
    /// Gets the agent instance for a component (routed by agent/reference assembly identity during retirement, see remarks).
    /// </summary>
    /// <param name="component">组件实例 / The component instance.</param>
    /// <param name="refAssemblyType">引用程序集类型（调用方所在热更程序集的类型，用于退役期归属判定；可为 <c>null</c>） / A type from the caller's hotfix assembly used for retirement ownership; may be <c>null</c>.</param>
    /// <typeparam name="T">代理类型 / The agent type.</typeparam>
    /// <returns>代理实例 / The agent instance.</returns>
    T GetAgent<T>(BaseComponent component, Type refAssemblyType) where T : class, IComponentAgent;

    /// <summary>
    /// 获取RPC处理器实例（始终解析当前模块）。
    /// Gets the RPC handler instance (always resolved from the current module).
    /// </summary>
    /// <param name="requestType">请求消息类型 / The request message type.</param>
    /// <returns>处理器实例，未找到为 <c>null</c> / The handler instance, or <c>null</c> when absent.</returns>
    IMessageHandler GetRpcHandler(Type requestType);

    /// <summary>
    /// 获取消息处理器（始终解析当前模块）。
    /// Gets the message handler (always resolved from the current module).
    /// </summary>
    /// <param name="messageId">消息ID / The message identifier.</param>
    /// <returns>处理器实例，未找到为 <c>null</c> / The handler instance, or <c>null</c> when absent.</returns>
    IMessageHandler GetMessageHandler(int messageId);

    /// <summary>
    /// 获取HTTP处理器（始终解析当前模块）。
    /// Gets the HTTP handler (always resolved from the current module).
    /// </summary>
    /// <param name="command">HTTP命令 / The HTTP command.</param>
    /// <returns>处理器实例，未找到为 <c>null</c> / The handler instance, or <c>null</c> when absent.</returns>
    BaseHttpHandler GetHttpHandler(string command);

    /// <summary>
    /// 获取所有HTTP处理器列表（去重后）。
    /// Gets the deduplicated list of all HTTP handlers.
    /// </summary>
    /// <returns>处理器列表 / The handler list.</returns>
    List<BaseHttpHandler> GetListHttpHandler();

    /// <summary>
    /// 获取指定Actor类型与事件参数类型的事件监听器列表（始终解析当前模块）。
    /// Gets event listeners for the given actor type and event-args type (always from the current module).
    /// </summary>
    /// <param name="actorType">Actor类型 / The actor type.</param>
    /// <param name="eventArgsType">事件参数类型（事件绑定键） / The event-args type (the event binding key).</param>
    /// <returns>监听器列表，无则空列表 / The listener list; empty when none.</returns>
    List<IEventListener> FindListeners(ushort actorType, Type eventArgsType);

    /// <summary>
    /// 获取指定事件参数类型的事件监听器列表（全局聚合，始终解析当前模块）。
    /// Gets event listeners for the given event-args type (globally aggregated, always from the current module).
    /// </summary>
    /// <param name="eventArgsType">事件参数类型（事件绑定键） / The event-args type (the event binding key).</param>
    /// <returns>监听器列表，无则空列表 / The listener list; empty when none.</returns>
    List<IEventListener> FindListeners(Type eventArgsType);

    /// <summary>
    /// 获取指定类型名称的实例（主要用于 Event / Timer / Schedule 的处理器实例；退役期按引用程序集身份路由）。
    /// Gets an instance of the given type name (mainly Event / Timer / Schedule handler instances; routed by
    /// reference assembly identity during retirement).
    /// </summary>
    /// <param name="typeName">类型名称 / The type name.</param>
    /// <param name="refAssemblyType">引用程序集类型（退役期归属判定；可为 <c>null</c>） / A type from the caller's hotfix assembly for retirement ownership; may be <c>null</c>.</param>
    /// <typeparam name="T">实例类型 / The instance type.</typeparam>
    /// <returns>实例，类型名为空或未找到为默认值 / The instance, or default for an empty/unknown type name.</returns>
    T GetInstance<T>(string typeName, Type refAssemblyType = null);
}
