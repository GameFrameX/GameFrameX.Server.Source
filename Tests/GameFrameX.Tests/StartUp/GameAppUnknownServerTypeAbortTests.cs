// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and applicable international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please refer to the LICENSE file in the source code root directory for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or otherwise infringe upon the legal rights and interests of others as prohibited by laws and regulations.
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes and liabilities arising from secondary development based on this project
//   shall be borne solely by the developer, the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository:  https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:     https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using GameFrameX.Foundation.Options;
using GameFrameX.StartUp;
using GameFrameX.StartUp.Options;

namespace GameFrameX.Tests.StartUp;

/// <summary>
/// 显式指定 ServerType 未命中任何已注册 Role 时的拒启判定与退出码测试（C364 / GFX-1072）。
/// </summary>
public class GameAppUnknownServerTypeAbortTests
{
    /// <summary>
    /// 单个未注册 ServerType：判定为应拒启（旧代码无此判定路径，静默空集合退出码 0）。
    /// </summary>
    [Fact]
    public void ShouldAbortForUnresolvedServerTypes_SingleUnknownServerType_ReturnsTrue()
    {
        var launcherOptions = OptionsBuilder.CreateFromArgsOnly<StartupOptions>(["--ServerType=Ghost"]);

        Assert.True(GameApp.ShouldAbortForUnresolvedServerTypes(launcherOptions));
    }

    /// <summary>
    /// 复数 ServerType 全部未注册：判定为应拒启。
    /// </summary>
    [Fact]
    public void ShouldAbortForUnresolvedServerTypes_PluralAllUnknownServerTypes_ReturnsTrue()
    {
        var launcherOptions = OptionsBuilder.CreateFromArgsOnly<StartupOptions>(["--ServerType=GhostOne,GhostTwo"]);

        Assert.True(GameApp.ShouldAbortForUnresolvedServerTypes(launcherOptions));
    }

    /// <summary>
    /// AllInOne 覆盖选择：即使并列 --ServerType 也不拒启（缺省启动全部已注册 Role）。
    /// </summary>
    [Fact]
    public void ShouldAbortForUnresolvedServerTypes_AllInOne_ReturnsFalse()
    {
        var launcherOptions = OptionsBuilder.CreateFromArgsOnly<StartupOptions>(["--ServerType=Ghost", "--AllInOne"]);

        Assert.False(GameApp.ShouldAbortForUnresolvedServerTypes(launcherOptions));
    }

    /// <summary>
    /// 缺省形态（无 --ServerType）：不拒启，保留现状缺省档行为。
    /// </summary>
    [Fact]
    public void ShouldAbortForUnresolvedServerTypes_NoServerType_ReturnsFalse()
    {
        var launcherOptions = OptionsBuilder.CreateFromArgsOnly<StartupOptions>([]);

        Assert.False(GameApp.ShouldAbortForUnresolvedServerTypes(launcherOptions));
    }

    /// <summary>
    /// 拒启路径经退出缝以 UnresolvedServerTypeExitCode（1）终止：守护系统按失败处理触发重启/告警。
    /// </summary>
    [Fact]
    public void AbortStartupForUnresolvedServerTypes_ExitsThroughSeamWithNonZeroExitCode()
    {
        var originalAbortProcess = GameApp.AbortProcess;
        var observedExitCodes = new List<int>();
        GameApp.AbortProcess = exitCode =>
        {
            observedExitCodes.Add(exitCode);
        };

        try
        {
            var launcherOptions = OptionsBuilder.CreateFromArgsOnly<StartupOptions>(["--ServerType=Ghost"]);

            GameApp.AbortStartupForUnresolvedServerTypes(launcherOptions);
        }
        finally
        {
            GameApp.AbortProcess = originalAbortProcess;
        }

        var exitCode = Assert.Single(observedExitCodes);
        Assert.Equal(GameApp.UnresolvedServerTypeExitCode, exitCode);
        Assert.Equal(1, exitCode);
    }
}
