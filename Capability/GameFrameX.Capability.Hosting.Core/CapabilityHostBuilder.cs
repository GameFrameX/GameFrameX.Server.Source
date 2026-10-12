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
/// 能力装配器（显式链式装配入口；自动扫描经 <see cref="CapabilityAssembly"/> 发现模块后同样走本装配器）。
/// <para>
/// 维护约束（装配红线）：manifest Id 全宿主唯一；<c>Requires</c> 做闭包校验（缺依赖/成环即装配失败 fail-fast）；
/// 启用集合按拓扑定序，同层稳定序 = 模块加入序；过滤器引用未发现的能力 Id 即装配失败；
/// 调度 job 全名（能力Id/jobId）唯一，重复即装配失败。
/// </para>
/// </summary>
public sealed class CapabilityHostBuilder
{
    /// <summary>
    /// 已加入模块（加入序）。
    /// </summary>
    private readonly List<ICapabilityModule> _modules = new();

    /// <summary>
    /// 装配过滤器（null = 全部启用）。
    /// </summary>
    private CapabilityAssemblyFilter _filter;

    /// <summary>
    /// 宿主共享服务注册表（组合根预置与模块注册共用；装配后经 <see cref="CapabilityHost.Services"/> 取用）。
    /// </summary>
    private readonly CapabilityServices _services = new();

    /// <summary>
    /// 加入一个能力模块。
    /// </summary>
    /// <param name="module">能力模块。</param>
    /// <returns>本装配器（链式）。</returns>
    public CapabilityHostBuilder AddModule(ICapabilityModule module)
    {
        if (module == null)
        {
            throw new ArgumentNullException(nameof(module));
        }

        _modules.Add(module);
        return this;
    }

    /// <summary>
    /// 设置装配过滤器（配置排除表与能力清单段）。
    /// </summary>
    /// <param name="filter">过滤器（null = 全部启用）。</param>
    /// <returns>本装配器（链式）。</returns>
    public CapabilityHostBuilder SetFilter(CapabilityAssemblyFilter filter)
    {
        _filter = filter;
        return this;
    }

    /// <summary>
    /// 预置一个服务（组合根侧的服务共享面：平台服务 / 包级 options / store 替换缝共用；
    /// 同类型重复注册即装配失败）。
    /// </summary>
    /// <typeparam name="TService">服务类型（调用点书写形态，可为接口）。</typeparam>
    /// <param name="service">服务实例（不可为 null）。</param>
    /// <returns>本装配器（链式）。</returns>
    public CapabilityHostBuilder UseService<TService>(TService service)
    {
        _services.AddService(service);
        return this;
    }

