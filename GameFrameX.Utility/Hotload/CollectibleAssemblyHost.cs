// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
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

using System.Collections.Concurrent;
using System.Reflection;
using GameFrameX.Foundation.Logger;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Utility.Setting;

namespace GameFrameX.Utility.Hotload;

/// <summary>
/// 可收集模块宿主：当前模块指针与退役链编排（宽限排空、卸载、GC 释放验证、按程序集身份路由）。
/// Collectible module host: current-module pointer plus retirement-chain orchestration (grace drain,
/// unload, GC release verification, assembly-identity routing).
/// </summary>
/// <typeparam name="TModule">模块类型 / The module type.</typeparam>
/// <remarks>
/// 机制归本类，策略归调用方：宽限期由 <see cref="Retire"/> 调用方注入，本类不携带默认值
/// （10 分钟宽限这类业务策略常量留在 Core 装载侧）。
/// Mechanisms live here while policy stays with callers: grace periods are injected by the caller of
/// <see cref="Retire"/> — this host carries no default (business-policy constants such as the 10-minute
/// grace stay on the Core loading side).
/// <para>
/// <b><see cref="InTransition"/> 时序不变量</b>：退役链非空（存在宽限未到或未完成卸载的模块）即为
/// <c>true</c>；仅当退役链<b>全部排空</b>（最后一个模块完成卸载）才回落 <c>false</c>。过渡期间常驻缓存
/// （如组件代理缓存）必须视为失效并重新解析，防止拿到已退役模块的类型实例。
/// <b><see cref="InTransition"/> timing invariant</b>: <c>true</c> while the retirement chain is non-empty
/// (any module still within grace or unloading); it falls back to <c>false</c> only after the chain has
/// <b>fully drained</b> (the last module finished unloading). Resident caches (e.g. component agent caches)
/// must treat entries as invalid during the transition and re-resolve, never holding instances from retired modules.
/// </para>
/// <para>
/// <b><see cref="TryGetOwner"/> 路由语义</b>：宽限期内 in-flight 请求按程序集身份仍解析到原属（已退役）模块，
/// 保证旧代码在排空窗口内行为不变；未命中或无退役模块时由调用方回落当前模块。
/// <b><see cref="TryGetOwner"/> routing semantics</b>: during grace, in-flight requests still resolve to their
/// original (retired) module by assembly identity so old code keeps behaving within the drain window; on miss
/// (or with no retired modules) the caller falls back to the current module.
/// </para>
/// <para>
/// 卸载验证仅 Debug 配置执行：可收集 ALC 卸载后需强制 GC 并检查弱引用存活方可判定真正释放
/// （.NET 官方推荐模式，故有意保留 <see cref="GC.Collect()"/>）。
/// Unload verification runs only under Debug configuration: a collectible ALC must be force-collected and its
/// weak reference polled to confirm real release (the .NET-recommended pattern — <see cref="GC.Collect()"/>
/// is intentional here).
/// </para>
/// </remarks>
public sealed class CollectibleAssemblyHost<TModule>
    where TModule : class, ICollectibleAssemblyModule
{
    /// <summary>
    /// 退役模块表（键为每次 Retire 发出的唯一票据；宽限到期即移除）。
    /// Retired-module table (keyed by a unique ticket issued per Retire; entries leave when grace expires).
    /// </summary>
    private readonly ConcurrentDictionary<long, TModule> _retiredModules = new();

    /// <summary>
    /// 退役票据发号器：每次 Retire 递增，保证退役表键唯一（替代 GetHashCode，杜绝哈希碰撞导致模块永不卸载）。
    /// Retirement ticket source: incremented per Retire so retirement keys are unique (replaces GetHashCode,
    /// eliminating hash-collision modules that would never unload).
    /// </summary>
    private long _retireTicketSource;

    /// <summary>
    /// 串行化「入链 + 置过渡标志」与「出链 + 清过渡标志」的锁：保证 InTransition 严格等于退役链非空，
    /// 消除 Retire 与清理任务之间的 TOCTOU 竞态。锁只覆盖字典与标志操作，宽限等待与卸载验证在锁外。
    /// Serializes "join chain + set flag" against "leave chain + clear flag": InTransition stays strictly equal
    /// to "chain non-empty", removing the Retire-vs-cleanup TOCTOU race. The lock covers dictionary and flag
    /// operations only; grace waits and unload verification stay outside.
    /// </summary>
    private readonly object _transitionLock = new();

    /// <summary>
    /// 热更过渡标志：语义见类备注的时序不变量。
    /// Transition flag: semantics per the timing invariant in class remarks.
    /// </summary>
    private volatile bool _inTransition;

    /// <summary>
    /// 当前模块指针。
    /// The current-module pointer.
    /// </summary>
    private volatile TModule _currentModule;

    /// <summary>
    /// 获取当前模块（从未装载过时为 <c>null</c>）。
    /// Gets the current module (<c>null</c> when nothing has ever been loaded).
    /// </summary>
    public TModule CurrentModule
    {
        get { return _currentModule; }
    }

    /// <summary>
    /// 获取是否处于热更过渡期（退役链未排空；期间常驻缓存必须视为失效）。
    /// Gets whether a hot transition is in progress (retirement chain not drained; resident caches must be treated as invalid meanwhile).
    /// </summary>
    public bool InTransition
    {
        get { return _inTransition; }
    }

    /// <summary>
    /// 注册当前模块（新模块装载成功后调用，指针即时切换；不做任何退役动作）。
    /// Registers the current module (called once a new module has loaded; the pointer swaps immediately, no retirement happens here).
    /// </summary>
    /// <param name="module">新装载的模块 / The newly loaded module.</param>
    public void Register(TModule module)
    {
        ArgumentNullException.ThrowIfNull(module);
        _currentModule = module;
    }

    /// <summary>
    /// 退役一个模块：同步进入过渡态并入退役链；宽限期到后卸载该模块并做 GC 释放验证，
    /// 退役链全部排空后清除过渡态（时序不变量见类备注）。
    /// Retires a module: synchronously enters transition and joins the retirement chain; after the grace
    /// period the module is unloaded with GC release verification, and the transition flag clears only once
    /// the whole chain has drained (see the timing invariant in class remarks).
    /// </summary>
    /// <param name="module">待退役模块 / The module to retire.</param>
    /// <param name="gracePeriod">排空宽限期（策略常量由调用方持有） / The drain grace period (policy constant owned by the caller).</param>
    public void Retire(TModule module, TimeSpan gracePeriod)
    {
        ArgumentNullException.ThrowIfNull(module);
        long moduleKey;
        lock (_transitionLock)
        {
            moduleKey = Interlocked.Increment(ref _retireTicketSource);
            _retiredModules[moduleKey] = module;
            _inTransition = true;
        }

        // 延迟清理退役模块：宽限参数化，常量归调用方；清理异常必须可见（Error 日志），不再 fire-and-forget 吞掉。
        // 红线：异步体只捕获 moduleKey —— 若捕获 module，闭包会经模块字段（HotfixAssembly / 加载上下文）
        // 保持对 ALC 的强可达，卸载验证期间弱引用恒存活，验证必然误报 failure。
        // Deferred cleanup of the retired module: grace is parameterized, the constant belongs to the caller;
        // cleanup failures must be visible (Error log) instead of being lost fire-and-forget.
        // Red line: the async body captures moduleKey ONLY — capturing module would keep the ALC strongly
        // reachable through module fields (HotfixAssembly / load context) for the whole verification, making the
        // weak reference permanently alive and the verdict a guaranteed false "failure".
        _ = RetireAsync(moduleKey, gracePeriod);
    }

    /// <summary>
    /// 退役链清理：宽限期到后在过渡锁内「出链 + 重算过渡标志」，再卸载并按需做 GC 释放验证；
    /// 任何异常记 Error 日志，不再无人观察。
    /// Retirement-chain cleanup: after the grace period, leaves the chain and recomputes the transition flag
    /// inside the transition lock, then unloads and optionally verifies GC release; all exceptions are logged
    /// as Error instead of going unobserved.
    /// </summary>
    /// <param name="moduleKey">退役票据键 / The retirement ticket key.</param>
    /// <param name="gracePeriod">排空宽限期 / The drain grace period.</param>
    private async Task RetireAsync(long moduleKey, TimeSpan gracePeriod)
    {
        try
        {
            await Task.Delay(gracePeriod);
            var weakReference = RemoveAndUnload(moduleKey);
            if (weakReference != null && GlobalSettings.CurrentSetting != null && GlobalSettings.CurrentSetting.IsDebug)
            {
                await VerifyUnloadAsync(weakReference);
            }
        }
        catch (Exception e)
        {
            // Localization: Utility.Hotload.RetireModuleFailed - CollectibleAssemblyHost 退役模块清理失败：{0}
            LogHelper.Error(LocalizationService.GetString(Localization.Keys.Utility.Hotload.RetireModuleFailed, e.ToString()));
        }
    }

    /// <summary>
    /// 取出并卸载退役模块（独立同步方法：过渡锁内「出链 + 清过渡标志」保证 InTransition 恒等于退役链非空；
    /// 模块引用随返回消亡，验证期无人可达模块，ALC 方可被回收——见 <see cref="Retire"/> 红线）。
    /// Removes and unloads a retired module (a separate synchronous helper: leaving the chain and clearing the
    /// transition flag happen under the transition lock so InTransition always equals "chain non-empty";
    /// the module reference dies on return so nothing can reach the module during verification and the ALC
    /// becomes collectible — see the red line in <see cref="Retire"/>).
    /// </summary>
    /// <param name="moduleKey">退役票据键 / The retirement ticket key.</param>
    /// <returns>模块卸载返回的弱引用；键不存在（已清理）为 <c>null</c> / The weak reference from the module's unload; <c>null</c> when the key is gone.</returns>
    private WeakReference RemoveAndUnload(long moduleKey)
    {
        lock (_transitionLock)
        {
            if (_retiredModules.TryRemove(moduleKey, out var module))
            {
                var weakReference = module.Unload();
                _inTransition = !_retiredModules.IsEmpty;
                return weakReference;
            }

            _inTransition = !_retiredModules.IsEmpty;
            return null;
        }
    }

    /// <summary>
    /// 按程序集身份定位退役模块（路由语义见类备注）；<paramref name="assembly"/> 为 <c>null</c> 或未命中返回 <c>false</c>。
    /// Locates a retired module by assembly identity (routing semantics in class remarks); returns <c>false</c> for a
    /// <c>null</c> <paramref name="assembly"/> or on miss.
    /// </summary>
    /// <param name="assembly">待路由的程序集身份 / The assembly identity to route.</param>
    /// <param name="module">命中的退役模块 / The matched retired module.</param>
    /// <returns>命中退役模块返回 <c>true</c> / <c>true</c> when a retired module matches.</returns>
    public bool TryGetOwner(Assembly assembly, out TModule module)
    {
        module = null;
        if (assembly == null || _retiredModules.IsEmpty)
        {
            return false;
        }

        foreach (var kv in _retiredModules)
        {
            var retired = kv.Value;
            if (assembly == retired.LoadedAssembly)
            {
                module = retired;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Debug 卸载验证：强制 GC 后轮询弱引用存活（可回收 ALC 判定真正释放的官方推荐模式）。
    /// Debug unload verification: polls weak-reference liveness after forced collections (the officially
    /// recommended pattern to tell a collectible ALC is truly released).
    /// </summary>
    /// <param name="weakReference">指向已卸载上下文的弱引用 / The weak reference to the unloaded context.</param>
    private static async Task VerifyUnloadAsync(WeakReference weakReference)
    {
        var tryCount = 0;
        while (weakReference.IsAlive && tryCount++ < 10)
        {
            await Task.Delay(100);
            // 可回收 AssemblyLoadContext 卸载后需强制 GC 以回收可卸载程序集并判定其是否真正释放，是 .NET 官方推荐模式，故此处有意保留 GC.Collect
            // Collectible AssemblyLoadContexts need forced collections after Unload to reclaim and detect real release — the .NET-recommended pattern; GC.Collect is intentional here
#pragma warning disable S1215 // 垃圾回收器不应被显式调用 / Garbage collector should not be called explicitly
            GC.Collect();
            GC.WaitForPendingFinalizers();
#pragma warning restore S1215 // 垃圾回收器不应被显式调用 / Garbage collector should not be called explicitly
        }

        // Localization: Core.Hotfix.DllUninstall - 热更DLL卸载{0}
        LogHelper.Warning(LocalizationService.GetString(Localization.Keys.Core.HotfixModule.DllUninstall, weakReference.IsAlive ? "failure" : "successful"));
    }
}
