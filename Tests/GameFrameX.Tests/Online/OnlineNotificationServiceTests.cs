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
//   Any legal disputes or liabilities arising from secondary development based on this project
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
// ==========================================================================================

using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Online.Contracts;
using GameFrameX.Online.Events;
using GameFrameX.Online.Scope;
using GameFrameX.Online.Social;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 通知服务测试（vault:C7 S6.8/S6.9；VC-6.11 推送失败留痕与离线补发、VC-6.12 入队去重不重复消费、
    /// VC-6.13 过期收敛与丢弃、VC-6.14 推送出口异常不逃逸）。
    /// </summary>
    public class OnlineNotificationServiceTests
    {
        /// <summary>测试用租户标识。</summary>
        private const long TenantId = 1;

        /// <summary>测试用应用标识。</summary>
        private const long AppId = 10;

        /// <summary>测试用区服标识。</summary>
        private const long ServerId = 100;

        /// <summary>测试用接收者玩家标识。</summary>
        private const long ReceiverId = 2001;

        /// <summary>测试用非接收者玩家标识。</summary>
        private const long OtherReceiverId = 2002;

        /// <summary>测试用去重键。</summary>
        private const string DedupeKey = "party-invite:1001";

        /// <summary>测试用载荷正文（含可检索的敏感标记）。</summary>
        private const string PayloadBody = "{\"secret\":\"payload-body-xyz\"}";

        /// <summary>测试用载荷敏感标记（脱敏断言用）。</summary>
        private const string PayloadMarker = "payload-body-xyz";

        /// <summary>
        /// 验证同一去重键重复入队返回同一条通知，不新建也不重发（VC-6.12：入队幂等）。
        /// </summary>
        [Fact]
        public async Task EnqueueAsync_WhenSameDedupeKey_ShouldReturnExistingNotificationAndNotRedispatch()
        {
            // Arrange
            var dispatcher = new StubNotificationDispatcher
            {
                Mode = DispatchMode.Deliver,
            };
            var service = CreateService(out _, dispatcher);
            var scope = CreateReceiverScope(ReceiverId);

            // Act
            var first = await service.EnqueueAsync(scope, OnlineNotificationKind.PartyInvite, DedupeKey, PayloadBody);
            var second = await service.EnqueueAsync(scope, OnlineNotificationKind.PartyInvite, DedupeKey, PayloadBody);

            // Assert
            Assert.True(first.IsSuccess);
            Assert.True(second.IsSuccess);
            Assert.Equal(first.Data.NotificationId, second.Data.NotificationId);
            Assert.Equal(OnlineNotificationState.Delivered, first.Data.State);
            Assert.Equal(OnlineNotificationState.Delivered, second.Data.State);
            Assert.Equal(1, dispatcher.DispatchCount);

            var listed = await service.ListAsync(scope);
            Assert.True(listed.IsSuccess);
            Assert.Single(listed.Data);
            Assert.Equal(first.Data.NotificationId, listed.Data[0].NotificationId);
        }

        /// <summary>
        /// 验证未装配推送出口等价于「推送失败且可重试」：通知留在队列中待补发，不被丢弃也不据此判定玩家离线
        /// （VC-6.11：推送失败必须留痕并转入重试 / 补发）。
        /// </summary>
        [Fact]
        public async Task EnqueueAsync_WhenDispatcherMissing_ShouldKeepNotificationRetryable()
        {
            // Arrange
            var service = CreateService(out _, null);
            var scope = CreateReceiverScope(ReceiverId);

            // Act
            var outcome = await service.EnqueueAsync(scope, OnlineNotificationKind.Announcement, DedupeKey, PayloadBody);

            // Assert
            Assert.True(outcome.IsSuccess);
            Assert.Equal(OnlineNotificationState.Retrying, outcome.Data.State);
            Assert.Equal(1, outcome.Data.AttemptCount);
            Assert.Equal("未装配推送出口", outcome.Data.LastError);
            Assert.False(OnlineNotificationStateMachine.IsTerminal(outcome.Data.State));

            var listed = await service.ListAsync(scope);
            Assert.True(listed.IsSuccess);
            Assert.Single(listed.Data);
            Assert.Equal(outcome.Data.NotificationId, listed.Data[0].NotificationId);
            Assert.Equal(OnlineNotificationState.Retrying, listed.Data[0].State);
        }

        /// <summary>
        /// 验证推送成功时状态落 Delivered 并写入投递时刻，失败原因清空（VC-6.11：推送成功路径）。
        /// </summary>
        [Fact]
        public async Task EnqueueAsync_WhenDispatcherSucceeds_ShouldReachDeliveredWithDeliveredAtTime()
        {
            // Arrange
            var dispatcher = new StubNotificationDispatcher
            {
                Mode = DispatchMode.Deliver,
            };
            var service = CreateService(out _, dispatcher);

            // Act
            var outcome = await service.EnqueueAsync(CreateReceiverScope(ReceiverId), OnlineNotificationKind.MatchResult, DedupeKey, PayloadBody);

            // Assert
            Assert.True(outcome.IsSuccess);
            Assert.Equal(OnlineNotificationState.Delivered, outcome.Data.State);
            Assert.True(outcome.Data.DeliveredAtTime > 0);
            Assert.Equal(string.Empty, outcome.Data.LastError);
            Assert.Equal(1, outcome.Data.AttemptCount);
            Assert.Equal(1, dispatcher.DispatchCount);
        }

        /// <summary>
        /// 验证可重试失败落 Retrying 中间态，失败原因与尝试次数同时留痕（VC-6.11：失败留痕是补发的唯一依据）。
        /// </summary>
        [Fact]
        public async Task EnqueueAsync_WhenDispatcherFailsRetryably_ShouldReachRetryingWithLastError()
        {
            // Arrange
            var dispatcher = new StubNotificationDispatcher
            {
                Mode = DispatchMode.RetryableFail,
            };
            var service = CreateService(out _, dispatcher);

            // Act
            var outcome = await service.EnqueueAsync(CreateReceiverScope(ReceiverId), OnlineNotificationKind.AssetChanged, DedupeKey, PayloadBody);

            // Assert
            Assert.True(outcome.IsSuccess);
            Assert.Equal(OnlineNotificationState.Retrying, outcome.Data.State);
            Assert.Equal("推送出口不可达", outcome.Data.LastError);
            Assert.Equal(1, outcome.Data.AttemptCount);
            Assert.False(OnlineNotificationStateMachine.IsTerminal(outcome.Data.State));
        }

        /// <summary>
        /// 验证不可重试失败（Retryable = false）直接落 Failed 而非 Retrying，重投路径再次失败后仍为 Failed
        /// 且尝试次数继续累加（VC-6.11：不可重试失败不占用重试预算）。
        /// </summary>
        [Fact]
        public async Task EnqueueAsync_WhenDispatcherFailsNonRetryably_ShouldReachFailed()
        {
            // Arrange
            var dispatcher = new StubNotificationDispatcher
            {
                Mode = DispatchMode.NonRetryableFail,
            };
            var service = CreateService(out _, dispatcher);

            // Act
            var outcome = await service.EnqueueAsync(CreateReceiverScope(ReceiverId), OnlineNotificationKind.Mail, DedupeKey, PayloadBody);

            // Assert
            Assert.True(outcome.IsSuccess);
            Assert.Equal(OnlineNotificationState.Failed, outcome.Data.State);
            Assert.Equal("接收者不存在", outcome.Data.LastError);
            Assert.Equal(1, outcome.Data.AttemptCount);

            var retried = await service.RetryPendingAsync(TenantId, AppId, 100);
            Assert.True(retried.IsSuccess);
            Assert.Equal(1, retried.Data);

            var listed = await service.ListAsync(CreateReceiverScope(ReceiverId));
            Assert.Equal(OnlineNotificationState.Failed, listed.Data[0].State);
            Assert.Equal(2, listed.Data[0].AttemptCount);
            Assert.Equal("接收者不存在", listed.Data[0].LastError);
        }

        /// <summary>
        /// 验证推送出口抛异常或违约返回 null 时异常被捕获并转入重试路径，不得逃逸出服务（VC-6.11：信任边界）。
        /// </summary>
        [Fact]
        public async Task EnqueueAsync_WhenDispatcherThrowsOrReturnsNull_ShouldFallBackToRetry()
        {
            // Arrange：抛异常的推送出口
            var throwingDispatcher = new StubNotificationDispatcher
            {
                Mode = DispatchMode.Throw,
            };
            var throwingService = CreateService(out _, throwingDispatcher);

            // Act
            var thrown = await throwingService.EnqueueAsync(CreateReceiverScope(ReceiverId), OnlineNotificationKind.Mail, DedupeKey, PayloadBody);

            // Assert
            Assert.True(thrown.IsSuccess);
            Assert.Equal(OnlineNotificationState.Retrying, thrown.Data.State);
            Assert.Equal("推送出口抛异常", thrown.Data.LastError);

            // Arrange：违约返回 null 的推送出口
            var nullDispatcher = new StubNotificationDispatcher
            {
                Mode = DispatchMode.ReturnNull,
            };
            var nullService = CreateService(out _, nullDispatcher);

            // Act
            var nullReturned = await nullService.EnqueueAsync(CreateReceiverScope(ReceiverId), OnlineNotificationKind.Mail, DedupeKey, PayloadBody);

            // Assert
            Assert.True(nullReturned.IsSuccess);
            Assert.Equal(OnlineNotificationState.Retrying, nullReturned.Data.State);
            Assert.Equal("推送出口未返回结果", nullReturned.Data.LastError);
        }

        /// <summary>
        /// 验证离线期间入队的通知在补发时被推送成功，且已投递的通知不被补发重复推送（VC-6.11：离线补发）。
        /// </summary>
        [Fact]
        public async Task BackfillAsync_ShouldDeliverPendingNotificationOnlyOnce()
        {
            // Arrange
            var dispatcher = new StubNotificationDispatcher
            {
                Mode = DispatchMode.RetryableFail,
            };
            var service = CreateService(out _, dispatcher);
            var scope = CreateReceiverScope(ReceiverId);
            var enqueued = await service.EnqueueAsync(scope, OnlineNotificationKind.PartyInvite, DedupeKey, PayloadBody);
            Assert.Equal(OnlineNotificationState.Retrying, enqueued.Data.State);
            var dispatchedBeforeBackfill = dispatcher.DispatchCount;

            // Act：推送出口恢复后补发
            dispatcher.Mode = DispatchMode.Deliver;
            var backfilled = await service.BackfillAsync(scope);

            // Assert
            Assert.True(backfilled.IsSuccess);
            Assert.Single(backfilled.Data);
            Assert.Equal(enqueued.Data.NotificationId, backfilled.Data[0].NotificationId);
            Assert.Equal(OnlineNotificationState.Delivered, backfilled.Data[0].State);
            Assert.Equal(dispatchedBeforeBackfill + 1, dispatcher.DispatchCount);

            var listed = await service.ListAsync(scope);
            Assert.Equal(OnlineNotificationState.Delivered, listed.Data[0].State);

            // 已投递的通知不再被补发（VC-6.12：不重复消费）
            var second = await service.BackfillAsync(scope);
            Assert.True(second.IsSuccess);
            Assert.Empty(second.Data);
            Assert.Equal(dispatchedBeforeBackfill + 1, dispatcher.DispatchCount);
        }

        /// <summary>
        /// 验证重试扫描重投失败记录，尝试次数耗尽后落 Failed 并保留失败原因（VC-6.11：重试预算与终态）。
        /// </summary>
        [Fact]
        public async Task RetryPendingAsync_ShouldExhaustRetryBudgetIntoFailed()
        {
            // Arrange
            var dispatcher = new StubNotificationDispatcher
            {
                Mode = DispatchMode.RetryableFail,
            };
            var service = CreateService(out _, dispatcher, 2);
            var scope = CreateReceiverScope(ReceiverId);
            var enqueued = await service.EnqueueAsync(scope, OnlineNotificationKind.MatchSettlement, DedupeKey, PayloadBody);
            Assert.Equal(OnlineNotificationState.Retrying, enqueued.Data.State);
            Assert.Equal(1, enqueued.Data.AttemptCount);

            // Act：本轮配额给 1，只重投 Retrying 批次，一次重投即耗尽 maxAttempts
            var retried = await service.RetryPendingAsync(TenantId, AppId, 1);

            // Assert
            Assert.True(retried.IsSuccess);
            Assert.Equal(1, retried.Data);

            var listed = await service.ListAsync(scope);
            Assert.True(listed.IsSuccess);
            Assert.Single(listed.Data);
            Assert.Equal(OnlineNotificationState.Failed, listed.Data[0].State);
            Assert.Equal(2, listed.Data[0].AttemptCount);
            Assert.Equal("推送出口不可达", listed.Data[0].LastError);
        }

        /// <summary>
        /// 验证超期通知被扫描收敛为 Expired，且默认列表不再返回过期通知（VC-6.13：过期丢弃、不误导玩家）。
        /// <para>
        /// 用「入队时还有效、随后才超期」构造，把验证点锁在**扫描轮**上：入队时已超期的记录会在推送入口
        /// 就被就地终结（见 <c>EnqueueAsync_WhenAlreadyExpired_ShouldNotDispatch</c>），扫不到它。
        /// </para>
        /// </summary>
        [Fact]
        public async Task SweepExpiredAsync_ShouldExpireOverdueNotificationAndHideItFromDefaultList()
        {
            // Arrange
            var dispatcher = new StubNotificationDispatcher
            {
                Mode = DispatchMode.Deliver,
            };
            var service = CreateService(out _, dispatcher);
            var scope = CreateReceiverScope(ReceiverId);
            var expiresAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 80;
            var enqueued = await service.EnqueueAsync(scope, OnlineNotificationKind.Mail, DedupeKey, PayloadBody, expiresAt);
            Assert.Equal(expiresAt, enqueued.Data.ExpiresAtTime);
            Assert.Equal(OnlineNotificationState.Delivered, enqueued.Data.State);

            await Task.Delay(120);

            // Act
            var sweep = await service.SweepExpiredAsync(TenantId, AppId);

            // Assert
            Assert.True(sweep.IsSuccess);
            Assert.Equal(1, sweep.Data);

            var visible = await service.ListAsync(scope);
            Assert.True(visible.IsSuccess);
            Assert.Empty(visible.Data);

            var history = await service.ListAsync(scope, true);
            Assert.True(history.IsSuccess);
            Assert.Single(history.Data);
            Assert.Equal(enqueued.Data.NotificationId, history.Data[0].NotificationId);
            Assert.Equal(OnlineNotificationState.Expired, history.Data[0].State);
        }

        /// <summary>
        /// 验证已投递的通知可标记已读并落终态，重复标记已读幂等（VC-6.12：终态不重复消费）。
        /// </summary>
        [Fact]
        public async Task MarkReadAsync_WhenDelivered_ShouldReachReadOnce()
        {
            // Arrange
            var dispatcher = new StubNotificationDispatcher
            {
                Mode = DispatchMode.Deliver,
            };
            var service = CreateService(out _, dispatcher);
            var scope = CreateReceiverScope(ReceiverId);
            var enqueued = await service.EnqueueAsync(scope, OnlineNotificationKind.FriendChanged, DedupeKey, PayloadBody);
            Assert.Equal(OnlineNotificationState.Delivered, enqueued.Data.State);

            // Act
            var read = await service.MarkReadAsync(scope, enqueued.Data.NotificationId);
            var repeated = await service.MarkReadAsync(scope, enqueued.Data.NotificationId);

            // Assert
            Assert.True(read.IsSuccess);
            Assert.Equal(OnlineNotificationState.Read, read.Data.State);
            Assert.True(read.Data.ReadAtTime > 0);
            Assert.True(OnlineNotificationStateMachine.IsTerminal(read.Data.State));

            Assert.True(repeated.IsSuccess);
            Assert.Equal(OnlineNotificationState.Read, repeated.Data.State);
            Assert.Equal(read.Data.ReadAtTime, repeated.Data.ReadAtTime);
        }

        /// <summary>
        /// 验证对已过期的通知标记已读返回状态操作禁止，过期属终态不可再迁移（VC-6.13：过期是终态）。
        /// </summary>
        [Fact]
        public async Task MarkReadAsync_WhenExpired_ShouldReturnStateOperationForbidden()
        {
            // Arrange
            var service = CreateService(out _, null);
            var scope = CreateReceiverScope(ReceiverId);
            var expiredAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 1_000;
            var enqueued = await service.EnqueueAsync(scope, OnlineNotificationKind.Mail, DedupeKey, PayloadBody, expiredAt);
            var sweep = await service.SweepExpiredAsync(TenantId, AppId);
            Assert.Equal(OnlineNotificationState.Expired, enqueued.Data.State);
            Assert.Equal(0, sweep.Data);

            // Act
            var read = await service.MarkReadAsync(scope, enqueued.Data.NotificationId);

            // Assert
            Assert.False(read.IsSuccess);
            Assert.Equal(OnlineErrorCode.StateOperationForbidden, read.Code);
        }

        /// <summary>
        /// 验证推送在途期间到达的第二次推送**不会真的再推一次**（VC-6.12：不重复消费）。
        /// <para>
        /// 场景：推送出口已发出、回执未回，记录停在 <see cref="OnlineNotificationState.Queued"/>；
        /// 此时重连触发离线补发 / Admin 点重推会再次进入推送入口。若「在途」被当成幂等自环放行，
        /// 第二个调用方照样会抢到在途标记并再推一次，玩家就会收到两条同样的推送——
        /// 所以「在途」必须由状态机拦下（自环非合法边），而不是由调用方自觉。
        /// </para>
        /// </summary>
        [Fact]
        public async Task TryDispatchAsync_WhenDispatchInFlight_ShouldNotPushAgain()
        {
            // Arrange
            var dispatcher = new StubNotificationDispatcher
            {
                Mode = DispatchMode.GatedDeliver,
            };
            var service = CreateService(out _, dispatcher);
            var scope = CreateReceiverScope(ReceiverId);

            // Act：第一次推送停在出口内部（在途窗口），此后再来一次推送。
            var inFlight = service.EnqueueAsync(scope, OnlineNotificationKind.Mail, DedupeKey, PayloadBody);
            await dispatcher.Entered;

            var listed = await service.ListAsync(scope);
            Assert.True(listed.IsSuccess);
            var notificationId = Assert.Single(listed.Data).NotificationId;

            var repeated = await service.TryDispatchAsync(TenantId, AppId, ReceiverId, notificationId);

            // Assert：第二次被「已在途」拦下，没有再次调用推送出口。
            Assert.False(repeated.IsSuccess);
            Assert.Equal(OnlineErrorCode.StateOperationForbidden, repeated.Code);
            Assert.Equal(1, dispatcher.DispatchCount);

            // 拦截不得留下写入痕迹：这条记录仍属第一次推送（尝试次数没有被第二次推高）。
            var stillQueued = await service.ListAsync(scope);
            Assert.True(stillQueued.IsSuccess);
            Assert.Equal(1, Assert.Single(stillQueued.Data).AttemptCount);

            dispatcher.Release();
            var first = await inFlight;
            Assert.True(first.IsSuccess);
            Assert.Equal(OnlineNotificationState.Delivered, first.Data.State);
            Assert.Equal(1, dispatcher.DispatchCount);
        }

        /// <summary>
        /// 验证入队时已超期的通知**不推送给玩家**、直接终结为 Expired（VC-6.13：过期丢弃、不误导玩家）。
        /// <para>
        /// 这条用例锁的是推送入口的过期兜底：扫描轮是周期性的，扫描间隔内到期的记录扫不到，
        /// 若推送入口不自己判一次有效期，一条早就过期的通知（例如离线节点补发迟到的旧消息）会照常推到玩家面前。
        /// </para>
        /// </summary>
        [Fact]
        public async Task EnqueueAsync_WhenAlreadyExpired_ShouldNotDispatch()
        {
            // Arrange
            var dispatcher = new StubNotificationDispatcher
            {
                Mode = DispatchMode.Deliver,
            };
            var service = CreateService(out var recorder, dispatcher);
            var scope = CreateReceiverScope(ReceiverId);
            var expiredAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 1_000;

            // Act
            var enqueued = await service.EnqueueAsync(scope, OnlineNotificationKind.Mail, DedupeKey, PayloadBody, expiredAt);

            // Assert
            Assert.True(enqueued.IsSuccess);
            Assert.Equal(OnlineNotificationState.Expired, enqueued.Data.State);
            Assert.Equal(0, dispatcher.DispatchCount);
            Assert.True(OnlineNotificationStateMachine.IsTerminal(enqueued.Data.State));

            // 过期通知不得出现在玩家可见列表里（VC-6.13）。
            var visible = await service.ListAsync(scope);
            Assert.True(visible.IsSuccess);
            Assert.Empty(visible.Data);

            // 就地终结同样外发一次状态变更事件（与扫描轮同源），故此处至少有一条 Changed 事件。
            Assert.NotEmpty(recorder.Events);
        }

        /// <summary>
        /// 验证未投递通知不得标记已读（状态未准备），非接收者按反预言返回未找到，空标识按参数非法拒绝
        /// （VC-6.12：已读是投递后的状态，不得越权窥探他人通知）。
        /// </summary>
        [Fact]
        public async Task MarkReadAsync_WhenNotDeliveredOrNotReceiver_ShouldBeRejected()
        {
            // Arrange
            var service = CreateService(out _, null);
            var scope = CreateReceiverScope(ReceiverId);
            var enqueued = await service.EnqueueAsync(scope, OnlineNotificationKind.Mail, DedupeKey, PayloadBody);
            Assert.Equal(OnlineNotificationState.Retrying, enqueued.Data.State);

            // Act
            var notDelivered = await service.MarkReadAsync(scope, enqueued.Data.NotificationId);
            var stranger = await service.MarkReadAsync(CreateReceiverScope(OtherReceiverId), enqueued.Data.NotificationId);
            var blankId = await service.MarkReadAsync(scope, string.Empty);

            // Assert
            Assert.False(notDelivered.IsSuccess);
            Assert.Equal(OnlineErrorCode.StateNotReady, notDelivered.Code);

            Assert.False(stranger.IsSuccess);
            Assert.Equal(OnlineErrorCode.ResourceNotFound, stranger.Code);

            Assert.False(blankId.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, blankId.Code);
        }

        /// <summary>
        /// 验证通知状态机固化终态与合法边：Read / Expired 为终态（无出边），表外边与自环一律拒绝，
        /// Failed → Queued 保留为合法边（VC-6.12：重投有处落址，补发仍可收敛）。
        /// </summary>
        [Fact]
        public void OnlineNotificationStateMachine_ShouldFreezeTerminalStatesAndLegalEdges()
        {
            // Act / Assert：终态集合（Read / Expired 无出边）
            Assert.True(OnlineNotificationStateMachine.IsTerminal(OnlineNotificationState.Read));
            Assert.True(OnlineNotificationStateMachine.IsTerminal(OnlineNotificationState.Expired));
            Assert.False(OnlineNotificationStateMachine.IsTerminal(OnlineNotificationState.Created));
            Assert.False(OnlineNotificationStateMachine.IsTerminal(OnlineNotificationState.Queued));
            Assert.False(OnlineNotificationStateMachine.IsTerminal(OnlineNotificationState.Delivered));
            Assert.False(OnlineNotificationStateMachine.IsTerminal(OnlineNotificationState.Retrying));
            Assert.False(OnlineNotificationStateMachine.IsTerminal(OnlineNotificationState.Failed));

            // Act / Assert：合法边
            Assert.True(OnlineNotificationStateMachine.TryTransition(OnlineNotificationState.Created, OnlineNotificationState.Queued));
            Assert.True(OnlineNotificationStateMachine.TryTransition(OnlineNotificationState.Queued, OnlineNotificationState.Delivered));
            Assert.True(OnlineNotificationStateMachine.TryTransition(OnlineNotificationState.Queued, OnlineNotificationState.Retrying));
            Assert.True(OnlineNotificationStateMachine.TryTransition(OnlineNotificationState.Delivered, OnlineNotificationState.Read));
            Assert.True(OnlineNotificationStateMachine.TryTransition(OnlineNotificationState.Failed, OnlineNotificationState.Queued));

            // Act / Assert：表外边与自环一律拒绝
            Assert.False(OnlineNotificationStateMachine.TryTransition(OnlineNotificationState.Created, OnlineNotificationState.Delivered));
            Assert.False(OnlineNotificationStateMachine.TryTransition(OnlineNotificationState.Queued, OnlineNotificationState.Queued));
            Assert.False(OnlineNotificationStateMachine.TryTransition(OnlineNotificationState.Read, OnlineNotificationState.Delivered));
            Assert.False(OnlineNotificationStateMachine.TryTransition(OnlineNotificationState.Expired, OnlineNotificationState.Created));
        }

        /// <summary>
        /// 验证入队与状态落定都发出 Online.Notification.Changed，信封归属接收者，且载荷与审计字段都不含
        /// 通知正文与去重键（VC-6.11 证据链 / 脱敏红线）。
        /// </summary>
        [Fact]
        public async Task EnqueueAsync_ShouldPublishNotificationChangedEventsWithoutPayloadOrDedupeKey()
        {
            // Arrange
            var service = CreateService(out var recorder, null);
            var scope = CreateReceiverScope(ReceiverId);

            // Act
            var outcome = await service.EnqueueAsync(scope, OnlineNotificationKind.Mail, DedupeKey, PayloadBody);

            // Assert
            Assert.True(outcome.IsSuccess);

            var changes = recorder.Filter(OnlineNotificationEvents.NotificationChanged);
            Assert.Equal(3, changes.Count);
            Assert.Equal("Created", changes[0].PayloadAuditFields["State"]);
            Assert.Equal("Queued", changes[1].PayloadAuditFields["State"]);
            Assert.Equal("Retrying", changes[2].PayloadAuditFields["State"]);

            foreach (var change in changes)
            {
                Assert.Equal(OnlineNotificationEvents.NotificationChanged, change.EventType);
                Assert.Equal(OnlineNotificationEvents.Source, change.Source);
                Assert.Equal(ReceiverId, change.PlayerId);
                Assert.Equal(ServerId, change.ServerId);
                Assert.Equal(outcome.Data.NotificationId, change.PayloadAuditFields["NotificationId"]);

                // 脱敏：审计字段不含正文与去重键
                Assert.False(change.PayloadAuditFields.ContainsKey("Payload"));
                Assert.False(change.PayloadAuditFields.ContainsKey("DedupeKey"));

                var json = Encoding.UTF8.GetString(change.Payload.ToArray());
                Assert.DoesNotContain(PayloadMarker, json);
                Assert.DoesNotContain(DedupeKey, json);
            }
        }

        /// <summary>
        /// 创建被测服务。
        /// </summary>
        /// <param name="recorder">事件记录桩。</param>
        /// <param name="dispatcher">推送出口桩（可为空，表示未装配推送出口）。</param>
        /// <param name="maxAttempts">最大推送尝试次数（含首次）。</param>
        /// <returns>通知服务实例。</returns>
        private static OnlineNotificationService CreateService(out OnlineEventRecorder recorder, IOnlineNotificationDispatcher dispatcher, int maxAttempts = 5)
        {
            recorder = new OnlineEventRecorder();
            return new OnlineNotificationService(new InMemoryOnlineNotificationStore(), recorder, dispatcher, maxAttempts);
        }

        /// <summary>
        /// 构造接收者作用域（接收者即作用域玩家）。
        /// </summary>
        /// <param name="playerId">接收者玩家标识。</param>
        /// <returns>作用域。</returns>
        private static OnlineScope CreateReceiverScope(long playerId)
        {
            return new OnlineScope(TenantId, AppId, ServerId, playerId);
        }

        /// <summary>推送出口桩的行为模式。</summary>
        private enum DispatchMode
        {
            /// <summary>推送成功。</summary>
            Deliver = 0,

            /// <summary>推送失败且可重试。</summary>
            RetryableFail = 1,

            /// <summary>推送失败且不可重试。</summary>
            NonRetryableFail = 2,

            /// <summary>推送出口抛异常（传输失败的显式表达）。</summary>
            Throw = 3,

            /// <summary>推送出口违约返回 null。</summary>
            ReturnNull = 4,

            /// <summary>推送挂起直到用例放行（构造「推送在途」窗口，用于并发重投用例）。</summary>
            GatedDeliver = 5,
        }

        /// <summary>
        /// 通知推送出口桩：按模式返回回执并记录推送调用次数。
        /// </summary>
        private sealed class StubNotificationDispatcher : IOnlineNotificationDispatcher
        {
            /// <summary>推送调用次数。</summary>
            private int _dispatchCount;

            /// <summary>「进入推送出口」信号（闸门模式用，让用例能观察到推送确实已在途）。</summary>
            private readonly TaskCompletionSource<bool> _entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>「放行推送出口」闸门（闸门模式用）。</summary>
            private readonly TaskCompletionSource<bool> _release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>获取一个任务，在推送出口被调用时完成（仅在闸门模式下有意义）。</summary>
            public Task Entered
            {
                get
                {
                    return _entered.Task;
                }
            }

            /// <summary>获取或设置本次推送的行为模式。</summary>
            public DispatchMode Mode
            {
                get;
                set;
            }

            /// <summary>获取推送调用次数。</summary>
            public int DispatchCount
            {
                get
                {
                    return _dispatchCount;
                }
            }

            /// <summary>放行闸门模式下被挂起的推送出口。</summary>
            public void Release()
            {
                _release.TrySetResult(true);
            }

            /// <summary>
            /// 按当前模式返回推送回执。
            /// </summary>
            /// <param name="notification">待推送的通知快照。</param>
            /// <param name="cancellationToken">取消令牌。</param>
            /// <returns>推送回执。</returns>
            public async Task<OnlineNotificationDispatchOutcome> DispatchAsync(OnlineNotification notification, CancellationToken cancellationToken = default)
            {
                System.Threading.Interlocked.Increment(ref _dispatchCount);
                switch (Mode)
                {
                    case DispatchMode.Deliver:
                        return OnlineNotificationDispatchOutcome.Deliver();
                    case DispatchMode.RetryableFail:
                        return OnlineNotificationDispatchOutcome.Fail("推送出口不可达", true);
                    case DispatchMode.NonRetryableFail:
                        return OnlineNotificationDispatchOutcome.Fail("接收者不存在", false);
                    case DispatchMode.ReturnNull:
                        return null;
                    case DispatchMode.GatedDeliver:
                        // 停在推送出口内部，模拟「推送已发出、回执还没回来」的在途窗口。
                        _entered.TrySetResult(true);
                        await _release.Task.ConfigureAwait(false);
                        return OnlineNotificationDispatchOutcome.Deliver();
                    default:
                        throw new InvalidOperationException("推送出口抛异常");
                }
            }
        }
    }
}
