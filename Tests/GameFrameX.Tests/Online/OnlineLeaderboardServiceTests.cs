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
using GameFrameX.Online.Leaderboard;
using GameFrameX.Online.Scope;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 排行榜组件测试（vault:C8 S7.1 / VC-7.3 / VC-7.14 服务端半边）：
    /// 创建、累计策略、Top N 全序、附近排名边界、游标分页稳定性、读缓存、跨作用域反预言。
    /// </summary>
    public class OnlineLeaderboardServiceTests
    {
        /// <summary>租户标识。</summary>
        private const long TenantId = 1;

        /// <summary>另一租户标识（跨租户用例）。</summary>
        private const long OtherTenantId = 2;

        /// <summary>App 标识。</summary>
        private const long AppId = 10;

        /// <summary>另一 App 标识（跨 App 用例，VC-7.14）。</summary>
        private const long OtherAppId = 11;

        /// <summary>区服标识。</summary>
        private const long ServerId = 100;

        /// <summary>基准时刻（UTC 毫秒）。</summary>
        private const long Now = 1000000L;

        /// <summary>
        /// 验证榜单创建成功与重复创建拒绝（同名榜单不得被覆盖）。
        /// </summary>
        [Fact]
        public async Task CreateAsync_ShouldCreateAndRejectDuplicate()
        {
            var harness = new Harness();
            var request = new OnlineLeaderboardCreateRequest { LeaderboardId = "board-1", SortOrder = OnlineLeaderboardSortOrder.Descending, ScoreUpdatePolicy = OnlineLeaderboardScoreUpdatePolicy.Best };

            var created = await harness.Service.CreateAsync(harness.Scope(), request, Now);

            Assert.True(created.IsSuccess, created.Message);
            Assert.Equal("board-1", created.Data.LeaderboardId);
            Assert.Equal(TenantId, created.Data.TenantId);
            Assert.Equal(AppId, created.Data.AppId);
            Assert.Single(harness.Recorder.Filter(OnlineLeaderboardEvents.LeaderboardCreated));

            var duplicated = await harness.Service.CreateAsync(harness.Scope(), request, Now + 1);

            Assert.False(duplicated.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, duplicated.Code);
            Assert.Single(harness.Recorder.Filter(OnlineLeaderboardEvents.LeaderboardCreated));
        }

        /// <summary>
        /// 验证空榜单标识被参数校验拒绝。
        /// </summary>
        [Fact]
        public async Task CreateAsync_EmptyId_ShouldBeParameterInvalid()
        {
            var harness = new Harness();

            var created = await harness.Service.CreateAsync(harness.Scope(), new OnlineLeaderboardCreateRequest { LeaderboardId = string.Empty }, Now);

            Assert.False(created.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, created.Code);
        }

        /// <summary>
        /// 验证累计策略在存储临界区内正确裁决：Best（降序保大 / 升序保小）、Sum（累加）、Latest（覆盖）。
        /// </summary>
        [Fact]
        public async Task ApplySubmission_Policies_ShouldResolveInStore()
        {
            var harness = new Harness();
            var board = await harness.CreateBoardAsync("board-best-desc", OnlineLeaderboardSortOrder.Descending, OnlineLeaderboardScoreUpdatePolicy.Best);

            await harness.SeedAsync(board, (1001, 100, Now), (1001, 50, Now + 1), (1001, 150, Now + 2));
            var bestDesc = await harness.Store.FindEntryAsync(TenantId, AppId, "board-best-desc", 1001);

            Assert.Equal(150, bestDesc.Score);
            Assert.Equal(3, bestDesc.SubmissionCount);
            Assert.Equal(OnlineLeaderboardScoreSource.MatchResult, bestDesc.SourceKind);
            Assert.NotNull(bestDesc.SourceMatchResultId);

            var ascBoard = await harness.CreateBoardAsync("board-best-asc", OnlineLeaderboardSortOrder.Ascending, OnlineLeaderboardScoreUpdatePolicy.Best);
            await harness.SeedAsync(ascBoard, (1001, 100, Now), (1001, 50, Now + 1));
            var bestAsc = await harness.Store.FindEntryAsync(TenantId, AppId, "board-best-asc", 1001);

            Assert.Equal(50, bestAsc.Score);

            var sumBoard = await harness.CreateBoardAsync("board-sum", OnlineLeaderboardSortOrder.Descending, OnlineLeaderboardScoreUpdatePolicy.Sum);
            await harness.SeedAsync(sumBoard, (1001, 10, Now), (1001, 20, Now + 1));
            var sum = await harness.Store.FindEntryAsync(TenantId, AppId, "board-sum", 1001);

            Assert.Equal(30, sum.Score);

            var latestBoard = await harness.CreateBoardAsync("board-latest", OnlineLeaderboardSortOrder.Descending, OnlineLeaderboardScoreUpdatePolicy.Latest);
            await harness.SeedAsync(latestBoard, (1001, 5, Now), (1001, 9, Now + 1));
            var latest = await harness.Store.FindEntryAsync(TenantId, AppId, "board-latest", 1001);

            Assert.Equal(9, latest.Score);
        }

        /// <summary>
        /// 验证 VC-7.3：降序榜 Top N 全序正确——同分先更新者靠前，再按玩家标识消解；名次无并列空洞。
        /// </summary>
        [Fact]
        public async Task GetTopAsync_Descending_ShouldOrderWithTieBreak()
        {
            var harness = new Harness();
            var board = await harness.CreateBoardAsync("board-1", OnlineLeaderboardSortOrder.Descending, OnlineLeaderboardScoreUpdatePolicy.Best);
            await harness.SeedAsync(
                board,
                (1001, 100, Now),
                (1002, 300, Now + 1),
                (1003, 100, Now + 5),
                (1004, 200, Now + 2),
                (1005, 100, Now + 5));

            var top = await harness.Service.GetTopAsync(harness.Scope(), new OnlineLeaderboardTopQuery { LeaderboardId = "board-1", Count = 10, NowUnixMilliseconds = Now + 100 });

            Assert.True(top.IsSuccess, top.Message);
            Assert.Equal(5, top.Data.TotalCount);
            Assert.Equal(new long[] { 1002, 1004, 1001, 1003, 1005 }, harness.PlayerIds(top.Data));
            Assert.Equal(new int[] { 1, 2, 3, 4, 5 }, harness.Ranks(top.Data));
        }

        /// <summary>
        /// 验证 VC-7.3：升序榜（用时类）分数低者靠前。
        /// </summary>
        [Fact]
        public async Task GetTopAsync_Ascending_ShouldOrderLowScoreFirst()
        {
            var harness = new Harness();
            var board = await harness.CreateBoardAsync("board-asc", OnlineLeaderboardSortOrder.Ascending, OnlineLeaderboardScoreUpdatePolicy.Best);
            await harness.SeedAsync(board, (1001, 90, Now), (1002, 30, Now + 1), (1003, 60, Now + 2));

            var top = await harness.Service.GetTopAsync(harness.Scope(), new OnlineLeaderboardTopQuery { LeaderboardId = "board-asc", Count = 10, NowUnixMilliseconds = Now + 100 });

            Assert.True(top.IsSuccess, top.Message);
            Assert.Equal(new long[] { 1002, 1003, 1001 }, harness.PlayerIds(top.Data));
        }

        /// <summary>
        /// 验证 VC-7.3 / VC-1.12：keyset 游标翻页全量无重复无漏项；翻页期间插入更高名次条目不影响后续页。
        /// </summary>
        [Fact]
        public async Task GetTopAsync_PagingCursor_ShouldBeStableAcrossInserts()
        {
            var harness = new Harness(cacheEnabled: false);
            var board = await harness.CreateBoardAsync("board-page", OnlineLeaderboardSortOrder.Descending, OnlineLeaderboardScoreUpdatePolicy.Best);
            await harness.SeedAsync(board, (1001, 100, Now), (1002, 90, Now + 1), (1003, 80, Now + 2), (1004, 70, Now + 3), (1005, 60, Now + 4));

            var collected = new List<long>();
            var firstPage = await harness.Service.GetTopAsync(harness.Scope(), new OnlineLeaderboardTopQuery { LeaderboardId = "board-page", Count = 2, NowUnixMilliseconds = Now + 100 });
            Assert.True(firstPage.IsSuccess, firstPage.Message);
            Assert.True(firstPage.Data.Cursor.HasMore);
            collected.AddRange(harness.PlayerIds(firstPage.Data));
            Assert.Equal(new long[] { 1001, 1002 }, collected);

            // 翻页期间插入新的榜首：keyset 游标不受影响，后续页不重不漏。
            await harness.SeedAsync(board, (1006, 150, Now + 200));

            var secondPage = await harness.Service.GetTopAsync(harness.Scope(), new OnlineLeaderboardTopQuery { LeaderboardId = "board-page", Count = 2, Cursor = firstPage.Data.Cursor.Cursor, NowUnixMilliseconds = Now + 201 });
            Assert.True(secondPage.IsSuccess, secondPage.Message);
            Assert.True(secondPage.Data.Cursor.HasMore);
            collected.AddRange(harness.PlayerIds(secondPage.Data));

            var lastPage = await harness.Service.GetTopAsync(harness.Scope(), new OnlineLeaderboardTopQuery { LeaderboardId = "board-page", Count = 2, Cursor = secondPage.Data.Cursor.Cursor, NowUnixMilliseconds = Now + 202 });
            Assert.True(lastPage.IsSuccess, lastPage.Message);
            Assert.False(lastPage.Data.Cursor.HasMore);
            Assert.Equal(string.Empty, lastPage.Data.Cursor.Cursor);
            collected.AddRange(harness.PlayerIds(lastPage.Data));

            Assert.Equal(new long[] { 1001, 1002, 1003, 1004, 1005 }, collected);
        }

        /// <summary>
        /// 验证单页条目数按 MaxPageSize 截断（防一次拉全榜）。
        /// </summary>
        [Fact]
        public async Task GetTopAsync_CountBeyondMaxPageSize_ShouldClamp()
        {
            var harness = new Harness(maxPageSize: 3);
            var board = await harness.CreateBoardAsync("board-clamp", OnlineLeaderboardSortOrder.Descending, OnlineLeaderboardScoreUpdatePolicy.Best);
            await harness.SeedAsync(board, (1001, 50, Now), (1002, 40, Now + 1), (1003, 30, Now + 2), (1004, 20, Now + 3), (1005, 10, Now + 4));

            var top = await harness.Service.GetTopAsync(harness.Scope(), new OnlineLeaderboardTopQuery { LeaderboardId = "board-clamp", Count = 10, NowUnixMilliseconds = Now + 100 });

            Assert.True(top.IsSuccess, top.Message);
            Assert.Equal(3, top.Data.Entries.Count);
            Assert.Equal(5, top.Data.TotalCount);
            Assert.True(top.Data.Cursor.HasMore);
        }

        /// <summary>
        /// 验证 VC-7.3：附近排名窗口连续含本人；榜首 / 榜尾边界截断；未上榜玩家拒绝。
        /// </summary>
        [Fact]
        public async Task GetAroundPlayerAsync_ShouldReturnContiguousWindow()
        {
            var harness = new Harness();
            var board = await harness.CreateBoardAsync("board-around", OnlineLeaderboardSortOrder.Descending, OnlineLeaderboardScoreUpdatePolicy.Best);
            await harness.SeedAsync(
                board,
                (1001, 500, Now),
                (1002, 400, Now + 1),
                (1003, 300, Now + 2),
                (1004, 200, Now + 3),
                (1005, 100, Now + 4));

            var middle = await harness.Service.GetAroundPlayerAsync(harness.Scope(), new OnlineLeaderboardAroundQuery { LeaderboardId = "board-around", PlayerId = 1003, Before = 1, After = 1, NowUnixMilliseconds = Now + 100 });

            Assert.True(middle.IsSuccess, middle.Message);
            Assert.Equal(3, middle.Data.Rank);
            Assert.Equal(5, middle.Data.TotalCount);
            Assert.Equal(new long[] { 1002, 1003, 1004 }, harness.AroundPlayerIds(middle.Data));

            var topEdge = await harness.Service.GetAroundPlayerAsync(harness.Scope(), new OnlineLeaderboardAroundQuery { LeaderboardId = "board-around", PlayerId = 1001, Before = 2, After = 2, NowUnixMilliseconds = Now + 100 });

            Assert.True(topEdge.IsSuccess, topEdge.Message);
            Assert.Equal(1, topEdge.Data.Rank);
            Assert.Equal(new long[] { 1001, 1002, 1003 }, harness.AroundPlayerIds(topEdge.Data));

            var bottomEdge = await harness.Service.GetAroundPlayerAsync(harness.Scope(), new OnlineLeaderboardAroundQuery { LeaderboardId = "board-around", PlayerId = 1005, Before = 2, After = 2, NowUnixMilliseconds = Now + 100 });

            Assert.True(bottomEdge.IsSuccess, bottomEdge.Message);
            Assert.Equal(5, bottomEdge.Data.Rank);
            Assert.Equal(new long[] { 1003, 1004, 1005 }, harness.AroundPlayerIds(bottomEdge.Data));

            var offBoard = await harness.Service.GetAroundPlayerAsync(harness.Scope(), new OnlineLeaderboardAroundQuery { LeaderboardId = "board-around", PlayerId = 9999, Before = 2, After = 2, NowUnixMilliseconds = Now + 100 });

            Assert.False(offBoard.IsSuccess);
            Assert.Equal(OnlineErrorCode.ResourceNotFound, offBoard.Code);
        }

        /// <summary>
        /// 验证玩家名次查询：在榜返回正确名次；未上榜拒绝。
        /// </summary>
        [Fact]
        public async Task GetPlayerRankAsync_ShouldReturnRankOrReject()
        {
            var harness = new Harness();
            var board = await harness.CreateBoardAsync("board-rank", OnlineLeaderboardSortOrder.Descending, OnlineLeaderboardScoreUpdatePolicy.Best);
            await harness.SeedAsync(board, (1001, 500, Now), (1002, 400, Now + 1));

            var ranked = await harness.Service.GetPlayerRankAsync(harness.Scope(), "board-rank", 1002, Now + 100);

            Assert.True(ranked.IsSuccess, ranked.Message);
            Assert.Equal(2, ranked.Data.Rank);
            Assert.Equal(400, ranked.Data.Entry.Score);

            var offBoard = await harness.Service.GetPlayerRankAsync(harness.Scope(), "board-rank", 9999, Now + 100);

            Assert.False(offBoard.IsSuccess);
            Assert.Equal(OnlineErrorCode.ResourceNotFound, offBoard.Code);
        }

        /// <summary>
        /// 验证 VC-7.14：跨 App / 跨租户读榜与榜单不存在同构拒绝（反预言，不泄露存在性）。
        /// </summary>
        [Fact]
        public async Task CrossScopeRead_ShouldBeResourceNotFound()
        {
            var harness = new Harness();
            var board = await harness.CreateBoardAsync("board-scope", OnlineLeaderboardSortOrder.Descending, OnlineLeaderboardScoreUpdatePolicy.Best);
            await harness.SeedAsync(board, (1001, 100, Now));

            var crossApp = await harness.Service.GetTopAsync(harness.Scope(OtherAppId), new OnlineLeaderboardTopQuery { LeaderboardId = "board-scope", Count = 10, NowUnixMilliseconds = Now + 100 });
            var crossTenant = await harness.Service.GetTopAsync(harness.Scope(AppId, OtherTenantId), new OnlineLeaderboardTopQuery { LeaderboardId = "board-scope", Count = 10, NowUnixMilliseconds = Now + 100 });
            var unknownBoard = await harness.Service.GetTopAsync(harness.Scope(), new OnlineLeaderboardTopQuery { LeaderboardId = "board-none", Count = 10, NowUnixMilliseconds = Now + 100 });

            Assert.False(crossApp.IsSuccess);
            Assert.Equal(OnlineErrorCode.ResourceNotFound, crossApp.Code);
            Assert.False(crossTenant.IsSuccess);
            Assert.Equal(OnlineErrorCode.ResourceNotFound, crossTenant.Code);
            Assert.False(unknownBoard.IsSuccess);
            Assert.Equal(OnlineErrorCode.ResourceNotFound, unknownBoard.Code);
        }

        /// <summary>
        /// 验证读缓存语义：TTL 窗口内沿用快照（读延迟优化），过期后重建；关闭缓存立即可见。
        /// </summary>
        [Fact]
        public async Task ReadCache_ShouldExpireByTtlAndBypassWhenDisabled()
        {
            var cached = new Harness(cacheTtlSeconds: 2);
            var board = await cached.CreateBoardAsync("board-cache", OnlineLeaderboardSortOrder.Descending, OnlineLeaderboardScoreUpdatePolicy.Best);
            await cached.SeedAsync(board, (1001, 100, Now), (1002, 90, Now + 1), (1003, 80, Now + 2));

            var firstRead = await cached.Service.GetTopAsync(cached.Scope(), new OnlineLeaderboardTopQuery { LeaderboardId = "board-cache", Count = 10, NowUnixMilliseconds = 10000L });
            Assert.Equal(3, firstRead.Data.TotalCount);

            await cached.SeedAsync(board, (1004, 70, 20000L));

            var withinTtl = await cached.Service.GetTopAsync(cached.Scope(), new OnlineLeaderboardTopQuery { LeaderboardId = "board-cache", Count = 10, NowUnixMilliseconds = 11000L });
            Assert.Equal(3, withinTtl.Data.TotalCount);

            var afterTtl = await cached.Service.GetTopAsync(cached.Scope(), new OnlineLeaderboardTopQuery { LeaderboardId = "board-cache", Count = 10, NowUnixMilliseconds = 30000L });
            Assert.Equal(4, afterTtl.Data.TotalCount);

            var uncached = new Harness(cacheEnabled: false);
            var uncachedBoard = await uncached.CreateBoardAsync("board-nocache", OnlineLeaderboardSortOrder.Descending, OnlineLeaderboardScoreUpdatePolicy.Best);
            await uncached.SeedAsync(uncachedBoard, (1001, 100, Now));

            var beforeWrite = await uncached.Service.GetTopAsync(uncached.Scope(), new OnlineLeaderboardTopQuery { LeaderboardId = "board-nocache", Count = 10, NowUnixMilliseconds = 10000L });
            Assert.Equal(1, beforeWrite.Data.TotalCount);

            await uncached.SeedAsync(uncachedBoard, (1002, 90, 20000L));
            var immediate = await uncached.Service.GetTopAsync(uncached.Scope(), new OnlineLeaderboardTopQuery { LeaderboardId = "board-nocache", Count = 10, NowUnixMilliseconds = 21000L });
            Assert.Equal(2, immediate.Data.TotalCount);
        }

        /// <summary>
        /// 测试基座：内存存储 + 可调选项 + 事件记录桩。
        /// </summary>
        private sealed class Harness
        {
            /// <summary>
            /// 初始化测试基座。
            /// </summary>
            /// <param name="cacheEnabled">是否启用读缓存。</param>
            /// <param name="cacheTtlSeconds">缓存 TTL（秒）。</param>
            /// <param name="maxPageSize">单页条目数上限。</param>
            public Harness(bool cacheEnabled = true, int cacheTtlSeconds = 5, int maxPageSize = 200)
            {
                Store = new InMemoryOnlineLeaderboardStore();
                Options = new OnlineLeaderboardOptions { CacheEnabled = cacheEnabled, CacheTtlSeconds = cacheTtlSeconds, MaxPageSize = maxPageSize };
                Recorder = new OnlineEventRecorder();
                Service = new OnlineLeaderboardService(Store, Options, Recorder);
            }

            /// <summary>获取排行榜存储。</summary>
            public InMemoryOnlineLeaderboardStore Store
            {
                get;
            }

            /// <summary>获取组件选项。</summary>
            public OnlineLeaderboardOptions Options
            {
                get;
            }

            /// <summary>获取事件记录桩。</summary>
            public OnlineEventRecorder Recorder
            {
                get;
            }

            /// <summary>获取排行榜服务。</summary>
            public OnlineLeaderboardService Service
            {
                get;
            }

            /// <summary>
            /// 构造查询作用域。
            /// </summary>
            /// <param name="appId">App 标识（默认本 App）。</param>
            /// <param name="tenantId">租户标识（默认本租户）。</param>
            /// <returns>作用域。</returns>
            public OnlineScope Scope(long appId = AppId, long tenantId = TenantId)
            {
                return new OnlineScope(tenantId, appId, ServerId);
            }

            /// <summary>
            /// 创建榜单并返回存储内定义副本。
            /// </summary>
            /// <param name="leaderboardId">榜单标识。</param>
            /// <param name="sortOrder">排序方向。</param>
            /// <param name="policy">累计策略。</param>
            /// <returns>榜单定义。</returns>
            public async Task<OnlineLeaderboard> CreateBoardAsync(string leaderboardId, OnlineLeaderboardSortOrder sortOrder, OnlineLeaderboardScoreUpdatePolicy policy)
            {
                var created = await Service.CreateAsync(Scope(), new OnlineLeaderboardCreateRequest { LeaderboardId = leaderboardId, SortOrder = sortOrder, ScoreUpdatePolicy = policy }, Now);
                Assert.True(created.IsSuccess, created.Message);
                return created.Data;
            }

            /// <summary>
            /// 直接经存储临界区写入条目（查询语义用种子数据）。
            /// </summary>
            /// <param name="board">榜单定义。</param>
            /// <param name="submissions">种子（玩家, 分数, 时刻）序列。</param>
            /// <returns>完成通知。</returns>
            public async Task SeedAsync(OnlineLeaderboard board, params (long PlayerId, long Score, long Time)[] submissions)
            {
                for (var index = 0; index < submissions.Length; index++)
                {
                    var submission = submissions[index];
                    await Store.ApplySubmissionAsync(board, new OnlineLeaderboardScoreSubmission
                    {
                        PlayerId = submission.PlayerId,
                        IncomingScore = submission.Score,
                        SourceKind = OnlineLeaderboardScoreSource.MatchResult,
                        SourceMatchResultId = "mrs-seed-" + submission.PlayerId + "-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        SubmittedTime = submission.Time,
                    });
                }
            }

            /// <summary>
            /// 提取页内玩家标识序列。
            /// </summary>
            /// <param name="page">分页结果。</param>
            /// <returns>玩家标识序列。</returns>
            public long[] PlayerIds(OnlineLeaderboardPage page)
            {
                var ids = new List<long>();
                foreach (var view in page.Entries)
                {
                    ids.Add(view.Entry.PlayerId);
                }

                return ids.ToArray();
            }

            /// <summary>
            /// 提取页内名次序列。
            /// </summary>
            /// <param name="page">分页结果。</param>
            /// <returns>名次序列。</returns>
            public int[] Ranks(OnlineLeaderboardPage page)
            {
                var ranks = new List<int>();
                foreach (var view in page.Entries)
                {
                    ranks.Add(view.Rank);
                }

                return ranks.ToArray();
            }

            /// <summary>
            /// 提取附近窗口玩家标识序列。
            /// </summary>
            /// <param name="view">附近排名视图。</param>
            /// <returns>玩家标识序列。</returns>
            public long[] AroundPlayerIds(OnlineLeaderboardAroundView view)
            {
                var ids = new List<long>();
                foreach (var entry in view.Around)
                {
                    ids.Add(entry.Entry.PlayerId);
                }

                return ids.ToArray();
            }
        }
    }
}
