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

using System.Collections.Immutable;
using System.Threading.Tasks;
using GameFrameX.Architecture.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace GameFrameX.Tests.CodeStyle;

/// <summary>
/// 代码风格分析器（GFX0019 / GFX0020 / GFX0021）的边界用例。
/// </summary>
public sealed class CodeStyleAnalyzersTests
{
    [Fact]
    public async Task Using_declaration_reports_GFX0021()
    {
        var diagnostics = await GetDiagnosticsAsync(Compilation("""
public class Sample
{
    public int Read()
    {
        using var stream = OpenStream();
        return 1;
    }

    private static System.IO.MemoryStream OpenStream()
    {
        return new System.IO.MemoryStream();
    }
}
"""), new UsingDeclarationBanAnalyzer());

        Assert.Single(diagnostics, item => item.Id == "GFX0021");
    }

    [Fact]
    public async Task Await_using_declaration_reports_GFX0021()
    {
        var diagnostics = await GetDiagnosticsAsync(Compilation("""
public class Sample
{
    public async System.Threading.Tasks.Task ReadAsync()
    {
        await using var stream = OpenStreamAsync();
        await System.Threading.Tasks.Task.CompletedTask;
    }

    private static System.Threading.Tasks.Task<System.IO.MemoryStream> OpenStreamAsync()
    {
        return System.Threading.Tasks.Task.FromResult(new System.IO.MemoryStream());
    }
}
"""), new UsingDeclarationBanAnalyzer());

        Assert.Single(diagnostics, item => item.Id == "GFX0021");
    }

    [Fact]
    public async Task Using_block_does_not_report_GFX0021()
    {
        var diagnostics = await GetDiagnosticsAsync(Compilation("""
public class Sample
{
    public int Read()
    {
        using (var stream = new System.IO.MemoryStream())
        {
            return (int)stream.Length;
        }
    }
}
"""), new UsingDeclarationBanAnalyzer());

        Assert.DoesNotContain(diagnostics, item => item.Id == "GFX0021");
    }

    [Fact]
    public async Task Two_top_level_types_report_GFX0019_once()
    {
        var diagnostics = await GetDiagnosticsAsync(Compilation("""
public class First
{
}

public class Second
{
}
"""), new OneTypePerFileAnalyzer());

        var diagnostic = Assert.Single(diagnostics, item => item.Id == "GFX0019");
        Assert.Contains("Second", diagnostic.GetMessage());
    }

    [Fact]
    public async Task Partial_and_nested_types_do_not_report_GFX0019()
    {
        var diagnostics = await GetDiagnosticsAsync(Compilation("""
public partial class Sample
{
    private sealed class Nested
    {
    }
}

public partial class Sample
{
}
"""), new OneTypePerFileAnalyzer());

        Assert.DoesNotContain(diagnostics, item => item.Id == "GFX0019");
    }

    [Fact]
    public async Task Declarations_inside_namespaces_still_report_GFX0019()
    {
        var diagnostics = await GetDiagnosticsAsync(Compilation("""
namespace Sample.Domain
{
    internal sealed class First
    {
    }

    internal enum Second
    {
    }
}
"""), new OneTypePerFileAnalyzer());

        Assert.Single(diagnostics, item => item.Id == "GFX0019");
    }

    [Fact]
    public async Task File_name_mismatch_reports_GFX0020()
    {
        var diagnostics = await GetDiagnosticsAsync(Compilation("public class ActualName\n{\n}\n", "WrongName.cs"), new FileNameMatchTypeAnalyzer());

        var diagnostic = Assert.Single(diagnostics, item => item.Id == "GFX0020");
        Assert.Contains("ActualName", diagnostic.GetMessage());
    }

    [Fact]
    public async Task Partial_slice_and_generic_suffix_file_names_do_not_report_GFX0020()
    {
        var sliceDiagnostics = await GetDiagnosticsAsync(Compilation("public partial class GameDb\n{\n}\n", "GameDb.Query.cs"), new FileNameMatchTypeAnalyzer());
        var genericDiagnostics = await GetDiagnosticsAsync(Compilation("public sealed class Result<TResp>\n{\n}\n", "Result{TResp}.cs"), new FileNameMatchTypeAnalyzer());

        Assert.DoesNotContain(sliceDiagnostics, item => item.Id == "GFX0020");
        Assert.DoesNotContain(genericDiagnostics, item => item.Id == "GFX0020");
    }

    [Fact]
    public async Task Multi_type_file_is_left_to_GFX0019()
    {
        var diagnostics = await GetDiagnosticsAsync(Compilation("public class First\n{\n}\n\npublic class Second\n{\n}\n", "First.cs"), new FileNameMatchTypeAnalyzer());

        Assert.DoesNotContain(diagnostics, item => item.Id == "GFX0020");
    }

    private static async Task<ImmutableArray<Diagnostic>> GetDiagnosticsAsync(Compilation compilation, DiagnosticAnalyzer analyzer)
    {
        var compilationWithAnalyzers = compilation.WithAnalyzers(ImmutableArray.Create(analyzer));
        return await compilationWithAnalyzers.GetAnalyzerDiagnosticsAsync();
    }

    private static CSharpCompilation Compilation(string source, string path = "Sample.cs")
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp10), path);
        return CSharpCompilation.Create(
            "GameFrameX.CodeStyle.Sample",
            [syntaxTree],
            [],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }
}
