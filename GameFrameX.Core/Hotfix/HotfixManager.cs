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
using GameFrameX.Network.Abstractions;
using GameFrameX.Network.HTTP;
using GameFrameX.Utility.Setting;

namespace GameFrameX.Core.Hotfix;

/// <summary>
/// 热更新管理器——static 转发门面。
/// Hotfix manager — a static forwarding facade.
/// </summary>
/// <remarks>
/// 职责边界：装载入口与策略（同路径幂等守卫、10min 宽限常量、目录兜底）留在此处；
/// ALC 基建与退役编排机制由 <c>GameFrameX.Utility.Hotload.CollectibleAssemblyHost&lt;TModule&gt;</c> 提供；
/// 全部解析路由经 <see cref="Resolver"/>（<see cref="IHotfixResolver"/> 契约实例）承载，Core 子系统与测试替身均依赖该契约。
/// Responsibility split: load entries and policies (the same-path idempotency guard, the 10-minute
/// grace constant, directory ensuring) stay here; ALC infrastructure and retirement orchestration live in
/// <c>GameFrameX.Utility.Hotload.CollectibleAssemblyHost&lt;TModule&gt;</c>; all resolution routing goes through
/// <see cref="Resolver"/> (an <see cref="IHotfixResolver"/> contract instance) consumed by Core subsystems and test doubles.
/// <para>
/// 同路径幂等守卫：单进程多 Role（--AllInOne）形态下首个 Role 装载热更模块后，其余 Role 经装载入口的重复调用
/// 在此短路返回，避免重复的可收集 ALC 装载制造跨 ALC 的类型身份割裂（旧模块 agent 的激活/转型随竞态随机失败）；
/// 路径由 <see cref="HotfixLoadOptions.ResolveAssemblyFilePath"/> 统一拼装（进程内 CWD 不变），字符串等值即路径等值。
/// Same-path idempotency guard: in the single-process multi-role (--AllInOne) shape, once the first role has loaded
/// the hotfix module, repeated load calls from the remaining roles short-circuit here — avoiding duplicate
/// collectible-ALC loads that would create cross-ALC type identity splits (agent activation/casts on old modules
/// would fail racily); paths are built uniformly by <see cref="HotfixLoadOptions.ResolveAssemblyFilePath"/> (the
/// CWD never changes in-process), so string equality is path equality.
/// </para>
/// </remarks>
public static class HotfixManager
{
    /// <summary>
    /// 热更宿主实例：当前模块指针与退役链的持有者（生产唯一装配）。
    /// The hotfix host instance: owner of the current-module pointer and the retirement chain (the single production wiring).
    /// </summary>
    private static readonly HotfixHost Host = new HotfixHost();

    /// <summary>
    /// 解析契约当前实现：默认为生产 <see cref="Host"/>；测试经 <see cref="SetResolverForTesting"/> 注入替身（传 <c>null</c> 复原）。
    /// The current resolution-contract implementation: the production <see cref="Host"/> by default; tests inject a
    /// substitute via <see cref="SetResolverForTesting"/> (pass <c>null</c> to restore).
    /// </summary>
    private static IHotfixResolver _resolver = Host;

    /// <summary>
    /// 基础配置信息
    /// Base application setting
    /// </summary>
    private static AppSetting _baseSetting;

    /// <summary>
    /// 热更解析契约注入缝：Core 子系统经此依赖抽象而非 static 实现；亦为测试替身入口。
    /// The hotfix resolution-contract seam: Core subsystems depend on the abstraction instead of the static
    /// implementation; also the entry point for test doubles.
    /// </summary>
    internal static IHotfixResolver Resolver
    {
        get { return _resolver; }
    }

    /// <summary>
    /// 注入/复原解析契约实现（测试专用）：传 <c>null</c> 复原为生产宿主。
    /// Injects/restores the resolution-contract implementation (testing only): passing <c>null</c> restores the production host.
    /// </summary>
    /// <param name="resolver">替身实现；<c>null</c> 复原 / The substitute; <c>null</c> restores.</param>
    internal static void SetResolverForTesting(IHotfixResolver resolver)
    {
        _resolver = resolver ?? Host;
    }

    /// <summary>
    /// 热更新程序集
    /// The hotfix assembly
    /// </summary>
    public static Assembly HotfixAssembly
    {
        get { return Resolver.HotfixAssembly; }
    }

    /// <summary>
    /// 最近一次热更新重载的时间
    /// The time of the most recent hot reload
    /// </summary>
    public static DateTime ReloadTime
    {
        get { return Host.ReloadTime; }
    }

