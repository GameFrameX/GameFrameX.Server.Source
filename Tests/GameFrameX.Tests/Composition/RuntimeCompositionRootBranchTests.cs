// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository:  https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:    https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:      https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.IO;
using System.Net;
using System.Net.Sockets;
using GameFrameX.Launcher.Composition;
using GameFrameX.Utility.Setting;

namespace GameFrameX.Tests.Composition;

/// <summary>
/// 验证真实 <see cref="RuntimeCompositionRoot.ComposeAndStartAsync"/> 三分支形态与失败回滚：
/// Online 运行面启用 = 族运行时；仅管理面启用 = admin-only 族运行时（返回 null，运行时经
/// <c>DetachAdminOnlyFamilyRuntime</c> 摘取停止，恰好一次）；双闸全关 = 返回 null 不组合；
/// admin 口端口被占用时组合失败自行回滚已启动部分并重抛原异常。
/// </summary>
public class RuntimeCompositionRootBranchTests
{
    /// <summary>
    /// 构造测试配置：合法区服键 + 指定 Online 双闸形态 + 探测到的空闲 admin 口。
    /// </summary>
    /// <param name="configureOnline">Online 双闸配置动作。</param>
    /// <returns>应用配置。</returns>
    private static AppSetting CreateSetting(Action<OnlineCapabilityOptions> configureOnline)
    {
        var setting = new AppSetting { ServerId = 1001, };
        var online = new OnlineCapabilityOptions();
        configureOnline(online);
        setting.Online = online;
        setting.OnlineAdminPort = (ushort)FindFreeLoopbackPort();
        return setting;
    }

    /// <summary>探测一个当前可绑定的回环空闲端口（探测后立即释放；admin 口测试用）。</summary>
    /// <returns>空闲端口号。</returns>
    private static int FindFreeLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>
    /// Online 运行面启用分支：返回已启动的族运行时；admin-only 槽位为空（摘取返回 null）。
    /// </summary>
    [Fact]
    public async Task ComposeAndStart_RuntimeEnabled_ComposesFamilyRuntime()
    {
        var root = new RuntimeCompositionRoot();
        var setting = CreateSetting(online => online.EnableRuntime = true);
        var runtime = await root.ComposeAndStartAsync(setting);
        Assert.NotNull(runtime);
        try
        {
            Assert.Null(root.DetachAdminOnlyFamilyRuntime(setting.ServerType));
        }
        finally
        {
            await runtime.StopAsync();
        }
    }

    /// <summary>
    /// 仅管理面启用分支：返回 null，admin-only 族运行时（真实 admin 口已开放）经摘取获得且恰好一次。
    /// </summary>
    [Fact]
    public async Task ComposeAndStart_AdminOnlyOnly_ReturnsNull_RuntimeDetachableExactlyOnce()
    {
        var root = new RuntimeCompositionRoot();
        var setting = CreateSetting(online =>
        {
            online.EnableRuntime = false;
            online.EnableAdminApi = true;
        });
        Assert.Null(await root.ComposeAndStartAsync(setting));

        var adminRuntime = root.DetachAdminOnlyFamilyRuntime(setting.ServerType);
        Assert.NotNull(adminRuntime);
        Assert.Null(root.DetachAdminOnlyFamilyRuntime(setting.ServerType));
        await adminRuntime.StopAsync();
    }

    /// <summary>
    /// 双闸全关分支：返回 null，不做任何组合（无族宿主空壳运行时形态已随宿主模块槽退役）。
    /// </summary>
    [Fact]
    public async Task ComposeAndStart_AllGatesOff_ReturnsNullWithoutComposing()
    {
        var root = new RuntimeCompositionRoot();
        var setting = CreateSetting(online =>
        {
            online.EnableRuntime = false;
            online.EnableAdminApi = false;
        });
        Assert.Null(await root.ComposeAndStartAsync(setting));
        Assert.Null(root.DetachAdminOnlyFamilyRuntime(setting.ServerType));
    }

    /// <summary>
    /// admin 组合失败回滚：admin 口端口被占用时 Kestrel 启动失败，组合根撤回已启动部分并重抛，
    /// admin-only 槽位不留残留运行时（摘取返回 null）。
    /// </summary>
    [Fact]
    public async Task ComposeAndStart_AdminPortOccupied_FailsFastAndRollsBack()
    {
        var root = new RuntimeCompositionRoot();
        using (var occupied = new TcpListener(IPAddress.Any, 0))
        {
            occupied.Start();
            var setting = CreateSetting(online =>
            {
                online.EnableRuntime = false;
                online.EnableAdminApi = true;
            });
            setting.OnlineAdminPort = (ushort)((IPEndPoint)occupied.LocalEndpoint).Port;

            await Assert.ThrowsAsync<IOException>(() => root.ComposeAndStartAsync(setting));
            Assert.Null(root.DetachAdminOnlyFamilyRuntime(setting.ServerType));
        }
    }
}
