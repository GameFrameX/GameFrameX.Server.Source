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
/// 依赖检查器（过滤之后、拓扑定序之前执行——早于任何模块 Register；检查不通过不执行任何模块代码）。
/// <para>
/// 维护约束（一次报全红线）：全部必选缺失与环路先收集（环路列完整路径，如 <c>a → b → c → a</c>），
/// 逐条打 Error 后由组合入口抛 <see cref="CapabilityAssemblyException"/>；可选缺失只记数据（Info 行由
/// 日志渲染器统一输出，固定文案由机制拼接）；缺席原因区分白名单未收录 / 排除表命中 / 部署目录未发现；
/// 定序只由依赖声明推导（必选边 + 在场可选边），同层按能力 Id 序数（StringComparer.Ordinal）。
/// </para>
/// </summary>
internal static class CapabilityDependencyChecker
{
    /// <summary>
    /// 执行依赖检查与拓扑定序。
    /// </summary>
    /// <param name="allModules">全部已发现模块（含被过滤的）。</param>
    /// <param name="enabledModules">过滤器判定启用的模块。</param>
    /// <param name="filter">装配过滤器（缺席原因判定用）。</param>
    /// <returns>检查结果（问题条目 / 依赖状态 / 缺失功能 / 解析来源策略 / 拓扑序）。</returns>
    public static Result Check(IReadOnlyList<ICapabilityModule> allModules, IReadOnlyList<ICapabilityModule> enabledModules, CapabilityAssemblyFilter filter)
    {
        var manifestsById = new Dictionary<string, CapabilityManifest>(StringComparer.Ordinal);
        for (var i = 0; i < allModules.Count; i++)
        {
            manifestsById[allModules[i].Manifest.Id] = allModules[i].Manifest;
        }

        var enabledIds = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < enabledModules.Count; i++)
        {
            enabledIds.Add(enabledModules[i].Manifest.Id);
        }

        var result = new Result();
        var edges = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        for (var i = 0; i < enabledModules.Count; i++)
        {
            edges[enabledModules[i].Manifest.Id] = new List<string>();
        }

        for (var i = 0; i < enabledModules.Count; i++)
        {
            CheckModule(enabledModules[i].Manifest, manifestsById, enabledIds, filter, result, edges);
        }

        CollectCycles(enabledModules, edges, result);
        if (result.Problems.Count == 0)
        {
            OrderByDependencies(enabledModules, edges, result);
        }

