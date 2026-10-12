// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and applicable international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file at the root of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.DataBase;
using GameFrameX.DataBase.Abstractions;
using GameFrameX.DataBase.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;

namespace GameFrameX.Tests.DataBase;

/// <summary>
/// SqliteDbService 契约测试（用例集语义对齐 PostgreSqlDbServiceQueryTests；SQLite 嵌入式无需环境变量门控）。
/// </summary>
/// <remarks>
/// Contract tests for <c>SqliteDbService</c> (semantics aligned with <c>PostgreSqlDbServiceQueryTests</c>).
/// SQLite is embedded: every test opens an isolated <c>Data Source=:memory:</c> service (no environment
/// gating); the path-diagnosis cases use per-test temp directories / files and restore permissions afterwards.
/// </remarks>
[Collection(nameof(GameDbStaticStateCollection))]
public sealed class SqliteDbServiceQueryTests : IDisposable
{
    private static long _idSeed = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private readonly List<string> _tempPaths = new();

    /// <summary>
    /// 测试往返持久化（含嵌套集合属性经 STJ 序列化还原）。
    /// </summary>
    [Fact]
    public async Task RoundTrip_ShouldPersistNestedCollectionProperties()
    {
        await ExecuteWithServiceAsync(async service =>
        {
            var state = CreateState("roundtrip", group: 1, score: 10);
            state.Tags = new List<string> { "alpha", "beta" };
            state.Items = new List<SqliteNestedItem>
            {
                new() { Name = "n1", Value = 1 },
                new() { Name = "n2", Value = 2 },
            };
            await service.AddAsync(state);

            var loaded = await service.FindAsync<SqliteQueryTestState>(state.Id, isCreateIfNotExists: false);
            Assert.NotNull(loaded);
            Assert.Equal(new[] { "alpha", "beta" }, loaded.Tags);
            Assert.Equal(2, loaded.Items.Count);
            Assert.Equal("n2", loaded.Items[1].Name);
            Assert.Equal(2, loaded.Items[1].Value);
        });
    }

    /// <summary>
    /// 测试按 ID 查询已存在数据。
    /// </summary>
    [Fact]
    public async Task FindAsync_ById_ShouldReturnExistingState()
    {
        await ExecuteWithServiceAsync(async service =>
        {
            var state = CreateState("player-1", group: 1, score: 30);
            await service.AddAsync(state);

            var result = await service.FindAsync<SqliteQueryTestState>(state.Id, isCreateIfNotExists: false);

            Assert.NotNull(result);
            Assert.Equal(state.Id, result.Id);
            Assert.Equal("player-1", result.Name);
            Assert.Equal(30, result.Score);
        });
    }

    /// <summary>
    /// 测试按 ID 查询不存在数据：不自动创建返回 null，自动创建返回补齐时间戳的新实例（未落库）。
    /// </summary>
    [Fact]
    public async Task FindAsync_ById_WhenNotExists_ShouldRespectCreateFlag()
    {
        await ExecuteWithServiceAsync(async service =>
        {
            var missingId = Interlocked.Increment(ref _idSeed);
            Assert.Null(await service.FindAsync<SqliteQueryTestState>(missingId, isCreateIfNotExists: false));

            var created = await service.FindAsync<SqliteQueryTestState>(missingId, isCreateIfNotExists: true);
            Assert.NotNull(created);
            Assert.Equal(missingId, created.Id);
            Assert.True(created.CreatedTime > 0);
            // 未落库：Count 仍为 0
            Assert.Equal(0, await service.CountAsync<SqliteQueryTestState>(x => x.Id == missingId));
        });
    }

    /// <summary>
    /// 测试条件查询与软删除过滤的一致性。
    /// </summary>
    [Fact]
    public async Task FindList_Count_Any_ShouldRespectSoftDeleteFilter()
    {
        await ExecuteWithServiceAsync(async service =>
        {
            var a = CreateState("group1-a", group: 1, score: 10);
            var b = CreateState("group1-b", group: 1, score: 20);
            var c = CreateState("group2-c", group: 2, score: 30);
            await service.AddListAsync(new[] { a, b, c });
            await service.DeleteAsync(b);

            var list = await service.FindListAsync<SqliteQueryTestState>(x => x.Group == 1);
            var count = await service.CountAsync<SqliteQueryTestState>(x => x.Group == 1);
            var anyGroup1 = await service.AnyAsync<SqliteQueryTestState>(x => x.Group == 1);
            var anyDeleted = await service.AnyAsync<SqliteQueryTestState>(x => x.Id == b.Id);
            var countIncludeDeleted = await service.CountAsync<SqliteQueryTestState>(x => x.Group == 1, includeDeleted: true);

            Assert.Single(list);
            Assert.Equal(a.Id, list[0].Id);
            Assert.Equal(1, count);
            Assert.Equal(2, countIncludeDeleted);
            Assert.True(anyGroup1);
            Assert.False(anyDeleted);
        });
    }

