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
/// 待发奖记录的逐玩家投递条目（outbox 行内嵌条目：一个玩家一条投递状态）。
/// <para>
/// 维护约束（红线）：发奖幂等边界 =（<see cref="PlayerId"/> + 记录级 MatchResultId），
/// 与 <see cref="OnlineMatchResultDispatcher"/> 的 <c>{MatchResultId}-{PlayerId}</c> 幂等键一一对应；
/// 条目状态只经存储 CAS 写回更新，调用方不得在共享实例上原地改写。
/// </para>
/// </summary>
public sealed class OnlineMatchRewardOutboxPlayerEntry
{
    /// <summary>
    /// 获取或设置玩家标识。
    /// </summary>
    public long PlayerId { get; set; }

    /// <summary>
    /// 获取或设置投递状态（<see cref="OnlineMatchRewardDeliveryState"/>）。
    /// </summary>
    public OnlineMatchRewardDeliveryState State { get; set; } = OnlineMatchRewardDeliveryState.Pending;

    /// <summary>
    /// 构造条目副本（深拷贝；状态字段的值语义由枚举保证）。
    /// </summary>
    /// <returns>条目副本。</returns>
    public OnlineMatchRewardOutboxPlayerEntry Copy()
    {
        return new OnlineMatchRewardOutboxPlayerEntry
        {
            PlayerId = PlayerId,
            State = State,
        };
    }
}
