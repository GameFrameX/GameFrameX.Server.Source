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
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Foundation.Logger;
using GameFrameX.Localization;

namespace GameFrameX.DataBase.Mongo;

public sealed partial class MongoDbService
{
    /// <summary>
    /// Provider 异常分类器单例（两级重试判定收敛至分类器，弹性核心环由共享引擎承载）。
    /// </summary>
    /// <remarks>
    /// Provider exception classifier singleton; the retry core loop lives in the shared
    /// <see cref="DbResilienceExecutor"/>.
    /// </remarks>
    internal static readonly MongoDbExceptionClassifier ExceptionClassifier = new();

    /// <summary>
    /// 共享弹性引擎（惰性构造——依赖本实例的可用性宿主与延迟配置访问器；并发首次访问幂等赋值，最坏重复构造一个等价实例）。
    /// </summary>
    /// <remarks>
    /// The shared resilience engine, constructed lazily because it binds this instance's
    /// availability host and delay configuration accessors.
    /// </remarks>
    private DbResilienceExecutor _resilienceExecutor;

    private DbResilienceExecutor ResilienceExecutor => _resilienceExecutor ??= new DbResilienceExecutor(new DbResilienceExecutorOptions
    {
        Classifier = ExceptionClassifier,
        Host = new AvailabilityResilienceHost(this),
        OperationRetryTotal = DbOperationRetryTotal,
        OperationFailTotal = DbOperationFailTotal,
        OperationLatencyMilliseconds = DbOperationLatencyMilliseconds,
        GetReadRetryDelaysMilliseconds = () => _readRetryDelaysMilliseconds,
        GetIdempotentWriteRetryDelaysMilliseconds = () => _idempotentWriteRetryDelaysMilliseconds,
        // Localization: Database.MongoDb.ServiceUnavailable - MongoDbService不可用，Open()尚未成功完成。
        GetServiceUnavailableMessage = () => LocalizationService.GetString(Keys.Database.MongoDbServiceUnavailable),
        // Localization: Database.Mongo.OperationTransientError - MongoDbService.{0} 瞬时错误，重试 {1}/{2}。error={3}
        GetTransientErrorMessage = (operationName, attempt, retries, errorMessage) => LocalizationService.GetString(Localization.Keys.Database.Mongo.OperationTransientError, operationName, attempt, retries, errorMessage),
        // Localization: Database.MongoDb.OperationRetryFailed - MongoDbService.{0}重试失败，未知异常。
        GetRetryFailedMessage = operationName => LocalizationService.GetString(Keys.Database.MongoDbOperationRetryFailed, operationName),
        LogWarning = message => LogHelper.Warning(message),
    });

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
    private Task<T> ExecuteReadWithRetryAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken, string operationName)
    {
        return ResilienceExecutor.ExecuteReadWithRetryAsync(operation, cancellationToken, operationName);
    }

    /// <summary>
    /// 执行带有自动重试机制的读取操作（支持降级返回）。
    /// </summary>
    /// <remarks>
    /// Executes a read operation with automatic retry on transient failures, consulting the
    /// availability host for a degraded fallback first.
    /// </remarks>
    /// <typeparam name="T">操作返回值类型。</typeparam>
    /// <param name="operation">读取操作。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="operationName">操作名称。</param>
    /// <param name="fallbackValueFactory">降级返回值工厂。</param>
    /// <returns>操作结果。</returns>
    private Task<T> ExecuteReadWithRetryAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken, string operationName, Func<T> fallbackValueFactory)
    {
        return ResilienceExecutor.ExecuteReadWithRetryAsync(operation, cancellationToken, operationName, fallbackValueFactory);
    }

    /// <summary>
    /// 执行带有自动重试机制的读取操作（无返回值）。
    /// </summary>
    /// <remarks>
    /// Executes a read operation with automatic retry mechanism on transient failures (no return value).
    /// </remarks>
    /// <param name="operation">要执行的读取操作 / The read operation to execute</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <param name="operationName">操作名称，用于日志记录 / Operation name for logging</param>
    private Task ExecuteReadWithRetryAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken, string operationName)
    {
        return ResilienceExecutor.ExecuteReadWithRetryAsync(operation, cancellationToken, operationName);
    }

    /// <summary>
    /// 执行带有自动重试机制的写入操作。
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
    private Task<T> ExecuteWriteWithRetryAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken, string operationName, bool isIdempotent)
    {
        return ResilienceExecutor.ExecuteWriteWithRetryAsync(operation, cancellationToken, operationName, isIdempotent);
    }

    /// <summary>
    /// 执行带有自动重试机制的写入操作（无返回值）。
    /// </summary>
    /// <remarks>
    /// Executes a write operation with automatic retry mechanism on transient failures (no return value).
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

    /// <summary>
    /// 可用性宿主适配器：把共享引擎的降级读/写门控/成败记账转发给本服务的可用性状态机。
    /// </summary>
    /// <remarks>
    /// Availability host adapter forwarding the shared engine's degraded-read, write-gate and
    /// bookkeeping hooks to this service's availability state machine.
    /// </remarks>
    private sealed class AvailabilityResilienceHost : IDbResilienceHost
    {
        private readonly MongoDbService _owner;

        public AvailabilityResilienceHost(MongoDbService owner)
        {
            _owner = owner;
        }

        public bool TryReturnDegradedReadFallback<T>(string operationName, Func<T> fallbackValueFactory, out T fallbackValue)
        {
            return _owner.TryReturnDegradedReadFallback(operationName, fallbackValueFactory, out fallbackValue);
        }

        public bool IsCoreWriteAllowed(string operationName)
        {
            return _owner.IsCoreWriteAllowed(operationName);
        }

        public void RecordOperationSuccess(string operationName, string operationType)
        {
            _owner.RecordOperationSuccess(operationName, operationType);
        }

        public void RecordRetryableFailure(string operationName, string operationType, Exception exception)
        {
            _owner.RecordRetryableFailure(operationName, operationType, exception);
        }
    }
}
