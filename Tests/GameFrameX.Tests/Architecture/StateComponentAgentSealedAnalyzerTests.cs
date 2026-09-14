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
using GameFrameX.Architecture.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace GameFrameX.Tests.Architecture;

public sealed class StateComponentAgentSealedAnalyzerTests
{
    [Fact]
    public async Task Sealed_state_component_agent_in_hotfix_reports_diagnostic()
    {
        var compilation = CreateCompilation("""
namespace GameFrameX.DataBase
{
    public abstract class BaseCacheState
    {
    }
}

namespace GameFrameX.Core.Components
{
    public abstract class StateComponent<TState> where TState : GameFrameX.DataBase.BaseCacheState, new()
    {
    }
}

namespace GameFrameX.Core.Hotfix.Agent
{
    public abstract class StateComponentAgent<TComponent, TState> where TComponent : GameFrameX.Core.Components.StateComponent<TState> where TState : GameFrameX.DataBase.BaseCacheState, new()
    {
    }
}

namespace GameFrameX.Hotfix.Logic.Sample
{
    public sealed class SampleAgent : GameFrameX.Core.Hotfix.Agent.StateComponentAgent<SampleComponent, SampleState>
    {
    }

    public class OpenSampleAgent : GameFrameX.Core.Hotfix.Agent.StateComponentAgent<SampleComponent, SampleState>
    {
    }

    public sealed class SampleComponent : GameFrameX.Core.Components.StateComponent<SampleState>
    {
    }

    public sealed class SampleState : GameFrameX.DataBase.BaseCacheState
    {
    }
}
""");

        var analyzer = new StateComponentAgentSealedAnalyzer();
        var diagnostics = await GetDiagnosticsAsync(compilation, analyzer);

        var diagnostic = Assert.Single(diagnostics, item => item.Id == "GFX0015");
        Assert.Contains("SampleAgent", diagnostic.GetMessage());
    }

    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(Compilation compilation, DiagnosticAnalyzer analyzer)
    {
        var compilationWithAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create(analyzer));
        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp10));
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
            "GameFrameX.Hotfix",
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
