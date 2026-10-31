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
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:    https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.Collections.Immutable;
using GameFrameX.Architecture.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace GameFrameX.Tests.Architecture;

/// <summary>
/// 验证 GFX0014（IComponentAgent 实现类必须以 ComponentAgent 结尾）的生成 Wrapper 豁免边界。
/// </summary>
/// <remarks>
/// Verifies the generated-wrapper exemption boundary of GFX0014 (IComponentAgent implementations must end with "ComponentAgent"):
/// a hand-written class named with the wrapper suffix outside Hotfix must be reported (CR-L02).
/// </remarks>
public sealed class ComponentAgentNameAnalyzerTests
{
    private const string StubSource = """
namespace GameFrameX.Core.Abstractions.Agent
{
    public interface IComponentAgent
    {
    }
}
""";

    [Fact]
    public async Task HandWrittenWrapperNamedAgentWrapper_OutsideHotfix_ReportsGFX0014()
    {
        var compilation = CreateCompilation(StubSource + """
namespace GameFrameX.Apps.Player.Component
{
    public sealed class PlayerComponentAgentWrapper : GameFrameX.Core.Abstractions.Agent.IComponentAgent
    {
    }
}
""", "GameFrameX.Apps");

        var diagnostics = await GetDiagnosticsAsync(compilation, new ComponentAgentNameAnalyzer());

        // Hotfix 之外的手写 Wrapper 后缀类名称不以 ComponentAgent 结尾：必须报 GFX0014（修复前被名称后缀豁免吞掉）
        var diagnostic = Assert.Single(diagnostics, item => item.Id == "GFX0014");
        Assert.Contains("PlayerComponentAgentWrapper", diagnostic.GetMessage());
    }

    [Fact]
    public async Task HandWrittenWrapperNamedAgentWrapper_InsideHotfix_NoDiagnostic()
    {
        var compilation = CreateCompilation(StubSource + """
namespace GameFrameX.Hotfix.Player
{
    public sealed class PlayerComponentAgentWrapper : GameFrameX.Core.Abstractions.Agent.IComponentAgent
    {
    }
}
""", "GameFrameX.Hotfix");

        var diagnostics = await GetDiagnosticsAsync(compilation, new ComponentAgentNameAnalyzer());

        // 过渡期兜底只对 Hotfix 程序集生效：Hotfix 内的 Wrapper 后缀类（生成器存量产物形态）保持豁免
        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task ProperlyNamedComponentAgent_OutsideHotfix_NoDiagnostic()
    {
        var compilation = CreateCompilation(StubSource + """
namespace GameFrameX.Apps.Player.Component
{
    public sealed class ProperlyNamedComponentAgent : GameFrameX.Core.Abstractions.Agent.IComponentAgent
    {
    }
}
""", "GameFrameX.Apps");

        var diagnostics = await GetDiagnosticsAsync(compilation, new ComponentAgentNameAnalyzer());

        // 命名合规：GFX0014 只管命名，程序集归属由 GFX0005 管
        Assert.Empty(diagnostics);
    }

    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(Compilation compilation, DiagnosticAnalyzer analyzer)
    {
        var compilationWithAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create(analyzer));
        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    private static CSharpCompilation CreateCompilation(string source, string assemblyName, string filePath = "")
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp10), filePath);
        var references = AppDomain.CurrentDomain
            .GetAssemblies()
            .Where(assembly => !assembly.IsDynamic
                               && !string.IsNullOrWhiteSpace(assembly.Location)
                               && !assembly.GetName().Name!.StartsWith("GameFrameX.", StringComparison.Ordinal))
            .Select(assembly => MetadataReference.CreateFromFile(assembly.Location))
            .GroupBy(reference => ((PortableExecutableReference)reference).FilePath)
            .Select(group => group.First())
            .ToArray();

        return CSharpCompilation.Create(
            assemblyName,
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
