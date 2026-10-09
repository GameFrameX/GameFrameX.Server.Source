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
//   or violate the legal rights and interests of others as prohibited by laws and regulations!
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


using System.Text;
using GameFrameX.DataBase;
using GameFrameX.DataBase.Abstractions;
using GameFrameX.DataBase.PostgreSql;
using GameFrameX.Foundation.Orm.Attribute;
using Npgsql;
using Xunit;

namespace GameFrameX.Tests.DataBase;

/// <summary>
/// C168 兼容性 / 索引引导集成测试：C166 存量 doc 经 EF 读取回归、表达式索引 cast 对齐 EXPLAIN 命中、同名异构索引滚动重建。
/// </summary>
/// <remarks>
/// Compatibility and index-bootstrap tests for the EF-based adapter (C168). Gated by
/// <c>GAMEFRAMEX_TEST_POSTGRESQL_CONNECTION_STRING</c> (tests silently skip without a reachable database,
/// aligned with the other PostgreSQL suites). Each test creates an isolated database, dropped on dispose.
/// </remarks>
[Collection(nameof(GameDbStaticStateCollection))]
public sealed class PostgreSqlDbServiceCompatibilityTests
{
    private static long _idSeed = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>
    /// 测试 C166 序列化器写入的存量 jsonb doc 经 EF owned JSON 读取字段一致（P0 回归门禁）。
    /// </summary>
    /// <remarks>
    /// Verifies that a C166-era stored document (PascalCase keys, explicit JSON nulls, enum-as-number)
    /// reads back byte-equivalently through the EF owned-JSON pipeline.
    /// </remarks>
    [Fact]
    public async Task LegacyC166Document_ShouldReadBackFieldEquivalent()
    {
        await ExecuteWithServiceAsync(async (service, connectionString) =>
        {
            var stateId = Interlocked.Increment(ref _idSeed);
            // 先经适配器建表（EnsureTable 单飞），再写入 C166 形态的存量文档
            Assert.Equal(0, await service.CountAsync<LegacyProbeState>(x => false));
            // C166 序列化器形态：PascalCase 键、null 显式写出、枚举数值、属性乱序、冗余键（旧类型已删字段）。
            var legacyDoc = $$"""
                {"Id":{{stateId}},"CreatedId":null,"CreatedName":null,"CreatedTime":100,"DeletedId":null,"DeletedName":null,"DeleteTime":null,"IsDeleted":null,"IsEnabled":null,"RowVersion":0,"UpdateCount":null,"UpdatedId":null,"UpdatedName":null,"UpdateTime":null,"LegacyOnly":"stale","Name":"legacy-name","Level":3,"Kind":2,"Items":[{"Sub":"x"}]}
                """;

            await using (var connection = new NpgsqlConnection(connectionString))
            {
                await connection.OpenAsync();
                await new NpgsqlCommand($"INSERT INTO \"{nameof(LegacyProbeState)}\" (id, doc) VALUES ({stateId}, '{legacyDoc.Replace("'", "''")}'::jsonb)", connection).ExecuteNonQueryAsync();
            }

            // IsDeleted 为 null 的可见性（对齐 Mongo 语义）+ 字段一致（多余键被忽略、冗余键被忽略）
            var loaded = await service.FindAsync<LegacyProbeState>(stateId, isCreateIfNotExists: false);
            Assert.NotNull(loaded);
            Assert.Equal("legacy-name", loaded.Name);
            Assert.Equal(3, loaded.Level);
            Assert.Equal(ProbeKind.Silver, loaded.Kind);
            Assert.Single(loaded.Items);
            Assert.Equal("x", loaded.Items[0].Sub);
            Assert.Equal(100, loaded.CreatedTime);
            Assert.Null(loaded.IsDeleted);

            // 读取后可走正常软删（旧档 IsDeleted=null 与新档 false 均可被置 true）
            await service.DeleteAsync(loaded);
            Assert.Equal(0, await service.CountAsync<LegacyProbeState>(x => x.Name == "legacy-name"));
            Assert.Equal(1, await service.CountAsync<LegacyProbeState>(x => x.Name == "legacy-name", includeDeleted: true));
        });
    }

