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


using GameFrameX.NetWork.RemoteMessaging.Discovery;

namespace GameFrameX.NetWork.RemoteMessaging.Routing;

/// <summary>
/// 基于 Mongo 发现层的跨进程 Role 转发器（C143d D3 case 2/3 真实实现）。
/// </summary>
/// <remarks>
/// The real D3 case 2/3 implementation consuming the MongoEndpointWatcher dual-view
/// route table (C143d; it replaces the C143c placeholder that always threw).
/// Case 2 resolves the envelope's target instance id through the Instance view
/// (Draining stays resolvable — in-flight deliveries remain valid during graceful
/// scale-down); case 3 picks from the Role view's Active instances (Draining
/// excluded — no new traffic) with a deterministic first-choice policy —
/// ponytail: upgrade path is the existing ConsistentHashServerInstanceSelector
/// once per-key affinity is needed. Both failures are loud
/// <see cref="RouteNotFoundException"/>s, matching the C143c seam contract of
/// never silently dropping a message.
/// </remarks>
public sealed class MongoDiscoveryRemoteRoleRouter : IRemoteRoleRouter
{
    /// <summary>
    /// 双视图路由表提供者。
    /// </summary>
    /// <remarks>
    /// The dual-view route table provider (the watcher in production).
    /// </remarks>
    private readonly IRoleRouteTableProvider _tableProvider;

    /// <summary>
    /// 信封转发缝（发送通道）。
    /// </summary>
    /// <remarks>
    /// The envelope forwarding seam (the send channel).
    /// </remarks>
    private readonly IEnvelopeForwarder _forwarder;

    /// <summary>
    /// 初始化跨进程 Role 转发器。
    /// </summary>
    /// <remarks>
    /// Initializes the router with the table provider and the send channel.
    /// </remarks>
    /// <param name="tableProvider">双视图路由表提供者 / The dual-view route table provider</param>
    /// <param name="forwarder">信封转发缝 / The envelope forwarding seam</param>
    public MongoDiscoveryRemoteRoleRouter(IRoleRouteTableProvider tableProvider, IEnvelopeForwarder forwarder)
    {
        ArgumentNullException.ThrowIfNull(tableProvider, nameof(tableProvider));
        ArgumentNullException.ThrowIfNull(forwarder, nameof(forwarder));

        _tableProvider = tableProvider;
        _forwarder = forwarder;
    }

    /// <summary>
    /// 将信封转发给目标 Role 所在的远端进程（D3 case 2/3）。
    /// </summary>
    /// <remarks>
    /// Resolves the target instance through the dual-view table and forwards the
    /// envelope through the send channel. An unresolvable target (unknown instance
    /// id for case 2, or no Active instance of the role for case 3) throws
    /// <see cref="RouteNotFoundException"/>; a transport failure propagates as-is.
    /// </remarks>
    /// <param name="envelope">路由信封（目标 Role 不属于本进程角色集）/ The routing envelope (target role is hosted by another process)</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>恒为 <see cref="RoleRouteDelivery.RemoteForwarded"/> / Always <see cref="RoleRouteDelivery.RemoteForwarded"/></returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="envelope"/> 为 null 时抛出 / Thrown when envelope is null</exception>
    /// <exception cref="RouteNotFoundException">当目标实例或目标 Role 的 Active 实例不存在时抛出 / Thrown when the target instance or the role's Active instances are absent</exception>
    public async Task<RoleRouteDelivery> ForwardAsync(MessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope, nameof(envelope));

        var table = _tableProvider.Current;
        var targetInstance = ResolveTargetInstance(table, envelope);

        // 把选中的实例 Id 盖到信封上：case 3 的选择结果对发送通道与接收端复投（C143e）都必须可见。
        var stampedEnvelope = envelope.TargetInstanceId == targetInstance.InstanceId
            ? envelope
            : new MessageEnvelope(envelope.TargetRole, envelope.Message, envelope.TargetActorId, targetInstance.InstanceId);

        var parsedEndpoint = EndpointParser.Parse(targetInstance.AdvertiseEndpoint);
        await _forwarder.ForwardAsync(parsedEndpoint, stampedEnvelope, cancellationToken);
        return RoleRouteDelivery.RemoteForwarded;
    }

    /// <summary>
    /// 解析目标实例（D3 case 2 按实例 Id / case 3 取 Active 首选）。
    /// </summary>
    /// <remarks>
    /// Resolves the target instance: case 2 by the envelope's instance id, case 3
    /// deterministically from the role's Active instances.
    /// </remarks>
    /// <param name="table">当前双视图快照 / The current dual-view snapshot</param>
    /// <param name="envelope">路由信封 / The routing envelope</param>
    /// <returns>目标实例 / The resolved target instance</returns>
    /// <exception cref="RouteNotFoundException">当目标不可解析时抛出 / Thrown when the target cannot be resolved</exception>
    private static InstanceDescriptor ResolveTargetInstance(RoleRouteTable table, MessageEnvelope envelope)
    {
        // D3 case 2：指定实例投递（Draining 实例仍可命中——在途投递合法）。
        if (!string.IsNullOrEmpty(envelope.TargetInstanceId))
        {
            if (!table.TryGetInstance(envelope.TargetInstanceId, out var instanceById))
            {
                throw new RouteNotFoundException(envelope.TargetRole, $"The target instance '{envelope.TargetInstanceId}' of role '{envelope.TargetRole}' is not present in the dual-view route table (unknown id, offline, or removed).");
            }

            return instanceById;
        }

        // D3 case 3：任意 Active 实例（Draining 不接新流量）。
        var activeInstances = table.GetActiveInstances(envelope.TargetRole);
        if (activeInstances.Count == 0)
        {
            throw new RouteNotFoundException(envelope.TargetRole, $"Role '{envelope.TargetRole}' has no Active instance in the dual-view route table; the role is scaled to zero or fully draining.");
        }

        // ponytail: 确定性首选策略——无粘性需求时最简正确；需要按 key 亲和时接入 ConsistentHashServerInstanceSelector（Unified/）。
        return activeInstances[0];
    }
}
