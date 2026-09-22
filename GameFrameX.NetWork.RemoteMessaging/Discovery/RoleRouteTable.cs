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
/// 双视图路由表快照（C143d D15：Role 视图 + Instance 视图）。
/// </summary>
/// <remarks>
/// The immutable dual-view route table snapshot (C143d D15).
/// The Role view serves D3 case 3 (any active instance of a role: only
/// <see cref="InstanceStatus.Active"/> instances — Draining is excluded from new
/// traffic); the Instance view serves D3 case 2 (a known instance: Active and
/// Draining both stay routable for in-flight deliveries).
/// The watcher replaces the whole snapshot atomically (Interlocked.Exchange);
/// readers never lock and never observe a partially-updated table.
/// </remarks>
public sealed class RoleRouteTable
{
    /// <summary>
    /// 空路由表（初始态）。
    /// </summary>
    /// <remarks>
    /// The empty table (the initial state before the first poll completes).
    /// </remarks>
    public static readonly RoleRouteTable Empty = new RoleRouteTable(
        new Dictionary<string, IReadOnlyList<InstanceDescriptor>>(StringComparer.Ordinal),
        new Dictionary<string, InstanceDescriptor>(StringComparer.Ordinal));

    /// <summary>
    /// Role 视图：Role 名 → Active 实例列表。
    /// </summary>
    /// <remarks>
    /// The Role view: role name to its Active instances (Draining excluded).
    /// </remarks>
    private readonly IReadOnlyDictionary<string, IReadOnlyList<InstanceDescriptor>> _activeInstancesByRole;

    /// <summary>
    /// Instance 视图：instanceId → 实例（Active + Draining）。
    /// </summary>
    /// <remarks>
    /// The Instance view: instance id to its descriptor (Active and Draining).
    /// </remarks>
    private readonly IReadOnlyDictionary<string, InstanceDescriptor> _instancesById;

    /// <summary>
    /// 初始化双视图路由表。
    /// </summary>
    /// <remarks>
    /// Initializes the snapshot from the live instance set; both views are derived
    /// in one pass so they can never disagree.
    /// </remarks>
    /// <param name="activeInstancesByRole">Role → Active 实例列表 / Role to its Active instances</param>
    /// <param name="instancesById">instanceId → 实例 / Instance id to its descriptor</param>
    private RoleRouteTable(IReadOnlyDictionary<string, IReadOnlyList<InstanceDescriptor>> activeInstancesByRole, IReadOnlyDictionary<string, InstanceDescriptor> instancesById)
    {
        _activeInstancesByRole = activeInstancesByRole;
        _instancesById = instancesById;
    }

    /// <summary>
    /// 从存活实例集合构建双视图快照。
    /// </summary>
    /// <remarks>
    /// Builds a snapshot from the live instance set. Only Active instances enter
    /// the Role view; the Instance view keeps Active and Draining; every other
    /// status (Booting, Stopped, Removed, ...) is excluded from both views so no
    /// not-yet-ready or decommissioned instance is ever routable.
    /// </remarks>
    /// <param name="liveInstances">本轮判活后的实例集合 / The instances judged live this round</param>
    /// <returns>双视图快照 / The dual-view snapshot</returns>
    public static RoleRouteTable FromInstances(IEnumerable<InstanceDescriptor> liveInstances)
    {
        ArgumentNullException.ThrowIfNull(liveInstances, nameof(liveInstances));

        var instancesById = new Dictionary<string, InstanceDescriptor>(StringComparer.Ordinal);
        var activeByRole = new Dictionary<string, List<InstanceDescriptor>>(StringComparer.Ordinal);
        foreach (var instance in liveInstances)
        {
            // Instance 视图仅收 Active/Draining（D3 case 2 契约）：Booting/Removed 等其余状态不参与任何路由，
            // 防止已注册但尚未就绪（或已摘除）的实例被 case 2 解析并转发流量。
            if (instance.Status == InstanceStatus.Active || instance.Status == InstanceStatus.Draining)
            {
                instancesById.Add(instance.InstanceId, instance);
            }

            if (instance.Status == InstanceStatus.Active)
            {
                if (!activeByRole.TryGetValue(instance.Role, out var instances))
                {
                    instances = new List<InstanceDescriptor>();
                    activeByRole[instance.Role] = instances;
                }

                instances.Add(instance);
            }
        }

        var activeView = new Dictionary<string, IReadOnlyList<InstanceDescriptor>>(activeByRole.Count, StringComparer.Ordinal);
        foreach (var pair in activeByRole)
        {
            activeView.Add(pair.Key, pair.Value.AsReadOnly());
        }

        return new RoleRouteTable(activeView, instancesById);
    }

    /// <summary>
    /// 按实例 Id 查找实例（D3 case 2）。
    /// </summary>
    /// <remarks>
    /// Resolves an instance by id (D3 case 2). Draining instances resolve on purpose:
    /// in-flight deliveries to a known draining instance are still valid.
    /// </remarks>
    /// <param name="instanceId">实例 Id / The instance id</param>
    /// <param name="instance">实例描述符；未找到时为 null / The descriptor, or null when absent</param>
    /// <returns>找到返回 true；否则 false / true when found; otherwise false</returns>
    public bool TryGetInstance(string instanceId, out InstanceDescriptor instance)
    {
        return _instancesById.TryGetValue(instanceId, out instance);
    }

    /// <summary>
    /// 获取指定 Role 的 Active 实例列表（D3 case 3）。
    /// </summary>
    /// <remarks>
    /// Gets the Active instances of a role (D3 case 3); Draining is excluded.
    /// </remarks>
    /// <param name="roleName">Role 名 / The role name</param>
    /// <returns>Active 实例只读列表；无实例时为空列表 / The read-only Active instance list, or an empty list</returns>
    public IReadOnlyList<InstanceDescriptor> GetActiveInstances(string roleName)
    {
        if (_activeInstancesByRole.TryGetValue(roleName, out var instances))
        {
            return instances;
        }

        return Array.Empty<InstanceDescriptor>();
    }

    /// <summary>
    /// 获取全部实例（Instance 视图值集合）。
    /// </summary>
    /// <remarks>
    /// Gets every instance in the Instance view.
    /// </remarks>
    /// <returns>全部实例 / All instances</returns>
    public IReadOnlyCollection<InstanceDescriptor> GetAllInstances()
    {
        return (IReadOnlyCollection<InstanceDescriptor>)_instancesById.Values;
    }
}
