// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
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
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.Collections.Generic;
using System.Threading.Tasks;
using GameFrameX.Foundation.Idempotency;
using GameFrameX.Online.Assets;
using GameFrameX.Online.Contracts;
using GameFrameX.Online.Events;
using GameFrameX.Online.Idempotency;
using GameFrameX.Online.Leaderboard;
using GameFrameX.Online.Match;
using GameFrameX.Online.Matchmaking;
using GameFrameX.Online.Scope;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 可信结果写榜链路测试（vault:C8 S7.2 / VC-7.1 / VC-7.2 / VC-7.4 / VC-7.14 服务端半边）：
    /// 载荷零信任（伪造 / 篡改拒绝）、重复投递幂等、防刷（分数上限 + 高频限流）逐玩家隔离、跨 App 拒绝。
    /// </summary>
    public class OnlineLeaderboardTrustedResultTests
    {
        /// <summary>租户标识。</summary>
        private const long TenantId = 1;

        /// <summary>App 标识。</summary>
        private const long AppId = 10;

        /// <summary>另一 App 标识（跨 App 用例，VC-7.14）。</summary>
        private const long OtherAppId = 11;

        /// <summary>区服标识。</summary>
        private const long ServerId = 100;

        /// <summary>玩家一。</summary>
        private const long PlayerOne = 1001;

        /// <summary>玩家二。</summary>
        private const long PlayerTwo = 1002;

        /// <summary>基准时刻（UTC 毫秒）。</summary>
        private const long Now = 1000000L;

        /// <summary>对局标识（真实对局链路用）。</summary>
        private const string MatchId = "match-1";

        /// <summary>
        /// 验证 VC-7.1 可信半边：真实结算结果经投影器落榜，分数来源与追溯键完整，事件发布。
        /// </summary>
        [Fact]
        public async Task ProjectAsync_FromRealSettledMatch_ShouldWriteTrustedScores()
        {
            var harness = new Harness();
            await harness.PlayToSettlingAsync();
            var settled = await harness.Settlement.SettleAsync(TenantId, AppId, MatchId, Now + 100);
            Assert.True(settled.IsSuccess, settled.Message);

            await harness.CreateBoardAsync("board-trust", OnlineLeaderboardScoreUpdatePolicy.Sum);

            var projected = await harness.Projector.ProjectAsync(harness.Scope(), "board-trust", MatchId, settled.Data.Result.MatchResultId, Now + 200);

            Assert.True(projected.IsSuccess, projected.Message);
            Assert.Equal(2, projected.Data.AppliedCount);
            Assert.Equal(0, projected.Data.ReplayCount);
            Assert.Empty(projected.Data.FailedPlayers);

            foreach (var entry in settled.Data.Result.Entries)
            {
                var boardEntry = await harness.LeaderboardStore.FindEntryAsync(TenantId, AppId, "board-trust", entry.PlayerId);
                Assert.NotNull(boardEntry);
                Assert.Equal(entry.Score, boardEntry.Score);
                Assert.Equal(OnlineLeaderboardScoreSource.MatchResult, boardEntry.SourceKind);
                Assert.Equal(settled.Data.Result.MatchResultId, boardEntry.SourceMatchResultId);
            }

            Assert.Equal(2, harness.Recorder.Filter(OnlineLeaderboardEvents.ScoreUpdated).Count);
            Assert.Single(harness.Recorder.Filter(OnlineLeaderboardEvents.LeaderboardCreated));
        }

        /// <summary>
        /// 验证 VC-7.1 不可信半边：伪造对局 / 伪造结果标识被拒绝，篡改载荷无从落榜。
        /// </summary>
        [Fact]
        public async Task ProjectAsync_ForgedMatchOrResultId_ShouldBeRejected()
        {
            var harness = new Harness();
            await harness.CreateBoardAsync("board-trust", OnlineLeaderboardScoreUpdatePolicy.Sum);

            // 伪造从未结算的对局标识：结算事实不存在。
            var forgedMatch = await harness.Projector.ProjectAsync(harness.Scope(), "board-trust", "match-forged", "mrs-forged", Now + 100);

            Assert.False(forgedMatch.IsSuccess);
            Assert.Equal(OnlineErrorCode.ResourceNotFound, forgedMatch.Code);

            // 真实结算 + 篡改结果标识（载荷与结算事实不一致）。
            await harness.CommitResultAsync("match-real", "mrs-real", (PlayerOne, 100));
            var tampered = await harness.Projector.ProjectAsync(harness.Scope(), "board-trust", "match-real", "mrs-tampered", Now + 110);

            Assert.False(tampered.IsSuccess);
            Assert.Equal(OnlineErrorCode.RiskControlRejected, tampered.Code);
            Assert.Null(await harness.LeaderboardStore.FindEntryAsync(TenantId, AppId, "board-trust", PlayerOne));
            Assert.Empty(harness.Recorder.Filter(OnlineLeaderboardEvents.ScoreUpdated));

            // 目标榜单不存在同样拒绝。
            var noBoard = await harness.Projector.ProjectAsync(harness.Scope(), "board-none", "match-real", "mrs-real", Now + 120);

            Assert.False(noBoard.IsSuccess);
            Assert.Equal(OnlineErrorCode.ResourceNotFound, noBoard.Code);
        }

        /// <summary>
        /// 验证 VC-7.2：同结果事件重复投递 3 次只计一次（首次落榜，后续全部幂等回放）。
        /// </summary>
        [Fact]
        public async Task ProjectAsync_RedeliveredThrice_ShouldCountOnce()
        {
            var harness = new Harness();
            await harness.CreateBoardAsync("board-idem", OnlineLeaderboardScoreUpdatePolicy.Sum);
            await harness.CommitResultAsync("match-idem", "mrs-idem", (PlayerOne, 10), (PlayerTwo, 20));

            var first = await harness.Projector.ProjectAsync(harness.Scope(), "board-idem", "match-idem", "mrs-idem", Now + 100);
            var second = await harness.Projector.ProjectAsync(harness.Scope(), "board-idem", "match-idem", "mrs-idem", Now + 200);
            var third = await harness.Projector.ProjectAsync(harness.Scope(), "board-idem", "match-idem", "mrs-idem", Now + 300);

            Assert.True(first.IsSuccess, first.Message);
            Assert.Equal(2, first.Data.AppliedCount);
            Assert.Equal(0, first.Data.ReplayCount);
            Assert.True(second.IsSuccess, second.Message);
            Assert.Equal(0, second.Data.AppliedCount);
            Assert.Equal(2, second.Data.ReplayCount);
            Assert.True(third.IsSuccess, third.Message);
            Assert.Equal(0, third.Data.AppliedCount);
            Assert.Equal(2, third.Data.ReplayCount);

            Assert.Equal(10, (await harness.LeaderboardStore.FindEntryAsync(TenantId, AppId, "board-idem", PlayerOne)).Score);
            Assert.Equal(20, (await harness.LeaderboardStore.FindEntryAsync(TenantId, AppId, "board-idem", PlayerTwo)).Score);
            Assert.Equal(1, (await harness.LeaderboardStore.FindEntryAsync(TenantId, AppId, "board-idem", PlayerOne)).SubmissionCount);
            Assert.Equal(2, harness.Recorder.Filter(OnlineLeaderboardEvents.ScoreUpdated).Count);
        }

        /// <summary>
        /// 验证 VC-7.4 防刷半边一：远超合理范围的分数被风控拒绝，同结果正常玩家不受影响。
        /// </summary>
        [Fact]
        public async Task ProjectAsync_AbnormalScore_ShouldRejectPlayerOnly()
        {
            var harness = new Harness(defaultMaxScorePerSubmission: 1000);
            await harness.CreateBoardAsync("board-abuse", OnlineLeaderboardScoreUpdatePolicy.Sum);
            await harness.CommitResultAsync("match-abuse", "mrs-abuse", (PlayerOne, 50), (PlayerTwo, 9999999));

            var projected = await harness.Projector.ProjectAsync(harness.Scope(), "board-abuse", "match-abuse", "mrs-abuse", Now + 100);

            Assert.True(projected.IsSuccess, projected.Message);
            Assert.Equal(1, projected.Data.AppliedCount);
            Assert.Single(projected.Data.FailedPlayers);
            Assert.Equal(PlayerTwo, projected.Data.FailedPlayers[0].PlayerId);
            Assert.Equal(OnlineErrorCode.RiskControlRejected, projected.Data.FailedPlayers[0].Code);

            Assert.Equal(50, (await harness.LeaderboardStore.FindEntryAsync(TenantId, AppId, "board-abuse", PlayerOne)).Score);
            Assert.Null(await harness.LeaderboardStore.FindEntryAsync(TenantId, AppId, "board-abuse", PlayerTwo));
            Assert.Single(harness.Recorder.Filter(OnlineLeaderboardEvents.ScoreUpdated));
        }

        /// <summary>
        /// 验证 VC-7.4 防刷半边二：高频提交被限流拒绝；窗口滚动后恢复，正常节奏不受永久影响。
        /// </summary>
        [Fact]
        public async Task ProjectAsync_HighFrequency_ShouldRateLimitThenRecover()
        {
            var harness = new Harness(rateLimitMaxSubmissions: 2, rateLimitWindowSeconds: 1);
            await harness.CreateBoardAsync("board-rate", OnlineLeaderboardScoreUpdatePolicy.Sum);
            await harness.CommitResultAsync("match-r1", "mrs-r1", (PlayerOne, 1));
            await harness.CommitResultAsync("match-r2", "mrs-r2", (PlayerOne, 2));
            await harness.CommitResultAsync("match-r3", "mrs-r3", (PlayerOne, 3));
            await harness.CommitResultAsync("match-r4", "mrs-r4", (PlayerOne, 4));

            var first = await harness.Projector.ProjectAsync(harness.Scope(), "board-rate", "match-r1", "mrs-r1", Now + 100);
            var second = await harness.Projector.ProjectAsync(harness.Scope(), "board-rate", "match-r2", "mrs-r2", Now + 200);
            var third = await harness.Projector.ProjectAsync(harness.Scope(), "board-rate", "match-r3", "mrs-r3", Now + 300);

            Assert.True(first.IsSuccess, first.Message);
            Assert.Equal(1, first.Data.AppliedCount);
            Assert.True(second.IsSuccess, second.Message);
            Assert.Equal(1, second.Data.AppliedCount);
            Assert.True(third.IsSuccess, third.Message);
            Assert.Equal(0, third.Data.AppliedCount);
            Assert.Single(third.Data.FailedPlayers);
            Assert.Equal(OnlineErrorCode.RateLimitExceeded, third.Data.FailedPlayers[0].Code);
            Assert.Equal(3, (await harness.LeaderboardStore.FindEntryAsync(TenantId, AppId, "board-rate", PlayerOne)).Score);

            // 窗口滚动后恢复正常（限流是速率保护不是配额）。
            var fourth = await harness.Projector.ProjectAsync(harness.Scope(), "board-rate", "match-r4", "mrs-r4", Now + 2000);

            Assert.True(fourth.IsSuccess, fourth.Message);
            Assert.Equal(1, fourth.Data.AppliedCount);
            Assert.Equal(7, (await harness.LeaderboardStore.FindEntryAsync(TenantId, AppId, "board-rate", PlayerOne)).Score);
        }

        /// <summary>
        /// 验证 VC-7.14 投影半边：跨 App 作用域无法把结果投影到他 App 榜单（与不存在同构拒绝）。
        /// </summary>
        [Fact]
        public async Task ProjectAsync_CrossAppBoard_ShouldBeResourceNotFound()
        {
            var harness = new Harness();
            await harness.CommitResultAsync("match-x", "mrs-x", (PlayerOne, 100));

            var boardInOtherApp = await harness.Service.CreateAsync(harness.Scope(OtherAppId), new OnlineLeaderboardCreateRequest { LeaderboardId = "board-other-app" }, Now);
            Assert.True(boardInOtherApp.IsSuccess, boardInOtherApp.Message);

            var projected = await harness.Projector.ProjectAsync(harness.Scope(), "board-other-app", "match-x", "mrs-x", Now + 100);

            Assert.False(projected.IsSuccess);
            Assert.Equal(OnlineErrorCode.ResourceNotFound, projected.Code);
            Assert.Empty(harness.Recorder.Filter(OnlineLeaderboardEvents.ScoreUpdated));
        }

        /// <summary>
        /// 验证 Best 策略经可信链路多笔结果只保留最优分（分数来源始终为最新可信结果）。
        /// </summary>
        [Fact]
        public async Task ProjectAsync_BestPolicy_ShouldKeepBestAcrossResults()
        {
            var harness = new Harness();
            await harness.CreateBoardAsync("board-best", OnlineLeaderboardScoreUpdatePolicy.Best);
            await harness.CommitResultAsync("match-b1", "mrs-b1", (PlayerOne, 100));
            await harness.CommitResultAsync("match-b2", "mrs-b2", (PlayerOne, 80));
            await harness.CommitResultAsync("match-b3", "mrs-b3", (PlayerOne, 150));

            await harness.Projector.ProjectAsync(harness.Scope(), "board-best", "match-b1", "mrs-b1", Now + 100);
            await harness.Projector.ProjectAsync(harness.Scope(), "board-best", "match-b2", "mrs-b2", Now + 200);
            await harness.Projector.ProjectAsync(harness.Scope(), "board-best", "match-b3", "mrs-b3", Now + 300);

            var entry = await harness.LeaderboardStore.FindEntryAsync(TenantId, AppId, "board-best", PlayerOne);

            Assert.Equal(150, entry.Score);
            Assert.Equal(3, entry.SubmissionCount);
            Assert.Equal("mrs-b3", entry.SourceMatchResultId);
        }

        /// <summary>
        /// 测试基座：结算存储 + 排行榜全栈 + 真实幂等协调器；可选真实对局链路（RPS 样例玩法）。
        /// </summary>
        private sealed class Harness
        {
            /// <summary>
            /// 初始化测试基座（真实 Foundation 幂等协调器 + C93 Online 幂等服务；防刷阈值在构造前注入）。
            /// </summary>
            /// <param name="defaultMaxScorePerSubmission">单次分数上限（0 = 默认）。</param>
            /// <param name="rateLimitMaxSubmissions">限流窗口次数（0 = 默认）。</param>
            /// <param name="rateLimitWindowSeconds">限流窗口秒数（0 = 默认）。</param>
            public Harness(long defaultMaxScorePerSubmission = 0, int rateLimitMaxSubmissions = 0, int rateLimitWindowSeconds = 0)
            {
                Options = new OnlineLeaderboardOptions { CacheEnabled = false };
                if (defaultMaxScorePerSubmission > 0)
                {
                    Options.DefaultMaxScorePerSubmission = defaultMaxScorePerSubmission;
                }

                if (rateLimitMaxSubmissions > 0)
                {
                    Options.RateLimitMaxSubmissions = rateLimitMaxSubmissions;
                }

                if (rateLimitWindowSeconds > 0)
                {
                    Options.RateLimitWindowSeconds = rateLimitWindowSeconds;
                }

                Recorder = new OnlineEventRecorder();
                IdempotencyStore = new InMemoryIdempotencyStore();
                var coordinator = new IdempotencyCoordinator(IdempotencyStore, new StepClock(), new IdempotencyOptions { ConcurrentWaitTimeoutMilliseconds = 1 });
                IdempotencyService = new OnlineIdempotencyService(coordinator, null);
                LeaderboardStore = new InMemoryOnlineLeaderboardStore();
                Service = new OnlineLeaderboardService(LeaderboardStore, Options, Recorder);
                ResultStore = new InMemoryOnlineMatchResultStore();
                Projector = new OnlineLeaderboardResultProjector(ResultStore, Service, IdempotencyService);
            }

            /// <summary>获取组件选项（构造时已固化）。</summary>
            public OnlineLeaderboardOptions Options
            {
                get;
            }

            /// <summary>获取事件记录桩。</summary>
            public OnlineEventRecorder Recorder
            {
                get;
            }

            /// <summary>获取幂等存储。</summary>
            public InMemoryIdempotencyStore IdempotencyStore
            {
                get;
            }

            /// <summary>获取 Online 幂等服务。</summary>
            public OnlineIdempotencyService IdempotencyService
            {
                get;
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

            /// <summary>获取可信结果投影器。</summary>
            public OnlineLeaderboardResultProjector Projector
            {
                get;
            }

            /// <summary>获取真实对局链路的结算服务（<see cref="PlayToSettlingAsync"/> 后有效）。</summary>
            public OnlineMatchSettlementService Settlement
            {
                get;
                private set;
            }

            /// <summary>
            /// 构造作用域。
            /// </summary>
            /// <param name="appId">App 标识（默认本 App）。</param>
            /// <param name="playerId">玩家位（默认无主体）。</param>
            /// <returns>作用域。</returns>
            public OnlineScope Scope(long appId = AppId, long playerId = 0)
            {
                return new OnlineScope(TenantId, appId, ServerId, playerId);
            }

            /// <summary>
            /// 创建榜单（Sum/Best 等策略按用例选择）。
            /// </summary>
            /// <param name="leaderboardId">榜单标识。</param>
            /// <param name="policy">累计策略。</param>
            /// <returns>完成通知。</returns>
            public async Task CreateBoardAsync(string leaderboardId, OnlineLeaderboardScoreUpdatePolicy policy)
            {
                var created = await Service.CreateAsync(Scope(), new OnlineLeaderboardCreateRequest { LeaderboardId = leaderboardId, ScoreUpdatePolicy = policy }, Now);
                Assert.True(created.IsSuccess, created.Message);
            }

            /// <summary>
            /// 直接向结算存储落一份服务端形态的结果（模拟已结算事实；条目分数由用例指定）。
            /// </summary>
            /// <param name="matchId">对局标识。</param>
            /// <param name="matchResultId">结果标识。</param>
            /// <param name="entries">条目（玩家, 分数）序列。</param>
            /// <returns>完成通知。</returns>
            public async Task CommitResultAsync(string matchId, string matchResultId, params (long PlayerId, long Score)[] entries)
            {
                var resultEntries = new List<OnlineMatchResultEntry>();
                foreach (var item in entries)
                {
                    resultEntries.Add(new OnlineMatchResultEntry { PlayerId = item.PlayerId, Rank = resultEntries.Count + 1, IsWinner = resultEntries.Count == 0, Score = item.Score, Rewards = new List<OnlineAssetChangeLine>() });
                }

                var committed = await ResultStore.CommitAsync(new OnlineMatchResult
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
            /// 建局并推进到结算阶段（玩家一 2:0 获胜；真实对局链路用例）。
            /// </summary>
            /// <returns>完成通知。</returns>
            public async Task PlayToSettlingAsync()
            {
                var matchStore = new InMemoryOnlineMatchActorStore();
                var runtime = new OnlineMatchRuntime(matchStore, Recorder);
                runtime.RegisterGame(new OnlineRockPaperScissorsGame());
                Settlement = new OnlineMatchSettlementService(runtime, ResultStore, Recorder);

                var assignment = new OnlineMatchAssignment
                {
                    AssignmentId = "asg-1",
                    MatchId = MatchId,
                    PlayerIds = new List<long> { PlayerOne, PlayerTwo },
                    Mode = OnlineRockPaperScissorsGame.MatchMode,
                    Region = 1,
                    CreatedAtTime = Now,
                    TicketIds = new List<string>(),
                    TenantId = TenantId,
                    AppId = AppId,
                    ServerId = ServerId,
                };
                var created = await runtime.CreateFromAssignmentAsync(assignment, Now);
                Assert.True(created.IsSuccess, created.Message);
                var actor = await runtime.ResolveActorAsync(TenantId, AppId, MatchId);
                Assert.NotNull(actor);

                await actor.SetReadyAsync(new OnlineScope(TenantId, AppId, ServerId, PlayerOne), true, Now);
                await actor.SetReadyAsync(new OnlineScope(TenantId, AppId, ServerId, PlayerTwo), true, Now + 1);
                var started = await actor.StartAsync(new OnlineScope(TenantId, AppId, ServerId, PlayerOne), Now + 2);
                Assert.True(started.IsSuccess, started.Message);

                await SubmitAsync(actor, PlayerOne, 1, OnlineRockPaperScissorsMove.Rock, Now + 10);
                await SubmitAsync(actor, PlayerTwo, 1, OnlineRockPaperScissorsMove.Scissors, Now + 20);
                await SubmitAsync(actor, PlayerOne, 2, OnlineRockPaperScissorsMove.Rock, Now + 30);
                await SubmitAsync(actor, PlayerTwo, 2, OnlineRockPaperScissorsMove.Scissors, Now + 40);
                Assert.Equal(OnlineMatchState.Settling, actor.State);
            }

            /// <summary>
            /// 提交一次出拳意图。
            /// </summary>
            /// <param name="actor">对局 Actor。</param>
            /// <param name="playerId">玩家标识。</param>
            /// <param name="clientSequence">客户端序号。</param>
            /// <param name="move">出拳。</param>
            /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒）。</param>
            /// <returns>完成通知。</returns>
            private static async Task SubmitAsync(OnlineMatchActor actor, long playerId, int clientSequence, OnlineRockPaperScissorsMove move, long nowUnixMilliseconds)
            {
                var input = new OnlineMatchInput
                {
                    PlayerId = playerId,
                    ClientSequence = clientSequence,
                    ActionId = (int)move,
                    ClientTime = nowUnixMilliseconds,
                };

                var ack = await actor.SubmitInputAsync(new OnlineScope(TenantId, AppId, ServerId, playerId), input, nowUnixMilliseconds);
                Assert.True(ack.IsSuccess, ack.Message);
                Assert.True(ack.Data.Accepted, ack.Data.Message);
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
