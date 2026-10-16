using System.Collections.Concurrent;
using System.Net;
using GameFrameX.Network;
using GameFrameX.Network.Abstractions;
using GameFrameX.Network.Message;
using GameFrameX.Network.Messages;
using GameFrameX.Network.RemoteMessaging;
using GameFrameX.Network.RemoteMessaging.Routing;
using GameFrameX.Network.RemoteMessaging.Contracts;
using GameFrameX.Network.RemoteMessaging.Versioning;
using GameFrameX.Proto.Proto;
using GameFrameX.ProtoBuf.Net;
using GameFrameX.SuperSocket.Connection;
using GameFrameX.SuperSocket.Server;
using GameFrameX.SuperSocket.Server.Abstractions;
using GameFrameX.SuperSocket.Server.Abstractions.Session;
using GameFrameX.SuperSocket.Server.Host;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ProtoBuf;

namespace GameFrameX.Tests.RemoteMessaging;

/// <summary>
/// C252 跨版本矩阵测试：同 major 互通 / 跨 major 拒绝 / 混布窗口（旧节点无握手响应）回落放行 /
/// 消息级标注消费。断言按数据身份（lesson 2026-10-10-test-port-assertion-identity-mismatch）：
/// 握手观测必须携带精确版本值，业务响应必须等于请求身份的确定映射；行为口径与部署文档逐条一致。
/// </summary>
/// <remarks>
/// Cross-version matrix tests for C252: same-major interop, cross-major rejection, the mixed-window
/// fallback (a legacy node never answers the handshake), and message-level attribute consumption.
/// Assertions follow data identity: observed handshakes must carry exact version values and business
/// responses must equal the deterministic mapping of the request identity; the behavior matrix
/// matches the deployment documentation clause by clause.
/// </remarks>
public sealed class ProtocolVersionNegotiationMatrixTests : IAsyncLifetime
{
    private const long IdentityBase = 88_000_000L;

    private readonly List<string> _environmentKeys = new();
    private readonly List<IHost> _hosts = new();

    private int _v1Port;
    private int _v2Port;
    private int _legacyPort;

    private readonly ConcurrentQueue<(int Major, int Minor)> _v1Handshakes = new();
    private readonly ConcurrentQueue<(int Major, int Minor)> _v2Handshakes = new();
    private readonly ConcurrentQueue<(int Major, int Minor)> _legacyHandshakes = new();
    private readonly ConcurrentDictionary<string, int> _businessMessages = new();
    private readonly ConcurrentDictionary<string, int> _disconnects = new();

    public async Task InitializeAsync()
    {
        MessageProtoHelper.Init(typeof(ReqHeartBeat).Assembly, typeof(RoleRouteEnvelopeMessage).Assembly);
        MessageHelper.SetMessageDecoderHandler(new DefaultMessageDecoderHandler(), new DefaultMessageDecompressHandler());
        MessageHelper.SetMessageEncoderHandler(new DefaultMessageEncoderHandler(), new DefaultMessageCompressHandler());

        _v1Port = GetAvailableTcpPort();
        _v2Port = GetAvailableTcpPort();
        _legacyPort = GetAvailableTcpPort();

        // 三条接收面：v1（同版本短路回包）、v2（major 2 拒绝）、legacy（不回握手——旧节点丢帧语义）
        _hosts.Add(await StartHostAsync(_v1Port, "v1", request => ProtocolVersionNegotiationServer.BuildReply(request, new ProtocolVersion(1, 0)), _v1Handshakes));
        _hosts.Add(await StartHostAsync(_v2Port, "v2", request => ProtocolVersionNegotiationServer.BuildReply(request, new ProtocolVersion(2, 4)), _v2Handshakes));
        _hosts.Add(await StartHostAsync(_legacyPort, "legacy", null, _legacyHandshakes));
    }

