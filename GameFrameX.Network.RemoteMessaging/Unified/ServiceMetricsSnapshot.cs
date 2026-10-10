// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规及开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,,
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

namespace GameFrameX.Network.RemoteMessaging.Unified;

/// <summary>
/// 服务指标快照
/// </summary>
/// <remarks>
/// Service metrics snapshot.
/// </remarks>
public sealed class ServiceMetricsSnapshot
{
    /// <summary>
    /// 指标键 (targetType:serviceName)
    /// </summary>
    /// <remarks>
    /// Metrics key (targetType:serviceName).
    /// </remarks>
    /// <value>指标键，格式为 targetType:serviceName / The metrics key in the format targetType:serviceName</value>
    public string Key { get; init; }

    /// <summary>
    /// 总调用次数
    /// </summary>
    /// <remarks>
    /// Total call count.
    /// </remarks>
    /// <value>总调用次数 / The total call count</value>
    public long TotalCalls { get; init; }

    /// <summary>
    /// 成功次数
    /// </summary>
    /// <remarks>
    /// Success count.
    /// </remarks>
    /// <value>成功次数 / The success count</value>
    public long SuccessCalls { get; init; }

    /// <summary>
    /// 超时次数
    /// </summary>
    /// <remarks>
    /// Timeout count.
    /// </remarks>
    /// <value>超时次数 / The timeout count</value>
    public long TimeoutCalls { get; init; }

    /// <summary>
    /// 重试次数
    /// </summary>
    /// <remarks>
    /// Retry count.
    /// </remarks>
    /// <value>重试次数 / The retry count</value>
    public long RetryCalls { get; init; }

    /// <summary>
    /// 成功率
    /// </summary>
    /// <remarks>
    /// Success rate.
    /// </remarks>
    /// <value>成功率（0 到 1 之间）/ The success rate (between 0 and 1)</value>
    public double SuccessRate { get; init; }

    /// <summary>
    /// 平均耗时毫秒
    /// </summary>
    /// <remarks>
    /// Average elapsed time in milliseconds.
    /// </remarks>
    /// <value>平均耗时（毫秒）/ The average elapsed time in milliseconds</value>
    public double AvgElapsedMs { get; init; }

    /// <summary>
    /// 最大耗时毫秒
    /// </summary>
    /// <remarks>
    /// Maximum elapsed time in milliseconds.
    /// </remarks>
    /// <value>最大耗时（毫秒）/ The maximum elapsed time in milliseconds</value>
    public long MaxElapsedMs { get; init; }

    /// <summary>
    /// P50 耗时
    /// </summary>
    /// <remarks>
    /// P50 latency.
    /// </remarks>
    /// <value>P50 耗时（毫秒）/ The P50 latency in milliseconds</value>
    public long P50ElapsedMs { get; init; }

    /// <summary>
    /// P90 耗时
    /// </summary>
    /// <remarks>
    /// P90 latency.
    /// </remarks>
    /// <value>P90 耗时（毫秒）/ The P90 latency in milliseconds</value>
    public long P90ElapsedMs { get; init; }

    /// <summary>
    /// P99 耗时
    /// </summary>
    /// <remarks>
    /// P99 latency.
    /// </remarks>
    /// <value>P99 耗时（毫秒）/ The P99 latency in milliseconds</value>
    public long P99ElapsedMs { get; init; }
}