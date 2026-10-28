// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and applicable international regulations.
//   使用本项目须严格遵守相应法律法规及开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file at the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:     https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.DataBase;
using GameFrameX.DataBase.Abstractions;
using GameFrameX.DataBase.Sqlite;
using GameFrameX.Foundation.Orm.Attribute;
using Microsoft.Data.Sqlite;
using Xunit;

namespace GameFrameX.Tests.DataBase;

/// <summary>
/// SQLite TryAddUniqueAsync 引擎唯一索引路径行为测试（GFX-1036 / C326）。
/// </summary>
/// <remarks>
/// Behavioral tests for the SQLite TryAddUniqueAsync engine-index path (GFX-1036 / C326). The declared
/// unique-index bootstrap must materialize <c>[EntityIndex(Unique = true)]</c> as a
/// <c>json_extract</c> expression unique index; reducible predicates must write through the single-statement
/// <c>ON CONFLICT DO NOTHING</c> path with scan-parity results; every non-reducible shape must keep the
/// legacy scan semantics. These facts use the public API only, so they also compile against the pre-change
/// implementation and fail there (index never materializes; plain duplicate inserts pass; a rolled-back
/// transaction that created the table bricks later access with "no such table").
/// </remarks>
[Collection(nameof(GameDbStaticStateCollection))]
public sealed class SqliteTryAddUniqueFastPathTests : IDisposable
{
    private static long _idSeed = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private readonly System.Collections.Generic.List<string> _tempPaths = new();

    /// <summary>
    /// 声明式唯一索引落地：首次写入后 sqlite_master 内出现类型前缀命名的 UNIQUE json_extract 表达式索引。
    /// </summary>
    [Fact]
    public async Task TryAddUnique_DeclaredUniqueIndex_ShouldMaterializeJsonExtractUniqueIndex()
    {
        var root = CreateTempDirectory();
        var dataFile = Path.Combine(root, "unique-index.db");
        var service = new SqliteDbService();
        Assert.True(await service.OpenAsync(new DbOptions { ConnectionString = $"Data Source={dataFile}", Name = "sqlite-fast-path-index" }));
        try
        {
            Assert.True(await service.TryAddUniqueAsync(new FastPathUniqueState { Id = NextId(), Identifier = "seed" }, x => x.Identifier == "seed"));

            await using (var rawConnection = new SqliteConnection($"Data Source={dataFile}"))
            {
                await rawConnection.OpenAsync();
                using (var definitionCommand = rawConnection.CreateCommand())
                {
                    definitionCommand.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'index' AND tbl_name = 'FastPathUniqueState' AND name = 'FastPathUniqueState_ux_fast_path_identifier';";
                    var definition = await definitionCommand.ExecuteScalarAsync() as string;
                    Assert.NotNull(definition);
                    Assert.StartsWith("CREATE UNIQUE INDEX ", definition, StringComparison.OrdinalIgnoreCase);
                    Assert.Contains("json_extract(doc, '$.\"Identifier\"')", definition);
                }

                using (var indexListCommand = rawConnection.CreateCommand())
                {
                    indexListCommand.CommandText = "SELECT \"unique\" FROM pragma_index_list('FastPathUniqueState') WHERE name = 'FastPathUniqueState_ux_fast_path_identifier';";
                    Assert.Equal(1L, await indexListCommand.ExecuteScalarAsync());
                }
            }
        }
        finally
        {
            await service.CloseAsync();
        }
    }

    /// <summary>
    /// 快路径等价性：同键插入恰一条、重复键 / 同 Id 不同键均返回 false 且不抛异常。
    /// </summary>
    [Fact]
    public async Task TryAddUnique_DeclaredUniqueMember_DuplicateKeyOrSameId_ShouldReturnFalse()
    {
        await ExecuteWithMemoryAsync(async service =>
        {
            var first = new FastPathUniqueState { Id = NextId(), Identifier = "alpha" };
            Assert.True(await service.TryAddUniqueAsync(first, x => x.Identifier == "alpha"));

            var duplicateKey = new FastPathUniqueState { Id = NextId(), Identifier = "alpha", Payload = "second" };
            Assert.False(await service.TryAddUniqueAsync(duplicateKey, x => x.Identifier == "alpha"));

            var sameId = new FastPathUniqueState { Id = first.Id, Identifier = "beta" };
            Assert.False(await service.TryAddUniqueAsync(sameId, x => x.Identifier == "beta"));

            Assert.Equal(1L, await service.CountAsync<FastPathUniqueState>(x => x.Identifier == "alpha"));
            Assert.Equal(0L, await service.CountAsync<FastPathUniqueState>(x => x.Identifier == "beta"));
        });
    }

