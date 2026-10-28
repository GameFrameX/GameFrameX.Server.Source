// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses!
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the full license text in the root directory's LICENSE file.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes and liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担任何责任。
//   shall be borne by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://www.cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Tests.Online;

using System.IO;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.Persistence.Social;
using GameFrameX.Capability.Online.Social;
using GameFrameX.DataBase;
using GameFrameX.DataBase.Abstractions;
using GameFrameX.DataBase.Sqlite;
using Xunit;

/// <summary>
/// 好友关系 GameDb 适配层测试。
/// </summary>
/// <remarks>
/// Tests for the GameDb adapter of the friendship store. Semantics facts run against
/// the static GameDb facade over embedded SQLite (in-memory mode) and anchor the store contract:
/// create-if-absent returning the existing record without overwriting, undirected-pair lookup in
/// both directions, CAS state transitions, renewal rewriting state/direction/expiry together,
/// per-player and per-state listings with tenant isolation, and soft-deleted records leaving the
/// Requested listing. The restart-acceptance fact runs SQLite in file mode and simulates a process
/// restart by closing the facade, re-initializing a fresh database service over the same file and
/// constructing a brand-new adapter instance — the adapter holds no in-process data cache, so
/// instance recreation is restart-equivalent by construction.
/// </remarks>
[Collection("GameDbStaticStateCollection")]
public sealed class GameDbOnlineFriendStorePersistenceTests
{
    /// <summary>
    /// 测试作用域租户标识。
    /// </summary>
    private const long TenantId = 7L;

    /// <summary>
    /// 测试作用域 App 标识。
    /// </summary>
    private const long AppId = 3L;

    /// <summary>
    /// 门面初始化后必须归还（关闭 + 复位静态注册表；串行集合保证静态门面独占）。
    /// </summary>
    private static async Task WithSqliteFacadeAsync(Func<Task> action, string name = "c244-friend-store-tests")
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

    /// <summary>
    /// SQLite 文件库门面（重启验收载体：关库 → 重开同文件 = 进程重启的数据层等价）。
    /// </summary>
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
    /// 构造待创建的好友关系（无向对已由调用方语义给定：low &lt; high）。
    /// </summary>
    private static OnlineFriendship BuildFriendship(long low, long high, OnlineFriendshipState state, string friendshipId)
    {
        return new OnlineFriendship
        {
            FriendshipId = friendshipId,
            TenantId = TenantId,
            AppId = AppId,
            ServerId = 5,
            LowPlayerId = low,
            HighPlayerId = high,
            RequesterId = low,
            AddresseeId = high,
            State = state,
            CreatedAtTime = 1000,
            UpdatedAtTime = 1001,
            ExpiresAtTime = state == OnlineFriendshipState.Requested ? 9000 : 0,
            RespondedAtTime = 0,
        };
    }

    /// <summary>
    /// 不存在则创建事实：新建返回入参副本；同键重复保存返回既有记录副本且**不覆盖**（方向 / 状态 / 时刻保持首写值）。
    /// </summary>
    [Fact]
    public async Task SaveIfAbsent_DuplicateKey_ShouldReturnExistingWithoutOverwrite()
    {
        await WithSqliteFacadeAsync(async () =>
        {
            var store = new GameDbOnlineFriendStore();

            // 新建：返回刚落库的入参。
            var created = await store.SaveIfAbsentAsync(BuildFriendship(11, 22, OnlineFriendshipState.Requested, "fs-1"));
            Assert.Equal("fs-1", created.FriendshipId);
            Assert.Equal(OnlineFriendshipState.Requested, created.State);
            Assert.Equal(11, created.LowPlayerId);
            Assert.Equal(22, created.HighPlayerId);

            // 同无向对重复保存（换方向 / 换状态 / 换时刻）：返回既有记录，绝不覆盖。
            var duplicate = await store.SaveIfAbsentAsync(BuildFriendship(11, 22, OnlineFriendshipState.Accepted, "fs-other"));
            Assert.Equal("fs-1", duplicate.FriendshipId);
            Assert.Equal(OnlineFriendshipState.Requested, duplicate.State);
            Assert.Equal(11, duplicate.RequesterId);
            Assert.Equal(1000, duplicate.CreatedAtTime);
            Assert.Equal(1001, duplicate.UpdatedAtTime);

            // 库内仅一行（ListByState 全状态面合计 1）。
            Assert.Single(await store.ListByPlayerAsync(TenantId, AppId, 11));
        });
    }

