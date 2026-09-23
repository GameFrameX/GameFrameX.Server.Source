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
/// 匹配票据存储的内存实现（单进程默认实现，vault:C5 启动前置：持久化归运行时装配）。
/// <para>
/// 维护约束（红线）：票据、玩家反查索引、对局分配三者在**同一把锁**下变更——
/// 这是 VC-4.2（取消不产生旧结果）与 VC-4.12（重复 assignment = 0）能够成立的前提。
/// 任何把三者拆到不同临界区、或让索引与票据状态分两次更新的改写，
/// 都会重新打开「取消与匹配成功同时生效」的竞态窗口。
/// </para>
/// <para>
/// 天花板（ponytail）：单锁全串行，票据基数上万后成组扫描会成为热点；
/// 升级路径是按 (TenantId, AppId) 分片加锁并保留同样的 CAS 语义，调用方无感。
/// </para>
/// </summary>
public sealed class InMemoryOnlineMatchTicketStore : IOnlineMatchTicketStore
{
    /// <summary>全局读写锁。</summary>
    private readonly object _syncRoot = new object();

    /// <summary>票据表：键 = (TenantId, AppId, TicketId)。</summary>
    private readonly Dictionary<string, OnlineMatchTicket> _tickets = new Dictionary<string, OnlineMatchTicket>();

    /// <summary>玩家反查索引：键 = (TenantId, AppId, PlayerId)，值 = 其排队中票据标识。</summary>
    private readonly Dictionary<string, string> _playerIndex = new Dictionary<string, string>();

    /// <summary>对局分配表：键 = (TenantId, AppId, AssignmentId)。</summary>
    private readonly Dictionary<string, OnlineMatchAssignment> _assignments = new Dictionary<string, OnlineMatchAssignment>();

    /// <summary>
    /// 深拷贝票据后写入内存票据表（新增或覆盖），票据处于排队态时同步写入玩家反查索引。
    /// </summary>
    /// <remarks>
    /// Stores a defensive copy of the ticket into the in-memory ticket table (insert or overwrite), and writes the player reverse-lookup index when the ticket is queued.
    /// </remarks>
    /// <param name="ticket">票据 / The match ticket</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>完成通知 / Completion notification</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="ticket"/> 为 null 时抛出 / Thrown when <paramref name="ticket"/> is null</exception>
    public Task SaveAsync(OnlineMatchTicket ticket, CancellationToken cancellationToken = default)
    {
        if (ticket == null)
        {
            throw new ArgumentNullException(nameof(ticket));
        }

        lock (_syncRoot)
        {
            _tickets[BuildKey(ticket.TenantId, ticket.AppId, ticket.TicketId)] = ticket.Copy();
            if (ticket.State == OnlineMatchTicketState.Queued)
            {
                IndexPlayers(ticket);
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 按作用域与票据标识从内存票据表读取票据副本（含终态历史）。
    /// </summary>
    /// <remarks>
    /// Reads a copy of the ticket from the in-memory ticket table by scope and ticket id, including terminal-state history.
    /// </remarks>
    /// <param name="tenantId">租户标识 / Tenant id</param>
    /// <param name="appId">App 标识 / App id</param>
    /// <param name="ticketId">票据标识 / Ticket id</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>票据副本；不存在返回 null / A copy of the ticket, or null when not found</returns>
    public Task<OnlineMatchTicket> FindAsync(long tenantId, long appId, string ticketId, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            return Task.FromResult(FindInternal(tenantId, appId, ticketId));
        }
    }

    /// <summary>
    /// 全表扫描内存票据表，返回同作用域内处于排队态且队伍标识匹配的首张票据副本；<paramref name="partyId"/> 为空直接返回 null。
    /// </summary>
    /// <remarks>
    /// Scans the in-memory ticket table and returns a copy of the first queued ticket whose party id matches within the scope; returns null immediately when <paramref name="partyId"/> is empty.
    /// </remarks>
    /// <param name="tenantId">租户标识 / Tenant id</param>
    /// <param name="appId">App 标识 / App id</param>
    /// <param name="partyId">队伍标识 / Party id</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>排队中的票据副本；无则返回 null / A copy of the queued ticket, or null when none exists</returns>
    public Task<OnlineMatchTicket> FindActiveByPartyAsync(long tenantId, long appId, string partyId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(partyId))
        {
            return Task.FromResult<OnlineMatchTicket>(null);
        }

        lock (_syncRoot)
        {
            foreach (var ticket in _tickets.Values)
            {
                if (ticket.TenantId == tenantId && ticket.AppId == appId && ticket.State == OnlineMatchTicketState.Queued && ticket.PartyId == partyId)
                {
                    return Task.FromResult(ticket.Copy());
                }
            }

            return Task.FromResult<OnlineMatchTicket>(null);
        }
    }

    /// <summary>
    /// 经玩家反查索引定位票据标识，并校验该票据仍处于排队态后返回其副本。
    /// </summary>
    /// <remarks>
    /// Locates the ticket id through the player reverse-lookup index, verifies the ticket is still queued, and returns its copy.
    /// </remarks>
    /// <param name="tenantId">租户标识 / Tenant id</param>
    /// <param name="appId">App 标识 / App id</param>
    /// <param name="playerId">玩家标识 / Player id</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>排队中的票据副本；无则返回 null / A copy of the queued ticket, or null when none exists</returns>
    public Task<OnlineMatchTicket> FindActiveByPlayerAsync(long tenantId, long appId, long playerId, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            return Task.FromResult(FindActiveByPlayerInternal(tenantId, appId, playerId));
        }
    }

