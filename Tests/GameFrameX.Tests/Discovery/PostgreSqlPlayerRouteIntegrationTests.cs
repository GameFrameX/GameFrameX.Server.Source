// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规及开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes and liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using GameFrameX.Discovery.Routing;
using GameFrameX.DataBase.PostgreSql.Discovery;
using GameFrameX.DataBase.PostgreSql.Routing;
using Npgsql;
using System.Reflection;

namespace GameFrameX.Tests.Discovery;

/// <summary>
/// PostgreSqlPlayerRouteSyncTarget / PostgreSqlPlayerRouteResolver 的 PostgreSQL 集成测试。
/// </summary>
/// <remarks>
/// PostgreSQL-backed integration tests for the player-route layer,
/// ported test-by-test from <c>MongoPlayerRouteIntegrationTests</c> with identical
/// names and assertions. Gated by GAMEFRAMEX_TEST_POSTGRESQL_CONNECTION_STRING:
/// without the variable the tests skip so plain <c>dotnet test</c> stays green on
/// PostgreSQL-less machines. Each test class creates an isolated database (Guid
/// suffix, dropped on Dispose with FORCE).
/// </remarks>
public sealed class PostgreSqlPlayerRouteIntegrationTests : IDisposable
{
    /// <summary>
    /// PostgreSQL 连接串（未设置时跳过）。
    /// </summary>
    /// <remarks>
    /// The PostgreSQL connection string (tests skip when unset).
    /// </remarks>
    private readonly string _connectionString = Environment.GetEnvironmentVariable("GAMEFRAMEX_TEST_POSTGRESQL_CONNECTION_STRING") ?? string.Empty;

    /// <summary>
    /// 本测试类的独立测试库句柄。
    /// </summary>
    /// <remarks>
    /// The isolated test-database handle of this test class.
    /// </remarks>
    private PostgreSqlTestDatabase _testDatabase;

    /// <summary>
    /// 是否跳过全部用例。
    /// </summary>
    /// <remarks>
    /// Whether all tests are skipped.
    /// </remarks>
    private bool ShouldSkip
    {
        get
        {
            return string.IsNullOrWhiteSpace(_connectionString);
        }
    }

    /// <summary>
    /// 创建独立测试库（含幂等建表 / 建索引）。
    /// </summary>
    /// <remarks>
    /// Creates the isolated test database (with the idempotent player_route schema bootstrap).
    /// </remarks>
    private async Task<PostgreSqlTestDatabase> CreateDatabaseAsync()
    {
        if (_testDatabase == null)
        {
            _testDatabase = await PostgreSqlTestDatabase.CreateAsync(_connectionString);
            await new PostgreSqlPlayerRouteStore(_testDatabase.DataSource).EnsureSchemaAsync();
        }

        return _testDatabase;
    }

    [Fact]
    public async Task SyncTarget_FirstTimeUpsert_InsertsDocument()
    {
        if (ShouldSkip)
        {
            return;
        }

        var testDatabase = await CreateDatabaseAsync();
        var target = new PlayerRouteSyncTarget(new PostgreSqlPlayerRouteStore(testDatabase.DataSource));

        await target.UpsertAsync(new PlayerRouteRecord { PlayerId = 101, InstanceId = "game-1", Role = "Game", Version = 1, });

        var stored = await ReadRouteAsync(testDatabase.DataSource, 101);
        Assert.NotNull(stored);
        Assert.Equal("game-1", stored.Value.InstanceId);
        Assert.Equal("Game", stored.Value.Role);
        Assert.Equal(1, stored.Value.Version);
    }

    [Fact]
    public async Task SyncTarget_FirstLoginConflict_SecondInsertOverwrites()
    {
        if (ShouldSkip)
        {
            return;
        }

        var testDatabase = await CreateDatabaseAsync();
        var store = new PostgreSqlPlayerRouteStore(testDatabase.DataSource);

        // 行缺失时的两次首登插入（不同实例，version=1，顺序执行）：第二次无条件覆盖后写者胜出，
        // 与 Mongo ReplaceOneAsync(IsUpsert) 的 last-writer-wins 对齐（EF 化后经公开 API 直接覆盖）。
        // 说明：顺序调用 UpsertAsync 时第二次会走 CAS 读回分支抛 PlayerRouteStaleException（行已存在），
        // 因此直接调用无条件首登写入以确定性地覆盖冲突覆盖路径；真并发收敛语义另见
        // SyncTarget_TrueConcurrentFirstLogin_ConvergesToOneRow。
        // Two sequential first-login inserts on a missing row (different instances, version=1): the second
        // unconditionally overwrites — aligned with Mongo ReplaceOneAsync(IsUpsert) last-writer-wins. The
        // store is EF-based and this path is covered directly through the public API; a sequential
        // second UpsertAsync would instead hit the CAS read-back branch and throw PlayerRouteStaleException.
        await store.InsertFirstLoginAsync(new PlayerRouteRecord { PlayerId = 105, InstanceId = "game-1", Role = "Game", Version = 1, });
        await store.InsertFirstLoginAsync(new PlayerRouteRecord { PlayerId = 105, InstanceId = "game-2", Role = "Game", Version = 1, });

        var stored = await ReadRouteAsync(testDatabase.DataSource, 105);
        Assert.NotNull(stored);
        Assert.Equal("game-2", stored.Value.InstanceId);
        Assert.Equal("Game", stored.Value.Role);
        Assert.Equal(1, stored.Value.Version);
    }

