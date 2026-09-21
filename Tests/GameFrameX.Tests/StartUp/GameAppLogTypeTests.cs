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
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using GameFrameX.Foundation.Logger;
using GameFrameX.StartUp;
using Xunit;

namespace GameFrameX.Tests.StartUp;

/// <summary>
/// GameApp 进程级固定日志标识的单元测试（C143a D20#4）。
/// </summary>
/// <remarks>
/// Unit tests for the process-level fixed log type of GameApp (C143a D20#4):
/// the log type is assigned only on the first call, so it does not drift
/// to the last started role in a multi-role process.
/// </remarks>
public class GameAppLogTypeTests : IDisposable
{
    /// <summary>
    /// 构造函数：每个测试前清空 LogType，隔离静态状态。
    /// </summary>
    public GameAppLogTypeTests()
    {
        LogOptions.Default.LogType = null;
    }

    /// <summary>
    /// 释放：测试后恢复，避免静态状态泄漏到其它测试。
    /// </summary>
    public void Dispose()
    {
        LogOptions.Default.LogType = null;
    }

    /// <summary>
    /// 首次调用赋值。
    /// </summary>
    [Fact]
    public void SetLogTypeOnce_FirstCall_Assigns()
    {
        GameApp.SetLogTypeOnce("Game");

        Assert.Equal("Game", LogOptions.Default.LogType);
    }

    /// <summary>
    /// 二次调用（模拟同进程第二个 Role 启动）不覆盖：LogType 不漂移到最后启动的 Role（D20#4）。
    /// </summary>
    [Fact]
    public void SetLogTypeOnce_SecondCall_DoesNotDrift()
    {
        GameApp.SetLogTypeOnce("Game");

        GameApp.SetLogTypeOnce("Social");

        Assert.Equal("Game", LogOptions.Default.LogType);
    }

    /// <summary>
    /// 空 serverType 不赋值（保持原值）。
    /// </summary>
    [Fact]
    public void SetLogTypeOnce_EmptyServerType_KeepsCurrentValue()
    {
        LogOptions.Default.LogType = "Existing";

        GameApp.SetLogTypeOnce(string.Empty);

        Assert.Equal("Existing", LogOptions.Default.LogType);
    }
}
