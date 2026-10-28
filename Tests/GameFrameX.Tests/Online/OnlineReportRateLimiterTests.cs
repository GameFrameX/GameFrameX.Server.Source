using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.Social;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 举报提交限流器回归：双固定窗口额度、边界重开、失败不烧额度、三维隔离、并发与过期清扫。
    /// </summary>
    public sealed class OnlineReportRateLimiterTests
    {
        private const long TenantId = 1;
        private const long AppId = 2;
        private const long ReporterId = 100;

        [Fact]
        public void TryAcquire_WhenWithinBothWindows_ShouldAllow()
        {
            var limiter = new OnlineReportRateLimiter(3, 10);
            Assert.True(limiter.TryAcquire(TenantId, AppId, ReporterId, 1000));
            Assert.True(limiter.TryAcquire(TenantId, AppId, ReporterId, 2000));
            Assert.True(limiter.TryAcquire(TenantId, AppId, ReporterId, 3000));
        }

        [Fact]
        public void TryAcquire_WhenMinuteLimitExceeded_ShouldRejectWithoutTouchingDayWindow()
        {
            var limiter = new OnlineReportRateLimiter(2, 10);
            Assert.True(limiter.TryAcquire(TenantId, AppId, ReporterId, 1000));
            Assert.True(limiter.TryAcquire(TenantId, AppId, ReporterId, 2000));
            Assert.False(limiter.TryAcquire(TenantId, AppId, ReporterId, 3000));

            // 拒绝不消耗日额度：分钟窗过期后仍可继续提交，直到日额度用尽。
            var minuteWindowStart = 1000;
            var afterMinuteWindow = minuteWindowStart + 60000L;
            Assert.True(limiter.TryAcquire(TenantId, AppId, ReporterId, afterMinuteWindow));
            Assert.True(limiter.TryAcquire(TenantId, AppId, ReporterId, afterMinuteWindow + 1000));
            Assert.False(limiter.TryAcquire(TenantId, AppId, ReporterId, afterMinuteWindow + 2000));
            Assert.False(limiter.TryAcquire(TenantId, AppId, ReporterId, afterMinuteWindow + 3000));
        }

        [Fact]
        public void TryAcquire_WhenDayLimitExceeded_ShouldRejectEvenAfterMinuteWindowRolls()
        {
            // 两个分钟窗共消耗 4 次，日额度 3 已用尽。
            var limiter = new OnlineReportRateLimiter(2, 3);
            Assert.True(limiter.TryAcquire(TenantId, AppId, ReporterId, 1000));
            Assert.True(limiter.TryAcquire(TenantId, AppId, ReporterId, 2000));
            Assert.True(limiter.TryAcquire(TenantId, AppId, ReporterId, 61000));
            Assert.False(limiter.TryAcquire(TenantId, AppId, ReporterId, 62000));

            // 日窗整体过期后额度重开。
            var dayWindowStart = 1000;
            Assert.True(limiter.TryAcquire(TenantId, AppId, ReporterId, dayWindowStart + 86400000L));
        }

        [Fact]
        public void TryAcquire_ShouldIsolateByTenantAppAndReporter()
        {
            var limiter = new OnlineReportRateLimiter(1, 1);
            Assert.True(limiter.TryAcquire(TenantId, AppId, ReporterId, 1000));
            Assert.False(limiter.TryAcquire(TenantId, AppId, ReporterId, 1000));
            Assert.True(limiter.TryAcquire(TenantId + 1, AppId, ReporterId, 1000));
            Assert.True(limiter.TryAcquire(TenantId, AppId + 1, ReporterId, 1000));
            Assert.True(limiter.TryAcquire(TenantId, AppId, ReporterId + 1, 1000));
        }

        [Fact]
        public void TryAcquire_UnderConcurrency_ShouldNotExceedMinuteLimit()
        {
            var limiter = new OnlineReportRateLimiter(5, 50);
            var allowed = 0;
            var gate = new ManualResetEventSlim(false);
            var workers = new Task[10];
            for (var index = 0; index < workers.Length; index++)
            {
                workers[index] = Task.Run(() =>
                {
                    gate.Wait();
                    if (limiter.TryAcquire(TenantId, AppId, ReporterId, 1000))
                    {
                        Interlocked.Increment(ref allowed);
                    }
                });
            }

            gate.Set();
            Task.WaitAll(workers);
            Assert.Equal(5, allowed);
        }

        [Fact]
        public void SweepExpired_ShouldRemoveOnlyFullyExpiredBuckets()
        {
            var limiter = new OnlineReportRateLimiter(5, 50);
            limiter.TryAcquire(TenantId, AppId, ReporterId, 1000);

            // 分钟窗已过、日窗未过：不得删除（删除即凭空重置日额度）。
            Assert.Equal(0, limiter.SweepExpired(1000 + 60000L));

            // 两窗均过期：可删除，且删除后额度重开。
            Assert.Equal(1, limiter.SweepExpired(1000 + 86400000L));
            Assert.True(limiter.TryAcquire(TenantId, AppId, ReporterId, 1000 + 86400000L));
        }
    }
}
