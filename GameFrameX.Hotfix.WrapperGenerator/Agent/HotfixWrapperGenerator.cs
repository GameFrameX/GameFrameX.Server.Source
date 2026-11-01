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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using GameFrameX.Hotfix.WrapperGenerator.Utils;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace GameFrameX.Hotfix.WrapperGenerator.Agent;

/// <summary>
/// 增量源代码生成器，为标记了特定属性的代理类自动生成线程安全调用的包装器分部类。
/// </summary>
/// <remarks>
/// Incremental source generator that automatically generates thread-safe wrapper partial classes for agent classes marked with specific attributes.
/// </remarks>
[Generator]
public class HotfixWrapperGenerator : IIncrementalGenerator
{
    /// <summary>
    /// 生成的包装器类名后缀。
    /// </summary>
    /// <remarks>
    /// Suffix for generated wrapper class names.
    /// </remarks>
    private const string WrapperNameSuffix = "Wrapper";

    /// <summary>
    /// 生成的热修复命名空间前缀。
    /// </summary>
    /// <remarks>
    /// Prefix for the generated hotfix namespace.
    /// </remarks>
    private const string HotfixNameSpaceNamePrefix = "GameFrameX.Hotfix.";

    /// <summary>
    /// 生成的包装器命名空间段（拼接在热更命名空间前缀之后）。
    /// </summary>
    /// <remarks>
    /// Namespace segment for generated wrappers (appended after the hotfix namespace prefix).
    /// </remarks>
    private const string WrapperNamespaceSegment = "Wrapper.Agent";

    /// <summary>
    /// Service 属性名称常量。
    /// </summary>
    /// <remarks>
    /// Service attribute name constant.
    /// </remarks>
    private const string ServiceAttributeName = "Service";

    /// <summary>
    /// ThreadSafe 属性名称常量。
    /// </summary>
    /// <remarks>
    /// ThreadSafe attribute name constant.
    /// </remarks>
    private const string ThreadSafeAttributeName = "ThreadSafe";

    /// <summary>
    /// Discard 属性名称常量。
    /// </summary>
    /// <remarks>
    /// Discard attribute name constant.
    /// </remarks>
    private const string DiscardAttributeName = "Discard";

    /// <summary>
    /// TimeOut 属性名称常量。
    /// </summary>
    /// <remarks>
    /// TimeOut attribute name constant.
    /// </remarks>
    private const string TimeOutAttributeName = "TimeOut";

    /// <summary>
    /// 初始化增量生成器，注册语法提供者和源代码输出。
    /// </summary>
    /// <remarks>
    /// Initializes the incremental generator, registering syntax providers and source output.
    /// </remarks>
    /// <param name="context">增量生成器初始化上下文 / Incremental generator initialization context</param>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
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

    /// <summary>
    /// 执行代码生成，遍历所有候选类并为符合条件的代理类生成包装器代码。
    /// </summary>
    /// <remarks>
    /// Executes code generation, iterating all candidate classes and generating wrapper code for qualifying agent classes.
    /// </remarks>
    /// <param name="context">源代码生成上下文 / Source production context</param>
    /// <param name="allClasses">所有候选类声明语法节点列表 / List of all candidate class declaration syntax nodes</param>
    private static void Execute(SourceProductionContext context, IReadOnlyList<ClassDeclarationSyntax> allClasses)
    {
        var agents = CollectAgents(allClasses);
        var partialClassCount = new Dictionary<string, int>();

        foreach (var agent in agents)
        {
            ProcessAgent(context, agent, partialClassCount);
        }
    }

