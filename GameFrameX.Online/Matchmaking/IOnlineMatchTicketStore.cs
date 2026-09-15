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

namespace GameFrameX.Online.Matchmaking;

/// <summary>
/// 匹配票据存储契约（vault:C5 S4.5 / S4.8）。
/// <para>
/// 维护约束（红线，VC-4.2 / VC-4.12 的落点）：<see cref="CommitMatchAsync"/> 与
/// <see cref="UpdateStateAsync"/> 是**唯一**允许改写票据状态的入口，且必须提供
/// 「期望状态」参数——状态裁决采用 CAS 语义：期望状态不匹配即整体失败并返回 null，
/// 绝不部分应用。取消与匹配成功的竞态由此收敛到同一临界区：
/// 谁先改到终态，谁的结果生效，另一方的 CAS 必然失败。
/// </para>
/// <para>
/// 存储实现负责防御性深拷贝，且**不得**在 CAS 失败时留下任何写入痕迹
/// （包括玩家反查索引）。
/// </para>
/// </summary>
public interface IOnlineMatchTicketStore
{
    /// <summary>
    /// 保存票据（新增或覆盖；仅用于入队时的首次落库）。
    /// </summary>
    /// <param name="ticket">票据。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>完成通知。</returns>
    Task SaveAsync(OnlineMatchTicket ticket, CancellationToken cancellationToken = default);

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
    /// 查找队伍/玩家当前处于排队中的票据（VC-4.12 唯一性：同一队伍至多一张活跃票据）。
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
    /// 列出作用域内排队中的全部票据（成组与过期扫描的输入）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>排队中的票据副本列表。</returns>
    Task<IReadOnlyList<OnlineMatchTicket>> ListQueuedAsync(long tenantId, long appId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 列出作用域内全部票据（含终态历史；VC-4.10 可观测性要求各状态计数可核对）。
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
    /// <param name="ticketId">票据标识。</param>
    /// <param name="expectedState">期望的当前状态；不匹配即失败。</param>
    /// <param name="newState">目标状态。</param>
    /// <param name="failureReason">失败原因码（非失败转迁移填 <see cref="OnlineMatchFailureReason.None"/>）。</param>
    /// <param name="assignmentId">关联的分配标识（无则空字符串）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>更新后的票据副本；CAS 失败或票据不存在返回 null。</returns>
    Task<OnlineMatchTicket> UpdateStateAsync(long tenantId, long appId, string ticketId, OnlineMatchTicketState expectedState, OnlineMatchTicketState newState, OnlineMatchFailureReason failureReason, string assignmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 原子提交一次成组：校验全部票据仍处于期望状态，是则一次性全部置为
    /// <see cref="OnlineMatchTicketState.Matched"/> 并落档 assignment；否则整体不生效并返回 null。
    /// <para>该方法的原子性是「重复 assignment = 0」（VC-4.12）的实现依据。</para>
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
}
