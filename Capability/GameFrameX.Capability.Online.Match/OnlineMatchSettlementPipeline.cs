// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and other rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the PRC and applicable international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please see the LICENSE file at the source code root for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   侵犯其他个人或组织合法权益等法律法规禁止的行为！
// ==========================================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.Abstractions.Contracts;

namespace GameFrameX.Capability.Online.Match;

/// <summary>
/// 对局结算管线：Tick 驱动的「结算 → 待发奖 outbox 扫描 → 领取投递 → CAS 写回」生产编排。
/// <para>
/// 维护约束（红线）：① Tick 互斥（并发双 Tick 串行安全）；② 不宣称「恰好一次」——结算幂等由
/// <see cref="OnlineMatchSettlementService"/>（首次结果获胜）、发奖幂等由
/// <see cref="OnlineMatchResultDispatcher"/> 的 <c>{MatchResultId}-{PlayerId}</c> 键保证；
/// 管线只做 outbox 编排，崩溃恢复依赖持久化待发奖记录（租约到期接管 + 幂等键兜底）；
/// ③ 结算回放（<c>IsReplay</c>）不重复走结算路径，但 outbox 扫描与回放解耦——
/// 已落库未投完的记录由扫描继续驱动，不再因回放被跳过（GFX-1116 缺陷根因之一）；
/// ④ 投递作用域 = 当前 Pending/Failed 玩家集合（已成功玩家零重投）；⑤ 投递轮次受
/// <see cref="OnlineMatchSettlementOptions.MaxGrantAttempts"/> 上限约束，达上限未成功玩家转
/// <see cref="OnlineMatchRewardDeliveryState.ManualReview"/> 冻结自动重投（经
/// <see cref="QueryStatusAsync"/> 暴露为人工裁决面）；⑥ 每对局 / 每记录独立异常隔离：
/// 单条异常记入结果但不得中断同轮其他对局或记录的处理。
/// </para>
/// <para>
/// 分发器可空：assets 能力被过滤（模块未装配 <c>OnlineGrantService</c>）时仅结算、不发奖、
/// 不建待发奖记录——与 Competition 域 season/tournament 的可空发放依赖同一模块化等价口径。
/// </para>
/// </summary>
/// <remarks>
/// The settlement pipeline: the tick-driven "settle → scan the reward outbox → claim &amp; dispatch →
/// CAS write-back" production orchestration. Ticks are mutually exclusive; no exactly-once claim is made —
/// settlement idempotency comes from the settlement service (first result wins) and grant idempotency from
/// the dispatcher's <c>{MatchResultId}-{PlayerId}</c> keys, while crash recovery relies on the durable
/// reward-outbox records (lease expiry take-over plus the idempotency-key backstop). Settlement replays do
/// not re-enter the settle path, yet outbox scanning is decoupled from replays: committed-but-undelivered
/// records keep being driven by the scan instead of being skipped on replay (one of the GFX-1116 root causes).
/// The dispatch scope is the current Pending/Failed player set only (already-granted players are never
/// re-dispatched); the attempt count is capped, at which point undelivered players freeze into ManualReview
/// (exposed via <see cref="QueryStatusAsync"/> as the manual-adjudication face). Per-match and per-record
/// failures are isolated: one failure never aborts the rest of the round. A null dispatcher means the assets
/// capability was filtered out: settle only, no grants, no outbox records.
/// </remarks>
public sealed class OnlineMatchSettlementPipeline
{
    /// <summary>对局运行时（结算 tick 的枚举源）。</summary>
    private readonly OnlineMatchRuntime _runtime;

    /// <summary>结算服务（幂等；结果与待发奖记录原子提交）。</summary>
    private readonly OnlineMatchSettlementService _settlementService;

    /// <summary>结果分发器（可空 = assets 能力缺席，仅结算不发奖）。</summary>
    private readonly OnlineMatchResultDispatcher _dispatcher;

