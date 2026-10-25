// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the PRC and relevant international regulations.
//   使用本项目须严格遵守相关法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please refer to the LICENSE file at the root of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development,
//   本组织与贡献者概不承担任何责任。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://www.cnb.cool/GameFrameX
//   CNB Repository: https://www.cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System;
using System.Linq;
using System.Text.Json;
using GameFrameX.Foundation.Json;
using GameFrameX.Utility.Setting;
using Xunit;

namespace GameFrameX.Tests.Utility;

/// <summary>
/// Online 能力配置段绑定测试（C229 立项，C294 键集重定）：角色配置 json 的嵌套 "Online" 节点经
/// GlobalSettings.Load 的反序列化通道（List&lt;AppSetting&gt;）捕获为强类型 POCO；
/// 节点缺失 = null（全装配语义）、空数组如实绑定（由组合入口归一为不过滤）；
/// 双闸（EnableRuntime / EnableAdminApi）并入配置段，能力级参数经 Settings 原始 JSON 节点承载。
/// </summary>
public class OnlineCapabilityConfigSectionTests
{
    /// <summary>
    /// 嵌套节点强类型绑定：白名单与排除表按字符串列表捕获。
    /// </summary>
    [Fact]
    public void Deserialize_BindsNestedOnlineSection()
    {
        const string json = """
            [
              {
                "ServerType": "Game",
                "ServerId": 1001,
                "Online": {
                  "Capabilities": ["online-assets", "online-social"],
                  "Disable": ["online-match"]
                }
              }
            ]
            """;

        var settings = JsonHelper.Deserialize<System.Collections.Generic.List<AppSetting>>(json);

        Assert.NotNull(settings);
        var setting = Assert.Single(settings);
        Assert.NotNull(setting.Online);
        Assert.Equal(new[] { "online-assets", "online-social" }, setting.Online.Capabilities.ToArray());
        Assert.Equal(new[] { "online-match" }, setting.Online.Disable.ToArray());
    }

    /// <summary>
    /// 节点缺失：Online 为 null——全装配缺省语义（组合入口按未过滤处理）。
    /// </summary>
    [Fact]
    public void Deserialize_WithoutOnlineSection_LeavesNull()
    {
        const string json = """
            [
              {
                "ServerType": "Game",
                "ServerId": 1001
              }
            ]
            """;

        var settings = JsonHelper.Deserialize<System.Collections.Generic.List<AppSetting>>(json);

        Assert.NotNull(settings);
        Assert.Null(Assert.Single(settings).Online);
    }

    /// <summary>
    /// 空数组如实绑定：绑定层不吞空列表，全装配归一由组合入口完成（层级职责分离）。
    /// </summary>
    [Fact]
    public void Deserialize_EmptyArrays_BindEmptyLists()
    {
        const string json = """
            [
              {
                "ServerType": "Game",
                "ServerId": 1001,
                "Online": {
                  "Capabilities": [],
                  "Disable": []
                }
              }
            ]
            """;

        var settings = JsonHelper.Deserialize<System.Collections.Generic.List<AppSetting>>(json);

        Assert.NotNull(settings);
        var section = Assert.Single(settings).Online;
        Assert.NotNull(section);
        Assert.Empty(section.Capabilities);
        Assert.Empty(section.Disable);
    }

    /// <summary>
    /// 双闸并入配置段：EnableRuntime / EnableAdminApi / EnableRoles / UseGameDbStores 在 "Online" 节点内绑定，
    /// 与平铺的作用域字段（OnlineTenantId / OnlineAppId）并存互不干扰；未配置的过滤表保持 null。
    /// </summary>
    [Fact]
    public void Deserialize_SectionGatesAndFlatScopeFields_Coexist()
    {
        const string json = """
            [
              {
                "ServerType": "Game",
                "ServerId": 1001,
                "OnlineTenantId": 1,
                "OnlineAppId": 1,
                "Online": {
                  "EnableRuntime": true,
                  "EnableAdminApi": true,
                  "EnableRoles": ["Game", "Social"],
                  "UseGameDbStores": true,
                  "Disable": ["online-operations"]
                }
              }
            ]
            """;

        var settings = JsonHelper.Deserialize<System.Collections.Generic.List<AppSetting>>(json);

        Assert.NotNull(settings);
        var setting = Assert.Single(settings);
        Assert.Equal(1, setting.OnlineTenantId);
        Assert.Equal(1, setting.OnlineAppId);
        Assert.NotNull(setting.Online);
        Assert.True(setting.Online.EnableRuntime);
        Assert.True(setting.Online.EnableAdminApi);
        Assert.True(setting.Online.UseGameDbStores.Value);
        Assert.Equal(new[] { "Game", "Social" }, setting.Online.GetEffectiveEnableRoles().ToArray());
        Assert.Null(setting.Online.Capabilities);
        Assert.Equal(new[] { "online-operations" }, setting.Online.Disable.ToArray());
    }

