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
using GameFrameX.Online.Events;

/// <summary>
/// 匹配协调器（vault:C5 S4.6：按模式/区域/规模/等级区间成组，产出 assignment）。
/// <para>
/// 维护约束（红线）：
/// ① 成组是**整票进整票出**——票据携带的所有成员要么一起进入对局，要么一起留在队列
/// （VC-4.3 队伍完整性）；因此累加候选时若加上一张票据会超过目标规模，就跳过该票据而不是拆开它。
/// ② 落档走 <see cref="IOnlineMatchTicketStore.CommitMatchAsync"/> 的原子提交：任何一张票据在提交瞬间
/// 已不在排队态（被取消/过期/上一轮消费），整组不生效且**不留半成品**，下一轮重新收敛（VC-4.2/VC-4.12）。
/// ③ FIFO 优先：按入队时刻升序挑选基准票据，等待最久者先成组（VC-4.5 等待时间扩展的公平性前提）。
/// </para>
/// <para>
/// 天花板（ponytail）：单轮 O(n²) 扫描（每张基准票据线性找同伴），队列规模上千后需换分桶索引
/// （按 Mode/Region/TeamSize 预分组）；当前量级下正确性优先。
/// </para>
/// </summary>
public sealed class OnlineMatchmakerCoordinator
{
    /// <summary>票据存储（原子提交入口）。</summary>
    private readonly IOnlineMatchTicketStore _store;

    /// <summary>票据服务（过期扫描与状态裁决）。</summary>
    private readonly OnlineMatchTicketService _ticketService;

    /// <summary>匹配规则（配置驱动的可匹配判定）。</summary>
    private readonly OnlineMatchRule _rule;

    /// <summary>事件发布器。</summary>
    private readonly IOnlineEventPublisher _eventPublisher;

    /// <summary>
    /// 初始化 <see cref="OnlineMatchmakerCoordinator"/>。
    /// </summary>
    /// <param name="store">票据存储。</param>
    /// <param name="ticketService">票据服务。</param>
    /// <param name="eventPublisher">事件发布器。</param>
    /// <param name="options">匹配与限流可配置项（null 取默认值）。</param>
    public OnlineMatchmakerCoordinator(IOnlineMatchTicketStore store, OnlineMatchTicketService ticketService, IOnlineEventPublisher eventPublisher, OnlineMatchmakerOptions options = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _ticketService = ticketService ?? throw new ArgumentNullException(nameof(ticketService));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        _rule = new OnlineMatchRule(options == null ? new OnlineMatchmakerOptions() : options);
    }

    /// <summary>
    /// 执行一轮匹配：先扫描过期，再按 FIFO 逐张基准票据尝试成组并原子落档。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒；传 0 取系统时钟，测试可注入）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>本轮执行结果。</returns>
    public async Task<OnlineResult<OnlineMatchmakingRunResult>> RunOnceAsync(long tenantId, long appId, long nowUnixMilliseconds = 0, CancellationToken cancellationToken = default)
    {
        var now = nowUnixMilliseconds > 0 ? nowUnixMilliseconds : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var sweep = await _ticketService.SweepExpiredAsync(tenantId, appId, now, cancellationToken).ConfigureAwait(false);
        var result = new OnlineMatchmakingRunResult
        {
            TenantId = tenantId,
            AppId = appId,
            ExpiredTicketCount = sweep.Data,
            AssignmentIds = new List<string>(),
        };

        var queued = await _store.ListQueuedAsync(tenantId, appId, cancellationToken).ConfigureAwait(false);
        result.ScannedTicketCount = queued.Count;

        var ordered = SortByArrival(queued);
        var consumed = new HashSet<string>();

        foreach (var anchor in ordered)
        {
            if (consumed.Contains(anchor.TicketId))
            {
                continue;
            }

            var group = SelectGroup(anchor, ordered, consumed, now);
            if (group == null)
            {
                continue;
            }

            var committed = await CommitAsync(anchor, group, now, cancellationToken).ConfigureAwait(false);
            if (committed == null)
            {
                continue;
            }

            foreach (var ticket in group)
            {
                consumed.Add(ticket.TicketId);
            }

            result.AssignmentIds.Add(committed.AssignmentId);
            result.MatchedTicketCount += group.Count;
            result.MatchedPlayerCount += committed.PlayerIds.Count;
        }

        return OnlineResult<OnlineMatchmakingRunResult>.Ok(result);
    }

