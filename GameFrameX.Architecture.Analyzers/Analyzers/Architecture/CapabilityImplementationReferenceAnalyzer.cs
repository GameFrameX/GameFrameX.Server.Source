// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规及开源许可证之规定。
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
//   Any legal disputes and liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:  https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace GameFrameX.Architecture.Analyzers;

/// <summary>
/// GFX0017：Apps / Hotfix 的编译引用闭包不得包含 Capability 实现包。
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CapabilityImplementationReferenceAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        "GFX0017",
        "Apps/Hotfix 不得依赖 Capability 实现",
        "程序集 '{0}' 引用了 Capability 实现 '{1}'（路径：{2}）；请仅经 Online.Abstractions 消费域契约，将实现装配移至 Launcher / Capability 模块",
        ArchitectureAnalyzerConstants.Category,
        DiagnosticSeverity.Error,
        true,
        customTags: new[] { WellKnownDiagnosticTags.CompilationEnd });

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.RegisterCompilationAction(AnalyzeReferences);
    }

    private static void AnalyzeReferences(CompilationAnalysisContext context)
    {
        var compilation = context.Compilation;
        var consumer = compilation.AssemblyName;
        if (consumer != ArchitectureAnalyzerConstants.AppsAssembly && consumer != ArchitectureAnalyzerConstants.HotfixAssembly)
        {
            return;
        }

        // 从编译器引用图出发；无源码使用的引用同样检查，不依赖 using 或命名空间。
        var queue = new Queue<(IAssemblySymbol Assembly, string Path)>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var assembly in compilation.SourceModule.ReferencedAssemblySymbols.OrderBy(a => a.Identity.ToString(), StringComparer.Ordinal))
        {
            queue.Enqueue((assembly, consumer + " → " + assembly.Identity.Name));
        }

        var tree = compilation.SyntaxTrees.FirstOrDefault();
        var location = tree == null ? Location.None : tree.GetRoot(context.CancellationToken).GetFirstToken().GetLocation();
        while (queue.Count > 0)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var (assembly, path) = queue.Dequeue();
            if (!visited.Add(assembly.Identity.ToString()))
            {
                continue;
            }

            if (CapabilityBoundaryFacts.IsImplementation(assembly.Identity.Name))
            {
                context.ReportDiagnostic(Diagnostic.Create(Rule, location, consumer, assembly.Identity.Name, path));
            }

            foreach (var dependency in assembly.Modules.SelectMany(m => m.ReferencedAssemblySymbols)
                         .OrderBy(a => a.Identity.ToString(), StringComparer.Ordinal))
            {
                queue.Enqueue((dependency, path + " → " + dependency.Identity.Name));
            }
        }
    }
}

/// <summary>Capability 边界共享事实；白名单来自已验证的 Online 契约编译闭包。</summary>
internal static class CapabilityBoundaryFacts
{
    internal const string HostingAbstractions = "GameFrameX.Capability.Hosting.Abstractions";
    internal const string HostingCore = "GameFrameX.Capability.Hosting.Core";
    internal const string OnlineAbstractions = "GameFrameX.Capability.Online.Abstractions";
    internal const string Launcher = "GameFrameX.Launcher";

    internal static bool IsImplementation(string name)
    {
        return name.StartsWith("GameFrameX.Capability.", StringComparison.Ordinal)
               && name != OnlineAbstractions
               && name != HostingAbstractions
               && name != "GameFrameX.Capability.Hosting.AdminApi";
    }

    internal static bool IsCompositionOrTest(string name)
    {
        return name == Launcher || name == "GameFrameX.Tests" || name == "GameFrameX.Hotfix.Tests";
    }
}
