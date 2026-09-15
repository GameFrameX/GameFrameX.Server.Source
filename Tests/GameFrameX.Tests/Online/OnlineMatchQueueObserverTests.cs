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
using GameFrameX.Online.Events;
using GameFrameX.Online.Matchmaking;
using GameFrameX.Online.Scope;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 匹配队列观测器测试（vault:C5 VC-4.10：Admin 可核对各状态计数与重复分配红指标）。
    /// </summary>
    public class OnlineMatchQueueObserverTests
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
        /// 验证观测快照按状态给出票据计数、玩家计数与分配数（VC-4.10 覆盖率）。
        /// </summary>
        [Fact]
        public async Task ObserveAsync_ShouldReportCountsByState()
        {
            // Arrange
            var fixture = CreateFixture();
            await EnqueueAsync(fixture.Tickets, PlayerOne);
            await EnqueueAsync(fixture.Tickets, PlayerTwo);
            var cancelled = await EnqueueAsync(fixture.Tickets, PlayerThree);
            await fixture.Tickets.CancelAsync(CreatePlayerScope(PlayerThree), cancelled.TicketId);
            await fixture.Coordinator.RunOnceAsync(TenantId, AppId);

            // Act
            var outcome = await fixture.Observer.ObserveAsync(TenantId, AppId);

            // Assert
            Assert.True(outcome.IsSuccess);
            Assert.Equal(2, outcome.Data.MatchedTicketCount);
            Assert.Equal(2, outcome.Data.MatchedPlayerCount);
            Assert.Equal(1, outcome.Data.CancelledTicketCount);
            Assert.Equal(0, outcome.Data.QueuedTicketCount);
            Assert.Equal(0, outcome.Data.ExpiredTicketCount);
            Assert.Equal(0, outcome.Data.FailedTicketCount);
            Assert.Equal(1, outcome.Data.AssignmentCount);
            Assert.Equal(3, outcome.Data.Tickets.Count);
        }

        /// <summary>
        /// 验证重复分配票据数在正常链路上恒为 0（VC-4.12 红指标）。
        /// </summary>
        [Fact]
        public async Task ObserveAsync_ShouldReportZeroDuplicateAssignmentTickets()
        {
            // Arrange
            var fixture = CreateFixture();
            await EnqueueAsync(fixture.Tickets, PlayerOne);
            await EnqueueAsync(fixture.Tickets, PlayerTwo);
            await fixture.Coordinator.RunOnceAsync(TenantId, AppId);

            // Act
            var outcome = await fixture.Observer.ObserveAsync(TenantId, AppId);

            // Assert
            Assert.True(outcome.IsSuccess);
            Assert.Equal(0, outcome.Data.DuplicateAssignmentTicketCount);
        }

        /// <summary>
        /// 验证排队中的票据玩家被计入队列人数（未成组时队列构成可核对）。
        /// </summary>
        [Fact]
        public async Task ObserveAsync_WithQueuedTickets_ShouldReportQueuedPlayers()
        {
            // Arrange
            var fixture = CreateFixture();
            await EnqueueAsync(fixture.Tickets, PlayerOne);
            await EnqueueAsync(fixture.Tickets, PlayerTwo);

            // Act
            var outcome = await fixture.Observer.ObserveAsync(TenantId, AppId);

            // Assert
            Assert.True(outcome.IsSuccess);
            Assert.Equal(2, outcome.Data.QueuedTicketCount);
            Assert.Equal(2, outcome.Data.QueuedPlayerCount);
            Assert.Equal(0, outcome.Data.MatchedTicketCount);
            Assert.Equal(0, outcome.Data.AssignmentCount);
            Assert.Equal(2, outcome.Data.Tickets.Count);
        }

        /// <summary>
        /// 验证空队列的观测快照各项为零（观测口径在无事发生时也确定）。
        /// </summary>
        [Fact]
        public async Task ObserveAsync_OnEmptyScope_ShouldReportZeros()
        {
            // Arrange
            var fixture = CreateFixture();

            // Act
            var outcome = await fixture.Observer.ObserveAsync(TenantId, AppId);

            // Assert
            Assert.True(outcome.IsSuccess);
            Assert.Equal(0, outcome.Data.QueuedTicketCount);
            Assert.Equal(0, outcome.Data.AssignmentCount);
            Assert.Empty(outcome.Data.Tickets);
        }

        /// <summary>
        /// 创建一套测试夹具（内存存储 + 票据服务 + 协调器 + 观测器）。
        /// </summary>
        /// <returns>测试夹具。</returns>
        private static Fixture CreateFixture()
        {
            var store = new InMemoryOnlineMatchTicketStore();
            var recorder = new OnlineEventRecorder();
            var tickets = new OnlineMatchTicketService(store, recorder);
            var coordinator = new OnlineMatchmakerCoordinator(store, tickets, recorder);
            return new Fixture(new OnlineMatchQueueObserver(store), tickets, coordinator);
        }

        /// <summary>
        /// 入队一张票据。
        /// </summary>
        /// <param name="service">票据服务。</param>
        /// <param name="playerId">发起玩家标识。</param>
        /// <returns>入队后的票据。</returns>
        private static async Task<OnlineMatchTicket> EnqueueAsync(OnlineMatchTicketService service, long playerId)
        {
            var request = new OnlineMatchTicketEnqueueRequest
            {
                PartyId = string.Empty,
                PlayerIds = new List<long> { playerId },
                Mode = 1,
                Region = 1,
                SkillRange = new OnlineMatchSkillRange { Min = 100, Max = 200 },
                TeamSize = 2,
                LatencyRequirement = 0,
                CustomProperties = new Dictionary<string, string>(),
            };

            var outcome = await service.EnqueueAsync(CreatePlayerScope(playerId), request);
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
        /// 观测链路测试夹具。
        /// </summary>
        private sealed class Fixture
        {
            /// <summary>
            /// 初始化 <see cref="Fixture"/>。
            /// </summary>
            /// <param name="observer">队列观测器。</param>
            /// <param name="tickets">票据服务。</param>
            /// <param name="coordinator">匹配协调器。</param>
            public Fixture(OnlineMatchQueueObserver observer, OnlineMatchTicketService tickets, OnlineMatchmakerCoordinator coordinator)
            {
                Observer = observer;
                Tickets = tickets;
                Coordinator = coordinator;
            }

            /// <summary>
            /// 获取队列观测器。
            /// </summary>
            public OnlineMatchQueueObserver Observer
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
    }
}
