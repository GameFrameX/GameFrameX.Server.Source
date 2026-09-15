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
//   Any legal disputes and liabilities arising from secondary development based on this project
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

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Foundation.Idempotency;
using GameFrameX.Online.Assets;
using GameFrameX.Online.Contracts;
using GameFrameX.Online.Events;
using GameFrameX.Online.Idempotency;
using GameFrameX.Online.Scope;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 资产对账服务测试（vault:C4 VC-3.13：账本累加 == 快照；漂移注入可发现、差异可定位）。
    /// </summary>
    public class OnlineAssetReconciliationServiceTests
    {
        /// <summary>
        /// 步进时钟。
        /// </summary>
        private sealed class StepClock : IClock
        {
            private long _nowMilliseconds = 1760000000000;

            public long UtcNowTime
            {
                get
                {
                    _nowMilliseconds += 10;
                    return _nowMilliseconds;
                }
            }
        }

        /// <summary>
        /// 空事件出口。
        /// </summary>
        private sealed class NullPublisher : IOnlineEventPublisher
        {
            public Task PublishAsync(OnlineEvent onlineEvent, CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }
        }

        /// <summary>
        /// 快照漂移存储（对指定玩家的货币快照加偏移——模拟快照侧数据损坏）。
        /// </summary>
        private sealed class DriftingAssetStore : IOnlineAssetStore
        {
            /// <summary>被包装存储。</summary>
            private readonly IOnlineAssetStore _inner;

            /// <summary>漂移目标玩家。</summary>
            private readonly long _playerId;

            /// <summary>漂移偏移量。</summary>
            private readonly long _balanceOffset;

            public DriftingAssetStore(IOnlineAssetStore inner, long playerId, long balanceOffset)
            {
                _inner = inner;
                _playerId = playerId;
                _balanceOffset = balanceOffset;
            }

            public Task<IReadOnlyList<OnlineWalletAccount>> ListWalletAccountsAsync(long tenantId, long appId, long playerId, CancellationToken cancellationToken = default)
            {
                if (playerId != _playerId)
                {
                    return _inner.ListWalletAccountsAsync(tenantId, appId, playerId, cancellationToken);
                }

                var drifted = _inner.ListWalletAccountsAsync(tenantId, appId, playerId, cancellationToken).GetAwaiter().GetResult();
                var tampered = new List<OnlineWalletAccount>();
                foreach (var account in drifted)
                {
                    tampered.Add(new OnlineWalletAccount { TenantId = account.TenantId, AppId = account.AppId, PlayerId = account.PlayerId, CurrencyId = account.CurrencyId, Balance = account.Balance + _balanceOffset, Version = account.Version, UpdatedTime = account.UpdatedTime });
                }

                return Task.FromResult<IReadOnlyList<OnlineWalletAccount>>(tampered);
            }

            public Task<OnlineAssetApplyResult> ApplyAsync(OnlineAssetChangeBatch batch, CancellationToken cancellationToken = default)
            {
                return _inner.ApplyAsync(batch, cancellationToken);
            }

            public Task<OnlineWalletAccount> FindWalletAsync(long tenantId, long appId, long playerId, string currencyId, CancellationToken cancellationToken = default)
            {
                return _inner.FindWalletAsync(tenantId, appId, playerId, currencyId, cancellationToken);
            }

            public Task<OnlineInventoryStack> FindInventoryAsync(long tenantId, long appId, long playerId, string itemId, CancellationToken cancellationToken = default)
            {
                return _inner.FindInventoryAsync(tenantId, appId, playerId, itemId, cancellationToken);
            }

            public Task<IReadOnlyList<OnlineInventoryStack>> ListInventoryStacksAsync(long tenantId, long appId, long playerId, CancellationToken cancellationToken = default)
            {
                return _inner.ListInventoryStacksAsync(tenantId, appId, playerId, cancellationToken);
            }

            public Task<IReadOnlyList<OnlineLedgerEntry>> ListLedgerEntriesAsync(long tenantId, long appId, long playerId, long afterSequenceNumber, int maxCount, CancellationToken cancellationToken = default)
            {
                return _inner.ListLedgerEntriesAsync(tenantId, appId, playerId, afterSequenceNumber, maxCount, cancellationToken);
            }

            public Task<IReadOnlyList<OnlineLedgerEntry>> FindLedgerEntriesByTransactionIdAsync(string transactionId, CancellationToken cancellationToken = default)
            {
                return _inner.FindLedgerEntriesByTransactionIdAsync(transactionId, cancellationToken);
            }

            public Task<IReadOnlyDictionary<string, long>> SumLedgerDeltasByAssetAsync(long tenantId, long appId, long playerId, CancellationToken cancellationToken = default)
            {
                return _inner.SumLedgerDeltasByAssetAsync(tenantId, appId, playerId, cancellationToken);
            }

            public Task<IReadOnlyList<long>> ListPlayerIdsAsync(long tenantId, long appId, CancellationToken cancellationToken = default)
            {
                return _inner.ListPlayerIdsAsync(tenantId, appId, cancellationToken);
            }
        }

        /// <summary>
        /// 测试基座。
        /// </summary>
        private sealed class Harness
        {
            public InMemoryOnlineAssetStore AssetStore
            {
                get;
            } = new InMemoryOnlineAssetStore();

            public OnlineGrantService CreateGrantService()
            {
                var options = new IdempotencyOptions
                {
                    ConcurrentWaitTimeoutMilliseconds = 1,
                };
                var coordinator = new IdempotencyCoordinator(new InMemoryIdempotencyStore(), new StepClock(), options);
                return new OnlineGrantService(AssetStore, new InMemoryOnlineAssetTransactionStore(), new OnlineIdempotencyService(coordinator, null), new NullPublisher(), null);
            }

            public OnlineAssetReconciliationService CreateReconciliationService(IOnlineAssetStore assetStore = null)
            {
                return new OnlineAssetReconciliationService(assetStore ?? AssetStore);
            }
        }

        /// <summary>
        /// 构造统一入口请求。
        /// </summary>
        private static OnlineGrantRequest BuildRequest(long playerId, string key, long amount)
        {
            var scope = new OnlineScope(1, 10, 100, playerId);
            return new OnlineGrantRequest(scope, OnlineAssetChangeSource.MatchReward, OnlineGrantOperation.Grant, "赛季结算", "bo-" + key, null, new[] { new OnlineAssetChangeLine(OnlineAssetKind.Currency, "gold", amount) }, key);
        }

        /// <summary>
        /// 验证正常流转后对账差异为 0（账本累加 == 快照）。
        /// </summary>
        [Fact]
        public async Task ReconcilePlayerAsync_AfterNormalFlow_IsConsistent()
        {
            // Arrange
            var harness = new Harness();
            var grantService = harness.CreateGrantService();
            await grantService.ExecuteAsync(BuildRequest(10001, "key-1", 100));
            await grantService.ExecuteAsync(BuildRequest(10001, "key-2", -30));
            var reconciliation = harness.CreateReconciliationService();

            // Act
            var report = await reconciliation.ReconcilePlayerAsync(new OnlineScope(1, 10, 100, 10001));

            // Assert
            Assert.Equal(1, report.PlayerCount);
            Assert.True(report.IsConsistent);
            Assert.Empty(report.Differences);
        }

        /// <summary>
        /// 验证快照漂移被对账发现（差异含玩家/类别/资产/两侧数值，可定位）。
        /// </summary>
        [Fact]
        public async Task ReconcilePlayerAsync_WithDriftedSnapshot_ReportsLocatedDifference()
        {
            // Arrange
            var harness = new Harness();
            var grantService = harness.CreateGrantService();
            await grantService.ExecuteAsync(BuildRequest(10001, "key-1", 100));
            var drifting = new DriftingAssetStore(harness.AssetStore, 10001, 7);
            var reconciliation = harness.CreateReconciliationService(drifting);

            // Act
            var report = await reconciliation.ReconcilePlayerAsync(new OnlineScope(1, 10, 100, 10001));

            // Assert
            Assert.False(report.IsConsistent);
            var difference = Assert.Single(report.Differences);
            Assert.Equal(10001, difference.PlayerId);
            Assert.Equal(OnlineAssetKind.Currency, difference.AssetKind);
            Assert.Equal("gold", difference.AssetId);
            Assert.Equal(107, difference.SnapshotAmount);
            Assert.Equal(100, difference.LedgerSumAmount);
        }

        /// <summary>
        /// 验证全量巡检覆盖全部玩家（多人玩家计数 + 单人漂移定位到具体玩家）。
        /// </summary>
        [Fact]
        public async Task ReconcileAllAsync_CoversAllPlayersAndLocatesDrift()
        {
            // Arrange
            var harness = new Harness();
            var grantService = harness.CreateGrantService();
            await grantService.ExecuteAsync(BuildRequest(10001, "key-1", 100));
            await grantService.ExecuteAsync(BuildRequest(10002, "key-2", 50));
            var drifting = new DriftingAssetStore(harness.AssetStore, 10002, 3);
            var reconciliation = harness.CreateReconciliationService(drifting);

            // Act
            var report = await reconciliation.ReconcileAllAsync(1, 10);

            // Assert
            Assert.Equal(2, report.PlayerCount);
            Assert.False(report.IsConsistent);
            var difference = Assert.Single(report.Differences);
            Assert.Equal(10002, difference.PlayerId);
            Assert.Equal(53, difference.SnapshotAmount);
            Assert.Equal(50, difference.LedgerSumAmount);
        }
    }
}
