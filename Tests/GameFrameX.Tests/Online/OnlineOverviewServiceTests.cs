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
using GameFrameX.Online.Match;
using GameFrameX.Online.Matchmaking;
using GameFrameX.Online.Party;
using GameFrameX.Online.Scope;
using GameFrameX.Online.Session;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 在线总览服务测试（C101 / vault:C9 S8.1 · VC-8.1-a / VC-8.1-b：计数口径、队列概况、
    /// 空作用域与跨作用域同构、作用域三键校验）。
    /// </summary>
    public class OnlineOverviewServiceTests
    {
        /// <summary>测试用租户标识。</summary>
        private const long TenantId = OnlineOverviewTimelineTestHarness.TenantId;

        /// <summary>测试用应用标识。</summary>
        private const long AppId = OnlineOverviewTimelineTestHarness.AppId;

        /// <summary>测试用区服标识。</summary>
        private const long ServerId = OnlineOverviewTimelineTestHarness.ServerId;

        /// <summary>测试用其它区服标识。</summary>
        private const long OtherServerId = OnlineOverviewTimelineTestHarness.OtherServerId;

        /// <summary>
        /// 验证总览计数口径：在线玩家数排除被限制玩家与他服玩家；会话数只计本作用域非终态会话并给出状态分布与重连率；
        /// 队伍数排除终态与他服队伍；对局数排除已释放（Closed）与他服对局并给出状态分布（VC-8.1-a）。
        /// </summary>
        [Fact]
        public async Task ReviewAsync_ShouldCountPlayersSessionsPartiesAndMatches()
        {
            // Arrange
            var harness = new OnlineOverviewTimelineTestHarness();
            var alice = await harness.RegisterPlayerAsync(ServerId, "alice");
            var bob = await harness.RegisterPlayerAsync(ServerId, "bob");
            var carol = await harness.RegisterPlayerAsync(ServerId, "carol");
            var dave = await harness.RegisterPlayerAsync(OtherServerId, "dave");

            var aliceSession = await harness.OpenSessionAsync(ServerId, alice.Player.Id);
            var bobSession = await harness.OpenSessionAsync(ServerId, bob.Player.Id, reconnecting: true);
            await harness.SetOnlineAsync(ServerId, alice.Player.Id, aliceSession.Id);
            await harness.SetOnlineAsync(ServerId, bob.Player.Id, bobSession.Id);
            await harness.SetOnlineAsync(ServerId, carol.Player.Id, "sess-blocked");
            await harness.BlockAsync(ServerId, carol.Player.Id);
            var daveSession = await harness.OpenSessionAsync(OtherServerId, dave.Player.Id);
            await harness.SetOnlineAsync(OtherServerId, dave.Player.Id, daveSession.Id);

            await harness.CreatePartyAsync(ServerId, alice.Player.Id);
            var bobParty = await harness.CreatePartyAsync(ServerId, bob.Player.Id);
            await harness.DisbandPartyAsync(ServerId, bob.Player.Id, bobParty.PartyId);
            await harness.CreatePartyAsync(OtherServerId, dave.Player.Id);

            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await harness.CreateMatchAsync(ServerId, alice.Player.Id, "match-running", OnlineMatchState.Running, now);
            await harness.CreateMatchAsync(ServerId, alice.Player.Id, "match-completed", OnlineMatchState.Completed, now, settled: true);
            await harness.CreateMatchAsync(ServerId, alice.Player.Id, "match-closed", OnlineMatchState.Closed, now);
            await harness.CreateMatchAsync(OtherServerId, dave.Player.Id, "match-other-server", OnlineMatchState.Running, now);

            // Act
            var outcome = await harness.CreateOverviewService().ReviewAsync(OnlineOverviewTimelineTestHarness.CreateScope());

            // Assert
            Assert.True(outcome.IsSuccess);
            var snapshot = outcome.Data;
            Assert.Equal(TenantId, snapshot.TenantId);
            Assert.Equal(AppId, snapshot.AppId);
            Assert.Equal(ServerId, snapshot.ServerId);
            Assert.True(snapshot.ObservedTime > 0);

            // 在线玩家数：alice + bob（carol 被限制、dave 在他服）
            Assert.Equal(2, snapshot.OnlinePlayerCount);

            // 会话数：alice（活跃）+ bob（重连中）；dave 的会话在他服
            Assert.Equal(2, snapshot.SessionCount);
            Assert.Equal(1, snapshot.SessionStateCounts[OnlineSessionState.Active]);
            Assert.Equal(1, snapshot.SessionStateCounts[OnlineSessionState.Reconnecting]);
            Assert.Equal(0.5d, snapshot.ReconnectRate, 4);

            // 队伍数：alice 的队伍存活；bob 的已解散；dave 的在他服
            Assert.Equal(1, snapshot.PartyCount);
            Assert.Equal(1, snapshot.PartyStateCounts[OnlinePartyState.Created]);

            // 对局数：本服两场（进行中 + 已结算）；已释放（Closed）一场与他服一场不计入
            Assert.Equal(2, snapshot.MatchCount);
            Assert.Equal(1, snapshot.MatchStateCounts[OnlineMatchState.Running]);
            Assert.Equal(1, snapshot.MatchStateCounts[OnlineMatchState.Completed]);
            Assert.False(snapshot.MatchStateCounts.ContainsKey(OnlineMatchState.Closed));

            Assert.Empty(snapshot.QueueSummaries);
        }

        /// <summary>
        /// 验证队列概况按 (玩法模式, 区域) 分组：深度为排队态票据数、平均等待可按匹配域口径复算、
        /// 最近一分钟吞吐取本作用域匹配分配所消费的票据数，且他服票据不计入（VC-8.1-b）。
        /// </summary>
        [Fact]
        public async Task ReviewAsync_ShouldGroupQueueSummaryByModeAndRegion()
        {
            // Arrange
            var harness = new OnlineOverviewTimelineTestHarness();
            await harness.EnqueueAsync(ServerId, 1001);
            await harness.EnqueueAsync(ServerId, 1002);
            await harness.RunMatchmakerAsync(ServerId);
            await harness.EnqueueAsync(ServerId, 1003);
            await harness.EnqueueAsync(ServerId, 1004, mode: 2, region: 3);
            await harness.EnqueueAsync(OtherServerId, 1005, mode: 5, region: 5);

            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var queued = await harness.TicketStore.ListQueuedAsync(TenantId, AppId);

            // Act
            var outcome = await harness.CreateOverviewService().ReviewAsync(OnlineOverviewTimelineTestHarness.CreateScope(), now);

            // Assert
            Assert.True(outcome.IsSuccess);
            var summaries = outcome.Data.QueueSummaries;
            Assert.Equal(2, summaries.Count);

            var modeOne = summaries[0];
            Assert.Equal(1, modeOne.Mode);
            Assert.Equal(1, modeOne.Region);
            Assert.Equal(1, modeOne.QueueDepth);
            Assert.Equal(2, modeOne.ThroughputPerMinute);

            var modeTwo = summaries[1];
            Assert.Equal(2, modeTwo.Mode);
            Assert.Equal(3, modeTwo.Region);
            Assert.Equal(1, modeTwo.QueueDepth);
            Assert.Equal(0, modeTwo.ThroughputPerMinute);

            // 平均等待按匹配域既有口径复算（同一时刻注入，口径可核对）
            var modeOneTicket = FindTicket(queued, 1, 1);
            var modeTwoTicket = FindTicket(queued, 2, 3);
            Assert.Equal(OnlineMatchRule.WaitSeconds(modeOneTicket, now), modeOne.AverageWaitSeconds);
            Assert.Equal(OnlineMatchRule.WaitSeconds(modeTwoTicket, now), modeTwo.AverageWaitSeconds);
        }

        /// <summary>
        /// 验证空作用域下各项计数为零、重连率分母为零时取 0（不产生除零与平滑偏差）。
        /// </summary>
        [Fact]
        public async Task ReviewAsync_OnEmptyScope_ShouldReturnZeros()
        {
            // Arrange
            var harness = new OnlineOverviewTimelineTestHarness();

            // Act
            var outcome = await harness.CreateOverviewService().ReviewAsync(OnlineOverviewTimelineTestHarness.CreateScope());

            // Assert
            Assert.True(outcome.IsSuccess);
            Assert.Equal(0, outcome.Data.OnlinePlayerCount);
            Assert.Equal(0, outcome.Data.SessionCount);
            Assert.Equal(0d, outcome.Data.ReconnectRate, 4);
            Assert.Equal(0, outcome.Data.PartyCount);
            Assert.Equal(0, outcome.Data.MatchCount);
            Assert.Empty(outcome.Data.QueueSummaries);
        }

        /// <summary>
        /// 验证跨作用域读取与无数据同构：他租户 / 他 App / 他区服的数据在本地作用域快照中一律不可见（反预言）。
        /// </summary>
        [Fact]
        public async Task ReviewAsync_CrossScope_ShouldBeIndistinguishableFromEmpty()
        {
            // Arrange
            var harness = new OnlineOverviewTimelineTestHarness();
            var dave = await harness.RegisterPlayerAsync(OtherServerId, "dave");
            var daveSession = await harness.OpenSessionAsync(OtherServerId, dave.Player.Id);
            await harness.SetOnlineAsync(OtherServerId, dave.Player.Id, daveSession.Id);
            await harness.CreatePartyAsync(OtherServerId, dave.Player.Id);
            await harness.CreateMatchAsync(OtherServerId, dave.Player.Id, "match-other", OnlineMatchState.Running, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            await harness.EnqueueAsync(OtherServerId, dave.Player.Id, mode: 7, region: 7);

            // Act
            var outcome = await harness.CreateOverviewService().ReviewAsync(OnlineOverviewTimelineTestHarness.CreateScope());

            // Assert
            Assert.True(outcome.IsSuccess);
            Assert.Equal(0, outcome.Data.OnlinePlayerCount);
            Assert.Equal(0, outcome.Data.SessionCount);
            Assert.Equal(0, outcome.Data.PartyCount);
            Assert.Equal(0, outcome.Data.MatchCount);
            Assert.Empty(outcome.Data.QueueSummaries);
        }

        /// <summary>
        /// 验证作用域三键缺失时拒绝（参数非法），空作用域实例抛参数异常。
        /// </summary>
        [Fact]
        public async Task ReviewAsync_InvalidScope_ShouldReturnParameterInvalid()
        {
            // Arrange
            var service = new OnlineOverviewTimelineTestHarness().CreateOverviewService();

            // Act
            var missingServer = await service.ReviewAsync(new OnlineScope(TenantId, AppId, 0));
            var missingTenant = await service.ReviewAsync(new OnlineScope(0, AppId, ServerId));

            // Assert
            Assert.False(missingServer.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, missingServer.Code);
            Assert.False(missingTenant.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, missingTenant.Code);
            await Assert.ThrowsAsync<ArgumentNullException>(() => service.ReviewAsync(null));
        }

        /// <summary>
        /// 按 (模式, 区域) 找到排队态票据。
        /// </summary>
        /// <param name="queued">排队态票据集合。</param>
        /// <param name="mode">玩法模式。</param>
        /// <param name="region">区域。</param>
        /// <returns>匹配到的票据。</returns>
        private static OnlineMatchTicket FindTicket(System.Collections.Generic.IReadOnlyList<OnlineMatchTicket> queued, int mode, int region)
        {
            foreach (var ticket in queued)
            {
                if (ticket.Mode == mode && ticket.Region == region)
                {
                    return ticket;
                }
            }

            Assert.Fail("排队态票据中未找到模式 " + mode + " / 区域 " + region);
            return null;
        }
    }
}
