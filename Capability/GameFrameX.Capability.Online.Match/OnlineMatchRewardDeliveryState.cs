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
/// 待发奖记录的逐玩家投递状态（outbox 生命周期）。
/// <para>
/// 状态迁移：<see cref="Pending"/> →（本轮投递）→ <see cref="Succeeded"/> 或 <see cref="Failed"/>；
/// <see cref="Failed"/> 下一轮重投；记录尝试次数达到上限时未成功玩家转 <see cref="ManualReview"/>
/// 并冻结自动重投（人工裁决面）。
/// </para>
/// </summary>
public enum OnlineMatchRewardDeliveryState
{
    /// <summary>待投递（outbox 落库初始态）。</summary>
    Pending = 0,

    /// <summary>投递成功（终态；不再出现在任何重投作用域）。</summary>
    Succeeded = 1,

    /// <summary>本轮投递失败（下一轮继续自动重投）。</summary>
    Failed = 2,

    /// <summary>尝试耗尽转人工复核（终态；冻结自动重投，经 admin 查询面暴露）。</summary>
    ManualReview = 3,
}
