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
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes and liabilities arising from secondary development based on this project
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

using System.Linq;
using System.Threading.Tasks;
using GameFrameX.Hotfix.WrapperGenerator.Agent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace GameFrameX.Tests.Hotfix;

/// <summary>
/// 验证 Hotfix Wrapper 生成器的空 wrapper 跳过、超时缺省语义、重复运行稳定性与并发驱动安全（C357）。
/// </summary>
/// <remarks>
/// Verifies the Hotfix wrapper generator's empty-wrapper skipping, timeout default semantics, repeated-run stability, and concurrent-driver safety (C357).
/// </remarks>
public sealed class HotfixWrapperGeneratorIncrementalSafetyTests
{
    private const string EmptyAgentSource = """
        namespace GameFrameX.Hotfix.Player
        {
            public class EmptyComponentAgent : BaseComponentAgent
            {
                public void PlainMethod()
                {
                }
            }
        }
        """;

    private const string AgentSource = """
        namespace GameFrameX.Hotfix.Player
        {
            public class SafetyComponentAgent : BaseComponentAgent
            {
                [Service]
                public virtual Task<int> QueryAsync(int value)
                {
                    return Task.FromResult(value);
                }

                [Discard]
                [TimeOut(3000)]
                public virtual Task SaveAsync()
                {
                    return Task.CompletedTask;
                }
            }
        }
        """;

    [Fact]
    public void Agent_without_wrappable_methods_produces_no_wrapper()
    {
        var (generatedText, diagnostics) = RunWrapperGenerator(EmptyAgentSource);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        // 无可包装方法的代理：不产出任何 wrapper 分部类
        Assert.DoesNotContain("Wrapper", generatedText);
    }

    [Fact]
    public void Unconfigured_timeout_omits_the_timeout_argument()
    {
        var (generatedText, diagnostics) = RunWrapperGenerator(AgentSource);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains("EnqueueAsync(()=>base.QueryAsync(value), chainId, false)", generatedText);
        // 未显式配置 [TimeOut]：不再发射 int.MaxValue 超时实参，走 EnqueueAsync 框架默认值
        Assert.DoesNotContain("2147483647", generatedText);
    }

    [Fact]
    public void Explicit_timeout_is_still_emitted()
    {
        var (generatedText, diagnostics) = RunWrapperGenerator(AgentSource);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains("EnqueueAsync(()=>base.SaveAsync(), callChainId, true, 3000)", generatedText);
    }

    [Fact]
    public void Repeated_run_regenerates_identical_output_without_duplicates()
    {
        var compilation = CreateCompilation(AgentSource);
        var driver = CSharpGeneratorDriver.Create(new HotfixWrapperGenerator());
        driver = (CSharpGeneratorDriver)driver.RunGenerators(compilation);
        var firstRun = driver.GetRunResult();
        var firstText = string.Join("\n", firstRun.GeneratedTrees.Select(tree => tree.ToString()));

        // 追加无关源文件再次运行：既有 wrapper 输出逐字节复用，不重复产出、不重复注册
        var updatedCompilation = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
            """
            namespace GameFrameX.Hotfix.Unrelated
            {
                public class UnrelatedClass
                {
                }
            }
            """));
        driver = (CSharpGeneratorDriver)driver.RunGenerators(updatedCompilation);
        var secondRun = driver.GetRunResult();
        var secondText = string.Join("\n", secondRun.GeneratedTrees.Select(tree => tree.ToString()));

        Assert.Contains("SafetyComponentAgentWrapper", secondText);
        Assert.Equal(firstText.Replace("\r", string.Empty), secondText.Replace("\r", string.Empty));
        // wrapper 类声明只出现一次（不重复注册）
        Assert.Equal(1, CountOccurrences(secondText, "public partial class SafetyComponentAgentWrapper"));
    }

    [Fact]
    public async Task Concurrent_driver_runs_produce_correct_output_each()
    {
        var tasks = Enumerable.Range(0, 4).Select(_ => Task.Run(() => RunWrapperGenerator(AgentSource))).ToArray();
        var results = await Task.WhenAll(tasks);

        foreach (var (generatedText, diagnostics) in results)
        {
            Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            // 局部 StringBuilder：并发驱动各自产出完整且一致的 wrapper 源码
            Assert.Contains("public partial class SafetyComponentAgentWrapper : SafetyComponentAgent", generatedText);
            Assert.Contains("EnqueueAsync(()=>base.QueryAsync(value), chainId, false)", generatedText);
            Assert.Contains("return Task.CompletedTask;", generatedText);
        }
    }

    private static int CountOccurrences(string text, string pattern)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(pattern, index, System.StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += pattern.Length;
        }

        return count;
    }

    /// <summary>
    /// 以测试源驱动 Wrapper 生成器并返回全部生成源码与生成期诊断。
    /// </summary>
    /// <returns>生成源码合并文本与诊断集合 / Combined generated source text and diagnostics</returns>
    private static (string GeneratedText, Diagnostic[] Diagnostics) RunWrapperGenerator(string source)
    {
        var driver = CSharpGeneratorDriver.Create(new HotfixWrapperGenerator());
        driver.RunGeneratorsAndUpdateCompilation(CreateCompilation(source), out var outputCompilation, out var diagnostics);

        var generatedText = string.Join("\n", outputCompilation.SyntaxTrees.Skip(1).Select(tree => tree.ToString()));
        return (generatedText, diagnostics.ToArray());
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        return CSharpCompilation.Create(
            "GeneratorTest",
            new[] { CSharpSyntaxTree.ParseText(source) },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) });
    }
}
