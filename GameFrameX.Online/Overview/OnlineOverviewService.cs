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

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Online.Contracts;
using GameFrameX.Online.Match;
using GameFrameX.Online.Matchmaking;
using GameFrameX.Online.Party;
using GameFrameX.Online.Presence;
using GameFrameX.Online.Scope;
using GameFrameX.Online.Session;

namespace GameFrameX.Online.Overview;

/// <summary>
/// 在线总览只读查询服务（vault:C9 S8.1：Admin 在线总览的服务端半边）。
/// <para>
/// 维护约束（红线）：
/// ① **只读**——本服务只依赖各域存储的读取方法（对齐 C97 <see cref="OnlineMatchQueueObserver"/>「只依赖存储的读取方法」先例），
/// 不持有任何写入型服务，调用本服务不会产生任何状态副作用或事件；
/// ② 数据源唯一 = Online 玩家侧存储，**不含管理员连接**（vault:C9 X6）；
/// ③ 所有计数按作用域三键 (TenantId, AppId, ServerId) 过滤，跨作用域读数与不存在同构（反预言）。
/// </para>
/// <para>
/// 统计口径（集中定义，禁止在别处重复定义）：
/// <list type="bullet">
/// <item><description>在线玩家数：作用域内 Presence 记录数，<see cref="OnlinePresenceState.Blocked"/> 不计入（沿用 C94 <c>OnlinePresenceService.CountOnlineAsync</c> 的既有 Admin 查询口径）。</description></item>
/// <item><description>会话数：作用域内**非终态**会话数（终态 = 已关闭/被踢/过期，见 <see cref="OnlineSessionStateExtensions.IsTerminal"/>）。</description></item>
/// <item><description>重连率：重连中会话数 ÷ 非终态会话数；分母为 0 时取 0（不做补 1 平滑，消费方按需自行处理）。</description></item>
/// <item><description>队伍数：作用域内**非终态**队伍数（终态判定复用 <see cref="OnlinePartyStateMachine.IsTerminal"/>）。</description></item>
/// <item><description>对局数：作用域内**非 <see cref="OnlineMatchState.Closed"/>** 对局数（Closed = 已释放，C98 口径下运行时随即删档；读取面仍显式排除，使该计数不依赖写入侧时序，VC-8.1-a）。</description></item>
/// <item><description>队列深度：作用域内排队态票据数。**匹配池在 C97 口径下为 App 级**，本快照报告该池在当前 Server 上的分区——各 Server 深度之和等于 App 级排队总数，不得把本值当作跨服共享的池大小。</description></item>
/// <item><description>平均等待：排队态票据等待时长的算术平均（等待时长复用 <see cref="OnlineMatchRule.WaitSeconds"/>，与匹配域同源）；无排队票据时为 0。</description></item>
/// <item><description>吞吐：最近 60 秒内（<c>(观测时刻 - 60000, 观测时刻]</c>，左开右闭——恰好落在窗口起点的分配不计入，避免把「上一轮窗口的边界」重复计入相邻两次查询）落档的匹配分配所消费的票据数——口径是「每分钟达成匹配的票据数」，不是对局数（一张票据可携带多人）。</description></item>
/// </list>
/// </para>
/// <para>
/// 天花板（ponytail）：单次查询全量拉取各域作用域内数据后现场聚合，规模到十万级应改为各域增量计数；
/// 当前实现保证口径简单可核对（分布与总数同源同一次读取）。
/// </para>
/// </summary>
public sealed class OnlineOverviewService
{
    /// <summary>吞吐滑动窗口长度（毫秒）。</summary>
    private const long ThroughputWindowMilliseconds = 60000L;

    /// <summary>在线状态存储。</summary>
    private readonly IOnlinePresenceStore _presenceStore;

    /// <summary>会话存储。</summary>
    private readonly IOnlineSessionStore _sessionStore;

    /// <summary>队伍存储。</summary>
    private readonly IOnlinePartyStore _partyStore;

    /// <summary>匹配票据存储（队列概况数据源）。</summary>
    private readonly IOnlineMatchTicketStore _ticketStore;

    /// <summary>对局存储。</summary>
    private readonly IOnlineMatchActorStore _matchStore;

