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
using GameFrameX.Discovery;
using GameFrameX.Network.RemoteMessaging;
using GameFrameX.Network.RemoteMessaging.Contracts;
using GameFrameX.Network.RemoteMessaging.Transport;

namespace GameFrameX.ServerLink;

/// <summary>
/// 服务器间链路薄门面构建器：在 <see cref="GameFrameX.Network.RemoteMessaging.RemoteMessagingBuilder"/> 组合面之上
/// 固化内置默认——直连 TCP 拨号 + 固定重试上限 + PassThrough 韧性（不熔断、不做端点健康评估）。
/// 路由表为必填（心跳注册表双视图读侧），未绑定时构建即失败。
/// </summary>
/// <remarks>
/// The thin builder facade for the inter-server link. It pins the built-in defaults on top of the
/// <see cref="GameFrameX.Network.RemoteMessaging.RemoteMessagingBuilder"/> composition surface: direct TCP dialing,
/// a fixed retry limit, and the pass-through resilience posture (no circuit breaking and no endpoint
/// health evaluation). The route table is required (the heartbeat registry's dual-view read side);
/// building without one fails fast.
/// </remarks>
/// <example>
///     <code>
/// var client = new ServerLinkBuilder()
///     .WithRouteTable(ActiveDiscoveryRuntime.RouteTableProvider)
///     .Build();
///
/// // 需要 UDP 传输时显式叠加 KCP
/// var kcpClient = new ServerLinkBuilder()
///     .WithRouteTable(ActiveDiscoveryRuntime.RouteTableProvider)
///     .WithKcpTransport()
///     .Build();
///     </code>
/// </example>
public sealed class ServerLinkBuilder
{
    /// <summary>
    /// 固定重试上限的缺省值（3 次重试）。
    /// </summary>
    /// <remarks>
    /// The default fixed retry limit (3 retries).
    /// </remarks>
    public const int DefaultFixedRetryLimit = 3;

    /// <summary>
    /// 缺省调用超时毫秒数（5000ms，与 RemoteMessaging 缺省一致）。
    /// </summary>
    /// <remarks>
    /// The default call timeout in milliseconds (5000 ms, same as the RemoteMessaging default).
    /// </remarks>
    public const int DefaultTimeoutMilliseconds = 5000;

    private IRoleRouteTableProvider _routeTableProvider;
    private Func<IConnectionProvider> _connectionProviderFactory;
    private int _timeoutMilliseconds = DefaultTimeoutMilliseconds;
    private int _fixedRetryLimit = DefaultFixedRetryLimit;
    private int _retryBaseDelayMilliseconds = 200;
    private bool _enableKcpTransport;

    /// <summary>
    /// 绑定发现层路由表（服务器间端点的唯一事实源：心跳注册表双视图读侧）。必填。
    /// </summary>
    /// <remarks>
    /// Binds the discovery-layer route table (the single source of truth for inter-server
    /// endpoints: the heartbeat registry's dual-view read side). Required.
    /// </remarks>
    /// <param name="routeTableProvider">双视图路由表提供者 / The dual-view route table provider</param>
    /// <returns>构建器实例 / The builder instance</returns>
    /// <exception cref="ArgumentNullException">当 routeTableProvider 为 null 时抛出 / Thrown when routeTableProvider is null</exception>
    public ServerLinkBuilder WithRouteTable(IRoleRouteTableProvider routeTableProvider)
    {
        _routeTableProvider = routeTableProvider ?? throw new ArgumentNullException(nameof(routeTableProvider));
        return this;
    }

    /// <summary>
    /// 设置调用超时毫秒数。
    /// </summary>
    /// <remarks>
    /// Sets the call timeout in milliseconds.
    /// </remarks>
    /// <param name="timeoutMilliseconds">超时毫秒数 / The timeout duration in milliseconds</param>
    /// <returns>构建器实例 / The builder instance</returns>
    public ServerLinkBuilder WithTimeoutMilliseconds(int timeoutMilliseconds)
    {
        _timeoutMilliseconds = timeoutMilliseconds;
        return this;
    }

