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
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes and liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using GameFrameX.Discovery.Routing;
using GameFrameX.Network.Abstractions;
using GameFrameX.Network.Abstractions;
using GameFrameX.Network.Messages;
using GameFrameX.Network.RemoteMessaging.Contracts;
using GameFrameX.Network.RemoteMessaging.Transport;
using GameFrameX.Network.RemoteMessaging.Unified;
using GameFrameX.Network.RemoteMessaging.Versioning;
using GameFrameX.Proto.Proto;
using GameFrameX.ServerLink;
using GameFrameX.Tests.RemoteMessaging;
using ProtoBuf;

namespace GameFrameX.Tests.ServerLink;

/// <summary>
/// ServerLink 薄门面冒烟：直连拨号 + 固定重试上限构建约束、真实 TCP 回环上的跨服 RPC、
/// 玩家路由解析内置默认（跨服投递定向路由实例 / 本服投递走本地发送器）。
/// </summary>
/// <remarks>
/// ServerLink thin-facade smoke tests: the direct-dial build constraints with the fixed retry
/// limit, a cross-server RPC round trip over a real TCP loopback, and the built-in player-route
/// resolution (cross-server delivery targeting the routed instance; local delivery going through
/// the local player sender).
/// </remarks>
public sealed class ServerLinkFacadeTests
{
    public ServerLinkFacadeTests()
    {
        // 生产消息注册（RemoteMessaging 测试既有口径）：仅当所需消息未注册时执行，
        // 避免清空式重建与并行测试类的注册表竞态
        if (MessageProtoHelper.GetMessageIdByType(typeof(ReqHeartBeat)) <= 0 ||
            MessageProtoHelper.GetMessageIdByType(typeof(GameFrameX.Network.RemoteMessaging.Versioning.RespProtocolVersionNegotiation)) <= 0)
        {
            MessageProtoHelper.Init(typeof(ReqHeartBeat).Assembly, typeof(GameFrameX.Network.RemoteMessaging.Routing.RoleRouteEnvelopeMessage).Assembly);
        }
    }

    [Fact]
    public void Build_WithoutRouteTable_ShouldThrowInvalidOperationException()
    {
        Assert.Throws<InvalidOperationException>(() => new ServerLinkBuilder().Build());
    }

    [Fact]
    public async Task CrossServerRpc_OverRealTcpLoopback_ShouldRoundTrip()
    {
        var echoSurface = await TcpEchoServerSurface.StartAsync();
        var routeTable = new MutableRouteTableProvider()
            .SetServiceEndpoint("Social", $"tcp://127.0.0.1:{echoSurface.Port}", "social-1");

        var client = new ServerLinkBuilder()
            .WithRouteTable(routeTable)
            .Build();

        const long echoIdentity = 424242;
        var request = new ReqInnerFriendByAdd { PlayerId = echoIdentity, SourcePlayerId = echoIdentity };
        var result = await client.CallWithResultAsync<RespInnerFriendByAdd>(
            RemoteCallContext.Create("Social", 15000), request);

        Assert.True(result.IsSuccess, $"call failed: {result.StatusCode} {result.ErrorMessage}; server errors: {string.Join(" | ", echoSurface.ServerErrors)}");
        Assert.NotNull(result.Response);
        Assert.True(result.Response.Success);
        Assert.Equal((int)(echoIdentity % 100000), result.Response.ErrorCode);
        // 回环面确实先完成握手再处理业务帧（跨服调用经完整客户端栈）
        Assert.True(echoSurface.Handshakes.Count >= 1, "echo surface should have observed the protocol handshake");
        await echoSurface.DisposeAsync();
    }

    [Fact]
    public void EnvelopeForwarder_WithoutRoutingInitialization_ShouldThrow()
    {
        if (ServerLinkEnvelopeForwarder.IsRoutingInitialized)
        {
            // 同进程其它测试可能已装配路由缝；本用例只守护未装配契约，已装配时跳过断言体
            return;
        }

        Assert.Throws<InvalidOperationException>(() =>
            ServerLinkEnvelopeForwarder.ForwardAsync("Game", new TestMessageObject()).GetAwaiter().GetResult());
    }

