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

using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Online.Contracts;
using GameFrameX.Online.Events;
using GameFrameX.Online.Scope;

/// <summary>
/// 队伍服务（vault:C5 S4.3、S4.7：队伍生命周期的唯一写者）。
/// <para>
/// 维护约束（红线）：
/// ① 状态迁移唯一判据是 <see cref="OnlinePartyStateMachine.TryTransition"/>，表外迁移映射
/// <see cref="OnlineErrorCode.StateOperationForbidden"/>，终态出边为空且映射
/// <see cref="OnlineErrorCode.StateEnded"/>——取消/解散后不可复活（vault:C5 S4.3：终态唯一）；
/// ② <see cref="OnlineParty.LeaderId"/> 必须始终是成员集合中的一员：队长退出要么转移给加入最早者、
/// 要么解散，绝不产生孤儿队长（VC-4.6）；
/// ③ 成员在线状态不落库，只在入队前经 <see cref="IOnlinePartyPresenceProbe"/> 读取唯一在线事实源
/// （VC-4.7）；
/// ④ 玩家主体位必须有效（PlayerId &lt;= 0 拒绝），跨玩家操作以 <c>PlayerId</c> 归属校验，
/// 不匹配一律 <see cref="OnlineErrorCode.ResourceNotFound"/>（反预言，不泄露他人队伍存在性）。
/// </para>
/// </summary>
public sealed class OnlinePartyService
{
    /// <summary>队伍存储。</summary>
    private readonly IOnlinePartyStore _store;

    /// <summary>事件发布出口。</summary>
    private readonly IOnlineEventPublisher _eventPublisher;

    /// <summary>在线事实探针（可空：未装配时离线清理退化为「不执行」而非误判全员离线）。</summary>
    private readonly IOnlinePartyPresenceProbe _presenceProbe;

    /// <summary>人数下限。</summary>
    private readonly int _minMembers;

    /// <summary>人数上限。</summary>
    private readonly int _maxMembers;

    /// <summary>邀请存活时长（秒）。</summary>
    private readonly long _inviteTimeToLiveSeconds;

    /// <summary>队伍空闲存活时长（秒）。</summary>
    private readonly long _idleTimeToLiveSeconds;

    /// <summary>
    /// 初始化 <see cref="OnlinePartyService"/>。
    /// </summary>
    /// <param name="store">队伍存储。</param>
    /// <param name="eventPublisher">事件发布出口。</param>
    /// <param name="minMembers">人数下限（默认 2）。</param>
    /// <param name="maxMembers">人数上限（默认 8）。</param>
    /// <param name="inviteTimeToLiveSeconds">邀请存活时长（秒；默认 120）。</param>
    /// <param name="idleTimeToLiveSeconds">队伍空闲存活时长（秒；默认 1800）。</param>
    /// <param name="presenceProbe">在线事实探针（可空）。</param>
    public OnlinePartyService(
        IOnlinePartyStore store,
        IOnlineEventPublisher eventPublisher,
        int minMembers = 2,
        int maxMembers = 8,
        long inviteTimeToLiveSeconds = 120,
        long idleTimeToLiveSeconds = 1800,
        IOnlinePartyPresenceProbe presenceProbe = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        _presenceProbe = presenceProbe;
        _minMembers = minMembers;
        _maxMembers = maxMembers;
        _inviteTimeToLiveSeconds = inviteTimeToLiveSeconds;
        _idleTimeToLiveSeconds = idleTimeToLiveSeconds;
    }

    /// <summary>
    /// 创建队伍（发起人即队长并立即成为首个成员，状态 <see cref="OnlinePartyState.Created"/>）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>新建队伍；发起人已在其他队伍时返回既有队伍（幂等）。</returns>
    public async Task<OnlineResult<OnlineParty>> CreateAsync(OnlineScope scope, string correlationId = null, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope(scope);
        if (failure != null)
        {
            return failure;
        }

        var existing = await _store.FindPartyByPlayerAsync(scope.TenantId, scope.AppId, scope.PlayerId, cancellationToken);
        if (existing != null)
        {
            return OnlineResult<OnlineParty>.Ok(existing);
        }

        var now = Now();
        var party = new OnlineParty
        {
            PartyId = "pty-" + Guid.NewGuid().ToString("N"),
            TenantId = scope.TenantId,
            AppId = scope.AppId,
            ServerId = scope.ServerId,
            LeaderId = scope.PlayerId,
            State = OnlinePartyState.Created,
            Members = new List<OnlinePartyMember>
            {
                new OnlinePartyMember
                {
                    PlayerId = scope.PlayerId,
                    MemberState = OnlinePartyMemberState.Joined,
                    JoinedAtTime = now,
                },
            },
            MinMembers = _minMembers,
            MaxMembers = _maxMembers,
            CreatedAtTime = now,
            UpdatedAtTime = now,
            ExpiresAtTime = now + (_idleTimeToLiveSeconds * 1000),
        };
        await _store.SavePartyAsync(party, cancellationToken);
        return OnlineResult<OnlineParty>.Ok(party);
    }

