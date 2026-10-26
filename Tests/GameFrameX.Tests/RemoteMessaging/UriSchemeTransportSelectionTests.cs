// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or to infringe upon the legal rights, interests of others as prohibited by laws and regulations.
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.Collections.Concurrent;
using GameFrameX.Discovery;
using GameFrameX.Discovery.Routing;
using GameFrameX.Network.RemoteMessaging;
using GameFrameX.Network.RemoteMessaging.Composition;
using GameFrameX.Network.RemoteMessaging.Discovery;
using GameFrameX.Network.RemoteMessaging.Transport;

namespace GameFrameX.Tests.RemoteMessaging;

/// <summary>
/// URI scheme 驱动传输选择的验证：路由表端点解析、注册表拨号期分发、回落告警一次、
/// TCP 解析面收敛后的行为差异钉住、builder 注册语义（C304：端点事实源迁至心跳注册表路由表，
/// 环境变量解析器已删除，相关 env 键语义不再钉住）。
/// </summary>
/// <remarks>
/// Verification for the URI-scheme-driven transport selection: route-table endpoint resolution,
/// registry dispatch at dial time, one-time fallback warning, the pinned behavior differences
/// after the TCP parsing-surface convergence, and the builder registration semantics (C304: the
/// endpoint source of truth moved to the heartbeat-registry route table; the environment-variable
/// resolver is gone and its env-key semantics are no longer pinned).
/// </remarks>
public sealed class UriSchemeTransportSelectionTests
{
    // ─────────────────────────────────────────────────────────────
    //  ① 动态发现解析器：advertiseEndpoint 原样透传 + scheme 过滤
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void DynamicResolver_AdvertiseEndpoint_PassesThroughAndFiltersByScheme()
    {
        var table = RoleRouteTable.FromInstances(new List<InstanceDescriptor>
        {
            RouteTableTestHelper.Descriptor("Game", "game-1", "kcp://10.0.1.1:7101"),
        });
        var resolver = new DiscoveryServiceEndpointResolver(new FixedRouteTableProvider(table));

        Assert.Equal("kcp://10.0.1.1:7101", resolver.ResolveEndpoint(new RemoteServiceTarget("Game")));
        Assert.Equal("kcp://10.0.1.1:7101", resolver.ResolveEndpoint(new RemoteServiceTarget("Game"), "kcp"));
        Assert.Equal(string.Empty, resolver.ResolveEndpoint(new RemoteServiceTarget("Game"), "tcp"));
        Assert.Equal(string.Empty, resolver.ResolveEndpoint(new RemoteServiceTarget("Nobody"), "kcp"));
    }

    // ─────────────────────────────────────────────────────────────
    //  ②/③ 注册表拨号期分发与回落
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Dispatch_KcpEndpointWithKcpAdapter_ShouldHitKcpAdapter()
    {
        var resolver = new FakeEndpointResolver().With("kcp", "kcp://10.0.0.2:7002");
        var kcpAdapter = new RecordingTransportAdapter("KCP-FAKE");
        var tcpAdapter = new RecordingTransportAdapter("TCP-FAKE");
        var registry = new TransportProtocolAdapterRegistry();
        registry.Register(TransportSchemes.Tcp, tcpAdapter);
        registry.Register(TransportSchemes.Kcp, kcpAdapter);
        var dispatcher = new SchemeDispatchTransportAdapter(resolver, registry);

        var stream = await dispatcher.GetOrCreateStreamAsync(new RemoteServiceTarget("C250Svc"));

        Assert.Same(kcpAdapter.ReturnedStream, stream);
        Assert.Equal("C250Svc", Assert.Single(kcpAdapter.RequestedTargets).ServerType);
        Assert.Empty(tcpAdapter.RequestedTargets);
        Assert.Equal(0, dispatcher.SchemeFallbackWarningCount);
    }

