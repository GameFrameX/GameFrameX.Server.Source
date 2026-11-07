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


namespace GameFrameX.DataBase.Abstractions;

/// <summary>
/// Provider 异常分类器契约——语句级与事务级两级重试判定的唯一 provider 差异面。
/// </summary>
/// <remarks>
/// Provider exception classifier contract: the single provider-specific surface of the shared
/// resilience engine. Statement-level classification (<see cref="IsRetryable"/>) drives the
/// engine's retry core loop; transaction-level classification
/// (<see cref="ShouldRetryTransaction"/> / <see cref="ShouldRetryTransactionCommit"/>) is
/// consumed by provider transaction retry loops (the loops themselves stay provider-specific
/// because transaction begin/commit APIs differ per driver).
/// 实现必须无状态且线程安全（引擎与事务重试环并发调用）；
/// 分类集合须有界并保持窄集哲学——仅收敛「确定可安全重试」的瞬时类别。
/// </remarks>
public interface IDbOperationExceptionClassifier
{
    /// <summary>
    /// 语句级判定：异常是否为可重试的瞬时错误（连接类/超时类/provider 有界瞬时码集）。
    /// </summary>
    /// <remarks>
    /// Statement-level classification: whether the exception is a retryable transient error.
    /// The engine core loop consults this on every failure; non-retryable exceptions are
    /// rethrown immediately without touching the availability state machine.
    /// </remarks>
    /// <param name="exception">待分类异常 / The exception to classify</param>
    /// <returns>可重试返回 true / true when the exception is retryable</returns>
    bool IsRetryable(Exception exception);

    /// <summary>
    /// 事务级判定：事务体内（含提交点）抛出的异常是否应重试整个事务。
    /// </summary>
    /// <remarks>
    /// Transaction-level classification: whether a failure inside a transaction body (commit
    /// point included) is transient enough to retry the whole transaction. Intentionally a
    /// narrower set than <see cref="IsRetryable"/> (Mongo transient-transaction error labels /
    /// PostgreSQL 40001+40P01 / SQLite busy+locked).
    /// </remarks>
    /// <param name="exception">待分类异常 / The exception to classify</param>
    /// <returns>可重试整个事务返回 true / true when the transaction may be retried</returns>
    bool ShouldRetryTransaction(Exception exception);

    /// <summary>
    /// 事务级判定：提交结果未知（是否已提交不明）时是否可安全重试提交/整个事务。
    /// </summary>
    /// <remarks>
    /// Transaction-level classification: whether an unknown commit result may be safely
    /// retried (Mongo <c>UnknownTransactionCommitResult</c> label). Providers whose retry loop
    /// does not distinguish the commit phase delegate to <see cref="ShouldRetryTransaction"/>.
    /// </remarks>
    /// <param name="exception">待分类异常 / The exception to classify</param>
    /// <returns>可重试提交返回 true / true when the commit may be retried</returns>
    bool ShouldRetryTransactionCommit(Exception exception);

    /// <summary>
    /// 获取失败原因指标标签（<c>reason</c> tag：timeout/connection/busy/... 的 provider 归一化短码）。
    /// </summary>
    /// <remarks>
    /// Normalized failure-reason tag for the retry/fail metric dimensions and availability
    /// transition audit reasons. Bounded vocabulary per provider; <c>null</c> maps to
    /// <c>unknown</c>.
    /// </remarks>
    /// <param name="exception">异常对象（可为 null）/ The exception object (may be null)</param>
    /// <returns>失败原因标签 / The failure reason tag</returns>
    string GetFailureReason(Exception exception);
}
