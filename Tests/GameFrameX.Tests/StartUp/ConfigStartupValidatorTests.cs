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

/// <summary>
/// 启动期配置校验器测试（C143f D4：一致放行 / 段缺失 / 同进程端口冲突 / CLI-文件冲突）。
/// </summary>
public class ConfigStartupValidatorTests
{
    /// <summary>
    /// 构建带段名与端口段的文件层配置段。
    /// </summary>
    /// <remarks>
    /// Builds a file-layer section with the given server type and ports.
    /// </remarks>
    /// <param name="serverType">段名 / The section server type</param>
    /// <param name="innerPort">InnerPort / The inner port</param>
    /// <param name="outerPort">OuterPort / The outer port</param>
    /// <param name="httpPort">HttpPort / The HTTP port</param>
    /// <returns>文件层配置段 / The file-layer section</returns>
    private static AppSetting CreateSection(string serverType, ushort innerPort, ushort outerPort, ushort httpPort)
    {
        return new AppSetting
        {
            ServerType = serverType,
            ServerId = 1001,
            InnerPort = innerPort,
            OuterPort = outerPort,
            HttpPort = httpPort,
        };
    }

    /// <summary>
    /// 一致形态放行：双段端口互不冲突 + CLI 仅 --AllInOne，不抛异常。
    /// </summary>
    [Fact]
    public void Validate_ConsistentSectionsAndAllInOne_Passes()
    {
        var sections = new List<AppSetting>
        {
            CreateSection("Game", 29100, 29100, 28080),
            CreateSection("Social", 29400, 29400, 28081),
        };

        var exception = Record.Exception(() => ConfigStartupValidator.Validate(
            new[] { "Game", "Social" },
            sections,
            OptionsBuilder.CreateDefault<StartupOptions>(),
            AllInOneOptions.Parse(["--AllInOne"])));

        Assert.Null(exception);
    }

    /// <summary>
    /// 复数 --ServerType 的 CLI 实例经 init setter 派生 ServerName="Game,Social"，不与逐段 ServerName 误报冲突。
    /// </summary>
    [Fact]
    public void Validate_PluralServerTypeDerivedServerName_DoesNotConflict()
    {
        var sections = new List<AppSetting>
        {
            CreateSection("Game", 29100, 29100, 28080),
            CreateSection("Social", 29400, 29400, 28081),
        };
        var launcherOptions = OptionsBuilder.Create<StartupOptions>(["--ServerType=Game,Social"]);

        var exception = Record.Exception(() => ConfigStartupValidator.Validate(
            new[] { "Game", "Social" },
            sections,
            launcherOptions,
            AllInOneOptions.Parse(["--ServerType=Game,Social"])));

        Assert.Null(exception);
    }

    /// <summary>
    /// 段缺失：复数 --ServerType 形态下缺失段 fail fast，冲突字段为 ServerType 且消息列出缺失 Role 与文件来源。
    /// </summary>
    [Fact]
    public void Validate_PluralServerTypeWithMissingSection_ThrowsMissingSection()
    {
        var sections = new List<AppSetting> { CreateSection("Game", 29100, 29100, 28080) };

        var exception = Assert.Throws<ConfigConflictException>(() => ConfigStartupValidator.Validate(
            new[] { "Game", "Social" },
            sections,
            OptionsBuilder.CreateDefault<StartupOptions>(),
            AllInOneOptions.Parse(["--ServerType=Game,Social"])));

        var conflict = Assert.Single(exception.Conflicts);
        Assert.Equal(ConfigConflictKind.MissingSection, conflict.Kind);
        Assert.Equal(nameof(AppSetting.ServerType), conflict.FieldName);
        Assert.Contains("Social", exception.Message);
        Assert.Contains("file(Configs/app_config.json)", exception.Message);
    }

    /// <summary>
    /// 单值形态段缺失保留现状回落（AC-3）：不抛异常。
    /// </summary>
    [Fact]
    public void Validate_SingleServerTypeWithMissingSection_KeepsFallback()
    {
        var exception = Record.Exception(() => ConfigStartupValidator.Validate(
            new[] { "Game" },
            new List<AppSetting>(),
            OptionsBuilder.CreateDefault<StartupOptions>(),
            AllInOneOptions.Parse(["--ServerType=Game"])));

        Assert.Null(exception);
    }

    /// <summary>
    /// 同进程端口冲突：两个 Role 的文件段 InnerPort 相同 → fail fast，列字段与双方来源段。
    /// </summary>
    [Fact]
    public void Validate_SamePortFieldAcrossRoles_ThrowsPortConflict()
    {
        var sections = new List<AppSetting>
        {
            CreateSection("Game", 29100, 29100, 28080),
            CreateSection("Social", 29100, 29400, 28081),
        };

        var exception = Assert.Throws<ConfigConflictException>(() => ConfigStartupValidator.Validate(
            new[] { "Game", "Social" },
            sections,
            OptionsBuilder.CreateDefault<StartupOptions>(),
            AllInOneOptions.Parse(["--AllInOne"])));

        var conflict = Assert.Single(exception.Conflicts);
        Assert.Equal(ConfigConflictKind.PortConflict, conflict.Kind);
        Assert.Equal(nameof(AppSetting.InnerPort), conflict.FieldName);
        Assert.Equal("file[Game]", conflict.FirstSource);
        Assert.Equal("file[Social]", conflict.SecondSource);
    }

