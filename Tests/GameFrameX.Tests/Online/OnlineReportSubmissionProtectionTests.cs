using System;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Events;
using GameFrameX.Capability.Online.Abstractions.Scope;
using GameFrameX.Capability.Online.Social;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 举报提交保护回归（服务级）：重复举报去重单事件、频控超限零落库零事件、结案后可重提。
    /// </summary>
    public sealed class OnlineReportSubmissionProtectionTests
    {
        private const long TenantId = 1;
        private const long AppId = 2;
        private const long PlayerOne = 100;
        private const long PlayerTwo = 200;

        [Fact]
        public async Task DuplicateSubmission_ShouldReturnOriginalEvidenceAndPublishOnce()
        {
            var service = CreateService(out var recorder);
            var first = await service.SubmitReportAsync(CreatePlayerScope(PlayerOne), new OnlineReportSubmission { ReportedPlayerId = PlayerTwo, Scene = OnlineReportScene.Profile, Reason = OnlineReportReason.Spam, Evidence = "原始证据" });
            var second = await service.SubmitReportAsync(CreatePlayerScope(PlayerOne), new OnlineReportSubmission { ReportedPlayerId = PlayerTwo, Scene = OnlineReportScene.Profile, Reason = OnlineReportReason.Spam, Evidence = "试图覆盖的新证据" });

            Assert.True(first.IsSuccess);
            Assert.True(second.IsSuccess);
            Assert.Equal(first.Data.ReportId, second.Data.ReportId);
            Assert.Equal("原始证据", second.Data.Evidence);
            Assert.Single(recorder.Filter(OnlineSocialEvents.ReportChanged));
        }

        [Fact]
        public async Task SubmissionAboveMinuteLimit_ShouldRejectWithoutCaseOrEvent()
        {
            var options = new OnlineReportOptions { MaximumSubmissionsPerMinute = 5, MaximumSubmissionsPerDay = 50 };
            var recorder = new OnlineEventRecorder();
            var reportStore = new InMemoryOnlineReportStore();
            var service = new OnlineSocialDecisionService(new InMemoryOnlineSocialGraphStore(), reportStore, recorder, options);

            OnlineResult<OnlineReportCase> last = null;
            for (var attempt = 0; attempt < 6; attempt++)
            {
                // 每次换一个被举报人：排除组合去重的影响，验证的是频控本身。
                last = await service.SubmitReportAsync(CreatePlayerScope(PlayerOne), new OnlineReportSubmission { ReportedPlayerId = PlayerTwo + attempt, Scene = OnlineReportScene.Profile, Reason = OnlineReportReason.Spam });
            }

            Assert.False(last.IsSuccess);
            Assert.Equal(OnlineErrorCode.RateLimitExceeded, last.Code);
            Assert.Equal(5, recorder.Filter(OnlineSocialEvents.ReportChanged).Count);

            var cases = await reportStore.ListByReporterAsync(TenantId, AppId, PlayerOne);
            Assert.Equal(5, cases.Count);
        }

        [Fact]
        public async Task Resubmission_AfterWithdrawn_ShouldCreateNewCaseWithNewEvidence()
        {
            var service = CreateService(out _);
            var first = await service.SubmitReportAsync(CreatePlayerScope(PlayerOne), new OnlineReportSubmission { ReportedPlayerId = PlayerTwo, Scene = OnlineReportScene.Profile, Reason = OnlineReportReason.Spam });
            Assert.True((await service.WithdrawReportAsync(CreatePlayerScope(PlayerOne), first.Data.ReportId)).IsSuccess);

            var second = await service.SubmitReportAsync(CreatePlayerScope(PlayerOne), new OnlineReportSubmission { ReportedPlayerId = PlayerTwo, Scene = OnlineReportScene.Profile, Reason = OnlineReportReason.Spam, Evidence = "撤回后补充证据" });

            Assert.True(second.IsSuccess);
            Assert.NotEqual(first.Data.ReportId, second.Data.ReportId);
            Assert.Equal("撤回后补充证据", second.Data.Evidence);
        }

        [Fact]
        public void Constructor_WhenReportOptionsMissing_ShouldThrow()
        {
            Assert.Throws<ArgumentNullException>(() => new OnlineSocialDecisionService(new InMemoryOnlineSocialGraphStore(), new InMemoryOnlineReportStore(), new OnlineEventRecorder(), null));
        }

        /// <summary>创建被测服务。</summary>
        private static OnlineSocialDecisionService CreateService(out OnlineEventRecorder recorder)
        {
            recorder = new OnlineEventRecorder();
            return new OnlineSocialDecisionService(new InMemoryOnlineSocialGraphStore(), new InMemoryOnlineReportStore(), recorder, new OnlineReportOptions());
        }

        /// <summary>创建玩家作用域。</summary>
        private static OnlineScope CreatePlayerScope(long playerId)
        {
            return new OnlineScope(TenantId, AppId, 1, playerId);
        }
    }
}