    /// <summary>
    /// 从候选类中收集组件代理类，并把与代理类同名的分部类合并进结果列表。
    /// </summary>
    /// <remarks>
    /// Collects component agent classes from the candidates and merges same-named partial classes into the result list.
    /// </remarks>
    /// <param name="allClasses">所有候选类声明语法节点列表 / List of all candidate class declaration syntax nodes</param>
    /// <returns>合并分部类后的代理类列表 / The list of agent classes after merging partial classes</returns>
    private static List<ClassDeclarationSyntax> CollectAgents(IReadOnlyList<ClassDeclarationSyntax> allClasses)
    {
        var agentNames = new HashSet<string>();
        var agents = new List<ClassDeclarationSyntax>();
        var partials = new List<ClassDeclarationSyntax>();

        foreach (var c in allClasses)
        {
            if (IsCompAgent(c))
            {
                // 合并键用完全限定名：跨命名空间同简单名的无关 partial 类不得并入代理（幽灵 Wrapper）
                agents.Add(c);
                agentNames.Add(c.GetFullName());
            }
            else if (c.Modifiers.Any(m => m.Text == "partial"))
            {
                partials.Add(c);
            }
        }

        foreach (var p in partials)
        {
            if (agentNames.Contains(p.GetFullName()))
            {
                agents.Add(p);
            }
        }

        return agents;
    }

    /// <summary>
    /// 处理单个代理类：构建代理信息、收集方法元数据并生成包装器源代码。
    /// </summary>
    /// <remarks>
    /// Processes a single agent class: builds agent info, collects method metadata, and emits the wrapper source code.
    /// </remarks>
    /// <param name="context">源代码生成上下文 / Source production context</param>
    /// <param name="agent">代理类声明语法节点 / Agent class declaration syntax node</param>
    /// <param name="partialClassCount">分部类输出文件名计数表 / Partial class output filename counter map</param>
    private static void ProcessAgent(SourceProductionContext context, ClassDeclarationSyntax agent, Dictionary<string, int> partialClassCount)
    {
        var fullName = agent.GetFullName();
        var info = CreateAgentInfo(agent, fullName, partialClassCount, out var outFileName);

        var root = agent.SyntaxTree.GetCompilationUnitRoot();
        foreach (var element in root.Usings)
        {
            info.UsingSpaces.Add(element.Name.ToString());
        }

        // 无命名空间代理 GetNameSpace 返回空串：空 using 会产出非法 C#，跳过发射
        var agentNameSpace = Tools.GetNameSpace(fullName);
        if (!string.IsNullOrEmpty(agentNameSpace))
        {
            info.UsingSpaces.Add(agentNameSpace);
        }

        foreach (var member in agent.Members)
        {
            AddAgentMethod(context, info, fullName, member);
        }
        if (info.Methods.Count == 0)
        {
            // 无可包装方法的代理：不产出任何 wrapper 分部类（空壳无行为，只污染生成面）。
            return;
        }

        var source = AgentTemplate.Run(info);
        context.AddSource(outFileName, source);
    }

