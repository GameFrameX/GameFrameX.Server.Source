// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and other rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Capability.Online.Persistence;

using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.Persistence.Assets;
using GameFrameX.Capability.Online.ServerLifecycle.ServerMerge;
using GameFrameX.DataBase;

/// <summary>
/// 区服域行键重写 GameDb 适配（资产归属列 <c>HomeServerId</c> 单列重写：账本行 OnlineLedgerEntryState +
/// 交易行 OnlineAssetTransactionRowState；行键本身为全局域玩家键（C462），键零改动、只重写归属快照列）。
/// <para>
/// 红线：① 只重写区服维度快照列，不触碰任何全局域行键（好友/身份/玩家存储/钱包/库存——合服零操作）；
/// ② 幂等可重入——按 <c>HomeServerId == 源服</c> 过滤重写，重跑批次为空；③ dry-run 只统计不落写
/// （SQL COUNT，零行物化）；④ 读取与写入同界——键集分页按 <paramref name="batchSize"/> 逐页读取逐页重写
/// （每页即读即写，不整表物化）；⑤ 单行落写为 CAS 条件写（仅当行版本一致才写入），读取后发生并发写
/// 不会整档回写覆盖，失配有界重读重试。
/// </para>
/// <para>
/// 运维语义：合服为离线管理操作，执行窗口内源服应停写（编排由运营台负责）；CAS 条件写是并发防御底线
/// 而非许可——窗口内残留并发写会触发失配重试；对账明细区分 <c>-skipped</c>（CAS 超限仍滞留源服，
/// 同一计划重跑走幂等回放不再重写，补救 = 停写后人工修复或换源集合重执行）与 <c>-moved</c>（被并发方
/// 迁出 / 删除，非本次重写责任面）。
/// </para>
/// </summary>
public sealed class GameDbOnlineServerMergeRowRewriter : IOnlineServerMergeRowRewriter
{
    /// <summary>
    /// 账本行类别名（报告明细键）。
    /// </summary>
    public const string LedgerCategory = "ledger-entries";

    /// <summary>
    /// 交易行类别名（报告明细键）。
    /// </summary>
    public const string TransactionCategory = "asset-transactions";

