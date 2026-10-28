// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相关法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   The full license text is available at the LICENSE file in the source root.
//   禁止利用本项目实施危害国家安全、破坏社会秩序、侵犯他人合法权益等法律法规所禁止的行为！
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   or infringe the lawful rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷及责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:   https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:     https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.DataBase;
using GameFrameX.DataBase.Abstractions;
using GameFrameX.DataBase.PostgreSql.Discovery;
using GameFrameX.Discovery;
using GameFrameX.Tests.DataBase;
using GameFrameX.Utility.Setting;
using Npgsql;
using Xunit;

namespace GameFrameX.Tests.Discovery;

/// <summary>
/// PostgreSqlDiscoveryRuntime 激活时序测试（回归：空库 ActivateAsync 不得因缺表抛错）。
/// </summary>
/// <remarks>
/// Activation-ordering tests for <c>PostgreSqlDiscoveryRuntime</c> (regression:
/// activating against a FRESH control database must not throw — unlike MongoDB, PostgreSQL does not
/// lazily create missing relations, so the schema must be ensured before the watcher's immediate
/// first poll). The control database is registered through the unified
/// <c>GameDb.Init(DbOptions)</c> entry and activated by <c>ConnectionName</c> (the
/// <c>NpgsqlDataSource</c> carrier direct-injection path was removed), so these tests exercise
/// the same path as production. Gated by GAMEFRAMEX_TEST_POSTGRESQL_CONNECTION_STRING
/// (silent skip when unset, same convention as the other PostgreSQL suites).
/// </remarks>
// 经 GameDb.Init 注册会突变 MultiDbRegistry 全局静态态，与 GameDbMultiDatabaseTests /
// MultiDbRegistryTests 等同类测试存在 xUnit 跨类并行竞争，固定进同一禁并行 Collection。
[Collection(nameof(GameDbStaticStateCollection))]
public sealed class PostgreSqlDiscoveryRuntimeTests
{
    private readonly string _connectionString = Environment.GetEnvironmentVariable("GAMEFRAMEX_TEST_POSTGRESQL_CONNECTION_STRING") ?? string.Empty;

    /// <summary>
    /// 是否跳过全部用例。
    /// </summary>
    /// <remarks>
    /// Whether all tests are skipped.
    /// </remarks>
    private bool ShouldSkip => string.IsNullOrWhiteSpace(_connectionString);

    /// <summary>
    /// 全新控制库上 ActivateAsync：建表先于读侧立即轮询 / TTL 清理，不得抛 42P01。
    /// </summary>
    /// <remarks>
    /// ActivateAsync on a brand-new control database: schema creation must precede the read side's
    /// immediate first poll and the TTL cleanup job, so no 42P01 (undefined_table) may escape.
    /// </remarks>
    [Fact]
    public async Task ActivateAsync_OnFreshDatabase_ShouldNotThrow()
    {
        if (ShouldSkip)
        {
            return;
        }

        PostgreSqlDiscoveryRuntime.ResetForTest();
        GameDb.ResetForTesting();
        await using (var testDatabase = await PostgreSqlTestDatabase.CreateAsync(_connectionString))
        {
            // 与生产一致的真实注册路径：测试库经统一入口 GameDb.Init 注册（Provider 反射解析 +
            // PostgreSqlDbService.Open 自建连接池），再按 ConnectionName 激活。
            var opened = await GameDb.InitAsync(new DbOptions
            {
                Provider = DatabaseProviderType.PostgreSql,
                ConnectionString = testDatabase.ConnectionString,
                Name = GameDb.ControlDatabaseName,
                IsDefault = false,
            });
            Assert.True(opened);

            var options = new DiscoveryActivationOptions
            {
                ConnectionName = GameDb.ControlDatabaseName,
                HostedRoleNames = new List<string> { "Game" },
            };

            var exception = await Record.ExceptionAsync(() => PostgreSqlDiscoveryRuntime.ActivateAsync(options));

            Assert.Null(exception);
            Assert.NotNull(PostgreSqlDiscoveryRuntime.TableProvider);

            // 成对槽位：ActivateAsync 末尾 BindRouteTable 后，公共读侧槽位非空且与 TableProvider 同一实例。
            Assert.NotNull(ActiveDiscoveryRuntime.RouteTableProvider);
            Assert.Same(PostgreSqlDiscoveryRuntime.TableProvider, ActiveDiscoveryRuntime.RouteTableProvider);

            // player_route 由 Bootstrap.Attach 在 ActivateAsync 内建好（TTL 清理 job 启动前）。
            await using (var connection = await testDatabase.DataSource.OpenConnectionAsync())
            {
                await using (var command = new NpgsqlCommand("SELECT to_regclass('public.player_route')::text", connection))
                {
                    var playerRoute = await command.ExecuteScalarAsync();
                    Assert.NotNull(playerRoute);
                }
            }

            // 停机顺序：先停发现层（读/写侧，尽力写 Stopped 终态）→ 再清 GameDb 注册表；
            // 随后的 DisposeAsync 以 DROP ... WITH (FORCE) 兜底断开 PostgreSqlDbService 自建连接池的服务侧连接。
            PostgreSqlDiscoveryRuntime.ResetForTest();
            GameDb.ResetForTesting();
        }
    }

