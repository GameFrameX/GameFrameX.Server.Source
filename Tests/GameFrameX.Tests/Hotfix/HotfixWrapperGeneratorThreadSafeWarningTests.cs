// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规及开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes and liabilities arising from secondary development based on this project,
//   本项目组织与贡献者概不承担。
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
/// <see cref="HotfixWrapperGenerator"/> ThreadSafe 特性组合诊断测试：单独标记 [ThreadSafe]
/// （未同时标记 [Discard]）的组件代理方法触发 Warning 诊断且不生成包装器；[ThreadSafe]+[Discard]
/// 组合不触发该 Warning。
/// Tests for the ThreadSafe attribute combination diagnostic in <see cref="HotfixWrapperGenerator"/>: a
/// component agent method marked [ThreadSafe] alone (without [Discard]) must produce a Warning diagnostic
/// and no wrapper, while the [ThreadSafe]+[Discard] combination must not produce that Warning.
/// </summary>
public sealed class HotfixWrapperGeneratorThreadSafeWarningTests
{
    /// <summary>
    /// 测试源码：一个代理类只标 [ThreadSafe]，另一个同时标 [ThreadSafe] 与 [Discard]。
    /// Test source: one agent marks [ThreadSafe] only, the other marks both [ThreadSafe] and [Discard].
    /// </summary>
    private const string TestSource = @"
namespace GameFrameX.Hotfix.Player
{
    public class ThreadOnlyAgent : FuncComponentAgent
    {
        [ThreadSafe]
        public virtual System.Threading.Tasks.Task<int> GetThreadOnlyValueAsync() { return null; }
    }

    public class ThreadWithDiscardAgent : FuncComponentAgent
    {
        [ThreadSafe, Discard]
        public virtual System.Threading.Tasks.Task GetThreadWithDiscardValueAsync() { return null; }
    }
}
";

    /// <summary>
    /// 单独 [ThreadSafe]（无 [Discard]）触发一条包含方法全名的 Warning 诊断，且不生成包装器。
    /// A lone [ThreadSafe] (without [Discard]) must produce a Warning diagnostic containing the fully
    /// qualified method name, and no wrapper may be generated for the method.
    /// </summary>
    [Fact]
    public void ThreadSafe_WithoutDiscard_ProducesWarningAndNoWrapper()
    {
        var runResult = RunGenerator();

        Assert.Contains(runResult.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Warning
            && diagnostic.GetMessage().Contains("GameFrameX.Hotfix.Player.ThreadOnlyAgent.GetThreadOnlyValueAsync"));
        Assert.DoesNotContain(runResult.GeneratedTrees, tree => tree.GetText().ToString().Contains("GetThreadOnlyValueAsync"));
    }

    /// <summary>
    /// [ThreadSafe]+[Discard] 组合不触发 ThreadSafe 单独使用的 Warning，包装器照常生成。
    /// The [ThreadSafe]+[Discard] combination must not produce the lone-ThreadSafe Warning, and the
    /// wrapper is generated as usual.
    /// </summary>
    [Fact]
    public void ThreadSafe_WithDiscard_NoWarningAndWrapperGenerated()
    {
        var runResult = RunGenerator();

        Assert.DoesNotContain(runResult.Diagnostics, diagnostic =>
            diagnostic.Severity == DiagnosticSeverity.Warning
            && diagnostic.GetMessage().Contains("GetThreadWithDiscardValueAsync"));
        Assert.Contains(runResult.GeneratedTrees, tree => tree.GetText().ToString().Contains("GetThreadWithDiscardValueAsync"));
    }

    /// <summary>
    /// 用 <see cref="HotfixWrapperGenerator"/> 驱动测试源码，返回生成器运行结果。
    /// Drives <see cref="HotfixWrapperGenerator"/> over the test source and returns the run result.
    /// </summary>
    /// <returns>生成器运行结果 / The generator run result</returns>
    private static GeneratorDriverRunResult RunGenerator()
    {
        var compilation = CSharpCompilation.Create(
            "HotfixWrapperGeneratorThreadSafeWarningTestsCompilation",
            new[] { CSharpSyntaxTree.ParseText(SourceText.From(TestSource, Encoding.UTF8)) });

        CSharpGeneratorDriver driver = CSharpGeneratorDriver.Create(new HotfixWrapperGenerator());

        return driver.RunGenerators(compilation).GetRunResult();
    }
}
