// ==========================================================================================
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   are protected by the laws of the People's Republic of China and related international regulations.
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   This project is licensed solely under the Apache License 2.0,
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   Any legal disputes and liabilities arising from secondary development based on this project
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
// ==========================================================================================


namespace GameFrameX.NetWork.RemoteMessaging.Unified;

/// <summary>
/// 消息发送指标记录参数对象（C154）：收敛原 7 散参 Record 形态，删除未使用的 playerId/traceId。
/// </summary>
/// <remarks>
/// Parameter object for message-send metrics recording (C154): collapses the former seven-parameter
/// Record shape and drops the unused playerId/traceId parameters. Player and server pipelines both
/// feed this record; TargetType + ServiceName form the metrics bucket key.
/// </remarks>
public sealed class SendMetricsRecord
{
    /// <summary>
    /// 获取或设置目标类型标签（"player" / "server"），与 ServiceName 共同构成指标分桶键。
    /// </summary>
    /// <remarks>
    /// Gets or sets the target-type label ("player" / "server"); combined with ServiceName it forms the metrics bucket key.
    /// </remarks>
    public string TargetType { get; init; }

    /// <summary>
    /// 获取或设置服务名标签。
    /// </summary>
    /// <remarks>
    /// Gets or sets the service-name label.
    /// </remarks>
    public string ServiceName { get; init; }

    /// <summary>
    /// 获取或设置状态码标签（如 "LocalDelivered"、"Success"、"Timeout"）。
    /// </summary>
    /// <remarks>
    /// Gets or sets the status-code label (e.g. "LocalDelivered", "Success", "Timeout").
    /// </remarks>
    public string StatusCode { get; init; }

    /// <summary>
    /// 获取或设置耗时（毫秒）。
    /// </summary>
    /// <remarks>
    /// Gets or sets the elapsed time in milliseconds.
    /// </remarks>
    public long ElapsedMs { get; init; }

    /// <summary>
    /// 获取或设置重试次数。
    /// </summary>
    /// <remarks>
    /// Gets or sets the retry count.
    /// </remarks>
    public int RetryCount { get; init; }
}
