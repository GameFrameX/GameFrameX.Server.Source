// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规及开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
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
//   CNB Repository:  https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.DataBase;
using GameFrameX.DataBase.Abstractions;
using GameFrameX.DataBase.Mongo;
using GameFrameX.DataBase.PostgreSql;
using GameFrameX.DataBase.Sqlite;
using GameFrameX.Foundation.Orm.Attribute;
using MongoDB.Driver;
using Npgsql;
using Xunit;

namespace GameFrameX.Tests.DataBase;

/// <summary>
/// IDatabaseService 条件写原语集（C247：CAS / 唯一插入 / 序列）跨 provider 语义等价与并发竞态测试。
/// </summary>
/// <remarks>
/// Cross-provider semantic-equivalence and concurrency-race tests for the conditional-write
/// primitives (C247: CAS / unique insert / sequence). Every fact runs the same scenario against
/// SQLite (embedded, always on), Mongo (<c>GAMEFRAMEX_TEST_MONGODB_CONNECTION_STRING</c>) and
/// PostgreSQL (<c>GAMEFRAMEX_TEST_POSTGRESQL_CONNECTION_STRING</c>) — the two real providers run
/// only when their environment variable is set and are silently skipped otherwise (the established
/// gating pattern). Race facts assert the C247 acceptance criteria: a CAS double-write has exactly
/// one winner, a concurrent unique insert lands exactly one row, and a sequence hands out distinct
/// non-regressing values.
/// </remarks>
[Collection(nameof(GameDbStaticStateCollection))]
public sealed class DatabaseConditionalWritePrimitivesTests
{
    /// <summary>
    /// 并发竞态用例的写者数量。
    /// </summary>
    private const int RacerCount = 8;

    /// <summary>
    /// 序列并发用例的取号数量。
    /// </summary>
    private const int SequenceClaimCount = 32;

    private static long _idSeed = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>
    /// 测试 CAS 版本推进：版本匹配写入成功并递增 RowVersion。
    /// </summary>
    [Fact]
    public async Task UpdateIfRowVersion_MatchedVersion_ShouldWriteAndBumpRowVersion()
    {
        await ForEachProviderAsync(async service =>
        {
            var state = new ConditionalWriteTestState { Id = NextId(), Identifier = "cas", Level = 1 };
            await service.AddAsync(state);

            var modified = new ConditionalWriteTestState { Id = state.Id, Identifier = "cas", Level = 2 };
            Assert.True(await service.UpdateIfRowVersionAsync(state.Id, 0L, modified));

            var loaded = await service.FindAsync<ConditionalWriteTestState>(state.Id, isCreateIfNotExists: false);
            Assert.NotNull(loaded);
            Assert.Equal(2, loaded.Level);
            Assert.Equal(1L, loaded.RowVersion);

            var second = new ConditionalWriteTestState { Id = state.Id, Identifier = "cas", Level = 3 };
            Assert.True(await service.UpdateIfRowVersionAsync(state.Id, 1L, second));

            var reloaded = await service.FindAsync<ConditionalWriteTestState>(state.Id, isCreateIfNotExists: false);
            Assert.NotNull(reloaded);
            Assert.Equal(3, reloaded.Level);
            Assert.Equal(2L, reloaded.RowVersion);
        });
    }

    /// <summary>
    /// 测试 CAS 失败可见性：版本不符或行缺失返回 false 且存量档不受影响。
    /// </summary>
    [Fact]
    public async Task UpdateIfRowVersion_StaleVersionOrMissingRow_ShouldReturnFalseAndKeepDocument()
    {
        await ForEachProviderAsync(async service =>
        {
            var state = new ConditionalWriteTestState { Id = NextId(), Identifier = "stale", Level = 10 };
            await service.AddAsync(state);

            var staleWriter = new ConditionalWriteTestState { Id = state.Id, Identifier = "stale", Level = 99 };
            Assert.False(await service.UpdateIfRowVersionAsync(state.Id, 42L, staleWriter));

            var missingWriter = new ConditionalWriteTestState { Id = NextId(), Identifier = "missing", Level = 1 };
            Assert.False(await service.UpdateIfRowVersionAsync(missingWriter.Id, 0L, missingWriter));

            var loaded = await service.FindAsync<ConditionalWriteTestState>(state.Id, isCreateIfNotExists: false);
            Assert.NotNull(loaded);
            Assert.Equal(10, loaded.Level);
            Assert.Equal(0L, loaded.RowVersion);
        });
    }

