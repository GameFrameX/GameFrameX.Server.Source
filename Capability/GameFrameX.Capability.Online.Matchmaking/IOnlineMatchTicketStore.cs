//  ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Capability.Online.Matchmaking;

/// <summary>
/// 匹配票据存储契约（票据状态 CAS 与成组原子提交的持久化契约）。
/// 维护约束（红线）：<see cref="CommitMatchAsync"/> 与
/// <see cref="UpdateStateAsync"/> 是**唯一**允许改写票据状态的入口，且必须提供
/// 「期望状态」参数——状态裁决采用 CAS 语义：期望状态不匹配即整体失败并返回 null，
/// 绝不部分应用。取消与匹配成功的竞态由此收敛到同一临界区：
/// 谁先改到终态，谁的结果生效，另一方的 CAS 必然失败。
/// 入队的「查重 + 写入」唯一性同样收敛在存储层——<see cref="TryEnqueueIfAbsentAsync"/>
/// 在同一临界区内裁决，调用方不得以「先查后存」的两段式绕过。
/// <para>
/// 存储实现负责防御性深拷贝，且**不得**在 CAS 失败时留下任何写入痕迹
/// （包括玩家反查索引）。
/// </para>
/// </summary>
public interface IOnlineMatchTicketStore
{
    /// <summary>
    /// 保存票据（新增或覆盖；保留给状态迁移与持久化装载，**不再作为入队入口**——
    /// 入队唯一性由 <see cref="TryEnqueueIfAbsentAsync"/> 在同一临界区内保证）。
    /// </summary>
    /// <param name="ticket">票据。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>完成通知。</returns>
    Task SaveAsync(OnlineMatchTicket ticket, CancellationToken cancellationToken = default);

