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


using System.Collections.Concurrent;
using GameFrameX.NetWork.Messages;
using GameFrameX.NetWork.RemoteMessaging.Routing;

namespace GameFrameX.Tests.Topology.Equivalence;

/// <summary>
/// 等价用例集的拓扑世界：把若干模拟进程 cell 组装成一种可路由的拓扑（C143c D9）。
/// </summary>
/// <remarks>
/// Topology world for the equivalence suite (C143c D9): assembles simulated process cells
/// into a routable topology. AllInOne is one cell hosting every role (every hop must hit
/// D3 case 1); MultiProcess follows the design source §D9 three-process split
/// (Gate | Game+Social | Match) with a loopback forwarder standing in for the C143d
/// remote hop. Handlers may route replies back through <see cref="RouteAsync"/> — a
/// handler runs on its own mailbox consumer, so cross-role round trips never deadlock.
/// </remarks>
public sealed class TopologyWorld : IDisposable
{
    /// <summary>
    /// 单个模拟进程 cell：角色集 + 路由器 + 邮箱。
    /// </summary>
    private sealed class TopologyCell
    {
        /// <summary>cell 承载的 Role 名 / The role names hosted by this cell</summary>
        public List<string> RoleNames { get; } = new List<string>();

        /// <summary>cell 路由器 / The cell router</summary>
        public InProcessRoleRouter Router { get; set; }

        /// <summary>Role → 邮箱 / Role to mailbox</summary>
        public Dictionary<string, RoleMailbox> Mailboxes { get; } = new Dictionary<string, RoleMailbox>();
    }

    /// <summary>
    /// cell 内本地投递器：按信封目标 Role 分发到对应邮箱。
    /// </summary>
    private sealed class CellMailboxDispatcher : ILocalRoleMessageDispatcher
    {
        /// <summary>cell 邮箱表 / The cell mailboxes</summary>
        private readonly IReadOnlyDictionary<string, RoleMailbox> _mailboxes;

        /// <summary>
        /// 初始化投递器。
        /// </summary>
        public CellMailboxDispatcher(IReadOnlyDictionary<string, RoleMailbox> mailboxes)
        {
            _mailboxes = mailboxes;
        }

        /// <inheritdoc />
        public Task DispatchAsync(MessageEnvelope envelope, CancellationToken cancellationToken = default)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }

            if (!_mailboxes.TryGetValue(envelope.TargetRole, out var mailbox))
            {
                throw new RouteNotFoundException(envelope.TargetRole, $"No mailbox for target role '{envelope.TargetRole}' in this cell.");
            }