    [Fact]
    public async Task SyncTarget_TrueConcurrentFirstLogin_ConvergesToOneRow()
    {
        if (ShouldSkip)
        {
            return;
        }

        var testDatabase = await CreateDatabaseAsync();
        var store = new PostgreSqlPlayerRouteStore(testDatabase.DataSource);

        // 真并发双首登（同 player、不同实例、同时发起）：主键竞争 / 并发令牌竞争由 store 内部重试收敛
        // ——不断言谁胜（时序非确定），断言硬不变量：恰好一行、无异常逃逸、version=1、终态实例 ∈ {game-1, game-2}。
        // Two truly concurrent first logins (same player, different instances, fired simultaneously): the
        // store's bounded retry converges the primary-key / concurrency-token race. The winner is
        // timing-dependent and NOT asserted; the hard invariants are: exactly one row, no escaping
        // exception, version = 1, and the surviving instance is one of the two writers.
        await Task.WhenAll(
            store.InsertFirstLoginAsync(new PlayerRouteRecord { PlayerId = 206, InstanceId = "game-1", Role = "Game", Version = 1, }),
            store.InsertFirstLoginAsync(new PlayerRouteRecord { PlayerId = 206, InstanceId = "game-2", Role = "Game", Version = 1, }));

        var stored = await ReadRouteAsync(testDatabase.DataSource, 206);
        Assert.NotNull(stored);
        Assert.Contains(stored.Value.InstanceId, new[] { "game-1", "game-2" });
        Assert.Equal("Game", stored.Value.Role);
        Assert.Equal(1, stored.Value.Version);
    }

    [Fact]
    public async Task SyncTarget_CasUpsert_CurrentVersion_Succeeds()
    {
        if (ShouldSkip)
        {
            return;
        }

        var testDatabase = await CreateDatabaseAsync();
        var target = new PlayerRouteSyncTarget(new PostgreSqlPlayerRouteStore(testDatabase.DataSource));

        await target.UpsertAsync(new PlayerRouteRecord { PlayerId = 102, InstanceId = "game-1", Role = "Game", Version = 1, });
        await target.UpsertAsync(new PlayerRouteRecord { PlayerId = 102, InstanceId = "game-2", Role = "Game", Version = 2, });

        var stored = await ReadRouteAsync(testDatabase.DataSource, 102);
        Assert.NotNull(stored);
        Assert.Equal("game-2", stored.Value.InstanceId);
        Assert.Equal(2, stored.Value.Version);
    }

    [Fact]
    public async Task SyncTarget_CasUpsert_StaleVersion_ThrowsPlayerRouteStaleException()
    {
        if (ShouldSkip)
        {
            return;
        }

        var testDatabase = await CreateDatabaseAsync();
        var target = new PlayerRouteSyncTarget(new PostgreSqlPlayerRouteStore(testDatabase.DataSource));

        await target.UpsertAsync(new PlayerRouteRecord { PlayerId = 103, InstanceId = "game-1", Role = "Game", Version = 1, });
        await target.UpsertAsync(new PlayerRouteRecord { PlayerId = 103, InstanceId = "game-2", Role = "Game", Version = 2, });

        // version=1 已经被 version=2 覆盖；再送 version=1 应抛 PlayerRouteStaleException
        await Assert.ThrowsAsync<PlayerRouteStaleException>(async () =>
        {
            await target.UpsertAsync(new PlayerRouteRecord { PlayerId = 103, InstanceId = "game-3", Role = "Game", Version = 1, });
        });
    }

    [Fact]
    public async Task SyncTarget_Delete_RemovesDocument()
    {
        if (ShouldSkip)
        {
            return;
        }

        var testDatabase = await CreateDatabaseAsync();
        var target = new PlayerRouteSyncTarget(new PostgreSqlPlayerRouteStore(testDatabase.DataSource));

        await target.UpsertAsync(new PlayerRouteRecord { PlayerId = 104, InstanceId = "game-1", Role = "Game", Version = 1, });
        await target.DeleteAsync(playerId: 104);

        var stored = await ReadRouteAsync(testDatabase.DataSource, 104);
        Assert.Null(stored);
    }

    [Fact]
    public async Task EnsureSchema_CreatesPrimaryKeyAndRoleIndex()
    {
        if (ShouldSkip)
        {
            return;
        }

        var testDatabase = await CreateDatabaseAsync();

        // player_id 主键（唯一性由 PK 兜底，等价 Mongo 的 playerId 唯一索引）+ role 普通索引（解析侧扫描）。
        var indexNames = await ReadIndexNamesAsync(testDatabase.DataSource, "player_route");
        Assert.Contains("player_route_pkey", indexNames);
        Assert.Contains("ix_player_route_role", indexNames);
    }

