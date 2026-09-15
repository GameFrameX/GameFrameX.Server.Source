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
using GameFrameX.Online.Assets;
using GameFrameX.Online.Contracts;
using GameFrameX.Online.Events;
using GameFrameX.Online.Leaderboard;
using GameFrameX.Online.Season;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 赛季生命周期测试（vault:C8 S7.3 / VC-7.5-a～d：开始/结束时间、分数重置、历史快照）。
    /// 覆盖快照先行不丢历史、CAS 重置不丢分、重试耗尽零损失、重置后读缓存不返陈旧榜、状态机与作用域隔离。
    /// </summary>
    public class OnlineSeasonLifecycleTests
    {
        /// <summary>
        /// 验证 VC-7.5-a：全流程「创建 → 开始 → 结束」，结束时先落快照再清空榜单，名次与分数完整冻结，
        /// 快照可回溯，三类生命周期事件按落定点发布（未结算不发 Settled）。
        /// </summary>
        [Fact]
        public async Task Lifecycle_HappyPath_ShouldSnapshotThenResetAndPublishEvents()
        {
            var harness = new OnlineSeasonTestHarness();
            await harness.CreateBoardAsync("board-1");
            await harness.WriteScoresAsync("board-1", (OnlineSeasonTestHarness.PlayerOne, 300), (OnlineSeasonTestHarness.PlayerTwo, 200), (OnlineSeasonTestHarness.PlayerThree, 100));

            var created = await harness.CreateSeasonAsync("s1", "board-1", OnlineSeasonTestHarness.Rule(1, 1, 1000), OnlineSeasonTestHarness.Rule(2, 3, 500));
            Assert.True(created.IsSuccess, created.Message);
            Assert.Equal(OnlineSeasonState.Scheduled, created.Data.State);

            var started = await harness.StartSeasonAsync("s1");
            Assert.True(started.IsSuccess, started.Message);
            Assert.Equal(OnlineSeasonState.Active, started.Data.State);

            var ended = await harness.EndSeasonAsync("s1");

            Assert.True(ended.IsSuccess, ended.Message);
            Assert.Equal(3, ended.Data.Entries.Count);
            Assert.Equal(OnlineSeasonTestHarness.PlayerOne, ended.Data.Entries[0].Entry.PlayerId);
            Assert.Equal(1, ended.Data.Entries[0].Rank);
            Assert.Equal(300, ended.Data.Entries[0].Entry.Score);
            Assert.Equal(OnlineSeasonTestHarness.PlayerTwo, ended.Data.Entries[1].Entry.PlayerId);
            Assert.Equal(2, ended.Data.Entries[1].Rank);
            Assert.Equal(OnlineSeasonTestHarness.PlayerThree, ended.Data.Entries[2].Entry.PlayerId);
            Assert.Equal(3, ended.Data.Entries[2].Rank);

            // 结束后：赛季推进到已结束，榜单条目已清空（分数重置）。
            Assert.Equal(OnlineSeasonState.Ended, await harness.SeasonStateAsync("s1"));
            Assert.Empty(await harness.BoardEntriesAsync("board-1"));

            // 历史快照可回溯（重置后旧榜仍可读）。
            var snapshot = await harness.SeasonService.GetSnapshotAsync(harness.Scope(), "s1");
            Assert.True(snapshot.IsSuccess, snapshot.Message);
            Assert.Equal(3, snapshot.Data.Entries.Count);

            // 事件：创建 / 开始 / 结束各一次；未结算不发结算事件。
            Assert.Single(harness.Recorder.Filter(OnlineSeasonEvents.SeasonCreated));
            Assert.Single(harness.Recorder.Filter(OnlineSeasonEvents.SeasonStarted));
            var endedEvents = harness.Recorder.Filter(OnlineSeasonEvents.SeasonEnded);
            Assert.Single(endedEvents);
            Assert.Equal("3", endedEvents[0].PayloadAuditFields["SnapshotEntryCount"]);
            Assert.Equal(ended.Data.SnapshottedTime.ToString(), endedEvents[0].PayloadAuditFields["SnapshottedTime"]);
            Assert.Empty(harness.Recorder.Filter(OnlineSeasonEvents.SeasonSettled));
        }

        /// <summary>
        /// 验证 VC-7.5-b：快照一经落档即冻结——下一届赛季写入新成绩不得改写上一届的历史快照，
        /// 且冻结名次与各字段与重置前逐项一致（历史不丢也不被覆盖）。
        /// </summary>
        [Fact]
        public async Task EndAsync_AfterNewSeasonWrites_ShouldKeepSnapshotFrozen()
        {
            var harness = new OnlineSeasonTestHarness();
            await harness.CreateBoardAsync("board-freeze");
            await harness.WriteScoresAsync("board-freeze", (OnlineSeasonTestHarness.PlayerOne, 300), (OnlineSeasonTestHarness.PlayerTwo, 200));
            await harness.CreateSeasonAsync("s1", "board-freeze", OnlineSeasonTestHarness.Rule(1, 2, 100));
            await harness.StartSeasonAsync("s1");

            var ended = await harness.EndSeasonAsync("s1");
            Assert.True(ended.IsSuccess, ended.Message);
            var frozenTime = ended.Data.SnapshottedTime;

            // 新一届赛季在同一榜单上写入（分数更高、名次不同）。
            await harness.WriteScoresAsync("board-freeze", (OnlineSeasonTestHarness.PlayerTwo, 900));

            var reread = await harness.SeasonService.GetSnapshotAsync(harness.Scope(), "s1");

            Assert.True(reread.IsSuccess, reread.Message);
            Assert.Equal(frozenTime, reread.Data.SnapshottedTime);
            Assert.Equal(2, reread.Data.Entries.Count);
            Assert.Equal(OnlineSeasonTestHarness.PlayerOne, reread.Data.Entries[0].Entry.PlayerId);
            Assert.Equal(300, reread.Data.Entries[0].Entry.Score);
            Assert.Equal(OnlineSeasonTestHarness.PlayerTwo, reread.Data.Entries[1].Entry.PlayerId);
            Assert.Equal(200, reread.Data.Entries[1].Entry.Score);

            // 当届榜单只含新写入的成绩（旧榜已重置，新成绩不受历史快照影响）。
            var board = await harness.BoardEntriesAsync("board-freeze");
            Assert.Single(board);
            Assert.Equal(OnlineSeasonTestHarness.PlayerTwo, board[0].PlayerId);
            Assert.Equal(900, board[0].Score);
        }

        /// <summary>
        /// 验证 VC-7.5-c 半边一：快照与清空之间被并发写入插入时，CAS 拒绝清空并重读重拍快照后重试成功——
        /// 晚到的成绩既进快照也不被丢弃，落档快照与被清空的条目集合完全一致。
        /// </summary>
        [Fact]
        public async Task EndAsync_ConcurrentWriteBetweenSnapshotAndReset_ShouldRetryAndKeepLateScore()
        {
            InterleavingLeaderboardStore interleaving = null;
            var harness = new OnlineSeasonTestHarness(inner => interleaving = new InterleavingLeaderboardStore(inner) { InterleaveOnResetAttempts = 1 });
            interleaving.LateEntries.Add((OnlineSeasonTestHarness.PlayerThree, 250));

            await harness.CreateBoardAsync("board-race");
            await harness.WriteScoresAsync("board-race", (OnlineSeasonTestHarness.PlayerOne, 300), (OnlineSeasonTestHarness.PlayerTwo, 200));
            await harness.CreateSeasonAsync("s1", "board-race", OnlineSeasonTestHarness.Rule(1, 3, 100));
            await harness.StartSeasonAsync("s1");

            var ended = await harness.EndSeasonAsync("s1");

            Assert.True(ended.IsSuccess, ended.Message);
            Assert.Equal(2, interleaving.ResetAttempts);

            // 晚到的 250 分以第 2 名进快照（300 > 250 > 200），历史未丢。
            Assert.Equal(3, ended.Data.Entries.Count);
            Assert.Equal(OnlineSeasonTestHarness.PlayerOne, ended.Data.Entries[0].Entry.PlayerId);
            Assert.Equal(OnlineSeasonTestHarness.PlayerThree, ended.Data.Entries[1].Entry.PlayerId);
            Assert.Equal(2, ended.Data.Entries[1].Rank);
            Assert.Equal(250, ended.Data.Entries[1].Entry.Score);
            Assert.Equal(OnlineSeasonTestHarness.PlayerTwo, ended.Data.Entries[2].Entry.PlayerId);

            // 落档快照与被清空的条目集合一致：榜单清空且不残留竞态写入。
            var snapshot = await harness.SeasonService.GetSnapshotAsync(harness.Scope(), "s1");
            Assert.Equal(3, snapshot.Data.Entries.Count);
            Assert.Empty(await harness.BoardEntriesAsync("board-race"));
            Assert.Equal(OnlineSeasonState.Ended, await harness.SeasonStateAsync("s1"));
        }

        /// <summary>
        /// 验证 VC-7.5-c 半边二：并发写入持续存在时重试有界收敛——返回可重试错误且榜单逐条无损、
        /// 赛季保持进行中（宁可拒绝重置也不丢一条真实成绩）。
        /// </summary>
        [Fact]
        public async Task EndAsync_PersistentConcurrentWrites_ShouldBeBusyWithoutLosingScores()
        {
            InterleavingLeaderboardStore interleaving = null;
            var harness = new OnlineSeasonTestHarness(inner => interleaving = new InterleavingLeaderboardStore(inner) { InterleaveOnResetAttempts = 3 });
            interleaving.LateEntries.Add((OnlineSeasonTestHarness.PlayerThree, 250));
            interleaving.LateEntries.Add((1004, 150));
            interleaving.LateEntries.Add((1005, 50));

            await harness.CreateBoardAsync("board-busy");
            await harness.WriteScoresAsync("board-busy", (OnlineSeasonTestHarness.PlayerOne, 300), (OnlineSeasonTestHarness.PlayerTwo, 200));
            await harness.CreateSeasonAsync("s1", "board-busy", OnlineSeasonTestHarness.Rule(1, 5, 100));
            await harness.StartSeasonAsync("s1");

            var ended = await harness.EndSeasonAsync("s1");

            Assert.False(ended.IsSuccess);
            Assert.Equal(OnlineErrorCode.ServiceBusy, ended.Code);
            Assert.Equal(3, interleaving.ResetAttempts);

            // 零损失：两次原始成绩 + 三次竞态写入全部仍在榜上，赛季未被推进。
            var board = await harness.BoardEntriesAsync("board-busy");
            Assert.Equal(5, board.Count);
            Assert.Equal(OnlineSeasonState.Active, await harness.SeasonStateAsync("s1"));

            // 可重试：停止注入后同一赛季可正常结束。
            interleaving.InterleaveOnResetAttempts = 0;
            var retried = await harness.EndSeasonAsync("s1");

            Assert.True(retried.IsSuccess, retried.Message);
            Assert.Equal(5, retried.Data.Entries.Count);
            Assert.Empty(await harness.BoardEntriesAsync("board-busy"));
        }

        /// <summary>
        /// 验证重置必须让读缓存失效：启用 Top N 缓存时，若重置后不清缓存，旧榜会在 TTL 内继续对外返回
        /// ——已重置的榜单仍能查到历史条目属于「分数重置未生效」的对外可见缺陷。
        /// </summary>
        [Fact]
        public async Task EndAsync_AfterReset_TopQueryMustNotServeStaleCache()
        {
            var harness = new OnlineSeasonTestHarness();
            await harness.CreateBoardAsync("board-cache");
            await harness.WriteScoresAsync("board-cache", (OnlineSeasonTestHarness.PlayerOne, 300));

            var before = await harness.LeaderboardService.GetTopAsync(harness.Scope(), "board-cache", 10, null, OnlineSeasonTestHarness.Now + 500);
            Assert.True(before.IsSuccess, before.Message);
            Assert.Single(before.Data.Entries);

            await harness.CreateSeasonAsync("s1", "board-cache", OnlineSeasonTestHarness.Rule(1, 1, 100));
            await harness.StartSeasonAsync("s1");
            var ended = await harness.EndSeasonAsync("s1");
            Assert.True(ended.IsSuccess, ended.Message);

            var after = await harness.LeaderboardService.GetTopAsync(harness.Scope(), "board-cache", 10, null, OnlineSeasonTestHarness.Now + 600);

            Assert.True(after.IsSuccess, after.Message);
            Assert.Empty(after.Data.Entries);
            Assert.Equal(0, after.Data.TotalCount);
        }

        /// <summary>
        /// 验证 VC-7.5-d：状态机只允许 Scheduled → Active → Ended → Settled——跳级、回退与终态后再推进
        /// 一律拒绝，且拒绝路径不产生任何状态副作用。
        /// </summary>
        [Fact]
        public async Task StateMachine_IllegalTransitions_ShouldBeRejected()
        {
            var harness = new OnlineSeasonTestHarness();
            await harness.CreateBoardAsync("board-sm");
            await harness.WriteScoresAsync("board-sm", (OnlineSeasonTestHarness.PlayerOne, 300));
            await harness.CreateSeasonAsync("s1", "board-sm", OnlineSeasonTestHarness.Rule(1, 1, 100));

            // Scheduled 跳级结束：拒绝。
            var skipped = await harness.EndSeasonAsync("s1");
            Assert.False(skipped.IsSuccess);
            Assert.Equal(OnlineErrorCode.StateOperationForbidden, skipped.Code);

            // Scheduled 直接结算：拒绝（未结束）。
            var earlySettle = await harness.SettleSeasonAsync("s1");
            Assert.False(earlySettle.IsSuccess);
            Assert.Equal(OnlineErrorCode.StateNotReady, earlySettle.Code);

            Assert.True((await harness.StartSeasonAsync("s1")).IsSuccess);

            // Active 重复开始：拒绝。
            var restarted = await harness.StartSeasonAsync("s1");
            Assert.False(restarted.IsSuccess);
            Assert.Equal(OnlineErrorCode.StateOperationForbidden, restarted.Code);

            // Active 直接结算：拒绝（未结束）。
            var activeSettle = await harness.SettleSeasonAsync("s1");
            Assert.False(activeSettle.IsSuccess);
            Assert.Equal(OnlineErrorCode.StateNotReady, activeSettle.Code);

            Assert.True((await harness.EndSeasonAsync("s1")).IsSuccess);

            // Ended 回退开始 / 重复结束：拒绝。
            var backToActive = await harness.StartSeasonAsync("s1");
            Assert.False(backToActive.IsSuccess);
            Assert.Equal(OnlineErrorCode.StateOperationForbidden, backToActive.Code);
            var endedTwice = await harness.EndSeasonAsync("s1");
            Assert.False(endedTwice.IsSuccess);
            Assert.Equal(OnlineErrorCode.StateOperationForbidden, endedTwice.Code);

            Assert.True((await harness.SettleSeasonAsync("s1")).IsSuccess);

            // Settled 为终态：任何推进均拒绝。
            Assert.False((await harness.StartSeasonAsync("s1")).IsSuccess);
            Assert.False((await harness.EndSeasonAsync("s1")).IsSuccess);
            Assert.Equal(OnlineSeasonState.Settled, await harness.SeasonStateAsync("s1"));

            // 拒绝路径无副作用：榜单未被误清、也只发过一对开始/结束事件。
            Assert.Single(harness.Recorder.Filter(OnlineSeasonEvents.SeasonStarted));
            Assert.Single(harness.Recorder.Filter(OnlineSeasonEvents.SeasonEnded));
        }

        /// <summary>
        /// 验证反预言：跨 App 作用域访问他 App 赛季的四种操作一律与「赛季不存在」同构返回 ResourceNotFound，
        /// 既不泄露存在性也不产生副作用。
        /// </summary>
        [Fact]
        public async Task CrossApp_SeasonOperations_ShouldBeResourceNotFound()
        {
            var harness = new OnlineSeasonTestHarness();
            await harness.CreateBoardAsync("board-x");
            await harness.WriteScoresAsync("board-x", (OnlineSeasonTestHarness.PlayerOne, 300));
            await harness.CreateSeasonAsync("s1", "board-x", OnlineSeasonTestHarness.Rule(1, 1, 100));
            await harness.StartSeasonAsync("s1");

            var scope = harness.Scope(OnlineSeasonTestHarness.OtherAppId);
            var start = await harness.SeasonService.StartAsync(scope, "s1", OnlineSeasonTestHarness.Now + 400);
            var end = await harness.SeasonService.EndAsync(scope, "s1", OnlineSeasonTestHarness.Now + 400);
            var snapshot = await harness.SeasonService.GetSnapshotAsync(scope, "s1");
            var settle = await harness.SeasonService.SettleAsync(scope, "s1", OnlineSeasonTestHarness.Now + 400);

            Assert.Equal(OnlineErrorCode.ResourceNotFound, start.Code);
            Assert.Equal(OnlineErrorCode.ResourceNotFound, end.Code);
            Assert.Equal(OnlineErrorCode.ResourceNotFound, snapshot.Code);
            Assert.Equal(OnlineErrorCode.ResourceNotFound, settle.Code);

            // 本 App 赛季不受影响：仍在进行中，榜单未被误清。
            Assert.Equal(OnlineSeasonState.Active, await harness.SeasonStateAsync("s1"));
            Assert.Single(await harness.BoardEntriesAsync("board-x"));
        }

        /// <summary>
        /// 验证创建校验红线：名次区间重叠 / 名次区间非法 / 奖励明细为空 / 排期倒挂 / 赛季标识重复
        /// 一律拒绝，且拒绝后不落任何赛季定义。
        /// </summary>
        /// <param name="caseName">用例名（决定构造哪种非法请求）。</param>
        [Theory]
        [InlineData("overlap")]
        [InlineData("from-rank-zero")]
        [InlineData("empty-rewards")]
        [InlineData("reversed-schedule")]
        [InlineData("duplicate")]
        public async Task CreateAsync_InvalidRequest_ShouldBeParameterInvalid(string caseName)
        {
            var harness = new OnlineSeasonTestHarness();
            await harness.CreateBoardAsync("board-v");

            var rules = new List<OnlineSeasonRewardRule> { OnlineSeasonTestHarness.Rule(1, 3, 100), OnlineSeasonTestHarness.Rule(3, 5, 50) };
            var startTime = OnlineSeasonTestHarness.Now;
            var endTime = OnlineSeasonTestHarness.Now + 10000;
            var seasonId = "s1";

            if (caseName == "from-rank-zero")
            {
                rules = new List<OnlineSeasonRewardRule> { OnlineSeasonTestHarness.Rule(0, 1, 100) };
            }

            if (caseName == "empty-rewards")
            {
                rules = new List<OnlineSeasonRewardRule>
                {
                    new OnlineSeasonRewardRule { FromRank = 1, ToRank = 1, Rewards = new List<OnlineAssetChangeLine>() },
                };
            }

            if (caseName == "reversed-schedule")
            {
                rules = new List<OnlineSeasonRewardRule> { OnlineSeasonTestHarness.Rule(1, 1, 100) };
                endTime = startTime;
            }

            if (caseName == "duplicate")
            {
                rules = new List<OnlineSeasonRewardRule> { OnlineSeasonTestHarness.Rule(1, 1, 100) };
                var first = await harness.CreateSeasonAsync(seasonId, "board-v", OnlineSeasonTestHarness.Rule(1, 1, 100));
                Assert.True(first.IsSuccess, first.Message);
            }

            var request = new OnlineSeasonCreateRequest
            {
                SeasonId = seasonId,
                LeaderboardId = "board-v",
                StartTime = startTime,
                EndTime = endTime,
                RewardRules = rules,
            };
            var result = await harness.SeasonService.CreateAsync(harness.Scope(), request, OnlineSeasonTestHarness.Now);

            Assert.False(result.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, result.Code);
        }

        /// <summary>
        /// 验证关联榜单不存在时拒绝创建赛季（赛季必须挂在可信事实来源上），且不落赛季定义。
        /// </summary>
        [Fact]
        public async Task CreateAsync_MissingBoard_ShouldBeResourceNotFound()
        {
            var harness = new OnlineSeasonTestHarness();

            var result = await harness.CreateSeasonAsync("s1", "board-missing", OnlineSeasonTestHarness.Rule(1, 1, 100));

            Assert.False(result.IsSuccess);
            Assert.Equal(OnlineErrorCode.ResourceNotFound, result.Code);
            Assert.Null(await harness.SeasonStore.FindAsync(OnlineSeasonTestHarness.TenantId, OnlineSeasonTestHarness.AppId, "s1"));
        }

        /// <summary>
        /// 验证未结束赛季无快照可回溯（快照只由结束流程产出，不提前暴露空榜）。
        /// </summary>
        [Fact]
        public async Task GetSnapshotAsync_BeforeEnd_ShouldBeResourceNotFound()
        {
            var harness = new OnlineSeasonTestHarness();
            await harness.CreateBoardAsync("board-pre");
            await harness.CreateSeasonAsync("s1", "board-pre", OnlineSeasonTestHarness.Rule(1, 1, 100));
            await harness.StartSeasonAsync("s1");

            var snapshot = await harness.SeasonService.GetSnapshotAsync(harness.Scope(), "s1");

            Assert.False(snapshot.IsSuccess);
            Assert.Equal(OnlineErrorCode.ResourceNotFound, snapshot.Code);
        }
    }
}
