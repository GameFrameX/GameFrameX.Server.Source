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
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes and liabilities arising from secondary development based on this project
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


using GameFrameX.NetWork.RemoteMessaging.Discovery;

namespace GameFrameX.Tests.Discovery;

/// <summary>
/// RoleRouteTable 双视图准入用例集（C143d D15 / D3 case 2/3）。
/// </summary>
/// <remarks>
/// The dual-view admission suite for RoleRouteTable (C143d D15 / D3 case 2/3):
/// only Active and Draining instances may enter the Instance view (and only
/// Active the Role view), so a registered-but-not-ready (Booting) or
/// decommissioned (Stopped/Removed) instance is never routable.
/// </remarks>
public sealed class RoleRouteTableTests
{
    [Fact]
    public void FromInstances_ShouldExcludeNonRoutableStatusesFromBothViews()
    {
        var now = DateTime.UtcNow;
        var table = RoleRouteTable.FromInstances(new List<InstanceDescriptor>
        {
            new InstanceDescriptor("Game", "game-active-1", "tcp://10.0.0.1:7001", InstanceStatus.Active, 10, EndpointAddressKind.IPv4, 201, now),
            new InstanceDescriptor("Game", "game-draining-1", "tcp://10.0.0.2:7002", InstanceStatus.Draining, 20, EndpointAddressKind.IPv4, 202, now),
            new InstanceDescriptor("Game", "game-booting-1", "tcp://10.0.0.3:7003", InstanceStatus.Booting, 0, EndpointAddressKind.IPv4, 203, now),
            new InstanceDescriptor("Game", "game-stopped-1", "tcp://10.0.0.4:7004", InstanceStatus.Stopped, 0, EndpointAddressKind.IPv4, 204, now),
            new InstanceDescriptor("Game", "game-removed-1", "tcp://10.0.0.5:7005", InstanceStatus.Removed, 0, EndpointAddressKind.IPv4, 205, now),
        });

        // Instance 视图（D3 case 2）：仅 Active 与 Draining 可解析；Booting/Stopped/Removed 一律拒绝。
        Assert.True(table.TryGetInstance("game-active-1", out _));
        Assert.True(table.TryGetInstance("game-draining-1", out _));
        Assert.False(table.TryGetInstance("game-booting-1", out _));
        Assert.False(table.TryGetInstance("game-stopped-1", out _));
        Assert.False(table.TryGetInstance("game-removed-1", out _));

        // Role 视图（D3 case 3）：仅 Active 进入；Draining 不接新流量。
        var activeIds = table.GetActiveInstances("Game").Select(instance => instance.InstanceId).ToList();
        Assert.Equal(new List<string> { "game-active-1" }, activeIds);
    }
}
