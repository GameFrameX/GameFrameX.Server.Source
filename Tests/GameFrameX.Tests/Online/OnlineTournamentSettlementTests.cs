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
using GameFrameX.Online.Tournament;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 赛事结算测试（vault:C8 S7.4 / VC-7.7：赛事奖励可安全重试）。
    /// 覆盖按冻结成绩发放、重复触发幂等回放不重复发奖、结算只读冻结成绩（不回落实时榜单）、
    /// 逐玩家失败隔离与重试补齐、部分发奖不标记完成、发放作用域固定无归属服、榜单不被结算改写。
    /// </summary>
    public class OnlineTournamentSettlementTests
    {
        /// <summary>
        /// 验证 VC-7.7-a：按冻结成绩名次发放赛事奖励，重复触发逐玩家命中幂等回放——余额与账本条目数守恒，
        /// 结算事件只发一次，赛事推进到结算完成态。
        /// </summary>
        [Fact]
        public async Task SettleAsync_RepeatedTrigger_ShouldGrantExactlyOnce()
        {
            var harness = new OnlineTournamentTestHarness();
            await harness.CreateBoardAsync("board-pay");
            await harness.WriteScoresAsync("board-pay", (OnlineTournamentTestHarness.PlayerOne, 300), (OnlineTournamentTestHarness.PlayerTwo, 200), (OnlineTournamentTestHarness.PlayerThree, 100));
            await harness.CreateTournamentAsync("t1", "board-pay", null, OnlineTournamentTestHarness.Rule(1, 1, 1000), OnlineTournamentTestHarness.Rule(2, 3, 500));
            Assert.True((await harness.RegisterAsync("t1", OnlineTournamentTestHarness.PlayerOne)).Data.Accepted);
            Assert.True((await harness.RegisterAsync("t1", OnlineTournamentTestHarness.PlayerTwo)).Data.Accepted);
            Assert.True((await harness.RegisterAsync("t1", OnlineTournamentTestHarness.PlayerThree)).Data.Accepted);
            await harness.StartTournamentAsync("t1");
            Assert.True((await harness.EndTournamentAsync("t1")).IsSuccess);

            var first = await harness.SettleTournamentAsync("t1");

            Assert.True(first.IsSuccess, first.Message);
            Assert.False(first.Data.IsReplay);
            Assert.Equal(3, first.Data.GrantedCount);
            Assert.Equal(0, first.Data.ReplayCount);
            Assert.Empty(first.Data.FailedPlayers);
            Assert.Equal(1000, await harness.BalanceAsync(OnlineTournamentTestHarness.PlayerOne));
            Assert.Equal(500, await harness.BalanceAsync(OnlineTournamentTestHarness.PlayerTwo));
            Assert.Equal(500, await harness.BalanceAsync(OnlineTournamentTestHarness.PlayerThree));
            Assert.Equal(OnlineTournamentState.Settled, await harness.TournamentStateAsync("t1"));

            // 重投：全部命中回放（返回首次结果），余额与账本条目数守恒。
            var second = await harness.SettleTournamentAsync("t1");

            Assert.True(second.IsSuccess, second.Message);
            Assert.True(second.Data.IsReplay);
            Assert.Equal(0, second.Data.GrantedCount);
            Assert.Equal(3, second.Data.ReplayCount);
            Assert.Empty(second.Data.FailedPlayers);
            Assert.Equal(1000, await harness.BalanceAsync(OnlineTournamentTestHarness.PlayerOne));
            Assert.Equal(500, await harness.BalanceAsync(OnlineTournamentTestHarness.PlayerTwo));
            Assert.Equal(500, await harness.BalanceAsync(OnlineTournamentTestHarness.PlayerThree));
            Assert.Equal(1, await harness.LedgerCountAsync(OnlineTournamentTestHarness.PlayerOne));
            Assert.Equal(1, await harness.LedgerCountAsync(OnlineTournamentTestHarness.PlayerTwo));
            Assert.Equal(1, await harness.LedgerCountAsync(OnlineTournamentTestHarness.PlayerThree));

            Assert.Single(harness.Recorder.Filter(OnlineTournamentEvents.TournamentSettled));
        }

        /// <summary>
        /// 验证 VC-7.7-b：结算只读**冻结成绩**——结束赛事后关联榜单继续变化也不影响发放名次，
        /// 且结算不改写榜单（赛事只读榜单，重置归 C103 赛季）。
        /// </summary>
        [Fact]
        public async Task SettleAsync_AfterBoardChanged_ShouldUseFrozenStandings()
        {
            var harness = new OnlineTournamentTestHarness();
            await harness.CreateBoardAsync("board-frozen");
            await harness.WriteScoresAsync("board-frozen", (OnlineTournamentTestHarness.PlayerOne, 300), (OnlineTournamentTestHarness.PlayerTwo, 200));
            await harness.CreateTournamentAsync("t1", "board-frozen", null, OnlineTournamentTestHarness.Rule(1, 1, 1000), OnlineTournamentTestHarness.Rule(2, 2, 100));
            Assert.True((await harness.RegisterAsync("t1", OnlineTournamentTestHarness.PlayerOne)).Data.Accepted);
            Assert.True((await harness.RegisterAsync("t1", OnlineTournamentTestHarness.PlayerTwo)).Data.Accepted);
            await harness.StartTournamentAsync("t1");
            Assert.True((await harness.EndTournamentAsync("t1")).IsSuccess);

            // 结束后榜单继续变化：玩家二登顶。
            await harness.WriteScoresAsync("board-frozen", (OnlineTournamentTestHarness.PlayerTwo, 9999));

            var settled = await harness.SettleTournamentAsync("t1");

            Assert.True(settled.IsSuccess, settled.Message);
            Assert.Equal(2, settled.Data.GrantedCount);
            Assert.Empty(settled.Data.FailedPlayers);

            // 名次与奖励按冻结时刻的成绩：玩家一第一、玩家二第二。
            Assert.Equal(1000, await harness.BalanceAsync(OnlineTournamentTestHarness.PlayerOne));
            Assert.Equal(100, await harness.BalanceAsync(OnlineTournamentTestHarness.PlayerTwo));

            // 榜单未被结算改写（排名变化来自外部写入，条目数不变，无条目被清除）。
            // 榜单记分策略为 Sum，故玩家二 200 + 9999 = 10199；该值与结算无关，只证明榜单仍是「活」的。
            var boardEntries = await harness.BoardEntriesAsync("board-frozen");
            Assert.Equal(2, boardEntries.Count);
            Assert.Equal(OnlineTournamentTestHarness.PlayerTwo, boardEntries[0].PlayerId);
            Assert.Equal(10199, boardEntries[0].Score);
        }

        /// <summary>
        /// 验证 VC-7.7-c：单玩家发放失败被逐玩家隔离（其余玩家照常到账、赛事不标记完成），
        /// 恢复后重试补齐且**已发放玩家回放不重复发奖**。
        /// <para>
        /// 重试补齐依赖宿主把失败重放策略装配为 <see cref="FailedReplayPolicy.ReExecute"/>（与 C103 赛季结算同一装配项）：
        /// 失败的幂等键可重新执行，故失败玩家在重试轮被真正补发；默认 <see cref="FailedReplayPolicy.ReplayError"/>
        /// 下失败原样回放的语义由 Foundation 幂等组件自身保证，不在本用例覆盖范围。
        /// </para>
        /// </summary>
        [Fact]
        public async Task SettleAsync_PartialFailure_ShouldIsolateAndRecoverOnRetry()
        {
            FailingAssetStore failing = null;
            var harness = new OnlineTournamentTestHarness(
                inner =>
                {
                    failing = new FailingAssetStore(inner);
                    return failing;
                },
                FailedReplayPolicy.ReExecute);

            await harness.CreateBoardAsync("board-partial");
            await harness.WriteScoresAsync("board-partial", (OnlineTournamentTestHarness.PlayerOne, 300), (OnlineTournamentTestHarness.PlayerTwo, 200));
            await harness.CreateTournamentAsync("t1", "board-partial", null, OnlineTournamentTestHarness.Rule(1, 2, 1000));
            Assert.True((await harness.RegisterAsync("t1", OnlineTournamentTestHarness.PlayerOne)).Data.Accepted);
            Assert.True((await harness.RegisterAsync("t1", OnlineTournamentTestHarness.PlayerTwo)).Data.Accepted);
            await harness.StartTournamentAsync("t1");
            Assert.True((await harness.EndTournamentAsync("t1")).IsSuccess);

            failing.FailingPlayerId = OnlineTournamentTestHarness.PlayerTwo;
            failing.FailTimes = 1;

            var first = await harness.SettleTournamentAsync("t1");

            Assert.True(first.IsSuccess, first.Message);
            Assert.Equal(1, first.Data.GrantedCount);
            var failure = Assert.Single(first.Data.FailedPlayers);
            Assert.Equal(OnlineTournamentTestHarness.PlayerTwo, failure.PlayerId);
            Assert.Equal(OnlineErrorCode.DependencyUnavailable, failure.Code);
            Assert.Equal(1, failing.FailuresInjected);

            // 部分发奖：赛事**不得**标记为结算完成。
            Assert.Equal(OnlineTournamentState.Ended, await harness.TournamentStateAsync("t1"));
            Assert.Empty(harness.Recorder.Filter(OnlineTournamentEvents.TournamentSettled));
            Assert.Equal(1000, await harness.BalanceAsync(OnlineTournamentTestHarness.PlayerOne));
            Assert.Equal(0, await harness.BalanceAsync(OnlineTournamentTestHarness.PlayerTwo));

            // 恢复后重试：已发放玩家回放、失败玩家补齐。
            var second = await harness.SettleTournamentAsync("t1");

            Assert.True(second.IsSuccess, second.Message);
            Assert.Equal(1, second.Data.GrantedCount);
            Assert.Equal(1, second.Data.ReplayCount);
            Assert.Empty(second.Data.FailedPlayers);
            Assert.Equal(1000, await harness.BalanceAsync(OnlineTournamentTestHarness.PlayerOne));
            Assert.Equal(1000, await harness.BalanceAsync(OnlineTournamentTestHarness.PlayerTwo));
            Assert.Equal(1, await harness.LedgerCountAsync(OnlineTournamentTestHarness.PlayerOne));
            Assert.Equal(1, await harness.LedgerCountAsync(OnlineTournamentTestHarness.PlayerTwo));
            Assert.Equal(OnlineTournamentState.Settled, await harness.TournamentStateAsync("t1"));
            Assert.Single(harness.Recorder.Filter(OnlineTournamentEvents.TournamentSettled));
        }

        /// <summary>
        /// 验证 VC-7.7-d：发放作用域固定无归属服（<c>ServerId = 0</c>）——调用方从不同区服作用域
        /// 触发结算不会落到不同幂等键上而重复发奖（承 C103 R8 结论），结算通道复用统一资产入口。
        /// </summary>
        [Fact]
        public async Task SettleAsync_FromOtherServerScope_ShouldReplayInsteadOfDoubleGrant()
        {
            var harness = new OnlineTournamentTestHarness();
            await harness.CreateBoardAsync("board-scope");
            await harness.WriteScoresAsync("board-scope", (OnlineTournamentTestHarness.PlayerOne, 300));
            await harness.CreateTournamentAsync("t1", "board-scope", null, OnlineTournamentTestHarness.Rule(1, 1, 1000));
            Assert.True((await harness.RegisterAsync("t1", OnlineTournamentTestHarness.PlayerOne)).Data.Accepted);
            await harness.StartTournamentAsync("t1");
            Assert.True((await harness.EndTournamentAsync("t1")).IsSuccess);

            Assert.True((await harness.SettleTournamentAsync("t1")).IsSuccess);

            // 从另一个区服作用域重复触发（无归属服 → 幂等键不随调用方区服漂移）。
            var otherServerScope = new GameFrameX.Online.Scope.OnlineScope(OnlineTournamentTestHarness.TenantId, OnlineTournamentTestHarness.AppId, 999, 0);
            var replay = await harness.TournamentService.SettleAsync(otherServerScope, "t1", OnlineTournamentTestHarness.Now + 400);

            Assert.True(replay.IsSuccess, replay.Message);
            Assert.True(replay.Data.IsReplay);
            Assert.Equal(1, replay.Data.ReplayCount);
            Assert.Equal(0, replay.Data.GrantedCount);
            Assert.Equal(1000, await harness.BalanceAsync(OnlineTournamentTestHarness.PlayerOne));
            Assert.Equal(1, await harness.LedgerCountAsync(OnlineTournamentTestHarness.PlayerOne));

            // 结算交易落在无归属服上（ServerId = 0）——重试口径与调用方作用域无关。
            var transactions = await harness.TransactionStore.ListByStateAsync(OnlineAssetTransactionState.Succeeded);
            Assert.NotEmpty(transactions);
            foreach (var transaction in transactions)
            {
                Assert.Equal(0, transaction.HomeServerId);
                Assert.Equal(OnlineAssetChangeSource.MatchReward, transaction.Source);
                Assert.Equal("tournament-t1-" + OnlineTournamentTestHarness.PlayerOne, transaction.BusinessOrderId);
            }
        }

        /// <summary>
        /// 验证 VC-7.7-e：未命中任何奖励规则的名次不产生发放（规则区间覆盖不足时不得兜底发奖），
        /// 且赛事仍正常推进到结算完成态。
        /// </summary>
        [Fact]
        public async Task SettleAsync_RankWithoutRule_ShouldGrantNothing()
        {
            var harness = new OnlineTournamentTestHarness();
            await harness.CreateBoardAsync("board-gap");
            await harness.WriteScoresAsync("board-gap", (OnlineTournamentTestHarness.PlayerOne, 300), (OnlineTournamentTestHarness.PlayerTwo, 200));
            await harness.CreateTournamentAsync("t1", "board-gap", null, OnlineTournamentTestHarness.Rule(1, 1, 1000));
            Assert.True((await harness.RegisterAsync("t1", OnlineTournamentTestHarness.PlayerOne)).Data.Accepted);
            Assert.True((await harness.RegisterAsync("t1", OnlineTournamentTestHarness.PlayerTwo)).Data.Accepted);
            await harness.StartTournamentAsync("t1");
            Assert.True((await harness.EndTournamentAsync("t1")).IsSuccess);

            var settled = await harness.SettleTournamentAsync("t1");

            Assert.True(settled.IsSuccess, settled.Message);
            Assert.Equal(1, settled.Data.GrantedCount);
            Assert.Equal(0, settled.Data.ReplayCount);
            Assert.Empty(settled.Data.FailedPlayers);
            Assert.Equal(1000, await harness.BalanceAsync(OnlineTournamentTestHarness.PlayerOne));
            Assert.Equal(0, await harness.BalanceAsync(OnlineTournamentTestHarness.PlayerTwo));
            Assert.Equal(OnlineTournamentState.Settled, await harness.TournamentStateAsync("t1"));
        }
    }
}
