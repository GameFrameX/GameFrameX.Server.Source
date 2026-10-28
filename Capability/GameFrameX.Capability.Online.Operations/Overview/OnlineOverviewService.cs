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
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Scope;
using GameFrameX.Capability.Online.Match;
using GameFrameX.Capability.Online.Matchmaking;
using GameFrameX.Capability.Online.Matchmaking.Party;
using GameFrameX.Capability.Online.Foundation.Presence;
using GameFrameX.Capability.Online.Foundation.Session;

namespace GameFrameX.Capability.Online.Operations.Overview;

/// <summary>
/// 在线总览只读查询服务（Admin 在线总览的服务端半边）。
/// <para>
/// 维护约束（红线）：
/// ① **只读**——本服务只依赖各域存储的读取方法（对齐 <see cref="OnlineMatchQueueObserver"/>「只依赖存储的读取方法」先例），
/// 不持有任何写入型服务，调用本服务不会产生任何状态副作用或事件；
/// ② 数据源唯一 = Online 玩家侧存储，**不含管理员连接**；
/// ③ 所有计数按作用域三键 (TenantId, AppId, ServerId) 过滤，跨作用域读数与不存在同构（反预言）。
/// </para>
/// <para>
/// 统计口径（集中定义，禁止在别处重复定义；**谓词经存储聚合方法下推执行**——各存储接口的聚合读方法以本口径为契约语义，
/// 存储实现负责在存储层完成过滤与聚合，不向本服务返回全量行）：
/// <list type="bullet">
/// <item><description>在线玩家数：作用域内 Presence 记录数，<see cref="OnlinePresenceState.Blocked"/> 不计入（沿用 <c>OnlinePresenceService.CountOnlineAsync</c> 的既有 Admin 查询口径）。</description></item>
/// <item><description>会话数：作用域内**非终态**会话数（终态 = 已关闭/被踢/过期，见 <see cref="OnlineSessionStateExtensions.IsTerminal"/>）。</description></item>
/// <item><description>重连率：重连中会话数 ÷ 非终态会话数；分母为 0 时取 0（不做补 1 平滑，消费方按需自行处理）。</description></item>
/// <item><description>队伍数：作用域内**非终态**队伍数（终态判定复用 <see cref="OnlinePartyStateMachine.IsTerminal"/>）。</description></item>
/// <item><description>对局数：作用域内**非 <see cref="OnlineMatchState.Closed"/>** 对局数（Closed = 已释放，按运行时口径随即删档；读取面仍显式排除，使该计数不依赖写入侧时序）。</description></item>
/// <item><description>队列深度：作用域内排队态票据数。**匹配池为 App 级**，本快照报告该池在当前 Server 上的分区——各 Server 深度之和等于 App 级排队总数，不得把本值当作跨服共享的池大小。</description></item>
/// <item><description>平均等待：排队态票据等待时长的算术平均（等待时长复用 <see cref="OnlineMatchRule.WaitSeconds"/>，与匹配域同源；无排队票据时为 0）。</description></item>
/// <item><description>吞吐：最近 60 秒内（<c>(观测时刻 - 60000, 观测时刻]</c>，左开右闭——恰好落在窗口起点的分配不计入，避免把「上一轮窗口的边界」重复计入相邻两次查询）落档的匹配分配所消费的票据数——口径是「每分钟达成匹配的票据数」，不是对局数（一张票据可携带多人）。</description></item>
/// </list>
/// </para>
/// <para>
/// 性能形态：六路存储聚合读（5 域计数 + 吞吐窗口聚合）经 <c>Task.WhenAll</c> 并发执行，总延迟取最大者而非求和；
/// 各路只返回计数/聚合行，不返回实体行，内存占用与在线规模解耦。
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
    /// 观测指定作用域的在线总览快照（六路存储聚合读并发执行）。
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
        var windowStart = now - ThroughputWindowMilliseconds;

        var presenceCountTask = _presenceStore.CountOnlineByServerAsync(scope.TenantId, scope.AppId, scope.ServerId, cancellationToken);
        var sessionCountsTask = _sessionStore.CountNonTerminalByScopeAsync(scope.TenantId, scope.AppId, scope.ServerId, cancellationToken);
        var partyCountsTask = _partyStore.CountNonTerminalByScopeAsync(scope.TenantId, scope.AppId, scope.ServerId, cancellationToken);
        var matchCountsTask = _matchStore.CountActiveByScopeAsync(scope.TenantId, scope.AppId, scope.ServerId, cancellationToken);
        var queueDepthTask = _ticketStore.AggregateQueuedBySlotAsync(scope.TenantId, scope.AppId, scope.ServerId, now, cancellationToken);
        var throughputTask = _ticketStore.CountAssignmentTicketsBySlotAsync(scope.TenantId, scope.AppId, scope.ServerId, windowStart, now, cancellationToken);

        await Task.WhenAll(presenceCountTask, sessionCountsTask, partyCountsTask, matchCountsTask, queueDepthTask, throughputTask).ConfigureAwait(false);

        var snapshot = new OnlineOverviewSnapshot
        {
            TenantId = scope.TenantId,
            AppId = scope.AppId,
            ServerId = scope.ServerId,
            ObservedTime = now,
            OnlinePlayerCount = presenceCountTask.Result,
            SessionStateCounts = sessionCountsTask.Result,
            PartyCount = SumStateCounts(partyCountsTask.Result),
            PartyStateCounts = partyCountsTask.Result,
            MatchCount = SumStateCounts(matchCountsTask.Result),
            MatchStateCounts = matchCountsTask.Result,
        };

        FillSessionDerivedCounts(snapshot, sessionCountsTask.Result);
        snapshot.QueueSummaries = MergeQueueSummaries(queueDepthTask.Result, throughputTask.Result);

        return OnlineResult<OnlineOverviewSnapshot>.Ok(snapshot);
    }

    /// <summary>
    /// 汇总状态分布计数为总数。
    /// </summary>
    /// <typeparam name="TState">状态类型。</typeparam>
    /// <param name="stateCounts">状态分布（存储聚合结果）。</param>
    /// <returns>各状态计数之和。</returns>
    private static int SumStateCounts<TState>(IReadOnlyDictionary<TState, int> stateCounts)
    {
        var total = 0;
        if (stateCounts != null)
        {
            foreach (var pair in stateCounts)
            {
                total += pair.Value;
            }
        }

        return total;
    }

    /// <summary>
    /// 由会话状态分布推导会话数与重连率。
    /// </summary>
    /// <param name="snapshot">目标快照。</param>
    /// <param name="sessionStateCounts">会话状态分布（存储聚合结果）。</param>
    private static void FillSessionDerivedCounts(OnlineOverviewSnapshot snapshot, IReadOnlyDictionary<OnlineSessionState, int> sessionStateCounts)
    {
        var total = 0;
        sessionStateCounts.TryGetValue(OnlineSessionState.Reconnecting, out var reconnecting);
        foreach (var pair in sessionStateCounts)
        {
            total += pair.Value;
        }

        snapshot.SessionCount = total;
        snapshot.ReconnectRate = total == 0 ? 0d : (double)reconnecting / total;
    }

    /// <summary>
    /// 合并队列深度聚合与吞吐聚合，产出排序后的队列概况。
    /// </summary>
    /// <param name="queueDepthSlots">排队态票据槽位聚合（深度 + 等待秒合计）。</param>
    /// <param name="throughputSlots">吞吐窗口槽位聚合（消费票据数）。</param>
    /// <returns>按 Mode 升序、同 Mode 按 Region 升序的队列概况列表。</returns>
    private static List<OnlineOverviewQueueSummary> MergeQueueSummaries(IReadOnlyList<OnlineMatchQueueSlotAggregate> queueDepthSlots, IReadOnlyList<OnlineMatchQueueSlotAggregate> throughputSlots)
    {
        var merged = new Dictionary<string, OnlineOverviewQueueSummary>();
        if (queueDepthSlots != null)
        {
            foreach (var slot in queueDepthSlots)
            {
                if (slot == null)
                {
                    continue;
                }

                var summary = ResolveSummary(merged, slot.Mode, slot.Region);
                summary.QueueDepth = slot.TicketCount;
                summary.AverageWaitSeconds = slot.TicketCount == 0 ? 0 : slot.TotalWaitSeconds / slot.TicketCount;
            }
        }

        if (throughputSlots != null)
        {
            foreach (var slot in throughputSlots)
            {
                if (slot == null)
                {
                    continue;
                }

                ResolveSummary(merged, slot.Mode, slot.Region).ThroughputPerMinute = slot.TicketCount;
            }
        }

        var summaries = new List<OnlineOverviewQueueSummary>(merged.Values);
        summaries.Sort(CompareQueueSummaries);
        return summaries;
    }

    /// <summary>
    /// 按 (Mode, Region) 取或建队列概况槽位。
    /// </summary>
    /// <param name="merged">合并表。</param>
    /// <param name="mode">玩法模式。</param>
    /// <param name="region">区域。</param>
    /// <returns>该分组的队列概况。</returns>
    private static OnlineOverviewQueueSummary ResolveSummary(Dictionary<string, OnlineOverviewQueueSummary> merged, int mode, int region)
    {
        var key = mode + ":" + region;
        if (!merged.TryGetValue(key, out var summary))
        {
            summary = new OnlineOverviewQueueSummary { Mode = mode, Region = region, };
            merged[key] = summary;
        }

        return summary;
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
}
