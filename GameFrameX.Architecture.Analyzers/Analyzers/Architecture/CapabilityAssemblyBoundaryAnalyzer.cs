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
/// GFX0018：能力注册及桥发布必须位于对应模块、组合根或测试夹具。
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CapabilityAssemblyBoundaryAnalyzer : DiagnosticAnalyzer
{
    private static readonly DiagnosticDescriptor Rule = new(
        "GFX0018",
        "Capability 装配或桥生命周期调用越界",
        "'{0}' 不允许在程序集 '{1}' 的当前类型中调用；{2}，Hotfix 请逐请求读取 holder.Current 并调用 lease 域契约",
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
        var registration = ResolveType(context.Compilation, CapabilityBoundaryFacts.HostingAbstractions, "CapabilityRegistrationContext");
        var services = ResolveType(context.Compilation, CapabilityBoundaryFacts.HostingAbstractions, "CapabilityServices");
        var builder = ResolveType(context.Compilation, CapabilityBoundaryFacts.HostingCore, "CapabilityHostBuilder");
        var assemblyLoader = ResolveType(context.Compilation, CapabilityBoundaryFacts.HostingCore, "CapabilityAssembly");
        var module = ResolveType(context.Compilation, CapabilityBoundaryFacts.HostingAbstractions, "ICapabilityModule");
        AddTargets(targets, registration, BoundaryKind.Registration, "AddService", "AddTickJob", "AddIntervalJob", "AddEventJob");
        AddTargets(targets, services, BoundaryKind.Registration, "AddService");
        AddTargets(targets, builder, BoundaryKind.Builder, "UseService", "AddModule", "Build");
        foreach (var name in new[] { "OnlineFriendDomainBridgeHolder", "OnlineMatchGameBridgeHolder", "OnlineMatchDomainBridgeHolder" })
        {
            var holder = ResolveType(context.Compilation, CapabilityBoundaryFacts.OnlineAbstractions, "Bridges." + name);
            AddTargets(targets, holder, BoundaryKind.Bridge, "Publish", "Retract");
        }

        if (targets.Count == 0)
        {
            return;
        }

        context.RegisterOperationAction(operationContext =>
        {
            var method = operationContext.Operation is IInvocationOperation invocation
                ? invocation.TargetMethod
                : ((IMethodReferenceOperation)operationContext.Operation).Method;
            if (!targets.TryGetValue(method.OriginalDefinition, out var kind)
                || IsAllowed(operationContext.ContainingSymbol, kind, registration, builder, assemblyLoader, module))
            {
                return;
            }

            var guidance = kind == BoundaryKind.Bridge
                ? "请将 Publish/Retract 移至 Launcher 启停 Wiring（测试可使用独立夹具）"
                : "请将注册移至 Capability 模块所属类型（含辅助方法），或将 builder 预置移至 Launcher / Capability hosting 扩展";
            operationContext.ReportDiagnostic(Diagnostic.Create(Rule, operationContext.Operation.Syntax.GetLocation(),
                method.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat),
                operationContext.Compilation.AssemblyName, guidance));
        }, OperationKind.Invocation, OperationKind.MethodReference);
    }

    private static bool IsAllowed(ISymbol caller, BoundaryKind kind, INamedTypeSymbol registration,
        INamedTypeSymbol builder, INamedTypeSymbol assemblyLoader, INamedTypeSymbol module)
    {
        var assembly = caller.ContainingAssembly.Identity.Name;
        if (CapabilityBoundaryFacts.IsCompositionOrTest(assembly))
        {
            return true;
        }

        if (kind == BoundaryKind.Bridge)
        {
            return false;
        }

        var type = caller as INamedTypeSymbol ?? caller.ContainingType;
        if (IsMechanismType(type, kind, registration, builder, assemblyLoader))
        {
            return true;
        }

        if (!CapabilityBoundaryFacts.IsImplementation(assembly))
        {
            return false;
        }

        if (ImplementsCapabilityModule(type, module))
        {
            return true;
        }

        // 组合根调用的能力包 hosting 扩展可预置 store/options；不把普通工具类一并豁免。
        return kind == BoundaryKind.Builder && IsBuilderHostExtension(caller, builder);
    }

    // 机制类型内部转交服务注册；不可用同名命名空间伪装这个身份。
    private static bool IsMechanismType(INamedTypeSymbol type, BoundaryKind kind, INamedTypeSymbol registration,
        INamedTypeSymbol builder, INamedTypeSymbol assemblyLoader)
    {
        if (SymbolEqualityComparer.Default.Equals(type, registration) || SymbolEqualityComparer.Default.Equals(type, builder))
        {
            return true;
        }

        return kind == BoundaryKind.Builder && SymbolEqualityComparer.Default.Equals(type, assemblyLoader);
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

    private static bool IsBuilderHostExtension(ISymbol caller, INamedTypeSymbol builder)
    {
        for (var current = caller; current != null; current = current.ContainingSymbol)
        {
            if (current is IMethodSymbol { IsExtensionMethod: true } extension
                && extension.Parameters.Length > 0
                && SymbolEqualityComparer.Default.Equals(extension.Parameters[0].Type, builder))
            {
                return true;
            }
        }

        return false;
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

    private enum BoundaryKind { Registration, Builder, Bridge }
}
