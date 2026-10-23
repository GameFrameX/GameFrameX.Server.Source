// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and other rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the PRC and applicable international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with relevant laws, regulations and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file at the source code root for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切纠纷与责任，
//   Any disputes or liabilities arising from secondary development based on this project
//   本组织不承担任何责任，开发者应自行承担。
//   The organization and contributors shall not be responsible; developers shall bear the responsibility themselves.
// ==========================================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.Abstractions.Contracts;

namespace GameFrameX.Capability.Online.Match;

/// <summary>
/// 对局结算管线（C245）：Tick 驱动的「结算 → 发奖 → 失败重投」生产编排。
/// <para>
/// 维护约束（红线）：① Tick 互斥（并发双 Tick 串行安全）；② 不宣称「恰好一次」——结算幂等由
/// <see cref="OnlineMatchSettlementService"/>（首结果获胜）、发奖幂等由
/// <see cref="OnlineMatchResultDispatcher"/> 的 <c>{MatchResultId}-{PlayerId}</c> 键保证，
/// 管线只做编排与重投，各阶段独立失败可观测（<see cref="QueryStatus"/>）可恢复；
/// ③ 结算回放（<c>IsReplay</c>）不重复发奖路径。
/// </para>
/// <para>
/// 分发器可空：assets 能力被过滤（模块未装配 <c>OnlineGrantService</c>）时仅结算、不发奖、
/// 不记重试——与 Competition 域 season/tournament 的可空发放依赖同一模块化等价口径。
/// </para>
/// </summary>
/// <remarks>
/// The settlement pipeline (C245): the tick-driven "settle → grant → retry failed grants" production
/// orchestration. Ticks are mutually exclusive (concurrent ticks serialize); no exactly-once claim is
/// made — settlement idempotency comes from the settlement service (first result wins) and grant
/// idempotency from the dispatcher's <c>{MatchResultId}-{PlayerId}</c> keys, while this pipeline only
/// orchestrates and re-dispatches, keeping per-stage failures observable (<see cref="QueryStatus"/>)
/// and recoverable. A null dispatcher means the assets capability was filtered out: settle only, no
/// grants, no retries — the same modular-equivalence precedent as the Competition domain's nullable
/// grant dependency.
/// </remarks>
public sealed class OnlineMatchSettlementPipeline
{
    /// <summary>对局运行时（结算 tick 的枚举源）。</summary>
    private readonly OnlineMatchRuntime _runtime;

    /// <summary>结算服务（幂等）。</summary>
    private readonly OnlineMatchSettlementService _settlementService;

    /// <summary>结果分发器（可空 = assets 能力缺席，仅结算不发奖）。</summary>
    private readonly OnlineMatchResultDispatcher _dispatcher;

    /// <summary>Tick 互斥门（并发双 Tick 串行安全）。</summary>
    private readonly SemaphoreSlim _tickGate = new(1, 1);

    /// <summary>待重试发奖表：键 = MatchResultId（去重更新）。</summary>
    private readonly Dictionary<string, RetryRecord> _retries = new();

    /// <summary>
    /// 初始化结算管线。
    /// </summary>
    /// <param name="runtime">对局运行时。</param>
    /// <param name="settlementService">结算服务。</param>
    /// <param name="dispatcher">结果分发器（可空：assets 能力缺席时仅结算不发奖不记重试）。</param>
    public OnlineMatchSettlementPipeline(OnlineMatchRuntime runtime, OnlineMatchSettlementService settlementService, OnlineMatchResultDispatcher dispatcher = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _settlementService = settlementService ?? throw new ArgumentNullException(nameof(settlementService));
        _dispatcher = dispatcher;
    }

    /// <summary>
    /// 推进一轮结算编排：结算全部 <see cref="OnlineMatchState.Settling"/> 对局 → 非回放结果发奖 →
    /// 失败进入待重试表 → 重投全部待重试项（幂等键保证不双发）。
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

                var settled = await _settlementService.SettleAsync(actor.TenantId, actor.AppId, actor.MatchId, nowUnixMilliseconds, cancellationToken).ConfigureAwait(false);
                if (!settled.IsSuccess || settled.Data.IsReplay || _dispatcher == null)
                {
                    continue;
                }

