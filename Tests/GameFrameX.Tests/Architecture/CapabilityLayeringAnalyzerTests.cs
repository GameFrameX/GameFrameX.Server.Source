//  ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
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
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GameFrameX.Architecture.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace GameFrameX.Tests.Architecture;

/// <summary>
/// 分层规则分析器单测（GFX0023 层级单调 + 同层禁互引 / GFX0024 契约包零运行时引用 / GFX0025 能力包四禁引）：
/// 全部经内存 Emit 的伪程序集构造引用图（不引入真实工程闭包），正反样例逐规则断言。
/// </summary>
public sealed class CapabilityLayeringAnalyzerTests
{
    private static readonly ImmutableArray<MetadataReference> PlatformReferences = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
        .Split(Path.PathSeparator)
        .Where(path => !Path.GetFileName(path).StartsWith("GameFrameX.", System.StringComparison.Ordinal))
        .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path)).ToImmutableArray();

    [Fact]
    public async Task SameLayerReference_IsRejected()
    {
        var matchmaking = Emit("GameFrameX.Capability.Online.Matchmaking", "public class Dependency { }");
        var social = Emit("GameFrameX.Capability.Online.Social", "public class Peer { public Dependency Value; }", matchmaking);
        var diagnostic = Assert.Single(await Analyze(Compile("GameFrameX.Tests", "public class Consumer { }", social), new CapabilityPackageLayeringAnalyzer()));
        Assert.Equal("GFX0023", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("GameFrameX.Capability.Online.Social", diagnostic.GetMessage());
        Assert.Contains("GameFrameX.Capability.Online.Matchmaking", diagnostic.GetMessage());
        Assert.Contains("L3.3", diagnostic.GetMessage());
    }

    [Fact]
    public async Task UpwardReference_IsRejected()
    {
        var match = Emit("GameFrameX.Capability.Online.Match", "public class Dependency { }");
        var social = Emit("GameFrameX.Capability.Online.Social", "public class Peer { public Dependency Value; }", match);
        var diagnostic = Assert.Single(await Analyze(Compile("GameFrameX.Tests", "public class Consumer { }", social), new CapabilityPackageLayeringAnalyzer()));
        Assert.Equal("GFX0023", diagnostic.Id);
        Assert.Contains("L3.4", diagnostic.GetMessage());
        Assert.Contains("L3.3", diagnostic.GetMessage());
    }

    [Theory]
    [InlineData("GameFrameX.Capability.Online.Match", "GameFrameX.Capability.Online.Assets")]
    [InlineData("GameFrameX.Capability.Online.Leaderboard", "GameFrameX.Capability.Online.Match")]
    [InlineData("GameFrameX.Capability.Online.Competition", "GameFrameX.Capability.Online.Leaderboard")]
    [InlineData("GameFrameX.Capability.Online.Persistence", "GameFrameX.DataBase")]
    [InlineData("GameFrameX.Hotfix", "GameFrameX.Apps")]
    [InlineData("GameFrameX.Capability.Hosting.AdminApi", "GameFrameX.Capability.Hosting.Core")]
    public async Task DownwardReference_IsAllowed(string fromName, string toName)
    {
        var dependency = Emit(toName, "public class Dependency { }");
        var consumer = Emit(fromName, "public class Peer { public Dependency Value; }", dependency);
        Assert.Empty(await Analyze(Compile("GameFrameX.Tests", "public class Consumer { }", consumer), new CapabilityPackageLayeringAnalyzer()));
    }

    [Fact]
    public async Task ContractPackageReferencingRuntime_IsRejected()
    {
        var adminApi = Emit("GameFrameX.Capability.Hosting.AdminApi", "public class Runtime { }");
        var contracts = Emit("GameFrameX.Capability.Online.Abstractions", "public class Contract { public Runtime Value; }", adminApi);
        var diagnostic = Assert.Single(await Analyze(Compile("GameFrameX.Tests", "public class Consumer { }", contracts), new CapabilityPackageLayeringAnalyzer()));
        Assert.Equal("GFX0024", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("GameFrameX.Capability.Online.Abstractions", diagnostic.GetMessage());
        Assert.Contains("GameFrameX.Capability.Hosting.AdminApi", diagnostic.GetMessage());
    }

    [Theory]
    [InlineData("GameFrameX.Localization")]
    [InlineData("GameFrameX.Utility")]
    [InlineData("GameFrameX.Capability.Hosting.Abstractions")]
    public async Task ContractPackageReferencingPrimitivesOrContracts_IsAllowed(string targetName)
    {
        var primitive = Emit(targetName, "public class Target { }");
        var contracts = Emit("GameFrameX.Capability.Online.Abstractions", "public class Contract { public Target Value; }", primitive);
        Assert.Empty(await Analyze(Compile("GameFrameX.Tests", "public class Consumer { }", contracts), new CapabilityPackageLayeringAnalyzer()));
    }

    [Theory]
    [InlineData("GameFrameX.Proto")]
    [InlineData("GameFrameX.Core")]
    public async Task CapabilityPackageReferencingBannedAssembly_IsRejected(string bannedName)
    {
        var banned = Emit(bannedName, "public class Banned { }");
        var capability = Emit("GameFrameX.Capability.Online.Social", "public class Peer { public Banned Value; }", banned);
        var diagnostic = Assert.Single(await Analyze(Compile("GameFrameX.Tests", "public class Consumer { }", capability), new CapabilityPackageLayeringAnalyzer()));
        Assert.Equal("GFX0025", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("GameFrameX.Capability.Online.Social", diagnostic.GetMessage());
        Assert.Contains(bannedName, diagnostic.GetMessage());
    }

    [Theory]
    [InlineData("GameFrameX.Apps")]
    [InlineData("GameFrameX.Hotfix")]
    public async Task CapabilityPackageReferencingGameplayLayer_ReportsLayeringAndBanTogether(string bannedName)
    {
        // 玩法层在能力包之上：同一出边同时命中层级单调（GFX0023）与四禁引（GFX0025），两条互补报出。
        var banned = Emit(bannedName, "public class Banned { }");
        var capability = Emit("GameFrameX.Capability.Online.Social", "public class Peer { public Banned Value; }", banned);
        var diagnostics = (await Analyze(Compile("GameFrameX.Tests", "public class Consumer { }", capability), new CapabilityPackageLayeringAnalyzer())).OrderBy(item => item.Id).ToArray();
        Assert.Equal(2, diagnostics.Length);
        Assert.Equal("GFX0023", diagnostics[0].Id);
        Assert.Equal("GFX0025", diagnostics[1].Id);
        Assert.Contains(bannedName, diagnostics[1].GetMessage());
    }

    [Fact]
    public async Task RegisteredConsumerAssembly_OwnEdgesAreChecked()
    {
        var apps = Emit("GameFrameX.Apps", "public class Data { }");
        var matchmaking = Emit("GameFrameX.Capability.Online.Matchmaking", "public class Dependency { }");
        var social = Emit("GameFrameX.Capability.Online.Social", "public class Peer { public Dependency Value; }", matchmaking);
        // 编译自身 = 登记包（同层违规），直接引用出边也应被检查（Hotfix→Apps 合法，Social→Matchmaking 同层违规）。
        var diagnostics = await Analyze(Compile("GameFrameX.Hotfix", "public class Consumer { }", apps, social), new CapabilityPackageLayeringAnalyzer());
        var violation = Assert.Single(diagnostics);
        Assert.Equal("GFX0023", violation.Id);
        Assert.Contains("GameFrameX.Capability.Online.Social", violation.GetMessage());
    }

    [Fact]
    public async Task UnregisteredAssemblies_AreIgnored()
    {
        var external = Emit("Contoso.External", "public class External { }");
        var consumer = Emit("GameFrameX.Capability.Online.Match", "public class Peer { public External Value; }", external);
        Assert.Empty(await Analyze(Compile("GameFrameX.Tests", "public class Consumer { }", consumer), new CapabilityPackageLayeringAnalyzer()));
    }

    private static CSharpCompilation Compile(string name, string source, params MetadataReference[] references)
    {
        var compilation = CSharpCompilation.Create(name, new[] { CSharpSyntaxTree.ParseText(source, path: "Fixture.cs") },
            PlatformReferences.Concat(references), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        AssertNoErrors(compilation);
        return compilation;
    }

    private static MetadataReference Emit(string name, string source, params MetadataReference[] references)
    {
        using (var stream = new MemoryStream())
        {
            var result = Compile(name, source, references).Emit(stream);
            Assert.True(result.Success, string.Join(System.Environment.NewLine, result.Diagnostics));
            return MetadataReference.CreateFromImage(stream.ToArray());
        }
    }

    private static void AssertNoErrors(Compilation compilation)
    {
        Assert.Empty(compilation.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
    }

    private static async Task<ImmutableArray<Diagnostic>> Analyze(Compilation compilation, DiagnosticAnalyzer analyzer)
    {
        var diagnostics = await compilation.WithAnalyzers(ImmutableArray.Create(analyzer)).GetAnalyzerDiagnosticsAsync();
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AD0001");
        return diagnostics;
    }
}
