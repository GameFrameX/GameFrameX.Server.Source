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
using GameFrameX.Capability.Online.Abstractions.Events;
using GameFrameX.Capability.Online.Abstractions.Idempotency;
using GameFrameX.Capability.Online.Abstractions.Scope;
using GameFrameX.Capability.Online.Foundation.Session;
using GameFrameX.Capability.Online.Social;
using GameFrameX.Launcher.Online;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 好友最小消费链装配测试：OnlineFriendService 经模块自装配注册回共享面、
    /// store 替换缝与 directory / presence 通用缝生效、好友请求过期清扫滴答 job 与查询共用同一状态来源、
    /// 真实组合根（AddOnlineCapabilities）可解析、宿主启停驱动清扫、清扫失败 containment 与错误配置行为。
    /// </summary>
    public class OnlineFriendConsumptionChainAssemblyTests
    {
        /// <summary>测试用租户标识（与组合根授权作用域一致）。</summary>
        private const long TenantId = 1;

        /// <summary>测试用应用标识。</summary>
        private const long AppId = 1;

        /// <summary>测试用区服标识（与组合根授权作用域一致）。</summary>
        private const long ServerId = 1001;

        /// <summary>测试用玩家一标识（请求发起方）。</summary>
        private const long PlayerOne = 1001;

        /// <summary>测试用玩家二标识（被请求方）。</summary>
        private const long PlayerTwo = 1002;

        /// <summary>
        /// 最小组合根（AddOnlineSocialRuntime 装配路径所需的预置：作用域 + 事件出口 + 幂等 + Foundation 会话面）。
        /// </summary>
        /// <returns>装配器。</returns>
        private static CapabilityHostBuilder MinimalRoot()
        {
            var builder = new CapabilityHostBuilder();
            builder.UseService(new OnlineScope(TenantId, AppId, ServerId));
            var eventPublisher = new OnlineEventPublisher(new GameFrameX.Foundation.Idempotency.InMemoryEventPublisher());
            builder.UseService<IOnlineEventPublisher>(eventPublisher);
            builder.UseService(new OnlineIdempotencyService(new GameFrameX.Foundation.Idempotency.IdempotencyCoordinator(
                new GameFrameX.Foundation.Idempotency.InMemoryIdempotencyStore(),
                GameFrameX.Foundation.Idempotency.SystemClock.Instance,
                new GameFrameX.Foundation.Idempotency.IdempotencyOptions())));
            var sessionStore = new InMemoryOnlineSessionStore();
            builder.UseService<IOnlineSessionStore>(sessionStore);
            builder.UseService(new OnlineSessionTokenService(sessionStore, eventPublisher));
            builder.AddModule(new FoundationStubModule());
            return builder;
        }

        /// <summary>
        /// 构造合法宿主选项（作用域与测试常量一致）。
        /// </summary>
        /// <param name="schedulerIntervalMilliseconds">调度滴答间隔。</param>
        /// <returns>宿主选项。</returns>
        private static CapabilityHostOptions ValidOptions(int schedulerIntervalMilliseconds = 1000)
        {
            return new CapabilityHostOptions { TenantId = TenantId, AppId = AppId, ServerId = ServerId, SchedulerIntervalMilliseconds = schedulerIntervalMilliseconds };
        }

        /// <summary>
        /// 构造真实组合根编排回调（业务闸单开；间隔与过滤器由用例覆写）。
        /// </summary>
        /// <param name="configure">用例追加的编排动作。</param>
        /// <returns>组合回调。</returns>
        private static Action<OnlineCapabilityCompositionOptions> Compose(Action<OnlineCapabilityCompositionOptions> configure = null)
        {
            return options =>
            {
                options.TenantId = TenantId;
                options.AppId = AppId;
                options.ServerId = ServerId;
                options.EnableRuntime = true;
                configure?.Invoke(options);
            };
        }

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
        /// 模块自装配把 OnlineFriendService 注册回共享面，且与共享面的好友存储同源
        ///（服务写入的关系经共享面存储可见 = 查询与清扫同一状态来源）。
        /// </summary>
        [Fact]
        public async Task MinimalRoot_RegistersFriendServiceIntoSharedFaceSharingStore()
        {
            var builder = MinimalRoot();
            builder.AddOnlineSocialRuntime();
            var host = builder.Build(ValidOptions());

            var service = host.Services.GetService<OnlineFriendService>();
            var store = host.Services.GetService<IOnlineFriendStore>();
            Assert.NotNull(service);
            Assert.NotNull(store);

            var request = await service.RequestAsync(CreatePlayerScope(PlayerOne), PlayerTwo, "corr-1");
            Assert.True(request.IsSuccess);
            Assert.NotNull(await store.FindAsync(TenantId, AppId, PlayerOne, PlayerTwo));
        }

        /// <summary>
        /// UseOnlineFriendStore 替换缝：组合根预置实例被模块采用（共享面同实例），
        /// 过期清扫滴答 job 与服务使用同一条预置存储（单轮 TickAsync 即终结超期请求）。
        /// </summary>
        [Fact]
        public async Task UseOnlineFriendStore_PreSeed_DrivesServiceAndSweepOnSameStateSource()
        {
            var preSeeded = new InMemoryOnlineFriendStore();
            await SeedExpiredRequestAsync(preSeeded, TenantId, AppId, PlayerOne, PlayerTwo);

            var builder = MinimalRoot();
            builder.UseOnlineFriendStore(preSeeded);
            builder.AddOnlineSocialRuntime();
            var host = builder.Build(ValidOptions());

            Assert.Same(preSeeded, host.Services.GetService<IOnlineFriendStore>());

            var service = host.Services.GetService<OnlineFriendService>();
            var request = await service.RequestAsync(CreatePlayerScope(PlayerTwo), PlayerOne + 10);
            Assert.True(request.IsSuccess);

            await host.Scheduler.TickAsync();

            var expired = await preSeeded.FindAsync(TenantId, AppId, PlayerOne, PlayerTwo);
            Assert.Equal(OnlineFriendshipState.Expired, expired.State);
        }

        /// <summary>
        /// 未预置玩家目录（当前全仓无实现）：好友搜索降级为空结果——这是装配披露的边界
        ///（空结果 ≠ 无匹配玩家，搜索能力缺席），不是可用的搜索功能。
        /// </summary>
        [Fact]
        public async Task MinimalRoot_WithoutDirectory_SearchDegradesToEmpty()
        {
            var builder = MinimalRoot();
            builder.AddOnlineSocialRuntime();
            var host = builder.Build(ValidOptions());

            var service = host.Services.GetService<OnlineFriendService>();
            var outcome = await service.SearchAsync(CreatePlayerScope(PlayerOne), "alice");

            Assert.True(outcome.IsSuccess);
            Assert.Empty(outcome.Data);
        }

        /// <summary>
        /// 玩家目录与在线探针经既有 UseService 通用缝预置即被模块采用：
        /// 搜索返回目录条目、好友列表带展示名与在线事实——证明缺席是可注入的披露态而非隐藏失败。
        /// </summary>
        [Fact]
        public async Task PreSeededDirectoryAndPresenceProbe_FeedFriendService()
        {
            var directory = new StubPlayerDirectory();
            directory.Add(PlayerTwo, "alice");
            var probe = new StubPresenceProbe();
            probe.SetOnline(PlayerTwo);

            var builder = MinimalRoot();
            builder.UseService<IOnlinePlayerDirectory>(directory);
            builder.UseService<IOnlineSocialPresenceProbe>(probe);
            builder.AddOnlineSocialRuntime();
            var host = builder.Build(ValidOptions());

            var service = host.Services.GetService<OnlineFriendService>();
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

        /// <summary>
        /// 清扫存储输入持续抛异常：当轮滴答失败被调度壳吞掉、循环继续，
        /// 宿主停机干净、服务查询面仍可用——异常 containment 的行为级可观测契约。
        /// </summary>
        [Fact]
        public async Task ThrowingFriendStore_SweepFailureContained_HostStaysRunnable()
        {
            var throwing = new ThrowingSweepFriendStore();
            var builder = MinimalRoot();
            builder.UseOnlineFriendStore(throwing);
            builder.AddOnlineSocialRuntime();
            var host = builder.Build(ValidOptions(100));

            await host.StartAsync();
            var observedThrow = await UntilAsync(() => Task.FromResult(throwing.SweepInputFailures > 0), 5000);
            await host.StopAsync();

            Assert.True(observedThrow);
            Assert.True(throwing.SweepInputFailures >= 1);

            var service = host.Services.GetService<OnlineFriendService>();
            var friends = await service.ListFriendsAsync(CreatePlayerScope(PlayerOne));
            Assert.True(friends.IsSuccess);
            Assert.Empty(friends.Data);
        }

        /// <summary>
        /// 真实组合根（AddOnlineCapabilities 业务闸）：Runtime 共享面可解析 OnlineFriendService，
        /// 服务查询与过期清扫共用同一存储实例（服务写入 → 共享面存储可见 → 单轮 TickAsync 终结超期请求）。
        /// </summary>
        [Fact]
        public async Task RealCompositionRoot_ExposesFriendServiceWithSweepOnSameStore()
        {
            var composition = OnlineCapabilityComposition.AddOnlineCapabilities(Compose());

            var service = composition.Runtime.Services.GetService<OnlineFriendService>();
            var store = composition.Runtime.Services.GetService<IOnlineFriendStore>();
            Assert.NotNull(service);
            Assert.NotNull(store);

            var request = await service.RequestAsync(CreatePlayerScope(PlayerOne), PlayerTwo, "corr-2");
            Assert.True(request.IsSuccess);
            Assert.NotNull(await store.FindAsync(TenantId, AppId, PlayerOne, PlayerTwo));

            await SeedExpiredRequestAsync(store, TenantId, AppId, PlayerTwo, PlayerOne + 10);
            await composition.Runtime.Scheduler.TickAsync();

            var expired = await store.FindAsync(TenantId, AppId, PlayerTwo, PlayerOne + 10);
            Assert.Equal(OnlineFriendshipState.Expired, expired.State);
            var live = await store.FindAsync(TenantId, AppId, PlayerOne, PlayerTwo);
            Assert.Equal(OnlineFriendshipState.Requested, live.State);
        }

        /// <summary>
        /// 清扫的作用域隔离：滴答 job 经组合根授权作用域锚定租户 / App——
        /// 其余租户或 App 的超期请求不被本宿主触碰。
        /// </summary>
        [Fact]
        public async Task RealCompositionRoot_SweepScopedToAuthorizedTenantAndApp()
        {
            var composition = OnlineCapabilityComposition.AddOnlineCapabilities(Compose());
            var store = composition.Runtime.Services.GetService<IOnlineFriendStore>();
            await SeedExpiredRequestAsync(store, TenantId, AppId, PlayerOne, PlayerTwo);
            await SeedExpiredRequestAsync(store, TenantId + 1, AppId, PlayerOne, PlayerTwo);
            await SeedExpiredRequestAsync(store, TenantId, AppId + 1, PlayerOne, PlayerTwo);

            await composition.Runtime.Scheduler.TickAsync();

            Assert.Equal(OnlineFriendshipState.Expired, (await store.FindAsync(TenantId, AppId, PlayerOne, PlayerTwo)).State);
            Assert.Equal(OnlineFriendshipState.Requested, (await store.FindAsync(TenantId + 1, AppId, PlayerOne, PlayerTwo)).State);
            Assert.Equal(OnlineFriendshipState.Requested, (await store.FindAsync(TenantId, AppId + 1, PlayerOne, PlayerTwo)).State);
        }

        /// <summary>
        /// 宿主启停生命周期：StartAsync 后调度循环自动驱动清扫（无需手动 TickAsync）；
        /// StopAsync 后循环退出，新落的超期请求不再被终结。
        /// </summary>
        [Fact]
        public async Task RealCompositionRoot_HostStartDrivesSweepAndStopHaltsIt()
        {
            var composition = OnlineCapabilityComposition.AddOnlineCapabilities(Compose(options => options.SchedulerIntervalMilliseconds = 100));
            var store = composition.Runtime.Services.GetService<IOnlineFriendStore>();
            var host = composition.Runtime;

            await host.StartAsync();
            await SeedExpiredRequestAsync(store, TenantId, AppId, PlayerOne, PlayerTwo);
            var swept = await UntilAsync(async () => (await store.FindAsync(TenantId, AppId, PlayerOne, PlayerTwo))?.State == OnlineFriendshipState.Expired, 5000);
            Assert.True(swept);

            await host.StopAsync();
            await SeedExpiredRequestAsync(store, TenantId, AppId, PlayerTwo, PlayerOne + 10);
            await Task.Delay(600);
            Assert.Equal(OnlineFriendshipState.Requested, (await store.FindAsync(TenantId, AppId, PlayerTwo, PlayerOne + 10)).State);
        }

        /// <summary>
        /// 错误配置行为：白名单未含 online-social（闭包完整，合法装配）时社交能力整体缺席，
        /// 好友服务在共享面不可解析（GetService fail-fast / TryGetService miss），不返回伪实例。
        /// </summary>
        [Fact]
        public void WhitelistExcludingSocial_FriendServiceAbsentFromSharedFace()
        {
            var composition = OnlineCapabilityComposition.AddOnlineCapabilities(Compose(options => options.Capabilities = new[] { "online-foundation" }));

            Assert.False(composition.Runtime.Services.TryGetService(out OnlineFriendService _));
            Assert.Throws<InvalidOperationException>(() => composition.Runtime.Services.GetService<OnlineFriendService>());
        }

        /// <summary>
        /// 地基占位模块（online-foundation 清单；Social 包闭包校验要求其在场）。
        /// </summary>
        private sealed class FoundationStubModule : ICapabilityModule
        {
            /// <inheritdoc />
            public CapabilityManifest Manifest { get; } = new("online-foundation", "1.0.0", Array.Empty<string>());

            /// <inheritdoc />
            public void Register(CapabilityRegistrationContext context)
            {
            }
        }

        /// <summary>
        /// 玩家目录桩：按名称子串匹配。
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
        /// 在线事实探针桩：白名单内玩家在线。
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
        /// 驱动好友过期清扫失败路径的 containment 断言。
        /// </summary>
        private sealed class ThrowingSweepFriendStore : IOnlineFriendStore
        {
            /// <summary>内层正常存储。</summary>
            private readonly InMemoryOnlineFriendStore _inner = new InMemoryOnlineFriendStore();

            /// <summary>
            /// 获取清扫输入（ListByStateAsync）失败次数。
            /// </summary>
            public int SweepInputFailures { get; private set; }

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
                SweepInputFailures++;
                throw new InvalidOperationException("friend sweep input failure (test)");
            }
        }
    }
}
