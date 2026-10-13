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

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 社交能力包（C231 P3）验收测试：无参模块扫描可发现 / AddOnlineSocialRuntime 最小组合（Foundation 预置）/
    /// UseOnlineChatStore 替换缝生效 / gate 面注册回共享面。
    /// </summary>
    public class OnlineSocialCapabilityPackageTests
    {
        /// <summary>
        /// 地基占位模块（online-foundation 清单；Social 包闭包校验要求其在场——Foundation 包 hosting 扩展归 P4）。
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

        /// 构造最小组合根所需的预置（作用域 + 事件出口 + Foundation 会话面）。
        /// </summary>
        /// <returns>装配器（已预置平台与地基服务）。</returns>
        private static CapabilityHostBuilder MinimalRoot()
        {
            var builder = new CapabilityHostBuilder();
            builder.UseService(new OnlineScope(1, 1, 1));
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
        /// 构造合法选项。
        /// </summary>
        /// <returns>选项。</returns>
        private static CapabilityHostOptions ValidOptions()
        {
            return new CapabilityHostOptions { TenantId = 1, AppId = 1, ServerId = 1 };
        }

        /// <summary>
        /// 无参模块扫描可发现（P4 前置）：社交包程序集扫描产出 online-social 模块。
        /// </summary>
        [Fact]
        public void DiscoverModules_FindsSocialCapabilityModule()
        {
            var modules = CapabilityAssembly.DiscoverModules(new[] { typeof(OnlineSocialCapabilityModule).Assembly });

            var module = Assert.Single(modules);
            Assert.Equal("online-social", module.Manifest.Id);
        }

        /// <summary>
        /// AddOnlineSocialRuntime 最小组合：Foundation 预置齐备即可装配，产物与 gate 面经共享面可取用。
        /// </summary>
        [Fact]
        public void AddOnlineSocialRuntime_MinimalRoot_ExposesProductsAndGate()
        {
            var builder = MinimalRoot();
            builder.AddOnlineSocialRuntime();

            var host = builder.Build(ValidOptions());

            Assert.NotNull(host.Services.GetService<OnlineSocialDecisionService>());
            Assert.NotNull(host.Services.GetService<OnlinePunishmentService>());
            Assert.NotNull(host.Services.GetService<OnlineNotificationService>());
            Assert.NotNull(host.Services.GetService<OnlineChatAuditProjection>());
            Assert.NotNull(host.Services.GetService<IOnlineSocialGate>());
            Assert.Equal(new[] { "online-foundation", "online-social" }, host.Report.EnabledIdsInOrder);
        }

        /// <summary>
        /// UseOnlineChatStore 替换缝生效：模块取用组合根注入的存储实例。
        /// </summary>
        [Fact]
        public void UseOnlineChatStore_PreSeedWinsOverInMemoryDefault()
        {
            var replacement = new InMemoryOnlineChatStore();
            var builder = MinimalRoot();
            builder.UseOnlineChatStore(replacement);
            builder.AddOnlineSocialRuntime();

            var host = builder.Build(ValidOptions());

            Assert.Same(replacement, host.Services.GetService<IOnlineChatStore>());
        }

        /// <summary>
        /// 包级 options 经回调承载并进共享面（能力族 AddXxx(options => ...) 惯例锚点）。
        /// </summary>
        [Fact]
        public void AddOnlineSocialRuntime_OptionsCarriedThroughCallback()
        {
            var builder = MinimalRoot();
            var seen = false;
            builder.AddOnlineSocialRuntime(options => seen = true);

            var host = builder.Build(ValidOptions());

            Assert.True(seen);
            Assert.NotNull(host.Services.GetService<OnlineSocialOptions>());
        }
    }
}
