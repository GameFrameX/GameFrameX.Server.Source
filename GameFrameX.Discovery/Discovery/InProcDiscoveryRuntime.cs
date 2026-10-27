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
//   please refer to the LICENSE file at the root directory of the source code for the full license text.
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


using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Foundation.Logger;
using GameFrameX.Localization;

namespace GameFrameX.Discovery;

/// <summary>
/// 单进程拓扑发现层 Runtime（SQLite 拓扑边界）：自指路由表替代心跳存储，无库依赖。
/// </summary>
/// <remarks>
/// The in-process discovery runtime for the SQLite single-process topology. It satisfies
/// the convention shape (<c>public static Task ActivateAsync(DiscoveryActivationOptions, CancellationToken)</c>, the
/// paired <see cref="ActiveDiscoveryRuntime.BindRouteTable"/> registration) without any database
/// backing: the route table is built once from <c>options.HostedRoleNames</c> plus this process's
/// endpoint (advertise-environment endpoint when configured, a loopback marker otherwise), so
/// cross-role queries resolve to this very process. The heartbeat store, the read-side watcher and
/// <see cref="ActiveDiscoveryRuntime.MarkActiveAsync"/> are intentionally no-ops: the registry slot
/// stays null (there is nothing to heartbeat against — the SQLite provider does not own a
/// cross-process discovery store by design), which also makes <c>MarkActiveAsync</c> a harmless
/// no-op. The topology boundary is therefore single-process: cross-process discovery / routing is
/// unavailable (disclosed by the SQLite startup boundary warning).
/// </remarks>
public static class InProcDiscoveryRuntime
{
    /// <summary>
    /// 一次性激活守卫（首调胜出，镜像 Mongo/PG Runtime）。
    /// </summary>
    /// <remarks>
    /// The one-shot activation guard (first call wins, mirroring the Mongo/PG runtimes).
    /// </remarks>
    private static int _activated;

    /// <summary>
    /// 激活产出的自指路由表提供者。
    /// </summary>
    /// <remarks>
    /// The self-referential route-table provider produced by activation.
    /// </remarks>
    private static volatile InProcRouteTableProvider _tableProvider;

    /// <summary>
    /// 激活产出的读侧路由表（测试/诊断用；未激活时 null）。
    /// </summary>
    /// <remarks>
    /// The activated route-table reader (test/diagnostic surface; null before activation).
    /// </remarks>
    public static IRoleRouteTableProvider TableProvider => _tableProvider;

