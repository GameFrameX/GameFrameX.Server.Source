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
using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Hosting.Core;
using GameFrameX.Capability.Online.Abstractions.Bridges;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace GameFrameX.Tests.Architecture;

public sealed class CapabilityBoundaryAnalyzerTests
{
    private static readonly ImmutableArray<MetadataReference> PlatformReferences = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
        .Split(Path.PathSeparator)
        .Where(path => !Path.GetFileName(path).StartsWith("GameFrameX.", StringComparison.Ordinal))
        .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path)).ToImmutableArray();

    private static readonly MetadataReference[] ContractReferences =
    {
        MetadataReference.CreateFromFile(typeof(OnlineFriendDomainBridgeHolder).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(CapabilityRegistrationContext).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(CapabilityHostBuilder).Assembly.Location)
    };

    [Theory]
    [InlineData("GameFrameX.Apps", "GameFrameX.Capability.Online.Social")]
    [InlineData("GameFrameX.Hotfix", "GameFrameX.Capability.Online.Match")]
    [InlineData("GameFrameX.Hotfix", "GameFrameX.Capability.Online.Matchmaking")]
    [InlineData("GameFrameX.Hotfix", "GameFrameX.Capability.Hosting.Core")]
    [InlineData("GameFrameX.Apps", "GameFrameX.Capability.Other.Abstractions")]
    public async Task Unused_direct_implementation_reference_is_rejected(string consumer, string implementation)
    {
        var reference = Emit(implementation, "public class Dependency { }");
        var compilation = Compile(consumer, "public class Consumer { }", reference);
        var diagnostic = Assert.Single(await Analyze(compilation, new CapabilityImplementationReferenceAnalyzer()));
        Assert.Equal("GFX0017", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains(consumer + " → " + implementation, diagnostic.GetMessage());
        Assert.Contains("Online.Abstractions", diagnostic.GetMessage());
        Assert.Contains("Launcher", diagnostic.GetMessage());
        Assert.True(diagnostic.Location.IsInSource);
    }

    [Theory]
    [InlineData("GameFrameX.Apps")]
    [InlineData("GameFrameX.Hotfix")]
    public async Task Transitive_reference_is_rejected_even_without_source_usage(string consumer)
    {
        var implementation = Emit("GameFrameX.Capability.Online.Social", "public class Implementation { }");
        var wrapper = Emit("Integration.Adapter", "public class Wrapper { public Implementation Value; }", implementation);
        // 实现元数据未直接传入编译；仍须从 wrapper 的 AssemblyRef 发现依赖身份。
        var diagnostic = Assert.Single(await Analyze(Compile(consumer, "public class Consumer { }", wrapper),
            new CapabilityImplementationReferenceAnalyzer()));
        Assert.Contains("Integration.Adapter → GameFrameX.Capability.Online.Social", diagnostic.GetMessage());
    }

    [Fact]
    public async Task Diamond_paths_report_each_implementation_once()
    {
        var implementation = Emit("GameFrameX.Capability.Online.Match", "public class Implementation { }");
        var left = Emit("Adapter.Left", "public class Left { public Implementation Value; }", implementation);
        var right = Emit("Adapter.Right", "public class Right { public Implementation Value; }", implementation);
        var diagnostics = await Analyze(Compile("GameFrameX.Hotfix", "public class Consumer { }", left, right, implementation),
            new CapabilityImplementationReferenceAnalyzer());
        Assert.Single(diagnostics);
    }

    [Theory]
    [InlineData("GameFrameX.Apps")]
    [InlineData("GameFrameX.Hotfix")]
    public async Task Actual_online_contract_closure_is_allowed(string consumer)
    {
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(OnlineFriendDomainBridgeHolder).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(CapabilityRegistrationContext).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(GameFrameX.Capability.Hosting.AdminApi.AdminActionRegistry).Assembly.Location)
        };
        Assert.Empty(await Analyze(Compile(consumer, "public class Consumer { }", references), new CapabilityImplementationReferenceAnalyzer()));
    }

    [Theory]
    [InlineData("GameFrameX.Launcher")]
    [InlineData("GameFrameX.Tests")]
    [InlineData("GameFrameX.Hotfix.Tests")]
    public async Task Composition_and_tests_can_reference_implementations(string consumer)
    {
        var reference = Emit("GameFrameX.Capability.Online.Social", "public class Implementation { }");
        Assert.Empty(await Analyze(Compile(consumer, "public class Consumer { }", reference), new CapabilityImplementationReferenceAnalyzer()));
    }

    [Fact]
    public async Task Empty_compilation_still_reports_project_level_reference()
    {
        var reference = Emit("GameFrameX.Capability.Online.Social", "public class Implementation { }");
        var compilation = Compile("GameFrameX.Apps", "public class Consumer { }", reference).RemoveAllSyntaxTrees();
        var diagnostic = Assert.Single(await Analyze(compilation, new CapabilityImplementationReferenceAnalyzer()));
        Assert.Equal(Location.None, diagnostic.Location);
    }

    [Theory]
    [InlineData("OnlineFriendDomainBridgeHolder")]
    [InlineData("OnlineMatchGameBridgeHolder")]
    [InlineData("OnlineMatchDomainBridgeHolder")]
    public async Task Actual_three_families_block_hotfix_publish_retract_and_delegate(string holder)
    {
        var source = $$"""
            using System;
            using Bridge = GameFrameX.Capability.Online.Abstractions.Bridges.{{holder}};
            namespace GameFrameX.Launcher.Online
            {
                public class PretendWiring
                {
                    public void Run()
                    {
                        Bridge.Publish(null, null);
                        Bridge.Retract();
                        Action later = Bridge.Retract;
                    }
                }
            }
            """;
        var diagnostics = await Analyze(Compile("GameFrameX.Hotfix", source, ContractReferences), new CapabilityAssemblyBoundaryAnalyzer());
        Assert.Equal(3, diagnostics.Length);
        Assert.All(diagnostics, diagnostic =>
        {
            Assert.Equal("GFX0018", diagnostic.Id);
            Assert.Contains("Launcher", diagnostic.GetMessage());
            Assert.Contains("holder.Current", diagnostic.GetMessage());
            var text = diagnostic.Location.SourceTree.GetText().ToString(diagnostic.Location.SourceSpan);
            Assert.StartsWith("Bridge.", text);
        });
    }

    [Theory]
    [InlineData("GameFrameX.Launcher")]
    [InlineData("GameFrameX.Tests")]
    [InlineData("GameFrameX.Hotfix.Tests")]
    public async Task Actual_three_family_lifecycle_wiring_is_allowed(string consumer)
    {
        var source = """
            using GameFrameX.Capability.Online.Abstractions.Bridges;
            public class Wiring
            {
                public void Run()
                {
                    OnlineFriendDomainBridgeHolder.Publish(null, null);
                    OnlineFriendDomainBridgeHolder.Retract();
                    OnlineMatchGameBridgeHolder.Publish(null, null);
                    OnlineMatchGameBridgeHolder.Retract();
                    OnlineMatchDomainBridgeHolder.Publish(null, null);
                    OnlineMatchDomainBridgeHolder.Retract();
                }
            }
            """;
        Assert.Empty(await Analyze(Compile(consumer, source, ContractReferences), new CapabilityAssemblyBoundaryAnalyzer()));
    }

    [Theory]
    [InlineData("GameFrameX.Apps")]
    [InlineData("GameFrameX.Hotfix")]
    public async Task Actual_hotfix_current_and_lease_domain_calls_are_allowed(string consumer)
    {
        var source = """
            using GameFrameX.Capability.Online.Abstractions.Bridges;
            public class Adapter
            {
                public void Run()
                {
                    var friend = OnlineFriendDomainBridgeHolder.Current;
                    var game = OnlineMatchGameBridgeHolder.Current;
                    var match = OnlineMatchDomainBridgeHolder.Current;
                    _ = friend.IsActive;
                    _ = friend.ListFriendsAsync(null);
                    _ = game.GetGameInfoAsync(null, 1, 1);
                    _ = match.GetStatusAsync(null);
                }
            }
            """;
        Assert.Empty(await Analyze(Compile(consumer, source, ContractReferences), new CapabilityAssemblyBoundaryAnalyzer()));
    }

    [Theory]
    [InlineData("GameFrameX.Hotfix")]
    [InlineData("GameFrameX.Apps")]
    [InlineData("GameFrameX.Hotfix.Tests.Evil")]
    [InlineData("ThirdParty.Tool")]
    public async Task Registration_and_builder_operations_require_authorized_location(string consumer)
    {
        var compilation = Compile(consumer, RegistrationSource, ContractReferences);
        var diagnostics = await Analyze(compilation, new CapabilityAssemblyBoundaryAnalyzer());
        Assert.Equal(8, diagnostics.Length);
        Assert.All(diagnostics, diagnostic =>
        {
            Assert.Contains("注册", diagnostic.GetMessage());
            Assert.True(diagnostic.Location.IsInSource);
        });
    }

    private const string RegistrationSource = """
        using System;
        using GameFrameX.Capability.Hosting.Abstractions;
        using GameFrameX.Capability.Hosting.Core;
        public class Wiring
        {
            public void Run(CapabilityRegistrationContext context, CapabilityServices services, CapabilityHostBuilder builder)
            {
                context.AddService(new object());
                services.AddService(new object());
                context.AddTickJob("tick", null);
                context.AddIntervalJob("interval", TimeSpan.FromMilliseconds(10), null);
                context.AddEventJob("event", null);
                builder.UseService(new object());
                builder.AddModule(null);
                builder.Build(null);
            }
        }
        """;

    [Theory]
    [InlineData("GameFrameX.Launcher")]
    [InlineData("GameFrameX.Tests")]
    [InlineData("GameFrameX.Hotfix.Tests")]
    public async Task Composition_registration_is_allowed(string consumer)
    {
        Assert.Empty(await Analyze(Compile(consumer, RegistrationSource, ContractReferences), new CapabilityAssemblyBoundaryAnalyzer()));
    }

    [Theory]
    [InlineData("GameFrameX.Capability.Online.Social")]
    [InlineData("GameFrameX.Capability.Online.Match")]
    [InlineData("GameFrameX.Capability.Online.Matchmaking")]
    public async Task Module_owned_register_helper_and_local_function_are_allowed(string implementation)
    {
        var source = """
            using GameFrameX.Capability.Hosting.Abstractions;
            public sealed class Module : ICapabilityModule
            {
                public CapabilityManifest Manifest => null;
                public void Register(CapabilityRegistrationContext context) => ResolveStore(context);
                private static void ResolveStore(CapabilityRegistrationContext context)
                {
                    context.AddService(new object());
                    void Local() => context.AddEventJob("test", null);
                    Local();
                }
                private sealed class NestedHelper
                {
                    public void Seed(CapabilityRegistrationContext context) => context.AddService(new object());
                }
            }
            """;
        Assert.Empty(await Analyze(Compile(implementation, source, ContractReferences), new CapabilityAssemblyBoundaryAnalyzer()));
        // 仅伪装 Register 方法或命名空间不足以获得模块权限。
        var ordinary = """
            using GameFrameX.Capability.Hosting.Abstractions;
            namespace GameFrameX.Launcher
            {
                public class NotAModule
                {
                    public void Register(CapabilityRegistrationContext context) => context.AddService(new object());
                }
            }
            """;
        Assert.Single(await Analyze(Compile(implementation, ordinary, ContractReferences), new CapabilityAssemblyBoundaryAnalyzer()));
    }

    [Fact]
    public async Task Module_interface_does_not_allow_hotfix_to_self_assemble()
    {
        var source = """
            using GameFrameX.Capability.Hosting.Abstractions;
            public class Module : ICapabilityModule
            {
                public CapabilityManifest Manifest => null;
                public void Register(CapabilityRegistrationContext context) => context.AddService(new object());
            }
            """;
        Assert.Single(await Analyze(Compile("GameFrameX.Hotfix", source, ContractReferences), new CapabilityAssemblyBoundaryAnalyzer()));
    }

    [Fact]
    public async Task Actual_capability_hosting_extension_can_preseed_builder()
    {
        var source = """
            using GameFrameX.Capability.Hosting.Core;
            public static class StoreExtensions
            {
                public static CapabilityHostBuilder UseStores(this CapabilityHostBuilder builder)
                {
                    builder.UseService(new object());
                    void Local() => builder.UseService("options");
                    Local();
                    return builder;
                }
            }
            """;
        Assert.Empty(await Analyze(Compile("GameFrameX.Capability.Online.Persistence", source, ContractReferences), new CapabilityAssemblyBoundaryAnalyzer()));
        Assert.Equal(2, (await Analyze(Compile("GameFrameX.Hotfix", source, ContractReferences), new CapabilityAssemblyBoundaryAnalyzer())).Length);
    }

    [Theory]
    [InlineData("CapabilityAssembly", 0)]
    [InlineData("ArbitraryHelper", 2)]
    public async Task Hosting_assembly_scan_entry_can_delegate_to_builder(string type, int diagnosticCount)
    {
        var source = $$"""
            namespace GameFrameX.Capability.Hosting.Core
            {
                public class CapabilityHostBuilder
                {
                    public void AddModule(object value) { }
                    public void Build(object value) { }
                }
                public class {{type}}
                {
                    public void Run(CapabilityHostBuilder builder)
                    {
                        builder.AddModule(null);
                        builder.Build(null);
                    }
                }
            }
            """;
        Assert.Equal(diagnosticCount, (await Analyze(Compile("GameFrameX.Capability.Hosting.Core", source),
            new CapabilityAssemblyBoundaryAnalyzer())).Length);
    }

    [Fact]
    public async Task Generic_registration_delegate_is_also_checked()
    {
        var source = """
            using System;
            using GameFrameX.Capability.Hosting.Abstractions;
            public class Wiring
            {
                public Action<object> Capture(CapabilityRegistrationContext context) => context.AddService<object>;
            }
            """;
        var diagnostic = Assert.Single(await Analyze(Compile("GameFrameX.Hotfix", source, ContractReferences), new CapabilityAssemblyBoundaryAnalyzer()));
        Assert.Equal("context.AddService<object>", diagnostic.Location.SourceTree.GetText().ToString(diagnostic.Location.SourceSpan));
    }

    [Fact]
    public async Task Generated_source_cannot_bypass_bridge_guard()
    {
        var source = """
            // <auto-generated/>
            public class GeneratedAdapter
            {
                public void Run() => GameFrameX.Capability.Online.Abstractions.Bridges.OnlineMatchDomainBridgeHolder.Retract();
            }
            """;
        Assert.Single(await Analyze(Compile("GameFrameX.Hotfix", source, ContractReferences), new CapabilityAssemblyBoundaryAnalyzer()));
    }

    [Fact]
    public async Task Same_named_methods_and_fake_framework_types_are_not_matched()
    {
        var source = """
            namespace GameFrameX.Capability.Online.Abstractions.Bridges
            {
                public static class OnlineFriendDomainBridgeHolder
                {
                    public static void Publish() { }
                    public static void Retract() { }
                }
            }
            namespace GameFrameX.Capability.Hosting.Abstractions
            {
                public class CapabilityRegistrationContext { public void AddService<T>(T value) { } }
            }
            public class Normal
            {
                public void Run()
                {
                    GameFrameX.Capability.Online.Abstractions.Bridges.OnlineFriendDomainBridgeHolder.Publish();
                    new GameFrameX.Capability.Hosting.Abstractions.CapabilityRegistrationContext().AddService(1);
                }
            }
            """;
        Assert.Empty(await Analyze(Compile("GameFrameX.Hotfix", source), new CapabilityAssemblyBoundaryAnalyzer()));
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
            Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
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
