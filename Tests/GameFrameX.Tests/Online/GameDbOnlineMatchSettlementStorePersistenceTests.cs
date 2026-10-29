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

namespace GameFrameX.Tests.Online;

using System;
using System.Collections.Generic;
using System.IO;
using GameFrameX.DataBase.Abstractions;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.Match;
using GameFrameX.Capability.Online.Persistence.Match;
using GameFrameX.DataBase;
using GameFrameX.DataBase.Sqlite;
using Xunit;

/// <summary>
/// Match 域结算存储 GameDb 适配的持久化回归（GFX-1116）：文件库关闭再重开后，
/// 结果与待发奖 outbox 完整恢复；CAS 领取 / 写回跨重启语义成立；历史结果不补建 outbox。
/// </summary>
[Collection("GameDbStaticStateCollection")]
public sealed class GameDbOnlineMatchSettlementStorePersistenceTests : IDisposable
{
    /// <summary>租户标识。</summary>
    private const long TenantId = 11L;

    /// <summary>App 标识。</summary>
    private const long AppId = 4L;

    /// <summary>临时根目录（文件库落点）。</summary>
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "gfx-c408-outbox-" + Guid.NewGuid().ToString("N"));

    /// <summary>
    /// 验证关闭再重开数据库后待发奖记录完整恢复：到期扫描可见、逐玩家状态与尝试轮次保留、
    /// 领取租约语义跨重启成立（未到期不可再领取，到期可接管）。
    /// </summary>
    [Fact]
    public async Task FileDatabase_CloseAndReopen_OutboxRecordSurvivesWithLeaseSemantics()
    {
        var dataFile = Path.Combine(_tempRoot, "outbox-recovery.db");
        Directory.CreateDirectory(_tempRoot);

        // 第一次进程：结果 + outbox 原子落库，第一轮投递部分成功后「崩溃」。
        await WithSqliteFileFacadeAsync(dataFile, "c408-open", async () =>
        {
            var store = new GameDbOnlineMatchSettlementStore();
            var committed = await store.CommitWithOutboxAsync(BuildSampleResult("result-1", "match-1"));
            Assert.NotNull(committed);

            var due = await store.FindDueAsync(1000, 10);
            var record = Assert.Single(due);
            Assert.Equal(1, record.RowVersion);
            Assert.All(record.PlayerEntries, entry => Assert.Equal(OnlineMatchRewardDeliveryState.Pending, entry.State));

            // 领取 → 部分写回（玩家一成功、玩家二失败），模拟领取者崩溃前的最后状态。
            Assert.True(await store.TryClaimAsync("result-1", record.RowVersion, 1000, 5000));
            var working = record.Copy();
            working.RowVersion = 2;
            working.Attempts = 1;
            working.PlayerEntries[0].State = OnlineMatchRewardDeliveryState.Succeeded;
            working.PlayerEntries[1].State = OnlineMatchRewardDeliveryState.Failed;
            working.ClaimExpiresAtUnixMilliseconds = 6000;
            Assert.True(await store.TryWriteBackAsync(working));
        });

        // 第二次进程（同库重开）：租约未到期不得重复领取；到期后可接管继续投递。
        await WithSqliteFileFacadeAsync(dataFile, "c408-reopen", async () =>
        {
            var store = new GameDbOnlineMatchSettlementStore();

            // 结果面恢复：按对局可查回既有结果，重复提交不覆盖。
            var existing = await store.FindByMatchAsync(TenantId, AppId, "match-1");
            Assert.NotNull(existing);
            Assert.Equal("result-1", existing.MatchResultId);
            var replay = await store.CommitWithOutboxAsync(BuildSampleResult("result-1-other", "match-1"));
            Assert.Equal("result-1", replay.MatchResultId);

            // outbox 恢复：租约期内不可领取。
            var duringLease = await store.FindDueAsync(5000, 10);
            Assert.Empty(duringLease);
            Assert.False(await store.TryClaimAsync("result-1", 2, 5000, 5000));

            // 租约到期（claim 到期时刻 6000）后可接管：已成功玩家不在扫描作用域。
            var due = await store.FindDueAsync(6000, 10);
            var record = Assert.Single(due);
            Assert.Equal(3, record.RowVersion);
            Assert.Equal(1, record.Attempts);
            Assert.True(await store.TryClaimAsync("result-1", 3, 6000, 5000));
            Assert.Equal(OnlineMatchRewardDeliveryState.Failed, record.PlayerEntries[1].State);
            var stale = record.Copy();
            stale.RowVersion = 3;
            Assert.False(await store.TryWriteBackAsync(stale));

            // CAS 失配：旧版本写回不得覆盖接管者的推进。
            Assert.False(await store.TryWriteBackAsync(record));
        });
    }

    /// <summary>
    /// 验证存量「已结算但无 outbox」的历史结果不补建待发奖记录（禁止对历史奖励盲目补发）。
    /// </summary>
    [Fact]
    public async Task CommitWithOutbox_LegacyResultWithoutOutbox_ShouldNotCreateOutbox()
    {
        var dataFile = Path.Combine(_tempRoot, "legacy-result.db");
        Directory.CreateDirectory(_tempRoot);

        await WithSqliteFileFacadeAsync(dataFile, "c408-legacy", async () =>
        {
            var store = new GameDbOnlineMatchSettlementStore();

            // 先按无发奖路径落一份「历史结果」。
            var committed = await store.CommitAsync(BuildSampleResult("result-legacy", "match-legacy"));
            Assert.NotNull(committed);

            // 之后再对同一对局走带 outbox 提交：返回既有结果、不追加 outbox。
            var replay = await store.CommitWithOutboxAsync(BuildSampleResult("result-legacy-new", "match-legacy"));
            Assert.Equal("result-legacy", replay.MatchResultId);
            Assert.Null(await store.FindByMatchResultAsync("result-legacy"));
            Assert.Empty(await store.ListUndeliveredAsync());
        });
    }

    /// <summary>
    /// 验证全部玩家成功后的记录脱离未完成观测面（行物理长存，但不再进入扫描与 admin 投影）。
    /// </summary>
    [Fact]
    public async Task TryWriteBack_AllSucceeded_ShouldLeaveUndeliveredProjection()
    {
        await WithSqliteFacadeAsync(async () =>
        {
            var store = new GameDbOnlineMatchSettlementStore();
            await store.CommitWithOutboxAsync(BuildSampleResult("result-done", "match-done"));

            var due = await store.FindDueAsync(1000, 10);
            var record = Assert.Single(due);
            Assert.True(await store.TryClaimAsync("result-done", record.RowVersion, 1000, 5000));

            var working = record.Copy();
            working.RowVersion = record.RowVersion + 1;
            working.Attempts = 1;
            foreach (var entry in working.PlayerEntries)
            {
                entry.State = OnlineMatchRewardDeliveryState.Succeeded;
            }

            working.ClaimExpiresAtUnixMilliseconds = 0;
            Assert.True(await store.TryWriteBackAsync(working));

            Assert.Empty(await store.FindDueAsync(2000, 10));
            Assert.Empty(await store.ListUndeliveredAsync());
        });
    }

    /// <summary>构造样本结算结果（两玩家，各一条奖励条目）。</summary>
    /// <param name="matchResultId">结算结果标识。</param>
    /// <param name="matchId">对局标识。</param>
    /// <returns>样本结果。</returns>
    private static OnlineMatchResult BuildSampleResult(string matchResultId, string matchId)
    {
        return new OnlineMatchResult
        {
            MatchResultId = matchResultId,
            MatchId = matchId,
            TenantId = TenantId,
            AppId = AppId,
            ServerId = 9,
            Mode = 1,
            Region = 1,
            Outcome = OnlineMatchState.Completed,
            SettledTime = 500,
            Entries = new List<OnlineMatchResultEntry>
            {
                new OnlineMatchResultEntry { PlayerId = 3001, Rank = 1, IsWinner = true, Score = 2 },
                new OnlineMatchResultEntry { PlayerId = 3002, Rank = 2, IsWinner = false, Score = 0 },
            },
        };
    }

    /// <summary>以内存 SQLite 门面执行动作（GameDb 静态面测试约定：结束后关闭并重置）。</summary>
    /// <param name="action">测试动作。</param>
    /// <returns>完成通知。</returns>
    private static async Task WithSqliteFacadeAsync(Func<Task> action)
    {
        var opened = await GameDb.InitAsync<SqliteDbService>(new DbOptions
        {
            ConnectionString = "Data Source=:memory:",
            Name = "c408-outbox-tests",
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

    /// <summary>以文件 SQLite 门面执行动作（真实跨进程重启语义：close 后重开同文件）。</summary>
    /// <param name="dataFile">数据库文件路径。</param>
    /// <param name="name">数据库名。</param>
    /// <param name="action">测试动作。</param>
    /// <returns>完成通知。</returns>
    private static async Task WithSqliteFileFacadeAsync(string dataFile, string name, Func<Task> action)
    {
        var opened = await GameDb.InitAsync<SqliteDbService>(new DbOptions
        {
            ConnectionString = "Data Source=" + dataFile,
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

    /// <summary>
    /// 清理临时目录。
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, true);
        }
    }
}
