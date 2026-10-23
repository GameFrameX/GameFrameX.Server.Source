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


using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace GameFrameX.DataBase.Abstractions;

/// <summary>
/// 数据库共享弹性引擎（单一事实源）：降级读前置检查、写门控、指数延迟 + 抖动重试核心环与三指标的统一载体。
/// </summary>
/// <remarks>
/// Shared resilience engine for all database providers: degraded-read pre-check, core-write
/// gating (checked before every write attempt), the retry core loop (bounded delay sequence
/// with jitter) and the three operation metrics. Providers contribute only an exception
/// classifier, an availability host, instruments, delay sources and localized messages via
/// <see cref="DbResilienceExecutorOptions"/>; elasticity bug fixes converge to this single point.
///
/// 弹性语义（业务规则级）：
/// ① 重试次数 = 延迟序列长度（首次尝试 + N 次重试），非幂等写固定零重试（防重复写入）；
/// ② 每次重试延迟 = 基值 + [0,100) 抖动，重试间接受 <c>CancellationToken</c> 约束（取消即抛出）；
/// ③ 不可重试异常原样上抛且不改变可用性状态；可重试异常按宿主状态机记账；
/// ④ 全部重试耗尽后抛 <see cref="DatabaseUnavailableException"/>（内层保留最后异常）；
/// ⑤ 读路径先问宿主降级（白名单读 + 状态机区间判定），写路径每次尝试前复检门控。
///
/// 并发语义：本引擎无可变状态，线程安全；可变状态全部位于注入的宿主/延迟访问器，
/// 由其实现方保证并发正确性（Mongo/PG 可用性状态机内部加锁）。
/// </remarks>
public sealed class DbResilienceExecutor
{
    private readonly DbResilienceExecutorOptions _options;

    /// <summary>
    /// 使用指定构造参数初始化 <see cref="DbResilienceExecutor"/>。
    /// </summary>
    /// <remarks>
    /// Initializes a new instance with the given provider parameters.
    /// </remarks>
    /// <param name="options">构造参数 / The provider parameters</param>
    public DbResilienceExecutor(DbResilienceExecutorOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    /// 执行带有自动重试机制的读取操作。
    /// </summary>
    /// <remarks>
    /// Executes a read operation with automatic retry mechanism on transient failures.
    /// </remarks>
    /// <typeparam name="T">操作返回值类型 / The type of the operation result</typeparam>
    /// <param name="operation">要执行的读取操作 / The read operation to execute</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <param name="operationName">操作名称，用于日志记录 / Operation name for logging</param>
    /// <returns>操作结果 / The operation result</returns>
    public async Task<T> ExecuteReadWithRetryAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken, string operationName)
    {
        return await ExecuteReadWithRetryAsync(operation, cancellationToken, operationName, fallbackValueFactory: null).ConfigureAwait(false);
    }

