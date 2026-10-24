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
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace GameFrameX.Architecture.Analyzers;

/// <summary>
/// GFX0018：Capability 机制 API 必须在其生命周期阶段与所属程序集内调用。
/// </summary>
/// <remarks>
/// 符号按泛型定义（<c>OriginalDefinition</c>）判定，任意新增桥契约 / 钩子契约自动受保护，不维护名字清单：
/// <list type="bullet">
/// <item><description>Register 期 API（<c>CapabilityRegistrationContext</c> 的注册 / 解析 / <c>GetOptions</c> / <c>DeclareHook</c> / <c>RequireHostService</c>）
/// 只在能力包模块内、持有 <c>CapabilityRegistrationContext</c> 参数的 Register 及其辅助方法中合法；</description></item>
/// <item><description><c>Bridges.Publish&lt;&gt;</c> 只在能力包模块内、持有激活期上下文的 <c>ActivateAsync</c> 及其辅助方法中合法；</description></item>
/// <item><description><c>CapabilityBridge&lt;&gt;.TryAcquire</c> 只在 Hotfix / Apps 消费侧合法；<c>CapabilityHook&lt;&gt;.Attach</c> 只在 Hotfix 合法；</description></item>
/// <item><description><c>CapabilityCompositionOptions.Override&lt;&gt;</c> / <c>Configure&lt;&gt;</c> / <c>SetAdminApiPlaneFactory</c> 只在宿主组合入口（Launcher）合法。</description></item>
/// </list>
/// 维护约束：Hosting 机制包是这些 API 的定义方，内部转交不受限；测试工程（精确程序集名）可构造任意阶段夹具。
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CapabilityAssemblyBoundaryAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        "GFX0018",
        "Capability 机制 API 调用越界",
        "'{0}' 不允许在程序集 '{1}' 的当前位置调用；{2}",
        ArchitectureAnalyzerConstants.Category,
        DiagnosticSeverity.Error,
        true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);
        context.RegisterCompilationStartAction(RegisterChecks);
    }

    private static void RegisterChecks(CompilationStartAnalysisContext context)
    {
        var targets = new Dictionary<IMethodSymbol, BoundaryKind>(SymbolEqualityComparer.Default);
        var compilation = context.Compilation;
        var registration = ResolveType(compilation, CapabilityBoundaryFacts.HostingAbstractions, "CapabilityRegistrationContext");
        var activation = ResolveType(compilation, CapabilityBoundaryFacts.HostingAbstractions, "CapabilityActivationContext");
        var publisher = ResolveType(compilation, CapabilityBoundaryFacts.HostingAbstractions, "CapabilityBridgePublisher");
        var bridge = ResolveType(compilation, CapabilityBoundaryFacts.HostingAbstractions, "CapabilityBridge`1");
        var hook = ResolveType(compilation, CapabilityBoundaryFacts.HostingAbstractions, "CapabilityHook`1");
        var compositionOptions = ResolveType(compilation, CapabilityBoundaryFacts.HostingCore, "CapabilityCompositionOptions");
        var module = ResolveType(compilation, CapabilityBoundaryFacts.HostingAbstractions, "ICapabilityModule");
        AddTargets(targets, registration, BoundaryKind.Registration, "AddService", "AddTickJob", "AddIntervalJob", "Subscribe",
            "GetService", "GetOptionalService", "GetServices", "GetOptions", "DeclareHook", "RequireHostService");
        AddTargets(targets, publisher, BoundaryKind.BridgePublish, "Publish");
        AddTargets(targets, bridge, BoundaryKind.BridgeAcquire, "TryAcquire");
        AddTargets(targets, hook, BoundaryKind.HookAttach, "Attach");
        AddTargets(targets, compositionOptions, BoundaryKind.HostComposition, "Configure", "Override", "SetAdminApiPlaneFactory");
        if (targets.Count == 0)
        {
            return;
        }

        var registrationPhase = new[] { registration };
        var activationPhase = new[] { activation, publisher };
        context.RegisterOperationAction(operationContext =>
        {
            var method = operationContext.Operation is IInvocationOperation invocation
                ? invocation.TargetMethod
                : ((IMethodReferenceOperation)operationContext.Operation).Method;
            if (!targets.TryGetValue(method.OriginalDefinition, out var kind))
            {
                return;
            }

            var phase = kind == BoundaryKind.BridgePublish ? activationPhase : registrationPhase;
            if (IsAllowed(operationContext.ContainingSymbol, kind, module, phase))
            {
                return;
            }

            operationContext.ReportDiagnostic(Diagnostic.Create(Rule, operationContext.Operation.Syntax.GetLocation(),
                method.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                operationContext.Compilation.AssemblyName, Guidance(kind)));
        }, OperationKind.Invocation, OperationKind.MethodReference);
    }

    private static bool IsAllowed(ISymbol caller, BoundaryKind kind, INamedTypeSymbol module, INamedTypeSymbol[] phase)
    {
        var assembly = caller.ContainingAssembly.Identity.Name;
        if (CapabilityBoundaryFacts.IsTest(assembly) || CapabilityBoundaryFacts.IsMechanism(assembly))
        {
            return true;
        }

        switch (kind)
        {
            case BoundaryKind.BridgeAcquire:
                return assembly == ArchitectureAnalyzerConstants.AppsAssembly || assembly == ArchitectureAnalyzerConstants.HotfixAssembly;
            case BoundaryKind.HookAttach:
                return assembly == ArchitectureAnalyzerConstants.HotfixAssembly;
            case BoundaryKind.HostComposition:
                return assembly == CapabilityBoundaryFacts.Launcher;
            default:
                if (!CapabilityBoundaryFacts.IsImplementation(assembly))
                {
                    return false;
                }

                return IsModulePhaseCall(caller, module, phase);
        }
    }

    // 模块阶段身份 = 调用点外层方法链上存在「持有该阶段上下文参数 + 所属类型实现 ICapabilityModule」的方法；
    // lambda / 局部函数向上追溯到 Register / ActivateAsync 本体，把上下文缓存到字段后在其它方法调用则不具备阶段身份。
    private static bool IsModulePhaseCall(ISymbol caller, INamedTypeSymbol module, INamedTypeSymbol[] phase)
    {
        for (var current = caller; current != null && current is not INamedTypeSymbol; current = current.ContainingSymbol)
        {
            if (current is IMethodSymbol method
                && method.Parameters.Any(parameter => phase.Any(type => type != null && SymbolEqualityComparer.Default.Equals(parameter.Type, type)))
                && ImplementsCapabilityModule(method.ContainingType, module))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ImplementsCapabilityModule(INamedTypeSymbol type, INamedTypeSymbol module)
    {
        for (var current = type; current != null; current = current.ContainingType)
        {
            if (module != null && current.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, module)))
            {
                return true;
            }
        }

        return false;
    }

    private static string Guidance(BoundaryKind kind)
    {
        switch (kind)
        {
            case BoundaryKind.BridgePublish:
                return "桥发布仅限能力包模块的 ActivateAsync（持有 CapabilityActivationContext 的激活期方法）经 context.Bridges.Publish 调用，撤销由宿主停止期自动完成";
            case BoundaryKind.BridgeAcquire:
                return "桥租约仅限 Hotfix / Apps 消费侧经 CapabilityBridge<T>.TryAcquire 获取，能力包与宿主请经服务注册面取用";
            case BoundaryKind.HookAttach:
                return "钩子挂载仅限 Hotfix 程序集经 CapabilityHook<T>.Attach 调用";
            case BoundaryKind.HostComposition:
                return "Override / Configure / SetAdminApiPlaneFactory 仅限宿主组合入口（Launcher）调用";
            default:
                return "Register 期 API 仅限能力包模块（实现 ICapabilityModule）的 Register 及持有 CapabilityRegistrationContext 参数的模块内辅助方法调用";
        }
    }

    private static INamedTypeSymbol ResolveType(Compilation compilation, string assembly, string relativeName)
    {
        var type = compilation.GetTypeByMetadataName(assembly + "." + relativeName);
        return type?.ContainingAssembly.Identity.Name == assembly ? type : null;
    }

    private static void AddTargets(Dictionary<IMethodSymbol, BoundaryKind> targets, INamedTypeSymbol type,
        BoundaryKind kind, params string[] names)
    {
        if (type == null)
        {
            return;
        }

        foreach (var method in names.SelectMany(name => type.GetMembers(name).OfType<IMethodSymbol>()))
        {
            targets[method.OriginalDefinition] = kind;
        }
    }

    private enum BoundaryKind { Registration, BridgePublish, BridgeAcquire, HookAttach, HostComposition }
}
