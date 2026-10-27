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
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using GameFrameX.Hotfix.WrapperGenerator.Agent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace GameFrameX.Tests.Hotfix;

/// <summary>
/// 验证 Hotfix Wrapper 生成器对同一属性块内多特性组合与相似名特性的识别。
/// </summary>
/// <remarks>
/// Verifies the Hotfix wrapper generator's recognition of multiple attributes combined in one attribute list and of lookalike attribute names.
/// </remarks>
public sealed class HotfixWrapperGeneratorAttributeParsingTests
{
    private const string AgentSource = """
        namespace GameFrameX.Hotfix.Player
        {
            public class AttributeParsingComponentAgent : BaseComponentAgent
            {
                [Service, Discard]
                public virtual async Task FlushAsync()
                {
                    return null;
                }

                [Discard, TimeOut(3000)]
                public virtual Task SaveAsync()
                {
                    return null;
                }

                [FooService]
                public virtual Task FooMethod()
                {
                    return null;
                }

                [ServiceAttribute]
                public virtual Task<int> QueryAsync()
                {
                    return 0;
                }
            }
        }
        """;

    [Fact]
    public void Combined_service_and_discard_attributes_are_both_recognized()
    {
        var (generatedText, diagnostics) = RunWrapperGenerator();

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        // Discard 分支执行：async 修饰符被剥除、生成丢弃体（true 入队 + CompletedTask），而非等待调用体
        Assert.Contains("public override Task FlushAsync()", generatedText);
        Assert.Contains("EnqueueAsync(()=>base.FlushAsync(), callChainId, true, 2147483647)", generatedText);
        Assert.Contains("return Task.CompletedTask;", generatedText);
    }

    [Fact]
    public void Timeout_is_parsed_from_the_matched_attribute_in_a_combined_list()
    {
        var (generatedText, diagnostics) = RunWrapperGenerator();

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        // 超时值取自命中的 TimeOut 特性自身参数，而非属性列表首个特性
        Assert.Contains("EnqueueAsync(()=>base.SaveAsync(), callChainId, true, 3000)", generatedText);
    }

    [Fact]
    public void Lookalike_attribute_name_is_not_matched()
    {
        var (generatedText, diagnostics) = RunWrapperGenerator();

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        // [FooService] 仅含 Service 子串，不得误判为 Api：不生成包装方法
        Assert.DoesNotContain("FooMethod", generatedText);
    }

    [Fact]
    public void Explicit_attribute_suffix_is_recognized()
    {
        var (generatedText, diagnostics) = RunWrapperGenerator();

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        // [ServiceAttribute] 显式尾缀书写与 [Service] 等价，仍识别为 Api（false 入队等待调用体）
        Assert.Contains("EnqueueAsync(()=>base.QueryAsync(), chainId, false, 2147483647)", generatedText);
    }

    /// <summary>
    /// 以测试源驱动 Wrapper 生成器并返回全部生成源码与生成期诊断。
    /// </summary>
    /// <returns>生成源码合并文本与诊断集合 / Combined generated source text and diagnostics</returns>
    private static (string GeneratedText, Diagnostic[] Diagnostics) RunWrapperGenerator()
    {
        var compilation = CSharpCompilation.Create(
            "GeneratorTest",
            new[] { CSharpSyntaxTree.ParseText(AgentSource) },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) });

        var driver = CSharpGeneratorDriver.Create(new HotfixWrapperGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        var generatedText = string.Join("\n", outputCompilation.SyntaxTrees.Skip(1).Select(tree => tree.ToString()));
        return (generatedText, diagnostics.ToArray());
    }
}