    /// <summary>
    /// 测试 CAS 失败不污染调用方对象：stale / 缺行失败路径回滚写前的临时版本递增，
    /// 调用方 state.RowVersion 保持调用前值（写前递增不回滚会使后续 CAS 持续 Stale）。
    /// </summary>
    [Fact]
    public async Task UpdateIfRowVersion_StaleFailure_ShouldNotPolluteCallerRowVersion()
    {
        await ForEachProviderAsync(async service =>
        {
            var state = new ConditionalWriteTestState { Id = NextId(), Identifier = "pollute", Level = 10 };
            await service.AddAsync(state);

            var staleWriter = new ConditionalWriteTestState { Id = state.Id, Identifier = "pollute", Level = 99, RowVersion = 7L };
            Assert.False(await service.UpdateIfRowVersionAsync(state.Id, 42L, staleWriter));
            Assert.Equal(7L, staleWriter.RowVersion);

            var missingWriter = new ConditionalWriteTestState { Id = NextId(), Identifier = "missing", Level = 1, RowVersion = 5L };
            Assert.False(await service.UpdateIfRowVersionAsync(missingWriter.Id, 0L, missingWriter));
            Assert.Equal(5L, missingWriter.RowVersion);
        });
    }

    /// <summary>
    /// 测试 CAS 参数守卫：state 的 Id 与 id 不一致抛 ArgumentException。
    /// </summary>
    [Fact]
    public async Task UpdateIfRowVersion_StateIdMismatch_ShouldThrowArgumentException()
    {
        await ForEachProviderAsync(async service =>
        {
            var state = new ConditionalWriteTestState { Id = NextId(), Identifier = "mismatch", Level = 1 };
            await Assert.ThrowsAsync<ArgumentException>(() => service.UpdateIfRowVersionAsync(state.Id + 1, 0L, state));
        });
    }

    /// <summary>
    /// 测试 CAS 审计字段口径：不触碰 UpdateTime/UpdateCount（按原样持久化）。
    /// </summary>
    [Fact]
    public async Task UpdateIfRowVersion_ShouldNotTouchAuditFields()
    {
        await ForEachProviderAsync(async service =>
        {
            var state = new ConditionalWriteTestState { Id = NextId(), Identifier = "audit", Level = 1 };
            await service.AddAsync(state);

            var modified = new ConditionalWriteTestState
            {
                Id = state.Id,
                Identifier = "audit",
                Level = 2,
                UpdateTime = 12345L,
                UpdateCount = 7,
            };
            Assert.True(await service.UpdateIfRowVersionAsync(state.Id, 0L, modified));

            var loaded = await service.FindAsync<ConditionalWriteTestState>(state.Id, isCreateIfNotExists: false);
            Assert.NotNull(loaded);
            Assert.Equal(12345L, loaded.UpdateTime);
            Assert.Equal(7, loaded.UpdateCount);
            Assert.Equal(1L, loaded.RowVersion);
        });
    }

    /// <summary>
    /// 测试 CAS 溢出守卫：long.MaxValue 匹配成功时 checked 递增抛 OverflowException 且不写库。
    /// </summary>
    [Fact]
    public async Task UpdateIfRowVersion_RowVersionAtMaxValue_ShouldOverflowWithoutWriting()
    {
        await ForEachProviderAsync(async service =>
        {
            var state = new ConditionalWriteTestState { Id = NextId(), Identifier = "overflow", Level = 5, RowVersion = long.MaxValue };
            await service.AddAsync(state);

            var writer = new ConditionalWriteTestState { Id = state.Id, Identifier = "overflow", Level = 6, RowVersion = long.MaxValue };
            await Assert.ThrowsAsync<OverflowException>(() => service.UpdateIfRowVersionAsync(state.Id, long.MaxValue, writer));

            var loaded = await service.FindAsync<ConditionalWriteTestState>(state.Id, isCreateIfNotExists: false);
            Assert.NotNull(loaded);
            Assert.Equal(5, loaded.Level);
            Assert.Equal(long.MaxValue, loaded.RowVersion);
        });
    }

