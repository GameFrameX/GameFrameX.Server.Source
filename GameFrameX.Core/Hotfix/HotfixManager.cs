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
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository:  https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:    https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:      https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.Reflection;
using GameFrameX.Core.Abstractions.Agent;
using GameFrameX.Core.Abstractions.Events;
using GameFrameX.Core.Abstractions.Hotfix;
using GameFrameX.Core.Components;
using GameFrameX.Network.Abstractions;
using GameFrameX.Network.HTTP;
using GameFrameX.Utility.Setting;

namespace GameFrameX.Core.Hotfix;

/// <summary>
/// 热更新管理器静态便捷层（过渡保留：转发 <see cref="HotfixRuntime.ProcessDefault"/>，公共成员已标记过时并指向注入面）。
/// </summary>
/// <remarks>
/// The static convenience layer of the hotfix manager (transitional: forwards to
/// <see cref="HotfixRuntime.ProcessDefault"/>; the public members are marked obsolete, pointing at
/// the injection seam). 组合根 / 宿主模块应注入 <see cref="HotfixRuntime"/> 实例；Core 子系统解析路由
/// 仍经内部 <see cref="Resolver"/> 缝（转发同核）；后续收敛版本迁移调用点后移除本层。
/// Compositions inject the <see cref="HotfixRuntime"/> instance; Core-subsystem resolution routing
/// keeps riding the internal <see cref="Resolver"/> seam (same core); the layer is removed once
/// later convergence versions migrate the call sites.
/// </remarks>
public static class HotfixManager
{
    /// <summary>
    /// 热更解析契约注入缝（静态便捷层：转发 <see cref="HotfixRuntime.Resolver"/>；Core 子系统与测试替身依赖该契约）。
    /// The hotfix resolution-contract seam (static forward of <see cref="HotfixRuntime.Resolver"/>).
    /// </summary>
    internal static IHotfixResolver Resolver
    {
        get { return HotfixRuntime.ProcessDefault.Resolver; }
    }

    /// <summary>
    /// 注入/复原解析契约实现（测试专用；静态便捷层转发 <see cref="HotfixRuntime.SetResolverForTesting"/>）。
    /// Injects/restores the resolution-contract implementation (testing only; static forward).
    /// </summary>
    /// <param name="resolver">替身实现；<c>null</c> 复原 / The substitute; <c>null</c> restores.</param>
    internal static void SetResolverForTesting(IHotfixResolver resolver)
    {
        HotfixRuntime.ProcessDefault.SetResolverForTesting(resolver);
    }

    /// <summary>
    /// 热更新程序集（静态便捷层：转发 <see cref="HotfixRuntime.HotfixAssembly"/>）。
    /// The hotfix assembly (static forward).
    /// </summary>
    [Obsolete("静态便捷层过渡保留：请改用注入的 HotfixRuntime 实例（进程缺省 HotfixRuntime.ProcessDefault）。Static convenience layer kept for transition; use the injected HotfixRuntime instance (process default HotfixRuntime.ProcessDefault) instead.", false)]
    public static Assembly HotfixAssembly
    {
        get { return HotfixRuntime.ProcessDefault.HotfixAssembly; }
    }

    /// <summary>
    /// 最近一次热更新重载的时间（静态便捷层：转发 <see cref="HotfixRuntime.ReloadTime"/>）。
    /// The time of the most recent hot reload (static forward).
    /// </summary>
    [Obsolete("静态便捷层过渡保留：请改用注入的 HotfixRuntime 实例（进程缺省 HotfixRuntime.ProcessDefault）。Static convenience layer kept for transition; use the injected HotfixRuntime instance (process default HotfixRuntime.ProcessDefault) instead.", false)]
    public static DateTime ReloadTime
    {
        get { return HotfixRuntime.ProcessDefault.ReloadTime; }
    }

    /// <summary>
    /// 加载热更新模块（同步入口；静态便捷层：转发 <see cref="HotfixRuntime.LoadHotfix"/>）。
    /// Loads the hotfix module (synchronous entry; static forward).
    /// </summary>
    /// <param name="setting">应用程序配置 / The application setting.</param>
    /// <param name="options">装载参数 / Load options.</param>
    /// <returns>返回是否加载成功 / Whether the load succeeded.</returns>
    [Obsolete("静态便捷层过渡保留：请改用注入的 HotfixRuntime 实例（进程缺省 HotfixRuntime.ProcessDefault）。Static convenience layer kept for transition; use the injected HotfixRuntime instance (process default HotfixRuntime.ProcessDefault) instead.", false)]
    public static bool LoadHotfix(AppSetting setting, HotfixLoadOptions options = null)
    {
        return HotfixRuntime.ProcessDefault.LoadHotfix(setting, options);
    }