    /// <summary>
    /// 邀请玩家入队（仅队长可邀请；重复邀请返回既有待答复邀请，不产生第二条）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="partyId">队伍标识。</param>
    /// <param name="inviteeId">被邀请玩家标识。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>邀请条目。</returns>
    public async Task<OnlineResult<OnlinePartyInvite>> InviteAsync(OnlineScope scope, string partyId, long inviteeId, string correlationId = null, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope<OnlinePartyInvite>(scope);
        if (failure != null)
        {
            return failure;
        }

        if (inviteeId <= 0)
        {
            return OnlineResult<OnlinePartyInvite>.Fail(OnlineErrorCode.ParameterInvalid, "被邀请玩家标识无效");
        }

        var party = await _store.FindPartyAsync(scope.TenantId, scope.AppId, partyId, cancellationToken);
        var reachable = EnsureLeaderReachable(party, scope.PlayerId);
        if (reachable != null)
        {
            return OnlineResult<OnlinePartyInvite>.Fail(reachable.Code, reachable.Message);
        }

        if (party.Members.Count >= party.MaxMembers)
        {
            return OnlineResult<OnlinePartyInvite>.Fail(OnlineErrorCode.StateNotReady, "队伍人数已达上限");
        }

        var existingInvite = await _store.FindPendingInviteAsync(scope.TenantId, scope.AppId, inviteeId, partyId, cancellationToken);
        if (existingInvite != null)
        {
            return OnlineResult<OnlinePartyInvite>.Ok(existingInvite);
        }

        var target = await _store.FindPartyByPlayerAsync(scope.TenantId, scope.AppId, inviteeId, cancellationToken);
        if (target != null)
        {
            return OnlineResult<OnlinePartyInvite>.Fail(OnlineErrorCode.DuplicateRequest, "被邀请玩家已在其它队伍中");
        }

        var now = Now();
        var invite = new OnlinePartyInvite
        {
            InviteId = "inv-" + Guid.NewGuid().ToString("N"),
            PartyId = party.PartyId,
            TenantId = scope.TenantId,
            AppId = scope.AppId,
            InviterId = scope.PlayerId,
            InviteeId = inviteeId,
            State = OnlinePartyInviteState.Pending,
            CreatedAtTime = now,
            ExpiresAtTime = now + (_inviteTimeToLiveSeconds * 1000),
        };
        await _store.SaveInviteAsync(invite, cancellationToken);
        await _eventPublisher.PublishAsync(OnlinePartyEvents.CreateInviteChanged(invite, party.ServerId, correlationId), cancellationToken);

        var invited = await TransitionAsync(party, OnlinePartyState.Inviting, OnlinePartyLeaveReason.Left, correlationId, cancellationToken);
        if (!invited.IsSuccess)
        {
            return OnlineResult<OnlinePartyInvite>.Fail(invited.Code, invited.Message);
        }

        return OnlineResult<OnlinePartyInvite>.Ok(invite);
    }

