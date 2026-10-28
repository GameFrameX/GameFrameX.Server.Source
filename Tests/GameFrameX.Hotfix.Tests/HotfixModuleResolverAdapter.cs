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
//   Any legal disputes or liabilities arising from secondary development based on this project
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
using GameFrameX.Core.Abstractions.Agent;
using GameFrameX.Core.Abstractions.Events;
using GameFrameX.Core.Abstractions.Hotfix;
using GameFrameX.Core.Components;
using GameFrameX.Core.Hotfix;
using GameFrameX.Network.Abstractions;
using GameFrameX.Network.HTTP;

namespace GameFrameX.Hotfix.Tests;

/// <summary>
/// 测试用解析契约适配器：把真实 <see cref="HotfixModule" /> 的解析结果接入
/// <see cref="HotfixManager.SetResolverForTesting" />（HotfixModule 自身不实现 IHotfixResolver）。
/// </summary>
/// <remarks>
/// The testing resolver adapter that feeds a real <see cref="HotfixModule" />'s resolution results
/// into <see cref="HotfixManager.SetResolverForTesting" /> (HotfixModule itself does not implement IHotfixResolver).
/// </remarks>
internal sealed class HotfixModuleResolverAdapter : IHotfixResolver
{
    private readonly HotfixModule _module;

    /// <summary>
    /// 以真实热更模块构造适配器 / Constructs the adapter with a real hotfix module.
    /// </summary>
    /// <param name="module">热更模块 / The hotfix module</param>
    public HotfixModuleResolverAdapter(HotfixModule module)
    {
        _module = module;
    }

    /// <summary>热更程序集 / The hotfix assembly.</summary>
    public Assembly HotfixAssembly
    {
        get { return _module.HotfixAssembly; }
    }

    /// <summary>是否处于热更过渡期（测试恒为否）/ Whether in hot-swap transition (always false in tests).</summary>
    public bool InTransition
    {
        get { return false; }
    }

    /// <summary>按组件类型解析代理类型 / Resolves the agent type for a component type.</summary>
    /// <param name="componentType">组件类型 / The component type</param>
    /// <returns>代理类型 / The agent type</returns>
    public Type GetAgentType(Type componentType)
    {
        return _module.GetAgentType(componentType);
    }

    /// <summary>按代理类型解析组件类型 / Resolves the component type for an agent type.</summary>
    /// <param name="agentType">代理类型 / The agent type</param>
    /// <returns>组件类型 / The component type</returns>
    public Type GetComponentType(Type agentType)
    {
        return _module.GetComponentType(agentType);
    }

    /// <summary>按组件实例解析代理 / Resolves the agent for a component instance.</summary>
    /// <typeparam name="T">代理类型 / The agent type</typeparam>
    /// <param name="component">组件实例 / The component instance</param>
    /// <param name="refAssemblyType">引用程序集类型（本实现不读取）/ The referencing assembly type (not read)</param>
    /// <returns>代理实例 / The agent instance</returns>
    public T GetAgent<T>(BaseComponent component, Type refAssemblyType) where T : class, IComponentAgent
    {
        return _module.GetAgent<T>(component);
    }

    /// <summary>按请求类型解析 RPC 处理器 / Resolves the RPC handler for a request type.</summary>
    /// <param name="requestType">请求类型 / The request type</param>
    /// <returns>消息处理器 / The message handler</returns>
    public IMessageHandler GetRpcHandler(Type requestType)
    {
        return _module.GetRpcHandler(requestType);
    }

    /// <summary>按消息 ID 解析消息处理器 / Resolves the message handler for a message id.</summary>
    /// <param name="messageId">消息 ID / The message id</param>
    /// <returns>消息处理器 / The message handler</returns>
    public IMessageHandler GetMessageHandler(int messageId)
    {
        return _module.GetMessageHandler(messageId);
    }

    /// <summary>按命令解析 HTTP 处理器 / Resolves the HTTP handler for a command.</summary>
    /// <param name="command">命令 / The command</param>
    /// <returns>HTTP 处理器 / The HTTP handler</returns>
    public BaseHttpHandler GetHttpHandler(string command)
    {
        return _module.GetHttpHandler(command);
    }

    /// <summary>获取全部 HTTP 处理器 / Gets all HTTP handlers.</summary>
    /// <returns>HTTP 处理器列表 / The HTTP handler list</returns>
    public List<BaseHttpHandler> GetListHttpHandler()
    {
        return _module.GetListHttpHandler();
    }

    /// <summary>按 Actor 类型 + 事件类型解析监听器 / Resolves listeners for an actor type and event type.</summary>
    /// <param name="actorType">Actor 类型 / The actor type</param>
    /// <param name="eventArgsType">事件参数类型 / The event args type</param>
    /// <returns>监听器列表 / The listener list</returns>
    public List<IEventListener> FindListeners(ushort actorType, Type eventArgsType)
    {
        return _module.FindListeners(actorType, eventArgsType);
    }

    /// <summary>按事件类型聚合全部 Actor 类型的监听器 / Aggregates listeners of all actor types for an event type.</summary>
    /// <param name="eventArgsType">事件参数类型 / The event args type</param>
    /// <returns>监听器列表 / The listener list</returns>
    public List<IEventListener> FindListeners(Type eventArgsType)
    {
        return _module.FindListeners(eventArgsType);
    }

    /// <summary>按类型名解析实例 / Resolves an instance by type name.</summary>
    /// <typeparam name="T">实例类型 / The instance type</typeparam>
    /// <param name="typeName">类型名 / The type name</param>
    /// <param name="refAssemblyType">引用程序集类型（本实现不读取）/ The referencing assembly type (not read)</param>
    /// <returns>实例 / The instance</returns>
    public T GetInstance<T>(string typeName, Type refAssemblyType = null)
    {
        return _module.GetInstance<T>(typeName);
    }
}
