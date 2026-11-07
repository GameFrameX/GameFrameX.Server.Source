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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Hosting.Core;
using GameFrameX.Capability.Online.Abstractions;
using GameFrameX.Capability.Online.Match;
using GameFrameX.Capability.Online.Operations.HotfixRollback;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Competition.Leaderboard;
using GameFrameX.Hotfix.Logic.Game.RockPaperScissors;
using GameFrameX.Hotfix.Logic.Player.Friend;
using GameFrameX.Hotfix.Logic.ServerRole.Match;
using GameFrameX.Proto.Proto;
using Xunit;

namespace GameFrameX.Hotfix.Tests;

/// <summary>
/// 黄金路径桥断言（C460：好友（C244）→撮合（C246）→对局→上榜 全链经 Hotfix 桥薄适配器走通）：
/// 真实 Online 能力族组合（零 Settings JSON——默认投影开箱即通），好友桥建立关系 → 撮合桥双玩家入队 →
/// 对局桥开局 / 出拳决胜 → 结算发奖投影 → 默认榜出现条目。桥契约槽位是进程级静态状态，入
/// <see cref="HotfixBridgeTestCollection"/> 与其余域桥用例串行。
/// </summary>
[Collection(HotfixBridgeTestCollection.Name)]
public sealed class GoldenPathBridgeChainTests
{
    /// <summary>测试区服标识（族配置段一致）。</summary>
    private const long ServerId = 1001;

    /// <summary>玩家一（好友请求方 / 胜方，连出石头两轮）。</summary>
    private const long PlayerOne = 1001;

    /// <summary>玩家二（好友接受方 / 负方，连出剪刀两轮）。</summary>
    private const long PlayerTwo = 1002;

    /// <summary>房间标识（确定性对局标识派生入参）。</summary>
    private const long RoomId = 5001;

    /// <summary>局号（同房同局幂等）。</summary>
    private const int Round = 1;

    /// <summary>驱动的最大 tick 轮数（每轮覆盖撮合 / 对局 / 结算 / 投影重投 job）。</summary>
    private const int MaxTickRounds = 10;

