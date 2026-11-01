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
//   Any legal disputes or liabilities arising from secondary development based on this project
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
/// Wrapper 生成器健壮性测试：TimeOut 实参防御式解析（常量名/缺参/空参/表达式不崩溃、命名实参与数字分隔符仍解析）、
/// 无命名空间与文件作用域命名空间代理的完全限定名、async 非泛型 Task 的 await 转发、Discard 返回类型校验先行。
/// </summary>
/// <remarks>
/// Robustness tests for the wrapper generator: defensive TimeOut argument parsing (constant names, missing/empty
/// argument lists and expressions must not crash; named arguments and digit separators still parse), fully qualified
/// names for namespace-less and file-scoped agents, await-forwarding for async non-generic Task methods, and
/// up-front Discard return-type validation.
/// </remarks>
public sealed class HotfixWrapperGeneratorRobustnessTests
{
    [Fact]
    public void TimeOut_WithConstName_ReportsDiagnosticAndUsesDefaultTimeout()
    {
        var result = RunWrapperGenerator("""
            namespace GameFrameX.Hotfix.Player
            {
                public class ConstTimeoutComponentAgent : BaseComponentAgent
                {
                    [Service, TimeOut(TimeoutMilliseconds)]
                    public virtual Task SaveWithConstAsync() { return null; }
                }
            }
            """);

        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.GetMessage().Contains("SaveWithConstAsync")
            && diagnostic.GetMessage().Contains("【TimeOut】参数必须是整数字面量"));
        // 解析失败降级为未配置超时：入队调用不携带超时实参（走框架默认值）
        Assert.Contains(result.GeneratedTrees, tree => tree.GetText().ToString().Contains("EnqueueAsync(()=>base.SaveWithConstAsync(), chainId, false)"));
    }

    [Fact]
    public void TimeOut_WithoutArgumentList_ReportsDiagnosticInsteadOfCrashing()
    {
        var result = RunWrapperGenerator("""
            namespace GameFrameX.Hotfix.Player
            {
                public class BareTimeoutComponentAgent : BaseComponentAgent
                {
                    [Service, TimeOut]
                    public virtual Task SaveBareAsync() { return null; }
                }
            }
            """);

        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.GetMessage().Contains("SaveBareAsync")
            && diagnostic.GetMessage().Contains("【TimeOut】参数必须是整数字面量"));
        Assert.Contains(result.GeneratedTrees, tree => tree.GetText().ToString().Contains("EnqueueAsync(()=>base.SaveBareAsync(), chainId, false)"));
    }

    [Fact]
    public void TimeOut_WithEmptyArgumentList_ReportsDiagnosticInsteadOfCrashing()
    {
        var result = RunWrapperGenerator("""
            namespace GameFrameX.Hotfix.Player
            {
                public class EmptyTimeoutComponentAgent : BaseComponentAgent
                {
                    [Service, TimeOut()]
                    public virtual Task SaveEmptyAsync() { return null; }
                }
            }
            """);

        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.GetMessage().Contains("SaveEmptyAsync")
            && diagnostic.GetMessage().Contains("【TimeOut】参数必须是整数字面量"));
    }

    [Fact]
    public void TimeOut_WithExpression_ReportsDiagnosticInsteadOfCrashing()
    {
        var result = RunWrapperGenerator("""
            namespace GameFrameX.Hotfix.Player
            {
                public class ExpressionTimeoutComponentAgent : BaseComponentAgent
                {
                    [Service, TimeOut((int)TimeSpan.FromSeconds(5).TotalMilliseconds)]
                    public virtual Task SaveExpressionAsync() { return null; }
                }
            }
            """);

        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.GetMessage().Contains("SaveExpressionAsync")
            && diagnostic.GetMessage().Contains("【TimeOut】参数必须是整数字面量"));
        Assert.Contains(result.GeneratedTrees, tree => tree.GetText().ToString().Contains("EnqueueAsync(()=>base.SaveExpressionAsync(), chainId, false)"));
    }

    [Fact]
    public void TimeOut_WithNamedArgument_StillEmitsTimeout()
    {
        var result = RunWrapperGenerator("""
            namespace GameFrameX.Hotfix.Player
            {
                public class NamedTimeoutComponentAgent : BaseComponentAgent
                {
                    [Service, TimeOut(timeOut: 2500)]
                    public virtual Task SaveNamedAsync() { return null; }
                }
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains(result.GeneratedTrees, tree => tree.GetText().ToString().Contains("EnqueueAsync(()=>base.SaveNamedAsync(), chainId, false, 2500)"));
    }

    [Fact]
    public void TimeOut_WithDigitSeparator_StillEmitsTimeout()
    {
        var result = RunWrapperGenerator("""
            namespace GameFrameX.Hotfix.Player
            {
                public class SeparatorTimeoutComponentAgent : BaseComponentAgent
                {
                    [Service, TimeOut(3_000)]
                    public virtual Task SaveSeparatorAsync() { return null; }
                }
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.Contains(result.GeneratedTrees, tree => tree.GetText().ToString().Contains("EnqueueAsync(()=>base.SaveSeparatorAsync(), chainId, false, 3000)"));
    }

    [Fact]
    public void TimeOut_WithUnderscoreLeadingIdentifier_ReportsDiagnosticInsteadOfMisParsing()
    {
        var result = RunWrapperGenerator("""
            namespace GameFrameX.Hotfix.Player
            {
                public class UnderscoreIdentifierTimeoutComponentAgent : BaseComponentAgent
                {
                    [Service, TimeOut(_3000)]
                    public virtual Task SaveUnderscoreIdentifierAsync() { return null; }
                }
            }
            """);

        // 下划线开头的合法常量名不得被「剥掉下划线后恰好是数字」静默误析为超时值
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.GetMessage().Contains("SaveUnderscoreIdentifierAsync")
            && diagnostic.GetMessage().Contains("【TimeOut】参数必须是整数字面量"));
        Assert.Contains(result.GeneratedTrees, tree => tree.GetText().ToString().Contains("EnqueueAsync(()=>base.SaveUnderscoreIdentifierAsync(), chainId, false)"));
    }

    [Fact]
    public void Agent_WithoutNamespace_GeneratesWrapperWithoutCrash()
    {
        var result = RunWrapperGenerator("""
            public class NoNamespaceComponentAgent : BaseComponentAgent
            {
                [Service]
                public virtual Task PingNoNamespaceAsync() { return null; }
            }
            """);

        var generatedText = JoinGeneratedText(result);
        // 无命名空间代理：命名空间回退到固定的 Wrapper.Agent 段，不发射空 using
        Assert.Contains("namespace GameFrameX.Hotfix.Wrapper.Agent", generatedText);
        Assert.Contains("class NoNamespaceComponentAgentWrapper : NoNamespaceComponentAgent", generatedText);
        Assert.DoesNotContain("using ;", generatedText);
    }

    [Fact]
    public void Agent_InFileScopedNamespace_GeneratesMirroredNamespace()
    {
        var result = RunWrapperGenerator("""
            namespace GameFrameX.Hotfix.Player;

            public class FileScopedComponentAgent : BaseComponentAgent
            {
                [Service]
                public virtual Task PingFileScopedAsync() { return null; }
            }
            """);

        var generatedText = JoinGeneratedText(result);
        Assert.Contains("namespace GameFrameX.Hotfix.Wrapper.Agent.Player", generatedText);
        Assert.Contains("class FileScopedComponentAgentWrapper : FileScopedComponentAgent", generatedText);
        Assert.Contains("using GameFrameX.Hotfix.Player;", generatedText);
    }

    [Fact]
    public void Service_AsyncNonGenericTask_GeneratesAwaitReturn()
    {
        var result = RunWrapperGenerator("""
            namespace GameFrameX.Hotfix.Player
            {
                public class AsyncTaskComponentAgent : BaseComponentAgent
                {
                    [Service]
                    public virtual async Task SyncAsync() { await Task.Delay(1); }
                }
            }
            """);

        var generatedText = JoinGeneratedText(result);
        // async 非泛型 Task：override async 方法体内必须 return await 转发（是否泛型与该判定无关）
        Assert.Contains("public override async Task SyncAsync()", generatedText);
        Assert.Contains("return await  base.SyncAsync();", generatedText);
    }

    [Fact]
    public void Discard_OnTaskOfInt_ReportsDiagnosticWithoutGeneratedBody()
    {
        var result = RunWrapperGenerator("""
            namespace GameFrameX.Hotfix.Player
            {
                public class DiscardTaskOfIntComponentAgent : BaseComponentAgent
                {
                    [Discard]
                    public virtual Task<int> CountDiscardAsync() { return 0; }

                    [Discard]
                    public virtual Task ValidDiscardSiblingAsync() { return null; }
                }
            }
            """);

        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.GetMessage().Contains("CountDiscardAsync")
            && diagnostic.GetMessage().Contains("只有返回值为Task类型或ValueTask类型才能添加【Discard】注解"));
        // 校验先行：违规方法绝不进入生成面（不再生成 Task<int>.CompletedTask 一类不可编译产物）；
        // 同类合法方法照常生成，钉住「跳过的是方法而非整类」
        Assert.Contains("ValidDiscardSiblingAsync", JoinGeneratedText(result));
        Assert.DoesNotContain("CountDiscardAsync", JoinGeneratedText(result));
    }

    [Fact]
    public void Discard_OnFullyQualifiedTask_NoFalsePositive()
    {
        var result = RunWrapperGenerator("""
            namespace GameFrameX.Hotfix.Player
            {
                public class DiscardQualifiedTaskComponentAgent : BaseComponentAgent
                {
                    [Discard]
                    public virtual System.Threading.Tasks.Task SaveQualifiedAsync() { return null; }
                }
            }
            """);

        Assert.DoesNotContain(result.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Error
            && diagnostic.GetMessage().Contains("SaveQualifiedAsync"));
        var generatedText = JoinGeneratedText(result);
        Assert.Contains("return System.Threading.Tasks.Task.CompletedTask;", generatedText);
    }

    /// <summary>
    /// 以测试源驱动 Wrapper 生成器并返回运行结果（生成树 + 生成器诊断）。
    /// </summary>
    /// <param name="source">测试源码 / Test source</param>
    /// <returns>生成器运行结果 / The generator run result</returns>
    private static GeneratorDriverRunResult RunWrapperGenerator(string source)
    {
        var compilation = CSharpCompilation.Create(
            "HotfixWrapperGeneratorRobustnessTestsCompilation",
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