    /// <summary>
    /// 段内开关缺省：未写双闸 = 业务面与管理面都不装配；未写 EnableRoles = 生效角色表回落 ["Game"]。
    /// </summary>
    [Fact]
    public void Deserialize_SectionWithoutGates_DefaultsToDisabledAndGameRole()
    {
        const string json = """
            [
              {
                "ServerType": "Game",
                "ServerId": 1001,
                "Online": {
                  "Capabilities": ["online-assets"]
                }
              }
            ]
            """;

        var settings = JsonHelper.Deserialize<System.Collections.Generic.List<AppSetting>>(json);

        Assert.NotNull(settings);
        var section = Assert.Single(settings).Online;
        Assert.NotNull(section);
        Assert.False(section.EnableRuntime);
        Assert.False(section.EnableAdminApi);
        Assert.Null(section.UseGameDbStores);
        Assert.Null(section.EnableRoles);
        Assert.Equal(new[] { "Game" }, section.GetEffectiveEnableRoles().ToArray());
    }

    /// <summary>
    /// 存储模式三态绑定（C302）：UseGameDbStores 缺省 = null（配置未声明，触发组合根告警）；
    /// 显式 false 如实绑定（有意选择 InMemory，不告警）。
    /// </summary>
    [Fact]
    public void Deserialize_UseGameDbStores_TriStateDefaultNullAndExplicitFalse()
    {
        const string json = """
            [
              {
                "ServerType": "Game",
                "ServerId": 1001,
                "Online": {
                  "EnableRuntime": true,
                  "EnableAdminApi": true,
                  "UseGameDbStores": false
                }
              }
            ]
            """;

        var settings = JsonHelper.Deserialize<System.Collections.Generic.List<AppSetting>>(json);

        Assert.NotNull(settings);
        var section = Assert.Single(settings).Online;
        Assert.NotNull(section);
        Assert.False(section.UseGameDbStores.Value);

        const string jsonWithoutKey = """
            [
              {
                "ServerType": "Game",
                "ServerId": 1001,
                "Online": {
                  "EnableRuntime": true,
                  "EnableAdminApi": true
                }
              }
            ]
            """;

        var settingsWithoutKey = JsonHelper.Deserialize<System.Collections.Generic.List<AppSetting>>(jsonWithoutKey);

        Assert.NotNull(settingsWithoutKey);
        var sectionWithoutKey = Assert.Single(settingsWithoutKey).Online;
        Assert.NotNull(sectionWithoutKey);
        Assert.Null(sectionWithoutKey.UseGameDbStores);
    }

    /// <summary>
    /// 能力级参数节点：Settings 按原始 JsonElement 绑定（键 = 能力 Id，值原样保留供装配期 Options 绑定器逐字段合并）；
    /// 节点缺失时为默认 JsonElement（ValueKind = Undefined，机制按「无覆盖」处理）。
    /// </summary>
    [Fact]
    public void Deserialize_Settings_BindsRawJsonElement_AndIsUndefinedWhenAbsent()
    {
        const string json = """
            [
              {
                "ServerType": "Game",
                "ServerId": 1001,
                "Online": {
                  "Settings": {
                    "online-social": {
                      "Friend": { "FriendLimit": 50 }
                    }
                  }
                }
              },
              {
                "ServerType": "Social",
                "ServerId": 5531,
                "Online": {
                  "EnableRuntime": true
                }
              }
            ]
            """;

        var settings = JsonHelper.Deserialize<System.Collections.Generic.List<AppSetting>>(json);

        Assert.NotNull(settings);
        Assert.Equal(2, settings.Count);

        var configured = settings[0].Online.Settings;
        Assert.Equal(JsonValueKind.Object, configured.ValueKind);
        var social = configured.GetProperty("online-social");
        Assert.Equal(JsonValueKind.Object, social.ValueKind);
        Assert.Equal(50, social.GetProperty("Friend").GetProperty("FriendLimit").GetInt32());

        Assert.Equal(JsonValueKind.Undefined, settings[1].Online.Settings.ValueKind);
    }
}
