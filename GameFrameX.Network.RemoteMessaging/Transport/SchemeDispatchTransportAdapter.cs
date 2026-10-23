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
using GameFrameX.Foundation.Localization.Core;

namespace GameFrameX.Network.RemoteMessaging.Transport;

/// <summary>
/// 拨号期按端点 scheme 分发的传输适配器：解析端点 URI → <see cref="EndpointParser"/> 小写归一 scheme →
/// 注册表选适配器；端点 scheme 无已注册适配器时回落 TCP 端点并告警一次（渐进迁移语义）。
/// </summary>
/// <remarks>
/// The dial-time scheme-dispatching transport adapter: resolves the endpoint URI, normalizes the
/// scheme through <see cref="EndpointParser"/>, selects the adapter from the registry, and — when
/// the endpoint scheme has no registered adapter — falls back to the service's TCP endpoint with
/// a one-time-per-(service, scheme) warning (gradual-migration semantics). "Automatic switching"
/// means this dial-time selection only; mid-flight cross-protocol failover belongs to the RPC
/// layer's retry/circuit-breaker, per the 2026-10-16 decision. When no endpoint of any scheme
/// resolves (including an unparseable endpoint), <see cref="RemoteEndpointNotFoundException"/> is
/// thrown, preserving the legacy exception contract. A registered universal adapter serves
/// every scheme and bypasses the fallback entirely (legacy single-selection semantics).
/// </remarks>
internal sealed class SchemeDispatchTransportAdapter : ITransportProtocolAdapter
{
    private readonly IServiceEndpointResolver _endpointResolver;
    private readonly TransportProtocolAdapterRegistry _registry;
    private readonly ConcurrentDictionary<string, byte> _schemeFallbackWarned = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 初始化拨号期 scheme 分发适配器。
    /// </summary>
    /// <remarks>
    /// Initializes the dial-time scheme-dispatching adapter.
    /// </remarks>
    /// <param name="endpointResolver">服务端点解析器 / The service endpoint resolver</param>
    /// <param name="registry">传输协议适配器注册表 / The transport protocol adapter registry</param>
    /// <exception cref="ArgumentNullException">当任一参数为 null 时抛出 / Thrown when any argument is null</exception>
    public SchemeDispatchTransportAdapter(IServiceEndpointResolver endpointResolver, TransportProtocolAdapterRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(endpointResolver);
        ArgumentNullException.ThrowIfNull(registry);

        _endpointResolver = endpointResolver;
        _registry = registry;
    }

    /// <summary>
    /// 获取协议名称，固定返回 SchemeDispatch（本适配器为分发面，实际协议由命中适配器决定）。
    /// </summary>
    /// <remarks>
    /// Gets the protocol name, always returning SchemeDispatch (this adapter is the dispatch
    /// surface; the actual protocol is decided by the matched adapter).
    /// </remarks>
    public string ProtocolName
    {
        get { return "SchemeDispatch"; }
    }

