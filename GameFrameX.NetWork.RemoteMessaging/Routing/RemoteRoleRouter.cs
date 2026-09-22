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


namespace GameFrameX.NetWork.RemoteMessaging.Routing;

/// <summary>
/// 跨进程 Role 转发占位实现（C143c → C143d）。
/// </summary>
/// <remarks>
/// Placeholder implementation of the D3 case 2/3 remote forwarding seam.
/// Every forward attempt throws <see cref="NotImplementedException"/> on purpose:
/// real forwarding needs the endpoint reachability table and ForwardToRemoteServerAsync,
/// which are delivered by change C143d. Keeping the placeholder as the production default
/// makes premature cross-process routing fail loudly at the seam instead of silently
/// dead-lettering messages. Topology equivalence tests replace it with a loopback forwarder.
/// </remarks>
public sealed class RemoteRoleRouter : IRemoteRoleRouter
{
    /// <summary>
    /// 转发占位：恒抛 <see cref="NotImplementedException"/>。
    /// </summary>
    /// <remarks>
    /// Placeholder forward: always throws <see cref="NotImplementedException"/>.
    /// </remarks>
    /// <param name="envelope">路由信封 / The routing envelope</param>
    /// <param name="cancellationToken">取消操作的令牌 / The cancellation token</param>
    /// <returns>恒不返回 / Never returns</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="envelope"/> 为 null 时抛出 / Thrown when <paramref name="envelope"/> is null</exception>
    /// <exception cref="NotImplementedException">恒抛出，等待 C143d 实现可达表转发 / Always thrown until C143d implements reachability-table forwarding</exception>
    public Task<RoleRouteDelivery> ForwardAsync(MessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope, nameof(envelope));

        throw new NotImplementedException(
            $"Remote role forwarding for target role '{envelope.TargetRole}' (D3 case 2/3) is not implemented yet; it arrives with change C143d together with the endpoint reachability table.");
    }
}
