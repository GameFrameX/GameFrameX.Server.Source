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

namespace GameFrameX.Capability.Online.Persistence.Match;

using GameFrameX.Capability.Online.Match;
using GameFrameX.DataBase;
using GameFrameX.Foundation.Orm.Attribute;

/// <summary>
/// 待发奖记录持久化行（结算发奖 outbox 的 GameDb 落库形态）。
/// <para>
/// 维护约束（红线）：① <see cref="MatchResultId"/> 单列唯一索引——一条结算结果至多一条待发奖记录；
/// ② 行版本（<c>RowVersion</c>）是领取与写回的 CAS 凭据，与
/// <see cref="OnlineMatchRewardOutboxRecord.RowVersion"/> 同步映射；③
/// <see cref="NextAttemptTimeUnixMilliseconds"/> / <see cref="ClaimExpiresAtUnixMilliseconds"/>
/// 物化为独立列供到期扫描谓词下推，其余状态在 <see cref="Record"/> 文档内。
/// </para>
/// <para>
/// 数据生命周期：行物理长存（与 InMemory 实现「成功即移除」的口径差异是有意为之——
/// 持久化行是崩溃恢复与人工复核的审计事实；全部成功后行不再进入任何扫描）。
/// </para>
/// </summary>
public sealed class OnlineMatchRewardOutboxRowState : BaseCacheState
{
    /// <summary>
    /// 获取或设置结算结果标识（唯一索引列；发奖幂等键的组成部分）。
    /// </summary>
    [EntityIndex("ux_online_match_reward_outbox_result_id", Unique = true)]
    public string MatchResultId { get; set; }

    /// <summary>
    /// 获取或设置对局标识。
    /// </summary>
    public string MatchId { get; set; }

    /// <summary>
    /// 获取或设置租户标识。
    /// </summary>
    public long TenantId { get; set; }

    /// <summary>
    /// 获取或设置应用标识。
    /// </summary>
    public long AppId { get; set; }

    /// <summary>
    /// 获取或设置下一轮投递最早时刻（Unix 毫秒；0 = 立即可投）。
    /// </summary>
    public long NextAttemptTimeUnixMilliseconds { get; set; }

    /// <summary>
    /// 获取或设置投递租约到期时刻（Unix 毫秒；0 = 未被领取）。
    /// </summary>
    public long ClaimExpiresAtUnixMilliseconds { get; set; }

    /// <summary>
    /// 获取或设置待发奖记录文档（结果快照 + 逐玩家投递状态 + 尝试轮次 + 行版本）。
    /// </summary>
    public OnlineMatchRewardOutboxRecord Record { get; set; }
}