    /// <summary>
    /// 软删行仍占用唯一键（契约：唯一性无视软删可见性；软删整档重写保留唯一字段）。
    /// </summary>
    [Fact]
    public async Task TryAddUnique_DeclaredUniqueMember_SoftDeletedOccupant_ShouldStillReturnFalse()
    {
        await ExecuteWithMemoryAsync(async service =>
        {
            var first = new FastPathUniqueState { Id = NextId(), Identifier = "gamma" };
            Assert.True(await service.TryAddUniqueAsync(first, x => x.Identifier == "gamma"));
            Assert.Equal(1L, await service.DeleteAsync(first));

            var revived = new FastPathUniqueState { Id = NextId(), Identifier = "gamma", Payload = "second" };
            Assert.False(await service.TryAddUniqueAsync(revived, x => x.Identifier == "gamma"));
        });
    }

    /// <summary>
    /// 谓词常量与自身值不一致时必须回落扫描路径：谓词匹配既有行即拒绝，而非按自身键放行。
    /// </summary>
    [Fact]
    public async Task TryAddUnique_PredicateConstantDifferingFromOwnValue_ShouldKeepScanSemantics()
    {
        await ExecuteWithMemoryAsync(async service =>
        {
            var existing = new FastPathUniqueState { Id = NextId(), Identifier = "alpha" };
            Assert.True(await service.TryAddUniqueAsync(existing, x => x.Identifier == "alpha"));

            // 谓词检查 "alpha"（已有行命中）而候选自身键为 "beta"：契约语义 = false（扫描路径判定）。
            // The predicate probes "alpha" (an existing row) while the candidate's own key is "beta":
            // the contract says false (the scan-path verdict).
            var candidate = new FastPathUniqueState { Id = NextId(), Identifier = "beta" };
            Assert.False(await service.TryAddUniqueAsync(candidate, x => x.Identifier == "alpha"));

            // 谓词检查 "zzz"（无命中）且自身键 "beta" 未被占用：契约语义 = true。
            // The predicate probes "zzz" (no hit) and the candidate's own key "beta" is free: true.
            var secondCandidate = new FastPathUniqueState { Id = NextId(), Identifier = "beta" };
            Assert.True(await service.TryAddUniqueAsync(secondCandidate, x => x.Identifier == "zzz"));
            Assert.Equal(1L, await service.CountAsync<FastPathUniqueState>(x => x.Identifier == "beta"));
        });
    }

    /// <summary>
    /// 复合谓词回落扫描路径：命中任一分支即拒绝，全不命中才插入。
    /// </summary>
    [Fact]
    public async Task TryAddUnique_CompositePredicate_ShouldKeepScanSemantics()
    {
        await ExecuteWithMemoryAsync(async service =>
        {
            Assert.True(await service.TryAddUniqueAsync(new FastPathUniqueState { Id = NextId(), Identifier = "one" }, x => x.Identifier == "one"));

            var blocked = new FastPathUniqueState { Id = NextId(), Identifier = "two" };
            Assert.False(await service.TryAddUniqueAsync(blocked, x => x.Identifier == "zzz" || x.Identifier == "one"));

            var allowed = new FastPathUniqueState { Id = NextId(), Identifier = "three" };
            Assert.True(await service.TryAddUniqueAsync(allowed, x => x.Identifier == "zzz" || x.Identifier == "yyy"));
            Assert.Equal(1L, await service.CountAsync<FastPathUniqueState>(x => x.Identifier == "three"));
        });
    }

    /// <summary>
    /// 未声明唯一索引的类型保持扫描路径：顺序冲突仍返回 false（引擎索引缺失由先查后插兜底）。
    /// </summary>
    [Fact]
    public async Task TryAddUnique_UndeclaredMember_ShouldKeepScanSemantics()
    {
        await ExecuteWithMemoryAsync(async service =>
        {
            Assert.True(await service.TryAddUniqueAsync(new FastPathPlainState { Id = NextId(), Identifier = "plain" }, x => x.Identifier == "plain"));
            Assert.False(await service.TryAddUniqueAsync(new FastPathPlainState { Id = NextId(), Identifier = "plain" }, x => x.Identifier == "plain"));
            Assert.Equal(1L, await service.CountAsync<FastPathPlainState>(x => x.Identifier == "plain"));
        });
    }

