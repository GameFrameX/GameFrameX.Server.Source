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
using System.Collections.Generic;
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
/// PostgreSqlDiscoveryRuntime 激活时序测试（回归：空库 Activate 不得因缺表抛错）。
/// </summary>
/// <remarks>
/// Activation-ordering tests for <c>PostgreSqlDiscoveryRuntime</c> (regression:
/// activating against a FRESH control database must not throw — unlike MongoDB, PostgreSQL does not
/// lazily create missing relations, so the schema must be ensured before the watcher's immediate
/// first poll). Since C185 the control database is registered through the unified
/// <c>GameDb.Init(DbOptions)</c> entry and activated by <c>ConnectionName</c> (the
/// <c>NpgsqlDataSource</c> carrier direct-injection path was removed), so these tests exercise
/// the same path as production. Gated by GAMEFRAMEX_TEST_POSTGRESQL_CONNECTION_STRING
/// (silent skip when unset, same convention as the other PostgreSQL suites).
/// </remarks>
// 经 GameDb.Init 注册会突变 MultiDbRegistry 全局静态态，与 GameDbMultiDatabaseTests /
// MultiDbRegistryTests 等同类测试存在 xUnit 跨类并行竞争，固定进同一禁并行 Collection（C185 隔离评估）。
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
    /// 全新控制库上 Activate：建表先于读侧立即轮询 / TTL 清理，不得抛 42P01。
    /// </summary>
    /// <remarks>
    /// Activate on a brand-new control database: schema creation must precede the read side's
    /// immediate first poll and the TTL cleanup job, so no 42P01 (undefined_table) may escape.
    /// </remarks>
    [Fact]
    public async Task Activate_OnFreshDatabase_ShouldNotThrow()
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
            // PostgreSqlDbService.Open 自建连接池），再按 ConnectionName 激活（C185）。
            var opened = await GameDb.Init(new DbOptions
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

            var exception = await Record.ExceptionAsync(() => Task.Run(() => PostgreSqlDiscoveryRuntime.Activate(options)));

            Assert.Null(exception);
            Assert.NotNull(PostgreSqlDiscoveryRuntime.TableProvider);

            // player_route 由 Bootstrap.Attach 在 Activate 内建好（TTL 清理 job 启动前）。
            await using (var connection = await testDatabase.DataSource.OpenConnectionAsync())
            {
                await using var command = new NpgsqlCommand("SELECT to_regclass('public.player_route')::text", connection);
                var playerRoute = await command.ExecuteScalarAsync();
                Assert.NotNull(playerRoute);
            }

            // 停机顺序：先停发现层（读/写侧，尽力写 Stopped 终态）→ 再清 GameDb 注册表；
            // 随后的 DisposeAsync 以 DROP ... WITH (FORCE) 兜底断开 PostgreSqlDbService 自建连接池的服务侧连接。
            PostgreSqlDiscoveryRuntime.ResetForTest();
            GameDb.ResetForTesting();
        }
    }

    /// <summary>
    /// 重复调用 Activate：第二次为 no-op——守卫短路，不重建 watcher/registry（TableProvider 引用不变）。
    /// </summary>
    /// <remarks>
    /// Repeated Activate calls: the second is a no-op — the one-shot guard short-circuits and does not
    /// rebuild the watcher/registry (TableProvider keeps the SAME instance). Reference equality is the
    /// discriminator: a full re-execution would reassign _watcher and expose a new provider instance.
    /// </remarks>
    [Fact]
    public async Task Activate_Twice_SecondCallIsNoOp()
    {
        if (ShouldSkip)
        {
            return;
        }

        PostgreSqlDiscoveryRuntime.ResetForTest();
        GameDb.ResetForTesting();
        await using (var testDatabase = await PostgreSqlTestDatabase.CreateAsync(_connectionString))
        {
            var opened = await GameDb.Init(new DbOptions
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

            PostgreSqlDiscoveryRuntime.Activate(options);
            var providerAfterFirst = PostgreSqlDiscoveryRuntime.TableProvider;
            Assert.NotNull(providerAfterFirst);

            PostgreSqlDiscoveryRuntime.Activate(options);

            Assert.Same(providerAfterFirst, PostgreSqlDiscoveryRuntime.TableProvider);

            PostgreSqlDiscoveryRuntime.ResetForTest();
            GameDb.ResetForTesting();
        }
    }
}
