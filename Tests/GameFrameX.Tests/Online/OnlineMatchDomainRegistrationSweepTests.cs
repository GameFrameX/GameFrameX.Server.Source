using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Scope;
using GameFrameX.Capability.Online.Match;
using GameFrameX.Capability.Online.Matchmaking;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 匹配域登记表过期清扫测试（GFX-1103 / M81：双向登记表只增不减致长期运行 OOM）：
    /// 陈年登记项被惰性清扫移除（旧数值标识入场 ResourceNotFound）、未过期登记在严苛节流下不误删
    ///（数值标识映射稳定）、Options 校验拦截 Registry 非正值。
    /// </summary>
    public class OnlineMatchDomainRegistrationSweepTests
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
        public async Task ExpiredRegistration_ShouldBeSweptAndOldNumericMatchIdRejected()
        {
            // 节流间隔 0 = 每次登记表访问都执行清扫（清扫触发面的严苛化）。
            var harness = new Harness(new OnlineMatchRegistryOptions { RegistrationSweepIntervalSeconds = 0 });

            // 单人入队不成组，得到一张排队中的票据。
            var enqueued = await harness.Domain.EnqueueAsync(harness.ScopeOf(PlayerOne), 1, 1000);
            Assert.True(enqueued.IsSuccess, enqueued.Message);
            Assert.Equal(0, enqueued.Data.MatchId);
            var activeTicket = await harness.TicketStore.FindActiveByPlayerAsync(TenantId, AppId, PlayerOne, CancellationToken.None);
            Assert.NotNull(activeTicket);

            // 手工落档陈年分配（CreatedAtTime = 3 小时前，超过默认 7200 秒生命周期；InMemory 落档保留传入值）。
            var staleAssignment = new OnlineMatchAssignment
            {
                TenantId = TenantId,
                AppId = AppId,
                ServerId = ServerId,
                AssignmentId = "as-stale-assignment-1",
                MatchId = "matched-s100-m-stale-1",
                Mode = 1,
                Region = 1,
                CreatedAtTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 3 * 60 * 60 * 1000L,
                PlayerIds = new List<long> { PlayerOne },
                TicketIds = new List<string> { activeTicket.TicketId },
            };
            var committed = await harness.TicketStore.CommitMatchAsync(staleAssignment, new[] { activeTicket.TicketId }, OnlineMatchTicketState.Queued, CancellationToken.None);
            Assert.NotNull(committed);

            // 状态查询触发懒登记：陈年分配时刻随登记项入库，数值标识已分配。
            var status = await harness.Domain.GetStatusAsync(harness.ScopeOf(PlayerOne));
            Assert.True(status.IsSuccess, status.Message);
            Assert.True(status.Data.Matched);
            var staleMatchId = status.Data.MatchId;
            Assert.True(staleMatchId > 0);
            Assert.Equal(1, harness.Domain.RegistrationCount);

            // 二次访问（入场只查登记不懒登记）：清扫先移除过期项，查找落空 → 旧数值标识显式失效。
            var entered = await harness.Domain.EnterAsync(harness.ScopeOf(PlayerOne), staleMatchId);
            Assert.False(entered.IsSuccess);
            Assert.Equal(OnlineErrorCode.ResourceNotFound, entered.Code);
            Assert.Equal(0, harness.Domain.RegistrationCount);
        }

        [Fact]
        public async Task UnexpiredRegistration_ShouldSurviveSweepsAndKeepNumericMatchIdStable()
        {
            // 生命周期默认 7200 秒（新登记远未过期）+ 节流间隔 0（每次访问都清扫——最严苛的误删压力）。
            var harness = new Harness(new OnlineMatchRegistryOptions { RegistrationSweepIntervalSeconds = 0 });

            var first = await harness.Domain.EnqueueAsync(harness.ScopeOf(PlayerOne), 1, 1000);
            Assert.True(first.IsSuccess, first.Message);
            var second = await harness.Domain.EnqueueAsync(harness.ScopeOf(PlayerTwo), 1, 1000);
            Assert.True(second.IsSuccess, second.Message);

            // 定时批次撮合（GFX-1104）：入队只建票，撮合由协调器批量驱动；数值对局标识经状态解析惰性登记。
            var matched = await harness.Coordinator.RunOnceAsync(TenantId, AppId);
            Assert.True(matched.IsSuccess, matched.Message);
            var statusOne = await harness.Domain.GetStatusAsync(harness.ScopeOf(PlayerOne));
            Assert.True(statusOne.IsSuccess, statusOne.Message);
            Assert.True(statusOne.Data.Matched);
            var matchId = statusOne.Data.MatchId;
            Assert.True(matchId > 0);

            for (var round = 0; round < 3; round++)
            {
                var roundStatusOne = await harness.Domain.GetStatusAsync(harness.ScopeOf(PlayerOne));
                Assert.True(roundStatusOne.IsSuccess, roundStatusOne.Message);
                var roundStatusTwo = await harness.Domain.GetStatusAsync(harness.ScopeOf(PlayerTwo));
                Assert.True(roundStatusTwo.IsSuccess, roundStatusTwo.Message);

                Assert.True(roundStatusOne.Data.Matched);
                Assert.True(roundStatusTwo.Data.Matched);
                Assert.Equal(matchId, roundStatusOne.Data.MatchId);
                Assert.Equal(matchId, roundStatusTwo.Data.MatchId);
            }

            Assert.Equal(1, harness.Domain.RegistrationCount);
        }

        [Fact]
        public void OnlineMatchOptions_Validate_ShouldRejectNonPositiveRegistryValues()
        {
            var invalidOptions = new OnlineMatchOptions();
            invalidOptions.Registry.AssignmentRegistrationLifetimeSeconds = 0;
            invalidOptions.Registry.RegistrationSweepIntervalSeconds = -1;
            var invalidValidation = new CapabilityOptionsValidation();
            invalidOptions.Validate(invalidValidation);
            Assert.False(invalidValidation.IsValid);
            Assert.Contains(invalidValidation.Problems, problem => problem.Contains("Registry.AssignmentRegistrationLifetimeSeconds"));
            Assert.Contains(invalidValidation.Problems, problem => problem.Contains("Registry.RegistrationSweepIntervalSeconds"));

            var defaultOptions = new OnlineMatchOptions();
            var defaultValidation = new CapabilityOptionsValidation();
            defaultOptions.Validate(defaultValidation);
            Assert.True(defaultValidation.IsValid);
        }

        /// <summary>
        /// 测试基座：票据域（入队 / 撮合）+ 对局运行时 + 注入清扫参数的域服务。
        /// </summary>
        private sealed class Harness
        {
            public Harness(OnlineMatchRegistryOptions registryOptions)
            {
                Publisher = new OnlineEventRecorder();
                TicketStore = new InMemoryOnlineMatchTicketStore();
                var matchmakerOptions = new OnlineMatchmakerOptions();
                Tickets = new OnlineMatchTicketService(TicketStore, Publisher, matchmakerOptions);
                Coordinator = new OnlineMatchmakerCoordinator(TicketStore, Tickets, Publisher, matchmakerOptions);
                Runtime = new OnlineMatchRuntime(new InMemoryOnlineMatchActorStore(), Publisher, new OnlineMatchRuntimeOptions());
                var game = new OnlineRockPaperScissorsGame();
                Runtime.RegisterGame(game);
                Domain = new OnlineMatchDomainService(TicketStore, Tickets, Runtime, game, registryOptions);
            }

            public OnlineEventRecorder Publisher { get; }

            public InMemoryOnlineMatchTicketStore TicketStore { get; }

            public OnlineMatchTicketService Tickets { get; }

            public OnlineMatchmakerCoordinator Coordinator { get; }

            public OnlineMatchRuntime Runtime { get; }

            public OnlineMatchDomainService Domain { get; }

            public OnlineScope ScopeOf(long playerId)
            {
                return new OnlineScope(TenantId, AppId, ServerId, playerId);
            }
        }
    }
}