    /// <summary>
    /// 测试 CAS 并发竞态（验收 #1）：同 id 同期望版本双写仅一个成功。
    /// </summary>
    [Fact]
    public async Task UpdateIfRowVersion_ConcurrentDoubleWrite_OnlyOneWinnerPerRound()
    {
        await ForEachProviderAsync(async service =>
        {
            var state = new ConditionalWriteTestState { Id = NextId(), Identifier = "seed", Level = 0 };
            await service.AddAsync(state);

            var firstRound = await RunRacersAsync(service, state.Id, expectedRowVersion: 0L);
            Assert.Equal(1, firstRound.Count(won => won));

            var afterFirst = await service.FindAsync<ConditionalWriteTestState>(state.Id, isCreateIfNotExists: false);
            Assert.NotNull(afterFirst);
            Assert.Equal(1L, afterFirst.RowVersion);
            Assert.StartsWith("racer-", afterFirst.Identifier, StringComparison.Ordinal);

            var secondRound = await RunRacersAsync(service, state.Id, expectedRowVersion: 1L);
            Assert.Equal(1, secondRound.Count(won => won));

            var afterSecond = await service.FindAsync<ConditionalWriteTestState>(state.Id, isCreateIfNotExists: false);
            Assert.NotNull(afterSecond);
            Assert.Equal(2L, afterSecond.RowVersion);
        });
    }

    /// <summary>
    /// 测试唯一插入顺序路径：新键插入成功并补齐 AddAsync 口径时间戳。
    /// </summary>
    [Fact]
    public async Task TryAddUnique_FreshKey_ShouldInsertWithAddTimestamps()
    {
        await ForEachProviderAsync(async service =>
        {
            var state = new ConditionalWriteUniqueTestState { Id = NextId(), Identifier = "alpha", Payload = "first" };
            Assert.True(await service.TryAddUniqueAsync(state, x => x.Identifier == "alpha"));

            var loaded = await service.FindAsync<ConditionalWriteUniqueTestState>(state.Id, isCreateIfNotExists: false);
            Assert.NotNull(loaded);
            Assert.Equal("first", loaded.Payload);
            Assert.True(loaded.CreatedTime > 0);
            Assert.Equal(loaded.CreatedTime, loaded.UpdateTime);
            Assert.Equal(0, loaded.UpdateCount);
        });
    }

    /// <summary>
    /// 测试唯一插入冲突返回：唯一键命中或 Id 已占用返回 false 不抛。
    /// </summary>
    [Fact]
    public async Task TryAddUnique_ExistingKeyOrOccupiedId_ShouldReturnFalse()
    {
        await ForEachProviderAsync(async service =>
        {
            var first = new ConditionalWriteUniqueTestState { Id = NextId(), Identifier = "alpha", Payload = "first" };
            Assert.True(await service.TryAddUniqueAsync(first, x => x.Identifier == "alpha"));

            var sameKey = new ConditionalWriteUniqueTestState { Id = NextId(), Identifier = "alpha", Payload = "second" };
            Assert.False(await service.TryAddUniqueAsync(sameKey, x => x.Identifier == "alpha"));

            var sameId = new ConditionalWriteUniqueTestState { Id = first.Id, Identifier = "beta", Payload = "third" };
            Assert.False(await service.TryAddUniqueAsync(sameId, x => x.Identifier == "beta"));

            Assert.Equal(1, await service.CountAsync<ConditionalWriteUniqueTestState>(x => x.Identifier == "alpha"));
        });
    }

    /// <summary>
    /// 测试唯一性口径无视软删：软删行同样占用唯一键（物理唯一索引口径）。
    /// </summary>
    [Fact]
    public async Task TryAddUnique_SoftDeletedRow_ShouldStillOccupyKey()
    {
        await ForEachProviderAsync(async service =>
        {
            var first = new ConditionalWriteUniqueTestState { Id = NextId(), Identifier = "gamma", Payload = "first" };
            Assert.True(await service.TryAddUniqueAsync(first, x => x.Identifier == "gamma"));
            Assert.Equal(1, await service.DeleteAsync(first));

            var revived = new ConditionalWriteUniqueTestState { Id = NextId(), Identifier = "gamma", Payload = "second" };
            Assert.False(await service.TryAddUniqueAsync(revived, x => x.Identifier == "gamma"));
        });
    }

    /// <summary>
    /// 测试无 provider 唯一索引时的顺序冲突兜底：先查后插同样返回 false。
    /// </summary>
    [Fact]
    public async Task TryAddUnique_WithoutProviderIndex_SequentialConflict_ShouldReturnFalse()
    {
        await ForEachProviderAsync(async service =>
        {
            var existing = new ConditionalWriteTestState { Id = NextId(), Identifier = "plain", Level = 1 };
            await service.AddAsync(existing);

            var candidate = new ConditionalWriteTestState { Id = NextId(), Identifier = "plain", Level = 2 };
            Assert.False(await service.TryAddUniqueAsync(candidate, x => x.Identifier == "plain"));
            Assert.Equal(1, await service.CountAsync<ConditionalWriteTestState>(x => x.Identifier == "plain"));
        });
    }