    /// <summary>
    /// 根据代理类创建代理信息，并确定生成的输出文件名。
    /// </summary>
    /// <remarks>
    /// Creates agent info from the agent class and determines the generated output filename.
    /// </remarks>
    /// <param name="agent">代理类声明语法节点 / Agent class declaration syntax node</param>
    /// <param name="fullName">代理类完全限定名 / Fully qualified name of the agent class</param>
    /// <param name="partialClassCount">分部类输出文件名计数表 / Partial class output filename counter map</param>
    /// <param name="outFileName">生成的输出文件名 / The generated output filename</param>
    /// <returns>填充好的代理信息 / The populated agent info</returns>
    private static AgentInfo CreateAgentInfo(ClassDeclarationSyntax agent, string fullName, Dictionary<string, int> partialClassCount, out string outFileName)
    {
        var info = new AgentInfo();

        // C292：生成类命名空间镜像代理源命名空间（GameFrameX.Hotfix.Wrapper.Agent + 源命名空间相对
        // GameFrameX.Hotfix. 的段）。仓内存在同名代理（如 Player/Login 与 ServerRole/Login 各一个
        // LoginComponentAgent），固定单一命名空间会让两个生成类同名冲突（CS0101 / CS8785）；
        // HotfixModule 按「前缀 GameFrameX.Hotfix. + 类名后缀 ComponentAgentWrapper」识别，镜像后仍满足。
        var sourceNamespace = Tools.GetNameSpace(fullName);
        var relativeNamespace = sourceNamespace.StartsWith(HotfixNameSpaceNamePrefix, StringComparison.Ordinal)
            ? sourceNamespace.Substring(HotfixNameSpaceNamePrefix.Length)
            : sourceNamespace;
        info.Space = HotfixNameSpaceNamePrefix + WrapperNamespaceSegment + (relativeNamespace.Length == 0 ? string.Empty : "." + relativeNamespace);

        // 基类引用取剥离命名空间后的容器链（嵌套分隔符「+」改写为源码层级的「.」）：
        // 顶层代理结果与简单名逐字一致；嵌套代理（Outer+Inner）发射 Outer.Inner，配合镜像命名空间
        // 与 using 源命名空间，生成类的基类在生成文件作用域内可解析。
        var containerQualifiedTypeName = (sourceNamespace.Length == 0 ? fullName : fullName.Substring(sourceNamespace.Length + 1)).Replace('+', '.');
        info.Super = containerQualifiedTypeName;

        // wrapper 类名同样携带容器链（容器分隔符折叠为「_」）：镜像命名空间只含真命名空间，同命名空间下
        // 嵌套代理与顶层同名代理（Outer+FooAgent 与 FooAgent）、不同容器同名嵌套代理若都只取简单名会得到
        // 同名同命名空间的两个 wrapper（CS0101/CS0263）；仍以 ComponentAgentWrapper 结尾，HotfixModule
        // 后缀识别不受影响。顶层代理与「简单名+Wrapper」逐字一致。
        info.Name = containerQualifiedTypeName.Replace('.', '_') + WrapperNameSuffix;

        var isPartialClass = agent.Modifiers.ToList().FindIndex(s => s.Text == "partial") >= 0;
        if (isPartialClass)
        {
            // 计数键用完全限定名：同名代理分属不同命名空间时各自独立编号，不再互相污染
            partialClassCount.TryGetValue(fullName, out var count);
            partialClassCount[fullName] = count + 1;
            outFileName = BuildHintName(fullName, count);
        }
        else
        {
            outFileName = BuildHintName(fullName, null);
        }

        return info;
    }

    /// <summary>
    /// 由代理完全限定名构造唯一的生成文件名（hintName）。
    /// </summary>
    /// <remarks>
    /// Builds the unique generated file name (hintName) from the agent's fully qualified name.
    /// </remarks>
    /// <param name="fullName">代理类完全限定名 / Fully qualified name of the agent class</param>
    /// <param name="sequence">分部类序号（非分部类为 null）/ Partial class sequence number (null for non-partial)</param>
    /// <returns>唯一的 hintName / A unique hintName</returns>
    private static string BuildHintName(string fullName, int? sequence)
    {
        // 命名空间分隔符与嵌套类分隔符折叠为不同占位（. → _、+ → __），保证映射单射：
        // NS.A.B（命名空间 NS.A 的类 B）与 NS.A+B（NS 下类 A 的嵌套类 B）净化后不再碰撞
        var sanitized = (fullName + WrapperNameSuffix).Replace(".", "_").Replace("+", "__");
        return sequence.HasValue ? $"{sanitized}{sequence.Value}.g.cs" : $"{sanitized}.g.cs";
    }

    /// <summary>
    /// 处理代理类的单个成员：若是需要包装的方法，则读取其元数据并收集到代理信息中。
    /// </summary>
    /// <remarks>
    /// Processes a single member of the agent class: if it is a method to wrap, reads its metadata and collects it into the agent info.
    /// </remarks>
    /// <param name="context">源代码生成上下文 / Source production context</param>
    /// <param name="info">代理信息 / Agent info</param>
    /// <param name="fullName">代理类完全限定名 / Fully qualified name of the agent class</param>
    /// <param name="member">成员声明语法节点 / Member declaration syntax node</param>
    private static void AddAgentMethod(SourceProductionContext context, AgentInfo info, string fullName, MemberDeclarationSyntax member)
    {
        if (!(member is MethodDeclarationSyntax method))
        {
            return;
        }

        // 生命周期方法 ActiveAsync / InactiveAsync 沿用原跳过语义。
        if (method.Identifier.Text.Equals("ActiveAsync") || method.Identifier.Text.Equals("InactiveAsync"))
        {
            return;
        }

        var mth = new MethodInfoData();
        ReadModifiers(method, mth);

        if (mth.IsStatic)
        {
            return;
        }

        mth.ReturnType = method.ReturnType?.ToString() ?? "void";
        foreach (var attributeListSyntax in method.AttributeLists)
        {
            ApplyAttribute(context, fullName, method, attributeListSyntax, mth);
        }

        CollectMethod(context, info, fullName, method, mth);
    }