    /// <summary>
    /// 加载热更新模块（重载入口；静态便捷层：转发 <see cref="HotfixRuntime.LoadHotfixModuleAsync"/>）。
    /// Loads the hotfix module (reload entry; static forward).
    /// </summary>
    /// <param name="setting">应用程序配置 / The application setting.</param>
    /// <param name="options">装载参数 / Load options.</param>
    /// <returns>返回是否加载成功 / Whether the load succeeded.</returns>
    [Obsolete("静态便捷层过渡保留：请改用注入的 HotfixRuntime 实例（进程缺省 HotfixRuntime.ProcessDefault）。Static convenience layer kept for transition; use the injected HotfixRuntime instance (process default HotfixRuntime.ProcessDefault) instead.", false)]
    public static Task<bool> LoadHotfixModuleAsync(AppSetting setting, HotfixLoadOptions options = null)
    {
        return HotfixRuntime.ProcessDefault.LoadHotfixModuleAsync(setting, options);
    }

    /// <summary>
    /// 停止热更新模块（静态便捷层：转发 <see cref="HotfixRuntime.StopAsync"/>）。
    /// Stops the hotfix module (static forward).
    /// </summary>
    /// <param name="message">停止原因 / The stop reason.</param>
    /// <returns>停止任务 / The stop task.</returns>
    [Obsolete("静态便捷层过渡保留：请改用注入的 HotfixRuntime 实例（进程缺省 HotfixRuntime.ProcessDefault）。Static convenience layer kept for transition; use the injected HotfixRuntime instance (process default HotfixRuntime.ProcessDefault) instead.", false)]
    public static Task StopAsync(string message = "")
    {
        return HotfixRuntime.ProcessDefault.StopAsync(message);
    }

    /// <summary>
    /// 获取组件的代理实例（静态便捷层：转发 <see cref="HotfixRuntime.GetAgent{T}"/>）。
    /// Gets the agent instance for a component (static forward).
    /// </summary>
    /// <param name="component">组件实例 / The component instance.</param>
    /// <param name="refAssemblyType">引用程序集类型 / A type from the caller's hotfix assembly.</param>
    /// <typeparam name="T">代理类型 / The agent type.</typeparam>
    /// <returns>返回代理实例 / The agent instance.</returns>
    [Obsolete("静态便捷层过渡保留：请改用注入的 HotfixRuntime 实例（进程缺省 HotfixRuntime.ProcessDefault）。Static convenience layer kept for transition; use the injected HotfixRuntime instance (process default HotfixRuntime.ProcessDefault) instead.", false)]
    public static T GetAgent<T>(BaseComponent component, Type refAssemblyType) where T : class, IComponentAgent
    {
        return HotfixRuntime.ProcessDefault.GetAgent<T>(component, refAssemblyType);
    }

    /// <summary>
    /// 获取RPC处理器实例（静态便捷层：转发 <see cref="HotfixRuntime.GetRpcHandler"/>）。
    /// Gets the RPC handler instance (static forward).
    /// </summary>
    /// <param name="type">请求消息的类型 / The request message type.</param>
    /// <returns>对应的 <see cref="IMessageHandler"/> 实例，如果未找到则返回 null / The matching handler, or null when absent.</returns>
    [Obsolete("静态便捷层过渡保留：请改用注入的 HotfixRuntime 实例（进程缺省 HotfixRuntime.ProcessDefault）。Static convenience layer kept for transition; use the injected HotfixRuntime instance (process default HotfixRuntime.ProcessDefault) instead.", false)]
    public static IMessageHandler GetRpcHandler(Type type)
    {
        return HotfixRuntime.ProcessDefault.GetRpcHandler(type);
    }

    /// <summary>
    /// 获取消息处理器（静态便捷层：转发 <see cref="HotfixRuntime.GetMessageHandler"/>）。
    /// Gets the message handler (static forward).
    /// </summary>
    /// <param name="msgId">消息ID / The message identifier.</param>
    /// <returns>返回对应的消息处理器 / The matching handler.</returns>
    [Obsolete("静态便捷层过渡保留：请改用注入的 HotfixRuntime 实例（进程缺省 HotfixRuntime.ProcessDefault）。Static convenience layer kept for transition; use the injected HotfixRuntime instance (process default HotfixRuntime.ProcessDefault) instead.", false)]
    public static IMessageHandler GetMessageHandler(int msgId)
    {
        return HotfixRuntime.ProcessDefault.GetMessageHandler(msgId);
    }