            return mailbox.DeliverAsync(envelope, cancellationToken);
        }
    }

    /// <summary>
    /// 拓扑内全部 cell。
    /// </summary>
    private readonly List<TopologyCell> _cells = new List<TopologyCell>();

    /// <summary>
    /// Role 名 → 承载 cell。
    /// </summary>
    private readonly Dictionary<string, TopologyCell> _cellByRole = new Dictionary<string, TopologyCell>();

    /// <summary>
    /// Role 名 → 邮箱。
    /// </summary>
    private readonly Dictionary<string, RoleMailbox> _mailboxByRole = new Dictionary<string, RoleMailbox>();

    /// <summary>
    /// 全部 Role 名（固定顺序）。
    /// </summary>
    private readonly List<string> _allRoleNames;

    /// <summary>
    /// 每一跳投递分支记录（含处理器内应答跳）。
    /// </summary>
    private readonly ConcurrentQueue<RoleRouteDelivery> _deliveries = new ConcurrentQueue<RoleRouteDelivery>();

    /// <summary>
    /// Role 业务状态存储（确定性快照用）。
    /// </summary>
    private readonly ConcurrentDictionary<string, string> _roleState = new ConcurrentDictionary<string, string>();

    /// <summary>
    /// 初始化拓扑世界。
    /// </summary>
    /// <param name="cellRoleGroups">每个 cell 承载的 Role 名分组 / The role name group of every cell</param>
    private TopologyWorld(IEnumerable<IReadOnlyList<string>> cellRoleGroups)
    {
        var allRoleNames = new List<string>();
        var cellRoutersByRolePlaceholder = new Dictionary<string, IRoleRouter>();
        LoopbackRemoteRoleRouter loopbackForwarder = new LoopbackRemoteRoleRouter(cellRoutersByRolePlaceholder);

        foreach (var roleGroup in cellRoleGroups)
        {
            var cell = new TopologyCell();
            cell.RoleNames.AddRange(roleGroup);
            foreach (var roleName in roleGroup)
            {
                var mailbox = new RoleMailbox(roleName);
                cell.Mailboxes[roleName] = mailbox;
                _mailboxByRole[roleName] = mailbox;
                _cellByRole[roleName] = cell;
                allRoleNames.Add(roleName);
            }

            var dispatcher = new CellMailboxDispatcher(cell.Mailboxes);
            cell.Router = new InProcessRoleRouter(cell.RoleNames, dispatcher, loopbackForwarder);
            _cells.Add(cell);
            foreach (var roleName in roleGroup)
            {
                cellRoutersByRolePlaceholder[roleName] = cell.Router;
            }
        }

        _allRoleNames = allRoleNames;
    }

    /// <summary>
    /// 创建 All-in-One 拓扑：单 cell 承载全部 Role，每一跳都必须命中 D3 case 1。
    /// </summary>
    /// <param name="roleNames">全部 Role 名 / All role names</param>
    /// <returns>拓扑世界 / The topology world</returns>
    public static TopologyWorld CreateAllInOne(IReadOnlyList<string> roleNames)
    {
        return new TopologyWorld(new List<IReadOnlyList<string>> { roleNames });
    }

    /// <summary>
    /// 创建三进程拓扑（设计源 §D9）：Gate 进程 / Game+Social 进程 / Match 进程。
    /// </summary>
    /// <param name="gateRoleName">Gate Role 名 / The Gate role name</param>
    /// <param name="gameRoleName">Game Role 名 / The Game role name</param>
    /// <param name="socialRoleName">Social Role 名 / The Social role name</param>
    /// <param name="matchRoleName">Match Role 名 / The Match role name</param>
    /// <returns>拓扑世界 / The topology world</returns>
    public static TopologyWorld CreateMultiProcess(string gateRoleName, string gameRoleName, string socialRoleName, string matchRoleName)
    {
        var cellRoleGroups = new List<IReadOnlyList<string>>
        {
            new List<string> { gateRoleName },
            new List<string> { gameRoleName, socialRoleName },
            new List<string> { matchRoleName },
        };
        return new TopologyWorld(cellRoleGroups);
    }

    /// <summary>
    /// 获取指定 Role 的邮箱。
    /// </summary>
    /// <param name="roleName">Role 名 / The role name</param>
    public RoleMailbox GetMailbox(string roleName)
    {
        return _mailboxByRole[roleName];
    }

    /// <summary>
    /// 从源 Role 向目标 Role 路由一封消息（带跳超时），并记录投递分支。
    /// </summary>
    /// <remarks>
    /// Routes one message hop from the source role's cell router — the routing decision
    /// belongs to the sending process (D3), so the source role selects the cell.
    /// The hop timeout is applied through a cancellation token, identically in both topologies.
    /// </remarks>
    /// <param name="sourceRoleName">源 Role 名 / The source role name</param>
    /// <param name="targetRoleName">目标 Role 名 / The target role name</param>
    /// <param name="message">消息 / The message</param>
    /// <param name="hopTimeoutMilliseconds">跳超时（毫秒）/ The hop timeout in milliseconds</param>
    /// <returns>投递分支 / The delivery branch</returns>
    public async Task<RoleRouteDelivery> RouteAsync(string sourceRoleName, string targetRoleName, MessageObject message, int hopTimeoutMilliseconds)
    {
        var envelope = new MessageEnvelope(targetRoleName, message);
        using (var cancellationSource = new CancellationTokenSource(hopTimeoutMilliseconds))
        {
            var delivery = await _cellByRole[sourceRoleName].Router.RouteAsync(envelope, cancellationSource.Token);
            _deliveries.Enqueue(delivery);
            return delivery;
        }
    }

    /// <summary>
    /// 写入一条 Role 业务状态（确定性快照的组成项）。
    /// </summary>
    /// <param name="roleName">Role 名 / The role name</param>
    /// <param name="stateKey">状态键 / The state key</param>
    /// <param name="stateValue">状态值 / The state value</param>
    public void SetRoleState(string roleName, string stateKey, string stateValue)
    {
        _roleState[$"{roleName}|{stateKey}"] = stateValue;
    }

    /// <summary>
    /// 捕获链路执行结果快照。
    /// </summary>
    /// <returns>链路执行结果 / The chain execution result</returns>
    public ChainExecutionResult CaptureResult()
    {
        var arrivalOrderByRole = new Dictionary<string, IReadOnlyList<string>>();
        foreach (var roleName in _allRoleNames)
        {
            arrivalOrderByRole[roleName] = _mailboxByRole[roleName].GetArrivalLog();
        }

        var stateSnapshot = string.Join(
            ";",
            _roleState.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={pair.Value}"));

        return new ChainExecutionResult
        {
            Deliveries = _deliveries.ToList(),
            ArrivalOrderByRole = arrivalOrderByRole,
            StateSnapshot = stateSnapshot,
        };
    }

    /// <summary>
    /// 关闭全部邮箱。
    /// </summary>
    public void Dispose()
    {
        foreach (var cell in _cells)
        {
            foreach (var mailbox in cell.Mailboxes.Values)
            {
                mailbox.Close();
            }
        }
    }
}
