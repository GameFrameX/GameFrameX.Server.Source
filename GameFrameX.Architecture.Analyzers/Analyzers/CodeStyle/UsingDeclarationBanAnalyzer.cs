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
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace GameFrameX.Architecture.Analyzers;

/// <summary>
/// GFX0021：禁止 using 声明（<c>using var x = ...;</c> 与 <c>await using var x = ...;</c>）。
/// </summary>
/// <remarks>
/// <para>规则：一次性资源必须使用带大括号的 using 语句块
/// （<c>using (var x = ...) { ... }</c> / <c>await using (var x = ...) { ... }</c>）。</para>
/// <para>原因：using 声明的释放时机隐式延续到所在块尾部，阅读时无法直观判断作用域，
/// 在长方法或嵌套 lambda 中极易误判资源存活范围。</para>
/// <para>实现说明：Roslyn 内置 IDE0063 只提供「块 → 声明」单向简化建议，没有「声明 → 块」的规则，
/// 因此禁用方向必须由本分析器强制；严重级别经 .editorconfig 的 dotnet_diagnostic 配置。</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UsingDeclarationBanAnalyzer : DiagnosticAnalyzer
{
    private const string DiagnosticId = "GFX0021";

    private static readonly DiagnosticDescriptor SDescriptor = new DiagnosticDescriptor(
        DiagnosticId,
        "using declaration is forbidden",
        "'using var' declaration is forbidden; use 'using (var x = ...) { ... }' instead.",
        ArchitectureAnalyzerConstants.CodeStyleCategory,
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    /// 返回本分析器唯一支持的诊断 GFX0021（禁止 using 声明，须改用带大括号的 using 语句块，严重级别 Error）。
    /// </summary>
    /// <remarks>
    /// Returns the single diagnostic supported by this analyzer: GFX0021 (using declarations are forbidden; use a braced using statement block, severity Error).
    /// </remarks>
    /// <value>仅包含 GFX0021 描述符的不可变数组 / Immutable array containing only the GFX0021 descriptor</value>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
    {
        get { return ImmutableArray.Create(SDescriptor); }
    }

    /// <summary>
    /// 注册分析动作：不分析生成代码，启用并发执行，并针对局部声明语句节点注册检查动作，带 using 关键字即上报。
    /// </summary>
    /// <remarks>
    /// Registers analysis: generated code is skipped, concurrent execution is enabled, and a syntax node action on
    /// local declaration statements reports any declaration carrying the using keyword at that keyword's location.
    /// </remarks>
    /// <param name="context">分析上下文，用于配置并注册语法节点动作 / Analysis context used to configure and register the syntax node action</param>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(AnalyzeLocalDeclaration, SyntaxKind.LocalDeclarationStatement);
    }

    private static void AnalyzeLocalDeclaration(SyntaxNodeAnalysisContext context)
    {
        var declaration = (LocalDeclarationStatementSyntax)context.Node;
        if (declaration.UsingKeyword.IsKind(SyntaxKind.None))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(SDescriptor, declaration.UsingKeyword.GetLocation()));
    }
}
