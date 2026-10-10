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
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:     https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using GameFrameX.DataBase.Abstractions;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Foundation.Logger;
using GameFrameX.Localization;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Diagnostics;

namespace GameFrameX.DataBase.PostgreSql;

public sealed partial class PostgreSqlDbService
{
    /// <summary>
    /// PostgreSQL 瞬态错误 SQLSTATE 分类（有界集合，对齐 Mongo Retryable* 错误标签语义）。
    /// </summary>
    /// <remarks>
    /// Bounded SQLSTATE classification for transient PostgreSQL errors (aligned with Mongo's retryable error labels):
    /// 40001 serialization_failure、40P01 deadlock_detected、53xxx 资源不足、55P03/55006 锁不可用、
    /// 57P01-57P04 管理干预、08xxx 连接异常。
    /// </remarks>
    private static readonly HashSet<string> TransientSqlStates = new(StringComparer.Ordinal)
    {
        "40001", // serialization_failure
        "40P01", // deadlock_detected
        "53000", // insufficient_resources
        "53100", // disk_full
        "53200", // out_of_memory
        "53300", // too_many_connections
        "53400", // configuration_limit_exceeded
        "55P03", // lock_not_available
        "55006", // object_in_use
        "57P01", // admin_shutdown
        "57P02", // crash_shutdown
        "57P03", // cannot_connect_now
        "57P04", // database_dropped
        "08000", // connection_exception
        "08001", // sqlclient_unable_to_establish_sqlconnection
        "08003", // connection_does_not_exist
        "08004", // sqlserver_rejected_establishment_of_sqlconnection
        "08006", // connection_failure
        "08007", // transaction_resolution_unknown
    };

