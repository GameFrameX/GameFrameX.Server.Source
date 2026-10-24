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

using System.Text.Json;
using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Foundation.Localization.Core;

namespace GameFrameX.Capability.Hosting.Core;

/// <summary>
/// 能力装配器（<see cref="CapabilityComposition"/> 的内部实现：过滤 → 依赖检查 → 拓扑定序 →
/// 解析来源策略 → Options 合并 → 逐模块 Register → 产出宿主与报告）。
/// <para>
/// 维护约束（装配红线，全部 fail-fast）：过滤器引用未发现的能力 Id 即失败；依赖检查在任何模块
/// <c>Register</c> 之前完成（必选缺失 / 成环 = 一次报全的 <see cref="CapabilityAssemblyException"/>，
/// 不执行任何模块代码）；同层按能力 Id 序数定序；<c>Settings</c> 出现未发现的能力 Id 即失败、
/// 被过滤能力的设置段忽略并标注；job 全名（能力Id/jobId）唯一；机制包零能力硬编码。
/// </para>
/// </summary>
internal sealed class CapabilityHostBuilder
{
    /// <summary>
    /// 宿主共享服务注册表（族基建已预置）。
    /// </summary>
    private readonly CapabilityServices _services;

    /// <summary>
    /// 机制事件总线。
    /// </summary>
    private readonly CapabilityEventBus _eventBus;

    /// <summary>
    /// 能力族。
    /// </summary>
    private readonly ICapabilityFamily _family;

    /// <summary>
    /// 组合选项。
    /// </summary>
    private readonly CapabilityCompositionOptions _options;