    /// <summary>
    /// 读取方法修饰符并填充到方法元数据中。
    /// </summary>
    /// <remarks>
    /// Reads method modifiers and populates them into the method metadata.
    /// </remarks>
    /// <param name="method">方法声明语法节点 / Method declaration syntax node</param>
    /// <param name="mth">待填充的方法元数据 / Method metadata to populate</param>
    private static void ReadModifiers(MethodDeclarationSyntax method, MethodInfoData mth)
    {
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
    }

    /// <summary>
    /// 解析单个属性列表中的全部特性，逐个按名称精确匹配 Service/Discard/TimeOut/ThreadSafe 并填充到方法元数据中。
    /// </summary>
    /// <remarks>
    /// Parses every attribute in a single attribute list, exactly matching Service/Discard/TimeOut/ThreadSafe by name and populating the method metadata.
    /// TimeOut 实参经防御式解析：无法静态求值时输出 Error 诊断并按未配置超时处理，绝不让异常外泄令生成器整体崩溃。
    /// </remarks>
    /// <param name="context">源代码生成上下文 / Source production context</param>
    /// <param name="fullName">代理类完全限定名 / Fully qualified name of the agent class</param>
    /// <param name="method">方法声明语法节点 / Method declaration syntax node</param>
    /// <param name="attributeListSyntax">属性列表语法节点 / Attribute list syntax node</param>
    /// <param name="mth">待填充的方法元数据 / Method metadata to populate</param>
    private static void ApplyAttribute(SourceProductionContext context, string fullName, MethodDeclarationSyntax method, AttributeListSyntax attributeListSyntax, MethodInfoData mth)
    {
        foreach (var attribute in attributeListSyntax.Attributes)
        {
            // 精确匹配而非子串搜索：同块多特性（如 [Service, Discard]）逐个识别，
            // 且 [FooService] 等仅含子串的相似名特性不再误命中。
            var name = StripAttributeSuffix(attribute.Name.ToString());
            if (string.Equals(name, StripAttributeSuffix(ServiceAttributeName), StringComparison.OrdinalIgnoreCase))
            {
                mth.IsApi = true;
            }
            else if (string.Equals(name, StripAttributeSuffix(DiscardAttributeName), StringComparison.OrdinalIgnoreCase))
            {
                mth.Discard = true;
                if (mth.IsAsync)
                {
                    mth.Modify = mth.Modify.Replace("async ", "");
                    mth.IsAsync = false;
                }
            }
            else if (string.Equals(name, StripAttributeSuffix(TimeOutAttributeName), StringComparison.OrdinalIgnoreCase))
            {
                mth.TimeOut = ParseTimeout(attribute, out var parsed);
                if (parsed)
                {
                    mth.HasTimeout = true;
                }
                else
                {
                    // 解析失败降级为未配置超时（走 EnqueueAsync 框架默认值），并以诊断指明写法约束
                    mth.HasTimeout = false;
                    var rawArgument = attribute.ArgumentList != null && attribute.ArgumentList.Arguments.Count > 0
                        ? attribute.ArgumentList.Arguments[0].ToString()
                        : string.Empty;
                    context.LogError($"{fullName}.{method.Identifier.Text}【{TimeOutAttributeName}】参数必须是整数字面量（毫秒），当前无法解析：{rawArgument}");
                }
            }
            else if (string.Equals(name, StripAttributeSuffix(ThreadSafeAttributeName), StringComparison.OrdinalIgnoreCase))
            {
                mth.IsThreadSafe = true;
            }
        }
    }

