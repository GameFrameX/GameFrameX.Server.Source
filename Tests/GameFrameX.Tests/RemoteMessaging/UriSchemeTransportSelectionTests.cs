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
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or to violate the legal rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
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
using GameFrameX.Discovery;
using GameFrameX.Discovery.Routing;
using GameFrameX.Network.RemoteMessaging;
using GameFrameX.Network.RemoteMessaging.Composition;
using GameFrameX.Network.RemoteMessaging.Discovery;
using GameFrameX.Network.RemoteMessaging.Transport;

namespace GameFrameX.Tests.RemoteMessaging;

/// <summary>
/// URI scheme 驱动传输选择的验证：resolver scheme 键解析、注册表拨号期分发、回落告警一次、
/// KCP 占位真实可用性、TCP 解析面收敛后的行为差异钉住、builder 注册语义。
/// </summary>
/// <remarks>
/// Verification for the URI-scheme-driven transport selection: resolver scheme-key parsing,
/// registry dispatch at dial time, one-time fallback warning, the KCP placeholder's real
/// availability, the pinned behavior differences after the TCP parsing-surface convergence,
/// and the builder registration semantics.
/// </remarks>
public sealed class UriSchemeTransportSelectionTests : IDisposable
{
    private readonly List<string> _environmentKeys = new();

    public void Dispose()
    {
        foreach (var key in _environmentKeys)
        {
            Environment.SetEnvironmentVariable(key, null);
        }
    }

    private void SetEnvironmentEndpoint(string serviceName, string scheme, string value)
    {
        var key = $"services__{serviceName}__{scheme}__0";
        Environment.SetEnvironmentVariable(key, value);
        _environmentKeys.Add(key);
    }

    // ─────────────────────────────────────────────────────────────
    //  ① 环境变量解析器：scheme 键
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void EnvResolver_TcpKeyOnly_ShouldReturnValueForBothOverloads()
    {
        SetEnvironmentEndpoint("C250TcpOnly", "tcp", "tcp://10.0.0.1:7001");
        var resolver = new EnvironmentVariableEndpointResolver();

        Assert.Equal("tcp://10.0.0.1:7001", resolver.ResolveEndpoint("C250TcpOnly"));
        Assert.Equal("tcp://10.0.0.1:7001", resolver.ResolveEndpoint("C250TcpOnly", "tcp"));
        Assert.Equal("tcp://10.0.0.1:7001", resolver.ResolveEndpoint("C250TcpOnly", "TCP"));
        Assert.Equal(string.Empty, resolver.ResolveEndpoint("C250TcpOnly", "kcp"));
    }

    [Fact]
    public void EnvResolver_KcpAndTcpKeys_KcpPreferredDeterministically()
    {
        SetEnvironmentEndpoint("C250Both", "tcp", "tcp://10.0.0.1:7001");
        SetEnvironmentEndpoint("C250Both", "kcp", "kcp://10.0.0.2:7002");
        var resolver = new EnvironmentVariableEndpointResolver();

        Assert.Equal("kcp://10.0.0.2:7002", resolver.ResolveEndpoint("C250Both"));
        Assert.Equal("tcp://10.0.0.1:7001", resolver.ResolveEndpoint("C250Both", "tcp"));
        Assert.Equal("kcp://10.0.0.2:7002", resolver.ResolveEndpoint("C250Both", "kcp"));
    }

    [Fact]
    public void EnvResolver_KeySchemeSegment_IsCaseInsensitive()
    {
        SetEnvironmentEndpoint("C250UpperKey", "KCP", "kcp://10.0.0.3:7003");
        var resolver = new EnvironmentVariableEndpointResolver();

        Assert.Equal("kcp://10.0.0.3:7003", resolver.ResolveEndpoint("C250UpperKey"));
    }

    [Fact]
    public void EnvResolver_KcpKeyOnly_ShouldReturnKcpEndpoint()
    {
        SetEnvironmentEndpoint("KcpOnlySchemeValue", "kcp", "kcp://10.0.0.4:7004");
        var resolver = new EnvironmentVariableEndpointResolver();

        Assert.Equal("kcp://10.0.0.4:7004", resolver.ResolveEndpoint("KcpOnlySchemeValue"));
    }

    [Fact]
    public void EnvResolver_BareHostPortValue_ShouldThrowEndpointFormatException()
    {
        // 行为差异钉住：裸 host:port 旧解析面接受（Uri 兜底），新解析面拒绝（缺 scheme）。
        SetEnvironmentEndpoint("BareHostPortValue", "tcp", "127.0.0.1:7777");
        var resolver = new EnvironmentVariableEndpointResolver();

        Assert.Throws<EndpointFormatException>(() => resolver.ResolveEndpoint("BareHostPortValue"));
        Assert.Throws<EndpointFormatException>(() => resolver.ResolveEndpoint("BareHostPortValue", "tcp"));
    }

