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
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
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
//  ==========================================================================================

using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Foundation.Localization.Core;

namespace GameFrameX.Capability.Hosting.Core;

/// <summary>
/// 能力宿主（装配完成的运行时承载：授权作用域 + 服务注册表 + 调度壳 + 事件总线 + 能力报告 + 三段生命周期编排）。
/// <para>
/// 维护约束（启停序红线）：<see cref="StartAsync"/> 与 <see cref="StopAsync"/> 经同一把生命周期闸串行执行——
/// 启动进行中到达的停止会等待启动结束（成功或回滚完毕）后再停，不会与激活交错。
/// <see cref="StartAsync"/> 按拓扑序逐模块 <c>ActivateAsync</c>，全部激活成功后启动调度器；任一模块激活失败或
/// 调度器启动失败，即先对失败模块本身执行一次 <c>DeactivateAsync</c>（激活失败场景），再对已激活模块逆序
/// <c>DeactivateAsync</c>，撤销全部桥并释放钩子声明后重抛原异常（回滚中的停用失败只记日志、不覆盖原异常）。
/// <see cref="StopAsync"/> 先停调度器，再按拓扑逆序逐模块 <c>DeactivateAsync</c>（失败记日志不阻断其余模块停止），
/// 最后机制自动撤销全部桥（撤销不再由模块或组合根手写）。本类不持有任何能力域状态；admin 面归 Hosting.AdminApi。
/// </para>
/// </summary>
public sealed class CapabilityHost
{
    /// <summary>
    /// 宿主共享服务注册表（族基建 + 宿主覆盖 + 能力模块注册的产物；装配后对组合根与管理面开放取用）。
    /// </summary>
    public CapabilityServices Services { get; }

    /// <summary>
    /// 启用能力模块实例表（依赖拓扑序；admin 组装的唯一模块来源——模块自装配后携带域状态，
    /// 必须与 Register 共用同一实例，禁止二次实例化）。
    /// </summary>
    public IReadOnlyList<ICapabilityModule> Modules { get; }

    /// <summary>
    /// 装配选项。
    /// </summary>
    public CapabilityHostOptions Options { get; }

    /// <summary>
    /// 启动能力报告。
    /// </summary>
    public CapabilityReport Report { get; }

    /// <summary>
    /// 后台调度壳。
    /// </summary>
    public CapabilityScheduler Scheduler { get; }

    /// <summary>
    /// 机制事件总线（Activate 开始后接受发布）。
    /// </summary>
    public CapabilityEventBus EventBus { get; }

    /// <summary>
    /// 装配日志出口（激活回滚与停止失败记录用）。
    /// </summary>
    private readonly ICapabilityAssemblyLog _assemblyLog;

    /// <summary>
    /// 全部模块的桥撤销凭据（激活期收集；停止期逆序撤销——消费方旧租约随即显式 8002）。
    /// </summary>
    private readonly List<IDisposable> _bridgePublications = new();

    /// <summary>
    /// 本宿主模块 Register 期声明的钩子默认实现释放凭据（进程级静态槽位；停止 / 激活失败 / 组合失败时统一释放，
    /// 保证同进程再次组合可重新声明）。
    /// </summary>
    private readonly List<IDisposable> _hookDeclarationReleases;

    /// <summary>
    /// 宿主状态（0 = 已装配未启动，1 = 运行中，2 = 已停止）；只在 <see cref="_lifecycleGate"/> 内读写。
    /// </summary>
    private int _state;