    /// <summary>
    /// 剥离特性名尾部的 Attribute 后缀（[Service] 与 [ServiceAttribute] 书写等价）。
    /// </summary>
    /// <remarks>
    /// Strips the trailing Attribute suffix from an attribute name ([Service] and [ServiceAttribute] are equivalent spellings).
    /// </remarks>
    /// <param name="name">特性名字符串 / Attribute name string</param>
    /// <returns>剥离后缀后的特性名 / The attribute name without the suffix</returns>
    private static string StripAttributeSuffix(string name)
    {
        const string attributeSuffix = "Attribute";
        return name.EndsWith(attributeSuffix, StringComparison.Ordinal)
            ? name.Substring(0, name.Length - attributeSuffix.Length)
            : name;
    }

    /// <summary>
    /// 从命中的 TimeOut 特性自身的参数中防御式解析超时时间（毫秒）。
    /// </summary>
    /// <remarks>
    /// Defensively parses the timeout duration (in milliseconds) from the matched TimeOut attribute's own argument.
    /// 仅数值字面量可静态求值（数字分隔符「_」与负号在字面量/一元负表达式中合法）；常量名、其他表达式、
    /// 属性赋值、缺参/空参等一切形态都返回 <c>0</c> 并置 <paramref name="parsed"/> 为 <c>false</c>，由调用方
    /// 降级处理——任何输入形态都不抛异常（生成器异常 = 全部 wrapper 消失）。
    /// Only numeric literals are statically evaluated (digit separators "_" and a unary minus are legal);
    /// constant names, any other expression, property assignments and missing/empty argument lists return 0
    /// with <paramref name="parsed"/> set to <c>false</c> for caller-side degradation — no input shape may
    /// throw (a generator crash removes every wrapper).
    /// </remarks>
    /// <param name="attribute">命中的 TimeOut 特性语法节点 / The matched TimeOut attribute syntax node</param>
    /// <param name="parsed">是否成功解析出整数字面量 / Whether an integer literal was successfully parsed</param>
    /// <returns>超时时间（毫秒）；解析失败返回 <c>0</c>（不使用）/ Timeout duration (in milliseconds); 0 when parsing failed (unused)</returns>
    private static int ParseTimeout(AttributeSyntax attribute, out bool parsed)
    {
        parsed = false;
        if (attribute.ArgumentList == null || attribute.ArgumentList.Arguments.Count == 0)
        {
            return 0;
        }

        var argument = attribute.ArgumentList.Arguments[0];
        if (argument.NameEquals != null)
        {
            // 属性赋值实参（Name = value）不是构造参数，无法取值
            return 0;
        }

        var milliseconds = ResolveIntegerLiteral(argument.Expression);
        if (!milliseconds.HasValue)
        {
            return 0;
        }

        parsed = true;
        return milliseconds.Value;
    }

    /// <summary>
    /// 把实参表达式解析为整数值：仅接受数值字面量（含数字分隔符「_」）与其一元负表达式。
    /// </summary>
    /// <remarks>
    /// Resolves an argument expression to an integer: only numeric literals (digit separators "_" included)
    /// and their unary-minus expressions are accepted.
    /// 常量名/其他表达式（含十六进制字面量等 TryParse 不支持的书写）都返回 <c>null</c> 交由调用方降级诊断，
    /// 杜绝「标识符文本恰好剥掉下划线后成数字」一类静默误析。
    /// Constant names and any other expression (including hex literals TryParse cannot handle) return
    /// <c>null</c> for caller-side diagnostic degradation, preventing silent mis-parsing of identifier texts
    /// that happen to become digits after underscore stripping.
    /// </remarks>
    /// <param name="expression">实参表达式语法节点 / The argument expression syntax node</param>
    /// <returns>整数值；不可静态求值时为 <c>null</c> / The integer value; <c>null</c> when not statically evaluable</returns>
    private static int? ResolveIntegerLiteral(ExpressionSyntax expression)
    {
        if (expression is PrefixUnaryExpressionSyntax prefixUnaryExpression
            && prefixUnaryExpression.IsKind(SyntaxKind.UnaryMinusExpression)
            && prefixUnaryExpression.Operand is LiteralExpressionSyntax minusOperand)
        {
            var negatedValue = ResolveNumericLiteralToken(minusOperand);
            return negatedValue.HasValue ? -negatedValue.Value : (int?)null;
        }

        if (expression is LiteralExpressionSyntax literalExpression)
        {
            return ResolveNumericLiteralToken(literalExpression);
        }

        return null;
    }

