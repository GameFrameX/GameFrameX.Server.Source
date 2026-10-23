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
/// 数据库弹性引擎的可用性宿主契约——降级读、写门控与成败记录的 per-service 实例状态参数化（C253）。
/// </summary>
/// <remarks>
/// Availability host contract for the shared resilience engine: the per-service instance state
/// behind degraded reads, core-write gating and success/failure bookkeeping is parameterized
/// through this interface. Implementations own an availability state machine (Mongo/PostgreSQL
/// adapters) or are constant-healthy (SQLite embedded engine, C222 boundary ④).
/// 实现方负责线程安全（多请求并发进入同一引擎实例时本接口会被并发调用；
/// Mongo/PG 的可用性状态机以内部锁守护，恒 Healthy 宿主无可变状态）。
/// </remarks>
public interface IDbResilienceHost
{
    /// <summary>
    /// 判断并执行非核心读降级返回：可用性状态已进入不可恢复区间且操作在降级白名单内时，
    /// 直接返回降级值而不再触达数据库。
    /// </summary>
    /// <remarks>
    /// Non-critical whitelisted reads are answered with a fallback value once the availability
    /// state machine has left the healthy/degraded band (or the half-open probe budget is
    /// exhausted), avoiding database pressure during outages.
    /// </remarks>
    /// <typeparam name="T">返回值类型 / The result type</typeparam>
    /// <param name="operationName">操作名称 / The operation name</param>
    /// <param name="fallbackValueFactory">降级值工厂（可为 null，null 时返回 default）/ The fallback value factory (null yields default)</param>
    /// <param name="fallbackValue">降级值 / The fallback value</param>
    /// <returns>触发降级返回 true / true when the degraded fallback was applied</returns>
    bool TryReturnDegradedReadFallback<T>(string operationName, Func<T> fallbackValueFactory, out T fallbackValue);

    /// <summary>
    /// 判断核心写操作当前是否允许执行（不健康区间快速失败、恢复半开窗口限流探测）。
    /// </summary>
    /// <remarks>
    /// Core whitelisted writes fast-fail while unhealthy and are rate-limited through the
    /// half-open probe window while recovering. The engine consults this gate before every
    /// write attempt, not only before the first.
    /// 引擎在写路径的每次尝试前都会复检本门控。
    /// </remarks>
    /// <param name="operationName">操作名称 / The operation name</param>
    /// <returns>允许执行返回 true / true when the write may proceed</returns>
    bool IsCoreWriteAllowed(string operationName);

    /// <summary>
    /// 记录一次操作成功并按状态机推进恢复（连续成功计数、半开探测转健康等）。
    /// </summary>
    /// <remarks>
    /// Records an operation success and advances the availability state machine
    /// (consecutive-success counting, recovering-to-healthy promotion).
    /// </remarks>
    /// <param name="operationName">操作名称 / The operation name</param>
    /// <param name="operationType">操作类型（read/write）/ The operation type (read/write)</param>
    void RecordOperationSuccess(string operationName, string operationType);

    /// <summary>
    /// 记录一次可重试失败并按状态机推进降级（连续失败计数、降级/不可健康迁移、恢复任务启动）。
    /// </summary>
    /// <remarks>
    /// Records a retryable failure and advances the availability state machine
    /// (consecutive-failure counting, healthy-to-degraded/unhealthy transitions, recovery task start).
    /// 仅在异常被分类器判为可重试后由引擎调用（不可重试异常不改变可用性状态）。
    /// </remarks>
    /// <param name="operationName">操作名称 / The operation name</param>
    /// <param name="operationType">操作类型（read/write）/ The operation type (read/write)</param>
    /// <param name="exception">被判定可重试的异常 / The exception classified as retryable</param>
    void RecordRetryableFailure(string operationName, string operationType, Exception exception);
}