    /// <summary>
    /// 初始化 <see cref="CapabilityHostBuilder"/>（仅 <see cref="CapabilityComposition"/> 构造）。
    /// </summary>
    /// <param name="services">服务注册表（族基建已预置）。</param>
    /// <param name="eventBus">机制事件总线。</param>
    /// <param name="family">能力族。</param>
    /// <param name="options">组合选项。</param>
    public CapabilityHostBuilder(CapabilityServices services, CapabilityEventBus eventBus, ICapabilityFamily family, CapabilityCompositionOptions options)
    {
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _eventBus = eventBus ?? throw new ArgumentNullException(nameof(eventBus));
        _family = family ?? throw new ArgumentNullException(nameof(family));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// 装配（过滤器校验 → 过滤 → 依赖检查 → 定序 → 来源策略 → Options 合并 → Register → 产出宿主）。
    /// </summary>
    /// <param name="modules">全部已发现模块（发现序）。</param>
    /// <returns>能力宿主（含报告与调度壳）。</returns>
    public CapabilityHost Build(IReadOnlyList<ICapabilityModule> modules)
    {
        var filter = _options.Filter ?? new CapabilityAssemblyFilter();
        var manifestsById = new Dictionary<string, ICapabilityModule>(StringComparer.Ordinal);
        for (var i = 0; i < modules.Count; i++)
        {
            manifestsById[modules[i].Manifest.Id] = modules[i];
        }

        ValidateFilterTargets(filter, manifestsById);

        var enabledModules = new List<ICapabilityModule>();
        for (var i = 0; i < modules.Count; i++)
        {
            if (filter.Match(modules[i].Manifest) == CapabilityFilterState.Enabled)
            {
                enabledModules.Add(modules[i]);
            }
        }

        // 依赖检查在任何模块 Register 之前：检查不通过就不执行任何模块代码，不会留下装了一半的状态。
        var dependencyResult = CapabilityDependencyChecker.Check(modules, enabledModules, filter);
        if (dependencyResult.Problems.Count > 0)
        {
            throw new CapabilityAssemblyException(dependencyResult.Problems);
        }

        _services.SetResolutionPolicy(dependencyResult.AllowedOwnersByConsumer, dependencyResult.UnassembledOptionalByConsumer);

        var orderedModules = OrderModulesByIds(manifestsById, dependencyResult.Order);
        var settingsSections = ExtractSettingsSections(manifestsById, enabledModules);
        ValidateConfigureTargets(manifestsById, enabledModules);

        var boundOptionsByCapability = new Dictionary<string, object>(StringComparer.Ordinal);
        var settingsFieldsByCapability = new Dictionary<string, List<CapabilitySettingsField>>(StringComparer.Ordinal);
        foreach (var module in orderedModules)
        {
            var errorPathPrefix = _family.ConfigurationSectionName + ".Settings." + module.Manifest.Id;
            settingsSections.TryGetValue(module.Manifest.Id, out var settingsElement);
            var binding = CapabilityOptionsBinder.Bind(module.Manifest, settingsElement, _options.ConfigureEntries, errorPathPrefix);
            if (binding.Instance != null)
            {
                boundOptionsByCapability[module.Manifest.Id] = binding.Instance;
            }

            if (binding.Fields.Count > 0)
            {
                settingsFieldsByCapability[module.Manifest.Id] = binding.Fields;
            }
        }

        // 钩子声明写入进程级静态槽位：装配任一步失败必须释放已声明项，否则同进程再次组合会误判重复声明。
        var hookDeclarationReleases = new List<IDisposable>();
        try
        {
            var tickJobs = new List<CapabilityTickJob>();
            var intervalJobs = new List<CapabilityIntervalJob>();
            foreach (var module in orderedModules)
            {
                boundOptionsByCapability.TryGetValue(module.Manifest.Id, out var boundOptions);
                var context = new CapabilityRegistrationContext(module.Manifest, _services, _eventBus, boundOptions, hookDeclarationReleases);
                module.Register(context);
                tickJobs.AddRange(context.TickJobs);
                intervalJobs.AddRange(context.IntervalJobs);
            }

            var hooksWithoutDeclaration = CapabilityHookSlots.GetAttachedWithoutDeclaration();
            if (hooksWithoutDeclaration.Count > 0)
            {
                CapabilityAssemblyLogRenderer.RenderHooksWithoutDeclaration(hooksWithoutDeclaration, _options.AssemblyLog);
            }

            var entries = BuildReportEntries(modules, filter, dependencyResult, settingsSections, settingsFieldsByCapability);
            var report = new CapabilityReport(entries, dependencyResult.Order);
            var schedulerObserver = _options.SchedulerObserver ?? new CapabilitySchedulerLogObserver(_options.AssemblyLog);
            var scheduler = new CapabilityScheduler(_options.HostOptions.SchedulerIntervalMilliseconds, tickJobs, intervalJobs, _options.SchedulerClock, schedulerObserver);
            return new CapabilityHost(_options.HostOptions, report, scheduler, _services, orderedModules, _eventBus, _options.AssemblyLog, hookDeclarationReleases);
        }
        catch (Exception)
        {
            CapabilityHost.ReleaseInReverse(hookDeclarationReleases);
            throw;
        }
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
    /// 按拓扑序 Id 序列取模块实例表。
    /// </summary>
    /// <param name="manifestsById">已发现模块表。</param>
    /// <param name="orderedIds">拓扑序 Id 序列。</param>
    /// <returns>拓扑序模块序列。</returns>
    private static List<ICapabilityModule> OrderModulesByIds(Dictionary<string, ICapabilityModule> manifestsById, List<string> orderedIds)
    {
        var orderedModules = new List<ICapabilityModule>(orderedIds.Count);
        for (var i = 0; i < orderedIds.Count; i++)
        {
            orderedModules.Add(manifestsById[orderedIds[i]]);
        }

        return orderedModules;
    }

    /// <summary>
    /// 提取 <c>Settings</c> 各能力配置段（未发现的能力 Id = fail-fast；被过滤能力不入返回表——调用方标注忽略）。
    /// </summary>
    /// <param name="manifestsById">已发现模块表。</param>
    /// <param name="enabledModules">启用模块。</param>
    /// <returns>能力 Id → 配置段节点表（仅启用能力）。</returns>
    private Dictionary<string, JsonElement> ExtractSettingsSections(Dictionary<string, ICapabilityModule> manifestsById, List<ICapabilityModule> enabledModules)
    {
        var sections = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        var section = _options.FamilyConfigurationSection;
        if (section.ValueKind != JsonValueKind.Object)
        {
            return sections;
        }

        JsonElement settingsElement = default;
        var hasSettings = false;
        foreach (var property in section.EnumerateObject())
        {
            if (string.Equals(property.Name, "Settings", StringComparison.OrdinalIgnoreCase))
            {
                settingsElement = property.Value;
                hasSettings = true;
                break;
            }
        }

        if (!hasSettings)
        {
            return sections;
        }

        if (settingsElement.ValueKind != JsonValueKind.Object)
        {
            // Settings 节点必须是「能力 Id → 配置段」对象；null / 数组 / 标量按类型不符 fail-fast，带族段路径。
            // Localization: Capability.Hosting.Assembly.SettingsFieldTypeMismatch - Options 字段类型不符。
            throw new InvalidOperationException(string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.Assembly.AssemblySettingsFieldTypeMismatch), _family.ConfigurationSectionName + ".Settings", "object", settingsElement.ValueKind.ToString()));
        }

        var enabledIds = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < enabledModules.Count; i++)
        {
            enabledIds.Add(enabledModules[i].Manifest.Id);
        }


