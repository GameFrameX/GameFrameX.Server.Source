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
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using GameFrameX.DataBase.Abstractions;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Foundation.Logger;
using GameFrameX.Localization;
using Npgsql;
using System.Diagnostics;

namespace GameFrameX.DataBase.PostgreSql;

public sealed partial class PostgreSqlDbService
{
    /// <summary>
    /// 连接数据库（创建池化 NpgsqlDataSource 并探活）。
    /// </summary>
    /// <remarks>
    /// Connects to the database (creates a pooled <c>NpgsqlDataSource</c> and probes it).
    /// 与 Mongo 适配器语义对齐：同目标快路径复用探活；失败按 300/700/1500ms + 抖动重试；全部失败置
    /// Unhealthy 并启动后台恢复任务。数据库由连接串 <c>Database</c> 决定，<paramref name="dbOptions"/>.Name 仅作注册名。
    /// </remarks>
    /// <param name="dbOptions">数据库配置选项 / Database configuration options</param>
    /// <returns>返回数据库是否初始化成功 / Returns whether the database was initialized successfully</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="dbOptions"/>、其 ConnectionString 或 Name 为 null 时抛出 / Thrown when <paramref name="dbOptions"/>, its ConnectionString, or its Name is null</exception>
    public async Task<bool> Open(DbOptions dbOptions)
    {
        ArgumentNullException.ThrowIfNull(dbOptions, nameof(dbOptions));
        ArgumentNullException.ThrowIfNull(dbOptions.ConnectionString, nameof(dbOptions.ConnectionString));
        ArgumentNullException.ThrowIfNull(dbOptions.Name, nameof(dbOptions.Name));
        var connectionTarget = BuildConnectionTargetTag(dbOptions.ConnectionString, dbOptions.Name);
        var openStopwatch = Stopwatch.StartNew();

        if (DataSource != null && Options != null)
        {
            var isSameTarget = string.Equals(Options.ConnectionString, dbOptions.ConnectionString, StringComparison.Ordinal) &&
                               string.Equals(Options.Name, dbOptions.Name, StringComparison.Ordinal);
            if (isSameTarget)
            {
                try
                {
                    await using var connection = DataSource.CreateConnection();
                    await ExecutePingAsync(connection, CancellationToken.None).ConfigureAwait(false);
                    Options = dbOptions;
                    ApplyRuntimeOptions(dbOptions.RuntimeOptions);
                    return true;
                }
                catch
                {
                    ResetConnectionState();
                }
            }
            else
            {
                ResetConnectionState();
            }
        }

        Options = dbOptions;
        ApplyRuntimeOptions(dbOptions.RuntimeOptions);
        Exception lastException = null;
        var retryDelays = new[] { 300, 700, 1500, };
        for (var attempt = 0; attempt < retryDelays.Length; attempt++)
        {
            try
            {
                DataSource = CreateDataSource(Options.ConnectionString);
                await using var connection = DataSource.CreateConnection();
                await ExecutePingAsync(connection, CancellationToken.None).ConfigureAwait(false);
                lock (_availabilityLock)
                {
                    _consecutiveFailures = 0;
                    _consecutiveSuccesses = 0;
                    ChangeAvailabilityState(DatabaseAvailabilityState.Healthy, "open_success");
                }

                DbOperationLatencyMilliseconds.Record(openStopwatch.Elapsed.TotalMilliseconds, new TagList { { "op", "open" }, { "name", nameof(Open) }, { "success", true }, });
                LogHelper.Info("PostgreSqlDbService.Open {dbName} {target} {postgreSqlInitializedSuccessfully}", dbOptions.Name, connectionTarget, LocalizationService.GetString(Keys.Database.PostgreSqlInitializedSuccessfully, connectionTarget, dbOptions.Name));
                return true;
            }
            catch (Exception exception)
            {
                lastException = exception;
                ResetConnectionState();
                if (attempt < retryDelays.Length - 1)
                {
                    DbOpenRetryTotal.Add(1, new TagList { { "db.target", connectionTarget }, });
                    LogHelper.Warning("PostgreSqlDbService.Open Retry {attempt}/{maxRetry} {dbName} {target} {exception}", attempt + 1, retryDelays.Length, dbOptions.Name, connectionTarget, exception.Message);
                    var delay = retryDelays[attempt] + Random.Shared.Next(0, 200);
                    await Task.Delay(delay).ConfigureAwait(false);
                }
            }
        }

        DbOperationFailTotal.Add(1, new TagList { { "op", "open" }, { "name", nameof(Open) }, { "reason", GetFailureReason(lastException) }, });
        DbOperationLatencyMilliseconds.Record(openStopwatch.Elapsed.TotalMilliseconds, new TagList { { "op", "open" }, { "name", nameof(Open) }, { "success", false }, });
        LogHelper.Fatal("PostgreSqlDbService.Open Exception {dbName} {target} {exception}", dbOptions.Name, connectionTarget, lastException);
        var message = LocalizationService.GetString(Keys.Database.PostgreSqlInitializationFailed, connectionTarget, dbOptions.Name);
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(message);
        Console.ResetColor();
        LogHelper.Error("PostgreSqlDbService.Open Exception {dbName} {target} {message}", dbOptions.Name, connectionTarget, message);
        lock (_availabilityLock)
        {
            ChangeAvailabilityState(DatabaseAvailabilityState.Unhealthy, "open_failed");
            EnsureRecoveryTaskStarted();
        }

        return false;
    }

