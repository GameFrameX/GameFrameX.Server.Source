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

using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace GameFrameX.Hotfix.WrapperGenerator.Utils;

/// <summary>
/// 提供 <see cref="ClassDeclarationSyntax"/> 的扩展方法，用于获取类的完全限定名和命名空间信息。
/// </summary>
/// <remarks>
/// Provides extension methods for <see cref="ClassDeclarationSyntax"/> to retrieve fully qualified names and namespace information.
/// </remarks>
public static class ClassDeclarationSyntaxExt
{
    /// <summary>
    /// 嵌套类之间的分隔符。
    /// </summary>
    /// <remarks>
    /// Delimiter between nested classes.
    /// </remarks>
    public const string NestedClassDelimiter = "+";

    /// <summary>
    /// 命名空间与类名之间的分隔符。
    /// </summary>
    /// <remarks>
    /// Delimiter between namespace and class name.
    /// </remarks>
    public const string NamespaceClassDelimiter = ".";

    /// <summary>
    /// 获取类声明的完全限定名，包含命名空间和嵌套类型路径。
    /// </summary>
    /// <remarks>
    /// Gets the fully qualified name of the class declaration, including namespace and nested type path.
    /// 向上收集容器链：命名空间段自带尾部「.」、嵌套类型段自带尾部「+」，直接连接后追加类名，
    /// 任意声明形态（块/文件作用域命名空间、无命名空间、嵌套类型容器）都返回确定值：
    /// 有命名空间时为 <c>Namespace.OuterClass+InnerClass</c>；无命名空间时仅返回类型链（<c>OuterClass+InnerClass</c> 或短名）。
    /// Collects the container chain upward: each namespace segment carries a trailing "." and each nested
    /// type segment a trailing "+"; concatenating them and appending the class name yields a deterministic
    /// value for any declaration shape (block/file-scoped namespace, no namespace, nested type containers).
    /// </remarks>
    /// <param name="source">类声明语法节点 / Class declaration syntax node</param>
    /// <returns>完全限定名（如 Namespace.OuterClass+InnerClass）/ Fully qualified name (e.g., Namespace.OuterClass+InnerClass)</returns>
    public static string GetFullName(this ClassDeclarationSyntax source)
    {
        if (source == null)
        {
            return string.Empty;
        }

        var containers = new List<string>();
        for (var node = source.Parent; node != null; node = node.Parent)
        {
            if (node is BaseTypeDeclarationSyntax typeDeclaration)
            {
                // 嵌套类型容器：类名后跟嵌套分隔符（OuterClass+InnerClass 中的 OuterClass+）
                containers.Add(typeDeclaration.Identifier.Text + NestedClassDelimiter);
            }
            else if (node is BaseNamespaceDeclarationSyntax namespaceDeclaration)
            {
                // 命名空间声明名本身已含全部上级段（嵌套命名空间声明为全名），取到即止
                containers.Add(namespaceDeclaration.Name.ToString() + NamespaceClassDelimiter);
                break;
            }
        }

        containers.Reverse();
        return string.Concat(containers) + source.Identifier.Text;
    }

}