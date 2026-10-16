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


using System.Diagnostics.Metrics;

namespace GameFrameX.DataBase.Abstractions;

/// <summary>
/// 共享弹性引擎构造参数（C253）。
/// </summary>
/// <remarks>
/// Construction parameters for <see cref="DbResilienceExecutor"/>. Providers contribute their
/// exception classifier, availability host, metric instruments, retry delay sources, localized
/// message factories and warning sink; the engine itself stays free of localization/logging
/// dependencies. Delay/message/message-factory delegates are invoked at operation time so
/// runtime option re-application and deferred culture resolution keep working.
/// 消息工厂与延迟访问器在每次操作入口求值；仪表实例由 provider 静态持有注入
/// （同一 Meter 下同名仪表保持单实例创建，provider 维度由 Meter 名承载，不新增 tag）。
/// </remarks>
public sealed class DbResilienceExecutorOptions
{
    /// <summary>
    /// Provider 异常分类器（两级重试判定）。
    /// </summary>
    public required IDbOperationExceptionClassifier Classifier { get; init; }

    /// <summary>
    /// 可用性宿主（降级读/写门控/成败记录）。
    /// </summary>
    public required IDbResilienceHost Host { get; init; }

    /// <summary>
    /// 操作重试次数指标（仪表名 <c>db_operation_retry_total</c>，tag：op/name/reason）。
    /// </summary>
    public required Counter<long> OperationRetryTotal { get; init; }

    /// <summary>
    /// 操作最终失败次数指标（仪表名 <c>db_operation_fail_total</c>，tag：op/name/reason）。
    /// </summary>
    public required Counter<long> OperationFailTotal { get; init; }

    /// <summary>
    /// 操作耗时直方图（仪表名 <c>db_operation_latency_ms</c>，tag：op/name/success）。
    /// </summary>
    public required Histogram<double> OperationLatencyMilliseconds { get; init; }

    /// <summary>
    /// 读取重试延迟（毫秒）访问器——每次读操作入口取当前配置。
    /// </summary>
    public required Func<IReadOnlyList<int>> GetReadRetryDelaysMilliseconds { get; init; }

    /// <summary>
    /// 幂等写重试延迟（毫秒）访问器——每次写操作入口取当前配置。
    /// </summary>
    public required Func<IReadOnlyList<int>> GetIdempotentWriteRetryDelaysMilliseconds { get; init; }

    /// <summary>
    /// 服务不可用文案（写门控拒绝时抛出的 <see cref="DatabaseUnavailableException"/> 消息）。
    /// </summary>
    public required Func<string> GetServiceUnavailableMessage { get; init; }

    /// <summary>
    /// 瞬时错误重试 Warning 文案（参数：操作名/当前重试序号/总重试数/异常消息）。
    /// </summary>
    public required Func<string, int, int, string, string> GetTransientErrorMessage { get; init; }

    /// <summary>
    /// 重试全部失败文案（参数：操作名）。
    /// </summary>
    public required Func<string, string> GetRetryFailedMessage { get; init; }

    /// <summary>
    /// Warning 日志输出动作（provider 侧接 <c>LogHelper.Warning</c>）。
    /// </summary>
    public required Action<string> LogWarning { get; init; }

    /// <summary>
    /// 重试延迟等待动作（默认 <c>Task.Delay</c>；契约测试注入假时钟记录退避序列）。
    /// </summary>
    public Func<int, CancellationToken, Task> DelayAsync { get; init; } = Task.Delay;

    /// <summary>
    /// 退避抖动值提供器（默认 <c>Random.Shared.Next(0, 100)</c>；契约测试注入定值消除随机性）。
    /// </summary>
    public Func<int> NextJitter { get; init; } = static () => Random.Shared.Next(0, 100);
}