    [Fact]
    public async Task Dispatch_KcpEndpointWithoutKcpAdapter_ShouldFallBackToTcpOnceWarned()
    {
        var resolver = new FakeEndpointResolver()
            .With("kcp", "kcp://10.0.0.2:7002")
            .With("tcp", "tcp://10.0.0.1:7001");
        var tcpAdapter = new RecordingTransportAdapter("TCP-FAKE");
        var registry = new TransportProtocolAdapterRegistry();
        registry.Register(TransportSchemes.Tcp, tcpAdapter);
        var dispatcher = new SchemeDispatchTransportAdapter(resolver, registry);

        var firstStream = await dispatcher.GetOrCreateStreamAsync(new RemoteServiceTarget("C250Svc"));
        var secondStream = await dispatcher.GetOrCreateStreamAsync(new RemoteServiceTarget("C250Svc"));

        Assert.Same(tcpAdapter.ReturnedStream, firstStream);
        Assert.Same(tcpAdapter.ReturnedStream, secondStream);
        Assert.Equal(2, tcpAdapter.RequestedTargets.Count);
        Assert.Equal(1, dispatcher.SchemeFallbackWarningCount);
    }

    [Fact]
    public async Task Dispatch_FallbackPerServiceAndScheme_WarnsOncePerPair()
    {
        var resolver = new FakeEndpointResolver()
            .With("kcp", "kcp://10.0.0.2:7002")
            .With("ws", "ws://10.0.0.3:7003")
            .With("tcp", "tcp://10.0.0.1:7001");
        var registry = new TransportProtocolAdapterRegistry();
        registry.Register(TransportSchemes.Tcp, new RecordingTransportAdapter("TCP-FAKE"));
        var dispatcher = new SchemeDispatchTransportAdapter(resolver, registry);

        await dispatcher.GetOrCreateStreamAsync(new RemoteServiceTarget("C250SvcA"));
        await dispatcher.GetOrCreateStreamAsync(new RemoteServiceTarget("C250SvcA"));
        await dispatcher.GetOrCreateStreamAsync(new RemoteServiceTarget("C250SvcB"));
        await dispatcher.GetOrCreateStreamAsync(new RemoteServiceTarget("C250WsSvc"));

        Assert.Equal(3, dispatcher.SchemeFallbackWarningCount);
    }

    [Fact]
    public async Task Dispatch_KcpEndpointWithoutAnyFallbackEndpoint_ShouldThrowEndpointNotFound()
    {
        var resolver = new FakeEndpointResolver().With("kcp", "kcp://10.0.0.2:7002");
        var registry = new TransportProtocolAdapterRegistry();
        registry.Register(TransportSchemes.Tcp, new RecordingTransportAdapter("TCP-FAKE"));
        var dispatcher = new SchemeDispatchTransportAdapter(resolver, registry);

        await Assert.ThrowsAsync<RemoteEndpointNotFoundException>(() => dispatcher.GetOrCreateStreamAsync(new RemoteServiceTarget("C250Svc")));
    }

    [Fact]
    public async Task Dispatch_TcpEndpointWithoutTcpAdapter_ShouldThrowEndpointNotFoundWithoutWarning()
    {
        var resolver = new FakeEndpointResolver().With("tcp", "tcp://10.0.0.1:7001");
        var registry = new TransportProtocolAdapterRegistry();
        registry.Register(TransportSchemes.Kcp, new RecordingTransportAdapter("KCP-FAKE"));
        var dispatcher = new SchemeDispatchTransportAdapter(resolver, registry);

        await Assert.ThrowsAsync<RemoteEndpointNotFoundException>(() => dispatcher.GetOrCreateStreamAsync(new RemoteServiceTarget("C250Svc")));
        Assert.Equal(0, dispatcher.SchemeFallbackWarningCount);
    }

