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

using System.Collections.Generic;
using System.Threading.Tasks;
using GameFrameX.Online.Contracts;
using GameFrameX.Online.Matchmaking;
using GameFrameX.Online.Party;
using GameFrameX.Online.Scope;
using GameFrameX.Online.Social;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 跨域社交裁决集成测试（vault:C7 关键约束「Block 必须在 Chat / Party / Matchmaker 三处统一生效」）。
    /// <para>
    /// 本类锁定的不是任何一个服务内部的规则，而是 **C97 既有链路接入 C99 裁决入口后的行为边界**：
    /// 未装配裁决时既有行为一字不变（C97 既有用例不受影响），装配后屏蔽与封禁在组队与匹配两条路径上
    /// 同源生效，且不拆散票据、不误伤禁言。
    /// </para>
    /// </summary>
    public class OnlineSocialGateIntegrationTests
    {
        /// <summary>测试用租户标识。</summary>
        private const long TenantId = 1;

        /// <summary>测试用应用标识。</summary>
        private const long AppId = 10;

        /// <summary>测试用区服标识。</summary>
        private const long ServerId = 100;

        /// <summary>测试用玩家一标识。</summary>
        private const long PlayerOne = 1001;

        /// <summary>测试用玩家二标识。</summary>
        private const long PlayerTwo = 1002;

        /// <summary>测试用玩家三标识。</summary>
        private const long PlayerThree = 1003;

        /// <summary>测试用玩家四标识。</summary>
        private const long PlayerFour = 1004;

        /// <summary>测试用玩家五标识。</summary>
        private const long PlayerFive = 1005;

        /// <summary>测试用 Admin 标识。</summary>
        private const long AdminId = 9001;

        /// <summary>
        /// 验证队伍服务未装配社交裁决时邀请照常成立（null = 不启用，C97 既有行为不变）。
        /// </summary>
        [Fact]
        public async Task InviteAsync_WhenSocialGateMissing_ShouldKeepLegacyBehavior()
        {
            // Arrange
            var fixture = CreatePartyFixture(withGate: false);
            var party = await CreatePartyAsync(fixture.Party, PlayerOne);

            // Act
            var invite = await fixture.Party.InviteAsync(CreatePlayerScope(PlayerOne), party.PartyId, PlayerTwo);

            // Assert
            Assert.True(invite.IsSuccess);
            var pending = await fixture.Party.ListPendingInvitesAsync(CreatePlayerScope(PlayerTwo));
            Assert.Single(pending.Data);
        }

        /// <summary>
        /// 验证屏蔽后邀请被拒，且**不留下待答复邀请**（拒绝不能只做表面功夫，通道必须一起关掉）。
        /// </summary>
        [Fact]
        public async Task InviteAsync_WhenBlocked_ShouldDenyAndLeaveNoPendingInvite()
        {
            // Arrange
            var fixture = CreatePartyFixture(withGate: true);
            var party = await CreatePartyAsync(fixture.Party, PlayerOne);
            Assert.True((await fixture.Decision.BlockAsync(CreatePlayerScope(PlayerOne), PlayerTwo)).IsSuccess);

            // Act
            var invite = await fixture.Party.InviteAsync(CreatePlayerScope(PlayerOne), party.PartyId, PlayerTwo);

            // Assert
            Assert.False(invite.IsSuccess);
            Assert.Equal(OnlineErrorCode.RiskControlRejected, invite.Code);

            var pending = await fixture.Party.ListPendingInvitesAsync(CreatePlayerScope(PlayerTwo));
            Assert.Empty(pending.Data);
        }

        /// <summary>
        /// 验证屏蔽对邀请的约束与谁先屏蔽无关（被邀请方屏蔽邀请方同样拒绝）。
        /// </summary>
        [Fact]
        public async Task InviteAsync_WhenInviteeBlockedInviter_ShouldAlsoDeny()
        {
            // Arrange
            var fixture = CreatePartyFixture(withGate: true);
            var party = await CreatePartyAsync(fixture.Party, PlayerOne);
            Assert.True((await fixture.Decision.BlockAsync(CreatePlayerScope(PlayerTwo), PlayerOne)).IsSuccess);

            // Act
            var invite = await fixture.Party.InviteAsync(CreatePlayerScope(PlayerOne), party.PartyId, PlayerTwo);

            // Assert
            Assert.False(invite.IsSuccess);
            Assert.Equal(OnlineErrorCode.RiskControlRejected, invite.Code);
        }

        /// <summary>
        /// 验证裁决发生在待答复去重**之前**：已存在的待答复邀请不能变成屏蔽的绕行道。
        /// <para>
        /// 若顺序反了，第二次邀请会命中去重分支原样返回旧邀请，屏蔽形同虚设。
        /// </para>
        /// </summary>
        [Fact]
        public async Task InviteAsync_WhenBlockedAfterInviteIssued_ShouldDenyRatherThanReturnExistingInvite()
        {
            // Arrange
            var fixture = CreatePartyFixture(withGate: true);
            var party = await CreatePartyAsync(fixture.Party, PlayerOne);
            var first = await fixture.Party.InviteAsync(CreatePlayerScope(PlayerOne), party.PartyId, PlayerTwo);
            Assert.True(first.IsSuccess);

            // 邀请已存在之后才建立屏蔽。
            Assert.True((await fixture.Decision.BlockAsync(CreatePlayerScope(PlayerOne), PlayerTwo)).IsSuccess);

            // Act
            var second = await fixture.Party.InviteAsync(CreatePlayerScope(PlayerOne), party.PartyId, PlayerTwo);

            // Assert
            Assert.False(second.IsSuccess);
            Assert.Equal(OnlineErrorCode.RiskControlRejected, second.Code);
        }

        /// <summary>
        /// 验证邀请**等待期内**建立的屏蔽会拦住答复面：接受被拒，玩家没有进队。
        /// <para>
        /// 邀请存活期默认 120 秒，只在发起面判定的话，这 120 秒就是屏蔽的盲区。
        /// </para>
        /// </summary>
        [Fact]
        public async Task AnswerInviteAsync_WhenBlockedWhilePending_ShouldDenyAcceptAndNotAddMember()
        {
            // Arrange
            var fixture = CreatePartyFixture(withGate: true);
            var party = await CreatePartyAsync(fixture.Party, PlayerOne);
            var invite = await fixture.Party.InviteAsync(CreatePlayerScope(PlayerOne), party.PartyId, PlayerTwo);
            Assert.True(invite.IsSuccess);
            Assert.True((await fixture.Decision.BlockAsync(CreatePlayerScope(PlayerTwo), PlayerOne)).IsSuccess);

            // Act
            var accepted = await fixture.Party.AnswerInviteAsync(CreatePlayerScope(PlayerTwo), invite.Data.InviteId, true);

            // Assert
            Assert.False(accepted.IsSuccess);
            Assert.Equal(OnlineErrorCode.RiskControlRejected, accepted.Code);

            var current = await fixture.Party.GetAsync(CreatePlayerScope(PlayerOne), party.PartyId);
            Assert.Single(current.Data.Members);
            Assert.Equal(PlayerOne, current.Data.Members[0].PlayerId);
        }

        /// <summary>
        /// 验证屏蔽建立后**拒绝**邀请依然放行（拒绝是脱离互动，拦住它会把玩家困在待答复状态）。
        /// </summary>
        [Fact]
        public async Task AnswerInviteAsync_WhenBlockedWhilePending_ShouldStillAllowReject()
        {
            // Arrange
            var fixture = CreatePartyFixture(withGate: true);
            var party = await CreatePartyAsync(fixture.Party, PlayerOne);
            var invite = await fixture.Party.InviteAsync(CreatePlayerScope(PlayerOne), party.PartyId, PlayerTwo);
            Assert.True(invite.IsSuccess);
            Assert.True((await fixture.Decision.BlockAsync(CreatePlayerScope(PlayerOne), PlayerTwo)).IsSuccess);

            // Act
            var rejected = await fixture.Party.AnswerInviteAsync(CreatePlayerScope(PlayerTwo), invite.Data.InviteId, false);

            // Assert
            Assert.True(rejected.IsSuccess);
        }

        /// <summary>
        /// 验证禁言**不**影响组队（禁言只管私聊展示与发言，不该波及邀请）。
        /// </summary>
        [Fact]
        public async Task InviteAsync_WhenInviterMuted_ShouldStillAllow()
        {
            // Arrange
            var fixture = CreatePartyFixture(withGate: true);
            var party = await CreatePartyAsync(fixture.Party, PlayerOne);
            Assert.True((await fixture.Punishments.ApplyAsync(TenantId, AppId, PlayerOne, OnlinePunishmentKind.Mute, "刷屏", 0, 0, AdminId)).IsSuccess);

            // Act
            var invite = await fixture.Party.InviteAsync(CreatePlayerScope(PlayerOne), party.PartyId, PlayerTwo);

            // Assert
            Assert.True(invite.IsSuccess);
        }

        /// <summary>
        /// 验证封禁玩家的邀请被拒并透传封禁码（不暴露屏蔽与封禁的差异之外的信息）。
        /// </summary>
        [Fact]
        public async Task InviteAsync_WhenInviterBanned_ShouldDenyWithAccountBanned()
        {
            // Arrange
            var fixture = CreatePartyFixture(withGate: true);
            var party = await CreatePartyAsync(fixture.Party, PlayerOne);
            Assert.True((await fixture.Punishments.ApplyAsync(TenantId, AppId, PlayerOne, OnlinePunishmentKind.Ban, "使用外挂", 0, 0, AdminId)).IsSuccess);

            // Act
            var invite = await fixture.Party.InviteAsync(CreatePlayerScope(PlayerOne), party.PartyId, PlayerTwo);

            // Assert
            Assert.False(invite.IsSuccess);
            Assert.Equal(OnlineErrorCode.AccountBanned, invite.Code);
        }

        /// <summary>
        /// 验证匹配协调器未装配社交裁决时行为不变（C97 既有用例的等价快照）。
        /// </summary>
        [Fact]
        public async Task RunOnceAsync_WhenSocialGateMissing_ShouldKeepLegacyBehavior()
        {
            // Arrange
            var fixture = CreateMatchFixture(withGate: false);
            await EnqueueAsync(fixture, PlayerOne, 2, new List<long> { PlayerOne });
            await EnqueueAsync(fixture, PlayerTwo, 2, new List<long> { PlayerTwo });

            // Act
            var outcome = await fixture.Coordinator.RunOnceAsync(TenantId, AppId);

            // Assert
            Assert.True(outcome.IsSuccess);
            Assert.Single(outcome.Data.AssignmentIds);
            Assert.Equal(2, outcome.Data.MatchedPlayerCount);
        }

        /// <summary>
        /// 验证装配裁决但不涉及屏蔽 / 处罚时匹配照常成组（裁决不是「一律拒绝」）。
        /// </summary>
        [Fact]
        public async Task RunOnceAsync_WithGateAndNoBlock_ShouldStillMatch()
        {
            // Arrange
            var fixture = CreateMatchFixture(withGate: true);
            await EnqueueAsync(fixture, PlayerOne, 2, new List<long> { PlayerOne });
            await EnqueueAsync(fixture, PlayerTwo, 2, new List<long> { PlayerTwo });

            // Act
            var outcome = await fixture.Coordinator.RunOnceAsync(TenantId, AppId);

            // Assert
            Assert.True(outcome.IsSuccess);
            Assert.Single(outcome.Data.AssignmentIds);
            Assert.Equal(2, outcome.Data.MatchedPlayerCount);
        }

        /// <summary>
        /// 验证被屏蔽的候选票据被**整票跳过**而不是拆开：
        /// 基准票据改与下一张票据成组，被屏蔽的票据原地留在队列中且成员一个不少。
        /// <para>
        /// 这是「整票进整票出」红线在社交裁决下的延续——拆票会把同一队伍的成员分进不同对局。
        /// </para>
        /// </summary>
        [Fact]
        public async Task RunOnceAsync_WhenCandidateBlocked_ShouldSkipWholeTicketAndMatchOthers()
        {
            // Arrange
            var fixture = CreateMatchFixture(withGate: true);
            var anchorTicket = await EnqueueAsync(fixture, PlayerOne, 3, new List<long> { PlayerOne });
            await NextArrivalTickAsync();
            var blockedTicket = await EnqueueAsync(fixture, PlayerTwo, 3, new List<long> { PlayerTwo, PlayerThree });
            await NextArrivalTickAsync();
            var healthyTicket = await EnqueueAsync(fixture, PlayerFour, 3, new List<long> { PlayerFour, PlayerFive });
            Assert.True((await fixture.Decision.BlockAsync(CreatePlayerScope(PlayerOne), PlayerTwo)).IsSuccess);

            // Act
            var outcome = await fixture.Coordinator.RunOnceAsync(TenantId, AppId);

            // Assert
            Assert.True(outcome.IsSuccess);
            Assert.Single(outcome.Data.AssignmentIds);

            var assignment = await fixture.Store.FindAssignmentAsync(TenantId, AppId, outcome.Data.AssignmentIds[0]);
            Assert.Equal(new List<long> { PlayerOne, PlayerFour, PlayerFive }, assignment.PlayerIds);
            Assert.Equal(new List<string> { anchorTicket.TicketId, healthyTicket.TicketId }, assignment.TicketIds);
            Assert.DoesNotContain(blockedTicket.TicketId, assignment.TicketIds);

            // 被跳过的票据整票留在队列里（既没被消费，也没被拆散）。
            var stillQueued = await fixture.Store.FindAsync(TenantId, AppId, blockedTicket.TicketId);
            Assert.NotNull(stillQueued);
            Assert.Equal(OnlineMatchTicketState.Queued, stillQueued.State);
            Assert.Equal(new List<long> { PlayerTwo, PlayerThree }, stillQueued.PlayerIds);
        }

        /// <summary>
        /// 验证**候选票据里**的封禁玩家同样让该票据被跳过。
        /// <para>
        /// 处罚是绑定在发起方身上的单向事实，而本处没有「谁是发起方」的概念（双方都是被动入组），
        /// 所以每一对玩家必须双向各判一次，否则「被处罚者恰好在候选票据里」就会逃过裁决。
        /// </para>
        /// </summary>
        [Fact]
        public async Task RunOnceAsync_WhenCandidateTicketHoldsBannedPlayer_ShouldSkipTicket()
        {
            // Arrange
            var fixture = CreateMatchFixture(withGate: true);
            await EnqueueAsync(fixture, PlayerOne, 3, new List<long> { PlayerOne });
            await NextArrivalTickAsync();
            var bannedTicket = await EnqueueAsync(fixture, PlayerTwo, 3, new List<long> { PlayerTwo, PlayerThree });
            await NextArrivalTickAsync();
            await EnqueueAsync(fixture, PlayerFour, 3, new List<long> { PlayerFour, PlayerFive });
            Assert.True((await fixture.Punishments.ApplyAsync(TenantId, AppId, PlayerTwo, OnlinePunishmentKind.Ban, "使用外挂", 0, 0, AdminId)).IsSuccess);

            // Act
            var outcome = await fixture.Coordinator.RunOnceAsync(TenantId, AppId);

            // Assert
            Assert.True(outcome.IsSuccess);
            Assert.Single(outcome.Data.AssignmentIds);

            var assignment = await fixture.Store.FindAssignmentAsync(TenantId, AppId, outcome.Data.AssignmentIds[0]);
            Assert.Equal(new List<long> { PlayerOne, PlayerFour, PlayerFive }, assignment.PlayerIds);
            Assert.DoesNotContain(bannedTicket.TicketId, assignment.TicketIds);

            var stillQueued = await fixture.Store.FindAsync(TenantId, AppId, bannedTicket.TicketId);
            Assert.Equal(OnlineMatchTicketState.Queued, stillQueued.State);
        }

        /// <summary>
        /// 验证封禁让**基准票据**无法成组（封禁在匹配链路同样生效，不只是组队链路）。
        /// </summary>
        [Fact]
        public async Task RunOnceAsync_WhenAnchorBanned_ShouldNotProduceAssignment()
        {
            // Arrange
            var fixture = CreateMatchFixture(withGate: true);
            var bannedTicket = await EnqueueAsync(fixture, PlayerOne, 2, new List<long> { PlayerOne });
            await NextArrivalTickAsync();
            await EnqueueAsync(fixture, PlayerTwo, 2, new List<long> { PlayerTwo });
            Assert.True((await fixture.Punishments.ApplyAsync(TenantId, AppId, PlayerOne, OnlinePunishmentKind.Ban, "使用外挂", 0, 0, AdminId)).IsSuccess);

            // Act
            var outcome = await fixture.Coordinator.RunOnceAsync(TenantId, AppId);

            // Assert
            Assert.True(outcome.IsSuccess);
            Assert.Empty(outcome.Data.AssignmentIds);

            var stillQueued = await fixture.Store.FindAsync(TenantId, AppId, bannedTicket.TicketId);
            Assert.Equal(OnlineMatchTicketState.Queued, stillQueued.State);
        }

        /// <summary>
        /// 创建队伍链路测试夹具。
        /// </summary>
        /// <param name="withGate">是否装配社交裁决入口。</param>
        /// <returns>队伍链路夹具。</returns>
        private static PartyFixture CreatePartyFixture(bool withGate)
        {
            var recorder = new OnlineEventRecorder();
            var graph = new InMemoryOnlineSocialGraphStore();
            var decision = new OnlineSocialDecisionService(graph, new InMemoryOnlineReportStore(), recorder);
            var punishments = new OnlinePunishmentService(graph, recorder);
            var party = new OnlinePartyService(new InMemoryOnlinePartyStore(), recorder, 2, 8, 120, 1800, null, withGate ? decision : null);
            return new PartyFixture(party, decision, punishments);
        }

        /// <summary>
        /// 创建匹配链路测试夹具。
        /// </summary>
        /// <param name="withGate">是否装配社交裁决入口。</param>
        /// <returns>匹配链路夹具。</returns>
        private static MatchFixture CreateMatchFixture(bool withGate)
        {
            var store = new InMemoryOnlineMatchTicketStore();
            var recorder = new OnlineEventRecorder();
            var graph = new InMemoryOnlineSocialGraphStore();
            var decision = new OnlineSocialDecisionService(graph, new InMemoryOnlineReportStore(), recorder);
            var punishments = new OnlinePunishmentService(graph, recorder);
            var tickets = new OnlineMatchTicketService(store, recorder);
            var coordinator = new OnlineMatchmakerCoordinator(store, tickets, recorder, null, withGate ? decision : null);
            return new MatchFixture(store, tickets, coordinator, decision, punishments);
        }

        /// <summary>
        /// 创建一支单人队伍。
        /// </summary>
        /// <param name="service">队伍服务。</param>
        /// <param name="leaderId">队长标识。</param>
        /// <returns>队伍。</returns>
        private static async Task<OnlineParty> CreatePartyAsync(OnlinePartyService service, long leaderId)
        {
            var outcome = await service.CreateAsync(CreatePlayerScope(leaderId));
            Assert.True(outcome.IsSuccess);
            return outcome.Data;
        }

        /// <summary>
        /// 让下一张入队票据的到达时刻严格大于前一张。
        /// <para>
        /// 协调器按「先到先服务」选基准票据（<c>CreatedAtTime</c> 升序，同毫秒时退化为票据号字典序，
        /// 而票据号是随机的）。不推开到达时刻，基准票据就取决于随机数，用例会时对时错，
        /// 或者更糟——「看起来在测候选票据」实际测的是另一条路径。
        /// </para>
        /// </summary>
        /// <returns>延迟任务。</returns>
        private static Task NextArrivalTickAsync()
        {
            return Task.Delay(5);
        }

        /// <summary>
        /// 入队一张票据（成员即票据携带的整支队伍）。
        /// </summary>
        /// <param name="fixture">匹配夹具。</param>
        /// <param name="leaderId">发起玩家标识（作用域主体位）。</param>
        /// <param name="teamSize">目标对局规模。</param>
        /// <param name="members">票据携带成员。</param>
        /// <returns>入队后的票据。</returns>
        private static async Task<OnlineMatchTicket> EnqueueAsync(MatchFixture fixture, long leaderId, int teamSize, List<long> members)
        {
            var request = new OnlineMatchTicketEnqueueRequest
            {
                PartyId = string.Empty,
                PlayerIds = members,
                Mode = 1,
                Region = 1,
                SkillRange = new OnlineMatchSkillRange { Min = 100, Max = 200 },
                TeamSize = teamSize,
                LatencyRequirement = 0,
                CustomProperties = new Dictionary<string, string>(),
            };

            var outcome = await fixture.Tickets.EnqueueAsync(CreatePlayerScope(leaderId), request);
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
        /// 队伍链路测试夹具。
        /// </summary>
        private sealed class PartyFixture
        {
            /// <summary>
            /// 初始化 <see cref="PartyFixture"/>。
            /// </summary>
            /// <param name="party">队伍服务。</param>
            /// <param name="decision">社交裁决服务。</param>
            /// <param name="punishments">处罚服务。</param>
            public PartyFixture(OnlinePartyService party, OnlineSocialDecisionService decision, OnlinePunishmentService punishments)
            {
                Party = party;
                Decision = decision;
                Punishments = punishments;
            }

            /// <summary>获取队伍服务。</summary>
            public OnlinePartyService Party { get; }

            /// <summary>获取社交裁决服务。</summary>
            public OnlineSocialDecisionService Decision { get; }

            /// <summary>获取处罚服务。</summary>
            public OnlinePunishmentService Punishments { get; }
        }

        /// <summary>
        /// 匹配链路测试夹具。
        /// </summary>
        private sealed class MatchFixture
        {
            /// <summary>
            /// 初始化 <see cref="MatchFixture"/>。
            /// </summary>
            /// <param name="store">票据存储。</param>
            /// <param name="tickets">票据服务。</param>
            /// <param name="coordinator">匹配协调器。</param>
            /// <param name="decision">社交裁决服务。</param>
            /// <param name="punishments">处罚服务。</param>
            public MatchFixture(InMemoryOnlineMatchTicketStore store, OnlineMatchTicketService tickets, OnlineMatchmakerCoordinator coordinator, OnlineSocialDecisionService decision, OnlinePunishmentService punishments)
            {
                Store = store;
                Tickets = tickets;
                Coordinator = coordinator;
                Decision = decision;
                Punishments = punishments;
            }

            /// <summary>获取票据存储。</summary>
            public InMemoryOnlineMatchTicketStore Store { get; }

            /// <summary>获取票据服务。</summary>
            public OnlineMatchTicketService Tickets { get; }

            /// <summary>获取匹配协调器。</summary>
            public OnlineMatchmakerCoordinator Coordinator { get; }

            /// <summary>获取社交裁决服务。</summary>
            public OnlineSocialDecisionService Decision { get; }

            /// <summary>获取处罚服务。</summary>
            public OnlinePunishmentService Punishments { get; }
        }
    }
}
