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
using System.Threading;
using System.Threading.Tasks;

namespace GameFrameX.Capability.Online.Match;

/// <summary>
/// 对局结算存储（结果幂等落定 + 待发奖 outbox 的持久化面）。
/// <para>
/// 维护约束（红线）：① <see cref="CommitWithOutboxAsync"/> 必须把「首次结果胜出」的结果写入与
/// 完整待发奖记录的创建放在**同一原子提交**内——先结果后 outbox 的两段式写会在中间崩溃时
/// 造成奖励永久丢失（这正是 GFX-1116 的缺陷根因），实现方不得拆分；② 领取与写回全部走
/// <see cref="OnlineMatchRewardOutboxRecord.RowVersion"/> CAS，禁止无条件覆盖写；
/// ③ 内存实现与数据库实现必须保持同一套 CAS / 原子契约（内存实现是易失的，仅保证语义等价）。
/// </para>
/// <para>
/// 存量已结算但无 outbox 的历史结果**不补建** outbox（禁止对历史奖励盲目补发）；
/// <see cref="CommitWithOutboxAsync"/> 对已存在结果只返回既有结果，不追加 outbox。
/// </para>
/// </summary>
public interface IOnlineMatchSettlementStore : IOnlineMatchResultStore
{
    /// <summary>
    /// 原子落定结算结果并创建待发奖记录：首次提交时结果与 outbox 同事务写入；
    /// 结果已存在时返回既有结果且不追加 outbox（历史结果不补发）。
    /// </summary>
    /// <param name="result">待落定结果（玩家条目 = 结果 Entries 的玩家集合，初始 Pending）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>落定后的结果副本（重试时即首次结果）。</returns>
    Task<OnlineMatchResult> CommitWithOutboxAsync(OnlineMatchResult result, CancellationToken cancellationToken = default);

    /// <summary>
    /// 扫描到期可投递的待发奖记录：存在 Pending/Failed 玩家、到达下一投递时刻、且领取租约已过期或未被领取。
    /// </summary>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒）。</param>
    /// <param name="limit">单轮扫描上限（有限批次：坏结果风暴不独占当轮调度）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>到期记录副本列表（已按批次上限截断）。</returns>
    Task<IReadOnlyList<OnlineMatchRewardOutboxRecord>> FindDueAsync(long nowUnixMilliseconds, int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按结算结果标识查找待发奖记录。
    /// </summary>
    /// <param name="matchResultId">结算结果标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>记录副本；不存在返回 null。</returns>
    Task<OnlineMatchRewardOutboxRecord> FindByMatchResultAsync(string matchResultId, CancellationToken cancellationToken = default);

    /// <summary>
    /// CAS 领取待发奖记录：行版本匹配且租约未被他人持有时设置新租约并推进版本。
    /// </summary>
    /// <param name="matchResultId">结算结果标识。</param>
    /// <param name="expectedRowVersion">期望行版本（来自扫描到的记录副本）。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒）。</param>
    /// <param name="leaseDurationMilliseconds">租约时长毫秒。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>领取成功返回 true（领取后的新版本 = expectedRowVersion + 1）；版本不符、记录缺失或租约被持返回 false。</returns>
    Task<bool> TryClaimAsync(string matchResultId, long expectedRowVersion, long nowUnixMilliseconds, long leaseDurationMilliseconds, CancellationToken cancellationToken = default);

    /// <summary>
    /// CAS 写回待发奖记录（逐玩家投递结果、尝试轮次、下一投递时刻与租约释放）。
    /// </summary>
    /// <param name="record">写回内容（<see cref="OnlineMatchRewardOutboxRecord.RowVersion"/> 必须为领取时获得的新版本）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>版本匹配并写入返回 true；版本不符（他人已推进）返回 false，调用方放弃本次结果。</returns>
    Task<bool> TryWriteBackAsync(OnlineMatchRewardOutboxRecord record, CancellationToken cancellationToken = default);

    /// <summary>
    /// 列出全部未完成投递的待发奖记录投影（admin 可观测面：含 ManualReview 冻结项）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>待重试发奖条目投影。</returns>
    Task<IReadOnlyList<OnlineMatchSettlementRetryEntry>> ListUndeliveredAsync(CancellationToken cancellationToken = default);
}
