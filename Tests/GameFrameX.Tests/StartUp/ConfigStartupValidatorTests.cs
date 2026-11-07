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
//   Gitee Repository:   https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:     https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using GameFrameX.Foundation.Options;
using GameFrameX.StartUp;
using GameFrameX.StartUp.Configuration;
using GameFrameX.StartUp.Options;
using GameFrameX.Utility.Setting;

namespace GameFrameX.Tests.StartUp;

/// <summary>单宿主监听端口、配置优先级与凭据脱敏校验。</summary>
public class ConfigStartupValidatorTests
{
    private static AppSetting CreateSetting()
    {
        return new AppSetting { ServerType = "Game", ServerId = 1000, InnerPort = 29000, HttpPort = 28080 };
    }

    [Fact]
    public void Validate_SingleApplication_Passes()
    {
        ConfigStartupValidator.Validate(CreateSetting());
    }

    [Fact]
    public void Validate_MissingFile_UsesLauncherConfiguration()
    {
        ConfigStartupValidator.Validate(new[] { "Game" }, Array.Empty<AppSetting>(), new StartupOptions { ServerType = "Game" }, Array.Empty<string>());
    }

    [Fact]
    public void Validate_MultipleApplications_RejectsConfiguration()
    {
        Assert.Throws<ArgumentException>(() => ConfigStartupValidator.Validate(new[] { "Game", "Other" }, new[] { CreateSetting() }, new StartupOptions(), Array.Empty<string>()));
    }

    [Fact]
    public void Validate_MultipleFileSections_RejectsConfiguration()
    {
        Assert.Throws<ArgumentException>(() => ConfigStartupValidator.Validate(new[] { "Game" }, new[] { CreateSetting(), CreateSetting() }, new StartupOptions(), Array.Empty<string>()));
    }

    [Fact]
    public void Validate_MismatchedIdentity_RejectsConfiguration()
    {
        Assert.Throws<ArgumentException>(() => ConfigStartupValidator.Validate(new[] { "Other" }, new[] { CreateSetting() }, new StartupOptions(), Array.Empty<string>()));
    }

    [Fact]
    public void Validate_ClientTcpAndHttpCollision_ThrowsPortConflict()
    {
        var setting = CreateSetting();
        setting.HttpPort = setting.InnerPort;
        var exception = Assert.Throws<ConfigConflictException>(() => ConfigStartupValidator.Validate(setting));
        Assert.Equal(ConfigConflictKind.PortConflict, Assert.Single(exception.Conflicts).Kind);
        Assert.Contains(nameof(AppSetting.InnerPort), exception.Message);
        Assert.Contains(nameof(AppSetting.HttpPort), exception.Message);
    }

    [Fact]
    public void Validate_KcpAndTcpSharePort_Passes()
    {
        var setting = CreateSetting();
        setting.IsEnableKcp = true;
        setting.KcpPort = setting.InnerPort;
        ConfigStartupValidator.Validate(setting);
    }

    [Fact]
    public void Validate_UdpAndKcpCollision_RejectsConfiguration()
    {
        var setting = CreateSetting();
        setting.IsEnableUdp = true;
        setting.IsEnableKcp = true;
        setting.KcpPort = setting.InnerPort;
        Assert.Throws<ConfigConflictException>(() => ConfigStartupValidator.Validate(setting));
    }

    [Fact]
    public void Validate_AdvertisedOuterPort_DoesNotCreateListener()
    {
        var setting = CreateSetting();
        setting.OuterPort = setting.InnerPort;
        ConfigStartupValidator.Validate(setting);
        setting.OuterPort = setting.HttpPort;
        ConfigStartupValidator.Validate(setting);
    }

    [Fact]
    public void Validate_DisabledListener_DoesNotConflict()
    {
        var setting = CreateSetting();
        setting.IsEnableHttp = false;
        setting.HttpPort = setting.InnerPort;
        ConfigStartupValidator.Validate(setting);
    }

    [Theory]
    [InlineData(nameof(AppSetting.HttpsPort))]
    [InlineData(nameof(AppSetting.WsPort))]
    [InlineData(nameof(AppSetting.WssPort))]
    [InlineData(nameof(AppSetting.MetricsPort))]
    [InlineData(nameof(AppSetting.OnlineAdminPort))]
    public void Validate_EnabledTcpEndpoint_CannotShareClientPort(string fieldName)
    {
        var setting = CreateSetting();
        setting.IsEnableWebSocket = true;
        setting.IsOpenTelemetry = true;
        setting.IsOpenTelemetryMetrics = true;
        setting.Online = new OnlineCapabilityOptions { EnableAdminApi = true };
        typeof(AppSetting).GetProperty(fieldName).SetValue(setting, setting.InnerPort);
        Assert.Throws<ConfigConflictException>(() => ConfigStartupValidator.Validate(setting));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(65536)]
    public void Validate_InvalidKcpPort_RejectsConfiguration(int port)
    {
        var setting = CreateSetting();
        setting.IsEnableKcp = true;
        setting.KcpPort = port;
        Assert.Throws<ArgumentOutOfRangeException>(() => ConfigStartupValidator.Validate(setting));
    }

    [Theory]
    [InlineData("--HttpPort=0")]
    [InlineData("--http-port=0")]
    [InlineData("--http_port=0")]
    public void Validate_ExplicitDefaultCommandLineValue_StillRejectsFileConflict(string argument)
    {
        var options = OptionsBuilder.CreateFromArgsOnly<StartupOptions>(new[] { argument });
        var exception = Assert.Throws<ConfigConflictException>(() => ConfigStartupValidator.Validate(new[] { "Game" }, new[] { CreateSetting() }, options, new[] { argument }));
        Assert.Contains(exception.Conflicts, conflict => conflict.Kind == ConfigConflictKind.CliFileValueConflict && conflict.FieldName == nameof(AppSetting.HttpPort));
    }

    [Fact]
    public void Validate_MatchingCommandLineValue_Passes()
    {
        var arguments = new[] { "--HttpPort=28080" };
        ConfigStartupValidator.Validate(new[] { "Game" }, new[] { CreateSetting() }, OptionsBuilder.CreateFromArgsOnly<StartupOptions>(arguments), arguments);
    }

    [Fact]
    public void Validate_SensitiveConflict_OmitsCredentialValues()
    {
        var setting = CreateSetting();
        setting.HttpSignSecret = "file-credential";
        var arguments = new[] { "--HttpSignSecret=command-credential" };
        var exception = Assert.Throws<ConfigConflictException>(() => ConfigStartupValidator.Validate(new[] { "Game" }, new[] { setting }, OptionsBuilder.CreateFromArgsOnly<StartupOptions>(arguments), arguments));
        Assert.Contains(nameof(AppSetting.HttpSignSecret), exception.Message);
        Assert.DoesNotContain("file-credential", exception.Message);
        Assert.DoesNotContain("command-credential", exception.Message);
    }
}
