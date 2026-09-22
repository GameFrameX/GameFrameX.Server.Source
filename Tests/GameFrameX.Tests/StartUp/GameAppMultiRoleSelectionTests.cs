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


using GameFrameX.StartUp;
using GameFrameX.StartUp.Options;
using GameFrameX.Utility.Setting;

namespace GameFrameX.Tests.StartUp;

/// <summary>
/// GameApp 多 Role 选择测试（C143b D2/D7：结果保持 StartUpTypeRegistry 优先级序）。
/// </summary>
public class GameAppMultiRoleSelectionTests
{
    /// <summary>
    /// 模拟已注册 Role 的优先级序快照：Alpha(100) → Beta(200) → Gamma(300)。
    /// </summary>
    private static List<KeyValuePair<Type, StartUpTagAttribute>> CreateSortedStartUpTypes()
    {
        return
        [
            new KeyValuePair<Type, StartUpTagAttribute>(typeof(MultiRoleSelectionTestsRoleAlpha), new StartUpTagAttribute("Alpha", 100)),
            new KeyValuePair<Type, StartUpTagAttribute>(typeof(MultiRoleSelectionTestsRoleBeta), new StartUpTagAttribute("Beta", 200)),
            new KeyValuePair<Type, StartUpTagAttribute>(typeof(MultiRoleSelectionTestsRoleGamma), new StartUpTagAttribute("Gamma", 300)),
        ];
    }

    /// <summary>
    /// All-in-One：返回全部已注册 Role，保持优先级序。
    /// </summary>
    [Fact]
    public void SelectStartUpTypes_AllInOne_ReturnsAllRegisteredRolesInPriorityOrder()
    {
        var selected = GameApp.SelectStartUpTypes(AllInOneOptions.Parse(["--AllInOne"]), CreateSortedStartUpTypes());

        Assert.Equal(new[] { "Alpha", "Beta", "Gamma" }, selected.Select(pair => pair.Value.ServerType));
    }

    /// <summary>
    /// 复数：按名匹配并保持优先级序，CLI 输入顺序不影响启动顺序。
    /// </summary>
    [Fact]
    public void SelectStartUpTypes_PluralServerTypes_ReturnsMatchesInPriorityOrderRegardlessOfInputOrder()
    {
        var selected = GameApp.SelectStartUpTypes(AllInOneOptions.Parse(["--ServerType=Gamma,Alpha"]), CreateSortedStartUpTypes());

        Assert.Equal(new[] { "Alpha", "Gamma" }, selected.Select(pair => pair.Value.ServerType));
    }

    /// <summary>
    /// Game+Social 单进程启动顺序（issue 验收对照）：Game(100) 先于 Social(200)，与 CLI 输入顺序无关。
    /// </summary>
    [Fact]
    public void SelectStartUpTypes_GameAndSocial_LaunchesGameBeforeSocial()
    {
        var sorted = new List<KeyValuePair<Type, StartUpTagAttribute>>
        {
            new(typeof(MultiRoleSelectionTestsRoleAlpha), new StartUpTagAttribute("Game", 100)),
            new(typeof(MultiRoleSelectionTestsRoleBeta), new StartUpTagAttribute("Social", 200)),
        };

        var selected = GameApp.SelectStartUpTypes(AllInOneOptions.Parse(["--ServerType=Social,Game"]), sorted);

        Assert.Equal(new[] { "Game", "Social" }, selected.Select(pair => pair.Value.ServerType));
    }

    /// <summary>
    /// 复数含未注册名：未注册名被跳过，不影响其余 Role。
    /// </summary>
    [Fact]
    public void SelectStartUpTypes_PluralWithUnregisteredName_SkipsUnregistered()
    {
        var selected = GameApp.SelectStartUpTypes(AllInOneOptions.Parse(["--ServerType=Alpha,Ghost"]), CreateSortedStartUpTypes());

        Assert.Equal(new[] { "Alpha" }, selected.Select(pair => pair.Value.ServerType));
    }

    /// <summary>
    /// 单 Role 现状形态：按名取唯一匹配。
    /// </summary>
    [Fact]
    public void SelectStartUpTypes_SingleServerType_ReturnsSingleMatch()
    {
        var selected = GameApp.SelectStartUpTypes(AllInOneOptions.Parse(["--ServerType=Beta"]), CreateSortedStartUpTypes());

        Assert.Equal(new[] { "Beta" }, selected.Select(pair => pair.Value.ServerType));
    }

    /// <summary>
    /// 单 Role 现状形态：未注册名返回空集合（保留现状静默跳过语义）。
    /// </summary>
    [Fact]
    public void SelectStartUpTypes_SingleUnregisteredServerType_ReturnsEmpty()
    {
        var selected = GameApp.SelectStartUpTypes(AllInOneOptions.Parse(["--ServerType=Ghost"]), CreateSortedStartUpTypes());

        Assert.Empty(selected);
    }