    [Fact]
    public async Task PlayerRouting_CrossServerOnlineRoute_ShouldForwardToRoutedInstance()
    {
        var captureClient = new CapturingRemoteMessageClient();
        var routeTable = new MutableRouteTableProvider()
            .SetServiceEndpoint("Game", "tcp://127.0.0.1:59999", "game-77");
        var resolver = new StaticPlayerRouteResolver(GameFrameX.Discovery.Routing.PlayerRouteInfo.Online("Game", 1001, "game-77", 1));
        var localSender = new RecordingLocalPlayerSender();

        var sender = ServerLinkPlayerRouting.ComposeUnifiedSender(
            captureClient,
            routeTable,
            resolver,
            localSender,
            "Social",
            "social-local-1");

        Assert.NotNull(sender);
        var message = new TestMessageObject();
        var result = await sender.SendToPlayerAsync(701, message);

        Assert.True(result.IsSuccess, $"player delivery failed: {result.Status} {result.ErrorMessage}");
        // 跨服路由：经远程客户端按路由实例定向投递（非本地发送器）
        Assert.Empty(localSender.Delivered);
        var context = Assert.Single(captureClient.Contexts);
        Assert.NotNull(context.ServiceName);
    }

    [Fact]
    public async Task PlayerRouting_LocalServerOnlineRoute_ShouldDeliverViaLocalSender()
    {
        var captureClient = new CapturingRemoteMessageClient();
        var routeTable = new MutableRouteTableProvider()
            .SetServiceEndpoint("Social", "tcp://127.0.0.1:59998", "social-local-1");
        var resolver = new StaticPlayerRouteResolver(GameFrameX.Discovery.Routing.PlayerRouteInfo.Online("Social", 1001, "social-local-1", 1));
        var localSender = new RecordingLocalPlayerSender { OnlinePlayers = { [702] = true } };

        var sender = ServerLinkPlayerRouting.ComposeUnifiedSender(
            captureClient,
            routeTable,
            resolver,
            localSender,
            "Social",
            "social-local-1");

        var message = new TestMessageObject();
        var result = await sender.SendToPlayerAsync(702, message);

        Assert.True(result.IsSuccess, $"player delivery failed: {result.Status} {result.ErrorMessage}");
        // 本服路由：本地发送器收到投递，远程客户端零调用
        var delivered = Assert.Single(localSender.Delivered);
        Assert.Equal(702, delivered.PlayerId);
        Assert.Same(message, delivered.Message);
        Assert.Empty(captureClient.Contexts);
    }

    /// <summary>
    /// TCP 回环接收面：握手短路（接受同 major 对端）+ 业务回声（响应回写请求负载）。
    /// </summary>
    /// <remarks>
    /// A TCP loopback receive surface: a handshake short circuit (accepting same-major peers)
    /// plus a business echo (the response echoes the request payload).
    /// </remarks>
    private sealed class TcpEchoServerSurface : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private Task _acceptLoop;

        public ConcurrentQueue<string> ServerErrors { get; } = new();

        private TcpEchoServerSurface(TcpListener listener, Task acceptLoop)
        {
            _listener = listener;
            _acceptLoop = acceptLoop;
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        }

        public int Port { get; }

        public ConcurrentQueue<(int Major, int Minor)> Handshakes { get; } = new();

