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


using System.Collections.Concurrent;
using System.Net.Sockets;
using GameFrameX.NetWork.RemoteMessaging.Transport;
using GameFrameX.ProtoBuf.Net;

namespace GameFrameX.NetWork.RemoteMessaging.Routing;

/// <summary>
/// TCP 信封转发器（C143d D3 case 2/3 默认发送通道）。
/// </summary>
/// <remarks>
/// The default TCP send channel for D3 case 2/3 (C143d).
/// One <see cref="IConnectionProvider"/> per target endpoint (each provider owns a
/// single pooled connection), so different instances stay on independent
/// connections. Frames reuse the standard codec layout, making the bytes
/// compatible with the existing protocol stack; a write failure invalidates that
/// endpoint's provider so the next forward reconnects.
/// </remarks>
public sealed class TcpEnvelopeForwarder : IEnvelopeForwarder, IDisposable
{
    /// <summary>
    /// 消息编解码器（标准帧格式）。
    /// </summary>
    /// <remarks>
    /// The message codec (the standard frame layout).
    /// </remarks>
    private readonly IMessageCodec _messageCodec;

    /// <summary>
    /// 目标端点 → 连接提供器（每端点单连接池）。
    /// </summary>
    /// <remarks>
    /// The per-endpoint connection providers (one pooled connection each).
    /// </remarks>
    private readonly ConcurrentDictionary<string, IConnectionProvider> _connectionProviders = new ConcurrentDictionary<string, IConnectionProvider>(StringComparer.Ordinal);

    /// <summary>
    /// 初始化 TCP 信封转发器（标准编解码器）。
    /// </summary>
    /// <remarks>
    /// Initializes the forwarder with the default codec.
    /// </remarks>
    public TcpEnvelopeForwarder()
    {
        _messageCodec = new DefaultMessageCodec();
    }

    /// <summary>
    /// 初始化 TCP 信封转发器（指定编解码器；注入用）。
    /// </summary>
    /// <remarks>
    /// Initializes the forwarder with a specific codec (injection point).
    /// </remarks>
    /// <param name="messageCodec">消息编解码器 / The message codec</param>
    public TcpEnvelopeForwarder(IMessageCodec messageCodec)
    {
        ArgumentNullException.ThrowIfNull(messageCodec, nameof(messageCodec));

        _messageCodec = messageCodec;
    }

    /// <summary>
    /// 将路由信封编码为标准帧并写到目标端点。
    /// </summary>
    /// <remarks>
    /// Encodes the envelope into a standard frame and writes it to the target
    /// endpoint's pooled stream. Transport failures invalidate the connection
    /// (so the next attempt reconnects) and propagate to the caller.
    /// </remarks>
    /// <param name="endpoint">已解析的目标端点 / The parsed target endpoint</param>
    /// <param name="envelope">路由信封 / The routing envelope</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>异步任务 / Async task</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="endpoint"/> 或 <paramref name="envelope"/> 为 null 时抛出 / Thrown when endpoint or envelope is null</exception>
    public async Task ForwardAsync(Discovery.ParsedEndpoint endpoint, MessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint, nameof(endpoint));
        ArgumentNullException.ThrowIfNull(envelope, nameof(envelope));

        var endpointKey = $"{endpoint.Host}:{endpoint.Port}";
        var connectionProvider = _connectionProviders.GetOrAdd(endpointKey, delegate (string key) { return new TcpConnectionProvider(); });
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
            try
            {
                var stream = await connectionProvider.GetOrCreateStreamAsync(endpoint.Host, endpoint.Port, cancellationToken);
                await stream.WriteAsync(frame.Memory, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is IOException || exception is SocketException || exception is OperationCanceledException)
            {
                connectionProvider.Invalidate();
                throw;
            }
        }
    }

    /// <summary>
    /// 释放全部连接提供器。
    /// </summary>
    /// <remarks>
    /// Disposes every per-endpoint connection provider.
    /// </remarks>
    public void Dispose()
    {
        foreach (var pair in _connectionProviders)
        {
            pair.Value.Dispose();
        }

        _connectionProviders.Clear();
    }
}