    /// <summary>
    /// 答复邀请（接受或拒绝；仅被邀请人本人可答复，终态邀请返回既有终态而非报错）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="inviteId">邀请标识。</param>
    /// <param name="accept">接受传 <c>true</c>，拒绝传 <c>false</c>。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>答复后的队伍（拒绝时为答复前队伍快照）。</returns>
    public async Task<OnlineResult<OnlineParty>> AnswerInviteAsync(OnlineScope scope, string inviteId, bool accept, string correlationId = null, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope(scope);
        if (failure != null)
        {
            return failure;
        }

        var invite = await _store.FindInviteAsync(scope.TenantId, scope.AppId, inviteId, cancellationToken);
        if (invite == null || invite.InviteeId != scope.PlayerId)
        {
            return OnlineResult<OnlineParty>.Fail(OnlineErrorCode.ResourceNotFound, "邀请不存在");
        }

        var party = await _store.FindPartyAsync(scope.TenantId, scope.AppId, invite.PartyId, cancellationToken);
        if (party == null)
        {
            return OnlineResult<OnlineParty>.Fail(OnlineErrorCode.ResourceNotFound, "队伍不存在");
        }

        if (invite.State != OnlinePartyInviteState.Pending)
        {
            return OnlineResult<OnlineParty>.Ok(party);
        }

        if (Now() >= invite.ExpiresAtTime)
        {
            invite.State = OnlinePartyInviteState.Expired;
            await _store.SaveInviteAsync(invite, cancellationToken);
            await _eventPublisher.PublishAsync(OnlinePartyEvents.CreateInviteChanged(invite, party.ServerId, correlationId), cancellationToken);
            return OnlineResult<OnlineParty>.Fail(OnlineErrorCode.StateEnded, "邀请已过期");
        }

        if (!accept)
        {
            invite.State = OnlinePartyInviteState.Rejected;
            await _store.SaveInviteAsync(invite, cancellationToken);
            await _eventPublisher.PublishAsync(OnlinePartyEvents.CreateInviteChanged(invite, party.ServerId, correlationId), cancellationToken);
            return OnlineResult<OnlineParty>.Ok(party);
        }

        if (party.Members.Count >= party.MaxMembers)
        {
            return OnlineResult<OnlineParty>.Fail(OnlineErrorCode.StateNotReady, "队伍人数已达上限");
        }

        if (FindMember(party, scope.PlayerId) == null)
        {
            party.Members.Add(new OnlinePartyMember
            {
                PlayerId = scope.PlayerId,
                MemberState = OnlinePartyMemberState.Joined,
                JoinedAtTime = Now(),
            });
        }

        invite.State = OnlinePartyInviteState.Accepted;
        await _store.SaveInviteAsync(invite, cancellationToken);
        await _eventPublisher.PublishAsync(OnlinePartyEvents.CreateInviteChanged(invite, party.ServerId, correlationId), cancellationToken);
        return await RefreshAfterMemberChangeAsync(party, OnlinePartyLeaveReason.Left, correlationId, cancellationToken);
    }

    /// <summary>
    /// 直接加入队伍（无需邀请的公开队伍入口；已在其他队伍的玩家拒绝）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="partyId">队伍标识。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>加入后的队伍。</returns>
    public async Task<OnlineResult<OnlineParty>> JoinAsync(OnlineScope scope, string partyId, string correlationId = null, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope(scope);
        if (failure != null)
        {
            return failure;
        }

        var party = await _store.FindPartyAsync(scope.TenantId, scope.AppId, partyId, cancellationToken);
        if (party == null)
        {
            return OnlineResult<OnlineParty>.Fail(OnlineErrorCode.ResourceNotFound, "队伍不存在");
        }

        if (OnlinePartyStateMachine.IsTerminal(party.State))
        {
            return OnlineResult<OnlineParty>.Fail(OnlineErrorCode.StateEnded, "队伍已结束：" + party.State);
        }

        if (party.State == OnlinePartyState.Matching || party.State == OnlinePartyState.Matched)
        {
            return OnlineResult<OnlineParty>.Fail(OnlineErrorCode.StateOperationForbidden, "匹配中的队伍不接受新成员");
        }

        if (FindMember(party, scope.PlayerId) != null)
        {
            return OnlineResult<OnlineParty>.Ok(party);
        }

        var occupied = await _store.FindPartyByPlayerAsync(scope.TenantId, scope.AppId, scope.PlayerId, cancellationToken);
        if (occupied != null)
        {
            return OnlineResult<OnlineParty>.Fail(OnlineErrorCode.DuplicateRequest, "玩家已在其它队伍中");
        }

        if (party.Members.Count >= party.MaxMembers)
        {
            return OnlineResult<OnlineParty>.Fail(OnlineErrorCode.StateNotReady, "队伍人数已达上限");
        }

        party.Members.Add(new OnlinePartyMember
        {
            PlayerId = scope.PlayerId,
            MemberState = OnlinePartyMemberState.Joined,
            JoinedAtTime = Now(),
        });
        return await RefreshAfterMemberChangeAsync(party, OnlinePartyLeaveReason.Left, correlationId, cancellationToken);
    }

    /// <summary>
    /// 退出队伍（S4.7 队长退出规则：转移给加入最早者，无人可转移则解散）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="reason">离开原因（审计用）。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>退出后的队伍（退队者已不在成员列表中）。</returns>
    public async Task<OnlineResult<OnlineParty>> LeaveAsync(OnlineScope scope, OnlinePartyLeaveReason reason = OnlinePartyLeaveReason.Left, string correlationId = null, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope(scope);
        if (failure != null)
        {
            return failure;
        }

        var party = await _store.FindPartyByPlayerAsync(scope.TenantId, scope.AppId, scope.PlayerId, cancellationToken);
        if (party == null)
        {
            return OnlineResult<OnlineParty>.Fail(OnlineErrorCode.ResourceNotFound, "玩家不在任何队伍中");
        }

        return await RemoveMemberAsync(party, scope.PlayerId, reason, correlationId, cancellationToken);
    }

