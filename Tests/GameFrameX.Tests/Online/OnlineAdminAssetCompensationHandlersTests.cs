// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人及其他合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others that are prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Foundation.Idempotency;
using GameFrameX.Capability.Hosting.AdminApi;
using GameFrameX.Capability.Online.Assets;
using GameFrameX.Capability.Online.Assets.AdminApi;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Events;
using GameFrameX.Capability.Online.Abstractions.Idempotency;
using GameFrameX.Capability.Online.Abstractions.Scope;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 资产域补偿队列 Admin 处理器测试（快照观测：未装配降级 / 转人工事实呈现；
    /// 裁决：Retry 回队、Discard 留痕、非法 Action / 未知条目 / 未装配拒绝）。
    /// </summary>
    public class OnlineAdminAssetCompensationHandlersTests
    {
        /// <summary>
        /// 业务性失败传输（固定返回确定业务性失败答复——转人工触发器）。
        /// </summary>
        private sealed class BusinessFailureTransport : IOnlineCrossServerGrantTransport
        {
            public Task<OnlineResult<OnlineGrantResult>> DeliverAsync(long targetServerId, OnlineGrantRequest request, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(OnlineResult<OnlineGrantResult>.Fail(OnlineErrorCode.StateOperationForbidden, "玩家不存在（业务性失败）"));
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
        /// 空审计读面（补偿观测/裁决不经审计面）。
        /// </summary>
        private sealed class NullAuditReader : IOnlineAuditReader
        {
            public Task<OnlineResult<OnlineAuditPage>> QueryAsync(OnlineScope scope, OnlineAuditQuery query = null, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(OnlineResult<OnlineAuditPage>.Ok(new OnlineAuditPage(new OnlineAuditRecord[0], null)));
            }
        }

        /// <summary>
        /// 步进时钟（幂等协调器依赖）。
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
        /// 构造处理器（可空注入补偿队列）。
        /// </summary>
        private static OnlineAdminAssetHandlers CreateHandlers(OnlineGrantCompensationQueue compensationQueue)
        {
            var alertSink = new CollectingAlertSink();
            var idempotency = new OnlineIdempotencyService(new IdempotencyCoordinator(new InMemoryIdempotencyStore(), new StepClock(), new IdempotencyOptions()), null);
            var grantService = new OnlineGrantService(new InMemoryOnlineAssetStore(), new InMemoryOnlineAssetTransactionStore(), idempotency, new NullPublisher(), alertSink);
            var assetQuery = new OnlineAssetQueryService(new InMemoryOnlineAssetStore(), new InMemoryOnlineAssetTransactionStore());
            return new OnlineAdminAssetHandlers(grantService, assetQuery, new NullAuditReader(), new InMemoryOnlineAssetTransactionStore(), compensationQueue);
        }

        /// <summary>
        /// 构造跨服请求（归属服 200）。
        /// </summary>
        private static OnlineGrantRequest BuildRemoteRequest(string key)
        {
            var scope = new OnlineScope(1, 10, 100, 10001);
            return new OnlineGrantRequest(scope, OnlineAssetChangeSource.MatchReward, OnlineGrantOperation.Grant, "跨服赛果", "bo-" + key, new[] { new OnlineAssetChangeLine(OnlineAssetKind.Currency, "gold", 100) }, key) { HomeServerId = 200 };
        }

        /// <summary>
        /// 验证未装配补偿队列：查询降级 QueueConfigured=false 空快照，裁决拒绝（StateOperationForbidden）。
        /// </summary>
        [Fact]
        public async Task QueryAndResolve_WithoutQueue_DegradesAndRejects()
        {
            // Arrange
            var handlers = CreateHandlers(null);
            var scope = new OnlineScope(1, 10, 100, 10001);

            // Act
            var snapshot = (OnlineAdminAssetHandlers.CompensationQueueResponse)await handlers.QueryCompensationQueueAsync(new AdminApiRequest("query_compensation_queue", "{}"), scope, CancellationToken.None);

            // Assert
            Assert.False(snapshot.QueueConfigured);
            Assert.Equal(0, snapshot.PendingCount);
            Assert.Equal(0, snapshot.ManualReviewCount);
            Assert.Empty(snapshot.Items);

            var exception = await Assert.ThrowsAsync<OnlineServiceException>(() => handlers.ResolveCompensationManualReviewAsync(
                new AdminApiRequest("resolve_compensation_manual_review", "{\"IdempotencyKey\":\"key-x\",\"Action\":\"Retry\",\"Reason\":\"数据已修复\"}"), scope, CancellationToken.None));
            Assert.Equal(OnlineErrorCode.StateOperationForbidden, exception.Code);
        }

        /// <summary>
        /// 验证查询呈现转人工事实：状态 / 错误码 / 失败描述 / 归属服 / 幂等键与计数。
        /// </summary>
        [Fact]
        public async Task QueryCompensationQueue_WithManualReviewEntry_ListsFailureFacts()
        {
            // Arrange
            var alertSink = new CollectingAlertSink();
            var queue = new OnlineGrantCompensationQueue(new BusinessFailureTransport(), alertSink);
            var handlers = CreateHandlers(queue);
            queue.Enqueue(BuildRemoteRequest("key-admin-view"));
            await queue.DrainAsync();

            // Act
            var snapshot = (OnlineAdminAssetHandlers.CompensationQueueResponse)await handlers.QueryCompensationQueueAsync(new AdminApiRequest("query_compensation_queue", "{}"), new OnlineScope(1, 10, 100, 10001), CancellationToken.None);

            // Assert
            Assert.True(snapshot.QueueConfigured);
            Assert.Equal(0, snapshot.PendingCount);
            Assert.Equal(1, snapshot.ManualReviewCount);
            var entry = Assert.Single(snapshot.Items);
            Assert.Equal("key-admin-view", entry.IdempotencyKey);
            Assert.Equal(200, entry.HomeServerId);
            Assert.Equal("10001", entry.PlayerId);
            Assert.Equal(OnlineGrantCompensationItemState.ManualReview.ToString(), entry.State);
            Assert.Equal(OnlineErrorCode.StateOperationForbidden.ToString(), entry.ManualReviewErrorCode);
            Assert.Contains("玩家不存在", entry.ManualReviewMessage);
            Assert.True(entry.ManualReviewAt > 0);
        }

        /// <summary>
        /// 验证 Retry 裁决：回执 Pending 且条目回待续投；非法 Action / 未知幂等键拒绝。
        /// </summary>
        [Fact]
        public async Task ResolveCompensationManualReview_Retry_RequeuesEntry()
        {
            // Arrange
            var queue = new OnlineGrantCompensationQueue(new BusinessFailureTransport(), new CollectingAlertSink());
            var handlers = CreateHandlers(queue);
            queue.Enqueue(BuildRemoteRequest("key-admin-retry"));
            await queue.DrainAsync();
            var scope = new OnlineScope(1, 10, 100, 10001);

            // Act
            var ack = (OnlineAdminAssetHandlers.CompensationResolveResponse)await handlers.ResolveCompensationManualReviewAsync(
                new AdminApiRequest("resolve_compensation_manual_review", "{\"IdempotencyKey\":\"key-admin-retry\",\"Action\":\"Retry\",\"Reason\":\"归属服数据已修复\"}"), scope, CancellationToken.None);

            // Assert
            Assert.Equal("key-admin-retry", ack.IdempotencyKey);
            Assert.Equal(OnlineGrantCompensationItemState.Pending.ToString(), ack.State);
            Assert.Empty(queue.ListManualReview());
            var pending = Assert.Single(queue.ListPending());
            Assert.Equal(OnlineGrantCompensationItemState.Pending, pending.State);

            var invalidAction = await Assert.ThrowsAsync<OnlineServiceException>(() => handlers.ResolveCompensationManualReviewAsync(
                new AdminApiRequest("resolve_compensation_manual_review", "{\"IdempotencyKey\":\"key-admin-retry\",\"Action\":\"Force\",\"Reason\":\"x\"}"), scope, CancellationToken.None));
            Assert.Equal(OnlineErrorCode.ParameterInvalid, invalidAction.Code);

            var unknownKey = await Assert.ThrowsAsync<OnlineServiceException>(() => handlers.ResolveCompensationManualReviewAsync(
                new AdminApiRequest("resolve_compensation_manual_review", "{\"IdempotencyKey\":\"nope\",\"Action\":\"Retry\",\"Reason\":\"x\"}"), scope, CancellationToken.None));
            Assert.Equal(OnlineErrorCode.ResourceNotFound, unknownKey.Code);
        }

        /// <summary>
        /// 验证 Discard 裁决：回执 Discarded、条目移除并留 CompensationManualReviewDiscarded 告警（原因入文）。
        /// </summary>
        [Fact]
        public async Task ResolveCompensationManualReview_Discard_RemovesEntryAndKeepsTrace()
        {
            // Arrange
            var alertSink = new CollectingAlertSink();
            var queue = new OnlineGrantCompensationQueue(new BusinessFailureTransport(), alertSink);
            var handlers = CreateHandlers(queue);
            queue.Enqueue(BuildRemoteRequest("key-admin-discard"));
            await queue.DrainAsync();

            // Act
            var ack = (OnlineAdminAssetHandlers.CompensationResolveResponse)await handlers.ResolveCompensationManualReviewAsync(
                new AdminApiRequest("resolve_compensation_manual_review", "{\"IdempotencyKey\":\"key-admin-discard\",\"Action\":\"Discard\",\"Reason\":\"线下已补偿\"}"),
                new OnlineScope(1, 10, 100, 10001), CancellationToken.None);

            // Assert
            Assert.Equal("Discarded", ack.State);
            Assert.Empty(queue.ListPending());
            Assert.Empty(queue.ListManualReview());
            var discardAlerts = alertSink.Records.Where(record => record.Kind == OnlineAssetAlertKind.CompensationManualReviewDiscarded).ToList();
            Assert.Single(discardAlerts);
            Assert.Contains("key-admin-discard", discardAlerts[0].Detail);
            Assert.Contains("线下已补偿", discardAlerts[0].Detail);
        }
    }
}
