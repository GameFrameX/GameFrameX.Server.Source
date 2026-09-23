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
    /// G3 端到端验收测试（vault:C4：四来源统一入口 → 幂等 → 事件 → 对账差异 0 →
    /// Admin 查询面可见；无来源变更结构性不可达）。
    /// </summary>
    public class OnlineAssetG3AcceptanceTests
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
        /// 事件收集出口。
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
        /// 验证 G3 全链路：每类来源一笔发放 + 同键重试，终态对账差异 0，
        /// 事件逐笔发布，查询面流水/反查可见，无资产类异常告警。
        /// </summary>
        [Fact]
        public async Task G3_Acceptance_FourSourcesEndToEnd()
        {
            // Arrange
            var assetStore = new InMemoryOnlineAssetStore();
            var transactionStore = new InMemoryOnlineAssetTransactionStore();
            var publisher = new RecordingPublisher();
            var alertSink = new CollectingAlertSink();
            var options = new IdempotencyOptions
            {
                ConcurrentWaitTimeoutMilliseconds = 1,
            };
            var coordinator = new IdempotencyCoordinator(new InMemoryIdempotencyStore(), new StepClock(), options);
            var grantService = new OnlineGrantService(assetStore, transactionStore, new OnlineIdempotencyService(coordinator, null), publisher, alertSink);
            var queryService = new OnlineAssetQueryService(assetStore, transactionStore);
            var reconciliationService = new OnlineAssetReconciliationService(assetStore);
            var sources = new[]
            {
                OnlineAssetChangeSource.PaymentConfirm,
                OnlineAssetChangeSource.RedeemCode,
                OnlineAssetChangeSource.MailAttachment,
                OnlineAssetChangeSource.MatchReward,
            };
            var scope = new OnlineScope(1, 10, 100, 10001);
            var transactionIds = new List<string>();

            // Act：每类来源发放 100（不同幂等键）+ 同键重试一次（应回放）。
            foreach (var source in sources)
            {
                var key = "g3-" + source;
                var request = new OnlineGrantRequest(scope, source, OnlineGrantOperation.Grant, "G3 验收发放", "bo-" + key, new[] { new OnlineAssetChangeLine(OnlineAssetKind.Currency, "gold", 100) }, key);
                var first = await grantService.ExecuteAsync(request);
                var retry = await grantService.ExecuteAsync(request);
                Assert.True(first.IsSuccess);
                Assert.True(retry.IsSuccess);
                Assert.True(retry.Data.IsReplay);
                Assert.Equal(first.Data.TransactionId, retry.Data.TransactionId);
                transactionIds.Add(first.Data.TransactionId);
            }

            // Assert：终态余额 = 4 × 100（重试不重复生效）。
            var wallet = await queryService.GetWalletAsync(scope, "gold");
            Assert.True(wallet.IsSuccess);
            Assert.Equal(400, wallet.Data.Balance);

            // 流水与反查：4 条账本条目逐笔可追溯。
            var ledgerPage = await queryService.GetLedgerPageAsync(scope, string.Empty, 100);
            Assert.True(ledgerPage.IsSuccess);
            Assert.Equal(4, ledgerPage.Data.Entries.Count);
            foreach (var transactionId in transactionIds)
            {
                var detail = await queryService.GetTransactionDetailAsync(scope, transactionId);
                Assert.True(detail.IsSuccess);
                Assert.Equal(100, Assert.Single(detail.Data.Entries).Delta);
            }

            // 事件：逐笔发布资产变更事实。
            Assert.Equal(4, publisher.Events.Count);
            Assert.All(publisher.Events, published =>
            {
                Assert.Equal(OnlineAssetEvents.AssetGranted, published.EventType);
                Assert.Equal("online-assets", published.Source);
            });

            // 对账：账本累加 == 快照（差异 = 0 红线）。
            var report = await reconciliationService.ReconcileAllAsync(1, 10);
            Assert.Equal(1, report.PlayerCount);
            Assert.True(report.IsConsistent);

            // 告警：只有重复意图观测，无负数尝试/补偿类告警。
            Assert.Equal(4, alertSink.Records.Count);
            Assert.All(alertSink.Records, record => Assert.Equal(OnlineAssetAlertKind.DuplicateRequestObserved, record.Kind));
        }
    }
}