    /// <summary>
    /// 踢出成员（仅队长可执行；不能踢自己——队长退出走 <see cref="LeaveAsync"/>）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="partyId">队伍标识。</param>
    /// <param name="memberId">被踢成员标识。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>踢出后的队伍。</returns>
    public async Task<OnlineResult<OnlineParty>> KickAsync(OnlineScope scope, string partyId, long memberId, string correlationId = null, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope(scope);
        if (failure != null)
        {
            return failure;
        }

        var party = await _store.FindPartyAsync(scope.TenantId, scope.AppId, partyId, cancellationToken);
        var reachable = EnsureLeaderReachable(party, scope.PlayerId);
        if (reachable != null)
        {
            return reachable;
        }

        if (memberId == party.LeaderId)
        {
            return OnlineResult<OnlineParty>.Fail(OnlineErrorCode.ParameterInvalid, "队长不能被踢出，请先转移队长或退出队伍");
        }

        if (FindMember(party, memberId) == null)
        {
            return OnlineResult<OnlineParty>.Fail(OnlineErrorCode.ResourceNotFound, "目标玩家不在队伍中");
        }

        return await RemoveMemberAsync(party, memberId, OnlinePartyLeaveReason.Kicked, correlationId, cancellationToken);
    }

    /// <summary>
    /// 转移队长（仅现任队长可执行；目标必须是本队成员）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="partyId">队伍标识。</param>
    /// <param name="newLeaderId">新队长标识。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>转移后的队伍。</returns>
    public async Task<OnlineResult<OnlineParty>> TransferLeaderAsync(OnlineScope scope, string partyId, long newLeaderId, string correlationId = null, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope(scope);
        if (failure != null)
        {
            return failure;
        }

        var party = await _store.FindPartyAsync(scope.TenantId, scope.AppId, partyId, cancellationToken);
        var reachable = EnsureLeaderReachable(party, scope.PlayerId);
        if (reachable != null)
        {
            return reachable;
        }

        if (FindMember(party, newLeaderId) == null)
        {
            return OnlineResult<OnlineParty>.Fail(OnlineErrorCode.ResourceNotFound, "目标玩家不在队伍中");
        }

        party.LeaderId = newLeaderId;
        party.UpdatedAtTime = Now();
        party.ExpiresAtTime = party.UpdatedAtTime + (_idleTimeToLiveSeconds * 1000);
        await _store.SavePartyAsync(party, cancellationToken);
        return OnlineResult<OnlineParty>.Ok(party);
    }

    /// <summary>
    /// 设置本人准备状态（全员准备后队伍转 <see cref="OnlinePartyState.Ready"/>）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="ready">准备传 <c>true</c>，取消准备传 <c>false</c>。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>设置后的队伍。</returns>
    public async Task<OnlineResult<OnlineParty>> SetReadyAsync(OnlineScope scope, bool ready, string correlationId = null, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope(scope);
        if (failure != null)
        {
            return failure;
        }

        var party = await _store.FindPartyByPlayerAsync(scope.TenantId, scope.AppId, scope.PlayerId, cancellationToken);
        if (party == null)
        {
            return OnlineResult<OnlineParty>.Fail(OnlineErrorCode.ResourceNotFound, "玩家不在任何队伍中");
        }

        var member = FindMember(party, scope.PlayerId);
        if (member == null)
        {
            return OnlineResult<OnlineParty>.Fail(OnlineErrorCode.ResourceNotFound, "玩家不在任何队伍中");
        }

        if (party.State == OnlinePartyState.Matching || party.State == OnlinePartyState.Matched)
        {
            return OnlineResult<OnlineParty>.Fail(OnlineErrorCode.StateOperationForbidden, "匹配中不允许变更准备状态");
        }

        member.MemberState = ready ? OnlinePartyMemberState.Ready : OnlinePartyMemberState.Joined;
        return await RefreshAfterMemberChangeAsync(party, OnlinePartyLeaveReason.Left, correlationId, cancellationToken);
    }

