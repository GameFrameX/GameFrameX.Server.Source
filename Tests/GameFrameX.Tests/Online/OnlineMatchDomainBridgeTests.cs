using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Hosting.Core;
using GameFrameX.Capability.Online.Abstractions.Bridges;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Events;
using GameFrameX.Capability.Online.Abstractions.Scope;
using GameFrameX.Capability.Online.Match;
using GameFrameX.Capability.Online.Matchmaking;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 匹配域桥接缝测试（C246）：域契约面装配、T2 裁定 R4-R13 的域面语义（即时窗口 / 取消终态回执 /
    /// 状态与票据映射 / 超时 / 入场到达同一对局）、holder 生命周期与模块拓扑序。
    /// </summary>
    public class OnlineMatchDomainBridgeTests : IDisposable
    {
        /// <summary>测试用租户标识。</summary>
        private const long TenantId = 1;

        /// <summary>测试用应用标识。</summary>
        private const long AppId = 1;

        /// <summary>测试用区服标识。</summary>
        private const long ServerId = 1001;

        /// <summary>玩家一。</summary>
        private const long PlayerOne = 2001;

        /// <summary>玩家二。</summary>
        private const long PlayerTwo = 2002;

        /// <summary>玩家三（非成员）。</summary>
        private const long PlayerThree = 2003;

        public OnlineMatchDomainBridgeTests()
        {
            OnlineMatchDomainBridgeHolder.Retract();
        }

        public void Dispose()
        {
            OnlineMatchDomainBridgeHolder.Retract();
        }

        /// <summary>
        /// 最小组合根（matchmaking + match 两运行时装配所需预置：作用域 + 事件出口 + 社交 gate 面 + 拓扑桩）。
        /// </summary>
        private static CapabilityHostBuilder MinimalRoot()
        {
            var builder = new CapabilityHostBuilder();
            builder.UseService(new OnlineScope(TenantId, AppId, ServerId));
            builder.UseService<IOnlineEventPublisher>(new OnlineEventPublisher(new GameFrameX.Foundation.Idempotency.InMemoryEventPublisher()));
            builder.UseService<IOnlineSocialGate>(new AllowAllSocialGate());
            builder.AddModule(new SocialStubModule());
            builder.AddModule(new AssetsStubModule());
            return builder;
        }

        /// <summary>
        /// 社交 gate 放行桩（满足 online-matchmaking 对 online-social 的依赖面）。
        /// </summary>
        private sealed class AllowAllSocialGate : IOnlineSocialGate
        {
            /// <inheritdoc />
            public Task<OnlineSocialDecision> EvaluateAsync(long tenantId, long appId, long fromPlayerId, long toPlayerId, OnlineSocialInteractionPurpose purpose, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(OnlineSocialDecision.Allow());
            }
        }

        /// <summary>
        /// online-social 拓扑桩（满足 Requires）。
        /// </summary>
        private sealed class SocialStubModule : ICapabilityModule
        {
            /// <inheritdoc />
            public CapabilityManifest Manifest { get; } = new("online-social", "1.0.0", Array.Empty<string>());

            public void Register(CapabilityRegistrationContext context)
            {
            }
        }

        /// <summary>
        /// online-assets 拓扑桩（满足 online-match 的 Requires）。
        /// </summary>
        private sealed class AssetsStubModule : ICapabilityModule
        {
            /// <inheritdoc />
            public CapabilityManifest Manifest { get; } = new("online-assets", "1.0.0", Array.Empty<string>());

            public void Register(CapabilityRegistrationContext context)
            {
            }
        }

        /// <summary>
        /// 构造合法宿主选项。
        /// </summary>
        private static CapabilityHostOptions ValidOptions()
        {
            return new CapabilityHostOptions { TenantId = TenantId, AppId = AppId, ServerId = ServerId };
        }

        /// <summary>
        /// 构造玩家作用域。
        /// </summary>
        private static OnlineScope PlayerScope(long playerId)
        {
            return new OnlineScope(TenantId, AppId, ServerId, playerId);
        }

        /// <summary>
        /// 构造已装配 matchmaking + match 运行时的宿主（不自启：仅验证装配与域面行为）。
        /// </summary>
        private static CapabilityHost BuildHost()
        {
            var builder = MinimalRoot();
            builder.AddOnlineMatchmakingRuntime();
            builder.AddOnlineMatchRuntime();
            return builder.Build(ValidOptions());
        }

        /// <summary>
        /// 模块自装配把匹配域契约面注册回共享面，且 matchmaking 先于 match 装配（拓扑序）。
        /// </summary>
        [Fact]
        public void Module_RegistersMatchDomainFace_AfterMatchmaking()
        {
            var host = BuildHost();

            Assert.NotNull(host.Services.GetService<IOnlineMatchDomain>());
            var order = host.Report.EnabledIdsInOrder.ToList();
            Assert.True(order.IndexOf("online-matchmaking") < order.IndexOf("online-match"), "online-matchmaking 必须先于 online-match 装配");
        }

        /// <summary>
        /// R5：单人入队无对手 → MatchId=0 排队；Status 反映排队中。
        /// </summary>
        [Fact]
        public async Task Enqueue_SoloPlayer_QueuesWithZeroMatchId()
        {
            var host = BuildHost();
            var domain = host.Services.GetService<IOnlineMatchDomain>();

            var enqueue = await domain.EnqueueAsync(PlayerScope(PlayerOne), 1, 1000);

            Assert.True(enqueue.IsSuccess);
            Assert.Equal(0, enqueue.Data.MatchId);
            var status = await domain.GetStatusAsync(PlayerScope(PlayerOne));
            Assert.True(status.Data.Queuing);
            Assert.False(status.Data.Matched);
            Assert.Equal(0, status.Data.MatchId);
        }

        /// <summary>
        /// R5/R9：第二人入队即时窗口成组——第二人响应即刻带 MatchId，第一人随后 Status 得同一 MatchId。
        /// </summary>
        [Fact]
        public async Task Enqueue_SecondPlayer_InstantWindowPairsBoth()
        {
            var host = BuildHost();
            var domain = host.Services.GetService<IOnlineMatchDomain>();

            await domain.EnqueueAsync(PlayerScope(PlayerOne), 1, 1000);
            var second = await domain.EnqueueAsync(PlayerScope(PlayerTwo), 1, 1050);

            Assert.True(second.IsSuccess);
            Assert.NotEqual(0, second.Data.MatchId);
            var firstStatus = await domain.GetStatusAsync(PlayerScope(PlayerOne));
            Assert.False(firstStatus.Data.Queuing);
            Assert.True(firstStatus.Data.Matched);
            Assert.Equal(second.Data.MatchId, firstStatus.Data.MatchId);
        }

        /// <summary>
        /// R4：排队中重复入队被拒（DuplicateRequest）。
        /// </summary>
        [Fact]
        public async Task Enqueue_DuplicatePlayer_Rejected()
        {
            var host = BuildHost();
            var domain = host.Services.GetService<IOnlineMatchDomain>();

            await domain.EnqueueAsync(PlayerScope(PlayerOne), 1, 1000);
            var duplicate = await domain.EnqueueAsync(PlayerScope(PlayerOne), 1, 1000);

            Assert.False(duplicate.IsSuccess);
            Assert.Equal(OnlineErrorCode.DuplicateRequest, duplicate.Code);
        }

        /// <summary>
        /// R6/R7：模式为池标签（不同模式不同池）；评分差超基础窗（±100）不成组。
        /// </summary>
        [Fact]
        public async Task Enqueue_ModePartitionsAndRatingWindow_PartitionPools()
        {
            var host = BuildHost();
            var domain = host.Services.GetService<IOnlineMatchDomain>();

            await domain.EnqueueAsync(PlayerScope(PlayerOne), 1, 1000);
            var otherMode = await domain.EnqueueAsync(PlayerScope(PlayerTwo), 2, 1050);
            Assert.Equal(0, otherMode.Data.MatchId);

            var third = await domain.EnqueueAsync(PlayerScope(PlayerThree), 1, 5000);
            Assert.Equal(0, third.Data.MatchId);
        }

        /// <summary>
        /// 入队本地校验：模式未定义 / 评分非正 → ParameterInvalid。
        /// </summary>
        [Fact]
        public async Task Enqueue_InvalidModeOrRating_Rejected()
        {
            var host = BuildHost();
            var domain = host.Services.GetService<IOnlineMatchDomain>();

            var badMode = await domain.EnqueueAsync(PlayerScope(PlayerOne), 9, 1000);
            Assert.False(badMode.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, badMode.Code);

            var badRating = await domain.EnqueueAsync(PlayerScope(PlayerOne), 1, 0);
            Assert.False(badRating.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, badRating.Code);
        }

        /// <summary>
        /// R8：排队中取消成功；无票取消回「未取消」回执（协议面映射 420003）。
        /// </summary>
        [Fact]
        public async Task Cancel_WhileQueued_Succeeds_AndWithoutTicket_ComesBackNotCancelled()
        {
            var host = BuildHost();
            var domain = host.Services.GetService<IOnlineMatchDomain>();

            await domain.EnqueueAsync(PlayerScope(PlayerOne), 1, 1000);
            var cancelled = await domain.CancelAsync(PlayerScope(PlayerOne));
            Assert.True(cancelled.IsSuccess);
            Assert.True(cancelled.Data.Cancelled);

            var noTicket = await domain.CancelAsync(PlayerScope(PlayerTwo));
            Assert.True(noTicket.IsSuccess);
            Assert.False(noTicket.Data.Cancelled);
        }

        /// <summary>
        /// R8：已成组后取消——终态回执不算失败，Cancelled=false（旧链 420003 可观测等价）。
        /// </summary>
        [Fact]
        public async Task Cancel_AfterMatch_ComesBackTerminalReceipt()
        {
            var host = BuildHost();
            var domain = host.Services.GetService<IOnlineMatchDomain>();

            await domain.EnqueueAsync(PlayerScope(PlayerOne), 1, 1000);
            await domain.EnqueueAsync(PlayerScope(PlayerTwo), 1, 1050);
            var lateCancel = await domain.CancelAsync(PlayerScope(PlayerOne));

            Assert.True(lateCancel.IsSuccess);
            Assert.False(lateCancel.Data.Cancelled);
        }

        /// <summary>
        /// R10：票据 TTL 过期（注入时刻推进扫描）→ Status=(false,0)，取消回未取消回执。
        /// </summary>
        [Fact]
        public async Task Status_AfterTicketExpiry_NotQueuingZeroMatch()
        {
            var host = BuildHost();
            var domain = host.Services.GetService<IOnlineMatchDomain>();
            var coordinator = host.Services.GetService<OnlineMatchmakerCoordinator>();

            await domain.EnqueueAsync(PlayerScope(PlayerOne), 1, 1000);
            var later = DateTimeOffset.UtcNow.AddSeconds(400).ToUnixTimeMilliseconds();
            await coordinator.RunOnceAsync(TenantId, AppId, later);

            var status = await domain.GetStatusAsync(PlayerScope(PlayerOne));
            Assert.False(status.Data.Queuing);
            Assert.False(status.Data.Matched);
            Assert.Equal(0, status.Data.MatchId);

            var cancel = await domain.CancelAsync(PlayerScope(PlayerOne));
            Assert.False(cancel.Data.Cancelled);
        }

        /// <summary>
        /// R13/T4：双方各自入场到达同一对局——同一数值 MatchId、双方成员、对局进入运行态（唯一 Actor）。
        /// </summary>
        [Fact]
        public async Task Enter_BothPlayers_ReachSameRunningMatch()
        {
            var host = BuildHost();
            var domain = host.Services.GetService<IOnlineMatchDomain>();
            var runtime = host.Services.GetService<OnlineMatchRuntime>();

            await domain.EnqueueAsync(PlayerScope(PlayerOne), 1, 1000);
            var second = await domain.EnqueueAsync(PlayerScope(PlayerTwo), 1, 1050);
            var matchId = second.Data.MatchId;

            var firstEntry = await domain.EnterAsync(PlayerScope(PlayerOne), matchId);
            var secondEntry = await domain.EnterAsync(PlayerScope(PlayerTwo), matchId);

            Assert.True(firstEntry.IsSuccess, firstEntry.Message);
            Assert.True(secondEntry.IsSuccess, secondEntry.Message);
            Assert.Equal(matchId, firstEntry.Data.MatchId);
            Assert.Equal(matchId, secondEntry.Data.MatchId);
            Assert.True(firstEntry.Data.Started);
            Assert.True(secondEntry.Data.Started);
            Assert.Contains(PlayerOne, firstEntry.Data.PlayerIds);
            Assert.Contains(PlayerTwo, secondEntry.Data.PlayerIds);

            var actors = runtime.SnapshotActors();
            var actor = Assert.Single(actors);
            var snapshot = actor.GetSnapshot();
            Assert.Equal(OnlineMatchState.Running, snapshot.State);
            Assert.Equal(2, snapshot.Members.Count(member => member != null));
        }

        /// <summary>
        /// R13：入场幂等——重复入场复用同一对局（create-or-reuse）。
        /// </summary>
        [Fact]
        public async Task Enter_Repeatedly_ReusesSameMatch()
        {
            var host = BuildHost();
            var domain = host.Services.GetService<IOnlineMatchDomain>();
            var runtime = host.Services.GetService<OnlineMatchRuntime>();

            await domain.EnqueueAsync(PlayerScope(PlayerOne), 1, 1000);
            var second = await domain.EnqueueAsync(PlayerScope(PlayerTwo), 1, 1050);

            await domain.EnterAsync(PlayerScope(PlayerOne), second.Data.MatchId);
            await domain.EnterAsync(PlayerScope(PlayerOne), second.Data.MatchId);

            Assert.Single(runtime.SnapshotActors());
        }

        /// <summary>
        /// R13：未知对局标识 → ResourceNotFound；非对局成员 → ScopeDenied。
        /// </summary>
        [Fact]
        public async Task Enter_UnknownOrForeign_Rejected()
        {
            var host = BuildHost();
            var domain = host.Services.GetService<IOnlineMatchDomain>();

            var unknown = await domain.EnterAsync(PlayerScope(PlayerOne), 424242);
            Assert.False(unknown.IsSuccess);
            Assert.Equal(OnlineErrorCode.ResourceNotFound, unknown.Code);

            await domain.EnqueueAsync(PlayerScope(PlayerOne), 1, 1000);
            var second = await domain.EnqueueAsync(PlayerScope(PlayerTwo), 1, 1050);
            var foreign = await domain.EnterAsync(PlayerScope(PlayerThree), second.Data.MatchId);
            Assert.False(foreign.IsSuccess);
            Assert.Equal(OnlineErrorCode.ScopeDenied, foreign.Code);
        }

        /// <summary>
        /// R12 支撑：通知推送方按 assignmentId 解析数值 MatchId 与成员表（撮合后可反复解析，幂等）。
        /// </summary>
        [Fact]
        public async Task ResolveAssignment_AfterMatch_ReturnsNumericAndPlayers_Idempotently()
        {
            var host = BuildHost();
            var domain = host.Services.GetService<IOnlineMatchDomain>();
            var store = host.Services.GetService<IOnlineMatchTicketStore>();

            await domain.EnqueueAsync(PlayerScope(PlayerOne), 1, 1000);
            var second = await domain.EnqueueAsync(PlayerScope(PlayerTwo), 1, 1050);
            var ticket = await store.FindLatestByPlayerAsync(TenantId, AppId, PlayerOne);
            Assert.NotNull(ticket);
            Assert.False(string.IsNullOrEmpty(ticket.AssignmentId));

            var resolved = await domain.ResolveAssignmentAsync(TenantId, AppId, ticket.AssignmentId);
            Assert.True(resolved.IsSuccess, resolved.Message);
            Assert.Equal(second.Data.MatchId, resolved.Data.MatchId);
            Assert.Contains(PlayerOne, resolved.Data.PlayerIds);
            Assert.Contains(PlayerTwo, resolved.Data.PlayerIds);

            var again = await domain.ResolveAssignmentAsync(TenantId, AppId, ticket.AssignmentId);
            Assert.Equal(resolved.Data.MatchId, again.Data.MatchId);
        }

        /// <summary>
        /// 票据按玩家最新读面：Matched 票据可查得（FindActiveByPlayer 只回 Queued 的缺口补齐）。
        /// </summary>
        [Fact]
        public async Task Store_FindLatestByPlayer_ReturnsMatchedTicket()
        {
            var host = BuildHost();
            var domain = host.Services.GetService<IOnlineMatchDomain>();
            var store = host.Services.GetService<IOnlineMatchTicketStore>();

            await domain.EnqueueAsync(PlayerScope(PlayerOne), 1, 1000);
            await domain.EnqueueAsync(PlayerScope(PlayerTwo), 1, 1050);

            var ticket = await store.FindLatestByPlayerAsync(TenantId, AppId, PlayerTwo);
            Assert.NotNull(ticket);
            Assert.Equal(OnlineMatchTicketState.Matched, ticket.State);
        }

        /// <summary>
        /// holder 生命周期：发布生成有效租约（可作状态查询）；再发布吊销旧租约；撤销后 Current 为空且旧租约显式失败。
        /// </summary>
        [Fact]
        public async Task Holder_PublishReplaceRetract_LeaseLifecycle()
        {
            var host = BuildHost();
            var domain = host.Services.GetService<IOnlineMatchDomain>();
            var scope = new OnlineScope(TenantId, AppId, ServerId);

            var first = OnlineMatchDomainBridgeHolder.Publish(domain, scope);
            var through = await first.GetStatusAsync(PlayerScope(PlayerOne));
            Assert.True(through.IsSuccess);

            var second = OnlineMatchDomainBridgeHolder.Publish(domain, scope);
            var staleFirst = await first.GetStatusAsync(PlayerScope(PlayerOne));
            Assert.False(staleFirst.IsSuccess);
            Assert.Equal(OnlineErrorCode.DependencyUnavailable, staleFirst.Code);

            OnlineMatchDomainBridgeHolder.Retract();
            Assert.Null(OnlineMatchDomainBridgeHolder.Current);
            var staleSecond = await second.GetStatusAsync(PlayerScope(PlayerOne));
            Assert.False(staleSecond.IsSuccess);
            Assert.Equal(OnlineErrorCode.DependencyUnavailable, staleSecond.Code);
            Assert.False(OnlineMatchDomainBridgeHolder.IsPublished);
        }

        /// <summary>
        /// R13/T4 并发回归：双端同时入场（真会话 E2E 实证过的竞态）——双方都必须成功且 Started=true。
        /// </summary>
        [Fact]
        public async Task Enter_ConcurrentlyFromBothPlayers_BothSucceed()
        {
            var host = BuildHost();
            var domain = host.Services.GetService<IOnlineMatchDomain>();

            await domain.EnqueueAsync(PlayerScope(PlayerOne), 1, 1000);
            var second = await domain.EnqueueAsync(PlayerScope(PlayerTwo), 1, 1050);
            var matchId = second.Data.MatchId;

            var firstEnter = Task.Run(() => domain.EnterAsync(PlayerScope(PlayerOne), matchId));
            var secondEnter = Task.Run(() => domain.EnterAsync(PlayerScope(PlayerTwo), matchId));
            var results = await Task.WhenAll(firstEnter, secondEnter);

            Assert.True(results[0].IsSuccess, results[0].Message);
            Assert.True(results[1].IsSuccess, results[1].Message);
            Assert.True(results[0].Data.Started);
            Assert.True(results[1].Data.Started);
            Assert.Equal(matchId, results[0].Data.MatchId);
            Assert.Equal(matchId, results[1].Data.MatchId);
        }

        /// <summary>
        /// 作用域校验：缺玩家主体位 → ParameterInvalid。
        /// </summary>
        [Fact]
        public async Task Enqueue_WithoutPlayerPosition_Rejected()
        {
            var host = BuildHost();
            var domain = host.Services.GetService<IOnlineMatchDomain>();
            var rejected = await domain.EnqueueAsync(new OnlineScope(TenantId, AppId, ServerId), 1, 1000);
            Assert.False(rejected.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, rejected.Code);
        }
    }
}