    /// <summary>
    /// 设置固定重试上限（直连拨号的内置重试策略；0 表示不重试）。
    /// </summary>
    /// <remarks>
    /// Sets the fixed retry limit (the built-in retry policy of the direct dial; 0 disables retries).
    /// </remarks>
    /// <param name="maxRetryCount">最大重试次数 / The maximum number of retry attempts</param>
    /// <param name="baseDelayMilliseconds">重试基础延迟毫秒数 / The base delay in milliseconds between retries</param>
    /// <returns>构建器实例 / The builder instance</returns>
    public ServerLinkBuilder WithFixedRetryLimit(int maxRetryCount, int baseDelayMilliseconds = 200)
    {
        _fixedRetryLimit = maxRetryCount;
        _retryBaseDelayMilliseconds = baseDelayMilliseconds;
        return this;
    }

    /// <summary>
    /// 叠加注册 KCP 拨号适配器（UDP 传输；TCP 仍为回落传输，与 RemoteMessaging 组合面一致）。
    /// </summary>
    /// <remarks>
    /// Registers the KCP dialing adapter on top (the UDP transport; tcp stays the fallback
    /// transport, identical to the RemoteMessaging composition surface).
    /// </remarks>
    /// <returns>构建器实例 / The builder instance</returns>
    public ServerLinkBuilder WithKcpTransport()
    {
        _enableKcpTransport = true;
        return this;
    }

    /// <summary>
    /// 配置连接提供器工厂（测试注入点）：替代缺省的进程级共享 TCP 连接池。
    /// </summary>
    /// <remarks>
    /// Configures the connection provider factory (a test injection point) that replaces the
    /// default process-wide shared TCP connection pool.
    /// </remarks>
    /// <param name="connectionProviderFactory">连接提供器工厂 / The connection provider factory</param>
    /// <returns>构建器实例 / The builder instance</returns>
    /// <exception cref="ArgumentNullException">当 connectionProviderFactory 为 null 时抛出 / Thrown when connectionProviderFactory is null</exception>
    public ServerLinkBuilder WithConnectionProvider(Func<IConnectionProvider> connectionProviderFactory)
    {
        _connectionProviderFactory = connectionProviderFactory ?? throw new ArgumentNullException(nameof(connectionProviderFactory));
        return this;
    }

    /// <summary>
    /// 构建远程消息客户端。内置默认：直连 TCP 拨号、固定重试上限、PassThrough 韧性、压缩与包上限取 RemoteMessaging 缺省。
    /// 未绑定路由表时抛 <see cref="InvalidOperationException"/>（fail-fast、无回落）。
    /// </summary>
    /// <remarks>
    /// Builds the remote message client with the built-in defaults: direct TCP dialing, the fixed
    /// retry limit, the pass-through resilience posture, and RemoteMessaging's default compression
    /// and packet limits. Throws <see cref="InvalidOperationException"/> when no route table was
    /// bound (fail fast; there is no fallback).
    /// </remarks>
    /// <returns>远程消息客户端实例 / The remote message client instance</returns>
    public IRemoteMessageClient Build()
    {
        if (_routeTableProvider == null)
        {
            throw new InvalidOperationException("ServerLinkBuilder requires a route table: call WithRouteTable(IRoleRouteTableProvider) before Build().");
        }

        var builder = new RemoteMessagingBuilder()
            .WithTimeoutMs(_timeoutMilliseconds)
            .WithRetry(_fixedRetryLimit, _retryBaseDelayMilliseconds)
            .WithRouteTable(_routeTableProvider);

        if (_enableKcpTransport)
        {
            builder.UseKcpTransportAdapter();
        }

        if (_connectionProviderFactory != null)
        {
            builder.WithConnectionProvider(_connectionProviderFactory);
        }

        return builder.Build();
    }
}