    /// <summary>
    /// 初始化 <see cref="OnlineOverviewService"/>。
    /// </summary>
    /// <param name="presenceStore">在线状态存储（在线玩家数数据源）。</param>
    /// <param name="sessionStore">会话存储（会话数与重连率数据源）。</param>
    /// <param name="partyStore">队伍存储（队伍数数据源）。</param>
    /// <param name="ticketStore">匹配票据存储（队列概况数据源）。</param>
    /// <param name="matchStore">对局存储（对局数数据源）。</param>
    public OnlineOverviewService(
        IOnlinePresenceStore presenceStore,
        IOnlineSessionStore sessionStore,
        IOnlinePartyStore partyStore,
        IOnlineMatchTicketStore ticketStore,
        IOnlineMatchActorStore matchStore)
    {
        _presenceStore = presenceStore ?? throw new ArgumentNullException(nameof(presenceStore));
        _sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
        _partyStore = partyStore ?? throw new ArgumentNullException(nameof(partyStore));
        _ticketStore = ticketStore ?? throw new ArgumentNullException(nameof(ticketStore));
        _matchStore = matchStore ?? throw new ArgumentNullException(nameof(matchStore));
    }

    /// <summary>
    /// 观测指定作用域的在线总览快照。
    /// </summary>
    /// <param name="scope">生效作用域（取 TenantId / AppId / ServerId 三键过滤；不含玩家主体位）。</param>
    /// <param name="nowUnixMilliseconds">观测时刻（UTC 毫秒；传 0 取系统时钟，测试可注入）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>在线总览快照；作用域三键非法时返回 <see cref="OnlineErrorCode.ParameterInvalid"/>。</returns>
    public async Task<OnlineResult<OnlineOverviewSnapshot>> ReviewAsync(OnlineScope scope, long nowUnixMilliseconds = 0, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope<OnlineOverviewSnapshot>(scope);
        if (failure != null)
        {
            return failure;
        }

        var now = nowUnixMilliseconds > 0 ? nowUnixMilliseconds : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        var snapshot = new OnlineOverviewSnapshot
        {
            TenantId = scope.TenantId,
            AppId = scope.AppId,
            ServerId = scope.ServerId,
            ObservedTime = now,
        };

        await FillPresenceAsync(snapshot, scope, cancellationToken).ConfigureAwait(false);
        await FillSessionAsync(snapshot, scope, cancellationToken).ConfigureAwait(false);
        await FillPartyAsync(snapshot, scope, cancellationToken).ConfigureAwait(false);
        await FillMatchAsync(snapshot, scope, cancellationToken).ConfigureAwait(false);
        await FillQueueAsync(snapshot, scope, now, cancellationToken).ConfigureAwait(false);

        return OnlineResult<OnlineOverviewSnapshot>.Ok(snapshot);
    }

    /// <summary>
    /// 填充在线玩家数（Presence 记录数，Blocked 不计入）。
    /// </summary>
    /// <param name="snapshot">目标快照。</param>
    /// <param name="scope">生效作用域。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    private async Task FillPresenceAsync(OnlineOverviewSnapshot snapshot, OnlineScope scope, CancellationToken cancellationToken)
    {
        var records = await _presenceStore.ListByAppAsync(scope.TenantId, scope.AppId, cancellationToken).ConfigureAwait(false);
        var count = 0;
        if (records != null)
        {
            foreach (var record in records)
            {
                if (record == null || record.ServerId != scope.ServerId || record.State == OnlinePresenceState.Blocked)
                {
                    continue;
                }

                count++;
            }
        }

        snapshot.OnlinePlayerCount = count;
    }

    /// <summary>
    /// 填充会话数与状态分布，并据此推导重连率。
    /// </summary>
    /// <param name="snapshot">目标快照。</param>
    /// <param name="scope">生效作用域。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    private async Task FillSessionAsync(OnlineOverviewSnapshot snapshot, OnlineScope scope, CancellationToken cancellationToken)
    {
        var sessions = await _sessionStore.ListNonTerminalAsync(cancellationToken).ConfigureAwait(false);
        var counts = new SortedDictionary<OnlineSessionState, int>();
        var total = 0;
        var reconnecting = 0;

        if (sessions != null)
        {
            foreach (var session in sessions)
            {
                if (session == null || !IsInScope(session.TenantId, session.AppId, session.ServerId, scope))
                {
                    continue;
                }

                total++;
                Increment(counts, session.State);
                if (session.State == OnlineSessionState.Reconnecting)
                {
                    reconnecting++;
                }
            }
        }

        snapshot.SessionCount = total;
        snapshot.SessionStateCounts = counts;
        snapshot.ReconnectRate = total == 0 ? 0d : (double)reconnecting / total;
    }

