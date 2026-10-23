// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please refer to the LICENSE file at the root of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯公民合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of citizens and other legal rights as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:  https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using GameFrameX.Network.Abstractions;
using GameFrameX.Network.RemoteMessaging;
using GameFrameX.Network.RemoteMessaging.Composition;
using GameFrameX.Network.RemoteMessaging.Contracts;
using GameFrameX.Network.RemoteMessaging.Transport;
using GameFrameX.Proto.Proto;

namespace GameFrameX.Tests.RemoteMessaging;

/// <summary>
/// RemoteMessageClient 并发与连接池回归测试。
/// 覆盖：全局信号量移除后的并发请求、连接关闭快速失败与池重建、多 endpoint 独立连接复用。
/// </summary>
public sealed class RemoteMessageClientConcurrencyTests : IDisposable
{
    static RemoteMessageClientConcurrencyTests()
    {
        // 消息类型注册表需显式初始化（与 SuperSocketKcpE2ETests 同模式）
        MessageProtoHelper.Init(typeof(ReqAccountQuery).Assembly, typeof(GameFrameX.Network.RemoteMessaging.Routing.RoleRouteEnvelopeMessage).Assembly);
    }

    private readonly List<string> _environmentKeys = new();
    private readonly List<FakeRemoteServer> _servers = new();

    [Fact]
    public async Task ConcurrentCalls_ShouldNotBeGloballySerialized()
    {
        // 服务端屏障：收到全部 6 个请求后才统一响应。
        // 旧实现（全局信号量 + 内联读）下第一个请求独占收发，屏障永远凑不齐 → 全体超时失败。
        const int requestCount = 6;
        using (var server = StartServer($"RMTestBarrier", requestCount, respondAfterCount: requestCount))
        {
            var client = new RemoteMessagingBuilder().Build();
            var calls = Enumerable.Range(0, requestCount)
                .Select(_ => client.CallWithResultAsync<RespAccountQuery>(
                    "RMTestBarrier",
                    new ReqAccountQuery { AccountName = "tester" },
                    timeoutMs: 15000))
                .ToArray();

            var results = await Task.WhenAll(calls);

            Assert.All(results, result =>
            {
                Assert.True(result.IsSuccess, $"call failed: {result.StatusCode} {result.ErrorMessage}");
                Assert.NotNull(result.Response);
            });
            Assert.True(server.ConnectionCount >= 2, $"并发调用应扇出多条连接，实际 {server.ConnectionCount}");
        }
    }

    [Fact]
    public async Task ConnectionClosed_ShouldFailPendingCallFast_AndPoolShouldRebuild()
    {
        // 首个连接收到一个请求后立即断开；后续连接正常响应。
        using (var server = StartServer("RMTestClose", 2, respondAfterCount: 1, closeFirstConnection: true))
        {
            var client = new RemoteMessagingBuilder().Build();
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            var first = await client.CallWithResultAsync<RespAccountQuery>(
                "RMTestClose", new ReqAccountQuery { AccountName = "a" }, timeoutMs: 15000);
            stopwatch.Stop();

            // 连接死亡经接收泵快速失败等待方：ConnectionFailed 而非等满超时
            Assert.False(first.IsSuccess);
            Assert.Equal(RemoteStatusCode.ConnectionFailed, first.StatusCode);
            Assert.True(stopwatch.ElapsedMilliseconds < 10000, $"应快速失败，实际耗时 {stopwatch.ElapsedMilliseconds}ms");

            // 池仅移除死亡连接，下一次调用应重建连接并成功
            var second = await client.CallWithResultAsync<RespAccountQuery>(
                "RMTestClose", new ReqAccountQuery { AccountName = "b" }, timeoutMs: 15000);
            Assert.True(second.IsSuccess, $"重建连接后调用应成功: {second.StatusCode} {second.ErrorMessage}");
        }
    }

    [Fact]
    public async Task AlternatingServices_ShouldReuseConnectionPerEndpoint()
    {
        // 旧实现的单连接槽位在两个 endpoint 间交替时每次断连重连；按 endpoint 池化后各自长期复用。
        using (var serverA = StartServer("RMTestAltA", 16, respondAfterCount: 1))
        {
            using (var serverB = StartServer("RMTestAltB", 16, respondAfterCount: 1))
            {
                var client = new RemoteMessagingBuilder().Build();
                for (var i = 0; i < 8; i++)
                {
                    var serviceName = i % 2 == 0 ? "RMTestAltA" : "RMTestAltB";
                    var result = await client.CallWithResultAsync<RespAccountQuery>(
                        serviceName, new ReqAccountQuery { AccountName = $"n{i}" }, timeoutMs: 15000);
                    Assert.True(result.IsSuccess, $"第 {i} 次调用({serviceName})失败: {result.StatusCode} {result.ErrorMessage}");
                }

                Assert.Equal(1, serverA.ConnectionCount);
                Assert.Equal(1, serverB.ConnectionCount);
            }
        }
    }

    public void Dispose()
    {
        foreach (var server in _servers)
        {
            server.Dispose();
        }

        foreach (var key in _environmentKeys)
        {
            Environment.SetEnvironmentVariable(key, null);
        }
    }

    private FakeRemoteServer StartServer(string serviceName, int acceptLimit, int respondAfterCount, bool closeFirstConnection = false)
    {
        var server = new FakeRemoteServer(acceptLimit, respondAfterCount, closeFirstConnection);
        server.Start();
        _servers.Add(server);
        var key = $"services__{serviceName}__tcp__0";
        Environment.SetEnvironmentVariable(key, $"tcp://127.0.0.1:{server.Port}");
        _environmentKeys.Add(key);
        return server;
    }