    /// <summary>
    /// 部分建库场景收敛：脚本首表存在、次表缺失时重跑 EnsureSchema 应补建缺失表（回归：整批脚本首语句即
    /// 42P07 中止且被吞，后续建表/建索引静默漏建，存量库心跳 42P01）。
    /// </summary>
    /// <remarks>
    /// Convergence on a partially provisioned control database: with the script's first table
    /// (player_route) present but server_heartbeat dropped, re-running EnsureSchema must recreate the missing
    /// table. The EF create script orders player_route first, so the former single-batch execution aborted on
    /// its very first statement (42P07) and swallowed the error — every later statement, including the
    /// missing heartbeat table, was silently skipped.
    /// </remarks>
    [Fact]
    public async Task EnsureSchema_OnPartiallyProvisionedDatabase_ShouldRecreateMissingTables()
    {
        if (ShouldSkip)
        {
            return;
        }

        await using (var testDatabase = await PostgreSqlTestDatabase.CreateAsync(_connectionString))
        {
            var store = new PostgreSqlHeartbeatStore(testDatabase.DataSource);
            await store.EnsureSchemaAsync();

            // 制造「部分建库」：仅删 server_heartbeat（脚本首表 player_route 保留），复现存量库部分建表场景。
            // Fabricate the partial schema: drop only server_heartbeat (the script's first table player_route
            // stays), mirroring a control database provisioned halfway.
            await using (var connection = await testDatabase.DataSource.OpenConnectionAsync())
            {
                await using (var dropCommand = new NpgsqlCommand("DROP TABLE server_heartbeat", connection))
                {
                    await dropCommand.ExecuteNonQueryAsync();
                }
            }

            await store.EnsureSchemaAsync();

            await using (var connection = await testDatabase.DataSource.OpenConnectionAsync())
            {
                await using (var command = new NpgsqlCommand("SELECT to_regclass('public.server_heartbeat')::text", connection))
                {
                    var serverHeartbeat = await command.ExecuteScalarAsync() as string;
                    Assert.Equal("server_heartbeat", serverHeartbeat);
                }
            }
        }
    }

    /// <summary>
    /// 重复调用 ActivateAsync：第二次为 no-op——守卫短路，不重建 watcher/registry（TableProvider 引用不变）。
    /// </summary>
    /// <remarks>
    /// Repeated ActivateAsync calls: the second is a no-op — the one-shot guard short-circuits and does not
    /// rebuild the watcher/registry (TableProvider keeps the SAME instance). Reference equality is the
    /// discriminator: a full re-execution would reassign _watcher and expose a new provider instance.
    /// </remarks>
    [Fact]
    public async Task ActivateAsync_Twice_SecondCallIsNoOp()
    {
        if (ShouldSkip)
        {
            return;
        }

        PostgreSqlDiscoveryRuntime.ResetForTest();
        GameDb.ResetForTesting();
        await using (var testDatabase = await PostgreSqlTestDatabase.CreateAsync(_connectionString))
        {
            var opened = await GameDb.InitAsync(new DbOptions
            {
                Provider = DatabaseProviderType.PostgreSql,
                ConnectionString = testDatabase.ConnectionString,
                Name = GameDb.ControlDatabaseName,
                IsDefault = false,
            });
            Assert.True(opened);

            var options = new DiscoveryActivationOptions
            {
                ConnectionName = GameDb.ControlDatabaseName,
                HostedRoleNames = new List<string> { "Game" },
            };

            await PostgreSqlDiscoveryRuntime.ActivateAsync(options);
            var providerAfterFirst = PostgreSqlDiscoveryRuntime.TableProvider;
            Assert.NotNull(providerAfterFirst);

            await PostgreSqlDiscoveryRuntime.ActivateAsync(options);

            Assert.Same(providerAfterFirst, PostgreSqlDiscoveryRuntime.TableProvider);

            PostgreSqlDiscoveryRuntime.ResetForTest();
            GameDb.ResetForTesting();
        }
    }

