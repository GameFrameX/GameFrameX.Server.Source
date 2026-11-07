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

namespace GameFrameX.Architecture.Analyzers;

/// <summary>
/// 顶层类型声明的收集工具，供「单类型每文件」与「文件名一致」两条规则共用。
/// </summary>
/// <remarks>
/// 顶层类型 = 直接位于编译单元或任意层 namespace 内、而非位于其它类型内部的类型声明。
/// 嵌套类型（类中类）按规则豁免，因此遍历时不再下钻类型成员。
/// </remarks>
internal static class CodeStyleSyntaxFacts
{
    /// <summary>
    /// 收集语法树中全部顶层类型声明。
    /// </summary>
    /// <param name="root">编译单元根节点。</param>
    /// <returns>按源码顺序排列的顶层类型列表，含类型名、泛型参数个数与标识符位置。</returns>
    public static List<TopLevelType> CollectTopLevelTypes(SyntaxNode root)
    {
        var result = new List<TopLevelType>();
        var pending = new Stack<SyntaxNode>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var node = pending.Pop();
            switch (node)
            {
                case CompilationUnitSyntax compilationUnit:
                    PushMembers(pending, compilationUnit.Members);
                    break;
                case BaseNamespaceDeclarationSyntax namespaceDeclaration:
                    PushMembers(pending, namespaceDeclaration.Members);
                    break;
                case BaseTypeDeclarationSyntax typeDeclaration:
                    result.Add(new TopLevelType(typeDeclaration.Identifier.Text, GetArityOf(typeDeclaration), typeDeclaration.Identifier.GetLocation()));
                    break;
                case DelegateDeclarationSyntax delegateDeclaration:
                    result.Add(new TopLevelType(delegateDeclaration.Identifier.Text, GetArity(delegateDeclaration.TypeParameterList), delegateDeclaration.Identifier.GetLocation()));
                    break;
            }
        }

        result.Sort((left, right) => left.Location.SourceSpan.Start.CompareTo(right.Location.SourceSpan.Start));
        return result;
    }

    private static void PushMembers(Stack<SyntaxNode> pending, SyntaxList<MemberDeclarationSyntax> members)
    {
        for (var index = members.Count - 1; index >= 0; index--)
        {
            pending.Push(members[index]);
        }
    }

    private static int GetArityOf(BaseTypeDeclarationSyntax typeDeclaration)
    {
        var typeDeclarationWithParameters = typeDeclaration as TypeDeclarationSyntax;
        return typeDeclarationWithParameters == null ? 0 : GetArity(typeDeclarationWithParameters.TypeParameterList);
    }

    private static int GetArity(TypeParameterListSyntax typeParameterList)
    {
        return typeParameterList?.Parameters.Count ?? 0;
    }

    /// <summary>
    /// 一个顶层类型声明的最小投影。
    /// </summary>
    /// <param name="Name">类型名。</param>
    /// <param name="Arity">泛型参数个数；非泛型类型为 0。</param>
    /// <param name="Location">类型名标识符所在位置。</param>
    internal readonly struct TopLevelType
    {
        /// <summary>
        /// 初始化一个顶层类型投影。
        /// </summary>
        /// <param name="name">类型名。</param>
        /// <param name="arity">泛型参数个数；非泛型类型为 0。</param>
        /// <param name="location">类型名标识符所在位置。</param>
        public TopLevelType(string name, int arity, Location location)
        {
            Name = name;
            Arity = arity;
            Location = location;
        }

        /// <summary>类型名。</summary>
        public string Name { get; }

        /// <summary>泛型参数个数；非泛型类型为 0。</summary>
        public int Arity { get; }

        /// <summary>类型名标识符所在位置。</summary>
        public Location Location { get; }
    }
}
