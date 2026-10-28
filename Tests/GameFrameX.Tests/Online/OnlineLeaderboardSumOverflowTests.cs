// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and other related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with relevant laws, regulations, and open-source licenses.
//   本项目基于 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file at the root of the source repository for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others as prohibited by law!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any disputes or liabilities arising from secondary development based on this project,
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer and project contributors.
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
using GameFrameX.Capability.Online.Assets;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Events;
using GameFrameX.Capability.Online.Abstractions.Idempotency;
using GameFrameX.Capability.Online.Competition.Leaderboard;
using GameFrameX.Capability.Online.Match;
using GameFrameX.Capability.Online.Abstractions.Scope;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// Sum 策略分数累加溢出回归（GFX-1113 / C403）：
    /// 溢出拒绝（正向 / 负向）、条目零状态变更、零事件副作用；正常累加不受影响。
    /// </summary>
    public class OnlineLeaderboardSumOverflowTests
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

        /// <summary>
        /// 验证正向溢出：榜上 long.MaxValue-1 再提交 2 被拒绝，分数与提交计数保持原值，无事件副作用。
        /// </summary>
        [Fact]
        public async Task SumOverflow_Positive_ShouldRejectAndKeepEntry()
        {
            var harness = new Harness();
            await harness.CreateBoardAsync("board-ovf-pos", OnlineLeaderboardScoreUpdatePolicy.Sum);
            await harness.CommitResultAsync("match-p1", "mrs-p1", (PlayerOne, long.MaxValue - 1));
            await harness.Projector.ProjectAsync(harness.Scope(), "board-ovf-pos", "match-p1", "mrs-p1", Now + 100);

            await harness.CommitResultAsync("match-p2", "mrs-p2", (PlayerOne, 2));
            var projected = await harness.Projector.ProjectAsync(harness.Scope(), "board-ovf-pos", "match-p2", "mrs-p2", Now + 200);

            Assert.True(projected.IsSuccess, projected.Message);
            Assert.Equal(0, projected.Data.AppliedCount);
            Assert.Single(projected.Data.FailedPlayers);
            Assert.Equal(PlayerOne, projected.Data.FailedPlayers[0].PlayerId);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, projected.Data.FailedPlayers[0].Code);

            var entry = await harness.LeaderboardStore.FindEntryAsync(TenantId, AppId, "board-ovf-pos", PlayerOne);
            Assert.Equal(long.MaxValue - 1, entry.Score);
            Assert.Equal(1, entry.SubmissionCount);
            Assert.Equal("mrs-p1", entry.SourceMatchResultId);
            Assert.Single(harness.Recorder.Filter(OnlineLeaderboardEvents.ScoreUpdated));
        }

        /// <summary>
        /// 验证负向溢出：榜上 long.MinValue+1 再提交 -2 被拒绝，分数不变。
        /// </summary>
        [Fact]
        public async Task SumOverflow_Negative_ShouldRejectAndKeepEntry()
        {
            var harness = new Harness();
            await harness.CreateBoardAsync("board-ovf-neg", OnlineLeaderboardScoreUpdatePolicy.Sum);
            await harness.CommitResultAsync("match-n1", "mrs-n1", (PlayerOne, long.MinValue + 1));
            await harness.Projector.ProjectAsync(harness.Scope(), "board-ovf-neg", "match-n1", "mrs-n1", Now + 100);

            await harness.CommitResultAsync("match-n2", "mrs-n2", (PlayerOne, -2));
            var projected = await harness.Projector.ProjectAsync(harness.Scope(), "board-ovf-neg", "match-n2", "mrs-n2", Now + 200);

            Assert.True(projected.IsSuccess, projected.Message);
            Assert.Equal(0, projected.Data.AppliedCount);
            Assert.Single(projected.Data.FailedPlayers);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, projected.Data.FailedPlayers[0].Code);

            var entry = await harness.LeaderboardStore.FindEntryAsync(TenantId, AppId, "board-ovf-neg", PlayerOne);
            Assert.Equal(long.MinValue + 1, entry.Score);
            Assert.Equal(1, entry.SubmissionCount);
        }

        /// <summary>
        /// 验证溢出拒绝只作用于溢出玩家：同批正常玩家照常落榜（零残留不扩散）。
        /// </summary>
        [Fact]
        public async Task SumOverflow_MixedBatch_ShouldOnlyRejectOverflowPlayer()
        {
            var harness = new Harness();
            await harness.CreateBoardAsync("board-ovf-mix", OnlineLeaderboardScoreUpdatePolicy.Sum);
            await harness.CommitResultAsync("match-m1", "mrs-m1", (PlayerOne, long.MaxValue - 1), (PlayerTwo, 10));
            await harness.Projector.ProjectAsync(harness.Scope(), "board-ovf-mix", "match-m1", "mrs-m1", Now + 100);

            await harness.CommitResultAsync("match-m2", "mrs-m2", (PlayerOne, 5), (PlayerTwo, 20));
            var projected = await harness.Projector.ProjectAsync(harness.Scope(), "board-ovf-mix", "match-m2", "mrs-m2", Now + 200);

            Assert.True(projected.IsSuccess, projected.Message);
            Assert.Equal(1, projected.Data.AppliedCount);
            Assert.Single(projected.Data.FailedPlayers);

            Assert.Equal(30, (await harness.LeaderboardStore.FindEntryAsync(TenantId, AppId, "board-ovf-mix", PlayerTwo)).Score);
        }

        /// <summary>
        /// 验证正常累加不受影响：边界内的大数累加照常生效。
        /// </summary>
        [Fact]
        public async Task SumNormal_Accumulation_ShouldNotBeAffected()
        {
            var harness = new Harness();
            await harness.CreateBoardAsync("board-ovf-ok", OnlineLeaderboardScoreUpdatePolicy.Sum);
            await harness.CommitResultAsync("match-o1", "mrs-o1", (PlayerOne, long.MaxValue / 2));
            await harness.CommitResultAsync("match-o2", "mrs-o2", (PlayerOne, 100));

            var first = await harness.Projector.ProjectAsync(harness.Scope(), "board-ovf-ok", "match-o1", "mrs-o1", Now + 100);
            var second = await harness.Projector.ProjectAsync(harness.Scope(), "board-ovf-ok", "match-o2", "mrs-o2", Now + 200);

            Assert.True(first.IsSuccess, first.Message);
            Assert.True(second.IsSuccess, second.Message);
            Assert.Equal(long.MaxValue / 2 + 100, (await harness.LeaderboardStore.FindEntryAsync(TenantId, AppId, "board-ovf-ok", PlayerOne)).Score);
        }

        /// <summary>
        /// 测试基座：可信投影链路（结算存储 + 排行榜全栈 + 幂等协调器）；关闭防刷分数上限以便构造边界分数。
        /// </summary>
        private sealed class Harness
        {
            /// <summary>
            /// 初始化测试基座（DefaultMaxScorePerSubmission 置 0 关闭单笔分数上限，用例才能注入 long 边界分数）。
            /// </summary>
            public Harness()
            {
                Options = new OnlineLeaderboardOptions { CacheEnabled = false, DefaultMaxScorePerSubmission = 0 };
                Recorder = new OnlineEventRecorder();
                var coordinator = new IdempotencyCoordinator(new InMemoryIdempotencyStore(), new StepClock(), new IdempotencyOptions { ConcurrentWaitTimeoutMilliseconds = 1 });
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

            /// <summary>
            /// 构造作用域。
            /// </summary>
            /// <param name="appId">App 标识。</param>
            /// <returns>作用域。</returns>
            public OnlineScope Scope(long appId = AppId)
            {
                return new OnlineScope(TenantId, appId, ServerId, 0);
            }

            /// <summary>
            /// 创建 Sum 策略榜单。
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
                    Mode = 410,
                    Region = 1,
                    Outcome = OnlineMatchState.Completed,
                    Entries = resultEntries,
                    SettledTime = Now,
                });
                Assert.Equal(matchResultId, committed.MatchResultId);
            }

            /// <summary>
            /// 步进时钟（幂等协调器时钟依赖）。
            /// </summary>
            private sealed class StepClock : IClock
            {
                /// <summary>步进基准（每次读取 +10ms）。</summary>
                private long _nowMilliseconds = 1760000000000;

                /// <summary>获取当前时刻（UTC 毫秒）。</summary>
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
