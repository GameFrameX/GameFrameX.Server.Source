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


using GameFrameX.DataBase.Abstractions;
using MongoDB.Driver;

namespace GameFrameX.DataBase.Mongo;

/// <summary>
/// MongoDB 异常分类器：语句级与事务级重试判定 + 失败原因标签的 provider 唯一差异面。
/// </summary>
/// <remarks>
/// MongoDB exception classifier: the provider-specific surface consumed by the shared
/// <c>DbResilienceExecutor</c> (statement-level retry classification) and the transaction retry
/// loops in <c>MongoDbService.UpsertTransaction.cs</c> (transaction-level classification).
/// 无状态、线程安全；分类集合有界（连接/超时/官方 Retryable* 错误标签 + 事务标签窄集）。
/// </remarks>
internal sealed class MongoDbExceptionClassifier : IDbOperationExceptionClassifier
{
    /// <summary>
    /// 语句级判定：连接错误、超时或官方 Retryable* 错误标签视为可重试瞬时错误。
    /// </summary>
    /// <remarks>
    /// Determines whether the exception is a retryable MongoDB exception such as connection
    /// error, timeout, or transient error.
    /// </remarks>
    /// <param name="exception">要检查的异常 / The exception to check</param>
    /// <returns>如果是可重试的异常则返回 <c>true</c>；否则返回 <c>false</c> / <c>true</c> if the exception is retryable; otherwise <c>false</c></returns>
    public bool IsRetryable(Exception exception)
    {
        if (exception is MongoConnectionException or TimeoutException or MongoExecutionTimeoutException)
        {
            return true;
        }

        if (exception is MongoException mongoException)
        {
            if (mongoException.HasErrorLabel("RetryableWriteError") || mongoException.HasErrorLabel("RetryableReadError"))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 事务级判定：官方 <c>TransientTransactionError</c> 错误标签。
    /// </summary>
    /// <remarks>
    /// Determines whether the exception is a transient transaction error that can be retried.
    /// </remarks>
    /// <param name="exception">要检查的异常 / The exception to check</param>
    /// <returns>如果是可重试的事务异常则返回 <c>true</c>；否则返回 <c>false</c> / <c>true</c> if the exception is retryable; otherwise <c>false</c></returns>
    public bool ShouldRetryTransaction(Exception exception)
    {
        return exception is MongoException mongoException && mongoException.HasErrorLabel("TransientTransactionError");
    }

    /// <summary>
    /// 事务级判定：官方 <c>UnknownTransactionCommitResult</c> 错误标签（提交结果未知，可安全重试提交）。
    /// </summary>
    /// <remarks>
    /// Determines whether the exception indicates an unknown transaction commit result that can be retried.
    /// </remarks>
    /// <param name="exception">要检查的异常 / The exception to check</param>
    /// <returns>如果是可重试的事务提交异常则返回 <c>true</c>；否则返回 <c>false</c> / <c>true</c> if the commit is retryable; otherwise <c>false</c></returns>
    public bool ShouldRetryTransactionCommit(Exception exception)
    {
        return exception is MongoException mongoException && mongoException.HasErrorLabel("UnknownTransactionCommitResult");
    }

    /// <summary>
    /// 获取失败原因标签（timeout/connection/retryable_write/... 有界词表）。
    /// </summary>
    /// <remarks>
    /// Gets a normalized failure reason tag.
    /// </remarks>
    /// <param name="exception">异常对象 / Exception object</param>
    /// <returns>失败原因标签 / Failure reason tag</returns>
    public string GetFailureReason(Exception exception)
    {
        return exception switch
        {
            null => "unknown",
            TimeoutException => "timeout",
            MongoConnectionException => "connection",
            MongoExecutionTimeoutException => "execution_timeout",
            MongoException mongoException when mongoException.HasErrorLabel("RetryableWriteError") => "retryable_write",
            MongoException mongoException when mongoException.HasErrorLabel("RetryableReadError") => "retryable_read",
            MongoException => "mongo_exception",
            DatabaseUnavailableException => "database_unavailable",
            _ => "unknown",
        };
    }
}
