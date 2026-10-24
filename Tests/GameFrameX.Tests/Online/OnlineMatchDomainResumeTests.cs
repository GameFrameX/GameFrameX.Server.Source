using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Scope;
using GameFrameX.Capability.Online.Match;
using GameFrameX.Capability.Online.Matchmaking;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 匹配域会话中介恢复面测试：断线通知（成员标记 + 令牌服务端留存）、
    /// 窗口内恢复（快照 + 增量）、活跃成员幂等、无对局空回执、超窗 / 已结束 / 已离局显式失败。
    /// </summary>
    public class OnlineMatchDomainResumeTests
    {
        /// <summary>租户标识。</summary>
        private const long TenantId = 1;

        /// <summary>App 标识。</summary>
        private const long AppId = 10;

        /// <summary>区服标识。</summary>
        private const long ServerId = 100;

        /// <summary>玩家一。</summary>
        private const long PlayerOne = 1001;

        /// <summary>玩家二。</summary>
        private const long PlayerTwo = 1002;

        [Fact]
        public async Task NotifyPlayerDisconnected_InLiveMatch_ShouldMarkMemberAndKeepTokenServerSide()
        {
            var harness = await Harness.StartedAsync();
            var notified = await harness.Domain.NotifyPlayerDisconnectedAsync(harness.ScopeOf(PlayerOne));
            Assert.True(notified.IsSuccess, notified.Message);
            Assert.True(notified.Data.Marked);
            Assert.Equal(harness.NumericMatchId, notified.Data.MatchId);

            var member = harness.FindMember(PlayerOne);
            Assert.Equal(OnlineMatchMemberState.Disconnected, member.State);
            Assert.False(string.IsNullOrEmpty(member.ReconnectToken), "rt- 令牌必须服务端留存（不下发客户端）");
            Assert.True(member.ReconnectDeadlineTime > 0);
        }

        [Fact]
        public async Task NotifyPlayerDisconnected_WithoutMatch_ShouldReturnNotMarked()
        {
            var harness = new Harness();
            var notified = await harness.Domain.NotifyPlayerDisconnectedAsync(harness.ScopeOf(PlayerOne));
            Assert.True(notified.IsSuccess);
            Assert.False(notified.Data.Marked);
            Assert.Equal(0, notified.Data.MatchId);
        }

        [Fact]
        public async Task TryResume_WithinWindow_ShouldResumeMemberAndReturnSnapshot()
        {
            var harness = await Harness.StartedAsync();
            await harness.Domain.NotifyPlayerDisconnectedAsync(harness.ScopeOf(PlayerOne));

            var resumed = await harness.Domain.TryResumeForPlayerAsync(harness.ScopeOf(PlayerOne), 0);
            Assert.True(resumed.IsSuccess, resumed.Message);
            Assert.True(resumed.Data.Resumed);
            Assert.Equal(harness.NumericMatchId, resumed.Data.MatchId);
            Assert.Equal((int)OnlineMatchState.Running, resumed.Data.MatchState);
            Assert.True(resumed.Data.ServerSequence > 0);

            var self = resumed.Data.Members.Single(member => member.PlayerId == PlayerOne);
            Assert.Equal((int)OnlineMatchMemberState.Playing, self.State);
        }

        [Fact]
        public async Task TryResume_ActiveMember_ShouldBeIdempotent()
        {
            var harness = await Harness.StartedAsync();

            var resumed = await harness.Domain.TryResumeForPlayerAsync(harness.ScopeOf(PlayerOne), 0);
            Assert.True(resumed.IsSuccess, resumed.Message);
            Assert.True(resumed.Data.Resumed);

            var member = harness.FindMember(PlayerOne);
            Assert.Equal(OnlineMatchMemberState.Playing, member.State);
        }

        [Fact]
        public async Task TryResume_WithoutMatch_ShouldReturnNotResumedSuccess()
        {
            var harness = new Harness();
            var resumed = await harness.Domain.TryResumeForPlayerAsync(harness.ScopeOf(PlayerOne), 0);
            Assert.True(resumed.IsSuccess);
            Assert.False(resumed.Data.Resumed);
            Assert.Equal(0, resumed.Data.MatchId);
        }

        [Fact]
        public async Task TryResume_AfterWindow_ShouldFailWithNetworkTimeout()
        {
            var harness = await Harness.StartedAsync();

            // 断线标记注入过去时刻（窗口 30 秒已过），恢复面使用当前时刻判定。
            var past = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 31_000;
            var marked = await harness.Actor.MarkDisconnectedAsync(harness.ScopeOf(PlayerOne), "rt-stale", past);
            Assert.True(marked.IsSuccess, marked.Message);

            var resumed = await harness.Domain.TryResumeForPlayerAsync(harness.ScopeOf(PlayerOne), 0);
            Assert.False(resumed.IsSuccess);
            Assert.Equal(OnlineErrorCode.NetworkTimeout, resumed.Code);
        }

        [Fact]
        public async Task TryResume_LeftMember_ShouldFailWithScopeDenied()
        {
            var harness = await Harness.StartedAsync();
            var left = await harness.Actor.LeaveAsync(harness.ScopeOf(PlayerOne), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            Assert.True(left.IsSuccess, left.Message);

            var resumed = await harness.Domain.TryResumeForPlayerAsync(harness.ScopeOf(PlayerOne), 0);
            Assert.False(resumed.IsSuccess);
            Assert.Equal(OnlineErrorCode.ScopeDenied, resumed.Code);
        }

        [Fact]
        public async Task TryResume_EndedMatch_ShouldFailWithStateEnded()
        {
            var harness = await Harness.StartedAsync();

            // 运行阶段超时（1 秒）推对局到 Timeout 终态。
            await harness.TickAllAsync(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 2000);

            var resumed = await harness.Domain.TryResumeForPlayerAsync(harness.ScopeOf(PlayerOne), 0);
            Assert.False(resumed.IsSuccess);
            Assert.Equal(OnlineErrorCode.StateEnded, resumed.Code);
        }

        /// <summary>
        /// 测试基座：票据域（入队 / 撮合）+ 对局运行时 + 域服务。
        /// </summary>
        private sealed class Harness
        {
            public Harness() : this(new OnlineMatchRuntimeOptions())
            {
            }

            public Harness(OnlineMatchRuntimeOptions options)
            {
                Publisher = new OnlineEventRecorder();
                TicketStore = new InMemoryOnlineMatchTicketStore();
                var matchmakerOptions = new OnlineMatchmakerOptions();
                Tickets = new OnlineMatchTicketService(TicketStore, Publisher, matchmakerOptions);
                Coordinator = new OnlineMatchmakerCoordinator(TicketStore, Tickets, Publisher, matchmakerOptions);
                Runtime = new OnlineMatchRuntime(new InMemoryOnlineMatchActorStore(), Publisher, options);
                var game = new OnlineRockPaperScissorsGame();
                Runtime.RegisterGame(game);
                Domain = new OnlineMatchDomainService(TicketStore, Tickets, Coordinator, Runtime, game);
            }

            public OnlineEventRecorder Publisher { get; }

            public InMemoryOnlineMatchTicketStore TicketStore { get; }

            public OnlineMatchTicketService Tickets { get; }

            public OnlineMatchmakerCoordinator Coordinator { get; }

            public OnlineMatchRuntime Runtime { get; }

            public OnlineMatchActor Actor { get; private set; }

            public OnlineMatchDomainService Domain { get; }

            public long NumericMatchId { get; private set; }

            public OnlineScope ScopeOf(long playerId)
            {
                return new OnlineScope(TenantId, AppId, ServerId, playerId);
            }

            public OnlineMatchMember FindMember(long playerId)
            {
                var snapshot = Actor.GetSnapshot();
                return snapshot.Members.Single(member => member.PlayerId == playerId);
            }

            public Task TickAllAsync(long nowUnixMilliseconds)
            {
                return Runtime.TickAllAsync(nowUnixMilliseconds);
            }

            public static async Task<Harness> StartedAsync()
            {
                var harness = new Harness(new OnlineMatchRuntimeOptions { RunningTimeoutSeconds = 1, ReconnectWindowSeconds = 30 });

                // 双人入队 → 即时窗口撮合 → 双方入场同一运行态对局。
                var first = await harness.Domain.EnqueueAsync(harness.ScopeOf(PlayerOne), 1, 1000);
                Assert.True(first.IsSuccess, first.Message);
                Assert.Equal(0, first.Data.MatchId);

                var second = await harness.Domain.EnqueueAsync(harness.ScopeOf(PlayerTwo), 1, 1000);
                Assert.True(second.IsSuccess, second.Message);
                Assert.NotEqual(0, second.Data.MatchId);
                harness.NumericMatchId = second.Data.MatchId;

                foreach (var playerId in new[] { PlayerOne, PlayerTwo })
                {
                    var entered = await harness.Domain.EnterAsync(harness.ScopeOf(playerId), harness.NumericMatchId);
                    Assert.True(entered.IsSuccess, entered.Message);
                    Assert.True(entered.Data.Started);
                }

                harness.Actor = await harness.Runtime.ResolveActorAsync(TenantId, AppId, DeriveRuntimeMatchId(harness.NumericMatchId));
                Assert.NotNull(harness.Actor);
                return harness;
            }

            private static string DeriveRuntimeMatchId(long numericMatchId)
            {
                return "matched-s" + ServerId + "-m" + numericMatchId;
            }
        }
    }
}
