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
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using GameFrameX.Foundation.Localization.Core;

namespace GameFrameX.Capability.Hosting;

/// <summary>
/// 能力宿主（装配完成的运行时承载：授权作用域 + 调度壳 + 能力报告 + 事件爆发口）。
/// <para>
/// 维护约束：本类是机制承接位（原 OnlineRuntimeHost 的机制半边），不持有任何能力域状态；
/// admin 面与管理端点归 Capability.Hosting.AdminApi，由组合根另行装配。
/// </para>
/// </summary>
public sealed class CapabilityHost
{

    /// <summary>
    /// 宿主共享服务注册表（组合根预置 + 能力模块注册的产物；「壳取用叶子包装配产物」的服务共享面，C231）。
    /// </summary>
    /// <remarks>
    /// The host-wide shared service registry (composition-root pre-seeds plus capability-module products;
    /// the service-sharing face for the shell consuming leaf-package assembly products, C231).
    /// </remarks>
    public CapabilityServices Services { get; }

    /// <summary>
    /// 启用能力模块实例表（Requires 拓扑序；admin 组装的唯一模块来源——模块自装配后携带域状态，
    /// 必须与 Register 共用同一实例，禁止二次实例化）。
    /// </summary>
    /// <remarks>
    /// The enabled capability module instances in Requires topological order; the single module source for
    /// admin assembly — modules carry domain state after self-assembly and must reuse the same instances that
    /// registered, never a second instantiation.
    /// </remarks>
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
    /// 启动标记（重复启动保护）。
    /// </summary>
    private bool _started;


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
    /// 初始化 <see cref="CapabilityHost"/>（共享服务注册表与模块实例缺省自建形态）。
    /// </summary>
    /// <param name="options">装配选项。</param>
    /// <param name="report">能力报告。</param>
    /// <param name="scheduler">调度壳（承载本宿主的全部注册 job）。</param>
    public CapabilityHost(CapabilityHostOptions options, CapabilityReport report, CapabilityScheduler scheduler)
        : this(options, report, scheduler, new CapabilityServices(), Array.Empty<ICapabilityModule>())
    {
    }

    /// <summary>
    /// 初始化 <see cref="CapabilityHost"/>（完整形态：装配器注入共享服务注册表与启用模块实例表）。
    /// </summary>
    /// <param name="options">装配选项。</param>
    /// <param name="report">能力报告。</param>
    /// <param name="scheduler">调度壳（承载本宿主的全部注册 job）。</param>
    /// <param name="services">宿主共享服务注册表。</param>
    /// <param name="modules">启用能力模块实例表（拓扑序）。</param>
    public CapabilityHost(CapabilityHostOptions options, CapabilityReport report, CapabilityScheduler scheduler, CapabilityServices services, IReadOnlyList<ICapabilityModule> modules)
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
    }
    /// <summary>
    /// 分发一条能力事件（同步驱动事件 job；P1 机制预留爆发口）。
    /// </summary>
    /// <param name="notification">事件通知。</param>
    public void RaiseEvent(CapabilityEventNotification notification)
    {
        Scheduler.RaiseEvent(notification);
    }

    /// <summary>
    /// 启动宿主（启动调度壳；可重复调用幂等）。
    /// </summary>
    /// <returns>异步任务。</returns>
    public Task StartAsync()
    {
        if (_started)
        {
            return Task.CompletedTask;
        }

        _started = true;
        Scheduler.Start();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 停止宿主（停调度壳，等待当轮完成）。
    /// </summary>
    /// <returns>异步任务。</returns>
    public async Task StopAsync()
    {
        _started = false;
        await Scheduler.StopAsync().ConfigureAwait(false);
    }
}