    /// <summary>
    /// 激活单进程发现层（约定形状；自指路由表 + no-op 心跳面）——无异步工作，同步完成后返回已完成任务。
    /// </summary>
    /// <remarks>
    /// Activates the in-process discovery layer. Validation order mirrors the provider runtimes so
    /// the guard test can drive every enum member through the resolver: null options, then
    /// ConnectionName (validated for contract-shape parity even though no control database is used),
    /// then HostedRoleNames. The route table registers through
    /// <see cref="ActiveDiscoveryRuntime.BindRouteTable"/>; the registry slot is intentionally left
    /// null (no heartbeat write side — see the class remarks). Calling more than once per process
    /// is a no-op. There is no asynchronous work here, so the implementation runs synchronously and
    /// returns a completed task — parameter validation therefore throws synchronously to the
    /// caller (awaiting callers observe it identically, matching the provider runtimes whose
    /// validation faults surface on the awaited task).
    /// </remarks>
    /// <param name="options">激活参数（HostedRoleNames 必填；ConnectionName 仅做契约形态校验）/ Activation options (HostedRoleNames required; ConnectionName validated for contract-shape parity only)</param>
    /// <param name="cancellationToken">取消令牌（契约形态对齐；本实现无异步工作，仅在装配前检查）/ Cancellation token (contract-shape parity; checked before wiring — no async work exists here)</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="options"/> 或 <c>HostedRoleNames</c> 为 null 时抛出 / Thrown when options or HostedRoleNames is null</exception>
    /// <exception cref="ArgumentException">当 <c>ConnectionName</c> 未设置时抛出 / Thrown when ConnectionName is not set</exception>
    public static Task ActivateAsync(DiscoveryActivationOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.ConnectionName))
        {
            // Localization: Database.Discovery.ConnectionNameRequired - 必须设置 ConnectionName。
            throw new ArgumentException(LocalizationService.GetString(Localization.Keys.Database.DiscoveryConnectionNameRequired), nameof(options));
        }

        var hostedRoleNames = options.HostedRoleNames;
        ArgumentNullException.ThrowIfNull(hostedRoleNames, nameof(options.HostedRoleNames));

        cancellationToken.ThrowIfCancellationRequested();

        if (Interlocked.CompareExchange(ref _activated, 1, 0) != 0)
        {
            return Task.CompletedTask;
        }

        var hostedRoles = (hostedRoleNames as IReadOnlyCollection<string> ?? hostedRoleNames.ToList()).Where(role => !string.IsNullOrWhiteSpace(role)).Distinct(StringComparer.Ordinal).ToList();
        var descriptors = new List<InstanceDescriptor>(hostedRoles.Count);
        var now = DateTime.UtcNow;
        foreach (var role in hostedRoles)
        {
            descriptors.Add(CreateSelfDescriptor(role, now));
        }

        var table = RoleRouteTable.FromInstances(descriptors);
        _tableProvider = new InProcRouteTableProvider(table);
        // 读侧路由表登记公共槽位：组合根经 RouteTableProvider 无分派回读装配路由缝。
        // The route-table reader is registered into the public slot so the composition root
        // wires the routing seam without branching on the provider enum.
        ActiveDiscoveryRuntime.BindRouteTable(_tableProvider);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 重置全部装配状态（仅测试隔离使用）。
    /// </summary>
    /// <remarks>
    /// Resets the wiring state for test isolation. Test-only: production code never calls this.
    /// </remarks>
    internal static void ResetForTest()
    {
        _activated = 0;
        _tableProvider = null;
    }

    /// <summary>
    /// 构建单 Role 的自指描述符（Active 态；优先广播环境端点，未配置时用本机回环标记端点）。
    /// </summary>
    /// <remarks>
    /// Builds the self-referential descriptor for one role in the Active status. The endpoint comes
    /// from the advertise environment when configured (rewritten from Booting to Active — only
    /// Active instances enter the Role view); otherwise a loopback marker endpoint is used: hosted
    /// roles are routed by the in-process router and never dial this endpoint, while cross-process
    /// routing is out of the SQLite topology by design.
    /// </remarks>
    /// <param name="role">Role 名 / The role name</param>
    /// <param name="now">激活时刻（UTC）/ The activation instant (UTC)</param>
    /// <returns>自指实例描述符 / The self-referential descriptor</returns>
    private static InstanceDescriptor CreateSelfDescriptor(string role, DateTime now)
    {
        var advertised = DiscoveryRegistry.CreateSelfDescriptorFromEnvironment(role);
        if (advertised != null)
        {
            return new InstanceDescriptor(advertised.Role, advertised.InstanceId, advertised.AdvertiseEndpoint, InstanceStatus.Active, advertised.Load, advertised.AddressKind, advertised.Incarnation, now);
        }

        const string loopbackEndpoint = "tcp://127.0.0.1:0";
        return new InstanceDescriptor(role, $"inproc-{role}", loopbackEndpoint, InstanceStatus.Active, 0, EndpointAddressKind.IPv4, now.Ticks, now);
    }

    /// <summary>
    /// 固定快照路由表提供者（单进程拓扑：表在激活时一次性构成，永不轮询）。
    /// </summary>
    /// <remarks>
    /// The fixed-snapshot route-table provider: the table is built once at activation and never
    /// polled (there is no watcher to feed it).
    /// </remarks>
    private sealed class InProcRouteTableProvider : IRoleRouteTableProvider
    {
        /// <summary>
        /// 以固定快照初始化提供者。
        /// </summary>
        /// <remarks>
        /// Initializes the provider with the fixed snapshot.
        /// </remarks>
        /// <param name="table">激活时构建的路由表快照 / The snapshot built at activation</param>
        public InProcRouteTableProvider(RoleRouteTable table)
        {
            Current = table;
        }

        /// <summary>
        /// 获取当前双视图路由表快照（永不为 null）。
        /// </summary>
        /// <remarks>
        /// Gets the current snapshot. Never returns null.
        /// </remarks>
        /// <value>固定快照 / The fixed snapshot</value>
        public RoleRouteTable Current { get; }
    }
}