    /// <summary>
    /// 为一组票据落档并发布事件；竞态失败返回 null（整组不生效）。
    /// </summary>
    /// <param name="anchor">基准票据（决定模式、区域、规模与区服）。</param>
    /// <param name="group">已选定的成组票据。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>落档后的分配；未落档返回 null。</returns>
    private async Task<OnlineMatchAssignment> CommitAsync(OnlineMatchTicket anchor, List<OnlineMatchTicket> group, long nowUnixMilliseconds, CancellationToken cancellationToken)
    {
        var ticketIds = new List<string>(group.Count);
        var players = new SortedSet<long>();
        foreach (var ticket in group)
        {
            ticketIds.Add(ticket.TicketId);
            foreach (var playerId in ticket.PlayerIds)
            {
                players.Add(playerId);
            }
        }

        var assignment = new OnlineMatchAssignment
        {
            AssignmentId = "asg-" + Guid.NewGuid().ToString("N"),
            MatchId = "match-" + Guid.NewGuid().ToString("N"),
            PlayerIds = new List<long>(players),
            Mode = anchor.Mode,
            Region = anchor.Region,
            RuleSnapshot = BuildSnapshot(group, nowUnixMilliseconds),
            CreatedAtTime = nowUnixMilliseconds,
            TicketIds = ticketIds,
            TenantId = anchor.TenantId,
            AppId = anchor.AppId,
            ServerId = anchor.ServerId,
        };

        var committed = await _store.CommitMatchAsync(assignment, ticketIds, OnlineMatchTicketState.Queued, cancellationToken).ConfigureAwait(false);
        if (committed == null)
        {
            return null;
        }

        await _eventPublisher.PublishAsync(OnlineMatchEvents.CreateAssignmentCreated(committed), cancellationToken).ConfigureAwait(false);

        foreach (var ticketId in ticketIds)
        {
            var matched = await _store.FindAsync(committed.TenantId, committed.AppId, ticketId, cancellationToken).ConfigureAwait(false);
            if (matched != null)
            {
                await _eventPublisher.PublishAsync(OnlineMatchEvents.CreateTicketChanged(matched, OnlineMatchTicketState.Queued, OnlineMatchTicketState.Matched), cancellationToken).ConfigureAwait(false);
            }
        }

        return committed;
    }

    /// <summary>
    /// 以基准票据为中心挑选成组票据（FIFO 顺序、整票累加、不拆队）。
    /// </summary>
    /// <param name="anchor">基准票据。</param>
    /// <param name="ordered">按入队顺序排列的候选票据。</param>
    /// <param name="consumed">本轮已消费的票据标识（不得再次成组）。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒）。</param>
    /// <returns>凑满目标规模的票据组；不足返回 null。</returns>
    private List<OnlineMatchTicket> SelectGroup(OnlineMatchTicket anchor, List<OnlineMatchTicket> ordered, HashSet<string> consumed, long nowUnixMilliseconds)
    {
        if (anchor.TeamSize <= 0 || consumed.Contains(anchor.TicketId))
        {
            return null;
        }

        var players = new HashSet<long>();
        if (anchor.PlayerIds == null || anchor.PlayerIds.Count > anchor.TeamSize)
        {
            return null;
        }

        foreach (var playerId in anchor.PlayerIds)
        {
            players.Add(playerId);
        }

        var group = new List<OnlineMatchTicket> { anchor };
        if (players.Count == anchor.TeamSize)
        {
            return group;
        }

        foreach (var candidate in ordered)
        {
            if (candidate.TicketId == anchor.TicketId || consumed.Contains(candidate.TicketId))
            {
                continue;
            }

            if (candidate.PlayerIds == null)
            {
                continue;
            }

            if (players.Count + candidate.PlayerIds.Count > anchor.TeamSize)
            {
                continue;
            }

            if (ContainsAny(players, candidate.PlayerIds))
            {
                continue;
            }

            if (!_rule.CanGroup(anchor, candidate, nowUnixMilliseconds))
            {
                continue;
            }

            group.Add(candidate);
            foreach (var playerId in candidate.PlayerIds)
            {
                players.Add(playerId);
            }

            if (players.Count == anchor.TeamSize)
            {
                return group;
            }
        }

        return null;
    }

