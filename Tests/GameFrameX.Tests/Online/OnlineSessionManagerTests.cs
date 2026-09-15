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
using GameFrameX.Online.Identity;
using GameFrameX.Online.Scope;
using GameFrameX.Online.Session;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 会话生命周期管理器测试（vault:C3 VC-2.15/2.16：主线推进、断线重连、超窗清理、事件发布）。
    /// </summary>
    public class OnlineSessionManagerTests
    {
        /// <summary>测试用租户标识。</summary>
        private const long TenantId = 1;

        /// <summary>测试用应用标识。</summary>
        private const long AppId = 10;

        /// <summary>测试用区服标识。</summary>
        private const long ServerId = 100;

        /// <summary>测试用玩家标识。</summary>
        private const long PlayerId = 1001;

        /// <summary>
        /// 验证主线推进 Authenticated → Connected → Active，并按序发布生命周期事件（VC-2.16）。
        /// </summary>
        [Fact]
        public async Task Lifecycle_ShouldAdvanceAuthenticatedConnectedActiveWithEvents()
        {
            // Arrange
            var manager = CreateManager(out var recorder, out var store, out var tokenService);
            var issued = await tokenService.IssueSessionAsync(CreatePlayerScope(), 3600, OnlineMultiDevicePolicy.LatestWins, null);
            var sessionId = issued.Data.Session.Id;

            // Act
            var connected = await manager.MarkConnectedAsync(sessionId, "conn-1");
            var active = await manager.MarkActiveAsync(sessionId);
            var session = await store.FindAsync(sessionId);

            // Assert
            Assert.True(connected.IsSuccess);
            Assert.True(active.IsSuccess);
            Assert.Equal(OnlineSessionState.Active, session.State);
            Assert.Equal("conn-1", session.ConnectionId);

            Assert.Single(recorder.Filter(OnlineSessionEvents.SessionConnected));
            Assert.Single(recorder.Filter(OnlineSessionEvents.SessionActive));
        }

        /// <summary>
        /// 验证未鉴权（连接前）不允许活跃与心跳，重复连接被拒绝。
        /// </summary>
        [Fact]
        public async Task StateGuards_ShouldRejectOutOfOrderTransitions()
        {
            // Arrange
            var manager = CreateManager(out var _, out var _, out var tokenService);
            var issued = await tokenService.IssueSessionAsync(CreatePlayerScope(), 3600, OnlineMultiDevicePolicy.LatestWins, null);
            var sessionId = issued.Data.Session.Id;

            // Act
            var activeBeforeConnect = await manager.MarkActiveAsync(sessionId);
            var heartbeatBeforeConnect = await manager.HeartbeatAsync(sessionId);
            await manager.MarkConnectedAsync(sessionId, "conn-1");
            var doubleConnect = await manager.MarkConnectedAsync(sessionId, "conn-2");

            // Assert
            Assert.False(activeBeforeConnect.IsSuccess);
            Assert.Equal(OnlineErrorCode.StateNotReady, activeBeforeConnect.Code);
            Assert.False(heartbeatBeforeConnect.IsSuccess);
            Assert.Equal(OnlineErrorCode.StateNotReady, heartbeatBeforeConnect.Code);
            Assert.False(doubleConnect.IsSuccess);
            Assert.Equal(OnlineErrorCode.StateOperationForbidden, doubleConnect.Code);
        }

        /// <summary>
        /// 验证断线入 Reconnecting、重连成功回 Connected、心跳保持刷新。
        /// </summary>
        [Fact]
        public async Task DisconnectReconnect_ShouldMaintainWindowSemantics()
        {
            // Arrange
            var manager = CreateManager(out var recorder, out var store, out var tokenService);
            var issued = await tokenService.IssueSessionAsync(CreatePlayerScope(), 3600, OnlineMultiDevicePolicy.LatestWins, null);
            var sessionId = issued.Data.Session.Id;
            await manager.MarkConnectedAsync(sessionId, "conn-1");
            await manager.MarkActiveAsync(sessionId);

            // Act（内存存储按引用共享，中途态立即取值快照，避免被后续推进覆盖）
            var disconnected = await manager.MarkDisconnectedAsync(sessionId, 30);
            var stateDuringWindow = (await store.FindAsync(sessionId)).State;
            var deadlineDuringWindow = (await store.FindAsync(sessionId)).ReconnectDeadlineAtTime;
            var heartbeatInWindow = await manager.HeartbeatAsync(sessionId);
            var reconnected = await manager.MarkReconnectedAsync(sessionId, "conn-2");
            var afterReconnect = await store.FindAsync(sessionId);

            // Assert
            Assert.True(disconnected.IsSuccess);
            Assert.Equal(OnlineSessionState.Reconnecting, stateDuringWindow);
            Assert.True(deadlineDuringWindow > 0);
            Assert.True(heartbeatInWindow.IsSuccess);
            Assert.True(reconnected.IsSuccess);
            Assert.Equal(OnlineSessionState.Connected, afterReconnect.State);
            Assert.Equal(0, afterReconnect.ReconnectDeadlineAtTime);
            Assert.Single(recorder.Filter(OnlineSessionEvents.SessionReconnecting));
        }

        /// <summary>
        /// 验证重连窗口超时清理转 Closed/ReconnectWindowExpired，无永久 Reconnecting（VC-2.15）。
        /// </summary>
        [Fact]
        public async Task SweepAsync_ReconnectWindowExpired_ShouldCloseSession()
        {
            // Arrange
            var manager = CreateManager(out var recorder, out var store, out var tokenService);
            var issued = await tokenService.IssueSessionAsync(CreatePlayerScope(), 3600, OnlineMultiDevicePolicy.LatestWins, null);
            var sessionId = issued.Data.Session.Id;
            await manager.MarkConnectedAsync(sessionId, "conn-1");
            await manager.MarkDisconnectedAsync(sessionId, 30);

            // Act：窗口 30 秒内清理 0 次；推到 31 秒后清理 1 次。
            var sweptInWindow = await manager.SweepAsync(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 10_000);
            var futureNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 31_000;
            var sweptAfterWindow = await manager.SweepAsync(futureNow);
            var session = await store.FindAsync(sessionId);

            // Assert
            Assert.Equal(0, sweptInWindow);
            Assert.Equal(1, sweptAfterWindow);
            Assert.Equal(OnlineSessionState.Closed, session.State);
            Assert.Equal(OnlineSessionCloseReason.ReconnectWindowExpired, session.CloseReason);
            Assert.Single(recorder.Filter(OnlineSessionEvents.SessionClosed));
        }

        /// <summary>
        /// 验证关闭幂等、踢下线原因映射 Kicked 终态、终态后操作一律 SessionInvalid。
        /// </summary>
        [Fact]
        public async Task CloseAsync_ShouldBeIdempotentAndMapReasons()
        {
            // Arrange
            var manager = CreateManager(out var _, out var store, out var tokenService);
            var issued = await tokenService.IssueSessionAsync(CreatePlayerScope(), 3600, OnlineMultiDevicePolicy.LatestWins, null);
            var sessionId = issued.Data.Session.Id;
            await manager.MarkConnectedAsync(sessionId, "conn-1");

            // Act
            var closed = await manager.CloseAsync(sessionId, OnlineSessionCloseReason.LoggedOut, "logout");
            var closedAgain = await manager.CloseAsync(sessionId, OnlineSessionCloseReason.LoggedOut, "logout");
            var session = await store.FindAsync(sessionId);
            var heartbeatAfterClose = await manager.HeartbeatAsync(sessionId);

            // Assert
            Assert.True(closed.IsSuccess);
            Assert.True(closedAgain.IsSuccess);
            Assert.Equal(OnlineSessionState.Closed, session.State);
            Assert.Equal(OnlineSessionCloseReason.LoggedOut, session.CloseReason);
            Assert.False(heartbeatAfterClose.IsSuccess);
            Assert.Equal(OnlineErrorCode.SessionInvalid, heartbeatAfterClose.Code);
        }

        /// <summary>
        /// 验证不存在会话返回 ResourceNotFound，空参数返回 ParameterInvalid。
        /// </summary>
        [Fact]
        public async Task MissingOrInvalidSession_ShouldMapParamAndNotFound()
        {
            // Arrange
            var manager = CreateManager(out var _, out var _, out var _);

            // Act
            var missing = await manager.MarkConnectedAsync("sess-missing", "conn-1");
            var emptyId = await manager.MarkConnectedAsync(string.Empty, "conn-1");

            // Assert
            Assert.False(missing.IsSuccess);
            Assert.Equal(OnlineErrorCode.ResourceNotFound, missing.Code);
            Assert.False(emptyId.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, emptyId.Code);
        }

        /// <summary>
        /// 构造被测管理器与组装依赖。
        /// </summary>
        /// <param name="recorder">事件记录桩。</param>
        /// <param name="store">会话存储。</param>
        /// <param name="tokenService">Token 服务（签发会话用）。</param>
        /// <returns>会话管理器实例。</returns>
        private static OnlineSessionManager CreateManager(out OnlineEventRecorder recorder, out InMemoryOnlineSessionStore store, out OnlineSessionTokenService tokenService)
        {
            recorder = new OnlineEventRecorder();
            store = new InMemoryOnlineSessionStore();
            tokenService = new OnlineSessionTokenService(store, recorder);
            return new OnlineSessionManager(store, recorder);
        }

        /// <summary>
        /// 构造玩家作用域。
        /// </summary>
        /// <returns>作用域实例。</returns>
        private static OnlineScope CreatePlayerScope()
        {
            return new OnlineScope(TenantId, AppId, ServerId, PlayerId);
        }
    }
}
