//  ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
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
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using System.Collections.Generic;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace GameFrameX.Architecture.Analyzers;

/// <summary>
/// GFX0023/24/25：框架仓包分层规则（引用图断言，与 GFX0017/18 同族 compilation-end 检查）。
/// <para>
/// 三条规则：① 层级单调——只准高层引低层（严格小于），同层禁互引（GFX0023）；
/// ② 契约包（*.Abstractions）零运行时引用——只准引用 L0 原语与其他契约包（GFX0024）；
/// ③ 能力实现包四禁引——Apps / Hotfix / Proto / Core（GFX0025）。
/// 数据来源为编译器引用图（含传递引用，无源码使用同样检查）；未登记进
/// <see cref="CapabilityLayeringFacts"/> 的程序集不参与断言。
/// </para>
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CapabilityPackageLayeringAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor LayerMonotonicRule = new(
        "GFX0023",
        "包分层必须单调（同层禁互引）",
        "程序集 '{0}'（{1}）引用了 '{2}'（{3}）：只准高层引低层（Ln→Lm, m&lt;n）、同层禁互引；请把公共面下沉到更低层或调整包边界",
        ArchitectureAnalyzerConstants.Category,
        DiagnosticSeverity.Error,
        true,
        customTags: new[] { WellKnownDiagnosticTags.CompilationEnd });

    private static readonly DiagnosticDescriptor ContractPurityRule = new(
        "GFX0024",
        "契约包不得引用运行时/实现工程",
        "契约包 '{0}' 引用了 '{1}'：契约包只准引用 L0 原语与其他契约包；管理面/机制运行时应反向依赖契约（契约上移，勿在契约内引实现）",
        ArchitectureAnalyzerConstants.Category,
        DiagnosticSeverity.Error,
        true,
        customTags: new[] { WellKnownDiagnosticTags.CompilationEnd });

    private static readonly DiagnosticDescriptor CapabilityBanRule = new(
        "GFX0025",
        "能力包禁止引用 Apps/Hotfix/Proto/Core",
        "能力包 '{0}' 引用了 '{1}'：会话请经 Foundation.Session 契约、协议请经契约 DTO 隔离、玩法请经桥契约；能力包不得依赖玩法层、协议产物与胖核心",
        ArchitectureAnalyzerConstants.Category,
        DiagnosticSeverity.Error,
        true,
        customTags: new[] { WellKnownDiagnosticTags.CompilationEnd });

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        => ImmutableArray.Create(LayerMonotonicRule, ContractPurityRule, CapabilityBanRule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.RegisterCompilationAction(AnalyzeReferences);
    }

    private static void AnalyzeReferences(CompilationAnalysisContext context)
    {
        var compilation = context.Compilation;
        var tree = compilation.SyntaxTrees.FirstOrDefault();
        var location = tree == null ? Location.None : tree.GetRoot(context.CancellationToken).GetFirstToken().GetLocation();

        // 引用闭包 BFS（与 GFX0017 同口径：含传递引用，不依赖源码使用）；
        // 编译自身为登记包时（分析器接线进业务工程的形态）其直接出边一并检查。
        var queue = new Queue<IAssemblySymbol>();
        var visited = new HashSet<string>(System.StringComparer.Ordinal);
        var reported = new HashSet<(int Rule, string From, string To)>();
        var consumerName = compilation.AssemblyName;
        var consumerRegistered = CapabilityLayeringFacts.TryGetRank(consumerName, out _);
        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols)
        {
            queue.Enqueue(assembly);
            if (consumerRegistered)
            {
                CheckEdge(context, location, reported, consumerName, assembly.Identity.Name);
            }
        }

        while (queue.Count > 0)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var assembly = queue.Dequeue();
            if (!visited.Add(assembly.Identity.Name))
            {
                continue;
            }

            foreach (var dependency in assembly.Modules.SelectMany(module => module.ReferencedAssemblySymbols))
            {
                queue.Enqueue(dependency);
                CheckEdge(context, location, reported, assembly.Identity.Name, dependency.Identity.Name);
            }
        }
    }

    /// <summary>单边检查：两端均在分层事实表登记时逐条执行三条规则（同一边可同时命中多条，各自去重报出）。</summary>
    private static void CheckEdge(CompilationAnalysisContext context, Location location, HashSet<(int Rule, string From, string To)> reported, string fromName, string toName)
    {
        if (!CapabilityLayeringFacts.TryGetRank(fromName, out var fromRank)
            || !CapabilityLayeringFacts.TryGetRank(toName, out var toRank))
        {
            return;
        }

        // ① 层级单调 + 同层禁互引（严格小于才合法）。
        if (toRank >= fromRank && reported.Add((1, fromName, toName)))
        {
            context.ReportDiagnostic(Diagnostic.Create(LayerMonotonicRule, location, fromName,
                CapabilityLayeringFacts.FormatRank(fromRank), toName, CapabilityLayeringFacts.FormatRank(toRank)));
        }

        // ② 契约包零运行时引用（L0 原语与其他契约包之外一律拒绝）。
        if (CapabilityLayeringFacts.IsContractPackage(fromName)
            && !CapabilityLayeringFacts.IsAllowedContractReference(toName)
            && reported.Add((2, fromName, toName)))
        {
            context.ReportDiagnostic(Diagnostic.Create(ContractPurityRule, location, fromName, toName));
        }

        // ③ 能力实现包四禁引（Apps / Hotfix / Proto / Core）。
        if (CapabilityBoundaryFacts.IsImplementation(fromName)
            && CapabilityLayeringFacts.IsBannedCapabilityReference(toName)
            && reported.Add((3, fromName, toName)))
        {
            context.ReportDiagnostic(Diagnostic.Create(CapabilityBanRule, location, fromName, toName));
        }
    }
}