    /// <summary>
    /// 加载热更新模块（同步入口）。
    /// Loads the hotfix module (synchronous entry).
    /// </summary>
    /// <param name="setting">应用程序配置 / The application setting.</param>
    /// <param name="options">装载参数（<c>null</c> 走默认值：目录 hotfix + GameFrameX.Hotfix.dll） / Load options (<c>null</c> for defaults: the hotfix directory plus GameFrameX.Hotfix.dll).</param>
    /// <returns>返回是否加载成功 / Whether the load succeeded.</returns>
    public static bool LoadHotfix(AppSetting setting, HotfixLoadOptions options = null)
    {
        var loadOptions = options ?? new HotfixLoadOptions();
        var path = loadOptions.ResolveAssemblyFilePath();
        if (setting != null)
        {
            _baseSetting = setting;
        }

        // 同路径幂等判重：当前模块即从该路径装载时直接返回成功，避免重复的可收集 ALC 装载
        //（单进程多 Role 形态下，后续 Role 对同一热更 DLL 的重复装载会制造跨 ALC 的类型身份割裂，
        // 旧模块 agent 的激活/转型随竞态随机失败；进程内同一 DLL 只装载一次）
        if (Host.IsSamePathLoaded(path))
        {
            return true;
        }

        // 装载前幂等确保目录存在（历史由调用方各自兜底创建，收敛为装载入口自身的前置条件）
        EnsureHotfixDirectory(path);
        var hotfixModule = new HotfixModule(path);
        var reload = Host.CurrentModule != null;
        // 起服时失败会有异常抛出
        var success = hotfixModule.Init(reload);
        if (!success)
        {
            return false;
        }

        if (reload)
        {
            // 补退役：与 LoadHotfixModuleAsync 路径对齐——旧模块进入退役链（宽限排空 + 卸载 GC 验证由模块链承担），
            // 否则旧可收集 ALC 永不卸载，每次同步重载泄漏一份完整热更程序集内存
            // Retire the old module to align with the LoadHotfixModuleAsync path — it joins the retirement chain
            // (grace drain plus unload GC verification belong to the module chain); without this the old collectible
            // ALC is never unloaded, leaking a full hotfix assembly per synchronous reload
            Host.Retire(HotfixHost.RetirementGracePeriod);
        }

        // 同步路径保持历史行为：仅切换当前模块指针与注册，不回调桥接
        // Synchronous path keeps historical behavior: swap and register the current module only — no bridge callback
        Host.RegisterCurrent(hotfixModule);
        return true;
    }

    /// <summary>
    /// 加载热更新模块（重载入口：旧模块退役编排 + 桥接回调）。
    /// Loads the hotfix module (reload entry: old-module retirement orchestration plus bridge callback).
    /// </summary>
    /// <param name="setting">应用程序配置 / The application setting.</param>
    /// <param name="options">装载参数（<c>null</c> 走默认值） / Load options (<c>null</c> for defaults).</param>
    /// <returns>返回是否加载成功 / Whether the load succeeded.</returns>
    public static async Task<bool> LoadHotfixModuleAsync(AppSetting setting, HotfixLoadOptions options = null)
    {
        var loadOptions = options ?? new HotfixLoadOptions();
        var path = loadOptions.ResolveAssemblyFilePath();
        if (setting != null)
        {
            _baseSetting = setting;
        }

        // 同路径幂等判重：当前模块即从该路径装载时直接返回成功，避免重复的可收集 ALC 装载
        //（热更重载总是指向新 version 子目录；同路径「重载」没有可观测收益，只会制造跨 ALC 类型身份割裂）
        if (Host.IsSamePathLoaded(path))
        {
            return true;
        }

        // 装载前幂等确保目录存在（历史由调用方各自兜底创建，收敛为装载入口自身的前置条件）
        EnsureHotfixDirectory(path);
        var hotfixModule = new HotfixModule(path);
        var reload = Host.CurrentModule != null;
        // 起服时失败会有异常抛出
        var success = hotfixModule.Init(reload);
        if (!success)
        {
            return false;
        }

        return await Host.LoadAsync(hotfixModule, _baseSetting, reload);
    }

    /// <summary>
    /// 停止热更新模块
    /// Stops the hotfix module
    /// </summary>
    /// <param name="message">停止原因 / The stop reason.</param>
    /// <returns>停止任务 / The stop task.</returns>
    public static Task StopAsync(string message = "")
    {
        return Host.StopAsync(message);
    }

    /// <summary>
    /// 获取组件的代理实例
    /// Gets the agent instance for a component
    /// </summary>
    /// <param name="component">组件实例 / The component instance.</param>
    /// <param name="refAssemblyType">引用程序集类型 / A type from the caller's hotfix assembly.</param>
    /// <typeparam name="T">代理类型 / The agent type.</typeparam>
    /// <returns>返回代理实例 / The agent instance.</returns>
    public static T GetAgent<T>(BaseComponent component, Type refAssemblyType) where T : class, IComponentAgent
    {
        return Resolver.GetAgent<T>(component, refAssemblyType);
    }

