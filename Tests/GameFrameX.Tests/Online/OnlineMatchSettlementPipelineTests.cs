//  ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and other rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the PRC and applicable international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please see the LICENSE file at the source code root for the full license text.
//   禁止利用本项目代码实施任何危害国家安全、破坏社会秩序、
//   侵犯其他个人或组织合法权益等法律法规禁止的行为！
//  ==========================================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.Assets;
using GameFrameX.Capability.Online.Match;
using GameFrameX.Capability.Online.Abstractions.Scope;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Idempotency;
using GameFrameX.Foundation.Idempotency;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 对局结算管线测试：Tick 驱动结算 + 持久化待发奖 outbox 投递、失败重投、租约接管恢复、
    /// 异常隔离、可空分发器、并发串行安全。
    /// </summary>
    public class OnlineMatchSettlementPipelineTests
    {
        /// <summary>租户标识。</summary>
        private const long TenantId = 1;

        /// <summary>App 标识。</summary>
        private const long AppId = 10;

        /// <summary>区服标识。</summary>
        private const long ServerId = 100;

        /// <summary>玩家一（胜方）。</summary>
        private const long PlayerOne = 1001;

        /// <summary>玩家二（负方）。</summary>
        private const long PlayerTwo = 1002;

        /// <summary>基准时刻（UTC 毫秒）。</summary>
        private const long Now = 1000000L;

        /// <summary>幂等记录默认保留时长（24 小时，对齐生产装配）。</summary>
        private const long DefaultIdempotencyRetentionMilliseconds = 86_400_000L;

        /// <summary>对局标识。</summary>
        private const string MatchId = "match-1";

        /// <summary>
        /// 验证完局后 Tick 完成结算（结果与待发奖记录原子落库）并发奖（样本奖励 100 / 10）。
        /// </summary>
        [Fact]
        public async Task TickAsync_AfterSettling_ShouldSettleAndGrant()
        {
            var harness = new Harness();
            await harness.PlayToSettlingAsync();

            await harness.Pipeline.TickAsync(Now + 100);

            Assert.Equal(OnlineMatchState.Completed, harness.Actor.State);
            Assert.Equal(OnlineRockPaperScissorsGame.WinnerRewardAmount, await harness.BalanceOfAsync(PlayerOne));
            Assert.Equal(OnlineRockPaperScissorsGame.ParticipantRewardAmount, await harness.BalanceOfAsync(PlayerTwo));
            var (_, retries, _, _) = await harness.Pipeline.QueryStatusAsync();
            Assert.Empty(retries);
        }

        /// <summary>
        /// 验证重复 Tick 不重复发奖（结算幂等回放 + 已成功玩家零重投）。
        /// </summary>
        [Fact]
        public async Task TickAsync_Twice_ShouldNotDoubleGrant()
        {
            var harness = new Harness();
            await harness.PlayToSettlingAsync();
            await harness.Pipeline.TickAsync(Now + 100);

            await harness.Pipeline.TickAsync(Now + 200);

            Assert.Equal(OnlineRockPaperScissorsGame.WinnerRewardAmount, await harness.BalanceOfAsync(PlayerOne));
            Assert.Equal(OnlineRockPaperScissorsGame.ParticipantRewardAmount, await harness.BalanceOfAsync(PlayerTwo));
        }

        /// <summary>
        /// 验证发奖失败进入待发奖记录，修复后下一 Tick 重投成功且不双发。
        /// </summary>
        [Fact]
        public async Task TickAsync_DispatchFailure_ShouldRetryUntilFixed()
        {
            var harness = new Harness(withFailingStore: true);
            await harness.PlayToSettlingAsync();

            await harness.Pipeline.TickAsync(Now + 100);

            // 结算已成立，但发奖全部失败：待发奖记录可见（每 Tick 一轮投递尝试）。
            Assert.Equal(OnlineMatchState.Completed, harness.Actor.State);
            Assert.Equal(0L, await harness.BalanceOfAsync(PlayerOne));
            var (settling, retries, _, _) = await harness.Pipeline.QueryStatusAsync();
            Assert.Empty(settling);
            Assert.Single(retries);
            Assert.Equal(2, retries[0].FailedPlayerIds.Count);
            Assert.Equal(1, retries[0].Attempts);

            // 修复资产存储后重投：成功且不双发。
            harness.AssetStore.FailureEnabled = false;
            await harness.Pipeline.TickAsync(Now + 200);

            Assert.Equal(OnlineRockPaperScissorsGame.WinnerRewardAmount, await harness.BalanceOfAsync(PlayerOne));
            Assert.Equal(OnlineRockPaperScissorsGame.ParticipantRewardAmount, await harness.BalanceOfAsync(PlayerTwo));
            var (_, retriesAfterFix, _, _) = await harness.Pipeline.QueryStatusAsync();
            Assert.Empty(retriesAfterFix);

            await harness.Pipeline.TickAsync(Now + 300);
            Assert.Equal(OnlineRockPaperScissorsGame.WinnerRewardAmount, await harness.BalanceOfAsync(PlayerOne));
            Assert.Equal(OnlineRockPaperScissorsGame.ParticipantRewardAmount, await harness.BalanceOfAsync(PlayerTwo));
        }

        /// <summary>
        /// 验证部分失败时只重投失败玩家（M79 回归）：已成功玩家的幂等记录过期后也不被再次发奖，
        /// 失败玩家集合不收窄波及成功玩家，且尝试达上限（默认 5 轮，含首发轮）转人工复核。
        /// </summary>
        [Fact]
        public async Task TickAsync_PartialFailure_ShouldRedispatchFailedPlayersOnly_NoDoubleGrantAfterIdempotencyExpiry()
        {
            var harness = new Harness(idempotencyRetentionMilliseconds: 100);
            harness.AssetStore.FailingPlayerIds.Add(PlayerTwo);
            await harness.PlayToSettlingAsync();

            await harness.Pipeline.TickAsync(Now + 100);

            // 玩家一首发成功、玩家二失败：待发奖记录只登记失败玩家。
            Assert.Equal(OnlineRockPaperScissorsGame.WinnerRewardAmount, await harness.BalanceOfAsync(PlayerOne));
            var (_, retries, _, _) = await harness.Pipeline.QueryStatusAsync();
            Assert.Single(retries);
            Assert.Equal(new List<long> { PlayerTwo }, retries[0].FailedPlayerIds);
            Assert.Equal(1, retries[0].Attempts);
            Assert.Equal(OnlineMatchSettlementRetryState.Pending, retries[0].State);

            // 越过幂等保留时长：玩家一的完成记录已过期——旧实现整份重投会在此后对其重复发奖。
            harness.Clock.Advance(1000);
            await harness.Pipeline.TickAsync(Now + 200);
            await harness.Pipeline.TickAsync(Now + 300);
            await harness.Pipeline.TickAsync(Now + 400);
            await harness.Pipeline.TickAsync(Now + 500);

            var (_, exhausted, _, _) = await harness.Pipeline.QueryStatusAsync();
            Assert.Single(exhausted);
            Assert.Equal(5, exhausted[0].Attempts);
            Assert.Equal(OnlineMatchSettlementRetryState.ManualReview, exhausted[0].State);
            Assert.Equal(new List<long> { PlayerTwo }, exhausted[0].FailedPlayerIds);

            // 玩家一恰好一份（幂等 TTL 已过期，任何重复投递都会翻倍）；玩家二未发。
            Assert.Equal(OnlineRockPaperScissorsGame.WinnerRewardAmount, await harness.BalanceOfAsync(PlayerOne));
            Assert.Equal(0L, await harness.BalanceOfAsync(PlayerTwo));

            // 人工复核冻结：后续 tick 不再自动重投。
            await harness.Pipeline.TickAsync(Now + 600);
            var (_, frozen, _, _) = await harness.Pipeline.QueryStatusAsync();
            Assert.Single(frozen);
            Assert.Equal(5, frozen[0].Attempts);
            Assert.Equal(OnlineMatchSettlementRetryState.ManualReview, frozen[0].State);
        }

        /// <summary>
        /// 验证全量失败达尝试上限后冻结为人工复核：修复资产存储也不再自动重投（人工裁决面语义）。
        /// </summary>
        [Fact]
        public async Task TickAsync_RetryExhausted_ShouldFreezeAtManualReview_AndStopAutoRedispatch()
        {
            var harness = new Harness(withFailingStore: true);
            await harness.PlayToSettlingAsync();

            await harness.Pipeline.TickAsync(Now + 100);
            await harness.Pipeline.TickAsync(Now + 200);
            await harness.Pipeline.TickAsync(Now + 300);
            await harness.Pipeline.TickAsync(Now + 400);
            await harness.Pipeline.TickAsync(Now + 500);

            var (_, retries, _, _) = await harness.Pipeline.QueryStatusAsync();
            Assert.Single(retries);
            Assert.Equal(5, retries[0].Attempts);
            Assert.Equal(OnlineMatchSettlementRetryState.ManualReview, retries[0].State);

            // 修复资产存储后仍不自动重投：人工复核条目脱离自动重试。
            harness.AssetStore.FailureEnabled = false;
            await harness.Pipeline.TickAsync(Now + 600);

            var (_, frozen, _, _) = await harness.Pipeline.QueryStatusAsync();
            Assert.Single(frozen);
            Assert.Equal(5, frozen[0].Attempts);
            Assert.Equal(OnlineMatchSettlementRetryState.ManualReview, frozen[0].State);
            Assert.Equal(0L, await harness.BalanceOfAsync(PlayerOne));
            Assert.Equal(0L, await harness.BalanceOfAsync(PlayerTwo));
        }

        /// <summary>
        /// 验证分发器缺席（assets 能力被过滤）时仅结算、不发奖、不建待发奖记录。
        /// </summary>
        [Fact]
        public async Task TickAsync_WithoutDispatcher_ShouldSettleOnly()
        {
            var harness = new Harness(withDispatcher: false);
            await harness.PlayToSettlingAsync();

            await harness.Pipeline.TickAsync(Now + 100);

            Assert.Equal(OnlineMatchState.Completed, harness.Actor.State);
            Assert.Equal(0L, await harness.BalanceOfAsync(PlayerOne));
            Assert.Equal(0L, await harness.BalanceOfAsync(PlayerTwo));
            var (_, retries, _, _) = await harness.Pipeline.QueryStatusAsync();
            Assert.Empty(retries);
        }

        /// <summary>
        /// 验证并发双 TickAsync 串行安全（互斥下只发一次奖）。
        /// </summary>
        [Fact]
        public async Task TickAsync_Concurrent_ShouldSerialize()
        {
            var harness = new Harness();
            await harness.PlayToSettlingAsync();

            await Task.WhenAll(harness.Pipeline.TickAsync(Now + 100), harness.Pipeline.TickAsync(Now + 101));

            Assert.Equal(OnlineRockPaperScissorsGame.WinnerRewardAmount, await harness.BalanceOfAsync(PlayerOne));
            Assert.Equal(OnlineRockPaperScissorsGame.ParticipantRewardAmount, await harness.BalanceOfAsync(PlayerTwo));
            var (_, retries, _, _) = await harness.Pipeline.QueryStatusAsync();
            Assert.Empty(retries);
        }

        /// <summary>
        /// 验证重投进行中并发 QueryStatusAsync 无集合并发修改异常且条目投影自洽。
        /// </summary>
        [Fact]
        public async Task TickAsync_WithPendingRetries_ConcurrentQueryStatus_ShouldNotThrow()
        {
            var harness = new Harness(withFailingStore: true, maxGrantAttempts: 64);
            await harness.PlayToSettlingAsync();
            await harness.Pipeline.TickAsync(Now + 100);
            var (_, seeded, _, _) = await harness.Pipeline.QueryStatusAsync();
            Assert.Single(seeded);

            // 多轮并发：重投 Tick 与 admin 查询同时读写待发奖存储。
            for (var round = 0; round < 10; round++)
            {
                var queries = new List<Task>(8);
                for (var i = 0; i < 8; i++)
                {
                    queries.Add(Task.Run(async () =>
                    {
                        var (_, retries, _, _) = await harness.Pipeline.QueryStatusAsync();
                        foreach (var entry in retries)
                        {
                            Assert.False(string.IsNullOrEmpty(entry.MatchResultId));
                            Assert.NotNull(entry.FailedPlayerIds);
                            Assert.True(entry.Attempts >= 1);
                        }
                    }));
                }

                await Task.WhenAll(harness.Pipeline.TickAsync(Now + 200 + round), Task.WhenAll(queries));
            }

            var (_, finalRetries, _, _) = await harness.Pipeline.QueryStatusAsync();
            Assert.Single(finalRetries);
            Assert.Equal(1 + 10, finalRetries[0].Attempts);
        }

        /// <summary>
        /// 验证存储层异常不再永久丢奖（GFX-1116 主回归）：领取抛异常时异常轮被隔离计数、
        /// 记录零状态变更；下一 Tick 重新领取投递，最终每人恰好一份奖励。
        /// </summary>
        [Fact]
        public async Task TickAsync_StoreThrowsDuringClaim_ShouldRecoverAndGrantExactlyOnce()
        {
            var harness = new Harness();
            await harness.PlayToSettlingAsync();

            // 第一轮：结算成功，领取待发奖记录时存储抛异常 → 异常被隔离，无奖励。
            harness.ResultStore.ThrowOnNextClaim = true;
            await harness.Pipeline.TickAsync(Now + 100);
            Assert.Equal(0L, await harness.BalanceOfAsync(PlayerOne));
            var (_, retries, settlementFailures, dispatchFailures) = await harness.Pipeline.QueryStatusAsync();
            Assert.Single(retries);
            Assert.Equal(0, settlementFailures);
            Assert.Equal(1, dispatchFailures);

            // 下一 Tick 恢复：重新领取投递成功，每人恰好一份，待发奖记录清空。
            await harness.Pipeline.TickAsync(Now + 200);
            Assert.Equal(OnlineRockPaperScissorsGame.WinnerRewardAmount, await harness.BalanceOfAsync(PlayerOne));
            Assert.Equal(OnlineRockPaperScissorsGame.ParticipantRewardAmount, await harness.BalanceOfAsync(PlayerTwo));
            var (_, recovered, _, _) = await harness.Pipeline.QueryStatusAsync();
            Assert.Empty(recovered);

            // 幂等兜底（已成功的玩家不重复入账）：后续 tick 零重投。
            await harness.Pipeline.TickAsync(Now + 300);
            Assert.Equal(OnlineRockPaperScissorsGame.WinnerRewardAmount, await harness.BalanceOfAsync(PlayerOne));
            Assert.Equal(OnlineRockPaperScissorsGame.ParticipantRewardAmount, await harness.BalanceOfAsync(PlayerTwo));
        }

        /// <summary>
        /// 验证模拟重启恢复（GFX-1116）：结算落库后重建全部服务（新运行时 + 新管线，沿用同一存储），
        /// 待发奖记录仍被扫描驱动补发，未发到的玩家获得奖励。
        /// </summary>
        [Fact]
        public async Task TickAsync_AfterSimulatedRestart_ShouldResumeUndeliveredRewards()
        {
            var harness = new Harness(withFailingStore: true);
            await harness.PlayToSettlingAsync();
            await harness.Pipeline.TickAsync(Now + 100);

            // 模拟崩溃：发奖全部失败后，仅「存储 + 资产域」存活，运行时 / 管线全部重建。
            harness.AssetStore.FailureEnabled = false;
            var restartedRuntime = new OnlineMatchRuntime(harness.MatchStore, harness.Publisher, new OnlineMatchRuntimeOptions());
            restartedRuntime.RegisterGame(new OnlineRockPaperScissorsGame());
            var restartedSettlement = new OnlineMatchSettlementService(restartedRuntime, harness.ResultStore, harness.Publisher);
            var restartedPipeline = new OnlineMatchSettlementPipeline(restartedRuntime, restartedSettlement, harness.ResultStore, harness.Dispatcher);

            await restartedPipeline.TickAsync(Now + 200);

            Assert.Equal(OnlineRockPaperScissorsGame.WinnerRewardAmount, await harness.BalanceOfAsync(PlayerOne));
            Assert.Equal(OnlineRockPaperScissorsGame.ParticipantRewardAmount, await harness.BalanceOfAsync(PlayerTwo));
            var (_, retries, _, _) = await restartedPipeline.QueryStatusAsync();
            Assert.Empty(retries);
        }

        /// <summary>
        /// 验证同一轮内单条投递异常不阻断其他对局（GFX-1116 逐记录隔离）：对局一的领取抛异常，
        /// 同轮对局二照常结算发奖；下一 Tick 对局一补发。
        /// </summary>
        [Fact]
        public async Task TickAsync_OneMatchStoreThrows_OtherMatchStillSettledAndGranted()
        {
            var harness = new Harness();
            await harness.PlayToSettlingAsync(MatchId, PlayerOne, PlayerTwo);
            await harness.PlayToSettlingAsync("match-2", 2001, 2002);
            harness.ResultStore.ThrowClaimForMatchId = MatchId;

            await harness.Pipeline.TickAsync(Now + 100);

            // 单条异常被隔离：同轮另一对局已发奖；失败计数暴露异常轮。
            var (_, _, _, dispatchFailures) = await harness.Pipeline.QueryStatusAsync();
            Assert.Equal(1, dispatchFailures);
            Assert.Equal(OnlineRockPaperScissorsGame.WinnerRewardAmount, await harness.BalanceOfAsync(2001));
            Assert.Equal(OnlineRockPaperScissorsGame.ParticipantRewardAmount, await harness.BalanceOfAsync(2002));

            // 解除注入后下一 Tick 对局一补发。
            harness.ResultStore.ThrowClaimForMatchId = null;
            await harness.Pipeline.TickAsync(Now + 200);
            Assert.Equal(OnlineRockPaperScissorsGame.WinnerRewardAmount, await harness.BalanceOfAsync(PlayerOne));
            Assert.Equal(OnlineRockPaperScissorsGame.ParticipantRewardAmount, await harness.BalanceOfAsync(PlayerTwo));
        }

        /// <summary>
        /// 测试基座：真实资产域（可注入失败 / 抛异常的存储装饰器）+ 对局域全栈。
        /// </summary>
        private sealed class Harness
        {
            /// <summary>
            /// 初始化测试基座。
            /// </summary>
            /// <param name="withFailingStore">是否装配可切换失败的资产存储。</param>
            /// <param name="withDispatcher">是否装配发奖分发器（false = 模拟 assets 能力缺席）。</param>
            /// <param name="idempotencyRetentionMilliseconds">幂等记录保留时长毫秒（默认 24 小时；缩短可模拟 TTL 过期）。</param>
            /// <param name="maxGrantAttempts">发奖投递尝试上限（null = 默认 5）。</param>
            /// <param name="claimLeaseMilliseconds">投递租约时长毫秒（默认 60 秒；缩短可模拟租约到期接管）。</param>
            public Harness(bool withFailingStore = false, bool withDispatcher = true, long idempotencyRetentionMilliseconds = DefaultIdempotencyRetentionMilliseconds, int? maxGrantAttempts = null, int claimLeaseMilliseconds = OnlineMatchSettlementOptions.DefaultGrantClaimLeaseMilliseconds)
            {
                AssetStore = new FailingAssetStore(new InMemoryOnlineAssetStore()) { FailureEnabled = withFailingStore };
                TransactionStore = new InMemoryOnlineAssetTransactionStore();
                Publisher = new OnlineEventRecorder();
                MatchStore = new InMemoryOnlineMatchActorStore();
                ResultStore = new ThrowingClaimSettlementStore(new InMemoryOnlineMatchSettlementStore());
                Runtime = new OnlineMatchRuntime(MatchStore, Publisher, new OnlineMatchRuntimeOptions());
                Runtime.RegisterGame(new OnlineRockPaperScissorsGame());

                OnlineGrantService grantService = null;
                if (withDispatcher)
                {
                    // 重试补齐依赖宿主把失败重放策略装配为 ReExecute（赛季/锦标赛结算重试测试同一装配项先例）。
                    var options = new IdempotencyOptions { ConcurrentWaitTimeoutMilliseconds = 1, FailedReplayPolicy = FailedReplayPolicy.ReExecute, RetentionMilliseconds = idempotencyRetentionMilliseconds };
                    Clock = new StepClock();
                    var coordinator = new IdempotencyCoordinator(new InMemoryIdempotencyStore(), Clock, options);
                    grantService = new OnlineGrantService(AssetStore, TransactionStore, new OnlineIdempotencyService(coordinator, null), Publisher);
                }

                Settlement = new OnlineMatchSettlementService(Runtime, ResultStore, Publisher, grantService != null);
                Dispatcher = grantService == null ? null : new OnlineMatchResultDispatcher(grantService);
                var settlementOptions = new OnlineMatchSettlementOptions { GrantClaimLeaseMilliseconds = claimLeaseMilliseconds };
                if (maxGrantAttempts.HasValue)
                {
                    settlementOptions.MaxGrantAttempts = maxGrantAttempts.Value;
                }

                Pipeline = new OnlineMatchSettlementPipeline(Runtime, Settlement, ResultStore, Dispatcher, settlementOptions);
            }

            /// <summary>获取资产存储（可切换失败 / 可按玩家注入失败 / 可注入抛异常）。</summary>
            public FailingAssetStore AssetStore
            {
                get;
            }

            /// <summary>获取幂等时钟（可外部跳变，用于模拟幂等记录过期）。</summary>
            public StepClock Clock
            {
                get;
            }

            /// <summary>获取资产流水存储。</summary>
            public InMemoryOnlineAssetTransactionStore TransactionStore
            {
                get;
            }

            /// <summary>获取事件记录桩。</summary>
            public OnlineEventRecorder Publisher
            {
                get;
            }

            /// <summary>获取对局存储。</summary>
            public InMemoryOnlineMatchActorStore MatchStore
            {
                get;
            }

            /// <summary>获取结算存储（结果 + 待发奖 outbox；可注入领取异常）。</summary>
            public ThrowingClaimSettlementStore ResultStore
            {
                get;
            }

            /// <summary>获取运行时。</summary>
            public OnlineMatchRuntime Runtime
            {
                get;
            }

            /// <summary>获取结算服务。</summary>
            public OnlineMatchSettlementService Settlement
            {
                get;
            }

            /// <summary>获取发奖分发器（可空）。</summary>
            public OnlineMatchResultDispatcher Dispatcher
            {
                get;
            }

            /// <summary>获取结算管线。</summary>
            public OnlineMatchSettlementPipeline Pipeline
            {
                get;
            }

            /// <summary>获取最近一次装载的对局 Actor。</summary>
            public OnlineMatchActor Actor
            {
                get;
                private set;
            }

            /// <summary>
            /// 查询玩家样本资产余额。
            /// </summary>
            /// <param name="playerId">玩家标识。</param>
            /// <returns>余额（无账户为 0）。</returns>
            public async Task<long> BalanceOfAsync(long playerId)
            {
                var wallet = await AssetStore.FindWalletAsync(TenantId, AppId, playerId, OnlineRockPaperScissorsGame.RewardAssetId);
                return wallet == null ? 0L : wallet.Balance;
            }

            /// <summary>
            /// 建局并打完一场对局（先出满两拳且全胜的一方 2:0 获胜），对局进入结算阶段。
            /// </summary>
            /// <param name="matchId">对局标识。</param>
            /// <param name="playerOne">玩家一（胜方）。</param>
            /// <param name="playerTwo">玩家二（负方）。</param>
            /// <returns>完成通知。</returns>
            public async Task PlayToSettlingAsync(string matchId = MatchId, long playerOne = PlayerOne, long playerTwo = PlayerTwo)
            {
                var assignment = new GameFrameX.Capability.Online.Matchmaking.OnlineMatchAssignment
                {
                    AssignmentId = "asg-" + matchId,
                    MatchId = matchId,
                    PlayerIds = new List<long> { playerOne, playerTwo },
                    Mode = OnlineRockPaperScissorsGame.MatchMode,
                    Region = 1,
                    CreatedAtTime = Now,
                    TicketIds = new List<string>(),
                    TenantId = TenantId,
                    AppId = AppId,
                    ServerId = ServerId,
                };
                var created = await Runtime.CreateFromAssignmentAsync(assignment, Now);
                Assert.True(created.IsSuccess, created.Message);
                Actor = await Runtime.ResolveActorAsync(TenantId, AppId, matchId);
                Assert.NotNull(Actor);

                await Actor.SetReadyAsync(ScopeOf(playerOne), true, Now);
                await Actor.SetReadyAsync(ScopeOf(playerTwo), true, Now + 1);
                Assert.True((await Actor.StartAsync(ScopeOf(playerOne), Now + 2)).IsSuccess);

                await SubmitAsync(matchId, playerOne, (int)OnlineRockPaperScissorsMove.Rock, Now + 10);
                await SubmitAsync(matchId, playerTwo, (int)OnlineRockPaperScissorsMove.Scissors, Now + 20);
                await SubmitAsync(matchId, playerOne, (int)OnlineRockPaperScissorsMove.Rock, Now + 30);
                await SubmitAsync(matchId, playerTwo, (int)OnlineRockPaperScissorsMove.Scissors, Now + 40);
                Assert.Equal(OnlineMatchState.Settling, Actor.State);
            }

            /// <summary>
            /// 提交一次出拳意图（客户端序号按提交次数递增）。
            /// </summary>
            /// <param name="matchId">对局标识。</param>
            /// <param name="playerId">玩家标识。</param>
            /// <param name="move">出拳。</param>
            /// <param name="nowUnixMilliseconds">当前时刻。</param>
            /// <returns>完成通知。</returns>
            private async Task SubmitAsync(string matchId, long playerId, int move, long nowUnixMilliseconds)
            {
                var key = matchId + ":" + playerId.ToString(System.Globalization.CultureInfo.InvariantCulture);
                _submitCounts[key] = _submitCounts.TryGetValue(key, out var count) ? count + 1 : 1;
                var input = new OnlineMatchInput
                {
                    PlayerId = playerId,
                    ClientSequence = _submitCounts[key],
                    ActionId = move,
                    ClientTime = nowUnixMilliseconds,
                };

                var ack = await Actor.SubmitInputAsync(ScopeOf(playerId), input, nowUnixMilliseconds);
                Assert.True(ack.IsSuccess, ack.Message);
                Assert.True(ack.Data.Accepted, ack.Data.Message);
            }

            /// <summary>每对局每玩家已提交次数（客户端序号派生源）。</summary>
            private readonly Dictionary<string, long> _submitCounts = new();

            /// <summary>
            /// 构造玩家级作用域。
            /// </summary>
            /// <param name="playerId">玩家标识。</param>
            /// <returns>作用域。</returns>
            private static OnlineScope ScopeOf(long playerId)
            {
                return new OnlineScope(TenantId, AppId, ServerId, playerId);
            }
        }

        /// <summary>
        /// 可注入领取异常的结算存储装饰器（GFX-1116 存储层异常注入：下一次 TryClaimAsync 抛异常后自复位）。
        /// </summary>
        private sealed class ThrowingClaimSettlementStore : IOnlineMatchSettlementStore
        {
            /// <summary>被装饰的真实存储。</summary>
            private readonly IOnlineMatchSettlementStore _inner;

            /// <summary>
            /// 初始化装饰器。
            /// </summary>
            /// <param name="inner">真实存储。</param>
            public ThrowingClaimSettlementStore(IOnlineMatchSettlementStore inner)
            {
                _inner = inner;
            }

            /// <summary>
            /// 获取或设置下一次领取是否抛异常（触发后自动复位；模拟存储层瞬态异常）。
            /// </summary>
            public bool ThrowOnNextClaim
            {
                get;
                set;
            }

            /// <summary>
            /// 获取或设置定向抛异常的对局标识（该对局的待发奖记录领取恒抛异常；null = 不注入）。
            /// </summary>
            public string ThrowClaimForMatchId
            {
                get;
                set;
            }

            /// <summary>最近一次到期扫描的 MatchResultId → MatchId 映射（定向异常注入的解析面）。</summary>
            private readonly Dictionary<string, string> _dueMatchIds = new();

            /// <summary>委托给内部存储。</summary>
            /// <param name="tenantId">租户标识。</param>
            /// <param name="appId">App 标识。</param>
            /// <param name="matchId">对局标识。</param>
            /// <param name="cancellationToken">取消令牌。</param>
            /// <returns>结果副本。</returns>
            public Task<OnlineMatchResult> FindByMatchAsync(long tenantId, long appId, string matchId, CancellationToken cancellationToken = default)
            {
                return _inner.FindByMatchAsync(tenantId, appId, matchId, cancellationToken);
            }

            /// <summary>委托给内部存储。</summary>
            /// <param name="result">待落定结果。</param>
            /// <param name="cancellationToken">取消令牌。</param>
            /// <returns>落定结果。</returns>
            public Task<OnlineMatchResult> CommitAsync(OnlineMatchResult result, CancellationToken cancellationToken = default)
            {
                return _inner.CommitAsync(result, cancellationToken);
            }

            /// <summary>委托给内部存储。</summary>
            /// <param name="result">待落定结果。</param>
            /// <param name="cancellationToken">取消令牌。</param>
            /// <returns>落定结果。</returns>
            public Task<OnlineMatchResult> CommitWithOutboxAsync(OnlineMatchResult result, CancellationToken cancellationToken = default)
            {
                return _inner.CommitWithOutboxAsync(result, cancellationToken);
            }

            /// <summary>委托给内部存储。</summary>
            /// <param name="nowUnixMilliseconds">当前时刻。</param>
            /// <param name="limit">批量上限。</param>
            /// <param name="cancellationToken">取消令牌。</param>
            /// <returns>到期记录列表。</returns>
            public async Task<IReadOnlyList<OnlineMatchRewardOutboxRecord>> FindDueAsync(long nowUnixMilliseconds, int limit, CancellationToken cancellationToken = default)
            {
                var due = await _inner.FindDueAsync(nowUnixMilliseconds, limit, cancellationToken).ConfigureAwait(false);
                _dueMatchIds.Clear();
                foreach (var record in due)
                {
                    if (record != null)
                    {
                        _dueMatchIds[record.MatchResultId] = record.MatchId;
                    }
                }

                return due;
            }

            /// <summary>委托给内部存储。</summary>
            /// <param name="matchResultId">结算结果标识。</param>
            /// <param name="cancellationToken">取消令牌。</param>
            /// <returns>记录副本。</returns>
            public Task<OnlineMatchRewardOutboxRecord> FindByMatchResultAsync(string matchResultId, CancellationToken cancellationToken = default)
            {
                return _inner.FindByMatchResultAsync(matchResultId, cancellationToken);
            }

            /// <summary>开启 <see cref="ThrowOnNextClaim"/> 时抛异常并复位，否则委托给内部存储。</summary>
            /// <param name="matchResultId">结算结果标识。</param>
            /// <param name="expectedRowVersion">期望行版本。</param>
            /// <param name="nowUnixMilliseconds">当前时刻。</param>
            /// <param name="leaseDurationMilliseconds">租约时长毫秒。</param>
            /// <param name="cancellationToken">取消令牌。</param>
            /// <returns>领取结果。</returns>
            public Task<bool> TryClaimAsync(string matchResultId, long expectedRowVersion, long nowUnixMilliseconds, long leaseDurationMilliseconds, CancellationToken cancellationToken = default)
            {
                if (ThrowOnNextClaim)
                {
                    ThrowOnNextClaim = false;
                    throw new InvalidOperationException("注入的领取异常（GFX-1116）");
                }

                if (!string.IsNullOrEmpty(ThrowClaimForMatchId) && _dueMatchIds.TryGetValue(matchResultId, out var dueMatchId) && dueMatchId == ThrowClaimForMatchId)
                {
                    throw new InvalidOperationException("注入的定向领取异常（GFX-1116）");
                }

                return _inner.TryClaimAsync(matchResultId, expectedRowVersion, nowUnixMilliseconds, leaseDurationMilliseconds, cancellationToken);
            }

            /// <summary>委托给内部存储。</summary>
            /// <param name="record">写回内容。</param>
            /// <param name="cancellationToken">取消令牌。</param>
            /// <returns>写回结果。</returns>
            public Task<bool> TryWriteBackAsync(OnlineMatchRewardOutboxRecord record, CancellationToken cancellationToken = default)
            {
                return _inner.TryWriteBackAsync(record, cancellationToken);
            }

            /// <summary>委托给内部存储。</summary>
            /// <param name="cancellationToken">取消令牌。</param>
            /// <returns>未完成条目投影。</returns>
            public Task<IReadOnlyList<OnlineMatchSettlementRetryEntry>> ListUndeliveredAsync(CancellationToken cancellationToken = default)
            {
                return _inner.ListUndeliveredAsync(cancellationToken);
            }
        }

        /// <summary>
        /// 可切换失败的资产存储装饰器（发奖失败 / 抛异常注入：ApplyAsync 按开关返回失败或抛异常）。
        /// </summary>
        private sealed class FailingAssetStore : IOnlineAssetStore
        {
            /// <summary>被装饰的真实存储。</summary>
            private readonly IOnlineAssetStore _inner;

            /// <summary>
            /// 初始化装饰器。
            /// </summary>
            /// <param name="inner">真实存储。</param>
            public FailingAssetStore(IOnlineAssetStore inner)
            {
                _inner = inner;
            }

            /// <summary>
            /// 获取或设置是否注入应用失败。
            /// </summary>
            public bool FailureEnabled
            {
                get;
                set;
            }

            /// <summary>
            /// 获取或设置剩余抛异常次数（> 0 时 ApplyAsync 直接抛异常并递减；用于投递异常注入）。
            /// </summary>
            public int ThrowRemainingCount
            {
                get;
                set;
            }

            /// <summary>
            /// 获取按玩家注入失败的玩家集合（命中的玩家应用直接失败，不受 <see cref="FailureEnabled"/> 影响）。
            /// </summary>
            public HashSet<long> FailingPlayerIds { get; } = new HashSet<long>();

            /// <summary>
            /// 查询钱包账户；原样委托给内部真实存储，不受失败注入影响。
            /// </summary>
            /// <param name="tenantId">租户标识。</param>
            /// <param name="appId">应用标识。</param>
            /// <param name="playerId">玩家标识。</param>
            /// <param name="currencyId">货币标识。</param>
            /// <param name="cancellationToken">取消令牌。</param>
            /// <returns>内部存储返回的钱包账户。</returns>
            public Task<OnlineWalletAccount> FindWalletAsync(long tenantId, long appId, long playerId, string currencyId, CancellationToken cancellationToken = default)
            {
                return _inner.FindWalletAsync(tenantId, appId, playerId, currencyId, cancellationToken);
            }

            /// <summary>
            /// 查询背包堆叠；原样委托给内部真实存储，不受失败注入影响。
            /// </summary>
            /// <param name="tenantId">租户标识。</param>
            /// <param name="appId">应用标识。</param>
            /// <param name="playerId">玩家标识。</param>
            /// <param name="itemId">物品标识。</param>
            /// <param name="cancellationToken">取消令牌。</param>
            /// <returns>内部存储返回的背包堆叠。</returns>
            public Task<OnlineInventoryStack> FindInventoryAsync(long tenantId, long appId, long playerId, string itemId, CancellationToken cancellationToken = default)
            {
                return _inner.FindInventoryAsync(tenantId, appId, playerId, itemId, cancellationToken);
            }

            /// <summary>
            /// 列出玩家全部钱包账户；原样委托给内部真实存储。
            /// </summary>
            /// <param name="tenantId">租户标识。</param>
            /// <param name="appId">应用标识。</param>
            /// <param name="playerId">玩家标识。</param>
            /// <param name="cancellationToken">取消令牌。</param>
            /// <returns>内部存储返回的钱包账户集合。</returns>
            public Task<IReadOnlyList<OnlineWalletAccount>> ListWalletAccountsAsync(long tenantId, long appId, long playerId, CancellationToken cancellationToken = default)
            {
                return _inner.ListWalletAccountsAsync(tenantId, appId, playerId, cancellationToken);
            }

            /// <summary>
            /// 列出玩家全部背包堆叠；原样委托给内部真实存储。
            /// </summary>
            /// <param name="tenantId">租户标识。</param>
            /// <param name="appId">应用标识。</param>
            /// <param name="playerId">玩家标识。</param>
            /// <param name="cancellationToken">取消令牌。</param>
            /// <returns>内部存储返回的背包堆叠集合。</returns>
            public Task<IReadOnlyList<OnlineInventoryStack>> ListInventoryStacksAsync(long tenantId, long appId, long playerId, CancellationToken cancellationToken = default)
            {
                return _inner.ListInventoryStacksAsync(tenantId, appId, playerId, cancellationToken);
            }

            /// <summary>
            /// 应用资产变更批次；<see cref="ThrowRemainingCount"/> 大于 0 时抛异常（投递异常注入），
            /// 开启 <c>FailureEnabled</c> 或批次归属玩家命中 <c>FailingPlayerIds</c> 时不触达内部存储，
            /// 直接返回 <c>InternalError</c> 失败结果（无流水），否则委托给内部真实存储。
            /// </summary>
            /// <param name="batch">资产变更批次。</param>
            /// <param name="cancellationToken">取消令牌。</param>
            /// <returns>注入的失败结果或内部存储的应用结果。</returns>
            public Task<OnlineAssetApplyResult> ApplyAsync(OnlineAssetChangeBatch batch, CancellationToken cancellationToken = default)
            {
                if (FailureEnabled || FailingPlayerIds.Contains(batch.PlayerId))
                {
                    return Task.FromResult(new OnlineAssetApplyResult(false, OnlineErrorCode.InternalError, "注入的发奖失败", new List<OnlineLedgerEntry>()));
                }

                return _inner.ApplyAsync(batch, cancellationToken);
            }

            /// <summary>
            /// 分页查询资产流水；原样委托给内部真实存储。
            /// </summary>
            /// <param name="query">流水分页查询条件。</param>
            /// <param name="cancellationToken">取消令牌。</param>
            /// <returns>内部存储返回的流水集合。</returns>
            public Task<IReadOnlyList<OnlineLedgerEntry>> ListLedgerEntriesAsync(OnlineLedgerPageQuery query, CancellationToken cancellationToken = default)
            {
                return _inner.ListLedgerEntriesAsync(query, cancellationToken);
            }

            /// <summary>
            /// 按事务标识查询资产流水；原样委托给内部真实存储，供断言注入失败后未落流水。
            /// </summary>
            /// <param name="transactionId">事务标识。</param>
            /// <param name="cancellationToken">取消令牌。</param>
            /// <returns>内部存储返回的流水集合。</returns>
            public Task<IReadOnlyList<OnlineLedgerEntry>> FindLedgerEntriesByTransactionIdAsync(string transactionId, CancellationToken cancellationToken = default)
            {
                return _inner.FindLedgerEntriesByTransactionIdAsync(transactionId, cancellationToken);
            }

            /// <summary>
            /// 按资产汇总玩家流水增量；原样委托给内部真实存储。
            /// </summary>
            /// <param name="tenantId">租户标识。</param>
            /// <param name="appId">应用标识。</param>
            /// <param name="playerId">玩家标识。</param>
            /// <param name="cancellationToken">取消令牌。</param>
            /// <returns>资产标识到增量合计的映射。</returns>
            public Task<IReadOnlyDictionary<string, long>> SumLedgerDeltasByAssetAsync(long tenantId, long appId, long playerId, CancellationToken cancellationToken = default)
            {
                return _inner.SumLedgerDeltasByAssetAsync(tenantId, appId, playerId, cancellationToken);
            }

            /// <summary>
            /// 列出拥有资产记录的玩家标识；原样委托给内部真实存储。
            /// </summary>
            /// <param name="tenantId">租户标识。</param>
            /// <param name="appId">应用标识。</param>
            /// <param name="cancellationToken">取消令牌。</param>
            /// <returns>内部存储返回的玩家标识集合。</returns>
            public Task<IReadOnlyList<long>> ListPlayerIdsAsync(long tenantId, long appId, CancellationToken cancellationToken = default)
            {
                return _inner.ListPlayerIdsAsync(tenantId, appId, cancellationToken);
            }
        }

        /// <summary>
        /// 步进时钟桩（沿用资产域测试约定：每次读取推进 10ms）。
        /// </summary>
        private sealed class StepClock : IClock
        {
            /// <summary>当前时刻。</summary>
            private long _nowMilliseconds;

            /// <summary>
            /// 一次性推进时钟（模拟时间流逝，如幂等记录过期）。
            /// </summary>
            /// <param name="deltaMilliseconds">推进毫秒数。</param>
            public void Advance(long deltaMilliseconds)
            {
                _nowMilliseconds += deltaMilliseconds;
            }

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
