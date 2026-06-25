// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//  ==========================================================================================

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace GameFrameX.Architecture.Analyzers;

/// <summary>
/// GFX0015：StateComponentAgent 子类在 GameFrameX.Hotfix 程序集中不能标记为 sealed。
/// </summary>
/// <remarks>
/// <para>规则：所有继承自 StateComponentAgent&lt;TComponent, TState&gt; 的 Hotfix 类不得使用 sealed 修饰符。</para>
/// <para>原因：StateComponentAgent 承载状态组件的业务逻辑和生命周期协作，
/// 在 hotfix 层需要保持可扩展性，以便后续补丁可以继续通过继承方式替换或扩展行为。</para>
/// <para>目的：避免 hotfix 中的状态组件代理被密封，保留热更新阶段的继承扩展能力。</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StateComponentAgentSealedAnalyzer : SingleDiagnosticSymbolAnalyzer
{
    private static readonly DiagnosticDescriptor SDescriptor = new DiagnosticDescriptor(
        "GFX0015",
        "StateComponentAgent must not be sealed in GameFrameX.Hotfix",
        "State component agent '{0}' must not be sealed in assembly '{1}'",
        ArchitectureAnalyzerConstants.Category,
        DiagnosticSeverity.Error,
        true);

    protected override DiagnosticDescriptor Descriptor
    {
        get { return SDescriptor; }
    }

    protected override void AnalyzeNamedType(SymbolAnalysisContext context, ArchitectureSymbols symbols, INamedTypeSymbol type)
    {
        var assemblyName = type.ContainingAssembly.Identity.Name;
        if (!type.IsSealed
            || assemblyName != ArchitectureAnalyzerConstants.HotfixAssembly
            || !ArchitectureSymbolFacts.InheritsFrom(type, symbols.StateComponentAgent))
        {
            return;
        }

        ArchitectureSymbolFacts.Report(context, Descriptor, type, type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), assemblyName);
    }
}
