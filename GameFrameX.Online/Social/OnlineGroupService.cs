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

namespace GameFrameX.Online.Social;

using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Online.Contracts;
using GameFrameX.Online.Events;
using GameFrameX.Online.Scope;

/// <summary>
/// 群组服务（vault:C7 S6.4：群组生命周期的唯一写者）。
/// <para>
/// 维护约束（红线）：
/// ① **群记录是聚合根**——成员集合、邀请集合、角色、Metadata 的一切变更都以整条记录 CAS 提交
/// （<see cref="IOnlineGroupStore.ReplaceAsync"/>），绝不拆成独立写入；写路径一律「读快照 → 校验 →
/// <see cref="OnlineGroup.Copy"/> → 改副本 → 以快照版本号提交」；
/// ② 权限判据只有两条正交轴：**是否成员**（非成员一律 <see cref="OnlineErrorCode.ResourceNotFound"/>，
/// 反预言，不泄露群存在性）与**角色高低**（成员但角色不足才 <see cref="OnlineErrorCode.ScopeDenied"/>）；
/// ③ 群主身份单点持有在 <see cref="OnlineGroup.OwnerId"/>——群主不得退出（须先解散）、不得被踢出、
/// 不得被降级；群主转让不在本 change 范围，服务不发明该行为（<c>role == Owner</c> 直接参数拒绝）；
/// ④ CAS 失败**不是错误**：重读一次后按当前事实返回成功结果（收敛，不抛错不打回），调用方重试即幂等。
/// </para>
/// </summary>
public sealed class OnlineGroupService : IOnlineChannelMembershipProbe
{
    /// <summary>群组存储。</summary>
    private readonly IOnlineGroupStore _store;

    /// <summary>事件发布出口。</summary>
    private readonly IOnlineEventPublisher _eventPublisher;

    /// <summary>建群时固定的成员数上限。</summary>
    private readonly int _maxMembers;

    /// <summary>邀请存活时长（秒）。</summary>
    private readonly long _inviteTimeToLiveSeconds;

