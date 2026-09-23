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
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Online.Contracts;
using GameFrameX.Online.Events;
using GameFrameX.Online.Scope;
using GameFrameX.Online.Social;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 社交裁决收口点测试（vault:C7 VC-6.3/VC-6.4/VC-6.5 + S6.3：屏蔽三通路统一、
    /// 禁言只挡发言、封禁拒绝一切主动互动，以及举报案件的状态机、证据链与反预言）。
    /// <para>
    /// 本类锁定的是一条**跨域唯一入口**的语义：Chat / Party / Matchmaking 三方都不自行判定屏蔽与处罚，
    /// 一律经 <see cref="OnlineSocialDecisionService.EvaluateAsync"/>，因此这里的断言就是三通路的一致性契约。
    /// </para>
    /// </summary>
    public class OnlineSocialDecisionServiceTests
    {
        /// <summary>测试用租户标识。</summary>
        private const long TenantId = 1;

        /// <summary>测试用应用标识。</summary>
        private const long AppId = 10;

        /// <summary>测试用区服标识。</summary>
        private const long ServerId = 100;

        /// <summary>测试用玩家一标识（多数场景的发起方）。</summary>
        private const long PlayerOne = 1001;

        /// <summary>测试用玩家二标识（多数场景的对端）。</summary>
        private const long PlayerTwo = 1002;

        /// <summary>测试用玩家三标识（无关第三方）。</summary>
        private const long PlayerThree = 1003;

        /// <summary>测试用 Admin 标识。</summary>
        private const long AdminId = 9001;

        /// <summary>
        /// 验证无任何关系与处罚时放行（基线：裁决不得无端拒绝）。
        /// </summary>
        [Fact]
        public async Task EvaluateAsync_WhenNoRelationAndNoPunishment_ShouldAllow()
        {
            // Arrange
            var service = CreateService(out _);

            // Act
            var decision = await service.EvaluateAsync(TenantId, AppId, PlayerOne, PlayerTwo, OnlineSocialInteractionPurpose.DirectMessage);

            // Assert
            Assert.True(decision.Allowed);
        }

        /// <summary>
        /// 验证对自己的定向互动被参数拒绝（自己不能是自己的社交对端）。
        /// </summary>
        [Fact]
        public async Task EvaluateAsync_WhenBothSidesAreSamePlayer_ShouldDenyAsInvalid()
        {
            // Arrange
            var service = CreateService(out _);

            // Act
            var decision = await service.EvaluateAsync(TenantId, AppId, PlayerOne, PlayerOne, OnlineSocialInteractionPurpose.DirectMessage);

            // Assert
            Assert.False(decision.Allowed);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, decision.Code);
        }

        /// <summary>
        /// 验证标识非法时被参数拒绝（0 或负数不构成有效玩家）。
        /// </summary>
        [Fact]
        public async Task EvaluateAsync_WhenPlayerIdInvalid_ShouldDenyAsInvalid()
        {
            // Arrange
            var service = CreateService(out _);

            // Act
            var zeroFrom = await service.EvaluateAsync(TenantId, AppId, 0, PlayerTwo, OnlineSocialInteractionPurpose.DirectMessage);
            var negativeTo = await service.EvaluateAsync(TenantId, AppId, PlayerOne, -1, OnlineSocialInteractionPurpose.DirectMessage);

            // Assert
            Assert.False(zeroFrom.Allowed);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, zeroFrom.Code);
            Assert.False(negativeTo.Allowed);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, negativeTo.Code);
        }

        /// <summary>
        /// 验证屏蔽在三种互动目的下都拒绝、且**两个方向都拒绝**（VC-6.3 三通路统一）。
        /// <para>
        /// 双向生效是刻意的：屏蔽若只挡一个方向，被屏蔽方仍可主动私聊、邀请、匹配到屏蔽者，
        /// 屏蔽就形同虚设（这正是「Block 后不能私聊、邀请或匹配到对方」的字面要求）。
        /// </para>
        /// </summary>
        [Fact]
        public async Task EvaluateAsync_WhenBlocked_ShouldDenyAllPurposesInBothDirections()
        {
            // Arrange
            var service = CreateService(out _);
            var purposes = new[]
            {
                OnlineSocialInteractionPurpose.DirectMessage,
                OnlineSocialInteractionPurpose.PartyInvite,
                OnlineSocialInteractionPurpose.Matchmaking,
            };
            Assert.True((await service.BlockAsync(CreatePlayerScope(PlayerOne), PlayerTwo)).IsSuccess);

            // Act & Assert
            foreach (var purpose in purposes)
            {
                var forward = await service.EvaluateAsync(TenantId, AppId, PlayerOne, PlayerTwo, purpose);
                var backward = await service.EvaluateAsync(TenantId, AppId, PlayerTwo, PlayerOne, purpose);
                Assert.False(forward.Allowed);
                Assert.False(backward.Allowed);
            }
        }

        /// <summary>
        /// 验证屏蔽不泄露方向（VC-6.3 隐私面）：只被单向屏蔽时，两个方向拿到的拒绝理由必须**逐字相同**。
        /// <para>
        /// 若两方向的错误码或文案有差异，被屏蔽方就能反推出「是谁屏蔽了谁」——
        /// 屏蔽是单向私密事实，泄露方向等于把屏蔽者的身份交出去。
        /// </para>
        /// </summary>
        [Fact]
        public async Task EvaluateAsync_WhenBlocked_ShouldNotRevealBlockDirection()
        {
            // Arrange
            var service = CreateService(out _);
            Assert.True((await service.BlockAsync(CreatePlayerScope(PlayerOne), PlayerTwo)).IsSuccess);

            // Act
            var blockedByOwner = await service.EvaluateAsync(TenantId, AppId, PlayerOne, PlayerTwo, OnlineSocialInteractionPurpose.DirectMessage);
            var blockedByTarget = await service.EvaluateAsync(TenantId, AppId, PlayerTwo, PlayerOne, OnlineSocialInteractionPurpose.DirectMessage);

            // Assert
            Assert.Equal(blockedByOwner.Code, blockedByTarget.Code);
            Assert.Equal(blockedByOwner.Reason, blockedByTarget.Reason);
        }

        /// <summary>
        /// 验证**静音（展示层静音）在任何目的下都不产生拒绝**。
        /// <para>
        /// 静音与禁言是两件事：静音是玩家自己的展示偏好，不构成对他人的权限剥夺。
        /// 若静音进入裁决路径，等于让玩家用一次静音单方面掐断对方的邀请与匹配——越权。
        /// </para>
        /// </summary>
        [Fact]
        public async Task EvaluateAsync_WhenDisplayMuted_ShouldNeverDenyAnyPurpose()
        {
            // Arrange
            var service = CreateService(out _);
            var muted = await service.MuteAsync(CreatePlayerScope(PlayerOne), PlayerTwo);
            Assert.True(muted.IsSuccess);
            var purposes = new[]
            {
                OnlineSocialInteractionPurpose.DirectMessage,
                OnlineSocialInteractionPurpose.PartyInvite,
                OnlineSocialInteractionPurpose.Matchmaking,
            };

            // Act & Assert
            foreach (var purpose in purposes)
            {
                var forward = await service.EvaluateAsync(TenantId, AppId, PlayerOne, PlayerTwo, purpose);
                var backward = await service.EvaluateAsync(TenantId, AppId, PlayerTwo, PlayerOne, purpose);
                Assert.True(forward.Allowed);
                Assert.True(backward.Allowed);
            }
        }

        /// <summary>
        /// 验证**禁言只挡发言**（VC-6.4）：对私聊拒绝，对邀请与匹配一律放行。
        /// </summary>
        [Fact]
        public async Task EvaluateAsync_WhenPunishedWithMute_ShouldDenyOnlyDirectMessage()
        {
            // Arrange
            var service = CreateService(out _, out var graphStore);
            await ApplyPunishmentAsync(graphStore, PlayerTwo, OnlinePunishmentKind.Mute);

            // Act
            var direct = await service.EvaluateAsync(TenantId, AppId, PlayerTwo, PlayerOne, OnlineSocialInteractionPurpose.DirectMessage);
            var invite = await service.EvaluateAsync(TenantId, AppId, PlayerTwo, PlayerOne, OnlineSocialInteractionPurpose.PartyInvite);
            var match = await service.EvaluateAsync(TenantId, AppId, PlayerTwo, PlayerOne, OnlineSocialInteractionPurpose.Matchmaking);

            // Assert
            Assert.False(direct.Allowed);
            Assert.Equal(OnlineErrorCode.RiskControlRejected, direct.Code);
            Assert.True(invite.Allowed);
            Assert.True(match.Allowed);
        }

        /// <summary>
        /// 验证禁言只约束**被处罚者自己发起**的互动，不影响他人与他的互动（处罚是行为约束不是关系切断）。
        /// </summary>
        [Fact]
        public async Task EvaluateAsync_WhenPunishedWithMute_ShouldNotBlockOthersInitiating()
        {
            // Arrange
            var service = CreateService(out _, out var graphStore);
            await ApplyPunishmentAsync(graphStore, PlayerTwo, OnlinePunishmentKind.Mute);

            // Act
            var otherInitiates = await service.EvaluateAsync(TenantId, AppId, PlayerOne, PlayerTwo, OnlineSocialInteractionPurpose.DirectMessage);

            // Assert
            Assert.True(otherInitiates.Allowed);
        }

        /// <summary>
        /// 验证封禁拒绝被处罚者主动发起的一切互动，错误码为 <see cref="OnlineErrorCode.AccountBanned"/>（VC-6.5）。
        /// </summary>
        [Fact]
        public async Task EvaluateAsync_WhenPunishedWithBan_ShouldDenyAllPurposesAsAccountBanned()
        {
            // Arrange
            var service = CreateService(out _, out var graphStore);
            await ApplyPunishmentAsync(graphStore, PlayerTwo, OnlinePunishmentKind.Ban);
            var purposes = new[]
            {
                OnlineSocialInteractionPurpose.DirectMessage,
                OnlineSocialInteractionPurpose.PartyInvite,
                OnlineSocialInteractionPurpose.Matchmaking,
            };

            // Act & Assert
            foreach (var purpose in purposes)
            {
                var decision = await service.EvaluateAsync(TenantId, AppId, PlayerTwo, PlayerOne, purpose);
                Assert.False(decision.Allowed);
                Assert.Equal(OnlineErrorCode.AccountBanned, decision.Code);
            }
        }

        /// <summary>
        /// 验证尚未生效的处罚不产生拒绝（生效时刻之前不得提前剥夺权利）。
        /// </summary>
        [Fact]
        public async Task EvaluateAsync_WhenPunishmentNotEffectiveYet_ShouldAllow()
        {
            // Arrange
            var service = CreateService(out _, out var graphStore);
            var future = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 60000;
            var punishmentService = new OnlinePunishmentService(graphStore, new OnlineEventRecorder());
            var scheduled = await punishmentService.ApplyAsync(new OnlinePunishmentRequest { TenantId = TenantId, AppId = AppId, PlayerId = PlayerTwo, Kind = OnlinePunishmentKind.Ban, Reason = "预置封禁", EffectiveAtTime = future, ExpiresAtTime = 0, AdminId = AdminId });
            Assert.True(scheduled.IsSuccess);

            // Act
            var decision = await service.EvaluateAsync(TenantId, AppId, PlayerTwo, PlayerOne, OnlineSocialInteractionPurpose.DirectMessage);

            // Assert
            Assert.True(decision.Allowed);
        }

        /// <summary>
        /// 验证已过失效时刻的处罚不产生拒绝（处罚到期即自动解除，无需人工撤销）。
        /// </summary>
        [Fact]
        public async Task EvaluateAsync_WhenPunishmentExpired_ShouldAllow()
        {
            // Arrange
            var service = CreateService(out _, out var graphStore);
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var punishmentService = new OnlinePunishmentService(graphStore, new OnlineEventRecorder());
            var expired = await punishmentService.ApplyAsync(new OnlinePunishmentRequest { TenantId = TenantId, AppId = AppId, PlayerId = PlayerTwo, Kind = OnlinePunishmentKind.Ban, Reason = "历史封禁", EffectiveAtTime = now - 100000, ExpiresAtTime = now - 50000, AdminId = AdminId });
            Assert.True(expired.IsSuccess);

            // Act
            var decision = await service.EvaluateAsync(TenantId, AppId, PlayerTwo, PlayerOne, OnlineSocialInteractionPurpose.DirectMessage);

            // Assert
            Assert.True(decision.Allowed);
        }

        /// <summary>
        /// 验证撤销处罚后裁决立即恢复放行（撤销是即时生效的权利恢复，不是等 TTL）。
        /// </summary>
        [Fact]
        public async Task EvaluateAsync_WhenPunishmentRevoked_ShouldAllow()
        {
            // Arrange
            var service = CreateService(out _, out var graphStore);
            var applied = await ApplyPunishmentAsync(graphStore, PlayerTwo, OnlinePunishmentKind.Ban);
            var punishmentService = new OnlinePunishmentService(graphStore, new OnlineEventRecorder());
            var revoked = await punishmentService.RevokeAsync(TenantId, AppId, applied.PunishmentId, AdminId);
            Assert.True(revoked.IsSuccess);

            // Act
            var decision = await service.EvaluateAsync(TenantId, AppId, PlayerTwo, PlayerOne, OnlineSocialInteractionPurpose.DirectMessage);

            // Assert
            Assert.True(decision.Allowed);
        }

        /// <summary>
        /// 验证同时被封禁与被屏蔽时，裁决先报封禁。
        /// <para>
        /// 顺序是刻意的：处罚是平台对该玩家账号状态的事实陈述，屏蔽是玩家间的私密事实。
        /// 先报处罚既让被处罚者拿到可申诉的真正理由（而不是一句无从申辩的「互动被拒」），
        /// 也顺带避免了用封禁做掩护去探测屏蔽关系。
        /// </para>
        /// </summary>
        [Fact]
        public async Task EvaluateAsync_WhenBothBannedAndBlocked_ShouldReportBanFirst()
        {
            // Arrange
            var service = CreateService(out _, out var graphStore);
            Assert.True((await service.BlockAsync(CreatePlayerScope(PlayerOne), PlayerTwo)).IsSuccess);
            await ApplyPunishmentAsync(graphStore, PlayerTwo, OnlinePunishmentKind.Ban);

            // Act
            var decision = await service.EvaluateAsync(TenantId, AppId, PlayerTwo, PlayerOne, OnlineSocialInteractionPurpose.DirectMessage);

            // Assert
            Assert.False(decision.Allowed);
            Assert.Equal(OnlineErrorCode.AccountBanned, decision.Code);
        }

        /// <summary>
        /// 验证无对端的发言入口同样受禁言约束（Global / Party / Group 频道发送与内容发布）。
        /// </summary>
        [Fact]
        public async Task EvaluateSendAsync_WhenPunishedWithMute_ShouldDeny()
        {
            // Arrange
            var service = CreateService(out _, out var graphStore);
            await ApplyPunishmentAsync(graphStore, PlayerTwo, OnlinePunishmentKind.Mute);

            // Act
            var decision = await service.EvaluateSendAsync(CreatePlayerScope(PlayerTwo));

            // Assert
            Assert.False(decision.Allowed);
            Assert.Equal(OnlineErrorCode.RiskControlRejected, decision.Code);
        }

        /// <summary>
        /// 验证无对端的发言入口在缺少玩家主体位时被参数拒绝。
        /// </summary>
        [Fact]
        public async Task EvaluateSendAsync_WhenScopeLacksPlayer_ShouldDenyAsInvalid()
        {
            // Arrange
            var service = CreateService(out _);

            // Act
            var decision = await service.EvaluateSendAsync(new OnlineScope(TenantId, AppId, ServerId, 0));

            // Assert
            Assert.False(decision.Allowed);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, decision.Code);
        }

        /// <summary>
        /// 验证屏蔽写入幂等：重复屏蔽返回既有记录，且**不重复发事件**。
        /// <para>
        /// 若重复屏蔽再发一次事件，下游通知与审计会看到两条「新增屏蔽」，
        /// 玩家侧表现为重复提示，审计侧则是无法对账的重复事实。
        /// </para>
        /// </summary>
        [Fact]
        public async Task BlockAsync_WhenRepeated_ShouldBeIdempotentAndNotRepublishEvent()
        {
            // Arrange
            var service = CreateService(out var recorder);

            // Act
            var first = await service.BlockAsync(CreatePlayerScope(PlayerOne), PlayerTwo);
            var second = await service.BlockAsync(CreatePlayerScope(PlayerOne), PlayerTwo);

            // Assert
            Assert.True(first.IsSuccess);
            Assert.True(second.IsSuccess);
            Assert.Equal(first.Data.BlockedPlayerId, second.Data.BlockedPlayerId);
            Assert.Equal(first.Data.CreatedAtTime, second.Data.CreatedAtTime);
            Assert.Single(recorder.Filter(OnlineSocialEvents.BlockChanged));
        }

        /// <summary>
        /// 验证屏蔽写入发出带动作标识的变更事件（下游据此刷新屏蔽列表）。
        /// </summary>
        [Fact]
        public async Task BlockAsync_ShouldPublishBlockChangedEvent()
        {
            // Arrange
            var service = CreateService(out var recorder);

            // Act
            var outcome = await service.BlockAsync(CreatePlayerScope(PlayerOne), PlayerTwo, "刷屏");

            // Assert
            Assert.True(outcome.IsSuccess);
            Assert.Equal("刷屏", outcome.Data.Reason);
            var events = recorder.Filter(OnlineSocialEvents.BlockChanged);
            Assert.Single(events);
            Assert.Equal(OnlineSocialEvents.Source, events[0].Source);
            Assert.Equal(OnlineSocialEvents.BlockAdded, events[0].PayloadAuditFields["Action"]);
        }

        /// <summary>
        /// 验证解除屏蔽后三种互动目的全部恢复放行（解除必须彻底，不能只放开一个方向）。
        /// </summary>
        [Fact]
        public async Task UnblockAsync_ShouldLiftDenialForAllPurposes()
        {
            // Arrange
            var service = CreateService(out _);
            Assert.True((await service.BlockAsync(CreatePlayerScope(PlayerOne), PlayerTwo)).IsSuccess);
            var purposes = new[]
            {
                OnlineSocialInteractionPurpose.DirectMessage,
                OnlineSocialInteractionPurpose.PartyInvite,
                OnlineSocialInteractionPurpose.Matchmaking,
            };

            // Act
            var unblocked = await service.UnblockAsync(CreatePlayerScope(PlayerOne), PlayerTwo);

            // Assert
            Assert.True(unblocked.IsSuccess);
            Assert.True(unblocked.Data);
            foreach (var purpose in purposes)
            {
                Assert.True((await service.EvaluateAsync(TenantId, AppId, PlayerOne, PlayerTwo, purpose)).Allowed);
                Assert.True((await service.EvaluateAsync(TenantId, AppId, PlayerTwo, PlayerOne, purpose)).Allowed);
            }
        }

        /// <summary>
        /// 验证聊天场景的举报必须带频道标识（否则 Admin 无从复现取证，证据链在入口就断了）。
        /// </summary>
        [Fact]
        public async Task SubmitReportAsync_WhenChatSceneWithoutChannel_ShouldDenyAsInvalid()
        {
            // Arrange
            var service = CreateService(out _);

            // Act
            var outcome = await service.SubmitReportAsync(CreatePlayerScope(PlayerOne), new OnlineReportSubmission { ReportedPlayerId = PlayerTwo, Scene = OnlineReportScene.Chat, Reason = OnlineReportReason.Spam });

            // Assert
            Assert.False(outcome.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, outcome.Code);
        }

        /// <summary>
        /// 验证举报原因为「其他」时必须填写补充说明（没有说明的「其他」不可审理）。
        /// </summary>
        [Fact]
        public async Task SubmitReportAsync_WhenReasonIsOtherWithoutEvidence_ShouldDenyAsInvalid()
        {
            // Arrange
            var service = CreateService(out _);

            // Act
            var outcome = await service.SubmitReportAsync(CreatePlayerScope(PlayerOne), new OnlineReportSubmission { ReportedPlayerId = PlayerTwo, Scene = OnlineReportScene.Profile, Reason = OnlineReportReason.Other });

            // Assert
            Assert.False(outcome.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, outcome.Code);
        }

        /// <summary>
        /// 验证举报自己是无效举报、被举报人标识非法同样被拒。
        /// </summary>
        [Fact]
        public async Task SubmitReportAsync_WhenTargetIsSelfOrInvalid_ShouldDenyAsInvalid()
        {
            // Arrange
            var service = CreateService(out _);

            // Act
            var selfReport = await service.SubmitReportAsync(CreatePlayerScope(PlayerOne), new OnlineReportSubmission { ReportedPlayerId = PlayerOne, Scene = OnlineReportScene.Profile, Reason = OnlineReportReason.Spam });
            var invalidTarget = await service.SubmitReportAsync(CreatePlayerScope(PlayerOne), new OnlineReportSubmission { ReportedPlayerId = 0, Scene = OnlineReportScene.Profile, Reason = OnlineReportReason.Spam });

            // Assert
            Assert.False(selfReport.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, selfReport.Code);
            Assert.False(invalidTarget.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, invalidTarget.Code);
        }

        /// <summary>
        /// 验证举报落库时证据链完整（举报人、被举报人、场景、关联标识、状态与时间戳齐备）并发出来变更事件。
        /// </summary>
        [Fact]
        public async Task SubmitReportAsync_ShouldRecordEvidenceChainAndPublishEvent()
        {
            // Arrange
            var service = CreateService(out var recorder);

            // Act
            var outcome = await service.SubmitReportAsync(
                CreatePlayerScope(PlayerOne),
                new OnlineReportSubmission { ReportedPlayerId = PlayerTwo, Scene = OnlineReportScene.Chat, Reason = OnlineReportReason.Harassment, MatchId = "match-1", ChatMessageId = "msg-1", ChannelId = "chan-1", Evidence = "连续辱骂" });

            // Assert
            Assert.True(outcome.IsSuccess);
            var reportCase = outcome.Data;
            Assert.Equal(PlayerOne, reportCase.ReporterId);
            Assert.Equal(PlayerTwo, reportCase.ReportedPlayerId);
            Assert.Equal(OnlineReportScene.Chat, reportCase.Scene);
            Assert.Equal(OnlineReportReason.Harassment, reportCase.Reason);
            Assert.Equal("match-1", reportCase.MatchId);
            Assert.Equal("msg-1", reportCase.ChatMessageId);
            Assert.Equal("chan-1", reportCase.ChannelId);
            Assert.Equal("连续辱骂", reportCase.Evidence);
            Assert.Equal(OnlineReportState.Submitted, reportCase.State);
            Assert.Equal(OnlineReportResolution.None, reportCase.Resolution);
            Assert.True(reportCase.CreatedAtTime > 0);
            Assert.Single(recorder.Filter(OnlineSocialEvents.ReportChanged));
        }

        /// <summary>
        /// 验证举报人可以撤回自己的举报，且终态写入结案时刻。
        /// </summary>
        [Fact]
        public async Task WithdrawReportAsync_WhenReporter_ShouldReachWithdrawnWithClosedTime()
        {
            // Arrange
            var service = CreateService(out _);
            var submitted = await service.SubmitReportAsync(CreatePlayerScope(PlayerOne), new OnlineReportSubmission { ReportedPlayerId = PlayerTwo, Scene = OnlineReportScene.Profile, Reason = OnlineReportReason.Spam });

            // Act
            var outcome = await service.WithdrawReportAsync(CreatePlayerScope(PlayerOne), submitted.Data.ReportId);

            // Assert
            Assert.True(outcome.IsSuccess);
            Assert.Equal(OnlineReportState.Withdrawn, outcome.Data.State);
            Assert.True(outcome.Data.ClosedAtTime > 0);
        }

        /// <summary>
        /// 验证非举报人撤回别人的举报返回未找到（反预言：不泄露他人案件的存在性）。
        /// </summary>
        [Fact]
        public async Task WithdrawReportAsync_WhenNotReporter_ShouldReturnResourceNotFound()
        {
            // Arrange
            var service = CreateService(out _);
            var submitted = await service.SubmitReportAsync(CreatePlayerScope(PlayerOne), new OnlineReportSubmission { ReportedPlayerId = PlayerTwo, Scene = OnlineReportScene.Profile, Reason = OnlineReportReason.Spam });

            // Act
            var byTarget = await service.WithdrawReportAsync(CreatePlayerScope(PlayerTwo), submitted.Data.ReportId);
            var byBystander = await service.WithdrawReportAsync(CreatePlayerScope(PlayerThree), submitted.Data.ReportId);

            // Assert
            Assert.False(byTarget.IsSuccess);
            Assert.Equal(OnlineErrorCode.ResourceNotFound, byTarget.Code);
            Assert.False(byBystander.IsSuccess);
            Assert.Equal(OnlineErrorCode.ResourceNotFound, byBystander.Code);
        }

        /// <summary>
        /// 验证状态机表外迁移被拒（Submitted 不能直接跳到 Actioned，必须先受理）。
        /// </summary>
        [Fact]
        public async Task TransitionReportAsync_WhenIllegalEdge_ShouldReturnStateOperationForbidden()
        {
            // Arrange
            var service = CreateService(out _);
            var submitted = await service.SubmitReportAsync(CreatePlayerScope(PlayerOne), new OnlineReportSubmission { ReportedPlayerId = PlayerTwo, Scene = OnlineReportScene.Profile, Reason = OnlineReportReason.Spam });

            // Act
            var outcome = await service.TransitionReportAsync(new OnlineReportTransition { TenantId = TenantId, AppId = AppId, ReportId = submitted.Data.ReportId, TargetState = OnlineReportState.Actioned, Resolution = OnlineReportResolution.Banned, HandlerAdminId = AdminId });

            // Assert
            Assert.False(outcome.IsSuccess);
            Assert.Equal(OnlineErrorCode.StateOperationForbidden, outcome.Code);
        }

        /// <summary>
        /// 验证「已处置」必须给出处置结果（没有结果的处置是空动作，下游无从执行）。
        /// </summary>
        [Fact]
        public async Task TransitionReportAsync_WhenActionedWithoutResolution_ShouldDenyAsInvalid()
        {
            // Arrange
            var service = CreateService(out _);
            var submitted = await service.SubmitReportAsync(CreatePlayerScope(PlayerOne), new OnlineReportSubmission { ReportedPlayerId = PlayerTwo, Scene = OnlineReportScene.Profile, Reason = OnlineReportReason.Spam });
            Assert.True((await service.TransitionReportAsync(new OnlineReportTransition { TenantId = TenantId, AppId = AppId, ReportId = submitted.Data.ReportId, TargetState = OnlineReportState.Reviewing, Resolution = OnlineReportResolution.None, HandlerAdminId = AdminId })).IsSuccess);

            // Act
            var outcome = await service.TransitionReportAsync(new OnlineReportTransition { TenantId = TenantId, AppId = AppId, ReportId = submitted.Data.ReportId, TargetState = OnlineReportState.Actioned, Resolution = OnlineReportResolution.None, HandlerAdminId = AdminId });

            // Assert
            Assert.False(outcome.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, outcome.Code);
        }

        /// <summary>
        /// 验证驳回案件时处置结果必须是「无违规」（驳回却带处罚结果自相矛盾）。
        /// </summary>
        [Fact]
        public async Task TransitionReportAsync_WhenRejected_ShouldRequireNoViolation()
        {
            // Arrange
            var service = CreateService(out _);
            var submitted = await service.SubmitReportAsync(CreatePlayerScope(PlayerOne), new OnlineReportSubmission { ReportedPlayerId = PlayerTwo, Scene = OnlineReportScene.Profile, Reason = OnlineReportReason.Spam });

            // Act
            var contradictory = await service.TransitionReportAsync(new OnlineReportTransition { TenantId = TenantId, AppId = AppId, ReportId = submitted.Data.ReportId, TargetState = OnlineReportState.Rejected, Resolution = OnlineReportResolution.Banned, HandlerAdminId = AdminId });
            var consistent = await service.TransitionReportAsync(new OnlineReportTransition { TenantId = TenantId, AppId = AppId, ReportId = submitted.Data.ReportId, TargetState = OnlineReportState.Rejected, Resolution = OnlineReportResolution.NoViolation, HandlerAdminId = AdminId, AdminCaseId = "ADM-1" });

            // Assert
            Assert.False(contradictory.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, contradictory.Code);
            Assert.True(consistent.IsSuccess);
            Assert.Equal(OnlineReportState.Rejected, consistent.Data.State);
            Assert.Equal(OnlineReportResolution.NoViolation, consistent.Data.Resolution);
            Assert.Equal(AdminId, consistent.Data.HandlerAdminId);
            Assert.Equal("ADM-1", consistent.Data.AdminCaseId);
            Assert.True(consistent.Data.ClosedAtTime > 0);
        }

        /// <summary>
        /// 验证重复裁决同一目标状态是幂等的（返回既有状态而非报错）。
        /// </summary>
        [Fact]
        public async Task TransitionReportAsync_WhenSameStateRepeated_ShouldBeIdempotent()
        {
            // Arrange
            var service = CreateService(out _);
            var submitted = await service.SubmitReportAsync(CreatePlayerScope(PlayerOne), new OnlineReportSubmission { ReportedPlayerId = PlayerTwo, Scene = OnlineReportScene.Profile, Reason = OnlineReportReason.Spam });
            var first = await service.TransitionReportAsync(new OnlineReportTransition { TenantId = TenantId, AppId = AppId, ReportId = submitted.Data.ReportId, TargetState = OnlineReportState.Reviewing, Resolution = OnlineReportResolution.None, HandlerAdminId = AdminId });
            Assert.True(first.IsSuccess);

            // Act
            var second = await service.TransitionReportAsync(new OnlineReportTransition { TenantId = TenantId, AppId = AppId, ReportId = submitted.Data.ReportId, TargetState = OnlineReportState.Reviewing, Resolution = OnlineReportResolution.None, HandlerAdminId = AdminId });

            // Assert
            Assert.True(second.IsSuccess);
            Assert.Equal(OnlineReportState.Reviewing, second.Data.State);
        }

        /// <summary>
        /// 验证「我的举报」只返回自己提交的案件（举报人之间互相不可见，VC-6.7 隐私隔离）。
        /// </summary>
        [Fact]
        public async Task ListMyReportsAsync_ShouldOnlyExposeOwnReports()
        {
            // Arrange
            var service = CreateService(out _);
            var mine = await service.SubmitReportAsync(CreatePlayerScope(PlayerOne), new OnlineReportSubmission { ReportedPlayerId = PlayerTwo, Scene = OnlineReportScene.Profile, Reason = OnlineReportReason.Spam });
            var others = await service.SubmitReportAsync(CreatePlayerScope(PlayerTwo), new OnlineReportSubmission { ReportedPlayerId = PlayerThree, Scene = OnlineReportScene.Profile, Reason = OnlineReportReason.Spam });
            Assert.True(mine.IsSuccess);
            Assert.True(others.IsSuccess);

            // Act
            var listed = await service.ListMyReportsAsync(CreatePlayerScope(PlayerOne));

            // Assert
            Assert.True(listed.IsSuccess);
            Assert.Single(listed.Data);
            Assert.Equal(mine.Data.ReportId, listed.Data[0].ReportId);
        }

        /// <summary>
        /// 验证举报状态机的合法边与终态是冻结契约（表外迁移一律非法）。
        /// </summary>
        [Fact]
        public void ReportStateMachine_ShouldFreezeTerminalStatesAndLegalEdges()
        {
            // 合法边
            Assert.True(OnlineReportStateMachine.TryTransition(OnlineReportState.Submitted, OnlineReportState.Reviewing));
            Assert.True(OnlineReportStateMachine.TryTransition(OnlineReportState.Submitted, OnlineReportState.Rejected));
            Assert.True(OnlineReportStateMachine.TryTransition(OnlineReportState.Submitted, OnlineReportState.Withdrawn));
            Assert.True(OnlineReportStateMachine.TryTransition(OnlineReportState.Reviewing, OnlineReportState.Actioned));
            Assert.True(OnlineReportStateMachine.TryTransition(OnlineReportState.Reviewing, OnlineReportState.Rejected));

            // 表外边
            Assert.False(OnlineReportStateMachine.TryTransition(OnlineReportState.Submitted, OnlineReportState.Actioned));
            Assert.False(OnlineReportStateMachine.TryTransition(OnlineReportState.Withdrawn, OnlineReportState.Reviewing));
            Assert.False(OnlineReportStateMachine.TryTransition(OnlineReportState.Actioned, OnlineReportState.Rejected));
            Assert.False(OnlineReportStateMachine.TryTransition(OnlineReportState.Rejected, OnlineReportState.Reviewing));

            // 终态
            Assert.True(OnlineReportStateMachine.IsTerminal(OnlineReportState.Actioned));
            Assert.True(OnlineReportStateMachine.IsTerminal(OnlineReportState.Rejected));
            Assert.True(OnlineReportStateMachine.IsTerminal(OnlineReportState.Withdrawn));
            Assert.False(OnlineReportStateMachine.IsTerminal(OnlineReportState.Submitted));
            Assert.False(OnlineReportStateMachine.IsTerminal(OnlineReportState.Reviewing));
        }

        /// <summary>
        /// 创建被测裁决服务。
        /// </summary>
        /// <param name="recorder">事件记录桩。</param>
        /// <returns>裁决服务实例。</returns>
        private static OnlineSocialDecisionService CreateService(out OnlineEventRecorder recorder)
        {
            return CreateService(out recorder, out _);
        }

        /// <summary>
        /// 创建被测裁决服务，并暴露其底层的社交图谱存储（供施加处罚用）。
        /// </summary>
        /// <param name="recorder">事件记录桩。</param>
        /// <param name="graphStore">社交图谱存储（与裁决服务共享同一实例）。</param>
        /// <returns>裁决服务实例。</returns>
        private static OnlineSocialDecisionService CreateService(out OnlineEventRecorder recorder, out InMemoryOnlineSocialGraphStore graphStore)
        {
            recorder = new OnlineEventRecorder();
            graphStore = new InMemoryOnlineSocialGraphStore();
            return new OnlineSocialDecisionService(graphStore, new InMemoryOnlineReportStore(), recorder);
        }

        /// <summary>
        /// 经处罚服务对玩家施加一条永久处罚（与裁决服务共享同一图谱存储）。
        /// </summary>
        /// <param name="graphStore">社交图谱存储。</param>
        /// <param name="playerId">被处罚玩家。</param>
        /// <param name="kind">处罚种类。</param>
        /// <returns>已落库的处罚。</returns>
        private static async Task<OnlinePunishment> ApplyPunishmentAsync(InMemoryOnlineSocialGraphStore graphStore, long playerId, OnlinePunishmentKind kind)
        {
            var punishmentService = new OnlinePunishmentService(graphStore, new OnlineEventRecorder());
            var outcome = await punishmentService.ApplyAsync(new OnlinePunishmentRequest { TenantId = TenantId, AppId = AppId, PlayerId = playerId, Kind = kind, Reason = "测试处罚", EffectiveAtTime = 0, ExpiresAtTime = 0, AdminId = AdminId });
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
    }
}