    /// <summary>
    /// 取消组队（幂等：已取消/已解散返回既有终态快照，重复取消不报错）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>取消后的队伍。</returns>
    public async Task<OnlineResult<OnlineParty>> CancelAsync(OnlineScope scope, string correlationId = null, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope(scope);
        if (failure != null)
        {
            return failure;
        }

        var party = await _store.FindPartyByPlayerAsync(scope.TenantId, scope.AppId, scope.PlayerId, cancellationToken);
        if (party == null)
        {
            return OnlineResult<OnlineParty>.Fail(OnlineErrorCode.ResourceNotFound, "玩家不在任何队伍中");
        }

        if (party.State == OnlinePartyState.Cancelled)
        {
            return OnlineResult<OnlineParty>.Ok(party);
        }

        return await TransitionAsync(party, OnlinePartyState.Cancelled, OnlinePartyLeaveReason.Disbanded, correlationId, cancellationToken);
    }

    /// <summary>
    /// 解散队伍（仅队长可执行；已解散返回既有终态快照）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="partyId">队伍标识。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>解散后的队伍。</returns>
    public async Task<OnlineResult<OnlineParty>> DisbandAsync(OnlineScope scope, string partyId, string correlationId = null, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope(scope);
        if (failure != null)
        {
            return failure;
        }

        var party = await _store.FindPartyAsync(scope.TenantId, scope.AppId, partyId, cancellationToken);
        var reachable = EnsureLeaderReachable(party, scope.PlayerId);
        if (reachable != null)
        {
            return reachable;
        }

        if (party.State == OnlinePartyState.Disbanded)
        {
            return OnlineResult<OnlineParty>.Ok(party);
        }

        return await TransitionAsync(party, OnlinePartyState.Disbanded, OnlinePartyLeaveReason.Disbanded, correlationId, cancellationToken);
    }

    /// <summary>
    /// 清理离线成员（S4.7 / VC-4.7：入队前以唯一在线事实源裁决，结果是确定的）。
    /// <para>
    /// 规则（确定性、可解释）：非队长成员离线即移除；队长离线**不**触发转移——队长身份与在线状态解耦，
    /// 离线队长在 Presence 重连窗口内可恢复。移除后人数跌破下限则队伍转 <see cref="OnlinePartyState.Left"/>，
    /// 否则保持原状态。未装配探针时不做任何清理（不误判为全员离线）。
    /// </para>
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位，且必须是队长）。</param>
    /// <param name="partyId">队伍标识。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>清理后的队伍。</returns>
    public async Task<OnlineResult<OnlineParty>> PruneOfflineMembersAsync(OnlineScope scope, string partyId, string correlationId = null, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope(scope);
        if (failure != null)
        {
            return failure;
        }

        var party = await _store.FindPartyAsync(scope.TenantId, scope.AppId, partyId, cancellationToken);
        var reachable = EnsureLeaderReachable(party, scope.PlayerId);
        if (reachable != null)
        {
            return reachable;
        }

        if (_presenceProbe == null)
        {
            return OnlineResult<OnlineParty>.Ok(party);
        }

        var offline = new List<long>();
        foreach (var member in party.Members)
        {
            if (member.PlayerId == party.LeaderId)
            {
                continue;
            }

            var online = await _presenceProbe.IsOnlineAsync(scope.TenantId, scope.AppId, member.PlayerId, cancellationToken);
            if (!online)
            {
                offline.Add(member.PlayerId);
            }
        }

        if (offline.Count == 0)
        {
            return OnlineResult<OnlineParty>.Ok(party);
        }

        foreach (var playerId in offline)
        {
            party.Members.RemoveAll(m => m.PlayerId == playerId);
        }

        return await RefreshAfterMemberChangeAsync(party, OnlinePartyLeaveReason.OfflineRemoved, correlationId, cancellationToken);
    }

    /// <summary>
    /// 查询队伍（跨作用域返回未找到，不泄露存在性）。
    /// </summary>
    /// <param name="scope">生效作用域。</param>
    /// <param name="partyId">队伍标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>队伍快照。</returns>
    public async Task<OnlineResult<OnlineParty>> GetAsync(OnlineScope scope, string partyId, CancellationToken cancellationToken = default)
    {
        if (scope == null)
        {
            return OnlineResult<OnlineParty>.Fail(OnlineErrorCode.ParameterInvalid, "缺少作用域");
        }

        var party = await _store.FindPartyAsync(scope.TenantId, scope.AppId, partyId, cancellationToken);
        if (party == null)
        {
            return OnlineResult<OnlineParty>.Fail(OnlineErrorCode.ResourceNotFound, "队伍不存在");
        }

        return OnlineResult<OnlineParty>.Ok(party);
    }