    [Fact]
    public async Task Dispatch_UnresolvedService_ShouldThrowEndpointNotFound()
    {
        var resolver = new FakeEndpointResolver();
        var registry = new TransportProtocolAdapterRegistry();
        registry.Register(TransportSchemes.Tcp, new RecordingTransportAdapter("TCP-FAKE"));
        var dispatcher = new SchemeDispatchTransportAdapter(resolver, registry);

        await Assert.ThrowsAsync<RemoteEndpointNotFoundException>(() => dispatcher.GetOrCreateStreamAsync(new RemoteServiceTarget("C250Nobody")));
        Assert.False(dispatcher.IsServiceAvailable(new RemoteServiceTarget("C250Nobody")));
    }

    [Fact]
    public void Dispatch_InvalidPreferredEndpoint_IsServiceAvailableReturnsFalse()
    {
        var resolver = new FakeEndpointResolver().With("kcp", "kcp://10.0.0.2:0");
        var registry = new TransportProtocolAdapterRegistry();
        registry.Register(TransportSchemes.Tcp, new RecordingTransportAdapter("TCP-FAKE"));
        var dispatcher = new SchemeDispatchTransportAdapter(resolver, registry);

        Assert.False(dispatcher.IsServiceAvailable(new RemoteServiceTarget("C250Svc")));
    }

    [Fact]
    public async Task Dispatch_UniversalAdapter_ServesEverySchemeWithoutFallback()
    {
        var resolver = new FakeEndpointResolver().With("kcp", "kcp://10.0.0.2:7002");
        var universal = new RecordingTransportAdapter("UNIVERSAL-FAKE");
        var registry = new TransportProtocolAdapterRegistry();
        registry.RegisterUniversal(universal);
        var dispatcher = new SchemeDispatchTransportAdapter(resolver, registry);

        var stream = await dispatcher.GetOrCreateStreamAsync(new RemoteServiceTarget("C250Svc"));

        Assert.Same(universal.ReturnedStream, stream);
        Assert.Equal(0, dispatcher.SchemeFallbackWarningCount);
    }

    [Fact]
    public async Task Dispatch_InvalidateAndDispose_BroadcastToAllRegisteredAdapters()
    {
        var resolver = new FakeEndpointResolver().With("tcp", "tcp://10.0.0.1:7001");
        var first = new RecordingTransportAdapter("TCP-FAKE");
        var second = new RecordingTransportAdapter("KCP-FAKE");
        var registry = new TransportProtocolAdapterRegistry();
        registry.Register(TransportSchemes.Tcp, first);
        registry.Register(TransportSchemes.Kcp, second);
        var dispatcher = new SchemeDispatchTransportAdapter(resolver, registry);

        dispatcher.Invalidate();
        dispatcher.Dispose();

        Assert.Equal(1, first.InvalidateAllCount);
        Assert.Equal(1, second.InvalidateAllCount);
        Assert.True(first.IsDisposed);
        Assert.True(second.IsDisposed);
    }

    // ─────────────────────────────────────────────────────────────
    //  ④ KCP 真实现注册语义（builder 级，端点来自路由表附加端点）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Builder_KcpEndpointOnly_UseKcpTransportAdapter_AvailableThroughKcpAdapter()
    {
        // 注册真 kcp 适配器后 kcp 附加端点由其服务（对照：未注册时同实例不可用，见 Builder_KcpEndpointOnly_DefaultBuild_UnresolvableThroughFallback）
        var provider = new MutableRouteTableProvider().SetServiceEndpoint(
            "C250BuilderKcpReal", "tcp://10.0.0.1:7001", "2001", new[] { "kcp://10.0.0.2:7002" });
        var client = new RemoteMessagingBuilder().WithRouteTable(provider).UseKcpTransportAdapter().Build();

        Assert.True(await client.IsServiceAvailableAsync("C250BuilderKcpReal"));
    }

    [Fact]
    public async Task Builder_TcpEndpointOnly_UseKcpTransportAdapter_TcpPathUnaffected()
    {
        var provider = new MutableRouteTableProvider().SetServiceEndpoint("C250BuilderTcpReal", "tcp://10.0.0.1:7001", "2001");
        var client = new RemoteMessagingBuilder().WithRouteTable(provider).UseKcpTransportAdapter().Build();

        Assert.True(await client.IsServiceAvailableAsync("C250BuilderTcpReal"));
    }

