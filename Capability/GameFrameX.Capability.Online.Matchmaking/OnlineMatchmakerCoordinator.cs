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

namespace GameFrameX.Capability.Online.Matchmaking;

using System;
using System.Collections.Generic;
using System.Threading;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Events;

/// <summary>
/// 匹配协调器（按模式/区域/规模/等级区间成组，产出 assignment）。
/// <para>
/// 维护约束（红线）：
/// ① 成组是**整票进整票出**——票据携带的所有成员要么一起进入对局，要么一起留在队列
/// （队伍完整性要求）；因此累加候选时若加上一张票据会超过目标规模，就跳过该票据而不是拆开它。
/// ② 落档走 <see cref="IOnlineMatchTicketStore.CommitMatchAsync"/> 的原子提交：任何一张票据在提交瞬间
/// 已不在排队态（被取消/过期/上一轮消费），整组不生效且**不留半成品**，下一轮重新收敛。
/// ③ FIFO 优先：按入队时刻升序挑选基准票据，等待最久者先成组（等待时间扩展的公平性前提）。
/// </para>
/// <para>
/// 天花板：单轮 O(n²) 扫描（每张基准票据线性找同伴），队列规模上千后需换分桶索引
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

    /// <summary>跨域社交裁决（可空：null = 不启用屏蔽/处罚裁决，既有行为不变）。</summary>
    private readonly IOnlineSocialGate _socialGate;

    /// <summary>匹配事件发件箱（可空：null = 无补偿直发，既有行为不变；C374）。</summary>
    private readonly IOnlineMatchEventOutbox _eventOutbox;

    /// <summary>
    /// 初始化 <see cref="OnlineMatchmakerCoordinator"/>。
    /// </summary>
    /// <param name="store">票据存储。</param>
    /// <param name="ticketService">票据服务。</param>
    /// <param name="eventPublisher">事件发布器。</param>
    /// <param name="options">匹配与限流可配置项（必填；机制合并后的 Options 终值，不再兜底默认）。</param>
    /// <param name="socialGate">跨域社交裁决入口（可空；由 <c>OnlineSocialDecisionService</c> 提供）。</param>
    public OnlineMatchmakerCoordinator(IOnlineMatchTicketStore store, OnlineMatchTicketService ticketService, IOnlineEventPublisher eventPublisher, OnlineMatchmakerOptions options, IOnlineSocialGate socialGate = null, IOnlineMatchEventOutbox eventOutbox = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _ticketService = ticketService ?? throw new ArgumentNullException(nameof(ticketService));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        _socialGate = socialGate;
        _eventOutbox = eventOutbox;
        _rule = new OnlineMatchRule(options ?? throw new ArgumentNullException(nameof(options)));
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

        // C374 补偿：本轮撮合前先重投上一轮发布失败的待投递事件（至少一次投递；无发件箱时跳过）。
        if (_eventOutbox != null)
        {
            await DeliverPendingEventsAsync(tenantId, appId, cancellationToken).ConfigureAwait(false);
        }

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

            var group = await SelectGroupAsync(anchor, ordered, consumed, now, tenantId, appId, cancellationToken).ConfigureAwait(false);
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
        CollectTicketIdsAndPlayers(group, ticketIds, players);

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

        await PublishCommittedEventsAsync(committed, ticketIds, cancellationToken).ConfigureAwait(false);

        return committed;
    }

    /// <summary>
    /// 累加成组票据的标识与成员（成员去重进有序集合，保证分配的玩家列表稳定有序）。
    /// </summary>
    /// <param name="group">成组票据。</param>
    /// <param name="ticketIds">票据标识输出（按组内顺序累加）。</param>
    /// <param name="players">玩家集合输出（去重累加）。</param>
    private static void CollectTicketIdsAndPlayers(List<OnlineMatchTicket> group, List<string> ticketIds, SortedSet<long> players)
    {
        foreach (var ticket in group)
        {
            ticketIds.Add(ticket.TicketId);
            foreach (var playerId in ticket.PlayerIds)
            {
                players.Add(playerId);
            }
        }
    }

    /// <summary>
    /// 为已落档的分配构建事件序列（assignment-created 先行；逐票据匹配态变更按提交票据序
    /// <see cref="IOnlineMatchTicketStore.FindAsync"/> 查得才建，竞态下票据缺失静默跳过该张；
    /// 空安全实参与既有直发口径逐字一致）。
    /// </summary>
    /// <param name="committed">已落档的分配。</param>
    /// <param name="ticketIds">本组票据标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>按投递顺序排列的事件列表。</returns>
    private async Task<List<OnlineEvent>> BuildMatchEventsAsync(OnlineMatchAssignment committed, List<string> ticketIds, CancellationToken cancellationToken)
    {
        var events = new List<OnlineEvent>
        {
            OnlineMatchEvents.CreateAssignmentCreated(committed.TenantId, committed.AppId, committed.ServerId, committed.AssignmentId, committed.MatchId, committed.Mode, committed.Region, committed.PlayerIds == null ? 0 : committed.PlayerIds.Count, committed.PlayerIds != null && committed.PlayerIds.Count > 0 ? committed.PlayerIds[0] : 0, committed.TicketIds),
        };

        foreach (var ticketId in ticketIds)
        {
            var matched = await _store.FindAsync(committed.TenantId, committed.AppId, ticketId, cancellationToken).ConfigureAwait(false);
            if (matched != null)
            {
                events.Add(OnlineMatchEvents.CreateTicketChanged(matched.TenantId, matched.AppId, matched.ServerId, matched.TicketId, matched.PartyId, matched.FailureReason.ToString(), matched.AssignmentId, matched.PlayerIds == null ? 0 : matched.PlayerIds.Count, matched.PlayerIds != null && matched.PlayerIds.Count > 0 ? matched.PlayerIds[0] : 0, OnlineMatchTicketState.Queued.ToString(), OnlineMatchTicketState.Matched.ToString()));
            }
        }

        return events;
    }

    /// <summary>
    /// 发布已落档分配的事件（C374 补偿：发件箱在场时先整批写入、再逐条尽力投递，
    /// 发布失败条目保留待投递、由下一轮 <see cref="RunOnceAsync"/> 起点重投，形成至少一次投递——
    /// 落档结果不受发布失败影响；发件箱缺席时保持既有直发行为，失败即抛出）。
    /// </summary>
    /// <param name="committed">已落档的分配。</param>
    /// <param name="ticketIds">本组票据标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>完成通知。</returns>
    private async Task PublishCommittedEventsAsync(OnlineMatchAssignment committed, List<string> ticketIds, CancellationToken cancellationToken)
    {
        var events = await BuildMatchEventsAsync(committed, ticketIds, cancellationToken).ConfigureAwait(false);

        if (_eventOutbox == null)
        {
            foreach (var onlineEvent in events)
            {
                await _eventPublisher.PublishAsync(onlineEvent, cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        await _eventOutbox.AppendAsync(events, cancellationToken).ConfigureAwait(false);
        await DeliverPendingEventsAsync(committed.TenantId, committed.AppId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 重投作用域内全部待投递事件：逐条发布、成功即标记已投递；单条失败吞没异常并保留条目，
    /// 交由后续轮次继续重投（异常不外溢——撮合与重投互不拖垮）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>完成通知。</returns>
    private async Task DeliverPendingEventsAsync(long tenantId, long appId, CancellationToken cancellationToken)
    {
        var pending = await _eventOutbox.ListPendingAsync(tenantId, appId, cancellationToken).ConfigureAwait(false);

        foreach (var entry in pending)
        {
            try
            {
                await _eventPublisher.PublishAsync(entry.Event, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception)
            {
                continue;
            }

            await _eventOutbox.MarkDeliveredAsync(tenantId, appId, entry.EventId, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 以基准票据为中心挑选成组票据（FIFO 顺序、整票累加、不拆队）。
    /// <para>
    /// 维护约束（社交裁决）：候选票据与**已入组的每一名成员**两两走
    /// <see cref="IOnlineSocialGate"/> 裁决（屏蔽双向生效、封禁拒绝），任一命中即**跳过该候选票据**——
    /// 跳过而不是拆开它，是「整票进整票出」红线在社交裁决下的延续：
    /// 拆票会让同一队伍的成员被分进不同对局，代价远大于多等一轮。
    /// 裁决对每一对玩家**双向各问一次**：屏蔽在裁决内部本就是对双向查的，但处罚（禁言 / 封禁）
    /// 是绑定在发起方身上的单向事实——只问一个方向会让「被处罚者恰好在候选票据里」逃过裁决，
    /// 而本处没有「谁是发起方」的概念，双方都是被动入组。约定措辞是「任一方向被 Block / 被处罚」。
    /// 天花板：两两裁决是 O(组规模 × 候选规模 × 2) 次判定，队规模上调后需换成
    /// 「先取成员屏蔽 / 处罚集合再求交」的批量形态。
    /// </para>
    /// </summary>
    /// <param name="anchor">基准票据。</param>
    /// <param name="ordered">按入队顺序排列的候选票据。</param>
    /// <param name="consumed">本轮已消费的票据标识（不得再次成组）。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒）。</param>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>凑满目标规模的票据组；不足返回 null。</returns>
    private async Task<List<OnlineMatchTicket>> SelectGroupAsync(OnlineMatchTicket anchor, List<OnlineMatchTicket> ordered, HashSet<string> consumed, long nowUnixMilliseconds, long tenantId, long appId, CancellationToken cancellationToken)
    {
        if (anchor.TeamSize <= 0 || consumed.Contains(anchor.TicketId))
        {
            return null;
        }

        var players = BuildAnchorPlayerSet(anchor);
        if (players == null)
        {
            return null;
        }

        var group = new List<OnlineMatchTicket> { anchor, };
        if (players.Count == anchor.TeamSize)
        {
            return group;
        }

        foreach (var candidate in ordered)
        {
            if (!await CanAddToGroupAsync(anchor, candidate, players, consumed, nowUnixMilliseconds, tenantId, appId, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            group.Add(candidate);
            players.UnionWith(candidate.PlayerIds);

            if (players.Count == anchor.TeamSize)
            {
                return group;
            }
        }

        return null;
    }

    /// <summary>
    /// 构造基准票据的初始玩家集合（整票累加的起点）。
    /// </summary>
    /// <param name="anchor">基准票据。</param>
    /// <returns>去重后的玩家集合；成员为空或超过目标规模（整票进组必超员）返回 <c>null</c>。</returns>
    private static HashSet<long> BuildAnchorPlayerSet(OnlineMatchTicket anchor)
    {
        if (anchor.PlayerIds == null || anchor.PlayerIds.Count > anchor.TeamSize)
        {
            return null;
        }

        return new HashSet<long>(anchor.PlayerIds);
    }

    /// <summary>
    /// 判断候选票据能否累加进当前组（六项裁决依次进行：自票据/已消费 → 空成员 → 超规模 → 成员重叠 → 规则 → 社交）。
    /// <para>
    /// 求值顺序与既有逐条 <c>continue</c> 顺序一致：廉价同步判定在前、社交裁决（异步）最后，
    /// 避免无谓的跨域裁决调用；任一命中即整体不中——跳过候选而不是拆开它（整票进整票出红线的延续）。
    /// </para>
    /// </summary>
    /// <param name="anchor">基准票据（决定目标规模与规则判定基准）。</param>
    /// <param name="candidate">候选票据。</param>
    /// <param name="players">已累加玩家集合。</param>
    /// <param name="consumed">本轮已消费的票据标识。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒）。</param>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>可累加返回 <c>true</c>。</returns>
    private async Task<bool> CanAddToGroupAsync(OnlineMatchTicket anchor, OnlineMatchTicket candidate, HashSet<long> players, HashSet<string> consumed, long nowUnixMilliseconds, long tenantId, long appId, CancellationToken cancellationToken)
    {
        if (candidate.TicketId == anchor.TicketId || consumed.Contains(candidate.TicketId))
        {
            return false;
        }

        if (candidate.PlayerIds == null)
        {
            return false;
        }

        if (players.Count + candidate.PlayerIds.Count > anchor.TeamSize)
        {
            return false;
        }

        if (ContainsAny(players, candidate.PlayerIds))
        {
            return false;
        }

        if (!_rule.CanGroup(anchor, candidate, nowUnixMilliseconds))
        {
            return false;
        }

        return await IsSociallyCompatibleAsync(players, candidate, tenantId, appId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 生成成组时的规则快照（记录本次实际生效的区间与等待时长）。
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

        var hasSkillRange = false;
        foreach (var ticket in group)
        {
            var effective = _rule.EffectiveSkillRange(ticket, nowUnixMilliseconds);
            if (effective != null)
            {
                hasSkillRange = true;
                snapshot.SkillMin = Math.Min(snapshot.SkillMin, effective.Min);
                snapshot.SkillMax = Math.Max(snapshot.SkillMax, effective.Max);
                if (ticket.SkillRange == null || effective.Min < ticket.SkillRange.Min || effective.Max > ticket.SkillRange.Max)
                {
                    snapshot.SkillRangeExpanded = true;
                }
            }

            snapshot.WaitSeconds = Math.Max(snapshot.WaitSeconds, OnlineMatchRule.WaitSeconds(ticket, nowUnixMilliseconds));
        }

        // 可达分支（非死代码）：SkillRange 允许为空入队（无技能约束匹配），且满员基准票据
        // （如 TeamSize=1 单人票或满员队伍票）在 SelectGroupAsync 中直接整票成组、不经过
        // CanGroup 的区间非空门——此时组内所有 EffectiveSkillRange 均为 null，
        // 快照以 0/0 记录「全组无技能区间」的合法语义。
        if (!hasSkillRange)
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
    /// 判断候选票据能否与本组共处一局（pairwise 社交裁决：屏蔽双向生效、封禁拒绝）。
    /// <para>
    /// 未注入 <see cref="IOnlineSocialGate"/>（<c>null</c>）时一律放行，保持既有行为不变。
    /// 组内为空（白板基准票据）时无需裁决——没有对手方就没有社交冲突。
    /// </para>
    /// </summary>
    /// <param name="players">已累加玩家集合。</param>
    /// <param name="candidate">候选票据。</param>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>可共处一局返回 <c>true</c>。</returns>
    private async Task<bool> IsSociallyCompatibleAsync(HashSet<long> players, OnlineMatchTicket candidate, long tenantId, long appId, CancellationToken cancellationToken)
    {
        if (_socialGate == null || players.Count == 0)
        {
            return true;
        }

        foreach (var existingPlayerId in players)
        {
            foreach (var candidatePlayerId in candidate.PlayerIds)
            {
                var forward = await _socialGate.EvaluateAsync(tenantId, appId, existingPlayerId, candidatePlayerId, OnlineSocialInteractionPurpose.Matchmaking, cancellationToken).ConfigureAwait(false);
                if (!forward.Allowed)
                {
                    return false;
                }

                var backward = await _socialGate.EvaluateAsync(tenantId, appId, candidatePlayerId, existingPlayerId, OnlineSocialInteractionPurpose.Matchmaking, cancellationToken).ConfigureAwait(false);
                if (!backward.Allowed)
                {
                    return false;
                }
            }
        }

        return true;
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