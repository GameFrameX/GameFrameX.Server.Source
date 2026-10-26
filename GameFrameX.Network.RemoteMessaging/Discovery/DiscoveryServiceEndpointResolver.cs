// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规及开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others as prohibited by laws and regulations!
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


namespace GameFrameX.Network.RemoteMessaging.Discovery;

/// <summary>
/// 基于心跳注册表路由表的服务端点解析器（服务器间端点的唯一事实源）。
/// </summary>
/// <remarks>
/// The <see cref="IServiceEndpointResolver"/> backed by the heartbeat registry's dual-view
/// route table — the single source of truth for inter-server endpoints (the former
/// environment-variable bootstrap is retired). Resolution rules:
/// <list type="bullet">
/// <item>指定 <see cref="RemoteServiceTarget.InstanceId"/>：走 Instance 视图
/// <see cref="RoleRouteTable.TryGetInstance"/>（Active + Draining 都可解析——下线中实例上的在途
/// 投递仍合法），并校验实例的 Role 与 <see cref="RemoteServiceTarget.ServerType"/> 一致，不一致按未找到处理。</item>
/// <item>只给 ServerType：取 Role 视图 Active 实例中 InstanceId 序数（<c>StringComparer.Ordinal</c>）最小者。
/// 等价性边界：等长数字实例 id（compose 形态，<c>GameFrameX__RoleInstanceId</c> 显式注入）时序数最小 ≡ 生成器的
/// 首实例；未注入该环境变量时实例 id 为自动生成形态，序数最小 = 最早启动的实例——语义不同，但同为确定性选择。</item>
/// <item>实例端点集合 = TCP 主端点（AdvertiseEndpoint）+ 附加广播端点；scheme 偏好 kcp &gt; ws &gt; wss &gt; tcp，
/// tcp 为通用回落（缺适配器时的回落由 SchemeDispatch 适配器承担）。</item>
/// </list>
/// 未找到时返回空字符串（既有契约）；端点结构非法（如 InProc 回环标记 <c>tcp://127.0.0.1:0</c>，
/// <c>EndpointParser</c> 拒绝端口 0）时 <see cref="EndpointFormatException"/> 原样上抛，由分发适配器转译为
/// <see cref="Transport.RemoteEndpointNotFoundException"/> → <see cref="Contracts.RemoteStatusCode.EndpointNotFound"/>。
/// </remarks>
internal sealed class DiscoveryServiceEndpointResolver : IServiceEndpointResolver
{
    /// <summary>
    /// 双视图路由表提供者。
    /// </summary>
    /// <remarks>
    /// The dual-view route table provider (the watcher in production).
    /// </remarks>
    private readonly IRoleRouteTableProvider _tableProvider;

    /// <summary>
    /// 初始化发现层端点解析器。
    /// </summary>
    /// <remarks>
    /// Initializes the resolver.
    /// </remarks>
    /// <param name="tableProvider">双视图路由表提供者 / The dual-view route table provider</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="tableProvider"/> 为 null 时抛出 / Thrown when <paramref name="tableProvider"/> is null</exception>
    public DiscoveryServiceEndpointResolver(IRoleRouteTableProvider tableProvider)
    {
        ArgumentNullException.ThrowIfNull(tableProvider);

        _tableProvider = tableProvider;
    }