        foreach (var property in settingsElement.EnumerateObject())
        {
            if (!manifestsById.ContainsKey(property.Name))
            {
                // Localization: Capability.Hosting.Assembly.SettingsUnknownCapability - Settings 配置段出现未发现的能力。
                throw new InvalidOperationException(string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.Assembly.AssemblySettingsUnknownCapability), property.Name));
            }

            if (!enabledIds.Contains(property.Name))
            {
                // 被过滤器排除的能力：设置段忽略，报告标注「已配置但未装配」。
                continue;
            }

            sections[property.Name] = property.Value;
        }

        return sections;
    }

    /// <summary>
    /// 校验代码 Configure 目标（引用未由任何已发现能力声明的 Options 类型 = fail-fast；被过滤能力忽略）。
    /// </summary>
    /// <param name="manifestsById">已发现模块表。</param>
    /// <param name="enabledModules">启用模块。</param>
    private void ValidateConfigureTargets(Dictionary<string, ICapabilityModule> manifestsById, List<ICapabilityModule> enabledModules)
    {
        var declaredOptionsTypes = new HashSet<Type>();
        for (var i = 0; i < enabledModules.Count; i++)
        {
            var optionsType = enabledModules[i].Manifest.OptionsType;
            if (optionsType != null)
            {
                declaredOptionsTypes.Add(optionsType);
            }
        }

        var configureEntries = _options.ConfigureEntries;
        for (var i = 0; i < configureEntries.Count; i++)
        {
            if (declaredOptionsTypes.Contains(configureEntries[i].OptionsType))
            {
                continue;
            }

            var declaredByAnyDiscovered = false;
            foreach (var pair in manifestsById)
            {
                if (pair.Value.Manifest.OptionsType == configureEntries[i].OptionsType)
                {
                    declaredByAnyDiscovered = true;
                    break;
                }
            }

            if (!declaredByAnyDiscovered)
            {
                // Localization: Capability.Hosting.ConfigureTargetMissing - 代码 Configure 引用的 Options 类型未声明。
                throw new InvalidOperationException(string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.ConfigureTargetMissing), configureEntries[i].OptionsType.FullName));
            }

            // 声明该 Options 的能力被过滤器排除：Configure 回调忽略（与 Settings 段忽略规则一致）。
        }
    }

    /// <summary>
    /// 构造报告条目（发现序；启用条目带依赖状态 / 缺失功能 / Options 字段；被过滤且配置了 Settings 的条目标注）。
    /// </summary>
    /// <param name="modules">全部已发现模块。</param>
    /// <param name="filter">过滤器。</param>
    /// <param name="dependencyResult">依赖检查结果。</param>
    /// <param name="settingsSections">启用能力的配置段表。</param>
    /// <param name="settingsFieldsByCapability">Options 字段生效值表。</param>
    /// <returns>报告条目集合。</returns>
    private List<CapabilityReportEntry> BuildReportEntries(IReadOnlyList<ICapabilityModule> modules, CapabilityAssemblyFilter filter, CapabilityDependencyChecker.Result dependencyResult, Dictionary<string, JsonElement> settingsSections, Dictionary<string, List<CapabilitySettingsField>> settingsFieldsByCapability)
    {
        var entries = new List<CapabilityReportEntry>(modules.Count);
        var settingsConfiguredIds = CollectSettingsConfiguredIds();
        for (var i = 0; i < modules.Count; i++)
        {
            var module = modules[i];
            var manifest = module.Manifest;
            var state = filter.Match(manifest);
            dependencyResult.DependenciesByCapability.TryGetValue(manifest.Id, out var dependencies);
            dependencyResult.AbsentFeaturesByCapability.TryGetValue(manifest.Id, out var absentFeatures);
            settingsFieldsByCapability.TryGetValue(manifest.Id, out var settingsFields);
            var settingsConfiguredButNotAssembled = state != CapabilityFilterState.Enabled && settingsConfiguredIds.Contains(manifest.Id);
            entries.Add(new CapabilityReportEntry(manifest, module.GetType().Assembly.GetName().Name, state, dependencies, absentFeatures, settingsFields, settingsConfiguredButNotAssembled));
        }

        return entries;
    }

    /// <summary>
    /// 收集配置段写了 Settings 的全部能力 Id（含被过滤的——标注「已配置但未装配」用）。
    /// </summary>
    /// <returns>能力 Id 集合。</returns>
    private HashSet<string> CollectSettingsConfiguredIds()
    {
        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        var section = _options.FamilyConfigurationSection;
        if (section.ValueKind != JsonValueKind.Object)
        {
            return identifiers;
        }

        foreach (var property in section.EnumerateObject())
        {
            if (string.Equals(property.Name, "Settings", StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.Object)
            {
                foreach (var settingsProperty in property.Value.EnumerateObject())
                {
                    identifiers.Add(settingsProperty.Name);
                }

                break;
            }
        }

        return identifiers;
    }
}
