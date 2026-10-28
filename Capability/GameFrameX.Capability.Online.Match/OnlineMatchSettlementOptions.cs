// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and other rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the PRC and applicable international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with relevant laws, regulations and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file at the source code root for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯公民合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切纠纷与责任，
//   Any disputes or liabilities arising from secondary development based on this project
//   本组织不承担任何责任，开发者应自行承担。
//   The organization and contributors shall not be responsible; developers should bear the responsibility themselves.
// ==========================================================================================

namespace GameFrameX.Capability.Online.Match;

/// <summary>
/// 结算编排 Options 子段（发奖重投终止条件）。
/// </summary>
public sealed class OnlineMatchSettlementOptions
{
    /// <summary>
    /// 发奖投递尝试上限默认值（含首发；默认 5 次，建议区间 3–5）。
    /// </summary>
    public const int DefaultMaxGrantAttempts = 5;

    /// <summary>
    /// 获取或设置单条结算结果的发奖投递尝试上限（含首发；默认 <see cref="DefaultMaxGrantAttempts"/>）。
    /// </summary>
    /// <remarks>
    /// 维护约束（红线）：达到上限仍存在失败玩家的条目转
    /// <see cref="OnlineMatchSettlementRetryState.ManualReview"/> 停止自动重投（重投永不发散）；
    /// 上限同时有界化最长重试周期（≤ 上限 × 结算 tick 调度间隔），该周期必须保持远小于发奖幂等键保留时长
    /// （Foundation <c>IdempotencyOptions.RetentionMilliseconds</c>，生产装配默认 24 小时）——
    /// 否则幂等 TTL 过期后的重投会对已成功玩家重复发奖。
    /// </remarks>
    public int MaxGrantAttempts { get; set; } = DefaultMaxGrantAttempts;
}
