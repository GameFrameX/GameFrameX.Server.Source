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

namespace GameFrameX.Online.Party;

/// <summary>
/// 队伍内存默认存储（vault:C5 S4.3：单进程/测试默认实现；持久化实现归运行时装配，X4）。
/// <para>
/// 维护约束（天花板）：全量驻留内存，进程重启即全部丢失——持久化与重启恢复由 Server 仓运行时装配
/// 以持久化实现替换（vault:C5 L3 层）；全局锁保护，读写均为轻量操作，粗粒度锁足够；
/// <c>_partyIdByPlayer</c> 索引在每次保存时按成员集合整体重建，不允许残留指向已移除成员的索引。
/// </para>
/// </summary>
public sealed class InMemoryOnlinePartyStore : IOnlinePartyStore
{
    /// <summary>全局读写锁。</summary>
    private readonly object _syncRoot = new object();

    /// <summary>队伍表：键 = (TenantId, AppId, PartyId)。</summary>
    private readonly Dictionary<string, OnlineParty> _parties = new Dictionary<string, OnlineParty>();

    /// <summary>玩家 → 队伍索引：键 = (TenantId, AppId, PlayerId)。</summary>
    private readonly Dictionary<string, string> _partyIdByPlayer = new Dictionary<string, string>();

    /// <summary>邀请表：键 = (TenantId, AppId, InviteId)。</summary>
    private readonly Dictionary<string, OnlinePartyInvite> _invites = new Dictionary<string, OnlinePartyInvite>();

    /// <summary>保存队伍（深拷贝入库并重建玩家索引）。</summary>
    /// <param name="party">队伍聚合。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task SavePartyAsync(OnlineParty party, CancellationToken cancellationToken = default)
    {
        if (party == null)
        {
            throw new ArgumentNullException(nameof(party));
        }

        lock (_syncRoot)
        {
            var key = BuildKey(party.TenantId, party.AppId, party.PartyId);
            _parties[key] = party.Copy();
            RebuildPlayerIndex(party.TenantId, party.AppId, party.PartyId);
        }

        return Task.CompletedTask;
    }

    /// <summary>按队伍标识查找队伍。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="partyId">队伍标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>队伍副本；不存在返回 null。</returns>
    public Task<OnlineParty> FindPartyAsync(long tenantId, long appId, string partyId, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            _parties.TryGetValue(BuildKey(tenantId, appId, partyId), out var party);
            return Task.FromResult(party == null ? null : party.Copy());
        }
    }

    /// <summary>按玩家反查其所在队伍。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>队伍副本；不在任何队伍返回 null。</returns>
    public Task<OnlineParty> FindPartyByPlayerAsync(long tenantId, long appId, long playerId, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            if (!_partyIdByPlayer.TryGetValue(BuildKey(tenantId, appId, playerId), out var partyId))
            {
                return Task.FromResult<OnlineParty>(null);
            }

            _parties.TryGetValue(BuildKey(tenantId, appId, partyId), out var party);
            return Task.FromResult(party == null ? null : party.Copy());
        }
    }

    /// <summary>移除队伍并清理其玩家索引。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="partyId">队伍标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task RemovePartyAsync(long tenantId, long appId, string partyId, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            _parties.Remove(BuildKey(tenantId, appId, partyId));
            RebuildPlayerIndex(tenantId, appId, partyId);
        }

        return Task.CompletedTask;
    }

    /// <summary>列出作用域内全部队伍。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>队伍副本列表。</returns>
    public Task<IReadOnlyList<OnlineParty>> ListPartiesAsync(long tenantId, long appId, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            var result = new List<OnlineParty>();
            foreach (var party in _parties.Values)
            {
                if (party.TenantId == tenantId && party.AppId == appId)
                {
                    result.Add(party.Copy());
                }
            }

            return Task.FromResult<IReadOnlyList<OnlineParty>>(result);
        }
    }

    /// <summary>保存邀请（深拷贝入库）。</summary>
    /// <param name="invite">邀请条目。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task SaveInviteAsync(OnlinePartyInvite invite, CancellationToken cancellationToken = default)
    {
        if (invite == null)
        {
            throw new ArgumentNullException(nameof(invite));
        }

        lock (_syncRoot)
        {
            _invites[BuildKey(invite.TenantId, invite.AppId, invite.InviteId)] = invite.Copy();
        }

        return Task.CompletedTask;
    }

    /// <summary>按邀请标识查找邀请。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="inviteId">邀请标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>邀请副本；不存在返回 null。</returns>
    public Task<OnlinePartyInvite> FindInviteAsync(long tenantId, long appId, string inviteId, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            _invites.TryGetValue(BuildKey(tenantId, appId, inviteId), out var invite);
            return Task.FromResult(invite == null ? null : invite.Copy());
        }
    }

    /// <summary>查找被邀请人的待答复邀请。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="inviteeId">被邀请玩家标识。</param>
    /// <param name="partyId">队伍标识（null 表示不限队伍）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>待答复邀请副本；无则返回 null。</returns>
    public Task<OnlinePartyInvite> FindPendingInviteAsync(long tenantId, long appId, long inviteeId, string partyId, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            foreach (var invite in _invites.Values)
            {
                if (invite.TenantId != tenantId || invite.AppId != appId || invite.InviteeId != inviteeId)
                {
                    continue;
                }

                if (invite.State != OnlinePartyInviteState.Pending)
                {
                    continue;
                }

                if (partyId != null && invite.PartyId != partyId)
                {
                    continue;
                }

                return Task.FromResult(invite.Copy());
            }

            return Task.FromResult<OnlinePartyInvite>(null);
        }
    }

    /// <summary>列出作用域内全部邀请。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>邀请副本列表。</returns>
    public Task<IReadOnlyList<OnlinePartyInvite>> ListInvitesAsync(long tenantId, long appId, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            var result = new List<OnlinePartyInvite>();
            foreach (var invite in _invites.Values)
            {
                if (invite.TenantId == tenantId && invite.AppId == appId)
                {
                    result.Add(invite.Copy());
                }
            }

            return Task.FromResult<IReadOnlyList<OnlinePartyInvite>>(result);
        }
    }

    /// <summary>按队伍当前成员集合整体重建玩家索引（先清该队伍的旧索引，再按成员重挂）。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="partyId">队伍标识。</param>
    private void RebuildPlayerIndex(long tenantId, long appId, string partyId)
    {
        var staleKeys = new List<string>();
        foreach (var pair in _partyIdByPlayer)
        {
            if (pair.Value == partyId && pair.Key.StartsWith(tenantId + ":" + appId + ":", StringComparison.Ordinal))
            {
                staleKeys.Add(pair.Key);
            }
        }

        foreach (var staleKey in staleKeys)
        {
            _partyIdByPlayer.Remove(staleKey);
        }

        if (!_parties.TryGetValue(BuildKey(tenantId, appId, partyId), out var party))
        {
            return;
        }

        if (OnlinePartyStateMachine.IsTerminal(party.State))
        {
            return;
        }

        foreach (var member in party.Members)
        {
            _partyIdByPlayer[BuildKey(tenantId, appId, member.PlayerId)] = partyId;
        }
    }

    /// <summary>构造玩家维度的存储键。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <returns>复合键字符串。</returns>
    private static string BuildKey(long tenantId, long appId, long playerId)
    {
        return BuildKey(tenantId, appId, playerId.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>构造存储键。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="id">标识（队伍或邀请）。</param>
    /// <returns>复合键字符串。</returns>
    private static string BuildKey(long tenantId, long appId, string id)
    {
        return tenantId + ":" + appId + ":" + id;
    }
}
