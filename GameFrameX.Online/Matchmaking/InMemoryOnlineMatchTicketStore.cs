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

    /// <inheritdoc />
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

    /// <inheritdoc />
    public Task<OnlineMatchTicket> FindAsync(long tenantId, long appId, string ticketId, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            return Task.FromResult(FindInternal(tenantId, appId, ticketId));
        }
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
    public Task<OnlineMatchTicket> FindActiveByPlayerAsync(long tenantId, long appId, long playerId, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            return Task.FromResult(FindActiveByPlayerInternal(tenantId, appId, playerId));
        }
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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
