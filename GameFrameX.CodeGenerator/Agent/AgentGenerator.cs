// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规及开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 MIT 许可证与 Apache License 2.0 双许可证分发，
//   This project is dual-licensed under the MIT License and Apache License 2.0,
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GameFrameX.CodeGenerator.Utils;
using GameFrameX.Core.Abstractions.Attribute;
using GameFrameX.Utility.Setting;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace GameFrameX.CodeGenerator.Agent;

[Generator]
public class AgentGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        ResLoader.LoadDll();

        var candidates = context.SyntaxProvider
                                .CreateSyntaxProvider(
                                    static (node, _) =>
                                    {
                                        var c = node as ClassDeclarationSyntax;
                                        if (c == null)
                                        {
                                            return false;
                                        }

                                        return c.BaseList != null || c.Modifiers.Any(m => m.Text == "partial");
                                    },
                                    static (ctx, _) => (ClassDeclarationSyntax)ctx.Node)
                                .Collect();

        context.RegisterSourceOutput(candidates, static (productionContext, classes) => { Execute(productionContext, classes); });
    }

    private static void Execute(SourceProductionContext context, IReadOnlyList<ClassDeclarationSyntax> allClasses)
    {
        var agentNames = new HashSet<string>();
        var agents = new List<ClassDeclarationSyntax>();
        var partials = new List<ClassDeclarationSyntax>();

        foreach (var c in allClasses)
        {
            if (IsCompAgent(c))
            {
                agents.Add(c);
                agentNames.Add(c.Identifier.Text);
            }
            else if (c.Modifiers.Any(m => m.Text == "partial"))
            {
                partials.Add(c);
            }
        }

        foreach (var p in partials)
        {
            if (agentNames.Contains(p.Identifier.Text))
            {
                agents.Add(p);
            }
        }

        var serviceAttributeName = nameof(ServiceAttribute).Replace("Attribute", string.Empty);
        var threadSafeAttributeName = nameof(ThreadSafeAttribute).Replace("Attribute", string.Empty);
        var discardAttributeName = nameof(DiscardAttribute).Replace("Attribute", string.Empty);
        var timeOutAttributeName = nameof(TimeOutAttribute).Replace("Attribute", string.Empty);

        var partialClassCount = new Dictionary<string, int>();

        foreach (var agent in agents)
        {
            var fullName = agent.GetFullName();
            var info = new AgentInfo();
            info.Super = agent.Identifier.Text;
            info.Name = info.Super + GlobalConst.WrapperNameSuffix;
            info.Space = GlobalConst.HotfixNameSpaceNamePrefix + GlobalConst.WrapperNameSuffix + ".Agent";

            string outFileName = null;

            var isPartialClass = agent.Modifiers.ToList().FindIndex(s => s.Text == "partial") >= 0;
            if (isPartialClass)
            {
                info.Partial = "partial";
                partialClassCount.TryGetValue(info.Name, out var count);
                partialClassCount[info.Name] = count + 1;
                outFileName = $"{info.Name}{count}.g.cs";
            }
            else
            {
                outFileName = $"{info.Name}.g.cs";
            }

            var root = agent.SyntaxTree.GetCompilationUnitRoot();
            foreach (var element in root.Usings)
            {
                info.UsingSpaces.Add(element.Name.ToString());
            }

            info.UsingSpaces.Add(Tools.GetNameSpace(fullName));

            foreach (var member in agent.Members)
            {
                if (member is MethodDeclarationSyntax method)
                {
                    if (method.Identifier.Text.Equals("Active")
                        || method.Identifier.Text.Equals("Inactive"))
                    {
                        continue;
                    }

                    var mth = new MthInfo();
                    foreach (var m in method.Modifiers)
                    {
                        if (m.Text.Equals("virtual"))
                        {
                            mth.IsVirtual = true;
                            mth.Modify += "override ";
                        }
                        else
                        {
                            mth.Modify += m.Text + " ";
                        }

                        if (m.Text.Equals("public"))
                        {
                            mth.IsPublic = true;
                        }

                        if (m.Text.Equals("static"))
                        {
                            mth.IsStatic = true;
                        }

                        if (m.Text.Equals("async"))
                        {
                            mth.IsAsync = true;
                        }
                    }

                    if (mth.IsStatic)
                    {
                        continue;
                    }

                    mth.ReturnType = method.ReturnType?.ToString() ?? "void";
                    foreach (var attributeListSyntax in method.AttributeLists)
                    {
                        var attrName = attributeListSyntax.ToString().RemoveWhitespace() + "Attribute";
                        if (attrName.IndexOf(serviceAttributeName, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            mth.IsApi = true;
                        }
                        else if (attrName.IndexOf(discardAttributeName, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            mth.Discard = true;
                            if (mth.IsAsync)
                            {
                                mth.Modify = mth.Modify.Replace("async ", "");
                                mth.IsAsync = false;
                            }
                        }
                        else if (attrName.IndexOf(timeOutAttributeName, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            mth.HasTimeout = true;
                            var argStr = attributeListSyntax.Attributes[0].ArgumentList.Arguments[0].ToString();
                            if (argStr.IndexOf(":", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                mth.TimeOut = int.Parse(argStr.Split(':')[1].Trim());
                            }
                            else
                            {
                                mth.TimeOut = int.Parse(argStr);
                            }
                        }
                        else if (attrName.IndexOf(threadSafeAttributeName, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            mth.IsThreadSafe = true;
                        }
                    }

                    if (mth.IsThreadSafe && mth.HasTimeout)
                    {
                        context.LogError($"{fullName}.{method.Identifier.Text}无法为标记【{threadSafeAttributeName}】的函数指定超时时间");
                    }

                    if (!mth.IsApi && !mth.Discard && mth.HasTimeout)
                    {
                        context.LogError($"{fullName}.{method.Identifier.Text}【{timeOutAttributeName}】注解只能配合【Api】或【{discardAttributeName}】使用");
                    }

                    if (!mth.IsApi && !mth.Discard && !mth.IsThreadSafe)
                    {
                        continue;
                    }

                    if (mth.IsThreadSafe && !mth.Discard)
                    {
                        continue;
                    }

                    if (mth.IsApi && !mth.IsThreadSafe && !mth.ReturnType.Contains("Task"))
                    {
                        context.LogError($"{fullName}.{method.Identifier.Text}, 非【{threadSafeAttributeName}】的【Api】接口只能是异步函数");
                    }

                    if ((mth.IsApi || mth.Discard || mth.IsThreadSafe) && !mth.IsVirtual)
                    {
                        context.LogError($"{fullName}.{method.Identifier.Text}标记了【AsyncApi】【{threadSafeAttributeName}】【{discardAttributeName}】注解的函数必须申明为virtual");
                    }

                    if (mth.IsVirtual)
                    {
                        info.Methods.Add(mth);
                        mth.Name = method.Identifier.Text;
                        mth.ParamDeclare = method.ParameterList.ToString();
                        if (mth.Discard && !mth.ReturnType.Equals(nameof(Task)) && !mth.ReturnType.Equals(nameof(ValueTask)))
                        {
                            context.LogError($"{fullName}.{method.Identifier.Text}只有返回值为Task类型或ValueTask类型才能添加【Discard】注解");
                        }

                        mth.Constraint = method.ConstraintClauses.ToString();
                        mth.Typeparams = method.TypeParameterList?.ToString();
                        foreach (var p in method.ParameterList.Parameters)
                        {
                            mth.Params.Add(p.Identifier.Text);
                        }
                    }
                }
            }

            var source = AgentTemplate.Run(info);
            context.AddSource(outFileName, source);
        }
    }

    private static bool IsCompAgent(ClassDeclarationSyntax source)
    {
        if (source.BaseList == null)
        {
            return false;
        }

        var baseTypes = source.BaseList.Types.Select(baseType => baseType);
        var res = baseTypes.Any(baseType =>
        {
            var baseName = baseType.ToString();
            return baseName.Contains("StateComponentAgent")
                   || baseName.Contains("FuncComponentAgent")
                   || baseName.Contains("QueryComponentAgent")
                   || baseName.Contains("BaseComponentAgent");
        });
        return res;
    }
}