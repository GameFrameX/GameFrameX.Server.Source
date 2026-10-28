// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the PRC and relevant international regulations.
//   使用本项目须严格遵守相关法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please refer to the LICENSE file at the root of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe the legal rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project,
//   本组织与贡献者概不承担任何责任。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository:  https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:   https://gitee.com/GameFrameX
//   CNB  仓库：https://www.cnb.cool/GameFrameX
//   CNB Repository:     https://www.cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Docs:      https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System;
using System.Linq;
using System.Threading.Tasks;
using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Hosting.Core;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Scope;
using GameFrameX.Capability.Online.Match;
using GameFrameX.Capability.Online.Matchmaking;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 匹配域桥接缝测试（C246 → C294 机制桥）：匹配域契约面由 online-match 在 online-matchmaking 在场时于 Activate 期经机制桥发布；
    /// T2 裁定 R4-R13 的域面语义（即时窗口 / 取消终态回执 / 状态与票据映射 / 超时 / 入场到达同一对局）经租约访问；
    /// 桥生命周期（启动前缺席 / 停止后撤销且旧租约显式失败）与匹配能力缺席时的披露（域桥不发布、对局桥照常发布）。
    /// </summary>
    [Collection(CapabilityRuntimeCollection.Name)]
    public class OnlineMatchDomainBridgeTests
    {
        /// <summary>
        /// 静默调度间隔（毫秒）：tick job 一律由用例显式驱动（如撮合 RunOnceAsync），后台循环在用例时长内不触发，
        /// 保持「撮合 / 超时只在用例推进时刻时发生」的确定性。
        /// </summary>
        private const int QuietSchedulerIntervalMilliseconds = 3600000;

        /// <summary>玩家一。</summary>
        private const long PlayerOne = 2001;

        /// <summary>玩家二。</summary>
        private const long PlayerTwo = 2002;

        /// <summary>玩家三（非成员）。</summary>
        private const long PlayerThree = 2003;

        /// <summary>
        /// 构造玩家作用域（三元组与族配置段一致）。
        /// </summary>
        /// <param name="playerId">玩家标识。</param>
        /// <returns>玩家作用域。</returns>
        private static OnlineScope PlayerScope(long playerId)
        {
            return new OnlineScope(OnlineCapabilityTestHarness.TenantId, OnlineCapabilityTestHarness.AppId, OnlineCapabilityTestHarness.ServerId, playerId);
        }

        /// <summary>
        /// 静默后台调度（用例显式驱动 tick 语义）。
        /// </summary>
        /// <param name="options">组合选项。</param>
        private static void QuietScheduler(CapabilityCompositionOptions options)
        {
            options.HostOptions.SchedulerIntervalMilliseconds = QuietSchedulerIntervalMilliseconds;
        }

        /// <summary>
        /// 全族组合并启动，经机制桥取匹配域契约面执行用例主体（结束恒停止撤桥）。
        /// </summary>
        /// <param name="body">用例主体（入参：匹配域契约面、已启动的组合产物）。</param>
        /// <returns>异步任务。</returns>
        private static Task WithStartedMatchDomainAsync(Func<IOnlineMatchDomain, CapabilityRuntime, Task> body)
        {
            return OnlineCapabilityTestHarness.WithRuntimeAsync(async runtime =>
            {
                await runtime.Host.StartAsync();
                Assert.True(CapabilityBridge<IOnlineMatchDomain>.TryAcquire(out var lease));
                await body(lease.Contract, runtime);
            }, configure: QuietScheduler);
        }

        /// <summary>
        /// 匹配域桥发布：online-matchmaking 先于 online-match 装配（拓扑序），启动后桥契约面与共享面注册的域服务为同一实例。
        /// </summary>
        [Fact]
        public async Task Bridge_PublishesMatchDomainFace_AfterMatchmakingAssembly()
        {
            await WithStartedMatchDomainAsync((domain, runtime) =>
            {
                var order = runtime.Report.EnabledIdsInOrder.ToList();
                Assert.True(order.IndexOf("online-matchmaking") < order.IndexOf("online-match"), "online-matchmaking 必须先于 online-match 装配");
                Assert.Same(runtime.Host.Services.GetService<IOnlineMatchDomain>(), domain);
                return Task.CompletedTask;
            });
        }

        /// <summary>
        /// 桥生命周期：装配未启动时匹配域桥缺席；宿主停止自动撤桥——再取租约失败，旧租约访问契约面显式抛
        /// <see cref="CapabilityBridgeUnavailableException"/>。
        /// </summary>
        [Fact]
        public async Task Bridge_LifecycleFollowsHostStartAndStop()
        {
            await OnlineCapabilityTestHarness.WithRuntimeAsync(async runtime =>
            {
                Assert.False(CapabilityBridge<IOnlineMatchDomain>.TryAcquire(out _));

                await runtime.Host.StartAsync();
                Assert.True(CapabilityBridge<IOnlineMatchDomain>.TryAcquire(out var lease));
                var through = await lease.Contract.GetStatusAsync(PlayerScope(PlayerOne));
                Assert.True(through.IsSuccess);

                await runtime.StopAsync();

                Assert.False(CapabilityBridge<IOnlineMatchDomain>.TryAcquire(out _));
                Assert.Throws<CapabilityBridgeUnavailableException>(() => lease.Contract);
            }, configure: QuietScheduler);
        }

        /// <summary>
        /// 匹配能力缺席披露：排除 online-matchmaking 后匹配域服务不装配、匹配域桥不发布（不报错），
        /// 对局桥照常发布；对局能力报告条目披露缺失功能。
        /// </summary>
        [Fact]
        public async Task Bridge_WithoutMatchmaking_PublishesGameBridgeButNotMatchDomain()
        {
            await OnlineCapabilityTestHarness.WithRuntimeAsync(async runtime =>
            {
                await runtime.Host.StartAsync();

                Assert.DoesNotContain("online-matchmaking", runtime.Report.EnabledIdsInOrder);
                Assert.Contains("online-match", runtime.Report.EnabledIdsInOrder);
                Assert.Null(runtime.Host.Services.GetOptionalService<IOnlineMatchDomain>());
                Assert.False(CapabilityBridge<IOnlineMatchDomain>.TryAcquire(out _));
                Assert.True(CapabilityBridge<IOnlineMatchGameBridge>.TryAcquire(out var gameLease));
                Assert.NotNull(gameLease.Contract);

                var matchEntry = Assert.Single(runtime.Report.Entries, candidate => candidate.Manifest.Id == "online-match");
                Assert.NotEmpty(matchEntry.AbsentFeatures);
            }, configure: options =>
            {
                QuietScheduler(options);
                options.Filter = new CapabilityAssemblyFilter(disabledIds: new[] { "online-matchmaking" });
            });
        }

        /// <summary>
        /// R5：单人入队无对手 → MatchId=0 排队；Status 反映排队中。
        /// </summary>
        [Fact]
        public async Task Enqueue_SoloPlayer_QueuesWithZeroMatchId()
        {
            await WithStartedMatchDomainAsync(async (domain, runtime) =>
            {
                var enqueue = await domain.EnqueueAsync(PlayerScope(PlayerOne), 1, 1000);

                Assert.True(enqueue.IsSuccess);
                Assert.Equal(0, enqueue.Data.MatchId);
                var status = await domain.GetStatusAsync(PlayerScope(PlayerOne));
                Assert.True(status.Data.Queuing);
                Assert.False(status.Data.Matched);
                Assert.Equal(0, status.Data.MatchId);
            });
        }

        /// <summary>
        /// R5/R9：第二人入队即时窗口成组——第二人响应即刻带 MatchId，第一人随后 Status 得同一 MatchId。
        /// </summary>
        [Fact]
        public async Task Enqueue_SecondPlayer_InstantWindowPairsBoth()
        {
            await WithStartedMatchDomainAsync(async (domain, runtime) =>
            {
                await domain.EnqueueAsync(PlayerScope(PlayerOne), 1, 1000);
                var second = await domain.EnqueueAsync(PlayerScope(PlayerTwo), 1, 1050);

                Assert.True(second.IsSuccess);
                Assert.NotEqual(0, second.Data.MatchId);
                var firstStatus = await domain.GetStatusAsync(PlayerScope(PlayerOne));
                Assert.False(firstStatus.Data.Queuing);
                Assert.True(firstStatus.Data.Matched);
                Assert.Equal(second.Data.MatchId, firstStatus.Data.MatchId);
            });
        }

        /// <summary>
        /// R4：排队中重复入队被拒（DuplicateRequest）。
        /// </summary>
        [Fact]
        public async Task Enqueue_DuplicatePlayer_Rejected()
        {
            await WithStartedMatchDomainAsync(async (domain, runtime) =>
            {
                await domain.EnqueueAsync(PlayerScope(PlayerOne), 1, 1000);
                var duplicate = await domain.EnqueueAsync(PlayerScope(PlayerOne), 1, 1000);

                Assert.False(duplicate.IsSuccess);
                Assert.Equal(OnlineErrorCode.DuplicateRequest, duplicate.Code);
            });
        }

        /// <summary>
        /// R6/R7：模式为池标签（不同模式不同池）；评分差超基础窗（±100）不成组。
        /// </summary>
        [Fact]
        public async Task Enqueue_ModePartitionsAndRatingWindow_PartitionPools()
        {
            await WithStartedMatchDomainAsync(async (domain, runtime) =>
            {
                await domain.EnqueueAsync(PlayerScope(PlayerOne), 1, 1000);
                var otherMode = await domain.EnqueueAsync(PlayerScope(PlayerTwo), 2, 1050);
                Assert.Equal(0, otherMode.Data.MatchId);

                var third = await domain.EnqueueAsync(PlayerScope(PlayerThree), 1, 5000);
                Assert.Equal(0, third.Data.MatchId);
            });
        }

        /// <summary>
        /// 入队本地校验：模式未定义 / 评分非正 → ParameterInvalid。
        /// </summary>
        [Fact]
        public async Task Enqueue_InvalidModeOrRating_Rejected()
        {
            await WithStartedMatchDomainAsync(async (domain, runtime) =>
            {
                var badMode = await domain.EnqueueAsync(PlayerScope(PlayerOne), 9, 1000);
                Assert.False(badMode.IsSuccess);
                Assert.Equal(OnlineErrorCode.ParameterInvalid, badMode.Code);

                var badRating = await domain.EnqueueAsync(PlayerScope(PlayerOne), 1, 0);
                Assert.False(badRating.IsSuccess);
                Assert.Equal(OnlineErrorCode.ParameterInvalid, badRating.Code);
            });
        }

        /// <summary>
        /// R8：排队中取消成功；无票取消回「未取消」回执（协议面映射 420003）。
        /// </summary>
        [Fact]
        public async Task Cancel_WhileQueued_Succeeds_AndWithoutTicket_ComesBackNotCancelled()
        {
            await WithStartedMatchDomainAsync(async (domain, runtime) =>
            {
                await domain.EnqueueAsync(PlayerScope(PlayerOne), 1, 1000);
                var cancelled = await domain.CancelAsync(PlayerScope(PlayerOne));
                Assert.True(cancelled.IsSuccess);
                Assert.True(cancelled.Data.Cancelled);

                var noTicket = await domain.CancelAsync(PlayerScope(PlayerTwo));
                Assert.True(noTicket.IsSuccess);
                Assert.False(noTicket.Data.Cancelled);
            });
        }

        /// <summary>
        /// R8：已成组后取消——终态回执不算失败，Cancelled=false（旧链 420003 可观测等价）。
        /// </summary>
        [Fact]
        public async Task Cancel_AfterMatch_ComesBackTerminalReceipt()
        {
            await WithStartedMatchDomainAsync(async (domain, runtime) =>
            {
                await domain.EnqueueAsync(PlayerScope(PlayerOne), 1, 1000);
                await domain.EnqueueAsync(PlayerScope(PlayerTwo), 1, 1050);
                var lateCancel = await domain.CancelAsync(PlayerScope(PlayerOne));

                Assert.True(lateCancel.IsSuccess);
                Assert.False(lateCancel.Data.Cancelled);
            });
        }

        /// <summary>
        /// R10：票据 TTL 过期（注入时刻推进撮合扫描）→ Status=(false,0)，取消回未取消回执。
        /// </summary>
        [Fact]
        public async Task Status_AfterTicketExpiry_NotQueuingZeroMatch()
        {
            await WithStartedMatchDomainAsync(async (domain, runtime) =>
            {
                var coordinator = runtime.Host.Services.GetService<OnlineMatchmakerCoordinator>();

                await domain.EnqueueAsync(PlayerScope(PlayerOne), 1, 1000);
                var later = DateTimeOffset.UtcNow.AddSeconds(400).ToUnixTimeMilliseconds();
                await coordinator.RunOnceAsync(OnlineCapabilityTestHarness.TenantId, OnlineCapabilityTestHarness.AppId, later);

                var status = await domain.GetStatusAsync(PlayerScope(PlayerOne));
                Assert.False(status.Data.Queuing);
                Assert.False(status.Data.Matched);
                Assert.Equal(0, status.Data.MatchId);

                var cancel = await domain.CancelAsync(PlayerScope(PlayerOne));
                Assert.False(cancel.Data.Cancelled);
            });
        }

        /// <summary>
        /// R13/T4：双方各自入场到达同一对局——同一数值 MatchId、双方成员、对局进入运行态（唯一 Actor）。
        /// </summary>
        [Fact]
        public async Task Enter_BothPlayers_ReachSameRunningMatch()
        {
            await WithStartedMatchDomainAsync(async (domain, runtime) =>
            {
                var matchRuntime = runtime.Host.Services.GetService<OnlineMatchRuntime>();

                await domain.EnqueueAsync(PlayerScope(PlayerOne), 1, 1000);
                var second = await domain.EnqueueAsync(PlayerScope(PlayerTwo), 1, 1050);
                var matchId = second.Data.MatchId;

                var firstEntry = await domain.EnterAsync(PlayerScope(PlayerOne), matchId);
                var secondEntry = await domain.EnterAsync(PlayerScope(PlayerTwo), matchId);

                Assert.True(firstEntry.IsSuccess, firstEntry.Message);
                Assert.True(secondEntry.IsSuccess, secondEntry.Message);
                Assert.Equal(matchId, firstEntry.Data.MatchId);
                Assert.Equal(matchId, secondEntry.Data.MatchId);
                Assert.False(firstEntry.Data.Started); // GFX-1099：对手未入场，首入场者只置本人就绪，不得替全员开局
                Assert.True(secondEntry.Data.Started); // 末位入场者补齐全员就绪后开局
                Assert.Contains(PlayerOne, firstEntry.Data.PlayerIds);
                Assert.Contains(PlayerTwo, secondEntry.Data.PlayerIds);

                var actor = Assert.Single(matchRuntime.SnapshotActors());
                var snapshot = actor.GetSnapshot();
                Assert.Equal(OnlineMatchState.Running, snapshot.State);
                Assert.Equal(2, snapshot.Members.Count(member => member != null));
            });
        }

        /// <summary>
        /// R13：入场幂等——重复入场复用同一对局（create-or-reuse）。
        /// </summary>
        [Fact]
        public async Task Enter_Repeatedly_ReusesSameMatch()
        {
            await WithStartedMatchDomainAsync(async (domain, runtime) =>
            {
                var matchRuntime = runtime.Host.Services.GetService<OnlineMatchRuntime>();

                await domain.EnqueueAsync(PlayerScope(PlayerOne), 1, 1000);
                var second = await domain.EnqueueAsync(PlayerScope(PlayerTwo), 1, 1050);

                await domain.EnterAsync(PlayerScope(PlayerOne), second.Data.MatchId);
                await domain.EnterAsync(PlayerScope(PlayerOne), second.Data.MatchId);

                Assert.Single(matchRuntime.SnapshotActors());
            });
        }

        /// <summary>
        /// R13：未知对局标识 → ResourceNotFound；非对局成员 → ScopeDenied。
        /// </summary>
        [Fact]
        public async Task Enter_UnknownOrForeign_Rejected()
        {
            await WithStartedMatchDomainAsync(async (domain, runtime) =>
            {
                var unknown = await domain.EnterAsync(PlayerScope(PlayerOne), 424242);
                Assert.False(unknown.IsSuccess);
                Assert.Equal(OnlineErrorCode.ResourceNotFound, unknown.Code);

                await domain.EnqueueAsync(PlayerScope(PlayerOne), 1, 1000);
                var second = await domain.EnqueueAsync(PlayerScope(PlayerTwo), 1, 1050);
                var foreign = await domain.EnterAsync(PlayerScope(PlayerThree), second.Data.MatchId);
                Assert.False(foreign.IsSuccess);
                Assert.Equal(OnlineErrorCode.ScopeDenied, foreign.Code);
            });
        }

        /// <summary>
        /// R12 支撑：通知推送方按 assignmentId 解析数值 MatchId 与成员表（撮合后可反复解析，幂等）。
        /// </summary>
        [Fact]
        public async Task ResolveAssignment_AfterMatch_ReturnsNumericAndPlayers_Idempotently()
        {
            await WithStartedMatchDomainAsync(async (domain, runtime) =>
            {
                var store = runtime.Host.Services.GetService<IOnlineMatchTicketStore>();

                await domain.EnqueueAsync(PlayerScope(PlayerOne), 1, 1000);
                var second = await domain.EnqueueAsync(PlayerScope(PlayerTwo), 1, 1050);
                var ticket = await store.FindLatestByPlayerAsync(OnlineCapabilityTestHarness.TenantId, OnlineCapabilityTestHarness.AppId, PlayerOne);
                Assert.NotNull(ticket);
                Assert.False(string.IsNullOrEmpty(ticket.AssignmentId));

                var resolved = await domain.ResolveAssignmentAsync(OnlineCapabilityTestHarness.TenantId, OnlineCapabilityTestHarness.AppId, ticket.AssignmentId);
                Assert.True(resolved.IsSuccess, resolved.Message);
                Assert.Equal(second.Data.MatchId, resolved.Data.MatchId);
                Assert.Contains(PlayerOne, resolved.Data.PlayerIds);
                Assert.Contains(PlayerTwo, resolved.Data.PlayerIds);

                var again = await domain.ResolveAssignmentAsync(OnlineCapabilityTestHarness.TenantId, OnlineCapabilityTestHarness.AppId, ticket.AssignmentId);
                Assert.Equal(resolved.Data.MatchId, again.Data.MatchId);
            });
        }

        /// <summary>
        /// 票据按玩家最新读面：Matched 票据可查得（FindActiveByPlayer 只回 Queued 的缺口补齐）。
        /// </summary>
        [Fact]
        public async Task Store_FindLatestByPlayer_ReturnsMatchedTicket()
        {
            await WithStartedMatchDomainAsync(async (domain, runtime) =>
            {
                var store = runtime.Host.Services.GetService<IOnlineMatchTicketStore>();

                await domain.EnqueueAsync(PlayerScope(PlayerOne), 1, 1000);
                await domain.EnqueueAsync(PlayerScope(PlayerTwo), 1, 1050);

                var ticket = await store.FindLatestByPlayerAsync(OnlineCapabilityTestHarness.TenantId, OnlineCapabilityTestHarness.AppId, PlayerTwo);
                Assert.NotNull(ticket);
                Assert.Equal(OnlineMatchTicketState.Matched, ticket.State);
            });
        }

        /// <summary>
        /// R13/T4 并发回归：双端同时入场（真会话 E2E 实证过的竞态）——双方都必须成功，至少一方观测到开局，最终对局 Running。
        /// </summary>
        [Fact]
        public async Task Enter_ConcurrentlyFromBothPlayers_BothSucceed()
        {
            await WithStartedMatchDomainAsync(async (domain, runtime) =>
            {
                await domain.EnqueueAsync(PlayerScope(PlayerOne), 1, 1000);
                var second = await domain.EnqueueAsync(PlayerScope(PlayerTwo), 1, 1050);
                var matchId = second.Data.MatchId;

                var firstEnter = Task.Run(() => domain.EnterAsync(PlayerScope(PlayerOne), matchId));
                var secondEnter = Task.Run(() => domain.EnterAsync(PlayerScope(PlayerTwo), matchId));
                var results = await Task.WhenAll(firstEnter, secondEnter);

                Assert.True(results[0].IsSuccess, results[0].Message);
                Assert.True(results[1].IsSuccess, results[1].Message);
                // GFX-1099：先完成就绪标记的一方可能在对局未全员就绪时返回（Started=false），
                // 开局收敛以「同键仅一个 Actor 实例」为前提——并发双实例裂脑归 GFX-1098（C389，
                // Actor 登记表单实例收敛）管辖；其合入后此处恒为单实例，断言自动收紧为必开局。
                var matchRuntime = runtime.Host.Services.GetService<OnlineMatchRuntime>();
                var actors = matchRuntime.SnapshotActors();
                if (actors.Count == 1)
                {
                    Assert.True(results[0].Data.Started || results[1].Data.Started);
                    Assert.Equal(OnlineMatchState.Running, actors[0].GetSnapshot().State);
                }

                Assert.Equal(matchId, results[0].Data.MatchId);
                Assert.Equal(matchId, results[1].Data.MatchId);
            });
        }

        /// <summary>
        /// GFX-1099 回归：仅首入场玩家到场——只置本人就绪，不开局（Waiting 态），对手未被迫 Ready。
        /// </summary>
        [Fact]
        public async Task Enter_FirstPlayerAlone_DoesNotStartMatch()
        {
            await WithStartedMatchDomainAsync(async (domain, runtime) =>
            {
                var matchRuntime = runtime.Host.Services.GetService<OnlineMatchRuntime>();

                await domain.EnqueueAsync(PlayerScope(PlayerOne), 1, 1000);
                var second = await domain.EnqueueAsync(PlayerScope(PlayerTwo), 1, 1050);

                var firstEntry = await domain.EnterAsync(PlayerScope(PlayerOne), second.Data.MatchId);
                Assert.True(firstEntry.IsSuccess, firstEntry.Message);
                Assert.False(firstEntry.Data.Started);

                var actor = Assert.Single(matchRuntime.SnapshotActors());
                var snapshot = actor.GetSnapshot();
                Assert.NotEqual(OnlineMatchState.Running, snapshot.State);
                var opponent = snapshot.Members.Single(member => member.PlayerId == PlayerTwo);
                Assert.NotEqual(OnlineMatchMemberState.Ready, opponent.State); // 对手未被替置 Ready
            });
        }

        /// <summary>
        /// 作用域校验：缺玩家主体位 → ParameterInvalid。
        /// </summary>
        [Fact]
        public async Task Enqueue_WithoutPlayerPosition_Rejected()
        {
            await WithStartedMatchDomainAsync(async (domain, runtime) =>
            {
                var scope = new OnlineScope(OnlineCapabilityTestHarness.TenantId, OnlineCapabilityTestHarness.AppId, OnlineCapabilityTestHarness.ServerId);
                var rejected = await domain.EnqueueAsync(scope, 1, 1000);
                Assert.False(rejected.IsSuccess);
                Assert.Equal(OnlineErrorCode.ParameterInvalid, rejected.Code);
            });
        }
    }
}
