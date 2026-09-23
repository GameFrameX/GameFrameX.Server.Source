// ==========================================================================================
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   This project is licensed solely under the Apache License 2.0,
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   ==========================================================================================


using System.Threading.Tasks;
using GameFrameX.NetWork.RemoteMessaging.Routing;
using MongoDB.Bson;
using MongoDB.Driver;

namespace GameFrameX.Tests.Discovery;

/// <summary>
/// MongoPlayerRouteSyncTarget / MongoPlayerRouteResolver 的 Mongo 集成测试（C143e D21）。
/// </summary>
/// <remarks>
/// Mongo-backed integration tests for the player-route layer (C143e D21),
/// following the repository's existing GAMEFRAMEX_TEST_MONGODB_CONNECTION_STRING
/// gating convention (MongoEndpointIntegrationTests): without the variable the
/// tests skip so plain <c>dotnet test</c> stays green on Mongo-less machines; the
/// topology-equivalence CI workflow sets the variable against a Mongo service
/// container so the suite really runs there. Each test uses a fresh database
/// (Guid suffix) so parallel runs do not cross-contaminate.
/// </remarks>
public sealed class MongoPlayerRouteIntegrationTests : IDisposable
{
    /// <summary>
    /// Mongo 连接串（未设置时跳过）。
    /// </summary>
    private readonly string _connectionString = Environment.GetEnvironmentVariable("GAMEFRAMEX_TEST_MONGODB_CONNECTION_STRING") ?? string.Empty;

    private IMongoDatabase _database;

    /// <summary>
    /// 是否跳过全部用例。
    /// </summary>
    private bool ShouldSkip
    {
        get
        {
            return string.IsNullOrWhiteSpace(_connectionString);
        }
    }

    /// <summary>
    /// 创建独立测试库（每个用例独立 database 防串扰）。
    /// </summary>
    private IMongoDatabase CreateDatabase()
    {
        if (_database == null)
        {
            var client = new MongoClient(_connectionString);
            _database = client.GetDatabase($"gameframex_player_route_test_{Guid.NewGuid():N}");
        }

        return _database;
    }

    public void Dispose()
    {
        if (_database != null)
        {
            try
            {
                _database.Client.DropDatabase(_database.DatabaseNamespace.DatabaseName);
            }
            catch
            {
                // best effort
            }
        }
    }

    [Fact]
    public async Task SyncTarget_FirstTimeUpsert_InsertsDocument()
    {
        if (ShouldSkip)
        {
            return;
        }

        var database = CreateDatabase();
        var target = new MongoPlayerRouteSyncTarget(database);

        await target.UpsertAsync(new PlayerRouteRecord { PlayerId = 101, InstanceId = "game-1", Role = "Game", Version = 1, });

        var collection = database.GetCollection<PlayerRouteDocument>(PlayerRouteCollection.CollectionName);
        var stored = await collection.Find(Builders<PlayerRouteDocument>.Filter.Eq(candidate => candidate.PlayerId, 101)).FirstOrDefaultAsync();
        Assert.NotNull(stored);
        Assert.Equal("game-1", stored.InstanceId);
        Assert.Equal("Game", stored.Role);
        Assert.Equal(1, stored.Version);
    }

    [Fact]
    public async Task SyncTarget_CasUpsert_CurrentVersion_Succeeds()
    {
        if (ShouldSkip)
        {
            return;
        }

        var database = CreateDatabase();
        var target = new MongoPlayerRouteSyncTarget(database);

        await target.UpsertAsync(new PlayerRouteRecord { PlayerId = 102, InstanceId = "game-1", Role = "Game", Version = 1, });
        await target.UpsertAsync(new PlayerRouteRecord { PlayerId = 102, InstanceId = "game-2", Role = "Game", Version = 2, });

        var collection = database.GetCollection<PlayerRouteDocument>(PlayerRouteCollection.CollectionName);
        var stored = await collection.Find(Builders<PlayerRouteDocument>.Filter.Eq(candidate => candidate.PlayerId, 102)).FirstOrDefaultAsync();
        Assert.NotNull(stored);
        Assert.Equal("game-2", stored.InstanceId);
        Assert.Equal(2, stored.Version);
    }

