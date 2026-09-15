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
using GameFrameX.Online.Contracts;
using GameFrameX.Online.Events;
using GameFrameX.Online.Tournament;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 赛事生命周期测试（vault:C8 S7.4 / VC-7.8：赛事生命周期与报名幂等）。
    /// 覆盖创建校验、报名资格判定与机器可读拒绝、报名幂等不重发事件、状态机门禁、
    /// 结束冻结成绩**且只读榜单**、未报名者不进成绩、跨 App 反预言。
    /// </summary>
    public class OnlineTournamentLifecycleTests
    {
        /// <summary>
        /// 验证 VC-7.8-a：同一玩家重复报名返回既有登记（重放），登记数守恒、报名事件只发一次。
        /// </summary>
        [Fact]
        public async Task RegisterAsync_DuplicateRegistration_ShouldReplayWithoutNewRegistration()
        {
            var harness = new OnlineTournamentTestHarness();
            await harness.CreateBoardAsync("board-reg");
            await harness.WriteScoresAsync("board-reg", (OnlineTournamentTestHarness.PlayerOne, 300));
            await harness.CreateTournamentAsync("t1", "board-reg", null, OnlineTournamentTestHarness.Rule(1, 1, 1000));

            var first = await harness.RegisterAsync("t1", OnlineTournamentTestHarness.PlayerOne);
            var second = await harness.RegisterAsync("t1", OnlineTournamentTestHarness.PlayerOne);

            Assert.True(first.IsSuccess, first.Message);
            Assert.True(first.Data.Accepted);
            Assert.False(first.Data.IsReplay);
            Assert.True(second.IsSuccess, second.Message);
            Assert.True(second.Data.Accepted);
            Assert.True(second.Data.IsReplay);
            Assert.Equal(first.Data.Registration.RegisteredTime, second.Data.Registration.RegisteredTime);

            var registrations = await harness.TournamentStore.ListRegistrationsAsync(OnlineTournamentTestHarness.TenantId, OnlineTournamentTestHarness.AppId, "t1");
            Assert.Single(registrations);
            Assert.Single(harness.Recorder.Filter(OnlineTournamentEvents.TournamentRegistered));
        }

        /// <summary>
        /// 验证 VC-7.8-b：资格条件不满足时以成功回执携带**机器可读拒绝码**返回（不是错误码），
        /// 且不落报名登记、不发报名事件；判定顺序「先名次后分数」：未上榜报 NotRanked。
        /// </summary>
        [Fact]
        public async Task RegisterAsync_EligibilityNotMet_ShouldRejectWithMachineReadableReason()
        {
            var harness = new OnlineTournamentTestHarness();
            await harness.CreateBoardAsync("board-elig");
            await harness.WriteScoresAsync("board-elig", (OnlineTournamentTestHarness.PlayerOne, 300), (OnlineTournamentTestHarness.PlayerTwo, 200), (OnlineTournamentTestHarness.PlayerThree, 100));
            await harness.CreateTournamentAsync("t1", "board-elig", OnlineTournamentTestHarness.Eligibility(2), OnlineTournamentTestHarness.Rule(1, 1, 1000));

            // 名次 1、2 通过；名次 3 超出上限。
            Assert.True((await harness.RegisterAsync("t1", OnlineTournamentTestHarness.PlayerOne)).Data.Accepted);
            Assert.True((await harness.RegisterAsync("t1", OnlineTournamentTestHarness.PlayerTwo)).Data.Accepted);

            var tooLow = await harness.RegisterAsync("t1", OnlineTournamentTestHarness.PlayerThree);

            Assert.True(tooLow.IsSuccess, tooLow.Message);
            Assert.False(tooLow.Data.Accepted);
            Assert.Equal(OnlineTournamentRegistrationRejection.RankTooLow, tooLow.Data.Rejection);

            // 未上榜玩家报 NotRanked（而非「分数不足」）。
            var notRanked = await harness.RegisterAsync("t1", OnlineTournamentTestHarness.PlayerThree + 999);

            Assert.True(notRanked.IsSuccess, notRanked.Message);
            Assert.False(notRanked.Data.Accepted);
            Assert.Equal(OnlineTournamentRegistrationRejection.NotRanked, notRanked.Data.Rejection);

            var registrations = await harness.TournamentStore.ListRegistrationsAsync(OnlineTournamentTestHarness.TenantId, OnlineTournamentTestHarness.AppId, "t1");
            Assert.Equal(2, registrations.Count);
            Assert.Equal(2, harness.Recorder.Filter(OnlineTournamentEvents.TournamentRegistered).Count);
        }

        /// <summary>
        /// 验证 VC-7.8-c：开始赛事后报名窗口关闭；未结束不能结算；终态不可回退。
        /// </summary>
        [Fact]
        public async Task Lifecycle_GateOnState_ShouldForbidOutOfOrderOperations()
        {
            var harness = new OnlineTournamentTestHarness();
            await harness.CreateBoardAsync("board-state");
            await harness.CreateTournamentAsync("t1", "board-state", null, OnlineTournamentTestHarness.Rule(1, 1, 1000));

            // 未结束不能结算。
            var settleEarly = await harness.SettleTournamentAsync("t1");
            Assert.False(settleEarly.IsSuccess);
            Assert.Equal(OnlineErrorCode.StateNotReady, settleEarly.Code);

            Assert.True((await harness.StartTournamentAsync("t1")).IsSuccess);

            var late = await harness.RegisterAsync("t1", OnlineTournamentTestHarness.PlayerOne);
            Assert.False(late.IsSuccess);
            Assert.Equal(OnlineErrorCode.StateOperationForbidden, late.Code);

            // 重复开始：状态机不承认 Scheduled→Active 之外的迁移。
            var startAgain = await harness.StartTournamentAsync("t1");
            Assert.False(startAgain.IsSuccess);
            Assert.Equal(OnlineErrorCode.StateOperationForbidden, startAgain.Code);

            Assert.True((await harness.EndTournamentAsync("t1")).IsSuccess);
            Assert.Equal(OnlineTournamentState.Ended, await harness.TournamentStateAsync("t1"));

            // 已结束的赛事不能回到 Active。
            var startAfterEnd = await harness.StartTournamentAsync("t1");
            Assert.False(startAfterEnd.IsSuccess);
            Assert.Equal(OnlineErrorCode.StateOperationForbidden, startAfterEnd.Code);
        }

        /// <summary>
        /// 验证 VC-7.8-d：结束赛事把关联榜单的**已报名参赛者**名次冻结为赛事成绩，
        /// 且**不写入也不重置**关联榜单（赛事只读榜单——同一榜单可被赛季与赛事共用）。
        /// </summary>
        [Fact]
        public async Task EndAsync_ShouldFreezeStandingsAndKeepLeaderboardIntact()
        {
            var harness = new OnlineTournamentTestHarness();
            await harness.CreateBoardAsync("board-freeze");
            await harness.WriteScoresAsync(
                "board-freeze",
                (OnlineTournamentTestHarness.PlayerOne, 300),
                (OnlineTournamentTestHarness.PlayerTwo, 200),
                (OnlineTournamentTestHarness.PlayerThree, 100));
            await harness.CreateTournamentAsync("t1", "board-freeze", null, OnlineTournamentTestHarness.Rule(1, 2, 500));

            // 只报名玩家一与玩家三（玩家二榜上第二但未报名）；报名窗口在 Scheduled 态开放。
            Assert.True((await harness.RegisterAsync("t1", OnlineTournamentTestHarness.PlayerOne)).Data.Accepted);
            Assert.True((await harness.RegisterAsync("t1", OnlineTournamentTestHarness.PlayerThree)).Data.Accepted);
            await harness.StartTournamentAsync("t1");

            var ended = await harness.EndTournamentAsync("t1");

            Assert.True(ended.IsSuccess, ended.Message);
            Assert.Equal(2, ended.Data.Entries.Count);
            Assert.Equal(OnlineTournamentTestHarness.PlayerOne, ended.Data.Entries[0].Entry.PlayerId);
            Assert.Equal(1, ended.Data.Entries[0].Rank);
            Assert.Equal(OnlineTournamentTestHarness.PlayerThree, ended.Data.Entries[1].Entry.PlayerId);
            Assert.Equal(2, ended.Data.Entries[1].Rank);

            // 未报名者不进赛事成绩。
            Assert.DoesNotContain(ended.Data.Entries, view => view.Entry.PlayerId == OnlineTournamentTestHarness.PlayerTwo);

            // 榜单原样保留（赛事只读榜单，绝不重置）。
            var boardEntries = await harness.BoardEntriesAsync("board-freeze");
            Assert.Equal(3, boardEntries.Count);
            Assert.Equal(OnlineTournamentTestHarness.PlayerOne, boardEntries[0].PlayerId);
            Assert.Equal(OnlineTournamentTestHarness.PlayerTwo, boardEntries[1].PlayerId);
            Assert.Equal(OnlineTournamentTestHarness.PlayerThree, boardEntries[2].PlayerId);

            // 结果查询读同一份冻结成绩。
            var queried = await harness.TournamentService.GetStandingsAsync(harness.Scope(), "t1");
            Assert.True(queried.IsSuccess, queried.Message);
            Assert.Equal(2, queried.Data.Entries.Count);
        }

        /// <summary>
        /// 验证 VC-7.8-e：创建校验（奖励规则区间重叠会重复发奖，必须拒绝）与跨 App 反预言
        /// （跨 App 读写与「赛事不存在」同构返回 ResourceNotFound）。
        /// </summary>
        [Fact]
        public async Task CreateAsync_ShouldRejectOverlappingRulesAndHideCrossAppTournaments()
        {
            var harness = new OnlineTournamentTestHarness();
            await harness.CreateBoardAsync("board-validate");

            var overlapping = await harness.CreateTournamentAsync(
                "t-bad",
                "board-validate",
                null,
                OnlineTournamentTestHarness.Rule(1, 3, 100),
                OnlineTournamentTestHarness.Rule(3, 5, 200));
            Assert.False(overlapping.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, overlapping.Code);

            var missingBoard = await harness.CreateTournamentAsync("t-noboard", "board-absent", null, OnlineTournamentTestHarness.Rule(1, 1, 100));
            Assert.False(missingBoard.IsSuccess);
            Assert.Equal(OnlineErrorCode.ResourceNotFound, missingBoard.Code);

            Assert.True((await harness.CreateTournamentAsync("t1", "board-validate", null, OnlineTournamentTestHarness.Rule(1, 1, 100))).IsSuccess);
            Assert.Equal(OnlineTournamentState.Scheduled, await harness.TournamentStateAsync("t1"));

            // 跨 App：与不存在同构。
            var crossApp = await harness.TournamentService.StartAsync(harness.Scope(OnlineTournamentTestHarness.OtherAppId), "t1");
            Assert.False(crossApp.IsSuccess);
            Assert.Equal(OnlineErrorCode.ResourceNotFound, crossApp.Code);

            var crossAppStandings = await harness.TournamentService.GetStandingsAsync(harness.Scope(OnlineTournamentTestHarness.OtherAppId), "t1");
            Assert.False(crossAppStandings.IsSuccess);
            Assert.Equal(OnlineErrorCode.ResourceNotFound, crossAppStandings.Code);

            Assert.Equal(OnlineTournamentState.Scheduled, await harness.TournamentStateAsync("t1"));
        }

        /// <summary>
        /// 验证 VC-7.8-f：结束事件只陈述计数与时刻（事件是事实不是状态），报名登记可回溯到判定输入。
        /// </summary>
        [Fact]
        public async Task Lifecycle_ShouldPublishAuditableFacts()
        {
            var harness = new OnlineTournamentTestHarness();
            await harness.CreateBoardAsync("board-events");
            await harness.WriteScoresAsync("board-events", (OnlineTournamentTestHarness.PlayerOne, 300));
            await harness.CreateTournamentAsync("t1", "board-events", OnlineTournamentTestHarness.Eligibility(1), OnlineTournamentTestHarness.Rule(1, 1, 1000));
            await harness.RegisterAsync("t1", OnlineTournamentTestHarness.PlayerOne);
            await harness.StartTournamentAsync("t1");
            await harness.EndTournamentAsync("t1");

            var registered = Assert.Single(harness.Recorder.Filter(OnlineTournamentEvents.TournamentRegistered));
            Assert.Equal(OnlineTournamentTestHarness.PlayerOne, registered.PlayerId);
            Assert.Equal("t1", registered.CorrelationId);
            Assert.Equal(OnlineTournamentEvents.Source, registered.Source);

            // 报名时刻冻结的判定输入随登记留存（事后榜单变化不影响已通过的报名）。
            var stored = await harness.TournamentStore.FindRegistrationAsync(OnlineTournamentTestHarness.TenantId, OnlineTournamentTestHarness.AppId, "t1", OnlineTournamentTestHarness.PlayerOne);
            Assert.NotNull(stored);
            Assert.Equal(1, stored.RankAtRegistration);
            Assert.Equal(300, stored.ScoreAtRegistration);

            var ended = Assert.Single(harness.Recorder.Filter(OnlineTournamentEvents.TournamentEnded));
            Assert.Equal("1", ended.PayloadAuditFields["StandingsEntryCount"]);
            Assert.Equal("board-events", ended.PayloadAuditFields["LeaderboardId"]);
            Assert.Equal(OnlineTournamentEvents.Source, ended.Source);

            Assert.Single(harness.Recorder.Filter(OnlineTournamentEvents.TournamentCreated));
            Assert.Single(harness.Recorder.Filter(OnlineTournamentEvents.TournamentStarted));
        }

        /// <summary>
        /// 验证 VC-7.8-g：无门槛赛事不读榜单，任何玩家（含未上榜者）都可报名。
        /// </summary>
        [Fact]
        public async Task RegisterAsync_WithoutEligibility_ShouldAcceptUnrankedPlayer()
        {
            var harness = new OnlineTournamentTestHarness();
            await harness.CreateBoardAsync("board-open");
            await harness.CreateTournamentAsync("t1", "board-open", OnlineTournamentTestHarness.Eligibility(), OnlineTournamentTestHarness.Rule(1, 1, 1000));

            var registered = await harness.RegisterAsync("t1", OnlineTournamentTestHarness.PlayerTwo);

            Assert.True(registered.IsSuccess, registered.Message);
            Assert.True(registered.Data.Accepted);
            Assert.Equal(OnlineTournamentRegistrationRejection.None, registered.Data.Rejection);
            Assert.Equal(0, registered.Data.Registration.RankAtRegistration);
        }

        /// <summary>
        /// 验证 VC-7.8-h：报名登记查询与「未报名」反预言同构（返回 ResourceNotFound 而非空对象）。
        /// </summary>
        [Fact]
        public async Task GetRegistrationAsync_UnregisteredPlayer_ShouldReturnNotFound()
        {
            var harness = new OnlineTournamentTestHarness();
            await harness.CreateBoardAsync("board-query");
            await harness.CreateTournamentAsync("t1", "board-query", null, OnlineTournamentTestHarness.Rule(1, 1, 1000));
            await harness.RegisterAsync("t1", OnlineTournamentTestHarness.PlayerOne);

            var found = await harness.TournamentService.GetRegistrationAsync(harness.Scope(), "t1", OnlineTournamentTestHarness.PlayerOne);
            Assert.True(found.IsSuccess, found.Message);
            Assert.Equal(OnlineTournamentTestHarness.PlayerOne, found.Data.PlayerId);

            var missing = await harness.TournamentService.GetRegistrationAsync(harness.Scope(), "t1", OnlineTournamentTestHarness.PlayerTwo);
            Assert.False(missing.IsSuccess);
            Assert.Equal(OnlineErrorCode.ResourceNotFound, missing.Code);
        }

        /// <summary>
        /// 验证 VC-7.8-i：未结束时查询成绩返回 ResourceNotFound（冻结成绩尚未产生，不回落实时榜单）。
        /// </summary>
        [Fact]
        public async Task GetStandingsAsync_BeforeEnd_ShouldReturnNotFound()
        {
            var harness = new OnlineTournamentTestHarness();
            await harness.CreateBoardAsync("board-early");
            await harness.WriteScoresAsync("board-early", (OnlineTournamentTestHarness.PlayerOne, 300));
            await harness.CreateTournamentAsync("t1", "board-early", null, OnlineTournamentTestHarness.Rule(1, 1, 1000));
            await harness.StartTournamentAsync("t1");

            var standings = await harness.TournamentService.GetStandingsAsync(harness.Scope(), "t1");

            Assert.False(standings.IsSuccess);
            Assert.Equal(OnlineErrorCode.ResourceNotFound, standings.Code);
        }

        /// <summary>
        /// 验证 VC-7.8-j：并发重复报名不会落下两条登记（存储层「判定重复 + 落档」在同一临界区）。
        /// </summary>
        [Fact]
        public async Task RegisterAsync_ConcurrentDuplicate_ShouldStoreSingleRegistration()
        {
            var harness = new OnlineTournamentTestHarness();
            await harness.CreateBoardAsync("board-concurrent");
            await harness.CreateTournamentAsync("t1", "board-concurrent", null, OnlineTournamentTestHarness.Rule(1, 1, 1000));

            var tasks = new List<Task<OnlineResult<OnlineTournamentRegistrationOutcome>>>();
            for (var index = 0; index < 8; index++)
            {
                tasks.Add(harness.RegisterAsync("t1", OnlineTournamentTestHarness.PlayerOne));
            }

            var results = await Task.WhenAll(tasks);

            foreach (var result in results)
            {
                Assert.True(result.IsSuccess, result.Message);
                Assert.True(result.Data.Accepted);
            }

            Assert.Equal(1, results.Count(result => !result.Data.IsReplay));
            var registrations = await harness.TournamentStore.ListRegistrationsAsync(OnlineTournamentTestHarness.TenantId, OnlineTournamentTestHarness.AppId, "t1");
            Assert.Single(registrations);
        }
    }
}
