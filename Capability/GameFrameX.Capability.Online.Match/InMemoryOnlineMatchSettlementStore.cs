//  ==========================================================================================
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
//   禁止利用本项目代码实施任何危害国家安全、破坏社会秩序、
//   侵犯其他个人或组织合法权益等法律法规禁止的行为！
//  ==========================================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace GameFrameX.Capability.Online.Match;

/// <summary>
/// 进程内结算存储（结果首次胜出 + 待发奖 outbox，同一把锁下的原子与 CAS 契约）。
/// <para>
/// 维护约束（红线）：① 结果存在性检查与写入、结果 + outbox 的原子提交都在**同一临界区**完成
/// （首次结果胜出与「失败不留单边结果」的前提）；② 领取与写回按
/// <see cref="OnlineMatchRewardOutboxRecord.RowVersion"/> 严格 CAS，与数据库实现语义逐字节同构；
/// ③ 全部成功（无未完成玩家）的 outbox 记录在写回时移除——与数据库实现「行物理长存」的口径差异
/// 是易失性存储的自然结果，不影响恢复语义（易失存储本身无跨进程恢复能力）。
/// </para>
/// <para>
/// 天花板：单锁全串行；且为**易失实现**——进程崩溃后 outbox 丢失，真实崩溃恢复必须装配
/// GameDb 适配（<c>GameDbOnlineMatchSettlementStore</c>），本实现仅保证契约语义等价。
/// </para>
/// </summary>
public sealed class InMemoryOnlineMatchSettlementStore : IOnlineMatchSettlementStore
{
    /// <summary>全局读写锁（结果表与 outbox 表共用，保证跨表原子性）。</summary>
    private readonly object _syncRoot = new();

    /// <summary>结果表：键 = (TenantId, AppId, MatchId)。</summary>
    private readonly Dictionary<string, OnlineMatchResult> _results = new();

    /// <summary>待发奖记录表：键 = MatchResultId。</summary>
    private readonly Dictionary<string, OnlineMatchRewardOutboxRecord> _outboxes = new();

    /// <summary>
    /// 在全局锁内按作用域与对局标识查找结算结果并返回副本；标识为空直接返回 null。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="matchId">对局标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>结果副本；未结算返回 null。</returns>
    public Task<OnlineMatchResult> FindByMatchAsync(long tenantId, long appId, string matchId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(matchId))
        {
            return Task.FromResult<OnlineMatchResult>(null);
        }

