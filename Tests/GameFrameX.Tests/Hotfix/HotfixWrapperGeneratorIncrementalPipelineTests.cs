// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or to infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:    https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
// ==========================================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using GameFrameX.Hotfix.WrapperGenerator.Agent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace GameFrameX.Tests.Hotfix;

/// <summary>
/// 验证 Hotfix Wrapper 生成器增量管线（C432）：按「代理类声明 → 输出」粒度注册，经
/// <c>WithTrackingName</c> 暴露的 TrackedSteps 断言逐声明的缓存/重算行为——编辑单个代理类只重算
/// 该代理的 wrapper 且仅其输出回调重跑；输出等值编辑与无关文件变更不触发任何输出重跑；partial
/// 部件独立产出各自的 wrapper 文件。
/// Verifies the Hotfix wrapper generator's incremental pipeline (C432): outputs are registered per agent
/// class declaration, and the tracking-name-exposed TrackedSteps assert per-declaration cache/recompute
/// behavior—editing one agent recomputes only its wrapper and reruns only its output callback; output-equal
/// edits and unrelated file changes rerun no output callbacks; partial parts each produce their own wrapper file.
/// </summary>
public sealed class HotfixWrapperGeneratorIncrementalPipelineTests
{
    /// <summary>
    /// 生成器输出步骤的跟踪名（与 <see cref="HotfixWrapperGenerator"/> 内部常量一致）。
    /// Tracking name of the generator's output step (matching the internal constant).
    /// </summary>
    private const string WrapperOutputTrackingName = "HotfixWrapperOutput";

    /// <summary>
    /// 注册输出步骤的自动跟踪名（Roslyn 对 <c>RegisterSourceOutput</c> 的内置命名）。
    /// Auto tracking name of the registered output step (Roslyn's built-in name for <c>RegisterSourceOutput</c>).
    /// </summary>
    private const string SourceOutputTrackingName = "SourceOutput";

    private const string FirstAgentSource = """
        namespace GameFrameX.Hotfix.Player
        {
            public class FirstComponentAgent : BaseComponentAgent
            {
                [Service]
                public virtual System.Threading.Tasks.Task<int> QueryFirstAsync(int value)
                {
                    return System.Threading.Tasks.Task.FromResult(value);
                }
            }
        }
        """;

    private const string SecondAgentSource = """
        namespace GameFrameX.Hotfix.Server
        {
            public class SecondComponentAgent : BaseComponentAgent
            {
                [Service]
                public virtual System.Threading.Tasks.Task<int> QuerySecondAsync(int value)
                {
                    return System.Threading.Tasks.Task.FromResult(value);
                }
            }
        }
        """;

    private const string EditedSecondAgentSource = """
        namespace GameFrameX.Hotfix.Server
        {
            public class SecondComponentAgent : BaseComponentAgent
            {
                [Service]
                public virtual System.Threading.Tasks.Task<int> QuerySecondAsync(int value)
                {
                    return System.Threading.Tasks.Task.FromResult(value);
                }

                [Service]
                public virtual System.Threading.Tasks.Task<int> QuerySecondEditedAsync(int value)
                {
                    return System.Threading.Tasks.Task.FromResult(value);
                }
            }
        }
        """;

    private const string OutputEqualEditFirstAgentSource = """
        namespace GameFrameX.Hotfix.Player
        {
            public class FirstComponentAgent : BaseComponentAgent
            {
                [Service]
                public virtual System.Threading.Tasks.Task<int> QueryFirstAsync(int value)
                {
                    return System.Threading.Tasks.Task.FromResult(value);
                }

                public void PlainMethodNotWrapped()
                {
                }
            }
        }
        """;

    private const string PartialAgentSource = """
        namespace GameFrameX.Hotfix.Player
        {
            public partial class PartialComponentAgent : BaseComponentAgent
            {
                [Service]
                public virtual System.Threading.Tasks.Task<int> QueryPartOneAsync(int value)
                {
                    return System.Threading.Tasks.Task.FromResult(value);
                }
            }

            public partial class PartialComponentAgent : BaseComponentAgent
            {
                [Service]
                public virtual System.Threading.Tasks.Task<int> QueryPartTwoAsync(int value)
                {
                    return System.Threading.Tasks.Task.FromResult(value);
                }
            }
        }
        """;

