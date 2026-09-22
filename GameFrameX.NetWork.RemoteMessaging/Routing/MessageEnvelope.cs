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
/// 跨 Role 路由信封（C143c D3）。
/// </summary>
/// <remarks>
/// Envelope for cross-role message routing (C143c D3).
/// Carries everything the role routing seam needs to make its three-step decision:
/// the target role name (process locality check), an optional target instance id
/// (distinguishes D3 case 2 "known instance" from case 3 "any active instance"),
/// the target actor id used by local in-process delivery, and the message payload itself.
/// The envelope is immutable after construction; routing never mutates it.
/// </remarks>
public sealed class MessageEnvelope
{
    /// <summary>
    /// 初始化路由信封。
    /// </summary>
    /// <remarks>
    /// Initializes the routing envelope.
    /// An empty or whitespace target role is intentionally not rejected here:
    /// role-name validity is a routing decision, and the router fails it loudly with
    /// <see cref="RouteNotFoundException"/> instead of a silent fallback (C143c risk mitigation).
    /// </remarks>
    /// <param name="targetRole">目标 Role 的服务器类型名 / The target role server type name</param>
    /// <param name="message">要路由的消息 / The message to route</param>
    /// <param name="targetActorId">本地投递目标 ActorId（跨进程跳时忽略）/ The local delivery target actor id (ignored on remote hops)</param>
    /// <param name="targetInstanceId">可选的目标实例 Id（非空走 D3 case 2，空走 case 3）/ Optional target instance id (non-null selects D3 case 2, null selects case 3)</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="message"/> 为 null 时抛出 / Thrown when <paramref name="message"/> is null</exception>
    public MessageEnvelope(string targetRole, MessageObject message, long targetActorId = 0, string targetInstanceId = null)
    {
        ArgumentNullException.ThrowIfNull(message, nameof(message));

        TargetRole = targetRole;
        Message = message;
        TargetActorId = targetActorId;
        TargetInstanceId = targetInstanceId;
    }

    /// <summary>
    /// 获取目标 Role 的服务器类型名。
    /// </summary>
    /// <remarks>
    /// Gets the target role server type name.
    /// </remarks>
    /// <value>目标 Role 名 / The target role name</value>
    public string TargetRole { get; }

    /// <summary>
    /// 获取要路由的消息。
    /// </summary>
    /// <remarks>
    /// Gets the message to route.
    /// </remarks>
    /// <value>消息负载 / The message payload</value>
    public MessageObject Message { get; }

    /// <summary>
    /// 获取本地投递目标 ActorId。
    /// </summary>
    /// <remarks>
    /// Gets the local delivery target actor id; ignored on remote hops.
    /// </remarks>
    /// <value>本地投递目标 ActorId / The local delivery target actor id</value>
    public long TargetActorId { get; }

    /// <summary>
    /// 获取可选的目标实例 Id。
    /// </summary>
    /// <remarks>
    /// Gets the optional target instance id. A non-null value selects D3 case 2
    /// (forward to a known instance); <c>null</c> selects D3 case 3 (any active instance of the role).
    /// </remarks>
    /// <value>目标实例 Id；未指定时为 <c>null</c> / The target instance id, or <c>null</c> when unspecified</value>
    public string TargetInstanceId { get; }
}
