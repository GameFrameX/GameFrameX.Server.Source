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
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository:  https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:  https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using GameFrameX.StartUp;
using Xunit;

namespace GameFrameX.Tests.StartUp;

/// <summary>
/// AllInOneOptions 多 Role / All-in-One CLI 解析测试（C143b D2）。
/// </summary>
public class AllInOneOptionsTests
{
    /// <summary>
    /// 单 Role 形态：--ServerType=Game 解析为单元素列表。
    /// </summary>
    [Fact]
    public void Parse_SingleServerType_ReturnsSingleElementList()
    {
        var options = AllInOneOptions.Parse(["--ServerType=Game"]);

        Assert.False(options.IsAllInOne);
        Assert.Equal(new[] { "Game" }, options.ServerTypes);
    }

    /// <summary>
    /// 复数形态：--ServerType=Game,Social 按逗号拆分。
    /// </summary>
    [Fact]
    public void Parse_CommaSeparatedServerType_SplitsOnComma()
    {
        var options = AllInOneOptions.Parse(["--ServerType=Game,Social"]);

        Assert.False(options.IsAllInOne);
        Assert.Equal(new[] { "Game", "Social" }, options.ServerTypes);
    }

    /// <summary>
    /// 复数形态带空白与空段：trim、去空、去重后仍得到干净列表。
    /// </summary>
    [Fact]
    public void Parse_MessyServerType_TrimsAndRemovesEmptyAndDuplicates()
    {
        var options = AllInOneOptions.Parse(["--ServerType= Game , Social ,, Game "]);

        Assert.Equal(new[] { "Game", "Social" }, options.ServerTypes);
    }

    /// <summary>
    /// 空格分隔形态：--ServerType Game,Social 同样识别。
    /// </summary>
    [Fact]
    public void Parse_SpaceSeparatedServerType_ReadsNextArgument()
    {
        var options = AllInOneOptions.Parse(["--ServerType", "Game,Social"]);

        Assert.Equal(new[] { "Game", "Social" }, options.ServerTypes);
    }

    /// <summary>
    /// All-in-One 裸开关：--AllInOne 等价 true。
    /// </summary>
    [Fact]
    public void Parse_BareAllInOneSwitch_EnablesAllInOne()
    {
        var options = AllInOneOptions.Parse(["--ServerType=Game", "--AllInOne"]);

        Assert.True(options.IsAllInOne);
        Assert.Equal(new[] { "Game" }, options.ServerTypes);
    }

    /// <summary>
    /// All-in-One 值形态：--AllInOne=true / --AllInOne=false 按布尔值解析。
    /// </summary>
    [Theory]
    [InlineData("--AllInOne=true", true)]
    [InlineData("--AllInOne=false", false)]
    public void Parse_AllInOneWithValue_ParsesBoolean(string argument, bool expected)
    {
        var options = AllInOneOptions.Parse([argument]);

        Assert.Equal(expected, options.IsAllInOne);
    }

    /// <summary>
    /// 缺省形态：无任何相关参数返回 None。
    /// </summary>
    [Fact]
    public void Parse_NoArguments_ReturnsNone()
    {
        Assert.Same(AllInOneOptions.None, AllInOneOptions.Parse([]));
        Assert.Same(AllInOneOptions.None, AllInOneOptions.Parse(["--ServerId=1001", "--LogIsConsole=true"]));

        var none = AllInOneOptions.None;
        Assert.False(none.IsAllInOne);
        Assert.Empty(none.ServerTypes);
    }

    /// <summary>
    /// 重复 --ServerType 参数：最后一次生效。
    /// </summary>
    [Fact]
    public void Parse_RepeatedServerType_LastOccurrenceWins()
    {
        var options = AllInOneOptions.Parse(["--ServerType=Game", "--ServerType=Social"]);

        Assert.Equal(new[] { "Social" }, options.ServerTypes);
    }

    /// <summary>
    /// 空格分隔形态的选项标记保护：--ServerType --AllInOne 不把 "--AllInOne" 当作 Role 名消费。
    /// </summary>
    [Fact]
    public void Parse_SpaceSeparatedServerType_DoesNotConsumeNextOptionMarker()
    {
        var options = AllInOneOptions.Parse(["--ServerType", "--AllInOne"]);

        Assert.True(options.IsAllInOne);
        Assert.Empty(options.ServerTypes);
    }

    /// <summary>
    /// 空格分隔形态的选项标记保护：后续参数仍正常解析（--ServerType --ServerId=1 Game）。
    /// </summary>
    [Fact]
    public void Parse_SpaceSeparatedServerType_SkipsMarkerButReadsLaterValue()
    {
        var options = AllInOneOptions.Parse(["--ServerType", "--ServerId=1", "--ServerType", "Game"]);

        Assert.False(options.IsAllInOne);
        Assert.Equal(new[] { "Game" }, options.ServerTypes);
    }

    /// <summary>
    /// 未识别参数被忽略，不影响解析结果。
    /// </summary>
    [Fact]
    public void Parse_UnknownArguments_Ignored()
    {
        var options = AllInOneOptions.Parse(["--Foo=bar", "--serverType=Game,Social", "--Baz"]);

        Assert.Equal(new[] { "Game", "Social" }, options.ServerTypes);
    }
}
