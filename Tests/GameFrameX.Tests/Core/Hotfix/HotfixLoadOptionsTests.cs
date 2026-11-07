// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规之开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights or interests of others, as prohibited by laws and regulations.
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
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

using GameFrameX.Core.Hotfix;
using Xunit;

namespace GameFrameX.Tests.Core.Hotfix;

/// <summary>
/// <see cref="HotfixLoadOptions"/> 校验与路径拼装测试：默认值与历史拼装逐字节等价、
/// Version 整体替代目录段、非法输入拒绝（含路径注入面）。
/// Tests for <see cref="HotfixLoadOptions"/> validation and path composition: defaults byte-identical
/// to the historical composition, Version replacing the directory segment, and rejection of illegal
/// inputs (including the path-injection surface).
/// </summary>
public sealed class HotfixLoadOptionsTests
{
    /// <summary>
    /// 默认值解析结果应与历史 LoadHotfix 的路径拼装逐字节一致。
    /// The default resolution must be byte-identical to the historical LoadHotfix path composition.
    /// </summary>
    [Fact]
    public void Resolve_Defaults_EquivalentToLegacyComposition()
    {
        var options = new HotfixLoadOptions();

        var actual = options.ResolveAssemblyFilePath();
        var expected = Path.Combine(Environment.CurrentDirectory, "hotfix", "GameFrameX.Hotfix.dll");

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// 非空 Version 应整体替代目录段（与历史 dllVersion 语义一致）。
    /// A non-empty Version must replace the directory segment as a whole (matching the historical dllVersion semantics).
    /// </summary>
    [Fact]
    public void Resolve_NonEmptyVersion_ReplacesDirectorySegment()
    {
        var options = new HotfixLoadOptions { Version = "1.2.3" };

        var actual = options.ResolveAssemblyFilePath();
        var expected = Path.Combine(Environment.CurrentDirectory, "1.2.3", "GameFrameX.Hotfix.dll");

        Assert.Equal(expected, actual);
        Assert.DoesNotContain("hotfix", actual);
    }

    /// <summary>
    /// null Version 应与空串同义（沿用历史 IsNullOrEmpty 容忍语义）。
    /// A null Version must behave like the empty string (preserving the historical IsNullOrEmpty tolerance).
    /// </summary>
    [Fact]
    public void Resolve_NullVersion_FallsBackToDefaultDirectory()
    {
        var options = new HotfixLoadOptions { Version = null };

        var actual = options.ResolveAssemblyFilePath();
        var expected = Path.Combine(Environment.CurrentDirectory, "hotfix", "GameFrameX.Hotfix.dll");

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// 自定义目录与文件名应参与拼装。
    /// Custom directory and file name must take part in the composition.
    /// </summary>
    [Fact]
    public void Resolve_CustomDirectoryAndFileName_Composed()
    {
        var options = new HotfixLoadOptions { AssemblyDirectory = "customDir", AssemblyFileName = "Custom.Hotfix.dll" };

        var actual = options.ResolveAssemblyFilePath();
        var expected = Path.Combine(Environment.CurrentDirectory, "customDir", "Custom.Hotfix.dll");

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// 空目录应抛 ArgumentException（沿用历史 ThrowIfNullOrEmpty 语义）。
    /// An empty directory must throw ArgumentException (preserving the historical ThrowIfNullOrEmpty semantics).
    /// </summary>
    [Fact]
    public void Resolve_EmptyDirectory_Throws()
    {
        var options = new HotfixLoadOptions { AssemblyDirectory = "" };

        Assert.Throws<ArgumentException>(() => options.ResolveAssemblyFilePath());
    }

    /// <summary>
    /// 空文件名应抛 ArgumentException。
    /// An empty file name must throw ArgumentException.
    /// </summary>
    [Fact]
    public void Resolve_EmptyFileName_Throws()
    {
        var options = new HotfixLoadOptions { AssemblyFileName = "" };

        Assert.Throws<ArgumentException>(() => options.ResolveAssemblyFilePath());
    }

    /// <summary>
    /// Version 含路径分隔符 / 卷分隔符 / 「..」上跳段应一律拒绝（HTTP 回滚流路径注入面）。
    /// A Version containing path separators / the volume separator / the ".." parent segment must be
    /// rejected outright (the HTTP rollback path-injection surface).
    /// </summary>
    [Theory]
    [InlineData("/etc")]
    [InlineData("a\\b")]
    [InlineData("..")]
    [InlineData("a/../b")]
    [InlineData("1:0")]
    public void Resolve_ForbiddenVersion_Throws(string version)
    {
        var options = new HotfixLoadOptions { Version = version };

        Assert.Throws<ArgumentException>(() => options.ResolveAssemblyFilePath());
    }

    /// <summary>
    /// 常规版本号（含点/连字符）应被接受。
    /// Regular version strings (dots / hyphens included) must be accepted.
    /// </summary>
    [Theory]
    [InlineData("1.0.0")]
    [InlineData("2.5.0-beta1")]
    [InlineData("20261012")]
    public void Resolve_RegularVersion_Accepted(string version)
    {
        var options = new HotfixLoadOptions { Version = version };

        var actual = options.ResolveAssemblyFilePath();

        Assert.Contains(version, actual);
    }
}