    // ─────────────────────────────────────────────────────────────
    //  ⑤ TCP 解析面收敛后的行为差异钉住
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void TcpAdapter_ValidTcpEndpoint_IsServiceAvailableTrue()
    {
        var adapter = new TcpTransportProtocolAdapter(
            new FakeEndpointResolver().With("tcp", "tcp://127.0.0.1:17777"), new FakeConnectionProvider());

        Assert.True(adapter.IsServiceAvailable(new RemoteServiceTarget("C250Svc")));
    }

    [Fact]
    public void TcpAdapter_BracketedIpv6Endpoint_IsServiceAvailableTrue()
    {
        var adapter = new TcpTransportProtocolAdapter(
            new FakeEndpointResolver().With("tcp", "tcp://[::1]:17777"), new FakeConnectionProvider());

        Assert.True(adapter.IsServiceAvailable(new RemoteServiceTarget("C250Svc")));
    }

    [Fact]
    public void TcpAdapter_BareHostPort_IsServiceAvailableFalse()
    {
        // 行为差异钉住：旧解析面（System.Uri 兜底）接受裸 host:port，新解析面拒绝（AC-4a 单一解析契约）。
        var adapter = new TcpTransportProtocolAdapter(
            new FakeEndpointResolver().With("tcp", "127.0.0.1:17777"), new FakeConnectionProvider());

        Assert.False(adapter.IsServiceAvailable(new RemoteServiceTarget("C250Svc")));
    }

    [Fact]
    public void TcpAdapter_ZeroPort_IsServiceAvailableFalse()
    {
        var adapter = new TcpTransportProtocolAdapter(
            new FakeEndpointResolver().With("tcp", "tcp://127.0.0.1:0"), new FakeConnectionProvider());

        Assert.False(adapter.IsServiceAvailable(new RemoteServiceTarget("C250Svc")));
    }

    [Fact]
    public void TcpAdapter_MissingPortWithSchemeDefaultMasking_IsServiceAvailableFalse()
    {
        // 行为差异钉住：旧解析面 System.Uri 会为 ws 填默认端口 80 掩盖端口缺失，新解析面拒绝。
        var adapter = new TcpTransportProtocolAdapter(
            new FakeEndpointResolver().With("tcp", "ws://127.0.0.1"), new FakeConnectionProvider());

        Assert.False(adapter.IsServiceAvailable(new RemoteServiceTarget("C250Svc")));
    }

    [Fact]
    public async Task TcpAdapter_UnparseableEndpoint_GetOrCreateStreamThrowsEndpointNotFound()
    {
        var adapter = new TcpTransportProtocolAdapter(
            new FakeEndpointResolver().With("tcp", "tcp://127.0.0.1:0"), new FakeConnectionProvider());

        await Assert.ThrowsAsync<RemoteEndpointNotFoundException>(() => adapter.GetOrCreateStreamAsync(new RemoteServiceTarget("C250Svc")));
    }

    // ─────────────────────────────────────────────────────────────
    //  ②（builder 级）UseXxx 注册语义与默认 tcp 回落
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Builder_TcpAndKcpEndpoints_DefaultBuild_FallsBackToTcp()
    {
        var provider = new MutableRouteTableProvider().SetServiceEndpoint(
            "C250BuilderBoth", "tcp://10.0.0.1:7001", "2001", new[] { "kcp://10.0.0.2:7002" });
        var client = new RemoteMessagingBuilder().WithRouteTable(provider).Build();

        Assert.True(await client.IsServiceAvailableAsync("C250BuilderBoth"));
    }

