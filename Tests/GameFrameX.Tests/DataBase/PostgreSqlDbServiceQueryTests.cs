// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
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
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
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
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.DataBase;
using GameFrameX.DataBase.Abstractions;
using GameFrameX.DataBase.PostgreSql;
using Npgsql;
using Xunit;

namespace GameFrameX.Tests.DataBase;

/// <summary>
/// PostgreSqlDbService 契约测试（C166 AC-2：用例集语义对齐 MongoDbServiceQueryTests）。
/// </summary>
/// <remarks>
/// Contract tests for <c>PostgreSqlDbService</c> (C166 AC-2: semantics aligned with <c>MongoDbServiceQueryTests</c>).
/// 门控：<c>GAMEFRAMEX_TEST_POSTGRESQL_CONNECTION_STRING</c>（空则静默跳过，对齐 Mongo 门控模式）；
/// 每次执行创建独立 database，Dispose 时 WITH (FORCE) 删除。
/// </remarks>
[Collection(nameof(GameDbStaticStateCollection))]
public sealed class PostgreSqlDbServiceQueryTests
{
    private static long _idSeed = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

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

            var result = await service.FindAsync<PostgreSqlQueryTestState>(state.Id, isCreateIfNotExists: false);

            Assert.NotNull(result);
            Assert.Equal(state.Id, result.Id);
            Assert.Equal("player-1", result.Name);
            Assert.Equal(30, result.Score);
        });
    }

    /// <summary>
    /// 测试按 ID 查询不存在数据且不自动创建时返回空。
    /// </summary>
    [Fact]
    public async Task FindAsync_ById_WhenNotExistsAndNoCreate_ShouldReturnNull()
    {
        await ExecuteWithServiceAsync(async service =>
        {
            var result = await service.FindAsync<PostgreSqlQueryTestState>(Interlocked.Increment(ref _idSeed), isCreateIfNotExists: false);
            Assert.Null(result);
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

            var list = await service.FindListAsync<PostgreSqlQueryTestState>(x => x.Group == 1);
            var count = await service.CountAsync<PostgreSqlQueryTestState>(x => x.Group == 1);
            var anyGroup1 = await service.AnyAsync<PostgreSqlQueryTestState>(x => x.Group == 1);
            var anyDeleted = await service.AnyAsync<PostgreSqlQueryTestState>(x => x.Id == b.Id);
            var countIncludeDeleted = await service.CountAsync<PostgreSqlQueryTestState>(x => x.Group == 1, includeDeleted: true);

            Assert.Single(list);
            Assert.Equal(a.Id, list[0].Id);
            Assert.Equal(1, count);
            Assert.Equal(2, countIncludeDeleted);
            Assert.True(anyGroup1);
            Assert.False(anyDeleted);
        });
    }

    /// <summary>
    /// 测试根据ID判断存在性接口。
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

            var existsVisible = await service.ExistsByIdAsync<PostgreSqlQueryTestState>(visible.Id);
            var existsDeleted = await service.ExistsByIdAsync<PostgreSqlQueryTestState>(deleted.Id);
            var existsMissing = await service.ExistsByIdAsync<PostgreSqlQueryTestState>(Interlocked.Increment(ref _idSeed));

            Assert.True(existsVisible);
            Assert.False(existsDeleted);
            Assert.False(existsMissing);
        });
    }

    /// <summary>
    /// 测试按条件查询不存在且允许创建时返回新实例（Id 由生成器分配）。
    /// </summary>
    [Fact]
    public async Task FindAsync_ByFilter_WhenNotExistsAndCreate_ShouldCreateState()
    {
        await ExecuteWithServiceAsync(async service =>
        {
            var state = await service.FindAsync<PostgreSqlQueryTestState>(x => x.Name == "transient-new");
            Assert.NotNull(state);
            Assert.True(state.Id > 0);
            Assert.Null(state.Name);
            Assert.True(state.CreatedTime > 0);
            // 未落库：Count 仍为 0
            Assert.Equal(0, await service.CountAsync<PostgreSqlQueryTestState>(x => x.Name == "transient-new"));
        });
    }

    /// <summary>
    /// 测试插入一条数据。
    /// </summary>
    [Fact]
    public async Task AddAsync_ShouldInsertOneRecord()
    {
        await ExecuteWithServiceAsync(async service =>
        {
            var state = CreateState("add-one", group: 3, score: 42);
            await service.AddAsync(state);

            var count = await service.CountAsync<PostgreSqlQueryTestState>(x => x.Name == "add-one");
            Assert.Equal(1, count);
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
            await service.AddListAsync(Array.Empty<PostgreSqlQueryTestState>());
            var total = await service.CountAsync<PostgreSqlQueryTestState>(x => true);
            Assert.Equal(0, total);
        });
    }

    /// <summary>
    /// 测试 Upsert：先插入后更新保持单行。
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

            var list = await service.FindListAsync<PostgreSqlQueryTestState>(x => x.Name == "upsert-one");
            Assert.Single(list);
            Assert.Equal(99, list[0].Score);
            Assert.Equal(2, list[0].UpdateCount);
        });
    }

    /// <summary>
    /// 测试软删后 AddOrUpdate 恢复可见（对齐 Mongo ReplaceOne 整文档替换语义）。
    /// </summary>
    [Fact]
    public async Task AddOrUpdateAsync_AfterSoftDelete_ShouldRestoreVisibility()
    {
        await ExecuteWithServiceAsync(async service =>
        {
            var state = CreateState("revive", group: 5, score: 7);
            await service.AddOrUpdateAsync(state);
            await service.DeleteAsync<PostgreSqlQueryTestState>(x => x.Name == "revive");
            Assert.Equal(0, await service.CountAsync<PostgreSqlQueryTestState>(x => x.Name == "revive"));

            state.Score = 8;
            await service.AddOrUpdateAsync(state);
            var revived = await service.FindAsync<PostgreSqlQueryTestState>(x => x.Name == "revive", isCreateIfNotExists: false);
            Assert.NotNull(revived);
            Assert.Equal(8, revived.Score);
        });
    }

    /// <summary>
    /// 测试单条更新将 null 字段移除（对齐 Mongo $unset 语义，读回为 null）。
    /// </summary>
    [Fact]
    public async Task UpdateAsync_Single_ShouldSupportUnsetNullField()
    {
        await ExecuteWithServiceAsync(async service =>
        {
            var state = CreateState("unset-null", group: 6, score: 3, note: "to-be-removed");
            await service.AddAsync(state);
            var loaded = await service.FindAsync<PostgreSqlQueryTestState>(state.Id, isCreateIfNotExists: false);
            Assert.NotNull(loaded);
            loaded.LoadFromDbPostHandler();
            loaded.OptionalNote = null;
            await service.UpdateAsync(loaded);

            var reread = await service.FindAsync<PostgreSqlQueryTestState>(state.Id, isCreateIfNotExists: false);
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
            var loadedA = await service.FindAsync<PostgreSqlQueryTestState>(a.Id, isCreateIfNotExists: false);
            var loadedB = await service.FindAsync<PostgreSqlQueryTestState>(b.Id, isCreateIfNotExists: false);
            Assert.NotNull(loadedA);
            Assert.NotNull(loadedB);
            loadedA.LoadFromDbPostHandler();
            loadedB.LoadFromDbPostHandler();
            loadedA.Score = 100;

            var changed = await service.UpdateAsync(new[] { loadedA, loadedB });
            Assert.Equal(1, changed);

            var refreshedA = await service.FindAsync<PostgreSqlQueryTestState>(a.Id, isCreateIfNotExists: false);
            Assert.NotNull(refreshedA);
            // StateHash 快照语义（与 Mongo 同构）：SaveToDbPostHandler 记录的是时间戳 bump 前的 hash 快照，
            // 已更新过的对象再次 UpdateAsync 仍视为已变更（Mongo 版测试同样不含二次返 0 断言）。
            var secondCall = await service.UpdateAsync(new[] { loadedA, loadedB });
            Assert.Equal(1, secondCall);
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
            Assert.Equal(0, await service.CountAsync<PostgreSqlQueryTestState>(x => x.Name == "del-state"));
            Assert.Equal(1, await service.CountAsync<PostgreSqlQueryTestState>(x => x.Name == "del-state", includeDeleted: true));
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

            var first = await service.DeleteAsync<PostgreSqlQueryTestState>(x => x.Name == "del-idem");
            var second = await service.DeleteAsync<PostgreSqlQueryTestState>(x => x.Name == "del-idem");

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

            var deleted = await service.DeleteListAsync<PostgreSqlQueryTestState>(x => x.Group == 10);
            Assert.Equal(2, deleted);
            Assert.Equal(1, await service.CountAsync<PostgreSqlQueryTestState>(x => x.Group == 11));
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

            var deleted = await service.DeleteListIdAsync<PostgreSqlQueryTestState>(new[] { a.Id, b.Id });
            var emptyDeleted = await service.DeleteListIdAsync<PostgreSqlQueryTestState>(Array.Empty<long>());

            Assert.Equal(2, deleted);
            Assert.Equal(0, emptyDeleted);
            Assert.Equal(1, await service.CountAsync<PostgreSqlQueryTestState>(x => x.Group == 12));
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

            var restored = await service.RestoreAsync<PostgreSqlQueryTestState>(x => x.Name == "restore-me");
            Assert.Equal(1, restored);

            var revived = await service.FindAsync<PostgreSqlQueryTestState>(state.Id, isCreateIfNotExists: false);
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

            var removed = await service.HardDeleteAsync<PostgreSqlQueryTestState>(x => x.Name == "hard-del");
            Assert.Equal(1, removed);
            Assert.Equal(0, await service.CountAsync<PostgreSqlQueryTestState>(x => x.Name == "hard-del", includeDeleted: true));
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

            var descFirst = await service.FindSortDescendingFirstOneAsync<PostgreSqlQueryTestState>(x => x.Group == 15, x => x.Score);
            var ascFirst = await service.FindSortAscendingFirstOneAsync<PostgreSqlQueryTestState>(x => x.Group == 15, x => x.Score);
            Assert.Equal(30, descFirst.Score);
            Assert.Equal(10, ascFirst.Score);

            var descPage = await service.FindSortDescendingAsync<PostgreSqlQueryTestState>(x => x.Group == 15, x => x.Score, pageIndex: 0, pageSize: 2);
            var ascPage = await service.FindSortAscendingAsync<PostgreSqlQueryTestState>(x => x.Group == 15, x => x.Score, pageIndex: 1, pageSize: 2);
            Assert.Equal(new[] { 30, 20 }, descPage.Select(static s => s.Score).ToArray());
            Assert.Equal(new[] { 30 }, ascPage.Select(static s => s.Score).ToArray());

            var (pageItems, total) = await service.FindPageAsync<PostgreSqlQueryTestState>(x => x.Group == 15, x => x.Score, descending: true, pageIndex: 1, pageSize: 2);
            Assert.Equal(3, total);
            Assert.Equal(new[] { 10 }, pageItems.Select(static s => s.Score).ToArray());
        });
    }

    /// <summary>
    /// 测试投影查询（成员绑定投影）。
    /// </summary>
    [Fact]
    public async Task FindProjectedAsync_ShouldProjectMembers()
    {
        await ExecuteWithServiceAsync(async service =>
        {
            await service.AddListAsync(new[]
            {
                CreateState("proj-1", group: 16, score: 1),
                CreateState("proj-2", group: 16, score: 2),
            });

            var names = await service.FindProjectedAsync<PostgreSqlQueryTestState, string>(x => x.Group == 16, x => x.Name);
            Assert.Equal(new[] { "proj-1", "proj-2" }.OrderBy(static n => n), names.OrderBy(static n => n));
        });
    }

    /// <summary>
    /// 测试部分更新：字段写入、字段移除、时间戳与更新计数推进。
    /// </summary>
    [Fact]
    public async Task UpdatePartialAsync_ShouldSetAndUnsetFields()
    {
        await ExecuteWithServiceAsync(async service =>
        {
            var state = CreateState("partial", group: 17, score: 5, note: "keep");
            await service.AddAsync(state);

            var modified = await service.UpdatePartialAsync<PostgreSqlQueryTestState>(state.Id, new Dictionary<string, object>
            {
                { nameof(PostgreSqlQueryTestState.Score), 77 },
                { nameof(PostgreSqlQueryTestState.OptionalNote), null },
            });
            Assert.Equal(1, modified);

            var reread = await service.FindAsync<PostgreSqlQueryTestState>(state.Id, isCreateIfNotExists: false);
            Assert.Equal(77, reread.Score);
            Assert.Null(reread.OptionalNote);
            Assert.Equal(1, reread.UpdateCount);
            Assert.NotNull(reread.UpdateTime);
        });
    }

    /// <summary>
    /// 测试 SaveBulk 批量 upsert：逐批 ack、不触碰时间戳、重复保存幂等。
    /// </summary>
    [Fact]
    public async Task SaveBulkAsync_ShouldUpsertBatchesAndKeepTimestamps()
    {
        await ExecuteWithServiceAsync(async service =>
        {
            var states = Enumerable.Range(0, 7).Select(i => CreateState($"bulk-{i}", group: 18, score: i)).ToArray();
            states[0].CreatedTime = 111;
            states[0].UpdateTime = 222;
            states[0].UpdateCount = 5;

            var acknowledged = await service.SaveBulkAsync(states, batchSize: 3);
            Assert.Equal(7, acknowledged.Count);

            var stored = await service.FindAsync<PostgreSqlQueryTestState>(states[0].Id, isCreateIfNotExists: false);
            Assert.Equal(111, stored.CreatedTime);
            Assert.Equal(222, stored.UpdateTime);
            Assert.Equal(5, stored.UpdateCount);

            states[0].Score = 1000;
            var acknowledgedAgain = await service.SaveBulkAsync(states, batchSize: 4);
            Assert.Equal(7, acknowledgedAgain.Count);
            Assert.Equal(7, await service.CountAsync<PostgreSqlQueryTestState>(x => x.Group == 18));

            var updated = await service.FindAsync<PostgreSqlQueryTestState>(states[0].Id, isCreateIfNotExists: false);
            Assert.Equal(1000, updated.Score);
        });
    }

    /// <summary>
    /// 测试事务壳：action 成功提交、失败不产生 DatabaseUnavailableException 且 action 已执行。
    /// </summary>
    [Fact]
    public async Task ExecuteInTransactionAsync_ShouldRunActionOnce()
    {
        await ExecuteWithServiceAsync(async service =>
        {
            var executions = 0;
            await service.ExecuteInTransactionAsync(async () =>
            {
                Interlocked.Increment(ref executions);
                await Task.CompletedTask;
            });

            Assert.Equal(1, executions);

            // Mongo 语义：非重试异常经重试耗尽路径包装为 DatabaseUnavailableException
            // Mongo semantics: non-retryable failures end up wrapped in DatabaseUnavailableException (retry-exhausted path).
            await Assert.ThrowsAsync<DatabaseUnavailableException>(() => service.ExecuteInTransactionAsync(() => throw new InvalidOperationException("boom")));
        });
    }

    /// <summary>
    /// 测试表达式方言：字符串方法与 null 语义（NotEqual 包含缺失字段行，对齐 C#/MQL）。
    /// </summary>
    [Fact]
    public async Task ExpressionDialect_StringMethodsAndNullSemantics()
    {
        await ExecuteWithServiceAsync(async service =>
        {
            var withNote = CreateState("dialect-1", group: 19, score: 1, note: "alpha-beta");
            var withoutNote = CreateState("dialect-2", group: 19, score: 2);
            await service.AddListAsync(new[] { withNote, withoutNote });

            var contains = await service.FindListAsync<PostgreSqlQueryTestState>(x => x.OptionalNote.Contains("beta"));
            var starts = await service.FindListAsync<PostgreSqlQueryTestState>(x => x.OptionalNote.StartsWith("alpha"));
            var ends = await service.FindListAsync<PostgreSqlQueryTestState>(x => x.OptionalNote.EndsWith("beta"));
            Assert.Single(contains);
            Assert.Single(starts);
            Assert.Single(ends);

            // null 检查与 NotEqual 缺失字段行保留（C# null != "x" 为真）
            var nulls = await service.FindListAsync<PostgreSqlQueryTestState>(x => x.OptionalNote == null);
            var notEqual = await service.CountAsync<PostgreSqlQueryTestState>(x => x.OptionalNote != "alpha-beta");
            Assert.Single(nulls);
            Assert.Equal(1, notEqual);

            // 布尔逻辑组合
            var combined = await service.FindListAsync<PostgreSqlQueryTestState>(x => x.Group == 19 && (x.Score > 1 || x.OptionalNote == null));
            Assert.Single(combined);
        });
    }

    /// <summary>
    /// 测试不可翻译节点显式失败（C168：EF 翻译器接管方言，超集节点可翻译，不可翻译节点抛 InvalidOperationException，绝不静默内存过滤）。
    /// </summary>
    [Fact]
    public async Task UntranslatableExpression_ShouldThrowExplicitly()
    {
        await ExecuteWithServiceAsync(async service =>
        {
            // 客户端方法调用无法翻译：显式失败（C166 时代为 NotSupportedException，C168 起为 EF 的 InvalidOperationException）
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.FindListAsync<PostgreSqlQueryTestState>(x => IsLongName(x.Name)));

            // 旧翻译器方言矩阵之外的节点（string.Length）在 EF 翻译器下可用（覆盖面升级）
            var state = CreateState("ef-superset", group: 22, score: 1);
            await service.AddAsync(state);
            var longNames = await service.FindListAsync<PostgreSqlQueryTestState>(x => x.Name.Length > 2);
            Assert.Single(longNames);
        });
    }

    /// <summary>
    /// 判定名称是否为长名的本地方法（仅供不可翻译表达式测试使用）。
    /// </summary>
    private static bool IsLongName(string name)
    {
        return name?.Length > 2;
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

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.FindListAsync<PostgreSqlQueryTestState>(x => x.Group == 20, canceled.Token));
            var state = CreateState("cancel", group: 20, score: 1);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.AddAsync(state, canceled.Token));
        });
    }

    /// <summary>
    /// 测试 FindByIds 只返回可见记录。
    /// </summary>
    [Fact]
    public async Task FindByIdsAsync_ShouldReturnVisibleRecordsOnly()
    {
        await ExecuteWithServiceAsync(async service =>
        {
            var a = CreateState("byids-a", group: 21, score: 1);
            var b = CreateState("byids-b", group: 21, score: 2);
            var c = CreateState("byids-c", group: 21, score: 3);
            await service.AddListAsync(new[] { a, b, c });
            await service.DeleteAsync(b);

            var found = await service.FindByIdsAsync<PostgreSqlQueryTestState>(new[] { a.Id, b.Id, c.Id });
            Assert.Equal(new[] { a.Id, c.Id }.OrderBy(static id => id), found.Select(static s => s.Id).OrderBy(static id => id));
        });
    }

    /// <summary>
    /// 创建测试状态实例。
    /// </summary>
    private static PostgreSqlQueryTestState CreateState(string name, int group, int score, string note = null)
    {
        return new PostgreSqlQueryTestState
        {
            Id = Interlocked.Increment(ref _idSeed),
            Name = name,
            Group = group,
            Score = score,
            OptionalNote = note,
        };
    }

    /// <summary>
    /// 测试 harness：门控 + 独立库创建/销毁。
    /// </summary>
    private static async Task ExecuteWithServiceAsync(Func<PostgreSqlDbService, Task> action)
    {
        var connectionString = Environment.GetEnvironmentVariable("GAMEFRAMEX_TEST_POSTGRESQL_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var dbName = $"gameframex_test_{Guid.NewGuid():N}".ToLowerInvariant();
        var adminConnectionString = WithDatabase(connectionString, "postgres");
        await using (var admin = new NpgsqlConnection(adminConnectionString))
        {
            await admin.OpenAsync();
            await using var createCommand = new NpgsqlCommand($"CREATE DATABASE \"{dbName}\"", admin);
            await createCommand.ExecuteNonQueryAsync();
        }

        var service = new PostgreSqlDbService();
        var options = new DbOptions
        {
            Type = "PostgreSql",
            ConnectionString = WithDatabase(connectionString, dbName),
            Name = dbName,
        };

        var opened = await service.Open(options);
        Assert.True(opened);

        try
        {
            await action(service);
        }
        finally
        {
            await service.Close();
            try
            {
                await using var admin = new NpgsqlConnection(adminConnectionString);
                await admin.OpenAsync();
                await using var dropCommand = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{dbName}\" WITH (FORCE)", admin);
                await dropCommand.ExecuteNonQueryAsync();
            }
            catch
            {
            }
        }
    }

    /// <summary>
    /// 替换连接串的目标数据库。
    /// </summary>
    private static string WithDatabase(string connectionString, string database)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { Database = database };
        return builder.ConnectionString;
    }

    /// <summary>
    /// PG 契约测试状态实体。
    /// </summary>
    private sealed class PostgreSqlQueryTestState : BaseCacheState
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
        /// 转换字节数组。
        /// </summary>
        public override byte[] ToBytes()
        {
            var data = $"{Id}|{Name}|{Group}|{Score}|{OptionalNote}|{IsDeleted}|{DeleteTime}|{UpdateCount}|{UpdateTime}";
            return Encoding.UTF8.GetBytes(data);
        }
    }
}
