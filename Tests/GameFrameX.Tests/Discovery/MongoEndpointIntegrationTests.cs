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
//   Any legal disputes and liabilities arising from secondary development based on this project
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


using GameFrameX.NetWork.RemoteMessaging.Discovery;
using MongoDB.Driver;

namespace GameFrameX.Tests.Discovery;

/// <summary>
/// MongoEndpointRegistry / MongoEndpointWatcher 的 Mongo 集成测试（C143d D11/D15）。
/// </summary>
/// <remarks>
/// Mongo-backed integration tests for the heartbeat writer and reader (C143d D11/D15),
/// following the repository's existing GAMEFRAMEX_TEST_MONGODB_CONNECTION_STRING gating
/// convention (MongoDbServiceConnectionTests): without the variable the tests skip so
/// plain <c>dotnet test</c> stays green on Mongo-less machines; the topology-equivalence
/// CI workflow sets the variable against a Mongo service container so the suite really
/// runs there. Intervals are shrunk (150 ms heartbeat / 150 ms poll / 450 ms staleness)
/// to keep the wall time small while exercising the same state machine.
/// </remarks>
public sealed class MongoEndpointIntegrationTests : IDisposable
{
    /// <summary>
    /// Mongo 连接串（未设置时跳过）。
    /// </summary>
    private readonly string _connectionString = Environment.GetEnvironmentVariable("GAMEFRAMEX_TEST_MONGODB_CONNECTION_STRING") ?? string.Empty;

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
    /// 本测试类的独立控制库。
    /// </summary>
    private IMongoDatabase _controlDatabase;

    /// <summary>
    /// 创建独立控制库（每个用例独立 database 防串扰）。
    /// </summary>
    private IMongoDatabase CreateControlDatabase()
    {
        if (_controlDatabase == null)
        {
            var client = new MongoClient(_connectionString);
            _controlDatabase = client.GetDatabase($"gameframex_control_test_{Guid.NewGuid():N}");
        }

        return _controlDatabase;
    }

