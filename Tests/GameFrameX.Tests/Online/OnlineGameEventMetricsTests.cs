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
using GameFrameX.Online.Events;
using GameFrameX.Online.GameEvents;
using GameFrameX.Online.Match;
using GameFrameX.Online.Session;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 游戏事件指标复算测试（vault:C8 S7.5 / VC-7.10：事件可查询、指标可由原始事件复算，可复算偏差 0）。
    /// 覆盖分类 / 事件名计数、重复复算结果恒等、时间窗闭区间边界、作用域隔离、死信计数。
    /// </summary>
    public class OnlineGameEventMetricsTests
    {
        /// <summary>
        /// 验证 VC-7.10-a：指标由原始事件现场复算，**重复复算结果恒等**（偏差 0），
        /// 且与存储中的事件总数守恒（不丢不重）。
        /// </summary>
        [Fact]
        public async Task ComputeAsync_RecomputedFromRawEvents_ShouldBeIdentical()
        {
            var harness = new OnlineGameEventTestHarness();
            await ProjectSampleTrafficAsync(harness);

            var first = await harness.MetricsService.ComputeAsync(OnlineGameEventTestHarness.TenantId, OnlineGameEventTestHarness.AppId, 0, long.MaxValue);
            var second = await harness.MetricsService.ComputeAsync(OnlineGameEventTestHarness.TenantId, OnlineGameEventTestHarness.AppId, 0, long.MaxValue);

            var stored = await harness.StoredEventsAsync();

            Assert.Equal(stored.Count, first.TotalCount);
            Assert.Equal(first.TotalCount, second.TotalCount);
            Assert.Equal(first.RejectedCount, second.RejectedCount);
            Assert.Equal(first.CountByCategory, second.CountByCategory);
            Assert.Equal(first.CountByName, second.CountByName);
            Assert.Equal(first.ActivePlayerCount, second.ActivePlayerCount);
            Assert.Equal(0, first.RejectedCount);

            // 活跃玩家数是去重口径：样例流量含 1 条会话开始（玩家一）→ 1；
            // 再补玩家一的第二条会话开始（重连）与玩家二的一条 → 仍去重为 2 人。
            Assert.Equal(1, first.ActivePlayerCount);
            await harness.Ingestor.IngestAsync(OnlineGameEventTestHarness.BuildEnvelope(OnlineGameEventName.SessionStart, OnlineGameEventTestHarness.CompleteFields(OnlineGameEventName.SessionStart), 1, OnlineGameEventTestHarness.TenantId, OnlineGameEventTestHarness.AppId, OnlineGameEventTestHarness.Now + 10, OnlineGameEventTestHarness.PlayerOne), OnlineGameEventTestHarness.Now + 10);
            await harness.Ingestor.IngestAsync(OnlineGameEventTestHarness.BuildEnvelope(OnlineGameEventName.SessionStart, OnlineGameEventTestHarness.CompleteFields(OnlineGameEventName.SessionStart), 1, OnlineGameEventTestHarness.TenantId, OnlineGameEventTestHarness.AppId, OnlineGameEventTestHarness.Now + 11, OnlineGameEventTestHarness.PlayerTwo), OnlineGameEventTestHarness.Now + 11);

            var withReconnect = await harness.MetricsService.ComputeAsync(OnlineGameEventTestHarness.TenantId, OnlineGameEventTestHarness.AppId, 0, long.MaxValue);

            Assert.Equal(2, withReconnect.ActivePlayerCount);
            Assert.Equal(3, withReconnect.GetNameCount(OnlineGameEventName.SessionStart));
        }

        /// <summary>
        /// 验证 VC-7.10-b：分类由登记表归属推导（调用方不指定），六类计数逐一正确。
        /// </summary>
        [Fact]
        public async Task ComputeAsync_ShouldCountByRegisteredCategory()
        {
            var harness = new OnlineGameEventTestHarness();
            await ProjectSampleTrafficAsync(harness);

            var metrics = await harness.MetricsService.ComputeAsync(OnlineGameEventTestHarness.TenantId, OnlineGameEventTestHarness.AppId, 0, long.MaxValue);

            Assert.Equal(2, metrics.GetCategoryCount(OnlineGameEventCategory.Login));
            Assert.Equal(1, metrics.GetCategoryCount(OnlineGameEventCategory.Matchmaking));
            Assert.Equal(3, metrics.GetCategoryCount(OnlineGameEventCategory.Match));
            Assert.Equal(1, metrics.GetCategoryCount(OnlineGameEventCategory.Reward));
            Assert.Equal(1, metrics.GetCategoryCount(OnlineGameEventCategory.Payment));
            Assert.Equal(1, metrics.GetCategoryCount(OnlineGameEventCategory.Report));

            Assert.Equal(1, metrics.GetNameCount(OnlineGameEventName.Login));
            Assert.Equal(1, metrics.GetNameCount(OnlineGameEventName.SessionStart));
            Assert.Equal(1, metrics.GetNameCount(OnlineGameEventName.MatchQueue));
            Assert.Equal(1, metrics.GetNameCount(OnlineGameEventName.MatchEnd));
            Assert.Equal(1, metrics.GetNameCount(OnlineGameEventName.Win));
            Assert.Equal(1, metrics.GetNameCount(OnlineGameEventName.Lose));
            Assert.Equal(1, metrics.GetNameCount(OnlineGameEventName.CurrencySpend));
            Assert.Equal(1, metrics.GetNameCount(OnlineGameEventName.Purchase));
            Assert.Equal(1, metrics.GetNameCount(OnlineGameEventName.ChatReport));

            // 未出现的事件名计数为 0（未登记的查询同样安全）。
            Assert.Equal(0, metrics.GetNameCount(OnlineGameEventName.MailOpen));
            Assert.Equal(0, metrics.GetNameCount("NotARegisteredEvent"));
            Assert.Equal(0, metrics.GetNameCount(null));
        }

        /// <summary>
        /// 验证 VC-7.10-c：时间窗为闭区间——窗口两端的事件被计入，窗口外的不计。
        /// </summary>
        [Fact]
        public async Task ComputeAsync_Window_ShouldIncludeBothBoundaries()
        {
            var harness = new OnlineGameEventTestHarness();
            await harness.Ingestor.IngestAsync(OnlineGameEventTestHarness.BuildEnvelope(OnlineGameEventName.Login, OnlineGameEventTestHarness.CompleteFields(OnlineGameEventName.Login), 1, OnlineGameEventTestHarness.TenantId, OnlineGameEventTestHarness.AppId, 1000), 1000);
            await harness.Ingestor.IngestAsync(OnlineGameEventTestHarness.BuildEnvelope(OnlineGameEventName.Login, OnlineGameEventTestHarness.CompleteFields(OnlineGameEventName.Login), 1, OnlineGameEventTestHarness.TenantId, OnlineGameEventTestHarness.AppId, 2000), 2000);
            await harness.Ingestor.IngestAsync(OnlineGameEventTestHarness.BuildEnvelope(OnlineGameEventName.Login, OnlineGameEventTestHarness.CompleteFields(OnlineGameEventName.Login), 1, OnlineGameEventTestHarness.TenantId, OnlineGameEventTestHarness.AppId, 3000), 3000);

            var window = await harness.MetricsService.ComputeAsync(OnlineGameEventTestHarness.TenantId, OnlineGameEventTestHarness.AppId, 1000, 2000);

            Assert.Equal(2, window.TotalCount);
            Assert.Equal(1000, window.FromTime);
            Assert.Equal(2000, window.ToTime);

            var everything = await harness.MetricsService.ComputeAsync(OnlineGameEventTestHarness.TenantId, OnlineGameEventTestHarness.AppId, 0, long.MaxValue);
            Assert.Equal(3, everything.TotalCount);
        }

        /// <summary>
        /// 验证 VC-7.10-d：指标与死信均按作用域隔离（跨 App 复算得 0，反预言），
        /// 脏事件计入死信计数而**不污染**事件计数。
        /// </summary>
        [Fact]
        public async Task ComputeAsync_ShouldIsolateScopeAndCountRejectedSeparately()
        {
            var harness = new OnlineGameEventTestHarness();
            await harness.Ingestor.IngestAsync(OnlineGameEventTestHarness.BuildEnvelope(OnlineGameEventName.Win, OnlineGameEventTestHarness.CompleteFields(OnlineGameEventName.Win)), OnlineGameEventTestHarness.Now);
            await harness.Ingestor.IngestAsync(OnlineGameEventTestHarness.BuildEnvelope("NotARegisteredEvent", new Dictionary<string, string>()), OnlineGameEventTestHarness.Now);
            await harness.Ingestor.IngestAsync(OnlineGameEventTestHarness.BuildEnvelope(OnlineGameEventName.Win, OnlineGameEventTestHarness.CompleteFields(OnlineGameEventName.Win, "PlayerId")), OnlineGameEventTestHarness.Now);

            var metrics = await harness.MetricsService.ComputeAsync(OnlineGameEventTestHarness.TenantId, OnlineGameEventTestHarness.AppId, 0, long.MaxValue);

            Assert.Equal(1, metrics.TotalCount);
            Assert.Equal(2, metrics.RejectedCount);
            Assert.Equal(1, metrics.GetCategoryCount(OnlineGameEventCategory.Match));

            var otherApp = await harness.MetricsService.ComputeAsync(OnlineGameEventTestHarness.OtherAppId, OnlineGameEventTestHarness.AppId, 0, long.MaxValue);
            Assert.Equal(0, otherApp.TotalCount);
            Assert.Equal(0, otherApp.RejectedCount);
        }

        /// <summary>
        /// 验证 VC-7.10-e：指标快照只反映窗口内事实，不携带跨窗口累计状态——
        /// 同一批事件切不同窗口复算，各窗口之和等于全窗口（无隐藏状态导致的漂移）。
        /// </summary>
        [Fact]
        public async Task ComputeAsync_ShouldCarryNoCrossWindowState()
        {
            var harness = new OnlineGameEventTestHarness();
            await harness.Ingestor.IngestAsync(OnlineGameEventTestHarness.BuildEnvelope(OnlineGameEventName.Login, OnlineGameEventTestHarness.CompleteFields(OnlineGameEventName.Login), 1, OnlineGameEventTestHarness.TenantId, OnlineGameEventTestHarness.AppId, 1000), 1000);
            await harness.Ingestor.IngestAsync(OnlineGameEventTestHarness.BuildEnvelope(OnlineGameEventName.Login, OnlineGameEventTestHarness.CompleteFields(OnlineGameEventName.Login), 1, OnlineGameEventTestHarness.TenantId, OnlineGameEventTestHarness.AppId, 2000), 2000);

            var firstHalf = await harness.MetricsService.ComputeAsync(OnlineGameEventTestHarness.TenantId, OnlineGameEventTestHarness.AppId, 1000, 1000);
            var secondHalf = await harness.MetricsService.ComputeAsync(OnlineGameEventTestHarness.TenantId, OnlineGameEventTestHarness.AppId, 1001, 2000);
            var whole = await harness.MetricsService.ComputeAsync(OnlineGameEventTestHarness.TenantId, OnlineGameEventTestHarness.AppId, 1000, 2000);

            Assert.Equal(1, firstHalf.TotalCount);
            Assert.Equal(1, secondHalf.TotalCount);
            Assert.Equal(whole.TotalCount, firstHalf.TotalCount + secondHalf.TotalCount);
            Assert.Equal(1, firstHalf.GetNameCount(OnlineGameEventName.Login));
            Assert.Equal(1, secondHalf.GetNameCount(OnlineGameEventName.Login));
        }

        /// <summary>
        /// 造一批覆盖六类的事件流量（经真实投影器与摄取器，与生产路径同构）。
        /// </summary>
        /// <param name="harness">测试基座。</param>
        /// <returns>完成通知。</returns>
        private static async Task ProjectSampleTrafficAsync(OnlineGameEventTestHarness harness)
        {
            var events = new List<OnlineEvent>();
            events.AddRange(harness.Projector.ProjectSessionEvents(OnlineGameEventTestHarness.Session(OnlineSessionState.Authenticated)));
            events.AddRange(harness.Projector.ProjectSessionEvents(OnlineGameEventTestHarness.Session(OnlineSessionState.Active)));
            events.AddRange(harness.Projector.ProjectMatchQueueEvents(OnlineGameEventTestHarness.Ticket()));
            events.AddRange(harness.Projector.ProjectMatchResultEvents(OnlineGameEventTestHarness.MatchResult(OnlineMatchState.Completed, (OnlineGameEventTestHarness.PlayerOne, true), (OnlineGameEventTestHarness.PlayerTwo, false))));
            events.AddRange(harness.Projector.ProjectAssetTransactionEvents(OnlineGameEventTestHarness.Transaction(GameFrameX.Online.Assets.OnlineAssetChangeSource.PaymentConfirm, GameFrameX.Online.Assets.OnlineGrantOperation.Grant)));
            events.AddRange(harness.Projector.ProjectAssetTransactionEvents(OnlineGameEventTestHarness.Transaction(GameFrameX.Online.Assets.OnlineAssetChangeSource.MatchReward, GameFrameX.Online.Assets.OnlineGrantOperation.Deduct)));
            events.AddRange(harness.Projector.ProjectReportCaseEvents(OnlineGameEventTestHarness.ReportCase(GameFrameX.Online.Social.OnlineReportScene.Chat)));

            var outcomes = await harness.Projector.ProjectAsync(events, OnlineGameEventTestHarness.Now);
            Assert.All(outcomes, outcome => Assert.True(outcome.IsAccepted, outcome.RejectionMessage));
        }
    }
}