    /// <summary>
    /// 执行带有自动重试机制的读取操作。
    /// </summary>
    /// <remarks>
    /// Executes a read operation with automatic retry on transient failures.
    /// </remarks>
    private async Task<T> ExecuteReadWithRetryAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken, string operationName)
    {
        return await ExecuteReadWithRetryAsync(operation, cancellationToken, operationName, null).ConfigureAwait(false);
    }

    /// <summary>
    /// 执行带有自动重试机制的读取操作（支持降级返回）。
    /// </summary>
    /// <remarks>
    /// Executes a read operation with automatic retry on transient failures,
    /// with an optional fallback value factory used when reads are degraded.
    /// </remarks>
    private async Task<T> ExecuteReadWithRetryAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken, string operationName, Func<T> fallbackValueFactory)
    {
        if (TryReturnDegradedReadFallback(operationName, fallbackValueFactory, out var degradedValue))
        {
            return degradedValue;
        }

        return await ExecuteWithRetryAsync(operation, cancellationToken, _readRetryDelaysMilliseconds, operationName, "read").ConfigureAwait(false);
    }

    /// <summary>
    /// 执行带有自动重试机制的写入操作。
    /// </summary>
    /// <remarks>
    /// Only idempotent operations are retried to avoid duplicate writes (aligned with the Mongo adapter).
    /// </remarks>
    private async Task<T> ExecuteWriteWithRetryAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken, string operationName, bool isIdempotent)
    {
        if (!IsCoreWriteAllowed(operationName))
        {
            throw new DatabaseUnavailableException(LocalizationService.GetString(Keys.Database.PostgreSqlServiceUnavailable));
        }

        if (!isIdempotent)
        {
            return await ExecuteWithRetryAsync(operation, cancellationToken, Array.Empty<int>(), operationName, "write").ConfigureAwait(false);
        }

        return await ExecuteWithRetryAsync(operation, cancellationToken, _idempotentWriteRetryDelaysMilliseconds, operationName, "write").ConfigureAwait(false);
    }

    /// <summary>
    /// 判断异常是否为可重试的事务异常（SQLSTATE 40001/40P01；沿异常链查找以覆盖 EF 包装）。
    /// </summary>
    /// <remarks>
    /// Determines whether the exception is a transient transaction error (SQLSTATE 40001/40P01),
    /// aligned with Mongo's <c>TransientTransactionError</c> label. The whole exception chain is
    /// inspected because EF can wrap the underlying <see cref="PostgresException"/>.
    /// </remarks>
    private static bool ShouldRetryTransactionException(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: "40001" or "40P01", })
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 判断异常是否为可重试的 PostgreSQL 异常。
    /// </summary>
    /// <remarks>
    /// Determines whether the exception is retryable: Npgsql transient flag, bounded transient SQLSTATE set,
    /// or timeout (aligned with the Mongo adapter's retryable classification). EF's <see cref="DbUpdateException"/>
    /// wrapper is unwrapped so transient inner Npgsql failures still classify (C168).
    /// </remarks>
    private static bool IsRetryablePostgreSqlException(Exception exception)
    {
        if (exception is TimeoutException)
        {
            return true;
        }

        if (exception is DbUpdateException { InnerException: not null, } updateException)
        {
            return IsRetryablePostgreSqlException(updateException.InnerException);
        }

        if (exception is PostgresException postgresException)
        {
            return TransientSqlStates.Contains(postgresException.SqlState);
        }

        if (exception is NpgsqlException npgsqlException)
        {
            return npgsqlException.IsTransient || IsRetryablePostgreSqlException(npgsqlException.InnerException);
        }

        return false;
    }

    /// <summary>
    /// 执行带有自定义重试延迟的通用重试机制。
    /// </summary>
    /// <exception cref="DatabaseUnavailableException">当所有重试都失败后抛出 / Thrown when all retry attempts fail</exception>
    private async Task<T> ExecuteWithRetryAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken, IReadOnlyList<int> retryDelaysMilliseconds, string operationName, string operationType)
    {
        ArgumentNullException.ThrowIfNull(operation);
        cancellationToken.ThrowIfCancellationRequested();
        var operationStopwatch = Stopwatch.StartNew();
        Exception lastException = null;
        for (var attempt = 0; attempt <= retryDelaysMilliseconds.Count; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (string.Equals(operationType, "write", StringComparison.Ordinal) && !IsCoreWriteAllowed(operationName))
                {
                    throw new DatabaseUnavailableException(LocalizationService.GetString(Keys.Database.PostgreSqlServiceUnavailable));
                }

                var result = await operation(cancellationToken).ConfigureAwait(false);
                RecordOperationSuccess(operationName, operationType);
                DbOperationLatencyMilliseconds.Record(operationStopwatch.Elapsed.TotalMilliseconds, new TagList { { "op", operationType }, { "name", operationName }, { "success", true }, });
                return result;
            }
            catch (Exception exception)
            {
                if (!IsRetryablePostgreSqlException(exception))
                {
                    throw;
                }

                lastException = exception;
                RecordRetryableFailure(operationName, operationType, exception);
                if (attempt < retryDelaysMilliseconds.Count)
                {
                    var delay = retryDelaysMilliseconds[attempt] + Random.Shared.Next(0, 100);
                    DbOperationRetryTotal.Add(1, new TagList { { "op", operationType }, { "name", operationName }, { "reason", GetFailureReason(exception) }, });
                    // Localization: Database.PostgreSql.OperationTransientError - PostgreSqlDbService.{0} 瞬时错误，重试 {1}/{2}。error={3}
                    LogHelper.Warning(LocalizationService.GetString(Localization.Keys.Database.PostgreSql.OperationTransientError, operationName, attempt + 1, retryDelaysMilliseconds.Count, exception.Message));
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                break;
            }
        }

        // Localization: Database.PostgreSql.OperationRetryFailed - PostgreSqlDbService.{0}重试失败，未知异常
        var finalException = new DatabaseUnavailableException(LocalizationService.GetString(Keys.Database.PostgreSqlOperationRetryFailed, operationName), lastException ?? new InvalidOperationException(LocalizationService.GetString(Keys.Database.PostgreSqlOperationRetryFailed, operationName)));
        DbOperationFailTotal.Add(1, new TagList { { "op", operationType }, { "name", operationName }, { "reason", GetFailureReason(lastException) }, });
        DbOperationLatencyMilliseconds.Record(operationStopwatch.Elapsed.TotalMilliseconds, new TagList { { "op", operationType }, { "name", operationName }, { "success", false }, });
        throw finalException;
    }

    /// <summary>
    /// 获取失败原因标签。
    /// </summary>
    /// <remarks>
    /// Gets the failure-reason metric tag for the given exception.
    /// </remarks>
    private static string GetFailureReason(Exception exception)
    {
        return exception switch
        {
            null                                                             => "unknown",
            TimeoutException                                                 => "timeout",
            PostgresException postgresException                              => $"postgres_{postgresException.SqlState}",
            NpgsqlException npgsqlException when npgsqlException.IsTransient => "connection",
            NpgsqlException                                                  => "npgsql_exception",
            DatabaseUnavailableException                                     => "database_unavailable",
            _                                                                => "unknown",
        };
    }
}
