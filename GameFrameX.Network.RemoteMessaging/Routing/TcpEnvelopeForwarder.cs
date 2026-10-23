// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
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
//   Any legal disputes and liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:  https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using System.Net.Sockets;
using GameFrameX.Network.RemoteMessaging.Transport;
using GameFrameX.ProtoBuf.Net;

namespace GameFrameX.Network.RemoteMessaging.Routing;

/// <summary>
/// TCP 信封转发器（case 2/3 默认发送通道）。
/// </summary>
/// <remarks>
/// The default TCP send channel for case 2/3. It dials through the process-level shared
/// TCP connection pool (<see cref="Transport.SharedTcpConnectionPool"/>) — the very
/// pool the RPC client consumes — so the routing layer and the RPC layer share one set of
/// outbound connections per endpoint instead of doubling them. Whole-frame atomicity on a
/// shared connection is guaranteed by the pool's per-connection write-gated streams; frames
/// reuse the standard codec layout, making the bytes compatible with the existing protocol
/// stack. A write failure invalidates only the connection that failed (stream-scoped), so the
/// next forward reconnects without disturbing the RPC layer's other connections.
/// </remarks>
public sealed class TcpEnvelopeForwarder : IEnvelopeForwarder
{
    /// <summary>
    /// 消息编解码器（标准帧格式）。
    /// </summary>
    /// <remarks>
    /// The message codec (the standard frame layout).
    /// </remarks>
    private readonly IMessageCodec _messageCodec;

    /// <summary>
    /// 连接提供器（进程共享池；转发层不拥有连接资源）。
    /// </summary>
    /// <remarks>
    /// The connection provider (the process-shared pool; the forwarder owns no connection
    /// resources of its own).
    /// </remarks>
    private readonly IConnectionProvider _connectionProvider;

    /// <summary>
    /// 初始化 TCP 信封转发器（标准编解码器 + 进程共享连接池）。
    /// </summary>
    /// <remarks>
    /// Initializes the forwarder with the default codec and the process-shared connection pool.
    /// </remarks>
    public TcpEnvelopeForwarder()
        : this(new DefaultMessageCodec())
    {
    }

    /// <summary>
    /// 初始化 TCP 信封转发器（指定编解码器；注入用）。
    /// </summary>
    /// <remarks>
    /// Initializes the forwarder with a specific codec (injection point) on the
    /// process-shared connection pool.
    /// </remarks>
    /// <param name="messageCodec">消息编解码器 / The message codec</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="messageCodec"/> 为 null 时抛出 / Thrown when <paramref name="messageCodec"/> is null</exception>
    public TcpEnvelopeForwarder(IMessageCodec messageCodec)
        : this(messageCodec, Transport.SharedTcpConnectionPool.Provider)
    {
    }

    /// <summary>
    /// 初始化 TCP 信封转发器（指定编解码器与连接提供器；隔离测试用注入缝）。
    /// </summary>
    /// <remarks>
    /// Initializes the forwarder with a specific codec and connection provider (an injection
    /// seam for isolated tests). Production wiring always passes the process-shared pool.
    /// </remarks>
    /// <param name="messageCodec">消息编解码器 / The message codec</param>
    /// <param name="connectionProvider">连接提供器 / The connection provider</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="messageCodec"/> 或 <paramref name="connectionProvider"/> 为 null 时抛出 / Thrown when message codec or connection provider is null</exception>
    public TcpEnvelopeForwarder(IMessageCodec messageCodec, IConnectionProvider connectionProvider)
    {
        ArgumentNullException.ThrowIfNull(messageCodec);
        ArgumentNullException.ThrowIfNull(connectionProvider);

        _messageCodec = messageCodec;
        _connectionProvider = connectionProvider;
    }

    /// <summary>
    /// 将路由信封编码为标准帧并写到目标端点。
    /// </summary>
    /// <remarks>
    /// Encodes the envelope into a standard frame and writes it to the target
    /// endpoint's pooled stream. Transport failures invalidate only the affected connection
    /// (stream-scoped, so the RPC layer's other pooled connections stay untouched) and
    /// propagate to the caller.
    /// </remarks>
    /// <param name="endpoint">已解析的目标端点 / The parsed target endpoint</param>
    /// <param name="envelope">路由信封 / The routing envelope</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>异步任务 / Async task</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="endpoint"/> 或 <paramref name="envelope"/> 为 null 时抛出 / Thrown when endpoint or envelope is null</exception>
    public async Task ForwardAsync(ParsedEndpoint endpoint, MessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(envelope);

        var envelopeMessage = new RoleRouteEnvelopeMessage
        {
            TargetRole = envelope.TargetRole,
            TargetActorId = envelope.TargetActorId,
            TargetInstanceId = envelope.TargetInstanceId,
            InnerMessageId = envelope.Message.MessageId,
            InnerMessageBytes = ProtoBufSerializerHelper.Serialize(envelope.Message),
        };
        envelopeMessage.SetMessageId(RoleRouteEnvelopeMessage.ReservedMessageId);

        using (var frame = _messageCodec.Encode(envelopeMessage))
        {
            var stream = await _connectionProvider.GetOrCreateStreamAsync(endpoint.Host, endpoint.Port, cancellationToken);
            try
            {
                // 整帧原子性由池内写门控流保证（共享池下 RPC 客户端与转发层可能命中同一条连接）
                await stream.WriteAsync(frame.Memory, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is IOException || exception is SocketException || exception is OperationCanceledException)
            {
                // 流粒度失效：共享池上全量失效会误杀 RPC 层在途连接
                _connectionProvider.Invalidate(stream);
                throw;
            }
        }
    }
}