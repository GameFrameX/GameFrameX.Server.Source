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
using System.Linq;
using System.Threading.Tasks;
using GameFrameX.Online.Assets;
using GameFrameX.Online.Events;
using GameFrameX.Online.GameEvents;
using GameFrameX.Online.Match;
using GameFrameX.Online.Session;
using GameFrameX.Online.Social;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 六类事件接入测试（vault:C8 S7.6：登录 / 匹配 / 对局 / 奖励 / 付费 / 举报）。
    /// 覆盖六个投影入口的状态归属规则、只投影已发生事实、投影结果一律经 L0 校验落档且可查询。
    /// </summary>
    public class OnlineGameEventProjectorTests
    {
        /// <summary>
        /// 验证 S7.6-a：会话状态映射到登录 / 会话开始 / 会话结束（Created 尚未产生可陈述事实，不投影）。
        /// </summary>
        [Fact]
        public void ProjectSessionEvents_ShouldMapStateToLoginStartEnd()
        {
            var harness = new OnlineGameEventTestHarness();

            Assert.Empty(harness.Projector.ProjectSessionEvents(OnlineGameEventTestHarness.Session(OnlineSessionState.Created)));

            Assert.Equal(OnlineGameEventName.Login, Single(harness.Projector.ProjectSessionEvents(OnlineGameEventTestHarness.Session(OnlineSessionState.Authenticated))));
            Assert.Equal(OnlineGameEventName.SessionStart, Single(harness.Projector.ProjectSessionEvents(OnlineGameEventTestHarness.Session(OnlineSessionState.Connected))));
            Assert.Equal(OnlineGameEventName.SessionStart, Single(harness.Projector.ProjectSessionEvents(OnlineGameEventTestHarness.Session(OnlineSessionState.Active))));
            Assert.Equal(OnlineGameEventName.SessionStart, Single(harness.Projector.ProjectSessionEvents(OnlineGameEventTestHarness.Session(OnlineSessionState.Reconnecting))));
            Assert.Equal(OnlineGameEventName.SessionEnd, Single(harness.Projector.ProjectSessionEvents(OnlineGameEventTestHarness.Session(OnlineSessionState.Kicked))));

            var ended = harness.Projector.ProjectSessionEvents(OnlineGameEventTestHarness.Session(OnlineSessionState.Expired)).Single();
            Assert.Equal(OnlineGameEventName.SessionEnd, ended.EventType);
            Assert.Equal("Expired", ended.PayloadAuditFields["EndReason"]);
            Assert.Equal(OnlineGameEventTestHarness.PlayerOne, ended.PlayerId);
            Assert.Equal(OnlineGameEventSchema.Source, ended.Source);
        }

        /// <summary>
        /// 验证 S7.6-b：匹配与对局开局的归属规则（未开始的对局不投影，取消 / 失败不是开局）。
        /// </summary>
        [Fact]
        public void ProjectMatchEvents_ShouldOnlyEmitWhenGameActuallyStarted()
        {
            var harness = new OnlineGameEventTestHarness();
            var queued = harness.Projector.ProjectMatchQueueEvents(OnlineGameEventTestHarness.Ticket());
            var queueEvent = Assert.Single(queued);
            Assert.Equal(OnlineGameEventName.MatchQueue, queueEvent.EventType);
            Assert.Equal("ticket-1", queueEvent.CorrelationId);
            Assert.Equal("2", queueEvent.PayloadAuditFields["PlayerCount"]);

            Assert.Empty(harness.Projector.ProjectMatchEvents(OnlineGameEventTestHarness.Match(OnlineMatchState.Created)));
            Assert.Empty(harness.Projector.ProjectMatchEvents(OnlineGameEventTestHarness.Match(OnlineMatchState.Waiting)));
            Assert.Empty(harness.Projector.ProjectMatchEvents(OnlineGameEventTestHarness.Match(OnlineMatchState.Cancelled)));

            var started = Assert.Single(harness.Projector.ProjectMatchEvents(OnlineGameEventTestHarness.Match(OnlineMatchState.Running)));
            Assert.Equal(OnlineGameEventName.MatchStart, started.EventType);
            Assert.Equal("match-1", started.CorrelationId);
        }

        /// <summary>
        /// 验证 S7.6-c：对局结果投影 <c>MatchEnd</c> 一例 + 逐玩家胜负；中断（Aborted）全员记退出。
        /// </summary>
        [Fact]
        public void ProjectMatchResultEvents_ShouldEmitMatchEndAndPerPlayerOutcome()
        {
            var harness = new OnlineGameEventTestHarness();
            var completed = harness.Projector.ProjectMatchResultEvents(
                OnlineGameEventTestHarness.MatchResult(OnlineMatchState.Completed, (OnlineGameEventTestHarness.PlayerOne, true), (OnlineGameEventTestHarness.PlayerTwo, false)));

            Assert.Equal(3, completed.Count);
            Assert.Equal(OnlineGameEventName.MatchEnd, completed[0].EventType);
            Assert.Equal(OnlineGameEventName.Win, completed[1].EventType);
            Assert.Equal(OnlineGameEventName.Lose, completed[2].EventType);
            Assert.Equal(OnlineGameEventTestHarness.PlayerOne, completed[1].PlayerId);
            Assert.Equal(OnlineGameEventTestHarness.PlayerTwo, completed[2].PlayerId);
            Assert.Equal("mrs-1", completed[1].CorrelationId);

            var aborted = harness.Projector.ProjectMatchResultEvents(
                OnlineGameEventTestHarness.MatchResult(OnlineMatchState.Aborted, (OnlineGameEventTestHarness.PlayerOne, false), (OnlineGameEventTestHarness.PlayerTwo, false)));

            Assert.Equal(3, aborted.Count);
            Assert.Equal(OnlineGameEventName.MatchEnd, aborted[0].EventType);
            Assert.Equal(OnlineGameEventName.Abandon, aborted[1].EventType);
            Assert.Equal(OnlineGameEventName.Abandon, aborted[2].EventType);
        }

        /// <summary>
        /// 验证 S7.6-d：资产交易归属（付费确认 → Purchase；扣减 / 撤销 → CurrencySpend；其余 → RewardGrant），
        /// 且**仅成功交易**投影（执行中 / 失败尚未产生资产事实）。
        /// </summary>
        [Fact]
        public void ProjectAssetTransactionEvents_ShouldMapSourceAndOperation()
        {
            var harness = new OnlineGameEventTestHarness();

            Assert.Equal(
                OnlineGameEventName.Purchase,
                Single(harness.Projector.ProjectAssetTransactionEvents(OnlineGameEventTestHarness.Transaction(OnlineAssetChangeSource.PaymentConfirm, OnlineGrantOperation.Grant))));
            Assert.Equal(
                OnlineGameEventName.CurrencySpend,
                Single(harness.Projector.ProjectAssetTransactionEvents(OnlineGameEventTestHarness.Transaction(OnlineAssetChangeSource.AdminOperation, OnlineGrantOperation.Deduct))));
            Assert.Equal(
                OnlineGameEventName.CurrencySpend,
                Single(harness.Projector.ProjectAssetTransactionEvents(OnlineGameEventTestHarness.Transaction(OnlineAssetChangeSource.AdminOperation, OnlineGrantOperation.Revoke))));
            Assert.Equal(
                OnlineGameEventName.RewardGrant,
                Single(harness.Projector.ProjectAssetTransactionEvents(OnlineGameEventTestHarness.Transaction(OnlineAssetChangeSource.MatchReward, OnlineGrantOperation.Grant))));
            Assert.Equal(
                OnlineGameEventName.RewardGrant,
                Single(harness.Projector.ProjectAssetTransactionEvents(OnlineGameEventTestHarness.Transaction(OnlineAssetChangeSource.SystemCompensation, OnlineGrantOperation.Reissue))));

            Assert.Empty(harness.Projector.ProjectAssetTransactionEvents(OnlineGameEventTestHarness.Transaction(OnlineAssetChangeSource.MatchReward, OnlineGrantOperation.Grant, OnlineAssetTransactionState.Executing)));
            Assert.Empty(harness.Projector.ProjectAssetTransactionEvents(OnlineGameEventTestHarness.Transaction(OnlineAssetChangeSource.MatchReward, OnlineGrantOperation.Grant, OnlineAssetTransactionState.Failed)));

            var purchase = harness.Projector.ProjectAssetTransactionEvents(OnlineGameEventTestHarness.Transaction(OnlineAssetChangeSource.PaymentConfirm, OnlineGrantOperation.Grant)).Single();
            Assert.Equal("order-1", purchase.PayloadAuditFields["BusinessOrderId"]);
        }

        /// <summary>
        /// 验证 S7.6-e：举报与处罚归属——只有聊天场景举报投影 <c>ChatReport</c>（事件名是冻结契约，不冒用），
        /// 已撤销的处罚不投影。
        /// </summary>
        [Fact]
        public void ProjectReportEvents_ShouldRespectSceneAndRevocation()
        {
            var harness = new OnlineGameEventTestHarness();
            var chat = Assert.Single(harness.Projector.ProjectReportCaseEvents(OnlineGameEventTestHarness.ReportCase(OnlineReportScene.Chat)));
            Assert.Equal(OnlineGameEventName.ChatReport, chat.EventType);
            Assert.Equal(OnlineGameEventTestHarness.PlayerOne, chat.PlayerId);
            Assert.Equal("report-1", chat.CorrelationId);

            Assert.Empty(harness.Projector.ProjectReportCaseEvents(OnlineGameEventTestHarness.ReportCase(OnlineReportScene.Profile)));
            Assert.Empty(harness.Projector.ProjectReportCaseEvents(OnlineGameEventTestHarness.ReportCase(OnlineReportScene.Group)));

            var penalty = Assert.Single(harness.Projector.ProjectPunishmentEvents(OnlineGameEventTestHarness.Punishment()));
            Assert.Equal(OnlineGameEventName.Penalty, penalty.EventType);
            Assert.Equal("Ban", penalty.PayloadAuditFields["Kind"]);
            Assert.Equal(OnlineGameEventTestHarness.PlayerTwo, penalty.PlayerId);

            Assert.Empty(harness.Projector.ProjectPunishmentEvents(OnlineGameEventTestHarness.Punishment(true)));
        }

        /// <summary>
        /// 验证 S7.6-f：六类投影结果一律经 L0 校验落档（无一条被拒绝），并可按作用域与时间窗查询，
        /// 覆盖六类中的 Server 半边可得事件。
        /// </summary>
        [Fact]
        public async Task ProjectAsync_ShouldIngestAllSixCategoriesWithoutRejection()
        {
            var harness = new OnlineGameEventTestHarness();
            var events = new List<OnlineEvent>();
            events.AddRange(harness.Projector.ProjectSessionEvents(OnlineGameEventTestHarness.Session(OnlineSessionState.Authenticated)));
            events.AddRange(harness.Projector.ProjectSessionEvents(OnlineGameEventTestHarness.Session(OnlineSessionState.Active)));
            events.AddRange(harness.Projector.ProjectSessionEvents(OnlineGameEventTestHarness.Session(OnlineSessionState.Closed)));
            events.AddRange(harness.Projector.ProjectMatchQueueEvents(OnlineGameEventTestHarness.Ticket()));
            events.AddRange(harness.Projector.ProjectMatchEvents(OnlineGameEventTestHarness.Match(OnlineMatchState.Running)));
            events.AddRange(harness.Projector.ProjectMatchResultEvents(OnlineGameEventTestHarness.MatchResult(OnlineMatchState.Completed, (OnlineGameEventTestHarness.PlayerOne, true), (OnlineGameEventTestHarness.PlayerTwo, false))));
            events.AddRange(harness.Projector.ProjectAssetTransactionEvents(OnlineGameEventTestHarness.Transaction(OnlineAssetChangeSource.PaymentConfirm, OnlineGrantOperation.Grant)));
            events.AddRange(harness.Projector.ProjectAssetTransactionEvents(OnlineGameEventTestHarness.Transaction(OnlineAssetChangeSource.MatchReward, OnlineGrantOperation.Deduct)));
            events.AddRange(harness.Projector.ProjectReportCaseEvents(OnlineGameEventTestHarness.ReportCase(OnlineReportScene.Chat)));
            events.AddRange(harness.Projector.ProjectPunishmentEvents(OnlineGameEventTestHarness.Punishment()));

            var outcomes = await harness.Projector.ProjectAsync(events, OnlineGameEventTestHarness.Now);

            Assert.Equal(events.Count, outcomes.Count);
            await Assert.ThrowsAsync<ArgumentNullException>(() => harness.Projector.ProjectAsync(null));
            Assert.All(outcomes, outcome => Assert.True(outcome.IsAccepted, outcome.RejectionMessage));
            Assert.All(outcomes, outcome => Assert.False(outcome.IsDuplicate));
            Assert.Empty(await harness.DeadLettersAsync());

            var stored = await harness.StoredEventsAsync();
            Assert.Equal(events.Count, stored.Count);

            var metrics = await harness.MetricsService.ComputeAsync(OnlineGameEventTestHarness.TenantId, OnlineGameEventTestHarness.AppId, 0, long.MaxValue);
            Assert.Equal(events.Count, metrics.TotalCount);
            Assert.Equal(3, metrics.GetCategoryCount(OnlineGameEventCategory.Login));
            Assert.Equal(1, metrics.GetCategoryCount(OnlineGameEventCategory.Matchmaking));
            Assert.Equal(4, metrics.GetCategoryCount(OnlineGameEventCategory.Match));
            Assert.Equal(1, metrics.GetCategoryCount(OnlineGameEventCategory.Reward));
            Assert.Equal(1, metrics.GetCategoryCount(OnlineGameEventCategory.Payment));
            Assert.Equal(2, metrics.GetCategoryCount(OnlineGameEventCategory.Report));
        }

        /// <summary>
        /// 从单元素投影结果中取事件名（顺带断言长度，避免投影静默多发）。
        /// </summary>
        /// <param name="events">投影结果。</param>
        /// <returns>唯一事件的事件名。</returns>
        private static string Single(List<OnlineEvent> events)
        {
            return Assert.Single(events).EventType;
        }
    }
}
