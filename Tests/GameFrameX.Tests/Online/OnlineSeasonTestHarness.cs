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

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GameFrameX.Foundation.Idempotency;
using GameFrameX.Online.Assets;
using GameFrameX.Online.Contracts;
using GameFrameX.Online.Idempotency;
using GameFrameX.Online.Leaderboard;
using GameFrameX.Online.Match;
using GameFrameX.Online.Scope;
using GameFrameX.Online.Season;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// C103 赛季生命周期测试基座：真实幂等协调器 + 真实 C102 可信写榜链路（榜内容一律经投影器落榜，
    /// 不直写存储——赛季测试的前置状态与生产路径同构）+ 真实 C95 统一资产入口。
    /// <para>
    /// 可选装饰器参数用于制造生产竞态：榜单存储装饰器在「快照与清空之间」插入真实写入（VC-7.5-c），
    /// 资产存储装饰器让指定玩家的发放瞬时失败（VC-7.6-b）。
    /// </para>
    /// </summary>
    internal sealed class OnlineSeasonTestHarness
    {
        /// <summary>租户标识。</summary>
        public const long TenantId = 1;

        /// <summary>App 标识。</summary>
        public const long AppId = 10;

        /// <summary>另一 App 标识（跨 App 反预言用例）。</summary>
        public const long OtherAppId = 11;

        /// <summary>区服标识。</summary>
        public const long ServerId = 100;

        /// <summary>玩家一。</summary>
        public const long PlayerOne = 1001;

        /// <summary>玩家二。</summary>
        public const long PlayerTwo = 1002;

        /// <summary>玩家三。</summary>
        public const long PlayerThree = 1003;

        /// <summary>基准时刻（UTC 毫秒）。</summary>
        public const long Now = 1000000L;

        /// <summary>奖励通货标识。</summary>
        public const string CoinAssetId = "coin";

        /// <summary>对局 / 结果标识序号（保证每次落榜前置事实的追溯键唯一）。</summary>
        private int _matchSequence;

        /// <summary>
        /// 初始化测试基座。
        /// </summary>
        /// <param name="leaderboardStoreDecorator">榜单存储装饰器（可空；用于注入并发写榜竞态）。</param>
        /// <param name="assetStoreDecorator">资产存储装饰器（可空；用于注入发放瞬时失败）。</param>
        /// <param name="failedReplayPolicy">幂等失败记录的重放策略（宿主装配项；默认为 Foundation 默认值）。</param>
        public OnlineSeasonTestHarness(Func<IOnlineLeaderboardStore, IOnlineLeaderboardStore> leaderboardStoreDecorator = null, Func<IOnlineAssetStore, IOnlineAssetStore> assetStoreDecorator = null, FailedReplayPolicy failedReplayPolicy = FailedReplayPolicy.ReplayError)
        {
            Options = new OnlineLeaderboardOptions();
            Recorder = new OnlineEventRecorder();

            IdempotencyStore = new InMemoryIdempotencyStore();
            var coordinator = new IdempotencyCoordinator(IdempotencyStore, new StepClock(), new IdempotencyOptions { ConcurrentWaitTimeoutMilliseconds = 1, FailedReplayPolicy = failedReplayPolicy });
            IdempotencyService = new OnlineIdempotencyService(coordinator, null);

            RawLeaderboardStore = new InMemoryOnlineLeaderboardStore();
            LeaderboardStore = leaderboardStoreDecorator == null ? (IOnlineLeaderboardStore)RawLeaderboardStore : leaderboardStoreDecorator(RawLeaderboardStore);
            LeaderboardService = new OnlineLeaderboardService(LeaderboardStore, Options, Recorder);

            ResultStore = new InMemoryOnlineMatchResultStore();
            Projector = new OnlineLeaderboardResultProjector(ResultStore, LeaderboardService, IdempotencyService);

            RawAssetStore = new InMemoryOnlineAssetStore();
            AssetStore = assetStoreDecorator == null ? (IOnlineAssetStore)RawAssetStore : assetStoreDecorator(RawAssetStore);
            TransactionStore = new InMemoryOnlineAssetTransactionStore();
            GrantService = new OnlineGrantService(AssetStore, TransactionStore, IdempotencyService, Recorder);

            SeasonStore = new InMemoryOnlineSeasonStore();
            SeasonService = new OnlineSeasonService(SeasonStore, LeaderboardService, GrantService, Recorder);
        }

        /// <summary>获取排行榜组件选项（读缓存默认开启，覆盖重置失效路径）。</summary>
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

        /// <summary>获取 Online 幂等服务（写榜链路与资产入口共用同一 C93 组件）。</summary>
        public OnlineIdempotencyService IdempotencyService
        {
            get;
        }

        /// <summary>获取赛季服务使用的榜单存储（装饰器可能包了一层）。</summary>
        public IOnlineLeaderboardStore LeaderboardStore
        {
            get;
        }

        /// <summary>获取未包装的榜单存储（直读断言用）。</summary>
        public InMemoryOnlineLeaderboardStore RawLeaderboardStore
        {
            get;
        }

        /// <summary>获取排行榜服务。</summary>
        public OnlineLeaderboardService LeaderboardService
        {
            get;
        }

        /// <summary>获取对局结算结果存储（可信事实来源）。</summary>
        public InMemoryOnlineMatchResultStore ResultStore
        {
            get;
        }

        /// <summary>获取可信结果投影器（榜内容唯一写入路径）。</summary>
        public OnlineLeaderboardResultProjector Projector
        {
            get;
        }

        /// <summary>获取赛季服务使用的资产存储（装饰器可能包了一层）。</summary>
        public IOnlineAssetStore AssetStore
        {
            get;
        }

        /// <summary>获取未包装的资产存储（余额与账本断言用）。</summary>
        public InMemoryOnlineAssetStore RawAssetStore
        {
            get;
        }

        /// <summary>获取资产交易存储。</summary>
        public InMemoryOnlineAssetTransactionStore TransactionStore
        {
            get;
        }

        /// <summary>获取统一资产入口。</summary>
        public OnlineGrantService GrantService
        {
            get;
        }

        /// <summary>获取赛季存储。</summary>
        public InMemoryOnlineSeasonStore SeasonStore
        {
            get;
        }

        /// <summary>获取赛季生命周期服务。</summary>
        public OnlineSeasonService SeasonService
        {
            get;
        }

        /// <summary>
        /// 构造作用域（默认本 App、无玩家主体位——赛季与榜单均为 (TenantId, AppId) 作用域）。
        /// </summary>
        /// <param name="appId">App 标识。</param>
        /// <returns>作用域。</returns>
        public OnlineScope Scope(long appId = AppId)
        {
            return new OnlineScope(TenantId, appId, ServerId, 0);
        }

        /// <summary>
        /// 创建累计型榜单（Sum 策略：历次成绩累加，便于用分数差构造名次）。
        /// </summary>
        /// <param name="leaderboardId">榜单标识。</param>
        /// <returns>完成通知。</returns>
        public async Task CreateBoardAsync(string leaderboardId)
        {
            var created = await LeaderboardService.CreateAsync(Scope(), new OnlineLeaderboardCreateRequest { LeaderboardId = leaderboardId, ScoreUpdatePolicy = OnlineLeaderboardScoreUpdatePolicy.Sum }, Now);
            Assert.True(created.IsSuccess, created.Message);
        }

        /// <summary>
        /// 经真实可信链路把一批成绩落榜（提交结算事实 → 投影器写榜；榜上名次由分数降序决定）。
        /// </summary>
        /// <param name="leaderboardId">榜单标识。</param>
        /// <param name="scores">条目（玩家, 分数）序列。</param>
        /// <returns>完成通知。</returns>
        public async Task WriteScoresAsync(string leaderboardId, params (long PlayerId, long Score)[] scores)
        {
            _matchSequence++;
            var matchId = "match-" + _matchSequence;
            var matchResultId = "mrs-" + _matchSequence;

            var resultEntries = new List<OnlineMatchResultEntry>();
            foreach (var item in scores)
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

            var projected = await Projector.ProjectAsync(Scope(), leaderboardId, matchId, matchResultId, Now + _matchSequence);
            Assert.True(projected.IsSuccess, projected.Message);
            Assert.Empty(projected.Data.FailedPlayers);
        }

        /// <summary>
        /// 创建赛季（排期 [Now, Now+10000)，奖励规则由用例给出）。
        /// </summary>
        /// <param name="seasonId">赛季标识。</param>
        /// <param name="leaderboardId">关联榜单标识。</param>
        /// <param name="rules">奖励规则。</param>
        /// <returns>创建结果。</returns>
        public Task<OnlineResult<OnlineSeason>> CreateSeasonAsync(string seasonId, string leaderboardId, params OnlineSeasonRewardRule[] rules)
        {
            var request = new OnlineSeasonCreateRequest
            {
                SeasonId = seasonId,
                LeaderboardId = leaderboardId,
                StartTime = Now,
                EndTime = Now + 10000,
                RewardRules = new List<OnlineSeasonRewardRule>(rules),
            };
            return SeasonService.CreateAsync(Scope(), request, Now);
        }

        /// <summary>
        /// 开始赛季。
        /// </summary>
        /// <param name="seasonId">赛季标识。</param>
        /// <returns>开始结果。</returns>
        public Task<OnlineResult<OnlineSeason>> StartSeasonAsync(string seasonId)
        {
            return SeasonService.StartAsync(Scope(), seasonId, Now + 100);
        }

        /// <summary>
        /// 结束赛季（含快照与重置）。
        /// </summary>
        /// <param name="seasonId">赛季标识。</param>
        /// <returns>结束结果（成功时携带落档快照）。</returns>
        public Task<OnlineResult<OnlineSeasonSnapshot>> EndSeasonAsync(string seasonId)
        {
            return SeasonService.EndAsync(Scope(), seasonId, Now + 200);
        }

        /// <summary>
        /// 结算赛季。
        /// </summary>
        /// <param name="seasonId">赛季标识。</param>
        /// <returns>结算回执。</returns>
        public Task<OnlineResult<OnlineSeasonSettlementOutcome>> SettleSeasonAsync(string seasonId)
        {
            return SeasonService.SettleAsync(Scope(), seasonId, Now + 300);
        }

        /// <summary>
        /// 读取赛季当前状态（断言状态推进用）。
        /// </summary>
        /// <param name="seasonId">赛季标识。</param>
        /// <returns>赛季状态。</returns>
        public async Task<OnlineSeasonState> SeasonStateAsync(string seasonId)
        {
            var season = await SeasonStore.FindAsync(TenantId, AppId, seasonId);
            Assert.NotNull(season);
            return season.State;
        }

        /// <summary>
        /// 读取玩家通货币余额（未发放过返回 0）。
        /// </summary>
        /// <param name="playerId">玩家标识。</param>
        /// <returns>币余额。</returns>
        public async Task<long> BalanceAsync(long playerId)
        {
            var wallet = await RawAssetStore.FindWalletAsync(TenantId, AppId, playerId, CoinAssetId);
            return wallet == null ? 0 : wallet.Balance;
        }

        /// <summary>
        /// 读取玩家账本条目数（重复发放判据：同玩家重试后条目数必须守恒）。
        /// </summary>
        /// <param name="playerId">玩家标识。</param>
        /// <returns>账本条目数。</returns>
        public async Task<int> LedgerCountAsync(long playerId)
        {
            var entries = await RawAssetStore.ListLedgerEntriesAsync(new OnlineLedgerPageQuery { TenantId = TenantId, AppId = AppId, PlayerId = playerId, AfterSequenceNumber = 0, MaxCount = 1000 });
            return entries.Count;
        }

        /// <summary>
        /// 读取榜单当前全序条目（重置判据：结束后必须为空）。
        /// </summary>
        /// <param name="leaderboardId">榜单标识。</param>
        /// <returns>全序条目。</returns>
        public async Task<List<OnlineLeaderboardEntry>> BoardEntriesAsync(string leaderboardId)
        {
            var board = await RawLeaderboardStore.FindAsync(TenantId, AppId, leaderboardId);
            Assert.NotNull(board);
            return await RawLeaderboardStore.ListOrderedEntriesAsync(board);
        }

        /// <summary>
        /// 构造名次区间奖励规则。
        /// </summary>
        /// <param name="fromRank">起始名次（含）。</param>
        /// <param name="toRank">结束名次（含）。</param>
        /// <param name="amount">通货数额。</param>
        /// <returns>奖励规则。</returns>
        public static OnlineSeasonRewardRule Rule(int fromRank, int toRank, long amount)
        {
            return new OnlineSeasonRewardRule
            {
                FromRank = fromRank,
                ToRank = toRank,
                Rewards = new List<OnlineAssetChangeLine> { new OnlineAssetChangeLine(OnlineAssetKind.Currency, CoinAssetId, amount) },
            };
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