    /// <summary>
    /// 双向查找事实：A→B 与 B→A 入参命中同一条记录（规范化无向对键）。
    /// </summary>
    [Fact]
    public async Task Find_BothDirections_ShouldHitSameRecord()
    {
        await WithSqliteFacadeAsync(async () =>
        {
            var store = new GameDbOnlineFriendStore();
            await store.SaveIfAbsentAsync(BuildFriendship(11, 22, OnlineFriendshipState.Requested, "fs-1"));

            var forward = await store.FindAsync(TenantId, AppId, 11, 22);
            var backward = await store.FindAsync(TenantId, AppId, 22, 11);
            Assert.NotNull(forward);
            Assert.NotNull(backward);
            Assert.Equal("fs-1", forward.FriendshipId);
            Assert.Equal("fs-1", backward.FriendshipId);
            Assert.Null(await store.FindAsync(TenantId, AppId, 11, 33));
            Assert.Null(await store.FindAsync(999, AppId, 11, 22));
        });
    }

    /// <summary>
    /// 状态迁移 CAS 事实：期望态匹配则状态 / 更新时刻 / 答复时刻落定；期望态不符返回 null 且零写入；不存在同样返回 null。
    /// </summary>
    [Fact]
    public async Task UpdateState_CasSemantics_ShouldFailCleanlyOnMismatch()
    {
        await WithSqliteFacadeAsync(async () =>
        {
            var store = new GameDbOnlineFriendStore();
            var created = await store.SaveIfAbsentAsync(BuildFriendship(11, 22, OnlineFriendshipState.Requested, "fs-1"));

            // CAS 成功：Requested → Accepted，答复时刻回填。
            var accepted = await store.UpdateStateAsync(TenantId, AppId, new FriendshipStateTransition
            {
                FriendshipId = created.FriendshipId,
                ExpectedState = OnlineFriendshipState.Requested,
                NewState = OnlineFriendshipState.Accepted,
                NowUnixMilliseconds = 5000,
                Responded = true,
            });
            Assert.NotNull(accepted);
            Assert.Equal(OnlineFriendshipState.Accepted, accepted.State);
            Assert.Equal(5000, accepted.UpdatedAtTime);
            Assert.Equal(5000, accepted.RespondedAtTime);

            // 期望态不符（仍以 Requested 为期望）：返回 null，读回状态不变。
            var stale = await store.UpdateStateAsync(TenantId, AppId, new FriendshipStateTransition
            {
                FriendshipId = created.FriendshipId,
                ExpectedState = OnlineFriendshipState.Requested,
                NewState = OnlineFriendshipState.Rejected,
                NowUnixMilliseconds = 6000,
                Responded = true,
            });
            Assert.Null(stale);
            var reloaded = await store.FindAsync(TenantId, AppId, 22, 11);
            Assert.Equal(OnlineFriendshipState.Accepted, reloaded.State);
            Assert.Equal(5000, reloaded.UpdatedAtTime);

            // 作用域不符：返回 null。
            Assert.Null(await store.UpdateStateAsync(999, AppId, new FriendshipStateTransition
            {
                FriendshipId = created.FriendshipId,
                ExpectedState = OnlineFriendshipState.Accepted,
                NewState = OnlineFriendshipState.Rejected,
                NowUnixMilliseconds = 6000,
                Responded = true,
            }));

            // 记录不存在：返回 null。
            Assert.Null(await store.UpdateStateAsync(TenantId, AppId, new FriendshipStateTransition
            {
                FriendshipId = "fs-missing",
                ExpectedState = OnlineFriendshipState.Requested,
                NewState = OnlineFriendshipState.Accepted,
                NowUnixMilliseconds = 6000,
                Responded = true,
            }));
        });
    }

