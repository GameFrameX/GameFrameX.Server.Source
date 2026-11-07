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
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace GameFrameX.Architecture.Analyzers;

/// <summary>
/// GFX0022：禁止新增静态组合根——静态类不得承载装配状态（<c>CapabilityRuntime</c>/<c>CapabilityHost</c>
/// 的静态字段或属性）或组合入口（静态调用 <c>CapabilityComposition.Compose</c>）。
/// </summary>
/// <remarks>
/// <para>规则：机制包（CapabilityComposition 所在程序集）与测试程序集之外的静态类，命中以下任一形态即报错：
/// ① 声明 <c>CapabilityRuntime</c> 或 <c>CapabilityHost</c> 类型的静态字段 / 属性（含私有）——装配状态静态化；
/// ② 任意成员方法体内调用 <c>CapabilityComposition.Compose</c>（两个重载均算）——静态组合入口。</para>
/// <para>原因：组合根唯一红线——一切装配经机制组合器（<c>CapabilityComposition</c> → CapabilityHostBuilder
/// 统一拓扑定序）由实例组合根驱动；静态组合根会再造第二套启停定序与进程级静态装配状态，
/// 重演双装配并存（旧链固定序列 + 新链装配）两套所有权分叉的缺陷形态。</para>
/// <para>豁免：机制程序集自身（<c>CapabilityComposition</c> 是无状态的纯工厂入口）；测试程序集
///（<see cref="ArchitectureSymbolFacts.ShouldIgnoreAssembly"/>）；实例类型（含实例字段持有
/// <c>CapabilityRuntime</c> 与实例方法调用 <c>Compose</c>——合法的实例组合根形态）；
/// 静态方法仅收发 <c>CapabilityRuntime</c> 参数 / 返回值不构成组合根（无状态静态转发合法）。</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StaticCompositionRootAnalyzer : DiagnosticAnalyzer
{
    private const string CapabilityCompositionMetadataName = "GameFrameX.Capability.Hosting.Core.CapabilityComposition";
    private const string CapabilityRuntimeMetadataName = "GameFrameX.Capability.Hosting.Core.CapabilityRuntime";
    private const string CapabilityHostMetadataName = "GameFrameX.Capability.Hosting.Core.CapabilityHost";

    private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
        "GFX0022",
        "Static composition roots are forbidden: capability assembly must ride the single composition root",
        "Static type '{0}' in assembly '{1}' acts as a static composition root ({2}); compose through CapabilityComposition from an instance composition root instead",
        ArchitectureAnalyzerConstants.Category,
        DiagnosticSeverity.Error,
        true);

    /// <summary>机制程序集名（其内的 CapabilityComposition 是无状态纯工厂入口，白名单豁免）。</summary>
    private const string MechanismAssemblyName = "GameFrameX.Capability.Hosting.Core";
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
    {
        get { return ImmutableArray.Create(Rule); }
    }

    /// <summary>
    /// 注册静态类符号分析（装配状态面）与调用点分析（组合入口面）。
    /// </summary>
    /// <remarks>
    /// Registers the static-type symbol analysis (the assembly-state face) and the invocation
    /// analysis (the compose-entry face); the mechanism assembly and test assemblies are exempt.
    /// </remarks>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(compilationContext =>
        {
            var compositionType = compilationContext.Compilation.GetTypeByMetadataName(CapabilityCompositionMetadataName);
            var runtimeType = compilationContext.Compilation.GetTypeByMetadataName(CapabilityRuntimeMetadataName);
            var hostType = compilationContext.Compilation.GetTypeByMetadataName(CapabilityHostMetadataName);
            if (compositionType == null && runtimeType == null && hostType == null)
            {
                // 本编译未引用机制包：规则无对象。
                return;
            }

            RegisterStaticAssemblyStateAnalysis(compilationContext, runtimeType, hostType);

            if (compositionType == null)
            {
                return;
            }

            RegisterComposeEntryAnalysis(compilationContext, compositionType);
        });
    }

    /// <summary>
    /// 注册装配状态面分析：非豁免程序集的静态类若声明 <c>CapabilityRuntime</c>/<c>CapabilityHost</c>
    /// 类型的静态字段或属性（含私有），按首个命中成员上报 GFX0022。
    /// </summary>
    private static void RegisterStaticAssemblyStateAnalysis(CompilationStartAnalysisContext compilationContext, INamedTypeSymbol runtimeType, INamedTypeSymbol hostType)
    {
        compilationContext.RegisterSymbolAction(symbolContext =>
        {
            var type = (INamedTypeSymbol)symbolContext.Symbol;
            if (!IsReportableStaticType(type, out var assemblyName))
            {
                return;
            }

            ReportStaticAssemblyStateMembers(symbolContext, type, assemblyName, runtimeType, hostType);
        }, SymbolKind.NamedType);
    }

    /// <summary>静态类候选判定：非误差/非隐式声明，且所在程序集不在机制与测试豁免集合内。</summary>
    private static bool IsReportableStaticType(INamedTypeSymbol type, out string assemblyName)
    {
        assemblyName = null;
        if (!type.IsStatic || type.TypeKind == TypeKind.Error || type.IsImplicitlyDeclared)
        {
            return false;
        }

        assemblyName = type.ContainingAssembly.Identity.Name;
        return assemblyName != MechanismAssemblyName && !ArchitectureSymbolFacts.ShouldIgnoreAssembly(assemblyName);
    }

    /// <summary>逐成员检查装配状态：首个命中 CapabilityRuntime/CapabilityHost 的字段或属性上报后停止。</summary>
    private static void ReportStaticAssemblyStateMembers(SymbolAnalysisContext symbolContext, INamedTypeSymbol type, string assemblyName, INamedTypeSymbol runtimeType, INamedTypeSymbol hostType)
    {
        foreach (var member in type.GetMembers())
        {
            var memberType = GetDeclaredMemberType(member);
            if (memberType == null)
            {
                continue;
            }

            var reason = DescribeAssemblyStateMemberType(memberType, runtimeType, hostType);
            if (reason != null)
            {
                symbolContext.ReportDiagnostic(Diagnostic.Create(Rule, member.Locations[0], type.Name, assemblyName, reason));
                return;
            }
        }
    }

    /// <summary>提取字段/属性声明类型；其余成员种类返回 null。</summary>
    private static ITypeSymbol GetDeclaredMemberType(ISymbol member)
    {
        return member switch
        {
            IFieldSymbol field => field.Type,
            IPropertySymbol property => property.Type,
            _ => null,
        };
    }

    /// <summary>命中机制装配状态类型时给出诊断 reason 文案，否则返回 null。</summary>
    private static string DescribeAssemblyStateMemberType(ITypeSymbol memberType, INamedTypeSymbol runtimeType, INamedTypeSymbol hostType)
    {
        if (SymbolEqualityComparer.Default.Equals(memberType, runtimeType))
        {
            return "static member holding CapabilityRuntime";
        }

        if (SymbolEqualityComparer.Default.Equals(memberType, hostType))
        {
            return "static member holding CapabilityHost";
        }

        return null;
    }

    /// <summary>
    /// 注册组合入口面分析：非豁免程序集的静态类成员方法内调用 <c>CapabilityComposition.Compose</c>（两个重载均算）
    /// 时按调用点上报 GFX0022。
    /// </summary>
    private static void RegisterComposeEntryAnalysis(CompilationStartAnalysisContext compilationContext, INamedTypeSymbol compositionType)
    {
        compilationContext.RegisterOperationAction(operationContext =>
        {
            var invocation = (Microsoft.CodeAnalysis.Operations.IInvocationOperation)operationContext.Operation;
            if (!IsCapabilityCompositionEntryPoint(invocation.TargetMethod, compositionType))
            {
                return;
            }

            var callerType = operationContext.ContainingSymbol?.ContainingType;
            if (callerType == null || !callerType.IsStatic || callerType.TypeKind == TypeKind.Error)
            {
                return;
            }

            var assemblyName = callerType.ContainingAssembly.Identity.Name;
            if (assemblyName == MechanismAssemblyName || ArchitectureSymbolFacts.ShouldIgnoreAssembly(assemblyName))
            {
                return;
            }

            operationContext.ReportDiagnostic(Diagnostic.Create(Rule, invocation.Syntax.GetLocation(), callerType.Name, assemblyName, "static entry invoking CapabilityComposition.Compose"));
        }, OperationKind.Invocation);
    }

    /// <summary>目标方法判定：名为 Compose 且声明于 CapabilityComposition 类型（两个重载均命中）。</summary>
    private static bool IsCapabilityCompositionEntryPoint(IMethodSymbol targetMethod, INamedTypeSymbol compositionType)
    {
        if (targetMethod == null || !string.Equals(targetMethod.Name, "Compose", System.StringComparison.Ordinal))
        {
            return false;
        }

        return SymbolEqualityComparer.Default.Equals(targetMethod.ContainingType, compositionType);
    }
}