    /// <summary>
    /// ActivateAsync 后经公共槽位 MarkActive：本实例 server_heartbeat 行由 Booting 翻转为 Active（宿主无分派就绪路径回归）。
    /// </summary>
    /// <remarks>
    /// After ActivateAsync, flipping through the shared <c>ActiveDiscoveryRuntime</c> slot — the
    /// exact call the hosts make at their readiness point — must turn this instance's
    /// heartbeat row from Booting to Active: the <c>Bind</c> performed at the end of
    /// ActivateAsync is what lets the provider-agnostic call reach the real registry.
    /// </remarks>
    [Fact]
    public async Task ActivateAsync_ThenMarkActiveThroughSharedSlot_FlipsHeartbeatToActive()
    {
        if (ShouldSkip)
        {
            return;
        }

        // 写侧需要广播身份：临时注入广播端口与稳定实例 ID（测试内恢复原值；进程内无其他用例读取这两个变量）。
        var previousPort = Environment.GetEnvironmentVariable(AdvertiseEndpointEnvironment.AdvertisePortEnvironmentVariable);
        var previousInstanceId = Environment.GetEnvironmentVariable(AdvertiseEndpointEnvironment.RoleInstanceIdEnvironmentVariable);
        var instanceId = $"slot-mark-active-{Guid.NewGuid():N}";
        Environment.SetEnvironmentVariable(AdvertiseEndpointEnvironment.AdvertisePortEnvironmentVariable, "29100");
        Environment.SetEnvironmentVariable(AdvertiseEndpointEnvironment.RoleInstanceIdEnvironmentVariable, instanceId);
        try
        {
            PostgreSqlDiscoveryRuntime.ResetForTest();
            GameDb.ResetForTesting();
            await using (var testDatabase = await PostgreSqlTestDatabase.CreateAsync(_connectionString))
            {
                var opened = await GameDb.InitAsync(new DbOptions
                {
                    Provider = DatabaseProviderType.PostgreSql,
                    ConnectionString = testDatabase.ConnectionString,
                    Name = GameDb.ControlDatabaseName,
                    IsDefault = false,
                });
                Assert.True(opened);

                await PostgreSqlDiscoveryRuntime.ActivateAsync(new DiscoveryActivationOptions
                {
                    ConnectionName = GameDb.ControlDatabaseName,
                    HostedRoleNames = new List<string> { "Game" },
                });

                // 宿主就绪点的真实调用形态：无分派经公共槽位异步切换。
                await ActiveDiscoveryRuntime.MarkActiveAsync();

                var status = string.Empty;
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (DateTime.UtcNow < deadline && status != "Active")
                {
                    await using (var connection = await testDatabase.DataSource.OpenConnectionAsync())
                    {
                        await using (var command = new NpgsqlCommand("SELECT status FROM server_heartbeat WHERE instance_id = ($1)", connection))
                        {
                            command.Parameters.AddWithValue(instanceId);
                            status = await command.ExecuteScalarAsync() as string;
                            if (status != "Active")
                            {
                                await Task.Delay(100);
                            }
                        }
                    }
                }

                Assert.Equal("Active", status);

                PostgreSqlDiscoveryRuntime.ResetForTest();
                GameDb.ResetForTesting();
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(AdvertiseEndpointEnvironment.AdvertisePortEnvironmentVariable, previousPort);
            Environment.SetEnvironmentVariable(AdvertiseEndpointEnvironment.RoleInstanceIdEnvironmentVariable, previousInstanceId);
        }
    }

    /// <summary>
    /// 单线程 SynchronizationContext 宿主（Unity / WPF 主线程形态）下 ActivateAsync：装配 I/O 挂起期间上下文线程保持可用，释放阻塞后装配完成且槽位绑定。
    /// </summary>
    /// <remarks>
    /// Regression for the sync-over-async activation hazard. Activation is started on a dedicated
    /// single-threaded pumped context while an external transaction holds an ACCESS EXCLUSIVE lock
    /// on <c>server_heartbeat</c>, so the activation necessarily suspends on database I/O (the
    /// schema-ensuring ALTER blocks on the lock). While activation is pending, the context thread
    /// must stay free to run posted work — the former blocking implementation monopolized the
    /// context thread for the whole activation, so no posted item could ever run (and with a
    /// context-capturing continuation this is exactly the deadlock shape). After the lock is
    /// released the activation completes and the table provider slot is bound.
    /// </remarks>
    [Fact]
    public async Task ActivateAsync_OnSingleThreadSynchronizationContext_KeepsContextThreadResponsiveDuringActivationIo()
    {
        if (ShouldSkip)
        {
            return;
        }

        PostgreSqlDiscoveryRuntime.ResetForTest();
        GameDb.ResetForTesting();
        await using (var testDatabase = await PostgreSqlTestDatabase.CreateAsync(_connectionString))
        {
            var opened = await GameDb.InitAsync(new DbOptions
            {
                Provider = DatabaseProviderType.PostgreSql,
                ConnectionString = testDatabase.ConnectionString,
                Name = GameDb.ControlDatabaseName,
                IsDefault = false,
            });
            Assert.True(opened);

            // 前置建表：锁目标 server_heartbeat 必须已存在，激活的幂等补列 ALTER 才会确定阻塞在锁上。
            var heartbeatStore = new PostgreSqlHeartbeatStore(testDatabase.DataSource);
            await heartbeatStore.EnsureSchemaAsync();

            var activationStarted = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
            var contextWorkItemObservedPendingActivation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var options = new DiscoveryActivationOptions
            {
                ConnectionName = GameDb.ControlDatabaseName,
                HostedRoleNames = new List<string> { "Game" },
            };

            await using (var lockConnection = await testDatabase.DataSource.OpenConnectionAsync())
            {
                await using (var lockTransaction = await lockConnection.BeginTransactionAsync())
                await using (var lockCommand = lockConnection.CreateCommand())
                {
                    lockCommand.Transaction = (NpgsqlTransaction)lockTransaction;
                    lockCommand.CommandText = "LOCK TABLE server_heartbeat IN ACCESS EXCLUSIVE MODE";
                    await lockCommand.ExecuteNonQueryAsync();

                    using (var context = new SingleThreadedPumpSynchronizationContext())
                    {
                        // 先投递激活：在泵线程（上下文当前）发起，任务在首个真实异步点返还调用方。
                        context.Post(_ => activationStarted.TrySetResult(PostgreSqlDiscoveryRuntime.ActivateAsync(options)), null);
                        // 再投递观测项：FIFO 保证它排在激活之后执行——此时激活必须仍挂起在锁等待上，
                        // 而上下文线程已空闲可执行它（旧同步入口独占上下文线程，本工作项永不执行）。
                        context.Post(_ => contextWorkItemObservedPendingActivation.TrySetResult(!activationStarted.Task.Result.IsCompleted), null);

                        var observed = await contextWorkItemObservedPendingActivation.Task.WaitAsync(TimeSpan.FromSeconds(30));
                        Assert.True(observed, "装配 I/O 挂起期间上下文线程应能执行投递工作项（同步阻塞入口会独占上下文线程直至死锁）");
                    }

                    // 释放锁（回滚持锁事务）后装配应完成且读侧槽位绑定。
                    await lockTransaction.RollbackAsync();
                }
            }

            var activationTask = await activationStarted.Task;
            await activationTask.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.NotNull(PostgreSqlDiscoveryRuntime.TableProvider);
            Assert.Same(PostgreSqlDiscoveryRuntime.TableProvider, ActiveDiscoveryRuntime.RouteTableProvider);

            PostgreSqlDiscoveryRuntime.ResetForTest();
            GameDb.ResetForTesting();
        }
    }

    /// <summary>
    /// 单线程队列泵同步上下文（模拟 Unity / WPF 主线程：同一线程按投递顺序串行执行工作项）。
    /// </summary>
    /// <remarks>
    /// A minimal single-threaded pumped context mirroring UI main threads: posted work items run
    /// sequentially on one dedicated thread with this context current, so any continuation that
    /// captures the context must wait for that thread to pump — the deadlock precondition the
    /// activation path must never rely on.
    /// </remarks>
    private sealed class SingleThreadedPumpSynchronizationContext : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<KeyValuePair<SendOrPostCallback, object>> _queue = new();
        private readonly Thread _pumpThread;

        public SingleThreadedPumpSynchronizationContext()
        {
            _pumpThread = new Thread(PumpLoop) { IsBackground = true, Name = "discovery-activation-sync-context" };
            _pumpThread.Start();
        }

        public override void Post(SendOrPostCallback callback, object state)
        {
            _queue.Add(new KeyValuePair<SendOrPostCallback, object>(callback, state));
        }

        public void Dispose()
        {
            _queue.CompleteAdding();
            // 失败路径（如回归判别超时）泵线程可能仍阻塞在旧式同步等待上：限时 Join 后弃管，
            // 由持锁释放后的完成链自行收尾，避免测试清理阶段二次挂起。
            if (!_pumpThread.Join(TimeSpan.FromSeconds(10)))
            {
                return;
            }
            _queue.Dispose();
        }

        private void PumpLoop()
        {
            SynchronizationContext.SetSynchronizationContext(this);
            foreach (var workItem in _queue.GetConsumingEnumerable())
            {
                workItem.Key(workItem.Value);
            }
        }
    }
}