    /// <summary>
    /// 引擎级约束证明：普通 AddAsync 插入重复声明唯一值被数据库以唯一约束拒绝（改动前静默成功）。
    /// </summary>
    [Fact]
    public async Task AddAsync_DuplicateDeclaredUniqueValue_ShouldFailWithEngineConstraint()
    {
        await ExecuteWithMemoryAsync(async service =>
        {
            await service.AddAsync(new FastPathUniqueState { Id = NextId(), Identifier = "dup" });

            var exception = await Assert.ThrowsAsync<SqliteException>(() => service.AddAsync(new FastPathUniqueState { Id = NextId(), Identifier = "dup" }));
            Assert.True(exception.SqliteErrorCode is 19 or 2067, $"unexpected error code {exception.SqliteErrorCode}");
            Assert.Equal(1L, await service.CountAsync<FastPathUniqueState>(x => x.Identifier == "dup"));
        });
    }

    /// <summary>
    /// 跨连接并发（验收 #2）：两个独立连接对同键并发唯一插入，恰一条入表、另一条返回 false 不抛异常。
    /// </summary>
    [Fact]
    public async Task TryAddUnique_ConcurrentSameKeyAcrossConnections_ExactlyOneInserts()
    {
        var root = CreateTempDirectory();
        var dataFile = Path.Combine(root, "race.db");
        var first = new SqliteDbService();
        var second = new SqliteDbService();
        Assert.True(await first.OpenAsync(new DbOptions { ConnectionString = $"Data Source={dataFile}", Name = "sqlite-fast-path-race-a" }));
        try
        {
            Assert.True(await second.OpenAsync(new DbOptions { ConnectionString = $"Data Source={dataFile}", Name = "sqlite-fast-path-race-b" }));
            try
            {
                // 预热：表与唯一索引先落地，竞态只发生在插入语句上。
                // Warm-up: the table and unique index exist first, so the race is purely on the insert.
                Assert.True(await first.TryAddUniqueAsync(new FastPathUniqueState { Id = NextId(), Identifier = "warmup" }, x => x.Identifier == "warmup"));

                var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(index => Task.Run(async () =>
                {
                    var racer = new FastPathUniqueState { Id = NextId(), Identifier = "race-key", Payload = $"racer-{index}" };
                    return await (index == 0 ? first : second).TryAddUniqueAsync(racer, x => x.Identifier == "race-key");
                })));

                Assert.Equal(1, results.Count(won => won));
                Assert.Equal(1L, await first.CountAsync<FastPathUniqueState>(x => x.Identifier == "race-key"));
            }
            finally
            {
                await second.CloseAsync();
            }
        }
        finally
        {
            await first.CloseAsync();
        }
    }

    /// <summary>
    /// 回滚重建（C326 配套修复）：事务内首建的表与索引随回滚撤销后，就绪缓存失效、下次访问重建，唯一性防线恢复。
    /// </summary>
    [Fact]
    public async Task TryAddUnique_TableCreatedInRolledBackTransaction_ShouldRebuildAndStayUnique()
    {
        await ExecuteWithMemoryAsync(async service =>
        {
            var rolledBack = new FastPathUniqueState { Id = NextId(), Identifier = "rolled" };
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteInTransactionAsync(async () =>
            {
                Assert.True(await service.TryAddUniqueAsync(rolledBack, x => x.Identifier == "rolled"));
                throw new InvalidOperationException("tx-rollback-boom");
            }));

            // 回滚清除了表与索引：此处在改动前抛「no such table」，修复后重建并可继续插入。
            // The rollback removed the table and index: pre-change this throws "no such table";
            // with the fix the bootstrap re-runs and the insert proceeds.
            Assert.True(await service.TryAddUniqueAsync(new FastPathUniqueState { Id = NextId(), Identifier = "fresh" }, x => x.Identifier == "fresh"));
            Assert.False(await service.TryAddUniqueAsync(new FastPathUniqueState { Id = NextId(), Identifier = "fresh" }, x => x.Identifier == "fresh"));
            Assert.Null(await service.FindAsync<FastPathUniqueState>(rolledBack.Id, isCreateIfNotExists: false));
        });
    }

    /// <summary>
    /// 存量脏数据显式失败：表内已存在重复唯一值时，索引引导以 SqliteException(19) 上抛而非静默降级。
    /// </summary>
    [Fact]
    public async Task TryAddUnique_LegacyDuplicateData_ShouldFailExplicitlyOnIndexBootstrap()
    {
        var root = CreateTempDirectory();
        var dataFile = Path.Combine(root, "dirty.db");
        await using (var rawConnection = new SqliteConnection($"Data Source={dataFile}"))
        {
            await rawConnection.OpenAsync();
            await using (var createCommand = rawConnection.CreateCommand())
            {
                createCommand.CommandText = "CREATE TABLE \"FastPathUniqueState\" (id INTEGER PRIMARY KEY, doc TEXT NOT NULL);";
                await createCommand.ExecuteNonQueryAsync();
            }

            await using (var insertCommand = rawConnection.CreateCommand())
            {
                insertCommand.CommandText = "INSERT INTO \"FastPathUniqueState\" (id, doc) VALUES (1, '{\"Identifier\":\"dup\"}'), (2, '{\"Identifier\":\"dup\"}');";
                await insertCommand.ExecuteNonQueryAsync();
            }
        }

        var service = new SqliteDbService();
        Assert.True(await service.OpenAsync(new DbOptions { ConnectionString = $"Data Source={dataFile}", Name = "sqlite-fast-path-dirty" }));
        try
        {
            var exception = await Assert.ThrowsAsync<SqliteException>(() => service.TryAddUniqueAsync(new FastPathUniqueState { Id = NextId(), Identifier = "other" }, x => x.Identifier == "other"));
            Assert.True(exception.SqliteErrorCode is 19 or 2067, $"unexpected error code {exception.SqliteErrorCode}");
        }
        finally
        {
            await service.CloseAsync();
        }
    }

    /// <summary>
    /// 索引漂移重建：同名索引残留旧 JSON 路径时按当前声明先删后建（IF NOT EXISTS 对同名索引为 no-op）。
    /// </summary>
    [Fact]
    public async Task TryAddUnique_DriftedIndexDefinition_ShouldRebuildToCurrentDeclaration()
    {
        var root = CreateTempDirectory();
        var dataFile = Path.Combine(root, "drift.db");
        var service = new SqliteDbService();
        Assert.True(await service.OpenAsync(new DbOptions { ConnectionString = $"Data Source={dataFile}", Name = "sqlite-fast-path-drift" }));
        try
        {
            Assert.True(await service.TryAddUniqueAsync(new FastPathUniqueState { Id = NextId(), Identifier = "seed" }, x => x.Identifier == "seed"));
            await service.CloseAsync();

            // 人为制造漂移：把索引替换为旧 JSON 路径上的同名唯一索引（模拟属性改名后的残留）。
            // Simulate drift: replace the index with a same-name unique index on a stale JSON path
            // (as if the property had been renamed).
            await using (var rawConnection = new SqliteConnection($"Data Source={dataFile}"))
            {
                await rawConnection.OpenAsync();
                await using (var driftCommand = rawConnection.CreateCommand())
                {
                    driftCommand.CommandText = "DROP INDEX IF EXISTS \"FastPathUniqueState_ux_fast_path_identifier\"; CREATE UNIQUE INDEX \"FastPathUniqueState_ux_fast_path_identifier\" ON \"FastPathUniqueState\" (json_extract(doc, '$.\"StaleIdentifier\"'));";
                    await driftCommand.ExecuteNonQueryAsync();
                }
            }

            Assert.True(await service.OpenAsync(new DbOptions { ConnectionString = $"Data Source={dataFile}", Name = "sqlite-fast-path-drift" }));

            // 重建后再访问：索引回到当前路径，重复键仍被拒绝（漂移残留会静默放行重复键）。
            // After the rebuild the index is back on the current path, so a duplicate key is still
            // rejected (the stale residue would silently admit it).
            Assert.False(await service.TryAddUniqueAsync(new FastPathUniqueState { Id = NextId(), Identifier = "seed" }, x => x.Identifier == "seed"));

            await using (var verifyConnection = new SqliteConnection($"Data Source={dataFile}"))
            {
                await verifyConnection.OpenAsync();
                using (var definitionCommand = verifyConnection.CreateCommand())
                {
                    definitionCommand.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'index' AND name = 'FastPathUniqueState_ux_fast_path_identifier';";
                    var definition = await definitionCommand.ExecuteScalarAsync() as string;
                    Assert.Contains("json_extract(doc, '$.\"Identifier\"')", definition);
                }
            }
        }
        finally
        {
            await service.CloseAsync();
        }
    }

    /// <summary>
    /// 事务内快路径：提交持久化、回滚丢弃，且环境事务内同键二次插入返回 false。
    /// </summary>
    [Fact]
    public async Task TryAddUnique_InsideAmbientTransaction_CommitPersistsAndRollbackDiscards()
    {
        await ExecuteWithMemoryAsync(async service =>
        {
            var committed = new FastPathUniqueState { Id = NextId(), Identifier = "tx-commit" };
            await service.ExecuteInTransactionAsync(async () =>
            {
                Assert.True(await service.TryAddUniqueAsync(committed, x => x.Identifier == "tx-commit"));
                Assert.False(await service.TryAddUniqueAsync(new FastPathUniqueState { Id = NextId(), Identifier = "tx-commit" }, x => x.Identifier == "tx-commit"));
            });
            Assert.NotNull(await service.FindAsync<FastPathUniqueState>(committed.Id, isCreateIfNotExists: false));

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteInTransactionAsync(async () =>
            {
                Assert.True(await service.TryAddUniqueAsync(new FastPathUniqueState { Id = NextId(), Identifier = "tx-rollback" }, x => x.Identifier == "tx-rollback"));
                throw new InvalidOperationException("tx-unique-boom");
            }));

            // 回滚后键位释放，可再次插入。
            // The rollback freed the key, so the same key can land again.
            Assert.True(await service.TryAddUniqueAsync(new FastPathUniqueState { Id = NextId(), Identifier = "tx-rollback" }, x => x.Identifier == "tx-rollback"));
            Assert.Equal(1L, await service.CountAsync<FastPathUniqueState>(x => x.Identifier == "tx-rollback"));
        });
    }

    /// <summary>
    /// 创建独立临时目录（Dispose 时清理）。
    /// </summary>
    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gfx-sqlite-fast-path-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        _tempPaths.Add(path);
        return path;
    }

    /// <summary>
    /// 内存库执行壳：每用例独立 service（嵌入式，无环境变量门控）。
    /// </summary>
    private static async Task ExecuteWithMemoryAsync(Func<SqliteDbService, Task> action)
    {
        var service = new SqliteDbService();
        Assert.True(await service.OpenAsync(new DbOptions { ConnectionString = "Data Source=:memory:", Name = "c326-fast-path-tests" }));
        try
        {
            await action(service);
        }
        finally
        {
            await service.CloseAsync();
        }
    }

    /// <summary>
    /// 生成唯一测试 Id。
    /// </summary>
    private static long NextId()
    {
        return Interlocked.Increment(ref _idSeed);
    }

    /// <summary>
    /// 清理临时目录（递归删除）。
    /// </summary>
    public void Dispose()
    {
        foreach (var path in _tempPaths)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch
            {
                // 临时目录清理失败不影响测试结果
            }
        }
    }

    /// <summary>
    /// 唯一插入快路径测试状态实体（<c>Identifier</c> 声明 provider 唯一索引）。
    /// </summary>
    private sealed class FastPathUniqueState : BaseCacheState
    {
        /// <summary>
        /// 唯一标识字段（provider 唯一索引声明）。
        /// </summary>
        [EntityIndex("ux_fast_path_identifier", Unique = true)]
        public string Identifier { get; set; }

        /// <summary>
        /// 载荷字段。
        /// </summary>
        public string Payload { get; set; }
    }

    /// <summary>
    /// 唯一插入扫描回落测试状态实体（无 provider 唯一索引声明——覆盖先查后插兜底路径）。
    /// </summary>
    private sealed class FastPathPlainState : BaseCacheState
    {
        /// <summary>
        /// 唯一标识字段。
        /// </summary>
        public string Identifier { get; set; }
    }
}
