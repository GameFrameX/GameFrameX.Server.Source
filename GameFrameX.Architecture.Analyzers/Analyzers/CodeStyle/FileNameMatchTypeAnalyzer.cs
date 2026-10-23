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
using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace GameFrameX.Architecture.Analyzers;

/// <summary>
/// GFX0020：单类型文件的文件名必须与顶层类型名一致。
/// </summary>
/// <remarks>
/// <para>规则：文件只有一个顶层类型时，文件名（去扩展名）必须等于该类型名。允许两类命名变体：
/// partial 切片后缀（<c>GameDb.Query.cs</c> 对应 <c>GameDb</c>）与同名泛型消歧后缀
/// （<c>ServerSendResult{TResp}.cs</c> 对应 <c>ServerSendResult</c>）。</para>
/// <para>原因：文件名即索引键；名字漂移会让「按类型找文件」失效，也让 review 与搜索落在旧名上。</para>
/// <para>范围：多顶层类型文件由 GFX0019 负责，本规则跳过；生成代码与多类型文件同样跳过。</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class FileNameMatchTypeAnalyzer : DiagnosticAnalyzer
{
    private const string DiagnosticId = "GFX0020";

    private static readonly DiagnosticDescriptor SDescriptor = new DiagnosticDescriptor(
        DiagnosticId,
        "File name must match the declared type name",
        "File name '{0}' does not match top-level type '{1}'; rename the file to '{1}.cs'",
        ArchitectureAnalyzerConstants.CodeStyleCategory,
        DiagnosticSeverity.Error,
        true);

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
    {
        get { return ImmutableArray.Create(SDescriptor); }
    }

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxTreeAction(AnalyzeTree);
    }

    private static void AnalyzeTree(SyntaxTreeAnalysisContext context)
    {
        var fileName = Path.GetFileName(context.Tree.FilePath);
        if (string.IsNullOrEmpty(fileName) || !fileName.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var types = CodeStyleSyntaxFacts.CollectTopLevelTypes(context.Tree.GetRoot(context.CancellationToken));
        if (types.Count != 1)
        {
            return;
        }

        var type = types[0];
        var stem = fileName.Substring(0, fileName.Length - 3);
        var baseName = GetComparableBaseName(stem);
        if (string.Equals(baseName, type.Name, StringComparison.Ordinal))
        {
            return;
        }

        context.ReportDiagnostic(Diagnostic.Create(SDescriptor, type.Location, fileName, type.Name));
    }

    private static string GetComparableBaseName(string stem)
    {
        var dotIndex = stem.IndexOf('.');
        if (dotIndex > 0)
        {
            stem = stem.Substring(0, dotIndex);
        }

        var braceIndex = stem.IndexOf('{');
        if (braceIndex > 0)
        {
            stem = stem.Substring(0, braceIndex);
        }

        return stem;
    }
}
