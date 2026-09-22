// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规及开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
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


namespace GameFrameX.NetWork.RemoteMessaging.Discovery;

/// <summary>
/// 基于发现层的动态服务端点解析器（C143d D15）。
/// </summary>
/// <remarks>
/// The dynamic IServiceEndpointResolver backed by the discovery layer (C143d D15).
/// <see cref="AspireEndpointResolver"/> stays as the static-bootstrap implementation
/// (frozen, not removed); this counterpart resolves a service name — which in the
/// dynamic topology is the role name — to the advertise endpoint of one of its
/// Active instances from the watcher's dual-view table. It lets the existing RPC
/// transport chain (RemoteMessageClient over ITransportProtocolAdapter) consume
/// the dynamic table without any change to the chain itself.
/// </remarks>
internal sealed class MongoDiscoveryServiceEndpointResolver : IServiceEndpointResolver
{
    /// <summary>
    /// 双视图路由表提供者。
    /// </summary>
    /// <remarks>
    /// The dual-view route table provider (the watcher in production).
    /// </remarks>
    private readonly IRoleRouteTableProvider _tableProvider;

    /// <summary>
    /// 初始化动态服务端点解析器。
    /// </summary>
    /// <remarks>
    /// Initializes the resolver.
    /// </remarks>
    /// <param name="tableProvider">双视图路由表提供者 / The dual-view route table provider</param>
    public MongoDiscoveryServiceEndpointResolver(IRoleRouteTableProvider tableProvider)
    {
        ArgumentNullException.ThrowIfNull(tableProvider, nameof(tableProvider));

        _tableProvider = tableProvider;
    }

    /// <summary>
    /// 解析指定服务（Role 名）的 TCP 端点地址。
    /// </summary>
    /// <remarks>
    /// Resolves the TCP endpoint for the given service (role) name from the Active
    /// instances of the dual-view table. Same deterministic first-choice policy as
    /// the router's D3 case 3 (and the same ConsistentHashServerInstanceSelector
    /// upgrade path); no Active instance resolves to an empty string, matching the
    /// Aspire resolver contract.
    /// </remarks>
    /// <param name="serviceName">目标服务名（动态拓扑下即 Role 名）/ The target service name (the role name in the dynamic topology)</param>
    /// <returns>端点地址字符串（scheme://host:port）；未找到时返回空字符串 / The endpoint string, or an empty string when not found</returns>
    public string ResolveTcpEndpoint(string serviceName)
    {
        var activeInstances = _tableProvider.Current.GetActiveInstances(serviceName);
        if (activeInstances.Count == 0)
        {
            return string.Empty;
        }

        return activeInstances[0].AdvertiseEndpoint;
    }
}