    /// <summary>结算存储（待发奖 outbox 的扫描 / 领取 / 写回 / 观测面）。</summary>
    private readonly IOnlineMatchSettlementStore _settlementStore;

    /// <summary>Tick 互斥门（并发双 Tick 串行安全）。</summary>
    private readonly SemaphoreSlim _tickGate = new(1, 1);

    /// <summary>发奖投递尝试上限（含首发；非正值回退默认）。</summary>
    private readonly int _maxGrantAttempts;

    /// <summary>发奖投递租约时长毫秒（非正值回退默认）。</summary>
    private readonly int _claimLeaseMilliseconds;

    /// <summary>单轮待发奖扫描批量上限（非正值回退默认）。</summary>
    private readonly int _outboxScanBatchSize;

    /// <summary>
    /// 初始化结算管线。
    /// </summary>
    /// <param name="runtime">对局运行时。</param>
    /// <param name="settlementService">结算服务。</param>
    /// <param name="settlementStore">结算存储（结果 + 待发奖 outbox）。</param>
    /// <param name="dispatcher">结果分发器（可空：assets 能力缺席时仅结算不发奖不建待发奖记录）。</param>
    /// <param name="options">结算编排选项（可空 = 默认值）。</param>
    public OnlineMatchSettlementPipeline(OnlineMatchRuntime runtime, OnlineMatchSettlementService settlementService, IOnlineMatchSettlementStore settlementStore, OnlineMatchResultDispatcher dispatcher = null, OnlineMatchSettlementOptions options = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _settlementService = settlementService ?? throw new ArgumentNullException(nameof(settlementService));
        _settlementStore = settlementStore ?? throw new ArgumentNullException(nameof(settlementStore));
        _dispatcher = dispatcher;
        var settlementOptions = options ?? new OnlineMatchSettlementOptions();
        _maxGrantAttempts = settlementOptions.MaxGrantAttempts > 0 ? settlementOptions.MaxGrantAttempts : OnlineMatchSettlementOptions.DefaultMaxGrantAttempts;
        _claimLeaseMilliseconds = settlementOptions.GrantClaimLeaseMilliseconds > 0 ? settlementOptions.GrantClaimLeaseMilliseconds : OnlineMatchSettlementOptions.DefaultGrantClaimLeaseMilliseconds;
        _outboxScanBatchSize = settlementOptions.OutboxScanBatchSize > 0 ? settlementOptions.OutboxScanBatchSize : OnlineMatchSettlementOptions.DefaultOutboxScanBatchSize;
    }

    /// <summary>被异常隔离的结算轮次累计（可观测面；对局保持 Settling 由下一轮重试）。</summary>
    private long _settlementFailureCount;

    /// <summary>被异常隔离的发奖投递轮次累计（可观测面；记录由租约到期恢复重投）。</summary>
    private long _dispatchFailureCount;

    /// <summary>
    /// 推进一轮结算编排：结算全部 <see cref="OnlineMatchState.Settling"/> 对局（结果与待发奖记录原子落库）→
    /// 扫描到期待发奖记录 → 逐记录领取（CAS）→ 投递 Pending/Failed 玩家 → 按回执写回（CAS）。
    /// </summary>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>完成通知。</returns>
    public async Task TickAsync(long nowUnixMilliseconds, CancellationToken cancellationToken = default)
    {
        await _tickGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var actor in _runtime.SnapshotActors())
            {
                if (actor.State != OnlineMatchState.Settling)
                {
                    continue;
                }

                // 逐对局异常隔离（GFX-1116）：单对局结算异常不得中断同轮其他对局。
                try
                {
                    await _settlementService.SettleAsync(actor.TenantId, actor.AppId, actor.MatchId, nowUnixMilliseconds, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception)
                {
                    OnSettlementFailed(actor.MatchId);
                }
            }

            if (_dispatcher == null)
            {
                return;
            }

            // outbox 扫描驱动全部投递（含结算回放遗留与崩溃恢复；租约保证多实例不双投）。
            var dueRecords = await _settlementStore.FindDueAsync(nowUnixMilliseconds, _outboxScanBatchSize, cancellationToken).ConfigureAwait(false);
            foreach (var dueRecord in dueRecords)
            {
                // 逐记录异常隔离：坏记录不得中断同轮其他记录的投递。
                try
                {
                    await DispatchOutboxRecordAsync(dueRecord, nowUnixMilliseconds, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception dispatchException)
                {
                    OnDispatchFailed(dueRecord.MatchId, dueRecord.MatchResultId);
                }
            }
        }
        finally
        {
            _tickGate.Release();
        }
    }

