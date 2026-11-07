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

/// <summary>单应用配置更新、默认规范化与配置冲突消息脱敏。</summary>
[Collection(GameAppRuntimeCollection.Name)]
public class GlobalSettingsSettingConflictTests : IDisposable
{
    public GlobalSettingsSettingConflictTests()
    {
        GlobalSettings.ResetCurrentSetting();
    }

    public void Dispose()
    {
        GlobalSettings.ResetCurrentSetting();
    }

    [Fact]
    public void SetCurrentSetting_FirstCall_Assigns()
    {
        var setting = new AppSetting { ServerType = "Game", ServerId = 1000 };
        GlobalSettings.SetCurrentSetting(setting);
        Assert.Same(setting, GlobalSettings.CurrentSetting);
    }

    [Fact]
    public void SetCurrentSetting_RepeatedCall_IsIdempotent()
    {
        var setting = new AppSetting { ServerType = "Game", ServerId = 1000 };
        GlobalSettings.SetCurrentSetting(setting);
        GlobalSettings.SetCurrentSetting(setting);
        Assert.Same(setting, GlobalSettings.CurrentSetting);
    }

    [Fact]
    public void SetCurrentSetting_ReplacesLocalConfiguration_WithoutCrossApplicationComparison()
    {
        GlobalSettings.SetCurrentSetting(new AppSetting { ServerType = "Game", ServerId = 1000, DataBaseName = "before" });
        var setting = new AppSetting { ServerType = "Game", ServerId = 1000, DataBaseName = "after", HttpSignSecret = "updated-credential" };
        GlobalSettings.SetCurrentSetting(setting);
        Assert.Same(setting, GlobalSettings.CurrentSetting);
        Assert.Equal("after", GlobalSettings.CurrentSetting.DataBaseName);
        Assert.Equal("updated-credential", GlobalSettings.CurrentSetting.HttpSignSecret);
    }

    [Fact]
    public void SetCurrentSetting_NormalizesInvalidLocalDefaults()
    {
        var setting = new AppSetting { ServerType = "Game", SaveDataInterval = 3000, HttpUrl = "", NetworkSendTimeOutSeconds = 0, ActorRecycleTime = 0 };
        GlobalSettings.SetCurrentSetting(setting);
        Assert.Equal(300000, setting.SaveDataInterval);
        Assert.Equal("/game/api/", setting.HttpUrl);
        Assert.Equal(5, setting.NetworkSendTimeOutSeconds);
        Assert.Equal(5, setting.ActorRecycleTime);
    }

    [Theory]
    [InlineData(nameof(AppSetting.DataBasePassword))]
    [InlineData(nameof(AppSetting.HttpSignSecret))]
    [InlineData(nameof(AppSetting.WssCertificatePassword))]
    [InlineData(nameof(AppSetting.DataBaseUrl))]
    public void SettingConflictException_SensitiveFields_OmitValues(string fieldName)
    {
        var conflict = new SettingFieldConflict(fieldName, "current", "first-credential", "incoming", "second-credential");
        var exception = new SettingConflictException(new[] { conflict });
        Assert.Contains(fieldName, exception.Message);
        Assert.DoesNotContain("first-credential", exception.Message);
        Assert.DoesNotContain("second-credential", exception.Message);
        Assert.Equal("first-credential", conflict.ExpectedValue);
    }

    [Fact]
    public void SetCurrentSetting_Null_DoesNotReplaceCurrentConfiguration()
    {
        var setting = new AppSetting { ServerType = "Game" };
        GlobalSettings.SetCurrentSetting(setting);
        Assert.Throws<ArgumentNullException>(() => GlobalSettings.SetCurrentSetting(null));
        Assert.Same(setting, GlobalSettings.CurrentSetting);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Load_SingleObjectOrArray_PreservesApplicationAndOnlineConfiguration(bool array)
    {
        var path = Path.GetTempFileName();
        try
        {
            var json = "{\"ServerType\":\"Game\",\"ServerId\":1000,\"Online\":{\"EnableAdminApi\":true}}";
            File.WriteAllText(path, array ? "[" + json + "]" : json);
            GlobalSettings.Load(path);
            var setting = Assert.Single(GlobalSettings.GetSettings());
            Assert.Equal("Game", setting.ServerType);
            Assert.True(setting.Online.EnableAdminApi);
        }
        finally
        {
            File.Delete(path);
            GlobalSettings.Load(null);
        }
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[{},{}]")]
    [InlineData("{\"ServerType\":\"Game,Other\",\"ServerId\":1000}")]
    public void Load_InvalidSingleApplicationConfiguration_Rejects(string json)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, json);
            Assert.Throws<InvalidOperationException>(() => GlobalSettings.Load(path));
        }
        finally
        {
            File.Delete(path);
            GlobalSettings.Load(null);
        }
    }
}
