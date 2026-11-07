//  ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and other rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the PRC and applicable international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please see the LICENSE file at the source code root for the full license text.
//   禁止利用本项目代码实施任何危害国家安全、破坏社会秩序、
//   侵犯其他个人或组织合法权益等法律法规禁止的行为！
//  ==========================================================================================

namespace GameFrameX.Capability.Online.Match;

/// <summary>
/// 对局结算编排选项（发奖尝试上限、投递租约与待发奖扫描批次）。
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
    /// <see cref="OnlineMatchRewardDeliveryState.ManualReview"/> 停止自动重投（重投永不发散）；
    /// 上限同时有界化最长重试周期（≤ 上限 × 结算 tick 调度间隔 + 崩溃恢复租约延迟），
    /// 该周期必须保持远小于发奖幂等键保留时长
    /// （Foundation <c>IdempotencyOptions.RetentionMilliseconds</c>，生产装配默认 24 小时）——
    /// 否则幂等 TTL 过期后的重投会对已成功玩家重复发奖。
    /// </remarks>
    public int MaxGrantAttempts { get; set; } = DefaultMaxGrantAttempts;

    /// <summary>
    /// 发奖投递租约时长默认值（毫秒；默认 60 秒）。
    /// </summary>
    public const int DefaultGrantClaimLeaseMilliseconds = 60_000;

    /// <summary>
    /// 单轮待发奖扫描的批量大小的默认值（有限批次：坏结果风暴不独占当轮调度）。
    /// </summary>
    public const int DefaultOutboxScanBatchSize = 64;

    /// <summary>
    /// 获取或设置发奖投递租约时长毫秒（默认 <see cref="DefaultGrantClaimLeaseMilliseconds"/>）。
    /// </summary>
    /// <remarks>
    /// 维护约束（红线）：租约时长必须**大于单轮发奖最长耗时**（领取 → 投递 → 写回），
    /// 否则活跃实例会被误判过期而出现双领取；同时它界定崩溃后的最长恢复延迟——
    /// 领取者崩溃后需等租约到期才可被其他实例接管重投，重投安全性由发奖幂等键保证。
    /// </remarks>
    public int GrantClaimLeaseMilliseconds { get; set; } = DefaultGrantClaimLeaseMilliseconds;

    /// <summary>
    /// 获取或设置单轮待发奖扫描的批量大小（默认 <see cref="DefaultOutboxScanBatchSize"/>；非正值回退默认）。
    /// </summary>
    public int OutboxScanBatchSize { get; set; } = DefaultOutboxScanBatchSize;
}