        return result;
    }

    /// <summary>
    /// 检查单个模块的必选 / 可选依赖（必选缺失记问题；可选缺失记数据；在场依赖记定序边与来源策略）。
    /// </summary>
    /// <param name="manifest">被检查模块清单。</param>
    /// <param name="manifestsById">全部已发现清单表。</param>
    /// <param name="enabledIds">启用能力 Id 集。</param>
    /// <param name="filter">装配过滤器。</param>
    /// <param name="result">检查结果累积器。</param>
    /// <param name="edges">定序边累积器（依赖方 → 依赖集合）。</param>
    private static void CheckModule(CapabilityManifest manifest, Dictionary<string, CapabilityManifest> manifestsById, HashSet<string> enabledIds, CapabilityAssemblyFilter filter, Result result, Dictionary<string, List<string>> edges)
    {
        var dependencies = new List<CapabilityDependencyReport>();
        var allowedOwners = new HashSet<string>(StringComparer.Ordinal) { manifest.Id, };
        var unassembledOptional = new List<string>();

        for (var i = 0; i < manifest.Requires.Count; i++)
        {
            var requiredId = manifest.Requires[i];
            var reason = ResolveAbsenceReason(requiredId, manifestsById, enabledIds, filter);
            if (reason == CapabilityDependencyAbsenceReason.None)
            {
                dependencies.Add(new CapabilityDependencyReport(requiredId, CapabilityDependencyKind.Required, CapabilityDependencyAssemblyState.Assembled, CapabilityDependencyAbsenceReason.None));
                edges[manifest.Id].Add(requiredId);
                allowedOwners.Add(requiredId);
                continue;
            }

            // Localization: Capability.Hosting.Assembly.RequiredDependencyMissing - 必选依赖未装配。
            result.Problems.Add(string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.Assembly.AssemblyRequiredDependencyMissing), manifest.Id, requiredId, DescribeReason(reason)));
            dependencies.Add(new CapabilityDependencyReport(requiredId, CapabilityDependencyKind.Required, CapabilityDependencyAssemblyState.NotAssembled, reason));
        }

        for (var i = 0; i < manifest.Optional.Count; i++)
        {
            var optional = manifest.Optional[i];
            var reason = ResolveAbsenceReason(optional.CapabilityId, manifestsById, enabledIds, filter);
            if (reason == CapabilityDependencyAbsenceReason.None)
            {
                dependencies.Add(new CapabilityDependencyReport(optional.CapabilityId, CapabilityDependencyKind.Optional, CapabilityDependencyAssemblyState.Assembled, CapabilityDependencyAbsenceReason.None));
                edges[manifest.Id].Add(optional.CapabilityId);
                allowedOwners.Add(optional.CapabilityId);
                continue;
            }

            dependencies.Add(new CapabilityDependencyReport(optional.CapabilityId, CapabilityDependencyKind.Optional, CapabilityDependencyAssemblyState.NotAssembled, reason));
            unassembledOptional.Add(optional.CapabilityId);
            result.AbsentFeaturesByCapability.TryGetValue(manifest.Id, out var absentFeatures);
            if (absentFeatures == null)
            {
                absentFeatures = new List<string>();
                result.AbsentFeaturesByCapability[manifest.Id] = absentFeatures;
            }

            absentFeatures.Add(LocalizationService.GetString(optional.AbsentFeatureLocalizationKey));
        }

        result.DependenciesByCapability[manifest.Id] = dependencies;
        result.AllowedOwnersByConsumer[manifest.Id] = allowedOwners;
        result.UnassembledOptionalByConsumer[manifest.Id] = unassembledOptional;
    }

    /// <summary>
    /// 解析依赖目标的缺席原因（已装配 = None；白名单未收录 / 排除表命中 / 部署目录未发现）。
    /// </summary>
    /// <param name="dependencyId">依赖能力 Id。</param>
    /// <param name="manifestsById">全部已发现清单表。</param>
    /// <param name="enabledIds">启用能力 Id 集。</param>
    /// <param name="filter">装配过滤器。</param>
    /// <returns>缺席原因（None = 已装配）。</returns>
    private static CapabilityDependencyAbsenceReason ResolveAbsenceReason(string dependencyId, Dictionary<string, CapabilityManifest> manifestsById, HashSet<string> enabledIds, CapabilityAssemblyFilter filter)
    {
        if (enabledIds.Contains(dependencyId))
        {
            return CapabilityDependencyAbsenceReason.None;
        }

        if (!manifestsById.TryGetValue(dependencyId, out var manifest))
        {
            return CapabilityDependencyAbsenceReason.NotDiscovered;
        }

        var state = filter.Match(manifest);
        if (state == CapabilityFilterState.ExcludedByDisableList)
        {
            return CapabilityDependencyAbsenceReason.ExcludedByDisableList;
        }

        return CapabilityDependencyAbsenceReason.NotInEnableList;
    }

    /// <summary>
    /// 收集全部循环依赖（必选边 + 在场可选边；每个环以完整路径记一条问题，规范化去重）。
    /// </summary>
    /// <param name="enabledModules">启用模块。</param>
    /// <param name="edges">定序边（依赖方 → 依赖集合）。</param>
    /// <param name="result">检查结果累积器。</param>
    private static void CollectCycles(IReadOnlyList<ICapabilityModule> enabledModules, Dictionary<string, List<string>> edges, Result result)
    {
        var nodeState = new Dictionary<string, int>(StringComparer.Ordinal);
        var stack = new List<string>();
        var seenCycles = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < enabledModules.Count; i++)
        {
            if (!nodeState.TryGetValue(enabledModules[i].Manifest.Id, out var state) || state == 0)
            {
                DepthFirstSearch(enabledModules[i].Manifest.Id, edges, nodeState, stack, seenCycles, result);
            }
        }
    }

    /// <summary>
    /// 深度优先搜索（0 = 未访问 / 1 = 在栈 / 2 = 已完成；回边即成环）。
    /// </summary>
    /// <param name="current">当前能力 Id。</param>
    /// <param name="edges">定序边。</param>
    /// <param name="nodeState">节点状态表。</param>
    /// <param name="stack">当前路径栈。</param>
    /// <param name="seenCycles">已报告环的规范化键集。</param>
    /// <param name="result">检查结果累积器。</param>
    private static void DepthFirstSearch(string current, Dictionary<string, List<string>> edges, Dictionary<string, int> nodeState, List<string> stack, HashSet<string> seenCycles, Result result)
    {
        nodeState[current] = 1;
        stack.Add(current);
        if (edges.TryGetValue(current, out var dependencies))
        {
            for (var i = 0; i < dependencies.Count; i++)
            {
                var next = dependencies[i];
                if (!nodeState.TryGetValue(next, out var nextState))
                {
                    nextState = 0;
                }

                if (nextState == 1)
                {
                    ReportCycle(next, stack, seenCycles, result);
                    continue;
                }

                if (nextState == 0)
                {
                    DepthFirstSearch(next, edges, nodeState, stack, seenCycles, result);
                }
            }
        }

        stack.RemoveAt(stack.Count - 1);
        nodeState[current] = 2;
    }

    /// <summary>
    /// 报告一个环（从栈中回边目标起截取路径；规范化旋转去重后记一条问题）。
    /// </summary>
    /// <param name="cycleStart">回边目标（环起点）。</param>
    /// <param name="stack">当前路径栈。</param>
    /// <param name="seenCycles">已报告环的规范化键集。</param>
    /// <param name="result">检查结果累积器。</param>
    private static void ReportCycle(string cycleStart, List<string> stack, HashSet<string> seenCycles, Result result)
    {
        var startIndex = stack.LastIndexOf(cycleStart);
        if (startIndex < 0)
        {
            return;
        }

        var cycle = new List<string>();
        for (var i = startIndex; i < stack.Count; i++)
        {
            cycle.Add(stack[i]);
        }

        cycle.Add(cycleStart);
        var canonicalKey = BuildCanonicalCycleKey(cycle);
        if (!seenCycles.Add(canonicalKey))
        {
            return;
        }

        var path = string.Join(" → ", cycle);
        // Localization: Capability.Hosting.Assembly.DependencyCycle - 依赖成环。
        result.Problems.Add(string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.Assembly.AssemblyDependencyCycle), path));
    }

    /// <summary>
    /// 生成环的规范化键（旋转到字典序最小起点；同一环只报一次）。
    /// </summary>
    /// <param name="cycle">环路径（首尾相同）。</param>
    /// <returns>规范化键。</returns>
    private static string BuildCanonicalCycleKey(List<string> cycle)
    {
        var nodes = cycle.Count - 1;
        var bestOffset = 0;
        for (var offset = 1; offset < nodes; offset++)
        {
            for (var i = 0; i < nodes; i++)
            {
                var comparison = string.CompareOrdinal(cycle[(offset + i) % nodes], cycle[(bestOffset + i) % nodes]);
                if (comparison < 0)
                {
                    bestOffset = offset;
                    break;
                }

                if (comparison > 0)
                {
                    break;
                }
            }
        }

        var parts = new string[nodes];
        for (var i = 0; i < nodes; i++)
        {
            parts[i] = cycle[(bestOffset + i) % nodes];
        }

        return string.Join("|", parts);
    }

    /// <summary>
    /// 按依赖边拓扑定序（Kahn：同层按能力 Id 序数取最小——装配顺序不与扫描序、程序集名挂钩）。
    /// </summary>
    /// <param name="enabledModules">启用模块。</param>
    /// <param name="edges">定序边（依赖方 → 依赖集合）。</param>
    /// <param name="result">检查结果累积器（写入 Order）。</param>
    private static void OrderByDependencies(IReadOnlyList<ICapabilityModule> enabledModules, Dictionary<string, List<string>> edges, Result result)
    {
        BuildDependencyGraph(edges, out var inDegree, out var dependents);

        var ready = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var pair in inDegree)
        {
            if (pair.Value == 0)
            {
                ready.Add(pair.Key);
            }
        }

        result.Order = DrainReadyQueue(ready, inDegree, dependents, enabledModules.Count);
    }

    /// <summary>
    /// 构建入度表与反向邻接表（依赖边 → 被依赖方索引）。
    /// </summary>
    /// <param name="edges">定序边（依赖方 → 依赖集合）。</param>
    /// <param name="inDegree">入度表（每个依赖方的依赖计数）。</param>
    /// <param name="dependents">反向邻接表（被依赖方 → 依赖方列表）。</param>
    private static void BuildDependencyGraph(Dictionary<string, List<string>> edges, out Dictionary<string, int> inDegree, out Dictionary<string, List<string>> dependents)
    {
        inDegree = new Dictionary<string, int>(StringComparer.Ordinal);
        dependents = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var pair in edges)
        {
            inDegree[pair.Key] = 0;
            dependents[pair.Key] = new List<string>();
        }

        foreach (var pair in edges)
        {
            for (var i = 0; i < pair.Value.Count; i++)
            {
                var dependency = pair.Value[i];
                inDegree[pair.Key]++;
                if (!dependents.ContainsKey(dependency))
                {
                    dependents[dependency] = new List<string>();
                }

                dependents[dependency].Add(pair.Key);
            }
        }
    }

    /// <summary>
    /// Kahn 排空就绪队列（同层按能力 Id 序数取最小，产出装配顺序）。
    /// </summary>
    /// <param name="ready">就绪集合（入度为零的节点）。</param>
    /// <param name="inDegree">入度表（随排空递减）。</param>
    /// <param name="dependents">反向邻接表。</param>
    /// <param name="capacity">结果容量预估。</param>
    /// <returns>拓扑定序结果。</returns>
    private static List<string> DrainReadyQueue(SortedSet<string> ready, Dictionary<string, int> inDegree, Dictionary<string, List<string>> dependents, int capacity)
    {
        var ordered = new List<string>(capacity);
        while (ready.Count > 0)
        {
            var id = ready.Min;
            ready.Remove(id);
            ordered.Add(id);
            var dependentIds = dependents[id];
            for (var i = 0; i < dependentIds.Count; i++)
            {
                var dependentId = dependentIds[i];
                if (!inDegree.ContainsKey(dependentId))
                {
                    continue;
                }

                inDegree[dependentId]--;
                if (inDegree[dependentId] == 0)
                {
                    ready.Add(dependentId);
                }
            }
        }

        return ordered;
    }

    /// <summary>
    /// 缺席原因文案（区分配置问题与部署产物问题）。
    /// </summary>
    /// <param name="reason">缺席原因。</param>
    /// <returns>本地化文案。</returns>
    public static string DescribeReason(CapabilityDependencyAbsenceReason reason)
    {
        switch (reason)
        {
            case CapabilityDependencyAbsenceReason.NotInEnableList:
                // Localization: Capability.Hosting.Assembly.ReasonNotInEnableList - 白名单未收录。
                return LocalizationService.GetString(Localization.Keys.Capability.Hosting.Assembly.AssemblyReasonNotInEnableList);
            case CapabilityDependencyAbsenceReason.ExcludedByDisableList:
                // Localization: Capability.Hosting.Assembly.ReasonExcludedByDisableList - 配置排除表。
                return LocalizationService.GetString(Localization.Keys.Capability.Hosting.Assembly.AssemblyReasonExcludedByDisableList);
            case CapabilityDependencyAbsenceReason.NotDiscovered:
                // Localization: Capability.Hosting.Assembly.ReasonNotDiscovered - 部署目录未发现。
                return LocalizationService.GetString(Localization.Keys.Capability.Hosting.Assembly.AssemblyReasonNotDiscovered);
            default:
                return string.Empty;
        }
    }

    /// <summary>
    /// 依赖检查结果（问题条目 + 依赖状态数据 + 解析来源策略 + 拓扑序）。
    /// </summary>
    internal sealed class Result
    {
        /// <summary>
        /// 初始化 <see cref="Result"/>。
        /// </summary>
        public Result()
        {
            Problems = new List<string>();
            DependenciesByCapability = new Dictionary<string, List<CapabilityDependencyReport>>(StringComparer.Ordinal);
            AbsentFeaturesByCapability = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            AllowedOwnersByConsumer = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            UnassembledOptionalByConsumer = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            Order = new List<string>();
        }

        /// <summary>
        /// 获取全部问题条目（必选缺失 + 环路；已本地化；非空即装配失败）。
        /// </summary>
        public List<string> Problems { get; }

        /// <summary>
        /// 获取能力 Id → 依赖状态条目表（报告数据面）。
        /// </summary>
        public Dictionary<string, List<CapabilityDependencyReport>> DependenciesByCapability { get; }

        /// <summary>
        /// 获取能力 Id → 缺失功能文案表（可选缺失；报告与日志共用）。
        /// </summary>
        public Dictionary<string, List<string>> AbsentFeaturesByCapability { get; }

        /// <summary>
        /// 获取消费者能力 Id → 允许解析的来源 Id 集合（含自身 + 直接必选与在场可选依赖；族 Id 由注册表另行放行）。
        /// </summary>
        public Dictionary<string, HashSet<string>> AllowedOwnersByConsumer { get; }

        /// <summary>
        /// 获取消费者能力 Id → 已声明但未装配的可选依赖 Id 列表（每个启用模块一条，可为空）。
        /// 模块作用域的 <c>GetOptionalService</c> 只有列表非空时才允许未命中返回 null，否则按「能力在场但服务未注册」fail-fast。
        /// </summary>
        public Dictionary<string, List<string>> UnassembledOptionalByConsumer { get; }

        /// <summary>
        /// 获取拓扑定序后的能力 Id 序（同层按 Id 序数；无问题时才有意义）。
        /// </summary>
        public List<string> Order { get; set; }
    }
}
