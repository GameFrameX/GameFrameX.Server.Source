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
            "--IsSingleMode=true",
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
        Assert.True(options.IsSingleMode);
        Assert.False(options.LogIsConsole);
    }

    /// <summary>
    /// IsAllInOne 开关可通过启动参数解析（C143b D2），缺省为 false。
    /// </summary>
    [Fact]
    public void TryCreate_ShouldBindIsAllInOneOption()
    {
        // Arrange
        string[] enabledArgs = ["--ServerType=Game,Social", "--IsAllInOne=true"];
        string[] defaultArgs = ["--ServerType=Game"];

        // Act
        var enabledSuccess = OptionsBuilder.TryCreate<StartupOptions>(enabledArgs, out var enabledOptions, out var enabledError);
        var defaultSuccess = OptionsBuilder.TryCreate<StartupOptions>(defaultArgs, out var defaultOptions, out var defaultError);

        // Assert
        Assert.True(enabledSuccess, enabledError);
        Assert.True(enabledOptions.IsAllInOne);
        Assert.True(defaultSuccess, defaultError);
        Assert.False(defaultOptions.IsAllInOne);
    }

    /// <summary>
    /// GetServerTypes 派生方法按逗号拆分 ServerType（C143b D2），未指定时为空数组。
    /// </summary>
    [Fact]
    public void GetServerTypes_DerivedFromServerType_SplitsOnComma()
    {
        // Arrange
        string[] pluralArgs = ["--ServerType=Game,Social"];
        string[] singleArgs = ["--ServerType=Game"];

        // Act
        var pluralSuccess = OptionsBuilder.TryCreate<StartupOptions>(pluralArgs, out var pluralOptions, out var pluralError);
        var singleSuccess = OptionsBuilder.TryCreate<StartupOptions>(singleArgs, out var singleOptions, out var singleError);

        // Assert
        Assert.True(pluralSuccess, pluralError);
        Assert.Equal(new[] { "Game", "Social" }, pluralOptions.GetServerTypes());
        Assert.True(singleSuccess, singleError);
        Assert.Equal(new[] { "Game" }, singleOptions.GetServerTypes());
        Assert.Empty(new StartupOptions().GetServerTypes());
    }
}
