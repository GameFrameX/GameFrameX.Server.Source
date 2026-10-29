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

namespace GameFrameX.Capability.Online.Persistence.Match;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.Match;
using GameFrameX.DataBase;

/// <summary>
/// Match 域结算存储的 GameDb 持久化适配（结果首次胜出 + 待发奖 outbox 崩溃恢复）。
/// <para>
/// 维护约束（红线）：① <see cref="CommitWithOutboxAsync"/> 在同一事务内完成结果唯一插入与
/// 待发奖记录唯一插入——失败不留单边结果（GFX-1116 的原子性担保）；结果已存在时零写入、
/// 不追加 outbox（历史结果不补发）；② 领取与写回走 <c>UpdateIfRowVersionAsync</c> CAS，
/// 与内存实现语义逐字节同构；③ 到期扫描谓词（下一投递时刻 / 租约）下推到独立列，
/// 玩家级状态过滤在适配层内存完成。
/// </para>
/// <para>
/// 数据生命周期：行物理长存（零删除）——持久化行同时是崩溃恢复与 ManualReview 人工复核的
/// 审计事实；全部成功的记录不再进入任何扫描。消费方零改动：仅实现
/// <see cref="IOnlineMatchSettlementStore"/> 全方法，接口面不变。
/// </para>
/// </summary>
public sealed class GameDbOnlineMatchSettlementStore : IOnlineMatchSettlementStore
{
    /// <summary>
    /// 结算结果行的行 Id 分配序列名（无自然主键实体；键控 = 状态类型名 + 序列名）。
    /// </summary>
    private const string ResultRowIdSequence = "row-id";

    /// <summary>
    /// 待发奖行的行 Id 分配序列名。
    /// </summary>
    private const string OutboxRowIdSequence = "row-id";

    /// <summary>按作用域与对局标识查找结算结果副本。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="matchId">对局标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>结果副本；未结算返回 null。</returns>
    public async Task<OnlineMatchResult> FindByMatchAsync(long tenantId, long appId, string matchId, CancellationToken cancellationToken = default)
    {
        var state = await FindResultRowAsync(BuildResultKey(tenantId, appId, matchId), cancellationToken).ConfigureAwait(false);
        return state == null || state.Result == null ? null : state.Result.Copy();
    }