    /// <summary>
    /// 编辑单个代理类所在文件（新增被包装方法）后：仅该代理的声明重算（Modified）且仅其输出回调
    /// 重跑（SourceOutput 恰一个 Modified），另一代理的输出命中缓存（Cached）——逐声明输出注册的增量契约。
    /// After editing the file of one agent (adding a wrapped method): only that agent's declaration
    /// recomputes (Modified) and only its output callback reruns (exactly one Modified in SourceOutput),
    /// while the other agent's output hits the cache (Cached)—the per-declaration incremental contract.
    /// </summary>
    [Fact]
    public void Editing_one_agent_file_reruns_only_that_agents_wrapper()
    {
        var firstTree = ParseTree(FirstAgentSource, "FirstComponentAgent.cs");
        var secondTree = ParseTree(SecondAgentSource, "SecondComponentAgent.cs");
        var compilation = CreateCompilation(firstTree, secondTree);
        var driver = CreateTrackingDriver();
        driver = (CSharpGeneratorDriver)driver.RunGenerators(compilation);

        var editedTree = ParseTree(EditedSecondAgentSource, "SecondComponentAgent.cs");
        var updatedCompilation = compilation.ReplaceSyntaxTree(secondTree, editedTree);
        driver = (CSharpGeneratorDriver)driver.RunGenerators(updatedCompilation);

        // 被编辑代理声明：输入变化且产出新值 → 恰一个 Modified；其余声明不得重算出新值
        var wrapperReasons = GetTrackedReasons(driver, WrapperOutputTrackingName);
        Assert.Equal(2, wrapperReasons.Count);
        Assert.Equal(1, wrapperReasons.Count(reason => reason == IncrementalStepRunReason.Modified));
        Assert.Equal(1, wrapperReasons.Count(reason => reason == IncrementalStepRunReason.Cached || reason == IncrementalStepRunReason.Unchanged));

        // 注册输出步骤：只有被编辑代理的输出回调重跑，另一代理命中缓存
        var outputReasons = GetTrackedReasons(driver, SourceOutputTrackingName);
        Assert.Equal(2, outputReasons.Count);
        Assert.Equal(1, outputReasons.Count(reason => reason == IncrementalStepRunReason.Modified));
        Assert.Equal(1, outputReasons.Count(reason => reason == IncrementalStepRunReason.Cached));

        var generatedText = JoinGeneratedText(driver);
        Assert.Contains("QueryFirstAsync", generatedText);
        Assert.Contains("QuerySecondEditedAsync", generatedText);
    }

    /// <summary>
    /// 对代理类做输出等值编辑（新增不被包装的普通方法）后：该声明重算但产出文本等值（Unchanged），
    /// 全部输出回调命中缓存（SourceOutput 全 Cached，无 Modified），生成文本不变。
    /// After an output-equal edit to an agent (adding a plain method that is not wrapped): that declaration
    /// recomputes but produces an equal text (Unchanged), all output callbacks hit the cache (SourceOutput
    /// all Cached, no Modified), and the generated text is unchanged.
    /// </summary>
    [Fact]
    public void Output_equal_edit_reports_unchanged_without_output_rerun()
    {
        var firstTree = ParseTree(FirstAgentSource, "FirstComponentAgent.cs");
        var secondTree = ParseTree(SecondAgentSource, "SecondComponentAgent.cs");
        var compilation = CreateCompilation(firstTree, secondTree);
        var driver = CreateTrackingDriver();
        driver = (CSharpGeneratorDriver)driver.RunGenerators(compilation);
        var firstRunText = JoinGeneratedText(driver);

        var editedTree = ParseTree(OutputEqualEditFirstAgentSource, "FirstComponentAgent.cs");
        var updatedCompilation = compilation.ReplaceSyntaxTree(firstTree, editedTree);
        driver = (CSharpGeneratorDriver)driver.RunGenerators(updatedCompilation);

        // 普通方法不入包装面：生成文本与首轮逐字符等值 → Unchanged（WithComparer 文本缓存生效）
        var wrapperReasons = GetTrackedReasons(driver, WrapperOutputTrackingName);
        Assert.Equal(2, wrapperReasons.Count);
        Assert.Contains(IncrementalStepRunReason.Unchanged, wrapperReasons);
        Assert.DoesNotContain(IncrementalStepRunReason.Modified, wrapperReasons);

        var outputReasons = GetTrackedReasons(driver, SourceOutputTrackingName);
        Assert.Equal(2, outputReasons.Count);
        Assert.All(outputReasons, reason => Assert.Equal(IncrementalStepRunReason.Cached, reason));

        Assert.Equal(NormalizeLineEndings(firstRunText), NormalizeLineEndings(JoinGeneratedText(driver)));
    }

