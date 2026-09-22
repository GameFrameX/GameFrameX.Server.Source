// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository:  https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:   https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:     https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using GameFrameX.NetWork.RemoteMessaging.Routing;

namespace GameFrameX.Tests.Topology.Equivalence;

/// <summary>
/// 多进程拓扑的环回远程转发器（C143c D9 等价用例集）。
/// </summary>
/// <remarks>
/// Loopback remote forwarder for the MultiProcess topology fixture (C143c D9).
/// Inside one test process the D3 case 2/3 remote hop cannot use real RemoteMessaging
/// (the production forwarder is the C143d placeholder that throws NotImplementedException),
/// so the fixture substitutes this loopback: it resolves the target role to the simulated
/// process cell that hosts it and routes the envelope into that cell's router — the same
/// decision C143d's reachability table will make, minus the physical transport.
/// Cancellation flows through the whole path so timeout behavior stays comparable
/// across topologies.
/// </remarks>
public sealed class LoopbackRemoteRoleRouter : IRemoteRoleRouter
{
    /// <summary>
    /// 目标 Role 名 → 承载进程 cell 的路由器。
    /// </summary>
    private readonly IReadOnlyDictionary<string, IRoleRouter> _cellRoutersByRole;

    /// <summary>
    /// 初始化环回转发器。
    /// </summary>
    /// <param name="cellRoutersByRole">目标 Role 名 → cell 路由器映射 / The map of target role name to the hosting cell's router</param>
    public LoopbackRemoteRoleRouter(IReadOnlyDictionary<string, IRoleRouter> cellRoutersByRole)
    {
        _cellRoutersByRole = cellRoutersByRole ?? throw new ArgumentNullException(nameof(cellRoutersByRole));
    }

    /// <summary>
    /// 将信封环回路由进承载目标 Role 的 cell。
    /// </summary>
    /// <param name="envelope">路由信封 / The routing envelope</param>
    /// <param name="cancellationToken">取消操作的令牌 / The cancellation token</param>
    /// <returns>恒为 <see cref="RoleRouteDelivery.RemoteForwarded"/> / Always <see cref="RoleRouteDelivery.RemoteForwarded"/></returns>
    /// <exception cref="RouteNotFoundException">当目标 Role 不在任何 cell 时抛出 / Thrown when no cell hosts the target role</exception>
    public async Task<RoleRouteDelivery> ForwardAsync(MessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        if (envelope == null)
        {
            throw new ArgumentNullException(nameof(envelope));
        }

        if (!_cellRoutersByRole.TryGetValue(envelope.TargetRole, out var cellRouter))
        {
            throw new RouteNotFoundException(envelope.TargetRole, $"No simulated process cell hosts target role '{envelope.TargetRole}'.");
        }

        await cellRouter.RouteAsync(envelope, cancellationToken);
        return RoleRouteDelivery.RemoteForwarded;
    }
}
