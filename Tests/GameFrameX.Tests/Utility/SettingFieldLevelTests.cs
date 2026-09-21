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

using System.Reflection;
using GameFrameX.Utility.Setting;
using Xunit;

namespace GameFrameX.Tests.Utility;

/// <summary>
/// SettingFieldLevel 标注的单元测试（C143a D19 字段分区）。
/// </summary>
/// <remarks>
/// Unit tests for the SettingFieldLevel annotations (C143a D19 field partition).
/// Guards against un-annotated AppSetting properties, which would silently bypass
/// process-level conflict validation in GlobalSettings.SetCurrentSetting.
/// </remarks>
public class SettingFieldLevelTests
{
    /// <summary>
    /// AppSetting 及其子类（含 StartupOptions）的全部公共属性都必须有分区标注（防漏标）。
    /// </summary>
    [Fact]
    public void AllPublicProperties_AreAnnotated()
    {
        var properties = typeof(AppSetting).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        Assert.NotEmpty(properties);

        var unannotated = properties
            .Where(property => SettingFieldLevelAttribute.GetLevel(property) == null)
            .Select(property => property.Name)
            .ToList();

        Assert.True(unannotated.Count == 0, $"Properties without SettingFieldLevel annotation: [{string.Join(", ", unannotated)}]");
    }

    /// <summary>
    /// 关键字段分区抽样：数据库与雪花 ID 为进程级；Role 身份与监听端口为 Role 级。
    /// </summary>
    /// <remarks>
    /// Spot checks of the partition: database and snowflake fields are process-level;
    /// role identity and listening ports are role-level.
    /// </remarks>
    [Theory]
    [InlineData(nameof(AppSetting.DataBaseUrl), SettingFieldLevel.ProcessLevel)]
    [InlineData(nameof(AppSetting.DataBaseName), SettingFieldLevel.ProcessLevel)]
    [InlineData(nameof(AppSetting.WorkerId), SettingFieldLevel.ProcessLevel)]
    [InlineData(nameof(AppSetting.DataCenterId), SettingFieldLevel.ProcessLevel)]
    [InlineData(nameof(AppSetting.ServerType), SettingFieldLevel.RoleLevel)]
    [InlineData(nameof(AppSetting.ServerId), SettingFieldLevel.RoleLevel)]
    [InlineData(nameof(AppSetting.InnerPort), SettingFieldLevel.RoleLevel)]
    [InlineData(nameof(AppSetting.HttpPort), SettingFieldLevel.RoleLevel)]
    public void KeyFields_HaveExpectedLevel(string propertyName, SettingFieldLevel expectedLevel)
    {
        var property = typeof(AppSetting).GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(property);
        Assert.Equal(expectedLevel, SettingFieldLevelAttribute.GetLevel(property));
    }

    /// <summary>
    /// 进程级字段集合非空且数量级符合 D19 普查（95% 进程级）。
    /// </summary>
    [Fact]
    public void ProcessLevelPartition_IsNotEmpty()
    {
        var properties = typeof(AppSetting).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var processLevelCount = properties.Count(property => SettingFieldLevelAttribute.GetLevel(property) == SettingFieldLevel.ProcessLevel);
        var roleLevelCount = properties.Count(property => SettingFieldLevelAttribute.GetLevel(property) == SettingFieldLevel.RoleLevel);

        Assert.True(processLevelCount > 0, "Process-level partition must not be empty");
        Assert.True(roleLevelCount > 0, "Role-level partition must not be empty");
        Assert.Equal(properties.Length, processLevelCount + roleLevelCount);
    }
}
