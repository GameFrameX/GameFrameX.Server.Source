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
//   please refer to the LICENSE file at the root of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
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

using System.Collections.Concurrent;
using System.Net;
using GameFrameX.Discovery;
using GameFrameX.Network;
using GameFrameX.Network.Message;
using GameFrameX.Network.Abstractions;
using GameFrameX.Network.Messages;
using GameFrameX.Network.RemoteMessaging;
using GameFrameX.Network.RemoteMessaging.Composition;
using GameFrameX.Network.RemoteMessaging.Discovery;
using GameFrameX.Network.RemoteMessaging.Routing;
using GameFrameX.Network.RemoteMessaging.Transport;
using GameFrameX.Proto.Proto;
using GameFrameX.SuperSocket.Connection;
using GameFrameX.SuperSocket.Kcp;
using GameFrameX.SuperSocket.Server;
using GameFrameX.SuperSocket.Server.Abstractions;
using GameFrameX.SuperSocket.Server.Abstractions.Session;
using GameFrameX.SuperSocket.Server.Host;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace GameFrameX.Tests.RemoteMessaging;

/// <summary>
/// C251 服务器间 KCP 传输真实现的端到端验证：`kcp://` 直配端点两节点真实往返（UDP loopback）、
/// tcp/kcp 传输矩阵与混跑迁移等价、断连失效（<c>Invalidate</c> 族）、<c>IsServiceAvailable</c> 真实口径、
/// 单帧上限内多段帧往返 + 超限帧快速失败（内核多段吞吐病态边界守卫）。
/// </summary>
/// <remarks>
/// End-to-end verification for the C251 inter-server KCP transport: real two-node round trips over
/// `kcp://` env-configured endpoints (UDP loopback), the tcp/kcp transport matrix with mixed migration
/// equivalence, invalidation semantics (<c>Invalidate</c> family), the real <c>IsServiceAvailable</c>
/// contract, and the fast failure of over-bound frames (the kernel multi-segment throughput pathology guard).
/// <para>断言按数据身份（lesson 2026-10-10-test-port-assertion-identity-mismatch）：
/// 每次调用的响应字段必须等于该次请求身份字段的确定映射，不做「成功收到」级弱断言。</para>
/// <para>服务端装配与 <c>SuperSocketKcpE2ETests</c> 同构（MultipleServerHostBuilder +
/// AddServer&lt;IMessage, MessageObjectPipelineFilter&gt; + UseKcp），包处理器按请求身份回写响应
/// （<c>RespInnerFriendByAdd</c>），与生产服务器间接收面（StartUp 共用消息漏斗）同一管线。</para>
/// </remarks>
public sealed class InterServerKcpTransportTests : IAsyncLifetime
{
    /// <summary>矩阵/大帧用例的逐调用身份基数（各测试用独立服务名避免串扰）。</summary>
    private const long IdentityBase = 77_000_000L;

    private IHost _kcpHost;
    private IHost _tcpHost;
    private int _kcpPort;
    private int _tcpPort;
    private readonly List<string> _environmentKeys = new();
    private readonly ConcurrentQueue<uint> _observedConvs = new();