    /// <summary>
    /// 初始化 <see cref="OnlineGroupService"/>。
    /// </summary>
    /// <param name="store">群组存储。</param>
    /// <param name="eventPublisher">事件发布出口。</param>
    /// <param name="maxMembers">成员数上限（默认 100；非正数回退默认值）。</param>
    /// <param name="inviteTimeToLiveSeconds">邀请存活时长（秒；默认 86400 即 1 天；非正数回退默认值）。</param>
    public OnlineGroupService(
        IOnlineGroupStore store,
        IOnlineEventPublisher eventPublisher,
        int maxMembers = 100,
        long inviteTimeToLiveSeconds = 86400)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        _maxMembers = maxMembers > 0 ? maxMembers : 100;
        _inviteTimeToLiveSeconds = inviteTimeToLiveSeconds > 0 ? inviteTimeToLiveSeconds : 86400;
    }

    /// <summary>
    /// 创建群组（创建人即群主并立即成为首个成员，状态 <see cref="OnlineGroupState.Active"/>）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="name">群组名称（非空）。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>新建群组。</returns>
    public async Task<OnlineResult<OnlineGroup>> CreateAsync(OnlineScope scope, string name, string correlationId = null, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope<OnlineGroup>(scope);
        if (failure != null)
        {
            return failure;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return OnlineResult<OnlineGroup>.Fail(OnlineErrorCode.ParameterInvalid, "群组名称不能为空");
        }

        var now = Now();
        var group = new OnlineGroup
        {
            GroupId = "grp-" + Guid.NewGuid().ToString("N"),
            TenantId = scope.TenantId,
            AppId = scope.AppId,
            OwnerId = scope.PlayerId,
            Name = name.Trim(),
            Metadata = new Dictionary<string, string>(StringComparer.Ordinal),
            Members = new List<OnlineGroupMember>
            {
                new OnlineGroupMember
                {
                    PlayerId = scope.PlayerId,
                    Role = OnlineGroupRole.Owner,
                    JoinedAtTime = now,
                },
            },
            Invites = new List<OnlineGroupInvite>(),
            MaxMembers = _maxMembers,
            State = OnlineGroupState.Active,
            Revision = 0,
            CreatedAtTime = now,
            UpdatedAtTime = now,
        };

        // 并发建群的收敛点：标识唯一的建群请求里先到者落库，后到者拿到既有记录（存储层原子）。
        var effective = await _store.SaveIfAbsentAsync(group, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(effective.GroupId, group.GroupId, StringComparison.Ordinal))
        {
            return OnlineResult<OnlineGroup>.Ok(effective);
        }

        await PublishGroupChangedAsync(effective, OnlineSocialEvents.GroupCreated, correlationId, cancellationToken).ConfigureAwait(false);
        return OnlineResult<OnlineGroup>.Ok(effective);
    }

    /// <summary>
    /// 邀请玩家入群（邀请人必须是群成员；同一被邀请人已有待答复邀请时幂等返回既有邀请）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="groupId">群组标识。</param>
    /// <param name="inviteeId">被邀请玩家标识。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>生效的邀请条目。</returns>
    public async Task<OnlineResult<OnlineGroupInvite>> InviteAsync(OnlineScope scope, string groupId, long inviteeId, string correlationId = null, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope<OnlineGroupInvite>(scope);
        if (failure != null)
        {
            return failure;
        }

        if (inviteeId <= 0)
        {
            return OnlineResult<OnlineGroupInvite>.Fail(OnlineErrorCode.ParameterInvalid, "被邀请玩家标识无效");
        }

        var group = await FindAsync(scope, groupId, cancellationToken).ConfigureAwait(false);
        if (group == null || !group.Contains(scope.PlayerId))
        {
            // 反预言：非成员看不到群存在性。
            return OnlineResult<OnlineGroupInvite>.Fail(OnlineErrorCode.ResourceNotFound, "群组不存在");
        }

        if (group.State == OnlineGroupState.Disbanded)
        {
            return OnlineResult<OnlineGroupInvite>.Fail(OnlineErrorCode.StateEnded, "群组已解散");
        }

        if (group.Contains(inviteeId))
        {
            return OnlineResult<OnlineGroupInvite>.Fail(OnlineErrorCode.StateOperationForbidden, "被邀请人已是群成员");
        }

        var existing = FindPendingInvite(group, inviteeId);
        if (existing != null)
        {
            // 幂等：同一被邀请人已有待答复邀请时返回既有邀请，不新建也不报错。
            return OnlineResult<OnlineGroupInvite>.Ok(existing);
        }

        var now = Now();
        var invite = new OnlineGroupInvite
        {
            InviteId = "ginv-" + Guid.NewGuid().ToString("N"),
            TenantId = scope.TenantId,
            AppId = scope.AppId,
            GroupId = group.GroupId,
            InviterId = scope.PlayerId,
            InviteeId = inviteeId,
            State = OnlineGroupInviteState.Pending,
            CreatedAtTime = now,
            ExpiresAtTime = now + (_inviteTimeToLiveSeconds * 1000),
            RespondedAtTime = 0,
        };

        var saved = await ReplaceWithReplayAsync(group, (copy) => TryAppendInvite(copy, invite, inviteeId), cancellationToken).ConfigureAwait(false);

        if (saved == null)
        {
            // 两轮 CAS 均失败：重读一次按当前事实收敛，不报错不打回。
            var current = await FindAsync(scope, groupId, cancellationToken).ConfigureAwait(false);
            if (current == null)
            {
                return OnlineResult<OnlineGroupInvite>.Fail(OnlineErrorCode.ResourceNotFound, "群组不存在");
            }

            var converged = FindPendingInvite(current, inviteeId);
            if (converged != null)
            {
                return OnlineResult<OnlineGroupInvite>.Ok(converged);
            }

            return OnlineResult<OnlineGroupInvite>.Fail(OnlineErrorCode.DependencyUnavailable, "群组邀请写入失败，请重试");
        }

        // 落库后的待答复邀请即本次生效的邀请（并发重复邀请时收敛到既有那条）。
        var effectiveInvite = FindPendingInvite(saved, inviteeId);
        if (effectiveInvite == null)
        {
            effectiveInvite = FindInvite(saved, invite.InviteId);
        }

        await PublishGroupChangedAsync(saved, OnlineSocialEvents.GroupInviteChanged, correlationId, cancellationToken).ConfigureAwait(false);
        return OnlineResult<OnlineGroupInvite>.Ok(effectiveInvite);
    }

    /// <summary>
    /// 接受邀请（仅被邀请人本人可答复；已是成员或已接受时幂等返回群组快照）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="groupId">群组标识。</param>
    /// <param name="inviteId">邀请标识。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>接受后的群组（被邀请人已在成员集合中）。</returns>
    public Task<OnlineResult<OnlineGroup>> AcceptInviteAsync(OnlineScope scope, string groupId, string inviteId, string correlationId = null, CancellationToken cancellationToken = default)
    {
        return AnswerInviteAsync(scope, groupId, inviteId, true, correlationId, cancellationToken);
    }

    /// <summary>
    /// 拒绝邀请（仅被邀请人本人可答复；拒绝不改变成员集合，重复拒绝幂等返回群组快照）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="groupId">群组标识。</param>
    /// <param name="inviteId">邀请标识。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>拒绝后的群组快照。</returns>
    public Task<OnlineResult<OnlineGroup>> RejectInviteAsync(OnlineScope scope, string groupId, string inviteId, string correlationId = null, CancellationToken cancellationToken = default)
    {
        return AnswerInviteAsync(scope, groupId, inviteId, false, correlationId, cancellationToken);
    }

    /// <summary>
    /// 撤销邀请（邀请发起人本人，或群主/管理员可撤销；已撤销幂等返回群组快照）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="groupId">群组标识。</param>
    /// <param name="inviteId">邀请标识。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>撤销后的群组。</returns>
    public async Task<OnlineResult<OnlineGroup>> RevokeInviteAsync(OnlineScope scope, string groupId, string inviteId, string correlationId = null, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope<OnlineGroup>(scope);
        if (failure != null)
        {
            return failure;
        }

        var group = await FindAsync(scope, groupId, cancellationToken).ConfigureAwait(false);
        if (group == null)
        {
            return OnlineResult<OnlineGroup>.Fail(OnlineErrorCode.ResourceNotFound, "群组不存在");
        }

        var invite = FindInvite(group, inviteId);
        if (invite == null)
        {
            return OnlineResult<OnlineGroup>.Fail(OnlineErrorCode.ResourceNotFound, "邀请不存在");
        }

        if (invite.InviterId != scope.PlayerId && !IsAdminOrOwner(group, scope.PlayerId))
        {
            if (!group.Contains(scope.PlayerId))
            {
                // 反预言：既非发起人也非成员，看不到这条邀请的存在。
                return OnlineResult<OnlineGroup>.Fail(OnlineErrorCode.ResourceNotFound, "邀请不存在");
            }

            return OnlineResult<OnlineGroup>.Fail(OnlineErrorCode.ScopeDenied, "当前角色无权撤销该邀请");
        }

        if (group.State == OnlineGroupState.Disbanded)
        {
            return OnlineResult<OnlineGroup>.Fail(OnlineErrorCode.StateEnded, "群组已解散");
        }

        if (invite.State == OnlineGroupInviteState.Revoked)
        {
            return OnlineResult<OnlineGroup>.Ok(group);
        }

        if (invite.State != OnlineGroupInviteState.Pending)
        {
            return OnlineResult<OnlineGroup>.Fail(OnlineErrorCode.StateEnded, "邀请已结束：" + invite.State);
        }

        var now = Now();
        var saved = await ReplaceWithReplayAsync(group, (copy) => TryRevokeInvite(copy, inviteId, now), cancellationToken).ConfigureAwait(false);

        if (saved == null)
        {
            return await ConvergeOnCurrentAsync(scope, groupId, cancellationToken).ConfigureAwait(false);
        }

        await PublishGroupChangedAsync(saved, OnlineSocialEvents.GroupInviteChanged, correlationId, cancellationToken).ConfigureAwait(false);
        return OnlineResult<OnlineGroup>.Ok(saved);
    }

    /// <summary>
    /// 退出群组（群主不得退出，须先解散；非成员按反预言返回未找到）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="groupId">群组标识。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>退出后的群组。</returns>
    public async Task<OnlineResult<OnlineGroup>> LeaveAsync(OnlineScope scope, string groupId, string correlationId = null, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope<OnlineGroup>(scope);
        if (failure != null)
        {
            return failure;
        }

        var group = await FindAsync(scope, groupId, cancellationToken).ConfigureAwait(false);
        if (group == null || !group.Contains(scope.PlayerId))
        {
            return OnlineResult<OnlineGroup>.Fail(OnlineErrorCode.ResourceNotFound, "群组不存在");
        }

        if (group.State == OnlineGroupState.Disbanded)
        {
            return OnlineResult<OnlineGroup>.Fail(OnlineErrorCode.StateEnded, "群组已解散");
        }

        if (group.OwnerId == scope.PlayerId)
        {
            return OnlineResult<OnlineGroup>.Fail(OnlineErrorCode.StateOperationForbidden, "群主不得退出群组，请先解散");
        }

        var saved = await ReplaceWithReplayAsync(group, (copy) =>
        {
            // 重放快照里可能已无此人（并发已被踢出）：无事可做即不提交。
            return copy.Members.RemoveAll(member => member.PlayerId == scope.PlayerId) > 0;
        }, cancellationToken).ConfigureAwait(false);

        if (saved == null)
        {
            return await ConvergeOnCurrentAsync(scope, groupId, cancellationToken).ConfigureAwait(false);
        }

        await PublishGroupChangedAsync(saved, OnlineSocialEvents.GroupMemberLeft, correlationId, cancellationToken).ConfigureAwait(false);
        return OnlineResult<OnlineGroup>.Ok(saved);
    }

    /// <summary>
    /// 踢出成员（管理员只能踢普通成员，群主可踢管理员与普通成员；不能踢自己与群主）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="groupId">群组标识。</param>
    /// <param name="targetPlayerId">被踢玩家标识。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>踢出后的群组。</returns>
    public async Task<OnlineResult<OnlineGroup>> KickAsync(OnlineScope scope, string groupId, long targetPlayerId, string correlationId = null, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope<OnlineGroup>(scope);
        if (failure != null)
        {
            return failure;
        }

        if (targetPlayerId <= 0)
        {
            return OnlineResult<OnlineGroup>.Fail(OnlineErrorCode.ParameterInvalid, "目标玩家标识无效");
        }

        var group = await FindAsync(scope, groupId, cancellationToken).ConfigureAwait(false);
        if (group == null || !group.Contains(scope.PlayerId))
        {
            return OnlineResult<OnlineGroup>.Fail(OnlineErrorCode.ResourceNotFound, "群组不存在");
        }

        if (group.State == OnlineGroupState.Disbanded)
        {
            return OnlineResult<OnlineGroup>.Fail(OnlineErrorCode.StateEnded, "群组已解散");
        }

        if (targetPlayerId == scope.PlayerId)
        {
            return OnlineResult<OnlineGroup>.Fail(OnlineErrorCode.ScopeDenied, "不能踢出自己，请使用退出群组");
        }

        if (targetPlayerId == group.OwnerId)
        {
            return OnlineResult<OnlineGroup>.Fail(OnlineErrorCode.StateOperationForbidden, "群主不能被踢出，请先解散群组");
        }

        var target = FindMember(group, targetPlayerId);
        if (target == null)
        {
            return OnlineResult<OnlineGroup>.Fail(OnlineErrorCode.ResourceNotFound, "目标玩家不在群组中");
        }

        OnlineGroupRole operatorRole;
        group.TryGetRole(scope.PlayerId, out operatorRole);
        if (operatorRole == OnlineGroupRole.Member)
        {
            return OnlineResult<OnlineGroup>.Fail(OnlineErrorCode.ScopeDenied, "普通成员无权踢出成员");
        }

        if (operatorRole == OnlineGroupRole.Admin && target.Role != OnlineGroupRole.Member)
        {
            return OnlineResult<OnlineGroup>.Fail(OnlineErrorCode.ScopeDenied, "管理员只能踢出普通成员");
        }

        var saved = await ReplaceWithReplayAsync(group, (copy) =>
        {
            // 重放快照里目标可能已不在（并发已被踢出/已退出）：无事可做即不提交。
            return copy.Members.RemoveAll(member => member.PlayerId == targetPlayerId) > 0;
        }, cancellationToken).ConfigureAwait(false);

        if (saved == null)
        {
            return await ConvergeOnCurrentAsync(scope, groupId, cancellationToken).ConfigureAwait(false);
        }

        await PublishGroupChangedAsync(saved, OnlineSocialEvents.GroupMemberLeft, correlationId, cancellationToken).ConfigureAwait(false);
        return OnlineResult<OnlineGroup>.Ok(saved);
    }

    /// <summary>
    /// 调整成员角色（仅群主可调用；<see cref="OnlineGroupRole.Owner"/> 不可作为目标角色——群主转让不在本
    /// change 范围，服务不发明该行为；群主自身角色不可被改写，否则 <see cref="OnlineGroup.OwnerId"/> 与
    /// 成员角色会失配）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位，且必须是群主）。</param>
    /// <param name="groupId">群组标识。</param>
    /// <param name="targetPlayerId">目标成员标识。</param>
    /// <param name="role">目标角色（仅 <see cref="OnlineGroupRole.Admin"/> 与 <see cref="OnlineGroupRole.Member"/>）。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>调整后的群组。</returns>
    public async Task<OnlineResult<OnlineGroup>> SetRoleAsync(OnlineScope scope, string groupId, long targetPlayerId, OnlineGroupRole role, string correlationId = null, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope<OnlineGroup>(scope);
        if (failure != null)
        {
            return failure;
        }

        if (targetPlayerId <= 0)
        {
            return OnlineResult<OnlineGroup>.Fail(OnlineErrorCode.ParameterInvalid, "目标玩家标识无效");
        }

        if (role == OnlineGroupRole.Owner)
        {
            return OnlineResult<OnlineGroup>.Fail(OnlineErrorCode.ParameterInvalid, "不支持把成员设为群主");
        }

        var group = await FindAsync(scope, groupId, cancellationToken).ConfigureAwait(false);
        if (group == null || !group.Contains(scope.PlayerId))
        {
            return OnlineResult<OnlineGroup>.Fail(OnlineErrorCode.ResourceNotFound, "群组不存在");
        }

        if (group.OwnerId != scope.PlayerId)
        {
            return OnlineResult<OnlineGroup>.Fail(OnlineErrorCode.ScopeDenied, "仅群主可调整成员角色");
        }

        if (group.State == OnlineGroupState.Disbanded)
        {
            return OnlineResult<OnlineGroup>.Fail(OnlineErrorCode.StateEnded, "群组已解散");
        }

        if (targetPlayerId == group.OwnerId)
        {
            return OnlineResult<OnlineGroup>.Fail(OnlineErrorCode.StateOperationForbidden, "群主角色不可变更");
        }

        var target = FindMember(group, targetPlayerId);
        if (target == null)
        {
            return OnlineResult<OnlineGroup>.Fail(OnlineErrorCode.ResourceNotFound, "目标玩家不在群组中");
        }

        if (target.Role == role)
        {
            // 幂等：角色已是目标值时不写库、不发事件。
            return OnlineResult<OnlineGroup>.Ok(group);
        }

        var saved = await ReplaceWithReplayAsync(group, (copy) =>
        {
            var member = FindMember(copy, targetPlayerId);
            if (member == null || member.Role == role)
            {
                // 目标已不在群里，或角色已被并发改成同一目标值：无事可做即不提交。
                return false;
            }

            member.Role = role;
            return true;
        }, cancellationToken).ConfigureAwait(false);

        if (saved == null)
        {
            return await ConvergeOnCurrentAsync(scope, groupId, cancellationToken).ConfigureAwait(false);
        }

        await PublishGroupChangedAsync(saved, OnlineSocialEvents.GroupRoleChanged, correlationId, cancellationToken).ConfigureAwait(false);
        return OnlineResult<OnlineGroup>.Ok(saved);
    }

    /// <summary>
    /// 解散群组（仅群主可执行；已解散幂等返回既有终态快照）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位，且必须是群主）。</param>
    /// <param name="groupId">群组标识。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>解散后的群组。</returns>
    public async Task<OnlineResult<OnlineGroup>> DisbandAsync(OnlineScope scope, string groupId, string correlationId = null, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope<OnlineGroup>(scope);
        if (failure != null)
        {
            return failure;
        }

        var group = await FindAsync(scope, groupId, cancellationToken).ConfigureAwait(false);
        if (group == null || !group.Contains(scope.PlayerId))
        {
            return OnlineResult<OnlineGroup>.Fail(OnlineErrorCode.ResourceNotFound, "群组不存在");
        }

        if (group.OwnerId != scope.PlayerId)
        {
            return OnlineResult<OnlineGroup>.Fail(OnlineErrorCode.ScopeDenied, "仅群主可解散群组");
        }

        if (group.State == OnlineGroupState.Disbanded)
        {
            return OnlineResult<OnlineGroup>.Ok(group);
        }

        var saved = await ReplaceWithReplayAsync(group, (copy) =>
        {
            if (copy.State == OnlineGroupState.Disbanded)
            {
                // 已被并发解散：终态一致，不重复写库、不重复发事件。
                return false;
            }

            copy.State = OnlineGroupState.Disbanded;
            return true;
        }, cancellationToken).ConfigureAwait(false);

        if (saved == null)
        {
            return await ConvergeOnCurrentAsync(scope, groupId, cancellationToken).ConfigureAwait(false);
        }

        await PublishGroupChangedAsync(saved, OnlineSocialEvents.GroupDisbanded, correlationId, cancellationToken).ConfigureAwait(false);
        return OnlineResult<OnlineGroup>.Ok(saved);
    }

    /// <summary>
    /// 查看成员列表（非成员按反预言返回未找到，不泄露群存在性）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="groupId">群组标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>成员列表。</returns>
    public async Task<OnlineResult<IReadOnlyList<OnlineGroupMember>>> ListMembersAsync(OnlineScope scope, string groupId, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope<IReadOnlyList<OnlineGroupMember>>(scope);
        if (failure != null)
        {
            return failure;
        }

        var group = await FindAsync(scope, groupId, cancellationToken).ConfigureAwait(false);
        if (group == null || !group.Contains(scope.PlayerId))
        {
            return OnlineResult<IReadOnlyList<OnlineGroupMember>>.Fail(OnlineErrorCode.ResourceNotFound, "群组不存在");
        }

        return OnlineResult<IReadOnlyList<OnlineGroupMember>>.Ok(group.Members);
    }

    /// <summary>
    /// 应答「某玩家是否为某频道的成员」（<see cref="OnlineChatChannelKind.Group"/> 频道的唯一裁决方，C99）。
    /// <para>
    /// 维护约束（职责边界）：频道成员集**不复制**进频道记录——复制会在退群/踢人后留下陈旧成员表，
    /// 让已被移出的人继续读到群内消息（隐私泄漏）。因此裁决权留在群组域，聊天域只问不判。
    /// </para>
    /// <para>
    /// 维护约束（已解散群组）：<see cref="OnlineGroupState.Disbanded"/> 的群组一律返回 <c>false</c>——
    /// 群已不存在，其频道随之一并封存；不这么做的话解散后成员仍能继续发言，形成「僵尸频道」
    /// （群组列表里看不到、消息却还在产生）。历史消息仍在存储中留档，供审计按其他路径调取。
    /// </para>
    /// <para>
    /// 维护约束（非本域频道）：<paramref name="kind"/> 不是 <see cref="OnlineChatChannelKind.Group"/> 时
    /// 一律返回 <c>false</c>（不认领），由对应的归属域应答。
    /// </para>
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="kind">频道类型。</param>
    /// <param name="boundId">频道绑定的业务标识（本域为群组标识）。</param>
    /// <param name="playerId">待裁决玩家。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>是成员且群组仍可发言返回 <c>true</c>。</returns>
    public async Task<bool> IsChannelMemberAsync(long tenantId, long appId, OnlineChatChannelKind kind, string boundId, long playerId, CancellationToken cancellationToken = default)
    {
        if (kind != OnlineChatChannelKind.Group || string.IsNullOrEmpty(boundId))
        {
            return false;
        }

        var group = await _store.FindAsync(tenantId, appId, boundId, cancellationToken).ConfigureAwait(false);
        if (group == null || group.State != OnlineGroupState.Active)
        {
            return false;
        }

        return group.Contains(playerId);
    }

    /// <summary>
    /// 列出本人所属的全部群组（含已解散群组；由消费方按状态过滤）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>群组列表。</returns>
    public async Task<OnlineResult<IReadOnlyList<OnlineGroup>>> ListMyGroupsAsync(OnlineScope scope, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope<IReadOnlyList<OnlineGroup>>(scope);
        if (failure != null)
        {
            return failure;
        }

        var groups = await _store.ListByPlayerAsync(scope.TenantId, scope.AppId, scope.PlayerId, cancellationToken).ConfigureAwait(false);
        return OnlineResult<IReadOnlyList<OnlineGroup>>.Ok(groups);
    }

    /// <summary>
    /// 写入一条群元数据（群主与管理员可写；键不能为空）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="groupId">群组标识。</param>
    /// <param name="key">元数据键（非空）。</param>
    /// <param name="value">元数据值（null 归一为空字符串，避免「键存在但值为 null」的歧义）。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>写入后的群组。</returns>
    public async Task<OnlineResult<OnlineGroup>> UpdateMetadataAsync(OnlineScope scope, string groupId, string key, string value, string correlationId = null, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope<OnlineGroup>(scope);
        if (failure != null)
        {
            return failure;
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            return OnlineResult<OnlineGroup>.Fail(OnlineErrorCode.ParameterInvalid, "元数据键不能为空");
        }

        var group = await FindAsync(scope, groupId, cancellationToken).ConfigureAwait(false);
        if (group == null || !group.Contains(scope.PlayerId))
        {
            return OnlineResult<OnlineGroup>.Fail(OnlineErrorCode.ResourceNotFound, "群组不存在");
        }

        if (!IsAdminOrOwner(group, scope.PlayerId))
        {
            return OnlineResult<OnlineGroup>.Fail(OnlineErrorCode.ScopeDenied, "普通成员无权写入群元数据");
        }

        if (group.State == OnlineGroupState.Disbanded)
        {
            return OnlineResult<OnlineGroup>.Fail(OnlineErrorCode.StateEnded, "群组已解散");
        }

        var normalizedKey = key.Trim();
        var normalizedValue = value ?? string.Empty;
        var saved = await ReplaceWithReplayAsync(group, (copy) =>
        {
            if (copy.State != OnlineGroupState.Active)
            {
                // 重放快照已被解散：结束的群不再接受元数据写入。
                return false;
            }

            string existed;
            if (copy.Metadata.TryGetValue(normalizedKey, out existed) && string.Equals(existed, normalizedValue, StringComparison.Ordinal))
            {
                // 值已是目标值：无事可做即不提交（避免空写推进版本号 + 外发无变化的变更事件）。
                return false;
            }

            copy.Metadata[normalizedKey] = normalizedValue;
            return true;
        }, cancellationToken).ConfigureAwait(false);

        if (saved == null)
        {
            return await ConvergeOnCurrentAsync(scope, groupId, cancellationToken).ConfigureAwait(false);
        }

        await PublishGroupChangedAsync(saved, OnlineSocialEvents.GroupMetadataUpdated, correlationId, cancellationToken).ConfigureAwait(false);
        return OnlineResult<OnlineGroup>.Ok(saved);
    }

    /// <summary>
    /// 扫描并终结超期待答复邀请（置 <see cref="OnlineGroupInviteState.Expired"/>；群组状态不受影响）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒；传 0 取系统时钟，测试可注入）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>本次置为过期的邀请条数。</returns>
    public async Task<OnlineResult<int>> SweepExpiredInvitesAsync(long tenantId, long appId, long nowUnixMilliseconds = 0, CancellationToken cancellationToken = default)
    {
        var now = nowUnixMilliseconds > 0 ? nowUnixMilliseconds : Now();
        var groups = await _store.ListByStateAsync(tenantId, appId, OnlineGroupState.Active, cancellationToken).ConfigureAwait(false);
        var affected = 0;
        foreach (var group in groups)
        {
            if (!HasExpirableInvite(group, now))
            {
                // 无到期邀请不做无谓写入：空写会平白推进版本号，让并发写路径白吃 CAS 失败。
                continue;
            }

            var expiredCount = 0;
            var saved = await ReplaceWithReplayAsync(group, (copy) =>
            {
                expiredCount = MarkExpiredInvites(copy, now);
                // 重放快照上已无可终结的邀请：不提交（版本号与外发事件都不该为一次空扫付出）。
                return expiredCount > 0;
            }, cancellationToken).ConfigureAwait(false);

            if (saved == null || expiredCount == 0)
            {
                // 两轮 CAS 均失败：本轮跳过（不重试到底）；下一轮扫描仍会看到这些待答复邀请，不会漏扫。
                continue;
            }

            await PublishGroupChangedAsync(saved, OnlineSocialEvents.GroupInviteChanged, null, cancellationToken).ConfigureAwait(false);
            affected += expiredCount;
        }

        return OnlineResult<int>.Ok(affected);
    }

    /// <summary>
    /// 答复邀请的唯一实现点（接受 / 拒绝共用；权限判据只取被邀请人本人）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="groupId">群组标识。</param>
    /// <param name="inviteId">邀请标识。</param>
    /// <param name="accept">接受传 <c>true</c>，拒绝传 <c>false</c>。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>答复后的群组。</returns>
    private async Task<OnlineResult<OnlineGroup>> AnswerInviteAsync(OnlineScope scope, string groupId, string inviteId, bool accept, string correlationId, CancellationToken cancellationToken)
    {
        var failure = ValidateScope<OnlineGroup>(scope);
        if (failure != null)
        {
            return failure;
        }

        var group = await FindAsync(scope, groupId, cancellationToken).ConfigureAwait(false);
        if (group == null)
        {
            return OnlineResult<OnlineGroup>.Fail(OnlineErrorCode.ResourceNotFound, "群组不存在");
        }

        var invite = FindInvite(group, inviteId);
        if (invite == null || invite.InviteeId != scope.PlayerId)
        {
            // 反预言：非被邀请人（含邀请发起人本人）看不到这条邀请的存在。
            return OnlineResult<OnlineGroup>.Fail(OnlineErrorCode.ResourceNotFound, "邀请不存在");
        }

        if (group.State == OnlineGroupState.Disbanded)
        {
            return OnlineResult<OnlineGroup>.Fail(OnlineErrorCode.StateEnded, "群组已解散");
        }

        var target = accept ? OnlineGroupInviteState.Accepted : OnlineGroupInviteState.Rejected;
        if (invite.State == target)
        {
            // 幂等：已答复到同一终态时返回既有快照，不重复写库、不重复发事件。
            return OnlineResult<OnlineGroup>.Ok(group);
        }

        if (invite.State != OnlineGroupInviteState.Pending)
        {
            return OnlineResult<OnlineGroup>.Fail(OnlineErrorCode.StateEnded, "邀请已结束：" + invite.State);
        }

        var now = Now();
        if (accept && now >= invite.ExpiresAtTime)
        {
            // 超期但尚未被扫描到的邀请就地终结：接受会新增成员，绝不能放过期邀请入群
            // （拒绝不校验有效期——拒绝不改变成员集合，被拒与过期同属「未入群」终态）。
            return await ExpireStaleInviteAsync(group, inviteId, correlationId, cancellationToken).ConfigureAwait(false);
        }

        if (accept && group.Members.Count >= group.MaxMembers)
        {
            return OnlineResult<OnlineGroup>.Fail(OnlineErrorCode.StateOperationForbidden, "群组成员数已达上限");
        }

        var saved = await ReplaceWithReplayAsync(group, (copy) => TryAnswerInvite(copy, inviteId, scope.PlayerId, accept, target, now), cancellationToken).ConfigureAwait(false);

        if (saved == null)
        {
            // 两轮 CAS 均失败：重读一次按当前事实收敛（多半已被并发答复抢先）。
            return await ConvergeOnCurrentAsync(scope, groupId, cancellationToken).ConfigureAwait(false);
        }

        var action = accept ? OnlineSocialEvents.GroupMemberJoined : OnlineSocialEvents.GroupInviteChanged;
        await PublishGroupChangedAsync(saved, action, correlationId, cancellationToken).ConfigureAwait(false);
        return OnlineResult<OnlineGroup>.Ok(saved);
    }

    /// <summary>
    /// 就地终结一条已超期且尚未被扫描到的待答复邀请（<see cref="AnswerInviteAsync"/> 接受路径专用）。
    /// <para>
    /// 维护约束：接受会新增成员，绝不能放过期邀请入群；拒绝不校验有效期——拒绝不改变成员集合，
    /// 被拒与过期同属「未入群」终态，故本方法只在接受路径调用。
    /// </para>
    /// </summary>
    /// <param name="group">答复依据的群记录快照。</param>
    /// <param name="inviteId">邀请标识。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>恒为「邀请已过期」错误结果（终结落库成功才外发邀请变更事件）。</returns>
    private async Task<OnlineResult<OnlineGroup>> ExpireStaleInviteAsync(OnlineGroup group, string inviteId, string correlationId, CancellationToken cancellationToken)
    {
        var stale = await ReplaceWithReplayAsync(group, (copy) =>
        {
            var expired = FindInvite(copy, inviteId);
            if (expired == null || expired.State != OnlineGroupInviteState.Pending)
            {
                // 已被并发答复/撤销：无需就地终结。
                return false;
            }

            expired.State = OnlineGroupInviteState.Expired;
            return true;
        }, cancellationToken).ConfigureAwait(false);

        if (stale != null)
        {
            await PublishGroupChangedAsync(stale, OnlineSocialEvents.GroupInviteChanged, correlationId, cancellationToken).ConfigureAwait(false);
        }

        return OnlineResult<OnlineGroup>.Fail(OnlineErrorCode.StateEnded, "邀请已过期");
    }

    /// <summary>
    /// 以 CAS 语义提交一次群记录变更（所有写路径的公用实现）。
    /// <para>
    /// 维护约束：变更先在副本上施加（绝不就地改快照——快照可能正在被其他读取方使用）；CAS 失败说明快照
    /// 已被并发改写，此时**重读一次**并以新版本号重放。返回 null 表示两轮都没提交，调用方据此重读收敛，
    /// 不把 CAS 失败当错误抛出。
    /// </para>
    /// <para>
    /// 维护约束（重放不得复活过期事实）：调用方的前置校验（是否已解散、成员数是否达上限、目标是否仍在集合内）
    /// 都是针对**它读到的那份快照**做的；重放换成了新快照，这些判据必须在新快照上**再判一次**——
    /// 否则「读时未解散 → 期间被解散 → 重放往已解散的群里加成员」会把已经结束的群改回活跃，
    /// 且这类写入还带版本号推进与事件外发，等于凭空造出一次不存在的业务动作。
    /// 因此 <paramref name="mutate"/> 的返回值即「本次变更在新快照上是否仍然成立」：
    /// 返回 <c>false</c> 表示已无事可做，直接放弃提交（不写库、不推版本号、不发事件）。
    /// </para>
    /// </summary>
    /// <param name="snapshot">本次变更依据的群记录快照（其 <c>Revision</c> 为期望版本号）。</param>
    /// <param name="mutate">在副本上施加变更的动作；在新快照上仍成立并已施加返回 <c>true</c>，无事可做返回 <c>false</c>。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>落库后的群记录副本；两轮都没提交或记录已不存在返回 null。</returns>
    private async Task<OnlineGroup> ReplaceWithReplayAsync(OnlineGroup snapshot, Func<OnlineGroup, bool> mutate, CancellationToken cancellationToken)
    {
        var saved = await CommitAsync(snapshot, mutate, cancellationToken).ConfigureAwait(false);
        if (saved != null)
        {
            return saved;
        }

        var current = await _store.FindAsync(snapshot.TenantId, snapshot.AppId, snapshot.GroupId, cancellationToken).ConfigureAwait(false);
        if (current == null)
        {
            return null;
        }

        return await CommitAsync(current, mutate, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 在指定快照的副本上施加变更并 CAS 落库（<see cref="ReplaceWithReplayAsync"/> 的单轮实现）。
    /// </summary>
    /// <param name="snapshot">变更依据的群记录快照。</param>
    /// <param name="mutate">在副本上施加变更的动作；无事可做返回 <c>false</c>。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>落库后的群记录副本；变更不成立、CAS 失败或记录不存在返回 null。</returns>
    private async Task<OnlineGroup> CommitAsync(OnlineGroup snapshot, Func<OnlineGroup, bool> mutate, CancellationToken cancellationToken)
    {
        var copy = snapshot.Copy();
        copy.UpdatedAtTime = Now();
        if (!mutate(copy))
        {
            // 无事可做：整体放弃提交——这一步是「空写」的唯一守卫点，拦在这里比写一条无变化的记录好，
            // 否则版本号会被平白推进（让并发写路径白吃一次 CAS 失败），并外发一条什么都没发生的变更事件。
            return null;
        }

        return await _store.ReplaceAsync(copy, snapshot.Revision, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 在重放快照副本上追加一条入群邀请（<see cref="InviteAsync"/> 的 CAS 变更判据）。
    /// </summary>
    /// <param name="copy">重放得到的群记录副本。</param>
    /// <param name="invite">待追加的邀请条目。</param>
    /// <param name="inviteeId">被邀请玩家标识。</param>
    /// <returns>已追加返回 <c>true</c>；重放快照已解散或已有同被邀请人待答复邀请返回 <c>false</c>（无事可做即不提交）。</returns>
    private static bool TryAppendInvite(OnlineGroup copy, OnlineGroupInvite invite, long inviteeId)
    {
        if (copy.State != OnlineGroupState.Active)
        {
            // 重放快照已被解散：不再追加邀请（否则邀请会挂在一个已结束的群上）。
            return false;
        }

        // 重放快照可能已有并发写入的同向邀请：已在则不再追加第二条（同一被邀请人至多一条待答复）。
        if (FindPendingInvite(copy, inviteeId) != null)
        {
            return false;
        }

        copy.Invites.Add(invite);
        return true;
    }

    /// <summary>
    /// 在重放快照副本上撤销一条待答复邀请（<see cref="RevokeInviteAsync"/> 的 CAS 变更判据）。
    /// </summary>
    /// <param name="copy">重放得到的群记录副本。</param>
    /// <param name="inviteId">邀请标识。</param>
    /// <param name="nowUnixMilliseconds">当前 UTC 毫秒时刻（回填答复时刻用）。</param>
    /// <returns>已撤销返回 <c>true</c>；重放快照已解散或邀请已被并发答复/撤销返回 <c>false</c>（无事可做即不提交）。</returns>
    private static bool TryRevokeInvite(OnlineGroup copy, string inviteId, long nowUnixMilliseconds)
    {
        if (copy.State != OnlineGroupState.Active)
        {
            return false;
        }

        var pending = FindInvite(copy, inviteId);
        if (pending == null || pending.State != OnlineGroupInviteState.Pending)
        {
            // 重放快照已被并发答复：不改写（调用方按当前事实收敛）。
            return false;
        }

        pending.State = OnlineGroupInviteState.Revoked;
        pending.RespondedAtTime = nowUnixMilliseconds;
        return true;
    }

    /// <summary>
    /// 在重放快照副本上答复一条待答复邀请（<see cref="AnswerInviteAsync"/> 的 CAS 变更判据；接受时按需追加成员）。
    /// </summary>
    /// <param name="copy">重放得到的群记录副本。</param>
    /// <param name="inviteId">邀请标识。</param>
    /// <param name="playerId">被邀请玩家（答复人）标识。</param>
    /// <param name="accept">接受传 <c>true</c>，拒绝传 <c>false</c>。</param>
    /// <param name="target">答复目标终态（Accepted / Rejected）。</param>
    /// <param name="nowUnixMilliseconds">当前 UTC 毫秒时刻（入群时刻与答复时刻用）。</param>
    /// <returns>已答复返回 <c>true</c>；重放快照已解散、邀请已被并发答复/撤销或接受越过成员上限返回 <c>false</c>（无事可做即不提交）。</returns>
    private static bool TryAnswerInvite(OnlineGroup copy, string inviteId, long playerId, bool accept, OnlineGroupInviteState target, long nowUnixMilliseconds)
    {
        if (copy.State != OnlineGroupState.Active)
        {
            // 重放快照已被解散：绝不把成员加回一个已结束的群（前置判据是针对旧快照做的，此处必须再判）。
            return false;
        }

        var pending = FindInvite(copy, inviteId);
        if (pending == null || pending.State != OnlineGroupInviteState.Pending)
        {
            // 重放快照已被并发答复/撤销：不改写（调用方按当前事实收敛）。
            return false;
        }

        if (accept && !copy.Contains(playerId))
        {
            if (copy.Members.Count >= copy.MaxMembers)
            {
                // 重放快照可能已被并发入群顶到上限：接受不得越过上限（同上，判据必须落在新快照上）。
                // 已在成员集合中的被邀请人不受此限——那一步不新增成员。
                return false;
            }

            copy.Members.Add(new OnlineGroupMember
            {
                PlayerId = playerId,
                Role = OnlineGroupRole.Member,
                JoinedAtTime = nowUnixMilliseconds,
            });
        }

        pending.State = target;
        pending.RespondedAtTime = nowUnixMilliseconds;
        return true;
    }

    /// <summary>
    /// CAS 失败后的收敛出口：重读群记录并按当前事实返回成功结果。
    /// <para>
    /// 维护约束：两轮 CAS 都被并发写入抢先**不是错误**——调用方本次意图多半已被别人（或上一个重试）达成，
    /// 这里返回权威快照让调用方自行判断，既不抛异常也不打回错误码，重试天然幂等。
    /// </para>
    /// </summary>
    /// <param name="scope">生效作用域。</param>
    /// <param name="groupId">群组标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>当前群记录快照；记录已不存在返回未找到。</returns>
    private async Task<OnlineResult<OnlineGroup>> ConvergeOnCurrentAsync(OnlineScope scope, string groupId, CancellationToken cancellationToken)
    {
        var current = await FindAsync(scope, groupId, cancellationToken).ConfigureAwait(false);
        if (current == null)
        {
            return OnlineResult<OnlineGroup>.Fail(OnlineErrorCode.ResourceNotFound, "群组不存在");
        }

        return OnlineResult<OnlineGroup>.Ok(current);
    }

    /// <summary>
    /// 按作用域与群组标识读取群记录（空标识直接判不存在，不做无谓查询）。
    /// </summary>
    /// <param name="scope">生效作用域。</param>
    /// <param name="groupId">群组标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>群记录副本；不存在返回 null。</returns>
    private Task<OnlineGroup> FindAsync(OnlineScope scope, string groupId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(groupId))
        {
            return Task.FromResult<OnlineGroup>(null);
        }

        return _store.FindAsync(scope.TenantId, scope.AppId, groupId, cancellationToken);
    }

    /// <summary>
    /// 按邀请标识在群记录内定位邀请。
    /// </summary>
    /// <param name="group">群记录。</param>
    /// <param name="inviteId">邀请标识。</param>
    /// <returns>邀请条目；不存在返回 null。</returns>
    private static OnlineGroupInvite FindInvite(OnlineGroup group, string inviteId)
    {
        if (group.Invites == null || string.IsNullOrEmpty(inviteId))
        {
            return null;
        }

        foreach (var invite in group.Invites)
        {
            if (string.Equals(invite.InviteId, inviteId, StringComparison.Ordinal))
            {
                return invite;
            }
        }

        return null;
    }

    /// <summary>
    /// 按被邀请人定位群记录内该玩家的待答复邀请。
    /// </summary>
    /// <param name="group">群记录。</param>
    /// <param name="inviteeId">被邀请人玩家标识。</param>
    /// <returns>待答复邀请；不存在返回 null。</returns>
    private static OnlineGroupInvite FindPendingInvite(OnlineGroup group, long inviteeId)
    {
        var pending = group.FindPendingInvitesFor(inviteeId);
        return pending.Count > 0 ? pending[0] : null;
    }

    /// <summary>
    /// 按玩家标识定位成员条目。
    /// </summary>
    /// <param name="group">群记录。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <returns>成员条目；不是成员返回 null。</returns>
    private static OnlineGroupMember FindMember(OnlineGroup group, long playerId)
    {
        if (group.Members == null)
        {
            return null;
        }

        foreach (var member in group.Members)
        {
            if (member.PlayerId == playerId)
            {
                return member;
            }
        }

        return null;
    }

    /// <summary>
    /// 判定玩家在群内的角色是否达到管理员及以上（元数据写入等管理动作的判据）。
    /// </summary>
    /// <param name="group">群记录。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <returns>是群主或管理员返回 <c>true</c>。</returns>
    private static bool IsAdminOrOwner(OnlineGroup group, long playerId)
    {
        OnlineGroupRole role;
        if (!group.TryGetRole(playerId, out role))
        {
            return false;
        }

        return role != OnlineGroupRole.Member;
    }

    /// <summary>
    /// 判定群记录内是否存在到期的待答复邀请（扫描前置判据，避免给没有到期邀请的群做空写入）。
    /// </summary>
    /// <param name="group">群记录。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒）。</param>
    /// <returns>存在到期邀请返回 <c>true</c>。</returns>
    private static bool HasExpirableInvite(OnlineGroup group, long nowUnixMilliseconds)
    {
        if (group.Invites == null)
        {
            return false;
        }

        foreach (var invite in group.Invites)
        {
            if (invite.State == OnlineGroupInviteState.Pending && invite.ExpiresAtTime <= nowUnixMilliseconds)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 把群记录内到期的待答复邀请置为 <see cref="OnlineGroupInviteState.Expired"/>（只改副本，提交由调用方完成）。
    /// </summary>
    /// <param name="group">待变更的群记录副本。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒）。</param>
    /// <returns>本次置位的邀请条数。</returns>
    private static int MarkExpiredInvites(OnlineGroup group, long nowUnixMilliseconds)
    {
        var affected = 0;
        if (group.Invites == null)
        {
            return affected;
        }

        foreach (var invite in group.Invites)
        {
            if (invite.State == OnlineGroupInviteState.Pending && invite.ExpiresAtTime <= nowUnixMilliseconds)
            {
                // 超期是时间流逝而非答复：只置终态，不回填答复时刻（与好友请求超期扫描同款）。
                invite.State = OnlineGroupInviteState.Expired;
                affected++;
            }
        }

        return affected;
    }

    /// <summary>
    /// 发布群组变更事件（顺序等待发布完成，不并发、不丢弃）。
    /// </summary>
    /// <param name="group">变更后的群记录。</param>
    /// <param name="action">动作常量（<c>OnlineSocialEvents</c> 的群组动作）。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>完成通知。</returns>
    private async Task PublishGroupChangedAsync(OnlineGroup group, string action, string correlationId, CancellationToken cancellationToken)
    {
        await _eventPublisher.PublishAsync(OnlineSocialEvents.CreateGroupChanged(group, action, correlationId), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 校验作用域与玩家主体位（泛型形态，供各返回类型直接返回）。
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
            return OnlineResult<TData>.Fail(OnlineErrorCode.ParameterInvalid, "群组操作必须具备玩家主体位");
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
