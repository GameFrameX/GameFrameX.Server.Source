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
using System.Linq;
using System.Reflection;
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
    /// OnlineGrantService 统一资产入口测试（vault:C4 VC-3.1～3.7：四来源幂等回放、
    /// 原子并发扣除、失败零半成品、部分应用补偿净效应归零、账本不可变只追加）。
    /// </summary>
    public class OnlineGrantServiceTests
    {
        /// <summary>
        /// 步进时钟（Foundation 协调器依赖 IClock；每次读取前进 10ms）。
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
        /// 事件收集出口（验证成功落账后发布资产变更事实）。
        /// </summary>
        private sealed class RecordingPublisher : IOnlineEventPublisher
        {
            public List<OnlineEvent> Events
            {
                get;
            } = new List<OnlineEvent>();

            public Task PublishAsync(OnlineEvent onlineEvent, CancellationToken cancellationToken = default)
            {
                Events.Add(onlineEvent);
                return Task.CompletedTask;
            }
        }

        /// <summary>
        /// 告警收集出口。
        /// </summary>
        private sealed class CollectingAlertSink : IOnlineAssetAlertSink
        {
            public List<OnlineAssetAlertRecord> Records
            {
                get;
            } = new List<OnlineAssetAlertRecord>();

            public Task AlertAsync(OnlineAssetAlertRecord record, CancellationToken cancellationToken = default)
            {
                Records.Add(record);
                return Task.CompletedTask;
            }
        }

        /// <summary>
        /// 故障注入模式（模拟持久化存储在应用阶段不同时点的进程崩溃）。
        /// </summary>
        private enum InjectionMode
        {
            /// <summary>不注入（透传）。</summary>
            None,

            /// <summary>应用前抛出（零落账中断）。</summary>
            ThrowBeforeApply,

            /// <summary>仅落第一行后抛出（部分应用中断）。</summary>
            ThrowAfterPartialApply,

            /// <summary>完整落账后抛出（成功落定前中断）。</summary>
            ThrowAfterFullApply,
        }

        /// <summary>
        /// 故障注入资产存储（包装 InMemory 实现：按模式在应用阶段制造中断；
        /// 部分应用模式经 internal 批次构造器反射落一行——持久化实现的多文档半写等价模拟）。
        /// </summary>
        private sealed class FaultInjectionAssetStore : IOnlineAssetStore
        {
            /// <summary>被包装存储。</summary>
            private readonly IOnlineAssetStore _inner;

            /// <summary>注入模式。</summary>
            public InjectionMode Mode
            {
                get;
                set;
            }

            /// <summary>只在第 N 次应用时注入（1 起；0 = 永不）。</summary>
            public int FailOnApplyCall
            {
                get;
                set;
            }

            /// <summary>应用调用计数。</summary>
            public int ApplyCalls
            {
                get;
                private set;
            }

            public FaultInjectionAssetStore(IOnlineAssetStore inner)
            {
                _inner = inner;
                Mode = InjectionMode.None;
                FailOnApplyCall = 0;
            }

            public Task<OnlineAssetApplyResult> ApplyAsync(OnlineAssetChangeBatch batch, CancellationToken cancellationToken = default)
            {
                ApplyCalls++;
                if (FailOnApplyCall == 0 || ApplyCalls != FailOnApplyCall)
                {
                    return _inner.ApplyAsync(batch, cancellationToken);
                }

                switch (Mode)
                {
                    case InjectionMode.ThrowBeforeApply:
                        throw new InvalidOperationException("注入：应用前崩溃");
                    case InjectionMode.ThrowAfterPartialApply:
                        {
                            var truncated = BuildBatch(batch, batch.Lines.Take(1).ToList());
                            var partial = _inner.ApplyAsync(truncated, cancellationToken).GetAwaiter().GetResult();
                            if (!partial.Success)
                            {
                                throw new InvalidOperationException("注入失败：部分批次被拒");
                            }

                            throw new InvalidOperationException("注入：部分落账后崩溃");
                        }

                    default:
                        {
                            var full = _inner.ApplyAsync(batch, cancellationToken).GetAwaiter().GetResult();
                            if (!full.Success)
                            {
                                return Task.FromResult(full);
                            }

                            throw new InvalidOperationException("注入：完整落账后崩溃");
                        }
                }
            }

            public Task<OnlineWalletAccount> FindWalletAsync(long tenantId, long appId, long playerId, string currencyId, CancellationToken cancellationToken = default)
            {
                return _inner.FindWalletAsync(tenantId, appId, playerId, currencyId, cancellationToken);
            }

            public Task<OnlineInventoryStack> FindInventoryAsync(long tenantId, long appId, long playerId, string itemId, CancellationToken cancellationToken = default)
            {
                return _inner.FindInventoryAsync(tenantId, appId, playerId, itemId, cancellationToken);
            }

            public Task<IReadOnlyList<OnlineWalletAccount>> ListWalletAccountsAsync(long tenantId, long appId, long playerId, CancellationToken cancellationToken = default)
            {
                return _inner.ListWalletAccountsAsync(tenantId, appId, playerId, cancellationToken);
            }

            public Task<IReadOnlyList<OnlineInventoryStack>> ListInventoryStacksAsync(long tenantId, long appId, long playerId, CancellationToken cancellationToken = default)
            {
                return _inner.ListInventoryStacksAsync(tenantId, appId, playerId, cancellationToken);
            }

            public Task<IReadOnlyList<OnlineLedgerEntry>> ListLedgerEntriesAsync(OnlineLedgerPageQuery query, CancellationToken cancellationToken = default)
            {
                return _inner.ListLedgerEntriesAsync(query, cancellationToken);
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

            /// <summary>
            /// 经 internal 构造器反射构造截断批次（测试程序集无法直接构造，X5）。
            /// </summary>
            private static OnlineAssetChangeBatch BuildBatch(OnlineAssetChangeBatch source, IReadOnlyList<OnlineAssetChangeLine> lines)
            {
                var constructor = typeof(OnlineAssetChangeBatch).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).Single();
                var header = new OnlineLedgerHeader
                {
                    TenantId = source.TenantId,
                    AppId = source.AppId,
                    PlayerId = source.PlayerId,
                    HomeServerId = source.HomeServerId,
                    InitiatingServerId = source.InitiatingServerId,
                    Source = source.Source,
                    Operation = source.Operation,
                    Reason = source.Reason,
                    BusinessOrderId = source.BusinessOrderId,
                    OperatorId = source.OperatorId,
                };
                return (OnlineAssetChangeBatch)constructor.Invoke(new object[] { source.TransactionId, header, lines, source.CompensatesTransactionId });
            }
        }

        /// <summary>
        /// 崩溃注入交易存储（在第 N 次保存时抛出，模拟落定持久化前崩溃）。
        /// </summary>
        private sealed class CrashyTransactionStore : IOnlineAssetTransactionStore
        {
            /// <summary>被包装存储。</summary>
            private readonly IOnlineAssetTransactionStore _inner;

            /// <summary>保存计数。</summary>
            public int SaveCalls
            {
                get;
                private set;
            }

            /// <summary>在第 N 次保存抛出（0 = 永不）。</summary>
            public int ThrowOnSave
            {
                get;
                set;
            }

            public CrashyTransactionStore(IOnlineAssetTransactionStore inner)
            {
                _inner = inner;
                ThrowOnSave = 0;
            }

            public Task<OnlineAssetTransaction> FindAsync(string transactionId, CancellationToken cancellationToken = default)
            {
                return _inner.FindAsync(transactionId, cancellationToken);
            }

            public Task SaveAsync(OnlineAssetTransaction transaction, CancellationToken cancellationToken = default)
            {
                SaveCalls++;
                if (ThrowOnSave != 0 && SaveCalls == ThrowOnSave)
                {
                    throw new InvalidOperationException("注入：落定保存前崩溃");
                }

                return _inner.SaveAsync(transaction, cancellationToken);
            }

            public Task<IReadOnlyList<OnlineAssetTransaction>> ListByStateAsync(OnlineAssetTransactionState state, CancellationToken cancellationToken = default)
            {
                return _inner.ListByStateAsync(state, cancellationToken);
            }
        }

        /// <summary>
        /// 测试基座（共享存储模拟持久化跨重启；CreateService 每次 = 一次进程重启重建服务）。
        /// </summary>
        private sealed class Harness
        {
            public InMemoryOnlineAssetStore AssetStore
            {
                get;
            } = new InMemoryOnlineAssetStore();

            public InMemoryOnlineAssetTransactionStore TransactionStore
            {
                get;
            } = new InMemoryOnlineAssetTransactionStore();

            public InMemoryIdempotencyStore IdempotencyStore
            {
                get;
            } = new InMemoryIdempotencyStore();

            public RecordingPublisher Publisher
            {
                get;
            } = new RecordingPublisher();

            public CollectingAlertSink AlertSink
            {
                get;
            } = new CollectingAlertSink();

            public OnlineGrantService CreateService(IOnlineAssetStore assetStore = null, IOnlineAssetTransactionStore transactionStore = null)
            {
                var options = new IdempotencyOptions
                {
                    ConcurrentWaitTimeoutMilliseconds = 1,
                };
                var coordinator = new IdempotencyCoordinator(IdempotencyStore, new StepClock(), options);
                var idempotencyService = new OnlineIdempotencyService(coordinator, null);
                return new OnlineGrantService(assetStore ?? AssetStore, transactionStore ?? TransactionStore, idempotencyService, Publisher, AlertSink);
            }
        }

        /// <summary>
        /// 构造统一入口请求（默认 Grant 语义；键与单号联动保证唯一；变更行 = 首行 + 追加行）。
        /// </summary>
        private static OnlineGrantRequest BuildRequest(long playerId, OnlineAssetChangeSource source, string key, OnlineAssetChangeLine line, params OnlineAssetChangeLine[] moreLines)
        {
            var scope = new OnlineScope(1, 10, 100, playerId);
            var lines = new List<OnlineAssetChangeLine> { line };
            lines.AddRange(moreLines);
            return new OnlineGrantRequest(scope, source, OnlineGrantOperation.Grant, "赛季结算奖励", "bo-" + key, lines, key);
        }

        /// <summary>
        /// 验证 VC-3.1～3.4：四类业务来源走统一入口，同键重试回放首次结果（只生效一次）。
        /// </summary>
        [Theory]
        [InlineData(OnlineAssetChangeSource.PaymentConfirm)]
        [InlineData(OnlineAssetChangeSource.RedeemCode)]
        [InlineData(OnlineAssetChangeSource.MailAttachment)]
        [InlineData(OnlineAssetChangeSource.MatchReward)]
        public async Task ExecuteAsync_FourSources_RetrySameKeyReplaysFirstResult(OnlineAssetChangeSource source)
        {
            // Arrange
            var harness = new Harness();
            var service = harness.CreateService();
            var request = BuildRequest(10001, source, "key-1", new OnlineAssetChangeLine(OnlineAssetKind.Currency, "gold", 100));

            // Act
            var first = await service.ExecuteAsync(request);
            var second = await service.ExecuteAsync(request);

            // Assert
            Assert.True(first.IsSuccess);
            Assert.False(first.Data.IsReplay);
            Assert.True(second.IsSuccess);
            Assert.True(second.Data.IsReplay);
            Assert.Equal(first.Data.TransactionId, second.Data.TransactionId);
            var wallet = await harness.AssetStore.FindWalletAsync(1, 10, 10001, "gold");
            Assert.Equal(100, wallet.Balance);
            var entries = await harness.AssetStore.FindLedgerEntriesByTransactionIdAsync(first.Data.TransactionId);
            Assert.Single(entries);
            Assert.Contains(harness.AlertSink.Records, record => record.Kind == OnlineAssetAlertKind.DuplicateRequestObserved);
        }

        /// <summary>
        /// 验证同键不同意图判冲突（规范化请求文本变化 → 6xxx，VC-1.4 语义在资产域的落地）。
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_SameKeyDifferentIntent_ShouldReturnVersionConflict()
        {
            // Arrange
            var harness = new Harness();
            var service = harness.CreateService();
            var first = await service.ExecuteAsync(BuildRequest(10001, OnlineAssetChangeSource.MatchReward, "key-1", new OnlineAssetChangeLine(OnlineAssetKind.Currency, "gold", 100)));
            var conflicting = BuildRequest(10001, OnlineAssetChangeSource.MatchReward, "key-1", new OnlineAssetChangeLine(OnlineAssetKind.Currency, "gold", 200));

            // Act
            var result = await service.ExecuteAsync(conflicting);

            // Assert
            Assert.True(first.IsSuccess);
            Assert.False(result.IsSuccess);
            Assert.Equal(OnlineErrorCode.VersionConflict, result.Code);
            var wallet = await harness.AssetStore.FindWalletAsync(1, 10, 10001, "gold");
            Assert.Equal(100, wallet.Balance);
        }

        /// <summary>
        /// 验证入口策略红线：系统补偿来源禁入、撤销/调整必带操作者、数额非 0、同交易资产不重复。
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_PolicyViolations_ShouldReturnParameterInvalid()
        {
            // Arrange
            var harness = new Harness();
            var service = harness.CreateService();
            var scope = new OnlineScope(1, 10, 100, 10001);

            // Act / Assert
            var compensationSource = await service.ExecuteAsync(new OnlineGrantRequest(scope, OnlineAssetChangeSource.SystemCompensation, OnlineGrantOperation.Adjust, "r", "bo-x", new[] { new OnlineAssetChangeLine(OnlineAssetKind.Currency, "gold", 100) }, "key-c") { OperatorId = "op-1" });
            Assert.False(compensationSource.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, compensationSource.Code);

            var noOperator = await service.ExecuteAsync(new OnlineGrantRequest(scope, OnlineAssetChangeSource.AdminOperation, OnlineGrantOperation.Revoke, "r", "bo-y", new[] { new OnlineAssetChangeLine(OnlineAssetKind.Currency, "gold", 100) }, "key-o"));
            Assert.False(noOperator.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, noOperator.Code);

            var zeroAmount = await service.ExecuteAsync(new OnlineGrantRequest(scope, OnlineAssetChangeSource.MatchReward, OnlineGrantOperation.Grant, "r", "bo-z", new[] { new OnlineAssetChangeLine(OnlineAssetKind.Currency, "gold", 0) }, "key-z"));
            Assert.False(zeroAmount.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, zeroAmount.Code);

            var duplicateAsset = await service.ExecuteAsync(new OnlineGrantRequest(scope, OnlineAssetChangeSource.MatchReward, OnlineGrantOperation.Grant, "r", "bo-d", new[] { new OnlineAssetChangeLine(OnlineAssetKind.Currency, "gold", 100), new OnlineAssetChangeLine(OnlineAssetKind.Currency, "gold", 50) }, "key-d"));
            Assert.False(duplicateAsset.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, duplicateAsset.Code);

            var emptyReason = await service.ExecuteAsync(new OnlineGrantRequest(scope, OnlineAssetChangeSource.MatchReward, OnlineGrantOperation.Grant, " ", "bo-e", new[] { new OnlineAssetChangeLine(OnlineAssetKind.Currency, "gold", 100) }, "key-e"));
            Assert.False(emptyReason.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, emptyReason.Code);
        }

        /// <summary>
        /// 验证 VC-3.6：余额不足整批拒绝（零半成品——快照与账本都不留痕迹）并告警。
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_InsufficientBalance_ShouldFailWithoutPartialState()
        {
            // Arrange
            var harness = new Harness();
            var service = harness.CreateService();
            await service.ExecuteAsync(BuildRequest(10001, OnlineAssetChangeSource.MatchReward, "key-init", new OnlineAssetChangeLine(OnlineAssetKind.Currency, "gold", 100)));

            // Act
            var result = await service.ExecuteAsync(BuildRequest(10001, OnlineAssetChangeSource.MatchReward, "key-deduct", new OnlineAssetChangeLine(OnlineAssetKind.Currency, "gold", -150)));

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal(OnlineErrorCode.StateOperationForbidden, result.Code);
            var wallet = await harness.AssetStore.FindWalletAsync(1, 10, 10001, "gold");
            Assert.Equal(100, wallet.Balance);
            var ledger = await harness.AssetStore.ListLedgerEntriesAsync(new OnlineLedgerPageQuery { TenantId = 1, AppId = 10, PlayerId = 10001, AfterSequenceNumber = 0, MaxCount = 100 });
            Assert.Single(ledger);
            Assert.Contains(harness.AlertSink.Records, record => record.Kind == OnlineAssetAlertKind.NegativeBalanceAttempt);
        }

        /// <summary>
        /// 验证 VC-3.5：并发扣除互斥——只有余额可覆盖的请求成功，终态余额精确归零。
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_ConcurrentDeducts_OnlyAffordableApply()
        {
            // Arrange
            var harness = new Harness();
            var service = harness.CreateService();
            await service.ExecuteAsync(BuildRequest(10001, OnlineAssetChangeSource.MatchReward, "key-init", new OnlineAssetChangeLine(OnlineAssetKind.Currency, "gold", 100)));
            var requests = Enumerable.Range(1, 10)
                .Select(index => BuildRequest(10001, OnlineAssetChangeSource.MatchReward, "key-deduct-" + index, new OnlineAssetChangeLine(OnlineAssetKind.Currency, "gold", -20)))
                .ToList();

            // Act
            var results = await Task.WhenAll(requests.Select(request => service.ExecuteAsync(request)));

            // Assert
            Assert.Equal(5, results.Count(result => result.IsSuccess));
            Assert.Equal(5, results.Count(result => !result.IsSuccess));
            var wallet = await harness.AssetStore.FindWalletAsync(1, 10, 10001, "gold");
            Assert.Equal(0, wallet.Balance);
            var ledger = await harness.AssetStore.ListLedgerEntriesAsync(new OnlineLedgerPageQuery { TenantId = 1, AppId = 10, PlayerId = 10001, AfterSequenceNumber = 0, MaxCount = 100 });
            Assert.Equal(6, ledger.Count);
        }

        /// <summary>
        /// 验证 VC-3.7/事件契约：多资产生效后账本前后值链式可推，事件带全量审计字段。
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_MultiAssetBatch_ChainsLedgerAndPublishesAuditedEvent()
        {
            // Arrange
            var harness = new Harness();
            var service = harness.CreateService();
            var request = BuildRequest(10001, OnlineAssetChangeSource.MatchReward, "key-multi", new OnlineAssetChangeLine(OnlineAssetKind.Currency, "gold", 100), new OnlineAssetChangeLine(OnlineAssetKind.Item, "sword", 1));

            // Act
            var result = await service.ExecuteAsync(request);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Equal(2, result.Data.Entries.Count);
            var goldEntry = result.Data.Entries.Single(entry => entry.AssetId == "gold");
            var swordEntry = result.Data.Entries.Single(entry => entry.AssetId == "sword");
            Assert.Equal(0, goldEntry.AmountBefore);
            Assert.Equal(100, goldEntry.AmountAfter);
            Assert.Equal(0, swordEntry.AmountBefore);
            Assert.Equal(1, swordEntry.AmountAfter);
            Assert.All(result.Data.Entries, entry =>
            {
                Assert.Equal(OnlineAssetChangeSource.MatchReward, entry.Source);
                Assert.Equal("bo-key-multi", entry.BusinessOrderId);
                Assert.StartsWith("tx-", entry.TransactionId);
            });

            var published = Assert.Single(harness.Publisher.Events);
            Assert.Equal(OnlineAssetEvents.AssetGranted, published.EventType);
            Assert.Equal("online-assets", published.Source);
            Assert.Equal(result.Data.TransactionId, published.PayloadAuditFields["TransactionId"]);
            Assert.Equal(nameof(OnlineAssetChangeSource.MatchReward), published.PayloadAuditFields["ChangeSource"]);
        }

        /// <summary>
        /// 验证 VC-3.6：部分应用中断 → 追加反转条目净效应归零，原条目不可变保留，原交易置 Compensated。
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_PartialApplyCrash_CompensatesToNetZero()
        {
            // Arrange
            var harness = new Harness();
            var faultStore = new FaultInjectionAssetStore(harness.AssetStore)
            {
                Mode = InjectionMode.ThrowAfterPartialApply,
                FailOnApplyCall = 1,
            };
            var service = harness.CreateService(faultStore);
            var request = BuildRequest(10001, OnlineAssetChangeSource.MatchReward, "key-partial", new OnlineAssetChangeLine(OnlineAssetKind.Currency, "gold", 100), new OnlineAssetChangeLine(OnlineAssetKind.Item, "sword", 1));

            // Act
            var result = await service.ExecuteAsync(request);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal(OnlineErrorCode.InternalError, result.Code);
            var wallet = await harness.AssetStore.FindWalletAsync(1, 10, 10001, "gold");
            Assert.NotNull(wallet);
            Assert.Equal(0, wallet.Balance);
            var stack = await harness.AssetStore.FindInventoryAsync(1, 10, 10001, "sword");
            Assert.Null(stack);

            var compensated = await harness.TransactionStore.ListByStateAsync(OnlineAssetTransactionState.Compensated);
            var original = Assert.Single(compensated);
            Assert.Equal("key-partial", original.IdempotencyKey);
            Assert.Equal(2, original.ExpectedChangeCount);

            var succeeded = await harness.TransactionStore.ListByStateAsync(OnlineAssetTransactionState.Succeeded);
            var compensation = Assert.Single(succeeded);
            Assert.Equal(OnlineAssetChangeSource.SystemCompensation, compensation.Source);
            Assert.Contains("补偿反转", compensation.Reason);

            var originalEntries = await harness.AssetStore.FindLedgerEntriesByTransactionIdAsync(original.TransactionId);
            var partial = Assert.Single(originalEntries);
            Assert.Equal(100, partial.Delta);
            var reversal = Assert.Single(await harness.AssetStore.FindLedgerEntriesByTransactionIdAsync(compensation.TransactionId));
            Assert.Equal(-100, reversal.Delta);
            Assert.Equal(original.TransactionId, reversal.CompensatesTransactionId);
            Assert.Contains(harness.AlertSink.Records, record => record.Kind == OnlineAssetAlertKind.CompensatedAfterApplyFailure);
        }

        /// <summary>
        /// 验证零落账中断判 Failed（可换新幂等键重试成功），快照无任何变更。
        /// </summary>
        [Fact]
        public async Task ExecuteAsync_ZeroApplyCrash_MarksFailedAndRetriableWithNewKey()
        {
            // Arrange
            var harness = new Harness();
            var faultStore = new FaultInjectionAssetStore(harness.AssetStore)
            {
                Mode = InjectionMode.ThrowBeforeApply,
                FailOnApplyCall = 1,
            };
            var service = harness.CreateService(faultStore);

            // Act（第一段：崩溃落零账）
            var crashed = await service.ExecuteAsync(BuildRequest(10001, OnlineAssetChangeSource.MatchReward, "key-zero", new OnlineAssetChangeLine(OnlineAssetKind.Currency, "gold", 100)));

            // Assert（崩溃后无任何半成品）
            Assert.False(crashed.IsSuccess);
            Assert.Equal(OnlineErrorCode.InternalError, crashed.Code);
            var failed = await harness.TransactionStore.ListByStateAsync(OnlineAssetTransactionState.Failed);
            Assert.Single(failed);
            var walletAfterCrash = await harness.AssetStore.FindWalletAsync(1, 10, 10001, "gold");
            Assert.Null(walletAfterCrash);

            // Act（第二段：换新幂等键重试）
            var retried = await service.ExecuteAsync(BuildRequest(10001, OnlineAssetChangeSource.MatchReward, "key-zero-retry", new OnlineAssetChangeLine(OnlineAssetKind.Currency, "gold", 100)));

            // Assert
            Assert.True(retried.IsSuccess);
            var wallet = await harness.AssetStore.FindWalletAsync(1, 10, 10001, "gold");
            Assert.Equal(100, wallet.Balance);
        }

        /// <summary>
        /// 验证 VC-3.12：完整落账后、落定保存前崩溃 → 重启恢复判 Succeeded，同键重试回放而非重复发放。
        /// </summary>
        [Fact]
        public async Task RecoverAsync_FullApplyCrashBeforeSettle_RecoversAsSucceededThenReplays()
        {
            // Arrange
            var harness = new Harness();
            var crashyStore = new CrashyTransactionStore(harness.TransactionStore)
            {
                ThrowOnSave = 2,
            };
            var crashingService = harness.CreateService(null, crashyStore);
            var request = BuildRequest(10001, OnlineAssetChangeSource.PaymentConfirm, "key-crash", new OnlineAssetChangeLine(OnlineAssetKind.Currency, "gold", 100));

            // Act（第一次：落账完成但落定保存崩溃）
            await Assert.ThrowsAsync<InvalidOperationException>(() => crashingService.ExecuteAsync(request));

            // 重启（同一持久化存储重建服务）
            var recoveredService = harness.CreateService();
            var recovered = await recoveredService.RecoverAsync();
            var replayed = await recoveredService.ExecuteAsync(request);

            // Assert
            Assert.Equal(1, recovered);
            var succeeded = await harness.TransactionStore.ListByStateAsync(OnlineAssetTransactionState.Succeeded);
            Assert.Single(succeeded);
            Assert.True(replayed.IsSuccess);
            Assert.True(replayed.Data.IsReplay);
            var wallet = await harness.AssetStore.FindWalletAsync(1, 10, 10001, "gold");
            Assert.Equal(100, wallet.Balance);
            var ledger = await harness.AssetStore.ListLedgerEntriesAsync(new OnlineLedgerPageQuery { TenantId = 1, AppId = 10, PlayerId = 10001, AfterSequenceNumber = 0, MaxCount = 100 });
            Assert.Single(ledger);
        }

        /// <summary>
        /// 验证 VC-3.12：重启时零落账的 Executing 交易判 Failed（不存在半成品长期滞留）。
        /// </summary>
        [Fact]
        public async Task RecoverAsync_ZeroEntryExecutingTransaction_MarksFailed()
        {
            // Arrange
            var harness = new Harness();
            var service = harness.CreateService();
            var orphan = new OnlineAssetTransaction
            {
                TransactionId = "tx-orphan-1",
                IdempotencyKey = "key-orphan",
                RequestDigest = string.Empty,
                TenantId = 1,
                AppId = 10,
                PlayerId = 10001,
                HomeServerId = 100,
                InitiatingServerId = 100,
                Source = OnlineAssetChangeSource.MatchReward,
                Operation = OnlineGrantOperation.Grant,
                Reason = "崩溃残留",
                BusinessOrderId = "bo-orphan",
                OperatorId = string.Empty,
                ExpectedChangeCount = 1,
                State = OnlineAssetTransactionState.Executing,
                AppliedLedgerEntryIds = Array.Empty<string>(),
                CreatedTime = 1760000000000,
                SettledTime = 0,
                FailureMessage = string.Empty,
            };
            await harness.TransactionStore.SaveAsync(orphan);

            // Act
            var recovered = await service.RecoverAsync();

            // Assert
            Assert.Equal(1, recovered);
            var failed = await harness.TransactionStore.ListByStateAsync(OnlineAssetTransactionState.Failed);
            Assert.Single(failed);
            Assert.Equal("tx-orphan-1", failed[0].TransactionId);
        }
    }
}