    [Fact]
    public void EnvResolver_ZeroPortValue_ShouldThrowEndpointFormatException()
    {
        SetEnvironmentEndpoint("ZeroPortValue", "tcp", "tcp://10.0.0.5:0");
        var resolver = new EnvironmentVariableEndpointResolver();

        Assert.Throws<EndpointFormatException>(() => resolver.ResolveEndpoint("ZeroPortValue"));
    }

    [Fact]
    public void EnvResolver_UnsupportedKeyScheme_ShouldBeIgnored()
    {
        SetEnvironmentEndpoint("C250Udp", "udp", "udp://10.0.0.6:7006");
        var resolver = new EnvironmentVariableEndpointResolver();

        Assert.Equal(string.Empty, resolver.ResolveEndpoint("C250Udp"));
        Assert.Equal(string.Empty, resolver.ResolveEndpoint("C250Udp", "udp"));
    }

    [Fact]
    public void EnvResolver_UndefinedService_ShouldReturnEmptyString()
    {
        var resolver = new EnvironmentVariableEndpointResolver();

        Assert.Equal(string.Empty, resolver.ResolveEndpoint("C250Undefined_12345"));
        Assert.Equal(string.Empty, resolver.ResolveEndpoint("C250Undefined_12345", "tcp"));
    }

    // ─────────────────────────────────────────────────────────────
    //  ①（续）动态发现解析器：advertiseEndpoint 原样透传 + scheme 过滤
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public void DynamicResolver_AdvertiseEndpoint_PassesThroughAndFiltersByScheme()
    {
        var table = RoleRouteTable.FromInstances(new List<InstanceDescriptor>
        {
            new InstanceDescriptor("Game", "game-1", "kcp://10.0.1.1:7101", InstanceStatus.Active, 10, EndpointAddressKind.IPv4, 201, DateTime.UtcNow),
        });
        var resolver = new DiscoveryServiceEndpointResolver(new FixedTableProvider(table));

        Assert.Equal("kcp://10.0.1.1:7101", resolver.ResolveEndpoint("Game"));
        Assert.Equal("kcp://10.0.1.1:7101", resolver.ResolveEndpoint("Game", "kcp"));
        Assert.Equal(string.Empty, resolver.ResolveEndpoint("Game", "tcp"));
        Assert.Equal(string.Empty, resolver.ResolveEndpoint("Nobody", "kcp"));
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

        var stream = await dispatcher.GetOrCreateStreamAsync("C250Svc");

        Assert.Same(kcpAdapter.ReturnedStream, stream);
        Assert.Equal("C250Svc", Assert.Single(kcpAdapter.RequestedServices));
        Assert.Empty(tcpAdapter.RequestedServices);
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

        var firstStream = await dispatcher.GetOrCreateStreamAsync("C250Svc");
        var secondStream = await dispatcher.GetOrCreateStreamAsync("C250Svc");

        Assert.Same(tcpAdapter.ReturnedStream, firstStream);
        Assert.Same(tcpAdapter.ReturnedStream, secondStream);
        Assert.Equal(2, tcpAdapter.RequestedServices.Count);
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

        await dispatcher.GetOrCreateStreamAsync("C250SvcA");
        await dispatcher.GetOrCreateStreamAsync("C250SvcA");
        await dispatcher.GetOrCreateStreamAsync("C250SvcB");
        await dispatcher.GetOrCreateStreamAsync("C250WsSvc");

        Assert.Equal(3, dispatcher.SchemeFallbackWarningCount);
    }

    [Fact]
    public async Task Dispatch_KcpEndpointWithoutAnyFallbackEndpoint_ShouldThrowEndpointNotFound()
    {
        var resolver = new FakeEndpointResolver().With("kcp", "kcp://10.0.0.2:7002");
        var registry = new TransportProtocolAdapterRegistry();
        registry.Register(TransportSchemes.Tcp, new RecordingTransportAdapter("TCP-FAKE"));
        var dispatcher = new SchemeDispatchTransportAdapter(resolver, registry);

        await Assert.ThrowsAsync<RemoteEndpointNotFoundException>(() => dispatcher.GetOrCreateStreamAsync("C250Svc"));
    }

    [Fact]
    public async Task Dispatch_TcpEndpointWithoutTcpAdapter_ShouldThrowEndpointNotFoundWithoutWarning()
    {
        var resolver = new FakeEndpointResolver().With("tcp", "tcp://10.0.0.1:7001");
        var registry = new TransportProtocolAdapterRegistry();
        registry.Register(TransportSchemes.Kcp, new RecordingTransportAdapter("KCP-FAKE"));
        var dispatcher = new SchemeDispatchTransportAdapter(resolver, registry);

        await Assert.ThrowsAsync<RemoteEndpointNotFoundException>(() => dispatcher.GetOrCreateStreamAsync("C250Svc"));
        Assert.Equal(0, dispatcher.SchemeFallbackWarningCount);
    }

