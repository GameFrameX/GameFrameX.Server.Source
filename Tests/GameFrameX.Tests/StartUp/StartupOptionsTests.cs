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

using GameFrameX.StartUp;
using GameFrameX.Utility.Setting;
using GameFrameX.Foundation.Options;
using GameFrameX.StartUp.Options;
using Xunit;

namespace GameFrameX.Tests.StartUp;

/// <summary>
/// StartupOptions 启动配置测试。
/// </summary>
public class StartupOptionsTests
{
    /// <summary>
    /// 测试继承自 AppSetting 的配置项可通过启动参数解析。
    /// </summary>
    [Fact]
    public void TryCreate_ShouldBindInheritedAppSettingOptions()
    {
        // Arrange
        string[] args =
        [
            "--ServerType=Game",
            "--ServerId=1001",
            "--IsMonitorMessageTimeOut=true",
            "--MonitorMessageTimeOutSeconds=7",
            "--LogIsConsole=false"
        ];

        // Act
        var success = OptionsBuilder.TryCreate<StartupOptions>(args, out var options, out var error);

        // Assert
        Assert.True(success, error);
        Assert.Equal("Game", options.ServerType);
        Assert.Equal("Game", options.ServerName);
        Assert.Equal(1001, options.ServerId);
        Assert.True(options.IsMonitorMessageTimeOut);
        Assert.Equal(7, options.MonitorMessageTimeOutSeconds);
        Assert.False(options.LogIsConsole);
    }

    /// <summary>
    /// ServerType 绑定与 GetServerTypes 投影：单值与复数形态按逗号拆分。
    /// </summary>
    [Fact]
    public void GetServerTypes_BindsAndProjects_SingleAndPlural()
    {
        var single = OptionsBuilder.CreateFromArgsOnly<StartupOptions>(["--ServerType=Game"]);
        var plural = OptionsBuilder.CreateFromArgsOnly<StartupOptions>(["--ServerType=Game,Social"]);

        Assert.Equal("Game", single.ServerType);
        Assert.Equal(new[] { "Game" }, single.GetServerTypes());
        Assert.Equal(new[] { "Game", "Social" }, plural.GetServerTypes());
    }

    /// <summary>
    /// 投影语义：trim、去空段、去重、保序。
    /// </summary>
    [Fact]
    public void GetServerTypes_TrimsDropsEmptyAndDeduplicates()
    {
        var options = OptionsBuilder.CreateFromArgsOnly<StartupOptions>(["--ServerType= Game , Social ,, Game "]);

        Assert.Equal(new[] { "Game", "Social" }, options.GetServerTypes());
    }

    /// <summary>
    /// 空格分隔形态：--ServerType Game,Social 由 binder 消费下一 token。
    /// </summary>
    [Fact]
    public void Create_SpaceSeparatedServerType_BindsNextToken()
    {
        var options = OptionsBuilder.CreateFromArgsOnly<StartupOptions>(["--ServerType", "Game,Social"]);

        Assert.Equal(new[] { "Game", "Social" }, options.GetServerTypes());
    }

    /// <summary>
    /// AllInOne 开关形态：裸开关（含大小写变体）与 =true|=false 均可绑定。
    /// </summary>
    [Theory]
    [InlineData("--AllInOne", true)]
    [InlineData("--allinone", true)]
    [InlineData("--AllInOne=true", true)]
    [InlineData("--AllInOne=false", false)]
    public void Create_AllInOneArgumentForms_BindExpectedValue(string argument, bool expected)
    {
        var options = OptionsBuilder.CreateFromArgsOnly<StartupOptions>([argument]);

        Assert.Equal(expected, options.AllInOne);
    }

    /// <summary>
    /// AllInOne 与 ServerType 并存：两者均绑定，覆盖判定在启动决策处。
    /// </summary>
    [Fact]
    public void Create_AllInOneWithServerType_BindsBoth()
    {
        var options = OptionsBuilder.CreateFromArgsOnly<StartupOptions>(["--ServerType=Game", "--AllInOne"]);

        Assert.True(options.AllInOne);
        Assert.Equal(new[] { "Game" }, options.GetServerTypes());
    }

    /// <summary>
    /// 重复 --ServerType：last-wins（与既有 CLI 语义一致）。
    /// </summary>
    [Fact]
    public void Create_RepeatedServerType_LastWins()
    {
        var options = OptionsBuilder.CreateFromArgsOnly<StartupOptions>(["--ServerType=Game", "--ServerType=Social"]);

        Assert.Equal(new[] { "Social" }, options.GetServerTypes());
    }