    /// <summary>
    /// 测试 jsonb 表达式索引与 EF 翻译谓词逐字对齐（EXPLAIN 命中 + pg_indexes 定义核对）。
    /// </summary>
    /// <remarks>
    /// C166 P1-8 equivalent for the EF pipeline: the adapter's index DDL must match the EF-translated predicate
    /// shape (<c>int → ::integer</c>) so the planner picks the index.
    /// </remarks>
    [Fact]
    public async Task Index_ShouldMatchEfPredicateShape_AndHitInExplain()
    {
        await ExecuteWithServiceAsync(async (service, connectionString) =>
        {
            var states = Enumerable.Range(0, 2000).Select(i => new IndexProbeState { Id = Interlocked.Increment(ref _idSeed), Score = i, Name = $"p{i}", }).ToArray();
            await service.AddListAsync(states);

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();

            // 1. pg_indexes 定义核对：索引 cast 为 ::integer（EF 实测形状）
            var indexDefinition = await ReadIndexDefinitionAsync(connection, nameof(IndexProbeState), "ix_probe_score");
            Assert.Contains("::integer", indexDefinition, StringComparison.OrdinalIgnoreCase);

            // 2. EXPLAIN：EF 形态谓词（CAST(... AS integer)）走该表达式索引
            await using var explain = new NpgsqlCommand($"EXPLAIN SELECT doc FROM \"{nameof(IndexProbeState)}\" WHERE CAST(doc ->> 'Score' AS integer) = @v", connection);
            explain.Parameters.AddWithValue("v", 1000);
            var planBuilder = new StringBuilder();
            await using (var reader = await explain.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    planBuilder.Append(reader.GetString(0)).Append('\n');
                }
            }

            var plan = planBuilder.ToString();
            Assert.Contains("Index Scan", plan, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ix_probe_score", plan, StringComparison.OrdinalIgnoreCase);
        });
    }

