// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
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
// ==========================================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Events;
using GameFrameX.Capability.Online.Match;
using GameFrameX.Capability.Online.Matchmaking;
using GameFrameX.Capability.Online.Abstractions.Scope;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// Match Runtime 与对局 Actor 生命周期测试：
    /// 分配建局、准备开局、阶段超时、终态释放与作用域隔离。
    /// </summary>
    public class OnlineMatchRuntimeTests
    {
        /// <summary>租户标识。</summary>
        private const long TenantId = 1;

        /// <summary>App 标识。</summary>
        private const long AppId = 10;

        /// <summary>区服标识。</summary>
        private const long ServerId = 100;

        /// <summary>玩家一。</summary>
        private const long PlayerOne = 1001;

        /// <summary>玩家二。</summary>
        private const long PlayerTwo = 1002;

        /// <summary>基准时刻（UTC 毫秒）。</summary>
        private const long Now = 1000000L;

        /// <summary>对局标识。</summary>
        private const string MatchId = "match-1";

        /// <summary>编排等待上限（防测试悬挂）。</summary>
        private static readonly TimeSpan OrchestrationTimeout = TimeSpan.FromSeconds(30);

        /// <summary>
        /// 验证分配建局后成员已就位并进入等待阶段。
        /// </summary>
        [Fact]
        public async Task CreateFromAssignmentAsync_ShouldSeedMembersAndEnterWaiting()
        {
            var harness = new Harness();

            var created = await harness.Runtime.CreateFromAssignmentAsync(BuildAssignment(PlayerOne, PlayerTwo), Now);

            Assert.True(created.IsSuccess, created.Message);
            Assert.Equal(MatchId, created.Data.MatchId);
            Assert.Equal(OnlineMatchState.Waiting, created.Data.State);
            Assert.Equal(OnlineRockPaperScissorsGame.MatchMode, created.Data.Mode);
            Assert.Equal(2, created.Data.Members.Count);
            Assert.Equal(1, harness.Runtime.ActiveActorCount);
        }

        /// <summary>
        /// 验证：未注册玩法的模式不得建局（避免产出无法推进的对局）。
        /// </summary>
        [Fact]
        public async Task CreateFromAssignmentAsync_UnknownMode_ShouldFail()
        {
            var harness = new Harness();
            var assignment = BuildAssignment(PlayerOne, PlayerTwo);
            assignment.Mode = 999999;

            var created = await harness.Runtime.CreateFromAssignmentAsync(assignment, Now);

            Assert.False(created.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, created.Code);
            Assert.Equal(0, harness.Runtime.ActiveActorCount);
        }

        /// <summary>
        /// 验证全员准备 → 房主开局 → 回合输入 → 分出胜负后进入结算阶段，且全程发布状态变更事件。
        /// </summary>
        [Fact]
        public async Task FullFlow_ShouldReachSettlingAndPublishStateChanged()
        {
            var harness = new Harness();
            await harness.CreateAsync();

            await harness.ReadyAndStartAsync(Now);

            Assert.Equal(OnlineMatchState.Running, harness.Actor.State);

            // 三局两胜：玩家一连续两局出石头胜剪刀，直接终结对局。
            await harness.SubmitAsync(PlayerOne, 1, OnlineRockPaperScissorsMove.Rock, Now + 10);
            await harness.SubmitAsync(PlayerTwo, 1, OnlineRockPaperScissorsMove.Scissors, Now + 20);
            await harness.SubmitAsync(PlayerOne, 2, OnlineRockPaperScissorsMove.Rock, Now + 30);
            var finalAck = await harness.SubmitAsync(PlayerTwo, 2, OnlineRockPaperScissorsMove.Scissors, Now + 40);

            Assert.True(finalAck.Accepted);
            Assert.Equal(OnlineMatchState.Settling, harness.Actor.State);
            Assert.Contains(harness.Publisher.Events, item => item.EventType == OnlineMatchRuntimeEvents.MatchStateChanged);
            Assert.Contains(harness.Publisher.Events, item => item.EventType == OnlineMatchRuntimeEvents.MatchStateChanged && item.Source == OnlineMatchRuntimeEvents.Source);
        }

        /// <summary>
        /// 验证等待阶段超时进入 Timeout，并在保留期后释放为 Closed（无僵尸对局）。
        /// </summary>
        [Fact]
        public async Task TickAsync_StageTimeout_ShouldReachTimeoutThenReleaseActor()
        {
            var harness = new Harness(new OnlineMatchRuntimeOptions
            {
                WaitingTimeoutSeconds = 1,
                EndedRetentionSeconds = 1,
            });
            await harness.CreateAsync();

            var timedOut = await harness.Runtime.TickAllAsync(Now + 2000);

            Assert.True(timedOut[0].TimedOut);
            Assert.Equal(OnlineMatchState.Timeout, harness.Actor.State);

            var closed = await harness.Runtime.TickAllAsync(Now + 4000);

            Assert.True(closed[0].Closable);
            Assert.Equal(OnlineMatchState.Closed, harness.Actor.State);
            Assert.Equal(0, harness.Runtime.ActiveActorCount);
            Assert.Equal(1, harness.Runtime.ReleasedActorCount);
            Assert.Null(await harness.Store.FindAsync(TenantId, AppId, MatchId));
        }

        /// <summary>
        /// 验证跨租户作用域不得读写他人对局。
        /// </summary>
        [Fact]
        public async Task CrossTenantScope_ShouldBeDenied()
        {
            var harness = new Harness();
            await harness.CreateAsync();

            var foreign = new OnlineScope(TenantId + 1, AppId, ServerId, PlayerOne);
            var result = await harness.Actor.SetReadyAsync(foreign, true, Now);

            Assert.False(result.IsSuccess);
            Assert.Equal(OnlineErrorCode.ScopeDenied, result.Code);
        }

        /// <summary>
        /// 验证仅房主可开始对局（房主 = 最早加入的在局成员）。
        /// </summary>
        [Fact]
        public async Task StartAsync_NonHost_ShouldBeForbidden()
        {
            var harness = new Harness();
            await harness.CreateAsync();
            await harness.Actor.SetReadyAsync(harness.ScopeOf(PlayerOne), true, Now);
            await harness.Actor.SetReadyAsync(harness.ScopeOf(PlayerTwo), true, Now);

            var started = await harness.Actor.StartAsync(harness.ScopeOf(PlayerTwo), Now);

            Assert.False(started.IsSuccess);
            Assert.Equal(OnlineErrorCode.StateOperationForbidden, started.Code);
            Assert.Equal(OnlineMatchState.Ready, harness.Actor.State);
        }

        /// <summary>
        /// 验证成员未齐或未全部准备时不得开局。
        /// </summary>
        [Fact]
        public async Task StartAsync_NotAllReady_ShouldBeRejected()
        {
            var harness = new Harness();
            await harness.CreateAsync();
            await harness.Actor.SetReadyAsync(harness.ScopeOf(PlayerOne), true, Now);

            var started = await harness.Actor.StartAsync(harness.ScopeOf(PlayerOne), Now);

            Assert.False(started.IsSuccess);
            Assert.Equal(OnlineErrorCode.StateNotReady, started.Code);
        }

        /// <summary>
        /// 验证踢出后成员退出对局且不再是有效参与者。
        /// </summary>
        [Fact]
        public async Task KickAsync_ShouldRemoveMemberFromActiveSet()
        {
            var harness = new Harness();
            await harness.CreateAsync();

            var kicked = await harness.Actor.KickAsync(harness.ScopeOf(PlayerOne), PlayerTwo, Now + 5);

            Assert.True(kicked.IsSuccess, kicked.Message);
            var member = kicked.Data.Members.Find(item => item.PlayerId == PlayerTwo);
            Assert.NotNull(member);
            Assert.Equal(OnlineMatchMemberState.Kicked, member.State);
        }

        /// <summary>
        /// 验证并发解析收敛：两个请求都越过缓存 miss（FindAsync 栅栏对齐）后并发注册，
        /// 必须收敛为同一 Actor 实例——旧盲写注册下后写覆盖先写、先到方持有孤儿实例（行为红）。
        /// </summary>
        [Fact]
        public async Task ResolveActorAsync_ConcurrentMiss_ShouldConvergeToSingleActor()
        {
            var store = new OrchestratedMatchActorStore(alignConcurrentFinds: true);
            await store.SeedAsync(BuildSeededMatch());
            var runtime = new OnlineMatchRuntime(store, new OnlineEventRecorder(), new OnlineMatchRuntimeOptions());
            runtime.RegisterGame(new OnlineRockPaperScissorsGame());

            var first = Task.Run(() => runtime.ResolveActorAsync(TenantId, AppId, MatchId));
            var second = Task.Run(() => runtime.ResolveActorAsync(TenantId, AppId, MatchId));
            var actors = await Task.WhenAll(first, second);

            Assert.NotNull(actors[0]);
            Assert.NotNull(actors[1]);
            Assert.Same(actors[0], actors[1]);
            Assert.Equal(1, runtime.ActiveActorCount);
        }

        /// <summary>
        /// 验证建局与解析并发收敛：建局落库后、注册前被解析方抢先注册时，
        /// 建局方必须复用在册实例而非盲写覆盖（旧实现下后续解析返回被覆盖实例，行为红）。
        /// </summary>
        [Fact]
        public async Task CreateFromAssignmentAsync_ConcurrentResolve_ShouldReuseRegisteredActor()
        {
            var store = new OrchestratedMatchActorStore(alignConcurrentFinds: false);
            var runtime = new OnlineMatchRuntime(store, new OnlineEventRecorder(), new OnlineMatchRuntimeOptions());
            runtime.RegisterGame(new OnlineRockPaperScissorsGame());

            var createTask = Task.Run(() => runtime.CreateFromAssignmentAsync(BuildAssignment(PlayerOne, PlayerTwo), Now));
            await store.CreatePersisted.Task.WaitAsync(OrchestrationTimeout);

            var resolverActor = await runtime.ResolveActorAsync(TenantId, AppId, MatchId);
            Assert.NotNull(resolverActor);
            store.ResolverRegistered.TrySetResult();

            var created = await createTask;
            Assert.True(created.IsSuccess, created.Message);

            var afterCreate = await runtime.ResolveActorAsync(TenantId, AppId, MatchId);
            Assert.Same(resolverActor, afterCreate);
            Assert.Equal(1, runtime.ActiveActorCount);
        }

        /// <summary>
        /// 验证并发解析压力不变量：空登记表运行时上多任务并发装载同一对局，
        /// 每轮全部调用方拿到同一实例且登记表计数恒 1。
        /// </summary>
        [Fact]
        public async Task ResolveActorAsync_ParallelStress_ShouldHoldSingleInstanceInvariant()
        {
            var harness = new Harness();
            await harness.CreateAsync();

            for (var round = 0; round < 10; round++)
            {
                var runtime = new OnlineMatchRuntime(harness.Store, new OnlineEventRecorder(), new OnlineMatchRuntimeOptions());
                runtime.RegisterGame(new OnlineRockPaperScissorsGame());

                var resolves = new Task<OnlineMatchActor>[8];
                for (var index = 0; index < resolves.Length; index++)
                {
                    resolves[index] = Task.Run(() => runtime.ResolveActorAsync(TenantId, AppId, MatchId));
                }

                var actors = await Task.WhenAll(resolves);
                foreach (var actor in actors)
                {
                    Assert.NotNull(actor);
                    Assert.Same(actors[0], actor);
                }

                Assert.Equal(1, runtime.ActiveActorCount);
            }
        }

        /// <summary>
        /// 构造只含指定玩家的匹配分配。
        /// </summary>
        /// <param name="playerIds">玩家集合。</param>
        /// <returns>分配实例。</returns>
        private static OnlineMatchAssignment BuildAssignment(params long[] playerIds)
        {
            return new OnlineMatchAssignment
            {
                AssignmentId = "asg-1",
                MatchId = MatchId,
                PlayerIds = new List<long>(playerIds),
                Mode = OnlineRockPaperScissorsGame.MatchMode,
                Region = 1,
                CreatedAtTime = Now,
                TicketIds = new List<string>(),
                TenantId = TenantId,
                AppId = AppId,
                ServerId = ServerId,
            };
        }

        /// <summary>
        /// 测试基座：内存存储 + 事件记录桩 + 石头剪刀布玩法。
        /// </summary>
        private sealed class Harness
        {
            /// <summary>
            /// 以包内默认运行参数初始化测试基座。
            /// </summary>
            public Harness() : this(new OnlineMatchRuntimeOptions())
            {
            }

            /// <summary>
            /// 以指定运行参数初始化测试基座。
            /// </summary>
            /// <param name="options">运行参数。</param>
            public Harness(OnlineMatchRuntimeOptions options)
            {
                Store = new InMemoryOnlineMatchActorStore();
                Publisher = new OnlineEventRecorder();
                Runtime = new OnlineMatchRuntime(Store, Publisher, options);
                Runtime.RegisterGame(new OnlineRockPaperScissorsGame());
            }

            /// <summary>获取对局存储。</summary>
            public InMemoryOnlineMatchActorStore Store
            {
                get;
            }

            /// <summary>获取事件记录桩。</summary>
            public OnlineEventRecorder Publisher
            {
                get;
            }

            /// <summary>获取运行时。</summary>
            public OnlineMatchRuntime Runtime
            {
                get;
            }

            /// <summary>获取已装载的对局 Actor。</summary>
            public OnlineMatchActor Actor
            {
                get;
                private set;
            }

            /// <summary>
            /// 按分配建局。
            /// </summary>
            /// <returns>完成通知。</returns>
            public async Task CreateAsync()
            {
                var created = await Runtime.CreateFromAssignmentAsync(BuildAssignment(PlayerOne, PlayerTwo), Now);
                Assert.True(created.IsSuccess, created.Message);
                Actor = await Runtime.ResolveActorAsync(TenantId, AppId, MatchId);
                Assert.NotNull(Actor);
            }

            /// <summary>
            /// 全员准备并由房主开局。
            /// </summary>
            /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒）。</param>
            /// <returns>完成通知。</returns>
            public async Task ReadyAndStartAsync(long nowUnixMilliseconds)
            {
                await Actor.SetReadyAsync(ScopeOf(PlayerOne), true, nowUnixMilliseconds);
                await Actor.SetReadyAsync(ScopeOf(PlayerTwo), true, nowUnixMilliseconds + 1);
                var started = await Actor.StartAsync(ScopeOf(PlayerOne), nowUnixMilliseconds + 2);
                Assert.True(started.IsSuccess, started.Message);
            }

            /// <summary>
            /// 提交一次出拳意图。
            /// </summary>
            /// <param name="playerId">玩家标识。</param>
            /// <param name="clientSequence">客户端序号。</param>
            /// <param name="move">出拳。</param>
            /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒）。</param>
            /// <returns>输入确认。</returns>
            public async Task<OnlineMatchInputAck> SubmitAsync(long playerId, int clientSequence, OnlineRockPaperScissorsMove move, long nowUnixMilliseconds)
            {
                var input = new OnlineMatchInput
                {
                    PlayerId = playerId,
                    ClientSequence = clientSequence,
                    ActionId = (int)move,
                    ClientTime = nowUnixMilliseconds,
                };

                var ack = await Actor.SubmitInputAsync(ScopeOf(playerId), input, nowUnixMilliseconds);
                Assert.True(ack.IsSuccess, ack.Message);
                return ack.Data;
            }

            /// <summary>
            /// 构造玩家级作用域。
            /// </summary>
            /// <param name="playerId">玩家标识。</param>
            /// <returns>作用域。</returns>
            public OnlineScope ScopeOf(long playerId)
            {
                return new OnlineScope(TenantId, AppId, ServerId, playerId);
            }
        }

        /// <summary>
        /// 构造已落库的种子对局（并发装载用例不经建局编排直接预置）。
        /// </summary>
        /// <returns>种子对局。</returns>
        private static OnlineMatch BuildSeededMatch()
        {
            return new OnlineMatch
            {
                TenantId = TenantId,
                AppId = AppId,
                ServerId = ServerId,
                MatchId = MatchId,
                Mode = OnlineRockPaperScissorsGame.MatchMode,
                State = OnlineMatchState.Waiting,
                Members = new List<OnlineMatchMember>
                {
                    new OnlineMatchMember
                    {
                        PlayerId = PlayerOne,
                        State = OnlineMatchMemberState.Joined,
                        JoinedTime = Now,
                        StateChangedTime = Now,
                    },
                },
                CreatedTime = Now,
                StateChangedTime = Now,
            };
        }

        /// <summary>
        /// 可编排时序的对局存储桩：委托内存实现，并在 FindAsync / CreateAsync 注入栅栏，
        /// 用于确定性复现「并发 miss 同时装载」与「建局落库后注册前被解析抢先」两个竞争窗口。
        /// </summary>
        private sealed class OrchestratedMatchActorStore : IOnlineMatchActorStore
        {
            /// <summary>内存实现（委托目标）。</summary>
            private readonly InMemoryOnlineMatchActorStore _inner = new();

            /// <summary>是否对齐并发 FindAsync（true = 前两次解析彼此等待后同时返回）。</summary>
            private readonly bool _alignConcurrentFinds;

            /// <summary>FindAsync 到达计数。</summary>
            private int _findArrivalCount;

            /// <summary>并发计数委托内存实现。</summary>
            public Task<IReadOnlyDictionary<OnlineMatchState, int>> CountActiveByScopeAsync(long tenantId, long appId, long serverId, CancellationToken cancellationToken = default)
            {
                return _inner.CountActiveByScopeAsync(tenantId, appId, serverId, cancellationToken);
            }

            /// <summary>
            /// 以编排开关初始化存储桩。
            /// </summary>
            /// <param name="alignConcurrentFinds">是否对齐并发 FindAsync。</param>
            public OrchestratedMatchActorStore(bool alignConcurrentFinds)
            {
                _alignConcurrentFinds = alignConcurrentFinds;
            }

            /// <summary>首个 FindAsync 已到达。</summary>
            public TaskCompletionSource FirstFindArrived
            {
                get;
            } = CreateSource();

            /// <summary>第二个 FindAsync 已到达。</summary>
            public TaskCompletionSource SecondFindArrived
            {
                get;
            } = CreateSource();

            /// <summary>建局已落库（建局方注册前）。</summary>
            public TaskCompletionSource CreatePersisted
            {
                get;
            } = CreateSource();

            /// <summary>解析方已完成注册（放行建局方注册）。</summary>
            public TaskCompletionSource ResolverRegistered
            {
                get;
            } = CreateSource();

            /// <summary>
            /// 落库后（成功时）阻塞至解析方注册完成再返回——复现建局方注册前的竞争窗口。
            /// </summary>
            /// <param name="match">对局。</param>
            /// <param name="cancellationToken">取消令牌。</param>
            /// <returns>落库结果。</returns>
            public async Task<OnlineMatch> CreateAsync(OnlineMatch match, CancellationToken cancellationToken = default)
            {
                var persisted = await _inner.CreateAsync(match, cancellationToken).ConfigureAwait(false);
                if (persisted != null)
                {
                    CreatePersisted.TrySetResult();
                    await ResolverRegistered.Task.WaitAsync(OrchestrationTimeout).ConfigureAwait(false);
                }

                return persisted;
            }

            /// <summary>
            /// 对齐模式下前两次查找彼此等待后同时返回，确保两方都越过缓存 miss 各自构造后并发注册。
            /// </summary>
            /// <param name="tenantId">租户标识。</param>
            /// <param name="appId">App 标识。</param>
            /// <param name="matchId">对局标识。</param>
            /// <param name="cancellationToken">取消令牌。</param>
            /// <returns>对局副本；不存在返回 null。</returns>
            public async Task<OnlineMatch> FindAsync(long tenantId, long appId, string matchId, CancellationToken cancellationToken = default)
            {
                if (_alignConcurrentFinds)
                {
                    var arrival = Interlocked.Increment(ref _findArrivalCount);
                    if (arrival == 1)
                    {
                        FirstFindArrived.TrySetResult();
                        await SecondFindArrived.Task.WaitAsync(OrchestrationTimeout).ConfigureAwait(false);
                    }
                    else if (arrival == 2)
                    {
                        SecondFindArrived.TrySetResult();
                        await FirstFindArrived.Task.WaitAsync(OrchestrationTimeout).ConfigureAwait(false);
                    }
                }

                return await _inner.FindAsync(tenantId, appId, matchId, cancellationToken).ConfigureAwait(false);
            }

            /// <summary>委托内存实现。</summary>
            /// <param name="match">对局。</param>
            /// <param name="cancellationToken">取消令牌。</param>
            /// <returns>更新结果。</returns>
            public Task<OnlineMatch> UpdateAsync(OnlineMatch match, CancellationToken cancellationToken = default)
            {
                return _inner.UpdateAsync(match, cancellationToken);
            }

            /// <summary>委托内存实现。</summary>
            /// <param name="tenantId">租户标识。</param>
            /// <param name="appId">App 标识。</param>
            /// <param name="matchId">对局标识。</param>
            /// <param name="cancellationToken">取消令牌。</param>
            /// <returns>删除结果。</returns>
            public Task<bool> DeleteAsync(long tenantId, long appId, string matchId, CancellationToken cancellationToken = default)
            {
                return _inner.DeleteAsync(tenantId, appId, matchId, cancellationToken);
            }

            /// <summary>委托内存实现。</summary>
            /// <param name="tenantId">租户标识。</param>
            /// <param name="appId">App 标识。</param>
            /// <param name="cancellationToken">取消令牌。</param>
            /// <returns>对局副本列表。</returns>
            public Task<IReadOnlyList<OnlineMatch>> ListAsync(long tenantId, long appId, CancellationToken cancellationToken = default)
            {
                return _inner.ListAsync(tenantId, appId, cancellationToken);
            }

            /// <summary>
            /// 预置已落库对局（不经建局编排）。
            /// </summary>
            /// <param name="match">对局。</param>
            /// <returns>落库结果。</returns>
            public Task<OnlineMatch> SeedAsync(OnlineMatch match)
            {
                return _inner.CreateAsync(match);
            }

            /// <summary>
            /// 构造延续异步执行的完成源（避免内联延续阻塞设置方线程）。
            /// </summary>
            /// <returns>完成源。</returns>
            private static TaskCompletionSource CreateSource()
            {
                return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }
    }
}