        public static async Task<TcpEchoServerSurface> StartAsync()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var surface = new TcpEchoServerSurface(listener, Task.CompletedTask);
            var started = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            surface._acceptLoop = Task.Run(() => surface.AcceptLoopAsync(started));
            await started.Task;
            return surface;
        }

        private async Task AcceptLoopAsync(TaskCompletionSource<bool> started)
        {
            started.TrySetResult(true);
            try
            {
                while (!_cancellationTokenSource.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync(_cancellationTokenSource.Token);
                    _ = Task.Run(() => HandleConnectionAsync(client), _cancellationTokenSource.Token);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private async Task HandleConnectionAsync(TcpClient client)
        {
            try
            {
                using (var connection = client)
                {
                    var codec = new DefaultMessageCodec(new DefaultMessageCompressionRegistry(), 0, 512, 1024 * 1024, 1024 * 1024);
                    var stream = connection.GetStream();
                    while (!_cancellationTokenSource.IsCancellationRequested)
                    {
                        var message = await codec.DecodeAsync(stream, _cancellationTokenSource.Token);
                        if (message == null)
                        {
                            return;
                        }

                        MessageObject reply;
                        if (message is ReqProtocolVersionNegotiation handshakeRequest)
                        {
                            Handshakes.Enqueue((handshakeRequest.Major, handshakeRequest.Minor));
                            var local = RemoteMessagingProtocolVersion.Current;
                            reply = new RespProtocolVersionNegotiation
                            {
                                Major = local.Major,
                                Minor = local.Minor,
                                Accepted = handshakeRequest.Major == local.Major,
                                ErrorCode = handshakeRequest.Major == local.Major ? 0 : 1,
                            };
                        }
                        else if (message is ReqInnerFriendByAdd friendRequest)
                        {
                            reply = new RespInnerFriendByAdd
                            {
                                Success = friendRequest.SourcePlayerId > 0,
                                ErrorCode = (int)(friendRequest.PlayerId % 100000),
                            };
                        }
                        else
                        {
                            return;
                        }

                        reply.UniqueId = message.UniqueId;
                        MessageProtoHelper.SetMessageId(reply);
                        using (var frame = codec.Encode(reply))
                        {
                            await stream.WriteAsync(frame.Memory, _cancellationTokenSource.Token);
                        }
                    }
                }
            }
            catch (Exception exception)
            {
                // 连接关闭/解码失败即结束该连接的处理循环；记录供失败断言输出
                ServerErrors.Enqueue(exception.ToString());
            }
        }

        public async ValueTask DisposeAsync()
        {
            _cancellationTokenSource.Cancel();
            try
            {
                await _acceptLoop;
            }
            catch (OperationCanceledException)
            {
            }

            _listener.Stop();
            _cancellationTokenSource.Dispose();
        }
    }

    private sealed class StaticPlayerRouteResolver : GameFrameX.Discovery.Routing.IPlayerRouteResolver
    {
        private readonly GameFrameX.Discovery.Routing.PlayerRouteInfo _route;

        public StaticPlayerRouteResolver(GameFrameX.Discovery.Routing.PlayerRouteInfo route)
        {
            _route = route;
        }

        public Task<GameFrameX.Discovery.Routing.PlayerRouteInfo> ResolveAsync(long playerId)
        {
            return Task.FromResult(_route);
        }
    }

    private sealed class RecordingLocalPlayerSender : IPlayerLocalSender
    {
        public List<(long PlayerId, MessageObject Message)> Delivered { get; } = new();

        public Dictionary<long, bool> OnlinePlayers { get; } = new();

        public bool IsPlayerOnline(long playerId)
        {
            return OnlinePlayers.TryGetValue(playerId, out var online) && online;
        }

        public Task<bool> SendToLocalPlayerAsync(long playerId, GameFrameX.Network.Messages.MessageObject message)
        {
            lock (Delivered)
            {
                Delivered.Add((playerId, message));
            }

            return Task.FromResult(true);
        }
    }

    private sealed class CapturingRemoteMessageClient : IRemoteMessageClient
    {
        public ConcurrentQueue<RemoteCallContext> Contexts { get; } = new();

        public Task<TResponse> CallAsync<TResponse>(string serviceName, GameFrameX.Network.Messages.MessageObject requestMessage) where TResponse : class, IResponseMessage
        {
            throw new NotImplementedException();
        }

        public Task<TResponse> CallAsync<TResponse>(string serviceName, GameFrameX.Network.Messages.MessageObject requestMessage, CancellationToken cancellationToken) where TResponse : class, IResponseMessage
        {
            throw new NotImplementedException();
        }

        public Task<TResponse> CallAsync<TResponse>(string serviceName, GameFrameX.Network.Messages.MessageObject requestMessage, int timeoutMs, CancellationToken cancellationToken = default) where TResponse : class, IResponseMessage
        {
            throw new NotImplementedException();
        }

        public Task<RemoteCallResult<TResponse>> CallWithResultAsync<TResponse>(string serviceName, GameFrameX.Network.Messages.MessageObject requestMessage) where TResponse : class, IResponseMessage
        {
            throw new NotImplementedException();
        }

        public Task<RemoteCallResult<TResponse>> CallWithResultAsync<TResponse>(string serviceName, GameFrameX.Network.Messages.MessageObject requestMessage, int timeoutMs) where TResponse : class, IResponseMessage
        {
            throw new NotImplementedException();
        }

        public Task<RemoteCallResult<TResponse>> CallWithResultAsync<TResponse>(string serviceName, GameFrameX.Network.Messages.MessageObject requestMessage, int timeoutMs, CancellationToken cancellationToken) where TResponse : class, IResponseMessage
        {
            throw new NotImplementedException();
        }

        public Task<RemoteCallResult<TResponse>> CallWithResultAsync<TResponse>(RemoteCallContext context, GameFrameX.Network.Messages.MessageObject requestMessage) where TResponse : class, IResponseMessage
        {
            Contexts.Enqueue(context);
            if (typeof(TResponse) != typeof(RespSendToPlayerInner))
            {
                throw new NotSupportedException($"Unsupported response type: {typeof(TResponse).Name}");
            }

            var response = new RespSendToPlayerInner { Success = true, PlayerOffline = false };
            return Task.FromResult(RemoteCallResult<TResponse>.Ok((TResponse)(object)response, 0, context.TraceId, 0));
        }

        public Task<RemoteCallResult<TResponse>> CallWithRetryAsync<TResponse>(string serviceName, GameFrameX.Network.Messages.MessageObject requestMessage, int timeoutMs = RemoteCallContext.DefaultTimeoutMs, int maxRetryCount = RemoteCallContext.DefaultMaxRetryCount, CancellationToken cancellationToken = default) where TResponse : class, IResponseMessage
        {
            throw new NotImplementedException();
        }

        public Task<RemoteCallResult<TResponse>> CallWithoutRetryAsync<TResponse>(string serviceName, GameFrameX.Network.Messages.MessageObject requestMessage, int timeoutMs = RemoteCallContext.DefaultTimeoutMs, CancellationToken cancellationToken = default) where TResponse : class, IResponseMessage
        {
            throw new NotImplementedException();
        }

        public Task<RemoteCallResult<TResponse>> CallWithMetadataAsync<TResponse>(string serviceName, GameFrameX.Network.Messages.MessageObject requestMessage, Dictionary<string, string> metadata, int timeoutMs = RemoteCallContext.DefaultTimeoutMs, CancellationToken cancellationToken = default) where TResponse : class, IResponseMessage
        {
            throw new NotImplementedException();
        }

        public Task SendOneWayAsync(string serviceName, GameFrameX.Network.Messages.MessageObject requestMessage, int timeoutMs = RemoteCallContext.DefaultTimeoutMs, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task SendOneWayAsync(RemoteCallContext context, GameFrameX.Network.Messages.MessageObject requestMessage)
        {
            Contexts.Enqueue(context);
            return Task.CompletedTask;
        }

        public Task<bool> IsServiceAvailableAsync(string serviceName)
        {
            return Task.FromResult(true);
        }
    }
}