    [Fact]
    public async Task Resolver_Tier1Hit_SkipsControlDatabase()
    {
        if (ShouldSkip)
        {
            return;
        }

        var testDatabase = await CreateDatabaseAsync();

        // 控制库故意写错数据（role=other）：如果 resolver 走到 Tier 2 就会拿到错误结果
        await InsertRouteAsync(testDatabase.DataSource, playerId: 201, instanceId: "other-1", role: "Other");

        var fastPath = new OnlineFastPath(playerId: 201, serverType: "Game", serverId: 7, version: 1);
        var resolver = new PlayerRouteResolver(new PostgreSqlPlayerRouteStore(testDatabase.DataSource), fastPath);

        var resolved = await resolver.ResolveAsync(201);

        Assert.True(resolved.IsOnline);
        Assert.Equal("Game", resolved.ServerType);
        Assert.Equal(7, resolved.ServerId);
        Assert.Equal(1, fastPath.Calls);
    }

    [Fact]
    public async Task Resolver_Tier1Miss_Tier2Hit_ReturnsControlInfo()
    {
        if (ShouldSkip)
        {
            return;
        }

        var testDatabase = await CreateDatabaseAsync();

        await InsertRouteAsync(testDatabase.DataSource, playerId: 202, instanceId: "7", role: "Game");

        var fastPath = new OnlineFastPath(playerId: 999, serverType: "X", serverId: 1);
        var resolver = new PlayerRouteResolver(new PostgreSqlPlayerRouteStore(testDatabase.DataSource), fastPath);

        var resolved = await resolver.ResolveAsync(202);

        Assert.True(resolved.IsOnline);
        Assert.Equal("Game", resolved.ServerType);
        Assert.Equal(7, resolved.ServerId);
    }

    [Fact]
    public async Task Resolver_AllTiersMiss_ReturnsOffline()
    {
        if (ShouldSkip)
        {
            return;
        }

        var testDatabase = await CreateDatabaseAsync();

        var fastPath = new OnlineFastPath(playerId: 999, serverType: "X", serverId: 1);
        var resolver = new PlayerRouteResolver(new PostgreSqlPlayerRouteStore(testDatabase.DataSource), fastPath);

        var resolved = await resolver.ResolveAsync(303);

        Assert.False(resolved.IsOnline);
    }

    /// <summary>
    /// 读取一行 player_route（无则 null）。
    /// </summary>
    /// <remarks>
    /// Reads one player_route row (null when absent).
    /// </remarks>
    private static async Task<(string InstanceId, string Role, long Version)?> ReadRouteAsync(NpgsqlDataSource dataSource, long playerId)
    {
        await using var command = dataSource.CreateCommand("SELECT instance_id, role, version FROM player_route WHERE player_id = $1;");
        command.Parameters.AddWithValue(playerId);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return (reader.GetString(0), reader.GetString(1), reader.GetInt64(2));
    }

    /// <summary>
    /// 写入一行 player_route（version=1）。
    /// </summary>
    /// <remarks>
    /// Inserts one player_route row (version = 1).
    /// </remarks>
    private static async Task InsertRouteAsync(NpgsqlDataSource dataSource, long playerId, string instanceId, string role)
    {
        await using var command = dataSource.CreateCommand("INSERT INTO player_route (player_id, instance_id, role, version, last_seen_at) VALUES ($1, $2, $3, 1, now());");
        command.Parameters.AddWithValue(playerId);
        command.Parameters.AddWithValue(instanceId);
        command.Parameters.AddWithValue(role);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 读取指定表全部索引名。
    /// </summary>
    /// <remarks>
    /// Reads every index name of the given table.
    /// </remarks>
    private static async Task<List<string>> ReadIndexNamesAsync(NpgsqlDataSource dataSource, string tableName)
    {
        var names = new List<string>();
        await using var command = dataSource.CreateCommand("SELECT indexname FROM pg_indexes WHERE tablename = $1;");
        command.Parameters.AddWithValue(tableName);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    /// <summary>
    /// 释放独立测试库。
    /// </summary>
    /// <remarks>
    /// Drops the isolated test database.
    /// </remarks>
    public void Dispose()
    {
        if (_testDatabase != null)
        {
            _testDatabase.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    /// <summary>
    /// Tier 1 测试替身：永远对指定玩家返回在线。
    /// </summary>
    /// <remarks>
    /// The Tier 1 test double: always online for the given player.
    /// </remarks>
    private sealed class OnlineFastPath : IPlayerRouteFastPath
    {
        public OnlineFastPath(long playerId, string serverType, int serverId, long version = 1)
        {
            PlayerId = playerId;
            ServerType = serverType;
            ServerId = serverId;
            Version = version;
        }

        public long PlayerId { get; }
        public string ServerType { get; }
        public int ServerId { get; }
        public long Version { get; }
        public int Calls { get; private set; }

        public bool TryGetOnline(long playerId, out PlayerRouteInfo info)
        {
            Calls++;
            if (playerId == PlayerId)
            {
                info = PlayerRouteInfo.Online(ServerType, ServerId, Version);
                return true;
            }

            info = PlayerRouteInfo.Offline();
            return false;
        }
    }
}