    /// <summary>
    /// 关闭 PostgreSQL 连接（释放数据源并停止恢复任务）。
    /// </summary>
    /// <remarks>
    /// Closes the PostgreSQL connection (disposes the data source and stops the recovery task).
    /// </remarks>
    public async Task Close()
    {
        await StopRecoveryTaskAsync().ConfigureAwait(false);
        ResetConnectionState();
        lock (_availabilityLock)
        {
            _consecutiveFailures = 0;
            _consecutiveSuccesses = 0;
            _recoveringProbeSuccessCount = 0;
            ChangeAvailabilityState(DatabaseAvailabilityState.Healthy, "close_reset");
        }
    }

    /// <summary>
    /// 创建池化数据源（应用连接与命令超时）。
    /// </summary>
    /// <remarks>
    /// Creates the pooled data source (applying connect and command timeouts).
    /// </remarks>
    /// <param name="connectionString">连接字符串 / Connection string</param>
    /// <returns>池化数据源 / The pooled data source</returns>
    private NpgsqlDataSource CreateDataSource(string connectionString)
    {
        var builder = new NpgsqlDataSourceBuilder(connectionString)
        {
            ConnectionStringBuilder =
            {
                Timeout = (int)_connectTimeout.TotalSeconds,
                CommandTimeout = (int)_socketTimeout.TotalSeconds,
            },
        };
        return builder.Build();
    }

    /// <summary>
    /// 在给定连接上执行探活命令。
    /// </summary>
    /// <remarks>
    /// Executes the liveness probe on the given connection.
    /// </remarks>
    private static async Task ExecutePingAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand("SELECT 1", connection);
        await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 重置连接状态并释放相关资源。
    /// </summary>
    /// <remarks>
    /// Resets the connection state and releases related resources.
    /// </remarks>
    private void ResetConnectionState()
    {
        DataSource?.Dispose();
        DataSource = null;
    }

    /// <summary>
    /// 确保数据库服务已初始化。
    /// </summary>
    /// <remarks>
    /// Ensures the database service has been initialized.
    /// </remarks>
    /// <exception cref="DatabaseUnavailableException">当服务未初始化时抛出 / Thrown when the service is not initialized</exception>
    private void EnsureInitialized()
    {
        if (DataSource == null)
        {
            // Localization: Database.PostgreSql.ServiceUnavailable - PostgreSqlDbService不可用，Open()尚未成功完成
            throw new DatabaseUnavailableException(LocalizationService.GetString(Keys.Database.PostgreSqlServiceUnavailable));
        }
    }
}