    /// <summary>
    /// 填充非终态队伍数与状态分布。
    /// </summary>
    /// <param name="snapshot">目标快照。</param>
    /// <param name="scope">生效作用域。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    private async Task FillPartyAsync(OnlineOverviewSnapshot snapshot, OnlineScope scope, CancellationToken cancellationToken)
    {
        var parties = await _partyStore.ListPartiesAsync(scope.TenantId, scope.AppId, cancellationToken).ConfigureAwait(false);
        var counts = new SortedDictionary<OnlinePartyState, int>();
        var total = 0;

        if (parties != null)
        {
            foreach (var party in parties)
            {
                if (party == null || party.ServerId != scope.ServerId || OnlinePartyStateMachine.IsTerminal(party.State))
                {
                    continue;
                }

                total++;
                Increment(counts, party.State);
            }
        }

        snapshot.PartyCount = total;
        snapshot.PartyStateCounts = counts;
    }

    /// <summary>
    /// 填充存活对局数与状态分布（<see cref="OnlineMatchState.Closed"/> 即已释放，不计入，VC-8.1-a）。
    /// </summary>
    /// <param name="snapshot">目标快照。</param>
    /// <param name="scope">生效作用域。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    private async Task FillMatchAsync(OnlineOverviewSnapshot snapshot, OnlineScope scope, CancellationToken cancellationToken)
    {
        var matches = await _matchStore.ListAsync(scope.TenantId, scope.AppId, cancellationToken).ConfigureAwait(false);
        var counts = new SortedDictionary<OnlineMatchState, int>();
        var total = 0;

        if (matches != null)
        {
            foreach (var match in matches)
            {
                if (match == null || match.ServerId != scope.ServerId || match.State == OnlineMatchState.Closed)
                {
                    continue;
                }

                total++;
                Increment(counts, match.State);
            }
        }

        snapshot.MatchCount = total;
        snapshot.MatchStateCounts = counts;
    }

    /// <summary>
    /// 填充按「玩法模式 + 区域」聚合的队列概况。
    /// </summary>
    /// <param name="snapshot">目标快照。</param>
    /// <param name="scope">生效作用域。</param>
    /// <param name="now">观测时刻（UTC 毫秒）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>异步任务。</returns>
    private async Task FillQueueAsync(OnlineOverviewSnapshot snapshot, OnlineScope scope, long now, CancellationToken cancellationToken)
    {
        var tickets = await _ticketStore.ListAllAsync(scope.TenantId, scope.AppId, cancellationToken).ConfigureAwait(false);
        var accumulator = new Dictionary<string, QueueAccumulator>();
        AccumulateQueueDepth(tickets, scope, accumulator, now);

        var assignments = await _ticketStore.ListAssignmentsAsync(scope.TenantId, scope.AppId, cancellationToken).ConfigureAwait(false);
        AccumulateThroughput(assignments, scope, accumulator, now);

        var summaries = new List<OnlineOverviewQueueSummary>(accumulator.Count);
        foreach (var pair in accumulator.Values)
        {
            summaries.Add(new OnlineOverviewQueueSummary
            {
                Mode = pair.Mode,
                Region = pair.Region,
                QueueDepth = pair.Depth,
                AverageWaitSeconds = pair.Depth == 0 ? 0 : pair.TotalWaitSeconds / pair.Depth,
                ThroughputPerMinute = pair.Throughput,
            });
        }

        summaries.Sort(CompareQueueSummaries);
        snapshot.QueueSummaries = summaries;
    }

    /// <summary>
    /// 累加队列深度与等待时长（排队态票据按 (Mode, Region) 槽位聚合）。
    /// </summary>
    /// <param name="tickets">全量票据列表（可为 <see langword="null"/>，此时不累加）。</param>
    /// <param name="scope">生效作用域。</param>
    /// <param name="accumulator">聚合表（按 (Mode, Region) 取或建槽位）。</param>
    /// <param name="now">观测时刻（UTC 毫秒）。</param>
    private static void AccumulateQueueDepth(IReadOnlyList<OnlineMatchTicket> tickets, OnlineScope scope, Dictionary<string, QueueAccumulator> accumulator, long now)
    {
        if (tickets != null)
        {
            foreach (var ticket in tickets)
            {
                if (ticket == null || ticket.ServerId != scope.ServerId || ticket.State != OnlineMatchTicketState.Queued)
                {
                    continue;
                }

                var entry = Resolve(accumulator, ticket.Mode, ticket.Region);
                entry.Depth++;
                entry.TotalWaitSeconds += OnlineMatchRule.WaitSeconds(ticket, now);
            }
        }
    }