    /// <summary>
    /// 裸 --ServerType 后随选项标记：不消费标记为值，ServerType 保持未设置。
    /// </summary>
    [Fact]
    public void Create_ServerTypeBeforeOptionToken_StaysUnset()
    {
        var options = OptionsBuilder.CreateFromArgsOnly<StartupOptions>(["--ServerType", "--AllInOne"]);

        Assert.True(options.AllInOne);
        Assert.Empty(options.GetServerTypes());
    }

    /// <summary>
    /// 混杂未知参数与大小写变体键：未知参数忽略，键大小写不敏感。
    /// </summary>
    [Fact]
    public void Create_UnknownArgsAndCaseInsensitiveKeys_IgnoredOrBound()
    {
        var options = OptionsBuilder.CreateFromArgsOnly<StartupOptions>(["--Foo=bar", "--serverType=Game,Social", "--Baz"]);

        Assert.Equal(new[] { "Game", "Social" }, options.GetServerTypes());
    }

    /// <summary>
    /// 缺省：AllInOne 为 false，投影为空数组。
    /// </summary>
    [Fact]
    public void Create_NoArguments_AllInOneFalseAndEmptyProjection()
    {
        var options = OptionsBuilder.CreateFromArgsOnly<StartupOptions>(Array.Empty<string>());

        Assert.False(options.AllInOne);
        Assert.Empty(options.GetServerTypes());
    }

    /// <summary>
    /// Online 能力过滤 CLI 别名：--OnlineCapabilities / --OnlineDisable 绑定并按逗号投影。
    /// </summary>
    [Fact]
    public void Create_OnlineCapabilityAliases_BindAndProject()
    {
        var options = OptionsBuilder.CreateFromArgsOnly<StartupOptions>(
        [
            "--OnlineCapabilities=online-assets,online-social",
            "--OnlineDisable=online-match",
        ]);

        Assert.Equal("online-assets,online-social", options.OnlineCapabilities);
        Assert.Equal("online-match", options.OnlineDisable);
        Assert.Equal(new[] { "online-assets", "online-social" }, options.GetOnlineCapabilities());
        Assert.Equal(new[] { "online-match" }, options.GetOnlineDisable());
    }

    /// <summary>
    /// Online 别名投影语义：trim、去空段、去重、保序（GetServerTypes 同款）。
    /// </summary>
    [Fact]
    public void GetOnlineCapabilities_TrimsDropsEmptyAndDeduplicates()
    {
        var options = OptionsBuilder.CreateFromArgsOnly<StartupOptions>(
        [
            "--OnlineCapabilities= online-assets ,, online-social , online-assets ",
            "--OnlineDisable= , online-match ,,",
        ]);

        Assert.Equal(new[] { "online-assets", "online-social" }, options.GetOnlineCapabilities());
        Assert.Equal(new[] { "online-match" }, options.GetOnlineDisable());
    }

    /// <summary>
    /// 无配置段 Role 的回落档：CLI 别名投影进嵌套强类型段（CreateRoleDefaultSetting 路径）。
    /// </summary>
    [Fact]
    public void ResolveAppSetting_ProjectsOnlineAliasesIntoNestedSection()
    {
        var launcherOptions = OptionsBuilder.CreateFromArgsOnly<StartupOptions>(
        [
            "--OnlineCapabilities=online-assets,online-social",
            "--OnlineDisable=online-match",
        ]);

        var resolved = GameApp.ResolveAppSetting("Game", Array.Empty<AppSetting>(), launcherOptions, false);

        Assert.NotNull(resolved);
        Assert.Equal("Game", resolved.ServerType);
        Assert.NotNull(resolved.Online);
        Assert.Equal(new[] { "online-assets", "online-social" }, resolved.Online.Capabilities);
        Assert.Equal(new[] { "online-match" }, resolved.Online.Disable);
    }

    /// <summary>
    /// 别名缺省：回落档 Online 段保持 null（全装配缺省语义）。
    /// </summary>
    [Fact]
    public void ResolveAppSetting_WithoutAliases_LeavesOnlineNull()
    {
        var launcherOptions = OptionsBuilder.CreateFromArgsOnly<StartupOptions>(["--ServerType=Game"]);

        var resolved = GameApp.ResolveAppSetting("Game", Array.Empty<AppSetting>(), launcherOptions, false);

        Assert.NotNull(resolved);
        Assert.Null(resolved.Online);
    }
}