    /// <summary>
    /// 测试唯一插入并发竞态（验收 #2）：同唯一键并发插入仅一条入表。
    /// </summary>
    [Fact]
    public async Task TryAddUnique_ConcurrentSameKey_OnlyOneRowLands()
    {
        await ForEachProviderAsync(async service =>
        {
            // 预热：确保 provider 唯一索引先落盘（Mongo 的索引引导挂在类型化集合入口，PG 在建表单飞内）。
            // Warm-up: make sure the provider unique index exists first (Mongo bootstraps indexes on the
            // typed collection entry; PG creates them inside the table-creation single flight).
            var warmup = new ConditionalWriteUniqueTestState { Id = NextId(), Identifier = "warmup", Payload = "warmup" };
            await service.AddOrUpdateAsync(warmup);

            var tasks = Enumerable.Range(0, RacerCount).Select(i => Task.Run(async () =>
            {
                var racer = new ConditionalWriteUniqueTestState { Id = NextId(), Identifier = "race-key", Payload = $"racer-{i}" };
                return await service.TryAddUniqueAsync(racer, x => x.Identifier == "race-key");
            }));
            var results = await Task.WhenAll(tasks);

            Assert.Equal(1, results.Count(won => won));
            Assert.Equal(1, await service.CountAsync<ConditionalWriteUniqueTestState>(x => x.Identifier == "race-key"));
        });
    }

    /// <summary>
    /// 测试序列基本语义：首次为 1、单调递增、不同序列名互相独立。
    /// </summary>
    [Fact]
    public async Task NextSequence_ShouldStartAtOneAndIncrementPerName()
    {
        await ForEachProviderAsync(async service =>
        {
            Assert.Equal(1L, await service.NextSequenceAsync<ConditionalWriteTestState>("account"));
            Assert.Equal(2L, await service.NextSequenceAsync<ConditionalWriteTestState>("account"));
            Assert.Equal(3L, await service.NextSequenceAsync<ConditionalWriteTestState>("account"));
            Assert.Equal(1L, await service.NextSequenceAsync<ConditionalWriteTestState>("player"));
        });
    }

    /// <summary>
    /// 测试序列键控：序列名与状态类型名联合键控，同名不同类型互不干扰。
    /// </summary>
    [Fact]
    public async Task NextSequence_IsKeyedByStateType()
    {
        await ForEachProviderAsync(async service =>
        {
            Assert.Equal(1L, await service.NextSequenceAsync<ConditionalWriteTestState>("shared"));
            Assert.Equal(1L, await service.NextSequenceAsync<ConditionalWriteUniqueTestState>("shared"));
            Assert.Equal(2L, await service.NextSequenceAsync<ConditionalWriteTestState>("shared"));
            Assert.Equal(2L, await service.NextSequenceAsync<ConditionalWriteUniqueTestState>("shared"));
        });
    }

    /// <summary>
    /// 测试序列名守卫：null 或空白序列名抛 ArgumentException。
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task NextSequence_BlankName_ShouldThrowArgumentException(string sequenceName)
    {
        await ForEachProviderAsync(async service =>
        {
            await Assert.ThrowsAsync<ArgumentException>(() => service.NextSequenceAsync<ConditionalWriteTestState>(sequenceName));
        });
    }

    /// <summary>
    /// 测试序列并发竞态（验收 #3）：并发取号无重复、无回退（结果恰为 1..N）。
    /// </summary>
    [Fact]
    public async Task NextSequence_ConcurrentClaims_ShouldYieldDistinctMonotonicValues()
    {
        await ForEachProviderAsync(async service =>
        {
            var tasks = Enumerable.Range(0, SequenceClaimCount).Select(_ => Task.Run(() => service.NextSequenceAsync<ConditionalWriteTestState>("race")));
            var values = await Task.WhenAll(tasks);

            var sorted = values.OrderBy(v => v).ToArray();
            for (var i = 0; i < sorted.Length; i++)
            {
                Assert.Equal(i + 1L, sorted[i]);
            }
        });
    }