    /// <summary>
    /// 测试同名异构索引（C166 int→bigint cast）被滚动重建为 EF 对齐形态。
    /// </summary>
    /// <remarks>
    /// C168's rolling path off C166 indexes: a same-name index with the legacy <c>::bigint</c> cast must be
    /// dropped and recreated with the EF-aligned <c>::integer</c> cast on first schema sync.
    /// </remarks>
    [Fact]
    public async Task DriftedLegacyIndex_ShouldBeRebuiltToEfAlignedShape()
    {
        await ExecuteWithServiceAsync(async (service, connectionString) =>
        {
            // 手工预置 C166 形态的同名索引（int 字段按旧矩阵 cast bigint），先于适配器首次 schema 同步
            await using (var setupConnection = new NpgsqlConnection(connectionString))
            {
                await setupConnection.OpenAsync();
                await new NpgsqlCommand($"CREATE TABLE IF NOT EXISTS \"{nameof(RebuildProbeState)}\" (id bigint PRIMARY KEY, doc jsonb NOT NULL)", setupConnection).ExecuteNonQueryAsync();
                await new NpgsqlCommand($"CREATE INDEX ix_probe_score ON \"{nameof(RebuildProbeState)}\" (((doc ->> 'Score')::bigint))", setupConnection).ExecuteNonQueryAsync();
            }

            // 触发适配器首次 schema 同步（单飞）：旧 bigint 索引应被检测为异构并重建为 ::integer
            var state = new RebuildProbeState { Id = Interlocked.Increment(ref _idSeed), Score = 7, Name = "rebuild", };
            await service.AddAsync(state);

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            var indexDefinition = await ReadIndexDefinitionAsync(connection, nameof(RebuildProbeState), "ix_probe_score");
            Assert.Contains("::integer", indexDefinition, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("::bigint", indexDefinition, StringComparison.OrdinalIgnoreCase);

            // 重建后功能不回退：读取 + 谓词查询仍正常
            var loaded = await service.FindAsync<RebuildProbeState>(state.Id, isCreateIfNotExists: false);
            Assert.Equal(7, loaded.Score);
        });
    }

    /// <summary>
    /// 测试 harness：门控 + 独立库创建/销毁（每次调用一个独立库）。
    /// </summary>
    private static async Task ExecuteWithServiceAsync(Func<PostgreSqlDbService, string, Task> action)
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

        var testConnectionString = WithDatabase(connectionString, dbName);
        var service = new PostgreSqlDbService();
        var options = new DbOptions
        {
            Type = "PostgreSql",
            ConnectionString = testConnectionString,
            Name = dbName,
        };

        var opened = await service.Open(options);
        Assert.True(opened);

        try
        {
            await action(service, testConnectionString);
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
    /// 读取指定索引的定义文本（pg_indexes）。
    /// </summary>
    private static async Task<string> ReadIndexDefinitionAsync(NpgsqlConnection connection, string tableName, string indexName)
    {
        await using var command = new NpgsqlCommand("SELECT indexdef FROM pg_indexes WHERE tablename = @tableName AND indexname = @indexName", connection);
        command.Parameters.AddWithValue("tableName", tableName);
        command.Parameters.AddWithValue("indexName", indexName);
        var definition = await command.ExecuteScalarAsync();
        Assert.NotNull(definition);
        return (string)definition;
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
    /// 存量兼容探针状态（含枚举 / 集合 / 字符串成员，对应 C166 序列化器写过的形态）。
    /// </summary>
    private sealed class LegacyProbeState : BaseCacheState
    {
        /// <summary>
        /// 名称。
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 等级。
        /// </summary>
        public int Level { get; set; }

        /// <summary>
        /// 枚举。
        /// </summary>
        public ProbeKind Kind { get; set; }

        /// <summary>
        /// 集合。
        /// </summary>
        public List<LegacyItem> Items { get; set; }

        /// <inheritdoc />
        public override byte[] ToBytes()
        {
            var data = $"{Id}|{Name}|{Level}";
            return Encoding.UTF8.GetBytes(data);
        }
    }

    /// <summary>
    /// 存量集合项。
    /// </summary>
    private sealed class LegacyItem
    {
        /// <summary>
        /// 子字段。
        /// </summary>
        public string Sub { get; set; }
    }

    /// <summary>
    /// 探针枚举。
    /// </summary>
    private enum ProbeKind
    {
        None = 0,
        Silver = 2,
        Gold = 3,
    }

    /// <summary>
    /// 表达式索引命中探针状态。
    /// </summary>
    private sealed class IndexProbeState : BaseCacheState
    {
        /// <summary>
        /// 带索引的分数字段。
        /// </summary>
        [EntityIndex("ix_probe_score")]
        public int Score { get; set; }

        /// <summary>
        /// 名称。
        /// </summary>
        public string Name { get; set; }

        /// <inheritdoc />
        public override byte[] ToBytes()
        {
            var data = $"{Id}|{Score}";
            return Encoding.UTF8.GetBytes(data);
        }
    }

    /// <summary>
    /// 索引重建探针状态。
    /// </summary>
    private sealed class RebuildProbeState : BaseCacheState
    {
        /// <summary>
        /// 带索引的分数字段。
        /// </summary>
        [EntityIndex("ix_probe_score")]
        public int Score { get; set; }

        /// <summary>
        /// 名称。
        /// </summary>
        public string Name { get; set; }

        /// <inheritdoc />
        public override byte[] ToBytes()
        {
            var data = $"{Id}|{Score}";
            return Encoding.UTF8.GetBytes(data);
        }
    }
}
