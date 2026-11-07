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
//   Any legal disputes and liabilities arising from secondary development based on this project
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


using GameFrameX.Foundation.Options;
using GameFrameX.StartUp;
using GameFrameX.StartUp.Options;
using GameFrameX.Utility.Setting;

namespace GameFrameX.Tests.StartUp;

/// <summary>单应用选择、配置优先级和缺省配置隔离。</summary>
public class GameAppSingleApplicationSelectionTests
{
    private static KeyValuePair<Type, StartUpTagAttribute> Application(string name)
    {
        return new(typeof(GameAppSingleApplicationSelectionTests), new StartUpTagAttribute(name, 1));
    }

    [Fact]
    public void SelectStartUpTypes_ExplicitIdentity_SelectsOnlyMatchingApplication()
    {
        var selected = GameApp.SelectStartUpTypes(new StartupOptions { ServerType = "Game" }, new[] { Application("Game"), Application("Other") });
        Assert.Equal("Game", Assert.Single(selected).Value.ServerType);
    }

    [Fact]
    public void SelectStartUpTypes_ConfiguredIdentity_SelectsApplication()
    {
        var selected = GameApp.SelectStartUpTypes(new StartupOptions(), new[] { Application("Game"), Application("Other") }, "Game");
        Assert.Equal("Game", Assert.Single(selected).Value.ServerType);
    }

    [Fact]
    public void SelectStartUpTypes_UnknownIdentity_ReturnsEmpty()
    {
        Assert.Empty(GameApp.SelectStartUpTypes(new StartupOptions { ServerType = "Unknown" }, new[] { Application("Game") }));
    }

    [Fact]
    public void SelectStartUpTypes_OnlyRegisteredApplication_SelectsDefault()
    {
        Assert.Single(GameApp.SelectStartUpTypes(new StartupOptions(), new[] { Application("Game") }));
    }

    [Fact]
    public void SelectStartUpTypes_AmbiguousDefault_RejectsStartup()
    {
        Assert.Throws<InvalidOperationException>(() => GameApp.SelectStartUpTypes(new StartupOptions(), new[] { Application("Game"), Application("Other") }));
    }

    [Fact]
    public void SelectStartUpTypes_DuplicateIdentity_RejectsStartup()
    {
        Assert.Throws<InvalidOperationException>(() => GameApp.SelectStartUpTypes(new StartupOptions { ServerType = "Game" }, new[] { Application("Game"), Application("Game") }));
    }

    [Fact]
    public void SelectStartUpTypes_CommaSeparatedIdentity_RejectsStartup()
    {
        Assert.Throws<ArgumentException>(() => GameApp.SelectStartUpTypes(new StartupOptions { ServerType = "Game,Other" }, new[] { Application("Game") }));
    }

    [Fact]
    public void ResolveAppSetting_MissingSection_CopiesConfigurationWithoutMutatingOptions()
    {
        var options = new StartupOptions { ServerId = 42, InnerPort = 25000 };
        var setting = GameApp.ResolveAppSetting("Game", Array.Empty<AppSetting>(), options, false);
        Assert.NotSame(options, setting);
        Assert.Equal("Game", setting.ServerType);
        Assert.Equal("Game", setting.ServerName);
        Assert.Equal(42, setting.ServerId);
        Assert.Equal(25000, setting.InnerPort);
        setting.InnerPort = 29000;
        Assert.Equal(25000, options.InnerPort);
    }

    [Fact]
    public void ResolveAppSetting_CustomApplicationName_IsPreserved()
    {
        var options = new StartupOptions { ServerType = "Game", ServerName = "Application One" };
        var setting = GameApp.ResolveAppSetting("Game", Array.Empty<AppSetting>(), options, false);
        Assert.Equal("Application One", setting.ServerName);
    }

    [Fact]
    public void ResolveAppSetting_ExistingSection_PreservesBusinessConfiguration()
    {
        var section = new AppSetting { ServerType = "Game", Online = new OnlineCapabilityOptions { EnableAdminApi = true } };
        Assert.Same(section, GameApp.ResolveAppSetting("Game", new[] { section }, new StartupOptions(), false));
    }

    [Fact]
    public void ResolveAppSetting_NullOptions_PreservesDefaultFactoryContract()
    {
        Assert.Null(GameApp.ResolveAppSetting("Game", Array.Empty<AppSetting>(), null, false));
    }

    [Fact]
    public void ResolveAppSetting_MultipleSections_RejectsConfiguration()
    {
        Assert.Throws<ArgumentException>(() => GameApp.ResolveAppSetting("Game", new[] { new AppSetting { ServerType = "Game" }, new AppSetting { ServerType = "Other" } }, new StartupOptions(), false));
    }
}