    /// <summary>
    /// 把数值字面量表达式的 token 文本解析为整数（剥除数字分隔符「_」）。
    /// </summary>
    /// <remarks>
    /// Parses the token text of a numeric literal expression into an integer (stripping digit separators "_").
    /// </remarks>
    /// <param name="literalExpression">数值字面量表达式 / The numeric literal expression</param>
    /// <returns>整数值；token 不是数值字面量或越界时为 <c>null</c> / The integer value; <c>null</c> when the token is not a numeric literal or overflows</returns>
    private static int? ResolveNumericLiteralToken(LiteralExpressionSyntax literalExpression)
    {
        if (!literalExpression.Token.IsKind(SyntaxKind.NumericLiteralToken))
        {
            return null;
        }

        return int.TryParse(literalExpression.Token.Text.Replace("_", string.Empty), NumberStyles.Integer, CultureInfo.InvariantCulture, out var milliseconds)
            ? milliseconds
            : (int?)null;
    }

    /// <summary>
    /// 对已读取元数据的方法进行属性组合校验、跳过判定、契约校验，并在符合条件时收集为包装方法。
    /// </summary>
    /// <remarks>
    /// Validates attribute combinations, applies skip rules and contract checks for a method whose metadata has been read, and collects it as a wrapped method when eligible.
    /// 校验先行：阻断违规（ref/out/in 参数、Discard 返回类型契约不满足）的方法输出 Error 诊断后不进入生成面。
    /// </remarks>
    /// <param name="context">源代码生成上下文 / Source production context</param>
    /// <param name="info">代理信息 / Agent info</param>
    /// <param name="fullName">代理类完全限定名 / Fully qualified name of the agent class</param>
    /// <param name="method">方法声明语法节点 / Method declaration syntax node</param>
    /// <param name="mth">方法元数据 / Method metadata</param>
    private static void CollectMethod(SourceProductionContext context, AgentInfo info, string fullName, MethodDeclarationSyntax method, MethodInfoData mth)
    {
        ReportAttributeViolations(context, fullName, method, mth);

        if (!ShouldWrapMethod(mth))
        {
            return;
        }

        ReportContractViolations(context, fullName, method, mth);

        // 有阻断违规的方法绝不进入生成面：报 Error 的同时跳过收集，产出保持可编译
        if (mth.HasBlockingViolation)
        {
            return;
        }

        if (mth.IsVirtual)
        {
            AddWrappedMethod(info, context, fullName, method, mth);
        }
    }

    /// <summary>
    /// 报告属性组合相关的诊断错误（ThreadSafe 与 TimeOut 冲突、非 Api/Discard 却带 TimeOut）。
    /// </summary>
    /// <remarks>
    /// Reports diagnostics for attribute-combination violations (ThreadSafe conflicting with TimeOut, and TimeOut used without Api/Discard).
    /// </remarks>
    /// <param name="context">源代码生成上下文 / Source production context</param>
    /// <param name="fullName">代理类完全限定名 / Fully qualified name of the agent class</param>
    /// <param name="method">方法声明语法节点 / Method declaration syntax node</param>
    /// <param name="mth">方法元数据 / Method metadata</param>
    private static void ReportAttributeViolations(SourceProductionContext context, string fullName, MethodDeclarationSyntax method, MethodInfoData mth)
    {
        if (mth.IsThreadSafe && mth.HasTimeout)
        {
            context.LogError($"{fullName}.{method.Identifier.Text}无法为标记【{ThreadSafeAttributeName}】的函数指定超时时间");
        }

        if (mth.IsThreadSafe && !mth.Discard)
        {
            context.LogWarning($"{fullName}.{method.Identifier.Text}【{ThreadSafeAttributeName}】注解需配合【{DiscardAttributeName}】使用，单独使用时不会生成包装器，方法将被跳过");
        }

        if (!mth.IsApi && !mth.Discard && mth.HasTimeout)
        {
            context.LogError($"{fullName}.{method.Identifier.Text}【{TimeOutAttributeName}】注解只能配合【Api】或【{DiscardAttributeName}】使用");
        }
    }