    /// <summary>
    /// 获取或创建目标服务对应的可用流：按端点 scheme 分发到已注册适配器；无匹配适配器时回落 TCP 端点。
    /// </summary>
    /// <remarks>
    /// Gets an existing or creates a new usable stream for the target service by dispatching to
    /// the adapter registered for the endpoint's scheme; when that scheme has no registered
    /// adapter, it falls back to the service's TCP endpoint (one-time warning per service and
    /// scheme). No usable endpoint of any scheme throws
    /// <see cref="RemoteEndpointNotFoundException"/>.
    /// </remarks>
    /// <param name="serviceName">目标服务名 / Target service name</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>可用流 / Usable stream</returns>
    /// <exception cref="RemoteEndpointNotFoundException">无任何可用端点时抛出 / Thrown when no usable endpoint of any scheme exists</exception>
    public async Task<Stream> GetOrCreateStreamAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        var adapter = ResolveAdapter(serviceName);
        return await adapter.GetOrCreateStreamAsync(serviceName, cancellationToken);
    }

    /// <summary>
    /// 检查目标服务是否可用：按端点 scheme 分发到命中适配器判定；端点不可解析或回落失败返回 false。
    /// </summary>
    /// <remarks>
    /// Checks whether the target service is available by delegating to the adapter matched for
    /// the endpoint's scheme; unresolvable endpoints or exhausted fallbacks return false
    /// (never throws).
    /// </remarks>
    /// <param name="serviceName">目标服务名 / Target service name</param>
    /// <returns>命中适配器判定服务可用时返回 true / true when the matched adapter considers the service available</returns>
    public bool IsServiceAvailable(string serviceName)
    {
        try
        {
            var adapter = ResolveAdapter(serviceName);
            return adapter.IsServiceAvailable(serviceName);
        }
        catch (RemoteEndpointNotFoundException)
        {
            return false;
        }
        catch (EndpointFormatException)
        {
            return false;
        }
    }

    /// <summary>
    /// 标记全部已注册适配器的连接失效，促使下次请求重建连接。
    /// </summary>
    /// <remarks>
    /// Invalidates the connections of every registered adapter so the next request recreates them.
    /// </remarks>
    public void Invalidate()
    {
        foreach (var adapter in _registry.GetAdapters())
        {
            adapter.Invalidate();
        }
    }

    /// <summary>
    /// 标记指定流所属连接失效：广播到全部已注册适配器（各适配器仅移除自己持有的连接）。
    /// </summary>
    /// <remarks>
    /// Invalidates the connection owning the given stream by broadcasting to every registered
    /// adapter (each adapter removes only the connections it owns).
    /// </remarks>
    /// <param name="stream">连接流 / The connection stream</param>
    public void Invalidate(Stream stream)
    {
        foreach (var adapter in _registry.GetAdapters())
        {
            adapter.Invalidate(stream);
        }
    }

    /// <summary>
    /// 释放全部已注册适配器持有的资源。
    /// </summary>
    /// <remarks>
    /// Disposes the resources held by every registered adapter.
    /// </remarks>
    public void Dispose()
    {
        foreach (var adapter in _registry.GetAdapters())
        {
            adapter.Dispose();
        }
    }

    /// <summary>
    /// 获取已触发回落告警的（服务, scheme）对数量（诊断/测试观测用）。
    /// </summary>
    /// <remarks>
    /// The number of distinct (service, scheme) pairs that have triggered the fallback warning
    /// (exposed for diagnostics and tests).
    /// </remarks>
    /// <value>已告警的去重对数量 / The de-duplicated warned-pair count</value>
    internal int SchemeFallbackWarningCount
    {
        get { return _schemeFallbackWarned.Count; }
    }

    /// <summary>
    /// 解析目标服务的端点并按 scheme 选择适配器：命中直接返回；未命中且非 tcp 时回落 TCP 端点。
    /// </summary>
    /// <remarks>
    /// Resolves the target service's endpoint and selects the adapter by scheme: a direct hit
    /// returns immediately; a miss on a non-tcp scheme falls back to the service's TCP endpoint.
    /// An empty resolution or an unparseable endpoint (resolver fail-fast on structurally
    /// invalid values) throws <see cref="RemoteEndpointNotFoundException"/>, as does an
    /// exhausted fallback.
    /// </remarks>
    /// <param name="serviceName">目标服务名 / Target service name</param>
    /// <returns>命中或回落选中的适配器 / The matched or fallback adapter</returns>
    /// <exception cref="RemoteEndpointNotFoundException">无任何可用端点或端点不可解析时抛出 / Thrown when no usable endpoint exists or the endpoint is unparseable</exception>
    private ITransportProtocolAdapter ResolveAdapter(string serviceName)
    {
        var endpoint = ResolvePreferredEndpoint(serviceName);
        var scheme = ParseEndpointScheme(serviceName, endpoint);

        if (_registry.TryGet(scheme, out var adapter))
        {
            return adapter;
        }

        // tcp 端点本身无适配器即无可回落目标，直接维持端点未找到异常契约。
        if (string.Equals(scheme, TransportSchemes.Tcp, StringComparison.OrdinalIgnoreCase))
        {
            throw new RemoteEndpointNotFoundException(serviceName, endpoint);
        }

        WarnSchemeFallbackOnce(serviceName, scheme);

        var tcpEndpoint = ResolveTcpFallbackEndpoint(serviceName);
        if (tcpEndpoint.Length == 0 || !_registry.TryGet(TransportSchemes.Tcp, out adapter))
        {
            throw new RemoteEndpointNotFoundException(serviceName, endpoint);
        }

        return adapter;
    }

    /// <summary>
    /// 解析偏好序选中的端点；解析器对非法键值的 fail-fast 异常转译为端点未找到。
    /// </summary>
    /// <remarks>
    /// Resolves the preferred endpoint; the resolver's fail-fast exception on structurally
    /// invalid values is translated into the endpoint-not-found contract.
    /// </remarks>
    /// <param name="serviceName">目标服务名 / Target service name</param>
    /// <returns>未解析端点 URI 字符串 / The unparsed endpoint URI string</returns>
    /// <exception cref="RemoteEndpointNotFoundException">未找到端点或端点不可解析时抛出 / Thrown when the endpoint is not found or unparseable</exception>
    private string ResolvePreferredEndpoint(string serviceName)
    {
        try
        {
            var endpoint = _endpointResolver.ResolveEndpoint(serviceName);
            if (endpoint.Length == 0)
            {
                throw new RemoteEndpointNotFoundException(serviceName, endpoint);
            }

            return endpoint;
        }
        catch (EndpointFormatException exception)
        {
            throw new RemoteEndpointNotFoundException(serviceName, exception.Message);
        }
    }

    /// <summary>
    /// 解析服务的 TCP 回落端点；空串表示未声明 TCP 端点，非法键值同样转译为端点未找到。
    /// </summary>
    /// <remarks>
    /// Resolves the service's TCP fallback endpoint; an empty string means no TCP endpoint is
    /// declared, and structurally invalid values are translated into the endpoint-not-found
    /// contract as well.
    /// </remarks>
    /// <param name="serviceName">目标服务名 / Target service name</param>
    /// <returns>TCP 端点字符串；未声明时为空串 / The TCP endpoint string; empty when not declared</returns>
    private string ResolveTcpFallbackEndpoint(string serviceName)
    {
        try
        {
            return _endpointResolver.ResolveEndpoint(serviceName, TransportSchemes.Tcp);
        }
        catch (EndpointFormatException exception)
        {
            throw new RemoteEndpointNotFoundException(serviceName, exception.Message);
        }
    }

    /// <summary>
    /// 解析端点 scheme（<see cref="EndpointParser"/> 小写归一）；结构非法即端点不可用。
    /// </summary>
    /// <remarks>
    /// Parses the endpoint scheme (normalized to lower case by <see cref="EndpointParser"/>);
    /// a structurally invalid endpoint is an unusable endpoint.
    /// </remarks>
    /// <param name="serviceName">目标服务名（用于异常消息）/ The target service name (used in the exception message)</param>
    /// <param name="endpoint">未解析端点 URI 字符串 / The unparsed endpoint URI string</param>
    /// <returns>小写归一后的 scheme / The lower-cased scheme</returns>
    private static string ParseEndpointScheme(string serviceName, string endpoint)
    {
        try
        {
            return EndpointParser.Parse(endpoint).Scheme;
        }
        catch (EndpointFormatException exception)
        {
            throw new RemoteEndpointNotFoundException(serviceName, exception.Message);
        }
    }

    /// <summary>
    /// 按（服务, scheme）去重告警回落事件，每个组合仅告警一次。
    /// </summary>
    /// <remarks>
    /// Emits the fallback warning once per distinct (service, scheme) pair.
    /// </remarks>
    /// <param name="serviceName">目标服务名 / Target service name</param>
    /// <param name="scheme">未注册适配器的端点 scheme / The endpoint scheme without a registered adapter</param>
    private void WarnSchemeFallbackOnce(string serviceName, string scheme)
    {
        if (_schemeFallbackWarned.TryAdd($"{scheme}|{serviceName}", 0))
        {
            // Localization: RemoteMessaging.Transport.SchemeFallbackToTcp - 端点 scheme '{0}' 未注册传输适配器，服务 '{1}' 回落使用 TCP 端点
            LogHelper.Warning(LocalizationService.GetString(Localization.Keys.RemoteMessaging.Transport.SchemeFallbackToTcp, scheme, serviceName));
        }
    }
}