    /// <summary>
    /// 装配（闭包校验 → 过滤 → 拓扑定序 → 逐能力注册 → 产出宿主与能力报告）。
    /// </summary>
    /// <param name="options">宿主选项。</param>
    /// <returns>能力宿主。</returns>
    public CapabilityHost Build(CapabilityHostOptions options)
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        var filter = _filter ?? new CapabilityAssemblyFilter();
        var manifestsById = new Dictionary<string, ICapabilityModule>(StringComparer.Ordinal);
        foreach (var module in _modules)
        {
            var id = module.Manifest.Id;
            if (manifestsById.ContainsKey(id))
            {
                // Localization: Capability.Hosting.DuplicateCapability - 能力 Id 重复注册。
                throw new InvalidOperationException(string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.DuplicateCapability), id));
            }

            manifestsById.Add(id, module);
        }

        ValidateFilterTargets(filter, manifestsById);

        var entries = new List<CapabilityReportEntry>();
        var enabled = new List<ICapabilityModule>();
        foreach (var module in _modules)
        {
            var state = filter.Match(module.Manifest);
            entries.Add(new CapabilityReportEntry(module.Manifest, module.GetType().Assembly.GetName().Name, state));
            if (state == CapabilityFilterState.Enabled)
            {
                enabled.Add(module);
            }
        }

        var ordered = OrderByRequires(enabled);

        var tickJobs = new List<CapabilityTickJob>();
        var intervalJobs = new List<CapabilityIntervalJob>();
        var eventJobs = new List<CapabilityEventJob>();
        foreach (var module in ordered)
        {
            var context = new CapabilityRegistrationContext(module.Manifest, _services);
            module.Register(context);
            tickJobs.AddRange(context.TickJobs);
            intervalJobs.AddRange(context.IntervalJobs);
            eventJobs.AddRange(context.EventJobs);
        }

        var report = new CapabilityReport(entries, ordered.Select(m => m.Manifest.Id).ToArray());
        var scheduler = new CapabilityScheduler(options.SchedulerIntervalMilliseconds, tickJobs, intervalJobs, eventJobs);
        return new CapabilityHost(options, report, scheduler, _services, ordered);
    }

    /// <summary>
    /// 校验过滤器目标（引用未发现的能力 Id 即 fail-fast）。
    /// </summary>
    /// <param name="filter">过滤器。</param>
    /// <param name="manifestsById">已发现模块表。</param>
    private static void ValidateFilterTargets(CapabilityAssemblyFilter filter, Dictionary<string, ICapabilityModule> manifestsById)
    {
        if (filter.EnabledIds != null)
        {
            foreach (var id in filter.EnabledIds)
            {
                if (!manifestsById.ContainsKey(id))
                {
                    // Localization: Capability.Hosting.FilterTargetMissing - 过滤器引用了未发现的能力。
                    throw new InvalidOperationException(string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.FilterTargetMissing), id));
                }
            }
        }

        foreach (var id in filter.DisabledIds)
        {
            if (!manifestsById.ContainsKey(id))
            {
                // Localization: Capability.Hosting.FilterTargetMissing - 过滤器引用了未发现的能力。
                throw new InvalidOperationException(string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.FilterTargetMissing), id));
            }
        }
    }

    /// <summary>
    /// 按 Requires 拓扑定序（Kahn 稳定版：同层保持加入序；缺依赖或成环即 fail-fast）。
    /// </summary>
    /// <param name="enabled">启用模块（加入序）。</param>
    /// <returns>拓扑定序后的模块序列。</returns>
    private static List<ICapabilityModule> OrderByRequires(List<ICapabilityModule> enabled)
    {
        var byId = enabled.ToDictionary(m => m.Manifest.Id, StringComparer.Ordinal);
        var inDegree = enabled.ToDictionary(m => m.Manifest.Id, _ => 0, StringComparer.Ordinal);
        var dependents = enabled.ToDictionary(m => m.Manifest.Id, _ => new List<string>(), StringComparer.Ordinal);
        foreach (var module in enabled)
        {
            foreach (var requiredId in module.Manifest.Requires)
            {
                if (!byId.TryGetValue(requiredId, out var required))
                {
                    // Localization: Capability.Hosting.RequirementMissing - 能力依赖缺失。
                    throw new InvalidOperationException(string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.RequirementMissing), module.Manifest.Id, requiredId));
                }

                inDegree[module.Manifest.Id]++;
                dependents[requiredId].Add(module.Manifest.Id);
            }
        }

        var ready = new Queue<string>(enabled.Where(m => inDegree[m.Manifest.Id] == 0).Select(m => m.Manifest.Id));
        var ordered = new List<ICapabilityModule>(enabled.Count);
        while (ready.Count > 0)
        {
            var id = ready.Dequeue();
            ordered.Add(byId[id]);
            foreach (var dependentId in dependents[id])
            {
                inDegree[dependentId]--;
                if (inDegree[dependentId] == 0)
                {
                    ready.Enqueue(dependentId);
                }
            }
        }

        if (ordered.Count != enabled.Count)
        {
            // Localization: Capability.Hosting.RequirementCycle - 能力依赖成环。
            throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.Capability.Hosting.RequirementCycle));
        }

        return ordered;
    }
}