    public async Task InitializeAsync()
    {
        MessageProtoHelper.Init(typeof(ReqHeartBeat).Assembly, typeof(RoleRouteEnvelopeMessage).Assembly);
        MessageHelper.SetMessageDecoderHandler(new DefaultMessageDecoderHandler(), new DefaultMessageDecompressHandler());
        MessageHelper.SetMessageEncoderHandler(new DefaultMessageEncoderHandler(), new DefaultMessageCompressHandler());
        _kcpPort = GetAvailableUdpPort();
        _tcpPort = GetAvailableTcpPort();

        // KCP 与 TCP 接收面各自独立成宿主：SuperSocket 连接监听器工厂按「最后注册者生效」解析
        // （SuperSocketHostListeningTests 的同型告诫），同一服务集合内混注册会互相顶掉对方的监听器

        // 服务器间 KCP 接收面：与 StartUp ConfigureInnerKcpServer 同构（无会话鉴权中间件，消息模式 + 窗口 128）
        var kcpHostBuilder = MultipleServerHostBuilder.Create();
        kcpHostBuilder.AddServer<IMessage, MessageObjectPipelineFilter>(builder =>
        {
            builder
                .UseKcp(o =>
                {
                    // 与生产 ConfigureInnerKcpServer / 客户端会话同参数（消息模式 + 显式窗口 128）
                    o.NoDelay = true;
                    o.Interval = 10;
                    o.Mtu = 1400;
                    o.SendWindow = 128;
                    o.ReceiveWindow = 128;
                })
                .UseSessionHandler(OnConnected, OnDisconnected)
                .UsePackageHandler(PackageHandler)
                .ConfigureServices((context, serviceCollection) =>
                {
                    serviceCollection.Configure<ServerOptions>(options =>
                    {
                        options.AddListener(new ListenOptions
                        {
                            Ip = IPAddress.Loopback.ToString(),
                            Port = _kcpPort,
                        });
                    });
                });
        });
        _kcpHost = kcpHostBuilder.Build();
        await _kcpHost.StartAsync();

        // 服务器间 TCP 接收面（矩阵对照腿）：同一管线过滤器与包处理器，仅传输不同
        var tcpHostBuilder = MultipleServerHostBuilder.Create();
        tcpHostBuilder.AddServer<IMessage, MessageObjectPipelineFilter>(builder =>
        {
            builder
                .UseSessionHandler(OnConnected, OnDisconnected)
                .UsePackageHandler(PackageHandler)
                .ConfigureServices((context, serviceCollection) =>
                {
                    serviceCollection.Configure<ServerOptions>(options =>
                    {
                        options.AddListener(new ListenOptions
                        {
                            Ip = IPAddress.Loopback.ToString(),
                            Port = _tcpPort,
                        });
                    });
                });
        });
        _tcpHost = tcpHostBuilder.Build();
        await _tcpHost.StartAsync();
    }

    public async Task DisposeAsync()
    {
        foreach (var key in _environmentKeys)
        {
            Environment.SetEnvironmentVariable(key, null);
        }

        if (_kcpHost != null)
        {
            await _kcpHost.StopAsync();
            _kcpHost.Dispose();
        }

        if (_tcpHost != null)
        {
            await _tcpHost.StopAsync();
            _tcpHost.Dispose();
        }
    }


    private ValueTask OnConnected(IAppSession session)
    {
        if (session.Connection is KcpPipeConnection kcpConnection)
        {
            _observedConvs.Enqueue(kcpConnection.Conv);
        }

        return ValueTask.CompletedTask;
    }

