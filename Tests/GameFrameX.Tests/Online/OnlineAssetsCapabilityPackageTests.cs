using System;
using System.Linq;
using System.Reflection;
using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Hosting.Core;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Assets;
using GameFrameX.Capability.Online.Abstractions.Idempotency;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 资产能力包验收测试：无参模块扫描可发现 / AddOnlineAssetsRuntime 最小组合 /
    /// UseOnlineAssetStore 替换缝生效 / 与壳全能力形态的装配等价锚点。
    /// </summary>
    public class OnlineAssetsCapabilityPackageTests
    {
        /// <summary>
        /// 替换缝替换实例（独立 InMemory 实例——断言模块取用的是组合根注入的实例而非包内默认构造）。
        /// </summary>
        private sealed class ProbeAssetStore : GameFrameX.Capability.Online.Assets.IOnlineAssetStore
        {
            /// <inheritdoc />
            public Task<GameFrameX.Capability.Online.Assets.OnlineWalletAccount> FindWalletAsync(long tenantId, long appId, long playerId, string currencyId, CancellationToken cancellationToken = default)
            {
                return Task.FromResult<GameFrameX.Capability.Online.Assets.OnlineWalletAccount>(null);
            }

            /// <inheritdoc />
            public Task<GameFrameX.Capability.Online.Assets.OnlineInventoryStack> FindInventoryAsync(long tenantId, long appId, long playerId, string itemId, CancellationToken cancellationToken = default)
            {
                return Task.FromResult<GameFrameX.Capability.Online.Assets.OnlineInventoryStack>(null);
            }

            /// <inheritdoc />
            public Task<IReadOnlyList<GameFrameX.Capability.Online.Assets.OnlineWalletAccount>> ListWalletAccountsAsync(long tenantId, long appId, long playerId, CancellationToken cancellationToken = default)
            {
                return Task.FromResult<IReadOnlyList<GameFrameX.Capability.Online.Assets.OnlineWalletAccount>>(Array.Empty<GameFrameX.Capability.Online.Assets.OnlineWalletAccount>());
            }

            /// <inheritdoc />
            public Task<IReadOnlyList<GameFrameX.Capability.Online.Assets.OnlineInventoryStack>> ListInventoryStacksAsync(long tenantId, long appId, long playerId, CancellationToken cancellationToken = default)
            {
                return Task.FromResult<IReadOnlyList<GameFrameX.Capability.Online.Assets.OnlineInventoryStack>>(Array.Empty<GameFrameX.Capability.Online.Assets.OnlineInventoryStack>());
            }

            /// <inheritdoc />
            public Task<GameFrameX.Capability.Online.Assets.OnlineAssetApplyResult> ApplyAsync(GameFrameX.Capability.Online.Assets.OnlineAssetChangeBatch batch, CancellationToken cancellationToken = default)
            {
                var entries = new List<GameFrameX.Capability.Online.Assets.OnlineLedgerEntry>();
                return Task.FromResult(new GameFrameX.Capability.Online.Assets.OnlineAssetApplyResult(true, GameFrameX.Capability.Online.Abstractions.Contracts.OnlineErrorCode.None, string.Empty, entries));
            }

            /// <inheritdoc />
            public Task<IReadOnlyList<GameFrameX.Capability.Online.Assets.OnlineLedgerEntry>> ListLedgerEntriesAsync(GameFrameX.Capability.Online.Assets.OnlineLedgerPageQuery query, CancellationToken cancellationToken = default)
            {
                return Task.FromResult<IReadOnlyList<GameFrameX.Capability.Online.Assets.OnlineLedgerEntry>>(Array.Empty<GameFrameX.Capability.Online.Assets.OnlineLedgerEntry>());
            }

            /// <inheritdoc />
            public Task<IReadOnlyList<GameFrameX.Capability.Online.Assets.OnlineLedgerEntry>> FindLedgerEntriesByTransactionIdAsync(string transactionId, CancellationToken cancellationToken = default)
            {
                return Task.FromResult<IReadOnlyList<GameFrameX.Capability.Online.Assets.OnlineLedgerEntry>>(Array.Empty<GameFrameX.Capability.Online.Assets.OnlineLedgerEntry>());
            }

            /// <inheritdoc />
            public Task<IReadOnlyDictionary<string, long>> SumLedgerDeltasByAssetAsync(long tenantId, long appId, long playerId, CancellationToken cancellationToken = default)
            {
                return Task.FromResult<IReadOnlyDictionary<string, long>>(new Dictionary<string, long>());
            }

            /// <inheritdoc />
            public Task<IReadOnlyList<long>> ListPlayerIdsAsync(long tenantId, long appId, CancellationToken cancellationToken = default)
            {
                return Task.FromResult<IReadOnlyList<long>>(Array.Empty<long>());
            }
        }

        /// <summary>
        /// 构造最小组合根所需的脊柱预置（作用域 + 事件出口 + 幂等）。
        /// </summary>
        /// <returns>装配器（已预置脊柱服务）。</returns>
        private static CapabilityHostBuilder MinimalRoot()
        {
            var builder = new CapabilityHostBuilder();
            builder.UseService(new GameFrameX.Capability.Online.Abstractions.Scope.OnlineScope(1, 1, 1));
            builder.UseService<GameFrameX.Capability.Online.Abstractions.Events.IOnlineEventPublisher>(new GameFrameX.Capability.Online.Abstractions.Events.OnlineEventPublisher(new GameFrameX.Foundation.Idempotency.InMemoryEventPublisher()));
            builder.UseService(new OnlineIdempotencyService(new GameFrameX.Foundation.Idempotency.IdempotencyCoordinator(
                new GameFrameX.Foundation.Idempotency.InMemoryIdempotencyStore(),
                GameFrameX.Foundation.Idempotency.SystemClock.Instance,
                new GameFrameX.Foundation.Idempotency.IdempotencyOptions())));
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
        /// 无参模块扫描可发现：资产包程序集扫描产出 online-assets 模块。
        /// </summary>
        [Fact]
        public void DiscoverModules_FindsAssetsCapabilityModule()
        {
            var modules = CapabilityAssembly.DiscoverModules(new[] { typeof(GameFrameX.Capability.Online.Assets.OnlineAssetsCapabilityModule).Assembly });

            var module = Assert.Single(modules);
            Assert.Equal("online-assets", module.Manifest.Id);
        }

        /// <summary>
        /// AddOnlineAssetsRuntime 最小组合：仅脊柱预置即可装配，产物经共享面可取用（grant / query / store 接口）。
        /// </summary>
        [Fact]
        public void AddOnlineAssetsRuntime_MinimalRoot_ExposesProductsThroughSharedFace()
        {
            var builder = MinimalRoot();
            builder.AddOnlineAssetsRuntime(options => { });

            var host = builder.Build(ValidOptions());

            Assert.NotNull(host.Services.GetService<GameFrameX.Capability.Online.Assets.OnlineGrantService>());
            Assert.NotNull(host.Services.GetService<GameFrameX.Capability.Online.Assets.OnlineAssetQueryService>());
            Assert.NotNull(host.Services.GetService<GameFrameX.Capability.Online.Assets.IOnlineAssetStore>());
            Assert.NotNull(host.Services.GetService<GameFrameX.Capability.Online.Assets.IOnlineAssetTransactionStore>());
            Assert.Equal("online-assets", Assert.Single(host.Report.EnabledIdsInOrder));
        }

        /// <summary>
        /// UseOnlineAssetStore 替换缝生效：模块取用组合根注入的存储实例（生产持久化替换位）。
        /// </summary>
        [Fact]
        public void UseOnlineAssetStore_PreSeedWinsOverInMemoryDefault()
        {
            var probe = new ProbeAssetStore();
            var builder = MinimalRoot();
            builder.UseOnlineAssetStore(probe);
            builder.AddOnlineAssetsRuntime();

            var host = builder.Build(ValidOptions());

            Assert.Same(probe, host.Services.GetService<GameFrameX.Capability.Online.Assets.IOnlineAssetStore>());
        }

        /// <summary>
        /// 包级 options 经回调承载并进共享面（能力族 AddXxx(options => ...) 惯例锚点）。
        /// </summary>
        [Fact]
        public void AddOnlineAssetsRuntime_OptionsCarriedThroughCallback()
        {
            var builder = MinimalRoot();
            var seen = false;
            builder.AddOnlineAssetsRuntime(options => seen = true);

            var host = builder.Build(ValidOptions());

            Assert.True(seen);
            Assert.NotNull(host.Services.GetService<GameFrameX.Capability.Online.Assets.OnlineAssetsOptions>());
        }
    }
}