    /// <summary>
    /// 获取HTTP消息处理器（静态便捷层：转发 <see cref="HotfixRuntime.GetHttpHandler"/>）。
    /// Gets the HTTP handler (static forward).
    /// </summary>
    /// <param name="cmd">HTTP命令 / The HTTP command.</param>
    /// <returns>返回对应HTTP处理器 / The matching HTTP handler.</returns>
    [Obsolete("静态便捷层过渡保留：请改用注入的 HotfixRuntime 实例（进程缺省 HotfixRuntime.ProcessDefault）。Static convenience layer kept for transition; use the injected HotfixRuntime instance (process default HotfixRuntime.ProcessDefault) instead.", false)]
    public static BaseHttpHandler GetHttpHandler(string cmd)
    {
        return HotfixRuntime.ProcessDefault.GetHttpHandler(cmd);
    }

    /// <summary>
    /// 获取所有HTTP消息处理器列表（静态便捷层：转发 <see cref="HotfixRuntime.GetListHttpHandler"/>）。
    /// Gets the list of all HTTP handlers (static forward).
    /// </summary>
    /// <returns>返回HTTP处理器列表 / The HTTP handler list.</returns>
    [Obsolete("静态便捷层过渡保留：请改用注入的 HotfixRuntime 实例（进程缺省 HotfixRuntime.ProcessDefault）。Static convenience layer kept for transition; use the injected HotfixRuntime instance (process default HotfixRuntime.ProcessDefault) instead.", false)]
    public static List<BaseHttpHandler> GetListHttpHandler()
    {
        return HotfixRuntime.ProcessDefault.GetListHttpHandler();
    }

    /// <summary>
    /// 获取指定Actor类型和事件参数类型的事件监听器列表（静态便捷层：转发 <see cref="HotfixRuntime.FindListeners(ushort, Type)"/>）。
    /// Gets event listeners for the given actor type and event-args type (static forward).
    /// </summary>
    /// <param name="actorType">Actor类型 / The actor type.</param>
    /// <param name="eventArgsType">事件参数类型（事件绑定键） / The event-args type (the event binding key).</param>
    /// <returns>返回监听器列表，如果没有则空列表 / The listener list; empty when none.</returns>
    [Obsolete("静态便捷层过渡保留：请改用注入的 HotfixRuntime 实例（进程缺省 HotfixRuntime.ProcessDefault）。Static convenience layer kept for transition; use the injected HotfixRuntime instance (process default HotfixRuntime.ProcessDefault) instead.", false)]
    public static List<IEventListener> FindListeners(ushort actorType, Type eventArgsType)
    {
        return HotfixRuntime.ProcessDefault.FindListeners(actorType, eventArgsType);
    }

    /// <summary>
    /// 获取指定事件参数类型的事件监听器列表（静态便捷层：转发 <see cref="HotfixRuntime.FindListeners(Type)"/>）。
    /// Gets event listeners for the given event-args type (static forward).
    /// </summary>
    /// <param name="eventArgsType">事件参数类型（事件绑定键） / The event-args type (the event binding key).</param>
    /// <returns>返回监听器列表，如果没有则空列表 / The listener list; empty when none.</returns>
    [Obsolete("静态便捷层过渡保留：请改用注入的 HotfixRuntime 实例（进程缺省 HotfixRuntime.ProcessDefault）。Static convenience layer kept for transition; use the injected HotfixRuntime instance (process default HotfixRuntime.ProcessDefault) instead.", false)]
    public static List<IEventListener> FindListeners(Type eventArgsType)
    {
        return HotfixRuntime.ProcessDefault.FindListeners(eventArgsType);
    }

    /// <summary>
    /// 获取指定类型的实例（静态便捷层：转发 <see cref="HotfixRuntime.GetInstance{T}"/>）。
    /// Gets an instance of the given type (static forward).
    /// </summary>
    /// <typeparam name="T">实例类型 / The instance type.</typeparam>
    /// <param name="typeName">类型名称 / The type name.</param>
    /// <param name="refAssemblyType">引用程序集类型 / A type from the caller's hotfix assembly.</param>
    /// <returns>返回指定类型的实例，如果类型名称为空则返回默认值 / The instance, or default for an empty type name.</returns>
    [Obsolete("静态便捷层过渡保留：请改用注入的 HotfixRuntime 实例（进程缺省 HotfixRuntime.ProcessDefault）。Static convenience layer kept for transition; use the injected HotfixRuntime instance (process default HotfixRuntime.ProcessDefault) instead.", false)]
    public static T GetInstance<T>(string typeName, Type refAssemblyType = null)
    {
        return HotfixRuntime.ProcessDefault.GetInstance<T>(typeName, refAssemblyType);
    }
}
