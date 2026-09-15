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
using GameFrameX.Foundation.Idempotency;
using GameFrameX.Online.Assets;
using GameFrameX.Online.Contracts;
using GameFrameX.Online.Events;
using GameFrameX.Online.Idempotency;
using GameFrameX.Online.Match;
using GameFrameX.Online.Matchmaking;
using GameFrameX.Online.Scope;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 对局结算与结果分发测试（vault:C6 S5.7 / S5.8 / VC-5.8 / VC-5.9）：
    /// 一局一个结果、可信结果事件、逐玩家幂等发奖。
    /// </summary>
    public class OnlineMatchSettlementTests
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

        /// <summary>对局标识。</summary>
        private const string MatchId = "match-1";

        /// <summary>
        /// 验证 VC-5.8 / VC-5.9 全链路：结算产出唯一结果 → 逐玩家发奖 → 可信结果事件。
        /// </summary>
        [Fact]
        public async Task SettleAndDispatch_ShouldProduceSingleResultAndGrantOnce()
        {
            var harness = new Harness();
            await harness.PlayToSettlingAsync();

            var settled = await harness.Settlement.SettleAsync(TenantId, AppId, MatchId, Now + 100);

            Assert.True(settled.IsSuccess, settled.Message);
            Assert.False(settled.Data.IsReplay);
            Assert.Equal(OnlineMatchState.Completed, settled.Data.State);
            Assert.Equal(OnlineMatchState.Completed, harness.Actor.State);

            var result = settled.Data.Result;
            Assert.False(string.IsNullOrEmpty(result.MatchResultId));
            Assert.Equal(2, result.Entries.Count);

            var winner = result.Entries.Find(item => item.PlayerId == PlayerOne);
            var loser = result.Entries.Find(item => item.PlayerId == PlayerTwo);
            Assert.True(winner.IsWinner);
            Assert.False(loser.IsWinner);
            Assert.Equal(OnlineRockPaperScissorsGame.WinnerRewardAmount, winner.Rewards[0].Amount);
            Assert.Equal(OnlineRockPaperScissorsGame.ParticipantRewardAmount, loser.Rewards[0].Amount);

            // 可信结果事件必须已发布（下游排行/通知只消费它）。
            Assert.NotEmpty(harness.Publisher.Filter(OnlineMatchRuntimeEvents.MatchSettled));

            var dispatched = await harness.Dispatcher.DispatchAsync(result);

            Assert.True(dispatched.IsSuccess, dispatched.Message);
            Assert.Equal(2, dispatched.Data.SucceededCount);
            Assert.Empty(dispatched.Data.FailedPlayerIds);

            var winnerWallet = await harness.AssetStore.FindWalletAsync(TenantId, AppId, PlayerOne, OnlineRockPaperScissorsGame.RewardAssetId);
            var loserWallet = await harness.AssetStore.FindWalletAsync(TenantId, AppId, PlayerTwo, OnlineRockPaperScissorsGame.RewardAssetId);
            Assert.Equal(OnlineRockPaperScissorsGame.WinnerRewardAmount, winnerWallet.Balance);
            Assert.Equal(OnlineRockPaperScissorsGame.ParticipantRewardAmount, loserWallet.Balance);

            // 重投同一份结果：幂等回放，余额不变（VC-5.9）。
            var replayed = await harness.Dispatcher.DispatchAsync(result);

            Assert.True(replayed.IsSuccess, replayed.Message);
            Assert.Equal(2, replayed.Data.ReplayCount);
            Assert.Equal(OnlineRockPaperScissorsGame.WinnerRewardAmount, (await harness.AssetStore.FindWalletAsync(TenantId, AppId, PlayerOne, OnlineRockPaperScissorsGame.RewardAssetId)).Balance);
            Assert.Equal(OnlineRockPaperScissorsGame.ParticipantRewardAmount, (await harness.AssetStore.FindWalletAsync(TenantId, AppId, PlayerTwo, OnlineRockPaperScissorsGame.RewardAssetId)).Balance);
        }

        /// <summary>
        /// 验证 VC-5.8：重复结算不产生第二份结果，且第二次为幂等回放。
        /// </summary>
        [Fact]
        public async Task SettleAsync_Twice_ShouldReplaySameResult()
        {
            var harness = new Harness();
            await harness.PlayToSettlingAsync();

            var first = await harness.Settlement.SettleAsync(TenantId, AppId, MatchId, Now + 100);
            var second = await harness.Settlement.SettleAsync(TenantId, AppId, MatchId, Now + 200);

            Assert.True(first.IsSuccess, first.Message);
            Assert.True(second.IsSuccess, second.Message);
            Assert.False(first.Data.IsReplay);
            Assert.True(second.Data.IsReplay);
            Assert.Equal(first.Data.Result.MatchResultId, second.Data.Result.MatchResultId);
            Assert.Single(harness.Publisher.Filter(OnlineMatchRuntimeEvents.MatchSettled));
        }

        /// <summary>
        /// 验证 VC-5.8：未进入结算阶段不得结算。
        /// </summary>
        [Fact]
        public async Task SettleAsync_BeforeSettling_ShouldBeStateNotReady()
        {
            var harness = new Harness();
            await harness.StartRunningAsync();

            var settled = await harness.Settlement.SettleAsync(TenantId, AppId, MatchId, Now + 100);

            Assert.False(settled.IsSuccess);
            Assert.Equal(OnlineErrorCode.StateNotReady, settled.Code);
        }

        /// <summary>
        /// 验证 VC-5.8：结算结果存储按 (租户, App, 对局) 唯一——并发提交只保留首份。
        /// </summary>
        [Fact]
        public async Task ResultStore_ConcurrentCommit_ShouldKeepFirstResult()
        {
            var store = new InMemoryOnlineMatchResultStore();
            var first = new OnlineMatchResult { MatchResultId = "mrs-first", MatchId = MatchId, TenantId = TenantId, AppId = AppId, Entries = new List<OnlineMatchResultEntry>() };
            var second = new OnlineMatchResult { MatchResultId = "mrs-second", MatchId = MatchId, TenantId = TenantId, AppId = AppId, Entries = new List<OnlineMatchResultEntry>() };

            var committedFirst = await store.CommitAsync(first);
            var committedSecond = await store.CommitAsync(second);

            Assert.Equal("mrs-first", committedFirst.MatchResultId);
            Assert.Equal("mrs-first", committedSecond.MatchResultId);
            Assert.Equal("mrs-first", (await store.FindByMatchAsync(TenantId, AppId, MatchId)).MatchResultId);
        }

        /// <summary>
        /// 验证 S5.8：无奖励条目的结果不触发任何发奖（空结果不得空转）。
        /// </summary>
        [Fact]
        public async Task DispatchAsync_WithoutRewards_ShouldNotGrant()
        {
            var harness = new Harness();
            var result = new OnlineMatchResult
            {
                MatchResultId = "mrs-empty",
                MatchId = MatchId,
                TenantId = TenantId,
                AppId = AppId,
                ServerId = ServerId,
                Entries = new List<OnlineMatchResultEntry>
                {
                    new OnlineMatchResultEntry { PlayerId = PlayerOne, Rank = 1, IsWinner = true, Rewards = new List<OnlineAssetChangeLine>() },
                },
            };

            var dispatched = await harness.Dispatcher.DispatchAsync(result);

            Assert.True(dispatched.IsSuccess, dispatched.Message);
            Assert.Equal(0, dispatched.Data.SucceededCount);
            Assert.Null(await harness.AssetStore.FindWalletAsync(TenantId, AppId, PlayerOne, OnlineRockPaperScissorsGame.RewardAssetId));
        }

        /// <summary>
        /// 按分配建局并推进到结算阶段（玩家一三局两胜）。
        /// </summary>
        /// <param name="playerIds">玩家集合。</param>
        /// <returns>分配实例。</returns>
        private static OnlineMatchAssignment BuildAssignment(params long[] playerIds)
        {
            return new OnlineMatchAssignment
            {
                AssignmentId = "asg-1",
                MatchId = MatchId,
                PlayerIds = new List<long>(playerIds),
                Mode = OnlineRockPaperScissorsGame.MatchMode,
                Region = 1,
                CreatedAtTime = Now,
                TicketIds = new List<string>(),
                TenantId = TenantId,
                AppId = AppId,
                ServerId = ServerId,
            };
        }

        /// <summary>
        /// 测试基座：对局存储 + 结算存储 + 资产域全栈（真实幂等协调器）。
        /// </summary>
        private sealed class Harness
        {
            /// <summary>
            /// 初始化测试基座。
            /// </summary>
            public Harness()
            {
                AssetStore = new InMemoryOnlineAssetStore();
                TransactionStore = new InMemoryOnlineAssetTransactionStore();
                IdempotencyStore = new InMemoryIdempotencyStore();
                Publisher = new OnlineEventRecorder();
                MatchStore = new InMemoryOnlineMatchActorStore();
                ResultStore = new InMemoryOnlineMatchResultStore();
                Runtime = new OnlineMatchRuntime(MatchStore, Publisher);
                Runtime.RegisterGame(new OnlineRockPaperScissorsGame());

                var options = new IdempotencyOptions { ConcurrentWaitTimeoutMilliseconds = 1 };
                var coordinator = new IdempotencyCoordinator(IdempotencyStore, new StepClock(), options);
                GrantService = new OnlineGrantService(AssetStore, TransactionStore, new OnlineIdempotencyService(coordinator, null), Publisher);
                Dispatcher = new OnlineMatchResultDispatcher(GrantService);
                Settlement = new OnlineMatchSettlementService(Runtime, ResultStore, Publisher);
            }

            /// <summary>获取资产存储。</summary>
            public InMemoryOnlineAssetStore AssetStore
            {
                get;
            }

            /// <summary>获取资产流水存储。</summary>
            public InMemoryOnlineAssetTransactionStore TransactionStore
            {
                get;
            }

            /// <summary>获取幂等存储。</summary>
            public InMemoryIdempotencyStore IdempotencyStore
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

            /// <summary>获取结算结果存储。</summary>
            public InMemoryOnlineMatchResultStore ResultStore
            {
                get;
            }

            /// <summary>获取运行时。</summary>
            public OnlineMatchRuntime Runtime
            {
                get;
            }

            /// <summary>获取发奖入口。</summary>
            public OnlineGrantService GrantService
            {
                get;
            }

            /// <summary>获取结果分发器。</summary>
            public OnlineMatchResultDispatcher Dispatcher
            {
                get;
            }

            /// <summary>获取结算服务。</summary>
            public OnlineMatchSettlementService Settlement
            {
                get;
            }

            /// <summary>获取已装载的对局 Actor。</summary>
            public OnlineMatchActor Actor
            {
                get;
                private set;
            }

            /// <summary>
            /// 建局并推进到运行阶段。
            /// </summary>
            /// <returns>完成通知。</returns>
            public async Task StartRunningAsync()
            {
                var created = await Runtime.CreateFromAssignmentAsync(BuildAssignment(PlayerOne, PlayerTwo), Now);
                Assert.True(created.IsSuccess, created.Message);
                Actor = await Runtime.ResolveActorAsync(TenantId, AppId, MatchId);
                Assert.NotNull(Actor);

                await Actor.SetReadyAsync(ScopeOf(PlayerOne), true, Now);
                await Actor.SetReadyAsync(ScopeOf(PlayerTwo), true, Now + 1);
                var started = await Actor.StartAsync(ScopeOf(PlayerOne), Now + 2);
                Assert.True(started.IsSuccess, started.Message);
            }

            /// <summary>
            /// 打完一场对局（玩家一 2:0 获胜），对局进入结算阶段。
            /// </summary>
            /// <returns>完成通知。</returns>
            public async Task PlayToSettlingAsync()
            {
                await StartRunningAsync();
                await SubmitAsync(PlayerOne, 1, OnlineRockPaperScissorsMove.Rock, Now + 10);
                await SubmitAsync(PlayerTwo, 1, OnlineRockPaperScissorsMove.Scissors, Now + 20);
                await SubmitAsync(PlayerOne, 2, OnlineRockPaperScissorsMove.Rock, Now + 30);
                await SubmitAsync(PlayerTwo, 2, OnlineRockPaperScissorsMove.Scissors, Now + 40);
                Assert.Equal(OnlineMatchState.Settling, Actor.State);
            }

            /// <summary>
            /// 提交一次出拳意图。
            /// </summary>
            /// <param name="playerId">玩家标识。</param>
            /// <param name="clientSequence">客户端序号。</param>
            /// <param name="move">出拳。</param>
            /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒）。</param>
            /// <returns>完成通知。</returns>
            private async Task SubmitAsync(long playerId, int clientSequence, OnlineRockPaperScissorsMove move, long nowUnixMilliseconds)
            {
                var input = new OnlineMatchInput
                {
                    PlayerId = playerId,
                    ClientSequence = clientSequence,
                    ActionId = (int)move,
                    ClientTime = nowUnixMilliseconds,
                };

                var ack = await Actor.SubmitInputAsync(ScopeOf(playerId), input, nowUnixMilliseconds);
                Assert.True(ack.IsSuccess, ack.Message);
                Assert.True(ack.Data.Accepted, ack.Data.Message);
            }

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
        /// 步进时钟桩（沿用资产域测试约定：每次读取推进 10ms，保证幂等记录有过期判定余地）。
        /// </summary>
        private sealed class StepClock : IClock
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