        lock (_syncRoot)
        {
            return Task.FromResult(_results.TryGetValue(BuildResultKey(tenantId, appId, matchId), out var result) ? result.Copy() : null);
        }
    }

    /// <summary>
    /// 在全局锁内的同一临界区完成存在性检查与写入，实现首次结果胜出：已存在时返回既有结果，不覆盖（无 outbox 追加）。
    /// </summary>
    /// <param name="result">待落定结果。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>落定后的结果副本（重试时即首次结果）。</returns>
    public Task<OnlineMatchResult> CommitAsync(OnlineMatchResult result, CancellationToken cancellationToken = default)
    {
        if (result == null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        lock (_syncRoot)
        {
            var key = BuildResultKey(result.TenantId, result.AppId, result.MatchId);
            if (_results.TryGetValue(key, out var existing))
            {
                return Task.FromResult(existing.Copy());
            }

            _results[key] = result.Copy();
            return Task.FromResult(result.Copy());
        }
    }

    /// <summary>
    /// 原子落定结果并创建待发奖记录：结果不存在时结果与 outbox 在同一临界区写入；
    /// 结果已存在时返回既有结果且不追加 outbox（历史结果不补发）。
    /// </summary>
    /// <param name="result">待落定结果。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>落定后的结果副本（重试时即首次结果）。</returns>
    public Task<OnlineMatchResult> CommitWithOutboxAsync(OnlineMatchResult result, CancellationToken cancellationToken = default)
    {
        if (result == null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        lock (_syncRoot)
        {
            var key = BuildResultKey(result.TenantId, result.AppId, result.MatchId);
            if (_results.TryGetValue(key, out var existing))
            {
                return Task.FromResult(existing.Copy());
            }

            _results[key] = result.Copy();
            _outboxes[result.MatchResultId] = BuildInitialOutbox(result);
            return Task.FromResult(result.Copy());
        }
    }

    /// <summary>
    /// 扫描到期可投递记录（存在自动重投玩家、到达下一投递时刻、租约未被持有）。
    /// </summary>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒）。</param>
    /// <param name="limit">单轮扫描上限。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>到期记录副本列表。</returns>
    public Task<IReadOnlyList<OnlineMatchRewardOutboxRecord>> FindDueAsync(long nowUnixMilliseconds, int limit, CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            return Task.FromResult<IReadOnlyList<OnlineMatchRewardOutboxRecord>>(Array.Empty<OnlineMatchRewardOutboxRecord>());
        }

        lock (_syncRoot)
        {
            var due = new List<OnlineMatchRewardOutboxRecord>();
            foreach (var record in _outboxes.Values)
            {
                if (due.Count >= limit)
                {
                    break;
                }

                if (IsDue(record, nowUnixMilliseconds))
                {
                    due.Add(record.Copy());
                }
            }

            return Task.FromResult<IReadOnlyList<OnlineMatchRewardOutboxRecord>>(due);
        }
    }

    /// <summary>
    /// 按结算结果标识查找待发奖记录副本。
    /// </summary>
    /// <param name="matchResultId">结算结果标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>记录副本；不存在返回 null。</returns>
    public Task<OnlineMatchRewardOutboxRecord> FindByMatchResultAsync(string matchResultId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(matchResultId))
        {
            return Task.FromResult<OnlineMatchRewardOutboxRecord>(null);
        }

        lock (_syncRoot)
        {
            return Task.FromResult(_outboxes.TryGetValue(matchResultId, out var record) ? record.Copy() : null);
        }
    }

    /// <summary>
    /// CAS 领取：版本匹配且租约未被持有时刷新租约并推进版本。
    /// </summary>
    /// <param name="matchResultId">结算结果标识。</param>
    /// <param name="expectedRowVersion">期望行版本。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒）。</param>
    /// <param name="leaseDurationMilliseconds">租约时长毫秒。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>领取成功返回 true。</returns>
    public Task<bool> TryClaimAsync(string matchResultId, long expectedRowVersion, long nowUnixMilliseconds, long leaseDurationMilliseconds, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(matchResultId))
        {
            return Task.FromResult(false);
        }

        lock (_syncRoot)
        {
            if (!_outboxes.TryGetValue(matchResultId, out var record)
                || record.RowVersion != expectedRowVersion
                || !IsDue(record, nowUnixMilliseconds))
            {
                return Task.FromResult(false);
            }

            record.RowVersion = expectedRowVersion + 1;
            record.ClaimExpiresAtUnixMilliseconds = nowUnixMilliseconds + leaseDurationMilliseconds;
            return Task.FromResult(true);
        }
    }

    /// <summary>
    /// CAS 写回：版本匹配时整体替换记录副本并推进版本；全部成功且无冻结玩家时移除记录。
    /// </summary>
    /// <param name="record">写回内容（RowVersion = 领取时的新版本）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>版本匹配并写入返回 true。</returns>
    public Task<bool> TryWriteBackAsync(OnlineMatchRewardOutboxRecord record, CancellationToken cancellationToken = default)
    {
        if (record == null || string.IsNullOrEmpty(record.MatchResultId))
        {
            return Task.FromResult(false);
        }

        lock (_syncRoot)
        {
            if (!_outboxes.TryGetValue(record.MatchResultId, out var existing) || existing.RowVersion != record.RowVersion)
            {
                return Task.FromResult(false);
            }

            if (!record.HasUndeliveredPlayers())
            {
                _outboxes.Remove(record.MatchResultId);
                return Task.FromResult(true);
            }

            var stored = record.Copy();
            stored.RowVersion = record.RowVersion + 1;
            _outboxes[record.MatchResultId] = stored;
            return Task.FromResult(true);
        }
    }

    /// <summary>
    /// 列出全部未完成投递记录的 admin 投影（含 ManualReview 冻结项）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>待重试发奖条目投影。</returns>
    public Task<IReadOnlyList<OnlineMatchSettlementRetryEntry>> ListUndeliveredAsync(CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            var entries = new List<OnlineMatchSettlementRetryEntry>();
            foreach (var record in _outboxes.Values)
            {
                if (record.HasUndeliveredPlayers())
                {
                    entries.Add(ToRetryEntry(record));
                }
            }

            return Task.FromResult<IReadOnlyList<OnlineMatchSettlementRetryEntry>>(entries);
        }
    }

    /// <summary>
    /// 判断记录是否到期可领取（自动重投玩家存在 + 到达投递时刻 + 租约未被持有）。
    /// </summary>
    /// <param name="record">待判断记录（锁内引用）。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒）。</param>
    /// <returns>到期返回 true。</returns>
    private static bool IsDue(OnlineMatchRewardOutboxRecord record, long nowUnixMilliseconds)
    {
        return record.HasAutoRetryablePlayers()
            && record.NextAttemptTimeUnixMilliseconds <= nowUnixMilliseconds
            && record.ClaimExpiresAtUnixMilliseconds <= nowUnixMilliseconds;
    }

    /// <summary>
    /// 由结算结果构造初始待发奖记录（全部玩家 Pending，行版本 1，立即可投）。
    /// </summary>
    /// <param name="result">已落定的结算结果。</param>
    /// <returns>初始待发奖记录。</returns>
    private static OnlineMatchRewardOutboxRecord BuildInitialOutbox(OnlineMatchResult result)
    {
        var record = new OnlineMatchRewardOutboxRecord
        {
            MatchResultId = result.MatchResultId,
            MatchId = result.MatchId,
            TenantId = result.TenantId,
            AppId = result.AppId,
            Result = result.Copy(),
            RowVersion = 1,
        };

        if (result.Entries != null)
        {
            foreach (var entry in result.Entries)
            {
                if (entry != null)
                {
                    record.PlayerEntries.Add(new OnlineMatchRewardOutboxPlayerEntry
                    {
                        PlayerId = entry.PlayerId,
                        State = OnlineMatchRewardDeliveryState.Pending,
                    });
                }
            }
        }

        return record;
    }

    /// <summary>
    /// 构造记录的 admin 投影（未完成玩家集合 + 冻结状态）。
    /// </summary>
    /// <param name="record">待投影记录。</param>
    /// <returns>待重试发奖条目。</returns>
    private static OnlineMatchSettlementRetryEntry ToRetryEntry(OnlineMatchRewardOutboxRecord record)
    {
        var failedPlayerIds = new List<long>();
        var frozen = false;
        foreach (var entry in record.PlayerEntries)
        {
            if (entry == null || entry.State == OnlineMatchRewardDeliveryState.Succeeded)
            {
                continue;
            }

            failedPlayerIds.Add(entry.PlayerId);
            if (entry.State == OnlineMatchRewardDeliveryState.ManualReview)
            {
                frozen = true;
            }
        }

        return new OnlineMatchSettlementRetryEntry
        {
            MatchResultId = record.MatchResultId,
            MatchId = record.MatchId,
            FailedPlayerIds = failedPlayerIds,
            Attempts = record.Attempts,
            State = frozen ? OnlineMatchSettlementRetryState.ManualReview : OnlineMatchSettlementRetryState.Pending,
        };
    }

    /// <summary>
    /// 构造结果作用域隔离键。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="matchId">对局标识。</param>
    /// <returns>复合键。</returns>
    private static string BuildResultKey(long tenantId, long appId, string matchId)
    {
        return string.Concat(
            tenantId.ToString(CultureInfo.InvariantCulture),
            ":",
            appId.ToString(CultureInfo.InvariantCulture),
            ":",
            matchId ?? string.Empty);
    }
}
