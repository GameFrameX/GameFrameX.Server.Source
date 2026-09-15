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
using GameFrameX.Online.Season;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 赛季结算测试（vault:C8 S7.3 / VC-7.6-a～c：赛季奖励可安全重试）。
    /// 覆盖按冻结快照发放、重复触发幂等回放不重复发奖、中途失败逐玩家隔离后重试补齐、
    /// 部分发奖不标记完成、快照缺失拒绝结算、发放通道复用统一资产入口。
    /// </summary>
    public class OnlineSeasonSettlementTests
    {
        /// <summary>
        /// 验证 VC-7.6-a：按快照名次发放赛季奖励，重复触发逐玩家命中幂等回放——余额与账本条目数守恒，
        /// 结算事件只发一次，赛季推进到结算完成态。
        /// </summary>
        [Fact]
        public async Task SettleAsync_RepeatedTrigger_ShouldGrantExactlyOnce()
        {
            var harness = new OnlineSeasonTestHarness();
            await harness.CreateBoardAsync("board-pay");
            await harness.WriteScoresAsync("board-pay", (OnlineSeasonTestHarness.PlayerOne, 300), (OnlineSeasonTestHarness.PlayerTwo, 200), (OnlineSeasonTestHarness.PlayerThree, 100));
            await harness.CreateSeasonAsync("s1", "board-pay", OnlineSeasonTestHarness.Rule(1, 1, 1000), OnlineSeasonTestHarness.Rule(2, 3, 500));
            await harness.StartSeasonAsync("s1");
            Assert.True((await harness.EndSeasonAsync("s1")).IsSuccess);

            var first = await harness.SettleSeasonAsync("s1");

            Assert.True(first.IsSuccess, first.Message);
            Assert.False(first.Data.IsReplay);
            Assert.Equal(3, first.Data.GrantedCount);
            Assert.Equal(0, first.Data.ReplayCount);
            Assert.Empty(first.Data.FailedPlayers);
            Assert.Equal(1000, await harness.BalanceAsync(OnlineSeasonTestHarness.PlayerOne));
            Assert.Equal(500, await harness.BalanceAsync(OnlineSeasonTestHarness.PlayerTwo));
            Assert.Equal(500, await harness.BalanceAsync(OnlineSeasonTestHarness.PlayerThree));
            Assert.Equal(OnlineSeasonState.Settled, await harness.SeasonStateAsync("s1"));

            // 重投：全部命中回放（返回首次结果），余额与账本条目数守恒。
            var second = await harness.SettleSeasonAsync("s1");

            Assert.True(second.IsSuccess, second.Message);
            Assert.True(second.Data.IsReplay);
            Assert.Equal(0, second.Data.GrantedCount);
            Assert.Equal(3, second.Data.ReplayCount);
            Assert.Empty(second.Data.FailedPlayers);
            Assert.Equal(1000, await harness.BalanceAsync(OnlineSeasonTestHarness.PlayerOne));
            Assert.Equal(500, await harness.BalanceAsync(OnlineSeasonTestHarness.PlayerTwo));
            Assert.Equal(500, await harness.BalanceAsync(OnlineSeasonTestHarness.PlayerThree));
            Assert.Equal(1, await harness.LedgerCountAsync(OnlineSeasonTestHarness.PlayerOne));
            Assert.Equal(1, await harness.LedgerCountAsync(OnlineSeasonTestHarness.PlayerTwo));
            Assert.Equal(1, await harness.LedgerCountAsync(OnlineSeasonTestHarness.PlayerThree));

            // 结算事件只在真正的落定点发一次；载荷只含计数，不含玩家明细。
            var settledEvents = harness.Recorder.Filter(OnlineSeasonEvents.SeasonSettled);
            Assert.Single(settledEvents);
            Assert.Equal("3", settledEvents[0].PayloadAuditFields["GrantedCount"]);
            Assert.Equal("0", settledEvents[0].PayloadAuditFields["ReplayCount"]);
            Assert.Equal("0", settledEvents[0].PayloadAuditFields["FailedCount"]);
            Assert.Equal(4, settledEvents[0].PayloadAuditFields.Count);
            Assert.Equal(0, settledEvents[0].PlayerId);
        }

        /// <summary>
        /// 验证发放通道复用 C95 统一资产入口：每玩家一笔结算交易、来源为既有的比赛奖励来源
        /// （赛季奖励复用既有来源不新增第 5 类来源）、业务单号由赛季与玩家确定性派生。
        /// </summary>
        [Fact]
        public async Task SettleAsync_ShouldRouteThroughUnifiedAssetEntry()
        {
            var harness = new OnlineSeasonTestHarness();
            await harness.CreateBoardAsync("board-entry");
            await harness.WriteScoresAsync("board-entry", (OnlineSeasonTestHarness.PlayerOne, 300), (OnlineSeasonTestHarness.PlayerTwo, 200));
            await harness.CreateSeasonAsync("s1", "board-entry", OnlineSeasonTestHarness.Rule(1, 2, 100));
            await harness.StartSeasonAsync("s1");
            Assert.True((await harness.EndSeasonAsync("s1")).IsSuccess);

            var settled = await harness.SettleSeasonAsync("s1");
            Assert.True(settled.IsSuccess, settled.Message);

            var transactions = await harness.TransactionStore.ListByStateAsync(OnlineAssetTransactionState.Succeeded);

            Assert.Equal(2, transactions.Count);
            Assert.All(transactions, transaction => Assert.Equal(OnlineAssetChangeSource.MatchReward, transaction.Source));
            Assert.All(transactions, transaction => Assert.Equal(OnlineGrantOperation.Grant, transaction.Operation));
            Assert.Contains(transactions, transaction => transaction.BusinessOrderId == "season-s1-" + OnlineSeasonTestHarness.PlayerOne);
            Assert.Contains(transactions, transaction => transaction.BusinessOrderId == "season-s1-" + OnlineSeasonTestHarness.PlayerTwo);
        }

        /// <summary>
        /// 验证 VC-7.6-b 隔离半边：单玩家发放失败被逐玩家隔离——其余玩家照常到账、失败玩家不进账，
        /// 赛季**不**推进到结算完成态（部分发奖不得标记完成），也不发结算事件。
        /// </summary>
        [Fact]
        public async Task SettleAsync_SinglePlayerFailure_ShouldIsolateAndNotMarkSeasonSettled()
        {
            FailingAssetStore failing = null;
            var harness = new OnlineSeasonTestHarness(null, inner => failing = new FailingAssetStore(inner) { FailingPlayerId = OnlineSeasonTestHarness.PlayerTwo, FailTimes = 1 });
            await harness.CreateBoardAsync("board-iso");
            await harness.WriteScoresAsync("board-iso", (OnlineSeasonTestHarness.PlayerOne, 300), (OnlineSeasonTestHarness.PlayerTwo, 200), (OnlineSeasonTestHarness.PlayerThree, 100));
            await harness.CreateSeasonAsync("s1", "board-iso", OnlineSeasonTestHarness.Rule(1, 1, 1000), OnlineSeasonTestHarness.Rule(2, 3, 500));
            await harness.StartSeasonAsync("s1");
            Assert.True((await harness.EndSeasonAsync("s1")).IsSuccess);

            var settled = await harness.SettleSeasonAsync("s1");

            Assert.True(settled.IsSuccess, settled.Message);
            Assert.Equal(1, failing.FailuresInjected);
            Assert.Equal(2, settled.Data.GrantedCount);
            Assert.Equal(0, settled.Data.ReplayCount);
            Assert.Single(settled.Data.FailedPlayers);
            Assert.Equal(OnlineSeasonTestHarness.PlayerTwo, settled.Data.FailedPlayers[0].PlayerId);
            Assert.Equal(OnlineErrorCode.DependencyUnavailable, settled.Data.FailedPlayers[0].Code);

            // 部分发奖不得标记完成：赛季停留在已结束态，不发结算事件。
            Assert.Equal(OnlineSeasonState.Ended, await harness.SeasonStateAsync("s1"));
            Assert.Empty(harness.Recorder.Filter(OnlineSeasonEvents.SeasonSettled));
            Assert.Equal(1000, await harness.BalanceAsync(OnlineSeasonTestHarness.PlayerOne));
            Assert.Equal(0, await harness.BalanceAsync(OnlineSeasonTestHarness.PlayerTwo));
            Assert.Equal(500, await harness.BalanceAsync(OnlineSeasonTestHarness.PlayerThree));
        }

        /// <summary>
        /// 验证 VC-7.6-b 重试半边（宿主装配 <see cref="FailedReplayPolicy.ReExecute"/>）：发放通道恢复后
        /// 重试补齐失败玩家，已发放玩家命中幂等回放，无一人重复到账，赛季推进到结算完成态。
        /// </summary>
        [Fact]
        public async Task SettleAsync_PartialFailureThenRetry_WithReExecutePolicy_ShouldCompleteWithoutDuplicateGrant()
        {
            FailingAssetStore failing = null;
            var harness = new OnlineSeasonTestHarness(null, inner => failing = new FailingAssetStore(inner) { FailingPlayerId = OnlineSeasonTestHarness.PlayerTwo, FailTimes = 1 }, FailedReplayPolicy.ReExecute);
            await harness.CreateBoardAsync("board-retry");
            await harness.WriteScoresAsync("board-retry", (OnlineSeasonTestHarness.PlayerOne, 300), (OnlineSeasonTestHarness.PlayerTwo, 200), (OnlineSeasonTestHarness.PlayerThree, 100));
            await harness.CreateSeasonAsync("s1", "board-retry", OnlineSeasonTestHarness.Rule(1, 1, 1000), OnlineSeasonTestHarness.Rule(2, 3, 500));
            await harness.StartSeasonAsync("s1");
            Assert.True((await harness.EndSeasonAsync("s1")).IsSuccess);

            var first = await harness.SettleSeasonAsync("s1");
            Assert.True(first.IsSuccess, first.Message);
            Assert.Equal(1, failing.FailuresInjected);
            Assert.Equal(2, first.Data.GrantedCount);
            Assert.Single(first.Data.FailedPlayers);
            Assert.Equal(OnlineSeasonState.Ended, await harness.SeasonStateAsync("s1"));

            var second = await harness.SettleSeasonAsync("s1");

            Assert.True(second.IsSuccess, second.Message);
            Assert.False(second.Data.IsReplay);
            Assert.Equal(1, second.Data.GrantedCount);
            Assert.Equal(2, second.Data.ReplayCount);
            Assert.Empty(second.Data.FailedPlayers);
            Assert.Equal(OnlineSeasonState.Settled, await harness.SeasonStateAsync("s1"));
            Assert.Equal(1000, await harness.BalanceAsync(OnlineSeasonTestHarness.PlayerOne));
            Assert.Equal(500, await harness.BalanceAsync(OnlineSeasonTestHarness.PlayerTwo));
            Assert.Equal(500, await harness.BalanceAsync(OnlineSeasonTestHarness.PlayerThree));
            Assert.Equal(1, await harness.LedgerCountAsync(OnlineSeasonTestHarness.PlayerOne));
            Assert.Equal(1, await harness.LedgerCountAsync(OnlineSeasonTestHarness.PlayerTwo));
            Assert.Equal(1, await harness.LedgerCountAsync(OnlineSeasonTestHarness.PlayerThree));
            Assert.Single(harness.Recorder.Filter(OnlineSeasonEvents.SeasonSettled));
        }

        /// <summary>
        /// 验证失败重放的默认策略（Foundation 默认 <see cref="FailedReplayPolicy.ReplayError"/>）下的重试语义：
        /// 失败的幂等键**不**可重新执行，重试原样回放失败——因此「永不重复发奖」是策略无关的硬保证，
        /// 而「重试补齐剩余玩家」依赖宿主把失败重放策略装配为 ReExecute，或在保留期后重新占位（C103 的运行时依赖）。
        /// </summary>
        [Fact]
        public async Task SettleAsync_RetryUnderDefaultReplayErrorPolicy_ShouldNeverDoubleGrant()
        {
            FailingAssetStore failing = null;
            var harness = new OnlineSeasonTestHarness(null, inner => failing = new FailingAssetStore(inner) { FailingPlayerId = OnlineSeasonTestHarness.PlayerTwo, FailTimes = 1 });
            await harness.CreateBoardAsync("board-replay");
            await harness.WriteScoresAsync("board-replay", (OnlineSeasonTestHarness.PlayerOne, 300), (OnlineSeasonTestHarness.PlayerTwo, 200), (OnlineSeasonTestHarness.PlayerThree, 100));
            await harness.CreateSeasonAsync("s1", "board-replay", OnlineSeasonTestHarness.Rule(1, 1, 1000), OnlineSeasonTestHarness.Rule(2, 3, 500));
            await harness.StartSeasonAsync("s1");
            Assert.True((await harness.EndSeasonAsync("s1")).IsSuccess);

            var first = await harness.SettleSeasonAsync("s1");
            Assert.True(first.IsSuccess, first.Message);
            Assert.Equal(2, first.Data.GrantedCount);

            var second = await harness.SettleSeasonAsync("s1");

            Assert.True(second.IsSuccess, second.Message);
            Assert.False(second.Data.IsReplay);
            Assert.Equal(0, second.Data.GrantedCount);
            Assert.Equal(2, second.Data.ReplayCount);
            Assert.Single(second.Data.FailedPlayers);
            Assert.Equal(OnlineSeasonTestHarness.PlayerTwo, second.Data.FailedPlayers[0].PlayerId);

            // 硬保证：无人重复到账（账本条目数守恒），赛季不因失败的重复回放而被误标完成。
            Assert.Equal(1, await harness.LedgerCountAsync(OnlineSeasonTestHarness.PlayerOne));
            Assert.Equal(0, await harness.LedgerCountAsync(OnlineSeasonTestHarness.PlayerTwo));
            Assert.Equal(1, await harness.LedgerCountAsync(OnlineSeasonTestHarness.PlayerThree));
            Assert.Equal(OnlineSeasonState.Ended, await harness.SeasonStateAsync("s1"));
            Assert.Empty(harness.Recorder.Filter(OnlineSeasonEvents.SeasonSettled));
        }

        /// <summary>
        /// 验证结算依据是冻结快照而非实时榜单：重置后榜单被新一届成绩覆盖，旧赛季仍按当时的快照名次发奖
        /// ——若结算回落读实时榜（重置后为空或已被新成绩占据），奖励名次会错位。
        /// </summary>
        [Fact]
        public async Task SettleAsync_AfterNewSeasonOverwritesBoard_ShouldStillUseFrozenSnapshot()
        {
            var harness = new OnlineSeasonTestHarness();
            await harness.CreateBoardAsync("board-snap");
            await harness.WriteScoresAsync("board-snap", (OnlineSeasonTestHarness.PlayerOne, 300), (OnlineSeasonTestHarness.PlayerTwo, 100));
            await harness.CreateSeasonAsync("s1", "board-snap", OnlineSeasonTestHarness.Rule(1, 1, 1000), OnlineSeasonTestHarness.Rule(2, 2, 500));
            await harness.StartSeasonAsync("s1");
            Assert.True((await harness.EndSeasonAsync("s1")).IsSuccess);

            // 新一届：玩家二以远高于上届的分数占据榜首。
            await harness.WriteScoresAsync("board-snap", (OnlineSeasonTestHarness.PlayerTwo, 9999));

            var settled = await harness.SettleSeasonAsync("s1");

            Assert.True(settled.IsSuccess, settled.Message);
            Assert.Equal(1000, await harness.BalanceAsync(OnlineSeasonTestHarness.PlayerOne));
            Assert.Equal(500, await harness.BalanceAsync(OnlineSeasonTestHarness.PlayerTwo));
        }

        /// <summary>
        /// 验证 VC-7.6-c：快照缺失时拒绝结算（不回落读榜、不降级发放），赛季状态与账本零变化，
        /// 待快照恢复后仍可正常结算。
        /// </summary>
        [Fact]
        public async Task SettleAsync_MissingSnapshot_ShouldRefuseAndGrantNothing()
        {
            var harness = new OnlineSeasonTestHarness();
            await harness.CreateBoardAsync("board-orphan");
            await harness.WriteScoresAsync("board-orphan", (OnlineSeasonTestHarness.PlayerOne, 300));

            // 白盒构造：赛季已处于结束态但快照未落档（模拟快照落档失败 / 快照存储被清理）。
            await harness.SeasonStore.CreateAsync(new OnlineSeason
            {
                SeasonId = "s-orphan",
                TenantId = OnlineSeasonTestHarness.TenantId,
                AppId = OnlineSeasonTestHarness.AppId,
                LeaderboardId = "board-orphan",
                StartTime = OnlineSeasonTestHarness.Now,
                EndTime = OnlineSeasonTestHarness.Now + 10000,
                RewardRules = new List<OnlineSeasonRewardRule> { OnlineSeasonTestHarness.Rule(1, 1, 1000) },
                State = OnlineSeasonState.Ended,
                CreatedTime = OnlineSeasonTestHarness.Now,
                EndedTime = OnlineSeasonTestHarness.Now + 200,
            });

            var refused = await harness.SettleSeasonAsync("s-orphan");

            Assert.False(refused.IsSuccess);
            Assert.Equal(OnlineErrorCode.StateNotReady, refused.Code);
            Assert.Equal(0, await harness.BalanceAsync(OnlineSeasonTestHarness.PlayerOne));
            Assert.Equal(0, await harness.LedgerCountAsync(OnlineSeasonTestHarness.PlayerOne));
            Assert.Equal(OnlineSeasonState.Ended, await harness.SeasonStateAsync("s-orphan"));
            Assert.Empty(harness.Recorder.Filter(OnlineSeasonEvents.SeasonSettled));
        }

        /// <summary>
        /// 验证名次未命中任何奖励规则时不发奖（奖励区间只覆盖部分名次是合法配置），
        /// 未被覆盖的玩家零到账且不计入失败明细。
        /// </summary>
        [Fact]
        public async Task SettleAsync_RankOutsideAllRules_ShouldGrantNothingForThatPlayer()
        {
            var harness = new OnlineSeasonTestHarness();
            await harness.CreateBoardAsync("board-partial");
            await harness.WriteScoresAsync("board-partial", (OnlineSeasonTestHarness.PlayerOne, 300), (OnlineSeasonTestHarness.PlayerTwo, 200), (OnlineSeasonTestHarness.PlayerThree, 100));
            await harness.CreateSeasonAsync("s1", "board-partial", OnlineSeasonTestHarness.Rule(1, 1, 1000));
            await harness.StartSeasonAsync("s1");
            Assert.True((await harness.EndSeasonAsync("s1")).IsSuccess);

            var settled = await harness.SettleSeasonAsync("s1");

            Assert.True(settled.IsSuccess, settled.Message);
            Assert.Equal(1, settled.Data.GrantedCount);
            Assert.Empty(settled.Data.FailedPlayers);
            Assert.Equal(1000, await harness.BalanceAsync(OnlineSeasonTestHarness.PlayerOne));
            Assert.Equal(0, await harness.BalanceAsync(OnlineSeasonTestHarness.PlayerTwo));
            Assert.Equal(0, await harness.BalanceAsync(OnlineSeasonTestHarness.PlayerThree));
            Assert.Equal(1, settled.Data.GrantedCount + settled.Data.ReplayCount);
        }
    }
}
