// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
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
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes and liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using GameFrameX.NetWork.RemoteMessaging.Discovery;
using GameFrameX.NetWork.RemoteMessaging.Routing;
using Npgsql;

namespace GameFrameX.Tests.Discovery;

/// <summary>
/// PostgreSqlEndpointRegistry / PostgreSqlEndpointWatcher（TTL 清理经 DiscoveryRegistry 循环）的 PostgreSQL 集成测试（C166 T8）。
/// </summary>
/// <remarks>
/// PostgreSQL-backed integration tests for the heartbeat writer and reader
/// (TTL cleanup via the DiscoveryRegistry loop) (C166 T8), ported test-by-test from
/// <c>MongoEndpointIntegrationTests</c> with identical names and assertions.
/// Gated by GAMEFRAMEX_TEST_POSTGRESQL_CONNECTION_STRING: without the variable
/// the tests skip so plain <c>dotnet test</c> stays green on PostgreSQL-less
/// machines. Each test class creates an isolated database (Guid suffix, dropped
/// on Dispose with FORCE). Intervals are shrunk (150 ms heartbeat / 150 ms poll /
/// 450 ms staleness) to keep the wall time small while exercising the same
/// state machine.
/// </remarks>
public sealed class PostgreSqlEndpointIntegrationTests : IDisposable
{
    /// <summary>
    /// PostgreSQL 连接串（未设置时跳过）。
    /// </summary>
    /// <remarks>
    /// The PostgreSQL connection string (tests skip when unset).
    /// </remarks>
    private readonly string _connectionString = Environment.GetEnvironmentVariable("GAMEFRAMEX_TEST_POSTGRESQL_CONNECTION_STRING") ?? string.Empty;

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
    /// 本测试类的独立控制库句柄。
    /// </summary>
    /// <remarks>
    /// The isolated control-database handle of this test class.
    /// </remarks>
    private PostgreSqlTestDatabase _testDatabase;

    /// <summary>
    /// 创建独立控制库（每个用例复用同一个独立 database 防串扰；用例内用 Guid 实例 id）。
    /// </summary>
    /// <remarks>
    /// Creates the isolated control database (lazily, once per test class; tests use Guid instance ids for isolation).
    /// </remarks>
    private async Task<PostgreSqlTestDatabase> CreateControlDatabaseAsync()
    {
        if (_testDatabase == null)
        {
            _testDatabase = await PostgreSqlTestDatabase.CreateAsync(_connectionString);
            await new PostgreSqlHeartbeatStore(_testDatabase.DataSource).EnsureSchemaAsync();
        }

        return _testDatabase;
    }

