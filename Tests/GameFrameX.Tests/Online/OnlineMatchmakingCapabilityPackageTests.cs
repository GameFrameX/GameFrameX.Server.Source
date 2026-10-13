using System;
using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Hosting.Core;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Events;
using GameFrameX.Capability.Online.Abstractions.Idempotency;
using GameFrameX.Capability.Online.Abstractions.Scope;
using GameFrameX.Capability.Online.Foundation.Session;
using GameFrameX.Capability.Online.Social;
using Xunit;

using GameFrameX.Capability.Online.Matchmaking;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 匹配能力包（C231 P3，含 Party 域）验收测试：无参模块扫描可发现 / AddOnlineMatchmakingRuntime 最小组合
    /// （social gate 面经共享面解析）/ UseOnlineMatchTicketStore 替换缝生效 / Party store 产物注册回共享面。
    /// </summary>
    public class OnlineMatchmakingCapabilityPackageTests
    {
        /// <summary>
        /// 社交占位模块（online-social 清单；Matchmaking 包闭包校验要求其在场——真实 gate 面经共享面预置）。
        /// </summary>
        private sealed class SocialStubModule : ICapabilityModule
        {
            /// <inheritdoc />
            public CapabilityManifest Manifest { get; } = new("online-social", "1.0.0", Array.Empty<string>());

            /// <inheritdoc />
            public void Register(CapabilityRegistrationContext context)
            {
            }
        }

        /// <summary>
        /// 社交裁决 gate 面探针（实现 IOnlineSocialGate 的最小实例——匹配模块的跨域硬依赖）。
        /// </summary>
        private sealed class StubSocialGate : IOnlineSocialGate
        {
            /// <inheritdoc />
            public Task<OnlineSocialDecision> EvaluateAsync(long tenantId, long appId, long fromPlayerId, long toPlayerId, OnlineSocialInteractionPurpose purpose, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(OnlineSocialDecision.Allow());
            }
        }

        /// <summary>
        /// 构造最小组合根所需的预置（作用域 + 事件出口 + 社交 gate 面）。
        /// </summary>
        /// <returns>装配器（已预置跨域硬依赖）。</returns>
        private static CapabilityHostBuilder MinimalRoot()
        {
            var builder = new CapabilityHostBuilder();
            builder.UseService(new OnlineScope(1, 1, 1));
            builder.UseService<IOnlineEventPublisher>(new OnlineEventPublisher(new GameFrameX.Foundation.Idempotency.InMemoryEventPublisher()));
            builder.UseService<IOnlineSocialGate>(new StubSocialGate());
            builder.AddModule(new SocialStubModule());
            return builder;
        }

        /// <summary>
        /// 构造合法选项。
        /// </summary>
        /// <returns>选项。</returns>
        private static CapabilityHostOptions ValidOptions()
        {
            return new CapabilityHostOptions { TenantId = 1, AppId = 1, ServerId = 1 };
        }

        /// <summary>
        /// 无参模块扫描可发现（P4 前置）：匹配包程序集扫描产出 online-matchmaking 模块。
        /// </summary>
        [Fact]
        public void DiscoverModules_FindsMatchmakingCapabilityModule()
        {
            var modules = CapabilityAssembly.DiscoverModules(new[] { typeof(OnlineMatchmakingCapabilityModule).Assembly });

            var module = Assert.Single(modules);
            Assert.Equal("online-matchmaking", module.Manifest.Id);
        }

        /// <summary>
        /// AddOnlineMatchmakingRuntime 最小组合：gate 面预置齐备即可装配，产物（票据 / 撮合 / 观察器 / Party store）经共享面可取用。
        /// </summary>
        [Fact]
        public void AddOnlineMatchmakingRuntime_MinimalRoot_ExposesProductsIncludingPartyStore()
        {
            var builder = MinimalRoot();
            builder.AddOnlineMatchmakingRuntime();

            var host = builder.Build(ValidOptions());

            Assert.NotNull(host.Services.GetService<OnlineMatchTicketService>());
            Assert.NotNull(host.Services.GetService<OnlineMatchmakerCoordinator>());
            Assert.NotNull(host.Services.GetService<OnlineMatchQueueObserver>());
            Assert.NotNull(host.Services.GetService<IOnlineMatchTicketStore>());
            Assert.NotNull(host.Services.GetService<GameFrameX.Capability.Online.Matchmaking.Party.IOnlinePartyStore>());
            Assert.Equal(new[] { "online-social", "online-matchmaking" }, host.Report.EnabledIdsInOrder);
        }

        /// <summary>
        /// UseOnlineMatchTicketStore 替换缝生效：模块取用组合根注入的存储实例。
        /// </summary>
        [Fact]
        public void UseOnlineMatchTicketStore_PreSeedWinsOverInMemoryDefault()
        {
            var replacement = new InMemoryOnlineMatchTicketStore();
            var builder = MinimalRoot();
            builder.UseOnlineMatchTicketStore(replacement);
            builder.AddOnlineMatchmakingRuntime();

            var host = builder.Build(ValidOptions());

            Assert.Same(replacement, host.Services.GetService<IOnlineMatchTicketStore>());
        }

        /// <summary>
        /// 包级 options 经回调承载并进共享面（能力族 AddXxx(options => ...) 惯例锚点）。
        /// </summary>
        [Fact]
        public void AddOnlineMatchmakingRuntime_OptionsCarriedThroughCallback()
        {
            var builder = MinimalRoot();
            var seen = false;
            builder.AddOnlineMatchmakingRuntime(options => seen = true);

            var host = builder.Build(ValidOptions());

            Assert.True(seen);
            Assert.NotNull(host.Services.GetService<OnlineMatchmakingOptions>());
        }
    }
}