    /// <summary>
    /// 生成成组时的规则快照（记录本次实际生效的区间与等待时长，VC-4.5 / VC-4.13）。
    /// </summary>
    /// <param name="group">成组票据。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒）。</param>
    /// <returns>规则快照。</returns>
    private OnlineMatchRuleSnapshot BuildSnapshot(List<OnlineMatchTicket> group, long nowUnixMilliseconds)
    {
        var anchor = group[0];
        var snapshot = new OnlineMatchRuleSnapshot
        {
            Mode = anchor.Mode,
            Region = anchor.Region,
            TeamSize = anchor.TeamSize,
            SkillMin = int.MaxValue,
            SkillMax = int.MinValue,
            WaitSeconds = 0,
            SkillRangeExpanded = false,
        };

        foreach (var ticket in group)
        {
            var effective = _rule.EffectiveSkillRange(ticket, nowUnixMilliseconds);
            if (effective != null)
            {
                snapshot.SkillMin = Math.Min(snapshot.SkillMin, effective.Min);
                snapshot.SkillMax = Math.Max(snapshot.SkillMax, effective.Max);
                if (ticket.SkillRange == null || effective.Min < ticket.SkillRange.Min || effective.Max > ticket.SkillRange.Max)
                {
                    snapshot.SkillRangeExpanded = true;
                }
            }

            snapshot.WaitSeconds = Math.Max(snapshot.WaitSeconds, OnlineMatchRule.WaitSeconds(ticket, nowUnixMilliseconds));
        }

        if (snapshot.SkillMin == int.MaxValue)
        {
            snapshot.SkillMin = 0;
            snapshot.SkillMax = 0;
        }

        return snapshot;
    }

    /// <summary>
    /// 按入队时刻升序排列（同刻用票据标识兜底，保证同输入同输出）。
    /// </summary>
    /// <param name="tickets">票据集合。</param>
    /// <returns>排序后的新列表。</returns>
    private static List<OnlineMatchTicket> SortByArrival(IReadOnlyList<OnlineMatchTicket> tickets)
    {
        var ordered = new List<OnlineMatchTicket>(tickets);
        ordered.Sort((left, right) =>
        {
            var byTime = left.CreatedAtTime.CompareTo(right.CreatedAtTime);
            if (byTime != 0)
            {
                return byTime;
            }

            return string.CompareOrdinal(left.TicketId ?? string.Empty, right.TicketId ?? string.Empty);
        });
        return ordered;
    }

    /// <summary>
    /// 判断玩家集合与票据成员是否存在交集。
    /// </summary>
    /// <param name="players">已累加玩家集合。</param>
    /// <param name="candidatePlayerIds">候选票据成员。</param>
    /// <returns>存在交集返回 <c>true</c>。</returns>
    private static bool ContainsAny(HashSet<long> players, List<long> candidatePlayerIds)
    {
        foreach (var playerId in candidatePlayerIds)
        {
            if (players.Contains(playerId))
            {
                return true;
            }
        }

        return false;
    }
}