    [Fact]
    public void Builder_KcpEndpointOnly_DefaultBuild_UnresolvableThroughFallback()
    {
        // 只有 kcp 端点（解析面无任何 tcp 端点）时，默认构建只注册 tcp 适配器 → 不可达。
        // 路由表形态下 tcp 主端点必填，故经 FakeEndpointResolver 在分发层钉住等价语义。
        var resolverOnlyKcp = new FakeEndpointResolver().With("kcp", "kcp://10.0.0.2:7002");
        var registry = new TransportProtocolAdapterRegistry();
        registry.Register(TransportSchemes.Tcp, new RecordingTransportAdapter("TCP-FAKE"));
        var dispatcher = new SchemeDispatchTransportAdapter(resolverOnlyKcp, registry);

        Assert.False(dispatcher.IsServiceAvailable(new RemoteServiceTarget("C250BuilderKcp")));
    }


    [Fact]
    public async Task Builder_WithTransportAdapterFactory_UniversalAdapterServesKcpEndpoint()
    {
        var provider = new MutableRouteTableProvider().SetServiceEndpoint(
            "C250BuilderFactory", "tcp://10.0.0.1:7001", "2001", new[] { "kcp://10.0.0.2:7002" });
        var universal = new RecordingTransportAdapter("UNIVERSAL-FAKE") { Available = true };
        var client = new RemoteMessagingBuilder()
            .WithRouteTable(provider)
            .WithTransportAdapterFactory((_, _) => universal)
            .Build();

        Assert.True(await client.IsServiceAvailableAsync("C250BuilderFactory"));
        Assert.Equal("C250BuilderFactory", Assert.Single(universal.AvailabilityChecks).ServerType);
    }

    /// <summary>
    /// 固定端点解析器（测试替身）：按 scheme 返回预置端点（忽略 target 分量）。
    /// </summary>
    private sealed class FakeEndpointResolver : IServiceEndpointResolver
    {
        private readonly ConcurrentDictionary<string, string> _endpointsByScheme = new(StringComparer.OrdinalIgnoreCase);

        public FakeEndpointResolver With(string scheme, string endpoint)
        {
            _endpointsByScheme[scheme] = endpoint;
            return this;
        }

        public string ResolveEndpoint(RemoteServiceTarget target)
        {
            foreach (var scheme in TransportSchemes.PreferenceOrder)
            {
                if (_endpointsByScheme.TryGetValue(scheme, out var endpoint))
                {
                    return endpoint;
                }
            }

            return string.Empty;
        }

        public string ResolveEndpoint(RemoteServiceTarget target, string scheme)
        {
            ArgumentException.ThrowIfNullOrEmpty(scheme);

            return _endpointsByScheme.TryGetValue(scheme, out var endpoint) ? endpoint : string.Empty;
        }
    }
    private sealed class RecordingTransportAdapter : ITransportProtocolAdapter
    {
        private readonly MemoryStream _stream = new();

        public RecordingTransportAdapter(string protocolName)
        {
            ProtocolName = protocolName;
        }

        public string ProtocolName { get; }

        public List<RemoteServiceTarget> RequestedTargets { get; } = new();

        public List<RemoteServiceTarget> AvailabilityChecks { get; } = new();

        public int InvalidateAllCount { get; private set; }

        public bool IsDisposed { get; private set; }

        public bool Available { get; set; }

        public Stream ReturnedStream
        {
            get { return _stream; }
        }

        public Task<Stream> GetOrCreateStreamAsync(RemoteServiceTarget target, CancellationToken cancellationToken = default)
        {
            RequestedTargets.Add(target);
            return Task.FromResult<Stream>(_stream);
        }

        public bool IsServiceAvailable(RemoteServiceTarget target)
        {
            AvailabilityChecks.Add(target);
            return Available;
        }

        public void Invalidate()
        {
            InvalidateAllCount++;
        }

        public void Invalidate(Stream stream)
        {
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    /// <summary>
    /// 空连接提供器（测试替身）：仅满足 TCP 适配器构造，可用性路径不触达连接。
    /// </summary>
    private sealed class FakeConnectionProvider : IConnectionProvider
    {
        public Task<Stream> GetOrCreateStreamAsync(string host, int port, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public void Invalidate()
        {
        }

        public void Invalidate(Stream stream)
        {
        }

        public void Dispose()
        {
        }
    }
}