    [Fact]
    public async Task SyncTarget_CasUpsert_StaleVersion_ThrowsPlayerRouteStaleException()
    {
        if (ShouldSkip)
        {
            return;
        }

        var database = CreateDatabase();
        var target = new MongoPlayerRouteSyncTarget(database);

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

        var database = CreateDatabase();
        var target = new MongoPlayerRouteSyncTarget(database);

        await target.UpsertAsync(new PlayerRouteRecord { PlayerId = 104, InstanceId = "game-1", Role = "Game", Version = 1, });
        await target.DeleteAsync(playerId: 104);

        var collection = database.GetCollection<PlayerRouteDocument>(PlayerRouteCollection.CollectionName);
        var stored = await collection.Find(Builders<PlayerRouteDocument>.Filter.Eq(candidate => candidate.PlayerId, 104)).FirstOrDefaultAsync();
        Assert.Null(stored);
    }

    [Fact]
    public async Task EnsureIndexesAsync_CreatesUniqueAndTtlIndexes()
    {
        if (ShouldSkip)
        {
            return;
        }

        var database = CreateDatabase();
        var collection = database.GetCollection<PlayerRouteDocument>(PlayerRouteCollection.CollectionName);
        await PlayerRouteCollection.EnsureIndexesAsync(collection);

        var indexes = await collection.Indexes.List().ToListAsync();
        var indexSummary = string.Join(" | ", indexes.Select(BuildIndexSummary));

        // 唯一索引：playerId_1
        Assert.Contains(indexes, x => HasKey(x, "playerId") && IsUnique(x));
        // TTL 索引：lastSeenAt_1（带 expireAfterSeconds 等于 30 天）
        Assert.Contains(indexes, x => HasKey(x, "lastSeenAt") && HasTtl(x));
    }

    private static string BuildIndexSummary(BsonDocument index)
    {
        return index["name"].AsString;
    }

    private static bool HasKey(BsonDocument index, string field)
    {
        var key = index["key"].AsBsonDocument;
        return key.Contains(field);
    }

    private static bool IsUnique(BsonDocument index)
    {
        return index.Contains("unique") && index["unique"].ToBoolean();
    }

    private static bool HasTtl(BsonDocument index)
    {
        if (!index.Contains("expireAfterSeconds"))
        {
            return false;
        }

        var seconds = index["expireAfterSeconds"].ToInt64();
        return seconds == PlayerRouteCollection.TtlSeconds;
    }

    [Fact]
    public async Task Resolver_Tier1Hit_SkipsControlDatabase()
    {
        if (ShouldSkip)
        {
            return;
        }

        var database = CreateDatabase();
        var collection = database.GetCollection<PlayerRouteDocument>(PlayerRouteCollection.CollectionName);
        await PlayerRouteCollection.EnsureIndexesAsync(collection);

        // 控制库故意写错数据（role=other, version=999）：如果 resolver 走到 Tier 2 就会拿到错误结果
        await collection.InsertOneAsync(PlayerRouteCollection.CreateOnline(playerId: 201, instanceId: "other-1", role: "Other"));

        var fastPath = new OnlineFastPath(playerId: 201, serverType: "Game", serverId: 7, version: 1);
        var resolver = new MongoPlayerRouteResolver(database, fastPath);

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

        var database = CreateDatabase();
        var collection = database.GetCollection<PlayerRouteDocument>(PlayerRouteCollection.CollectionName);
        await PlayerRouteCollection.EnsureIndexesAsync(collection);

        await collection.InsertOneAsync(PlayerRouteCollection.CreateOnline(playerId: 202, instanceId: "7", role: "Game"));

        var fastPath = new OnlineFastPath(playerId: 999, serverType: "X", serverId: 1);
        var resolver = new MongoPlayerRouteResolver(database, fastPath);

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

        var database = CreateDatabase();
        var collection = database.GetCollection<PlayerRouteDocument>(PlayerRouteCollection.CollectionName);
        await PlayerRouteCollection.EnsureIndexesAsync(collection);

        var fastPath = new OnlineFastPath(playerId: 999, serverType: "X", serverId: 1);
        var resolver = new MongoPlayerRouteResolver(database, fastPath);

        var resolved = await resolver.ResolveAsync(303);

        Assert.False(resolved.IsOnline);
    }

    /// <summary>
    /// Tier 1 测试替身：永远对指定玩家返回在线。
    /// </summary>
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