    public async Task DisposeAsync()
    {
        foreach (var key in _environmentKeys)
        {
            Environment.SetEnvironmentVariable(key, null);
        }

        foreach (var host in _hosts)
        {
            await host.StopAsync();
            host.Dispose();
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  ① 同 major 互通：握手完成版本交换，业务调用身份保持
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task SameMajor_HandshakeExchanged_CallSucceedsWithIdentity()
    {
        const string serviceName = "C252SameMajor";
        SetEnvironmentEndpoint(serviceName, $"tcp://127.0.0.1:{_v1Port}");

        var client = new RemoteMessagingBuilder()
            .WithTimeoutMs(15000)
            .Build();

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var request = new ReqInnerFriendByAdd { PlayerId = IdentityBase + attempt, SourcePlayerId = 42 };
            var response = await client.CallAsync<RespInnerFriendByAdd>(serviceName, request);

            // 数据身份断言：响应必须携带本次请求身份的确定映射
            Assert.NotNull(response);
            Assert.Equal((int)((IdentityBase + attempt) % 100000), response.ErrorCode);
            Assert.True(response.Success);
        }

        // 握手已被服务端观测到，且携带客户端精确版本 (1,0)——版本交换真实发生
        Assert.True(_v1Handshakes.Count >= 1, "server should have observed at least one handshake request");
        Assert.All(_v1Handshakes, handshake => Assert.Equal((RemoteMessagingProtocolVersion.Current.Major, RemoteMessagingProtocolVersion.Current.Minor), handshake));
    }

    // ─────────────────────────────────────────────────────────────
    //  ② 跨 major 拒绝：服务端 v2 显式拒绝，客户端状态码明确且无半开残留
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CrossMajor_ClientVersion1_ServerVersion2_ConnectionRejected()
    {
        const string serviceName = "C252CrossMajor";
        SetEnvironmentEndpoint(serviceName, $"tcp://127.0.0.1:{_v2Port}");

        var client = new RemoteMessagingBuilder()
            .WithTimeoutMs(15000)
            .Build();

        var request = new ReqInnerFriendByAdd { PlayerId = IdentityBase + 1, SourcePlayerId = 42 };
        var result = await client.CallWithResultAsync<RespInnerFriendByAdd>(serviceName, request);

        // 显式拒绝：状态码必须是协议版本不兼容（区别于超时/连接失败等泛化错误）
        Assert.Equal(RemoteStatusCode.ProtocolVersionIncompatible, result.StatusCode);
        Assert.Null(result.Response);

        // 拒绝可观测：服务端收到过握手（版本 1,0），且拒绝了业务消息
        Assert.True(_v2Handshakes.Count >= 1);
        Assert.All(_v2Handshakes, handshake => Assert.Equal((1, 0), handshake));
        Assert.False(_businessMessages.TryGetValue("v2", out var v2Count) && v2Count > 0, "business message must not reach the rejecting server");

        // 无半开残留：再次调用仍得到确定拒绝（新连接重新握手、重新拒绝，而非复用半开连接成功）
        var retryResult = await client.CallWithResultAsync<RespInnerFriendByAdd>(serviceName, new ReqInnerFriendByAdd { PlayerId = IdentityBase + 2, SourcePlayerId = 42 });
        Assert.Equal(RemoteStatusCode.ProtocolVersionIncompatible, retryResult.StatusCode);
    }

    // ─────────────────────────────────────────────────────────────
    //  ③ 混布窗口：旧节点不回握手 → 短超时后按旧版本放行，业务调用照常
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task MixedWindow_LegacyServer_HandshakeTimeout_FallsBackAndCallSucceeds()
    {
        const string serviceName = "C252MixedWindow";
        SetEnvironmentEndpoint(serviceName, $"tcp://127.0.0.1:{_legacyPort}");

        var client = new RemoteMessagingBuilder()
            .WithTimeoutMs(15000)
            .WithHandshakeTimeoutMs(300)
            .Build();

        var request = new ReqInnerFriendByAdd { PlayerId = IdentityBase + 1, SourcePlayerId = 42 };
        var response = await client.CallAsync<RespInnerFriendByAdd>(serviceName, request);

        // 混布口径：旧节点（未实现协商）不回握手帧，新拨号端按 Legacy 版本放行，数据面不受损
        Assert.NotNull(response);
        Assert.Equal((int)((IdentityBase + 1) % 100000), response.ErrorCode);
        Assert.True(response.Success);

        // 握手帧确实发出且被旧节点观测（旧节点选择不回）——证明尝试过协商而非跳过
        Assert.True(_legacyHandshakes.Count >= 1, "legacy server should have observed (and dropped) the handshake request");
        Assert.True(_businessMessages.TryGetValue("legacy", out var legacyCount) && legacyCount >= 1, "business message must reach the legacy server after fallback");
    }

    // ─────────────────────────────────────────────────────────────
    //  ④ 消息级标注消费：标注 major 2 的消息对 major 1 对端拒绝发送
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task MessageAnnotation_MajorBeyondRemote_IsRejectedBeforeSending()
    {
        const string serviceName = "C252Annotation";
        SetEnvironmentEndpoint(serviceName, $"tcp://127.0.0.1:{_v1Port}");

        var client = new RemoteMessagingBuilder()
            .WithTimeoutMs(15000)
            .Build();

        var request = new AnnotatedMajor2Message { PlayerId = IdentityBase + 1 };
        var result = await client.CallWithResultAsync<RespInnerFriendByAdd>(serviceName, request);

        // 协商远端为 (1,0)：标注 (2,0) 的消息必须在发送前被拒绝，且不进入服务端业务面
        Assert.Equal(RemoteStatusCode.ProtocolVersionIncompatible, result.StatusCode);
        Assert.Null(result.Response);
        Assert.False(_businessMessages.TryGetValue("v1", out var v1Count) && v1Count > 0, "annotated business message must not be sent to the major-1 peer");
    }

    /// <summary>
    /// 启动一条 TCP 接收面：握手短路行为可注入（null = 旧节点不回）；业务消息按身份回写并计数。
    /// </summary>
    /// <remarks>
    /// Starts one TCP receive surface: the handshake short-circuit behavior is injectable
    /// (null simulates a legacy node that never replies); business messages are echoed by
    /// identity and counted.
    /// </remarks>
    private async Task<IHost> StartHostAsync(int port, string serverKey, Func<ReqProtocolVersionNegotiation, RespProtocolVersionNegotiation>? handshakeReply, ConcurrentQueue<(int Major, int Minor)> observedHandshakes)
    {
        var hostBuilder = MultipleServerHostBuilder.Create();
        hostBuilder.AddServer<IMessage, MessageObjectPipelineFilter>(builder =>
        {
            builder
                .UseSessionHandler(OnConnected, (session, args) => OnDisconnected(serverKey, args))
                .UsePackageHandler((session, message) => PackageHandler(session, message, serverKey, handshakeReply, observedHandshakes))
                .ConfigureServices((context, serviceCollection) =>
                {
                    serviceCollection.Configure<ServerOptions>(options =>
                    {
                        options.AddListener(new ListenOptions
                        {
                            Ip = IPAddress.Loopback.ToString(),
                            Port = port,
                        });
                    });
                });
        });
        var host = hostBuilder.Build();
        await host.StartAsync();
        return host;
    }

    private static ValueTask OnConnected(IAppSession session)
    {
        return ValueTask.CompletedTask;
    }

    private ValueTask OnDisconnected(string serverKey, CloseEventArgs args)
    {
        _disconnects.AddOrUpdate(serverKey, 1, (_, count) => count + 1);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// 漏斗形状与生产 StartUp 一致：握手短路（位于业务分发之前）→ 业务回声。
    /// </summary>
    /// <remarks>
    /// The funnel shape mirrors the production StartUp funnel: a handshake short circuit
    /// (before business dispatch) followed by a business echo.
    /// </remarks>
    private async ValueTask PackageHandler(
        IAppSession session,
        IMessage message,
        string serverKey,
        Func<ReqProtocolVersionNegotiation, RespProtocolVersionNegotiation>? handshakeReply,
        ConcurrentQueue<(int Major, int Minor)> observedHandshakes)
    {
        if (message is not NetworkMessagePackage package || package.DeserializeMessageObject() is not MessageObject request)
        {
            return;
        }

        if (request is ReqProtocolVersionNegotiation handshakeRequest)
        {
            // 记录对端版本（数据身份：精确 major/minor），再按注入行为回包（null = 旧节点丢帧不回）
            observedHandshakes.Enqueue((handshakeRequest.Major, handshakeRequest.Minor));
            if (handshakeReply != null)
            {
                var reply = handshakeReply(handshakeRequest);
                reply.UniqueId = handshakeRequest.UniqueId;
                var frame = MessageHelper.EncoderHandler.Handler(reply);
                await session.SendAsync(frame);
                if (!reply.Accepted)
                {
                    // 拒绝路径与生产漏斗一致：拒绝包先行送达，宽限期后关闭会话（无半开残留）
                    _ = CloseAfterGraceAsync(session);
                }
            }

            return;
        }

        if (request is ReqInnerFriendByAdd friendRequest)
        {
            _businessMessages.AddOrUpdate(serverKey, 1, (_, count) => count + 1);
            var response = new RespInnerFriendByAdd
            {
                Success = friendRequest.SourcePlayerId > 0,
                ErrorCode = (int)(friendRequest.PlayerId % 100000),
                UniqueId = friendRequest.UniqueId,
            };
            var responseFrame = MessageHelper.EncoderHandler.Handler(response);
            await session.SendAsync(responseFrame);
        }
    }

    /// <summary>
    /// 标注 (2,0) 的测试业务消息：对 major 1 对端必须拒绝发送。
    /// </summary>
    /// <remarks>
    /// A test business message annotated (2,0): sending it to a major-1 peer must be rejected.
    /// </remarks>
    [ProtoContract]
    [ProtocolVersion(2, 0)]
    private sealed class AnnotatedMajor2Message : GameFrameX.Network.Messages.MessageObject, GameFrameX.Network.Abstractions.IRequestMessage
    {
        [ProtoMember(1)]
        public long PlayerId { get; set; }

        public override void Clear()
        {
            PlayerId = 0;
        }
    }

    /// <summary>
    /// 与生产 <c>ProtocolVersionNegotiationServer</c> 同宽限语义：拒绝包先送达，再关闭会话。
    /// </summary>
    private static async Task CloseAfterGraceAsync(IAppSession session)
    {
        try
        {
            await Task.Delay(250);
            await session.CloseAsync(CloseReason.ProtocolError);
        }
        catch (Exception)
        {
            // 对端可能已先行关闭；拒绝结论已回包
        }
    }

    private void SetEnvironmentEndpoint(string serviceName, string value)
    {
        var key = $"services__{serviceName}__tcp__0";
        Environment.SetEnvironmentVariable(key, value);
        _environmentKeys.Add(key);
    }

    private static int GetAvailableTcpPort()
    {
        using var tcpClient = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        tcpClient.Start();
        var port = ((IPEndPoint)tcpClient.LocalEndpoint).Port;
        tcpClient.Stop();
        return port;
    }
}