    /// <summary>
    /// 并发迁移原子性事实：同一关系的两个并发 Requested→Accepted 迁移恰一成功——RowVersion 条件写拦下
    /// 另一个（重读后期望态已变，按 CAS 失败返回 null），终态只推进一次、时刻只落一个写者的值。
    /// </summary>
    [Fact]
    public async Task UpdateState_ConcurrentSameTransition_ExactlyOneWinner()
    {
        await WithSqliteFacadeAsync(async () =>
        {
            var store = new GameDbOnlineFriendStore();
            var created = await store.SaveIfAbsentAsync(BuildFriendship(11, 22, OnlineFriendshipState.Requested, "fs-race"));

            var first = store.UpdateStateAsync(TenantId, AppId, new FriendshipStateTransition { FriendshipId = created.FriendshipId, ExpectedState = OnlineFriendshipState.Requested, NewState = OnlineFriendshipState.Accepted, NowUnixMilliseconds = 5000, Responded = true, });
            var second = store.UpdateStateAsync(TenantId, AppId, new FriendshipStateTransition { FriendshipId = created.FriendshipId, ExpectedState = OnlineFriendshipState.Requested, NewState = OnlineFriendshipState.Accepted, NowUnixMilliseconds = 6000, Responded = true, });
            var results = await Task.WhenAll(first, second);

            var winners = 0;
            foreach (var result in results)
            {
                if (result != null)
                {
                    winners++;
                    Assert.Equal(OnlineFriendshipState.Accepted, result.State);
                }
            }

            Assert.Equal(1, winners);

            var final = await store.FindAsync(TenantId, AppId, 11, 22);
            Assert.NotNull(final);
            Assert.Equal(OnlineFriendshipState.Accepted, final.State);

            // 版本事实：胜者的条件写把行版本从 0 推进到 1（旧「覆盖写」实现下版本恒为 0）。
            var row = await GameDb.FindAsync<OnlineFriendshipRowState>(state => state.FriendshipId == created.FriendshipId, isCreateIfNotExists: false);
            Assert.NotNull(row);
            Assert.Equal(1L, row.RowVersion);
        });
    }

    /// <summary>
    /// 存量行补全事实：RowVersion 为 null 的旧数据行首次迁移自动补全（null → 0）并成功走 CAS 落库，
    /// 补全后版本单调递增（0 → 1）。
    /// </summary>
    [Fact]
    public async Task UpdateState_LegacyNullRowVersion_ShouldBackfillAndSucceed()
    {
        await WithSqliteFacadeAsync(async () =>
        {
            var store = new GameDbOnlineFriendStore();

            // 模拟存量旧行：直接落库一行 RowVersion = null 的关系（绕开 store 的创建路径）。
            var legacy = CreateFriendshipRow(9001, "fs-legacy", (int)OnlineFriendshipState.Requested, null, 1001);
            await GameDb.AddOrUpdateAsync(legacy);

            var updated = await store.UpdateStateAsync(TenantId, AppId, new FriendshipStateTransition { FriendshipId = "fs-legacy", ExpectedState = OnlineFriendshipState.Requested, NewState = OnlineFriendshipState.Accepted, NowUnixMilliseconds = 5000, Responded = true, });
            Assert.NotNull(updated);
            Assert.Equal(OnlineFriendshipState.Accepted, updated.State);

            var row = await GameDb.FindAsync<OnlineFriendshipRowState>(state => state.FriendshipId == "fs-legacy", isCreateIfNotExists: false);
            Assert.NotNull(row);
            Assert.Equal(1L, row.RowVersion);
        });
    }