    /// <summary>
    /// 进程内 TCP 假服务端：解析 14 字节帧头（4 总长 + 1 操作 + 1 算法 + 4 UniqueId + 4 消息ID），
    /// 支持屏障响应（凑齐 N 个请求后统一回复 RespAccountQuery）与首连接读后即断。
    /// </summary>
    private sealed class FakeRemoteServer : IDisposable
    {
        private readonly int _acceptLimit;
        private readonly int _respondAfterCount;
        private readonly bool _closeFirstConnection;
        private readonly DefaultMessageCodec _codec = new();
        private readonly List<(int UniqueId, NetworkStream Stream)> _received = new();
        private readonly SemaphoreSlim _receiveLock = new(1, 1);
        private TcpListener _listener;
        private int _connectionCount;
        private int _accepted;
        private volatile bool _closed;

        public FakeRemoteServer(int acceptLimit, int respondAfterCount, bool closeFirstConnection)
        {
            _acceptLimit = acceptLimit;
            _respondAfterCount = respondAfterCount;
            _closeFirstConnection = closeFirstConnection;
        }

        public int Port { get; private set; }

        public int ConnectionCount => Volatile.Read(ref _connectionCount);

        public void Start()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = AcceptLoopAsync();
        }

        private async Task AcceptLoopAsync()
        {
            while (!_closed && Volatile.Read(ref _accepted) < _acceptLimit)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync();
                }
                catch (Exception)
                {
                    return;
                }

                Interlocked.Increment(ref _accepted);
                Interlocked.Increment(ref _connectionCount);
                _ = HandleConnectionAsync(client, Volatile.Read(ref _connectionCount) == 1 && _closeFirstConnection);
            }
        }

        private async Task HandleConnectionAsync(TcpClient client, bool closeAfterFirstFrame)
        {
            using (client)
            {
                var stream = client.GetStream();
                try
                {
                    while (!_closed)
                    {
                        var (uniqueId, messageId) = await ReadFrameHeaderAsync(stream);
                        if (uniqueId == null)
                        {
                            return;
                        }

                        // 协议版本握手帧（C252）：立即按同版本节点回包，不计入业务屏障
                        if (messageId == GameFrameX.Network.RemoteMessaging.Versioning.ReqProtocolVersionNegotiation.ReservedMessageId)
                        {
                            await WriteHandshakeResponseAsync(stream, uniqueId.Value);
                            continue;
                        }
                        if (uniqueId == null)
                        {
                            return;
                        }

                        if (closeAfterFirstFrame)
                        {
                            // 读后即断：制造连接死亡
                            return;
                        }

                        List<(int UniqueId, NetworkStream Stream)> toRespond = null;
                        await _receiveLock.WaitAsync();
                        try
                        {
                            _received.Add((uniqueId.Value, stream));
                            if (_received.Count >= _respondAfterCount)
                            {
                                toRespond = new List<(int, NetworkStream)>(_received);
                                _received.Clear();
                            }
                        }
                        finally
                        {
                            _receiveLock.Release();
                        }

                        if (toRespond != null)
                        {
                            foreach (var entry in toRespond)
                            {
                                await WriteResponseAsync(entry.Stream, entry.UniqueId);
                            }
                        }
                    }
                }
                catch (Exception)
                {
                    // 连接异常关闭：结束该连接的处理
                }
            }
        }

        /// <summary>
        /// 读取一帧并返回其 UniqueId（帧头偏移 6）与消息 ID（帧头偏移 10）。连接关闭返回 null。
        /// </summary>
        private static async Task<(int? UniqueId, int MessageId)> ReadFrameHeaderAsync(NetworkStream stream)
        {
            var header = new byte[14];
            if (!await ReadExactlyAsync(stream, header))
            {
                return (null, 0);
            }

            var totalLength = BinaryPrimitives.ReadInt32BigEndian(header);
            var remaining = totalLength - header.Length;
            if (remaining > 0)
            {
                var payload = new byte[remaining];
                if (!await ReadExactlyAsync(stream, payload))
                {
                    return (null, 0);
                }
            }

            return (BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(6)), BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(10)));
        }

        private static async Task<bool> ReadExactlyAsync(NetworkStream stream, byte[] buffer)
        {
            var offset = 0;
            while (offset < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(offset));
                if (read == 0)
                {
                    return false;
                }

                offset += read;
            }

            return true;
        }

        /// <summary>
        /// 按同版本节点回协议版本握手包（接受，1.0）。
        /// </summary>
        private async Task WriteHandshakeResponseAsync(NetworkStream stream, int uniqueId)
        {
            var response = new GameFrameX.Network.RemoteMessaging.Versioning.RespProtocolVersionNegotiation
            {
                Major = GameFrameX.Network.RemoteMessaging.Versioning.RemoteMessagingProtocolVersion.Current.Major,
                Minor = GameFrameX.Network.RemoteMessaging.Versioning.RemoteMessagingProtocolVersion.Current.Minor,
                Accepted = true,
            };
            MessageProtoHelper.SetMessageId(response);
            response.SetUniqueId(uniqueId);
            using (var buffer = _codec.Encode(response))
            {
                await stream.WriteAsync(buffer.Memory);
                await stream.FlushAsync();
            }
        }

        private async Task WriteResponseAsync(NetworkStream stream, int uniqueId)
        {
            var response = new RespAccountQuery { AccountId = 7 };
            MessageProtoHelper.SetMessageId(response);
            response.SetUniqueId(uniqueId);
            using (var buffer = _codec.Encode(response))
            {
                await stream.WriteAsync(buffer.Memory);
                await stream.FlushAsync();
            }
        }

        public void Dispose()
        {
            _closed = true;
            _listener?.Stop();
            _receiveLock.Dispose();
        }
    }
}