    /// <summary>
    /// 测试根据 ID 判断存在性接口。
    /// </summary>
    [Fact]
    public async Task ExistsByIdAsync_ShouldRespectSoftDeleteFilter()
    {
        await ExecuteWithServiceAsync(async service =>
        {
            var visible = CreateState("exists-visible", group: 2, score: 11);
            var deleted = CreateState("exists-deleted", group: 2, score: 22);
            await service.AddListAsync(new[] { visible, deleted });
            await service.DeleteAsync(deleted);

            var existsVisible = await service.ExistsByIdAsync<SqliteQueryTestState>(visible.Id);
            var existsDeleted = await service.ExistsByIdAsync<SqliteQueryTestState>(deleted.Id);
            var existsMissing = await service.ExistsByIdAsync<SqliteQueryTestState>(Interlocked.Increment(ref _idSeed));

            Assert.True(existsVisible);
            Assert.False(existsDeleted);
            Assert.False(existsMissing);
        });
    }

    /// <summary>
    /// 测试按条件查询不存在且允许创建时返回新实例（Id 由生成器分配，未落库）。
    /// </summary>
    [Fact]
    public async Task FindAsync_ByFilter_WhenNotExistsAndCreate_ShouldCreateTransientState()
    {
        await ExecuteWithServiceAsync(async service =>
        {
            var state = await service.FindAsync<SqliteQueryTestState>(x => x.Name == "transient-new");
            Assert.NotNull(state);
            Assert.True(state.Id > 0);
            Assert.Null(state.Name);
            Assert.True(state.CreatedTime > 0);
            // 未落库：Count 仍为 0
            Assert.Equal(0, await service.CountAsync<SqliteQueryTestState>(x => x.Name == "transient-new"));
        });
    }

    /// <summary>
    /// 测试插入一条数据，以及主键冲突抛 SqliteException。
    /// </summary>
    [Fact]
    public async Task AddAsync_WithDuplicatePrimaryKey_ShouldThrowSqliteException()
    {
        await ExecuteWithServiceAsync(async service =>
        {
            var state = CreateState("add-dup", group: 3, score: 42);
            await service.AddAsync(state);
            Assert.Equal(1, await service.CountAsync<SqliteQueryTestState>(x => x.Name == "add-dup"));

            var duplicate = CreateState("add-dup-2", group: 3, score: 43);
            duplicate.Id = state.Id;
            await Assert.ThrowsAsync<SqliteException>(() => service.AddAsync(duplicate));
        });
    }

    /// <summary>
    /// 测试空集合插入不做任何写入。
    /// </summary>
    [Fact]
    public async Task AddListAsync_WithEmptyCollection_ShouldNotInsertAnyRecord()
    {
        await ExecuteWithServiceAsync(async service =>
        {
            await service.AddListAsync(Array.Empty<SqliteQueryTestState>());
            var total = await service.CountAsync<SqliteQueryTestState>(x => true);
            Assert.Equal(0, total);
        });
    }

    /// <summary>
    /// 测试 Upsert：先插入后更新保持单行且 UpdateCount 推进。
    /// </summary>
    [Fact]
    public async Task AddOrUpdateAsync_ShouldInsertThenUpdate()
    {
        await ExecuteWithServiceAsync(async service =>
        {
            var state = CreateState("upsert-one", group: 4, score: 1);
            await service.AddOrUpdateAsync(state);
            state.Score = 99;
            await service.AddOrUpdateAsync(state);

            var list = await service.FindListAsync<SqliteQueryTestState>(x => x.Name == "upsert-one");
            Assert.Single(list);
            Assert.Equal(99, list[0].Score);
            Assert.Equal(2, list[0].UpdateCount);
        });
    }

    /// <summary>
    /// 测试软删后 AddOrUpdate 复活该行（IsDeleted 清除且数据更新可见）。
    /// </summary>
    [Fact]
    public async Task AddOrUpdateAsync_AfterSoftDelete_ShouldRestoreVisibility()
    {
        await ExecuteWithServiceAsync(async service =>
        {
            var state = CreateState("revive", group: 5, score: 7);
            await service.AddOrUpdateAsync(state);
            await service.DeleteAsync<SqliteQueryTestState>(x => x.Name == "revive");
            Assert.Equal(0, await service.CountAsync<SqliteQueryTestState>(x => x.Name == "revive"));

            state.Score = 8;
            await service.AddOrUpdateAsync(state);
            var revived = await service.FindAsync<SqliteQueryTestState>(x => x.Name == "revive", isCreateIfNotExists: false);
            Assert.NotNull(revived);
            Assert.Equal(8, revived.Score);
            Assert.NotEqual(true, revived.IsDeleted);
        });
    }

    /// <summary>
    /// 测试单条更新将 null 字段移除（读回为 null）。
    /// </summary>
    [Fact]
    public async Task UpdateAsync_Single_ShouldSupportUnsetNullField()
    {
        await ExecuteWithServiceAsync(async service =>
        {
            var state = CreateState("unset-null", group: 6, score: 3, note: "to-be-removed");
            await service.AddAsync(state);
            var loaded = await service.FindAsync<SqliteQueryTestState>(state.Id, isCreateIfNotExists: false);
            Assert.NotNull(loaded);
            loaded.LoadFromDbPostHandler();
            loaded.OptionalNote = null;
            await service.UpdateAsync(loaded);

            var reread = await service.FindAsync<SqliteQueryTestState>(state.Id, isCreateIfNotExists: false);
            Assert.Null(reread.OptionalNote);
        });
    }

