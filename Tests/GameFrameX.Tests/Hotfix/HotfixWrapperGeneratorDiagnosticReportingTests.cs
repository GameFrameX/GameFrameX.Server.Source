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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GameFrameX.Hotfix.WrapperGenerator.Agent;
using GameFrameX.Hotfix.WrapperGenerator.Utils;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace GameFrameX.Tests.Hotfix;

/// <summary>
/// 验证 Hotfix Wrapper 生成器的诊断形态（C432 / CR-M07+L05）：全部诊断携带稳定 ID
/// （GFW0001=契约违规 Error、GFW0002=契约警示 Warning，与分析器 GFX 系列前缀错开）与违规方法的
/// 源位置（可点击定位、可按 ID 抑制）；诊断消息引用真实特性名【Service】等常量，不再出现
/// 框架中不存在的【Api】/【AsyncApi】幻觉特性名；<see cref="AgentTemplate"/> 无静态可变状态。
/// Verifies the Hotfix wrapper generator's diagnostic shape (C432 / CR-M07+L05): every diagnostic
/// carries a stable identifier (GFW0001 = contract violation Error, GFW0002 = contract warning Warning,
/// prefixed to avoid the GFX analyzer series) and the violating method's source location (clickable and
/// suppressible per ID); messages reference the real attribute names like 【Service】 instead of the
/// nonexistent 【Api】/【AsyncApi】 spellings; and <see cref="AgentTemplate"/> holds no static mutable state.
/// </summary>
public sealed class HotfixWrapperGeneratorDiagnosticReportingTests
{
    private const string TestSource = """
        namespace GameFrameX.Hotfix.Player
        {
            public class DiagnosticsComponentAgent : BaseComponentAgent
            {
                [TimeOut(3000)]
                public virtual System.Threading.Tasks.Task TimeOutOnlyAsync()
                {
                    return null;
                }

                [ThreadSafe]
                public virtual System.Threading.Tasks.Task<int> ThreadSafeOnlyAsync()
                {
                    return null;
                }

                [Service]
                public virtual int SyncValue()
                {
                    return 0;
                }

                [Service]
                public int NotVirtualValue()
                {
                    return 0;
                }
            }
        }
        """;

    /// <summary>
    /// 全部诊断使用稳定 ID GFW0001/GFW0002 与生成器专属类别，不再使用 "Error"/"Warning" 占位 ID。
    /// All diagnostics use the stable identifiers GFW0001/GFW0002 with the generator-specific category
    /// instead of the "Error"/"Warning" placeholder identifiers.
    /// </summary>
    [Fact]
    public void All_diagnostics_use_stable_generator_identifiers()
    {
        var diagnostics = RunGeneratorDiagnostics();

        Assert.Equal(5, diagnostics.Length);
        Assert.All(diagnostics, diagnostic =>
        {
            Assert.True(
                diagnostic.Id == Logger.ErrorDiagnosticIdentifier || diagnostic.Id == Logger.WarningDiagnosticIdentifier,
                $"诊断 ID 必须是 GFW 系列，实际: {diagnostic.Id}");
            Assert.Equal(Logger.DiagnosticCategory, diagnostic.Descriptor.Category);
        });
        Assert.Equal(4, diagnostics.Count(diagnostic => diagnostic.Id == Logger.ErrorDiagnosticIdentifier && diagnostic.Severity == DiagnosticSeverity.Error));
        Assert.Single(diagnostics, diagnostic => diagnostic.Id == Logger.WarningDiagnosticIdentifier && diagnostic.Severity == DiagnosticSeverity.Warning);
    }