                await DispatchAndTrackAsync(settled.Data.Result, cancellationToken).ConfigureAwait(false);
            }

            if (_dispatcher == null || _retries.Count == 0)
            {
                return;
            }

            // 快照后遍历：成功项在门内移除。
            var pending = new List<RetryRecord>(_retries.Values);
            foreach (var record in pending)
            {
                var dispatched = await _dispatcher.DispatchAsync(record.Result, cancellationToken).ConfigureAwait(false);
                record.Attempts++;
                if (dispatched.IsSuccess && dispatched.Data != null && dispatched.Data.FailedPlayerIds.Count == 0)
                {
                    _retries.Remove(record.Entry.MatchResultId);
                    continue;
                }

                UpdateFailedPlayers(record, dispatched);
            }
        }
        finally
        {
            _tickGate.Release();
        }
    }

    /// <summary>
    /// 查询编排状态（admin 可观测面）。
    /// </summary>
    /// <returns>结算中对局标识清单 + 待重试发奖条目副本。</returns>
    public (IReadOnlyList<string> SettlingMatchIds, IReadOnlyList<OnlineMatchSettlementRetryEntry> Retries) QueryStatus()
    {
        var settling = new List<string>();
        foreach (var actor in _runtime.SnapshotActors())
        {
            if (actor.State == OnlineMatchState.Settling)
            {
                settling.Add(actor.MatchId);
            }
        }

        var retries = new List<OnlineMatchSettlementRetryEntry>();
        lock (_retries)
        {
            foreach (var record in _retries.Values)
            {
                retries.Add(new OnlineMatchSettlementRetryEntry
                {
                    MatchResultId = record.Entry.MatchResultId,
                    MatchId = record.Entry.MatchId,
                    FailedPlayerIds = new List<long>(record.Entry.FailedPlayerIds),
                    Attempts = record.Entry.Attempts,
                });
            }
        }

        return (settling, retries);
    }

    /// <summary>
    /// 发奖并登记结果（成功且无失败玩家移除既有重试项，否则按 MatchResultId 去重更新待重试表）。
    /// </summary>
    /// <param name="result">已落定的结算结果。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>完成通知。</returns>
    private async Task DispatchAndTrackAsync(OnlineMatchResult result, CancellationToken cancellationToken)
    {
        var dispatched = await _dispatcher.DispatchAsync(result, cancellationToken).ConfigureAwait(false);
        if (dispatched.IsSuccess && dispatched.Data != null && dispatched.Data.FailedPlayerIds.Count == 0)
        {
            lock (_retries)
            {
                _retries.Remove(result.MatchResultId);
            }

            return;
        }

        lock (_retries)
        {
            if (!_retries.TryGetValue(result.MatchResultId, out var record))
            {
                record = new RetryRecord
                {
                    Result = result,
                    Entry = new OnlineMatchSettlementRetryEntry
                    {
                        MatchResultId = result.MatchResultId,
                        MatchId = result.MatchId,
                        FailedPlayerIds = new List<long>(),
                        Attempts = 0,
                    },
                };

                _retries[result.MatchResultId] = record;
            }

            record.Result = result;
            record.Attempts++;
            UpdateFailedPlayers(record, dispatched);
        }
    }

    /// <summary>
    /// 按分发回执刷新失败玩家列表（分发整体失败时按结果条目全量登记）。
    /// </summary>
    /// <param name="record">待重试记录。</param>
    /// <param name="dispatched">分发回执。</param>
    private static void UpdateFailedPlayers(RetryRecord record, OnlineResult<OnlineMatchDispatchOutcome> dispatched)
    {
        record.Entry.FailedPlayerIds.Clear();
        if (dispatched.IsSuccess && dispatched.Data != null)
        {
            record.Entry.FailedPlayerIds.AddRange(dispatched.Data.FailedPlayerIds);
            return;
        }

        if (record.Result.Entries == null)
        {
            return;
        }

        foreach (var entry in record.Result.Entries)
        {
            if (entry != null)
            {
                record.Entry.FailedPlayerIds.Add(entry.PlayerId);
            }
        }
    }

    /// <summary>
    /// 待重试记录（内部形态：保留结果对象供重投，对外只暴露 <see cref="OnlineMatchSettlementRetryEntry"/> 投影）。
    /// </summary>
    private sealed class RetryRecord
    {
        /// <summary>待重投的结算结果（幂等键派生源）。</summary>
        public OnlineMatchResult Result { get; set; }

        /// <summary>对外只读投影。</summary>
        public OnlineMatchSettlementRetryEntry Entry { get; set; }

        /// <summary>累计投递尝试次数（含首发）。</summary>
        public int Attempts
        {
            get { return Entry.Attempts; }
            set { Entry.Attempts = value; }
        }
    }
}