    /// <summary>
    /// 测试列表更新返回实际变更数，未变更项不计。
    /// </summary>
    [Fact]
    public async Task UpdateAsync_List_ShouldReturnChangedCount()
    {
        await ExecuteWithServiceAsync(async service =>
        {
            var a = CreateState("list-a", group: 7, score: 1);
            var b = CreateState("list-b", group: 7, score: 2);
            await service.AddListAsync(new[] { a, b });
            var loadedA = await service.FindAsync<SqliteQueryTestState>(a.Id, isCreateIfNotExists: false);
            var loadedB = await service.FindAsync<SqliteQueryTestState>(b.Id, isCreateIfNotExists: false);
            Assert.NotNull(loadedA);
            Assert.NotNull(loadedB);
            loadedA.LoadFromDbPostHandler();
            loadedB.LoadFromDbPostHandler();
            loadedA.Score = 100;

            var changed = await service.UpdateAsync(new[] { loadedA, loadedB });
            Assert.Equal(1, changed);

            var refreshedA = await service.FindAsync<SqliteQueryTestState>(a.Id, isCreateIfNotExists: false);
            Assert.NotNull(refreshedA);
            Assert.Equal(100, refreshedA.Score);
        });
    }

    /// <summary>
    /// 测试按对象软删除。
    /// </summary>
    [Fact]
    public async Task DeleteAsync_ByState_ShouldSoftDeleteRecord()
    {
        await ExecuteWithServiceAsync(async service =>
        {
            var state = CreateState("del-state", group: 8, score: 5);
            await service.AddAsync(state);

            var deleted = await service.DeleteAsync(state);
            Assert.Equal(1, deleted);
            Assert.Equal(0, await service.CountAsync<SqliteQueryTestState>(x => x.Name == "del-state"));
            Assert.Equal(1, await service.CountAsync<SqliteQueryTestState>(x => x.Name == "del-state", includeDeleted: true));
        });
    }

    /// <summary>
    /// 测试按条件软删除首条匹配，重复删除幂等返回 0。
    /// </summary>
    [Fact]
    public async Task DeleteAsync_ByFilter_MultipleTimes_ShouldBeIdempotent()
    {
        await ExecuteWithServiceAsync(async service =>
        {
            var state = CreateState("del-idem", group: 9, score: 5);
            await service.AddAsync(state);

            var first = await service.DeleteAsync<SqliteQueryTestState>(x => x.Name == "del-idem");
            var second = await service.DeleteAsync<SqliteQueryTestState>(x => x.Name == "del-idem");

            Assert.Equal(1, first);
            Assert.Equal(0, second);
        });
    }

    /// <summary>
    /// 测试批量条件软删除。
    /// </summary>
    [Fact]
    public async Task DeleteListAsync_ShouldSoftDeleteMatchedRecords()
    {
        await ExecuteWithServiceAsync(async service =>
        {
            var a = CreateState("batch-a", group: 10, score: 1);
            var b = CreateState("batch-b", group: 10, score: 2);
            var c = CreateState("batch-c", group: 11, score: 3);
            await service.AddListAsync(new[] { a, b, c });

            var deleted = await service.DeleteListAsync<SqliteQueryTestState>(x => x.Group == 10);
            Assert.Equal(2, deleted);
            Assert.Equal(1, await service.CountAsync<SqliteQueryTestState>(x => x.Group == 11));
        });
    }

    /// <summary>
    /// 测试按 ID 列表软删除与空集合返回 0。
    /// </summary>
    [Fact]
    public async Task DeleteListIdAsync_ShouldSoftDeleteSpecifiedIds()
    {
        await ExecuteWithServiceAsync(async service =>
        {
            var a = CreateState("idlist-a", group: 12, score: 1);
            var b = CreateState("idlist-b", group: 12, score: 2);
            var c = CreateState("idlist-c", group: 12, score: 3);
            await service.AddListAsync(new[] { a, b, c });

            var deleted = await service.DeleteListIdAsync<SqliteQueryTestState>(new[] { a.Id, b.Id });
            var emptyDeleted = await service.DeleteListIdAsync<SqliteQueryTestState>(Array.Empty<long>());

            Assert.Equal(2, deleted);
            Assert.Equal(0, emptyDeleted);
            Assert.Equal(1, await service.CountAsync<SqliteQueryTestState>(x => x.Group == 12));
        });
    }

