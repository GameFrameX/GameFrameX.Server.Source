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
/// 装配日志渲染器（从 <see cref="CapabilityReport"/> 渲染装配日志；<c>list_capabilities</c> 读同一份报告，
/// 两边不会对不上）。
/// <para>
/// 维护约束（文案红线）：全部文案走本地化（<c>Capability.Hosting.Assembly.*</c>）；级别只用 Info 与 Error
/// ——必选缺失、环路、汇总失败用 Error，其余（开始、排除、逐能力汇总、可选缺失、完成）用 Info；
/// 可选缺失文案格式固定：{能力} 可选依赖 {依赖} 未装配（{原因}）：缺失功能「{功能}」，不影响 {能力} 其余功能运行。
/// </para>
/// </summary>
internal static class CapabilityAssemblyLogRenderer
{
    /// <summary>
    /// 可选依赖逐项列出的上限（超出改用计数形态；两种形态数据同源）。
    /// </summary>
    private const int OptionalListedLimit = 3;

    /// <summary>
    /// 渲染成功装配日志（开始行 + 排除行 + 逐能力汇总行 + 可选缺失行 + 完成行）。
    /// </summary>
    /// <param name="report">能力报告。</param>
    /// <param name="log">日志出口。</param>
    public static void RenderSuccess(CapabilityReport report, ICapabilityAssemblyLog log)
    {
        var enabledCount = report.EnabledIdsInOrder.Count;
        var excludedCount = report.Entries.Count - enabledCount;
        // Localization: Capability.Hosting.Assembly.Started - 能力装配开始。
        log.Info(string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.Assembly.AssemblyStarted), report.Entries.Count, enabledCount, excludedCount));

        RenderExcludedEntries(report, log);
        RenderModuleSummaries(report, log);
        RenderOptionalDependencyMissing(report, log);

        var absentFeatureCount = CountAbsentFeatures(report);
        // Localization: Capability.Hosting.Assembly.Completed - 能力装配完成。
        log.Info(string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.Assembly.AssemblyCompleted), enabledCount, absentFeatureCount));
    }

    /// <summary>
    /// 渲染失败装配日志（汇总头 + 逐条问题；必选缺失 / 环路）。
    /// </summary>
    /// <param name="problems">全部问题条目（已本地化）。</param>
    /// <param name="log">日志出口。</param>
    public static void RenderFailures(IReadOnlyList<string> problems, ICapabilityAssemblyLog log)
    {
        // Localization: Capability.Hosting.Assembly.Failed - 能力装配失败。
        log.Error(string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.Assembly.AssemblyFailed), problems.Count));
        for (var i = 0; i < problems.Count; i++)
        {
            log.Error("  " + problems[i]);
        }
    }

    /// <summary>
    /// 渲染已挂载但无能力声明的钩子（Info；对应能力未装配或未声明该钩子）。
    /// </summary>
    /// <param name="contractTypes">未被声明的已挂载契约类型。</param>
    /// <param name="log">日志出口。</param>
    public static void RenderHooksWithoutDeclaration(IReadOnlyList<Type> contractTypes, ICapabilityAssemblyLog log)
    {
        for (var i = 0; i < contractTypes.Count; i++)
        {
            // Localization: Capability.Hosting.Assembly.HookNotDeclared - 钩子已挂载但无能力声明。
            log.Info(string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.Assembly.AssemblyHookNotDeclared), contractTypes[i].FullName));
        }
    }

    /// <summary>
    /// 渲染排除行（每条注明缺席原因：白名单未收录 / 配置排除表）。
    /// </summary>
    /// <param name="report">能力报告。</param>
    /// <param name="log">日志出口。</param>
    private static void RenderExcludedEntries(CapabilityReport report, ICapabilityAssemblyLog log)
    {
        for (var i = 0; i < report.Entries.Count; i++)
        {
            var entry = report.Entries[i];
            if (entry.State != CapabilityFilterState.NotInEnableList && entry.State != CapabilityFilterState.ExcludedByDisableList)
            {
                continue;
            }

            var reason = entry.State == CapabilityFilterState.ExcludedByDisableList
                ? CapabilityDependencyAbsenceReason.ExcludedByDisableList
                : CapabilityDependencyAbsenceReason.NotInEnableList;
            // Localization: Capability.Hosting.Assembly.Excluded - 已排除。
            log.Info(string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.Assembly.AssemblyExcluded), entry.Manifest.Id, CapabilityDependencyChecker.DescribeReason(reason)));
        }
    }

    /// <summary>
    /// 渲染逐能力汇总行（装配序；无依赖显示来源程序集，有依赖显示依赖描述）。
    /// </summary>
    /// <param name="report">能力报告。</param>
    /// <param name="log">日志出口。</param>
    private static void RenderModuleSummaries(CapabilityReport report, ICapabilityAssemblyLog log)
    {
        var entryById = new Dictionary<string, CapabilityReportEntry>(StringComparer.Ordinal);
        for (var i = 0; i < report.Entries.Count; i++)
        {
            entryById[report.Entries[i].Manifest.Id] = report.Entries[i];
        }

        for (var i = 0; i < report.EnabledIdsInOrder.Count; i++)
        {
            var entry = entryById[report.EnabledIdsInOrder[i]];
            var ordinal = i + 1;
            var dependencyDescriptor = BuildDependencyDescriptor(entry);
            if (dependencyDescriptor == null)
            {
                // Localization: Capability.Hosting.Assembly.ModuleNoDependency - 无依赖能力汇总行。
                log.Info(string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.Assembly.AssemblyModuleNoDependency), ordinal, entry.Manifest.Id, entry.Manifest.Version, entry.SourceAssemblyName));
                continue;
            }

            // Localization: Capability.Hosting.Assembly.ModuleWithDependency - 带依赖能力汇总行。
            log.Info(string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.Assembly.AssemblyModuleWithDependency), ordinal, entry.Manifest.Id, entry.Manifest.Version, dependencyDescriptor));
        }
    }

    /// <summary>
    /// 构造依赖描述（必选段 + 可选段；可选 ≤ 3 个逐项列状态，超出用计数形态）。
    /// </summary>
    /// <param name="entry">报告条目。</param>
    /// <returns>依赖描述；无任何依赖为 null。</returns>
    private static string BuildDependencyDescriptor(CapabilityReportEntry entry)
    {
        var requiredIds = new List<string>();
        var optionalIds = new List<CapabilityDependencyReport>();
        for (var i = 0; i < entry.Dependencies.Count; i++)
        {
            if (entry.Dependencies[i].Kind == CapabilityDependencyKind.Required)
            {
                requiredIds.Add(entry.Dependencies[i].CapabilityId);
            }
            else
            {
                optionalIds.Add(entry.Dependencies[i]);
            }
        }

        if (requiredIds.Count == 0 && optionalIds.Count == 0)
        {
            return null;
        }

        var segments = new List<string>();
        if (requiredIds.Count > 0)
        {
            // Localization: Capability.Hosting.Assembly.DependencyRequired - 必选依赖段。
            segments.Add(string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.Assembly.AssemblyDependencyRequired), string.Join(" ", requiredIds)));
        }

        if (optionalIds.Count > 0)
        {
            segments.Add(BuildOptionalSegment(optionalIds));
        }

        return string.Join("  ", segments);
    }

    /// <summary>
    /// 构造可选依赖段（≤ 上限逐项列装配状态，超出用「X 个已装配，Y 个未装配」计数形态）。
    /// </summary>
    /// <param name="optionalDependencies">可选依赖条目。</param>
    /// <returns>可选依赖段文案。</returns>
    private static string BuildOptionalSegment(List<CapabilityDependencyReport> optionalDependencies)
    {
        if (optionalDependencies.Count > OptionalListedLimit)
        {
            var assembledCount = 0;
            var missingCount = 0;
            for (var i = 0; i < optionalDependencies.Count; i++)
            {
                if (optionalDependencies[i].State == CapabilityDependencyAssemblyState.Assembled)
                {
                    assembledCount++;
                }
                else
                {
                    missingCount++;
                }
            }

            // Localization: Capability.Hosting.Assembly.DependencyOptionalCounted - 可选依赖计数段。
            return string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.Assembly.AssemblyDependencyOptionalCounted), assembledCount, missingCount);
        }

        var tokens = new List<string>();
        for (var i = 0; i < optionalDependencies.Count; i++)
        {
            if (optionalDependencies[i].State == CapabilityDependencyAssemblyState.Assembled)
            {
                // Localization: Capability.Hosting.Assembly.OptionalStateAssembled - 已装配状态。
                tokens.Add(string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.Assembly.AssemblyOptionalStateAssembled), optionalDependencies[i].CapabilityId));
            }
            else
            {
                // Localization: Capability.Hosting.Assembly.OptionalStateNotAssembled - 未装配状态。
                tokens.Add(string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.Assembly.AssemblyOptionalStateNotAssembled), optionalDependencies[i].CapabilityId));
            }
        }

        // Localization: Capability.Hosting.Assembly.DependencyOptionalListed - 可选依赖逐项段。
        return string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.Assembly.AssemblyDependencyOptionalListed), string.Join(" ", tokens));
    }

    /// <summary>
    /// 渲染可选缺失行（固定文案：能力 可选依赖 依赖 未装配（原因）：缺失功能「功能」，不影响 能力 其余功能运行）。
    /// </summary>
    /// <param name="report">能力报告。</param>
    /// <param name="log">日志出口。</param>
    private static void RenderOptionalDependencyMissing(CapabilityReport report, ICapabilityAssemblyLog log)
    {
        for (var i = 0; i < report.Entries.Count; i++)
        {
            var entry = report.Entries[i];
            if (entry.State != CapabilityFilterState.Enabled)
            {
                continue;
            }

            for (var j = 0; j < entry.Dependencies.Count; j++)
            {
                var dependency = entry.Dependencies[j];
                if (dependency.Kind != CapabilityDependencyKind.Optional || dependency.State != CapabilityDependencyAssemblyState.NotAssembled)
                {
                    continue;
                }

                var featureText = FindAbsentFeatureText(entry.Manifest, dependency.CapabilityId);
                // Localization: Capability.Hosting.Assembly.OptionalDependencyMissing - 可选依赖缺失固定文案。
                log.Info(string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.Assembly.AssemblyOptionalDependencyMissing), entry.Manifest.Id, dependency.CapabilityId, CapabilityDependencyChecker.DescribeReason(dependency.AbsenceReason), featureText));
            }
        }
    }

    /// <summary>
    /// 查找可选依赖声明的缺失功能文案（本地化）。
    /// </summary>
    /// <param name="manifest">能力清单。</param>
    /// <param name="dependencyId">依赖能力 Id。</param>
    /// <returns>缺失功能文案。</returns>
    private static string FindAbsentFeatureText(CapabilityManifest manifest, string dependencyId)
    {
        for (var i = 0; i < manifest.Optional.Count; i++)
        {
            if (string.Equals(manifest.Optional[i].CapabilityId, dependencyId, StringComparison.Ordinal))
            {
                return LocalizationService.GetString(manifest.Optional[i].AbsentFeatureLocalizationKey);
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// 统计可选依赖缺失的功能总数（完成行用）。
    /// </summary>
    /// <param name="report">能力报告。</param>
    /// <returns>缺失功能总数。</returns>
    private static int CountAbsentFeatures(CapabilityReport report)
    {
        var count = 0;
        for (var i = 0; i < report.Entries.Count; i++)
        {
            count += report.Entries[i].AbsentFeatures.Count;
        }

        return count;
    }
}
