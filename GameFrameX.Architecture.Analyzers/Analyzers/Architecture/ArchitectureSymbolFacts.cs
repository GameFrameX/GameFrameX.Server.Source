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

#nullable enable

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace GameFrameX.Architecture.Analyzers;

/// <summary>
/// 架构分析器共用的符号判断工具方法。
/// </summary>
/// <remarks>
/// 提供 类型继承、接口实现、属性标注、程序集过滤 等判断逻辑，
/// 供所有分析器在 AnalyzeNamedType 中调用。
/// </remarks>
internal static class ArchitectureSymbolFacts
{
    /// <summary>
    /// 判断类型是否为需要分析的候选类。
    /// 排除非 Class 类型（struct/enum/interface 等）和编译器隐式生成的类型（如匿名类、闭包类）。
    /// </summary>
    public static bool IsCandidateClass(INamedTypeSymbol type)
    {
        return type.TypeKind == TypeKind.Class && !type.IsImplicitlyDeclared;
    }

    /// <summary>
    /// 判断程序集是否应被所有分析器跳过。
    /// </summary>
    /// <remarks>
    /// 跳过规则：
    ///   - GameFrameX.Proto  ：Proto 消息定义，不含业务逻辑。
    ///   - GameFrameX.Config ：配置定义，不含业务逻辑。
    ///   - *.Tests / *.Tests.*：单元测试程序集，测试中可能需要违反架构规则进行 mock。
    /// </remarks>
    public static bool ShouldIgnoreAssembly(string assemblyName)
    {
        return assemblyName == "GameFrameX.Proto"
               || assemblyName == "GameFrameX.Config"
               || assemblyName.EndsWith(".Tests", StringComparison.Ordinal)
               || assemblyName.Contains(".Tests.", StringComparison.Ordinal);
    }

    /// <summary>
    /// 判断类型是否继承自指定基类（沿继承链向上搜索，不含 object）。
    /// </summary>
    public static bool InheritsFrom(INamedTypeSymbol type, INamedTypeSymbol? expectedBaseType)
    {
        if (expectedBaseType == null)
        {
            return false;
        }

        for (var current = type.BaseType; current != null; current = current.BaseType)
        {
            if (SymbolEquals(current, expectedBaseType))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 判断类型是否实现了指定接口（含所有已声明的接口，包括继承的）。
    /// </summary>
    public static bool Implements(INamedTypeSymbol type, INamedTypeSymbol? expectedInterface)
    {
        if (expectedInterface == null)
        {
            return false;
        }

        foreach (var candidate in type.AllInterfaces)
        {
            if (SymbolEquals(candidate, expectedInterface))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 判断类型是否标注了指定特性（Attribute）。
    /// </summary>
    public static bool HasAttribute(INamedTypeSymbol type, INamedTypeSymbol? expectedAttribute)
    {
        if (expectedAttribute == null)
        {
            return false;
        }

        foreach (var attribute in type.GetAttributes())
        {
            var attributeClass = attribute.AttributeClass;
            if (attributeClass != null && SymbolEquals(attributeClass, expectedAttribute))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 判断两个类型符号是否代表同一个类型（比较 OriginalDefinition 以忽略泛型参数差异）。
    /// </summary>
    public static bool SymbolEquals(INamedTypeSymbol? left, INamedTypeSymbol? right)
    {
        if (left == null || right == null)
        {
            return false;
        }

        return SymbolEqualityComparer.Default.Equals(left.OriginalDefinition, right.OriginalDefinition);
    }

    /// <summary>
    /// 判断类型是否为源生成器自动生成的 ComponentAgentWrapper。
    /// </summary>
    /// <remarks>
    /// 三段判定（按可信度顺序）：
    ///   1. 类型带 System.CodeDom.Compiler.GeneratedCodeAttribute 且 tool 名精确等于
    ///      WrapperGenerator 标识（<see cref="ArchitectureAnalyzerConstants.WrapperGeneratorToolName"/>）——
    ///      伪造 tool 名属显式声明生成代码（主动退出架构分析），与命名意外冒充不同源；
    ///   2. 任一声明位置位于 ".g.cs" 源文件——源生成器产物按 Roslyn 约定的文件后缀落地；
    ///   3. 过渡期兜底：所属程序集为 GameFrameX.Hotfix 且名称以 "ComponentAgentWrapper" 结尾——
    ///      覆盖标记与文件后缀信号引入前的存量生成形态。兜底只对 Hotfix 程序集生效：
    ///      Hotfix 之外手写同名后缀类不豁免，GFX0005/GFX0014 无条件生效。
    /// 引擎边界：分析器以 GeneratedCodeAnalysisFlags.None 初始化时，Roslyn 对带任意 GeneratedCodeAttribute
    /// 或位于生成文件（.g.cs 等）的符号在进入符号回调前即抑制——前两段在当前引擎配置下为纵深防御
    /// （引擎/宿主行为变化时仍正确），可观测豁免主要来自引擎抑制与第三段。
    /// The exemption applies only to genuine generator output; a hand-written class with the
    /// wrapper name suffix outside the Hotfix assembly is not exempt.
    /// </remarks>
    public static bool IsGeneratedComponentAgentWrapper(INamedTypeSymbol type)
    {
        if (HasWrapperGeneratorMarker(type))
        {
            return true;
        }

        if (IsDeclaredInGeneratedCodeFile(type))
        {
            return true;
        }

        return type.ContainingAssembly.Identity.Name == ArchitectureAnalyzerConstants.HotfixAssembly
               && type.Name.EndsWith("ComponentAgentWrapper", StringComparison.Ordinal);
    }

    /// <summary>
    /// 判断类型是否标注了 WrapperGenerator 的 GeneratedCodeAttribute 标记（tool 名精确匹配）。
    /// </summary>
    /// <remarks>
    /// Checks whether the type carries the wrapper generator's GeneratedCodeAttribute marker with an
    /// exact tool-name match; GeneratedCodeAttribute from other tools does not exempt the type.
    /// </remarks>
    private static bool HasWrapperGeneratorMarker(INamedTypeSymbol type)
    {
        foreach (var attribute in type.GetAttributes())
        {
            var attributeClass = attribute.AttributeClass;
            if (attributeClass == null
                || attributeClass.MetadataName != "GeneratedCodeAttribute"
                || attributeClass.ContainingNamespace == null
                || attributeClass.ContainingNamespace.ToDisplayString() != "System.CodeDom.Compiler")
            {
                continue;
            }

            if (attribute.ConstructorArguments.Length > 0
                && attribute.ConstructorArguments[0].Value is string toolName
                && toolName == ArchitectureAnalyzerConstants.WrapperGeneratorToolName)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 判断类型的任一声明位置是否位于 ".g.cs" 生成源文件。
    /// </summary>
    /// <remarks>
    /// Checks whether any declaration location of the type lies in a generated ".g.cs" source file.
    /// </remarks>
    private static bool IsDeclaredInGeneratedCodeFile(INamedTypeSymbol type)
    {
        foreach (var location in type.Locations)
        {
            if (location.IsInMetadata)
            {
                continue;
            }

            var filePath = location.SourceTree?.FilePath ?? string.Empty;
            if (filePath.EndsWith(".g.cs", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 向编译器报告一个诊断错误，定位到类型声明位置。
    /// </summary>
    public static void Report(SymbolAnalysisContext context, DiagnosticDescriptor descriptor, INamedTypeSymbol type, params object[] args)
    {
        context.ReportDiagnostic(Diagnostic.Create(descriptor, type.Locations.FirstOrDefault(), args));
    }
}