    /// <summary>
    /// 查询玩家当前所在队伍。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>队伍快照；不在任何队伍返回未找到。</returns>
    public async Task<OnlineResult<OnlineParty>> FindMyPartyAsync(OnlineScope scope, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope(scope);
        if (failure != null)
        {
            return failure;
        }

        var party = await _store.FindPartyByPlayerAsync(scope.TenantId, scope.AppId, scope.PlayerId, cancellationToken);
        if (party == null)
        {
            return OnlineResult<OnlineParty>.Fail(OnlineErrorCode.ResourceNotFound, "玩家不在任何队伍中");
        }

        return OnlineResult<OnlineParty>.Ok(party);
    }

    /// <summary>
    /// 列出玩家待答复的邀请。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>待答复邀请列表。</returns>
    public async Task<OnlineResult<IReadOnlyList<OnlinePartyInvite>>> ListPendingInvitesAsync(OnlineScope scope, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope<IReadOnlyList<OnlinePartyInvite>>(scope);
        if (failure != null)
        {
            return failure;
        }

        var invites = await _store.ListInvitesAsync(scope.TenantId, scope.AppId, cancellationToken);
        var result = new List<OnlinePartyInvite>();
        foreach (var invite in invites)
        {
            if (invite.InviteeId == scope.PlayerId && invite.State == OnlinePartyInviteState.Pending)
            {
                result.Add(invite);
            }
        }

        return OnlineResult<IReadOnlyList<OnlinePartyInvite>>.Ok(result);
    }

    /// <summary>
    /// 按匹配流程裁决结果迁移队伍状态（匹配域对本域的唯一写入口）。
    /// <para>
    /// 维护约束：仅允许迁入 <see cref="OnlinePartyState.Matching"/>、
    /// <see cref="OnlinePartyState.Matched"/>、<see cref="OnlinePartyState.Failed"/>、
    /// <see cref="OnlinePartyState.Ready"/> 四态，且必须过状态机；其余目标一律
    /// <see cref="OnlineErrorCode.ParameterInvalid"/>，防止匹配域越权改写队伍生命周期。
    /// </para>
    /// </summary>
    /// <param name="scope">生效作用域。</param>
    /// <param name="partyId">队伍标识。</param>
    /// <param name="targetState">目标状态。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>迁移后的队伍。</returns>
    public async Task<OnlineResult<OnlineParty>> BindMatchStateAsync(OnlineScope scope, string partyId, OnlinePartyState targetState, string correlationId = null, CancellationToken cancellationToken = default)
    {
        if (scope == null)
        {
            return OnlineResult<OnlineParty>.Fail(OnlineErrorCode.ParameterInvalid, "缺少作用域");
        }

        if (targetState != OnlinePartyState.Matching
            && targetState != OnlinePartyState.Matched
            && targetState != OnlinePartyState.Failed
            && targetState != OnlinePartyState.Ready)
        {
            return OnlineResult<OnlineParty>.Fail(OnlineErrorCode.ParameterInvalid, "匹配域不允许迁入状态：" + targetState);
        }

        var party = await _store.FindPartyAsync(scope.TenantId, scope.AppId, partyId, cancellationToken);
        if (party == null)
        {
            return OnlineResult<OnlineParty>.Fail(OnlineErrorCode.ResourceNotFound, "队伍不存在");
        }

        return await TransitionAsync(party, targetState, OnlinePartyLeaveReason.Left, correlationId, cancellationToken);
    }

    /// <summary>
    /// 扫描并终结过期对象：超期邀请置 <see cref="OnlinePartyInviteState.Expired"/>；空闲队伍置
    /// <see cref="OnlinePartyState.Expired"/>。匹配中与终态队伍不参与空闲过期。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒；传 0 取系统时钟，测试可注入）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>本次终结的（邀请 + 队伍）总数。</returns>
    public async Task<OnlineResult<int>> SweepExpiredAsync(long tenantId, long appId, long nowUnixMilliseconds = 0, CancellationToken cancellationToken = default)
    {
        var now = nowUnixMilliseconds > 0 ? nowUnixMilliseconds : Now();
        var affected = 0;

        var invites = await _store.ListInvitesAsync(tenantId, appId, cancellationToken);
        foreach (var invite in invites)
        {
            if (invite.State != OnlinePartyInviteState.Pending || invite.ExpiresAtTime > now)
            {
                continue;
            }

            invite.State = OnlinePartyInviteState.Expired;
            await _store.SaveInviteAsync(invite, cancellationToken);
            await _eventPublisher.PublishAsync(OnlinePartyEvents.CreateInviteChanged(invite, 0), cancellationToken);
            affected++;
        }

        var parties = await _store.ListPartiesAsync(tenantId, appId, cancellationToken);
        foreach (var party in parties)
        {
            if (OnlinePartyStateMachine.IsTerminal(party.State) || party.State == OnlinePartyState.Matching)
            {
                continue;
            }

            if (party.ExpiresAtTime > now)
            {
                continue;
            }

            var expired = await TransitionAsync(party, OnlinePartyState.Expired, OnlinePartyLeaveReason.Disbanded, null, cancellationToken);
            if (expired.IsSuccess)
            {
                affected++;
            }
        }

        return OnlineResult<int>.Ok(affected);
    }

