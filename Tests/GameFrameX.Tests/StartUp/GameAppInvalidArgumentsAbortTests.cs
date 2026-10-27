// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and applicable international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository:  https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Foundation.Options;
using GameFrameX.Localization;
using GameFrameX.StartUp;
using GameFrameX.StartUp.Options;
using Xunit;

namespace GameFrameX.Tests.StartUp;

/// <summary>
/// 命令行参数解析失败 fail-fast 回归测试（GFX-1071 / C363）。
/// </summary>
/// <remarks>
/// Regression tests for the fail-fast behaviour of launcher argument parsing failures:
/// parsing failures must abort the process with a non-zero exit code after writing the
/// localized error to standard error, instead of silently falling back to an all-defaults
/// <c>StartupOptions</c> instance. The process abort goes through the replaceable
/// <see cref="GameApp.AbortProcess"/> seam so the test host survives.
/// </remarks>
public class GameAppInvalidArgumentsAbortTests
{
    /// <summary>
    /// 非法参数（int 选项传非数字）触发中止：退出码 1、stderr 携带本地化错误消息（含底层解析异常）。
    /// </summary>
    [Fact]
    public void ParseLauncherOptions_InvalidArguments_AbortsWithNonZeroExitCodeAndWritesStandardError()
    {
        var capturedExitCodes = new List<int>();
        var standardErrorWriter = new StringWriter();
        var originalAbortProcess = GameApp.AbortProcess;
        var originalStandardError = Console.Error;
        GameApp.AbortProcess = capturedExitCodes.Add;
        Console.SetError(standardErrorWriter);
        try
        {
            var options = GameApp.ParseLauncherOptions(["--ServerId=abc"]);

            // 退出缝被替换为记录器后控制流继续，走中止后的不可达回落分支（生产实现此处已终止进程）
            Assert.Equal(0, options.ServerId);
        }
        finally
        {
            GameApp.AbortProcess = originalAbortProcess;
            Console.SetError(originalStandardError);
        }

        Assert.Single(capturedExitCodes);
        Assert.Equal(GameApp.InvalidLauncherArgumentsExitCode, capturedExitCodes[0]);
        Assert.Equal(1, capturedExitCodes[0]);
        Assert.Equal(BuildExpectedAbortMessage(), standardErrorWriter.ToString().TrimEnd());
    }

    /// <summary>
    /// 合法参数不触发中止：退出缝未被调、选项正常绑定。
    /// </summary>
    [Fact]
    public void ParseLauncherOptions_ValidArguments_DoesNotAbortAndBindsOptions()
    {
        var abortInvoked = false;
        var originalAbortProcess = GameApp.AbortProcess;
        GameApp.AbortProcess = _ => abortInvoked = true;
        try
        {
            var options = GameApp.ParseLauncherOptions(["--ServerType=Game"]);

            Assert.False(abortInvoked);
            Assert.Equal("Game", options.ServerType);
        }
        finally
        {
            GameApp.AbortProcess = originalAbortProcess;
        }
    }

    /// <summary>
    /// 构造与生产中止路径一致的期望消息（同一输入、同一本地化服务，与文化环境无关）。
    /// </summary>
    /// <returns>期望的标准错误输出 / The expected standard error output</returns>
    private static string BuildExpectedAbortMessage()
    {
        string underlyingMessage;
        try
        {
            OptionsBuilder.CreateWithDebug<StartupOptions>(["--ServerId=abc"]);
            throw new InvalidOperationException("expected the same malformed arguments to keep failing");
        }
        catch (Exception exception)
        {
            underlyingMessage = exception.Message;
        }

        return string.Format(LocalizationService.GetString(Keys.StartUp.InvalidLauncherArguments), underlyingMessage, GameApp.InvalidLauncherArgumentsExitCode);
    }
}
