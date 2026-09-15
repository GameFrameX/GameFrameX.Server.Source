// ==========================================================================================
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
// ==========================================================================================

using System;
using System.Threading.Tasks;
using GameFrameX.Online.Contracts;
using GameFrameX.Online.Events;
using GameFrameX.Online.Scope;
using GameFrameX.Online.Social;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 群组服务测试（vault:C7 S6.4 群组生命周期、S6.5 群组频道成员资格；VC-6.6 群组生命周期、
    /// VC-6.7 邀请与元数据、VC-6.8/VC-6.9 频道成员资格与 C99 频道探针）。
    /// </summary>
    public class OnlineGroupServiceTests
    {
        /// <summary>测试用租户标识。</summary>
        private const long TenantId = 1;

        /// <summary>测试用应用标识。</summary>
        private const long AppId = 10;

        /// <summary>测试用区服标识。</summary>
        private const long ServerId = 100;

        /// <summary>测试用群主玩家标识。</summary>
        private const long OwnerId = 1001;

        /// <summary>测试用成员玩家标识一。</summary>
        private const long MemberOneId = 1002;

        /// <summary>测试用成员玩家标识二。</summary>
        private const long MemberTwoId = 1003;

        /// <summary>测试用群外玩家标识（全程不入群）。</summary>
        private const long OutsiderId = 1004;

        /// <summary>测试用群组名称。</summary>
        private const string GroupName = "测试群组";

        /// <summary>测试用邀请存活时长（秒）。</summary>
        private const long InviteTimeToLiveSeconds = 86400;

        /// <summary>
        /// 验证建群后发起人即群主、且已是首个成员，群组处于 Active（VC-6.6：建群人即群主且是首个成员）。
        /// </summary>
        [Fact]
        public async Task CreateAsync_ShouldCreateActiveGroupWithOwnerAsFirstMember()
        {
            // Arrange
            var service = CreateService(out var recorder);

            // Act
            var outcome = await service.CreateAsync(CreatePlayerScope(OwnerId), GroupName);

            // Assert
            Assert.True(outcome.IsSuccess);
            Assert.Equal(OnlineGroupState.Active, outcome.Data.State);
            Assert.Equal(OwnerId, outcome.Data.OwnerId);
            Assert.Equal(GroupName, outcome.Data.Name);
            Assert.Equal(0, outcome.Data.Revision);
            Assert.Empty(outcome.Data.Metadata);
            Assert.Single(outcome.Data.Members);
            Assert.Equal(OwnerId, outcome.Data.Members[0].PlayerId);
            Assert.Equal(OnlineGroupRole.Owner, outcome.Data.Members[0].Role);
            Assert.Equal(1, CountGroupActions(recorder, OnlineSocialEvents.GroupCreated));
        }

        /// <summary>
        /// 验证群名为空白时按参数非法拒绝，不落库（VC-6.6：建群入参校验）。
        /// </summary>
        [Fact]
        public async Task CreateAsync_WhenNameBlank_ShouldReturnParameterInvalid()
        {
            // Arrange
            var service = CreateService(out var recorder);

            // Act
            var outcome = await service.CreateAsync(CreatePlayerScope(OwnerId), "   ");

            // Assert
            Assert.False(outcome.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, outcome.Code);
            Assert.Equal(0, recorder.Count);
        }

        /// <summary>
        /// 验证邀请被接受后成员数变为 2，且发出成员加入事件（VC-6.7：邀请 → 接受 → 成员数为 2）。
        /// </summary>
        [Fact]
        public async Task AnswerInviteAsync_WhenAccepted_ShouldAddMemberAndPublishJoinedEvent()
        {
            // Arrange
            var service = CreateService(out var recorder);
            var group = await CreateGroupAsync(service, OwnerId);

            // Act
            var invite = await service.InviteAsync(CreatePlayerScope(OwnerId), group.GroupId, MemberOneId);
            var outcome = await service.AcceptInviteAsync(CreatePlayerScope(MemberOneId), group.GroupId, invite.Data.InviteId);

            // Assert
            Assert.True(invite.IsSuccess);
            Assert.Equal(OwnerId, invite.Data.InviterId);
            Assert.Equal(MemberOneId, invite.Data.InviteeId);
            Assert.Equal(OnlineGroupInviteState.Pending, invite.Data.State);
            Assert.True(invite.Data.ExpiresAtTime > 0);
            Assert.Equal(1, CountGroupActions(recorder, OnlineSocialEvents.GroupInviteChanged));

            Assert.True(outcome.IsSuccess);
            Assert.Equal(2, outcome.Data.Members.Count);
            Assert.True(outcome.Data.Contains(MemberOneId));
            Assert.Equal(OnlineGroupRole.Member, FindMember(outcome.Data, MemberOneId).Role);

            var accepted = FindInvite(outcome.Data, invite.Data.InviteId);
            Assert.Equal(OnlineGroupInviteState.Accepted, accepted.State);
            Assert.True(accepted.RespondedAtTime > 0);
            Assert.Equal(1, CountGroupActions(recorder, OnlineSocialEvents.GroupMemberJoined));
        }

        /// <summary>
        /// 验证拒绝邀请不改变成员集合，且发出邀请变更事件（VC-6.7：拒绝后成员数不变）。
        /// </summary>
        [Fact]
        public async Task AnswerInviteAsync_WhenRejected_ShouldKeepMemberCount()
        {
            // Arrange
            var service = CreateService(out var recorder);
            var group = await CreateGroupAsync(service, OwnerId);
            var invite = await service.InviteAsync(CreatePlayerScope(OwnerId), group.GroupId, MemberOneId);

            // Act
            var outcome = await service.RejectInviteAsync(CreatePlayerScope(MemberOneId), group.GroupId, invite.Data.InviteId);

            // Assert
            Assert.True(outcome.IsSuccess);
            Assert.Single(outcome.Data.Members);
            Assert.False(outcome.Data.Contains(MemberOneId));
            Assert.Equal(OnlineGroupInviteState.Rejected, FindInvite(outcome.Data, invite.Data.InviteId).State);
            Assert.Equal(2, CountGroupActions(recorder, OnlineSocialEvents.GroupInviteChanged));
        }

        /// <summary>
        /// 验证邀请已是成员者被状态操作禁止拒绝，且不新建第二条邀请（VC-6.7：邀请幂等与成员唯一性）。
        /// </summary>
        [Fact]
        public async Task InviteAsync_WhenInviteeAlreadyMember_ShouldReturnStateOperationForbidden()
        {
            // Arrange
            var service = CreateService(out _);
            var group = await CreateGroupAsync(service, OwnerId);
            await InviteAndAcceptAsync(service, group.GroupId, OwnerId, MemberOneId);

            // Act
            var outcome = await service.InviteAsync(CreatePlayerScope(OwnerId), group.GroupId, MemberOneId);

            // Assert
            Assert.False(outcome.IsSuccess);
            Assert.Equal(OnlineErrorCode.StateOperationForbidden, outcome.Code);
        }

        /// <summary>
        /// 验证成员数触达 MaxMembers 上限时接受邀请被状态操作禁止，成员数不越界（VC-6.7：上限错误码）。
        /// </summary>
        [Fact]
        public async Task AnswerInviteAsync_WhenMembersReachMax_ShouldReturnStateOperationForbidden()
        {
            // Arrange
            var service = CreateService(out _, 2, InviteTimeToLiveSeconds);
            var group = await CreateGroupAsync(service, OwnerId);
            await InviteAndAcceptAsync(service, group.GroupId, OwnerId, MemberOneId);
            var invite = await service.InviteAsync(CreatePlayerScope(OwnerId), group.GroupId, MemberTwoId);
            Assert.True(invite.IsSuccess);

            // Act
            var outcome = await service.AcceptInviteAsync(CreatePlayerScope(MemberTwoId), group.GroupId, invite.Data.InviteId);

            // Assert
            Assert.False(outcome.IsSuccess);
            Assert.Equal(OnlineErrorCode.StateOperationForbidden, outcome.Code);

            var members = await service.ListMembersAsync(CreatePlayerScope(OwnerId), group.GroupId);
            Assert.True(members.IsSuccess);
            Assert.Equal(2, members.Data.Count);
        }

        /// <summary>
        /// 验证群主不得退出群组，成员集合保持不变（VC-6.6：群主身份单点持有，须先解散）。
        /// </summary>
        [Fact]
        public async Task LeaveAsync_WhenCallerIsOwner_ShouldReturnStateOperationForbidden()
        {
            // Arrange
            var service = CreateService(out var recorder);
            var group = await CreateGroupAsync(service, OwnerId);
            await InviteAndAcceptAsync(service, group.GroupId, OwnerId, MemberOneId);

            // Act
            var outcome = await service.LeaveAsync(CreatePlayerScope(OwnerId), group.GroupId);

            // Assert
            Assert.False(outcome.IsSuccess);
            Assert.Equal(OnlineErrorCode.StateOperationForbidden, outcome.Code);
            Assert.Equal(0, CountGroupActions(recorder, OnlineSocialEvents.GroupMemberLeft));

            var members = await service.ListMembersAsync(CreatePlayerScope(OwnerId), group.GroupId);
            Assert.Equal(2, members.Data.Count);
        }

        /// <summary>
        /// 验证群主不能被踢出（管理员执行时报状态操作禁止），群主也不能踢出自己（VC-6.6：群主不可被踢）。
        /// </summary>
        [Fact]
        public async Task KickAsync_WhenTargetIsOwnerOrSelf_ShouldBeRejected()
        {
            // Arrange
            var service = CreateService(out _);
            var group = await CreateGroupAsync(service, OwnerId);
            await InviteAndAcceptAsync(service, group.GroupId, OwnerId, MemberOneId);
            var promoted = await service.SetRoleAsync(CreatePlayerScope(OwnerId), group.GroupId, MemberOneId, OnlineGroupRole.Admin);
            Assert.True(promoted.IsSuccess);

            // Act
            var kickedOwner = await service.KickAsync(CreatePlayerScope(MemberOneId), group.GroupId, OwnerId);
            var kickedSelf = await service.KickAsync(CreatePlayerScope(OwnerId), group.GroupId, OwnerId);

            // Assert
            Assert.False(kickedOwner.IsSuccess);
            Assert.Equal(OnlineErrorCode.StateOperationForbidden, kickedOwner.Code);
            Assert.False(kickedSelf.IsSuccess);
            Assert.Equal(OnlineErrorCode.ScopeDenied, kickedSelf.Code);

            var members = await service.ListMembersAsync(CreatePlayerScope(OwnerId), group.GroupId);
            Assert.Equal(2, members.Data.Count);
        }

        /// <summary>
        /// 验证把成员设为 Owner 被参数校验直接拒绝（VC-6.6：群主转让不在范围内，服务不发明该行为）。
        /// </summary>
        [Fact]
        public async Task SetRoleAsync_WhenTargetRoleIsOwner_ShouldReturnParameterInvalid()
        {
            // Arrange
            var service = CreateService(out _);
            var group = await CreateGroupAsync(service, OwnerId);
            await InviteAndAcceptAsync(service, group.GroupId, OwnerId, MemberOneId);

            // Act
            var outcome = await service.SetRoleAsync(CreatePlayerScope(OwnerId), group.GroupId, MemberOneId, OnlineGroupRole.Owner);

            // Assert
            Assert.False(outcome.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, outcome.Code);
        }

        /// <summary>
        /// 验证群主调整成员角色生效并发出角色变更事件，重复调整为同一角色时幂等不重发（VC-6.6：角色是权限唯一判据）。
        /// </summary>
        [Fact]
        public async Task SetRoleAsync_ShouldChangeRoleAndPublishRoleChangedEventOnce()
        {
            // Arrange
            var service = CreateService(out var recorder);
            var group = await CreateGroupAsync(service, OwnerId);
            await InviteAndAcceptAsync(service, group.GroupId, OwnerId, MemberOneId);

            // Act
            var outcome = await service.SetRoleAsync(CreatePlayerScope(OwnerId), group.GroupId, MemberOneId, OnlineGroupRole.Admin);
            var repeated = await service.SetRoleAsync(CreatePlayerScope(OwnerId), group.GroupId, MemberOneId, OnlineGroupRole.Admin);

            // Assert
            Assert.True(outcome.IsSuccess);
            Assert.Equal(OnlineGroupRole.Admin, FindMember(outcome.Data, MemberOneId).Role);
            Assert.True(repeated.IsSuccess);
            Assert.Equal(1, CountGroupActions(recorder, OnlineSocialEvents.GroupRoleChanged));

            var demotedOwner = await service.SetRoleAsync(CreatePlayerScope(OwnerId), group.GroupId, OwnerId, OnlineGroupRole.Admin);
            Assert.False(demotedOwner.IsSuccess);
            Assert.Equal(OnlineErrorCode.StateOperationForbidden, demotedOwner.Code);
        }

        /// <summary>
        /// 验证被踢者与退群者都从成员列表消失，且各发出一条成员离开事件（VC-6.6：踢人 / 退群事实一致）。
        /// </summary>
        [Fact]
        public async Task KickAndLeave_ShouldRemovePlayersFromMemberList()
        {
            // Arrange
            var service = CreateService(out var recorder);
            var group = await CreateGroupAsync(service, OwnerId);
            await InviteAndAcceptAsync(service, group.GroupId, OwnerId, MemberOneId);
            await InviteAndAcceptAsync(service, group.GroupId, OwnerId, MemberTwoId);

            // Act
            var kicked = await service.KickAsync(CreatePlayerScope(OwnerId), group.GroupId, MemberOneId);
            var left = await service.LeaveAsync(CreatePlayerScope(MemberTwoId), group.GroupId);

            // Assert
            Assert.True(kicked.IsSuccess);
            Assert.False(kicked.Data.Contains(MemberOneId));
            Assert.Equal(2, kicked.Data.Members.Count);

            Assert.True(left.IsSuccess);
            Assert.False(left.Data.Contains(MemberTwoId));
            Assert.Single(left.Data.Members);
            Assert.Equal(OwnerId, left.Data.Members[0].PlayerId);
            Assert.Equal(2, CountGroupActions(recorder, OnlineSocialEvents.GroupMemberLeft));
        }

        /// <summary>
        /// 验证非成员对群的一切操作一律返回 ResourceNotFound 而非 ScopeDenied，不泄露群存在性（VC-6.6：反预言）。
        /// </summary>
        [Fact]
        public async Task NonMemberOperations_ShouldReturnResourceNotFoundInsteadOfScopeDenied()
        {
            // Arrange
            var service = CreateService(out _);
            var group = await CreateGroupAsync(service, OwnerId);
            await InviteAndAcceptAsync(service, group.GroupId, OwnerId, MemberOneId);
            var pending = await service.InviteAsync(CreatePlayerScope(OwnerId), group.GroupId, MemberTwoId);
            var outsider = CreatePlayerScope(OutsiderId);

            // Act
            var invite = await service.InviteAsync(outsider, group.GroupId, MemberTwoId);
            var members = await service.ListMembersAsync(outsider, group.GroupId);
            var metadata = await service.UpdateMetadataAsync(outsider, group.GroupId, "notice", "公告");
            var kick = await service.KickAsync(outsider, group.GroupId, MemberOneId);
            var setRole = await service.SetRoleAsync(outsider, group.GroupId, MemberOneId, OnlineGroupRole.Admin);
            var leave = await service.LeaveAsync(outsider, group.GroupId);
            var disband = await service.DisbandAsync(outsider, group.GroupId);
            var revoke = await service.RevokeInviteAsync(outsider, group.GroupId, pending.Data.InviteId);

            // Assert
            Assert.Equal(OnlineErrorCode.ResourceNotFound, invite.Code);
            Assert.Equal(OnlineErrorCode.ResourceNotFound, members.Code);
            Assert.Equal(OnlineErrorCode.ResourceNotFound, metadata.Code);
            Assert.Equal(OnlineErrorCode.ResourceNotFound, kick.Code);
            Assert.Equal(OnlineErrorCode.ResourceNotFound, setRole.Code);
            Assert.Equal(OnlineErrorCode.ResourceNotFound, leave.Code);
            Assert.Equal(OnlineErrorCode.ResourceNotFound, disband.Code);
            Assert.Equal(OnlineErrorCode.ResourceNotFound, revoke.Code);

            // 群外玩家的全部调用都是纯拒绝：群状态与成员集合不受影响
            var ownerView = await service.ListMembersAsync(CreatePlayerScope(OwnerId), group.GroupId);
            Assert.True(ownerView.IsSuccess);
            Assert.Equal(2, ownerView.Data.Count);
        }

        /// <summary>
        /// 验证成员但角色不足时返回 ScopeDenied（普通成员踢人 / 写元数据 / 调角色 / 解散，管理员踢管理员）
        /// （VC-6.6：权限判据只有「是否成员」与「角色高低」两条正交轴）。
        /// </summary>
        [Fact]
        public async Task MemberOperations_WhenRoleInsufficient_ShouldReturnScopeDenied()
        {
            // Arrange
            var service = CreateService(out _);
            var group = await CreateGroupAsync(service, OwnerId);
            await InviteAndAcceptAsync(service, group.GroupId, OwnerId, MemberOneId);
            await InviteAndAcceptAsync(service, group.GroupId, OwnerId, MemberTwoId);
            var memberScope = CreatePlayerScope(MemberOneId);

            // Act
            var kick = await service.KickAsync(memberScope, group.GroupId, MemberTwoId);
            var metadata = await service.UpdateMetadataAsync(memberScope, group.GroupId, "notice", "公告");
            var setRole = await service.SetRoleAsync(memberScope, group.GroupId, MemberTwoId, OnlineGroupRole.Admin);
            var disband = await service.DisbandAsync(memberScope, group.GroupId);

            // Assert
            Assert.Equal(OnlineErrorCode.ScopeDenied, kick.Code);
            Assert.Equal(OnlineErrorCode.ScopeDenied, metadata.Code);
            Assert.Equal(OnlineErrorCode.ScopeDenied, setRole.Code);
            Assert.Equal(OnlineErrorCode.ScopeDenied, disband.Code);

            await service.SetRoleAsync(CreatePlayerScope(OwnerId), group.GroupId, MemberOneId, OnlineGroupRole.Admin);
            await service.SetRoleAsync(CreatePlayerScope(OwnerId), group.GroupId, MemberTwoId, OnlineGroupRole.Admin);
            var peerKick = await service.KickAsync(CreatePlayerScope(MemberOneId), group.GroupId, MemberTwoId);
            Assert.Equal(OnlineErrorCode.ScopeDenied, peerKick.Code);
        }

        /// <summary>
        /// 验证解散后状态为 Disbanded、成员集合保留供审计、再次邀请被状态结束拒绝，重复解散幂等（VC-6.6：解散是唯一终态）。
        /// </summary>
        [Fact]
        public async Task DisbandAsync_ShouldReachDisbandedAndRejectFurtherInvite()
        {
            // Arrange
            var service = CreateService(out var recorder);
            var group = await CreateGroupAsync(service, OwnerId);
            await InviteAndAcceptAsync(service, group.GroupId, OwnerId, MemberOneId);

            // Act
            var outcome = await service.DisbandAsync(CreatePlayerScope(OwnerId), group.GroupId);

            // Assert
            Assert.True(outcome.IsSuccess);
            Assert.Equal(OnlineGroupState.Disbanded, outcome.Data.State);
            Assert.Equal(2, outcome.Data.Members.Count);
            Assert.Equal(1, CountGroupActions(recorder, OnlineSocialEvents.GroupDisbanded));

            var invite = await service.InviteAsync(CreatePlayerScope(OwnerId), group.GroupId, MemberTwoId);
            Assert.False(invite.IsSuccess);
            Assert.Equal(OnlineErrorCode.StateEnded, invite.Code);

            var repeated = await service.DisbandAsync(CreatePlayerScope(OwnerId), group.GroupId);
            Assert.True(repeated.IsSuccess);
            Assert.Equal(OnlineGroupState.Disbanded, repeated.Data.State);
            Assert.Equal(1, CountGroupActions(recorder, OnlineSocialEvents.GroupDisbanded));
        }

        /// <summary>
        /// 验证元数据写入按键落值（键去空白、值 null 归一为空串），并发出元数据变更事件；空键按参数非法拒绝
        /// （VC-6.7：UpdateMetadataAsync 写键值）。
        /// </summary>
        [Fact]
        public async Task UpdateMetadataAsync_ShouldWriteKeyValueAndPublishMetadataUpdatedEvent()
        {
            // Arrange
            var service = CreateService(out var recorder);
            var group = await CreateGroupAsync(service, OwnerId);

            // Act
            var outcome = await service.UpdateMetadataAsync(CreatePlayerScope(OwnerId), group.GroupId, " notice ", "公告内容");
            var nullValue = await service.UpdateMetadataAsync(CreatePlayerScope(OwnerId), group.GroupId, "empty", null);
            var blankKey = await service.UpdateMetadataAsync(CreatePlayerScope(OwnerId), group.GroupId, "   ", "公告内容");

            // Assert
            Assert.True(outcome.IsSuccess);
            Assert.True(outcome.Data.Metadata.ContainsKey("notice"));
            Assert.Equal("公告内容", outcome.Data.Metadata["notice"]);
            Assert.Equal(2, CountGroupActions(recorder, OnlineSocialEvents.GroupMetadataUpdated));

            Assert.True(nullValue.IsSuccess);
            Assert.Equal(string.Empty, nullValue.Data.Metadata["empty"]);

            Assert.False(blankKey.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, blankKey.Code);
        }

        /// <summary>
        /// 验证超期邀请被扫描收敛为 Expired 并返回扫描条数，过期邀请不能再被接受，重复扫描不重复计数（VC-6.7：邀请超期）。
        /// </summary>
        [Fact]
        public async Task SweepExpiredInvitesAsync_ShouldExpireOverdueInvite()
        {
            // Arrange
            var service = CreateService(out _, 100, 1);
            var group = await CreateGroupAsync(service, OwnerId);
            var invite = await service.InviteAsync(CreatePlayerScope(OwnerId), group.GroupId, MemberOneId);
            Assert.Equal(OnlineGroupInviteState.Pending, invite.Data.State);
            var futureNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 3_600_000;

            // Act
            var sweep = await service.SweepExpiredInvitesAsync(TenantId, AppId, futureNow);
            var accepted = await service.AcceptInviteAsync(CreatePlayerScope(MemberOneId), group.GroupId, invite.Data.InviteId);
            var repeated = await service.SweepExpiredInvitesAsync(TenantId, AppId, futureNow);

            // Assert
            Assert.True(sweep.IsSuccess);
            Assert.Equal(1, sweep.Data);

            Assert.False(accepted.IsSuccess);
            Assert.Equal(OnlineErrorCode.StateEnded, accepted.Code);

            Assert.Equal(0, repeated.Data);
        }

        /// <summary>
        /// 验证群频道成员资格按当前成员事实裁决（C99 / VC-6.9）：在群成员为 true，非成员为 false，
        /// 被踢后立即翻转为 false，群标识不存在或为空一律 false。
        /// </summary>
        [Fact]
        public async Task IsChannelMemberAsync_WhenGroupChannel_ShouldJudgeByCurrentMembership()
        {
            // Arrange
            var service = CreateService(out _);
            var group = await CreateGroupAsync(service, OwnerId);
            await InviteAndAcceptAsync(service, group.GroupId, OwnerId, MemberOneId);

            // Act
            var ownerMember = await service.IsChannelMemberAsync(TenantId, AppId, OnlineChatChannelKind.Group, group.GroupId, OwnerId);
            var joinedMember = await service.IsChannelMemberAsync(TenantId, AppId, OnlineChatChannelKind.Group, group.GroupId, MemberOneId);
            var outsider = await service.IsChannelMemberAsync(TenantId, AppId, OnlineChatChannelKind.Group, group.GroupId, OutsiderId);
            var missingGroup = await service.IsChannelMemberAsync(TenantId, AppId, OnlineChatChannelKind.Group, "grp-不存在", OwnerId);
            var emptyBoundId = await service.IsChannelMemberAsync(TenantId, AppId, OnlineChatChannelKind.Group, string.Empty, OwnerId);
            var nullBoundId = await service.IsChannelMemberAsync(TenantId, AppId, OnlineChatChannelKind.Group, null, OwnerId);

            // Assert
            Assert.True(ownerMember);
            Assert.True(joinedMember);
            Assert.False(outsider);
            Assert.False(missingGroup);
            Assert.False(emptyBoundId);
            Assert.False(nullBoundId);

            await service.KickAsync(CreatePlayerScope(OwnerId), group.GroupId, MemberOneId);
            var kickedMember = await service.IsChannelMemberAsync(TenantId, AppId, OnlineChatChannelKind.Group, group.GroupId, MemberOneId);
            Assert.False(kickedMember);
        }

        /// <summary>
        /// 验证探针不认领非本域频道（Direct / Party / Global 一律 false），且群解散后频道随群封存，
        /// 即使成员集合仍含该玩家也返回 false（C99 / VC-6.8：频道成员资格的唯一裁决方）。
        /// </summary>
        [Fact]
        public async Task IsChannelMemberAsync_WhenKindIsNotGroupOrGroupDisbanded_ShouldReturnFalse()
        {
            // Arrange
            var service = CreateService(out _);
            var group = await CreateGroupAsync(service, OwnerId);
            await InviteAndAcceptAsync(service, group.GroupId, OwnerId, MemberOneId);

            // Act
            var direct = await service.IsChannelMemberAsync(TenantId, AppId, OnlineChatChannelKind.Direct, group.GroupId, OwnerId);
            var party = await service.IsChannelMemberAsync(TenantId, AppId, OnlineChatChannelKind.Party, group.GroupId, OwnerId);
            var global = await service.IsChannelMemberAsync(TenantId, AppId, OnlineChatChannelKind.Global, group.GroupId, OwnerId);

            await service.DisbandAsync(CreatePlayerScope(OwnerId), group.GroupId);
            var disbanded = await service.IsChannelMemberAsync(TenantId, AppId, OnlineChatChannelKind.Group, group.GroupId, OwnerId);

            // Assert
            Assert.False(direct);
            Assert.False(party);
            Assert.False(global);
            Assert.False(disbanded);

            var members = await service.ListMembersAsync(CreatePlayerScope(OwnerId), group.GroupId);
            Assert.True(members.IsSuccess);
            Assert.Contains(members.Data, member => member.PlayerId == OwnerId);
        }

        /// <summary>
        /// 创建被测服务（默认成员上限与邀请存活时长）。
        /// </summary>
        /// <param name="recorder">事件记录桩。</param>
        /// <returns>群组服务实例。</returns>
        private static OnlineGroupService CreateService(out OnlineEventRecorder recorder)
        {
            recorder = new OnlineEventRecorder();
            return new OnlineGroupService(new InMemoryOnlineGroupStore(), recorder);
        }

        /// <summary>
        /// 创建被测服务（自定义成员上限与邀请存活时长）。
        /// </summary>
        /// <param name="recorder">事件记录桩。</param>
        /// <param name="maxMembers">成员数上限。</param>
        /// <param name="inviteTimeToLiveSeconds">邀请存活时长（秒）。</param>
        /// <returns>群组服务实例。</returns>
        private static OnlineGroupService CreateService(out OnlineEventRecorder recorder, int maxMembers, long inviteTimeToLiveSeconds)
        {
            recorder = new OnlineEventRecorder();
            return new OnlineGroupService(new InMemoryOnlineGroupStore(), recorder, maxMembers, inviteTimeToLiveSeconds);
        }

        /// <summary>
        /// 创建一个仅含群主的新群组。
        /// </summary>
        /// <param name="service">群组服务。</param>
        /// <param name="ownerId">群主玩家标识。</param>
        /// <returns>新建群组。</returns>
        private static async Task<OnlineGroup> CreateGroupAsync(OnlineGroupService service, long ownerId)
        {
            var outcome = await service.CreateAsync(CreatePlayerScope(ownerId), GroupName);
            Assert.True(outcome.IsSuccess);
            return outcome.Data;
        }

        /// <summary>
        /// 邀请指定玩家入群并接受邀请。
        /// </summary>
        /// <param name="service">群组服务。</param>
        /// <param name="groupId">群组标识。</param>
        /// <param name="inviterId">邀请人玩家标识。</param>
        /// <param name="inviteeId">被邀请玩家标识。</param>
        /// <returns>接受后的群组。</returns>
        private static async Task<OnlineGroup> InviteAndAcceptAsync(OnlineGroupService service, string groupId, long inviterId, long inviteeId)
        {
            var invite = await service.InviteAsync(CreatePlayerScope(inviterId), groupId, inviteeId);
            Assert.True(invite.IsSuccess);
            var outcome = await service.AcceptInviteAsync(CreatePlayerScope(inviteeId), groupId, invite.Data.InviteId);
            Assert.True(outcome.IsSuccess);
            return outcome.Data;
        }

        /// <summary>
        /// 构造玩家作用域。
        /// </summary>
        /// <param name="playerId">玩家标识。</param>
        /// <returns>作用域。</returns>
        private static OnlineScope CreatePlayerScope(long playerId)
        {
            return new OnlineScope(TenantId, AppId, ServerId, playerId);
        }

        /// <summary>
        /// 统计已发布群组变更事件中指定动作的出现次数。
        /// </summary>
        /// <param name="recorder">事件记录桩。</param>
        /// <param name="action">群组变更动作常量。</param>
        /// <returns>出现次数。</returns>
        private static int CountGroupActions(OnlineEventRecorder recorder, string action)
        {
            var count = 0;
            foreach (var published in recorder.Filter(OnlineSocialEvents.GroupChanged))
            {
                string current;
                if (published.PayloadAuditFields.TryGetValue("Action", out current) && current == action)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// 在群记录内按玩家标识定位成员条目。
        /// </summary>
        /// <param name="group">群记录。</param>
        /// <param name="playerId">玩家标识。</param>
        /// <returns>成员条目；不是成员返回 null。</returns>
        private static OnlineGroupMember FindMember(OnlineGroup group, long playerId)
        {
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
        /// 在群记录内按邀请标识定位邀请条目。
        /// </summary>
        /// <param name="group">群记录。</param>
        /// <param name="inviteId">邀请标识。</param>
        /// <returns>邀请条目；不存在返回 null。</returns>
        private static OnlineGroupInvite FindInvite(OnlineGroup group, string inviteId)
        {
            foreach (var invite in group.Invites)
            {
                if (string.Equals(invite.InviteId, inviteId, StringComparison.Ordinal))
                {
                    return invite;
                }
            }

            return null;
        }
    }
}
