using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.Social;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 举报存储组合唯一性回归：五维组合去重、Reviewing 占位、终态释放、CAS 冲突、身份不可改与并发新建。
    /// </summary>
    public sealed class OnlineReportStoreUniquenessTests
    {
        private const long TenantId = 1;
        private const long AppId = 2;
        private const long ReporterId = 100;
        private const long ReportedPlayerId = 200;

        [Fact]
        public async Task SaveIfAbsentAsync_WhenSameCombinationActive_ShouldReturnExistingCase()
        {
            var store = new InMemoryOnlineReportStore();
            var first = await store.SaveIfAbsentAsync(CreateCase("rpt-1"));
            var second = await store.SaveIfAbsentAsync(CreateCase("rpt-2", evidence: "补充证据"));

            Assert.Equal(first.ReportId, second.ReportId);
            Assert.Equal(string.Empty, second.Evidence);
        }

        [Fact]
        public async Task SaveIfAbsentAsync_WhenDifferentCombinationDimension_ShouldCreateNewCase()
        {
            var store = new InMemoryOnlineReportStore();
            await store.SaveIfAbsentAsync(CreateCase("rpt-1"));
            var otherTarget = await store.SaveIfAbsentAsync(CreateCase("rpt-2", reportedPlayerId: ReportedPlayerId + 1));
            var otherScene = await store.SaveIfAbsentAsync(CreateCase("rpt-3", scene: OnlineReportScene.Chat, channelId: "chan-1"));
            var otherReporter = await store.SaveIfAbsentAsync(CreateCase("rpt-4", reporterId: ReporterId + 1));
            var otherScope = await store.SaveIfAbsentAsync(CreateCase("rpt-5", tenantId: TenantId + 1));
            var otherApp = await store.SaveIfAbsentAsync(CreateCase("rpt-6", appId: AppId + 1));

            Assert.Equal(4, (await store.ListByReporterAsync(TenantId, AppId, ReporterId)).Count + (await store.ListByReporterAsync(TenantId + 1, AppId, ReporterId)).Count);
            Assert.NotEqual("rpt-1", otherTarget.ReportId);
            Assert.NotEqual("rpt-1", otherScene.ReportId);
            Assert.NotEqual("rpt-1", otherReporter.ReportId);
            Assert.NotEqual("rpt-1", otherScope.ReportId);
            Assert.NotEqual("rpt-1", otherApp.ReportId);
        }

        [Fact]
        public async Task SaveIfAbsentAsync_WhenReviewing_ShouldOccupyCombinationSlot()
        {
            var store = new InMemoryOnlineReportStore();
            var first = await store.SaveIfAbsentAsync(CreateCase("rpt-1"));

            var reviewing = first.Copy();
            reviewing.State = OnlineReportState.Reviewing;
            Assert.NotNull(await store.ReplaceAsync(reviewing, OnlineReportState.Submitted));

            var second = await store.SaveIfAbsentAsync(CreateCase("rpt-2"));
            Assert.Equal("rpt-1", second.ReportId);
        }

        [Theory]
        [InlineData(OnlineReportState.Actioned)]
        [InlineData(OnlineReportState.Rejected)]
        [InlineData(OnlineReportState.Withdrawn)]
        public async Task SaveIfAbsentAsync_AfterTerminalState_ShouldAllowNewCase(OnlineReportState terminalState)
        {
            var store = new InMemoryOnlineReportStore();
            var first = await store.SaveIfAbsentAsync(CreateCase("rpt-1"));
            var fromState = OnlineReportState.Submitted;
            if (terminalState == OnlineReportState.Actioned)
            {
                var reviewing = first.Copy();
                reviewing.State = OnlineReportState.Reviewing;
                Assert.NotNull(await store.ReplaceAsync(reviewing, OnlineReportState.Submitted));
                fromState = OnlineReportState.Reviewing;
            }

            var terminal = first.Copy();
            terminal.State = terminalState;
            Assert.NotNull(await store.ReplaceAsync(terminal, fromState));

            var second = await store.SaveIfAbsentAsync(CreateCase("rpt-2", evidence: "结案后新证据"));
            Assert.Equal("rpt-2", second.ReportId);
            Assert.Equal("结案后新证据", second.Evidence);
        }

        [Fact]
        public async Task SaveIfAbsentAsync_UnderConcurrency_ShouldCreateSingleActiveCase()
        {
            var store = new InMemoryOnlineReportStore();
            var results = new OnlineReportCase[10];
            var tasks = new Task[10];
            for (var index = 0; index < tasks.Length; index++)
            {
                var caseIndex = index;
                tasks[index] = Task.Run(async () => { results[caseIndex] = await store.SaveIfAbsentAsync(CreateCase("rpt-" + caseIndex)); });
            }

            await Task.WhenAll(tasks);
            Assert.Single(results.Select(caseItem => caseItem.ReportId).Distinct());
        }

        [Fact]
        public async Task ReplaceAsync_WhenIdentityChanged_ShouldFail()
        {
            var store = new InMemoryOnlineReportStore();
            var first = await store.SaveIfAbsentAsync(CreateCase("rpt-1"));

            var tampered = first.Copy();
            tampered.ReportedPlayerId = ReportedPlayerId + 1;
            tampered.State = OnlineReportState.Reviewing;
            Assert.Null(await store.ReplaceAsync(tampered, OnlineReportState.Submitted));

            var unchanged = await store.FindAsync(TenantId, AppId, "rpt-1");
            Assert.Equal(OnlineReportState.Submitted, unchanged.State);
        }

        [Fact]
        public async Task ReplaceAsync_WhenStaleTerminalUpdate_ShouldNotReleaseNewCaseSlot()
        {
            var store = new InMemoryOnlineReportStore();
            var first = await store.SaveIfAbsentAsync(CreateCase("rpt-1"));
            var withdrawn = first.Copy();
            withdrawn.State = OnlineReportState.Withdrawn;
            Assert.NotNull(await store.ReplaceAsync(withdrawn, OnlineReportState.Submitted));

            // 结案后新案占位。
            var second = await store.SaveIfAbsentAsync(CreateCase("rpt-2"));
            Assert.Equal("rpt-2", second.ReportId);

            // 旧案的迟到终态更新不得释放已归属新案的占位。
            var staleWithdrawn = first.Copy();
            staleWithdrawn.State = OnlineReportState.Withdrawn;
            var stale = await store.ReplaceAsync(staleWithdrawn, OnlineReportState.Withdrawn);
            Assert.NotNull(stale);
            var third = await store.SaveIfAbsentAsync(CreateCase("rpt-3"));
            Assert.Equal("rpt-2", third.ReportId);
        }

        /// <summary>
        /// 构造测试案件。
        /// </summary>
        private static OnlineReportCase CreateCase(string reportId, long tenantId = TenantId, long appId = AppId, long reporterId = ReporterId, long reportedPlayerId = ReportedPlayerId, OnlineReportScene scene = OnlineReportScene.Profile, string channelId = "", string evidence = "")
        {
            return new OnlineReportCase
            {
                ReportId = reportId,
                TenantId = tenantId,
                AppId = appId,
                ReporterId = reporterId,
                ReportedPlayerId = reportedPlayerId,
                Scene = scene,
                Reason = OnlineReportReason.Spam,
                MatchId = string.Empty,
                ChatMessageId = string.Empty,
                ChannelId = channelId,
                Evidence = evidence,
                State = OnlineReportState.Submitted,
                Resolution = OnlineReportResolution.None,
                HandlerAdminId = 0,
                AdminCaseId = string.Empty,
                CreatedAtTime = 1000,
                UpdatedAtTime = 1000,
                ClosedAtTime = 0,
            };
        }
    }
}
