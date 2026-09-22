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
/// 本地 Role 消息投递缝（C143c D3 case 1）。
/// </summary>
/// <remarks>
/// The local in-process delivery seam behind D3 case 1.
/// Production delivery goes through the actor pipeline (Actor.Tell/SendAsync semantics);
/// this interface is the explicit seam so the routing decision itself stays free of
/// actor-world dependencies (GameFrameX.Core is outside this assembly's reference closure).
/// The production actor-backed dispatcher arrives with C143e; until then the router is
/// wired without one and a case 1 hit fails loudly with <see cref="RouteNotFoundException"/>.
/// </remarks>
public interface ILocalRoleMessageDispatcher
{
    /// <summary>
    /// 将信封投递给本进程目标 Role 的消息处理队列。
    /// </summary>
    /// <remarks>
    /// Delivers the envelope to the target role's in-process message handling queue.
    /// The returned task completes when the message has been handed to (or processed by)
    /// the target queue, preserving per-role FIFO ordering for messages routed in order.
    /// </remarks>
    /// <param name="envelope">路由信封（目标 Role 必属于本进程角色集）/ The routing envelope (target role is hosted by this process)</param>
    /// <param name="cancellationToken">取消操作的令牌 / The cancellation token</param>
    Task DispatchAsync(MessageEnvelope envelope, CancellationToken cancellationToken = default);
}
