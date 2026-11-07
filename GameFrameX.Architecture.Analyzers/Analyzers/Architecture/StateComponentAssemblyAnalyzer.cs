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
/// GFX0002：StateComponent 子类必须定义在 GameFrameX.Apps 程序集中。
/// </summary>
/// <remarks>
/// <para>规则：所有继承自 StateComponent&lt;TState&gt; 的类必须位于 GameFrameX.Apps 程序集。</para>
/// <para>原因：StateComponent 持有 CacheState 的引用，与状态数据紧密绑定。
/// 若将 StateComponent 放在 Hotfix 中，热重载时组件实例会被重建，
/// 但关联的 CacheState 仍在 Apps 层，导致引用断裂和状态丢失。</para>
/// <para>目的：确保 Component+State 组合始终在状态层中共存，维护 ECS 架构中状态的完整性。</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StateComponentAssemblyAnalyzer : SingleDiagnosticSymbolAnalyzer
{
    private static readonly DiagnosticDescriptor SDescriptor = new DiagnosticDescriptor(
        "GFX0002",
        "StateComponent must be defined in GameFrameX.Apps",
        "Type '{0}' inherits StateComponent<TState> and must be defined in assembly '{1}', not '{2}'",
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
        if (!ArchitectureSymbolFacts.InheritsFrom(type, symbols.StateComponent) || assemblyName == ArchitectureAnalyzerConstants.AppsAssembly)
        {
            return;
        }

        ArchitectureSymbolFacts.Report(context, Descriptor, type, type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), ArchitectureAnalyzerConstants.AppsAssembly, assemblyName);
    }
}
