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
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GameFrameX.DataBase.PostgreSql;

/// <summary>
/// PostgreSQL 异常分类器：语句级与事务级重试判定 + 失败原因标签的 provider 唯一差异面。
/// </summary>
/// <remarks>
/// PostgreSQL exception classifier: the provider-specific surface consumed by the shared
/// <c>DbResilienceExecutor</c> (statement-level retry classification) and the transaction retry
/// loop in <c>PostgreSqlDbService.UpsertTransaction.cs</c> (transaction-level classification).
/// 无状态、线程安全；分类集合有界（瞬时 SQLSTATE 有界集 + Npgsql 瞬时标志）。
/// </remarks>
internal sealed class PostgreSqlDbExceptionClassifier : IDbOperationExceptionClassifier
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
    /// 语句级判定：Npgsql 瞬时标志、有界瞬时 SQLSTATE 集或超时（EF 包装逐层解开）。
    /// </summary>
    /// <remarks>
    /// Determines whether the exception is retryable: Npgsql transient flag, bounded transient
    /// SQLSTATE set, or timeout (aligned with the Mongo adapter's retryable classification).
    /// EF's <see cref="DbUpdateException"/> wrapper is unwrapped so transient inner Npgsql
    /// failures still classify.
    /// </remarks>
    /// <param name="exception">要检查的异常 / The exception to check</param>
    /// <returns>如果是可重试的异常则返回 <c>true</c>；否则返回 <c>false</c> / <c>true</c> if the exception is retryable; otherwise <c>false</c></returns>
    public bool IsRetryable(Exception exception)
    {
        if (exception is TimeoutException)
        {
            return true;
        }

        if (exception is DbUpdateException { InnerException: not null, } updateException)
        {
            return IsRetryable(updateException.InnerException);
        }

        if (exception is PostgresException postgresException)
        {
            return TransientSqlStates.Contains(postgresException.SqlState);
        }

        if (exception is NpgsqlException npgsqlException)
        {
            return npgsqlException.IsTransient || IsRetryable(npgsqlException.InnerException);
        }

        return false;
    }

    /// <summary>
    /// 事务级判定（SQLSTATE 40001/40P01；沿异常链查找以覆盖 EF 包装）。
    /// </summary>
    /// <remarks>
    /// Determines whether the exception is a transient transaction error (SQLSTATE 40001/40P01),
    /// aligned with Mongo's <c>TransientTransactionError</c> label. The whole exception chain is
    /// inspected because EF can wrap the underlying <see cref="PostgresException"/>.
    /// </remarks>
    /// <param name="exception">要检查的异常 / The exception to check</param>
    /// <returns>如果是可重试的事务异常则返回 <c>true</c>；否则返回 <c>false</c> / <c>true</c> if the exception is retryable; otherwise <c>false</c></returns>
    public bool ShouldRetryTransaction(Exception exception)
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
    /// 事务级提交判定：PG 事务重试环不区分提交阶段（单 catch 全程用 <see cref="ShouldRetryTransaction"/>），此处委托同一判定。
    /// </summary>
    /// <remarks>
    /// The PostgreSQL transaction retry loop does not distinguish the commit phase — a single
    /// catch applies <see cref="ShouldRetryTransaction"/> to every failure — so this predicate
    /// delegates to it (no separate commit-retry loop exists; a contract-completeness member with no existing call sites).
    /// </remarks>
    /// <param name="exception">要检查的异常 / The exception to check</param>
    /// <returns>如果是可重试的事务提交异常则返回 <c>true</c>；否则返回 <c>false</c> / <c>true</c> if the commit is retryable; otherwise <c>false</c></returns>
    public bool ShouldRetryTransactionCommit(Exception exception)
    {
        return ShouldRetryTransaction(exception);
    }

    /// <summary>
    /// 获取失败原因标签（timeout/connection/postgres_{SQLSTATE}/... 有界词表）。
    /// </summary>
    /// <remarks>
    /// Gets the failure-reason metric tag for the given exception.
    /// </remarks>
    /// <param name="exception">异常对象 / Exception object</param>
    /// <returns>失败原因标签 / Failure reason tag</returns>
    public string GetFailureReason(Exception exception)
    {
        return exception switch
        {
            null => "unknown",
            TimeoutException => "timeout",
            PostgresException postgresException => $"postgres_{postgresException.SqlState}",
            NpgsqlException npgsqlException when npgsqlException.IsTransient => "connection",
            NpgsqlException => "npgsql_exception",
            DatabaseUnavailableException => "database_unavailable",
            _ => "unknown",
        };
    }
}
