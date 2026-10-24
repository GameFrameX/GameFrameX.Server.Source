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
using GameFrameX.Capability.Online.Abstractions.Scope;
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
        MetadataReference.CreateFromFile(typeof(OnlineScope).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(CapabilityRegistrationContext).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(CapabilityCompositionOptions).Assembly.Location)
    };

    [Theory]
    [InlineData("GameFrameX.Apps", "GameFrameX.Capability.Online.Social")]
    [InlineData("GameFrameX.Hotfix", "GameFrameX.Capability.Online.Match")]
    [InlineData("GameFrameX.Hotfix", "GameFrameX.Capability.Online.Matchmaking")]
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
        // C294：Online.Abstractions → Hosting.AdminApi → Hosting.Core 传递可达；Hosting 三包均为机制定义方（非能力实现），
        // 与 CapabilityBoundaryFacts.IsImplementation / IsMechanism 同一集合，契约闭包含全部三包即合法。
        var references = new[]
        {
            MetadataReference.CreateFromFile(typeof(OnlineScope).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(CapabilityRegistrationContext).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(CapabilityCompositionOptions).Assembly.Location),
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
    [InlineData("GameFrameX.Hotfix")]
    [InlineData("GameFrameX.Apps")]
    [InlineData("GameFrameX.Launcher")]
    [InlineData("GameFrameX.Capability.Online.Social")]
    [InlineData("ThirdParty.Tool")]
    public async Task Bridge_publish_outside_module_activation_is_rejected(string consumer)
    {
        var source = """
            using System;
            using GameFrameX.Capability.Hosting.Abstractions;
            public class Wiring
            {
                public void Run(CapabilityActivationContext context)
                {
                    context.Bridges.Publish<IFixtureContract>(null);
                    Action publish = () => context.Bridges.Publish<IFixtureContract>(null);
                }
            }
            public interface IFixtureContract { }
            """;
        var diagnostics = await Analyze(Compile(consumer, source, ContractReferences), new CapabilityAssemblyBoundaryAnalyzer());
        Assert.Equal(2, diagnostics.Length);
        Assert.All(diagnostics, diagnostic =>
        {
            Assert.Equal("GFX0018", diagnostic.Id);
            Assert.Contains("ActivateAsync", diagnostic.GetMessage());
            var text = diagnostic.Location.SourceTree.GetText().ToString(diagnostic.Location.SourceSpan);
            Assert.StartsWith("context.Bridges.Publish", text);
        });
    }

    [Theory]
    [InlineData("GameFrameX.Capability.Online.Social")]
    [InlineData("GameFrameX.Capability.Online.Match")]
    [InlineData("GameFrameX.Capability.Online.Matchmaking")]
    public async Task Bridge_publish_inside_module_activation_is_allowed(string implementation)
    {
        var source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using GameFrameX.Capability.Hosting.Abstractions;
            public sealed class Module : ICapabilityModule
            {
                public CapabilityManifest Manifest => null;
                public Task ActivateAsync(CapabilityActivationContext context, CancellationToken cancellationToken)
                {
                    context.Bridges.Publish<IFixtureContract>(null);
                    void Local() => context.Bridges.Publish<IFixtureContract>(null);
                    Local();
                    PublishLater(context.Bridges);
                    return Task.CompletedTask;
                }
                public void Register(CapabilityRegistrationContext context) { }
                public Task DeactivateAsync(CancellationToken cancellationToken) => Task.CompletedTask;
                private static void PublishLater(CapabilityBridgePublisher publisher) => publisher.Publish<IFixtureContract>(null);
            }
            public interface IFixtureContract { }
            """;
        Assert.Empty(await Analyze(Compile(implementation, source, ContractReferences), new CapabilityAssemblyBoundaryAnalyzer()));
    }

    [Fact]
    public async Task Bridge_publish_after_cached_activation_context_is_rejected()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using GameFrameX.Capability.Hosting.Abstractions;
            public sealed class Module : ICapabilityModule
            {
                private CapabilityActivationContext _cached;
                public CapabilityManifest Manifest => null;
                public Task ActivateAsync(CapabilityActivationContext context, CancellationToken cancellationToken)
                {
                    _cached = context;
                    return Task.CompletedTask;
                }
                public void Register(CapabilityRegistrationContext context) { }
                public Task DeactivateAsync(CancellationToken cancellationToken)
                {
                    _cached.Bridges.Publish<IFixtureContract>(null);
                    PublishWithoutParameter();
                    return Task.CompletedTask;
                }
                private void PublishWithoutParameter() => _cached.Bridges.Publish<IFixtureContract>(null);
            }
            public interface IFixtureContract { }
            """;
        var diagnostics = await Analyze(Compile("GameFrameX.Capability.Online.Social", source, ContractReferences), new CapabilityAssemblyBoundaryAnalyzer());
        Assert.Equal(2, diagnostics.Length);
        Assert.All(diagnostics, diagnostic => Assert.Contains("ActivateAsync", diagnostic.GetMessage()));
    }

    [Theory]
    [InlineData("GameFrameX.Apps", 0)]
    [InlineData("GameFrameX.Hotfix", 0)]
    [InlineData("GameFrameX.Launcher", 1)]
    [InlineData("GameFrameX.Capability.Online.Social", 1)]
    [InlineData("GameFrameX.Hotfix.Tests.Evil", 1)]
    [InlineData("ThirdParty.Tool", 1)]
    public async Task Bridge_acquire_is_consumer_side_only(string consumer, int diagnosticCount)
    {
        var source = """
            using GameFrameX.Capability.Hosting.Abstractions;
            public class Adapter
            {
                public string Run()
                {
                    return CapabilityBridge<IFixtureContract>.TryAcquire(out var lease) ? lease.Contract.Name : "absent";
                }
            }
            public interface IFixtureContract { string Name { get; } }
            """;
        var diagnostics = await Analyze(Compile(consumer, source, ContractReferences), new CapabilityAssemblyBoundaryAnalyzer());
        Assert.Equal(diagnosticCount, diagnostics.Length);
        Assert.All(diagnostics, diagnostic => Assert.Contains("Hotfix / Apps", diagnostic.GetMessage()));
    }

    [Theory]
    [InlineData("GameFrameX.Hotfix", 0)]
    [InlineData("GameFrameX.Apps", 1)]
    [InlineData("GameFrameX.Launcher", 1)]
    [InlineData("GameFrameX.Capability.Online.Social", 1)]
    public async Task Hook_attach_is_hotfix_only(string consumer, int diagnosticCount)
    {
        var source = """
            using GameFrameX.Capability.Hosting.Abstractions;
            public class HotfixLoad
            {
                public void Run()
                {
                    var lease = CapabilityHook<IFixtureHook>.Attach(new FixtureHook());
                    lease.Dispose();
                }
            }
            public interface IFixtureHook { }
            public sealed class FixtureHook : IFixtureHook { }
            """;
        var diagnostics = await Analyze(Compile(consumer, source, ContractReferences), new CapabilityAssemblyBoundaryAnalyzer());
        Assert.Equal(diagnosticCount, diagnostics.Length);
        Assert.All(diagnostics, diagnostic => Assert.Contains("Hotfix", diagnostic.GetMessage()));
    }

    [Theory]
    [InlineData("GameFrameX.Launcher", 0)]
    [InlineData("GameFrameX.Hotfix", 3)]
    [InlineData("GameFrameX.Apps", 3)]
    [InlineData("GameFrameX.Capability.Online.Social", 3)]
    [InlineData("ThirdParty.Tool", 3)]
    public async Task Host_composition_overrides_are_launcher_only(string consumer, int diagnosticCount)
    {
        var source = """
            using GameFrameX.Capability.Hosting.Core;
            public class CompositionEntry
            {
                public void Run(CapabilityCompositionOptions options)
                {
                    options.Configure<FixtureOptions>(configured => { });
                    options.Override<IFixtureContract>(resolver => null);
                    options.SetAdminApiPlaneFactory(null);
                }
            }
            public interface IFixtureContract { }
            public sealed class FixtureOptions { }
            """;
        var diagnostics = await Analyze(Compile(consumer, source, ContractReferences), new CapabilityAssemblyBoundaryAnalyzer());
        Assert.Equal(diagnosticCount, diagnostics.Length);
        Assert.All(diagnostics, diagnostic => Assert.Contains("Launcher", diagnostic.GetMessage()));
    }

    [Theory]
    [InlineData("GameFrameX.Hotfix")]
    [InlineData("GameFrameX.Apps")]
    [InlineData("GameFrameX.Launcher")]
    [InlineData("GameFrameX.Hotfix.Tests.Evil")]
    [InlineData("ThirdParty.Tool")]
    public async Task Registration_surface_requires_module_register_phase(string consumer)
    {
        var compilation = Compile(consumer, RegistrationSource, ContractReferences);
        var diagnostics = await Analyze(compilation, new CapabilityAssemblyBoundaryAnalyzer());
        Assert.Equal(12, diagnostics.Length);
        Assert.All(diagnostics, diagnostic =>
        {
            Assert.Contains("Register", diagnostic.GetMessage());
            Assert.True(diagnostic.Location.IsInSource);
        });
    }

    private const string RegistrationSource = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using GameFrameX.Capability.Hosting.Abstractions;
        public class Wiring
        {
            public void Run(CapabilityRegistrationContext context)
            {
                context.AddService(new object());
                context.AddService<object>(resolver => new object());
                context.AddService("name", new object());
                var service = context.GetService<object>();
                var optional = context.GetOptionalService<object>();
                var all = context.GetServices<object>();
                var options = context.GetOptions<FixtureOptions>();
                var handle = context.DeclareHook<IFixtureHook>(new FixtureHook());
                context.RequireHostService<IFixtureHostService>();
                context.Subscribe<FixtureEvent>("topic", received => { });
                context.AddTickJob("tick", cancellationToken => Task.CompletedTask);
                context.AddIntervalJob("interval", TimeSpan.FromMilliseconds(10), cancellationToken => Task.CompletedTask);
            }
        }
        public interface IFixtureHook { }
        public interface IFixtureHostService { }
        public sealed class FixtureHook : IFixtureHook { }
        public sealed class FixtureOptions { }
        public sealed class FixtureEvent { }
        """;

    [Theory]
    [InlineData("GameFrameX.Capability.Online.Social")]
    [InlineData("GameFrameX.Capability.Online.Match")]
    [InlineData("GameFrameX.Capability.Online.Matchmaking")]
    public async Task Module_register_surface_with_helpers_is_allowed(string implementation)
    {
        var source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using GameFrameX.Capability.Hosting.Abstractions;
            public sealed class Module : ICapabilityModule
            {
                public CapabilityManifest Manifest => null;
                public void Register(CapabilityRegistrationContext context)
                {
                    context.AddService(new object());
                    var service = context.GetService<object>();
                    var optional = context.GetOptionalService<object>();
                    var options = context.GetOptions<FixtureOptions>();
                    var handle = context.DeclareHook<IFixtureHook>(new FixtureHook());
                    context.RequireHostService<IFixtureHostService>();
                    context.Subscribe<FixtureEvent>("topic", received => { });
                    context.AddTickJob("tick", cancellationToken => Task.CompletedTask);
                    ResolveStore(context);
                    void Local() => context.AddService(new object());
                    Local();
                }
                public Task ActivateAsync(CapabilityActivationContext context, CancellationToken cancellationToken) => Task.CompletedTask;
                public Task DeactivateAsync(CancellationToken cancellationToken) => Task.CompletedTask;
                private static void ResolveStore(CapabilityRegistrationContext context) => context.AddService("nested", new object());
                private sealed class NestedHelper
                {
                    public void Seed(CapabilityRegistrationContext context) => context.AddService(new object());
                }
            }
            public interface IFixtureHook { }
            public interface IFixtureHostService { }
            public sealed class FixtureHook : IFixtureHook { }
            public sealed class FixtureOptions { }
            public sealed class FixtureEvent { }
            """;
        Assert.Empty(await Analyze(Compile(implementation, source, ContractReferences), new CapabilityAssemblyBoundaryAnalyzer()));
        // 仅实现 ICapabilityModule 的类型获得 Register 期权限；伪装 Register 方法或命名空间不足以获得。
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
            using System.Threading;
            using System.Threading.Tasks;
            using GameFrameX.Capability.Hosting.Abstractions;
            public class Module : ICapabilityModule
            {
                public CapabilityManifest Manifest => null;
                public void Register(CapabilityRegistrationContext context) => context.AddService(new object());
                public Task ActivateAsync(CapabilityActivationContext context, CancellationToken cancellationToken) => Task.CompletedTask;
                public Task DeactivateAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            }
            """;
        Assert.Single(await Analyze(Compile("GameFrameX.Hotfix", source, ContractReferences), new CapabilityAssemblyBoundaryAnalyzer()));
    }

    [Fact]
    public async Task Register_api_after_cached_context_is_rejected()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using GameFrameX.Capability.Hosting.Abstractions;
            public sealed class Module : ICapabilityModule
            {
                private CapabilityRegistrationContext _cached;
                public CapabilityManifest Manifest => null;
                public void Register(CapabilityRegistrationContext context)
                {
                    _cached = context;
                    RegisterLater();
                }
                public Task ActivateAsync(CapabilityActivationContext context, CancellationToken cancellationToken) => Task.CompletedTask;
                public Task DeactivateAsync(CancellationToken cancellationToken) => Task.CompletedTask;
                private void RegisterLater() => _cached.AddService(new object());
            }
            """;
        Assert.Single(await Analyze(Compile("GameFrameX.Capability.Online.Social", source, ContractReferences), new CapabilityAssemblyBoundaryAnalyzer()));
    }

    [Theory]
    [InlineData("GameFrameX.Capability.Hosting.Core")]
    [InlineData("GameFrameX.Capability.Hosting.AdminApi")]
    public async Task Mechanism_assemblies_own_their_api_surface(string mechanism)
    {
        var compilation = Compile(mechanism, RegistrationSource + BridgeConsumerSource, HostingAbstractionsReference);
        Assert.Empty(await Analyze(compilation, new CapabilityAssemblyBoundaryAnalyzer()));
    }

    [Theory]
    [InlineData("GameFrameX.Tests")]
    [InlineData("GameFrameX.Hotfix.Tests")]
    public async Task Test_fixtures_may_exercise_every_phase(string consumer)
    {
        var compilation = Compile(consumer, RegistrationSource + BridgeConsumerSource + HostCompositionSource, ContractReferences);
        Assert.Empty(await Analyze(compilation, new CapabilityAssemblyBoundaryAnalyzer()));
    }

    private const string BridgeConsumerSource = """
        public class BridgeAdapter
        {
            public void Run(GameFrameX.Capability.Hosting.Abstractions.CapabilityActivationContext context)
            {
                context.Bridges.Publish<IFixtureContract>(null);
                GameFrameX.Capability.Hosting.Abstractions.CapabilityBridge<IFixtureContract>.TryAcquire(out var lease);
                GameFrameX.Capability.Hosting.Abstractions.CapabilityHook<IFixtureHook>.Attach(new FixtureHook());
            }
        }
        public interface IFixtureContract { }
        """;

    private const string HostCompositionSource = """
        public class CompositionEntry
        {
            public void Run(GameFrameX.Capability.Hosting.Core.CapabilityCompositionOptions options)
            {
                options.Configure<FixtureOptions>(configured => { });
                options.Override<IFixtureContract>(resolver => null);
                options.SetAdminApiPlaneFactory(null);
            }
        }
        """;

    private static readonly MetadataReference[] HostingAbstractionsReference =
    {
        MetadataReference.CreateFromFile(typeof(CapabilityRegistrationContext).Assembly.Location)
    };

    [Fact]
    public async Task Generic_registration_delegate_is_also_checked()
    {
        var source = """
            using System;
            using GameFrameX.Capability.Hosting.Abstractions;
            public class Wiring
            {
                public Func<object> Capture(CapabilityRegistrationContext context) => context.GetService<object>;
            }
            """;
        var diagnostic = Assert.Single(await Analyze(Compile("GameFrameX.Hotfix", source, ContractReferences), new CapabilityAssemblyBoundaryAnalyzer()));
        Assert.Equal("context.GetService<object>", diagnostic.Location.SourceTree.GetText().ToString(diagnostic.Location.SourceSpan));
    }

    [Fact]
    public async Task Generated_source_cannot_bypass_bridge_guard()
    {
        var source = """
            // <auto-generated/>
            public class GeneratedAdapter
            {
                public void Run() => GameFrameX.Capability.Hosting.Abstractions.CapabilityBridge<IFixtureContract>.TryAcquire(out _);
            }
            public interface IFixtureContract { }
            """;
        Assert.Single(await Analyze(Compile("GameFrameX.Launcher", source, ContractReferences), new CapabilityAssemblyBoundaryAnalyzer()));
    }

    [Fact]
    public async Task Same_named_methods_and_fake_framework_types_are_not_matched()
    {
        var source = """
            namespace GameFrameX.Capability.Hosting.Abstractions
            {
                public class CapabilityRegistrationContext { public void AddService<T>(T value) { } }
                public sealed class CapabilityBridgePublisher { public void Publish<TContract>(TContract contract) { } }
                public class CapabilityActivationContext { public CapabilityBridgePublisher Bridges { get; } }
                public static class CapabilityBridge<TContract> { public static bool TryAcquire(out object lease) { lease = null; return false; } }
                public static class CapabilityHook<TContract> { public static object Attach(TContract implementation) => null; }
            }
            namespace GameFrameX.Capability.Hosting.Core
            {
                public sealed class CapabilityCompositionOptions
                {
                    public void Configure<TOptions>(System.Action<TOptions> configure) { }
                    public void Override<TService>(TService instance) { }
                }
            }
            public class Normal
            {
                public void Run(GameFrameX.Capability.Hosting.Abstractions.CapabilityActivationContext context)
                {
                    context.Bridges.Publish<object>(null);
                    GameFrameX.Capability.Hosting.Abstractions.CapabilityRegistrationContext capture = null;
                    capture.AddService(1);
                    GameFrameX.Capability.Hosting.Abstractions.CapabilityBridge<object>.TryAcquire(out _);
                    GameFrameX.Capability.Hosting.Abstractions.CapabilityHook<object>.Attach(null);
                    GameFrameX.Capability.Hosting.Core.CapabilityCompositionOptions options = null;
                    options.Configure<object>(configured => { });
                    options.Override(new object());
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
