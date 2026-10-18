// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and other international regulations.
//   使用本项目须严格遵守相关法律法规及开源许可证之规定。
//   Usage of this project must comply strictly with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please refer to the LICENSE file at the root of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   shall be borne by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool
//   CNB Repository:  https://cnb.cool
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.DataBase;
using GameFrameX.DataBase.Abstractions;
using GameFrameX.DataBase.Mongo;
using GameFrameX.DataBase.PostgreSql;
using GameFrameX.DataBase.Sqlite;
using MongoDB.Bson;
using MongoDB.Driver;
using Npgsql;
using Xunit;

namespace GameFrameX.Tests.DataBase;

/// <summary>
/// IDatabaseService.ExecuteInTransactionAsync 事务原子性（C262）跨 provider 测试。
/// </summary>
/// <remarks>
/// Cross-provider transaction-atomicity tests for <c>ExecuteInTransactionAsync</c> (C262): every
/// write path issued inside the shell must join the shell's transaction — commits persist all of
/// them, action failures roll all of them back, and nested shells join the ambient transaction.
/// Every fact runs the same scenario against SQLite (embedded, always on), Mongo
/// (<c>GAMEFRAMEX_TEST_MONGODB_CONNECTION_STRING</c>) and PostgreSQL
/// (<c>GAMEFRAMEX_TEST_POSTGRESQL_CONNECTION_STRING</c>) — the two real providers run only when
/// their environment variable is set and are silently skipped otherwise (the established gating
/// pattern). Rollback-dependent facts additionally probe whether the Mongo server is a replica-set
/// member (standalone <c>mongod</c> cannot run multi-document transactions; the shell degrades to a
/// single non-transactional pass, so rollback assertions are skipped and the fallback's
/// exactly-once execution is asserted instead). Assertions follow the data-identity lesson from
/// lesson/test-port-assertion-identity-mismatch: each row is asserted by its own id/polarity.
/// </remarks>
[Collection(nameof(GameDbStaticStateCollection))]
public sealed class DatabaseExecuteInTransactionAtomicityTests
{
    private static long _idSeed = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>
    /// 提交路径：事务壳内混合全部写路径（单插/批插/upsert 单+批/唯一插入/CAS/序列）提交后全部持久化。
    /// </summary>
    [Fact]
    public async Task ExecuteInTransaction_Commit_ShouldPersistAllWritePaths()
    {
        await ForEachProviderAsync(async (service, _, _) =>
        {
            var single = CreateState("tx-commit-single", group: 41, score: 1);
            var listOne = CreateState("tx-commit-list-1", group: 42, score: 1);
            var listTwo = CreateState("tx-commit-list-2", group: 42, score: 2);
            var upsert = CreateState("tx-commit-upsert", group: 43, score: 1);
            var bulkOne = CreateState("tx-commit-bulk-1", group: 44, score: 1);
            var bulkTwo = CreateState("tx-commit-bulk-2", group: 44, score: 2);
            var unique = new TransactionUniqueTestState { Id = NextId(), Identifier = "tx-commit-unique" };
            var cas = CreateState("tx-commit-cas", group: 45, score: 1);

            var sequence = 0L;
            await service.ExecuteInTransactionAsync(async () =>
            {
                await service.AddAsync(single);
                await service.AddListAsync(new[] { listOne, listTwo, });
                await service.AddOrUpdateAsync(upsert);
                upsert.Score = 7;
                await service.AddOrUpdateAsync(upsert);
                await service.AddOrUpdateListAsync(new[] { bulkOne, bulkTwo, });
                Assert.True(await service.TryAddUniqueAsync(unique, x => x.Identifier == "tx-commit-unique"));
                await service.AddAsync(cas);
                Assert.True(await service.UpdateIfRowVersionAsync(cas.Id, 0L, new TransactionAtomicityTestState { Id = cas.Id, Name = "tx-commit-cas", Group = 45, Score = 9, }));
                sequence = await service.NextSequenceAsync<TransactionAtomicityTestState>("tx-commit-seq");
            });

            Assert.NotNull(await service.FindAsync<TransactionAtomicityTestState>(single.Id, isCreateIfNotExists: false));
            Assert.Equal(2, await service.CountAsync<TransactionAtomicityTestState>(x => x.Group == 42));
            var loadedUpsert = await service.FindAsync<TransactionAtomicityTestState>(upsert.Id, isCreateIfNotExists: false);
            Assert.NotNull(loadedUpsert);
            Assert.Equal(7, loadedUpsert.Score);
            Assert.Equal(2, await service.CountAsync<TransactionAtomicityTestState>(x => x.Group == 44));
            Assert.NotNull(await service.FindAsync<TransactionUniqueTestState>(unique.Id, isCreateIfNotExists: false));
            var loadedCas = await service.FindAsync<TransactionAtomicityTestState>(cas.Id, isCreateIfNotExists: false);
            Assert.NotNull(loadedCas);
            Assert.Equal(9, loadedCas.Score);
            Assert.Equal(1L, loadedCas.RowVersion);
            Assert.Equal(1L, sequence);
            Assert.Equal(2L, await service.NextSequenceAsync<TransactionAtomicityTestState>("tx-commit-seq"));
        });
    }

