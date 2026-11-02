// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository:  https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:    https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:      https://cnb.cool/GameFrameX
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
/// GFX0022 静态组合根分析器测试：静态类持有 CapabilityRuntime / CapabilityHost 装配状态或静态调用
/// CapabilityComposition.Compose 必报；实例组合根（实例字段持有运行时 / 实例方法调用 Compose）与
/// 机制程序集白名单不报。机制类型以源内桩呈现（分析器按元数据名解析，桩与真实类型等价）。
/// </summary>
public sealed class StaticCompositionRootAnalyzerTests
{
    private const string MechanismStubSource = """
namespace GameFrameX.Capability.Hosting.Core
{
    public static class CapabilityComposition
    {
        public static object Compose(object family, object options)
        {
            return new CapabilityRuntime();
        }
    }

    public sealed class CapabilityRuntime
    {
    }

    public sealed class CapabilityHost
    {
    }
}
""";

    [Fact]
    public async Task Static_class_holding_runtime_state_reports_diagnostic()
    {
        var compilation = CreateCompilation(MechanismStubSource + """
namespace GameFrameX.Launcher.Sample
{
    public static class SampleStaticCompositionRoot
    {
        private static readonly GameFrameX.Capability.Hosting.Core.CapabilityRuntime Runtime = null;
    }
}
""", "GameFrameX.Launcher.Sample");

        var diagnostics = await GetDiagnosticsAsync(compilation, new StaticCompositionRootAnalyzer());

        var diagnostic = Assert.Single(diagnostics, item => item.Id == "GFX0022");
        Assert.Contains("SampleStaticCompositionRoot", diagnostic.GetMessage());
        Assert.Contains("CapabilityRuntime", diagnostic.GetMessage());
    }

    [Fact]
    public async Task Static_class_holding_host_state_reports_diagnostic()
    {
        var compilation = CreateCompilation(MechanismStubSource + """
namespace GameFrameX.Launcher.Sample
{
    public static class SampleHostHolder
    {
        public static GameFrameX.Capability.Hosting.Core.CapabilityHost HostProperty { get; set; }
    }
}
""", "GameFrameX.Launcher.Sample");

        var diagnostics = await GetDiagnosticsAsync(compilation, new StaticCompositionRootAnalyzer());

        Assert.Single(diagnostics, item => item.Id == "GFX0022");
    }

    [Fact]
    public async Task Static_class_invoking_compose_reports_diagnostic()
    {
        var compilation = CreateCompilation(MechanismStubSource + """
namespace GameFrameX.Launcher.Sample
{
    public static class SampleStaticComposer
    {
        public static object ComposeIfEnabled()
        {
            return GameFrameX.Capability.Hosting.Core.CapabilityComposition.Compose(null, null);
        }
    }
}
""", "GameFrameX.Launcher.Sample");

        var diagnostics = await GetDiagnosticsAsync(compilation, new StaticCompositionRootAnalyzer());

        var diagnostic = Assert.Single(diagnostics, item => item.Id == "GFX0022");
        Assert.Contains("SampleStaticComposer", diagnostic.GetMessage());
        Assert.Contains("Compose", diagnostic.GetMessage());
    }

    [Fact]
    public async Task Instance_composition_root_does_not_report()
    {
        var compilation = CreateCompilation(MechanismStubSource + """
namespace GameFrameX.Launcher.Sample
{
    public sealed class SampleInstanceCompositionRoot
    {
        private GameFrameX.Capability.Hosting.Core.CapabilityRuntime runtime;

        public object ComposeAndStart()
        {
            runtime = (GameFrameX.Capability.Hosting.Core.CapabilityRuntime)GameFrameX.Capability.Hosting.Core.CapabilityComposition.Compose(null, null);
            return runtime;
        }
    }
}
""", "GameFrameX.Launcher.Sample");

        var diagnostics = await GetDiagnosticsAsync(compilation, new StaticCompositionRootAnalyzer());

        Assert.DoesNotContain(diagnostics, item => item.Id == "GFX0022");
    }

    [Fact]
    public async Task Static_forwarding_of_runtime_parameter_does_not_report()
    {
        var compilation = CreateCompilation(MechanismStubSource + """
namespace GameFrameX.Launcher.Sample
{
    public static class SampleRuntimeForwarder
    {
        public static void Stop(GameFrameX.Capability.Hosting.Core.CapabilityRuntime runtime)
        {
        }
    }
}
""", "GameFrameX.Launcher.Sample");

        var diagnostics = await GetDiagnosticsAsync(compilation, new StaticCompositionRootAnalyzer());

        Assert.DoesNotContain(diagnostics, item => item.Id == "GFX0022");
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