    /// <summary>
    /// 重新发起事实：状态 + 方向 + 有效期三项同临界区落定（方向跟随本次发起人翻转）。
    /// </summary>
    [Fact]
    public async Task RenewRequest_ShouldRewriteStateDirectionAndExpiryTogether()
    {
        await WithSqliteFacadeAsync(async () =>
        {
            var store = new GameDbOnlineFriendStore();
            var created = await store.SaveIfAbsentAsync(BuildFriendship(11, 22, OnlineFriendshipState.Requested, "fs-1"));

            // 先经 CAS 置为删除后的静止态（Removed）。
            await store.UpdateStateAsync(TenantId, AppId, new FriendshipStateTransition
            {
                FriendshipId = created.FriendshipId,
                ExpectedState = OnlineFriendshipState.Requested,
                NewState = OnlineFriendshipState.Removed,
                NowUnixMilliseconds = 4000,
                Responded = false,
            });

            // 重新发起：高标识侧为发起人，方向翻转 + 状态回 Requested + 有效期重置。
            var renewed = await store.RenewRequestAsync(TenantId, AppId, new FriendshipRenewal
            {
                FriendshipId = created.FriendshipId,
                ExpectedState = OnlineFriendshipState.Removed,
                RequesterId = 22,
                AddresseeId = 11,
                NowUnixMilliseconds = 7000,
                ExpiresAtTime = 12000,
            });
            Assert.NotNull(renewed);
            Assert.Equal(OnlineFriendshipState.Requested, renewed.State);
            Assert.Equal(22, renewed.RequesterId);
            Assert.Equal(11, renewed.AddresseeId);
            Assert.Equal(7000, renewed.UpdatedAtTime);
            Assert.Equal(12000, renewed.ExpiresAtTime);

            // 期望态不符：返回 null 且零写入。
            Assert.Null(await store.RenewRequestAsync(TenantId, AppId, new FriendshipRenewal
            {
                FriendshipId = created.FriendshipId,
                ExpectedState = OnlineFriendshipState.Removed,
                RequesterId = 11,
                AddresseeId = 22,
                NowUnixMilliseconds = 8000,
                ExpiresAtTime = 13000,
            }));
            var reloaded = await store.FindAsync(TenantId, AppId, 11, 22);
            Assert.Equal(OnlineFriendshipState.Requested, reloaded.State);
            Assert.Equal(22, reloaded.RequesterId);
            Assert.Equal(12000, reloaded.ExpiresAtTime);
        });
    }

    /// <summary>
    /// 玩家列举事实：低 / 高两侧均命中 + 跨租户隔离（他租户同对不串读）。
    /// </summary>
    [Fact]
    public async Task ListByPlayer_BothSidesAndTenantIsolation_ShouldFilter()
    {
        await WithSqliteFacadeAsync(async () =>
        {
            var store = new GameDbOnlineFriendStore();
            await store.SaveIfAbsentAsync(BuildFriendship(11, 22, OnlineFriendshipState.Accepted, "fs-1"));
            await store.SaveIfAbsentAsync(BuildFriendship(22, 33, OnlineFriendshipState.Requested, "fs-2"));

            // 双侧命中：22 同时在两条关系里，11 只在一条。
            Assert.Equal(2, (await store.ListByPlayerAsync(TenantId, AppId, 22)).Count);
            var byLow = await store.ListByPlayerAsync(TenantId, AppId, 11);
            var single = Assert.Single(byLow);
            Assert.Equal("fs-1", single.FriendshipId);

            // 跨租户隔离：他租户同对关系不可见。
            var otherTenant = BuildFriendship(11, 22, OnlineFriendshipState.Requested, "fs-other-tenant");
            otherTenant.TenantId = 999;
            await store.SaveIfAbsentAsync(otherTenant);
            Assert.Single(await store.ListByPlayerAsync(TenantId, AppId, 11));
            Assert.Single(await store.ListByPlayerAsync(999, AppId, 11));

            // 无关玩家：空列表。
            Assert.Empty(await store.ListByPlayerAsync(TenantId, AppId, 44));
        });
    }

    /// <summary>
    /// 状态列举事实：按状态过滤作用域内记录；软删（Deleted）后 ListByState(Requested) 不含该关系。
    /// </summary>
    [Fact]
    public async Task ListByState_FilterAndSoftDelete_ShouldExcludeDeletedFromRequested()
    {
        await WithSqliteFacadeAsync(async () =>
        {
            var store = new GameDbOnlineFriendStore();
            await store.SaveIfAbsentAsync(BuildFriendship(11, 22, OnlineFriendshipState.Requested, "fs-1"));
            await store.SaveIfAbsentAsync(BuildFriendship(33, 44, OnlineFriendshipState.Requested, "fs-2"));
            await store.SaveIfAbsentAsync(BuildFriendship(55, 66, OnlineFriendshipState.Accepted, "fs-3"));

            Assert.Equal(2, (await store.ListByStateAsync(TenantId, AppId, OnlineFriendshipState.Requested)).Count);
            Assert.Single(await store.ListByStateAsync(TenantId, AppId, OnlineFriendshipState.Accepted));

            // 软删 fs-1：Requested 面只剩 fs-2，Removed 面出现 fs-1（行物理长存）。
            await store.UpdateStateAsync(TenantId, AppId, new FriendshipStateTransition
            {
                FriendshipId = "fs-1",
                ExpectedState = OnlineFriendshipState.Requested,
                NewState = OnlineFriendshipState.Removed,
                NowUnixMilliseconds = 8000,
                Responded = false,
            });
            var requested = await store.ListByStateAsync(TenantId, AppId, OnlineFriendshipState.Requested);
            var remaining = Assert.Single(requested);
            Assert.Equal("fs-2", remaining.FriendshipId);
            Assert.Single(await store.ListByStateAsync(TenantId, AppId, OnlineFriendshipState.Removed));
            Assert.NotNull(await store.FindAsync(TenantId, AppId, 11, 22));
        });
    }

