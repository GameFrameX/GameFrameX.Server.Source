// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
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
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:   https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Foundation.Logger;
using GameFrameX.Network.Abstractions;

namespace GameFrameX.Network.RemoteMessaging.Versioning;

/// <summary>
/// 服务端漏斗的协议版本握手短路。位于业务 handler 分发之前（与心跳短路同级）：
/// 收到 <see cref="ReqProtocolVersionNegotiation"/> 时按本端 <see cref="RemoteMessagingProtocolVersion"/>
/// 回 <see cref="RespProtocolVersionNegotiation"/>；major 不符回拒绝包、告警并在宽限期后关闭会话
/// （拒绝包先送达，避免与在途发送竞争；无半开残留）。旧拨号端不发握手帧，短路不触发，行为与升级前等价。
/// </summary>
/// <remarks>
/// The server funnel's protocol version handshake short circuit. It sits before business handler
/// dispatch (the same level as the heartbeat short circuit): upon receiving a
/// <see cref="ReqProtocolVersionNegotiation"/>, it replies with a
/// <see cref="RespProtocolVersionNegotiation"/> built from the local
/// <see cref="RemoteMessagingProtocolVersion"/>; on a major mismatch it replies with a rejection,
/// logs a warning, and closes the session after a grace period (the rejection is delivered first,
/// avoiding a race with in-flight sends; no half-open residue). Legacy dialers send no handshake
/// frame, so the short circuit never triggers and behavior stays identical to before the upgrade.
/// </remarks>
public static class ProtocolVersionNegotiationServer
{
    /// <summary>
    /// 拒绝后的会话关闭宽限期（毫秒）：保证拒绝包先于连接关闭送达对端。
    /// </summary>
    /// <remarks>
    /// The grace period (milliseconds) before closing a rejected session: it guarantees the
    /// rejection reaches the peer ahead of the connection teardown.
    /// </remarks>
    private const int RejectCloseGraceMs = 250;

    /// <summary>
    /// 判断消息包是否为协议版本握手请求。
    /// </summary>
    /// <remarks>
    /// Determines whether the message package is a protocol version handshake request.
    /// </remarks>
    /// <param name="messagePackage">收到的消息包 / The received message package</param>
    /// <returns>是握手请求时返回 true / true when the package is a handshake request</returns>
    public static bool IsHandshakeRequest(INetworkMessagePackage messagePackage)
    {
        return messagePackage.MessageType == typeof(ReqProtocolVersionNegotiation);
    }

    /// <summary>
    /// 处理握手请求：反序列化 → major 比对 → 经网络通道回包；major 不符告警并在宽限期后关闭通道。
    /// </summary>
    /// <remarks>
    /// Handles a handshake request: deserialize, compare majors, reply through the network channel;
    /// on a major mismatch, log a warning and close the channel after the grace period.
    /// </remarks>
    /// <param name="networkChannel">会话网络通道 / The session network channel</param>
    /// <param name="messagePackage">握手消息包 / The handshake message package</param>
    public static async ValueTask ReplyAsync(INetworkChannel networkChannel, INetworkMessagePackage messagePackage)
    {
        if (messagePackage.DeserializeMessageObject() is not ReqProtocolVersionNegotiation request)
        {
            return;
        }

        var reply = BuildReply(request, RemoteMessagingProtocolVersion.Current);
        reply.UniqueId = request.UniqueId;
        await networkChannel.WriteAsync(reply);

        if (!reply.Accepted)
        {
            // 拒绝路径：回包后关闭会话，避免跨 major 半开连接残留
            // Localization: RemoteMessaging.Versioning.ServerRejectedIncompatibleClient - 拒绝协议 major 不符的连接：对端 {0}，本端 {1}。回包后关闭会话。
            LogHelper.Warning(LocalizationService.GetString(
                Localization.Keys.RemoteMessaging.Versioning.ServerRejectedIncompatibleClient,
                new ProtocolVersion(request.Major, request.Minor),
                RemoteMessagingProtocolVersion.Current));

            // 宽限期后再关闭：立即关闭会与在途发送竞争，可能丢弃刚写出的拒绝包。
            // 显式 await（C270）：拒绝路径是该漏斗的终结分支（回包→关连接），等待 250ms 不影响其它会话；
            // 关闭期异常由此处可见（不再 fire-and-forget 吞掉）。
            await CloseAfterGraceAsync(networkChannel);
        }
    }

    /// <summary>
    /// 构建握手响应：携带本端版本与 major 比对结论（ErrorCode 0=接受 / 1=major 不符拒绝）。
    /// </summary>
    /// <remarks>
    /// Builds the handshake reply: it carries the local version and the major-comparison verdict
    /// (ErrorCode 0 = accepted, 1 = rejected due to a major mismatch).
    /// </remarks>
    /// <param name="request">握手请求 / The handshake request</param>
    /// <param name="localVersion">本端协议版本（默认 <see cref="RemoteMessagingProtocolVersion.Current"/>）/ The local protocol version (defaults to <see cref="RemoteMessagingProtocolVersion.Current"/>)</param>
    /// <returns>握手响应（未回填 UniqueId，由调用方回填请求身份）/ The handshake reply (UniqueId not yet echoed; the caller fills in the request identity)</returns>
    internal static RespProtocolVersionNegotiation BuildReply(ReqProtocolVersionNegotiation request, ProtocolVersion localVersion)
    {
        var peer = new ProtocolVersion(request.Major, request.Minor);

        // 连接级拒绝语义与协商器一致：major 相等即接受（minor 差异属向后兼容演进）
        var accepted = peer.Major == localVersion.Major;
        return new RespProtocolVersionNegotiation
        {
            Major = localVersion.Major,
            Minor = localVersion.Minor,
            Accepted = accepted,
            ErrorCode = accepted ? 0 : 1,
        };
    }

    /// <summary>
    /// 宽限期后关闭通道（对端可能已先行关闭；关闭失败不吞——留 Warning 供排障，语义不受影响）。
    /// </summary>
    /// <remarks>
    /// Closes the channel after the grace period. The peer may have closed first; a failed
    /// close is logged as a warning instead of being swallowed — the rejection has already
    /// been delivered, so the semantics are unaffected.
    /// </remarks>
    /// <param name="networkChannel">会话网络通道 / The session network channel</param>
    private static async Task CloseAfterGraceAsync(INetworkChannel networkChannel)
    {
        try
        {
            await Task.Delay(RejectCloseGraceMs);
            networkChannel.Close();
        }
        catch (Exception exception)
        {
            // 通道可能已在对端关闭竞态中释放；拒绝结论已回包，关闭失败不影响语义——留告警可见（C270）
            // Localization: RemoteMessaging.Versioning.ServerRejectCloseFailed - 拒绝握手后延迟关闭通道失败（对端可能已先行关闭；拒绝包已送达，语义不受影响）
            LogHelper.Warning(exception, LocalizationService.GetString(Localization.Keys.RemoteMessaging.Versioning.ServerRejectCloseFailed));
        }
    }
}