    /// <summary>
    /// 获取RPC处理器实例。
    /// Gets the RPC handler instance.
    /// </summary>
    /// <param name="type">请求消息的类型。/ The request message type.</param>
    /// <returns>对应的 <see cref="IMessageHandler"/> 实例，如果未找到则返回 null。/ The matching <see cref="IMessageHandler"/>, or null when absent.</returns>
    public static IMessageHandler GetRpcHandler(Type type)
    {
        return Resolver.GetRpcHandler(type);
    }

    /// <summary>
    /// 获取消息处理器
    /// Gets the message handler
    /// </summary>
    /// <param name="msgId">消息ID / The message identifier.</param>
    /// <returns>返回对应的消息处理器 / The matching handler.</returns>
    public static IMessageHandler GetMessageHandler(int msgId)
    {
        return Resolver.GetMessageHandler(msgId);
    }

    /// <summary>
    /// 获取HTTP消息处理器
    /// Gets the HTTP handler
    /// </summary>
    /// <param name="cmd">HTTP命令 / The HTTP command.</param>
    /// <returns>返回对应HTTP处理器 / The matching HTTP handler.</returns>
    public static BaseHttpHandler GetHttpHandler(string cmd)
    {
        return Resolver.GetHttpHandler(cmd);
    }

    /// <summary>
    /// 获取所有HTTP消息处理器列表
    /// Gets the list of all HTTP handlers
    /// </summary>
    /// <returns>返回HTTP处理器列表 / The HTTP handler list.</returns>
    public static List<BaseHttpHandler> GetListHttpHandler()
    {
        return Resolver.GetListHttpHandler();
    }

    /// <summary>
    /// 获取指定Actor类型和事件参数类型的事件监听器列表
    /// Gets event listeners for the given actor type and event-args type
    /// </summary>
    /// <param name="actorType">Actor类型 / The actor type.</param>
    /// <param name="eventArgsType">事件参数类型（事件绑定键） / The event-args type (the event binding key).</param>
    /// <returns>返回监听器列表，如果没有则空列表 / The listener list; empty when none.</returns>
    public static List<IEventListener> FindListeners(ushort actorType, Type eventArgsType)
    {
        return Resolver.FindListeners(actorType, eventArgsType);
    }

    /// <summary>
    /// 获取指定事件参数类型的事件监听器列表
    /// Gets event listeners for the given event-args type
    /// </summary>
    /// <param name="eventArgsType">事件参数类型（事件绑定键） / The event-args type (the event binding key).</param>
    /// <returns>返回监听器列表，如果没有则空列表 / The listener list; empty when none.</returns>
    public static List<IEventListener> FindListeners(Type eventArgsType)
    {
        return Resolver.FindListeners(eventArgsType);
    }

    /// <summary>
    /// 获取指定类型的实例
    /// 主要用于获取Event,Timer, Schedule的Handler实例
    /// Gets an instance of the given type, mainly Event/Timer/Schedule handler instances
    /// </summary>
    /// <typeparam name="T">实例类型 / The instance type.</typeparam>
    /// <param name="typeName">类型名称 / The type name.</param>
    /// <param name="refAssemblyType">引用程序集类型 / A type from the caller's hotfix assembly.</param>
    /// <returns>返回指定类型的实例，如果类型名称为空则返回默认值 / The instance, or default for an empty type name.</returns>
    public static T GetInstance<T>(string typeName, Type refAssemblyType = null)
    {
        return Resolver.GetInstance<T>(typeName, refAssemblyType);
    }

    /// <summary>
    /// 确保热更新模块所在目录存在（装载前幂等建目录）。
    /// Ensures the directory holding the hotfix assembly exists (idempotent creation before loading).
    /// </summary>
    /// <remarks>
    /// 历史上由调用方各自兜底创建热更目录；该职责属装载入口自身的前置条件，收敛于此。
    /// <see cref="Directory.CreateDirectory(string)"/> 对已存在目录为 no-op，且天然覆盖按
    /// version 子目录装载（<paramref name="assemblyFilePath"/> 的任意父目录链）的场景。
    /// Directory creation used to be each caller's concern; it belongs to the load entry itself.
    /// <see cref="Directory.CreateDirectory(string)"/> is a no-op for existing directories and covers
    /// version-subdirectory loads (any parent chain of <paramref name="assemblyFilePath"/>).
    /// </remarks>
    /// <param name="assemblyFilePath">热更程序集文件完整路径 / The full path of the hotfix assembly file.</param>
    private static void EnsureHotfixDirectory(string assemblyFilePath)
    {
        // 根路径/裸文件名时 GetDirectoryName 返回 null 或空串，无从建目录，直接跳过
        var directory = Path.GetDirectoryName(assemblyFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