    /// <summary>
    /// 回滚路径：action 抛异常时事务内全部写路径整体回滚（含序列不推进、删除撤销）。
    /// </summary>
    [Fact]
    public async Task ExecuteInTransaction_Rollback_WhenActionThrows_ShouldDiscardAllWrites()
    {
        await ForEachProviderAsync(async (service, kind, supportsTransactions) =>
        {
            if (!supportsTransactions)
            {
                // standalone mongod：无多文档事务能力，回滚断言不适用（由 fallback 用例覆盖）。
                return;
            }

            var persistent = CreateState("tx-rollback-keep", group: 51, score: 1);
            await service.AddAsync(persistent);
            var toDelete = CreateState("tx-rollback-delete", group: 51, score: 2);
            await service.AddAsync(toDelete);
            var rolledSingle = CreateState("tx-rollback-single", group: 52, score: 1);
            var rolledList = CreateState("tx-rollback-list", group: 52, score: 2);
            var rolledUpsert = CreateState("tx-rollback-upsert", group: 52, score: 3);
            var sequenceBefore = await service.NextSequenceAsync<TransactionAtomicityTestState>("tx-rollback-seq");

            await Assert.ThrowsAsync(kind == ProviderKind.Sqlite ? typeof(InvalidOperationException) : typeof(DatabaseUnavailableException), () => service.ExecuteInTransactionAsync(async () =>
            {
                await service.AddAsync(rolledSingle);
                await service.AddListAsync(new[] { rolledList, });
                await service.AddOrUpdateAsync(rolledUpsert);
                await service.HardDeleteAsync<TransactionAtomicityTestState>(x => x.Id == toDelete.Id);
                await service.NextSequenceAsync<TransactionAtomicityTestState>("tx-rollback-seq");
                throw new InvalidOperationException("tx-rollback-boom");
            }));

            Assert.Null(await service.FindAsync<TransactionAtomicityTestState>(rolledSingle.Id, isCreateIfNotExists: false));
            Assert.Null(await service.FindAsync<TransactionAtomicityTestState>(rolledList.Id, isCreateIfNotExists: false));
            Assert.Null(await service.FindAsync<TransactionAtomicityTestState>(rolledUpsert.Id, isCreateIfNotExists: false));
            Assert.Equal(2, await service.CountAsync<TransactionAtomicityTestState>(x => x.Group == 51));
            Assert.NotNull(await service.FindAsync<TransactionAtomicityTestState>(toDelete.Id, isCreateIfNotExists: false));
            Assert.Equal(sequenceBefore + 1, await service.NextSequenceAsync<TransactionAtomicityTestState>("tx-rollback-seq"));
        });
    }

    /// <summary>
    /// 嵌套壳：内层 ExecuteInTransactionAsync 加入环境事务，外层回滚连带内层写入。
    /// </summary>
    [Fact]
    public async Task ExecuteInTransaction_NestedShell_ShouldJoinAmbientTransaction()
    {
        await ForEachProviderAsync(async (service, kind, supportsTransactions) =>
        {
            if (!supportsTransactions)
            {
                return;
            }

            var outer = CreateState("tx-nested-outer", group: 61, score: 1);
            var inner = CreateState("tx-nested-inner", group: 61, score: 2);
            var committed = CreateState("tx-nested-committed", group: 62, score: 1);
            await service.AddAsync(committed);

            await service.ExecuteInTransactionAsync(async () =>
            {
                await service.AddAsync(outer);
                await service.ExecuteInTransactionAsync(() => service.AddAsync(inner));
            });
            Assert.Equal(2, await service.CountAsync<TransactionAtomicityTestState>(x => x.Group == 61));

            var rolledOuter = CreateState("tx-nested-rolled-outer", group: 63, score: 1);
            var rolledInner = CreateState("tx-nested-rolled-inner", group: 63, score: 2);
            await Assert.ThrowsAsync(ExpectedTransactionFailureException(kind), () => service.ExecuteInTransactionAsync(async () =>
            {
                await service.AddAsync(rolledOuter);
                await service.ExecuteInTransactionAsync(() => service.AddAsync(rolledInner));
                throw new InvalidOperationException("tx-nested-boom");
            }));
            Assert.Equal(0, await service.CountAsync<TransactionAtomicityTestState>(x => x.Group == 63));

            Assert.NotNull(await service.FindAsync<TransactionAtomicityTestState>(committed.Id, isCreateIfNotExists: false));
        });
    }

