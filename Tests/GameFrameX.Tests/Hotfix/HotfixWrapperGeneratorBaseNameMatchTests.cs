// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
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
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:  https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com
//  ==========================================================================================

using System.Collections.Generic;
using System.Linq;
using System.Text;
using GameFrameX.Hotfix.WrapperGenerator.Agent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace GameFrameX.Tests.Hotfix;

/// <summary>
/// <see cref="HotfixWrapperGenerator"/> 组件代理基类识别测试：基类名精确匹配四个组件代理基类
/// （含泛型与限定名形态），基类名仅包含子串（如 NonStateComponentAgent）的类不被识别为组件代理。
/// Tests for component agent base type recognition in <see cref="HotfixWrapperGenerator"/>: base names
/// must match the four component agent base classes exactly (including generic and qualified forms), while
/// classes whose base names merely contain the substring (e.g. NonStateComponentAgent) are not recognized.
/// </summary>
public sealed class HotfixWrapperGeneratorBaseNameMatchTests
{
    /// <summary>
    /// 测试源码：正例为精确基类（泛型 / 非泛型 / 限定名），负例为仅含子串的自定义基类。
    /// Test source: positives use exact base names (generic / non-generic / qualified), negatives use
    /// custom base names that merely contain the substring.
    /// </summary>
    private const string TestSource = @"
namespace GameFrameX.Hotfix.Player
{
    // 负例基类：名称仅包含组件代理基类名子串，不属于四个基类之一
    public class NonStateComponentAgent { }
    public class MyFuncComponentAgentFactory { }
    public class MyQueryComponentAgentHelper { }
    public class MyBaseComponentAgentFactory { }

    public class TrapAgent : NonStateComponentAgent
    {
        public virtual System.Threading.Tasks.Task<int> GetValueAsync() { return null; }
    }

    public class FactoryTrapAgent : MyFuncComponentAgentFactory
    {
        public virtual System.Threading.Tasks.Task<int> GetValueAsync() { return null; }
    }

    public class HelperTrapAgent : MyQueryComponentAgentHelper
    {
        public virtual System.Threading.Tasks.Task<int> GetValueAsync() { return null; }
    }

    public class BaseTrapAgent : MyBaseComponentAgentFactory
    {
        public virtual System.Threading.Tasks.Task<int> GetValueAsync() { return null; }
    }

    // 正例：泛型基类（GenericNameSyntax）
    public class PositiveAgent : StateComponentAgent<object>
    {
        [Service]
        public virtual System.Threading.Tasks.Task<int> GetPositiveValueAsync() { return null; }
    }

    // 正例：非泛型基类（IdentifierNameSyntax）
    public class SimpleAgent : FuncComponentAgent
    {
        public virtual System.Threading.Tasks.Task<int> GetSimpleValueAsync() { return null; }
    }

    // 正例：限定名基类（QualifiedNameSyntax 兜底分支）
    public class QualifiedAgent : GameFrameX.Agents.QueryComponentAgent
    {
        public virtual System.Threading.Tasks.Task<int> GetQualifiedValueAsync() { return null; }
    }
}
";

    /// <summary>
    /// 基类名与四个组件代理基类精确一致（泛型 / 非泛型 / 限定名形态）时生成包装器。
    /// Wrapper generation must happen when base names exactly equal one of the four component agent
    /// base classes (generic / non-generic / qualified forms).
    /// </summary>
    [Fact]
    public void IsCompAgent_ExactBaseName_GeneratesWrapper()
    {
        var generatedSources = RunGenerator();

        Assert.Contains(generatedSources, text => text.Contains("class PositiveAgentWrapper"));
        Assert.Contains(generatedSources, text => text.Contains("class SimpleAgentWrapper"));
        Assert.Contains(generatedSources, text => text.Contains("class QualifiedAgentWrapper"));
    }

    /// <summary>
    /// 基类名仅包含组件代理基类名子串（NonStateComponentAgent 等）时不生成包装器。
    /// No wrapper may be generated when base names merely contain the substring of a component agent
    /// base class name (NonStateComponentAgent etc.).
    /// </summary>
    [Fact]
    public void IsCompAgent_SubstringOnlyBaseName_DoesNotGenerateWrapper()
    {
        var generatedSources = RunGenerator();

        Assert.DoesNotContain(generatedSources, text => text.Contains("TrapAgentWrapper"));
        Assert.DoesNotContain(generatedSources, text => text.Contains("FactoryTrapAgentWrapper"));
        Assert.DoesNotContain(generatedSources, text => text.Contains("HelperTrapAgentWrapper"));
        Assert.DoesNotContain(generatedSources, text => text.Contains("BaseTrapAgentWrapper"));
    }

    /// <summary>
    /// 用 <see cref="HotfixWrapperGenerator"/> 驱动测试源码，返回全部生成源码文本。
    /// Drives <see cref="HotfixWrapperGenerator"/> over the test source and returns all generated source texts.
    /// </summary>
    /// <returns>生成源码文本列表 / The list of generated source texts</returns>
    private static List<string> RunGenerator()
    {
        var compilation = CSharpCompilation.Create(
            "HotfixWrapperGeneratorBaseNameMatchTestsCompilation",
            new[] { CSharpSyntaxTree.ParseText(SourceText.From(TestSource, Encoding.UTF8)) });

        CSharpGeneratorDriver driver = CSharpGeneratorDriver.Create(new HotfixWrapperGenerator());
        var runResult = driver.RunGenerators(compilation).GetRunResult();

        return runResult.GeneratedTrees.Select(tree => tree.GetText().ToString()).ToList();
    }
}
