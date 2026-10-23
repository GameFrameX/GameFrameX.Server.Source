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

namespace GameFrameX.Network.RemoteMessaging.Transport;

/// <summary>
/// KCP 协议适配器：负责服务端点解析（scheme=kcp）与 KCP 会话建立，
/// 结构与 TCP 适配器同构（解析端点 → 委托连接提供器）。
/// </summary>
/// <remarks>
/// KCP protocol adapter: responsible for service endpoint resolution (scheme=kcp) and KCP session
/// setup, structurally mirroring the TCP adapter (resolve endpoint → delegate to the connection
/// provider). Sessions are managed by <see cref="KcpConnectionProvider"/> (dedicated UDP socket +
/// conversation id per session, stale eviction below the server-side idle-clear window); the
/// returned <see cref="Stream"/> is the <see cref="KcpStream"/> duplex adaptation (length-prefixed
/// framing equivalent to the TCP path).
/// </remarks>
internal sealed class KcpTransportProtocolAdapter : ITransportProtocolAdapter
{
    private readonly IConnectionProvider _connectionProvider;
    private readonly IServiceEndpointResolver _endpointResolver;

    /// <summary>
    /// 初始化 KCP 协议适配器。
    /// </summary>
    /// <remarks>
    /// Initializes the KCP protocol adapter.
    /// </remarks>
    /// <param name="endpointResolver">服务发现解析器 / Service endpoint resolver</param>
    /// <param name="connectionProvider">KCP 会话连接提供器 / The KCP session connection provider</param>
    public KcpTransportProtocolAdapter(IServiceEndpointResolver endpointResolver, IConnectionProvider connectionProvider)
    {
        _endpointResolver = endpointResolver;
        _connectionProvider = connectionProvider;
    }

    /// <summary>
    /// 获取协议名称，固定返回 KCP。
    /// </summary>
    /// <remarks>
    /// Gets the protocol name, always returning KCP.
    /// </remarks>
    public string ProtocolName
    {
        get { return "KCP"; }
    }

    /// <summary>
    /// 获取或创建目标服务对应的可用 KCP 会话流：解析服务的 KCP 端点 URI 并提取主机与端口，
    /// 端点非法时抛出 <see cref="RemoteEndpointNotFoundException"/>，否则委托连接提供器获取或建立会话流。
    /// </summary>
    /// <remarks>
    /// Gets an existing or creates a new usable KCP session stream for the target service: it resolves
    /// the service's KCP endpoint URI (<c>services__{ServiceName}__kcp__*</c> key or the advertise
    /// endpoint) and extracts the host and port through the authoritative <c>EndpointParser</c>,
    /// throws <see cref="RemoteEndpointNotFoundException"/> when the endpoint is invalid, and
    /// otherwise delegates to the connection provider.
    /// </remarks>
    /// <param name="serviceName">目标服务名 / Target service name</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>可用的会话流 / A usable session stream</returns>
    /// <exception cref="RemoteEndpointNotFoundException">服务端点无法解析为合法主机与端口时抛出 / Thrown when the service endpoint cannot be parsed into a valid host and port</exception>
    public async Task<Stream> GetOrCreateStreamAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        var endpoint = _endpointResolver.ResolveEndpoint(serviceName, TransportSchemes.Kcp);
        if (!TryParseEndpoint(endpoint, out var host, out var port))
        {
            throw new RemoteEndpointNotFoundException(serviceName, endpoint);
        }

        return await _connectionProvider.GetOrCreateStreamAsync(host, port, cancellationToken);
    }

    /// <summary>
    /// 检查目标服务是否可用：解析服务的 KCP 端点 URI 并尝试提取主机与端口，解析成功即视为可用
    /// （与 TCP 适配器同口径：可达性判定 = 端点可解析，不预拨号）。
    /// </summary>
    /// <remarks>
    /// Checks whether the target service is available: it resolves the service's KCP endpoint URI
    /// and tries to extract the host and port; a successful parse means the service is available
    /// (same contract as the TCP adapter: availability = endpoint resolvable, no eager dialing).
    /// </remarks>
    /// <param name="serviceName">目标服务名 / Target service name</param>
    /// <returns>端点可解析为合法主机与端口时返回 true / Returns true when the endpoint parses into a valid host and port</returns>
    public bool IsServiceAvailable(string serviceName)
    {
        var endpoint = _endpointResolver.ResolveEndpoint(serviceName, TransportSchemes.Kcp);
        return TryParseEndpoint(endpoint, out _, out _);
    }

    /// <summary>
    /// 标记连接失效：委托连接提供器丢弃当前全部会话，促使下次请求重建。
    /// </summary>
    /// <remarks>
    /// Invalidates the connections by delegating to the connection provider, so the next request recreates them.
    /// </remarks>
    public void Invalidate()
    {
        _connectionProvider.Invalidate();
    }

    /// <summary>
    /// 标记指定流所属会话失效：委托连接提供器仅丢弃该会话，其余会话保持可用。
    /// </summary>
    /// <remarks>
    /// Invalidates the session owning the given stream by delegating to the connection provider,
    /// which drops only that session while others stay usable.
    /// </remarks>
    /// <param name="stream">会话流 / The session stream</param>
    public void Invalidate(Stream stream)
    {
        _connectionProvider.Invalidate(stream);
    }

    /// <summary>
    /// 释放适配器持有的资源：委托连接提供器释放其管理的全部会话。
    /// </summary>
    /// <remarks>
    /// Disposes the resources held by the adapter by delegating to the connection provider
    /// to release all sessions it manages.
    /// </remarks>
    public void Dispose()
    {
        _connectionProvider.Dispose();
    }

    /// <summary>
    /// 通过权威解析面 <see cref="EndpointParser"/> 提取主机与端口；结构非法（缺 scheme、裸 host:port、端口 0 等）返回 false。
    /// </summary>
    /// <remarks>
    /// Extracts the host and port through the authoritative <see cref="EndpointParser"/>; any
    /// structural violation (missing scheme, bare host:port, port 0, unbracketed IPv6, ...)
    /// returns false — identical to the TCP adapter's single parsing-surface contract.
    /// </remarks>
    /// <param name="endpoint">未解析端点 URI 字符串 / The unparsed endpoint URI string</param>
    /// <param name="host">解析出的主机 / The parsed host</param>
    /// <param name="port">解析出的端口 / The parsed port</param>
    /// <returns>解析成功返回 true；否则 false / true when parsed; otherwise false</returns>
    private static bool TryParseEndpoint(string endpoint, out string host, out int port)
    {
        host = string.Empty;
        port = 0;
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return false;
        }

        try
        {
            var parsedEndpoint = EndpointParser.Parse(endpoint);
            host = parsedEndpoint.Host;
            port = parsedEndpoint.Port;
            return true;
        }
        catch (EndpointFormatException)
        {
            return false;
        }
    }
}
