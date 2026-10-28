// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
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

using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Idempotency;
using GameFrameX.Capability.Online.Abstractions.Scope;
using GameFrameX.Capability.Online.Assets;
using GameFrameX.Capability.Online.Foundation.Identity;
using GameFrameX.Capability.Online.Foundation.Storage;
using GameFrameX.Capability.Online.Persistence;
using GameFrameX.Capability.Online.Abstractions.Events;
using GameFrameX.Capability.Online.Persistence.Assets;
using GameFrameX.Capability.Online.Persistence.Identity;
using GameFrameX.Capability.Online.Persistence.Storage;
using GameFrameX.DataBase;
using GameFrameX.DataBase.Abstractions;
using GameFrameX.DataBase.Sqlite;
using GameFrameX.Foundation.Idempotency;
using Xunit;

/// <summary>
/// Online 一级 store 的 GameDb 适配层测试（Identity / PlayerStorage / 资产账本三域）。
/// </summary>
/// <remarks>
/// Tests for the GameDb adapters of the Online level-one stores (identity, player storage
/// and the asset ledger). Semantics facts run against the static GameDb facade over embedded
/// SQLite; asset write paths drive the real unified entry (<see cref="OnlineGrantService"/>)
/// because change batches are assembly-internal by contract. The restart-acceptance facts run
/// SQLite in file mode and simulate a process restart by closing the facade, re-initializing a
/// fresh database service over the same file and constructing brand-new adapter instances —
/// adapters hold no in-process data cache, so instance recreation is restart-equivalent by
/// construction (write → restart → read back identical).
/// </remarks>
[Collection("GameDbStaticStateCollection")]
public sealed class OnlineGameDbStorePersistenceTests
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
    private static async Task WithSqliteFacadeAsync(Func<Task> action, string name = "c249-store-tests")
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
    /// 身份域全生命周期事实：四元组唯一查找 / 覆盖写 / 归属列举 / 设备二元组 / 自然主键覆盖。
    /// </summary>
    [Fact]
    public async Task IdentityStore_FullCycle_ShouldRoundTrip()
    {
        await WithSqliteFacadeAsync(async () =>
        {
            var store = new GameDbOnlineIdentityStore();

            // 未命中返回 null。
            Assert.Null(await store.FindIdentityAsync(TenantId, AppId, OnlineIdentityKind.Email, "user@example.com"));

            // 新建 → 按唯一键读回全字段。
            var identity = new OnlineIdentity
            {
                Id = "idn-1",
                TenantId = TenantId,
                AppId = AppId,
                Kind = OnlineIdentityKind.Email,
                Identifier = "user@example.com",
                GameAccountId = 101,
                CreatedAtTime = 1000,
                UpdatedAtTime = 1001,
                UnboundAtTime = 0,
            };
            await store.AddOrUpdateIdentityAsync(identity);
            var loaded = await store.FindIdentityAsync(TenantId, AppId, OnlineIdentityKind.Email, "user@example.com");
            Assert.NotNull(loaded);
            Assert.Equal("idn-1", loaded.Id);
            Assert.Equal(101, loaded.GameAccountId);
            Assert.Equal(OnlineIdentityKind.Email, loaded.Kind);
            Assert.Equal(1000, loaded.CreatedAtTime);

            // 同四元组覆盖写（换绑账号）：仍为一行、字段覆盖。
            identity.GameAccountId = 202;
            identity.UpdatedAtTime = 2000;
            await store.AddOrUpdateIdentityAsync(identity);
            var rebound = await store.FindIdentityAsync(TenantId, AppId, OnlineIdentityKind.Email, "user@example.com");
            Assert.Equal(202, rebound.GameAccountId);
            Assert.Single(await store.ListIdentitiesAsync(202));

            // 账号 / 玩家档案：自然主键覆盖写 + 归属列举按创建时间升序。
            await store.AddOrUpdateAccountAsync(new OnlineGameAccount { Id = 101, TenantId = TenantId, AppId = AppId, Status = OnlineGameAccountStatus.Active, CreatedAtTime = 1, UpdatedAtTime = 2, });
            Assert.Equal(OnlineGameAccountStatus.Active, (await store.FindAccountAsync(101)).Status);
            await store.AddOrUpdatePlayerAsync(new OnlinePlayerProfile { Id = 1, GameAccountId = 101, TenantId = TenantId, AppId = AppId, ServerId = 5, Name = "p1", CreatedAtTime = 10, });
            await store.AddOrUpdatePlayerAsync(new OnlinePlayerProfile { Id = 2, GameAccountId = 101, TenantId = TenantId, AppId = AppId, ServerId = 5, Name = "p2", CreatedAtTime = 20, });
            await store.AddOrUpdatePlayerAsync(new OnlinePlayerProfile { Id = 3, GameAccountId = 101, TenantId = TenantId, AppId = AppId, ServerId = 6, Name = "other-server", CreatedAtTime = 30, });
            Assert.Equal(new long[] { 1, 2 }, (await store.ListPlayersAsync(101, AppId, 5)).Select(player => player.Id).ToArray());
            Assert.Equal(new long[] { 1, 2, 3 }, (await store.ListAllPlayersAsync(101)).Select(player => player.Id).ToArray());

            // 设备二元组唯一键：新建 → 覆盖读回。
            await store.AddOrUpdateDeviceAsync(new OnlinePlayerDevice { Id = "dev-1", GameAccountId = 101, DeviceIdentifier = "device-a", Platform = "ios", BoundAtTime = 100, });
            Assert.Equal("ios", (await store.FindDeviceAsync(101, "device-a")).Platform);
            await store.AddOrUpdateDeviceAsync(new OnlinePlayerDevice { Id = "dev-1", GameAccountId = 101, DeviceIdentifier = "device-a", Platform = "android", BoundAtTime = 100, LastActiveAtTime = 200, });
            Assert.Equal("android", (await store.FindDeviceAsync(101, "device-a")).Platform);
            Assert.Null(await store.FindDeviceAsync(101, "device-b"));
        });
    }

    /// <summary>
    /// 身份序列接管事实：账号 / 玩家标识经数据库序列单调递增。
    /// </summary>
    [Fact]
    public async Task IdentityStore_Sequences_ShouldBeMonotonic()
    {
        await WithSqliteFacadeAsync(async () =>
        {
            var store = new GameDbOnlineIdentityStore();
            Assert.Equal(1L, await store.NextAccountIdAsync());
            Assert.Equal(2L, await store.NextAccountIdAsync());
            Assert.Equal(1L, await store.NextPlayerIdAsync());
            Assert.Equal(2L, await store.NextPlayerIdAsync());
            Assert.Equal(3L, await store.NextAccountIdAsync());
        });
    }

    /// <summary>
    /// 身份条件写事实：唯一插入冲突零覆写；CAS 匹配写入推进行版本、过期版本拒绝；盲写保留版本。
    /// </summary>
    [Fact]
    public async Task IdentityStore_ConditionalWrites_ShouldMatchContract()
    {
        await WithSqliteFacadeAsync(async () =>
        {
            var store = new GameDbOnlineIdentityStore();
            var identity = new OnlineIdentity
            {
                Id = "idn-cas-1",
                TenantId = TenantId,
                AppId = AppId,
                Kind = OnlineIdentityKind.Email,
                Identifier = "cas@example.com",
                GameAccountId = 101,
                CreatedAtTime = 1000,
                UpdatedAtTime = 1000,
                UnboundAtTime = 0,
            };

            // 唯一插入：首插成功建档版本 1；同键冲突 false 且零覆写。
            Assert.True(await store.TryAddIdentityAsync(identity));
            var winner = await store.FindIdentityAsync(TenantId, AppId, OnlineIdentityKind.Email, "cas@example.com");
            Assert.Equal(1, winner.RowVersion);
            var contender = new OnlineIdentity { Id = "idn-cas-2", TenantId = TenantId, AppId = AppId, Kind = OnlineIdentityKind.Email, Identifier = "cas@example.com", GameAccountId = 202, CreatedAtTime = 1000, UpdatedAtTime = 1000, };
            Assert.False(await store.TryAddIdentityAsync(contender));
            Assert.Equal(101, (await store.FindIdentityAsync(TenantId, AppId, OnlineIdentityKind.Email, "cas@example.com")).GameAccountId);

            // CAS：匹配版本写入并推进到 2；过期版本拒绝零写入。
            var rebound = new OnlineIdentity { Id = "idn-cas-1", TenantId = TenantId, AppId = AppId, Kind = OnlineIdentityKind.Email, Identifier = "cas@example.com", GameAccountId = 202, CreatedAtTime = 1000, UpdatedAtTime = 2000, UnboundAtTime = 0, };
            Assert.True(await store.TryUpdateIdentityIfRowVersionAsync(rebound, winner.RowVersion));
            var updated = await store.FindIdentityAsync(TenantId, AppId, OnlineIdentityKind.Email, "cas@example.com");
            Assert.Equal(202, updated.GameAccountId);
            Assert.Equal(2, updated.RowVersion);
            var stale = new OnlineIdentity { Id = "idn-cas-1", TenantId = TenantId, AppId = AppId, Kind = OnlineIdentityKind.Email, Identifier = "cas@example.com", GameAccountId = 303, CreatedAtTime = 1000, UpdatedAtTime = 3000, UnboundAtTime = 0, };
            Assert.False(await store.TryUpdateIdentityIfRowVersionAsync(stale, 1));
            Assert.Equal(202, (await store.FindIdentityAsync(TenantId, AppId, OnlineIdentityKind.Email, "cas@example.com")).GameAccountId);

            // 盲写：覆盖账号但保留存量行版本（不推进、不抹空）。
            var blind = new OnlineIdentity { Id = "idn-cas-1", TenantId = TenantId, AppId = AppId, Kind = OnlineIdentityKind.Email, Identifier = "cas@example.com", GameAccountId = 404, CreatedAtTime = 1000, UpdatedAtTime = 4000, UnboundAtTime = 0, };
            await store.AddOrUpdateIdentityAsync(blind);
            var afterBlindWrite = await store.FindIdentityAsync(TenantId, AppId, OnlineIdentityKind.Email, "cas@example.com");
            Assert.Equal(404, afterBlindWrite.GameAccountId);
            Assert.Equal(2, afterBlindWrite.RowVersion);
        });
    }

    /// <summary>
    /// 身份遗留无版本行领养事实：历史盲写行（RowVersion 为 null）以期望版本 0 一次性领养进入版本化 regime。
    /// </summary>
    [Fact]
    public async Task IdentityStore_LegacyRowWithoutVersion_AdoptionThenCas_ShouldConverge()
    {
        await WithSqliteFacadeAsync(async () =>
        {
            var store = new GameDbOnlineIdentityStore();

            // 直接落一行无版本的遗留行（绕过适配层，模拟历史盲写产物）。
            var rowId = await GameDb.NextSequenceAsync<OnlineIdentityState>("row-id");
            var legacyState = new OnlineIdentityState
            {
                Id = rowId,
                UniqueKey = TenantId + ":" + AppId + ":" + (int)OnlineIdentityKind.Email + ":" + "legacy@example.com",
                EntityId = "idn-legacy-1",
                TenantId = TenantId,
                AppId = AppId,
                Kind = (int)OnlineIdentityKind.Email,
                Identifier = "legacy@example.com",
                GameAccountId = 101,
                CreatedAtTime = 1000,
                UpdatedAtTime = 1000,
                UnboundAtTime = 500,
            };
            await GameDb.AddOrUpdateAsync(legacyState);

            // 读回映射为版本 0；以期望 0 CAS（领养）成功并播种版本 1；此后正常 CAS 生效。
            var legacy = await store.FindIdentityAsync(TenantId, AppId, OnlineIdentityKind.Email, "legacy@example.com");
            Assert.NotNull(legacy);
            Assert.Equal(0, legacy.RowVersion);
            var adopted = new OnlineIdentity { Id = legacy.Id, TenantId = TenantId, AppId = AppId, Kind = OnlineIdentityKind.Email, Identifier = "legacy@example.com", GameAccountId = 202, CreatedAtTime = legacy.CreatedAtTime, UpdatedAtTime = 2000, UnboundAtTime = 0, };
            Assert.True(await store.TryUpdateIdentityIfRowVersionAsync(adopted, 0));
            var afterAdoption = await store.FindIdentityAsync(TenantId, AppId, OnlineIdentityKind.Email, "legacy@example.com");
            Assert.Equal(202, afterAdoption.GameAccountId);
            Assert.Equal(1, afterAdoption.RowVersion);
            var nextWrite = new OnlineIdentity { Id = legacy.Id, TenantId = TenantId, AppId = AppId, Kind = OnlineIdentityKind.Email, Identifier = "legacy@example.com", GameAccountId = 303, CreatedAtTime = legacy.CreatedAtTime, UpdatedAtTime = 3000, UnboundAtTime = 0, };
            Assert.True(await store.TryUpdateIdentityIfRowVersionAsync(nextWrite, 1));
            Assert.Equal(303, (await store.FindIdentityAsync(TenantId, AppId, OnlineIdentityKind.Email, "legacy@example.com")).GameAccountId);
        });
    }

    /// <summary>
    /// 玩家云存储 CAS / 软删 / 复活事实。
    /// </summary>
    [Fact]
    public async Task StorageStore_CreateCasSoftDeleteRevive_ShouldMatchContract()
    {
        await WithSqliteFacadeAsync(async () =>
        {
            var store = new GameDbOnlinePlayerStorageStore();
            var key = new OnlineStorageEntryKey { TenantId = TenantId, AppId = AppId, PlayerId = 9, Collection = "settings", Key = "volume", };

            // 创建（expected=0）：成功且读回 Version == 1（行 RowVersion 0 + 校准恒等式）。
            Assert.True(await store.UpsertAsync(BuildEntry(key, new byte[] { 1, 2, 3 }, 1), 0));
            var created = await store.FindAsync(key);
            Assert.Equal(1, created.Version);
            Assert.Equal(new byte[] { 1, 2, 3 }, created.Payload);

            // 重复创建拒绝。
            Assert.False(await store.UpsertAsync(BuildEntry(key, new byte[] { 9 }, 1), 0));

            // CAS 版本推进：expected=1 → 目标 Version=2。
            Assert.True(await store.UpsertAsync(BuildEntry(key, new byte[] { 4, 5 }, 2), 1));
            Assert.Equal(2, (await store.FindAsync(key)).Version);

            // 过期版本拒绝 + 读值不变。
            Assert.False(await store.UpsertAsync(BuildEntry(key, new byte[] { 9 }, 3), 1));
            Assert.Equal(2, (await store.FindAsync(key)).Version);

            // 行缺失 CAS 拒绝。
            var missingKey = new OnlineStorageEntryKey { TenantId = TenantId, AppId = AppId, PlayerId = 9, Collection = "settings", Key = "missing", };
            Assert.False(await store.UpsertAsync(BuildEntry(missingKey, new byte[] { 1 }, 1), 1));

            // 软删：Find 可见 / List 不可见 / 活跃键数排除；ListAll 含软删（物理保留）。
            var softDeleted = BuildEntry(key, new byte[] { 4, 5 }, 3);
            softDeleted.DeletedAtTime = 5000;
            Assert.True(await store.UpsertAsync(softDeleted, 2));
            Assert.NotNull(await store.FindAsync(key));
            Assert.Empty(await ListAsync(store, 9, "settings"));
            Assert.Equal(0, await store.CountActiveKeysAsync(TenantId, AppId, 9, "settings"));
            Assert.Single(await store.ListAllAsync());

            // 复活：以软删条目版本（3）为期望 CAS 写回可见行。
            Assert.True(await store.UpsertAsync(BuildEntry(key, new byte[] { 6, 7 }, 4), 3));
            var listed = await ListAsync(store, 9, "settings");
            Assert.Single(listed);
            Assert.Equal(4, listed[0].Version);
            Assert.Equal(0, listed[0].DeletedAtTime);
            Assert.Equal(1, await store.CountActiveKeysAsync(TenantId, AppId, 9, "settings"));
        });
    }

    /// <summary>
    /// 玩家云存储服务级复活事实（GameDb 适配实景）：复活返回版本 = 软删末版 + 1 且与读回一致（恒等式自洽），
    /// 以返回版本续写成功、以旧版本续写冲突（C404：复活不重置版本，杜绝返回 1 / 读回 N 的分裂与版本倒退 ABA）。
    /// </summary>
    [Fact]
    public async Task StorageService_ResurrectThroughGameDb_ShouldContinueVersionMonotonically()
    {
        await WithSqliteFacadeAsync(async () =>
        {
            var service = new OnlinePlayerStorageService(new GameDbOnlinePlayerStorageStore(), new OnlinePlayerStorageOptions());
            var scope = new OnlineScope(TenantId, AppId, 5, 21);

            // 创建 v1 → 软删 v2（DeleteAsync 版本推进）。
            var created = await service.WriteAsync(scope, "settings", "theme", new byte[] { 1 }, 0, "sess-1");
            Assert.True(created.IsSuccess);
            Assert.Equal(1, created.Data.Version);
            Assert.True((await service.DeleteAsync(scope, "settings", "theme")).IsSuccess);

            // 复活：返回版本 = 软删末版 + 1，且与读回版本一致（旧缺陷：返回 1、读回 3）。
            var resurrected = await service.WriteAsync(scope, "settings", "theme", new byte[] { 2 }, 0, "sess-2");
            var readBack = await service.ReadAsync(scope, "settings", "theme");
            Assert.True(resurrected.IsSuccess);
            Assert.Equal(3, resurrected.Data.Version);
            Assert.True(readBack.IsSuccess);
            Assert.Equal(resurrected.Data.Version, readBack.Data.Version);
            Assert.Equal(new byte[] { 2 }, readBack.Data.Payload);

            // 以返回版本续写成功（→ v4）；持旧版本 1 续写被拒（无 ABA 窗口）。
            var continueWrite = await service.WriteAsync(scope, "settings", "theme", new byte[] { 3 }, resurrected.Data.Version, "sess-2");
            var staleWrite = await service.WriteAsync(scope, "settings", "theme", new byte[] { 9 }, 1, "sess-3");
            Assert.True(continueWrite.IsSuccess);
            Assert.Equal(resurrected.Data.Version + 1, continueWrite.Data.Version);
            Assert.False(staleWrite.IsSuccess);
            Assert.Equal(OnlineErrorCode.VersionConflict, staleWrite.Code);
        });
    }


    /// <summary>
    /// 玩家云存储游标分页事实：ordinal 字典序 + 排他游标不漏不重。
    /// </summary>
    [Fact]
    public async Task StorageStore_ListCursorPaging_ShouldBeOrdinalWithoutSkipOrDup()
    {
        await WithSqliteFacadeAsync(async () =>
        {
            var store = new GameDbOnlinePlayerStorageStore();

            // 大小写混合键 + 非连续插入顺序，覆盖 ordinal 语义（'B'(66) &lt; 'a'(97)）。
            var keys = new[] { "a", "B", "b", "Aa", "c", "z9", "Z", };
            foreach (var entryKey in keys)
            {
                var payloadKey = new OnlineStorageEntryKey { TenantId = TenantId, AppId = AppId, PlayerId = 11, Collection = "archive", Key = entryKey, };
                Assert.True(await store.UpsertAsync(BuildEntry(payloadKey, new byte[] { 1 }, 1), 0));
            }

            // 另一集合的数据不得串页。
            var otherKey = new OnlineStorageEntryKey { TenantId = TenantId, AppId = AppId, PlayerId = 11, Collection = "other", Key = "a", };
            Assert.True(await store.UpsertAsync(BuildEntry(otherKey, new byte[] { 1 }, 1), 0));

            // 全量分页（页大小 3）收集全部键，无重无漏、序 = ordinal 字典序。
            var collected = new List<string>();
            string cursor = string.Empty;
            while (true)
            {
                var page = await store.ListAsync(new OnlineStorageListQuery { TenantId = TenantId, AppId = AppId, PlayerId = 11, Collection = "archive", AfterKey = cursor, MaxCount = 3, });
                if (page.Count == 0)
                {
                    break;
                }

                collected.AddRange(page.Select(entry => entry.Key));
                cursor = page[^1].Key;
            }

            var expected = keys.OrderBy(candidate => candidate, StringComparer.Ordinal).ToArray();
            Assert.Equal(expected, collected.ToArray());
        });
    }

    /// <summary>
    /// 资产域事实：统一入口落账 → 快照/账本/交易三面一致；余额不足整批净效应 0；账本序连续。
    /// </summary>
    [Fact]
    public async Task AssetStore_GrantPipeline_ShouldPersistSnapshotsLedgerAndTransactions()
    {
        await WithSqliteFacadeAsync(async () =>
        {
            var assetStore = new GameDbOnlineAssetStore();
            var transactionStore = new GameDbOnlineAssetTransactionStore();
            var (grantService, scope) = BuildGrantService(assetStore, transactionStore, playerId: 21);

            // 首批：两货币 + 一道具（一笔交易三行）。
            var first = await grantService.ExecuteAsync(Request(scope, "g3-first", new (OnlineAssetKind, string, long)[]
            {
                (OnlineAssetKind.Currency, "gold", 100),
                (OnlineAssetKind.Currency, "gem", 5),
                (OnlineAssetKind.Item, "sword", 2),
            }));
            Assert.True(first.IsSuccess);
            var firstTransactionId = first.Data.TransactionId;

            // 快照读回（直查 store）。
            var gold = await assetStore.FindWalletAsync(TenantId, AppId, 21, "gold");
            Assert.Equal(100, gold.Balance);
            Assert.Equal(1, gold.Version);
            Assert.Equal(2, (await assetStore.FindInventoryAsync(TenantId, AppId, 21, "sword")).Quantity);
            Assert.Equal(new[] { "gem", "gold" }, (await assetStore.ListWalletAccountsAsync(TenantId, AppId, 21)).Select(wallet => wallet.CurrencyId).ToArray());

            // 余额不足整批拒绝：净效应 0（无新账本行、快照不动），交易落 Failed。
            var overdrawn = await grantService.ExecuteAsync(Request(scope, "g3-overdrawn", new (OnlineAssetKind, string, long)[]
            {
                (OnlineAssetKind.Currency, "gold", 10),
                (OnlineAssetKind.Currency, "gem", -999),
            }));
            Assert.False(overdrawn.IsSuccess);
            Assert.Equal(OnlineErrorCode.StateOperationForbidden, overdrawn.Code);
            Assert.Equal(100, (await assetStore.FindWalletAsync(TenantId, AppId, 21, "gold")).Balance);
            Assert.Equal(3, (await assetStore.ListLedgerEntriesAsync(new OnlineLedgerPageQuery { TenantId = TenantId, AppId = AppId, PlayerId = 21, AfterSequenceNumber = 0, MaxCount = 100, })).Count);

            // 同一交易内资产重复：服务层参数拒绝。
            var duplicate = await grantService.ExecuteAsync(Request(scope, "g3-duplicate", new (OnlineAssetKind, string, long)[]
            {
                (OnlineAssetKind.Currency, "gold", 1),
                (OnlineAssetKind.Currency, "gold", 2),
            }));
            Assert.False(duplicate.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, duplicate.Code);

            // 第二批：玩家维度账本序连续（4、5）+ 快照版本递增。
            var second = await grantService.ExecuteAsync(Request(scope, "g3-second", new (OnlineAssetKind, string, long)[]
            {
                (OnlineAssetKind.Currency, "gold", -30),
                (OnlineAssetKind.Item, "sword", 1),
            }));
            Assert.True(second.IsSuccess);
            // 账本序单调不稠密：拒绝批在预演期已取号作废（gold 行 seq 4），第二批续 5、6——与 InMemory 预演取号语义逐语句等价。
            var ledger = await assetStore.ListLedgerEntriesAsync(new OnlineLedgerPageQuery { TenantId = TenantId, AppId = AppId, PlayerId = 21, AfterSequenceNumber = 0, MaxCount = 100, });
            Assert.Equal(new long[] { 1, 2, 3, 5, 6 }, ledger.Select(entry => entry.SequenceNumber).ToArray());
            gold = await assetStore.FindWalletAsync(TenantId, AppId, 21, "gold");
            Assert.Equal(70, gold.Balance);
            Assert.Equal(2, gold.Version);

            // 账本序游标分页 + 交易反查 + 聚合 + 玩家清单。
            var tail = await assetStore.ListLedgerEntriesAsync(new OnlineLedgerPageQuery { TenantId = TenantId, AppId = AppId, PlayerId = 21, AfterSequenceNumber = 4, MaxCount = 2, });
            Assert.Equal(new long[] { 5, 6 }, tail.Select(entry => entry.SequenceNumber).ToArray());
            Assert.Equal(new long[] { 1, 2, 3 }, (await assetStore.FindLedgerEntriesByTransactionIdAsync(firstTransactionId)).Select(entry => entry.SequenceNumber).ToArray());
            var sums = await assetStore.SumLedgerDeltasByAssetAsync(TenantId, AppId, 21);
            Assert.Equal(70, sums["c:gold"]);
            Assert.Equal(5, sums["c:gem"]);
            Assert.Equal(3, sums["i:sword"]);
            Assert.Equal(new long[] { 21 }, (await assetStore.ListPlayerIdsAsync(TenantId, AppId)).ToArray());

            // 交易读回：成功交易终态 Succeeded + 外键列表；按状态列举含 Failed 拒绝单。
            var settled = await transactionStore.FindAsync(firstTransactionId);
            Assert.Equal(OnlineAssetTransactionState.Succeeded, settled.State);
            Assert.Equal(3, settled.AppliedLedgerEntryIds.Count);
            var failed = await transactionStore.ListByStateAsync(OnlineAssetTransactionState.Failed);
            Assert.Contains(failed, transaction => transaction.FailureMessage.Contains("资产不足"));

            // 交易记录适配器直写覆盖（SaveAsync 整体覆盖语义）：状态迁移。
            settled.State = OnlineAssetTransactionState.CompensationPending;
            settled.FailureMessage = "manual-drill";
            await transactionStore.SaveAsync(settled);
            Assert.Equal(OnlineAssetTransactionState.CompensationPending, (await transactionStore.FindAsync(firstTransactionId)).State);
        });
    }

    /// <summary>
    /// 并发验收（GFX-1109 场景 1）：两个独立 store 实例（不共享分片信号量、共享同一 GameDb）
    /// 同时给同一钱包各 +10——最终余额 = 初始 + 20，账本链连续（每条 AmountBefore 等于上一条 AmountAfter）。
    /// </summary>
    /// <remarks>
    /// Two independent store instances (separate shard semaphores over one shared GameDb facade) apply
    /// +10 concurrently to the same wallet: the final balance must be initial + 20 and the per-asset
    /// ledger chain must stay contiguous — the loser of a snapshot conditional write rolls the whole
    /// attempt back and re-plans from the winner's snapshot instead of overwriting it.
    /// </remarks>
    [Fact]
    public async Task AssetStore_TwoInstancesConcurrentApply_ShouldNotLoseUpdates()
    {
        await WithSqliteFacadeAsync(async () =>
        {
            // 预置初始余额 100（经 store A 的统一入口，正常建档 rv=0）。
            var storeOne = new GameDbOnlineAssetStore();
            var (serviceOne, scopeOne) = BuildGrantService(storeOne, new GameDbOnlineAssetTransactionStore(), playerId: 31);
            var seed = await serviceOne.ExecuteAsync(Request(scopeOne, "g1109-seed", new[] { (OnlineAssetKind.Currency, "gold", 100L) }));
            Assert.True(seed.IsSuccess);
            Assert.Equal(100, (await storeOne.FindWalletAsync(TenantId, AppId, 31, "gold")).Balance);

            // 两实例并发各 +10：分片信号量各自独立，唯一串行化点是数据库事务。
            var storeTwo = new GameDbOnlineAssetStore();
            var (serviceTwo, scopeTwo) = BuildGrantService(storeTwo, new GameDbOnlineAssetTransactionStore(), playerId: 31);
            var results = await Task.WhenAll(
                Task.Run(() => serviceOne.ExecuteAsync(Request(scopeOne, "g1109-a", new[] { (OnlineAssetKind.Currency, "gold", 10L) }))),
                Task.Run(() => serviceTwo.ExecuteAsync(Request(scopeTwo, "g1109-b", new[] { (OnlineAssetKind.Currency, "gold", 10L) }))));
            Assert.All(results, result => Assert.True(result.IsSuccess));

            var wallet = await storeOne.FindWalletAsync(TenantId, AppId, 31, "gold");
            Assert.Equal(120, wallet.Balance);

            // 账本链连续：预置 1 条 + 并发 2 条，按序每条 AmountBefore = 上一条 AmountAfter（无孤儿/断链）。
            var ledger = (await storeOne.ListLedgerEntriesAsync(new OnlineLedgerPageQuery { TenantId = TenantId, AppId = AppId, PlayerId = 31, AfterSequenceNumber = 0, MaxCount = 100, })).OrderBy(entry => entry.SequenceNumber).ToArray();
            Assert.Equal(3, ledger.Length);
            Assert.Equal(0L, ledger[0].AmountBefore);
            for (var i = 1; i < ledger.Length; i++)
            {
                Assert.Equal(ledger[i - 1].AmountAfter, ledger[i].AmountBefore);
            }

            Assert.Equal(120, ledger[ledger.Length - 1].AmountAfter);
        });
    }

    /// <summary>
    /// 并发验收（GFX-1109 场景 2）：两实例同时对不存在的钱包首次建档——只生成一行快照，两笔都入账且链连续。
    /// </summary>
    /// <remarks>
    /// First-create race: two instances concurrently create the same missing wallet — exactly one
    /// snapshot row lands and both changes are accounted (the TryAddUnique loser rolls back, re-plans
    /// from the winner's row, and lands as a regular conditional write).
    /// </remarks>
    [Fact]
    public async Task AssetStore_TwoInstancesConcurrentFirstCreate_ShouldLandSingleRowWithBothEntries()
    {
        await WithSqliteFacadeAsync(async () =>
        {
            var storeOne = new GameDbOnlineAssetStore();
            var storeTwo = new GameDbOnlineAssetStore();
            var (serviceOne, scopeOne) = BuildGrantService(storeOne, new GameDbOnlineAssetTransactionStore(), playerId: 32);
            var (serviceTwo, scopeTwo) = BuildGrantService(storeTwo, new GameDbOnlineAssetTransactionStore(), playerId: 32);

            var results = await Task.WhenAll(
                Task.Run(() => serviceOne.ExecuteAsync(Request(scopeOne, "g1109-create-a", new[] { (OnlineAssetKind.Currency, "gem", 10L) }))),
                Task.Run(() => serviceTwo.ExecuteAsync(Request(scopeTwo, "g1109-create-b", new[] { (OnlineAssetKind.Currency, "gem", 10L) }))));
            Assert.All(results, result => Assert.True(result.IsSuccess));

            // 只生成一行快照，两笔都入账。
            var wallets = await storeOne.ListWalletAccountsAsync(TenantId, AppId, 32);
            var row = Assert.Single(wallets);
            Assert.Equal("gem", row.CurrencyId);
            Assert.Equal(20, row.Balance);

            var ledger = (await storeOne.ListLedgerEntriesAsync(new OnlineLedgerPageQuery { TenantId = TenantId, AppId = AppId, PlayerId = 32, AfterSequenceNumber = 0, MaxCount = 100, })).OrderBy(entry => entry.SequenceNumber).ToArray();
            Assert.Equal(2, ledger.Length);
            Assert.Equal(0L, ledger[0].AmountBefore);
            Assert.Equal(ledger[0].AmountAfter, ledger[1].AmountBefore);
            Assert.Equal(20, ledger[1].AmountAfter);
        });
    }

    /// <summary>
    /// 存量行验收（GFX-1109 场景 3）：RowVersion = null 的旧数据行首次落账自动回填并入账正确，此后版本单调递增。
    /// </summary>
    /// <remarks>
    /// Legacy row backfill: a wallet row persisted with a null row version (pre-migration data shape)
    /// is backfilled on its first apply — the conditional write lands RowVersion = 1 with the correct
    /// balance, and subsequent applies continue on the regular CAS chain with strictly increasing
    /// versions (no permanent VersionConflict as a plain expected-0 CAS would produce).
    /// </remarks>
    [Fact]
    public async Task AssetStore_LegacyNullRowVersion_ShouldBackfillOnFirstApplyAndKeepVersionsMonotonic()
    {
        await WithSqliteFacadeAsync(async () =>
        {
            // 构造行版本缺位的存量行（模拟旧数据；显式 null 持久化为 JSON null）。
            var uniqueKey = TenantId + ":" + AppId + ":33:oldcoin";
            await GameDb.SaveOneAsync(new OnlineWalletAccountState
            {
                Id = 987001,
                UniqueKey = uniqueKey,
                TenantId = TenantId,
                AppId = AppId,
                PlayerId = 33,
                CurrencyId = "oldcoin",
                Balance = 100,
                UpdatedTime = 1,
                RowVersion = null,
            });

            var store = new GameDbOnlineAssetStore();
            var (service, scope) = BuildGrantService(store, new GameDbOnlineAssetTransactionStore(), playerId: 33);

            // 首次落账：回填原语补全 + 首写（余额 100 + 50）落行版本 0（与新建行基线一致），实体 Version = 0 + 1 = 1。
            var first = await service.ExecuteAsync(Request(scope, "g1109-legacy-first", new[] { (OnlineAssetKind.Currency, "oldcoin", 50L) }));
            Assert.True(first.IsSuccess);
            var wallet = await store.FindWalletAsync(TenantId, AppId, 33, "oldcoin");
            Assert.Equal(150, wallet.Balance);
            Assert.Equal(1, wallet.Version);

            // 第二次落账：转正常 CAS（期望行版本 0），版本单调递增。
            var second = await service.ExecuteAsync(Request(scope, "g1109-legacy-second", new[] { (OnlineAssetKind.Currency, "oldcoin", 30L) }));
            Assert.True(second.IsSuccess);
            wallet = await store.FindWalletAsync(TenantId, AppId, 33, "oldcoin");
            Assert.Equal(180, wallet.Balance);
            Assert.Equal(2, wallet.Version);

            // 行侧事实：null →（回填 0）→ CAS 1，不再是 null。
            var row = await GameDb.FindAsync<OnlineWalletAccountState>(state => state.UniqueKey == uniqueKey, isCreateIfNotExists: false);
            Assert.NotNull(row);
            Assert.Equal(1L, row.RowVersion);
            Assert.Equal(180, row.Balance);

            // 账本链自存量余额续写。
            var ledger = (await store.ListLedgerEntriesAsync(new OnlineLedgerPageQuery { TenantId = TenantId, AppId = AppId, PlayerId = 33, AfterSequenceNumber = 0, MaxCount = 100, })).OrderBy(entry => entry.SequenceNumber).ToArray();
            Assert.Equal(2, ledger.Length);
            Assert.Equal(100L, ledger[0].AmountBefore);
            Assert.Equal(150L, ledger[0].AmountAfter);
            Assert.Equal(150L, ledger[1].AmountBefore);
            Assert.Equal(180L, ledger[1].AmountAfter);
        });
    }


    /// <summary>
    /// 重启验收（SQLite 文件模式）：身份域写 → 关库重开 + 新适配实例 → 全字段读回一致 + 序列不复用。
    /// </summary>
    [Fact]
    public async Task Restart_IdentityDomain_ShouldSurviveRestartWithSequenceContinuation()
    {
        var dataFile = Path.Combine(Path.GetTempPath(), "c249-restart-identity-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            // 第一段进程：写入身份 / 账号 / 档案 / 设备 + 取走账号序列 1、2。
            await WithSqliteFileFacadeAsync(dataFile, async () =>
            {
                var store = new GameDbOnlineIdentityStore();
                await store.AddOrUpdateIdentityAsync(new OnlineIdentity { Id = "idn-r", TenantId = TenantId, AppId = AppId, Kind = OnlineIdentityKind.UserName, Identifier = "restart-user", GameAccountId = 900, CreatedAtTime = 10, UpdatedAtTime = 11, });
                await store.AddOrUpdateAccountAsync(new OnlineGameAccount { Id = 900, TenantId = TenantId, AppId = AppId, Status = OnlineGameAccountStatus.Active, CreatedAtTime = 10, });
                await store.AddOrUpdatePlayerAsync(new OnlinePlayerProfile { Id = 55, GameAccountId = 900, TenantId = TenantId, AppId = AppId, ServerId = 5, Name = "restart-player", CreatedAtTime = 12, });
                await store.AddOrUpdateDeviceAsync(new OnlinePlayerDevice { Id = "dev-r", GameAccountId = 900, DeviceIdentifier = "device-r", Platform = "ios", BoundAtTime = 13, });
                Assert.Equal(1L, await store.NextAccountIdAsync());
                Assert.Equal(2L, await store.NextAccountIdAsync());
            });

            // 第二段进程：同一数据文件新 service + 新适配实例。
            await WithSqliteFileFacadeAsync(dataFile, async () =>
            {
                var store = new GameDbOnlineIdentityStore();
                var identity = await store.FindIdentityAsync(TenantId, AppId, OnlineIdentityKind.UserName, "restart-user");
                Assert.NotNull(identity);
                Assert.Equal("idn-r", identity.Id);
                Assert.Equal(900, identity.GameAccountId);
                Assert.Equal(10, identity.CreatedAtTime);

                Assert.Equal(OnlineGameAccountStatus.Active, (await store.FindAccountAsync(900)).Status);
                Assert.Equal("restart-player", (await store.FindPlayerAsync(55)).Name);
                Assert.Equal("ios", (await store.FindDeviceAsync(900, "device-r")).Platform);
                var players = await store.ListAllPlayersAsync(900);
                Assert.Equal(new long[] { 55 }, players.Select(player => player.Id).ToArray());

                // 序列重启不复用：继续在 2 之后递增。
                Assert.Equal(3L, await store.NextAccountIdAsync());
            });
        }
        finally
        {
            File.Delete(dataFile);
        }
    }

    /// <summary>
    /// 重启验收（SQLite 文件模式）：玩家云存储写（含软删）→ 重启 → 版本 / 软删状态保真且 CAS 可继续。
    /// </summary>
    [Fact]
    public async Task Restart_StorageDomain_ShouldSurviveRestartAndContinueCas()
    {
        var dataFile = Path.Combine(Path.GetTempPath(), "c249-restart-storage-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            await WithSqliteFileFacadeAsync(dataFile, async () =>
            {
                var store = new GameDbOnlinePlayerStorageStore();
                var key = new OnlineStorageEntryKey { TenantId = TenantId, AppId = AppId, PlayerId = 33, Collection = "settings", Key = "theme", };
                Assert.True(await store.UpsertAsync(BuildEntry(key, new byte[] { 7, 7, 7 }, 1), 0));
                Assert.True(await store.UpsertAsync(BuildEntry(key, new byte[] { 8, 8 }, 2), 1));
                var softDeleted = BuildEntry(key, new byte[] { 8, 8 }, 3);
                softDeleted.DeletedAtTime = 7777;
                Assert.True(await store.UpsertAsync(softDeleted, 2));
                var other = new OnlineStorageEntryKey { TenantId = TenantId, AppId = AppId, PlayerId = 33, Collection = "settings", Key = "sound", };
                Assert.True(await store.UpsertAsync(BuildEntry(other, new byte[] { 1 }, 1), 0));
            });

            await WithSqliteFileFacadeAsync(dataFile, async () =>
            {
                var store = new GameDbOnlinePlayerStorageStore();
                var key = new OnlineStorageEntryKey { TenantId = TenantId, AppId = AppId, PlayerId = 33, Collection = "settings", Key = "theme", };

                // 软删条目读回：DeletedAtTime 与版本（3 = 行 RowVersion 2 + 校准）保真；活跃列举只见 sound。
                var softDeletedEntry = await store.FindAsync(key);
                Assert.Equal(3, softDeletedEntry.Version);
                Assert.Equal(7777, softDeletedEntry.DeletedAtTime);
                Assert.Equal(new byte[] { 8, 8 }, softDeletedEntry.Payload);
                Assert.Equal(new[] { "sound" }, (await ListAsync(store, 33, "settings")).Select(entry => entry.Key).ToArray());
                Assert.Equal(2, (await store.ListAllAsync()).Count);

                // 重启后 CAS 以软删版本继续：复活成功且版本推进 4。
                Assert.True(await store.UpsertAsync(BuildEntry(key, new byte[] { 9 }, 4), 3));
                Assert.Equal(2, await store.CountActiveKeysAsync(TenantId, AppId, 33, "settings"));
            });
        }
        finally
        {
            File.Delete(dataFile);
        }
    }

    /// <summary>
    /// 重启验收（SQLite 文件模式）：资产域写 → 重启 → 余额 / 库存 / 账本全量读回一致且账本序继续单调。
    /// </summary>
    [Fact]
    public async Task Restart_AssetDomain_ShouldSurviveRestartWithLedgerContinuation()
    {
        var dataFile = Path.Combine(Path.GetTempPath(), "c249-restart-assets-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            OnlineLedgerEntry[] beforeLedger = null;
            await WithSqliteFileFacadeAsync(dataFile, async () =>
            {
                var (grantService, scope) = BuildGrantService(new GameDbOnlineAssetStore(), new GameDbOnlineAssetTransactionStore(), playerId: 44);
                Assert.True((await grantService.ExecuteAsync(Request(scope, "restart-1", new (OnlineAssetKind, string, long)[]
                {
                    (OnlineAssetKind.Currency, "gold", 500),
                    (OnlineAssetKind.Item, "sword", 3),
                }))).IsSuccess);
                Assert.True((await grantService.ExecuteAsync(Request(scope, "restart-2", new (OnlineAssetKind, string, long)[]
                {
                    (OnlineAssetKind.Currency, "gold", -120),
                }))).IsSuccess);
                beforeLedger = (await new GameDbOnlineAssetStore().ListLedgerEntriesAsync(new OnlineLedgerPageQuery { TenantId = TenantId, AppId = AppId, PlayerId = 44, AfterSequenceNumber = 0, MaxCount = 100, })).ToArray();
            });

            await WithSqliteFileFacadeAsync(dataFile, async () =>
            {
                var assetStore = new GameDbOnlineAssetStore();
                var transactionStore = new GameDbOnlineAssetTransactionStore();

                // 快照读回：余额 / 数量 / 版本一致。
                var gold = await assetStore.FindWalletAsync(TenantId, AppId, 44, "gold");
                Assert.Equal(380, gold.Balance);
                Assert.Equal(2, gold.Version);
                Assert.Equal(3, (await assetStore.FindInventoryAsync(TenantId, AppId, 44, "sword")).Quantity);

                // 账本全量读回：逐字段一致（前后值链完整 = 余额可由账本重算）。
                var ledger = (await assetStore.ListLedgerEntriesAsync(new OnlineLedgerPageQuery { TenantId = TenantId, AppId = AppId, PlayerId = 44, AfterSequenceNumber = 0, MaxCount = 100, })).ToArray();
                Assert.Equal(beforeLedger.Length, ledger.Length);
                for (var i = 0; i < ledger.Length; i++)
                {
                    Assert.Equal(beforeLedger[i].EntryId, ledger[i].EntryId);
                    Assert.Equal(beforeLedger[i].AmountBefore, ledger[i].AmountBefore);
                    Assert.Equal(beforeLedger[i].Delta, ledger[i].Delta);
                    Assert.Equal(beforeLedger[i].AmountAfter, ledger[i].AmountAfter);
                    Assert.Equal(beforeLedger[i].SequenceNumber, ledger[i].SequenceNumber);
                }

                // 聚合一致 + 交易终态读回（含外键列表）。
                Assert.Equal(380, (await assetStore.SumLedgerDeltasByAssetAsync(TenantId, AppId, 44))["c:gold"]);
                var succeeded = await transactionStore.ListByStateAsync(OnlineAssetTransactionState.Succeeded);
                Assert.Equal(2, succeeded.Count);
                Assert.All(succeeded, transaction => Assert.NotEmpty(transaction.AppliedLedgerEntryIds));

                // 重启后新落账：账本序继续单调（3）+ 快照基于读回余额正确续算。
                var (grantService, scope) = BuildGrantService(assetStore, transactionStore, playerId: 44);
                Assert.True((await grantService.ExecuteAsync(Request(scope, "restart-3", new (OnlineAssetKind, string, long)[]
                {
                    (OnlineAssetKind.Currency, "gold", 20),
                }))).IsSuccess);
                var after = await assetStore.ListLedgerEntriesAsync(new OnlineLedgerPageQuery { TenantId = TenantId, AppId = AppId, PlayerId = 44, AfterSequenceNumber = 3, MaxCount = 10, });
                var continuation = Assert.Single(after);
                Assert.Equal(4, continuation.SequenceNumber);
                Assert.Equal(380, continuation.AmountBefore);
                Assert.Equal(400, continuation.AmountAfter);
                Assert.Equal(400, (await assetStore.FindWalletAsync(TenantId, AppId, 44, "gold")).Balance);
            });
        }
        finally
        {
            File.Delete(dataFile);
        }
    }

    /// <summary>
    /// SQLite 文件库门面（重启验收载体：关库 → 重开同文件 = 进程重启的数据层等价）。
    /// </summary>
    private static async Task WithSqliteFileFacadeAsync(string dataFile, Func<Task> action)
    {
        var opened = await GameDb.InitAsync<SqliteDbService>(new DbOptions
        {
            ConnectionString = "Data Source=" + dataFile,
            Name = "c249-restart",
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
    /// 构造统一发放入口（GameDb store 实景装配；幂等面用内存协调器，测试不重复键）。
    /// </summary>
    private static (OnlineGrantService Service, OnlineScope Scope) BuildGrantService(GameDbOnlineAssetStore assetStore, GameDbOnlineAssetTransactionStore transactionStore, long playerId)
    {
        var coordinator = new IdempotencyCoordinator(new InMemoryIdempotencyStore(), SystemClock.Instance);
        var service = new OnlineGrantService(assetStore, transactionStore, new OnlineIdempotencyService(coordinator, null), new NopEventPublisher(), null);
        return (service, new OnlineScope(TenantId, AppId, 5, playerId));
    }

    /// <summary>
    /// 构造发放请求（幂等键唯一，来源系统调整）。
    /// </summary>
    private static OnlineGrantRequest Request(OnlineScope scope, string idempotencyKey, (OnlineAssetKind Kind, string AssetId, long Amount)[] lines)
    {
        return new OnlineGrantRequest(
            scope,
            OnlineAssetChangeSource.AdminOperation,
            OnlineGrantOperation.Grant,
            "persistence test",
            "bo-" + idempotencyKey,
            lines.Select(line => new OnlineAssetChangeLine(line.Kind, line.AssetId, line.Amount)).ToArray(),
            idempotencyKey);
    }

    /// <summary>
    /// 列举集合内非软删条目（测试便捷重载）。
    /// </summary>
    private static Task<IReadOnlyList<OnlinePlayerStorageEntry>> ListAsync(GameDbOnlinePlayerStorageStore store, long playerId, string collection)
    {
        return store.ListAsync(new OnlineStorageListQuery { TenantId = TenantId, AppId = AppId, PlayerId = playerId, Collection = collection, AfterKey = string.Empty, MaxCount = 10, });
    }

    /// <summary>
    /// 构造存储条目。
    /// </summary>
    private static OnlinePlayerStorageEntry BuildEntry(OnlineStorageEntryKey key, byte[] payload, long version)
    {
        return new OnlinePlayerStorageEntry
        {
            TenantId = key.TenantId,
            AppId = key.AppId,
            PlayerId = key.PlayerId,
            Collection = key.Collection,
            Key = key.Key,
            Payload = payload,
            Version = version,
            CreatedAtTime = 1,
            CreatedBy = "tester",
            UpdatedAtTime = 2,
            UpdatedBy = "tester",
        };
    }

    /// <summary>
    /// 空事件出口（发放事件的测试桩）。
    /// </summary>
    private sealed class NopEventPublisher : IOnlineEventPublisher
    {
        /// <summary>
        /// 发布在线事件；本桩仅满足存储构造所需的事件出口，持久化测试不关心事件，因此直接丢弃并完成。
        /// </summary>
        /// <remarks>
        /// Publishes an online event; this stub only satisfies the event sink dependency and discards the event, completing immediately.
        /// </remarks>
        /// <param name="onlineEvent">待发布的在线事件（被丢弃） / Online event to publish (discarded)</param>
        /// <param name="cancellationToken">取消令牌（本桩忽略） / Cancellation token (ignored by this stub)</param>
        /// <returns>已完成的任务 / An already completed task</returns>
        public Task PublishAsync(OnlineEvent onlineEvent, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