    /// <summary>
    /// 单行 CAS 条件写失配后的最大重试次数（重读最新档后再试；超限跳过该行，如实少计）。
    /// </summary>
    public const int RowRewriteMaxAttempts = 3;

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, long>> CountServerDomainRowsAsync(IReadOnlyList<long> sourceServerIds, CancellationToken cancellationToken)
    {
        var counts = new Dictionary<string, long>();
        foreach (var (category, count) in await CountCoreAsync(sourceServerIds, cancellationToken).ConfigureAwait(false))
        {
            counts[category] = count;
        }

        return counts;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, long>> RewriteServerDomainRowsAsync(IReadOnlyList<long> sourceServerIds, long targetServerId, bool dryRun, int batchSize, CancellationToken cancellationToken)
    {
        if (sourceServerIds == null)
        {
            throw new ArgumentNullException(nameof(sourceServerIds));
        }

        if (targetServerId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetServerId), "TargetServerId must be positive.");
        }

        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), "BatchSize must be positive.");
        }

        var counts = new Dictionary<string, long>();
        foreach (var sourceServerId in sourceServerIds)
        {
            // dry-run 零物化：CountAsync 走 SQL COUNT，不反序列化任何行。
            if (dryRun)
            {
                counts[LedgerCategory] = counts.GetValueOrDefault(LedgerCategory) + await GameDb.CountAsync<OnlineLedgerEntryState>(state => state.HomeServerId == sourceServerId, cancellationToken).ConfigureAwait(false);
                counts[TransactionCategory] = counts.GetValueOrDefault(TransactionCategory) + await GameDb.CountAsync<OnlineAssetTransactionRowState>(state => state.HomeServerId == sourceServerId, cancellationToken).ConfigureAwait(false);
                continue;
            }

            var ledgerOutcome = await RewriteRowsAsync<OnlineLedgerEntryState>(sourceServerId, batchSize, cursor => state => state.HomeServerId == sourceServerId && state.Id > cursor, state => state.HomeServerId, state => state.HomeServerId = targetServerId, cancellationToken).ConfigureAwait(false);
            counts[LedgerCategory] = counts.GetValueOrDefault(LedgerCategory) + ledgerOutcome.Rewritten;
            AddReconciliationDetails(counts, LedgerCategory, ledgerOutcome.Skipped, ledgerOutcome.Moved);
            var transactionOutcome = await RewriteRowsAsync<OnlineAssetTransactionRowState>(sourceServerId, batchSize, cursor => state => state.HomeServerId == sourceServerId && state.Id > cursor, state => state.HomeServerId, state => state.HomeServerId = targetServerId, cancellationToken).ConfigureAwait(false);
            counts[TransactionCategory] = counts.GetValueOrDefault(TransactionCategory) + transactionOutcome.Rewritten;
            AddReconciliationDetails(counts, TransactionCategory, transactionOutcome.Skipped, transactionOutcome.Moved);
        }

        return counts;
    }

    /// <summary>
    /// 统计各区服域类别待重写行数（SQL COUNT，零行物化）。
    /// </summary>
    private static async Task<List<(string Category, long Count)>> CountCoreAsync(IReadOnlyList<long> sourceServerIds, CancellationToken cancellationToken)
    {
        var ledgerCount = 0L;
        var transactionCount = 0L;
        foreach (var sourceServerId in sourceServerIds)
        {
            ledgerCount += await GameDb.CountAsync<OnlineLedgerEntryState>(state => state.HomeServerId == sourceServerId, cancellationToken).ConfigureAwait(false);
            transactionCount += await GameDb.CountAsync<OnlineAssetTransactionRowState>(state => state.HomeServerId == sourceServerId, cancellationToken).ConfigureAwait(false);
        }

        return new List<(string, long)> { (LedgerCategory, ledgerCount), (TransactionCategory, transactionCount) };
    }

    /// <summary>
    /// 键集分页重写归属列（单列 CAS 条件写）。
    /// <para>
    /// 分页推进不变式：每页按 <c>Id</c> 升序读取（过滤 <c>归属列 == 源服 &amp;&amp; Id &gt; 游标</c>，
    /// 页大小 = <paramref name="batchSize"/>），读后立即重写整页——已重写行离开过滤集，但游标只按页内
    /// 最大 Id 严格递增推进，重写与推进互不干扰；页空即终止。中断后重跑从未重写行续写（幂等可重入）。
    /// </para>
    /// </summary>
    private static async Task<(long Rewritten, long Skipped, long Moved)> RewriteRowsAsync<TState>(long sourceServerId, int batchSize, Func<long, Expression<Func<TState, bool>>> buildPageFilter, Func<TState, long> getHomeServerId, Action<TState> setHomeServerId, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        long rewritten = 0;
        long skipped = 0;
        long moved = 0;
        long cursor = 0;
        while (true)
        {
            var page = await GameDb.FindPageAsync<TState>(
                buildPageFilter(cursor),
                state => state.Id,
                false,
                0,
                batchSize,
                cancellationToken).ConfigureAwait(false);
            if (page.Items.Count == 0)
            {
                break;
            }

            foreach (var row in page.Items)
            {
                var outcome = await TryRewriteRowAsync(row, sourceServerId, getHomeServerId, setHomeServerId, cancellationToken).ConfigureAwait(false);
                if (outcome == RowRewriteOutcome.Rewritten)
                {
                    rewritten++;
                }
                else if (outcome == RowRewriteOutcome.CasExhausted)
                {
                    skipped++;
                }
                else
                {
                    moved++;
                }
            }

            cursor = page.Items[page.Items.Count - 1].Id;
        }

        return (rewritten, skipped, moved);
    }

    /// <summary>
    /// 追加对账明细键（仅非零时出现）：<c>&lt;category&gt;-skipped</c> = CAS 失配超限仍滞留源服的行；
    /// <c>&lt;category&gt;-moved</c> = 读取后被并发方迁出 / 删除、已不在源服的行（非本次重写责任面）。
    /// </summary>
    private static void AddReconciliationDetails(Dictionary<string, long> counts, string category, long skipped, long moved)
    {
        if (skipped > 0)
        {
            counts[category + "-skipped"] = counts.GetValueOrDefault(category + "-skipped") + skipped;
        }

        if (moved > 0)
        {
            counts[category + "-moved"] = counts.GetValueOrDefault(category + "-moved") + moved;
        }
    }

    /// <summary>
    /// 单行重写结局。
    /// </summary>
    private enum RowRewriteOutcome
    {
        /// <summary>
        /// 已条件写成功（归属列已重写为目标服）。
        /// </summary>
        Rewritten,

        /// <summary>
        /// CAS 失配超限——行仍属源服，归属列停留未改（对账须计入 skipped）。
        /// </summary>
        CasExhausted,

        /// <summary>
        /// 读取后被并发方迁出源服或删除（不属于本次重写责任面）。
        /// </summary>
        LeftSource,
    }

    /// <summary>
    /// 单行归属列 CAS 条件写：仅当存量行版本与读取时一致才写入（消除读-改-写整档回写覆盖并发更新的窗口）。
    /// <para>
    /// 版本失配 = 读取后发生过并发写：重读最新档，仍属源服则以最新档重试（保留并发方的字段更新），
    /// 已离开源服（并发迁移 / 删除）按 LeftSource 记入 moved 明细；<see cref="RowRewriteMaxAttempts"/>
    /// 次仍失配按 CasExhausted 记入 skipped 明细（行仍滞留源服）。遗留行版本缺失（null）走
    /// <c>UpdateIfRowVersionIsNullAsync</c> 存量回填原语（同为条件写）。
    /// </para>
    /// </summary>
    private static async Task<RowRewriteOutcome> TryRewriteRowAsync<TState>(TState row, long sourceServerId, Func<TState, long> getHomeServerId, Action<TState> setHomeServerId, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        var current = row;
        for (var attempt = 0; attempt < RowRewriteMaxAttempts; attempt++)
        {
            setHomeServerId(current);
            var written = current.RowVersion == null
                ? await GameDb.UpdateIfRowVersionIsNullAsync(current.Id, current, cancellationToken).ConfigureAwait(false)
                : await GameDb.UpdateIfRowVersionAsync(current.Id, current.RowVersion.Value, current, cancellationToken).ConfigureAwait(false);
            if (written)
            {
                return RowRewriteOutcome.Rewritten;
            }

            var fresh = await GameDb.FindAsync<TState>(current.Id, filter: null, isCreateIfNotExists: false, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (fresh == null || getHomeServerId(fresh) != sourceServerId)
            {
                return RowRewriteOutcome.LeftSource;
            }

            current = fresh;
        }

        return RowRewriteOutcome.CasExhausted;
    }
}