    /// <summary>
    /// 遍历内存票据表，收集作用域内全部处于排队态的票据副本。
    /// </summary>
    /// <remarks>
    /// Iterates the in-memory ticket table and collects copies of all queued tickets within the scope.
    /// </remarks>
    /// <param name="tenantId">租户标识 / Tenant id</param>
    /// <param name="appId">App 标识 / App id</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>排队中的票据副本列表 / The list of queued ticket copies</returns>
    public Task<IReadOnlyList<OnlineMatchTicket>> ListQueuedAsync(long tenantId, long appId, CancellationToken cancellationToken = default)
    {
        var queued = new List<OnlineMatchTicket>();

        lock (_syncRoot)
        {
            foreach (var ticket in _tickets.Values)
            {
                if (ticket.TenantId == tenantId && ticket.AppId == appId && ticket.State == OnlineMatchTicketState.Queued)
                {
                    queued.Add(ticket.Copy());
                }
            }
        }

        return Task.FromResult<IReadOnlyList<OnlineMatchTicket>>(queued);
    }

    /// <summary>
    /// 遍历内存票据表，收集作用域内全部票据副本（含终态历史）。
    /// </summary>
    /// <remarks>
    /// Iterates the in-memory ticket table and collects copies of all tickets within the scope, including terminal-state history.
    /// </remarks>
    /// <param name="tenantId">租户标识 / Tenant id</param>
    /// <param name="appId">App 标识 / App id</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>票据副本列表 / The list of ticket copies</returns>
    public Task<IReadOnlyList<OnlineMatchTicket>> ListAllAsync(long tenantId, long appId, CancellationToken cancellationToken = default)
    {
        var tickets = new List<OnlineMatchTicket>();

        lock (_syncRoot)
        {
            foreach (var ticket in _tickets.Values)
            {
                if (ticket.TenantId == tenantId && ticket.AppId == appId)
                {
                    tickets.Add(ticket.Copy());
                }
            }
        }

        return Task.FromResult<IReadOnlyList<OnlineMatchTicket>>(tickets);
    }