    /// <summary>
    /// 测试恢复软删除数据（IsDeleted 置 false 且 DeleteTime 移除）。
    /// </summary>
    [Fact]
    public async Task RestoreAsync_ShouldRestoreSoftDeleted()
    {
        await ExecuteWithServiceAsync(async service =>
        {
            var state = CreateState("restore-me", group: 13, score: 6);
            await service.AddAsync(state);
            await service.DeleteAsync(state);

            var restored = await service.RestoreAsync<SqliteQueryTestState>(x => x.Name == "restore-me");
            Assert.Equal(1, restored);

            var revived = await service.FindAsync<SqliteQueryTestState>(state.Id, isCreateIfNotExists: false);
            Assert.NotNull(revived);
            Assert.False(revived.IsDeleted);
            Assert.Null(revived.DeleteTime);
        });
    }

    /// <summary>
    /// 测试物理删除彻底移除（includeDeleted 也查不到）。
    /// </summary>
    [Fact]
    public async Task HardDeleteAsync_ShouldPhysicallyRemove()
    {
        await ExecuteWithServiceAsync(async service =>
        {
            var state = CreateState("hard-del", group: 14, score: 6);
            await service.AddAsync(state);
            await service.DeleteAsync(state);

            var removed = await service.HardDeleteAsync<SqliteQueryTestState>(x => x.Name == "hard-del");
            Assert.Equal(1, removed);
            Assert.Equal(0, await service.CountAsync<SqliteQueryTestState>(x => x.Name == "hard-del", includeDeleted: true));
        });
    }

    /// <summary>
    /// 测试排序与分页查询（FindSort* / FindPageAsync）。
    /// </summary>
    [Fact]
    public async Task SortAndPageQueries_ShouldReturnExpectedOrder()
    {
        await ExecuteWithServiceAsync(async service =>
        {
            var a = CreateState("sort-1", group: 15, score: 30);
            var b = CreateState("sort-2", group: 15, score: 10);
            var c = CreateState("sort-3", group: 15, score: 20);
            await service.AddListAsync(new[] { a, b, c });

            var descFirst = await service.FindSortDescendingFirstOneAsync<SqliteQueryTestState>(x => x.Group == 15, x => x.Score);
            var ascFirst = await service.FindSortAscendingFirstOneAsync<SqliteQueryTestState>(x => x.Group == 15, x => x.Score);
            Assert.Equal(30, descFirst.Score);
            Assert.Equal(10, ascFirst.Score);

            var descPage = await service.FindSortDescendingAsync<SqliteQueryTestState>(x => x.Group == 15, x => x.Score, pageIndex: 0, pageSize: 2);
            var ascPage = await service.FindSortAscendingAsync<SqliteQueryTestState>(x => x.Group == 15, x => x.Score, pageIndex: 1, pageSize: 2);
            Assert.Equal(new[] { 30, 20 }, descPage.Select(static s => s.Score).ToArray());
            Assert.Equal(new[] { 30 }, ascPage.Select(static s => s.Score).ToArray());

            var (pageItems, total) = await service.FindPageAsync<SqliteQueryTestState>(x => x.Group == 15, x => x.Score, descending: true, pageIndex: 1, pageSize: 2);
            Assert.Equal(3, total);
            Assert.Equal(new[] { 10 }, pageItems.Select(static s => s.Score).ToArray());
        });
    }

    /// <summary>
    /// 测试排序 null 放置：升序 null 在前、降序 null 在后（稳定排序）。
    /// </summary>
    [Fact]
    public async Task SortQueries_NullKeyPlacement_ShouldBeStable()
    {
        await ExecuteWithServiceAsync(async service =>
        {
            var a = CreateState("null-a", group: 16, score: 1, note: "b");
            var b = CreateState("null-b", group: 16, score: 2);
            var c = CreateState("null-c", group: 16, score: 3, note: "a");
            var d = CreateState("null-d", group: 16, score: 4);
            await service.AddListAsync(new[] { a, b, c, d });

            var asc = await service.FindSortAscendingAsync<SqliteQueryTestState>(x => x.Group == 16, x => x.OptionalNote, pageIndex: 0, pageSize: 10);
            var desc = await service.FindSortDescendingAsync<SqliteQueryTestState>(x => x.Group == 16, x => x.OptionalNote, pageIndex: 0, pageSize: 10);

            Assert.Equal(new[] { null, null, "a", "b" }, asc.Select(static s => s.OptionalNote).ToArray());
            Assert.Equal(new[] { "b", "a", null, null }, desc.Select(static s => s.OptionalNote).ToArray());
        });
    }

    /// <summary>
    /// 测试投影查询（成员绑定投影，内存投影无 LoadFromDbPostHandler）。
    /// </summary>
    [Fact]
    public async Task FindProjectedAsync_ShouldProjectMembers()
    {
        await ExecuteWithServiceAsync(async service =>
        {
            await service.AddListAsync(new[]
            {
                CreateState("proj-1", group: 17, score: 1),
                CreateState("proj-2", group: 17, score: 2),
            });

            var names = await service.FindProjectedAsync<SqliteQueryTestState, string>(x => x.Group == 17, x => x.Name);
            Assert.Equal(new[] { "proj-1", "proj-2" }.OrderBy(static n => n), names.OrderBy(static n => n));
        });
    }