    /// <summary>
    /// 测试 GameDb 门面转发条件写三原语（转发语义冒烟，SQLite 载体）。
    /// </summary>
    [Fact]
    public async Task GameDb_Facade_ShouldForwardConditionalWritePrimitives()
    {
        var service = new SqliteDbService();
        var opened = await GameDb.Init<SqliteDbService>(new DbOptions
        {
            ConnectionString = "Data Source=:memory:",
            Name = "c247-facade-smoke",
        });
        Assert.True(opened);

        try
        {
            var state = new ConditionalWriteTestState { Id = NextId(), Identifier = "facade", Level = 1 };
            await GameDb.SaveOneAsync(state);
            Assert.True(await GameDb.UpdateIfRowVersionAsync(state.Id, 0L, new ConditionalWriteTestState { Id = state.Id, Identifier = "facade", Level = 2 }));
            Assert.True(await GameDb.TryAddUniqueAsync(new ConditionalWriteUniqueTestState { Id = NextId(), Identifier = "facade-unique" }, x => x.Identifier == "facade-unique"));
            Assert.Equal(1L, await GameDb.NextSequenceAsync<ConditionalWriteTestState>("facade-seq"));
        }
        finally
        {
            await GameDb.CloseAsync();
            GameDb.ResetForTesting();
        }
    }

    /// <summary>
    /// 同 id 同期望版本的并发 CAS 竞态辅助：返回各写者胜负。
    /// </summary>
    private static async Task<bool[]> RunRacersAsync(IDatabaseService service, long id, long expectedRowVersion)
    {
        var tasks = Enumerable.Range(0, RacerCount).Select(i => Task.Run(async () =>
        {
            var racer = new ConditionalWriteTestState { Id = id, Identifier = $"racer-{i}", Level = i };
            return await service.UpdateIfRowVersionAsync(id, expectedRowVersion, racer);
        }));
        return await Task.WhenAll(tasks);
    }

    /// <summary>
    /// 生成唯一测试 Id。
    /// </summary>
    private static long NextId()
    {
        return Interlocked.Increment(ref _idSeed);
    }

    /// <summary>
    /// 跨 provider 矩阵驱动：同一用例依次在 SQLite / Mongo / PostgreSQL 上执行。
    /// </summary>
    private static async Task ForEachProviderAsync(Func<IDatabaseService, Task> scenario)
    {
        await ExecuteWithSqliteAsync(scenario);
        await ExecuteWithMongoAsync(scenario);
        await ExecuteWithPostgreSqlAsync(scenario);
    }

    /// <summary>
    /// SQLite harness：每次执行创建独立内存库 service（嵌入式，无环境变量门控）。
    /// </summary>
    private static async Task ExecuteWithSqliteAsync(Func<IDatabaseService, Task> action)
    {
        var service = new SqliteDbService();
        var opened = await service.Open(new DbOptions
        {
            ConnectionString = "Data Source=:memory:",
            Name = "c247-conditional-write-tests",
        });
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
    /// Mongo harness：环境变量门控 + 独立库创建/销毁。
    /// </summary>
    private static async Task ExecuteWithMongoAsync(Func<IDatabaseService, Task> action)
    {
        var connectionString = Environment.GetEnvironmentVariable("GAMEFRAMEX_TEST_MONGODB_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var dbName = $"gameframex_test_{Guid.NewGuid():N}";
        var service = new MongoDbService();
        var options = new DbOptions
        {
            ConnectionString = connectionString,
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
                var client = new MongoClient(connectionString);
                await client.DropDatabaseAsync(dbName);
            }
            catch
            {
            }
        }
    }

    /// <summary>
    /// PostgreSQL harness：环境变量门控 + 独立库创建/销毁。
    /// </summary>
    private static async Task ExecuteWithPostgreSqlAsync(Func<IDatabaseService, Task> action)
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
    /// 条件写契约测试状态实体（无 provider 唯一索引声明——覆盖先查后插兜底路径）。
    /// </summary>
    private sealed class ConditionalWriteTestState : BaseCacheState
    {
        /// <summary>
        /// 唯一标识字段。
        /// </summary>
        public string Identifier { get; set; }

        /// <summary>
        /// 等级字段。
        /// </summary>
        public int Level { get; set; }
    }

    /// <summary>
    /// 唯一插入契约测试状态实体（<c>Identifier</c> 声明 provider 唯一索引——覆盖竞态安全路径）。
    /// </summary>
    private sealed class ConditionalWriteUniqueTestState : BaseCacheState
    {
        /// <summary>
        /// 唯一标识字段（provider 唯一索引，三 provider 一致声明）。
        /// </summary>
        [EntityIndex("ux_conditional_write_identifier", Unique = true)]
        public string Identifier { get; set; }

        /// <summary>
        /// 载荷字段。
        /// </summary>
        public string Payload { get; set; }
    }
}