    /// <summary>
    /// 移除成员并按成员集合归位队伍状态（S4.7 队长退出规则的唯一实现点）。
    /// </summary>
    /// <param name="party">队伍聚合。</param>
    /// <param name="playerId">被移除玩家标识。</param>
    /// <param name="reason">离开原因。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>移除后的队伍。</returns>
    private async Task<OnlineResult<OnlineParty>> RemoveMemberAsync(OnlineParty party, long playerId, OnlinePartyLeaveReason reason, string correlationId, CancellationToken cancellationToken)
    {
        party.Members.RemoveAll(m => m.PlayerId == playerId);

        if (party.Members.Count == 0)
        {
            return await TransitionAsync(party, OnlinePartyState.Disbanded, OnlinePartyLeaveReason.Disbanded, correlationId, cancellationToken);
        }

        if (party.LeaderId == playerId)
        {
            var successor = FindEarliestJoined(party);
            party.LeaderId = successor.PlayerId;
            reason = OnlinePartyLeaveReason.LeaderExit;
        }

        return await RefreshAfterMemberChangeAsync(party, reason, correlationId, cancellationToken);
    }

    /// <summary>
    /// 按成员集合归位队伍状态：人数不足 → <see cref="OnlinePartyState.Left"/>；全员准备 →
    /// <see cref="OnlinePartyState.Ready"/>；否则 <see cref="OnlinePartyState.Formed"/>。
    /// </summary>
    /// <param name="party">队伍聚合。</param>
    /// <param name="reason">变更原因。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>归位后的队伍。</returns>
    private async Task<OnlineResult<OnlineParty>> RefreshAfterMemberChangeAsync(OnlineParty party, OnlinePartyLeaveReason reason, string correlationId, CancellationToken cancellationToken)
    {
        if (party.Members.Count == 0)
        {
            return await TransitionAsync(party, OnlinePartyState.Disbanded, OnlinePartyLeaveReason.Disbanded, correlationId, cancellationToken);
        }

        if (party.Members.Count < party.MinMembers)
        {
            if (party.State == OnlinePartyState.Created || party.State == OnlinePartyState.Inviting)
            {
                await TouchAsync(party, cancellationToken);
                return OnlineResult<OnlineParty>.Ok(party);
            }

            return await TransitionAsync(party, OnlinePartyState.Left, reason, correlationId, cancellationToken);
        }

        var target = AllReady(party) ? OnlinePartyState.Ready : OnlinePartyState.Formed;
        return await TransitionAsync(party, target, reason, correlationId, cancellationToken);
    }

    /// <summary>
    /// 执行一次状态迁移（唯一入口：过状态机 + 落库 + 发事件）。
    /// </summary>
    /// <param name="party">队伍聚合。</param>
    /// <param name="target">目标状态。</param>
    /// <param name="reason">变更原因。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>迁移后的队伍。</returns>
    private async Task<OnlineResult<OnlineParty>> TransitionAsync(OnlineParty party, OnlinePartyState target, OnlinePartyLeaveReason reason, string correlationId, CancellationToken cancellationToken)
    {
        if (party.State == target)
        {
            // 幂等路径也要落库：目标态与当前态相同不等于「没有变更」——
            // 准备状态等成员级字段是在本方法之前改的，直接返回会把这些改动丢掉
            // （表现：全员就绪永远停在 Formed，Ready 不可达）。
            await TouchAsync(party, cancellationToken);
            return OnlineResult<OnlineParty>.Ok(party);
        }

        if (OnlinePartyStateMachine.IsTerminal(party.State))
        {
            return OnlineResult<OnlineParty>.Fail(OnlineErrorCode.StateEnded, "队伍已处于终态，不可再迁移：" + party.State);
        }

        if (!OnlinePartyStateMachine.TryTransition(party.State, target))
        {
            return OnlineResult<OnlineParty>.Fail(OnlineErrorCode.StateOperationForbidden, "非法队伍状态迁移：" + party.State + " → " + target);
        }

        var from = party.State;
        party.State = target;
        party.UpdatedAtTime = Now();
        party.ExpiresAtTime = party.UpdatedAtTime + (_idleTimeToLiveSeconds * 1000);
        await _store.SavePartyAsync(party, cancellationToken);
        await _eventPublisher.PublishAsync(OnlinePartyEvents.CreatePartyChanged(party, from, target, reason, correlationId), cancellationToken);
        return OnlineResult<OnlineParty>.Ok(party);
    }