    /// <summary>
    /// 测试 FindByIds：去重、空集合返回空、可见性过滤。
    /// </summary>
    [Fact]
    public async Task FindByIdsAsync_ShouldReturnVisibleRecordsOnly()
    {
        await ExecuteWithServiceAsync(async service =>
        {
            var a = CreateState("byids-a", group: 18, score: 1);
            var b = CreateState("byids-b", group: 18, score: 2);
            var c = CreateState("byids-c", group: 18, score: 3);
            await service.AddListAsync(new[] { a, b, c });
            await service.DeleteAsync(b);

            Assert.Empty(await service.FindByIdsAsync<SqliteQueryTestState>(Array.Empty<long>()));

            var found = await service.FindByIdsAsync<SqliteQueryTestState>(new[] { a.Id, b.Id, c.Id, a.Id });
            Assert.Equal(new[] { a.Id, c.Id }.OrderBy(static id => id), found.Select(static s => s.Id).OrderBy(static id => id));
        });
    }

    /// <summary>
    /// 测试部分更新：字段写入、null 移除（引用置 null/值类型置默认）、排除键（Id/CreatedTime）不受影响。
    /// </summary>
    [Fact]
    public async Task UpdatePartialAsync_ShouldSetUnsetAndIgnoreExcludedKeys()
    {
        await ExecuteWithServiceAsync(async service =>
        {
            var state = CreateState("partial", group: 19, score: 5, note: "keep");
            await service.AddAsync(state);
            var original = await service.FindAsync<SqliteQueryTestState>(state.Id, isCreateIfNotExists: false);
            Assert.NotNull(original);

            var modified = await service.UpdatePartialAsync<SqliteQueryTestState>(state.Id, new Dictionary<string, object>
            {
                { nameof(SqliteQueryTestState.Score), 77 },
                { nameof(SqliteQueryTestState.OptionalNote), null },
                { nameof(SqliteQueryTestState.Group), null },
                { nameof(SqliteQueryTestState.Id), state.Id + 1000 },
                { nameof(SqliteQueryTestState.CreatedTime), 999L },
            });
            Assert.Equal(1, modified);

            var reread = await service.FindAsync<SqliteQueryTestState>(state.Id, isCreateIfNotExists: false);
            Assert.Equal(77, reread.Score);
            Assert.Null(reread.OptionalNote);
            Assert.Equal(0, reread.Group);
            Assert.Equal(state.Id, reread.Id);
            Assert.Equal(original.CreatedTime, reread.CreatedTime);
            Assert.Equal(1, reread.UpdateCount);
            Assert.NotNull(reread.UpdateTime);
        });
    }

    /// <summary>
    /// 测试软删后的部分更新：仅可见行可更新，软删行返回 0 且不改动库（对齐 Mongo/PG 可见性口径）。
    /// </summary>
    [Fact]
    public async Task UpdatePartialAsync_AfterSoftDelete_ShouldReturnZeroAndNotModifyRow()
    {
        await ExecuteWithServiceAsync(async service =>
        {
            var state = CreateState("partial-deleted", group: 20, score: 5);
            await service.AddAsync(state);
            Assert.Equal(1, await service.DeleteAsync(state));

            var modified = await service.UpdatePartialAsync<SqliteQueryTestState>(state.Id, new Dictionary<string, object>
            {
                { nameof(SqliteQueryTestState.Score), 999 },
            });
            Assert.Equal(0, modified);

            var includeDeleted = await service.CountAsync<SqliteQueryTestState>(x => x.Id == state.Id, includeDeleted: true);
            Assert.Equal(1, includeDeleted);
            var visible = await service.CountAsync<SqliteQueryTestState>(x => x.Id == state.Id);
            Assert.Equal(0, visible);

            var restored = await service.RestoreAsync<SqliteQueryTestState>(x => x.Id == state.Id);
            Assert.Equal(1, restored);
            var revived = await service.FindAsync<SqliteQueryTestState>(state.Id, isCreateIfNotExists: false);
            Assert.Equal(5, revived.Score);
        });
    }