    /// <summary>
    /// 追加与代理无关的语法树后：没有任何声明重算出新值，全部输出回调命中缓存，生成 hint 集合不变。
    /// After appending a syntax tree unrelated to agents: no declaration recomputes a new value, all output
    /// callbacks hit the cache, and the generated hint set is unchanged.
    /// </summary>
    [Fact]
    public void Unrelated_tree_addition_keeps_all_wrapper_outputs_cached()
    {
        var firstTree = ParseTree(FirstAgentSource, "FirstComponentAgent.cs");
        var secondTree = ParseTree(SecondAgentSource, "SecondComponentAgent.cs");
        var compilation = CreateCompilation(firstTree, secondTree);
        var driver = CreateTrackingDriver();
        driver = (CSharpGeneratorDriver)driver.RunGenerators(compilation);
        var firstRunHints = GetGeneratedHintNames(driver);

        var updatedCompilation = compilation.AddSyntaxTrees(ParseTree(
            """
            namespace GameFrameX.Hotfix.Unrelated
            {
                public class UnrelatedClass
                {
                }
            }
            """,
            "Unrelated.cs"));
        driver = (CSharpGeneratorDriver)driver.RunGenerators(updatedCompilation);

        var wrapperReasons = GetTrackedReasons(driver, WrapperOutputTrackingName);
        Assert.Equal(2, wrapperReasons.Count);
        Assert.DoesNotContain(IncrementalStepRunReason.Modified, wrapperReasons);
        Assert.DoesNotContain(IncrementalStepRunReason.New, wrapperReasons);

        var outputReasons = GetTrackedReasons(driver, SourceOutputTrackingName);
        Assert.Equal(2, outputReasons.Count);
        Assert.All(outputReasons, reason => Assert.Equal(IncrementalStepRunReason.Cached, reason));
        Assert.Equal(
            firstRunHints.OrderBy(hint => hint, StringComparer.Ordinal),
            GetGeneratedHintNames(driver).OrderBy(hint => hint, StringComparer.Ordinal));
    }

    /// <summary>
    /// partial 代理各部件独立产出各自的 wrapper 文件（每部件自带相同基类列表）：两份生成文件各含
    /// 本部件的被包装方法，hintName 互不相同且重复运行稳定（声明位置稳定键）。
    /// Each partial agent part independently produces its own wrapper file (each part carries the same
    /// base list): two generated files each containing its own part's wrapped method, with distinct
    /// hintNames that stay stable across repeated runs (declaration-position stable key).
    /// </summary>
    [Fact]
    public void Partial_agent_parts_each_produce_their_own_wrapper_file_with_stable_hints()
    {
        var compilation = CreateCompilation(ParseTree(PartialAgentSource, "PartialComponentAgent.cs"));
        var driver = CreateTrackingDriver();
        driver = (CSharpGeneratorDriver)driver.RunGenerators(compilation);

        var firstRunHintNames = GetGeneratedHintNames(driver);
        Assert.Equal(2, firstRunHintNames.Count);
        Assert.Equal(2, firstRunHintNames.Distinct(StringComparer.Ordinal).Count());
        Assert.All(firstRunHintNames, hintName => Assert.Matches(@".*PartialComponentAgentWrapper[0-9A-F]{16}\.g\.cs$", hintName));

        // 每个部件的方法各自进入自己部件的 wrapper 文件，不串件
        var generatedFiles = GetGeneratedFilesText(driver);
        var partOneWrapper = generatedFiles.First(file => file.Contains("QueryPartOneAsync"));
        var partTwoWrapper = generatedFiles.First(file => file.Contains("QueryPartTwoAsync"));
        Assert.DoesNotContain("QueryPartTwoAsync", partOneWrapper);
        Assert.DoesNotContain("QueryPartOneAsync", partTwoWrapper);
        Assert.All(generatedFiles, file => Assert.Contains("public partial class PartialComponentAgentWrapper : PartialComponentAgent", file));

        // 重复运行：声明位置稳定键保证 hintName 逐字节复用，输出全命中缓存
        driver = (CSharpGeneratorDriver)driver.RunGenerators(compilation);
        Assert.Equal(
            firstRunHintNames.OrderBy(hint => hint, StringComparer.Ordinal),
            GetGeneratedHintNames(driver).OrderBy(hint => hint, StringComparer.Ordinal));
        Assert.All(GetTrackedReasons(driver, SourceOutputTrackingName), reason => Assert.Equal(IncrementalStepRunReason.Cached, reason));
    }

