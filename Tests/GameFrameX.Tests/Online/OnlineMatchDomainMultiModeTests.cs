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
    /// 匹配域多模式回归：多玩法注册时票据模式必须命中对应玩法的注册键（不再串写首个玩法的模式），
    /// 以及 <see cref="OnlineMatchRuntime.ResolveGameForTicketMode"/> 的精确命中 / 单玩法回落 / 多玩法未命三分支。
    /// </summary>
    public class OnlineMatchDomainMultiModeTests
    {
        /// <summary>区服标识。</summary>
        private const long ServerId = 100;

        /// <summary>玩家一。</summary>
        private const long PlayerOne = 1001;

        /// <summary>玩家二。</summary>
        private const long PlayerTwo = 1002;

        /// <summary>第二玩法注册键（票据模式同值精确命中）。</summary>
        private const int SecondGameMode = 2;

        [Fact]
        public async Task CreateFromTicket_MultiGameRegistered_MatchModeShouldFollowTicketMode()
        {
            var harness = await Harness.StartedMatchedAsync(SecondGameMode);
            foreach (var playerId in new[] { PlayerOne, PlayerTwo })
            {
                var entered = await harness.Domain.EnterAsync(harness.ScopeOf(playerId), harness.MatchId);
                Assert.True(entered.IsSuccess, entered.Message);
            }

            var actor = harness.Runtime.SnapshotActors().Single();
            var snapshot = actor.GetSnapshot();

            // 票据模式 2 + 玩法 2 在册 → 对局 Mode 必须是玩法 2 的注册键（回归断言：旧实现恒写首个注册玩法的模式）。
            Assert.Equal(SecondGameMode, snapshot.Mode);
        }

        [Fact]
        public void ResolveGameForTicketMode_ExactRegistration_ShouldReturnThatGame()
        {
            var runtime = BuildRuntime(new StubMatchGame(1), new StubMatchGame(SecondGameMode));

            var game = runtime.ResolveGameForTicketMode(SecondGameMode);

            Assert.NotNull(game);
            Assert.Equal(SecondGameMode, game.Mode);
        }

        [Fact]
        public void ResolveGameForTicketMode_SingleGameRegistered_ShouldFallBackForAnyTicketMode()
        {
            var runtime = BuildRuntime(new StubMatchGame(1));

            var game = runtime.ResolveGameForTicketMode(SecondGameMode);

            Assert.NotNull(game);
            Assert.Equal(1, game.Mode);
        }

        [Fact]
        public void ResolveGameForTicketMode_MultiGameUnregisteredTicketMode_ShouldReturnNull()
        {
            var runtime = BuildRuntime(new StubMatchGame(1), new StubMatchGame(SecondGameMode));

            Assert.Null(runtime.ResolveGameForTicketMode(9));
        }

        /// <summary>
        /// 测试基座：双玩法（模式 1 + 指定模式）注册 + 票据域 + 运行时 + 域服务，
        /// 两名玩家按指定票据模式入队并完成一次撮合（对局已创建）。
        /// </summary>
        private sealed class Harness
        {
            public OnlineMatchTicketService Tickets { get; }

            public OnlineMatchmakerCoordinator Coordinator { get; }

            public OnlineMatchRuntime Runtime { get; }

            public OnlineMatchDomainService Domain { get; }

            private Harness(OnlineMatchTicketService tickets, OnlineMatchmakerCoordinator coordinator, OnlineMatchRuntime runtime, OnlineMatchDomainService domain)
            {
                Tickets = tickets;
                Coordinator = coordinator;
                Runtime = runtime;
                Domain = domain;
            }

            public OnlineScope ScopeOf(long playerId)
            {
                return new OnlineScope(ServerId, playerId);
            }

            public static async Task<Harness> StartedMatchedAsync(int ticketMode)
            {
                var publisher = new OnlineEventRecorder();
                var ticketStore = new InMemoryOnlineMatchTicketStore();
                var matchmakerOptions = new OnlineMatchmakerOptions();
                var tickets = new OnlineMatchTicketService(ticketStore, publisher, matchmakerOptions);
                var coordinator = new OnlineMatchmakerCoordinator(ticketStore, tickets, publisher, matchmakerOptions);
                var runtime = new OnlineMatchRuntime(new InMemoryOnlineMatchActorStore(), publisher, new OnlineMatchRuntimeOptions());
                var firstGame = new StubMatchGame(1);
                var secondGame = new StubMatchGame(ticketMode);
                runtime.RegisterGame(firstGame);
                runtime.RegisterGame(secondGame);
                var domain = new OnlineMatchDomainService(ticketStore, tickets, runtime, secondGame, new OnlineMatchRegistryOptions());
                var harness = new Harness(tickets, coordinator, runtime, domain);

                foreach (var playerId in new[] { PlayerOne, PlayerTwo })
                {
                    var enqueued = await domain.EnqueueAsync(harness.ScopeOf(playerId), ticketMode, 1000);
                    Assert.True(enqueued.IsSuccess, enqueued.Message);
                }

                var matched = await coordinator.RunOnceAsync();
                Assert.True(matched.IsSuccess, matched.Message);
                Assert.Single(matched.Data.AssignmentIds);

                var firstStatus = await domain.GetStatusAsync(harness.ScopeOf(PlayerOne));
                Assert.True(firstStatus.IsSuccess, firstStatus.Message);
                Assert.True(firstStatus.Data.Matched);
                harness.MatchId = firstStatus.Data.MatchId;
                return harness;
            }

            public long MatchId { get; private set; }
        }


        /// <summary>构造指定注册键的最小玩法桩（纯裁决零状态：不开局、不出结果）。</summary>
        private sealed class StubMatchGame : IOnlineMatchGame
        {
            public StubMatchGame(int mode)
            {
                Mode = mode;
            }

            public int Mode { get; }

            public int MinPlayers
            {
                get { return 2; }
            }

            public int MaxPlayers
            {
                get { return 2; }
            }

            public byte[] CreateInitialState(OnlineMatch match)
            {
                return new byte[0];
            }

            public OnlineMatchGameStepResult ApplyInput(OnlineMatch match, OnlineMatchMember member, OnlineMatchInput input)
            {
                return new OnlineMatchGameStepResult { Accepted = false };
            }

            public bool Advance(OnlineMatch match, long nowUnixMilliseconds, List<OnlineMatchServerEvent> emittedEvents)
            {
                return false;
            }

            public bool IsCompleted(OnlineMatch match)
            {
                return false;
            }

            public OnlineMatchBridgeGameView BuildBridgeView(long roomId, OnlineMatchSnapshot snapshot, IReadOnlyList<OnlineMatchServerEvent> events)
            {
                return null;
            }

            public OnlineMatchResult BuildResult(OnlineMatch match)
            {
                return null;
            }
        }

        private static OnlineMatchRuntime BuildRuntime(params IOnlineMatchGame[] games)
        {
            var runtime = new OnlineMatchRuntime(new InMemoryOnlineMatchActorStore(), new OnlineEventRecorder(), new OnlineMatchRuntimeOptions());
            foreach (var game in games)
            {
                runtime.RegisterGame(game);
            }

            return runtime;
        }
    }
}
