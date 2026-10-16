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


using System.Diagnostics.Metrics;
using GameFrameX.DataBase.Abstractions;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Foundation.Logger;
using GameFrameX.Localization;

namespace GameFrameX.DataBase.Sqlite;

/// <summary>
/// 共享弹性引擎接入分部（C253）：SQLite 全套弹性（重试核心环 + 三指标）首次生效。
/// </summary>
/// <remarks>
/// Resilience wiring partial (C253): retry core loop and operation metrics become active for
/// the SQLite adapter via the shared <see cref="DbResilienceExecutor"/>. Availability remains
/// constant-healthy by the C222 boundary-④ ruling — the host is
/// <see cref="DbResilienceHosts.AlwaysHealthy"/>, so degraded reads and write gating never
/// trigger in production while engine behaviour stays identical under the contract matrix.
/// </remarks>
public sealed partial class SqliteDbService
{
    /// <summary>
    /// SQLite 操作指标 Meter（provider 维度 = Meter 名，与 Mongo/PG 同名仪表并列）。
    /// </summary>
    private static readonly Meter DbMeter = new("GameFrameX.DataBase.Sqlite", "1.0.0");

    /// <summary>
    /// 操作重试次数指标（与 Mongo/PG 同名同 tag 集）。
    /// </summary>
    private static readonly Counter<long> DbOperationRetryTotal = DbMeter.CreateCounter<long>("db_operation_retry_total");

    /// <summary>
    /// 操作最终失败次数指标（与 Mongo/PG 同名同 tag 集）。
    /// </summary>
    private static readonly Counter<long> DbOperationFailTotal = DbMeter.CreateCounter<long>("db_operation_fail_total");

    /// <summary>
    /// 操作耗时直方图（与 Mongo/PG 同名同 tag 集）。
    /// </summary>
    private static readonly Histogram<double> DbOperationLatencyMilliseconds = DbMeter.CreateHistogram<double>("db_operation_latency_ms", unit: "ms");

    /// <summary>
    /// 读取重试延迟基值（与 Mongo/PG 缺省对齐——契约矩阵「同一延迟序列」的配置前提）。
    /// </summary>
    private static readonly int[] ReadRetryDelaysMilliseconds = { 120, 300, 700 };

    /// <summary>
    /// 幂等写重试延迟基值（与 Mongo/PG 缺省对齐）。
    /// </summary>
    private static readonly int[] IdempotentWriteRetryDelaysMilliseconds = { 150, 400, 900 };

    /// <summary>
    /// Provider 异常分类器单例（busy/locked/timeout 瞬时分类，C253）。
    /// </summary>
    internal static readonly SqliteDbExceptionClassifier ExceptionClassifier = new();

    /// <summary>
    /// 共享弹性引擎（惰性构造；宿主为恒 Healthy——C222 边界④，降级/门控生产不触发）。
    /// </summary>
    private DbResilienceExecutor _resilienceExecutor;

    private DbResilienceExecutor ResilienceExecutor => _resilienceExecutor ??= new DbResilienceExecutor(new DbResilienceExecutorOptions
    {
        Classifier = ExceptionClassifier,
        Host = DbResilienceHosts.AlwaysHealthy,
        OperationRetryTotal = DbOperationRetryTotal,
        OperationFailTotal = DbOperationFailTotal,
        OperationLatencyMilliseconds = DbOperationLatencyMilliseconds,
        GetReadRetryDelaysMilliseconds = () => ReadRetryDelaysMilliseconds,
        GetIdempotentWriteRetryDelaysMilliseconds = () => IdempotentWriteRetryDelaysMilliseconds,
        // Localization: Database.Sqlite.ServiceUnavailable - SqliteDbService 尚未打开（请先调用 Open）。
        GetServiceUnavailableMessage = () => LocalizationService.GetString(Localization.Keys.Database.Sqlite.ServiceUnavailable),
        // Localization: Database.Sqlite.OperationTransientError - SqliteDbService.{0} 瞬时错误，重试 {1}/{2}。error={3}
        GetTransientErrorMessage = (operationName, attempt, retries, errorMessage) => LocalizationService.GetString(Localization.Keys.Database.Sqlite.OperationTransientError, operationName, attempt, retries, errorMessage),
        // Localization: Database.Sqlite.OperationRetryFailed - SqliteDbService.{0}重试失败，未知异常。
        GetRetryFailedMessage = operationName => LocalizationService.GetString(Localization.Keys.Database.Sqlite.OperationRetryFailed, operationName),
        LogWarning = message => LogHelper.Warning(message),
    });

    /// <summary>
    /// 执行带有自动重试机制的读取操作（busy/locked/timeout 瞬时错误按延迟序列重试）。
    /// </summary>
    /// <remarks>
    /// Executes a read operation with automatic retry on transient failures (C253).
    /// </remarks>
    /// <typeparam name="T">操作返回值类型 / The type of the operation result</typeparam>
    /// <param name="operation">要执行的读取操作 / The read operation to execute</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <param name="operationName">操作名称，用于日志记录 / Operation name for logging</param>
    /// <returns>操作结果 / The operation result</returns>
    private Task<T> ExecuteReadWithRetryAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken, string operationName)
    {
        return ResilienceExecutor.ExecuteReadWithRetryAsync(operation, cancellationToken, operationName);
    }

    /// <summary>
    /// 执行带有自动重试机制的读取操作（无返回值）。
    /// </summary>
    /// <remarks>
    /// Executes a read operation with automatic retry on transient failures (no return value).
    /// </remarks>
    /// <param name="operation">要执行的读取操作 / The read operation to execute</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <param name="operationName">操作名称，用于日志记录 / Operation name for logging</param>
    private Task ExecuteReadWithRetryAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken, string operationName)
    {
        return ResilienceExecutor.ExecuteReadWithRetryAsync(operation, cancellationToken, operationName);
    }

    /// <summary>
    /// 执行带有自动重试机制的写入操作（仅幂等操作重试，防重复写入；与 Mongo/PG 同语义）。
    /// </summary>
    /// <remarks>
    /// Executes a write operation with automatic retry on transient failures. Only idempotent
    /// operations will be retried to avoid duplicate writes (aligned with the Mongo/PG adapters).
    /// </remarks>
    /// <typeparam name="T">操作返回值类型 / The type of the operation result</typeparam>
    /// <param name="operation">要执行的写入操作 / The write operation to execute</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <param name="operationName">操作名称，用于日志记录 / Operation name for logging</param>
    /// <param name="isIdempotent">操作是否为幂等操作 / Whether the operation is idempotent</param>
    /// <returns>操作结果 / The operation result</returns>
    private Task<T> ExecuteWriteWithRetryAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken, string operationName, bool isIdempotent)
    {
        return ResilienceExecutor.ExecuteWriteWithRetryAsync(operation, cancellationToken, operationName, isIdempotent);
    }

    /// <summary>
    /// 执行带有自动重试机制的写入操作（无返回值；仅幂等操作重试）。
    /// </summary>
    /// <remarks>
    /// Executes a write operation with automatic retry on transient failures (no return value).
    /// Only idempotent operations will be retried to avoid duplicate writes.
    /// </remarks>
    /// <param name="operation">要执行的写入操作 / The write operation to execute</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <param name="operationName">操作名称，用于日志记录 / Operation name for logging</param>
    /// <param name="isIdempotent">操作是否为幂等操作 / Whether the operation is idempotent</param>
    private Task ExecuteWriteWithRetryAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken, string operationName, bool isIdempotent)
    {
        return ResilienceExecutor.ExecuteWriteWithRetryAsync(operation, cancellationToken, operationName, isIdempotent);
    }
}
