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

namespace GameFrameX.Tests.Online;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Keys;
using GameFrameX.Capability.Online.Persistence;
using GameFrameX.Capability.Online.Persistence.Assets;
using GameFrameX.Capability.Online.ServerLifecycle;
using GameFrameX.Capability.Online.ServerLifecycle.ServerMerge;
using GameFrameX.DataBase;
using GameFrameX.DataBase.Abstractions;
using GameFrameX.DataBase.Sqlite;
using Xunit;

/// <summary>
/// GameDb 区服域行键重写持久化回归：账本 / 交易行 <c>HomeServerId</c> 单列重写幂等可重入、
/// dry-run 零写入、行键（全局域玩家键）零改动。
/// </summary>
[Collection("GameDbStaticStateCollection")]
public sealed class GameDbOnlineServerMergeRowRewriterTests
{
    /// <summary>
    /// SQLite 内存库门面（静态门面串行集合保证独占）。
    /// </summary>
    private static async Task WithSqliteFacadeAsync(Func<Task> action, string name)
    {
        var opened = await GameDb.InitAsync<SqliteDbService>(new DbOptions
        {
            ConnectionString = "Data Source=:memory:",
            Name = name,
        });
        Assert.True(opened);
        try
        {
            await action();
        }
        finally
        {
            await GameDb.CloseAsync();
            GameDb.ResetForTesting();
        }
    }

    [Fact]
    public async Task Rewrite_HomeServerIdColumn_IsIdempotentAndRerunnable()
    {
        await WithSqliteFacadeAsync(async () =>
        {
            await GameDb.SaveOneAsync(new OnlineLedgerEntryState
            {
                EntryId = "led-1",
                PlayerId = 11,
                HomeServerId = 1,
                InitiatingServerId = 1,
                AmountBefore = 0,
                Delta = 10,
                AmountAfter = 10,
                SequenceNumber = 1,
            });
            await GameDb.SaveOneAsync(new OnlineAssetTransactionRowState
            {
                TransactionId = "txn-1",
                PlayerId = 11,
                HomeServerId = 2,
                InitiatingServerId = 2,
            });

            var rewriter = new GameDbOnlineServerMergeRowRewriter();

            var dryRunCounts = await rewriter.RewriteServerDomainRowsAsync(new long[] { 1, 2 }, 3, true, 500, CancellationToken.None);
            Assert.Equal(1, dryRunCounts[GameDbOnlineServerMergeRowRewriter.LedgerCategory]);
            Assert.Equal(1, dryRunCounts[GameDbOnlineServerMergeRowRewriter.TransactionCategory]);

            // dry-run 零写入。
            var untouched = await GameDb.FindListAsync<OnlineLedgerEntryState>(state => state.HomeServerId == 1);
            Assert.Single(untouched);

            var executedCounts = await rewriter.RewriteServerDomainRowsAsync(new long[] { 1, 2 }, 3, false, 500, CancellationToken.None);
            Assert.Equal(1, executedCounts[GameDbOnlineServerMergeRowRewriter.LedgerCategory]);
            Assert.Equal(1, executedCounts[GameDbOnlineServerMergeRowRewriter.TransactionCategory]);

            // 行键（全局域玩家键）零改动，只有归属列被重写。
            var ledgerRow = (await GameDb.FindListAsync<OnlineLedgerEntryState>(state => state.HomeServerId == 3)).Single();
            Assert.Equal("led-1", ledgerRow.EntryId);
            Assert.Equal(11, ledgerRow.PlayerId);
            var transactionRow = (await GameDb.FindListAsync<OnlineAssetTransactionRowState>(state => state.HomeServerId == 3)).Single();
            Assert.Equal("txn-1", transactionRow.TransactionId);

            // 重跑幂等：无行可重写。
            var rerunCounts = await rewriter.RewriteServerDomainRowsAsync(new long[] { 1, 2 }, 3, false, 500, CancellationToken.None);
            Assert.Equal(0, rerunCounts[GameDbOnlineServerMergeRowRewriter.LedgerCategory]);
            Assert.Equal(0, rerunCounts[GameDbOnlineServerMergeRowRewriter.TransactionCategory]);
        }, "c466-row-rewriter-tests");
    }

    [Fact]
    public async Task CountServerDomainRows_MatchesRewriteCounts()
    {
        await WithSqliteFacadeAsync(async () =>
        {
            await GameDb.SaveOneAsync(new OnlineLedgerEntryState
            {
                EntryId = "led-2",
                PlayerId = 12,
                HomeServerId = 1,
                InitiatingServerId = 1,
                AmountBefore = 0,
                Delta = 5,
                AmountAfter = 5,
                SequenceNumber = 1,
            });

            var rewriter = new GameDbOnlineServerMergeRowRewriter();
            var counts = await rewriter.CountServerDomainRowsAsync(new long[] { 1 }, CancellationToken.None);
            Assert.Equal(1, counts[GameDbOnlineServerMergeRowRewriter.LedgerCategory]);
            Assert.Equal(0, counts[GameDbOnlineServerMergeRowRewriter.TransactionCategory]);
        }, "c466-row-rewriter-count-tests");
    }

}