    [Fact]
    public async Task Registry_ShouldPublishBootingThenActiveAndKeepHeartbeatFresh()
    {
        if (ShouldSkip)
        {
            return;
        }

        var controlDatabase = CreateControlDatabase();
        var selfDescriptor = new InstanceDescriptor("Game", "integration-registry-1", "tcp://127.0.0.1:7701", InstanceStatus.Booting, 0, EndpointAddressKind.IPv4, 9001, DateTime.UtcNow);
        using (var registry = new MongoEndpointRegistry(controlDatabase, selfDescriptor, TimeSpan.FromMilliseconds(150)))
        {
            await registry.StartAsync();

            var collection = controlDatabase.GetCollection<MongoDB.Bson.BsonDocument>(MongoEndpointRegistry.HeartbeatCollectionName);
            var bootingDocument = await WaitForDocumentAsync(collection, "integration-registry-1");
            Assert.NotNull(bootingDocument);
            // 启动即宣告 Booting 而非 Active：其他进程在服务真正就绪前不应向本实例路由流量。
            Assert.Equal("Booting", bootingDocument["status"].AsString);
            Assert.Equal("Game", bootingDocument["role"].AsString);
            Assert.Equal("tcp://127.0.0.1:7701", bootingDocument["advertiseEndpoint"].AsString);
            Assert.Equal(9001, bootingDocument["incarnation"].AsInt64);

            await registry.MarkActiveAsync();
            var activeDocument = await WaitForStatusAsync(collection, "integration-registry-1", "Active");
            Assert.NotNull(activeDocument);

            // 首次写入后心跳循环必须继续全量 upsert：lastHeartbeat 持续前进（否则 watcher 会在三周期后将健康实例误判下线）。
            var heartbeatBefore = activeDocument["lastHeartbeat"].ToUniversalTime();
            var heartbeatAdvanced = false;
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                var latest = await collection.Find(candidate => candidate["_id"] == "integration-registry-1").FirstOrDefaultAsync();
                if (latest != null && latest["lastHeartbeat"].ToUniversalTime() > heartbeatBefore)
                {
                    heartbeatAdvanced = true;
                    break;
                }

                await Task.Delay(100);
            }

            Assert.True(heartbeatAdvanced, "The heartbeat lastHeartbeat did not advance after the initial write.");

            await registry.StopAsync();
            var stoppedDocument = await collection.Find(candidate => candidate["_id"] == "integration-registry-1").FirstOrDefaultAsync();
            Assert.NotNull(stoppedDocument);
            Assert.Equal("Stopped", stoppedDocument["status"].AsString);
        }
    }

    [Fact]
    public async Task Watcher_ShouldSkipEventsForIneligibleFirstObservations()
    {
        if (ShouldSkip)
        {
            return;
        }

        var controlDatabase = CreateControlDatabase();
        var collection = controlDatabase.GetCollection<MongoDB.Bson.BsonDocument>(MongoEndpointRegistry.HeartbeatCollectionName);
        var events = new RecordingInstanceEvents();
        using (var watcher = new MongoEndpointWatcher(controlDatabase, TimeSpan.FromMilliseconds(150), TimeSpan.FromSeconds(30)))
        {
            watcher.Subscribe(events);
            await watcher.StartAsync();

            // 三类不具备路由资格的首次观测：Stopped、陈旧（超过判活阈值）、Booting；外加一个首次即为 Draining 的实例。
            var stoppedId = $"integration-ineligible-stopped-{Guid.NewGuid():N}";
            var staleId = $"integration-ineligible-stale-{Guid.NewGuid():N}";
            var bootingId = $"integration-ineligible-booting-{Guid.NewGuid():N}";
            var drainingId = $"integration-ineligible-draining-{Guid.NewGuid():N}";
            await collection.InsertManyAsync(new[]
            {
                new MongoDB.Bson.BsonDocument
                {
                    { "_id", stoppedId }, { "role", "Game" }, { "advertiseEndpoint", "tcp://10.0.0.1:7501" }, { "status", "Stopped" },
                    { "load", 0 }, { "addressKind", "IPv4" }, { "incarnation", 9400L }, { "lastHeartbeat", DateTime.UtcNow },
                },
                new MongoDB.Bson.BsonDocument
                {
                    { "_id", staleId }, { "role", "Game" }, { "advertiseEndpoint", "tcp://10.0.0.2:7502" }, { "status", "Active" },
                    { "load", 0 }, { "addressKind", "IPv4" }, { "incarnation", 9401L }, { "lastHeartbeat", DateTime.UtcNow.AddSeconds(-60) },
                },
                new MongoDB.Bson.BsonDocument
                {
                    { "_id", bootingId }, { "role", "Game" }, { "advertiseEndpoint", "tcp://10.0.0.3:7503" }, { "status", "Booting" },
                    { "load", 0 }, { "addressKind", "IPv4" }, { "incarnation", 9402L }, { "lastHeartbeat", DateTime.UtcNow },
                },
                new MongoDB.Bson.BsonDocument
                {
                    { "_id", drainingId }, { "role", "Match" }, { "advertiseEndpoint", "tcp://match.internal:7504" }, { "status", "Draining" },
                    { "load", 0 }, { "addressKind", "DnsName" }, { "incarnation", 9403L }, { "lastHeartbeat", DateTime.UtcNow },
                },
            });

            // 等待 watcher 至少完成一轮 poll（见到 Draining 广播即证明该轮已处理同批插入的全部实例）：
            // 不具备路由资格的实例既不发 Online，也不进路由表；Draining 首次观测发 Draining 且进 Instance 视图。
            await WaitUntilAsync(delegate ()
            {
                lock (events.Observed)
                {
                    return events.Observed.Contains((RoleInstanceChangeKind.Draining, drainingId));
                }
            }, TimeSpan.FromSeconds(30));

            lock (events.Observed)
            {
                Assert.DoesNotContain((RoleInstanceChangeKind.Online, stoppedId), events.Observed);
                Assert.DoesNotContain((RoleInstanceChangeKind.Online, staleId), events.Observed);
                Assert.DoesNotContain((RoleInstanceChangeKind.Online, bootingId), events.Observed);
                Assert.Contains((RoleInstanceChangeKind.Draining, drainingId), events.Observed);
            }

            Assert.False(watcher.Current.TryGetInstance(stoppedId, out _));
            Assert.False(watcher.Current.TryGetInstance(staleId, out _));
            Assert.False(watcher.Current.TryGetInstance(bootingId, out _));
            Assert.True(watcher.Current.TryGetInstance(drainingId, out _));

            // Booting → Active 跃迁后才发 Online 并进入路由表（与写侧 MarkActive 的就绪语义闭环）。
            var update = Builders<MongoDB.Bson.BsonDocument>.Update
                .Set("status", "Active")
                .Set("lastHeartbeat", DateTime.UtcNow);
            await collection.UpdateOneAsync(candidate => candidate["_id"] == bootingId, update);
            await WaitUntilAsync(delegate ()
            {
                return watcher.Current.TryGetInstance(bootingId, out _) && watcher.Current.GetActiveInstances("Game").Any(instance => instance.InstanceId == bootingId);
            }, TimeSpan.FromSeconds(30));
            Assert.Contains((RoleInstanceChangeKind.Online, bootingId), events.Observed);
        }
    }

    [Fact]
    public async Task Watcher_ShouldDiscoverOnlineAndBroadcastEvictedOnRemoval()
    {
        if (ShouldSkip)
        {
            return;
        }

        var controlDatabase = CreateControlDatabase();
        var collection = controlDatabase.GetCollection<MongoDB.Bson.BsonDocument>(MongoEndpointRegistry.HeartbeatCollectionName);
        var events = new RecordingInstanceEvents();
        using (var watcher = new MongoEndpointWatcher(controlDatabase, TimeSpan.FromMilliseconds(150), TimeSpan.FromSeconds(5)))
        {
            watcher.Subscribe(events);
            await watcher.StartAsync();

            // 直接写一份心跳文档（扮演另一进程的写侧），watcher 应广播 Online 且路由表包含该实例。
            var instanceId = $"integration-watcher-{Guid.NewGuid():N}";
            await collection.InsertOneAsync(new MongoDB.Bson.BsonDocument
            {
                { "_id", instanceId },
                { "role", "Social" },
                { "advertiseEndpoint", "tcp://social.internal:7101" },
                { "status", "Active" },
                { "load", 0 },
                { "addressKind", "DnsName" },
                { "incarnation", 9100L },
                { "lastHeartbeat", DateTime.UtcNow },
            });

            await WaitUntilAsync(() => watcher.Current.TryGetInstance(instanceId, out _), TimeSpan.FromSeconds(30));
            Assert.Contains((RoleInstanceChangeKind.Online, instanceId), events.Observed);

            // 删除文档（模拟 TTL 清除），watcher 应广播 Evicted 且路由表摘除该实例。
            await collection.DeleteOneAsync(candidate => candidate["_id"] == instanceId);
            await WaitUntilAsync(() => !watcher.Current.TryGetInstance(instanceId, out _), TimeSpan.FromSeconds(30));
            Assert.Contains((RoleInstanceChangeKind.Evicted, instanceId), events.Observed);
        }
    }

    [Fact]
    public async Task Watcher_ShouldEmitOfflineThenOnlineOnIncarnationChange()
    {
        if (ShouldSkip)
        {
            return;
        }

        var controlDatabase = CreateControlDatabase();
        var collection = controlDatabase.GetCollection<MongoDB.Bson.BsonDocument>(MongoEndpointRegistry.HeartbeatCollectionName);
        var events = new RecordingInstanceEvents();
        using (var watcher = new MongoEndpointWatcher(controlDatabase, TimeSpan.FromMilliseconds(150), TimeSpan.FromSeconds(5)))
        {
            watcher.Subscribe(events);
            await watcher.StartAsync();

            var instanceId = $"integration-incarnation-{Guid.NewGuid():N}";
            await collection.InsertOneAsync(new MongoDB.Bson.BsonDocument
            {
                { "_id", instanceId },
                { "role", "Game" },
                { "advertiseEndpoint", "tcp://10.0.0.9:7301" },
                { "status", "Active" },
                { "load", 0 },
                { "addressKind", "IPv4" },
                { "incarnation", 9200L },
                { "lastHeartbeat", DateTime.UtcNow },
            });
            await WaitUntilAsync(() => watcher.Current.TryGetInstance(instanceId, out _), TimeSpan.FromSeconds(30));

            // 同 instanceId 换 incarnation（进程重启语义）：应广播 Offline+Online，而非 Recovered。
            var restarted = new MongoDB.Bson.BsonDocument
            {
                { "_id", instanceId },
                { "role", "Game" },
                { "advertiseEndpoint", "tcp://10.0.0.9:7302" },
                { "status", "Active" },
                { "load", 0 },
                { "addressKind", "IPv4" },
                { "incarnation", 9201L },
                { "lastHeartbeat", DateTime.UtcNow },
            };
            await collection.ReplaceOneAsync(candidate => candidate["_id"] == instanceId, restarted);

            await WaitUntilAsync(delegate ()
            {
                return watcher.Current.TryGetInstance(instanceId, out var instance) && instance.Incarnation == 9201;
            }, TimeSpan.FromSeconds(30));
            lock (events.Observed)
            {
                var kinds = events.Observed.Where(pair => pair.InstanceId == instanceId).Select(pair => pair.Kind).ToList();
                Assert.Equal(RoleInstanceChangeKind.Online, kinds[0]);
                Assert.Contains(RoleInstanceChangeKind.Offline, kinds.Skip(1));
                Assert.Contains(RoleInstanceChangeKind.Online, kinds.Skip(1));
                Assert.DoesNotContain(RoleInstanceChangeKind.Recovered, kinds);
            }
        }
    }

    [Fact]
    public async Task Watcher_ShouldEmitDrainingAndExcludeFromRoleView()
    {
        if (ShouldSkip)
        {
            return;
        }

        var controlDatabase = CreateControlDatabase();
        var collection = controlDatabase.GetCollection<MongoDB.Bson.BsonDocument>(MongoEndpointRegistry.HeartbeatCollectionName);
        var events = new RecordingInstanceEvents();
        using (var watcher = new MongoEndpointWatcher(controlDatabase, TimeSpan.FromMilliseconds(150), TimeSpan.FromSeconds(5)))
        {
            watcher.Subscribe(events);
            await watcher.StartAsync();

            var instanceId = $"integration-draining-{Guid.NewGuid():N}";
            await collection.InsertOneAsync(new MongoDB.Bson.BsonDocument
            {
                { "_id", instanceId },
                { "role", "Match" },
                { "advertiseEndpoint", "tcp://match.internal:7401" },
                { "status", "Active" },
                { "load", 0 },
                { "addressKind", "DnsName" },
                { "incarnation", 9300L },
                { "lastHeartbeat", DateTime.UtcNow },
            });
            await WaitUntilAsync(() => watcher.Current.GetActiveInstances("Match").Count > 0, TimeSpan.FromSeconds(30));

            // 状态跃迁 Active → Draining：应广播 Draining 事件；Role 视图摘除、Instance 视图保留（在途投递合法）。
            var update = Builders<MongoDB.Bson.BsonDocument>.Update
                .Set("status", "Draining")
                .Set("lastHeartbeat", DateTime.UtcNow);
            await collection.UpdateOneAsync(candidate => candidate["_id"] == instanceId, update);

            await WaitUntilAsync(delegate ()
            {
                return watcher.Current.GetActiveInstances("Match").Count == 0;
            }, TimeSpan.FromSeconds(30));
            Assert.True(watcher.Current.TryGetInstance(instanceId, out _));
            Assert.Contains((RoleInstanceChangeKind.Draining, instanceId), events.Observed);
        }
    }

    /// <summary>
    /// 轮询等待指定实例文档出现。
    /// </summary>
    private static async Task<MongoDB.Bson.BsonDocument> WaitForDocumentAsync(IMongoCollection<MongoDB.Bson.BsonDocument> collection, string instanceId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        MongoDB.Bson.BsonDocument document = null;
        while (document == null && DateTime.UtcNow < deadline)
        {
            document = await collection.Find(candidate => candidate["_id"] == instanceId).FirstOrDefaultAsync();
            if (document == null)
            {
                await Task.Delay(100);
            }
        }

        return document;
    }

    /// <summary>
    /// 轮询等待指定实例文档达到期望状态。
    /// </summary>
    private static async Task<MongoDB.Bson.BsonDocument> WaitForStatusAsync(IMongoCollection<MongoDB.Bson.BsonDocument> collection, string instanceId, string status)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        MongoDB.Bson.BsonDocument document = null;
        while (DateTime.UtcNow < deadline)
        {
            document = await collection.Find(candidate => candidate["_id"] == instanceId).FirstOrDefaultAsync();
            if (document != null && document["status"].AsString == status)
            {
                return document;
            }

            await Task.Delay(100);
        }

        return document;
    }

    /// <summary>
    /// 轮询断言直到条件成立或超时。
    /// </summary>
    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(100);
        }

        Assert.True(condition(), "The expected condition was not met within the timeout.");
    }

    /// <summary>
    /// 记录型事件订阅者。
    /// </summary>
    private sealed class RecordingInstanceEvents : IRoleInstanceEvents
    {
        public List<(RoleInstanceChangeKind Kind, string InstanceId)> Observed { get; } = new List<(RoleInstanceChangeKind, string)>();

        public void OnInstanceChanged(RoleInstanceChangeKind kind, InstanceDescriptor instance)
        {
            lock (Observed)
            {
                Observed.Add((kind, instance.InstanceId));
            }
        }
    }

    /// <summary>
    /// 释放独立控制库。
    /// </summary>
    public void Dispose()
    {
        if (_controlDatabase != null)
        {
            try
            {
                _controlDatabase.Client.DropDatabaseAsync(_controlDatabase.DatabaseNamespace.DatabaseName).GetAwaiter().GetResult();
            }
            catch (Exception)
            {
                // 清理失败不影响测试结果。
            }
        }
    }
}