    /// <summary>
    /// 每条诊断都携带违规方法的源位置（非 Location.None，落在测试源文件上，且声明跨度覆盖方法名），
    /// 错误列表点击可直达违规方法。
    /// Every diagnostic carries the violating method's source location (not Location.None, on the test
    /// source file, with the declaration span covering the method name) so the error list can navigate
    /// directly to the violating method.
    /// </summary>
    [Fact]
    public void Every_diagnostic_locates_the_violating_method()
    {
        var syntaxTree = ParseTestTree();
        var diagnostics = RunGeneratorDiagnostics(syntaxTree);

        Assert.All(diagnostics, diagnostic =>
        {
            Assert.NotEqual(Location.None, diagnostic.Location);
            Assert.Same(syntaxTree, diagnostic.Location.SourceTree);
        });
        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Id == Logger.ErrorDiagnosticIdentifier
            && GetLocatedFragment(diagnostic).Contains("TimeOutOnlyAsync"));
        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Id == Logger.WarningDiagnosticIdentifier
            && GetLocatedFragment(diagnostic).Contains("ThreadSafeOnlyAsync"));
        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Id == Logger.ErrorDiagnosticIdentifier
            && GetLocatedFragment(diagnostic).Contains("SyncValue"));
        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Id == Logger.ErrorDiagnosticIdentifier
            && GetLocatedFragment(diagnostic).Contains("NotVirtualValue"));
    }

    /// <summary>
    /// 诊断消息引用真实特性名（【Service】/【Discard】/【ThreadSafe】常量），不出现框架中不存在的
    /// 【Api】/【AsyncApi】字样（CR-L05）。
    /// Diagnostic messages reference the real attribute names (the 【Service】/【Discard】/【ThreadSafe】
    /// constants) and never mention the nonexistent 【Api】/【AsyncApi】 spellings (CR-L05).
    /// </summary>
    [Fact]
    public void Messages_reference_real_attribute_names_only()
    {
        var diagnostics = RunGeneratorDiagnostics();

        Assert.All(diagnostics, diagnostic =>
        {
            Assert.DoesNotContain("AsyncApi", diagnostic.GetMessage(), StringComparison.Ordinal);
            Assert.DoesNotContain("【Api】", diagnostic.GetMessage(), StringComparison.Ordinal);
        });

        var timeoutOnlyMessage = diagnostics.Single(diagnostic => diagnostic.GetMessage().Contains("TimeOutOnlyAsync")).GetMessage();
        Assert.Contains("【Service】", timeoutOnlyMessage, StringComparison.Ordinal);
        Assert.Contains("【Discard】", timeoutOnlyMessage, StringComparison.Ordinal);

        var syncValueMessage = diagnostics.Single(diagnostic => diagnostic.GetMessage().Contains("SyncValue")).GetMessage();
        Assert.Contains("【Service】", syncValueMessage, StringComparison.Ordinal);
        Assert.Contains("Task 或 ValueTask", syncValueMessage, StringComparison.Ordinal);

        // NotVirtualValue 触发两条 Error（返回类型必须 Task/ValueTask + 必须申明为 virtual）
        var notVirtualMessages = diagnostics.Where(diagnostic => diagnostic.GetMessage().Contains("NotVirtualValue")).ToList();
        Assert.Equal(2, notVirtualMessages.Count);
        Assert.Contains(notVirtualMessages, message => message.GetMessage().Contains("Task 或 ValueTask", StringComparison.Ordinal));
        Assert.Contains(notVirtualMessages, message => message.GetMessage().Contains("必须申明为virtual", StringComparison.Ordinal));
        Assert.All(notVirtualMessages, message => Assert.Contains("【Service】", message.GetMessage(), StringComparison.Ordinal));
    }

    /// <summary>
    /// <see cref="AgentTemplate"/> 无任何静态字段：模板无跨调用静态可变状态（CR-L03，并发驱动安全）。
    /// <see cref="AgentTemplate"/> has no static fields at all: the template holds no cross-call static
    /// mutable state (CR-L03, safe for concurrent drivers).
    /// </summary>
    [Fact]
    public void AgentTemplate_has_no_static_fields()
    {
        var staticFields = typeof(AgentTemplate).GetFields(
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);

        Assert.Empty(staticFields);
    }

    private static SyntaxTree ParseTestTree()
    {
        return CSharpSyntaxTree.ParseText(SourceText.From(TestSource, Encoding.UTF8), path: "DiagnosticsComponentAgent.cs");
    }

    private static Diagnostic[] RunGeneratorDiagnostics(SyntaxTree syntaxTree = null)
    {
        syntaxTree ??= ParseTestTree();
        var compilation = CSharpCompilation.Create(
            "HotfixWrapperGeneratorDiagnosticReportingTestsCompilation",
            new[] { syntaxTree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) });

        var driver = CSharpGeneratorDriver.Create(new HotfixWrapperGenerator());
        return driver.RunGenerators(compilation).GetRunResult().Diagnostics.ToArray();
    }

    /// <summary>
    /// 取诊断定位覆盖的源文本片段（即违规方法声明本身）。
    /// Gets the source fragment covered by the diagnostic's location (the violating method declaration itself).
    /// </summary>
    /// <param name="diagnostic">诊断 / The diagnostic</param>
    /// <returns>源文本片段 / The source fragment</returns>
    private static string GetLocatedFragment(Diagnostic diagnostic)
    {
        var sourceText = diagnostic.Location.SourceTree.GetText().ToString();
        return sourceText.Substring(diagnostic.Location.SourceSpan.Start, diagnostic.Location.SourceSpan.Length);
    }
}
