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

using System;
using System.Collections.Generic;

namespace GameFrameX.Capability.Online.Match;

/// <summary>
/// 待发奖记录（结算发奖的持久化 outbox：与结算结果同事务落库、按 CAS 领取/写回驱动投递）。
/// <para>
/// 维护约束（红线）：① <see cref="RowVersion"/> 是全部并发控制的唯一凭据——领取
/// （<c>TryClaimAsync</c>）与写回（<c>TryWriteBackAsync</c>）都必须以 CAS 方式推进版本，
/// 任何无条件覆盖写都会重新打开双实例重复发奖窗口；② <see cref="ClaimExpiresAtUnixMilliseconds"/>
/// 非零表示已被某实例领取，租约到期（&lt;= now）后才可被其他实例领取；领取者崩溃后由租约过期
/// 兜底恢复，重投安全性由发奖幂等键保证（崩溃前已成功的玩家不重复入账）；③ 已成功玩家
/// （<see cref="OnlineMatchRewardDeliveryState.Succeeded"/>）永不再进入投递作用域。
/// </para>
/// </summary>
public sealed class OnlineMatchRewardOutboxRecord
{
    /// <summary>
    /// 获取或设置结算结果标识（记录唯一键；发奖幂等键的组成部分）。
    /// </summary>
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
    /// 获取或设置 App 标识。
    /// </summary>
    public long AppId { get; set; }

    /// <summary>
    /// 获取或设置结果快照（重投时的发奖数据源；按失败玩家收窄后投递）。
    /// </summary>
    public OnlineMatchResult Result { get; set; }

    /// <summary>
    /// 获取或设置逐玩家投递条目（与结果 Entries 的玩家一一对应）。
    /// </summary>
    public List<OnlineMatchRewardOutboxPlayerEntry> PlayerEntries { get; set; } = new List<OnlineMatchRewardOutboxPlayerEntry>();

    /// <summary>
    /// 获取或设置累计投递轮次（每轮领取投递 +1；含首发轮）。
    /// </summary>
    public int Attempts { get; set; }

    /// <summary>
    /// 获取或设置下一轮投递最早时刻（Unix 毫秒；0 = 立即可投）。
    /// </summary>
    public long NextAttemptTimeUnixMilliseconds { get; set; }

    /// <summary>
    /// 获取或设置投递租约到期时刻（Unix 毫秒；0 = 未被领取）。
    /// </summary>
    public long ClaimExpiresAtUnixMilliseconds { get; set; }

    /// <summary>
    /// 获取或设置行版本号（CAS 凭据；领取与写回各推进一次）。
    /// </summary>
    public long RowVersion { get; set; }

    /// <summary>
    /// 判断是否还有未完成投递的玩家（Pending / Failed / ManualReview 任一存在）。
    /// </summary>
    /// <returns>存在未完成玩家返回 true；全部成功返回 false。</returns>
    public bool HasUndeliveredPlayers()
    {
        return PlayerEntries != null && PlayerEntries.Exists(static entry => entry != null && entry.State != OnlineMatchRewardDeliveryState.Succeeded);
    }

    /// <summary>
    /// 判断是否处于自动重投可达状态：存在 Pending/Failed 玩家（ManualReview 已冻结）。
    /// </summary>
    /// <returns>可自动重投返回 true。</returns>
    public bool HasAutoRetryablePlayers()
    {
        return PlayerEntries != null && PlayerEntries.Exists(static entry => entry != null && (entry.State == OnlineMatchRewardDeliveryState.Pending || entry.State == OnlineMatchRewardDeliveryState.Failed));
    }

    /// <summary>
    /// 构造记录副本（结果快照与逐玩家条目深拷贝）。
    /// </summary>
    /// <returns>记录副本。</returns>
    public OnlineMatchRewardOutboxRecord Copy()
    {
        var copy = new OnlineMatchRewardOutboxRecord
        {
            MatchResultId = MatchResultId,
            MatchId = MatchId,
            TenantId = TenantId,
            AppId = AppId,
            Result = Result == null ? null : Result.Copy(),
            Attempts = Attempts,
            NextAttemptTimeUnixMilliseconds = NextAttemptTimeUnixMilliseconds,
            ClaimExpiresAtUnixMilliseconds = ClaimExpiresAtUnixMilliseconds,
            RowVersion = RowVersion,
        };

        if (PlayerEntries != null)
        {
            foreach (var entry in PlayerEntries)
            {
                copy.PlayerEntries.Add(entry == null ? null : entry.Copy());
            }
        }

        return copy;
    }
}