    [Fact]
    public async Task Registry_ShouldPublishBootingThenActiveAndKeepHeartbeatFresh()
    {
        if (ShouldSkip)
        {
            return;
        }

        var testDatabase = await CreateControlDatabaseAsync();
        var selfDescriptor = new InstanceDescriptor("Game", "integration-registry-1", "tcp://127.0.0.1:7701", InstanceStatus.Booting, 0, EndpointAddressKind.IPv4, 9001, DateTime.UtcNow);
        using (var registry = new DiscoveryRegistry(new PostgreSqlHeartbeatStore(testDatabase.DataSource), null, selfDescriptor, TimeSpan.FromMilliseconds(150)))
        {
            await registry.StartAsync();

            var bootingRow = await WaitForRowAsync(testDatabase.DataSource, "integration-registry-1");
            Assert.NotNull(bootingRow);
            // 启动即宣告 Booting 而非 Active：其他进程在服务真正就绪前不应向本实例路由流量。
            Assert.Equal("Booting", bootingRow.Status);
            Assert.Equal("Game", bootingRow.Role);
            Assert.Equal("tcp://127.0.0.1:7701", bootingRow.AdvertiseEndpoint);
            Assert.Equal(9001, bootingRow.Incarnation);

            await registry.MarkActiveAsync();
            var activeRow = await WaitForStatusAsync(testDatabase.DataSource, "integration-registry-1", "Active");
            Assert.NotNull(activeRow);

            // 首次写入后心跳循环必须继续全量 upsert：last_heartbeat 持续前进（否则 watcher 会在三周期后将健康实例误判下线）。
            var heartbeatBefore = activeRow.LastHeartbeat;
            var heartbeatAdvanced = false;
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                var latest = await ReadRowAsync(testDatabase.DataSource, "integration-registry-1");
                if (latest != null && latest.LastHeartbeat > heartbeatBefore)
                {
                    heartbeatAdvanced = true;
                    break;
                }

                await Task.Delay(100);
            }

            Assert.True(heartbeatAdvanced, "The heartbeat last_heartbeat did not advance after the initial write.");

            await registry.StopAsync();
            var stoppedRow = await ReadRowAsync(testDatabase.DataSource, "integration-registry-1");
            Assert.NotNull(stoppedRow);
            Assert.Equal("Stopped", stoppedRow.Status);
        }
    }

    [Fact]
    public async Task Watcher_ShouldSkipEventsForIneligibleFirstObservations()
    {
        if (ShouldSkip)
        {
            return;
        }

        var testDatabase = await CreateControlDatabaseAsync();
        var events = new RecordingInstanceEvents();
        using (var watcher = new DiscoveryWatcher(new PostgreSqlHeartbeatStore(testDatabase.DataSource), TimeSpan.FromMilliseconds(150), TimeSpan.FromSeconds(30)))
        {
            watcher.Subscribe(events);
            await watcher.StartAsync();

            // 三类不具备路由资格的首次观测：Stopped、陈旧（超过判活阈值）、Booting；外加一个首次即为 Draining 的实例。
            var stoppedId = $"integration-ineligible-stopped-{Guid.NewGuid():N}";
            var staleId = $"integration-ineligible-stale-{Guid.NewGuid():N}";
            var bootingId = $"integration-ineligible-booting-{Guid.NewGuid():N}";
            var drainingId = $"integration-ineligible-draining-{Guid.NewGuid():N}";
            await InsertHeartbeatAsync(testDatabase.DataSource, stoppedId, "Game", "tcp://10.0.0.1:7501", "Stopped", "IPv4", 9400L, DateTime.UtcNow);
            await InsertHeartbeatAsync(testDatabase.DataSource, staleId, "Game", "tcp://10.0.0.2:7502", "Active", "IPv4", 9401L, DateTime.UtcNow.AddSeconds(-60));
            await InsertHeartbeatAsync(testDatabase.DataSource, bootingId, "Game", "tcp://10.0.0.3:7503", "Booting", "IPv4", 9402L, DateTime.UtcNow);
            await InsertHeartbeatAsync(testDatabase.DataSource, drainingId, "Match", "tcp://match.internal:7504", "Draining", "DnsName", 9403L, DateTime.UtcNow);

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
            await ExecuteAsync(testDatabase.DataSource, $"UPDATE server_heartbeat SET status = 'Active', last_heartbeat = now() WHERE instance_id = '{bootingId}';");
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

        var testDatabase = await CreateControlDatabaseAsync();
        var events = new RecordingInstanceEvents();
        using (var watcher = new DiscoveryWatcher(new PostgreSqlHeartbeatStore(testDatabase.DataSource), TimeSpan.FromMilliseconds(150), TimeSpan.FromSeconds(5)))
        {
            watcher.Subscribe(events);
            await watcher.StartAsync();

            // 直接写一份心跳行（扮演另一进程的写侧），watcher 应广播 Online 且路由表包含该实例。
            var instanceId = $"integration-watcher-{Guid.NewGuid():N}";
            await InsertHeartbeatAsync(testDatabase.DataSource, instanceId, "Social", "tcp://social.internal:7101", "Active", "DnsName", 9100L, DateTime.UtcNow);

            await WaitUntilAsync(() => watcher.Current.TryGetInstance(instanceId, out _), TimeSpan.FromSeconds(30));
            Assert.Contains((RoleInstanceChangeKind.Online, instanceId), events.Observed);

            // 删除行（模拟 TTL 清理清除），watcher 应广播 Evicted 且路由表摘除该实例。
            await ExecuteAsync(testDatabase.DataSource, $"DELETE FROM server_heartbeat WHERE instance_id = '{instanceId}';");
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

        var testDatabase = await CreateControlDatabaseAsync();
        var events = new RecordingInstanceEvents();
        using (var watcher = new DiscoveryWatcher(new PostgreSqlHeartbeatStore(testDatabase.DataSource), TimeSpan.FromMilliseconds(150), TimeSpan.FromSeconds(5)))
        {
            watcher.Subscribe(events);
            await watcher.StartAsync();

            var instanceId = $"integration-incarnation-{Guid.NewGuid():N}";
            await InsertHeartbeatAsync(testDatabase.DataSource, instanceId, "Game", "tcp://10.0.0.9:7301", "Active", "IPv4", 9200L, DateTime.UtcNow);
            await WaitUntilAsync(() => watcher.Current.TryGetInstance(instanceId, out _), TimeSpan.FromSeconds(30));

            // 同 instanceId 换 incarnation（进程重启语义）：应广播 Offline+Online，而非 Recovered。
            await ExecuteAsync(testDatabase.DataSource, $"UPDATE server_heartbeat SET advertise_endpoint = 'tcp://10.0.0.9:7302', incarnation = 9201, last_heartbeat = now() WHERE instance_id = '{instanceId}';");

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

        var testDatabase = await CreateControlDatabaseAsync();
        var events = new RecordingInstanceEvents();
        using (var watcher = new DiscoveryWatcher(new PostgreSqlHeartbeatStore(testDatabase.DataSource), TimeSpan.FromMilliseconds(150), TimeSpan.FromSeconds(5)))
        {
            watcher.Subscribe(events);
            await watcher.StartAsync();

            var instanceId = $"integration-draining-{Guid.NewGuid():N}";
            await InsertHeartbeatAsync(testDatabase.DataSource, instanceId, "Match", "tcp://match.internal:7401", "Active", "DnsName", 9300L, DateTime.UtcNow);
            await WaitUntilAsync(() => watcher.Current.GetActiveInstances("Match").Count > 0, TimeSpan.FromSeconds(30));

            // 状态跃迁 Active → Draining：应广播 Draining 事件；Role 视图摘除、Instance 视图保留（在途投递合法）。
            await ExecuteAsync(testDatabase.DataSource, $"UPDATE server_heartbeat SET status = 'Draining', last_heartbeat = now() WHERE instance_id = '{instanceId}';");

            await WaitUntilAsync(delegate ()
            {
                return watcher.Current.GetActiveInstances("Match").Count == 0;
            }, TimeSpan.FromSeconds(30));
            Assert.True(watcher.Current.TryGetInstance(instanceId, out _));
            Assert.Contains((RoleInstanceChangeKind.Draining, instanceId), events.Observed);
        }
    }

    [Fact]
    public async Task TtlCleanup_ShouldRemoveExpiredHeartbeatRows()
    {
        if (ShouldSkip)
        {
            return;
        }

        var testDatabase = await CreateControlDatabaseAsync();

        // 一行新鲜心跳、一行过期心跳（15s 窗口）与一行过期 player_route（30 天窗口，先建表）。
        var freshId = $"integration-ttl-fresh-{Guid.NewGuid():N}";
        var expiredId = $"integration-ttl-expired-{Guid.NewGuid():N}";
        await InsertHeartbeatAsync(testDatabase.DataSource, freshId, "Game", "tcp://10.0.0.4:7601", "Active", "IPv4", 9500L, DateTime.UtcNow);
        await InsertHeartbeatAsync(testDatabase.DataSource, expiredId, "Game", "tcp://10.0.0.5:7602", "Active", "IPv4", 9501L, DateTime.UtcNow.AddSeconds(-60));
        await new PostgreSqlPlayerRouteStore(testDatabase.DataSource).EnsureSchemaAsync();
        await ExecuteAsync(testDatabase.DataSource, $"INSERT INTO player_route (player_id, instance_id, role, version, last_seen_at) VALUES (901, '{expiredId}', 'Game', 1, now() - interval '40 days');");

        using (var registry = new DiscoveryRegistry(new PostgreSqlHeartbeatStore(testDatabase.DataSource), new PostgreSqlPlayerRouteStore(testDatabase.DataSource), null, null, TimeSpan.FromMilliseconds(200)))
        {
            await registry.StartAsync();

            // 同一轮清理 pass 的两个 DELETE（心跳 / 路由）之间没有原子性：等待条件必须覆盖两者，
            // 否则并发负载下偶发在两步之间退出导致断言时序缺陷（C168 实测修复）。
            // The two deletes of a cleanup pass are not atomic with each other: the wait must cover both,
            // otherwise the test can exit between them under concurrent load (C168 timing fix).
            await WaitUntilAsync(async () => await ReadRowAsync(testDatabase.DataSource, expiredId) == null &&
                                            Convert.ToInt64(await ExecuteScalarAsync(testDatabase.DataSource, "SELECT count(*) FROM player_route WHERE player_id = 901;")) == 0L, TimeSpan.FromSeconds(30));
        }

        Assert.NotNull(await ReadRowAsync(testDatabase.DataSource, freshId));
        var routeRow = await ExecuteScalarAsync(testDatabase.DataSource, "SELECT count(*) FROM player_route WHERE player_id = 901;");
        Assert.Equal(0L, Convert.ToInt64(routeRow));
    }

    /// <summary>
    /// 写入一行心跳。
    /// </summary>
    /// <remarks>
    /// Inserts one heartbeat row.
    /// </remarks>
    private static async Task InsertHeartbeatAsync(NpgsqlDataSource dataSource, string instanceId, string role, string advertiseEndpoint, string status, string addressKind, long incarnation, DateTime lastHeartbeat)
    {
        await using var command = dataSource.CreateCommand("INSERT INTO server_heartbeat (instance_id, role, advertise_endpoint, status, load, address_kind, incarnation, last_heartbeat) VALUES ($1, $2, $3, $4, 0, $5, $6, $7);");
        command.Parameters.AddWithValue(instanceId);
        command.Parameters.AddWithValue(role);
        command.Parameters.AddWithValue(advertiseEndpoint);
        command.Parameters.AddWithValue(status);
        command.Parameters.AddWithValue(addressKind);
        command.Parameters.AddWithValue(incarnation);
        command.Parameters.AddWithValue(lastHeartbeat.ToUniversalTime());
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 读取一行心跳（无则 null）。
    /// </summary>
    /// <remarks>
    /// Reads one heartbeat row (null when absent).
    /// </remarks>
    private static async Task<HeartbeatSnapshot> ReadRowAsync(NpgsqlDataSource dataSource, string instanceId)
    {
        await using var command = dataSource.CreateCommand("SELECT status, role, advertise_endpoint, incarnation, last_heartbeat FROM server_heartbeat WHERE instance_id = $1;");
        command.Parameters.AddWithValue(instanceId);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new HeartbeatSnapshot(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetInt64(3), reader.GetDateTime(4).ToUniversalTime());
    }

    /// <summary>
    /// 轮询等待指定实例行出现。
    /// </summary>
    /// <remarks>
    /// Polls until the row appears.
    /// </remarks>
    private static async Task<HeartbeatSnapshot> WaitForRowAsync(NpgsqlDataSource dataSource, string instanceId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        HeartbeatSnapshot row = null;
        while (row == null && DateTime.UtcNow < deadline)
        {
            row = await ReadRowAsync(dataSource, instanceId);
            if (row == null)
            {
                await Task.Delay(100);
            }
        }

        return row;
    }

    /// <summary>
    /// 轮询等待指定实例行达到期望状态。
    /// </summary>
    /// <remarks>
    /// Polls until the row reaches the expected status.
    /// </remarks>
    private static async Task<HeartbeatSnapshot> WaitForStatusAsync(NpgsqlDataSource dataSource, string instanceId, string status)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            var row = await ReadRowAsync(dataSource, instanceId);
            if (row != null && row.Status == status)
            {
                return row;
            }

            await Task.Delay(100);
        }

        return await ReadRowAsync(dataSource, instanceId);
    }

    /// <summary>
    /// 心跳行快照（测试断言投影）。
    /// </summary>
    /// <remarks>
    /// The heartbeat row snapshot (the assertion projection).
    /// </remarks>
    private sealed record HeartbeatSnapshot(string Status, string Role, string AdvertiseEndpoint, long Incarnation, DateTime LastHeartbeat);

    /// <summary>
    /// 执行无参 SQL。
    /// </summary>
    /// <remarks>
    /// Executes a parameter-less statement.
    /// </remarks>
    private static async Task ExecuteAsync(NpgsqlDataSource dataSource, string sql)
    {
        await using var command = dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// 执行标量 SQL。
    /// </summary>
    /// <remarks>
    /// Executes a scalar statement.
    /// </remarks>
    private static async Task<object> ExecuteScalarAsync(NpgsqlDataSource dataSource, string sql)
    {
        await using var command = dataSource.CreateCommand(sql);
        return await command.ExecuteScalarAsync();
    }

    /// <summary>
    /// 轮询断言直到条件成立或超时（含异步条件重载）。
    /// </summary>
    /// <remarks>
    /// Polls until the condition holds or the timeout elapses (async-condition overload).
    /// </remarks>
    private static async Task WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(100);
        }

        Assert.True(await condition(), "The expected condition was not met within the timeout.");
    }

    /// <summary>
    /// 轮询断言直到条件成立或超时。
    /// </summary>
    /// <remarks>
    /// Polls until the condition holds or the timeout elapses.
    /// </remarks>
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
    /// <remarks>
    /// The recording event subscriber.
    /// </remarks>
    private sealed class RecordingInstanceEvents : IRoleInstanceEvents
    {
        public List<(RoleInstanceChangeKind Kind, string InstanceId)> Observed { get; } = new List<(RoleInstanceChangeKind, string InstanceId)>();

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
    /// <remarks>
    /// Drops the isolated control database.
    /// </remarks>
    public void Dispose()
    {
        if (_testDatabase != null)
        {
            _testDatabase.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}
