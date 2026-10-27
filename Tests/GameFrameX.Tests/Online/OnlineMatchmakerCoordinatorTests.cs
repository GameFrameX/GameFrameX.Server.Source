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
using GameFrameX.Capability.Online.Abstractions.Events;
using GameFrameX.Capability.Online.Matchmaking;
using GameFrameX.Capability.Online.Abstractions.Scope;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 匹配协调器测试（成组、整队不拆散、等待扩展、竞态与唯一性、assignment 快照）。
    /// </summary>
    public class OnlineMatchmakerCoordinatorTests
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

        /// <summary>
        /// 验证两张兼容票据成组并产出唯一分配，票据同时转 Matched。
        /// </summary>
        [Fact]
        public async Task RunOnceAsync_WithCompatibleTickets_ShouldProduceSingleAssignment()
        {
            // Arrange
            var fixture = CreateFixture();
            await EnqueueAsync(fixture, PlayerOne);
            await EnqueueAsync(fixture, PlayerTwo);

            // Act
            var outcome = await fixture.Coordinator.RunOnceAsync(TenantId, AppId);

            // Assert
            Assert.True(outcome.IsSuccess);
            Assert.Single(outcome.Data.AssignmentIds);
            Assert.Equal(2, outcome.Data.MatchedTicketCount);
            Assert.Equal(2, outcome.Data.MatchedPlayerCount);

            var assignment = await fixture.Store.FindAssignmentAsync(TenantId, AppId, outcome.Data.AssignmentIds[0]);
            Assert.Equal(new List<long> { PlayerOne, PlayerTwo }, assignment.PlayerIds);
            Assert.Equal(2, assignment.TicketIds.Count);
            Assert.Single(fixture.Recorder.Filter(OnlineMatchEvents.AssignmentCreated));
            Assert.Equal(2, fixture.Recorder.Filter(OnlineMatchEvents.TicketChanged).Count);
        }

        /// <summary>
        /// 验证同一批票据不会被第二轮重复成组（重复 assignment = 0）。
        /// </summary>
        [Fact]
        public async Task RunOnceAsync_WhenRunAgain_ShouldNotProduceSecondAssignment()
        {
            // Arrange
            var fixture = CreateFixture();
            await EnqueueAsync(fixture, PlayerOne);
            await EnqueueAsync(fixture, PlayerTwo);
            await fixture.Coordinator.RunOnceAsync(TenantId, AppId);

            // Act
            var outcome = await fixture.Coordinator.RunOnceAsync(TenantId, AppId);

            // Assert
            Assert.True(outcome.IsSuccess);
            Assert.Empty(outcome.Data.AssignmentIds);
            Assert.Equal(0, outcome.Data.MatchedTicketCount);
            var assignments = await fixture.Store.ListAssignmentsAsync(TenantId, AppId);
            Assert.Single(assignments);
        }

        /// <summary>
        /// 验证人数不足的票据不被单独成组，继续留在队列中（不做半组提交）。
        /// </summary>
        [Fact]
        public async Task RunOnceAsync_WithSingleTicket_ShouldKeepQueued()
        {
            // Arrange
            var fixture = CreateFixture();
            var ticket = await EnqueueAsync(fixture, PlayerOne);

            // Act
            var outcome = await fixture.Coordinator.RunOnceAsync(TenantId, AppId);

            // Assert
            Assert.True(outcome.IsSuccess);
            Assert.Empty(outcome.Data.AssignmentIds);
            var queued = await fixture.Store.FindAsync(TenantId, AppId, ticket.TicketId);
            Assert.Equal(OnlineMatchTicketState.Queued, queued.State);
        }

        /// <summary>
        /// 验证队伍票据整队成组、不被拆散，也不与落单玩家拼组（队伍完整性红线）。
        /// </summary>
        [Fact]
        public async Task RunOnceAsync_WithPartyTicket_ShouldNotSplitParty()
        {
            // Arrange
            var fixture = CreateFixture();
            await EnqueueAsync(fixture, PlayerOne, members: new List<long> { PlayerOne, PlayerTwo }, partyId: "pty-1");
            var lone = await EnqueueAsync(fixture, PlayerThree);

            // Act
            var outcome = await fixture.Coordinator.RunOnceAsync(TenantId, AppId);

            // Assert
            Assert.True(outcome.IsSuccess);
            var assignment = await fixture.Store.FindAssignmentAsync(TenantId, AppId, outcome.Data.AssignmentIds[0]);
            Assert.Equal(new List<long> { PlayerOne, PlayerTwo }, assignment.PlayerIds);
            var queued = await fixture.Store.FindAsync(TenantId, AppId, lone.TicketId);
            Assert.Equal(OnlineMatchTicketState.Queued, queued.State);
        }

        /// <summary>
        /// 验证技术水平区间无交集时不匹配（区间是成组的必要条件）。
        /// </summary>
        [Fact]
        public async Task RunOnceAsync_WithDisjointSkillRange_ShouldNotMatch()
        {
            // Arrange
            var fixture = CreateFixture();
            await EnqueueAsync(fixture, PlayerOne, skillMin: 100, skillMax: 200);
            await EnqueueAsync(fixture, PlayerTwo, skillMin: 900, skillMax: 1000);

            // Act
            var outcome = await fixture.Coordinator.RunOnceAsync(TenantId, AppId);

            // Assert
            Assert.True(outcome.IsSuccess);
            Assert.Empty(outcome.Data.AssignmentIds);
        }

        /// <summary>
        /// 验证等待时间扩展后区间放宽并促成匹配，且规则快照记录扩展事实。
        /// </summary>
        [Fact]
        public async Task RunOnceAsync_AfterWaitExpansion_ShouldMatchAndRecordSnapshot()
        {
            // Arrange
            var options = new OnlineMatchmakerOptions
            {
                WaitExpansionThresholdSeconds = 30,
                ExpansionIntervalSeconds = 15,
                SkillExpansionPerInterval = 50,
                TicketTimeToLiveSeconds = 3600,
            };
            var fixture = CreateFixture(options);
            var waiting = await EnqueueAsync(fixture, PlayerOne, skillMin: 100, skillMax: 200);
            await EnqueueAsync(fixture, PlayerTwo, skillMin: 250, skillMax: 300);

            var stale = await fixture.Store.FindAsync(TenantId, AppId, waiting.TicketId);
            stale.CreatedAtTime = waiting.CreatedAtTime - 60000;
            await fixture.Store.SaveAsync(stale);

            // Act
            var outcome = await fixture.Coordinator.RunOnceAsync(TenantId, AppId, waiting.CreatedAtTime);

            // Assert
            Assert.True(outcome.IsSuccess);
            Assert.Single(outcome.Data.AssignmentIds);
            var assignment = await fixture.Store.FindAssignmentAsync(TenantId, AppId, outcome.Data.AssignmentIds[0]);
            Assert.True(assignment.RuleSnapshot.SkillRangeExpanded);
            Assert.Equal(0, assignment.RuleSnapshot.SkillMin);
            Assert.True(assignment.RuleSnapshot.WaitSeconds >= 60);
        }

        /// <summary>
        /// 验证取消后的票据不再参与成组，且不产生属于它的结果（取消后旧结果 = 0）。
        /// </summary>
        [Fact]
        public async Task RunOnceAsync_AfterCancel_ShouldNotMatchCancelledTicket()
        {
            // Arrange
            var fixture = CreateFixture();
            var cancelled = await EnqueueAsync(fixture, PlayerOne);
            var remaining = await EnqueueAsync(fixture, PlayerTwo);
            await fixture.Tickets.CancelAsync(CreatePlayerScope(PlayerOne), cancelled.TicketId);

            // Act
            var outcome = await fixture.Coordinator.RunOnceAsync(TenantId, AppId);

            // Assert
            Assert.True(outcome.IsSuccess);
            Assert.Empty(outcome.Data.AssignmentIds);
            var queued = await fixture.Store.FindAsync(TenantId, AppId, remaining.TicketId);
            Assert.Equal(OnlineMatchTicketState.Queued, queued.State);
            var assignments = await fixture.Store.ListAssignmentsAsync(TenantId, AppId);
            Assert.Empty(assignments);
        }

        /// <summary>
        /// 验证协调器先清理过期票据，过期票据不参与成组。
        /// </summary>
        [Fact]
        public async Task RunOnceAsync_WithOverdueTicket_ShouldExpireItFirst()
        {
            // Arrange
            var fixture = CreateFixture(new OnlineMatchmakerOptions { TicketTimeToLiveSeconds = 1 });
            var ticket = await EnqueueAsync(fixture, PlayerOne);

            // Act
            var outcome = await fixture.Coordinator.RunOnceAsync(TenantId, AppId, ticket.ExpiresAtTime + 1);

            // Assert
            Assert.True(outcome.IsSuccess);
            Assert.Equal(1, outcome.Data.ExpiredTicketCount);
            Assert.Equal(0, outcome.Data.ScannedTicketCount);
            var expired = await fixture.Store.FindAsync(TenantId, AppId, ticket.TicketId);
            Assert.Equal(OnlineMatchTicketState.Expired, expired.State);
        }

        /// <summary>
        /// 验证发件箱补偿（C374）：Commit 成功后 assignment-created 发布失败不外溢、不丢事件，
        /// 下一轮 RunOnce 起点重投送达，且不产生第二个 assignment。
        /// </summary>
        [Fact]
        public async Task RunOnceAsync_WhenAssignmentEventPublishFails_ShouldRedeliverOnNextRun()
        {
            // Arrange
            var fixture = CreateFixture(new OnlineMatchmakerOptions(), new FlakyOnlineEventPublisher());
            await EnqueueAsync(fixture, PlayerOne);
            await EnqueueAsync(fixture, PlayerTwo);

            // Act：第一轮——assignment-created 发布抛异常，落档与逐票据事件不受影响。
            var firstOutcome = await fixture.Coordinator.RunOnceAsync(TenantId, AppId);

            // Assert：撮合结果完整，失败事件保留在发件箱待投递。
            Assert.True(firstOutcome.IsSuccess);
            Assert.Single(firstOutcome.Data.AssignmentIds);
            Assert.Empty(fixture.Recorder.Filter(OnlineMatchEvents.AssignmentCreated));
            Assert.Equal(2, fixture.Recorder.Filter(OnlineMatchEvents.TicketChanged).Count);
            var pendingAfterFirstRun = await fixture.Outbox.ListPendingAsync(TenantId, AppId);
            Assert.Single(pendingAfterFirstRun);
            Assert.Equal(OnlineMatchEvents.AssignmentCreated, pendingAfterFirstRun[0].Event.EventType);

            // Act：第二轮——无新票据，故障桩已恢复，仅重投待投递事件。
            var secondOutcome = await fixture.Coordinator.RunOnceAsync(TenantId, AppId);

            // Assert：事件补投送达且不重复，assignment 仍唯一，发件箱清空。
            Assert.True(secondOutcome.IsSuccess);
            Assert.Empty(secondOutcome.Data.AssignmentIds);
            Assert.Single(fixture.Recorder.Filter(OnlineMatchEvents.AssignmentCreated));
            Assert.Equal(2, fixture.Recorder.Filter(OnlineMatchEvents.TicketChanged).Count);
            var assignments = await fixture.Store.ListAssignmentsAsync(TenantId, AppId);
            Assert.Single(assignments);
            Assert.Empty(await fixture.Outbox.ListPendingAsync(TenantId, AppId));
        }

        /// <summary>
        /// 验证发件箱补偿（C374）：发布持续失败时事件保留待投递，多轮重试无副作用
        /// （assignment 不重复、事件不重复入箱），撮合结果不受影响。
        /// </summary>
        [Fact]
        public async Task RunOnceAsync_WhenPublishKeepsFailing_ShouldRetainPendingWithoutSideEffects()
        {
            // Arrange
            var publisher = new FlakyOnlineEventPublisher { FailAssignmentCreatedForever = true, };
            var fixture = CreateFixture(new OnlineMatchmakerOptions(), publisher);
            await EnqueueAsync(fixture, PlayerOne);
            await EnqueueAsync(fixture, PlayerTwo);

            // Act
            var firstOutcome = await fixture.Coordinator.RunOnceAsync(TenantId, AppId);
            var secondOutcome = await fixture.Coordinator.RunOnceAsync(TenantId, AppId);

            // Assert：两轮撮合结果正确，assignment 唯一；失败事件保留且不重复。
            Assert.True(firstOutcome.IsSuccess);
            Assert.Single(firstOutcome.Data.AssignmentIds);
            Assert.True(secondOutcome.IsSuccess);
            Assert.Empty(secondOutcome.Data.AssignmentIds);
            var assignments = await fixture.Store.ListAssignmentsAsync(TenantId, AppId);
            Assert.Single(assignments);
            var pending = await fixture.Outbox.ListPendingAsync(TenantId, AppId);
            Assert.Single(pending);
            Assert.Equal(OnlineMatchEvents.AssignmentCreated, pending[0].Event.EventType);
        }

        /// <summary>
        /// 验证发件箱在场时成功路径的事件顺序：assignment-created 先于逐票据 ticket-changed。
        /// </summary>
        [Fact]
        public async Task RunOnceAsync_WithOutbox_ShouldPreserveEventOrder()
        {
            // Arrange
            var fixture = CreateFixture(new OnlineMatchmakerOptions(), null);
            await EnqueueAsync(fixture, PlayerOne);
            await EnqueueAsync(fixture, PlayerTwo);

            // Act
            var outcome = await fixture.Coordinator.RunOnceAsync(TenantId, AppId);

            // Assert
            Assert.True(outcome.IsSuccess);
            var events = fixture.Recorder.Events;
            Assert.Equal(OnlineMatchEvents.AssignmentCreated, events[0].EventType);
            Assert.Equal(2, fixture.Recorder.Filter(OnlineMatchEvents.TicketChanged).Count);
        }

        /// <summary>
        /// 验证无技能区间的满员单票（TeamSize=1）可整票成组，快照以 0/0 记录「全组无技能区间」
        /// （GFX-1083：BuildSnapshot 兜底分支可达，钉住语义防止被误判为死代码删除）。
        /// </summary>
        [Fact]
        public async Task RunOnceAsync_WithSoloTicketWithoutSkillRange_ShouldSnapshotZeroSkillRange()
        {
            // Arrange
            var fixture = CreateFixture();
            var request = new OnlineMatchTicketEnqueueRequest
            {
                PartyId = string.Empty,
                PlayerIds = new List<long> { PlayerOne },
                Mode = 1,
                Region = 1,
                SkillRange = null,
                TeamSize = 1,
                LatencyRequirement = 0,
                CustomProperties = new Dictionary<string, string>(),
            };
            var outcome = await fixture.Tickets.EnqueueAsync(CreatePlayerScope(PlayerOne), request);
            Assert.True(outcome.IsSuccess);

            // Act
            var run = await fixture.Coordinator.RunOnceAsync(TenantId, AppId);

            // Assert
            Assert.True(run.IsSuccess);
            Assert.Single(run.Data.AssignmentIds);
            var assignment = await fixture.Store.FindAssignmentAsync(TenantId, AppId, run.Data.AssignmentIds[0]);
            Assert.NotNull(assignment);
            Assert.Equal(0, assignment.RuleSnapshot.SkillMin);
            Assert.Equal(0, assignment.RuleSnapshot.SkillMax);
            Assert.False(assignment.RuleSnapshot.SkillRangeExpanded);
        }

        /// <summary>
        /// 以包内默认匹配参数创建一套测试夹具。
        /// </summary>
        /// <returns>测试夹具。</returns>
        private static Fixture CreateFixture()
        {
            return CreateFixture(new OnlineMatchmakerOptions());
        }

        /// <summary>
        /// 以指定匹配参数与可注入事件发布器创建一套测试夹具（内存存储 + 票据服务 + 协调器 + 发件箱；
        /// 故障注入时记录桩挂在故障桩内侧，成功投递仍可被断言）。
        /// </summary>
        /// <param name="options">匹配与限流可配置项。</param>
        /// <param name="eventPublisher">事件发布器（null = 直连记录桩）。</param>
        /// <returns>测试夹具。</returns>
        private static Fixture CreateFixture(OnlineMatchmakerOptions options, FlakyOnlineEventPublisher eventPublisher)
        {
            var store = new InMemoryOnlineMatchTicketStore();
            var recorder = eventPublisher == null ? new OnlineEventRecorder() : eventPublisher.InnerRecorder;
            var outbox = new InMemoryOnlineMatchEventOutbox();
            var tickets = new OnlineMatchTicketService(store, recorder, options);
            var publisher = eventPublisher == null ? (IOnlineEventPublisher)recorder : eventPublisher;
            var coordinator = new OnlineMatchmakerCoordinator(store, tickets, publisher, options, null, outbox);
            return new Fixture(store, recorder, tickets, coordinator, outbox);
        }

        /// <summary>
        /// 以指定匹配参数创建一套测试夹具（内存存储 + 票据服务 + 协调器；无发件箱，既有直发路径）。
        /// </summary>
        /// <param name="options">匹配与限流可配置项。</param>
        /// <returns>测试夹具。</returns>
        private static Fixture CreateFixture(OnlineMatchmakerOptions options)
        {
            var store = new InMemoryOnlineMatchTicketStore();
            var recorder = new OnlineEventRecorder();
            var tickets = new OnlineMatchTicketService(store, recorder, options);
            var coordinator = new OnlineMatchmakerCoordinator(store, tickets, recorder, options);
            return new Fixture(store, recorder, tickets, coordinator, null);
        }

        /// <summary>
        /// 入队一张票据。
        /// </summary>
        /// <param name="fixture">测试夹具。</param>
        /// <param name="playerId">发起玩家标识。</param>
        /// <param name="teamSize">目标对局规模。</param>
        /// <param name="skillMin">技能区间下界。</param>
        /// <param name="skillMax">技能区间上界。</param>
        /// <param name="members">票据携带成员（默认仅发起人）。</param>
        /// <param name="partyId">来源队伍标识。</param>
        /// <returns>入队后的票据。</returns>
        private static async Task<OnlineMatchTicket> EnqueueAsync(Fixture fixture, long playerId, int teamSize = 2, int skillMin = 100, int skillMax = 200, List<long> members = null, string partyId = "")
        {
            var request = new OnlineMatchTicketEnqueueRequest
            {
                PartyId = partyId,
                PlayerIds = members == null ? new List<long> { playerId } : members,
                Mode = 1,
                Region = 1,
                SkillRange = new OnlineMatchSkillRange { Min = skillMin, Max = skillMax },
                TeamSize = teamSize,
                LatencyRequirement = 0,
                CustomProperties = new Dictionary<string, string>(),
            };

            var outcome = await fixture.Tickets.EnqueueAsync(CreatePlayerScope(playerId), request);
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
        /// 匹配链路测试夹具。
        /// </summary>
        private sealed class Fixture
        {
            /// <summary>
            /// 初始化 <see cref="Fixture"/>。
            /// </summary>
            /// <param name="store">票据存储。</param>
            /// <param name="recorder">事件记录桩。</param>
            /// <param name="tickets">票据服务。</param>
            /// <param name="outbox">匹配事件发件箱（无补偿直发夹具为 null）。</param>
            public Fixture(InMemoryOnlineMatchTicketStore store, OnlineEventRecorder recorder, OnlineMatchTicketService tickets, OnlineMatchmakerCoordinator coordinator, InMemoryOnlineMatchEventOutbox outbox)
            {
                Store = store;
                Recorder = recorder;
                Tickets = tickets;
                Coordinator = coordinator;
                Outbox = outbox;
            }

            /// <summary>
            /// 获取匹配事件发件箱（无补偿直发夹具为 null）。
            /// </summary>
            public InMemoryOnlineMatchEventOutbox Outbox
            {
                get;
            }

            /// <summary>
            /// 获取票据存储。
            /// </summary>
            public InMemoryOnlineMatchTicketStore Store
            {
                get;
            }

            /// <summary>
            /// 获取事件记录桩。
            /// </summary>
            public OnlineEventRecorder Recorder
            {
                get;
            }

            /// <summary>
            /// 获取票据服务。
            /// </summary>
            public OnlineMatchTicketService Tickets
            {
                get;
            }

            /// <summary>
            /// 获取匹配协调器。
            /// </summary>
            public OnlineMatchmakerCoordinator Coordinator
            {
                get;
            }
        }

        /// <summary>
        /// 故障注入事件发布桩（C374）：内侧挂记录桩，成功路径照常入册；
        /// assignment-created 事件按配置注入失败（首轮一次或持续），其余事件恒成功。
        /// </summary>
        private sealed class FlakyOnlineEventPublisher : IOnlineEventPublisher
        {
            /// <summary>
            /// 初始化 <see cref="FlakyOnlineEventPublisher"/>。
            /// </summary>
            public FlakyOnlineEventPublisher()
            {
                InnerRecorder = new OnlineEventRecorder();
            }

            /// <summary>
            /// 获取内侧事件记录桩（成功投递的断言面）。
            /// </summary>
            public OnlineEventRecorder InnerRecorder
            {
                get;
            }

            /// <summary>
            /// 获取或设置 assignment-created 事件是否持续发布失败。
            /// </summary>
            public bool FailAssignmentCreatedForever
            {
                get;
                set;
            }

            /// <summary>
            /// 是否已对 assignment-created 注入过一次失败（一次性故障模式）。
            /// </summary>
            private bool _failedOnce;

            /// <summary>
            /// 发布事件：assignment-created 事件按故障配置抛 <see cref="InvalidOperationException"/>，其余转内侧记录桩。
            /// </summary>
            /// <param name="onlineEvent">待发布事件。</param>
            /// <param name="cancellationToken">取消令牌。</param>
            /// <returns>完成通知。</returns>
            public Task PublishAsync(OnlineEvent onlineEvent, CancellationToken cancellationToken = default)
            {
                if (onlineEvent.EventType == OnlineMatchEvents.AssignmentCreated && (FailAssignmentCreatedForever || !_failedOnce))
                {
                    _failedOnce = true;
                    throw new InvalidOperationException("simulated assignment event publish failure");
                }

                return InnerRecorder.PublishAsync(onlineEvent, cancellationToken);
            }
        }
    }
}
