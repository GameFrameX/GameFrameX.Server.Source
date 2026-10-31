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
//   侵犯公民合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes and liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
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

using System.Linq;
using GameFrameX.Hotfix.WrapperGenerator.Agent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace GameFrameX.Tests.Hotfix;

/// <summary>
/// Wrapper 生成器契约违规测试：ref/out/in 参数方法被诊断拒绝且不生成包装体、Discard 的 ValueTask 方法经
/// 异步 lambda 入队（绑定 EnqueueAsync(Func&lt;Task&gt;) 重载）、跨命名空间同简单名类不并入代理、
/// 嵌套代理的命名空间镜像不含「+」且 using 不含类型名。
/// </summary>
/// <remarks>
/// Contract violation tests for the wrapper generator: methods with ref/out/in parameters are rejected by a
/// diagnostic with no generated body; Discard ValueTask methods enqueue through an async lambda (binding the
/// EnqueueAsync(Func&lt;Task&gt;) overload); same-simple-name classes in different namespaces are never merged into
/// an agent; nested agents mirror their namespace without "+" and without type names in usings.
/// </remarks>
public sealed class HotfixWrapperGeneratorContractViolationTests
{
    [Fact]
    public void Service_MethodWithRefOrOutParameter_ReportsDiagnosticAndSkipsGeneration()
    {
        var result = RunWrapperGenerator("""
            namespace GameFrameX.Hotfix.Player
            {
                public class RefParameterComponentAgent : BaseComponentAgent
                {
                    [Service]
                    public virtual Task WriteRefAsync(ref int value) { return null; }

                    [Service]
                    public virtual Task WriteOutAsync(out int value) { value = 0; return null; }

                    [Service]
                    public virtual Task ValidSiblingAsync() { return null; }
                }
            }
            """);

        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.GetMessage().Contains("WriteRefAsync")
            && diagnostic.GetMessage().Contains("不允许 ref/out/in 参数"));
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.GetMessage().Contains("WriteOutAsync")
            && diagnostic.GetMessage().Contains("不允许 ref/out/in 参数"));
        // 拒绝诊断与生成面互斥：同类合法方法照常生成（钉住「跳过的是方法而非整类」），违规方法不发射
        var generatedText = JoinGeneratedText(result);
        Assert.Contains("ValidSiblingAsync", generatedText);
        Assert.DoesNotContain("WriteRefAsync", generatedText);
        Assert.DoesNotContain("WriteOutAsync", generatedText);
    }

    [Fact]
    public void Discard_ValueTaskMethod_UsesAsyncLambdaEnqueue()
    {
        var result = RunWrapperGenerator("""
            namespace GameFrameX.Hotfix.Player
            {
                public class DiscardValueTaskComponentAgent : BaseComponentAgent
                {
                    [Discard]
                    public virtual ValueTask NotifyValueAsync() { return default; }
                }
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var generatedText = JoinGeneratedText(result);
        // 异步 lambda（async ()=> await ...）统一绑定 EnqueueAsync(Func<Task>) 重载：
        // ValueTask 不再作为 Func<T> 的 T 被当作普通返回值，其异步完成被正确观察（一次性消费契约不被违反）
        Assert.Contains("EnqueueAsync(async ()=> await base.NotifyValueAsync(), callChainId, true)", generatedText);
        Assert.Contains("return ValueTask.CompletedTask;", generatedText);
    }

    [Fact]
    public void SameSimpleNameInDifferentNamespaces_OnlyAgentSideGeneratesWrapper()
    {
        var result = RunWrapperGenerator("""
            namespace GameFrameX.Hotfix.Player
            {
                public partial class LoginComponentAgent : BaseComponentAgent
                {
                    [Service]
                    public virtual Task LoginAsync() { return null; }
                }
            }

            namespace GameFrameX.Hotfix.ServerRole
            {
                // 与代理同简单名的无关 partial 辅助类：不得按简单名并入代理得到幽灵 Wrapper
                public partial class LoginComponentAgent
                {
                    [Service]
                    public virtual Task HelperLoginAsync() { return null; }
                }
            }
            """);

        var generatedText = JoinGeneratedText(result);
        Assert.Contains("LoginAsync", generatedText);
        Assert.Contains("GameFrameX.Hotfix.Wrapper.Agent.Player", generatedText);
        Assert.DoesNotContain("ServerRole", generatedText);
        Assert.DoesNotContain("HelperLoginAsync", generatedText);
    }

    [Fact]
    public void NestedAgent_NamespaceMirroredWithoutPlus()
    {
        var result = RunWrapperGenerator("""
            namespace GameFrameX.Hotfix.Player
            {
                public class OuterHolder
                {
                    public class NestedComponentAgent : BaseComponentAgent
                    {
                        [Service]
                        public virtual Task NestedPingAsync() { return null; }
                    }
                }

                // 与嵌套代理同简单名的顶层代理：镜像命名空间相同，wrapper 类名必须携带容器链避免同名碰撞
                public class NestedComponentAgent : BaseComponentAgent
                {
                    [Service]
                    public virtual Task TopLevelPingAsync() { return null; }
                }
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var generatedText = JoinGeneratedText(result);
        // 命名空间镜像取真命名空间：不含嵌套分隔符「+」，using 也不得引入类型名（OuterHolder 不是命名空间）
        Assert.Contains("namespace GameFrameX.Hotfix.Wrapper.Agent.Player", generatedText);
        Assert.DoesNotContain("+", generatedText);
        Assert.Contains("using GameFrameX.Hotfix.Player;", generatedText);
        Assert.DoesNotContain("using GameFrameX.Hotfix.Player.OuterHolder", generatedText);
        // 嵌套代理基类用容器限定名、wrapper 类名携带容器链（. 折叠为 _）：与同简单名顶层代理的 wrapper
        // 同命名空间共存不碰撞（CS0101/CS0263），且两者都仍以 ComponentAgentWrapper 结尾满足 HotfixModule 识别
        Assert.Contains("class OuterHolder_NestedComponentAgentWrapper : OuterHolder.NestedComponentAgent", generatedText);
        Assert.Contains("class NestedComponentAgentWrapper : NestedComponentAgent", generatedText);
        Assert.Contains("NestedPingAsync", generatedText);
        Assert.Contains("TopLevelPingAsync", generatedText);
    }

    /// <summary>
    /// 以测试源驱动 Wrapper 生成器并返回运行结果（生成树 + 生成器诊断）。
    /// </summary>
    /// <param name="source">测试源码 / Test source</param>
    /// <returns>生成器运行结果 / The generator run result</returns>
    private static GeneratorDriverRunResult RunWrapperGenerator(string source)
    {
        var compilation = CSharpCompilation.Create(
            "HotfixWrapperGeneratorContractViolationTestsCompilation",
            new[] { CSharpSyntaxTree.ParseText(source) });

        var driver = CSharpGeneratorDriver.Create(new HotfixWrapperGenerator());
        return driver.RunGenerators(compilation).GetRunResult();
    }

    /// <summary>
    /// 合并全部生成树的文本。
    /// </summary>
    /// <param name="result">生成器运行结果 / The generator run result</param>
    /// <returns>合并后的生成源码文本 / The combined generated source text</returns>
    private static string JoinGeneratedText(GeneratorDriverRunResult result)
    {
        return string.Join("\n", result.GeneratedTrees.Select(tree => tree.GetText().ToString()));
    }
}