    /// <summary>
    /// 刷新队伍时间戳并落库（无状态迁移的成员变动）。
    /// </summary>
    /// <param name="party">队伍聚合。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>完成通知。</returns>
    private async Task TouchAsync(OnlineParty party, CancellationToken cancellationToken)
    {
        party.UpdatedAtTime = Now();
        party.ExpiresAtTime = party.UpdatedAtTime + (_idleTimeToLiveSeconds * 1000);
        await _store.SavePartyAsync(party, cancellationToken);
    }

    /// <summary>
    /// 校验操作者为队伍队长且队伍可写。
    /// </summary>
    /// <param name="party">队伍聚合（可为 null）。</param>
    /// <param name="playerId">操作者标识。</param>
    /// <returns>成功返回 null，失败返回错误结果。</returns>
    private static OnlineResult<OnlineParty> EnsureLeaderReachable(OnlineParty party, long playerId)
    {
        if (party == null)
        {
            return OnlineResult<OnlineParty>.Fail(OnlineErrorCode.ResourceNotFound, "队伍不存在");
        }

        if (OnlinePartyStateMachine.IsTerminal(party.State))
        {
            return OnlineResult<OnlineParty>.Fail(OnlineErrorCode.StateEnded, "队伍已结束：" + party.State);
        }

        if (party.LeaderId != playerId)
        {
            return OnlineResult<OnlineParty>.Fail(OnlineErrorCode.ResourceNotFound, "队伍不存在");
        }

        return null;
    }

    /// <summary>
    /// 从成员集合中查找指定玩家。
    /// </summary>
    /// <param name="party">队伍聚合。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <returns>成员条目；不在队伍返回 null。</returns>
    private static OnlinePartyMember FindMember(OnlineParty party, long playerId)
    {
        foreach (var member in party.Members)
        {
            if (member.PlayerId == playerId)
            {
                return member;
            }
        }

        return null;
    }

    /// <summary>
    /// 取出加入最早的成员（队长退出后的确定性继任者）。
    /// </summary>
    /// <param name="party">队伍聚合（成员集合非空）。</param>
    /// <returns>加入最早的成员。</returns>
    private static OnlinePartyMember FindEarliestJoined(OnlineParty party)
    {
        var earliest = party.Members[0];
        foreach (var member in party.Members)
        {
            if (member.JoinedAtTime < earliest.JoinedAtTime)
            {
                earliest = member;
            }
        }

        return earliest;
    }

    /// <summary>
    /// 判定成员是否全员处于准备态。
    /// </summary>
    /// <param name="party">队伍聚合。</param>
    /// <returns>全员准备返回 <c>true</c>。</returns>
    private static bool AllReady(OnlineParty party)
    {
        foreach (var member in party.Members)
        {
            if (member.MemberState != OnlinePartyMemberState.Ready)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 校验作用域与玩家主体位（返回队伍结果，供队伍形态的接口直接返回）。
    /// </summary>
    /// <param name="scope">生效作用域。</param>
    /// <returns>成功返回 null，失败返回错误结果。</returns>
    private static OnlineResult<OnlineParty> ValidateScope(OnlineScope scope)
    {
        return ValidateScope<OnlineParty>(scope);
    }

    /// <summary>
    /// 校验作用域与玩家主体位（泛型形态，供邀请等非队伍形态的接口直接返回）。
    /// </summary>
    /// <typeparam name="TData">接口成功负载类型。</typeparam>
    /// <param name="scope">生效作用域。</param>
    /// <returns>成功返回 null，失败返回错误结果。</returns>
    private static OnlineResult<TData> ValidateScope<TData>(OnlineScope scope)
    {
        if (scope == null)
        {
            return OnlineResult<TData>.Fail(OnlineErrorCode.ParameterInvalid, "缺少作用域");
        }

        if (scope.PlayerId <= 0)
        {
            return OnlineResult<TData>.Fail(OnlineErrorCode.ParameterInvalid, "队伍操作必须具备玩家主体位");
        }

        return null;
    }

    /// <summary>取当前 UTC 毫秒时刻。</summary>
    /// <returns>UTC 毫秒时间戳。</returns>
    private static long Now()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}
