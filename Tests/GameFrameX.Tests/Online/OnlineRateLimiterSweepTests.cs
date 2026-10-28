// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、侵犯他人合法权益等法律法规所禁止的行为！
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer, the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
// ==========================================================================================

using GameFrameX.Capability.Online.Competition.Leaderboard;
using GameFrameX.Capability.Online.Matchmaking;
using GameFrameX.Capability.Online.Social;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 限流器过期桶清扫回归测试（GFX-1085 / C377）：桶在窗口过期后必须被物理删除，
    /// 活跃桶保留且限流判定语义不变；机会性清扫随流量按节流间隔自动推进。
    /// </summary>
    public class OnlineRateLimiterSweepTests
    {
        /// <summary>窗口 10 秒、阈值 3 条的聊天限流器测试基准时刻。</summary>
        private const long BaseTime = 1_000_000L;

        [Fact]
        public void ChatLimiter_SweepExpired_RemovesExpiredKeepsActive()
        {
            var limiter = new OnlineChatRateLimiter(3, 10);

            // 玩家 1 在旧窗口发过消息（已过期），玩家 2 在当前窗口活跃。
            Assert.True(limiter.TryAcquire(1, 1, 1, "global", BaseTime));
            Assert.True(limiter.TryAcquire(1, 1, 2, "global", BaseTime + 5_000));

            var removed = limiter.SweepExpired(BaseTime + 10_000);

            Assert.Equal(1, removed);

            // 玩家 2 的桶被保留：同窗口继续计数（1→3）并在超限时拒绝。
            Assert.True(limiter.TryAcquire(1, 1, 2, "global", BaseTime + 5_000 + 1));
            Assert.True(limiter.TryAcquire(1, 1, 2, "global", BaseTime + 5_000 + 2));
            Assert.False(limiter.TryAcquire(1, 1, 2, "global", BaseTime + 5_000 + 3));

            // 玩家 1 的桶已被删除：过期窗口后重新从 1 计数。
            Assert.True(limiter.TryAcquire(1, 1, 1, "global", BaseTime + 10_001));

            // 全部过期后清扫清零。
            Assert.Equal(2, limiter.SweepExpired(BaseTime + 30_000));
            Assert.Equal(0, limiter.SweepExpired(BaseTime + 30_000));
        }

        [Fact]
        public void MatchLimiter_SweepExpired_RemovesExpiredKeepsActive()
        {
            var limiter = new OnlineMatchRateLimiter(2, 10);

            Assert.True(limiter.TryAcquire(1, 1, 1, BaseTime));
            Assert.True(limiter.TryAcquire(1, 1, 2, BaseTime + 5_000));

            var removed = limiter.SweepExpired(BaseTime + 10_000);

            Assert.Equal(1, removed);
            Assert.True(limiter.TryAcquire(1, 1, 2, BaseTime + 5_000 + 1));
            Assert.False(limiter.TryAcquire(1, 1, 2, BaseTime + 5_000 + 2));
        }

        [Fact]
        public void LeaderboardLimiter_SweepExpired_RemovesExpiredKeepsActive()
        {
            var limiter = new OnlineLeaderboardRateLimiter(2, 10);

            Assert.True(limiter.TryAcquire(1, 1, 1, BaseTime));
            Assert.True(limiter.TryAcquire(1, 1, 2, BaseTime + 5_000));

            var removed = limiter.SweepExpired(BaseTime + 10_000);

            Assert.Equal(1, removed);
            Assert.True(limiter.TryAcquire(1, 1, 2, BaseTime + 5_000 + 1));
            Assert.False(limiter.TryAcquire(1, 1, 2, BaseTime + 5_000 + 2));
        }

        [Fact]
        public void TryAcquire_SweepsExpiredBucketsAutomatically_AfterThrottleInterval()
        {
            var limiter = new OnlineMatchRateLimiter(2, 1);

            // 稀疏的一次性玩家：只来一次，之后不再出现——这类键正是内存泄漏源。
            Assert.True(limiter.TryAcquire(1, 1, 100 + 0, BaseTime));

            // 清扫间隔为 max(window, 60s) = 60s；窗口 1s 早已过期。
            // 距上次清扫不足间隔时不触发全量清扫，但单键访问路径仍按窗口重建，行为不受影响。
            Assert.True(limiter.TryAcquire(1, 1, 200, BaseTime + 10_000));

            // 跨过清扫间隔后，任何流量都会推进全量回收。
            Assert.True(limiter.TryAcquire(1, 1, 300, BaseTime + 60_000));

            // 清扫后历史一次性玩家重新从 1 计数（桶已被物理删除）。
            Assert.True(limiter.TryAcquire(1, 1, 100, BaseTime + 60_001));
            Assert.True(limiter.TryAcquire(1, 1, 100, BaseTime + 60_002));
            Assert.False(limiter.TryAcquire(1, 1, 100, BaseTime + 60_003));
        }

        [Fact]
        public void SweepExpired_DoesNotChangeRateLimitSemantics_WithinWindow()
        {
            var limiter = new OnlineChatRateLimiter(2, 10);

            Assert.True(limiter.TryAcquire(1, 1, 1, "private", BaseTime));
            // 窗口内的清扫必须保留活跃桶（不会把在窗计数误删）。
            Assert.Equal(0, limiter.SweepExpired(BaseTime + 1));
            Assert.True(limiter.TryAcquire(1, 1, 1, "private", BaseTime + 2));
            Assert.False(limiter.TryAcquire(1, 1, 1, "private", BaseTime + 3));
        }
    }
}