    /// <summary>
    /// 累加吞吐（最近一分钟窗口 <c>(now - 60000, now]</c> 内落档的匹配分配所消费的票据数，按 (Mode, Region) 槽位聚合）。
    /// </summary>
    /// <param name="assignments">全量分配列表（可为 <see langword="null"/>，此时不累加）。</param>
    /// <param name="scope">生效作用域。</param>
    /// <param name="accumulator">聚合表（按 (Mode, Region) 取或建槽位）。</param>
    /// <param name="now">观测时刻（UTC 毫秒）。</param>
    private static void AccumulateThroughput(IReadOnlyList<OnlineMatchAssignment> assignments, OnlineScope scope, Dictionary<string, QueueAccumulator> accumulator, long now)
    {
        var windowStart = now - ThroughputWindowMilliseconds;
        if (assignments != null)
        {
            foreach (var assignment in assignments)
            {
                if (assignment == null || assignment.ServerId != scope.ServerId || assignment.CreatedAtTime <= windowStart || assignment.CreatedAtTime > now)
                {
                    continue;
                }

                var entry = Resolve(accumulator, assignment.Mode, assignment.Region);
                entry.Throughput += assignment.TicketIds == null ? 0 : assignment.TicketIds.Count;
            }
        }
    }

    /// <summary>
    /// 判断实体作用域是否落在观测作用域内。
    /// </summary>
    /// <param name="tenantId">实体租户标识。</param>
    /// <param name="appId">实体 App 标识。</param>
    /// <param name="serverId">实体区服标识。</param>
    /// <param name="scope">观测作用域。</param>
    /// <returns>在作用域内返回 <c>true</c>。</returns>
    private static bool IsInScope(long tenantId, long appId, long serverId, OnlineScope scope)
    {
        return tenantId == scope.TenantId && appId == scope.AppId && serverId == scope.ServerId;
    }

    /// <summary>
    /// 按 (Mode, Region) 取或建聚合槽位。
    /// </summary>
    /// <param name="accumulator">聚合表。</param>
    /// <param name="mode">玩法模式。</param>
    /// <param name="region">区域。</param>
    /// <returns>该分组的聚合槽位。</returns>
    private static QueueAccumulator Resolve(Dictionary<string, QueueAccumulator> accumulator, int mode, int region)
    {
        var key = mode + ":" + region;
        if (!accumulator.TryGetValue(key, out var entry))
        {
            entry = new QueueAccumulator { Mode = mode, Region = region };
            accumulator[key] = entry;
        }

        return entry;
    }

    /// <summary>
    /// 累加状态分布计数。
    /// </summary>
    /// <typeparam name="TState">状态类型。</typeparam>
    /// <param name="counts">分布表。</param>
    /// <param name="state">状态。</param>
    private static void Increment<TState>(SortedDictionary<TState, int> counts, TState state)
    {
        counts.TryGetValue(state, out var current);
        counts[state] = current + 1;
    }

    /// <summary>
    /// 队列概况排序（Mode 升序，同 Mode 按 Region 升序）。
    /// </summary>
    /// <param name="left">左值。</param>
    /// <param name="right">右值。</param>
    /// <returns>比较结果。</returns>
    private static int CompareQueueSummaries(OnlineOverviewQueueSummary left, OnlineOverviewQueueSummary right)
    {
        var byMode = left.Mode.CompareTo(right.Mode);
        return byMode != 0 ? byMode : left.Region.CompareTo(right.Region);
    }

    /// <summary>
    /// 校验作用域三键（总览不面向单个玩家，故不要求玩家主体位）。
    /// </summary>
    /// <typeparam name="TData">结果负载类型。</typeparam>
    /// <param name="scope">生效作用域。</param>
    /// <returns>非法时返回失败结果，合法返回 <see langword="null"/>。</returns>
    private static OnlineResult<TData> ValidateScope<TData>(OnlineScope scope)
    {
        if (scope == null)
        {
            throw new ArgumentNullException(nameof(scope));
        }

        if (scope.TenantId <= 0 || scope.AppId <= 0 || scope.ServerId <= 0)
        {
            return OnlineResult<TData>.Fail(OnlineErrorCode.ParameterInvalid, "在线总览查询必须携带完整作用域三键（TenantId / AppId / ServerId）");
        }

        return null;
    }

    /// <summary>
    /// 队列聚合槽位（按 Mode+Region 一组的累加器）。
    /// </summary>
    private sealed class QueueAccumulator
    {
        /// <summary>获取或设置玩法模式。</summary>
        public int Mode
        {
            get;
            set;
        }

        /// <summary>获取或设置区域。</summary>
        public int Region
        {
            get;
            set;
        }

        /// <summary>获取或设置排队态票据数。</summary>
        public int Depth
        {
            get;
            set;
        }

        /// <summary>获取或设置排队态票据等待时长合计（秒）。</summary>
        public int TotalWaitSeconds
        {
            get;
            set;
        }

        /// <summary>获取或设置最近一分钟达成匹配的票据数。</summary>
        public int Throughput
        {
            get;
            set;
        }
    }
}
