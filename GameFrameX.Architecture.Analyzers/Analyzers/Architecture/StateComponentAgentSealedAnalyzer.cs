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