    /// <summary>
    /// 验证零 JSON 默认配置下桥全链：好友桥 → 撮合桥 → 对局桥 → 结算发奖 → 默认榜条目出现。
    /// </summary>
    [Fact]
    public async Task GoldenPath_ZeroSettings_FriendBridge_MatchBridge_RpsMatch_ProjectsOntoDefaultBoard()
    {
        CapabilityRuntime runtime = null;
        try
        {
            var section = JsonDocument.Parse("{\"ServerId\":1001}").RootElement.Clone();
            var options = new CapabilityCompositionOptions(section, new HotfixBridgeAssemblyLog())
            {
                HostOptions = new CapabilityHostOptions { ServerId = ServerId, SchedulerIntervalMilliseconds = 3600000, },
                Filter = new CapabilityAssemblyFilter(new[] { "online-foundation", "online-social", "online-matchmaking", "online-match", "online-assets", "online-leaderboard", "online-competition" }),
            };
            options.Override<IOnlinePlayerDirectory>(new EmptyPlayerDirectoryStub());
            options.Override<IOnlineHotfixRollbackExecutor>(new NoopHotfixRollbackExecutorStub());
            options.Configure<OnlineMatchOptions>(match => match.AddGame(new OnlineRockPaperScissorsGame()));

            runtime = CapabilityComposition.Compose(new OnlineCapabilityFamily(), options);
            await runtime.Host.StartAsync();

            // —— 好友桥（C244）：请求 → 接受 → 双方好友列表可见。
            var sendRequest = new RespFriendSendRequest();
            await FriendDomainBridge.SendRequestAsync(PlayerOne, PlayerTwo, sendRequest);
            Assert.Equal(0, sendRequest.ErrorCode);

            var accept = new RespFriendAccept();
            await FriendDomainBridge.AcceptAsync(PlayerTwo, PlayerOne, accept);
            Assert.Equal(0, accept.ErrorCode);

            var friendList = new RespFriendList();
            await FriendDomainBridge.QueryFriendListAsync(PlayerOne, friendList);
            Assert.Equal(0, friendList.ErrorCode);
            Assert.Contains(friendList.Friends, friend => friend.PlayerId == PlayerTwo);

            // —— 撮合桥（C246）：双玩家入队 1v1 匹配池。
            foreach (var player in new[] { PlayerOne, PlayerTwo })
            {
                var enqueueResponse = new RespMatchEnqueue();
                await MatchDomainBridge.EnqueueAsync(player, new ReqMatchEnqueue { Mode = MatchMode.OneVsOne, Rating = 1000, }, enqueueResponse);
                Assert.Equal(0, enqueueResponse.ErrorCode);
            }

            // —— 对局桥：开局（确定性对局标识）+ 双方各两轮出拳（石头胜剪刀两轮 = 决胜）。
            Assert.True(RockPaperScissorsMatchBridge.IsBridgeEnabled, "online-match 装配后对局桥应在场接管");

            var startResponse = new RespStartRoomGame();
            var handled = await RockPaperScissorsMatchBridge.TryStartRoomGameAsync(PlayerOne, RoomId, Round, new List<long> { PlayerOne, PlayerTwo }, startResponse);
            Assert.True(handled);
            Assert.Equal(0, startResponse.ErrorCode);

            for (var roundIndex = 0; roundIndex < 2; roundIndex++)
            {
                foreach (var player in new[] { PlayerOne, PlayerTwo })
                {
                    var gesture = player == PlayerOne ? (int)RockPaperScissorsGesture.Rock : (int)RockPaperScissorsGesture.Scissors;
                    var submitResponse = new RespSubmitRockPaperScissorsGesture();
                    var (gestureHandled, _) = await RockPaperScissorsMatchBridge.TrySubmitGestureAsync(player, RoomId, Round, gesture, submitResponse);
                    Assert.True(gestureHandled);
                    Assert.Equal(0, submitResponse.ErrorCode);
                }

                // 本轮双方已提交：驱动一轮 tick 裁决本轮，开启下一轮。
                await runtime.Host.Scheduler.TickAsync();
            }

            // —— 结算 → 发奖 → 投影：显式驱动调度轮直至默认榜出现条目。
            var leaderboardStore = runtime.Host.Services.GetService<IOnlineLeaderboardStore>();
            OnlineLeaderboardEntry winnerEntry = null;
            for (var tickRound = 0; tickRound < MaxTickRounds; tickRound++)
            {
                await runtime.Host.Scheduler.TickAsync();
                winnerEntry = await leaderboardStore.FindEntryAsync(OnlineLeaderboardProjectionOptions.DefaultMatchSettledBoardId, PlayerOne);
                if (winnerEntry != null)
                {
                    break;
                }
            }

            Assert.NotNull(winnerEntry);
            var loserEntry = await leaderboardStore.FindEntryAsync(OnlineLeaderboardProjectionOptions.DefaultMatchSettledBoardId, PlayerTwo);
            Assert.NotNull(loserEntry);
            Assert.True(winnerEntry.Score >= loserEntry.Score, "winner score (" + winnerEntry.Score + ") should rank above loser score (" + loserEntry.Score + ")");
        }
        finally
        {
            if (runtime != null)
            {
                await runtime.StopAsync();
            }
        }
    }

    /// <summary>
    /// 空玩家目录桩（社交能力的展示名检索宿主扩展点：黄金路径不消费搜索结果）。
    /// </summary>
    private sealed class EmptyPlayerDirectoryStub : IOnlinePlayerDirectory
    {
        /// <summary>按名检索恒返回空（黄金路径不消费）。</summary>
        public Task<IReadOnlyList<OnlinePlayerDirectoryEntry>> SearchByNameAsync(string keyword, int limit, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<OnlinePlayerDirectoryEntry>>(Array.Empty<OnlinePlayerDirectoryEntry>());
        }

        /// <summary>按标识批量解析恒返回空（好友列表展示名回退标识字面量）。</summary>
        public Task<IReadOnlyList<OnlinePlayerDirectoryEntry>> FindAsync(IReadOnlyList<long> playerIds, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<OnlinePlayerDirectoryEntry>>(Array.Empty<OnlinePlayerDirectoryEntry>());
        }
    }

    /// <summary>
    /// 空操作热更回滚执行器桩（Online 运营能力的宿主扩展点：黄金路径不触发回滚）。
    /// </summary>
    private sealed class NoopHotfixRollbackExecutorStub : IOnlineHotfixRollbackExecutor
    {
        /// <summary>恒完成（黄金路径不触发回滚）。</summary>
        public Task RollbackAsync(string targetVersion, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
