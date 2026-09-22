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
/// 跨 Role 消息路由缝（C143c D3）。
/// </summary>
/// <remarks>
/// The cross-role message routing seam (C143c D3).
/// Implementations apply the three-step decision to every envelope:
/// target role hosted by this process goes to local in-process delivery (case 1);
/// everything else is handed to the remote forwarding seam (case 2/3, delivered by C143d).
/// Business code reaches the process-wide instance through <see cref="RoleRouterHolder"/>.
/// </remarks>
public interface IRoleRouter
{
    /// <summary>
    /// 路由一封跨 Role 消息信封。
    /// </summary>
    /// <remarks>
    /// Routes one cross-role message envelope.
    /// </remarks>
    /// <param name="envelope">路由信封 / The routing envelope</param>
    /// <param name="cancellationToken">取消操作的令牌 / The cancellation token</param>
    /// <returns>实际命中的投递分支 / The delivery branch that was actually hit</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="envelope"/> 为 null 时抛出 / Thrown when <paramref name="envelope"/> is null</exception>
    /// <exception cref="RouteNotFoundException">当路由决策失败（空目标 Role / 缺本地投递器 / 缺远程转发器）时抛出 / Thrown when routing cannot decide a route</exception>
    Task<RoleRouteDelivery> RouteAsync(MessageEnvelope envelope, CancellationToken cancellationToken = default);
}
