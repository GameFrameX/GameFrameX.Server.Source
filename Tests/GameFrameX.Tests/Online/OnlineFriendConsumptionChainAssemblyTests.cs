// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and other rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws and regulations of the People's Republic of China and international regulations.
//   使用本项目须严格遵守相关法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the full license text in the LICENSE file located at the root of the source code.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activity that endangers national security, disrupts social order,
//   侵犯公民合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from derivative development,
//   本组织不承担任何责任，开发者应自行承担。
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
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Hosting.Core;
using GameFrameX.Capability.Online.Abstractions;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Scope;
using GameFrameX.Capability.Online.Operations.HotfixRollback;
using GameFrameX.Capability.Online.Social;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 好友最小消费链装配测试（C242，C294 改写为真实族组合）：OnlineFriendService 经 Social 模块自装配注册回共享面、
    /// store 替换缝（宿主 Override）与 directory / presence 扩展点生效、好友请求过期清扫滴答 job 与查询共用同一状态来源、
    /// 宿主启停驱动清扫、清扫失败 containment 与白名单排除 Social 的缺席行为。
    /// <para>好友请求 / 接受 / 过期均发布事件——事件总线 Activate 前拒绝发布，故涉及写路径的用例先启动业务面。</para>
    /// </summary>
    [Collection(CapabilityRuntimeCollection.Name)]
    public class OnlineFriendConsumptionChainAssemblyTests
    {
        /// <summary>测试用租户标识（与族配置段授权作用域一致）。</summary>
        private const long TenantId = OnlineCapabilityTestHarness.TenantId;

        /// <summary>测试用应用标识。</summary>
        private const long AppId = OnlineCapabilityTestHarness.AppId;

        /// <summary>测试用区服标识（与族配置段授权作用域一致）。</summary>
        private const long ServerId = OnlineCapabilityTestHarness.ServerId;

        /// <summary>测试用玩家一标识（请求发起方）。</summary>
        private const long PlayerOne = 1001;

        /// <summary>测试用玩家二标识（被请求方）。</summary>
        private const long PlayerTwo = 1002;

        /// <summary>
        /// 构造玩家作用域（作用域三元组与测试常量一致）。
        /// </summary>
        /// <param name="playerId">玩家标识。</param>
        /// <returns>作用域。</returns>
        private static OnlineScope CreatePlayerScope(long playerId)
        {
            return new OnlineScope(TenantId, AppId, ServerId, playerId);
        }

        /// <summary>
        /// 构造自定义玩家目录的组合选项（不经挂具 CreateOptions——同键第二个 Override 即装配失败；
        /// 热更回滚执行器仍以空操作桩满足 Operations 的宿主扩展点）。
        /// </summary>
        /// <param name="directory">玩家目录宿主实现。</param>
        /// <returns>组合选项。</returns>
        private static CapabilityCompositionOptions CreateOptionsWithDirectory(IOnlinePlayerDirectory directory)
        {
            var options = new CapabilityCompositionOptions(OnlineCapabilityTestHarness.FamilySection(), new CollectingCapabilityAssemblyLog())
            {
                HostOptions = new CapabilityHostOptions { TenantId = TenantId, AppId = AppId, ServerId = ServerId, },
            };
            options.Override<IOnlinePlayerDirectory>(directory);
            options.Override<IOnlineHotfixRollbackExecutor>(new OnlineCapabilityTestHarness.NoopHotfixRollbackExecutor());
            return options;
        }

        /// <summary>
        /// 直接向存储落一条已超期的待答复请求（清扫用例的种子数据；不经过服务以控制时间戳）。
        /// </summary>
        /// <param name="store">好友存储。</param>
        /// <param name="tenantId">租户标识。</param>
        /// <param name="appId">App 标识。</param>
        /// <param name="requesterId">发起方。</param>
        /// <param name="addresseeId">被请求方。</param>
        /// <returns>落库后的关系记录。</returns>
        private static async Task<OnlineFriendship> SeedExpiredRequestAsync(IOnlineFriendStore store, long tenantId, long appId, long requesterId, long addresseeId)
        {
            OnlineFriendship.Canonicalize(requesterId, addresseeId, out var low, out var high);
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            return await store.SaveIfAbsentAsync(new OnlineFriendship
            {
                FriendshipId = "frd-seed-" + Guid.NewGuid().ToString("N"),
                TenantId = tenantId,
                AppId = appId,
                ServerId = ServerId,
                LowPlayerId = low,
                HighPlayerId = high,
                RequesterId = requesterId,
                AddresseeId = addresseeId,
                State = OnlineFriendshipState.Requested,
                CreatedAtTime = now - 10000,
                UpdatedAtTime = now - 10000,
                ExpiresAtTime = now - 1000,
            });
        }

        /// <summary>
        /// 轮询等待条件成立（超时返回 false 由调用方断言）。
        /// </summary>
        /// <param name="condition">条件。</param>
        /// <param name="timeoutMilliseconds">超时毫秒。</param>
        /// <returns>条件成立返回 true。</returns>
        private static async Task<bool> UntilAsync(Func<Task<bool>> condition, int timeoutMilliseconds)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMilliseconds);
            while (DateTime.UtcNow < deadline)
            {
                if (await condition())
                {
                    return true;
                }

                await Task.Delay(50);
            }

            return await condition();
        }

        /// <summary>
        /// 真实族组合：共享面可解析 OnlineFriendService，服务写入与过期清扫共用同一存储实例
        ///（服务写入 → 共享面存储可见 → 单轮 TickAsync 终结超期请求、未超期请求保持待答复）。
        /// </summary>
        [Fact]
        public async Task Compose_ExposesFriendServiceWithSweepOnSameStore()
        {
            await OnlineCapabilityTestHarness.WithRuntimeAsync(async runtime =>
            {
                var service = runtime.Host.Services.GetService<OnlineFriendService>();
                var store = runtime.Host.Services.GetService<IOnlineFriendStore>();
                Assert.NotNull(service);
                Assert.IsType<InMemoryOnlineFriendStore>(store);
                await runtime.Host.StartAsync();

                var request = await service.RequestAsync(CreatePlayerScope(PlayerOne), PlayerTwo, "corr-2");
                Assert.True(request.IsSuccess);
                Assert.NotNull(await store.FindAsync(TenantId, AppId, PlayerOne, PlayerTwo));

                await SeedExpiredRequestAsync(store, TenantId, AppId, PlayerTwo, PlayerOne + 10);
                await runtime.Host.Scheduler.TickAsync();

                var expired = await store.FindAsync(TenantId, AppId, PlayerTwo, PlayerOne + 10);
                Assert.Equal(OnlineFriendshipState.Expired, expired.State);
                var live = await store.FindAsync(TenantId, AppId, PlayerOne, PlayerTwo);
                Assert.Equal(OnlineFriendshipState.Requested, live.State);
            });
        }

        /// <summary>
        /// 好友存储替换缝（宿主 Override）：预置实例胜出 Social 包内默认（共享面同实例），
        /// 过期清扫滴答 job 与服务使用同一条预置存储（单轮 TickAsync 即终结超期请求）。
        /// </summary>
        [Fact]
        public async Task OverrideFriendStore_PreSeed_DrivesServiceAndSweepOnSameStateSource()
        {
            var preSeeded = new InMemoryOnlineFriendStore();
            await SeedExpiredRequestAsync(preSeeded, TenantId, AppId, PlayerOne, PlayerTwo);

            await OnlineCapabilityTestHarness.WithRuntimeAsync(async runtime =>
            {
                Assert.Same(preSeeded, runtime.Host.Services.GetService<IOnlineFriendStore>());
                await runtime.Host.StartAsync();

                var service = runtime.Host.Services.GetService<OnlineFriendService>();
                var request = await service.RequestAsync(CreatePlayerScope(PlayerTwo), PlayerOne + 10);
                Assert.True(request.IsSuccess);
                Assert.NotNull(await preSeeded.FindAsync(TenantId, AppId, PlayerTwo, PlayerOne + 10));

                await runtime.Host.Scheduler.TickAsync();

                var expired = await preSeeded.FindAsync(TenantId, AppId, PlayerOne, PlayerTwo);
                Assert.Equal(OnlineFriendshipState.Expired, expired.State);
            }, null, options => options.Override<IOnlineFriendStore>(preSeeded));
        }

        /// <summary>
        /// 空玩家目录（宿主扩展点以空实现满足）：好友搜索降级为空结果——这是装配披露的边界
        ///（空结果 ≠ 无匹配玩家，搜索能力缺席），不是可用的搜索功能。
        /// </summary>
        [Fact]
        public async Task EmptyDirectory_SearchDegradesToEmpty()
        {
            await OnlineCapabilityTestHarness.WithRuntimeAsync(async runtime =>
            {
                var service = runtime.Host.Services.GetService<OnlineFriendService>();
                var outcome = await service.SearchAsync(CreatePlayerScope(PlayerOne), "alice");

                Assert.True(outcome.IsSuccess);
                Assert.Empty(outcome.Data);
            });
        }

        /// <summary>
        /// 玩家目录（宿主 Override）与在线探针（宿主 Override 胜出 Foundation 默认探针）被 Social 采用：
        /// 搜索返回目录条目、好友列表带展示名与在线事实——证明缺席是可注入的披露态而非隐藏失败。
        /// </summary>
        [Fact]
        public async Task OverriddenDirectoryAndPresenceProbe_FeedFriendService()
        {
            var directory = new StubPlayerDirectory();
            directory.Add(PlayerTwo, "alice");
            var probe = new StubPresenceProbe();
            probe.SetOnline(PlayerTwo);
            var options = CreateOptionsWithDirectory(directory);
            options.Override<IOnlineSocialPresenceProbe>(probe);

            var runtime = CapabilityComposition.Compose(new OnlineCapabilityFamily(), options);
            try
            {
                Assert.Same(probe, runtime.Host.Services.GetService<IOnlineSocialPresenceProbe>());
                Assert.Same(directory, runtime.Host.Services.GetService<IOnlinePlayerDirectory>());
                await runtime.Host.StartAsync();

                var service = runtime.Host.Services.GetService<OnlineFriendService>();
                var search = await service.SearchAsync(CreatePlayerScope(PlayerOne), "ali");
                Assert.True(search.IsSuccess);
                var entry = Assert.Single(search.Data);
                Assert.Equal(PlayerTwo, entry.PlayerId);

                var request = await service.RequestAsync(CreatePlayerScope(PlayerOne), PlayerTwo);
                Assert.True(request.IsSuccess);
                var accepted = await service.AcceptAsync(CreatePlayerScope(PlayerTwo), request.Data.FriendshipId);
                Assert.True(accepted.IsSuccess);

                var friends = await service.ListFriendsAsync(CreatePlayerScope(PlayerOne));
                Assert.True(friends.IsSuccess);
                var summary = Assert.Single(friends.Data);
                Assert.Equal("alice", summary.Name);
                Assert.True(summary.Online);
            }
            finally
            {
                await runtime.StopAsync();
            }
        }

        /// <summary>
        /// 清扫存储输入持续抛异常：当轮滴答失败被调度壳逐 job 吞掉、循环继续，
        /// 宿主停机干净、服务查询面仍可用——异常 containment 的行为级可观测契约。
        /// </summary>
        [Fact]
        public async Task ThrowingFriendStore_SweepFailureContained_HostStaysRunnable()
        {
            var throwing = new ThrowingSweepFriendStore();
            await OnlineCapabilityTestHarness.WithRuntimeAsync(async runtime =>
            {
                await runtime.Host.StartAsync();
                var observedThrow = await UntilAsync(() => Task.FromResult(throwing.SweepInputFailures > 1), 5000);
                await runtime.Host.StopAsync();

                Assert.True(observedThrow);
                Assert.True(throwing.SweepInputFailures >= 2);

                var service = runtime.Host.Services.GetService<OnlineFriendService>();
                var friends = await service.ListFriendsAsync(CreatePlayerScope(PlayerOne));
                Assert.True(friends.IsSuccess);
                Assert.Empty(friends.Data);
            }, null, options =>
            {
                options.HostOptions.SchedulerIntervalMilliseconds = 100;
                options.Override<IOnlineFriendStore>(throwing);
            });
        }

        /// <summary>
        /// 清扫的作用域隔离：滴答 job 经族基建授权作用域锚定租户 / App——
        /// 其余租户或 App 的超期请求不被本宿主触碰。
        /// </summary>
        [Fact]
        public async Task Sweep_ScopedToAuthorizedTenantAndApp()
        {
            await OnlineCapabilityTestHarness.WithRuntimeAsync(async runtime =>
            {
                var store = runtime.Host.Services.GetService<IOnlineFriendStore>();
                await SeedExpiredRequestAsync(store, TenantId, AppId, PlayerOne, PlayerTwo);
                await SeedExpiredRequestAsync(store, TenantId + 1, AppId, PlayerOne, PlayerTwo);
                await SeedExpiredRequestAsync(store, TenantId, AppId + 1, PlayerOne, PlayerTwo);
                await runtime.Host.StartAsync();

                await runtime.Host.Scheduler.TickAsync();

                Assert.Equal(OnlineFriendshipState.Expired, (await store.FindAsync(TenantId, AppId, PlayerOne, PlayerTwo)).State);
                Assert.Equal(OnlineFriendshipState.Requested, (await store.FindAsync(TenantId + 1, AppId, PlayerOne, PlayerTwo)).State);
                Assert.Equal(OnlineFriendshipState.Requested, (await store.FindAsync(TenantId, AppId + 1, PlayerOne, PlayerTwo)).State);
            });
        }

        /// <summary>
        /// 宿主启停生命周期：StartAsync 后调度循环自动驱动清扫（无需手动 TickAsync）；
        /// StopAsync 后循环退出，新落的超期请求不再被终结。
        /// </summary>
        [Fact]
        public async Task HostStartDrivesSweepAndStopHaltsIt()
        {
            await OnlineCapabilityTestHarness.WithRuntimeAsync(async runtime =>
            {
                var store = runtime.Host.Services.GetService<IOnlineFriendStore>();

                await runtime.Host.StartAsync();
                await SeedExpiredRequestAsync(store, TenantId, AppId, PlayerOne, PlayerTwo);
                var swept = await UntilAsync(async () => (await store.FindAsync(TenantId, AppId, PlayerOne, PlayerTwo))?.State == OnlineFriendshipState.Expired, 5000);
                Assert.True(swept);

                await runtime.Host.StopAsync();
                await SeedExpiredRequestAsync(store, TenantId, AppId, PlayerTwo, PlayerOne + 10);
                await Task.Delay(600);
                Assert.Equal(OnlineFriendshipState.Requested, (await store.FindAsync(TenantId, AppId, PlayerTwo, PlayerOne + 10)).State);
            }, null, options => options.HostOptions.SchedulerIntervalMilliseconds = 100);
        }

        /// <summary>
        /// 白名单仅含 online-foundation（闭包完整，合法装配）：社交能力整体缺席，
        /// 好友服务在共享面不可解析（GetOptionalService 返回 null / GetService fail-fast），不返回伪实例。
        /// </summary>
        [Fact]
        public async Task WhitelistExcludingSocial_FriendServiceAbsentFromSharedFace()
        {
            await OnlineCapabilityTestHarness.WithRuntimeAsync(runtime =>
            {
                Assert.Equal(new[] { "online-foundation" }, runtime.Report.EnabledIdsInOrder);
                Assert.Null(runtime.Host.Services.GetOptionalService<OnlineFriendService>());
                Assert.Throws<InvalidOperationException>(() => runtime.Host.Services.GetService<OnlineFriendService>());
                return Task.CompletedTask;
            }, null, options => options.Filter = new CapabilityAssemblyFilter(new[] { "online-foundation" }));
        }

        /// <summary>
        /// 玩家目录桩：按名称子串匹配（宿主扩展点 <see cref="IOnlinePlayerDirectory"/> 的测试实现）。
        /// </summary>
        private sealed class StubPlayerDirectory : IOnlinePlayerDirectory
        {
            /// <summary>目录条目。</summary>
            private readonly List<OnlinePlayerDirectoryEntry> _entries = new List<OnlinePlayerDirectoryEntry>();

            /// <summary>
            /// 登记一名玩家。
            /// </summary>
            /// <param name="playerId">玩家标识。</param>
            /// <param name="name">展示名。</param>
            public void Add(long playerId, string name)
            {
                _entries.Add(new OnlinePlayerDirectoryEntry { PlayerId = playerId, Name = name, ServerId = ServerId });
            }

            /// <summary>按名称子串检索。</summary>
            /// <param name="tenantId">租户标识。</param>
            /// <param name="appId">App 标识。</param>
            /// <param name="keyword">名称关键字。</param>
            /// <param name="limit">返回条数上限。</param>
            /// <param name="cancellationToken">取消令牌。</param>
            /// <returns>匹配的目录条目。</returns>
            public Task<IReadOnlyList<OnlinePlayerDirectoryEntry>> SearchByNameAsync(long tenantId, long appId, string keyword, int limit, CancellationToken cancellationToken = default)
            {
                var matched = new List<OnlinePlayerDirectoryEntry>();
                foreach (var entry in _entries)
                {
                    if (matched.Count >= limit)
                    {
                        break;
                    }

                    if (entry.Name.Contains(keyword, StringComparison.Ordinal))
                    {
                        matched.Add(entry);
                    }
                }

                return Task.FromResult<IReadOnlyList<OnlinePlayerDirectoryEntry>>(matched);
            }

            /// <summary>按标识批量解析。</summary>
            /// <param name="tenantId">租户标识。</param>
            /// <param name="appId">App 标识。</param>
            /// <param name="playerIds">玩家标识集合。</param>
            /// <param name="cancellationToken">取消令牌。</param>
            /// <returns>命中标识的目录条目。</returns>
            public Task<IReadOnlyList<OnlinePlayerDirectoryEntry>> FindAsync(long tenantId, long appId, IReadOnlyList<long> playerIds, CancellationToken cancellationToken = default)
            {
                var matched = new List<OnlinePlayerDirectoryEntry>();
                foreach (var entry in _entries)
                {
                    if (((IList<long>)playerIds).Contains(entry.PlayerId))
                    {
                        matched.Add(entry);
                    }
                }

                return Task.FromResult<IReadOnlyList<OnlinePlayerDirectoryEntry>>(matched);
            }
        }

        /// <summary>
        /// 在线事实探针桩：白名单内玩家在线（宿主 Override 胜出 Foundation 默认探针）。
        /// </summary>
        private sealed class StubPresenceProbe : IOnlineSocialPresenceProbe
        {
            /// <summary>在线玩家集合。</summary>
            private readonly HashSet<long> _online = new HashSet<long>();

            /// <summary>
            /// 标记玩家在线。
            /// </summary>
            /// <param name="playerId">玩家标识。</param>
            public void SetOnline(long playerId)
            {
                _online.Add(playerId);
            }

            /// <summary>白名单判定。</summary>
            /// <param name="tenantId">租户标识。</param>
            /// <param name="appId">App 标识。</param>
            /// <param name="playerId">玩家标识。</param>
            /// <param name="cancellationToken">取消令牌。</param>
            /// <returns>在白名单中返回 true。</returns>
            public Task<bool> IsOnlineAsync(long tenantId, long appId, long playerId, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(_online.Contains(playerId));
            }
        }

        /// <summary>
        /// 清扫输入持续抛异常的好友存储（其余面委托内层 InMemory）：
        /// 驱动好友过期清扫失败路径的 containment 断言（失败计数跨调度线程可见）。
        /// </summary>
        private sealed class ThrowingSweepFriendStore : IOnlineFriendStore
        {
            /// <summary>内层正常存储。</summary>
            private readonly InMemoryOnlineFriendStore _inner = new InMemoryOnlineFriendStore();

            /// <summary>清扫输入失败次数（调度线程写、用例线程读）。</summary>
            private int _sweepInputFailures;

            /// <summary>
            /// 获取清扫输入（ListByStateAsync）失败次数。
            /// </summary>
            public int SweepInputFailures
            {
                get { return Volatile.Read(ref _sweepInputFailures); }
            }

            /// <summary>委托内层。</summary>
            /// <param name="friendship">待创建的关系。</param>
            /// <param name="cancellationToken">取消令牌。</param>
            /// <returns>当前生效的关系记录。</returns>
            public Task<OnlineFriendship> SaveIfAbsentAsync(OnlineFriendship friendship, CancellationToken cancellationToken = default)
            {
                return _inner.SaveIfAbsentAsync(friendship, cancellationToken);
            }

            /// <summary>委托内层。</summary>
            /// <param name="tenantId">租户标识。</param>
            /// <param name="appId">App 标识。</param>
            /// <param name="leftPlayerId">一侧玩家标识。</param>
            /// <param name="rightPlayerId">另一侧玩家标识。</param>
            /// <param name="cancellationToken">取消令牌。</param>
            /// <returns>关系副本；不存在返回 null。</returns>
            public Task<OnlineFriendship> FindAsync(long tenantId, long appId, long leftPlayerId, long rightPlayerId, CancellationToken cancellationToken = default)
            {
                return _inner.FindAsync(tenantId, appId, leftPlayerId, rightPlayerId, cancellationToken);
            }

            /// <summary>委托内层。</summary>
            /// <param name="tenantId">租户标识。</param>
            /// <param name="appId">App 标识。</param>
            /// <param name="transition">状态迁移载荷。</param>
            /// <param name="cancellationToken">取消令牌。</param>
            /// <returns>更新后的关系副本；CAS 失败或记录不存在返回 null。</returns>
            public Task<OnlineFriendship> UpdateStateAsync(long tenantId, long appId, FriendshipStateTransition transition, CancellationToken cancellationToken = default)
            {
                return _inner.UpdateStateAsync(tenantId, appId, transition, cancellationToken);
            }

            /// <summary>委托内层。</summary>
            /// <param name="tenantId">租户标识。</param>
            /// <param name="appId">App 标识。</param>
            /// <param name="transition">状态迁移载荷。</param>
            /// <param name="establishedFriendLimit">已建立好友数上限（<= 0 表示不启用上限判定）。</param>
            /// <param name="cancellationToken">取消令牌。</param>
            /// <returns>迁移结果：成功携带更新后的副本；上限拒绝置 FriendLimitReached；CAS 失败两者皆空。</returns>
            public Task<OnlineFriendStateTransitionOutcome> UpdateStateWithFriendLimitAsync(long tenantId, long appId, FriendshipStateTransition transition, int establishedFriendLimit, CancellationToken cancellationToken = default)
            {
                return _inner.UpdateStateWithFriendLimitAsync(tenantId, appId, transition, establishedFriendLimit, cancellationToken);
            }

            /// <summary>委托内层。</summary>
            /// <param name="tenantId">租户标识。</param>
            /// <param name="appId">App 标识。</param>
            /// <param name="renewal">重新发起载荷。</param>
            /// <param name="cancellationToken">取消令牌。</param>
            /// <returns>更新后的关系副本；CAS 失败或记录不存在返回 null。</returns>
            public Task<OnlineFriendship> RenewRequestAsync(long tenantId, long appId, FriendshipRenewal renewal, CancellationToken cancellationToken = default)
            {
                return _inner.RenewRequestAsync(tenantId, appId, renewal, cancellationToken);
            }

            /// <summary>委托内层。</summary>
            /// <param name="tenantId">租户标识。</param>
            /// <param name="appId">App 标识。</param>
            /// <param name="playerId">玩家标识。</param>
            /// <param name="cancellationToken">取消令牌。</param>
            /// <returns>关系副本列表。</returns>
            public Task<IReadOnlyList<OnlineFriendship>> ListByPlayerAsync(long tenantId, long appId, long playerId, CancellationToken cancellationToken = default)
            {
                return _inner.ListByPlayerAsync(tenantId, appId, playerId, cancellationToken);
            }

            /// <summary>清扫输入面：计数后抛异常。</summary>
            /// <param name="tenantId">租户标识。</param>
            /// <param name="appId">App 标识。</param>
            /// <param name="state">关系状态。</param>
            /// <param name="cancellationToken">取消令牌。</param>
            /// <returns>不返回（恒抛）。</returns>
            public Task<IReadOnlyList<OnlineFriendship>> ListByStateAsync(long tenantId, long appId, OnlineFriendshipState state, CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref _sweepInputFailures);
                throw new InvalidOperationException("friend sweep input failure (test)");
            }
        }
    }
}