    /// 谓词收窄守卫（C432 语义调整）：无基类列表的 partial 类即使与代理同名（同命名空间或跨命名空间）
    /// 也不进入生成面——partial 代理需每个部件自带相同基类列表才会逐部件产出 wrapper。
    /// Predicate-narrowing guard (C432 semantic adjustment): a partial class without a base list never
    /// enters the generation surface even when it shares its name with an agent (same or other
    /// namespace)—partial agents must carry the same base list on every part to get per-part wrappers.
    /// </summary>
    [Fact]
    public void Partial_class_without_base_list_never_generates_a_wrapper()
    {
        var source = """
            namespace GameFrameX.Hotfix.Player
            {
                public partial class GuardedComponentAgent : BaseComponentAgent
                {
                    [Service]
                    public virtual System.Threading.Tasks.Task<int> QueryGuardedAsync(int value)
                    {
                        return System.Threading.Tasks.Task.FromResult(value);
                    }
                }

                public partial class GuardedComponentAgent
                {
                    [Service]
                    public virtual System.Threading.Tasks.Task<int> QueryBaselessPartAsync(int value)
                    {
                        return System.Threading.Tasks.Task.FromResult(value);
                    }
                }
            }

            namespace GameFrameX.Hotfix.Server
            {
                public partial class GuardedComponentAgent
                    {
                    [Service]
                    public virtual System.Threading.Tasks.Task<int> QueryCrossNamespacePartAsync(int value)
                    {
                        return System.Threading.Tasks.Task.FromResult(value);
                    }
                }
            }
            """;
        var compilation = CreateCompilation(ParseTree(source, "GuardedComponentAgent.cs"));
        var driver = CreateTrackingDriver();
        driver = (CSharpGeneratorDriver)driver.RunGenerators(compilation);

        // 仅带基类列表的部件产出 wrapper；两个无基类部件（含同名同命名空间与跨命名空间）都不产出
        var generatedFiles = GetGeneratedFilesText(driver);
        var wrapperFile = Assert.Single(generatedFiles);
        Assert.Contains("QueryGuardedAsync", wrapperFile);
        Assert.DoesNotContain("QueryBaselessPartAsync", wrapperFile);
        Assert.DoesNotContain("QueryCrossNamespacePartAsync", wrapperFile);
    }

    /// <summary>
    /// 非 partial 代理的 hintName 保持无后缀形态（与历史输出文件名逐字节一致，C292 单射契约不变）。
    /// Non-partial agents keep the suffix-less hintName form (byte-identical to the historical output
    /// filenames; the C292 injective contract is unchanged).
    /// </summary>
    [Fact]
    public void Non_partial_agent_hint_name_has_no_position_suffix()
    {
        var compilation = CreateCompilation(ParseTree(FirstAgentSource, "FirstComponentAgent.cs"));
        var driver = CreateTrackingDriver();
        driver = (CSharpGeneratorDriver)driver.RunGenerators(compilation);

        var hintName = Assert.Single(GetGeneratedHintNames(driver));
        Assert.Equal("GameFrameX_Hotfix_Player_FirstComponentAgentWrapper.g.cs", hintName);
    }

    /// <summary>
    /// 创建开启增量步骤跟踪的驱动：TrackedSteps 断言依赖
    /// <c>trackIncrementalGeneratorSteps: true</c>（默认关闭）。
    /// Creates a driver with incremental step tracking enabled: TrackedSteps assertions require
    /// <c>trackIncrementalGeneratorSteps: true</c> (off by default).
    /// </summary>
    /// <returns>跟踪模式驱动 / The tracking-enabled driver</returns>
    private static CSharpGeneratorDriver CreateTrackingDriver()
    {
        var driverOptions = new GeneratorDriverOptions(disabledOutputs: IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true);
        return CSharpGeneratorDriver.Create(
            new ISourceGenerator[] { new HotfixWrapperGenerator().AsSourceGenerator() },
            additionalTexts: null,
            parseOptions: null,
            optionsProvider: null,
            driverOptions: driverOptions);
    }

    private static SyntaxTree ParseTree(string source, string path)
    {
        return CSharpSyntaxTree.ParseText(SourceText.From(source, Encoding.UTF8), path: path);
    }

    private static CSharpCompilation CreateCompilation(params SyntaxTree[] syntaxTrees)
    {
        return CSharpCompilation.Create(
            "HotfixWrapperGeneratorIncrementalPipelineTestsCompilation",
            syntaxTrees,
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) });
    }

    private static IReadOnlyList<IncrementalStepRunReason> GetTrackedReasons(CSharpGeneratorDriver driver, string trackingName)
    {
        var runResult = driver.GetRunResult();
        var generatorResult = Assert.Single(runResult.Results);
        return generatorResult.TrackedSteps[trackingName]
            .SelectMany(step => step.Outputs)
            .Select(output => output.Item2)
            .ToList();
    }

    private static IReadOnlyList<string> GetGeneratedHintNames(CSharpGeneratorDriver driver)
    {
        var runResult = driver.GetRunResult();
        // 生成树 FilePath 形如「生成器程序集/生成器类型/hintName」，取末段即 hintName
        return runResult.GeneratedTrees.Select(tree => Path.GetFileName(tree.FilePath)).ToList();
    }

    private static IReadOnlyList<string> GetGeneratedFilesText(CSharpGeneratorDriver driver)
    {
        var runResult = driver.GetRunResult();
        return runResult.GeneratedTrees.Select(tree => tree.ToString()).ToList();
    }

    private static string JoinGeneratedText(CSharpGeneratorDriver driver)
    {
        return string.Join("\n", GetGeneratedFilesText(driver));
    }

    private static string NormalizeLineEndings(string text)
    {
        return text.Replace("\r", string.Empty);
    }
}