    /// <summary>
    /// 解析端点目标的偏好端点 URI（确定性偏好序：kcp &gt; ws &gt; wss &gt; tcp）。
    /// </summary>
    /// <remarks>
    /// Resolves the preferred endpoint URI for the target (deterministic preference order
    /// kcp &gt; ws &gt; wss &gt; tcp): the instance's endpoint collection is its tcp primary
    /// advertise endpoint plus its additional advertise endpoints, so when several schemes
    /// are advertised at once the non-tcp scheme wins and tcp stays the universal fallback
    /// (gradual-migration semantics; the missing-adapter fallback itself belongs to the
    /// scheme-dispatch adapter). Deployments that advertise only the tcp endpoint resolve
    /// exactly that endpoint. The value is returned unparsed — dial-time consumers parse it
    /// with <c>EndpointParser</c>.
    /// </remarks>
    /// <param name="target">结构化端点目标 / The structured endpoint target</param>
    /// <returns>未解析端点 URI 字符串（scheme://host:port）；未找到时返回空字符串 / The unparsed endpoint URI string, or an empty string when not found</returns>
    public string ResolveEndpoint(RemoteServiceTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        foreach (var scheme in TransportSchemes.PreferenceOrder)
        {
            var endpoint = ResolveEndpoint(target, scheme);
            if (endpoint.Length > 0)
            {
                return endpoint;
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// 解析端点目标在指定 scheme 下的端点 URI。
    /// </summary>
    /// <remarks>
    /// Resolves the endpoint URI of the target for the specified scheme (compared
    /// case-insensitively after <c>EndpointParser</c> normalization): the instance's
    /// endpoint collection is its tcp primary advertise endpoint plus its additional
    /// advertise endpoints (at most one entry per scheme), so this returns the unique
    /// endpoint of the requested scheme or an empty string when the instance declares
    /// none.
    /// </remarks>
    /// <param name="target">结构化端点目标 / The structured endpoint target</param>
    /// <param name="scheme">端点 scheme / The endpoint scheme</param>
    /// <returns>未解析端点 URI 字符串；scheme 未声明时返回空字符串 / The unparsed endpoint URI string; an empty string when no endpoint of that scheme is declared</returns>
    public string ResolveEndpoint(RemoteServiceTarget target, string scheme)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrEmpty(scheme);

        if (!TryResolveDescriptor(target, out var descriptor))
        {
            return string.Empty;
        }

        var normalizedScheme = scheme.Trim().ToLowerInvariant();
        foreach (var endpoint in EnumerateInstanceEndpoints(descriptor))
        {
            if (string.Equals(EndpointParser.Parse(endpoint).Scheme, normalizedScheme, StringComparison.Ordinal))
            {
                return endpoint;
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// 按解析规则定位实例描述符（指定实例走 Instance 视图并校验 Role；ServerType 级取 Active 实例中 InstanceId 序数最小者）。
    /// </summary>
    /// <remarks>
    /// Locates the instance descriptor by the resolution rules: an explicit instance id goes
    /// through the Instance view (Active and Draining — in-flight deliveries to a draining
    /// instance stay valid) with a Role consistency check (a mismatch counts as not found);
    /// a ServerType-level target picks the Active instance with the ordinally smallest
    /// InstanceId (a deterministic choice — see the class remarks for the equivalence
    /// boundary against the former first-instance bootstrap).
    /// </remarks>
    private bool TryResolveDescriptor(RemoteServiceTarget target, out InstanceDescriptor descriptor)
    {
        var table = _tableProvider.Current;

        if (target.InstanceId != null)
        {
            return table.TryGetInstance(target.InstanceId, out descriptor)
                   && string.Equals(descriptor.Role, target.ServerType, StringComparison.Ordinal);
        }

        var activeInstances = table.GetActiveInstances(target.ServerType);
        InstanceDescriptor smallest = null;
        foreach (var instance in activeInstances)
        {
            if (smallest == null || string.Compare(instance.InstanceId, smallest.InstanceId, StringComparison.Ordinal) < 0)
            {
                smallest = instance;
            }
        }

        descriptor = smallest;
        return smallest != null;
    }

    /// <summary>
    /// 枚举实例端点集合：TCP 主端点在前，附加广播端点随后。
    /// </summary>
    /// <remarks>
    /// Enumerates the instance's endpoint collection: the tcp primary advertise endpoint
    /// first, then the additional advertise endpoints.
    /// </remarks>
    private static IEnumerable<string> EnumerateInstanceEndpoints(InstanceDescriptor descriptor)
    {
        yield return descriptor.AdvertiseEndpoint;

        for (var index = 0; index < descriptor.AdditionalAdvertiseEndpoints.Count; index++)
        {
            yield return descriptor.AdditionalAdvertiseEndpoints[index];
        }
    }
}