    /// <summary>
    /// 执行带有自动重试机制的读取操作（支持降级返回）。
    /// </summary>
    /// <remarks>
    /// Executes a read operation with automatic retry on transient failures, consulting the
    /// availability host for a degraded fallback before touching the database.
    /// </remarks>
    /// <typeparam name="T">操作返回值类型 / The type of the operation result</typeparam>
    /// <param name="operation">读取操作 / The read operation to execute</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <param name="operationName">操作名称 / Operation name</param>
    /// <param name="fallbackValueFactory">降级返回值工厂 / The degraded fallback value factory</param>
    /// <returns>操作结果 / The operation result</returns>
    public async Task<T> ExecuteReadWithRetryAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken, string operationName, Func<T> fallbackValueFactory)
    {
        if (_options.Host.TryReturnDegradedReadFallback(operationName, fallbackValueFactory, out T degradedValue))
        {
            return degradedValue;
        }

        return await ExecuteWithRetryAsync(operation, cancellationToken, _options.GetReadRetryDelaysMilliseconds(), operationName, "read").ConfigureAwait(false);
    }

    /// <summary>
    /// 执行带有自动重试机制的读取操作（无返回值；无降级前置检查）。
    /// </summary>
    /// <remarks>
    /// Executes a read operation with automatic retry mechanism on transient failures (no return
    /// value; no degraded pre-check since there is no fallback value to return).
    /// </remarks>
    /// <param name="operation">要执行的读取操作 / The read operation to execute</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <param name="operationName">操作名称，用于日志记录 / Operation name for logging</param>
    public async Task ExecuteReadWithRetryAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken, string operationName)
    {
        await ExecuteWithRetryAsync(async token =>
        {
            await operation(token).ConfigureAwait(false);
            return true;
        }, cancellationToken, _options.GetReadRetryDelaysMilliseconds(), operationName, "read").ConfigureAwait(false);
    }

    /// <summary>
    /// 执行带有自动重试机制的写入操作（仅幂等操作重试，防重复写入）。
    /// </summary>
    /// <remarks>
    /// Executes a write operation with automatic retry mechanism on transient failures.
    /// Only idempotent operations will be retried to avoid duplicate writes.
    /// </remarks>
    /// <typeparam name="T">操作返回值类型 / The type of the operation result</typeparam>
    /// <param name="operation">要执行的写入操作 / The write operation to execute</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <param name="operationName">操作名称，用于日志记录 / Operation name for logging</param>
    /// <param name="isIdempotent">操作是否为幂等操作 / Whether the operation is idempotent</param>
    /// <returns>操作结果 / The operation result</returns>
    public async Task<T> ExecuteWriteWithRetryAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken, string operationName, bool isIdempotent)
    {
        if (!_options.Host.IsCoreWriteAllowed(operationName))
        {
            throw new DatabaseUnavailableException(_options.GetServiceUnavailableMessage());
        }

        if (!isIdempotent)
        {
            return await ExecuteWithRetryAsync(operation, cancellationToken, Array.Empty<int>(), operationName, "write").ConfigureAwait(false);
        }

        return await ExecuteWithRetryAsync(operation, cancellationToken, _options.GetIdempotentWriteRetryDelaysMilliseconds(), operationName, "write").ConfigureAwait(false);
    }

    /// <summary>
    /// 执行带有自动重试机制的写入操作（无返回值；仅幂等操作重试，防重复写入）。
    /// </summary>
    /// <remarks>
    /// Executes a write operation with automatic retry mechanism on transient failures (no return
    /// value). Only idempotent operations will be retried to avoid duplicate writes.
    /// </remarks>
    /// <param name="operation">要执行的写入操作 / The write operation to execute</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <param name="operationName">操作名称，用于日志记录 / Operation name for logging</param>
    /// <param name="isIdempotent">操作是否为幂等操作 / Whether the operation is idempotent</param>
    public async Task ExecuteWriteWithRetryAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken, string operationName, bool isIdempotent)
    {
        if (!_options.Host.IsCoreWriteAllowed(operationName))
        {
            throw new DatabaseUnavailableException(_options.GetServiceUnavailableMessage());
        }

        var retryDelays = isIdempotent ? _options.GetIdempotentWriteRetryDelaysMilliseconds() : Array.Empty<int>();
        await ExecuteWithRetryAsync(async token =>
        {
            await operation(token).ConfigureAwait(false);
            return true;
        }, cancellationToken, retryDelays, operationName, "write").ConfigureAwait(false);
    }

    /// <summary>
    /// 执行带有自定义重试延迟的通用重试机制（核心环）。
    /// </summary>
    /// <remarks>
    /// Executes an operation with custom retry delays and bounded backoff with jitter. The
    /// write gate is re-checked before every attempt; non-retryable exceptions rethrow
    /// immediately; exhausting all attempts throws <see cref="DatabaseUnavailableException"/>
    /// preserving the last exception.
    /// </remarks>
    /// <typeparam name="T">操作返回值类型 / The type of the operation result</typeparam>
    /// <param name="operation">要执行的操作 / The operation to execute</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <param name="retryDelaysMilliseconds">每次重试的延迟毫秒数列表 / List of retry delays in milliseconds</param>
    /// <param name="operationName">操作名称，用于日志记录 / Operation name for logging</param>
    /// <param name="operationType">操作类型（read/write）/ Operation type (read/write)</param>
    /// <returns>操作结果 / The operation result</returns>
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
                if (string.Equals(operationType, "write", StringComparison.Ordinal) && !_options.Host.IsCoreWriteAllowed(operationName))
                {
                    throw new DatabaseUnavailableException(_options.GetServiceUnavailableMessage());
                }

                var result = await operation(cancellationToken).ConfigureAwait(false);
                _options.Host.RecordOperationSuccess(operationName, operationType);
                _options.OperationLatencyMilliseconds.Record(operationStopwatch.Elapsed.TotalMilliseconds, new TagList { { "op", operationType }, { "name", operationName }, { "success", true }, });
                return result;
            }
            catch (Exception exception)
            {
                if (!_options.Classifier.IsRetryable(exception))
                {
                    throw;
                }

                lastException = exception;
                _options.Host.RecordRetryableFailure(operationName, operationType, exception);
                if (attempt < retryDelaysMilliseconds.Count)
                {
                    var delay = retryDelaysMilliseconds[attempt] + _options.NextJitter();
                    _options.OperationRetryTotal.Add(1, new TagList { { "op", operationType }, { "name", operationName }, { "reason", _options.Classifier.GetFailureReason(exception) }, });
                    _options.LogWarning(_options.GetTransientErrorMessage(operationName, attempt + 1, retryDelaysMilliseconds.Count, exception.Message));
                    await _options.DelayAsync(delay, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                break;
            }
        }

        var finalException = new DatabaseUnavailableException(_options.GetRetryFailedMessage(operationName), lastException ?? new InvalidOperationException(_options.GetRetryFailedMessage(operationName)));
        _options.OperationFailTotal.Add(1, new TagList { { "op", operationType }, { "name", operationName }, { "reason", _options.Classifier.GetFailureReason(lastException) }, });
        _options.OperationLatencyMilliseconds.Record(operationStopwatch.Elapsed.TotalMilliseconds, new TagList { { "op", operationType }, { "name", operationName }, { "success", false }, });
        throw finalException;
    }
}