    /// <summary>唯一插入结算结果（首次胜出：键已存在返回既有结果，不覆盖、零写入）。</summary>
    /// <param name="result">待落定结果。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>落定后的结果副本（重试时即首次结果）。</returns>
    public async Task<OnlineMatchResult> CommitAsync(OnlineMatchResult result, CancellationToken cancellationToken = default)
    {
        if (result == null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        var uniqueKey = BuildResultKey(result.TenantId, result.AppId, result.MatchId);
        var rowId = await GameDb.NextSequenceAsync<OnlineMatchResultRowState>(ResultRowIdSequence, cancellationToken).ConfigureAwait(false);
        var state = new OnlineMatchResultRowState
        {
            Id = rowId,
            UniqueKey = uniqueKey,
            TenantId = result.TenantId,
            AppId = result.AppId,
            MatchId = result.MatchId,
            Result = result.Copy(),
        };
        state.RowVersion = 1;

        if (await GameDb.TryAddUniqueAsync(state, row => row.UniqueKey == uniqueKey, cancellationToken).ConfigureAwait(false))
        {
            return result.Copy();
        }

        var existing = await FindResultRowAsync(uniqueKey, cancellationToken).ConfigureAwait(false);
        return existing == null || existing.Result == null ? null : existing.Result.Copy();
    }

    /// <summary>
    /// 同一事务内原子落定结算结果并创建待发奖记录：结果唯一插入失败（已存在）时零写入、
    /// 返回既有结果且不追加 outbox；outbox 插入失败时整体回滚（失败不留单边结果）。
    /// </summary>
    /// <param name="result">待落定结果。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>落定后的结果副本（重试时即首次结果）。</returns>
    public async Task<OnlineMatchResult> CommitWithOutboxAsync(OnlineMatchResult result, CancellationToken cancellationToken = default)
    {
        if (result == null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        OnlineMatchResult committed = null;
        await GameDb.ExecuteInTransactionAsync(async () =>
        {
            committed = await CommitAsync(result, cancellationToken).ConfigureAwait(false);
            if (committed == null || !string.Equals(committed.MatchResultId, result.MatchResultId, StringComparison.Ordinal))
            {
                // 首次结果胜出败者路径：返回既有结果，零 outbox 写入（历史结果不补发）。
                return;
            }

            var rowId = await GameDb.NextSequenceAsync<OnlineMatchRewardOutboxRowState>(OutboxRowIdSequence, cancellationToken).ConfigureAwait(false);
            var record = BuildInitialOutbox(result);
            var state = ToOutboxRow(rowId, record);
            state.RowVersion = record.RowVersion;
            if (!await GameDb.TryAddUniqueAsync(state, row => row.MatchResultId == record.MatchResultId, cancellationToken).ConfigureAwait(false))
            {
                // 同事务内结果刚插入成功而 outbox 键冲突 = 存量脏数据；整体回滚，不留单边结果。
                throw new InvalidOperationException($"待发奖记录唯一插入冲突：{record.MatchResultId}");
            }
        }, cancellationToken).ConfigureAwait(false);

        return committed == null ? null : committed.Copy();
    }

    /// <summary>扫描到期待投递的待发奖记录（谓词下推到期列；玩家级过滤在适配层完成）。</summary>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒）。</param>
    /// <param name="limit">单轮扫描上限。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>到期记录副本列表。</returns>
    public async Task<IReadOnlyList<OnlineMatchRewardOutboxRecord>> FindDueAsync(long nowUnixMilliseconds, int limit, CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            return Array.Empty<OnlineMatchRewardOutboxRecord>();
        }

        var rows = await GameDb.FindListAsync<OnlineMatchRewardOutboxRowState>(
            row => row.NextAttemptTimeUnixMilliseconds <= nowUnixMilliseconds && (row.ClaimExpiresAtUnixMilliseconds == 0 || row.ClaimExpiresAtUnixMilliseconds <= nowUnixMilliseconds),
            cancellationToken).ConfigureAwait(false);

        return rows
            .Where(row => row.Record != null && row.Record.HasAutoRetryablePlayers())
            .Take(limit)
            .Select(row => row.Record.Copy())
            .ToList();
    }

    /// <summary>按结算结果标识查找待发奖记录副本。</summary>
    /// <param name="matchResultId">结算结果标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>记录副本；不存在返回 null。</returns>
    public async Task<OnlineMatchRewardOutboxRecord> FindByMatchResultAsync(string matchResultId, CancellationToken cancellationToken = default)
    {
        var row = await FindOutboxRowAsync(matchResultId, cancellationToken).ConfigureAwait(false);
        return row == null || row.Record == null ? null : row.Record.Copy();
    }

    /// <summary>CAS 领取：版本匹配且租约未被持有时刷新租约并推进行版本。</summary>
    /// <param name="matchResultId">结算结果标识。</param>
    /// <param name="expectedRowVersion">期望行版本。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒）。</param>
    /// <param name="leaseDurationMilliseconds">租约时长毫秒。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>领取成功返回 true。</returns>
    public async Task<bool> TryClaimAsync(string matchResultId, long expectedRowVersion, long nowUnixMilliseconds, long leaseDurationMilliseconds, CancellationToken cancellationToken = default)
    {
        var row = await FindOutboxRowAsync(matchResultId, cancellationToken).ConfigureAwait(false);
        if (row == null || row.Record == null
            || (row.RowVersion ?? 0) != expectedRowVersion
            || row.Record.NextAttemptTimeUnixMilliseconds > nowUnixMilliseconds
            || (row.ClaimExpiresAtUnixMilliseconds != 0 && row.ClaimExpiresAtUnixMilliseconds > nowUnixMilliseconds))
        {
            return false;
        }

        var record = row.Record.Copy();
        record.RowVersion = expectedRowVersion + 1;
        row.Record = record;
        row.ClaimExpiresAtUnixMilliseconds = nowUnixMilliseconds + leaseDurationMilliseconds;
        return await GameDb.UpdateIfRowVersionAsync(row.Id, expectedRowVersion, row, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>CAS 写回：版本匹配时整体替换记录并推进行版本（行物理长存，全部成功也不删除）。</summary>
    /// <param name="record">写回内容（RowVersion = 领取时的新版本）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>版本匹配并写入返回 true。</returns>
    public async Task<bool> TryWriteBackAsync(OnlineMatchRewardOutboxRecord record, CancellationToken cancellationToken = default)
    {
        if (record == null || string.IsNullOrEmpty(record.MatchResultId))
        {
            return false;
        }

        var row = await FindOutboxRowAsync(record.MatchResultId, cancellationToken).ConfigureAwait(false);
        if (row == null || row.Record == null)
        {
            return false;
        }

        var expectedRowVersion = row.RowVersion ?? 0;
        if (expectedRowVersion != record.RowVersion)
        {
            return false;
        }

        var stored = record.Copy();
        stored.RowVersion = record.RowVersion + 1;
        row.Record = stored;
        row.MatchId = stored.MatchId;
        row.TenantId = stored.TenantId;
        row.AppId = stored.AppId;
        row.NextAttemptTimeUnixMilliseconds = stored.NextAttemptTimeUnixMilliseconds;
        row.ClaimExpiresAtUnixMilliseconds = stored.ClaimExpiresAtUnixMilliseconds;
        return await GameDb.UpdateIfRowVersionAsync(row.Id, expectedRowVersion, row, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>列出全部未完成投递记录的 admin 投影（含 ManualReview 冻结项）。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>待重试发奖条目投影。</returns>
    public async Task<IReadOnlyList<OnlineMatchSettlementRetryEntry>> ListUndeliveredAsync(CancellationToken cancellationToken = default)
    {
        var rows = await GameDb.FindListAsync<OnlineMatchRewardOutboxRowState>(row => row.MatchResultId != null, cancellationToken).ConfigureAwait(false);
        return rows
            .Where(row => row.Record != null && row.Record.HasUndeliveredPlayers())
            .Select(row => ToRetryEntry(row.Record))
            .ToList();
    }

    /// <summary>按唯一键查找结算结果行。</summary>
    /// <param name="uniqueKey">物化复合键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>结果行；不存在返回 null。</returns>
    private static async Task<OnlineMatchResultRowState> FindResultRowAsync(string uniqueKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(uniqueKey))
        {
            return null;
        }

        var rows = await GameDb.FindListAsync<OnlineMatchResultRowState>(row => row.UniqueKey == uniqueKey, cancellationToken).ConfigureAwait(false);
        return rows.FirstOrDefault();
    }

    /// <summary>按结算结果标识查找待发奖行。</summary>
    /// <param name="matchResultId">结算结果标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>待发奖行；不存在返回 null。</returns>
    private static async Task<OnlineMatchRewardOutboxRowState> FindOutboxRowAsync(string matchResultId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(matchResultId))
        {
            return null;
        }

        var rows = await GameDb.FindListAsync<OnlineMatchRewardOutboxRowState>(row => row.MatchResultId == matchResultId, cancellationToken).ConfigureAwait(false);
        return rows.FirstOrDefault();
    }

    /// <summary>由结算结果构造初始待发奖记录（全部玩家 Pending，行版本 1，立即可投）。</summary>
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

    /// <summary>由待发奖记录构造持久化行（列物化 + 文档内嵌）。</summary>
    /// <param name="rowId">行 Id。</param>
    /// <param name="record">待发奖记录。</param>
    /// <returns>持久化行。</returns>
    private static OnlineMatchRewardOutboxRowState ToOutboxRow(long rowId, OnlineMatchRewardOutboxRecord record)
    {
        return new OnlineMatchRewardOutboxRowState
        {
            Id = rowId,
            MatchResultId = record.MatchResultId,
            MatchId = record.MatchId,
            TenantId = record.TenantId,
            AppId = record.AppId,
            NextAttemptTimeUnixMilliseconds = record.NextAttemptTimeUnixMilliseconds,
            ClaimExpiresAtUnixMilliseconds = record.ClaimExpiresAtUnixMilliseconds,
            Record = record.Copy(),
        };
    }

    /// <summary>构造记录的 admin 投影（未完成玩家集合 + 冻结状态）。</summary>
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

    /// <summary>构造结果作用域隔离键（与 InMemory 实现逐字节同构）。</summary>
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
