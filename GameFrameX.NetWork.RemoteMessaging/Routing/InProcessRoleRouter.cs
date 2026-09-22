// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository:  https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:   https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:     https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


namespace GameFrameX.NetWork.RemoteMessaging.Routing;

/// <summary>
/// 进程内跨 Role 路由器（C143c D3 三步判定）。
/// </summary>
/// <remarks>
/// In-process cross-role router implementing the D3 three-step decision.
/// The hosted role names are captured as a defensive snapshot at construction time
/// (mirroring the RoleSet snapshot semantics of C143b), so this instance always routes
/// against the process shape it was built for. The constructor accepts plain role names
/// instead of the StartUp RoleSet type because this assembly must not depend on the
/// startup module; the launch flow passes the published role snapshot when wiring
/// <see cref="RoleRouterHolder"/>.
/// Failure semantics: every undecidable route throws <see cref="RouteNotFoundException"/> —
/// the router never drops a message and never falls back to another branch silently.
/// </remarks>
public sealed class InProcessRoleRouter : IRoleRouter
{
    /// <summary>
    /// 本进程承载的 Role 名快照。
    /// </summary>
    /// <remarks>
    /// The defensive snapshot of role names hosted by this process.
    /// Uses the default ordinal comparer, matching the RoleSet snapshot semantics of C143b.
    /// </remarks>
    private readonly HashSet<string> _hostedRoleNames;

    /// <summary>
    /// 本地投递缝（case 1；可为 null，为 null 时 case 1 命中即显式失败）。
    /// </summary>
    /// <remarks>
    /// The local delivery seam (case 1); may be null, in which case a case 1 hit fails loudly.
    /// </remarks>
    private readonly ILocalRoleMessageDispatcher _localDispatcher;

    /// <summary>
    /// 远程转发缝（case 2/3；可为 null，为 null 时非本进程目标即显式失败）。
    /// </summary>
    /// <remarks>
    /// The remote forwarding seam (case 2/3); may be null, in which case any non-local target fails loudly.
    /// </remarks>
    private readonly IRemoteRoleRouter _remoteRouter;

    /// <summary>
    /// 初始化进程内跨 Role 路由器。
    /// </summary>
    /// <remarks>
    /// Initializes the router with a defensive copy of the hosted role names
    /// and the optional delivery seams. Both seams are optional on purpose:
    /// production wiring (GameApp) installs the router before the actor-backed local
    /// dispatcher exists (C143e) and before remote forwarding exists (C143d), so a
    /// premature route attempt fails with <see cref="RouteNotFoundException"/> instead
    /// of silently succeeding against a half-built pipeline.
    /// </remarks>
    /// <param name="hostedRoleNames">本进程承载的 Role 名集合（构造时防御性拷贝）/ The role names hosted by this process (defensively copied)</param>
    /// <param name="localDispatcher">本地投递缝；null 表示 case 1 命中时显式失败 / The local delivery seam; null makes case 1 hits fail loudly</param>
    /// <param name="remoteRouter">远程转发缝；null 表示 case 2/3 显式失败 / The remote forwarding seam; null makes case 2/3 fail loudly</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="hostedRoleNames"/> 为 null 时抛出 / Thrown when <paramref name="hostedRoleNames"/> is null</exception>
    public InProcessRoleRouter(IEnumerable<string> hostedRoleNames, ILocalRoleMessageDispatcher localDispatcher = null, IRemoteRoleRouter remoteRouter = null)
    {
        ArgumentNullException.ThrowIfNull(hostedRoleNames, nameof(hostedRoleNames));

        _hostedRoleNames = new HashSet<string>(hostedRoleNames);
        _localDispatcher = localDispatcher;
        _remoteRouter = remoteRouter;
    }

    /// <summary>
    /// 路由一封跨 Role 消息信封（D3 三步判定）。
    /// </summary>
    /// <remarks>
    /// Applies the D3 three-step decision to the envelope:
    /// target role hosted by this process goes to the local dispatcher (case 1);
    /// anything else goes to the remote forwarding seam (case 2/3).
    /// An empty target role, a case 1 hit without a local dispatcher, or a non-local
    /// target without a remote forwarder all throw <see cref="RouteNotFoundException"/> —
    /// never a silent fallback.
    /// </remarks>
    /// <param name="envelope">路由信封 / The routing envelope</param>
    /// <param name="cancellationToken">取消操作的令牌 / The cancellation token</param>
    /// <returns>实际命中的投递分支 / The delivery branch that was actually hit</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="envelope"/> 为 null 时抛出 / Thrown when <paramref name="envelope"/> is null</exception>
    /// <exception cref="RouteNotFoundException">当路由决策失败时抛出 / Thrown when routing cannot decide a route</exception>
    public async Task<RoleRouteDelivery> RouteAsync(MessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope, nameof(envelope));

        if (string.IsNullOrWhiteSpace(envelope.TargetRole))
        {
            throw new RouteNotFoundException(envelope.TargetRole, "Cannot route an envelope without a target role.");
        }

        // D3 case 1：目标 Role 属于本进程角色集 → 本地投递
        if (_hostedRoleNames.Contains(envelope.TargetRole))
        {
            if (_localDispatcher == null)
            {
                throw new RouteNotFoundException(
                    envelope.TargetRole,
                    $"Target role '{envelope.TargetRole}' is hosted by this process but no local message dispatcher is configured (the actor-backed dispatcher arrives with C143e).");
            }

            await _localDispatcher.DispatchAsync(envelope, cancellationToken);
            return RoleRouteDelivery.LocalActor;
        }

        // D3 case 2/3：目标 Role 不属于本进程角色集 → 远程转发缝（真实实现随 C143d 交付）
        if (_remoteRouter == null)
        {
            throw new RouteNotFoundException(
                envelope.TargetRole,
                $"Target role '{envelope.TargetRole}' is not hosted by this process and no remote role router is configured (remote forwarding arrives with C143d).");
        }

        return await _remoteRouter.ForwardAsync(envelope, cancellationToken);
    }
}
