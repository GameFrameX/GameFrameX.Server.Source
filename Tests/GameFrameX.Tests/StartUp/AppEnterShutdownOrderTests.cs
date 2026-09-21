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
using GameFrameX.StartUp.Abstractions;
using GameFrameX.Utility.Setting;
using Xunit;

namespace GameFrameX.Tests.StartUp;

/// <summary>
/// AppEnter 逆序停机测试（C143b D7：启动优先级序拉起，低优先级先停）。
/// </summary>
public class AppEnterShutdownOrderTests
{
    /// <summary>
    /// 多宿主按启动顺序的逆序停机（最后启动的先停）。
    /// </summary>
    [Fact]
    public async Task StopHostsInReverseOrderAsync_MultipleHosts_StopsInReverseLaunchOrder()
    {
        var stopOrder = new List<string>();
        var hosts = new List<IAppStartUp>
        {
            new RecordingStartUp("Alpha", stopOrder),
            new RecordingStartUp("Beta", stopOrder),
            new RecordingStartUp("Gamma", stopOrder),
        };

        await AppEnter.StopHostsInReverseOrderAsync(hosts, "unit-test");

        Assert.Equal(new[] { "Gamma", "Beta", "Alpha" }, stopOrder);
    }

    /// <summary>
    /// 单宿主（现状形态）只停自身。
    /// </summary>
    [Fact]
    public async Task StopHostsInReverseOrderAsync_SingleHost_StopsTheHost()
    {
        var stopOrder = new List<string>();
        var hosts = new List<IAppStartUp> { new RecordingStartUp("Alpha", stopOrder) };

        await AppEnter.StopHostsInReverseOrderAsync(hosts, "unit-test");

        Assert.Equal(new[] { "Alpha" }, stopOrder);
    }

    /// <summary>
    /// 集合中的 null 宿主被跳过，不影响其余宿主的停机顺序。
    /// </summary>
    [Fact]
    public async Task StopHostsInReverseOrderAsync_NullEntry_Skipped()
    {
        var stopOrder = new List<string>();
        var hosts = new List<IAppStartUp>
        {
            new RecordingStartUp("Alpha", stopOrder),
            null,
            new RecordingStartUp("Gamma", stopOrder),
        };

        await AppEnter.StopHostsInReverseOrderAsync(hosts, "unit-test");

        Assert.Equal(new[] { "Gamma", "Alpha" }, stopOrder);
    }

    /// <summary>
    /// 记录停机顺序的假宿主。
    /// </summary>
    private sealed class RecordingStartUp : IAppStartUp
    {
        private readonly List<string> _stopOrder;

        public RecordingStartUp(string serverType, List<string> stopOrder)
        {
            ServerType = serverType;
            _stopOrder = stopOrder;
        }

        public Task<string> AppExitToken
        {
            get { return Task.FromResult<string>(null); }
        }

        public string ServerType { get; }

        public AppSetting Setting { get; } = new AppSetting();

        public bool Init(string serverType, AppSetting setting, string[] args = null)
        {
            return true;
        }

        public Task StartAsync()
        {
            return Task.CompletedTask;
        }

        public Task StopAsync(string message = "")
        {
            _stopOrder.Add(ServerType);
            return Task.CompletedTask;
        }
    }
}