    /// <summary>
    /// 测试 SaveBulk 批量 upsert：逐批 ack、不触碰时间戳、重复保存幂等、batchSize&lt;=0 抛 ArgumentOutOfRangeException。
    /// </summary>
    [Fact]
    public async Task SaveBulkAsync_ShouldUpsertBatchesAndKeepTimestamps()
    {
        await ExecuteWithServiceAsync(async service =>
        {
            var states = Enumerable.Range(0, 7).Select(i => CreateState($"bulk-{i}", group: 20, score: i)).ToArray();
            states[0].CreatedTime = 111;
            states[0].UpdateTime = 222;
            states[0].UpdateCount = 5;

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.SaveBulkAsync(states, batchSize: 0));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => service.SaveBulkAsync(states, batchSize: -1));

            var acknowledged = await service.SaveBulkAsync(states, batchSize: 3);
            Assert.Equal(7, acknowledged.Count);

            var stored = await service.FindAsync<SqliteQueryTestState>(states[0].Id, isCreateIfNotExists: false);
            Assert.Equal(111, stored.CreatedTime);
            Assert.Equal(222, stored.UpdateTime);
            Assert.Equal(5, stored.UpdateCount);

            states[0].Score = 1000;
            var acknowledgedAgain = await service.SaveBulkAsync(states, batchSize: 4);
            Assert.Equal(7, acknowledgedAgain.Count);
            Assert.Equal(7, await service.CountAsync<SqliteQueryTestState>(x => x.Group == 20));

            var updated = await service.FindAsync<SqliteQueryTestState>(states[0].Id, isCreateIfNotExists: false);
            Assert.Equal(1000, updated.Score);
            Assert.Equal(111, updated.CreatedTime);
            Assert.Equal(222, updated.UpdateTime);
        });
    }

    /// <summary>
    /// 测试事务回滚：action 内 Add 后抛异常，数据整体不落库。
    /// </summary>
    [Fact]
    public async Task ExecuteInTransactionAsync_WhenActionThrows_ShouldRollBack()
    {
        await ExecuteWithServiceAsync(async service =>
        {
            // 预建表：表创建（DDL）也是事务性的，回滚会一并撤销首事务内的 CREATE TABLE
            Assert.Equal(0, await service.CountAsync<SqliteQueryTestState>(x => x.Group == 21, includeDeleted: true));
            var state = CreateState("tx-rollback", group: 21, score: 1);

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteInTransactionAsync(async () =>
            {
                await service.AddAsync(state);
                throw new InvalidOperationException("boom");
            }));

            Assert.Equal(0, await service.CountAsync<SqliteQueryTestState>(x => x.Name == "tx-rollback", includeDeleted: true));
        });
    }

    /// <summary>
    /// 测试事务提交与嵌套事务壳：嵌套壳加入环境事务，内层数据随外层一起提交；外层失败内层一并回滚。
    /// </summary>
    [Fact]
    public async Task ExecuteInTransactionAsync_NestedShell_ShouldJoinAmbientTransaction()
    {
        await ExecuteWithServiceAsync(async service =>
        {
            Assert.Equal(0, await service.CountAsync<SqliteQueryTestState>(x => x.Group == 22, includeDeleted: true));
            var committed = CreateState("tx-nested-ok", group: 22, score: 1);
            var inner = CreateState("tx-nested-inner", group: 22, score: 2);

            await service.ExecuteInTransactionAsync(async () =>
            {
                await service.AddAsync(committed);
                await service.ExecuteInTransactionAsync(() => service.AddAsync(inner));
            });

            Assert.Equal(2, await service.CountAsync<SqliteQueryTestState>(x => x.Group == 22));

            var rolledBackOuter = CreateState("tx-nested-fail", group: 23, score: 1);
            var rolledBackInner = CreateState("tx-nested-fail-inner", group: 23, score: 2);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteInTransactionAsync(async () =>
            {
                await service.AddAsync(rolledBackOuter);
                await service.ExecuteInTransactionAsync(() => service.AddAsync(rolledBackInner));
                throw new InvalidOperationException("outer boom");
            }));

            Assert.Equal(0, await service.CountAsync<SqliteQueryTestState>(x => x.Group == 23, includeDeleted: true));
        });
    }

    /// <summary>
    /// 测试取消令牌在查询与写入路径生效。
    /// </summary>
    [Fact]
    public async Task CanceledToken_ShouldThrowOperationCanceledException()
    {
        await ExecuteWithServiceAsync(async service =>
        {
            using var canceled = new CancellationTokenSource();
            await canceled.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.FindListAsync<SqliteQueryTestState>(x => x.Group == 24, canceled.Token));
            var state = CreateState("cancel", group: 24, score: 1);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.AddAsync(state, canceled.Token));
        });
    }

    /// <summary>
    /// 测试 LoadFromDbPostHandler 钩子语义：命中读取 isNew=false，自动创建 isNew=true。
    /// </summary>
    [Fact]
    public async Task LoadFromDbPostHandler_ShouldDistinguishHitAndCreate()
    {
        await ExecuteWithServiceAsync(async service =>
        {
            var state = CreateState("hook-hit", group: 25, score: 1);
            await service.AddAsync(state);

            var hit = await service.FindAsync<SqliteQueryTestState>(state.Id, isCreateIfNotExists: false);
            Assert.NotNull(hit);
            Assert.True(hit.LoadHookInvoked);
            Assert.False(hit.LoadHookIsNew);

            var missingId = Interlocked.Increment(ref _idSeed);
            var created = await service.FindAsync<SqliteQueryTestState>(missingId, isCreateIfNotExists: true);
            Assert.True(created.LoadHookInvoked);
            Assert.True(created.LoadHookIsNew);
        });
    }


    /// <summary>
    /// 测试文件模式 Open：自动创建深层目录、数据文件出现在解析后的全路径。
    /// </summary>
    [Fact]
    public async Task Open_FileMode_ShouldCreateDeepDirectoryAndDataFile()
    {
        var root = CreateTempDirectory();
        var service = new SqliteDbService();
        var options = new DbOptions
        {
            ConnectionString = $"Data Source={Path.Combine(root, "deep", "nested", "game.db")}",
            Name = "sqlite-file-mode",
        };

        try
        {
            Assert.True(await service.Open(options));
            var expectedFullPath = Path.GetFullPath(Path.Combine(root, "deep", "nested", "game.db"));
            Assert.True(File.Exists(expectedFullPath), $"data file should exist at {expectedFullPath}");
        }
        finally
        {
            await service.Close();
        }
    }

    /// <summary>
    /// 测试相对路径解析与自动建目录（相对当前目录的深层相对路径，Open 后全路径存在文件）。
    /// </summary>
    [Fact]
    public async Task Open_RelativePath_ShouldResolveAgainstWorkingDirectory()
    {
        var root = CreateTempDirectory();
        var previous = Environment.CurrentDirectory;
        var service = new SqliteDbService();
        try
        {
            Environment.CurrentDirectory = root;
            Assert.True(await service.Open(new DbOptions
            {
                ConnectionString = "Data Source=rel/deep/dir/rel.db",
                Name = "sqlite-relative",
            }));
            Assert.True(File.Exists(Path.GetFullPath(Path.Combine(root, "rel", "deep", "dir", "rel.db"))));
        }
        finally
        {
            await service.Close();
            Environment.CurrentDirectory = previous;
        }
    }

    /// <summary>
    /// 测试内存模式 Open：零数据文件（临时根目录不产生任何文件）。
    /// </summary>
    [Fact]
    public async Task Open_MemoryMode_ShouldNotCreateAnyFile()
    {
        var root = CreateTempDirectory();
        var service = new SqliteDbService();
        try
        {
            Assert.True(await service.Open(new DbOptions
            {
                ConnectionString = "Data Source=:memory:",
                Name = "sqlite-memory-mode",
            }));
            Assert.Empty(Directory.GetFiles(root, "*", SearchOption.AllDirectories));
        }
        finally
        {
            await service.Close();
        }
    }

    /// <summary>
    /// 测试边界警告进程内仅一次：首个 Open 计 1，后续 service Open 不再累计；重置闸后再 Open 又计 1。
    /// </summary>
    [Fact]
    public async Task Open_BoundaryWarning_ShouldEmitOncePerProcess()
    {
        SqliteDbService.ResetBoundaryWarningForTest();
        try
        {
            await ExecuteWithServiceAsync(async _ =>
            {
                Assert.Equal(1, SqliteDbService.BoundaryWarningEmitCount);
                var second = new SqliteDbService();
                try
                {
                    Assert.True(await second.Open(CreateMemoryOptions("sqlite-boundary-second")));
                    Assert.Equal(1, SqliteDbService.BoundaryWarningEmitCount);
                }
                finally
                {
                    await second.Close();
                }
            });

            SqliteDbService.ResetBoundaryWarningForTest();
            await ExecuteWithServiceAsync(static _ =>
            {
                Assert.Equal(1, SqliteDbService.BoundaryWarningEmitCount);
                return Task.CompletedTask;
            });
        }
        finally
        {
            SqliteDbService.ResetBoundaryWarningForTest();
        }
    }

    /// <summary>
    /// 测试连接串缺 Data Source：fail-fast 抛 InvalidOperationException，报错自带两个示例。
    /// </summary>
    [Fact]
    public async Task Open_MissingDataSource_ShouldThrowWithExamples()
    {
        var service = new SqliteDbService();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.Open(new DbOptions
        {
            ConnectionString = "Mode=ReadOnly",
            Name = "sqlite-missing-datasource",
        }));

        Assert.Contains("Data Source=:memory:", exception.Message);
        Assert.Contains("Data Source=/abs/path/game.db", exception.Message);
    }

    /// <summary>
    /// 测试只读目录 fail-fast：目录可写探针失败，报错含全路径与失败环节（POSIX chmod 555 模拟，用后恢复）。
    /// </summary>
    [Fact]
    public async Task Open_ReadOnlyDirectory_ShouldFailFastOnWriteProbe()
    {
        var root = CreateTempDirectory();
        var dataFile = Path.Combine(root, "game.db");
        var service = new SqliteDbService();
        var restored = false;
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                restored = true;
                MakeReadOnly(root);
            }

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.Open(new DbOptions
            {
                ConnectionString = $"Data Source={dataFile}",
                Name = "sqlite-readonly-dir",
            }));

            Assert.Contains(Path.GetFullPath(dataFile), exception.Message);
            Assert.Contains("directory-write-probe", exception.Message);
        }
        finally
        {
            if (restored)
            {
                MakeWritable(root);
            }

            await service.Close();
        }
    }

    /// <summary>
    /// 测试文件被占用 fail-fast：独占句柄（FileShare.None）下 ReadWrite 探测失败，报错含失败环节。
    /// </summary>
    [Fact]
    public async Task Open_LockedFile_ShouldFailFastOnReadWriteProbe()
    {
        var root = CreateTempDirectory();
        var dataFile = Path.Combine(root, "locked.db");
        await File.WriteAllTextAsync(dataFile, "placeholder");

        using var _ = File.Open(dataFile, FileMode.Open, FileAccess.Read, FileShare.None);
        var service = new SqliteDbService();
        try
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.Open(new DbOptions
            {
                ConnectionString = $"Data Source={dataFile}",
                Name = "sqlite-locked-file",
            }));

            Assert.Contains(Path.GetFullPath(dataFile), exception.Message);
            Assert.Contains("file-read-write-probe", exception.Message);
        }
        finally
        {
            await service.Close();
        }
    }

    /// <summary>
    /// 创建测试状态实例。
    /// </summary>
    private static SqliteQueryTestState CreateState(string name, int group, int score, string note = null)
    {
        return new SqliteQueryTestState
        {
            Id = Interlocked.Increment(ref _idSeed),
            Name = name,
            Group = group,
            Score = score,
            OptionalNote = note,
        };
    }

    /// <summary>
    /// 测试 harness：每次执行创建独立内存库 service（嵌入式，无环境变量门控）。
    /// </summary>
    private static async Task ExecuteWithServiceAsync(Func<SqliteDbService, Task> action, string name = "sqlite-query-tests")
    {
        var service = new SqliteDbService();
        var opened = await service.Open(CreateMemoryOptions(name));
        Assert.True(opened);

        try
        {
            await action(service);
        }
        finally
        {
            await service.Close();
        }
    }

    /// <summary>
    /// 构造内存模式连接配置。
    /// </summary>
    private static DbOptions CreateMemoryOptions(string name)
    {
        return new DbOptions
        {
            ConnectionString = "Data Source=:memory:",
            Name = name,
        };
    }

    /// <summary>
    /// 创建独立临时目录（Dispose 时清理）。
    /// </summary>
    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gfx-sqlite-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        _tempPaths.Add(path);
        return path;
    }

    /// <summary>
    /// 置目录只读（POSIX 555）。
    /// </summary>
    private static void MakeReadOnly(string path)
    {
        chmod(path, "555");
    }

    /// <summary>
    /// 恢复目录可写（POSIX 755）。
    /// </summary>
    private static void MakeWritable(string path)
    {
        chmod(path, "755");
    }

    /// <summary>
    /// 调用 chmod 调整目录权限。
    /// </summary>
    private static void chmod(string path, string mode)
    {
        using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("chmod", $"{mode} {path}")
        {
            UseShellExecute = false,
        });
        process?.WaitForExit(5000);
    }

    /// <summary>
    /// SQLite 契约测试嵌套项实体。
    /// </summary>
    private sealed class SqliteNestedItem
    {
        /// <summary>
        /// 名称字段。
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 数值字段。
        /// </summary>
        public int Value { get; set; }
    }

    /// <summary>
    /// SQLite 契约测试状态实体。
    /// </summary>
    private sealed class SqliteQueryTestState : BaseCacheState
    {
        /// <summary>
        /// 名称字段。
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 分组字段。
        /// </summary>
        public int Group { get; set; }

        /// <summary>
        /// 分数字段。
        /// </summary>
        public int Score { get; set; }

        /// <summary>
        /// 可为空的备注字段。
        /// </summary>
        public string OptionalNote { get; set; }

        /// <summary>
        /// 嵌套集合属性（字符串标签）。
        /// </summary>
        public List<string> Tags { get; set; }

        /// <summary>
        /// 嵌套集合属性（结构化条目）。
        /// </summary>
        public List<SqliteNestedItem> Items { get; set; }

        /// <summary>
        /// LoadFromDbPostHandler 是否被调用（钩子语义断言用）。
        /// </summary>
        public bool LoadHookInvoked { get; private set; }

        /// <summary>
        /// LoadFromDbPostHandler 最近一次的 isNew 参数。
        /// </summary>
        public bool LoadHookIsNew { get; private set; }

        /// <summary>
        /// 记录钩子调用（命中 isNew=false / 创建 isNew=true）。
        /// </summary>
        public override void LoadFromDbPostHandler(bool isNew = false)
        {
            LoadHookInvoked = true;
            LoadHookIsNew = isNew;
            base.LoadFromDbPostHandler(isNew);
        }

        /// <summary>
        /// 转换字节数组。
        /// </summary>
        public override byte[] ToBytes()
        {
            var tags = Tags == null ? string.Empty : string.Join(",", Tags);
            var items = Items == null ? string.Empty : string.Join(",", Items.Select(static i => $"{i.Name}:{i.Value}"));
            var data = $"{Id}|{Name}|{Group}|{Score}|{OptionalNote}|{IsDeleted}|{DeleteTime}|{UpdateCount}|{UpdateTime}|{tags}|{items}";
            return Encoding.UTF8.GetBytes(data);
        }
    }

    /// <summary>
    /// 清理临时目录（恢复权限后递归删除）。
    /// </summary>
    public void Dispose()
    {
        foreach (var path in _tempPaths)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    MakeWritable(path);
                    foreach (var descendant in Directory.GetDirectories(path, "*", SearchOption.AllDirectories))
                    {
                        MakeWritable(descendant);
                    }

                    Directory.Delete(path, recursive: true);
                }
            }
            catch
            {
                // 临时目录清理失败不影响测试结果
            }
        }
    }
}
