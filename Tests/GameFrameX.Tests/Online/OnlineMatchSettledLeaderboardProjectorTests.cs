// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the PRC and relevant international regulations.
//   使用本项目须严格遵守相关法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with relevant laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file at the root of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://www.cnb.cool/GameFrameX
//   CNB Repository: https://www.cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================
using System.Threading.Tasks;
using GameFrameX.Foundation.Idempotency;
using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Events;
using GameFrameX.Capability.Online.Abstractions.Idempotency;
using GameFrameX.Capability.Online.Competition;
using GameFrameX.Capability.Online.Competition.Leaderboard;
using GameFrameX.Capability.Online.Match;
using GameFrameX.Capability.Online.Abstractions.Scope;
using GameFrameX.Capability.Online.Assets;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// Match.Settled → 排行榜投影生产接线测试：
    /// 订阅消费（EventId 去重 + 幂等键回放双保险）、失败待重试 / 重投补齐、结果不存在终态移除、模块装配探测。
    /// </summary>
    public class OnlineMatchSettledLeaderboardProjectorTests
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

        /// <summary>基准时刻（UTC 毫秒）。</summary>
        private const long Now = 1000000L;

        /// <summary>目标榜单标识。</summary>
        private const string BoardId = "board-match-settled";

        /// <summary>
        /// 验证订阅半边：结算事件驱动投影落榜（Top 查询可见分数），同 EventId 重复投递被去重。
        /// </summary>
        [Fact]
        public async Task HandleEventAsync_SettledEvent_ShouldProjectAndDeduplicateByEventId()
        {
            var harness = new Harness();
            await harness.CreateBoardAsync();
            await harness.CommitResultAsync("match-1", "mrs-1", (PlayerOne, 30), (PlayerTwo, 10));

            var settledEvent = harness.CreateSettledEvent("mrs-1", "match-1", (PlayerOne, 30), (PlayerTwo, 10));
            await harness.Projector.HandleEventAsync(settledEvent);
            // 同 EventId 重复投递：EventId 去重直接短路。
            await harness.Projector.HandleEventAsync(settledEvent);

            Assert.Equal(30, await harness.ScoreOfAsync(PlayerOne));
            Assert.Equal(10, await harness.ScoreOfAsync(PlayerTwo));
            Assert.Equal(1, (await harness.LeaderboardStore.FindEntryAsync(TenantId, AppId, BoardId, PlayerOne)).SubmissionCount);
            Assert.Empty(harness.Projector.QueryStatus());
        }

        /// <summary>
        /// 验证幂等键回放半边：不同 EventId 同 MatchResultId 重投只回放不入榜（不双计）。
        /// </summary>
        [Fact]
        public async Task HandleEventAsync_DifferentEventIdSameMatchResult_ShouldReplayWithoutDoubleCount()
        {
            var harness = new Harness();
            await harness.CreateBoardAsync();
            await harness.CommitResultAsync("match-2", "mrs-2", (PlayerOne, 7));

            await harness.Projector.HandleEventAsync(harness.CreateSettledEvent("mrs-2", "match-2", (PlayerOne, 7)));
            await harness.Projector.HandleEventAsync(harness.CreateSettledEvent("mrs-2", "match-2", (PlayerOne, 7)));

            var entry = await harness.LeaderboardStore.FindEntryAsync(TenantId, AppId, BoardId, PlayerOne);
            Assert.Equal(7, entry.Score);
            Assert.Equal(1, entry.SubmissionCount);
            Assert.Empty(harness.Projector.QueryStatus());
        }

        /// <summary>
        /// 验证失败半边：榜单不存在 → 待重试可见；创建榜单后 RetryPendingAsync 补投成功且不双计。
        /// </summary>
        [Fact]
        public async Task RetryPendingAsync_BoardCreatedLater_ShouldBackfillWithoutDoubleCount()
        {
            var harness = new Harness();
            await harness.CommitResultAsync("match-3", "mrs-3", (PlayerOne, 50));

            await harness.Projector.HandleEventAsync(harness.CreateSettledEvent("mrs-3", "match-3", (PlayerOne, 50)));

            var pending = harness.Projector.QueryStatus();
            Assert.Single(pending);
            Assert.Equal("mrs-3", pending[0].MatchResultId);
            Assert.Equal("match-3", pending[0].MatchId);
            Assert.Equal(1, pending[0].Attempts);

            await harness.CreateBoardAsync();
            await harness.Projector.RetryPendingAsync(CancellationToken.None);
            // 重投后再次手动重投：幂等键回放，不双计。
            await harness.Projector.RetryPendingAsync(CancellationToken.None);

            var entry = await harness.LeaderboardStore.FindEntryAsync(TenantId, AppId, BoardId, PlayerOne);
            Assert.NotNull(entry);
            Assert.Equal(50, entry.Score);
            Assert.Equal(1, entry.SubmissionCount);
            Assert.Empty(harness.Projector.QueryStatus());
        }

        /// <summary>
        /// 验证终态半边：结果不存在（已被清理）的待重试条目在重投时被移除，不再滞留。
        /// </summary>
        [Fact]
        public async Task RetryPendingAsync_ResultMissing_ShouldDropPendingEntry()
        {
            var harness = new Harness();
            await harness.CreateBoardAsync();

            // 结果从未落库（等价已被清理）：投影失败进待重试。
            await harness.Projector.HandleEventAsync(harness.CreateSettledEvent("mrs-gone", "match-gone", (PlayerOne, 5)));
            Assert.Single(harness.Projector.QueryStatus());

            await harness.Projector.RetryPendingAsync(CancellationToken.None);

            Assert.Empty(harness.Projector.QueryStatus());
            Assert.Null(await harness.LeaderboardStore.FindEntryAsync(TenantId, AppId, BoardId, PlayerOne));
        }

        /// <summary>
        /// 验证异常吞没：载荷损坏的事件不向订阅方抛异常，也不进待重试（不可解析 = 无从重投）。
        /// </summary>
        [Fact]
        public async Task HandleEventAsync_MalformedPayload_ShouldSwallowWithoutThrowing()
        {
            var harness = new Harness();
            await harness.CreateBoardAsync();

            var malformed = harness.CreateSettledEvent("mrs-x", "match-x", (PlayerOne, 1));
            malformed.Payload = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };

            await harness.Projector.HandleEventAsync(malformed);

            Assert.Empty(harness.Projector.QueryStatus());
        }

        /// <summary>
        /// 验证模块装配半边一：组合根预置投影选项 + 结果存储 + 幂等服务 + 事件总线 → 投影器装配，
        /// 结算事件经总线订阅链真实落榜，重试 tick job 注册。
        /// </summary>
        [Fact]
        public async Task ModuleRegister_WithProjectionOptions_ShouldAssembleProjectorAndSubscribe()
        {
            var eventBus = new InMemoryEventPublisher();
            var resultStore = new InMemoryOnlineMatchResultStore();
            var coordinator = new IdempotencyCoordinator(new InMemoryIdempotencyStore(), new Harness.StepClock(), new IdempotencyOptions { ConcurrentWaitTimeoutMilliseconds = 1 });
            var idempotency = new OnlineIdempotencyService(coordinator, null);
            var context = new CapabilityRegistrationContext(new CapabilityManifest("test-competition", "1.0.0", new string[] { }, new string[] { }));
            context.AddService<InMemoryEventPublisher>(eventBus);
            context.AddService<IOnlineEventPublisher>(new OnlineEventPublisher(eventBus));
            context.AddService(idempotency);
            context.AddService<IOnlineMatchResultStore>(resultStore);
            context.AddService(new OnlineLeaderboardProjectionOptions { MatchSettledBoardId = BoardId });

            new OnlineCompetitionCapabilityModule().Register(context);

            var projector = context.Services.GetService<OnlineMatchSettledLeaderboardProjector>();
            Assert.NotNull(projector);
            Assert.Contains(context.TickJobs, job => job.Name.EndsWith("leaderboard-projection-retry", System.StringComparison.Ordinal));

            var leaderboards = context.Services.GetService<OnlineLeaderboardService>();
            var created = await leaderboards.CreateAsync(new OnlineScope(TenantId, AppId, ServerId), new OnlineLeaderboardCreateRequest { LeaderboardId = BoardId, ScoreUpdatePolicy = OnlineLeaderboardScoreUpdatePolicy.Sum }, Now);
            Assert.True(created.IsSuccess, created.Message);

            await CommitResultAsync(resultStore, "match-mod", "mrs-mod", (PlayerOne, 42));
            var settledEvent = OnlineMatchRuntimeEvents.CreateMatchSettled(TenantId, AppId, ServerId, "mrs-mod", "match-mod", OnlineRockPaperScissorsGame.MatchMode, 1, OnlineMatchState.Completed.ToString(), Now, new (long PlayerId, int Rank, bool IsWinner, long Score, int RewardCount)[] { (PlayerOne, 1, true, 42, 0) });
            await eventBus.PublishAsync(OnlineEventEnvelopeMapper.ToEnvelope(settledEvent));

            var entry = await context.Services.GetService<IOnlineLeaderboardStore>().FindEntryAsync(TenantId, AppId, BoardId, PlayerOne);
            Assert.NotNull(entry);
            Assert.Equal(42, entry.Score);
        }

        /// <summary>
        /// 验证模块装配半边二：未预置投影选项（或 board id 空白）→ 不装配投影器也不报错。
        /// </summary>
        [Fact]
        public void ModuleRegister_WithoutProjectionOptions_ShouldSkipProjector()
        {
            var context = new CapabilityRegistrationContext(new CapabilityManifest("test-competition", "1.0.0", new string[] { }, new string[] { }));
            context.AddService<InMemoryEventPublisher>(new InMemoryEventPublisher());
            context.AddService<IOnlineMatchResultStore>(new InMemoryOnlineMatchResultStore());

            context.AddService<IOnlineEventPublisher>(new OnlineEventPublisher(new InMemoryEventPublisher()));
            new OnlineCompetitionCapabilityModule().Register(context);

            Assert.False(context.Services.TryGetService(out OnlineMatchSettledLeaderboardProjector _));
            Assert.DoesNotContain(context.TickJobs, job => job.Name.EndsWith("leaderboard-projection-retry", System.StringComparison.Ordinal));
        }

        /// <summary>
        /// 验证依赖声明：online-competition 声明依赖 online-match，而 online-match 不依赖 online-competition（拓扑无环）。
        /// </summary>
        [Fact]
        public void ManifestDependency_ShouldBeAcyclic()
        {
            Assert.Contains("online-match", OnlineCompetitionCapabilityModule.ManifestInstance.Requires);
            Assert.DoesNotContain("online-competition", OnlineMatchCapabilityModule.ManifestInstance.Requires);
        }

        /// <summary>
        /// 直接向结算存储落一份服务端形态的结果（模拟已结算事实）。
        /// </summary>
        /// <param name="resultStore">结算结果存储。</param>
        /// <param name="matchId">对局标识。</param>
        /// <param name="matchResultId">结果标识。</param>
        /// <param name="entries">条目（玩家, 分数）序列。</param>
        /// <returns>完成通知。</returns>
        private static async Task CommitResultAsync(InMemoryOnlineMatchResultStore resultStore, string matchId, string matchResultId, params (long PlayerId, long Score)[] entries)
        {
            var resultEntries = new List<OnlineMatchResultEntry>();
            foreach (var item in entries)
            {
                resultEntries.Add(new OnlineMatchResultEntry { PlayerId = item.PlayerId, Rank = resultEntries.Count + 1, IsWinner = resultEntries.Count == 0, Score = item.Score, Rewards = new List<OnlineAssetChangeLine>() });
            }

            var committed = await resultStore.CommitAsync(new OnlineMatchResult
            {
                MatchResultId = matchResultId,
                MatchId = matchId,
                TenantId = TenantId,
                AppId = AppId,
                ServerId = ServerId,
                Mode = OnlineRockPaperScissorsGame.MatchMode,
                Region = 1,
                Outcome = OnlineMatchState.Completed,
                Entries = resultEntries,
                SettledTime = Now,
            });
            Assert.Equal(matchResultId, committed.MatchResultId);
        }

        /// <summary>
        /// 测试基座：结算存储 + 排行榜全栈 + 真实幂等协调器 + 被测投影器。
        /// </summary>
        private sealed class Harness
        {
            /// <summary>
            /// 初始化测试基座。
            /// </summary>
            public Harness()
            {
                LeaderboardStore = new InMemoryOnlineLeaderboardStore();
                Service = new OnlineLeaderboardService(LeaderboardStore, new OnlineLeaderboardOptions { CacheEnabled = false }, null);
                ResultStore = new InMemoryOnlineMatchResultStore();
                var coordinator = new IdempotencyCoordinator(new InMemoryIdempotencyStore(), new StepClock(), new IdempotencyOptions { ConcurrentWaitTimeoutMilliseconds = 1 });
                IdempotencyService = new OnlineIdempotencyService(coordinator, null);
                Projector = new OnlineMatchSettledLeaderboardProjector(ResultStore, Service, IdempotencyService, BoardId);
            }

            /// <summary>获取排行榜存储。</summary>
            public InMemoryOnlineLeaderboardStore LeaderboardStore
            {
                get;
            }

            /// <summary>获取排行榜服务。</summary>
            public OnlineLeaderboardService Service
            {
                get;
            }

            /// <summary>获取对局结算结果存储（可信事实来源）。</summary>
            public InMemoryOnlineMatchResultStore ResultStore
            {
                get;
            }

            /// <summary>获取 Online 幂等服务。</summary>
            public OnlineIdempotencyService IdempotencyService
            {
                get;
            }

            /// <summary>获取被测投影器。</summary>
            public OnlineMatchSettledLeaderboardProjector Projector
            {
                get;
            }

            /// <summary>
            /// 创建目标榜单（Sum 策略）。
            /// </summary>
            /// <returns>完成通知。</returns>
            public async Task CreateBoardAsync()
            {
                var created = await Service.CreateAsync(new OnlineScope(TenantId, AppId, ServerId), new OnlineLeaderboardCreateRequest { LeaderboardId = BoardId, ScoreUpdatePolicy = OnlineLeaderboardScoreUpdatePolicy.Sum }, Now);
                Assert.True(created.IsSuccess, created.Message);
            }

            /// <summary>
            /// 直接向结算存储落一份服务端形态的结果。
            /// </summary>
            /// <param name="matchId">对局标识。</param>
            /// <param name="matchResultId">结果标识。</param>
            /// <param name="entries">条目（玩家, 分数）序列。</param>
            /// <returns>完成通知。</returns>
            public async Task CommitResultAsync(string matchId, string matchResultId, params (long PlayerId, long Score)[] entries)
            {
                await OnlineMatchSettledLeaderboardProjectorTests.CommitResultAsync(ResultStore, matchId, matchResultId, entries);
            }

            /// <summary>
            /// 构造一个 Match.Settled 事件（载荷与结算事实一致由用例保证）。
            /// </summary>
            /// <param name="matchResultId">结果标识。</param>
            /// <param name="matchId">对局标识。</param>
            /// <param name="entries">条目（玩家, 分数）序列。</param>
            /// <returns>事件实例。</returns>
            public OnlineEvent CreateSettledEvent(string matchResultId, string matchId, params (long PlayerId, long Score)[] entries)
            {
                var payloadEntries = new List<(long PlayerId, int Rank, bool IsWinner, long Score, int RewardCount)>();
                foreach (var item in entries)
                {
                    payloadEntries.Add((item.PlayerId, payloadEntries.Count + 1, payloadEntries.Count == 0, item.Score, 0));
                }

                return OnlineMatchRuntimeEvents.CreateMatchSettled(TenantId, AppId, ServerId, matchResultId, matchId, OnlineRockPaperScissorsGame.MatchMode, 1, OnlineMatchState.Completed.ToString(), Now, payloadEntries);
            }

            /// <summary>
            /// 读取玩家当前榜上分数。
            /// </summary>
            /// <param name="playerId">玩家标识。</param>
            /// <returns>分数。</returns>
            public async Task<long> ScoreOfAsync(long playerId)
            {
                var entry = await LeaderboardStore.FindEntryAsync(TenantId, AppId, BoardId, playerId);
                Assert.NotNull(entry);
                return entry.Score;
            }

            /// <summary>
            /// 步进时钟桩（沿用资产域测试约定：每次读取推进 10ms）。
            /// </summary>
            public sealed class StepClock : IClock
            {
                /// <summary>当前时刻。</summary>
                private long _nowMilliseconds;

                /// <summary>
                /// 获取当前时刻（每次读取推进 10ms）。
                /// </summary>
                public long UtcNowTime
                {
                    get
                    {
                        _nowMilliseconds += 10;
                        return _nowMilliseconds;
                    }
                }
            }
        }
    }
}