    /// <summary>
    /// 查询编排状态（admin 可观测面：结算中对局清单 + 未完成待发奖条目投影）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>结算中对局标识清单 + 待重试发奖条目副本。</returns>
    public async Task<(IReadOnlyList<string> SettlingMatchIds, IReadOnlyList<OnlineMatchSettlementRetryEntry> Retries, long SettlementFailureCount, long DispatchFailureCount)> QueryStatusAsync(CancellationToken cancellationToken = default)
    {
        var settling = new List<string>();
        foreach (var actor in _runtime.SnapshotActors())
        {
            if (actor.State == OnlineMatchState.Settling)
            {
                settling.Add(actor.MatchId);
            }
        }

        var retries = await _settlementStore.ListUndeliveredAsync(cancellationToken).ConfigureAwait(false);
        return (settling, retries, Interlocked.Read(ref _settlementFailureCount), Interlocked.Read(ref _dispatchFailureCount));
    }

    /// <summary>
    /// 投递一条到期待发奖记录：CAS 领取 → 按 Pending/Failed 玩家收窄投递 → 按回执更新玩家状态 →
    /// 达上限玩家转 ManualReview → CAS 写回（版本冲突 = 他人已推进，放弃本次结果）。
    /// </summary>
    /// <param name="dueRecord">扫描到的到期记录副本。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>完成通知。</returns>
    private async Task DispatchOutboxRecordAsync(OnlineMatchRewardOutboxRecord dueRecord, long nowUnixMilliseconds, CancellationToken cancellationToken)
    {
        var claimed = await _settlementStore.TryClaimAsync(dueRecord.MatchResultId, dueRecord.RowVersion, nowUnixMilliseconds, _claimLeaseMilliseconds, cancellationToken).ConfigureAwait(false);
        if (!claimed)
        {
            // 版本不符或租约被持：他人已接管或已推进，本实例放弃。
            return;
        }

        var working = dueRecord.Copy();
        working.RowVersion = dueRecord.RowVersion + 1;
        working.Attempts++;

        var dispatched = await _dispatcher.DispatchAsync(NarrowToUndeliveredPlayers(working), cancellationToken).ConfigureAwait(false);
        ApplyDispatchOutcome(working, dispatched);
        if (working.Attempts >= _maxGrantAttempts)
        {
            FreezeRemainingPlayers(working);
        }

        // 写回释放租约（ClaimExpires 归零）；CAS 失败 = 他人在租约过期后接管推进，放弃本次结果
        //（重投安全性由发奖幂等键保证：接管者重投同一玩家不会重复入账）。
        working.ClaimExpiresAtUnixMilliseconds = 0;
        working.NextAttemptTimeUnixMilliseconds = nowUnixMilliseconds;
        await _settlementStore.TryWriteBackAsync(working, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 将待发奖结果快照收窄为仅含未完成玩家（Pending/Failed/ManualReview 之外无条目）条目的副本；
    /// 已成功玩家零重投——整份重投在幂等 TTL 过期后会重复发奖。
    /// </summary>
    /// <param name="record">待投递的待发奖记录。</param>
    /// <returns>仅含未完成玩家条目的结果副本；无未完成条目时返回 null。</returns>
    private static OnlineMatchResult NarrowToUndeliveredPlayers(OnlineMatchRewardOutboxRecord record)
    {
        var targetPlayerIds = new HashSet<long>();
        foreach (var entry in record.PlayerEntries)
        {
            if (entry != null && entry.State != OnlineMatchRewardDeliveryState.Succeeded)
            {
                targetPlayerIds.Add(entry.PlayerId);
            }
        }

        if (record.Result == null || record.Result.Entries == null || targetPlayerIds.Count == 0)
        {
            return null;
        }

        var narrowed = record.Result.Copy();
        narrowed.Entries = new List<OnlineMatchResultEntry>();
        foreach (var entry in record.Result.Entries)
        {
            if (entry != null && targetPlayerIds.Contains(entry.PlayerId))
            {
                narrowed.Entries.Add(entry);
            }
        }

        return narrowed.Entries.Count == 0 ? null : narrowed;
    }

    /// <summary>
    /// 按分发回执刷新逐玩家投递状态：回执成功玩家置 Succeeded、失败玩家置 Failed；
    /// 分发整体失败（异常由调用方隔离；此处为返回失败结果）时本次投递作用域全部置 Failed——
    /// 作用域已收窄为未完成玩家，不波及已成功玩家。
    /// </summary>
    /// <param name="working">领取后的工作副本（原地更新玩家状态）。</param>
    /// <param name="dispatched">分发回执（可为整体失败）。</param>
    private static void ApplyDispatchOutcome(OnlineMatchRewardOutboxRecord working, OnlineResult<OnlineMatchDispatchOutcome> dispatched)
    {
        var failedPlayerIds = dispatched != null && dispatched.IsSuccess && dispatched.Data != null
            ? new HashSet<long>(dispatched.Data.FailedPlayerIds ?? new List<long>())
            : null;

        foreach (var entry in working.PlayerEntries)
        {
            if (entry == null || entry.State == OnlineMatchRewardDeliveryState.Succeeded || entry.State == OnlineMatchRewardDeliveryState.ManualReview)
            {
                continue;
            }

            // 整体失败（failedPlayerIds == null）= 本次作用域全部失败；否则按回执失败集合判定。
            var succeeded = failedPlayerIds != null && !failedPlayerIds.Contains(entry.PlayerId);
            entry.State = succeeded ? OnlineMatchRewardDeliveryState.Succeeded : OnlineMatchRewardDeliveryState.Failed;
        }
    }

    /// <summary>
    /// 尝试轮次达上限：仍处于 Pending/Failed 的玩家冻结为 ManualReview（停止自动重投的人工裁决面）。
    /// </summary>
    /// <param name="working">领取后的工作副本（原地更新玩家状态）。</param>
    private static void FreezeRemainingPlayers(OnlineMatchRewardOutboxRecord working)
    {
        foreach (var entry in working.PlayerEntries)
        {
            if (entry != null
                && (entry.State == OnlineMatchRewardDeliveryState.Pending || entry.State == OnlineMatchRewardDeliveryState.Failed))
            {
                entry.State = OnlineMatchRewardDeliveryState.ManualReview;
            }
        }
    }

    /// <summary>
    /// 结算异常观测点（Capability 包零 LogHelper 惯例：异常被隔离不中断同轮处理，
    /// 对局仍在 Settling 态，下一轮 Tick 重试结算；可观测面 = admin 查询 + 宿主异常计数）。
    /// </summary>
    /// <param name="matchId">对局标识。</param>
    private void OnSettlementFailed(string matchId)
    {
        _ = matchId;
        Interlocked.Increment(ref _settlementFailureCount);
    }

    /// <summary>
    /// 投递异常观测点：异常被隔离不中断同轮处理；记录租约到期后自动恢复重投，
    /// 可观测面 = <see cref="QueryStatusAsync"/> 的失败计数与未完成条目投影。
    /// </summary>
    /// <param name="matchId">对局标识。</param>
    /// <param name="matchResultId">结算结果标识。</param>
    private void OnDispatchFailed(string matchId, string matchResultId)
    {
        _ = matchId;
        _ = matchResultId;
        Interlocked.Increment(ref _dispatchFailureCount);
    }
}
