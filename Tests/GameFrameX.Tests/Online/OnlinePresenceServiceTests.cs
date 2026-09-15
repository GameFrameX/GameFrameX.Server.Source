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
using System.Threading.Tasks;
using GameFrameX.Online.Contracts;
using GameFrameX.Online.Events;
using GameFrameX.Online.Presence;
using GameFrameX.Online.Scope;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 在线状态服务测试（vault:C3 VC-2.5/2.6/2.15/2.16：上线登记、心跳、超窗清理、风控、在线计数口径）。
    /// </summary>
    public class OnlinePresenceServiceTests
    {
        /// <summary>测试用租户标识。</summary>
        private const long TenantId = 1;

        /// <summary>测试用应用标识。</summary>
        private const long AppId = 10;

        /// <summary>测试用区服标识。</summary>
        private const long ServerId = 100;

        /// <summary>测试用玩家一标识。</summary>
        private const long PlayerOne = 1001;

        /// <summary>测试用玩家二标识。</summary>
        private const long PlayerTwo = 1002;

        /// <summary>
        /// 验证无效玩家主体位被结构性拒绝（VC-2.6：Admin 管理员连接不进入 Presence）。
        /// </summary>
        [Fact]
        public async Task SetOnlineAsync_WithoutPlayerScope_ShouldBeRejected()
        {
            // Arrange
            var service = CreateService(out var recorder);
            var scope = new OnlineScope(TenantId, AppId, ServerId, 0);

            // Act
            var outcome = await service.SetOnlineAsync(scope, "sess-a");

            // Assert
            Assert.False(outcome.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, outcome.Code);
            Assert.Equal(0, recorder.Count);
        }

        /// <summary>
        /// 验证上线登记落 Online 并发布 Offline → Online 变更事件（VC-2.16）。
        /// </summary>
        [Fact]
        public async Task SetOnlineAsync_ShouldPublishOfflineToOnlineEvent()
        {
            // Arrange
            var service = CreateService(out var recorder);
            var scope = CreatePlayerScope(PlayerOne);

            // Act
            var outcome = await service.SetOnlineAsync(scope, "sess-a");
            var state = await service.GetAsync(scope);

            // Assert
            Assert.True(outcome.IsSuccess);
            Assert.Equal(OnlinePresenceState.Online, outcome.Data);
            Assert.Equal(OnlinePresenceState.Online, state.Data);

            var changeEvents = recorder.Filter(OnlinePresenceEvents.PresenceChanged);
            var changeEvent = Assert.Single(changeEvents);
            Assert.Equal("Offline", changeEvent.PayloadAuditFields["FromState"]);
            Assert.Equal("Online", changeEvent.PayloadAuditFields["ToState"]);
            Assert.Equal("Connected", changeEvent.PayloadAuditFields["Reason"]);
        }

        /// <summary>
        /// 验证同会话重复上线幂等（刷新心跳，不重复发事件）。
        /// </summary>
        [Fact]
        public async Task SetOnlineAsync_SameSessionTwice_ShouldBeIdempotent()
        {
            // Arrange
            var service = CreateService(out var recorder);
            var scope = CreatePlayerScope(PlayerOne);

            // Act
            await service.SetOnlineAsync(scope, "sess-a");
            var second = await service.SetOnlineAsync(scope, "sess-a");

            // Assert
            Assert.True(second.IsSuccess);
            Assert.Single(recorder.Filter(OnlinePresenceEvents.PresenceChanged));
        }

        /// <summary>
        /// 验证断线进入 Reconnecting、重连成功回 Online（事件链完整）。
        /// </summary>
        [Fact]
        public async Task DisconnectThenReconnect_ShouldTransitionThroughReconnecting()
        {
            // Arrange
            var service = CreateService(out var recorder);
            var scope = CreatePlayerScope(PlayerOne);
            await service.SetOnlineAsync(scope, "sess-a");

            // Act
            var disconnected = await service.MarkDisconnectedAsync(scope, 30);
            var stateDuringWindow = await service.GetAsync(scope);
            var reconnected = await service.MarkReconnectedAsync(scope, OnlinePresenceState.Online);
            var stateAfter = await service.GetAsync(scope);

            // Assert
            Assert.True(disconnected.IsSuccess);
            Assert.Equal(OnlinePresenceState.Reconnecting, stateDuringWindow.Data);
            Assert.True(reconnected.IsSuccess);
            Assert.Equal(OnlinePresenceState.Online, stateAfter.Data);

            var changeEvents = recorder.Filter(OnlinePresenceEvents.PresenceChanged);
            Assert.Equal(3, changeEvents.Count);
            Assert.Equal("Reconnecting", changeEvents[1].PayloadAuditFields["ToState"]);
            Assert.Equal("ReconnectSucceeded", changeEvents[2].PayloadAuditFields["Reason"]);
        }

        /// <summary>
        /// 验证重连窗口超时清理转 Offline，不留永久 Reconnecting（VC-2.15）。
        /// </summary>
        [Fact]
        public async Task SweepTimeoutsAsync_ReconnectWindowExpired_ShouldRemoveRecordAndPublishOffline()
        {
            // Arrange
            var service = CreateService(out var recorder);
            var scope = CreatePlayerScope(PlayerOne);
            await service.SetOnlineAsync(scope, "sess-a");
            await service.MarkDisconnectedAsync(scope, 30);

            // Act：窗口 30 秒，基准时刻推到 31 秒后。
            var futureNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 31_000;
            var swept = await service.SweepTimeoutsAsync(TenantId, AppId, futureNow);
            var state = await service.GetAsync(scope);

            // Assert
            Assert.Equal(1, swept);
            Assert.Equal(OnlinePresenceState.Offline, state.Data);
            var lastChange = recorder.Filter(OnlinePresenceEvents.PresenceChanged)[^1];
            Assert.Equal("Reconnecting", lastChange.PayloadAuditFields["FromState"]);
            Assert.Equal("Offline", lastChange.PayloadAuditFields["ToState"]);
            Assert.Equal("ReconnectWindowExpired", lastChange.PayloadAuditFields["Reason"]);
        }

        /// <summary>
        /// 验证空闲超时清理转 Idle（默认阈值 300 秒），心跳恢复 Online。
        /// </summary>
        [Fact]
        public async Task SweepThenHeartbeat_ShouldIdleThenResumeOnline()
        {
            // Arrange
            var service = CreateService(out var recorder);
            var scope = CreatePlayerScope(PlayerOne);
            await service.SetOnlineAsync(scope, "sess-a");

            // Act：心跳推到 301 秒前（等效无操作 301 秒）。
            var futureNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 301_000;
            var swept = await service.SweepTimeoutsAsync(TenantId, AppId, futureNow);
            var idleState = await service.GetAsync(scope);
            var resumed = await service.HeartbeatAsync(scope);
            var finalState = await service.GetAsync(scope);

            // Assert
            Assert.Equal(1, swept);
            Assert.Equal(OnlinePresenceState.Idle, idleState.Data);
            Assert.True(resumed.IsSuccess);
            Assert.Equal(OnlinePresenceState.Online, resumed.Data);
            Assert.Equal(OnlinePresenceState.Online, finalState.Data);
        }

        /// <summary>
        /// 验证风控 Blocked 生命周期：受限中拒绝上线与关闭，解除后回 Offline。
        /// </summary>
        [Fact]
        public async Task BlockedLifecycle_ShouldForbidOnlineUntilReleased()
        {
            // Arrange
            var service = CreateService(out var recorder);
            var scope = CreatePlayerScope(PlayerOne);
            await service.SetOnlineAsync(scope, "sess-a");

            // Act
            var blocked = await service.MarkBlockedAsync(scope);
            var blockedState = await service.GetAsync(scope);
            var onlineWhileBlocked = await service.SetOnlineAsync(scope, "sess-b");
            var closeWhileBlocked = await service.MarkClosedAsync(scope);
            var released = await service.ReleaseBlockedAsync(scope);
            var finalState = await service.GetAsync(scope);

            // Assert
            Assert.True(blocked.IsSuccess);
            Assert.Equal(OnlinePresenceState.Blocked, blockedState.Data);
            Assert.False(onlineWhileBlocked.IsSuccess);
            Assert.Equal(OnlineErrorCode.StateOperationForbidden, onlineWhileBlocked.Code);
            Assert.False(closeWhileBlocked.IsSuccess);
            Assert.True(released.IsSuccess);
            Assert.Equal(OnlinePresenceState.Offline, finalState.Data);
        }

        /// <summary>
        /// 验证在线计数排除 Blocked（VC-2.6 计数口径）。
        /// </summary>
        [Fact]
        public async Task CountOnlineAsync_ShouldExcludeBlockedPlayers()
        {
            // Arrange
            var service = CreateService(out var _);
            await service.SetOnlineAsync(CreatePlayerScope(PlayerOne), "sess-a");
            await service.SetOnlineAsync(CreatePlayerScope(PlayerTwo), "sess-b");

            // Act
            var before = await service.CountOnlineAsync(TenantId, AppId);
            await service.MarkBlockedAsync(CreatePlayerScope(PlayerOne));
            var after = await service.CountOnlineAsync(TenantId, AppId);

            // Assert
            Assert.Equal(2, before);
            Assert.Equal(1, after);
        }

        /// <summary>
        /// 验证玩法驱动迁移走状态机判定：合法转换成功、非法转换拒绝。
        /// </summary>
        [Fact]
        public async Task SetActivityStateAsync_ShouldFollowStateMachine()
        {
            // Arrange
            var service = CreateService(out var _);
            var scope = CreatePlayerScope(PlayerOne);
            await service.SetOnlineAsync(scope, "sess-a");

            // Act
            var matching = await service.SetActivityStateAsync(scope, OnlinePresenceState.Matching);
            var inMatch = await service.SetActivityStateAsync(scope, OnlinePresenceState.InMatch);
            var backOnline = await service.SetActivityStateAsync(scope, OnlinePresenceState.Online);
            var rematching = await service.SetActivityStateAsync(scope, OnlinePresenceState.Matching);
            var illegalIdleJump = await service.SetActivityStateAsync(scope, OnlinePresenceState.Idle);

            // Assert
            Assert.True(matching.IsSuccess);
            Assert.Equal(OnlinePresenceState.Matching, matching.Data);
            Assert.True(inMatch.IsSuccess);
            Assert.True(backOnline.IsSuccess);
            Assert.True(rematching.IsSuccess);
            Assert.False(illegalIdleJump.IsSuccess);
            Assert.Equal(OnlineErrorCode.StateOperationForbidden, illegalIdleJump.Code);
        }

        /// <summary>
        /// 验证离线玩家（无记录）的玩法迁移与心跳被拒绝。
        /// </summary>
        [Fact]
        public async Task OfflinePlayer_ActivityAndHeartbeat_ShouldBeRejected()
        {
            // Arrange
            var service = CreateService(out var _);
            var scope = CreatePlayerScope(PlayerOne);

            // Act
            var activity = await service.SetActivityStateAsync(scope, OnlinePresenceState.Matching);
            var heartbeat = await service.HeartbeatAsync(scope);
            var close = await service.MarkClosedAsync(scope);

            // Assert
            Assert.False(activity.IsSuccess);
            Assert.Equal(OnlineErrorCode.StateNotReady, activity.Code);
            Assert.False(heartbeat.IsSuccess);
            Assert.Equal(OnlineErrorCode.StateNotReady, heartbeat.Code);
            Assert.True(close.IsSuccess);
        }

        /// <summary>
        /// 构造被测服务与事件记录桩。
        /// </summary>
        /// <param name="recorder">事件记录桩。</param>
        /// <returns>在线状态服务实例。</returns>
        private static OnlinePresenceService CreateService(out OnlineEventRecorder recorder)
        {
            recorder = new OnlineEventRecorder();
            return new OnlinePresenceService(new InMemoryOnlinePresenceStore(), recorder);
        }

        /// <summary>
        /// 构造玩家作用域。
        /// </summary>
        /// <param name="playerId">玩家标识。</param>
        /// <returns>作用域实例。</returns>
        private static OnlineScope CreatePlayerScope(long playerId)
        {
            return new OnlineScope(TenantId, AppId, ServerId, playerId);
        }
    }
}
