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
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:    https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using GameFrameX.Hotfix.WrapperGenerator.Agent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace GameFrameX.Tests.Hotfix;

/// <summary>
/// 验证生成的 Wrapper 类携带 GeneratedCodeAttribute 标记与 ".g.cs" 文件后缀（C430）。
/// </summary>
/// <remarks>
/// Verifies that generated wrapper classes carry the GeneratedCodeAttribute marker and the ".g.cs" file suffix:
/// the two semantic signals the architecture analyzers use to exempt genuine generator output (CR-L02).
/// </remarks>
public sealed class HotfixWrapperGeneratorGeneratedCodeMarkerTests
{
    private const string AgentSource = """
        namespace GameFrameX.Hotfix.Player
        {
            public class MarkerComponentAgent : BaseComponentAgent
            {
                [Service]
                public virtual Task<int> QueryAsync()
                {
                    return 0;
                }
            }
        }
        """;

    [Fact]
    public void Partial_agent_wrapper_pieces_emit_marker_exactly_once()
    {
        var partialAgentSource = """
            namespace GameFrameX.Hotfix.Player
            {
                public partial class PartialComponentAgent : BaseComponentAgent
                {
                    [Service]
                    public virtual Task<int> QueryAsync()
                    {
                        return 0;
                    }
                }

                public partial class PartialComponentAgent
                {
                    [Service]
                    public virtual Task<int> LoadAsync()
                    {
                        return 0;
                    }
                }
            }
            """;
        var compilation = CSharpCompilation.Create(
            "GeneratorTest",
            new[] { CSharpSyntaxTree.ParseText(partialAgentSource) },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) });
        var driver = CSharpGeneratorDriver.Create(new HotfixWrapperGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var wrapperTrees = outputCompilation.SyntaxTrees
            .Skip(1)
            .Where(tree => tree.ToString().Contains("public partial class PartialComponentAgentWrapper"))
            .ToArray();
        // 分部代理的两个落盘分部都生成 Wrapper 声明，但标记只允许出现一次：
        // GeneratedCodeAttribute 的 AllowMultiple 为 false，重复特性会令宿主编译失败（CS0579）
        Assert.Equal(2, wrapperTrees.Length);
        var markerCount = wrapperTrees.Sum(tree => CountMarkerOccurrences(tree.ToString()));
        Assert.Equal(1, markerCount);
    }

    [Fact]
    public void Partial_agent_with_method_less_first_piece_still_emits_marker_once()
    {
        var partialAgentSource = """
            namespace GameFrameX.Hotfix.Player
            {
                public partial class PartialComponentAgent : BaseComponentAgent
                {
                    public virtual Task<int> UnmarkedAsync()
                    {
                        return 0;
                    }
                }

                public partial class PartialComponentAgent
                {
                    [Service]
                    public virtual Task<int> QueryAsync()
                    {
                        return 0;
                    }
                }
            }
            """;
        var compilation = CSharpCompilation.Create(
            "GeneratorTest",
            new[] { CSharpSyntaxTree.ParseText(partialAgentSource) },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) });
        var driver = CSharpGeneratorDriver.Create(new HotfixWrapperGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var wrapperTrees = outputCompilation.SyntaxTrees
            .Skip(1)
            .Where(tree => tree.ToString().Contains("public partial class PartialComponentAgentWrapper"))
            .ToArray();
        // 首个声明分部无可包装方法不落盘：标记必须随唯一落盘分部出现（按落盘计数而非声明序号）
        Assert.Single(wrapperTrees);
        Assert.Equal(1, CountMarkerOccurrences(wrapperTrees[0].ToString()));
    }

    /// <summary>
    /// 统计生成文本中标记特性行的出现次数。
    /// </summary>
    /// <param name="generatedText">生成源码文本 / Generated source text</param>
    /// <returns>标记行出现次数 / Occurrence count of the marker line</returns>
    private static int CountMarkerOccurrences(string generatedText)
    {
        var marker = "[global::System.CodeDom.Compiler.GeneratedCodeAttribute(\"GameFrameX.Hotfix.WrapperGenerator\", \"1.0\")]";
        var count = 0;
        var index = 0;
        while ((index = generatedText.IndexOf(marker, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += marker.Length;
        }

        return count;
    }

    [Fact]
    public void Generated_wrapper_class_carries_generated_code_marker_attribute()
    {
        var (generatedText, diagnostics) = RunWrapperGenerator();

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        // 标记行精确匹配：tool 名与版本是分析器豁免判定的同值契约
        Assert.Contains("[global::System.CodeDom.Compiler.GeneratedCodeAttribute(\"GameFrameX.Hotfix.WrapperGenerator\", \"1.0\")]", generatedText);
    }

    [Fact]
    public void Generated_wrapper_trees_use_g_cs_file_suffix()
    {
        var (trees, diagnostics) = RunWrapperGeneratorReturningTrees();

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.NotEmpty(trees);
        Assert.All(trees, tree => Assert.True(
            tree.FilePath.EndsWith(".g.cs", StringComparison.Ordinal),
            $"生成树文件名必须以 .g.cs 结尾：{tree.FilePath}"));
    }

    /// <summary>
    /// 以测试源驱动 Wrapper 生成器并返回全部生成源码与生成期诊断。
    /// </summary>
    /// <returns>生成源码合并文本与诊断集合 / Combined generated source text and diagnostics</returns>
    private static (string GeneratedText, Diagnostic[] Diagnostics) RunWrapperGenerator()
    {
        var (trees, diagnostics) = RunWrapperGeneratorReturningTrees();
        return (string.Join("\n", trees.Select(tree => tree.ToString())), diagnostics);
    }

    /// <summary>
    /// 以测试源驱动 Wrapper 生成器并返回全部生成语法树与生成期诊断。
    /// </summary>
    /// <returns>生成语法树集合与诊断集合 / Generated syntax trees and diagnostics</returns>
    private static (SyntaxTree[] Trees, Diagnostic[] Diagnostics) RunWrapperGeneratorReturningTrees()
    {
        var compilation = CSharpCompilation.Create(
            "GeneratorTest",
            new[] { CSharpSyntaxTree.ParseText(AgentSource) },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) });

        var driver = CSharpGeneratorDriver.Create(new HotfixWrapperGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        return (outputCompilation.SyntaxTrees.Skip(1).ToArray(), diagnostics.ToArray());
    }
}