    /// <summary>
    /// 缺省形态（无 --ServerType）：取优先级最高的第一个可用 Role（现状行为）。
    /// </summary>
    [Fact]
    public void SelectStartUpTypes_Default_ReturnsFirstAvailableInPriorityOrder()
    {
        var selected = GameApp.SelectStartUpTypes(AllInOneOptions.Parse(Array.Empty<string>()), CreateSortedStartUpTypes());

        Assert.Equal(new[] { "Alpha" }, selected.Select(pair => pair.Value.ServerType));
    }

    /// <summary>
    /// 优先级冲突表：重复优先级输出对应分组文本，无冲突返回 null。
    /// </summary>
    [Fact]
    public void GetPriorityConflictTable_DuplicatePriorities_BuildsConflictTable()
    {
        var conflicting = new List<KeyValuePair<Type, StartUpTagAttribute>>
        {
            new(typeof(MultiRoleSelectionTestsRoleAlpha), new StartUpTagAttribute("Alpha", 1000)),
            new(typeof(MultiRoleSelectionTestsRoleBeta), new StartUpTagAttribute("Beta", 1000)),
            new(typeof(MultiRoleSelectionTestsRoleGamma), new StartUpTagAttribute("Gamma", 2000)),
        };

        var conflictTable = GameApp.GetPriorityConflictTable(conflicting);

        Assert.NotNull(conflictTable);
        Assert.Contains("priority 1000", conflictTable);
        Assert.Contains("Alpha", conflictTable);
        Assert.Contains("Beta", conflictTable);
        Assert.DoesNotContain("Gamma", conflictTable);

        Assert.Null(GameApp.GetPriorityConflictTable(CreateSortedStartUpTypes()));
    }

    /// <summary>
    /// 缺配置段的多个 Role 各获得独立 AppSetting：不共享启动器选项实例，ServerType 为当前 Role 名。
    /// </summary>
    [Fact]
    public void ResolveAppSetting_MissingConfigSection_EachRoleGetsIndependentSettingWithOwnServerType()
    {
        var launcherOptions = new StartupOptions { ServerType = "Alpha,Beta", ServerId = 42, InnerPort = 25000, };
        var appSettings = Array.Empty<AppSetting>();

        var alphaSetting = GameApp.ResolveAppSetting("Alpha", appSettings, launcherOptions, warnOnMissingConfiguration: false);
        var betaSetting = GameApp.ResolveAppSetting("Beta", appSettings, launcherOptions, warnOnMissingConfiguration: false);

        Assert.NotNull(alphaSetting);
        Assert.NotNull(betaSetting);
        Assert.NotSame(alphaSetting, betaSetting);
        Assert.NotSame(alphaSetting, launcherOptions);
        Assert.NotSame(betaSetting, launcherOptions);
        Assert.Equal("Alpha", alphaSetting.ServerType);
        Assert.Equal("Beta", betaSetting.ServerType);

        // 启动器选项值被复制到各 Role 副本，且副本互不影响
        Assert.Equal(42, alphaSetting.ServerId);
        Assert.Equal(42, betaSetting.ServerId);
        Assert.Equal(25000, alphaSetting.InnerPort);
        alphaSetting.InnerPort = 29999;
        Assert.Equal(25000, betaSetting.InnerPort);
        Assert.Equal(25000, launcherOptions.InnerPort);
    }

    /// <summary>
    /// 配置段存在时优先返回配置段实例（现状行为不变）。
    /// </summary>
    [Fact]
    public void ResolveAppSetting_ExistingConfigSection_ReturnsSectionInstance()
    {
        var sectionSetting = new AppSetting { ServerType = "Alpha", ServerId = 7, };
        var launcherOptions = new StartupOptions { ServerType = "Alpha,Beta", ServerId = 42, };

        var resolved = GameApp.ResolveAppSetting("Alpha", new[] { sectionSetting }, launcherOptions, warnOnMissingConfiguration: false);

        Assert.Same(sectionSetting, resolved);
    }

    /// <summary>
    /// 启动器选项不可用（参数解析失败）时返回 null，让 Role 的 Init 创建自己的缺省配置。
    /// </summary>
    [Fact]
    public void ResolveAppSetting_NullLauncherOptions_ReturnsNullForRoleDefaults()
    {
        var resolved = GameApp.ResolveAppSetting("Alpha", Array.Empty<AppSetting>(), null, warnOnMissingConfiguration: false);

        Assert.Null(resolved);
    }

    /// <summary>
    /// 测试用 Role 类。
    /// </summary>
    private sealed class MultiRoleSelectionTestsRoleAlpha
    {
    }

    /// <summary>
    /// 测试用 Role 类。
    /// </summary>
    private sealed class MultiRoleSelectionTestsRoleBeta
    {
    }

    /// <summary>
    /// 测试用 Role 类。
    /// </summary>
    private sealed class MultiRoleSelectionTestsRoleGamma
    {
    }
}
