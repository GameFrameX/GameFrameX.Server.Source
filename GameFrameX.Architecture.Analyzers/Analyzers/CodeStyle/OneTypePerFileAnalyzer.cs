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

using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace GameFrameX.Architecture.Analyzers;

/// <summary>
/// GFX0019：每个 .cs 文件只允许定义一个顶层类型。
/// </summary>
/// <remarks>
/// <para>规则：顶层类型（class / interface / record / struct / enum / delegate）必须各自独占一个文件；
/// 同一 partial 类型的多个声明视为同一类型，不计违规。</para>
/// <para>原因：一个文件多个平级类型会让检索、review diff 与代码归属变得模糊，
/// 并在改动单个类型时放大冲突面。</para>
/// <para>豁免：嵌套类型（类中类）、生成代码、以及经 .editorconfig 按路径关闭本规则的文件
/// （如 ProtoExport 导出的 GameFrameX.Proto 生成物）。</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class OneTypePerFileAnalyzer : DiagnosticAnalyzer
{
    private const string DiagnosticId = "GFX0019";

    private static readonly DiagnosticDescriptor SDescriptor = new DiagnosticDescriptor(
        DiagnosticId,
        "File must contain only one top-level type",
        "Top-level type '{0}' must be moved to its own file '{0}.cs'",
        ArchitectureAnalyzerConstants.CodeStyleCategory,
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    /// 返回本分析器唯一支持的诊断 GFX0019（每个文件只允许一个顶层类型，严重级别 Error）。
    /// </summary>
    /// <remarks>
    /// Returns the single diagnostic supported by this analyzer: GFX0019 (one top-level type per file, severity Error).
    /// </remarks>
    /// <value>仅包含 GFX0019 描述符的不可变数组 / Immutable array containing only the GFX0019 descriptor</value>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
    {
        get { return ImmutableArray.Create(SDescriptor); }
    }

    /// <summary>
    /// 注册分析动作：不分析生成代码，启用并发执行，并为每棵语法树注册顶层类型计数动作。
    /// </summary>
    /// <remarks>
    /// Registers analysis: generated code is skipped, concurrent execution is enabled, and a syntax tree action collects top-level types.
    /// Declarations sharing the same name and arity (partial parts) count as one type; only the first extra type is reported.
    /// </remarks>
    /// <param name="context">分析上下文，用于配置并注册语法树动作 / Analysis context used to configure and register the syntax tree action</param>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxTreeAction(AnalyzeTree);
    }

    private static void AnalyzeTree(SyntaxTreeAnalysisContext context)
    {
        var types = CodeStyleSyntaxFacts.CollectTopLevelTypes(context.Tree.GetRoot(context.CancellationToken));
        string firstKey = null;
        foreach (var type in types)
        {
            var key = type.Name + "`" + type.Arity;
            if (firstKey == null)
            {
                firstKey = key;
                continue;
            }

            if (string.Equals(firstKey, key, StringComparison.Ordinal))
            {
                continue;
            }

            // 只在第一个多余类型上报：同一文件的 N 个平级类型不刷 N-1 条重复诊断。
            context.ReportDiagnostic(Diagnostic.Create(SDescriptor, type.Location, type.Name));
            return;
        }
    }
}