    [Fact]
    public async Task Dispatch_UnresolvedService_ShouldThrowEndpointNotFound()
    {
        var resolver = new FakeEndpointResolver();
        var registry = new TransportProtocolAdapterRegistry();
        registry.Register(TransportSchemes.Tcp, new RecordingTransportAdapter("TCP-FAKE"));
        var dispatcher = new SchemeDispatchTransportAdapter(resolver, registry);

        await Assert.ThrowsAsync<RemoteEndpointNotFoundException>(() => dispatcher.GetOrCreateStreamAsync("C250Nobody"));
        Assert.False(dispatcher.IsServiceAvailable("C250Nobody"));
    }

    [Fact]
    public void Dispatch_InvalidPreferredEndpoint_IsServiceAvailableReturnsFalse()
    {
        var resolver = new FakeEndpointResolver().With("kcp", "kcp://10.0.0.2:0");
        var registry = new TransportProtocolAdapterRegistry();
        registry.Register(TransportSchemes.Tcp, new RecordingTransportAdapter("TCP-FAKE"));
        var dispatcher = new SchemeDispatchTransportAdapter(resolver, registry);

        Assert.False(dispatcher.IsServiceAvailable("C250Svc"));
    }

    [Fact]
    public async Task Dispatch_UniversalAdapter_ServesEverySchemeWithoutFallback()
    {
        var resolver = new FakeEndpointResolver().With("kcp", "kcp://10.0.0.2:7002");
        var universal = new RecordingTransportAdapter("UNIVERSAL-FAKE");
        var registry = new TransportProtocolAdapterRegistry();
        registry.RegisterUniversal(universal);
        var dispatcher = new SchemeDispatchTransportAdapter(resolver, registry);

        var stream = await dispatcher.GetOrCreateStreamAsync("C250Svc");

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
    //  ④ KCP 真实现注册语义（占位已退役；占位期「恒 false/恒抛」语义随之删除，不重钉）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Builder_KcpKeyOnly_UseKcpTransportAdapter_AvailableThroughKcpAdapter()
    {
        // 注册真 kcp 适配器后 kcp 端点由其服务（对照：未注册时同键不可用，见 Builder_KcpKeyOnly_DefaultBuild_UnresolvableThroughFallback）
        SetEnvironmentEndpoint("C250BuilderKcpReal", "kcp", "kcp://10.0.0.2:7002");
        var client = new RemoteMessagingBuilder().UseKcpTransportAdapter().Build();

        Assert.True(await client.IsServiceAvailableAsync("C250BuilderKcpReal"));
    }

    [Fact]
    public async Task Builder_TcpKeyOnly_UseKcpTransportAdapter_TcpPathUnaffected()
    {
        SetEnvironmentEndpoint("C250BuilderTcpReal", "tcp", "tcp://10.0.0.1:7001");
        var client = new RemoteMessagingBuilder().UseKcpTransportAdapter().Build();

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

        Assert.True(adapter.IsServiceAvailable("C250Svc"));
    }

    [Fact]
    public void TcpAdapter_BracketedIpv6Endpoint_IsServiceAvailableTrue()
    {
        var adapter = new TcpTransportProtocolAdapter(
            new FakeEndpointResolver().With("tcp", "tcp://[::1]:17777"), new FakeConnectionProvider());

        Assert.True(adapter.IsServiceAvailable("C250Svc"));
    }

    [Fact]
    public void TcpAdapter_BareHostPort_IsServiceAvailableFalse()
    {
        // 行为差异钉住：旧解析面（System.Uri 兜底）接受裸 host:port，新解析面拒绝（AC-4a 单一解析契约）。
        var adapter = new TcpTransportProtocolAdapter(
            new FakeEndpointResolver().With("tcp", "127.0.0.1:17777"), new FakeConnectionProvider());

        Assert.False(adapter.IsServiceAvailable("C250Svc"));
    }

    [Fact]
    public void TcpAdapter_ZeroPort_IsServiceAvailableFalse()
    {
        var adapter = new TcpTransportProtocolAdapter(
            new FakeEndpointResolver().With("tcp", "tcp://127.0.0.1:0"), new FakeConnectionProvider());

        Assert.False(adapter.IsServiceAvailable("C250Svc"));
    }

    [Fact]
    public void TcpAdapter_MissingPortWithSchemeDefaultMasking_IsServiceAvailableFalse()
    {
        // 行为差异钉住：旧解析面 System.Uri 会为 ws 填默认端口 80 掩盖端口缺失，新解析面拒绝。
        var adapter = new TcpTransportProtocolAdapter(
            new FakeEndpointResolver().With("tcp", "ws://127.0.0.1"), new FakeConnectionProvider());

        Assert.False(adapter.IsServiceAvailable("C250Svc"));
    }

    [Fact]
    public async Task TcpAdapter_UnparseableEndpoint_GetOrCreateStreamThrowsEndpointNotFound()
    {
        var adapter = new TcpTransportProtocolAdapter(
            new FakeEndpointResolver().With("tcp", "tcp://127.0.0.1:0"), new FakeConnectionProvider());

        await Assert.ThrowsAsync<RemoteEndpointNotFoundException>(() => adapter.GetOrCreateStreamAsync("C250Svc"));
    }

    // ─────────────────────────────────────────────────────────────
    //  ②（builder 级）UseXxx 注册语义与默认 tcp 回落
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Builder_TcpAndKcpKeys_DefaultBuild_FallsBackToTcp()
    {
        SetEnvironmentEndpoint("C250BuilderBoth", "tcp", "tcp://10.0.0.1:7001");
        SetEnvironmentEndpoint("C250BuilderBoth", "kcp", "kcp://10.0.0.2:7002");
        var client = new RemoteMessagingBuilder().Build();

        Assert.True(await client.IsServiceAvailableAsync("C250BuilderBoth"));
    }

    [Fact]
    public async Task Builder_KcpKeyOnly_DefaultBuild_UnresolvableThroughFallback()
    {
        SetEnvironmentEndpoint("C250BuilderKcp", "kcp", "kcp://10.0.0.2:7002");
        var client = new RemoteMessagingBuilder().Build();

        Assert.False(await client.IsServiceAvailableAsync("C250BuilderKcp"));
    }


    [Fact]
    public async Task Builder_WithTransportAdapterFactory_UniversalAdapterServesKcpEndpoint()
    {
        SetEnvironmentEndpoint("C250BuilderFactory", "kcp", "kcp://10.0.0.2:7002");
        var universal = new RecordingTransportAdapter("UNIVERSAL-FAKE") { Available = true };
        var client = new RemoteMessagingBuilder()
            .WithTransportAdapterFactory((_, _) => universal)
            .Build();

        Assert.True(await client.IsServiceAvailableAsync("C250BuilderFactory"));
        Assert.Equal("C250BuilderFactory", Assert.Single(universal.AvailabilityChecks));
    }

    /// <summary>
    /// 固定路由表提供者（测试替身）。
    /// </summary>
    private sealed class FixedTableProvider : IRoleRouteTableProvider
    {
        public FixedTableProvider(RoleRouteTable table)
        {
            Current = table;
        }

        public RoleRouteTable Current { get; }
    }

    /// <summary>
    /// 固定端点解析器（测试替身）：按 scheme 返回预置端点。
    /// </summary>
    private sealed class FakeEndpointResolver : IServiceEndpointResolver
    {
        private readonly ConcurrentDictionary<string, string> _endpointsByScheme = new(StringComparer.OrdinalIgnoreCase);

        public FakeEndpointResolver With(string scheme, string endpoint)
        {
            _endpointsByScheme[scheme] = endpoint;
            return this;
        }

        public string ResolveEndpoint(string serviceName)
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

        public string ResolveEndpoint(string serviceName, string scheme)
        {
            ArgumentException.ThrowIfNullOrEmpty(scheme);

            return _endpointsByScheme.TryGetValue(scheme, out var endpoint) ? endpoint : string.Empty;
        }
    }

    /// <summary>
    /// 记录型传输适配器（测试替身）：记录请求、返回固定流、统计失效与释放。
    /// </summary>
    private sealed class RecordingTransportAdapter : ITransportProtocolAdapter
    {
        private readonly MemoryStream _stream = new();

        public RecordingTransportAdapter(string protocolName)
        {
            ProtocolName = protocolName;
        }

        public string ProtocolName { get; }

        public List<string> RequestedServices { get; } = new();

        public List<string> AvailabilityChecks { get; } = new();

        public int InvalidateAllCount { get; private set; }

        public bool IsDisposed { get; private set; }

        public bool Available { get; set; } = true;

        public Stream ReturnedStream
        {
            get { return _stream; }
        }

        public Task<Stream> GetOrCreateStreamAsync(string serviceName, CancellationToken cancellationToken = default)
        {
            RequestedServices.Add(serviceName);
            return Task.FromResult<Stream>(_stream);
        }

        public bool IsServiceAvailable(string serviceName)
        {
            AvailabilityChecks.Add(serviceName);
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
            throw new NotSupportedException("FakeConnectionProvider is not expected to dial in these tests.");
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
