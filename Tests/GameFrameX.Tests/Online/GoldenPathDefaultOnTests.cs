// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the PRC and related international regulations.
//   使用本项目须严格遵守相关法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with relevant laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file at the root of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯公民合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷及责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB   仓库：https://www.cnb.cool/GameFrameX
//   CNB Repository: https://www.cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Scope;
using GameFrameX.Capability.Online.Assets;
using GameFrameX.Capability.Online.Competition.Leaderboard;
using GameFrameX.Capability.Online.Match;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 黄金路径端到端验收（C460：Options 默认值开箱即通）：
    /// 零 Online JSON（不写 Settings 键）的真实族组合——RPS 对局（桥服务开局 / 出拳）→
    /// 结算管线（match-settlement-tick）→ Grant 幂等发奖 → Match.Settled 投影写默认榜，
    /// 断言钱包余额与默认榜条目同时出现；显式空白覆盖关闭投影的反例见投影器组合测试。
    /// </summary>
    [Collection(CapabilityRuntimeCollection.Name)]
    public class GoldenPathDefaultOnTests
    {
        /// <summary>玩家一（胜方，连出石头两轮）。</summary>
        private const long PlayerOne = 1001;

        /// <summary>玩家二（负方，连出剪刀两轮）。</summary>
        private const long PlayerTwo = 1002;

        /// <summary>房间标识（确定性对局标识派生入参）。</summary>
        private const long RoomId = 5001;

        /// <summary>局号（同房同局幂等）。</summary>
        private const int Round = 1;

        /// <summary>驱动的最大 tick 轮数（每轮覆盖 match-tick / match-settlement-tick / 投影重投 job）。</summary>
        private const int MaxTickRounds = 10;

        /// <summary>
        /// 验证零 JSON 默认配置即通黄金路径：默认榜随装配预建，RPS 两轮决胜 → 结算 → 发奖 → 默认榜条目出现。
        /// </summary>
        [Fact]
        public async Task ZeroSettings_RpsMatch_Settles_Grants_And_ProjectsOntoDefaultBoard()
        {
            await OnlineCapabilityTestHarness.WithRuntimeAsync(async runtime =>
            {
                await runtime.Host.StartAsync();
                var services = runtime.Host.Services;
                var scope = new OnlineScope(OnlineCapabilityTestHarness.ServerId);

                // 默认榜已随装配预建（ActivateAsync 查询-创建）：Top 探针即成功。
                var probe = await services.GetService<OnlineLeaderboardService>().GetTopAsync(scope, new OnlineLeaderboardTopQuery { LeaderboardId = OnlineLeaderboardProjectionOptions.DefaultMatchSettledBoardId, Count = 1, });
                Assert.True(probe.IsSuccess, probe.Message);

                // 对局：开局幂等 + 双方各两轮出拳（石头胜剪刀两轮 = 达到 WinTarget 决胜）。
                var bridge = services.GetService<IOnlineMatchGameBridge>();
                var started = await bridge.StartRoomGameAsync(new OnlineScope(scope.ServerId, PlayerOne), RoomId, Round, new List<long> { PlayerOne, PlayerTwo });
                Assert.True(started.IsSuccess, started.Message);

                for (var roundIndex = 0; roundIndex < 2; roundIndex++)
                {
                    foreach (var player in new[] { PlayerOne, PlayerTwo })
                    {
                        var gesture = player == PlayerOne ? (int)OnlineRockPaperScissorsMove.Rock : (int)OnlineRockPaperScissorsMove.Scissors;
                        var submitted = await bridge.SubmitGestureAsync(new OnlineScope(scope.ServerId, player), RoomId, Round, gesture);
                        Assert.True(submitted.IsSuccess, submitted.Message);
                    }

                    // 本轮双方已提交：驱动一轮 tick 裁决本轮，开启下一轮。
                    await runtime.Host.Scheduler.TickAsync();
                }

                // 结算 → 发奖 → 投影：显式驱动调度轮直至默认榜出现条目（事件订阅链同步投影，重投 job 兜底）。
                OnlineLeaderboardEntry winnerEntry = null;
                for (var tickRound = 0; tickRound < MaxTickRounds; tickRound++)
                {
                    await runtime.Host.Scheduler.TickAsync();
                    winnerEntry = await services.GetService<IOnlineLeaderboardStore>().FindEntryAsync(OnlineLeaderboardProjectionOptions.DefaultMatchSettledBoardId, PlayerOne);
                    if (winnerEntry != null)
                    {
                        break;
                    }
                }

                // 发奖断言：胜方 +WinnerRewardAmount、负方 +ParticipantRewardAmount（样例币资产）。
                var assetStore = services.GetService<IOnlineAssetStore>();
                var winnerWallet = await assetStore.FindWalletAsync(PlayerOne, OnlineRockPaperScissorsGame.RewardAssetId);
                var loserWallet = await assetStore.FindWalletAsync(PlayerTwo, OnlineRockPaperScissorsGame.RewardAssetId);
                Assert.NotNull(winnerWallet);
                Assert.Equal(OnlineRockPaperScissorsGame.WinnerRewardAmount, winnerWallet.Balance);
                Assert.NotNull(loserWallet);
                Assert.Equal(OnlineRockPaperScissorsGame.ParticipantRewardAmount, loserWallet.Balance);

                // 上榜断言：默认榜出现双方条目（胜方分高在前）。
                Assert.NotNull(winnerEntry);
                var loserEntry = await services.GetService<IOnlineLeaderboardStore>().FindEntryAsync(OnlineLeaderboardProjectionOptions.DefaultMatchSettledBoardId, PlayerTwo);
                Assert.NotNull(loserEntry);
                Assert.True(winnerEntry.Score >= loserEntry.Score, "winner score (" + winnerEntry.Score + ") should rank above loser score (" + loserEntry.Score + ")");
            }, null, options =>
            {
                options.Filter = new CapabilityAssemblyFilter(new[] { "online-foundation", "online-social", "online-matchmaking", "online-match", "online-assets", "online-leaderboard", "online-competition" });
                options.HostOptions.SchedulerIntervalMilliseconds = 3600000;
            });
        }
    }
}
