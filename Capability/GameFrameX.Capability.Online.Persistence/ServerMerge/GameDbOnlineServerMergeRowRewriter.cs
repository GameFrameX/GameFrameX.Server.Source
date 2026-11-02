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
using System.Linq;
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
/// ② 幂等可重入——按 <c>HomeServerId == 源服</c> 过滤重写，重跑批次为空；③ dry-run 只统计不落写；
/// ④ 批处理按 <paramref name="batchSize"/> 分批提交，中断后重跑续写剩余批次。
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
            counts[LedgerCategory] = counts.GetValueOrDefault(LedgerCategory) + await RewriteLedgerBatchAsync(sourceServerId, targetServerId, dryRun, batchSize, cancellationToken).ConfigureAwait(false);
            counts[TransactionCategory] = counts.GetValueOrDefault(TransactionCategory) + await RewriteTransactionBatchAsync(sourceServerId, targetServerId, dryRun, batchSize, cancellationToken).ConfigureAwait(false);
        }

        return counts;
    }

    /// <summary>
    /// 统计各区服域类别待重写行数。
    /// </summary>
    private static async Task<List<(string Category, long Count)>> CountCoreAsync(IReadOnlyList<long> sourceServerIds, CancellationToken cancellationToken)
    {
        var ledgerCount = 0L;
        var transactionCount = 0L;
        foreach (var sourceServerId in sourceServerIds)
        {
            var ledgerRows = await GameDb.FindListAsync<OnlineLedgerEntryState>(state => state.HomeServerId == sourceServerId, cancellationToken).ConfigureAwait(false);
            ledgerCount += ledgerRows.Count;
            var transactionRows = await GameDb.FindListAsync<OnlineAssetTransactionRowState>(state => state.HomeServerId == sourceServerId, cancellationToken).ConfigureAwait(false);
            transactionCount += transactionRows.Count;
        }

        return new List<(string, long)> { (LedgerCategory, ledgerCount), (TransactionCategory, transactionCount) };
    }

    /// <summary>
    /// 重写账本行归属列（单列 UPDATE 批处理）。
    /// </summary>
    private static async Task<long> RewriteLedgerBatchAsync(long sourceServerId, long targetServerId, bool dryRun, int batchSize, CancellationToken cancellationToken)
    {
        var rows = await GameDb.FindListAsync<OnlineLedgerEntryState>(state => state.HomeServerId == sourceServerId, cancellationToken).ConfigureAwait(false);
        if (dryRun)
        {
            return rows.Count;
        }

        return await ApplyBatchesAsync(rows, row => row.HomeServerId = targetServerId, batchSize, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 重写交易行归属列（单列 UPDATE 批处理）。
    /// </summary>
    private static async Task<long> RewriteTransactionBatchAsync(long sourceServerId, long targetServerId, bool dryRun, int batchSize, CancellationToken cancellationToken)
    {
        var rows = await GameDb.FindListAsync<OnlineAssetTransactionRowState>(state => state.HomeServerId == sourceServerId, cancellationToken).ConfigureAwait(false);
        if (dryRun)
        {
            return rows.Count;
        }

        return await ApplyBatchesAsync(rows, row => row.HomeServerId = targetServerId, batchSize, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 分批落写（每批一个 UpdateAsync 提交；中断后重跑从未重写行续写）。
    /// </summary>
    private static async Task<long> ApplyBatchesAsync<TState>(IReadOnlyList<TState> rows, Action<TState> rewriteColumn, int batchSize, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        var affected = 0L;
        for (var offset = 0; offset < rows.Count; offset += batchSize)
        {
            var batch = new List<TState>();
            for (var index = offset; index < Math.Min(offset + batchSize, rows.Count); index++)
            {
                rewriteColumn(rows[index]);
                batch.Add(rows[index]);
            }

            affected += await GameDb.UpdateAsync(batch, cancellationToken).ConfigureAwait(false);
        }

        return affected;
    }
}
