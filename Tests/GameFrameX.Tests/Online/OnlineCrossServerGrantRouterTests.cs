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
    /// 跨服发奖路由与补偿队列测试（vault:C4 VC-3.10/3.11：归属服路由、不可达入队、
    /// 恢复后自动续投不重复、业务性失败不重试、SLO 超时告警一次）。
    /// </summary>
    public class OnlineCrossServerGrantRouterTests
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
        /// 可编程传输（按投递序执行行为队列：返回结果或抛异常）。
        /// </summary>
        private sealed class ScriptedTransport : IOnlineCrossServerGrantTransport
        {
            /// <summary>每次投递的行为（true = 返回成功，false = 返回业务失败，null = 抛不可达异常）。</summary>
            private readonly Queue<bool?> _script;

            /// <summary>投递记录（目标服, 幂等键）。</summary>
            public List<(long ServerId, string IdempotencyKey)> Calls
            {
                get;
            } = new List<(long, string)>();

            public ScriptedTransport(params bool?[] script)
            {
                _script = new Queue<bool?>(script);
            }

            public Task<OnlineResult<OnlineGrantResult>> DeliverAsync(long targetServerId, OnlineGrantRequest request, CancellationToken cancellationToken = default)
            {
                Calls.Add((targetServerId, request.IdempotencyKey));
                var behavior = _script.Count > 0 ? _script.Dequeue() : null;
                if (behavior == null)
                {
                    throw new InvalidOperationException("归属服不可达（脚本注入）");
                }

                if (behavior.Value)
                {
                    return Task.FromResult(OnlineResult<OnlineGrantResult>.Ok(new OnlineGrantResult { TransactionId = "tx-remote-1", State = OnlineAssetTransactionState.Succeeded, IsReplay = false, Entries = new OnlineLedgerEntry[0] }));
                }

                return Task.FromResult(OnlineResult<OnlineGrantResult>.Fail(OnlineErrorCode.StateOperationForbidden, "资产不足（业务性失败）"));
            }
        }

        /// <summary>
        /// 转发传输（把请求投给真实的远端统一入口——验证续投只生效一次）。
        /// </summary>
        private sealed class ForwardingTransport : IOnlineCrossServerGrantTransport
        {
            /// <summary>远端统一入口。</summary>
            private readonly OnlineGrantService _remoteService;

            public List<(long ServerId, string IdempotencyKey)> Calls
            {
                get;
            } = new List<(long, string)>();

            public ForwardingTransport(OnlineGrantService remoteService)
            {
                _remoteService = remoteService;
            }

            public Task<OnlineResult<OnlineGrantResult>> DeliverAsync(long targetServerId, OnlineGrantRequest request, CancellationToken cancellationToken = default)
            {
                Calls.Add((targetServerId, request.IdempotencyKey));
                return _remoteService.ExecuteAsync(request, cancellationToken);
            }
        }

        /// <summary>
        /// 测试基座（本地与远端各自持有独立存储，模拟两台服务器）。
        /// </summary>
        private sealed class Harness
        {
            public InMemoryOnlineAssetStore LocalAssetStore
            {
                get;
            } = new InMemoryOnlineAssetStore();

            public InMemoryOnlineAssetStore RemoteAssetStore
            {
                get;
            } = new InMemoryOnlineAssetStore();

            public CollectingAlertSink AlertSink
            {
                get;
            } = new CollectingAlertSink();

            public OnlineGrantService CreateGrantService(InMemoryOnlineAssetStore assetStore)
            {
                var options = new IdempotencyOptions
                {
                    ConcurrentWaitTimeoutMilliseconds = 1,
                };
                var coordinator = new IdempotencyCoordinator(new InMemoryIdempotencyStore(), new StepClock(), options);
                var idempotencyService = new OnlineIdempotencyService(coordinator, null);
                return new OnlineGrantService(assetStore, new InMemoryOnlineAssetTransactionStore(), idempotencyService, new NullPublisher(), AlertSink);
            }

            public OnlineCrossServerGrantRouter CreateRouter(OnlineGrantService localService, IOnlineCrossServerGrantTransport transport, OnlineGrantCompensationQueue queue = null)
            {
                return new OnlineCrossServerGrantRouter(100, localService, transport, queue ?? new OnlineGrantCompensationQueue(transport, AlertSink));
            }
        }

        /// <summary>
        /// 构造跨服请求（发起服 100，归属服 200）。
        /// </summary>
        private static OnlineGrantRequest BuildRemoteRequest(long playerId, string key)
        {
            var scope = new OnlineScope(1, 10, 100, playerId);
            return new OnlineGrantRequest(scope, OnlineAssetChangeSource.MatchReward, OnlineGrantOperation.Grant, "跨服赛果", "bo-" + key, new[] { new OnlineAssetChangeLine(OnlineAssetKind.Currency, "gold", 100) }, key) { HomeServerId = 200 };
        }

        /// <summary>
        /// 验证 VC-3.10：归属服 = 本服时直接本地执行（不经传输）。
        /// </summary>
        [Fact]
        public async Task RouteAsync_LocalHome_ExecutesLocally()
        {
            // Arrange
            var harness = new Harness();
            var localService = harness.CreateGrantService(harness.LocalAssetStore);
            var transport = new ScriptedTransport(true);
            var router = harness.CreateRouter(localService, transport);
            var scope = new OnlineScope(1, 10, 100, 10001);
            var request = new OnlineGrantRequest(scope, OnlineAssetChangeSource.MatchReward, OnlineGrantOperation.Grant, "本地赛果", "bo-local", new[] { new OnlineAssetChangeLine(OnlineAssetKind.Currency, "gold", 100) }, "key-local");

            // Act
            var result = await router.RouteAsync(request);

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Empty(transport.Calls);
            var wallet = await harness.LocalAssetStore.FindWalletAsync(1, 10, 10001, "gold");
            Assert.Equal(100, wallet.Balance);
        }

        /// <summary>
        /// 验证 VC-3.10：归属服为远端时经传输投递，结果原样透传，本地存储不变。
        /// </summary>
        [Fact]
        public async Task RouteAsync_RemoteHome_DeliversViaTransport()
        {
            // Arrange
            var harness = new Harness();
            var localService = harness.CreateGrantService(harness.LocalAssetStore);
            var transport = new ScriptedTransport(true);
            var router = harness.CreateRouter(localService, transport);

            // Act
            var result = await router.RouteAsync(BuildRemoteRequest(10001, "key-remote"));

            // Assert
            Assert.True(result.IsSuccess);
            Assert.Equal("tx-remote-1", result.Data.TransactionId);
            var call = Assert.Single(transport.Calls);
            Assert.Equal(200, call.ServerId);
            Assert.Equal("key-remote", call.IdempotencyKey);
            var localWallet = await harness.LocalAssetStore.FindWalletAsync(1, 10, 10001, "gold");
            Assert.Null(localWallet);
        }

        /// <summary>
        /// 验证 VC-3.11：远端不可达 → 返回 DependencyUnavailable 且入补偿队列；
        /// 恢复后续投恰好一次成功、远端只生效一次（幂等键不变）。
        /// </summary>
        [Fact]
        public async Task RouteAsync_RemoteUnreachable_EnqueuesAndDrainDeliversOnce()
        {
            // Arrange
            var harness = new Harness();
            var localService = harness.CreateGrantService(harness.LocalAssetStore);
            var remoteService = harness.CreateGrantService(harness.RemoteAssetStore);
            var forwarding = new ForwardingTransport(remoteService);
            var queue = new OnlineGrantCompensationQueue(forwarding, harness.AlertSink);
            // 首投不可达：直接用会抛异常的传输构造路由器，队列接转发传输。
            var unreachable = new ScriptedTransport();
            var router = new OnlineCrossServerGrantRouter(100, localService, unreachable, queue);
            var request = BuildRemoteRequest(10001, "key-comp");

            // Act
            var unreachableResult = await router.RouteAsync(request);
            var pendingAfterEnqueue = queue.ListPending();
            var drain = await queue.DrainAsync();
            var drainAgain = await queue.DrainAsync();

            // Assert
            Assert.False(unreachableResult.IsSuccess);
            Assert.Equal(OnlineErrorCode.DependencyUnavailable, unreachableResult.Code);
            var pending = Assert.Single(pendingAfterEnqueue);
            Assert.Equal("key-comp", pending.Request.IdempotencyKey);

            Assert.Equal(1, drain.DeliveredCount);
            Assert.Equal(0, drain.RemainingCount);
            Assert.Empty(queue.ListPending());
            // 续投一次即成功出队（重试不重复生效由远端幂等键保证）。
            Assert.Single(forwarding.Calls);
            var remoteWallet = await harness.RemoteAssetStore.FindWalletAsync(1, 10, 10001, "gold");
            Assert.Equal(100, remoteWallet.Balance);
            var remoteLedger = await harness.RemoteAssetStore.ListLedgerEntriesAsync(new OnlineLedgerPageQuery { TenantId = 1, AppId = 10, PlayerId = 10001, AfterSequenceNumber = 0, MaxCount = 100 });
            Assert.Single(remoteLedger);
            Assert.Equal(0, drainAgain.RemainingCount);
        }

        /// <summary>
        /// 验证业务性失败（非异常）不入重试循环：队列投递获得确定答复即移除。
        /// </summary>
        [Fact]
        public async Task CompensationQueue_BusinessFailureAnswer_RemovesWithoutRetry()
        {
            // Arrange
            var harness = new Harness();
            var transport = new ScriptedTransport(false);
            var queue = new OnlineGrantCompensationQueue(transport, harness.AlertSink);
            // 直接入队一笔待补偿请求（模拟此前不可达遗留）。
            queue.Enqueue(BuildRemoteRequest(10001, "key-biz"));

            // Act
            var drain = await queue.DrainAsync();

            // Assert
            Assert.Equal(0, drain.DeliveredCount);
            Assert.Equal(0, drain.RemainingCount);
            Assert.Single(transport.Calls);
            Assert.Empty(queue.ListPending());
        }

        /// <summary>
        /// 验证 SLO 超时告警只发一次且条目保留（转人工）。
        /// </summary>
        [Fact]
        public async Task CompensationQueue_SloExceeded_AlertsOnceAndKeepsItem()
        {
            // Arrange
            var harness = new Harness();
            var transport = new ScriptedTransport();
            var queue = new OnlineGrantCompensationQueue(transport, harness.AlertSink, sloMilliseconds: 0);
            queue.Enqueue(BuildRemoteRequest(10001, "key-slo"));
            await Task.Delay(2);

            // Act
            var firstDrain = await queue.DrainAsync();
            var secondDrain = await queue.DrainAsync();

            // Assert
            Assert.Equal(1, firstDrain.RemainingCount);
            Assert.Equal(1, secondDrain.RemainingCount);
            var sloAlerts = harness.AlertSink.Records.Where(record => record.Kind == OnlineAssetAlertKind.CompensationSloExceeded).ToList();
            Assert.Single(sloAlerts);
            Assert.Contains("key-slo", sloAlerts[0].Detail);
            Assert.Single(queue.ListPending());
        }
    }
}