    /// <summary>
    /// 判断方法是否需要生成包装器（排除无 Api/Discard/ThreadSafe 标记，以及 ThreadSafe 但非 Discard 的方法）。
    /// </summary>
    /// <remarks>
    /// Determines whether the method needs a generated wrapper (excluding methods without Api/Discard/ThreadSafe, and ThreadSafe-but-not-Discard methods).
    /// </remarks>
    /// <param name="mth">方法元数据 / Method metadata</param>
    /// <returns>如果需要生成包装器则为 <c>true</c>；否则为 <c>false</c> / <c>true</c> if a wrapper should be generated; otherwise <c>false</c></returns>
    private static bool ShouldWrapMethod(MethodInfoData mth)
    {
        if (!mth.IsApi && !mth.Discard && !mth.IsThreadSafe)
        {
            return false;
        }

        if (mth.IsThreadSafe && !mth.Discard)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// 报告契约相关的诊断错误（非 ThreadSafe 的 Api 必须为异步、标记 Api/ThreadSafe/Discard 必须申明为 virtual），
    /// 并对无法生成合法包装体的形态（ref/out/in 参数、Discard 返回类型契约不满足）置阻断标记。
    /// </summary>
    /// <remarks>
    /// Reports diagnostics for contract violations (non-ThreadSafe Api must be async; Api/ThreadSafe/Discard must be declared virtual)
    /// and marks blocking violations (ref/out/in parameters; Discard return-type contract) that can never yield compilable wrappers.
    /// </remarks>
    /// <param name="context">源代码生成上下文 / Source production context</param>
    /// <param name="fullName">代理类完全限定名 / Fully qualified name of the agent class</param>
    /// <param name="method">方法声明语法节点 / Method declaration syntax node</param>
    /// <param name="mth">方法元数据 / Method metadata</param>
    private static void ReportContractViolations(SourceProductionContext context, string fullName, MethodDeclarationSyntax method, MethodInfoData mth)
    {
        if (mth.IsApi && !mth.IsThreadSafe && !mth.ReturnType.Contains("Task"))
        {
            context.LogError($"{fullName}.{method.Identifier.Text}, 非【{ThreadSafeAttributeName}】的【Api】接口只能是异步函数");
        }

        if ((mth.IsApi || mth.Discard || mth.IsThreadSafe) && !mth.IsVirtual)
        {
            context.LogError($"{fullName}.{method.Identifier.Text}标记了【AsyncApi】【{ThreadSafeAttributeName}】【{DiscardAttributeName}】注解的函数必须申明为virtual");
        }

        // 包装体以 lambda 捕获转发参数：ref/out/in 参数无法跨 Actor 队列转发引用（生成即 CS1628），显式拒绝
        foreach (var parameter in method.ParameterList.Parameters)
        {
            if (parameter.Modifiers.Any(modifier => modifier.Text == "ref" || modifier.Text == "out" || modifier.Text == "in"))
            {
                context.LogError($"{fullName}.{method.Identifier.Text}代理方法不允许 ref/out/in 参数（Wrapper 无法跨 Actor 队列转发引用）");
                mth.HasBlockingViolation = true;
            }
        }

        if (mth.Discard)
        {
            // 类型名取最后一段比较：System.Threading.Tasks.Task 等全限定写法与短名等价，不误报；
            // Task<T>/ValueTask<T> 仍拒绝——生成体契约是「返回 Xxx.CompletedTask」，泛型任务无该静态字段
            var returnTypeName = mth.ReturnType;
            var lastSegmentStart = returnTypeName.LastIndexOf('.');
            if (lastSegmentStart >= 0)
            {
                returnTypeName = returnTypeName.Substring(lastSegmentStart + 1);
            }

            var isPlainTask = string.Equals(returnTypeName, nameof(Task), StringComparison.Ordinal)
                              || string.Equals(returnTypeName, nameof(ValueTask), StringComparison.Ordinal);
            if (!isPlainTask)
            {
                context.LogError($"{fullName}.{method.Identifier.Text}只有返回值为Task类型或ValueTask类型才能添加【{DiscardAttributeName}】注解");
                mth.HasBlockingViolation = true;
            }
        }
    }

    /// <summary>
    /// 将虚方法收集为包装方法：填充签名与参数元数据并加入代理信息的方法列表（校验已由 ReportContractViolations 先行完成）。
    /// </summary>
    /// <remarks>
    /// Collects a virtual method as a wrapped method: populates signature and parameter metadata and adds it to the agent info's method list
    /// (validation already happened up front in ReportContractViolations).
    /// </remarks>
    /// <param name="info">代理信息 / Agent info</param>
    /// <param name="context">源代码生成上下文 / Source production context</param>
    /// <param name="fullName">代理类完全限定名 / Fully qualified name of the agent class</param>
    /// <param name="method">方法声明语法节点 / Method declaration syntax node</param>
    /// <param name="mth">方法元数据 / Method metadata</param>
    private static void AddWrappedMethod(AgentInfo info, SourceProductionContext context, string fullName, MethodDeclarationSyntax method, MethodInfoData mth)
    {
        mth.Name = method.Identifier.Text;
        mth.ParamDeclare = method.ParameterList.ToString();
        mth.Constraint = method.ConstraintClauses.ToString();
        mth.Typeparams = method.TypeParameterList?.ToString();
        foreach (var p in method.ParameterList.Parameters)
        {
            mth.Params.Add(p.Identifier.Text);
        }

        info.Methods.Add(mth);
    }

    /// <summary>
    /// 组件代理基类名集合（StateComponentAgent、FuncComponentAgent、QueryComponentAgent、BaseComponentAgent）。
    /// </summary>
    /// <remarks>
    /// Set of component agent base class names (StateComponentAgent, FuncComponentAgent, QueryComponentAgent, BaseComponentAgent).
    /// </remarks>
    private static readonly HashSet<string> ComponentAgentBaseNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "StateComponentAgent",
        "FuncComponentAgent",
        "QueryComponentAgent",
        "BaseComponentAgent",
    };

