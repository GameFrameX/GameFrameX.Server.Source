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