    /// <summary>
    /// 原子入队：在同一临界区内检查队伍和每个成员是否已有排队中票据；
    /// 没有冲突就写入票据和反查索引并返回入参副本，有冲突就不写入，返回已存在的活跃票据。
    /// <para>
    /// 未来接持久化存储时，以（租户、应用、玩家、活跃）唯一索引承接同一约束；
    /// 冲突路径不得留下任何写入痕迹（包括玩家反查索引）。
    /// </para>
    /// </summary>
    /// <param name="ticket">待入队票据（Queued 态，作用域与成员已由调用方规整）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>生效票据副本：无冲突为刚写入的入参副本，有冲突为已存在的活跃票据副本。</returns>
    Task<OnlineMatchTicket> TryEnqueueIfAbsentAsync(OnlineMatchTicket ticket, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按标识查找票据（含终态历史）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="ticketId">票据标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>票据副本；不存在返回 null。</returns>
    Task<OnlineMatchTicket> FindAsync(long tenantId, long appId, string ticketId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 查找队伍/玩家当前处于排队中的票据（唯一性：同一队伍至多一张活跃票据）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="partyId">队伍标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>排队中的票据副本；无则返回 null。</returns>
    Task<OnlineMatchTicket> FindActiveByPartyAsync(long tenantId, long appId, string partyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 查找玩家当前处于排队中的票据（用于重复排队拦截与「我的匹配状态」查询）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>排队中的票据副本；无则返回 null。</returns>
    Task<OnlineMatchTicket> FindActiveByPlayerAsync(long tenantId, long appId, long playerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 查找玩家最近一张票据（任意状态；匹配域桥状态面读）。
    /// <para>
    /// 语义：玩家每次入队覆盖其反查位，故本方法返回「当前最近一次匹配尝试」——
    /// 排队中为 Queued 票据，已撮合为 Matched 终态票据（承载 AssignmentId），取消 / 过期为对应终态。
    /// </para>
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>最近票据副本；从未入队返回 null。</returns>
    Task<OnlineMatchTicket> FindLatestByPlayerAsync(long tenantId, long appId, long playerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 列出作用域内排队中的全部票据（成组与过期扫描的输入）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>排队中的票据副本列表。</returns>
    Task<IReadOnlyList<OnlineMatchTicket>> ListQueuedAsync(long tenantId, long appId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 列出作用域内全部票据（含终态历史；可观测性要求各状态计数可核对）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>票据副本列表。</returns>
    Task<IReadOnlyList<OnlineMatchTicket>> ListAllAsync(long tenantId, long appId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 以 CAS 语义更新单张票据状态（取消 / 过期 / 失败走此入口）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="transition">状态迁移载荷（票据定位 + 期望/目标状态 + 失败原因与分配标识）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>更新后的票据副本；CAS 失败或票据不存在返回 null。</returns>
    Task<OnlineMatchTicket> UpdateStateAsync(long tenantId, long appId, MatchTicketStateTransition transition, CancellationToken cancellationToken = default);

    /// <summary>
    /// 原子提交一次成组：校验全部票据仍处于期望状态，是则一次性全部置为
    /// <see cref="OnlineMatchTicketState.Matched"/> 并落档 assignment；否则整体不生效并返回 null。
    /// <para>该方法的原子性是「重复 assignment = 0」的实现依据。</para>
    /// </summary>
    /// <param name="assignment">待落档的对局分配。</param>
    /// <param name="ticketIds">本分配消费的票据标识集合（不得重复、不得为空）。</param>
    /// <param name="expectedState">期望的票据当前状态。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>落档后的分配副本；任一票据不满足前置条件返回 null。</returns>
    Task<OnlineMatchAssignment> CommitMatchAsync(OnlineMatchAssignment assignment, IReadOnlyList<string> ticketIds, OnlineMatchTicketState expectedState, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按标识查找对局分配。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="assignmentId">分配标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>分配副本；不存在返回 null。</returns>
    Task<OnlineMatchAssignment> FindAssignmentAsync(long tenantId, long appId, string assignmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 列出作用域内全部对局分配（可观测性与对账用）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>分配副本列表。</returns>
    Task<IReadOnlyList<OnlineMatchAssignment>> ListAssignmentsAsync(long tenantId, long appId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 聚合作用域 (TenantId, AppId, ServerId) 内排队态票据，按 (Mode, Region) 槽位产出队列深度与等待时长合计
    /// （聚合谓词下推：排队态判定与等待秒来源 <see cref="OnlineMatchRule.WaitSeconds"/> 均与在线总览口径一致）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="serverId">区服标识。</param>
    /// <param name="nowUnixMilliseconds">观测时刻（UTC 毫秒；等待时长按该时刻计算）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>槽位聚合列表（无排队票据时为空表）。</returns>
    Task<IReadOnlyList<OnlineMatchQueueSlotAggregate>> AggregateQueuedBySlotAsync(long tenantId, long appId, long serverId, long nowUnixMilliseconds, CancellationToken cancellationToken = default);

    /// <summary>
    /// 统计时间窗 <c>(windowStartExclusiveUnixMilliseconds, windowEndInclusiveUnixMilliseconds]</c> 内落档的匹配分配所消费的票据数，
    /// 按 (Mode, Region) 槽位聚合（聚合谓词下推：窗口左开右闭边界与在线总览吞吐口径一致）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="serverId">区服标识。</param>
    /// <param name="windowStartExclusiveUnixMilliseconds">窗口起点（UTC 毫秒；不含）。</param>
    /// <param name="windowEndInclusiveUnixMilliseconds">窗口终点（UTC 毫秒；含）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>槽位聚合列表（TotalWaitSeconds 恒为 0；窗口内无分配时为空表）。</returns>
    Task<IReadOnlyList<OnlineMatchQueueSlotAggregate>> CountAssignmentTicketsBySlotAsync(long tenantId, long appId, long serverId, long windowStartExclusiveUnixMilliseconds, long windowEndInclusiveUnixMilliseconds, CancellationToken cancellationToken = default);
}