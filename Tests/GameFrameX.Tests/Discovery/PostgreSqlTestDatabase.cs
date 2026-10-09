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
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using Npgsql;

namespace GameFrameX.Tests.Discovery;

/// <summary>
/// PostgreSQL 集成测试的独立库句柄（每测试类独立 database，Dispose 强制 DROP）。
/// </summary>
/// <remarks>
/// The isolated-database handle for the PostgreSQL integration tests:
/// each test class gets its own database (CREATE DATABASE gameframex_pg_test_{Guid})
/// so parallel runs never cross-contaminate; Dispose drops it with WITH (FORCE)
/// through a maintenance connection pinned to the <c>postgres</c> database. Gated
/// by GAMEFRAMEX_TEST_POSTGRESQL_CONNECTION_STRING (tests skip when unset, same
/// convention as the Mongo suites).
/// </remarks>
internal sealed class PostgreSqlTestDatabase : IAsyncDisposable
{
    /// <summary>
    /// 维护连接串（postgres 库）。
    /// </summary>
    /// <remarks>
    /// The maintenance connection string (pinned to the postgres database).
    /// </remarks>
    private readonly string _maintenanceConnectionString;

    /// <summary>
    /// 测试库名。
    /// </summary>
    /// <remarks>
    /// The test database name.
    /// </remarks>
    private readonly string _databaseName;

    private PostgreSqlTestDatabase(string maintenanceConnectionString, string databaseName, string connectionString, NpgsqlDataSource dataSource)
    {
        _maintenanceConnectionString = maintenanceConnectionString;
        _databaseName = databaseName;
        ConnectionString = connectionString;
        DataSource = dataSource;
    }

    /// <summary>
    /// 测试库连接串（Database 已指向测试库名，供 <c>DbOptions.ConnectionString</c> 使用）。
    /// </summary>
    /// <remarks>
    /// The test-database connection string (Database pinned to the test database
    /// name), consumed by <c>DbOptions.ConnectionString</c> when a test registers
    /// the test database through the unified <c>GameDb.Init</c> entry (C185).
    /// </remarks>
    public string ConnectionString { get; }

    /// <summary>
    /// 测试库数据源。
    /// </summary>
    /// <remarks>
    /// The test-database data source.
    /// </remarks>
    public NpgsqlDataSource DataSource { get; }

    /// <summary>
    /// 创建独立测试库。
    /// </summary>
    /// <remarks>
    /// Creates an isolated test database (maintenance connection to postgres, then CREATE DATABASE).
    /// </remarks>
    /// <param name="connectionString">门控连接串（任意库）/ The gating connection string (any database)</param>
    /// <returns>库句柄 / The handle</returns>
    public static async Task<PostgreSqlTestDatabase> CreateAsync(string connectionString)
    {
        var databaseName = $"gameframex_pg_test_{Guid.NewGuid():N}";
        var maintenanceBuilder = new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres" };
        var maintenanceConnectionString = maintenanceBuilder.ConnectionString;

        await using (var maintenanceConnection = new NpgsqlConnection(maintenanceConnectionString))
        {
            await maintenanceConnection.OpenAsync();
            await using var createCommand = maintenanceConnection.CreateCommand();
            createCommand.CommandText = $"CREATE DATABASE {databaseName};";
            await createCommand.ExecuteNonQueryAsync();
        }

        var testBuilder = new NpgsqlConnectionStringBuilder(connectionString) { Database = databaseName };
        var testConnectionString = testBuilder.ConnectionString;
        return new PostgreSqlTestDatabase(maintenanceConnectionString, databaseName, testConnectionString, NpgsqlDataSource.Create(testConnectionString));
    }

    /// <summary>
    /// 强制删除测试库并释放数据源。
    /// </summary>
    /// <remarks>
    /// Drops the test database (WITH (FORCE), terminating leftover connections) and disposes the data source.
    /// </remarks>
    /// <returns>异步任务 / Async task</returns>
    public async ValueTask DisposeAsync()
    {
        await DataSource.DisposeAsync();
        try
        {
            await using var maintenanceConnection = new NpgsqlConnection(_maintenanceConnectionString);
            await maintenanceConnection.OpenAsync();
            await using var dropCommand = maintenanceConnection.CreateCommand();
            dropCommand.CommandText = $"DROP DATABASE {_databaseName} WITH (FORCE);";
            await dropCommand.ExecuteNonQueryAsync();
        }
        catch (Exception)
        {
            // 清理失败不影响测试结果。
        }
    }
}
