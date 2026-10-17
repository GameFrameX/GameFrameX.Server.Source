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
//   please refer to the LICENSE file in the root of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Network.RemoteMessaging.Transport;

/// <summary>
/// 进程级共享 TCP 连接池（C270）。RPC 客户端（<see cref="RemoteMessagingBuilder.Build"/>）
/// 与跨 Role 信封转发层（<see cref="Routing.TcpEnvelopeForwarder"/>）历史上各建连接池，对同一目标端点
/// 双份开满连接；本 Holder 收敛为进程单例——同端点连接只开一份，两个消费层经池内每连接写门控流安全共享。
/// </summary>
/// <remarks>
/// The process-level shared TCP connection pool (C270). The RPC client
/// (<see cref="RemoteMessagingBuilder.Build"/>) and the cross-role envelope forwarder
/// (<see cref="Routing.TcpEnvelopeForwarder"/>) used to build their own pools, opening a doubled set of
/// connections to the same target endpoint. This holder converges them into one process-wide singleton —
/// a single set of connections per endpoint, safely shared by both consumers through the per-connection
/// write-gated streams owned by the pool.
/// <para>生命周期：池随进程存续，任何消费方不得对其实施 <see cref="IDisposable.Dispose"/>（多消费方共享
/// 单例后，单方释放会误杀他方在途连接）。进程级 Holder 先例：<see cref="Routing.RoleRouterHolder"/> /
/// <see cref="Unified.UnifiedMessageSenderHolder"/>。</para>
/// <para>多 <see cref="RemoteMessagingBuilder.Build"/> 调用共享同一池（SDK 行为变化）：
/// 生产形态每进程仅构建一次客户端；同进程多实例消费方共享连接面是本收敛的预期语义。</para>
/// </remarks>
internal static class SharedTcpConnectionPool
{
    /// <summary>
    /// 进程唯一的 TCP 连接提供器。
    /// </summary>
    /// <remarks>
    /// The process-wide unique TCP connection provider.
    /// </remarks>
    internal static IConnectionProvider Provider { get; } = new TcpConnectionProvider();
}
