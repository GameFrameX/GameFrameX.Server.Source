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

public sealed class MongoImplementationAccessAnalyzerTests
{
    private const string StubSource = """
namespace GameFrameX.DataBase
{
    public static class MultiDbRegistry
    {
    }

    public static class GameDb
    {
        public static void Init<T>(string connectionString) where T : new()
        {
        }
    }
}

namespace GameFrameX.DataBase.Mongo
{
    public class MongoDbService
    {
    }
}
""";

    [Fact]
    public async Task Declaration_surface_referencing_mongo_service_in_hotfix_reports_diagnostic()
    {
        var compilation = CreateCompilation(StubSource + """
namespace GameFrameX.Hotfix.Logic.Sample
{
    public sealed class SampleService
    {
        private readonly GameFrameX.DataBase.Mongo.MongoDbService _service;

        public SampleService(GameFrameX.DataBase.Mongo.MongoDbService service)
        {
            _service = service;
        }

        public GameFrameX.DataBase.Mongo.MongoDbService Service
        {
            get { return _service; }
        }
    }
}
""", "GameFrameX.Hotfix");

        var diagnostics = await GetDiagnosticsAsync(compilation, new MongoImplementationAccessAnalyzer());

        var diagnostic = Assert.Single(diagnostics, item => item.Id == "GFX0016");
        Assert.Contains("SampleService", diagnostic.GetMessage());
    }

    [Fact]
    public async Task Declaration_surface_referencing_multi_db_registry_in_launcher_reports_diagnostic()
    {
        var compilation = CreateCompilation(StubSource + """
namespace GameFrameX.Launcher.StartUp
{
    public sealed class StartUpGame
    {
        private readonly GameFrameX.DataBase.MultiDbRegistry _registry;

        public StartUpGame(GameFrameX.DataBase.MultiDbRegistry registry)
        {
            _registry = registry;
        }
    }
}
""", "GameFrameX.Launcher");

        var diagnostics = await GetDiagnosticsAsync(compilation, new MongoImplementationAccessAnalyzer());

        Assert.Single(diagnostics, item => item.Id == "GFX0016");
    }

    [Fact]
    public async Task Composition_root_generic_registration_in_method_body_is_exempt()
    {
        var compilation = CreateCompilation(StubSource + """
namespace GameFrameX.Launcher.StartUp
{
    public sealed class StartUpGame
    {
        public static void Init()
        {
            GameFrameX.DataBase.GameDb.Init<GameFrameX.DataBase.Mongo.MongoDbService>("mongodb://127.0.0.1");
        }
    }
}
""", "GameFrameX.Launcher");

        var diagnostics = await GetDiagnosticsAsync(compilation, new MongoImplementationAccessAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Mongo_implementation_layer_assembly_is_exempt()
    {
        var compilation = CreateCompilation(StubSource + """
namespace GameFrameX.DataBase.Mongo.Internal
{
    public sealed class MongoInternals
    {
        public GameFrameX.DataBase.Mongo.MongoDbService Resolve(GameFrameX.DataBase.MultiDbRegistry registry)
        {
            return null;
        }
    }
}
""", "GameFrameX.DataBase.Mongo");

        var diagnostics = await GetDiagnosticsAsync(compilation, new MongoImplementationAccessAnalyzer());

        Assert.Empty(diagnostics);
    }

    [Fact]
    public async Task Facade_only_access_in_apps_reports_no_diagnostic()
    {
        var compilation = CreateCompilation(StubSource + """
namespace GameFrameX.Apps.Player.Component
{
    public sealed class PlayerComponent
    {
        public void Save()
        {
            GameFrameX.DataBase.GameDb.Init<GameFrameX.DataBase.Mongo.MongoDbService>("");
        }
    }
}
""", "GameFrameX.Apps");

        var diagnostics = await GetDiagnosticsAsync(compilation, new MongoImplementationAccessAnalyzer());

        Assert.Empty(diagnostics);
    }

    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(Compilation compilation, DiagnosticAnalyzer analyzer)
    {
        var compilationWithAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create(analyzer));
        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    private static CSharpCompilation CreateCompilation(string source, string assemblyName)
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
            assemblyName,
            [syntaxTree],
            references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
