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

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace GameFrameX.Architecture.Analyzers;

/// <summary>
/// GFX0016：非 Mongo 实现层代码不得在类型声明面上引用 MongoDbService / MultiDbRegistry。
/// </summary>
/// <remarks>
/// <para>规则：GameFrameX.DataBase* 与 GameFrameX.NetWork.RemoteMessaging（Mongo 专属实现层）之外的程序集，
/// 其类型的声明面（基类、字段、属性、事件、方法签名）不得出现 MongoDbService（含其子类）
/// 或 MultiDbRegistry 引用。</para>
/// <para>原因：C159 统一入口铁律——数据库访问一律经 GameDb 门面（判重用 GameDb.Contains、
/// 控制库名用 GameDb.ControlDatabaseName），直接持有实现类型会绕开门面默认库语义，
/// 重演 C143a 门面绑定错位的 split-brain 缺陷。</para>
/// <para>豁免（声明面天然不可见，故不误报）：Launcher 组合根 GameDb.Init&lt;MongoDbService&gt;
/// 注册泛型实参与方法体内的 GameDb.As&lt;MongoDbService&gt;(name) 调用属于装配而非数据访问；
/// GlobalUsings 的命名空间 using 不产生符号引用。</para>
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class MongoImplementationAccessAnalyzer : SingleDiagnosticSymbolAnalyzer
{
    private static readonly DiagnosticDescriptor SDescriptor = new DiagnosticDescriptor(
        "GFX0016",
        "MongoDbService/MultiDbRegistry must not be referenced outside Mongo implementation layers",
        "Type '{0}' in assembly '{1}' references '{2}' in its declaration surface; database access must go through the GameDb facade",
        ArchitectureAnalyzerConstants.Category,
        DiagnosticSeverity.Error,
        true);

    protected override DiagnosticDescriptor Descriptor
    {
        get { return SDescriptor; }
    }

    protected override void AnalyzeNamedType(SymbolAnalysisContext context, ArchitectureSymbols symbols, INamedTypeSymbol type)
    {
        if (symbols.MongoDbService == null && symbols.MultiDbRegistry == null)
        {
            return;
        }

        var assemblyName = type.ContainingAssembly.Identity.Name;
        if (IsMongoImplementationLayer(assemblyName) || ArchitectureSymbolFacts.ShouldIgnoreAssembly(assemblyName))
        {
            return;
        }

        if (IsForbiddenReference(type.BaseType, symbols, out var baseReference))
        {
            ArchitectureSymbolFacts.Report(context, Descriptor, type, type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), assemblyName, baseReference);
            return;
        }

        foreach (var member in type.GetMembers())
        {
            ITypeSymbol? memberType = member switch
            {
                IFieldSymbol field => field.Type,
                IPropertySymbol property => property.Type,
                IEventSymbol eventSymbol => eventSymbol.Type,
                IMethodSymbol method => method.ReturnType,
                _ => null,
            };

            if (IsForbiddenReference(memberType, symbols, out var memberReference))
            {
                ArchitectureSymbolFacts.Report(context, Descriptor, type, type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), assemblyName, memberReference);
                return;
            }

            if (member is IMethodSymbol parameterOwner)
            {
                foreach (var parameter in parameterOwner.Parameters)
                {
                    if (IsForbiddenReference(parameter.Type, symbols, out var parameterReference))
                    {
                        ArchitectureSymbolFacts.Report(context, Descriptor, type, type.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), assemblyName, parameterReference);
                        return;
                    }
                }
            }
        }
    }

    private static bool IsMongoImplementationLayer(string assemblyName)
    {
        return assemblyName.StartsWith("GameFrameX.DataBase", StringComparison.Ordinal)
               || assemblyName == "GameFrameX.NetWork.RemoteMessaging";
    }

    private static bool IsForbiddenReference(ITypeSymbol? type, ArchitectureSymbols symbols, out string referenceName)
    {
        referenceName = string.Empty;
        if (type == null)
        {
            return false;
        }

        if (type is IArrayTypeSymbol arrayType)
        {
            return IsForbiddenReference(arrayType.ElementType, symbols, out referenceName);
        }

        if (!(type is INamedTypeSymbol namedType))
        {
            return false;
        }

        if (ArchitectureSymbolFacts.SymbolEquals(namedType, symbols.MongoDbService) || ArchitectureSymbolFacts.InheritsFrom(namedType, symbols.MongoDbService))
        {
            referenceName = namedType.Name;
            return true;
        }

        if (ArchitectureSymbolFacts.SymbolEquals(namedType, symbols.MultiDbRegistry))
        {
            referenceName = namedType.Name;
            return true;
        }

        foreach (var typeArgument in namedType.TypeArguments)
        {
            if (IsForbiddenReference(typeArgument, symbols, out referenceName))
            {
                return true;
            }
        }

        return false;
    }
}
