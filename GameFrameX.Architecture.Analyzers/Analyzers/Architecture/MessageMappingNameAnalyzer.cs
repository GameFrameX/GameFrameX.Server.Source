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

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace GameFrameX.Architecture.Analyzers;

/// <summary>
/// GFX0012：标记了 MessageMappingAttribute 的类型名称必须以 "Handler" 结尾。
/// </summary>
/// <remarks>
/// <para>规则：所有标注了 MessageMappingAttribute 的类型的名称必须以 "Handler" 结尾。</para>
/// <para>原因：统一的命名约定让开发者一眼识别出消息处理器的职责，
/// 也便于源生成器和文档工具按约定发现处理器类型。</para>
/// <para>目的：强制执行命名约定，提高代码可读性和可维护性。</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MessageMappingNameAnalyzer : SingleDiagnosticSymbolAnalyzer
{
    private static readonly DiagnosticDescriptor SDescriptor = new DiagnosticDescriptor(
        "GFX0012",
        "MessageMapping handler name must end with Handler",
        "MessageMapping handler '{0}' must end with 'Handler'",
        ArchitectureAnalyzerConstants.Category,
        DiagnosticSeverity.Error,
        true);

    protected override DiagnosticDescriptor Descriptor
    {
        get { return SDescriptor; }
    }

    protected override void AnalyzeNamedType(SymbolAnalysisContext context, ArchitectureSymbols symbols, INamedTypeSymbol type)
    {
        if (!ArchitectureSymbolFacts.HasAttribute(type, symbols.MessageMappingAttribute) || type.Name.EndsWith("Handler", StringComparison.Ordinal))
        {
            return;
        }

        ArchitectureSymbolFacts.Report(context, Descriptor, type, type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat));
    }
}