    /// <summary>
    /// 以 CAS 语义更新单张票据状态：票据不存在或当前状态与期望不符时整体失败返回 null、不留任何写入痕迹；成功时写入目标状态、失败原因与分配标识，并在离开排队态时释放玩家反查索引。
    /// </summary>
    /// <remarks>
    /// Updates a single ticket state with CAS semantics: returns null without leaving any write trace when the ticket is missing or its current state does not match the expected one; on success writes the new state, failure reason and assignment id, and releases the player reverse-lookup index when leaving the queued state.
    /// </remarks>
    /// <param name="tenantId">租户标识 / Tenant id</param>
    /// <param name="appId">App 标识 / App id</param>
    /// <param name="ticketId">票据标识 / Ticket id</param>
    /// <param name="expectedState">期望的当前状态；不匹配即失败 / The expected current state; a mismatch fails the update</param>
    /// <param name="newState">目标状态 / The target state</param>
    /// <param name="failureReason">失败原因码；非失败转迁移填 None / The failure reason code; None for non-failure transitions</param>
    /// <param name="assignmentId">关联的分配标识；空字符串时保留原值 / The associated assignment id; an empty string keeps the original value</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>更新后的票据副本；CAS 失败或票据不存在返回 null / A copy of the updated ticket, or null when the CAS fails or the ticket does not exist</returns>
    public Task<OnlineMatchTicket> UpdateStateAsync(long tenantId, long appId, string ticketId, OnlineMatchTicketState expectedState, OnlineMatchTicketState newState, OnlineMatchFailureReason failureReason, string assignmentId, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            var current = FindInternal(tenantId, appId, ticketId);
            if (current == null || current.State != expectedState)
            {
                return Task.FromResult<OnlineMatchTicket>(null);
            }

            var stored = _tickets[BuildKey(tenantId, appId, ticketId)];
            stored.State = newState;
            stored.FailureReason = failureReason;
            stored.AssignmentId = string.IsNullOrEmpty(assignmentId) ? stored.AssignmentId : assignmentId;

            if (newState != OnlineMatchTicketState.Queued)
            {
                ReleasePlayers(stored);
            }

            return Task.FromResult(stored.Copy());
        }
    }

    /// <summary>
    /// 在同一临界区内原子提交一次成组：票据标识集合含空标识、重复标识，或任一票据不存在、不处于期望状态时整体不生效并返回 null；全部满足时一次性把票据置为 Matched、清空失败原因、写入分配标识、释放玩家反查索引并落档分配副本。
    /// </summary>
    /// <remarks>
    /// Atomically commits a group within a single critical section: when the ticket id collection contains an empty or duplicate id, or any ticket is missing or not in the expected state, nothing is applied and null is returned; otherwise all tickets are set to Matched at once, their failure reasons cleared, assignment ids written, player reverse-lookup indexes released, and a copy of the assignment archived.
    /// </remarks>
    /// <param name="assignment">待落档的对局分配 / The match assignment to archive</param>
    /// <param name="ticketIds">本分配消费的票据标识集合（不得重复、不得为空）/ The ticket ids consumed by this assignment (no duplicates, non-empty)</param>
    /// <param name="expectedState">期望的票据当前状态 / The expected current state of the tickets</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>落档后的分配副本；任一前置条件不满足返回 null / A copy of the archived assignment, or null when any precondition is not met</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="assignment"/> 为 null 时抛出 / Thrown when <paramref name="assignment"/> is null</exception>
    public Task<OnlineMatchAssignment> CommitMatchAsync(OnlineMatchAssignment assignment, IReadOnlyList<string> ticketIds, OnlineMatchTicketState expectedState, CancellationToken cancellationToken = default)
    {
        if (assignment == null)
        {
            throw new ArgumentNullException(nameof(assignment));
        }

        if (ticketIds == null || ticketIds.Count == 0)
        {
            return Task.FromResult<OnlineMatchAssignment>(null);
        }

        lock (_syncRoot)
        {
            var seen = new HashSet<string>();
            var resolved = new List<OnlineMatchTicket>(ticketIds.Count);

            foreach (var ticketId in ticketIds)
            {
                if (string.IsNullOrEmpty(ticketId) || !seen.Add(ticketId))
                {
                    return Task.FromResult<OnlineMatchAssignment>(null);
                }

                var current = FindInternal(assignment.TenantId, assignment.AppId, ticketId);
                if (current == null || current.State != expectedState)
                {
                    return Task.FromResult<OnlineMatchAssignment>(null);
                }

                resolved.Add(_tickets[BuildKey(assignment.TenantId, assignment.AppId, ticketId)]);
            }

            var storedAssignment = assignment.Copy();
            foreach (var stored in resolved)
            {
                stored.State = OnlineMatchTicketState.Matched;
                stored.FailureReason = OnlineMatchFailureReason.None;
                stored.AssignmentId = storedAssignment.AssignmentId;
                ReleasePlayers(stored);
            }

            _assignments[BuildKey(assignment.TenantId, assignment.AppId, assignment.AssignmentId)] = storedAssignment;

            return Task.FromResult(storedAssignment.Copy());
        }
    }

    /// <summary>
    /// 按作用域与分配标识从内存分配表读取分配副本；<paramref name="assignmentId"/> 为空直接返回 null。
    /// </summary>
    /// <remarks>
    /// Reads a copy of the assignment from the in-memory assignment table by scope and assignment id; returns null immediately when <paramref name="assignmentId"/> is empty.
    /// </remarks>
    /// <param name="tenantId">租户标识 / Tenant id</param>
    /// <param name="appId">App 标识 / App id</param>
    /// <param name="assignmentId">分配标识 / Assignment id</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>分配副本；不存在返回 null / A copy of the assignment, or null when not found</returns>
    public Task<OnlineMatchAssignment> FindAssignmentAsync(long tenantId, long appId, string assignmentId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(assignmentId))
        {
            return Task.FromResult<OnlineMatchAssignment>(null);
        }

        lock (_syncRoot)
        {
            if (_assignments.TryGetValue(BuildKey(tenantId, appId, assignmentId), out var assignment))
            {
                return Task.FromResult(assignment.Copy());
            }

            return Task.FromResult<OnlineMatchAssignment>(null);
        }
    }

    /// <summary>
    /// 遍历内存分配表，收集作用域内全部对局分配副本。
    /// </summary>
    /// <remarks>
    /// Iterates the in-memory assignment table and collects copies of all match assignments within the scope.
    /// </remarks>
    /// <param name="tenantId">租户标识 / Tenant id</param>
    /// <param name="appId">App 标识 / App id</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>分配副本列表 / The list of assignment copies</returns>
    public Task<IReadOnlyList<OnlineMatchAssignment>> ListAssignmentsAsync(long tenantId, long appId, CancellationToken cancellationToken = default)
    {
        var assignments = new List<OnlineMatchAssignment>();

        lock (_syncRoot)
        {
            foreach (var assignment in _assignments.Values)
            {
                if (assignment.TenantId == tenantId && assignment.AppId == appId)
                {
                    assignments.Add(assignment.Copy());
                }
            }
        }

        return Task.FromResult<IReadOnlyList<OnlineMatchAssignment>>(assignments);
    }

    /// <summary>
    /// 按标识读取票据副本（调用方必须已持有 <see cref="_syncRoot"/>）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="ticketId">票据标识。</param>
    /// <returns>票据副本；不存在返回 null。</returns>
    private OnlineMatchTicket FindInternal(long tenantId, long appId, string ticketId)
    {
        if (string.IsNullOrEmpty(ticketId))
        {
            return null;
        }

        if (_tickets.TryGetValue(BuildKey(tenantId, appId, ticketId), out var ticket))
        {
            return ticket.Copy();
        }

        return null;
    }

    /// <summary>
    /// 按玩家读取其排队中票据副本（调用方必须已持有 <see cref="_syncRoot"/>）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <returns>票据副本；无则返回 null。</returns>
    private OnlineMatchTicket FindActiveByPlayerInternal(long tenantId, long appId, long playerId)
    {
        if (!_playerIndex.TryGetValue(BuildKey(tenantId, appId, playerId.ToString()), out var ticketId))
        {
            return null;
        }

        var ticket = FindInternal(tenantId, appId, ticketId);
        if (ticket == null || ticket.State != OnlineMatchTicketState.Queued)
        {
            return null;
        }

        return ticket;
    }

    /// <summary>
    /// 把票据携带的玩家写入反查索引（调用方必须已持有 <see cref="_syncRoot"/>）。
    /// </summary>
    /// <param name="ticket">票据。</param>
    private void IndexPlayers(OnlineMatchTicket ticket)
    {
        if (ticket.PlayerIds == null)
        {
            return;
        }

        foreach (var playerId in ticket.PlayerIds)
        {
            _playerIndex[BuildKey(ticket.TenantId, ticket.AppId, playerId.ToString())] = ticket.TicketId;
        }
    }

    /// <summary>
    /// 释放票据携带的玩家反查索引（调用方必须已持有 <see cref="_syncRoot"/>）。
    /// <para>
    /// 仅删除仍指向本票据的条目——同一玩家若已进入新票据（理论上被唯一性拦截，
    /// 但索引必须自愈），不得误删新票据的索引。
    /// </para>
    /// </summary>
    /// <param name="ticket">票据。</param>
    private void ReleasePlayers(OnlineMatchTicket ticket)
    {
        if (ticket.PlayerIds == null)
        {
            return;
        }

        foreach (var playerId in ticket.PlayerIds)
        {
            var key = BuildKey(ticket.TenantId, ticket.AppId, playerId.ToString());
            if (_playerIndex.TryGetValue(key, out var indexedTicketId) && indexedTicketId == ticket.TicketId)
            {
                _playerIndex.Remove(key);
            }
        }
    }

    /// <summary>
    /// 构造存储键。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="id">末段标识。</param>
    /// <returns>存储键。</returns>
    private static string BuildKey(long tenantId, long appId, string id)
    {
        return tenantId + ":" + appId + ":" + id;
    }
}