    /// <summary>
    /// Mongo standalone 回退：非副本集上事务壳降级为单次直通执行（action 恰执行一次、无双写）。
    /// </summary>
    [Fact]
    public async Task ExecuteInTransaction_MongoStandalone_FallbackShouldRunActionExactlyOnce()
    {
        await ForEachProviderAsync(async (service, kind, supportsTransactions) =>
        {
            if (kind != ProviderKind.Mongo || supportsTransactions)
            {
                return;
            }

            var state = CreateState("tx-standalone", group: 71, score: 1);
            var executions = 0;
            await service.ExecuteInTransactionAsync(async () =>
            {
                executions++;
                await service.AddAsync(state);
            });

            Assert.Equal(1, executions);
            Assert.NotNull(await service.FindAsync<TransactionAtomicityTestState>(state.Id, isCreateIfNotExists: false));
        });
    }


    /// <summary>
    /// 事务壳失败异常按 provider 契约断言：Sqlite 裸抛原始异常，Mongo、PG 包 DatabaseUnavailableException（既有契约，C262 冻结）。
    /// </summary>
    private static Type ExpectedTransactionFailureException(ProviderKind kind)
    {
        return kind == ProviderKind.Sqlite ? typeof(InvalidOperationException) : typeof(DatabaseUnavailableException);
    }
    /// <summary>
    /// 生成唯一测试 Id。
    /// </summary>
    private static long NextId()
    {
        return Interlocked.Increment(ref _idSeed);
    }

    private static TransactionAtomicityTestState CreateState(string name, int group, int score)
    {
        return new TransactionAtomicityTestState { Id = NextId(), Name = name, Group = group, Score = score, };
    }

    /// <summary>
    /// 跨 provider 矩阵驱动：同一用例依次在 SQLite / Mongo / PostgreSQL 上执行。
    /// </summary>
    private static async Task ForEachProviderAsync(Func<IDatabaseService, ProviderKind, bool, Task> scenario)
    {
        await ExecuteWithSqliteAsync(scenario);
        await ExecuteWithMongoAsync(scenario);
        await ExecuteWithPostgreSqlAsync(scenario);
    }

    /// <summary>
    /// SQLite harness：每次执行创建独立内存库 service（嵌入式，无环境变量门控）。
    /// </summary>
    private static async Task ExecuteWithSqliteAsync(Func<IDatabaseService, ProviderKind, bool, Task> action)
    {
        var service = new SqliteDbService();
        var opened = await service.Open(new DbOptions
        {
            ConnectionString = "Data Source=:memory:",
            Name = "c262-transaction-atomicity-tests",
        });
        Assert.True(opened);

        try
        {
            await action(service, ProviderKind.Sqlite, true);
        }
        finally
        {
            await service.Close();
        }
    }

    /// <summary>
    /// Mongo harness：环境变量门控 + 独立库创建/销毁 + 副本集事务能力探测（hello.setName）。
    /// </summary>
    private static async Task ExecuteWithMongoAsync(Func<IDatabaseService, ProviderKind, bool, Task> action)
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
            var hello = await service.CurrentDatabase.RunCommandAsync(new JsonCommand<BsonDocument>("{ hello: 1 }"));
            var supportsTransactions = hello.Contains("setName");
            await action(service, ProviderKind.Mongo, supportsTransactions);
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
    private static async Task ExecuteWithPostgreSqlAsync(Func<IDatabaseService, ProviderKind, bool, Task> action)
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
            await action(service, ProviderKind.PostgreSql, true);
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
    /// provider 种类标记（回滚异常契约按 provider 断言：Sqlite 裸抛 / Mongo、PG 包 DatabaseUnavailableException）。
    /// </summary>
    private enum ProviderKind
    {
        /// <summary>SQLite（嵌入式）。</summary>
        Sqlite,

        /// <summary>MongoDB。</summary>
        Mongo,

        /// <summary>PostgreSQL。</summary>
        PostgreSql,
    }

    /// <summary>
    /// 事务原子性测试状态实体。
    /// </summary>
    private sealed class TransactionAtomicityTestState : BaseCacheState
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
    }

    /// <summary>
    /// 唯一插入路径测试状态实体。
    /// </summary>
    private sealed class TransactionUniqueTestState : BaseCacheState
    {
        /// <summary>
        /// 唯一标识字段。
        /// </summary>
        public string Identifier { get; set; }
    }
}
