//  ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
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
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Online.Matchmaking;

using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Online.Contracts;

/// <summary>
/// 匹配队列观测器（vault:C5 S4.10 / VC-4.10：Admin 可观测覆盖率 100%）。
/// <para>
/// 维护约束（红线）：观测器**只读**——不得借助观测路径顺手改写票据或分配状态，
/// 否则「观测一致性」与「写入一致性」会互相污染，排查时无法分辨看到的是原始事实还是观测副作用。
/// 因此本类只依赖存储的读取方法。
/// </para>
/// <para>
/// 天花板（ponytail）：单次观测全量拉取票据与分配，队列规模上万后应改为增量计数与分页明细；
/// 当前实现保证口径简单可核对（计数与明细同源同一次读取）。
/// </para>
/// </summary>
public sealed class OnlineMatchQueueObserver
{
    /// <summary>票据存储。</summary>
    private readonly IOnlineMatchTicketStore _store;

    /// <summary>
    /// 初始化 <see cref="OnlineMatchQueueObserver"/>。
    /// </summary>
    /// <param name="store">票据存储。</param>
    public OnlineMatchQueueObserver(IOnlineMatchTicketStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <summary>
    /// 观测指定作用域的匹配队列。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒；传 0 取系统时钟）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>队列观测快照。</returns>
    public async Task<OnlineResult<OnlineMatchQueueSnapshot>> ObserveAsync(long tenantId, long appId, long nowUnixMilliseconds = 0, CancellationToken cancellationToken = default)
    {
        var now = nowUnixMilliseconds > 0 ? nowUnixMilliseconds : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var tickets = await _store.ListAllAsync(tenantId, appId, cancellationToken).ConfigureAwait(false);
        var assignments = await _store.ListAssignmentsAsync(tenantId, appId, cancellationToken).ConfigureAwait(false);

        var snapshot = new OnlineMatchQueueSnapshot
        {
            TenantId = tenantId,
            AppId = appId,
            ObservedAtTime = now,
            AssignmentCount = assignments.Count,
            Tickets = new List<OnlineMatchTicketSummary>(tickets.Count),
        };

        var queuedPlayers = new HashSet<long>();
        var matchedPlayers = new HashSet<long>();

        foreach (var ticket in tickets)
        {
            var playerCount = ticket.PlayerIds == null ? 0 : ticket.PlayerIds.Count;
            switch (ticket.State)
            {
                case OnlineMatchTicketState.Queued:
                    snapshot.QueuedTicketCount++;
                    AddPlayers(queuedPlayers, ticket);
                    break;
                case OnlineMatchTicketState.Matched:
                    snapshot.MatchedTicketCount++;
                    AddPlayers(matchedPlayers, ticket);
                    break;
                case OnlineMatchTicketState.Cancelled:
                    snapshot.CancelledTicketCount++;
                    break;
                case OnlineMatchTicketState.Expired:
                    snapshot.ExpiredTicketCount++;
                    break;
                case OnlineMatchTicketState.Failed:
                    snapshot.FailedTicketCount++;
                    break;
                default:
                    snapshot.FailedTicketCount++;
                    break;
            }

            snapshot.Tickets.Add(new OnlineMatchTicketSummary
            {
                TicketId = ticket.TicketId ?? string.Empty,
                PartyId = ticket.PartyId ?? string.Empty,
                PlayerCount = playerCount,
                Mode = ticket.Mode,
                Region = ticket.Region,
                TeamSize = ticket.TeamSize,
                State = ticket.State,
                WaitSeconds = OnlineMatchRule.WaitSeconds(ticket, now),
                AssignmentId = ticket.AssignmentId ?? string.Empty,
            });
        }

        snapshot.QueuedPlayerCount = queuedPlayers.Count;
        snapshot.MatchedPlayerCount = matchedPlayers.Count;
        snapshot.DuplicateAssignmentTicketCount = CountDuplicateAssignmentTickets(assignments);
        snapshot.Tickets.Sort((left, right) => string.CompareOrdinal(left.TicketId, right.TicketId));

        return OnlineResult<OnlineMatchQueueSnapshot>.Ok(snapshot);
    }

    /// <summary>
    /// 统计被多份分配同时消费的票据数（VC-4.12 红指标）。
    /// </summary>
    /// <param name="assignments">分配集合。</param>
    /// <returns>重复消费的票据数（正常为 0）。</returns>
    private static int CountDuplicateAssignmentTickets(IReadOnlyList<OnlineMatchAssignment> assignments)
    {
        var seen = new HashSet<string>();
        var duplicated = new HashSet<string>();

        foreach (var assignment in assignments)
        {
            if (assignment.TicketIds == null)
            {
                continue;
            }

            foreach (var ticketId in assignment.TicketIds)
            {
                if (!seen.Add(ticketId))
                {
                    duplicated.Add(ticketId);
                }
            }
        }

        return duplicated.Count;
    }

    /// <summary>
    /// 把票据携带的玩家并入统计集合。
    /// </summary>
    /// <param name="players">玩家集合。</param>
    /// <param name="ticket">票据。</param>
    private static void AddPlayers(HashSet<long> players, OnlineMatchTicket ticket)
    {
        if (ticket.PlayerIds == null)
        {
            return;
        }

        foreach (var playerId in ticket.PlayerIds)
        {
            players.Add(playerId);
        }
    }
}