    private static ValueTask OnDisconnected(IAppSession session, CloseEventArgs closeEventArgs)
    {
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// 回声包处理器：按请求身份回写响应——<c>ReqInnerFriendByAdd</c> 以
    /// <c>ErrorCode = PlayerId 模 100000</c> 与 <c>Success = SourcePlayerId &gt; 0</c> 回写；
    /// <c>RoleRouteEnvelopeMessage</c>（大载荷形态）以 <c>ErrorCode = InnerMessageBytes.Length</c> 回写。
    /// 响应 <c>UniqueId</c> 必须回填请求 <c>UniqueId</c>（请求-响应匹配器契约）。
    /// </summary>
    private static async ValueTask PackageHandler(IAppSession session, IMessage message)
    {
        if (message is not NetworkMessagePackage package || package.DeserializeMessageObject() is not MessageObject request)
        {
            return;
        }

        MessageObject response = null;
        switch (request)
        {
            case ReqInnerFriendByAdd friendRequest:
                response = new RespInnerFriendByAdd
                {
                    Success = friendRequest.SourcePlayerId > 0,
                    ErrorCode = (int)(friendRequest.PlayerId % 100000),
                };
                break;
            case RoleRouteEnvelopeMessage envelope:
                response = new RespInnerFriendByAdd
                {
                    Success = envelope.InnerMessageBytes != null,
                    ErrorCode = envelope.InnerMessageBytes?.Length ?? -1,
                };
                break;
        }

        if (response != null)
        {
            // 回写路径与生产漏斗一致：MessageHelper.EncoderHandler 负责 SetMessageId + 14 字节帧编码
            response.UniqueId = request.UniqueId;
            var frame = MessageHelper.EncoderHandler.Handler(response);
            // 异步回写：不在服务端收包管线内同步阻塞（对齐 SuperSocketKcpE2ETests 的拆除竞态注释）
            await session.SendAsync(frame);
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  ① kcp:// 直配端点两节点真实往返（按数据身份断言）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task KcpEndpoint_TwoNodeRoundTrip_ResponsesMatchRequestIdentity()
    {
        const string serviceName = "C251KcpRoundTrip";
        SetEnvironmentEndpoint(serviceName, "kcp", $"kcp://127.0.0.1:{_kcpPort}");

        var client = new RemoteMessagingBuilder()
            .UseKcpTransportAdapter()
            .WithTimeoutMs(15000)
            .Build();

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var request = new ReqInnerFriendByAdd { PlayerId = IdentityBase + attempt, SourcePlayerId = 42 };
            var response = await client.CallAsync<RespInnerFriendByAdd>(serviceName, request);

            Assert.NotNull(response);
            // 数据身份断言：响应必须携带本次请求身份（PlayerId 模 100000 的确定映射）
            Assert.Equal((int)((IdentityBase + attempt) % 100000), response.ErrorCode);
            Assert.True(response.Success);
        }

        // 反证：KCP 会话已按 EndPoint:Conv 建立且 conv 非 0（复用 C190 E2E 断言口径）
        Assert.True(_observedConvs.TryPeek(out var observed) && observed != 0,
            "KCP session should have been established with a non-zero conv.");
    }

    // ─────────────────────────────────────────────────────────────
    //  ② 拓扑等价矩阵：tcp / kcp / 混跑迁移（tcp 键 → kcp 键重指向）行为等价
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task TransportMatrix_TcpKcpAndMigration_ProduceEquivalentResponses()
    {
        const string serviceName = "C251Matrix";
        var identities = new[] { 101L, 202L, 303L };
        var observed = new List<(long Identity, bool Success, int ErrorCode)>();

        // 腿一：仅配 tcp 键（现状部署形态）
        SetEnvironmentEndpoint(serviceName, "tcp", $"tcp://127.0.0.1:{_tcpPort}");
        var tcpClient = new RemoteMessagingBuilder().WithTimeoutMs(15000).Build();
        foreach (var identity in identities)
        {
            var request = new ReqInnerFriendByAdd { PlayerId = IdentityBase + identity, SourcePlayerId = identity };
            var response = await tcpClient.CallAsync<RespInnerFriendByAdd>(serviceName, request);
            Assert.NotNull(response);
            observed.Add((identity, response.Success, response.ErrorCode));
        }

        // 腿二：混跑迁移——同服务重指向 kcp 键（旧 tcp 键撤除），新客户端实例按 scheme 重新分发
        SetEnvironmentEndpoint(serviceName, "kcp", $"kcp://127.0.0.1:{_kcpPort}");
        var kcpClient = new RemoteMessagingBuilder()
            .UseKcpTransportAdapter()
            .WithTimeoutMs(15000)
            .Build();
        foreach (var (identity, tcpSuccess, tcpErrorCode) in observed)
        {
            var request = new ReqInnerFriendByAdd { PlayerId = IdentityBase + identity, SourcePlayerId = identity };
            var response = await kcpClient.CallAsync<RespInnerFriendByAdd>(serviceName, request);

            // 等价断言：kcp 腿的响应与 tcp 腿逐字段一致（同一请求身份 → 同一响应身份）
            Assert.NotNull(response);
            Assert.Equal(tcpSuccess, response.Success);
            Assert.Equal(tcpErrorCode, response.ErrorCode);
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  ③ 断连失效与重连（Invalidate 族）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Invalidate_KcpConnection_PoolRecreatesSessionOnNextCall()
    {
        const string serviceName = "C251Invalidate";
        SetEnvironmentEndpoint(serviceName, "kcp", $"kcp://127.0.0.1:{_kcpPort}");

        var adapter = new KcpTransportProtocolAdapter(
            new EnvironmentVariableEndpointResolver(),
            new KcpConnectionProvider());
        try
        {
            // 存活会话按池语义复用（同一 Stream 实例，与 TCP 池行为对齐）
            var first = await adapter.GetOrCreateStreamAsync(serviceName);
            var pooled = await adapter.GetOrCreateStreamAsync(serviceName);
            Assert.Same(first, pooled);

            // 流级失效：仅丢弃该会话，下次调用重建（新 Stream 实例）
            adapter.Invalidate(first);
            var recreated = await adapter.GetOrCreateStreamAsync(serviceName);
            Assert.NotSame(first, recreated);

            // 全量失效：丢弃全部会话
            adapter.Invalidate();
            var afterAll = await adapter.GetOrCreateStreamAsync(serviceName);
            Assert.NotSame(recreated, afterAll);
        }
        finally
        {
            adapter.Dispose();
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  ④ IsServiceAvailable 真实口径（可达性 = 端点可解析，与 TCP 适配器同构）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void IsServiceAvailable_ReflectsKcpEndpointResolvability()
    {
        const string serviceName = "C251Availability";
        var adapter = new KcpTransportProtocolAdapter(
            new EnvironmentVariableEndpointResolver(),
            new KcpConnectionProvider());
        try
        {
            Assert.False(adapter.IsServiceAvailable(serviceName));

            SetEnvironmentEndpoint(serviceName, "kcp", $"kcp://127.0.0.1:{_kcpPort}");
            Assert.True(adapter.IsServiceAvailable(serviceName));

            // 结构非法端点值（缺 scheme）沿 C250 契约在 resolver 解析期抛 EndpointFormatException
            //（与 TCP 适配器同口径：env 值连接期过 EndpointParser），不静默当作不可用
            SetEnvironmentEndpoint(serviceName, "kcp", "127.0.0.1:9999");
            Assert.Throws<EndpointFormatException>(() => adapter.IsServiceAvailable(serviceName));
        }
        finally
        {
            adapter.Dispose();
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  ⑤ 单帧上限内多段帧往返 + 超限帧快速失败
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task FrameBound_MultiSegmentFrameRoundTrips_AndOversizedFrameFailsFast()
    {
        const string serviceName = "C251FrameBound";
        SetEnvironmentEndpoint(serviceName, "kcp", $"kcp://127.0.0.1:{_kcpPort}");

        // 8KB 内嵌载荷 ≈ 6 个 KCP 段（Mtu 1400，单帧上限 8 段 = 11008B）：证明多段帧经内核
        // 重组为整帧并按长度前缀解码无残缺（超限守卫之外的多段路径真实走通）
        var innerPayload = new byte[8 * 1024];
        new Random(20261016).NextBytes(innerPayload);
        var envelope = new RoleRouteEnvelopeMessage
        {
            TargetRole = "Game",
            InnerMessageId = 65535,
            InnerMessageBytes = innerPayload,
        };

        var client = new RemoteMessagingBuilder()
            .UseKcpTransportAdapter()
            .WithCompression(0)
            .Build();

        var response = await client.CallAsync<RespInnerFriendByAdd>(serviceName, envelope);

        Assert.NotNull(response);
        Assert.True(response.Success);
        // 数据身份断言：回写长度 = 发送载荷长度（证明 8KB 全量到达且按长度前缀重组无残缺）
        Assert.Equal(innerPayload.Length, response.ErrorCode);

        // 超限帧（60KB > 单帧上限 11008B）必须快速失败（IOException → 连接级失败），
        // 而非进入内核多段吞吐病态（实测 ≥20 段分钟级爬行）；禁重试以钉住「单次尝试即失败」
        var oversized = new RoleRouteEnvelopeMessage
        {
            TargetRole = "Game",
            InnerMessageId = 65535,
            InnerMessageBytes = new byte[60 * 1024],
        };
        var strictClient = new RemoteMessagingBuilder()
            .UseKcpTransportAdapter()
            .WithCompression(0)
            .WithRetry(0)
            .Build();

        var oversizedResponse = await strictClient.CallAsync<RespInnerFriendByAdd>(serviceName, oversized);

        Assert.Null(oversizedResponse);
    }

    // ─────────────────────────────────────────────────────────────
    //  ⑥ 基准数据（决策硬要求）：同载荷串行往返 吞吐/CPU tcp vs kcp
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 采集 tcp 与 kcp 传输的串行往返吞吐与 CPU 基线（数值留档 test.md；
    /// KCP 单帧上限内小帧 = 服务器间 KCP 的目标载荷画像）。
    /// </summary>
    [Fact]
    public async Task Benchmark_SerialRoundTrip_ThroughputAndCpu_TcpVsKcp()
    {
        const int rounds = 200;
        const string kcpService = "C251BenchKcp";
        const string tcpService = "C251BenchTcp";
        SetEnvironmentEndpoint(kcpService, "kcp", $"kcp://127.0.0.1:{_kcpPort}");
        SetEnvironmentEndpoint(tcpService, "tcp", $"tcp://127.0.0.1:{_tcpPort}");

        var kcpResult = await MeasureSerialRoundTripsAsync(kcpService, rounds);
        var tcpResult = await MeasureSerialRoundTripsAsync(tcpService, rounds);

        // 基线断言（弱）：两传输在回环上都应完成全部往返（吞吐/CPU 数值本身不做 pass/fail 判据，
        // 仅留档——调优依据由 verify 阶段 test.md 承载）
        Assert.Equal(rounds, kcpResult.Completed);
        Assert.Equal(rounds, tcpResult.Completed);
        Console.WriteLine($"[C251-bench] kcp: {kcpResult.Completed} rounds, {kcpResult.ElapsedMilliseconds}ms wall, {kcpResult.CpuMilliseconds}ms cpu");
        Console.WriteLine($"[C251-bench] tcp: {tcpResult.Completed} rounds, {tcpResult.ElapsedMilliseconds}ms wall, {tcpResult.CpuMilliseconds}ms cpu");
    }

    /// <summary>
    /// 串行往返测量：逐次请求-响应按数据身份校验完成数，同时采样壁钟与进程 CPU 时间。
    /// </summary>
    private static async Task<(int Completed, long ElapsedMilliseconds, long CpuMilliseconds)> MeasureSerialRoundTripsAsync(string serviceName, int rounds)
    {
        var client = new RemoteMessagingBuilder()
            .UseKcpTransportAdapter()
            .WithCompression(0)
            .Build();
        var completed = 0;
        var process = System.Diagnostics.Process.GetCurrentProcess();
        var cpuStart = process.TotalProcessorTime;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        for (var attempt = 1; attempt <= rounds; attempt++)
        {
            var request = new ReqInnerFriendByAdd { PlayerId = IdentityBase + attempt, SourcePlayerId = attempt };
            var response = await client.CallAsync<RespInnerFriendByAdd>(serviceName, request);
            if (response != null && response.ErrorCode == (int)((IdentityBase + attempt) % 100000))
            {
                completed++;
            }
        }

        stopwatch.Stop();
        var cpu = (process.TotalProcessorTime - cpuStart).TotalMilliseconds;
        return (completed, stopwatch.ElapsedMilliseconds, (long)cpu);
    }

    private void SetEnvironmentEndpoint(string serviceName, string scheme, string value)
    {
        var key = $"services__{serviceName}__{scheme}__0";
        Environment.SetEnvironmentVariable(key, value);
        _environmentKeys.Add(key);
    }

    private static int GetAvailableUdpPort()
    {
        using (var udpClient = new System.Net.Sockets.UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
        {
            return ((IPEndPoint)udpClient.Client.LocalEndPoint).Port;
        }
    }

    private static int GetAvailableTcpPort()
    {
        using (var tcpClient = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0))
        {
            tcpClient.Start();
            var port = ((IPEndPoint)tcpClient.LocalEndpoint).Port;
            tcpClient.Stop();
            return port;
        }
    }
}