    /// <summary>
    /// 判断类声明是否为组件代理类（继承自 StateComponentAgent、FuncComponentAgent、QueryComponentAgent 或 BaseComponentAgent）。
    /// </summary>
    /// <remarks>
    /// Determines whether the class declaration is a component agent (inheriting from StateComponentAgent, FuncComponentAgent, QueryComponentAgent, or BaseComponentAgent).
    /// </remarks>
    /// <param name="source">要检查的类声明语法节点 / Class declaration syntax node to check</param>
    /// <returns>如果是组件代理类则为 <c>true</c>；否则为 <c>false</c> / <c>true</c> if it is a component agent; otherwise <c>false</c></returns>
    private static bool IsCompAgent(ClassDeclarationSyntax source)
    {
        if (source.BaseList == null)
        {
            return false;
        }

        return source.BaseList.Types.Any(baseTypeSyntax =>
        {
            string identifier;
            if (baseTypeSyntax.Type is GenericNameSyntax genericName)
            {
                identifier = genericName.Identifier.Text;
            }
            else if (baseTypeSyntax.Type is IdentifierNameSyntax identifierName)
            {
                identifier = identifierName.Identifier.Text;
            }
            else
            {
                // QualifiedNameSyntax 等场景：取最右段标识符，并去掉泛型参数后缀
                var raw = baseTypeSyntax.Type.ToString();
                var dot = raw.LastIndexOf('.');
                var rightPart = dot >= 0 ? raw.Substring(dot + 1) : raw;
                var angleBracket = rightPart.IndexOf('<');
                identifier = angleBracket >= 0 ? rightPart.Substring(0, angleBracket) : rightPart;
            }

            return ComponentAgentBaseNames.Contains(identifier);
        });
    }
}
