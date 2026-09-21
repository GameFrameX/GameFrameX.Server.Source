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
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using GameFrameX.Utility.Setting;
using Xunit;

namespace GameFrameX.Tests.Utility;

/// <summary>
/// GlobalSettings.SetCurrentSetting 进程级一致性校验的单元测试（C143a D19）。
/// </summary>
/// <remarks>
/// Unit tests for the process-level consistency validation of GlobalSettings.SetCurrentSetting (C143a D19):
/// identical process-level fields pass through idempotently; conflicting ones fail fast with
/// <see cref="SettingConflictException"/> listing field names and source segments.
/// </remarks>
public class GlobalSettingsSettingConflictTests : IDisposable
{
    /// <summary>
    /// 构造函数：每个测试前清空 CurrentSetting，隔离静态状态。
    /// </summary>
    public GlobalSettingsSettingConflictTests()
    {
        GlobalSettings.ResetCurrentSetting();
    }

    /// <summary>
    /// 释放：测试后再次清空，避免静态状态泄漏到其它测试。
    /// </summary>
    public void Dispose()
    {
        GlobalSettings.ResetCurrentSetting();
    }

    /// <summary>
    /// 构造一份与默认进程级字段一致的测试配置（只改 Role 级字段，避免与并行测试的默认值冲突）。
    /// </summary>
    /// <param name="serverType">Role 身份 / Role identity</param>
    /// <returns>测试配置 / The test setting</returns>
    private static AppSetting CreateSetting(string serverType)
    {
        return new AppSetting
        {
            ServerType = serverType,
            ServerId = 1000,
        };
    }

    /// <summary>
    /// 首次设置放行，CurrentSetting 生效。
    /// </summary>
    [Fact]
    public void SetCurrentSetting_FirstCall_Assigns()
    {
        var setting = CreateSetting("Game");

        GlobalSettings.SetCurrentSetting(setting);

        Assert.Same(setting, GlobalSettings.CurrentSetting);
    }

    /// <summary>
    /// 二次设置且进程级字段全部一致：幂等放行，CurrentSetting 更新为新实例（Role 级以最后一次为准）。
    /// </summary>
    [Fact]
    public void SetCurrentSetting_IdenticalProcessLevelFields_PassesIdempotently()
    {
        GlobalSettings.SetCurrentSetting(CreateSetting("Game"));
        var secondSetting = CreateSetting("Social");

        GlobalSettings.SetCurrentSetting(secondSetting);

        Assert.Same(secondSetting, GlobalSettings.CurrentSetting);
    }

    /// <summary>
    /// 二次设置且进程级字段不一致：抛 SettingConflictException，列出冲突字段、期望/实际值与来源段。
    /// </summary>
    [Fact]
    public void SetCurrentSetting_ConflictingProcessLevelField_ThrowsWithDetails()
    {
        GlobalSettings.SetCurrentSetting(CreateSetting("Game"));
        var conflictingSetting = CreateSetting("Social");
        conflictingSetting.DataBaseUrl = "mongodb://conflicting";

        var exception = Assert.Throws<SettingConflictException>(() => GlobalSettings.SetCurrentSetting(conflictingSetting));

        var conflict = Assert.Single(exception.Conflicts);
        Assert.Equal(nameof(AppSetting.DataBaseUrl), conflict.FieldName);
        Assert.Equal("Game", conflict.ExpectedSegment);
        Assert.Equal("Social", conflict.ActualSegment);
        Assert.Null(conflict.ExpectedValue);
        Assert.Equal("mongodb://conflicting", conflict.ActualValue);
        Assert.Contains(nameof(AppSetting.DataBaseUrl), exception.Message);
    }

    /// <summary>
    /// 多个进程级字段冲突：全部列出（不只第一个）。
    /// </summary>
    [Fact]
    public void SetCurrentSetting_MultipleConflictingFields_ListsAll()
    {
        var firstSetting = CreateSetting("Game");
        firstSetting.DataBaseUrl = "mongodb://first";
        firstSetting.DataBaseName = "gameframex";
        GlobalSettings.SetCurrentSetting(firstSetting);

        var secondSetting = CreateSetting("Social");
        secondSetting.DataBaseUrl = "mongodb://second";
        secondSetting.DataBaseName = "gameframex_control";

        var exception = Assert.Throws<SettingConflictException>(() => GlobalSettings.SetCurrentSetting(secondSetting));

        Assert.Equal(2, exception.Conflicts.Count);
        Assert.Contains(exception.Conflicts, conflict => conflict.FieldName == nameof(AppSetting.DataBaseUrl));
        Assert.Contains(exception.Conflicts, conflict => conflict.FieldName == nameof(AppSetting.DataBaseName));
    }

    /// <summary>
    /// 冲突抛出后 CurrentSetting 保持原值（不被部分更新）。
    /// </summary>
    [Fact]
    public void SetCurrentSetting_AfterConflict_CurrentSettingUnchanged()
    {
        var firstSetting = CreateSetting("Game");
        GlobalSettings.SetCurrentSetting(firstSetting);
        var conflictingSetting = CreateSetting("Social");
        conflictingSetting.WorkerId = 999;

        Assert.Throws<SettingConflictException>(() => GlobalSettings.SetCurrentSetting(conflictingSetting));

        Assert.Same(firstSetting, GlobalSettings.CurrentSetting);
    }

    /// <summary>
    /// null 入参抛 ArgumentNullException。
    /// </summary>
    [Fact]
    public void SetCurrentSetting_Null_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => GlobalSettings.SetCurrentSetting(null));
    }
}