    /// <summary>
    /// 同 Role 的 InnerPort 与 OuterPort 同值不构成冲突（合法形态：内外共用监听端口）。
    /// </summary>
    [Fact]
    public void Validate_InnerAndOuterPortSameValueWithinOneRole_Passes()
    {
        var sections = new List<AppSetting> { CreateSection("Game", 29100, 29100, 28080) };

        var exception = Record.Exception(() => ConfigStartupValidator.Validate(
            new[] { "Game" },
            sections,
            OptionsBuilder.CreateDefault<StartupOptions>(),
            AllInOneOptions.Parse(["--ServerType=Game"])));

        Assert.Null(exception);
    }

    /// <summary>
    /// 使能开关开启后的同端口才构成冲突：两段 OnlineAdminPort 同值且 IsEnableOnlineAdmin=true → fail fast。
    /// </summary>
    [Fact]
    public void Validate_EnabledSameOnlineAdminPort_ThrowsPortConflict()
    {
        var sections = new List<AppSetting>
        {
            new AppSetting { ServerType = "Game", ServerId = 1001, InnerPort = 29100, OuterPort = 29100, HttpPort = 28080, IsEnableOnlineAdmin = true, OnlineAdminPort = 28090 },
            new AppSetting { ServerType = "Social", ServerId = 5531, InnerPort = 29400, OuterPort = 29400, HttpPort = 28081, IsEnableOnlineAdmin = true, OnlineAdminPort = 28090 },
        };

        var exception = Assert.Throws<ConfigConflictException>(() => ConfigStartupValidator.Validate(
            new[] { "Game", "Social" },
            sections,
            OptionsBuilder.CreateDefault<StartupOptions>(),
            AllInOneOptions.Parse(["--AllInOne"])));

        var conflict = Assert.Single(exception.Conflicts);
        Assert.Equal(ConfigConflictKind.PortConflict, conflict.Kind);
        Assert.Equal(nameof(AppSetting.OnlineAdminPort), conflict.FieldName);
    }

    /// <summary>
    /// CLI-文件冲突：CLI 显式 InnerPort 与文件段不同 → fail fast，列字段与双方来源。
    /// </summary>
    [Fact]
    public void Validate_CliValueDiffersFromFileSection_ThrowsCliFileConflict()
    {
        var sections = new List<AppSetting> { CreateSection("Game", 29100, 29100, 28080) };
        var launcherOptions = OptionsBuilder.Create<StartupOptions>(["--ServerType=Game", "--InnerPort=30000"]);

        var exception = Assert.Throws<ConfigConflictException>(() => ConfigStartupValidator.Validate(
            new[] { "Game" },
            sections,
            launcherOptions,
            AllInOneOptions.Parse(["--ServerType=Game"])));

        var conflict = Assert.Single(exception.Conflicts);
        Assert.Equal(ConfigConflictKind.CliFileValueConflict, conflict.Kind);
        Assert.Equal(nameof(AppSetting.InnerPort), conflict.FieldName);
        Assert.Equal("CLI", conflict.FirstSource);
        Assert.Equal("file[Game]", conflict.SecondSource);
        Assert.Contains("30000", exception.Message);
        Assert.Contains("29100", exception.Message);
    }

    /// <summary>
    /// CLI 显式值与文件段一致：不构成冲突（值相同无丢弃）。
    /// </summary>
    [Fact]
    public void Validate_CliValueMatchesFileSection_Passes()
    {
        var sections = new List<AppSetting> { CreateSection("Game", 29100, 29100, 28080) };
        var launcherOptions = OptionsBuilder.Create<StartupOptions>(["--ServerType=Game", "--InnerPort=29100"]);

        var exception = Record.Exception(() => ConfigStartupValidator.Validate(
            new[] { "Game" },
            sections,
            launcherOptions,
            AllInOneOptions.Parse(["--ServerType=Game"])));

        Assert.Null(exception);
    }

    /// <summary>
    /// 异常消息完整性：同时含冲突字段名、CLI 来源与文件来源段（D4 验收）。
    /// </summary>
    [Fact]
    public void Validate_ConflictMessageListsFieldAndSources()
    {
        var sections = new List<AppSetting> { CreateSection("Game", 29100, 29100, 28080) };
        var launcherOptions = OptionsBuilder.Create<StartupOptions>(["--ServerType=Game", "--HttpPort=30000"]);

        var exception = Assert.Throws<ConfigConflictException>(() => ConfigStartupValidator.Validate(
            new[] { "Game" },
            sections,
            launcherOptions,
            AllInOneOptions.Parse(["--ServerType=Game"])));

        Assert.Contains(nameof(AppSetting.HttpPort), exception.Message);
        Assert.Contains("CLI", exception.Message);
        Assert.Contains("file[Game]", exception.Message);
    }
}