    /// <summary>
    /// 重启等价事实：文件库上写入 → 关库重开同文件 + 新 store 实例 → 数据仍在且语义不变（含 CAS 续算）。
    /// </summary>
    [Fact]
    public async Task RestartEquivalence_DataSurvivesFreshStoreInstance()
    {
        var dataFile = Path.Combine(Path.GetTempPath(), "c244-friend-restart-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            await WithSqliteFileFacadeAsync(dataFile, "c244-friend-restart-a", async () =>
            {
                var store = new GameDbOnlineFriendStore();
                await store.SaveIfAbsentAsync(BuildFriendship(11, 22, OnlineFriendshipState.Requested, "fs-1"));
            });

            // 关库重开同文件 + 全新 store 实例 = 进程重启的数据层等价。
            await WithSqliteFileFacadeAsync(dataFile, "c244-friend-restart-b", async () =>
            {
                var store = new GameDbOnlineFriendStore();
                var reloaded = await store.FindAsync(TenantId, AppId, 22, 11);
                Assert.NotNull(reloaded);
                Assert.Equal("fs-1", reloaded.FriendshipId);
                Assert.Equal(OnlineFriendshipState.Requested, reloaded.State);
                Assert.Equal(11, reloaded.RequesterId);
                Assert.Equal(9000, reloaded.ExpiresAtTime);
                Assert.Single(await store.ListByStateAsync(TenantId, AppId, OnlineFriendshipState.Requested));

                // 重启后语义不变：重复保存仍返回既有（不覆盖），CAS 仍可推进状态。
                var duplicate = await store.SaveIfAbsentAsync(BuildFriendship(11, 22, OnlineFriendshipState.Accepted, "fs-other"));
                Assert.Equal("fs-1", duplicate.FriendshipId);
                var accepted = await store.UpdateStateAsync(TenantId, AppId, new FriendshipStateTransition
                {
                    FriendshipId = "fs-1",
                    ExpectedState = OnlineFriendshipState.Requested,
                    NewState = OnlineFriendshipState.Accepted,
                    NowUnixMilliseconds = 9500,
                    Responded = true,
                });
                Assert.NotNull(accepted);
                Assert.Equal(9500, accepted.RespondedAtTime);
            });
        }
        finally
        {
            File.Delete(dataFile);
        }
    }

    /// <summary>
    /// 构造好友关系行（玩家对 11/22、租户 / App / 区服固定；行标识、状态、版本与更新时刻可变）。
    /// </summary>
    /// <param name="id">行 Id。</param>
    /// <param name="friendshipId">关系标识。</param>
    /// <param name="state">关系状态。</param>
    /// <param name="rowVersion">行版本（null = 存量旧行）。</param>
    /// <param name="updatedAtTime">更新时刻。</param>
    /// <returns>关系行。</returns>
    internal static OnlineFriendshipRowState CreateFriendshipRow(long id, string friendshipId, int state, long? rowVersion, long updatedAtTime)
    {
        return new OnlineFriendshipRowState
        {
            Id = id,
            UniqueKey = "friend:" + TenantId + ":" + AppId + ":11:22",
            FriendshipId = friendshipId,
            TenantId = TenantId,
            AppId = AppId,
            ServerId = 5,
            LowPlayerId = 11,
            HighPlayerId = 22,
            RequesterId = 11,
            AddresseeId = 22,
            State = state,
            CreatedAtTime = 1000,
            UpdatedAtTime = updatedAtTime,
            ExpiresAtTime = 9000,
            RespondedAtTime = 0,
            RowVersion = rowVersion,
        };
    }
}
