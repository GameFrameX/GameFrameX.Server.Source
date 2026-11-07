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
//   please refer to the LICENSE file at the root directory of the source code for the full license text.
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


using GameFrameX.DataBase.Abstractions;
using Microsoft.Data.Sqlite;

namespace GameFrameX.DataBase.Sqlite;

/// <summary>
/// SQLite 异常分类器：busy/locked 写冲突与超时的瞬时分类——provider 唯一差异面，全套弹性随共享引擎首次生效。
/// </summary>
/// <remarks>
/// SQLite exception classifier: the provider-specific surface consumed by the shared
/// <c>DbResilienceExecutor</c>. SQLite write contention surfaces as <c>SQLITE_BUSY (5)</c> /
/// <c>SQLITE_LOCKED (6)</c> result codes (multi-process file access; the in-process single
/// connection is serialized by the gate, so self-inflicted busy is not expected).
/// 无状态、线程安全；分类集合有界（busy/locked + 超时，对齐 PG TransientSqlStates 有界哲学）。
/// 事务级谓词为 busy/locked 窄集（不含超时，对齐 Mongo 事务错误标签窄集 / PG 40001+40P01 窄集），
/// 供事务内 CAS 原语消费；<c>ExecuteInTransactionAsync</c> 保持单次直通
/// （嵌套壳加入环境事务，盲目重试会在外层事务内重复执行 action）。
/// </remarks>
internal sealed class SqliteDbExceptionClassifier : IDbOperationExceptionClassifier
{
    /// <summary>
    /// SQLITE_BUSY 结果码（数据库文件被另一连接锁住）。
    /// </summary>
    private const int SqliteBusyResultCode = 5;

    /// <summary>
    /// SQLITE_LOCKED 结果码（表被另一连接锁定，多写者共享缓存场景）。
    /// </summary>
    private const int SqliteLockedResultCode = 6;

    /// <summary>
    /// 语句级判定：超时或 SQLite busy/locked 写冲突结果码视为可重试瞬时错误（连接闸等待超时除外——多为不可重入死锁，重试只会成倍延长挂起）。
    /// </summary>
    /// <remarks>
    /// Determines whether the exception is a retryable transient error: timeout, or a SQLite
    /// busy/locked write conflict (the bounded transient set for the embedded engine). A
    /// <see cref="SqliteGateWaitTimeoutException"/> is excluded: a gate timeout almost always means a
    /// non-reentrant deadlock inside a transaction-shell action, and retrying would multiply the hang.
    /// </remarks>
    /// <param name="exception">要检查的异常 / The exception to check</param>
    /// <returns>如果是可重试的异常则返回 <c>true</c>；否则返回 <c>false</c> / <c>true</c> if the exception is retryable; otherwise <c>false</c></returns>
    public bool IsRetryable(Exception exception)
    {
        if (exception is SqliteGateWaitTimeoutException)
        {
            return false;
        }

        if (exception is TimeoutException)
        {
            return true;
        }

        return exception is SqliteException { SqliteErrorCode: SqliteBusyResultCode or SqliteLockedResultCode, };
    }

    /// <summary>
    /// 事务级判定：SQLite busy/locked 窄集（超时不纳入，对齐 Mongo/PG 事务窄集哲学）。
    /// </summary>
    /// <remarks>
    /// Determines whether the exception is a transient transaction error (busy/locked only —
    /// intentionally narrower than <see cref="IsRetryable"/>, aligned with the Mongo error-label
    /// and PostgreSQL 40001/40P01 narrow sets).
    /// </remarks>
    /// <param name="exception">要检查的异常 / The exception to check</param>
    /// <returns>如果是可重试的事务异常则返回 <c>true</c>；否则返回 <c>false</c> / <c>true</c> if the exception is retryable; otherwise <c>false</c></returns>
    public bool ShouldRetryTransaction(Exception exception)
    {
        return exception is SqliteException { SqliteErrorCode: SqliteBusyResultCode or SqliteLockedResultCode, };
    }

    /// <summary>
    /// 事务级提交判定：与 <see cref="ShouldRetryTransaction"/> 同集（SQLite 事务壳 BEGIN → action → COMMIT 单阶段直通，无独立提交重试环）。
    /// </summary>
    /// <remarks>
    /// The SQLite transaction shell (BEGIN → action → COMMIT) runs single-pass with no separate
    /// commit-retry loop, so the commit predicate shares the busy/locked narrow set (a contract
    /// member consumed by transaction-level retry call sites).
    /// </remarks>
    /// <param name="exception">要检查的异常 / The exception to check</param>
    /// <returns>如果是可重试的事务提交异常则返回 <c>true</c>；否则返回 <c>false</c> / <c>true</c> if the commit is retryable; otherwise <c>false</c></returns>
    public bool ShouldRetryTransactionCommit(Exception exception)
    {
        return ShouldRetryTransaction(exception);
    }

    /// <summary>
    /// 获取失败原因标签（timeout/busy/locked/sqlite_{code}/... 有界词表）。
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
            SqliteException { SqliteErrorCode: SqliteBusyResultCode, } => "busy",
            SqliteException { SqliteErrorCode: SqliteLockedResultCode, } => "locked",
            SqliteException sqliteException => $"sqlite_{sqliteException.SqliteErrorCode}",
            DatabaseUnavailableException => "database_unavailable",
            _ => "unknown",
        };
    }
}