    /// <summary>
    /// 生命周期闸（串行化启动与停止：两者都是跨 await 的多步序列，交错会让停止撤桥后启动继续发布新桥）。
    /// </summary>
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);

    /// <summary>
    /// 获取授权租户标识。
    /// </summary>
    public long TenantId
    {
        get { return Options.TenantId; }
    }

    /// <summary>
    /// 获取授权应用标识。
    /// </summary>
    public long AppId
    {
        get { return Options.AppId; }
    }

    /// <summary>
    /// 获取授权区服标识。
    /// </summary>
    public long ServerId
    {
        get { return Options.ServerId; }
    }

    /// <summary>
    /// 初始化 <see cref="CapabilityHost"/>（完整形态；仅机制组合器构造）。
    /// </summary>
    /// <param name="options">装配选项。</param>
    /// <param name="report">能力报告。</param>
    /// <param name="scheduler">调度壳（承载本宿主的全部注册 job）。</param>
    /// <param name="services">宿主共享服务注册表。</param>
    /// <param name="modules">启用能力模块实例表（拓扑序）。</param>
    /// <param name="eventBus">机制事件总线。</param>
    /// <param name="assemblyLog">装配日志出口。</param>
    /// <param name="hookDeclarationReleases">钩子声明释放凭据（所有权移交本宿主）。</param>
    internal CapabilityHost(CapabilityHostOptions options, CapabilityReport report, CapabilityScheduler scheduler, CapabilityServices services, IReadOnlyList<ICapabilityModule> modules, CapabilityEventBus eventBus, ICapabilityAssemblyLog assemblyLog, List<IDisposable> hookDeclarationReleases)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        if (!options.IsValid())
        {
            // Localization: Capability.Hosting.OptionsInvalid - 能力宿主选项非法（作用域三元组 / 调度间隔）。
            throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.Capability.Hosting.OptionsInvalid));
        }

        Report = report ?? throw new ArgumentNullException(nameof(report));
        Scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        Services = services ?? throw new ArgumentNullException(nameof(services));
        Modules = modules ?? throw new ArgumentNullException(nameof(modules));
        EventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _assemblyLog = assemblyLog ?? throw new ArgumentNullException(nameof(assemblyLog));
        _hookDeclarationReleases = hookDeclarationReleases ?? throw new ArgumentNullException(nameof(hookDeclarationReleases));
    }

    /// <summary>
    /// 启动宿主（拓扑序逐模块 ActivateAsync → 启动调度器；任一步失败即回滚后重抛；可重复调用幂等）。
    /// 与 <see cref="StopAsync"/> 串行执行。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            await StartCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <summary>
    /// 停止宿主（停调度器 → 逆序逐模块 DeactivateAsync（失败记日志不阻断）→ 机制自动撤销全部桥）。
    /// 启动进行中调用时等待启动结束后再停。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            await StopCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    /// <summary>
    /// 启动主体（调用方已持有生命周期闸）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    private async Task StartCoreAsync(CancellationToken cancellationToken)
    {
        if (_state != 0)
        {
            return;
        }

        _state = 1;
        EventBus.MarkActivationBegun();
        Services.MarkActivationStarted();

        var activatedModules = new List<ICapabilityModule>();
        for (var i = 0; i < Modules.Count; i++)
        {
            var module = Modules[i];
            var modulePublications = new List<IDisposable>();
            var resolver = new CapabilityServiceResolver(Services, module.Manifest.Id);
            var publisher = new CapabilityBridgePublisher(module.Manifest.Id, modulePublications);
            var context = new CapabilityActivationContext(resolver, publisher);
            try
            {
                await module.ActivateAsync(context, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Localization: Capability.Hosting.ActivationFailed - 激活失败逆序回滚。
                _assemblyLog.Error(string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.ActivationFailed), module.Manifest.Id, activatedModules.Count));
                // 失败模块在抛出前可能已取得资源（桥以外的订阅 / 句柄）：机制保证「Activate 抛异常 ⇒ 该模块必被 Deactivate」。
                await DeactivateModuleAsync(module, CancellationToken.None).ConfigureAwait(false);
                await RollBackActivationAsync(activatedModules, modulePublications).ConfigureAwait(false);
                throw;
            }

            activatedModules.Add(module);
            _bridgePublications.AddRange(modulePublications);
        }

        try
        {
            Scheduler.Start();
        }
        catch (Exception)
        {
            // Localization: Capability.Hosting.SchedulerStartFailed - 调度器启动失败逆序回滚。
            _assemblyLog.Error(string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.SchedulerStartFailed), activatedModules.Count));
            await RollBackActivationAsync(activatedModules, null).ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// 停止主体（调用方已持有生命周期闸）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    private async Task StopCoreAsync(CancellationToken cancellationToken)
    {
        if (_state == 0)
        {
            // 已装配从未启动：无激活资源需回收，但 Register 期钩子声明仍占用进程级槽位，必须释放。
            _state = 2;
            ReleaseHookDeclarations();
            return;
        }

        if (_state != 1)
        {
            return;
        }

        _state = 2;
        await Scheduler.StopAsync().ConfigureAwait(false);
        await DeactivateModulesInReverseAsync(Modules, cancellationToken).ConfigureAwait(false);
        ReleaseInReverse(_bridgePublications);
        ReleaseHookDeclarations();
    }

    /// <summary>
    /// 启动失败回滚（逆序 Deactivate 已激活模块 → 撤销失败模块未入账的桥与全部已入账桥 → 释放钩子声明 → 置已停止）。
    /// </summary>
    /// <param name="activatedModules">已激活成功的模块（正序）。</param>
    /// <param name="pendingPublications">失败模块已发布但未入账的桥凭据（调度器启动失败时为 null）。</param>
    /// <returns>异步任务。</returns>
    private async Task RollBackActivationAsync(List<ICapabilityModule> activatedModules, List<IDisposable> pendingPublications)
    {
        await DeactivateModulesInReverseAsync(activatedModules, CancellationToken.None).ConfigureAwait(false);
        if (pendingPublications != null)
        {
            ReleaseInReverse(pendingPublications);
        }

        ReleaseInReverse(_bridgePublications);
        ReleaseHookDeclarations();
        _state = 2;
    }

    /// <summary>
    /// 释放本宿主声明的全部钩子默认实现（幂等；组合器在业务面未启用 / 组合后续步骤失败时直接调用）。
    /// </summary>
    internal void ReleaseHookDeclarations()
    {
        ReleaseInReverse(_hookDeclarationReleases);
    }

    /// <summary>
    /// 逆序释放凭据并清空收集器（桥撤销与钩子声明释放共用；重复调用为空操作）。
    /// </summary>
    /// <param name="releases">释放凭据收集器。</param>
    internal static void ReleaseInReverse(List<IDisposable> releases)
    {
        for (var i = releases.Count - 1; i >= 0; i--)
        {
            releases[i].Dispose();
        }

        releases.Clear();
    }

    /// <summary>
    /// 逆序执行模块 DeactivateAsync（单个失败记 Error 日志，不阻断其余模块停止）。
    /// </summary>
    /// <param name="modules">模块集合（正序传入，内部逆序执行）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    private async Task DeactivateModulesInReverseAsync(IReadOnlyList<ICapabilityModule> modules, CancellationToken cancellationToken)
    {
        for (var i = modules.Count - 1; i >= 0; i--)
        {
            await DeactivateModuleAsync(modules[i], cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 执行单个模块 DeactivateAsync（失败记 Error 日志、不外抛：停止链与激活回滚都不得被单模块停用失败打断或覆盖原异常）。
    /// </summary>
    /// <param name="module">模块。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    private async Task DeactivateModuleAsync(ICapabilityModule module, CancellationToken cancellationToken)
    {
        try
        {
            await module.DeactivateAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // Localization: Capability.Hosting.DeactivationFailed - 停止失败不阻断其余模块。
            _assemblyLog.Error(string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.DeactivationFailed), module.Manifest.Id, exception.Message));
        }
    }
}
