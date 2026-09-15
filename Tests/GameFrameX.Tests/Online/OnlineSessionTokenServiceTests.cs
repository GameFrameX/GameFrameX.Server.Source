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
using GameFrameX.Online.Tokens;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 会话 Token 服务测试（vault:C3 VC-2.2/2.3/2.4/2.12：原子轮换、重放拒绝、踢下线审计、多端策略）。
    /// </summary>
    public class OnlineSessionTokenServiceTests
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
        /// 验证签发即建会话：状态 Authenticated、代数 1，Token 可校验通过。
        /// </summary>
        [Fact]
        public async Task IssueSessionAsync_ShouldCreateAuthenticatedSessionWithValidToken()
        {
            // Arrange
            var service = CreateService(out var _);
            var scope = CreatePlayerScope();

            // Act
            var outcome = await service.IssueSessionAsync(scope, 3600, OnlineMultiDevicePolicy.LatestWins, "device-a");
            var validation = await service.ValidateTokenAsync(outcome.Data.Token);

            // Assert
            Assert.True(outcome.IsSuccess);
            Assert.Equal(OnlineSessionState.Authenticated, outcome.Data.Session.State);
            Assert.Equal(1, outcome.Data.Session.TokenGeneration);
            Assert.StartsWith("otk-", outcome.Data.Token);
            Assert.True(validation.IsSuccess);
            Assert.Equal(outcome.Data.Session.Id, validation.Data.Id);
        }

        /// <summary>
        /// 验证缺玩家主体位与非法有效期被拒绝。
        /// </summary>
        [Fact]
        public async Task IssueSessionAsync_InvalidScopeOrTtl_ShouldBeRejected()
        {
            // Arrange
            var service = CreateService(out var _);

            // Act
            var noPlayer = await service.IssueSessionAsync(new OnlineScope(TenantId, AppId, ServerId), 3600, OnlineMultiDevicePolicy.LatestWins, null);
            var badTtl = await service.IssueSessionAsync(CreatePlayerScope(), 0, OnlineMultiDevicePolicy.LatestWins, null);

            // Assert
            Assert.False(noPlayer.IsSuccess);
            Assert.Equal(OnlineErrorCode.ScopeMissing, noPlayer.Code);
            Assert.False(badTtl.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, badTtl.Code);
        }

        /// <summary>
        /// 验证刷新为原子轮换：新 Token 可用，旧 Token 即刻失效（VC-2.2）。
        /// </summary>
        [Fact]
        public async Task RefreshAsync_ShouldRotateTokenAndInvalidateOldOne()
        {
            // Arrange
            var service = CreateService(out var _);
            var scope = CreatePlayerScope();
            var issued = await service.IssueSessionAsync(scope, 3600, OnlineMultiDevicePolicy.LatestWins, null);

            // Act
            var refreshed = await service.RefreshSessionTokenAsync(scope, issued.Data.Token);
            var oldValidation = await service.ValidateTokenAsync(issued.Data.Token);
            var newValidation = await service.ValidateTokenAsync(refreshed.Data.Token);

            // Assert
            Assert.True(refreshed.IsSuccess);
            Assert.NotEqual(issued.Data.Token, refreshed.Data.Token);
            Assert.False(oldValidation.IsSuccess);
            Assert.Equal(OnlineErrorCode.TokenRevoked, oldValidation.Code);
            Assert.True(newValidation.IsSuccess);
            Assert.Equal(2, newValidation.Data.TokenGeneration);
        }

        /// <summary>
        /// 验证重放已轮换的旧 Token 判 TokenRevoked（VC-2.12；契约通道抛段位化异常）。
        /// </summary>
        [Fact]
        public async Task RefreshAsync_ReplayRotatedToken_ShouldThrowTokenRevoked()
        {
            // Arrange
            var service = CreateService(out var _);
            var scope = CreatePlayerScope();
            var issued = await service.IssueSessionAsync(scope, 3600, OnlineMultiDevicePolicy.LatestWins, null);
            var firstRefresh = await service.RefreshSessionTokenAsync(scope, issued.Data.Token);
            Assert.True(firstRefresh.IsSuccess);

            // Act
            var replayOutcome = await service.RefreshSessionTokenAsync(scope, issued.Data.Token);
            var contractException = await Assert.ThrowsAsync<OnlineServiceException>(() => service.RefreshAsync(new OnlineTokenRefreshRequest
            {
                Scope = scope,
                Token = issued.Data.Token,
            }));

            // Assert
            Assert.False(replayOutcome.IsSuccess);
            Assert.Equal(OnlineErrorCode.TokenRevoked, replayOutcome.Code);
            Assert.Equal(OnlineErrorCode.TokenRevoked, contractException.Code);
        }

        /// <summary>
        /// 验证踢下线后旧 Token 失效，且审计事件含 SessionId 与 Reason（VC-2.3）。
        /// </summary>
        [Fact]
        public async Task KickAsync_ShouldInvalidateTokenAndAuditSessionAndReason()
        {
            // Arrange
            var service = CreateService(out var recorder, out var store);
            var scope = CreatePlayerScope();
            var issued = await service.IssueSessionAsync(scope, 3600, OnlineMultiDevicePolicy.LatestWins, null);

            // Act
            var kickResult = await service.KickAsync(new OnlineTokenKickRequest
            {
                Scope = scope,
                SessionId = issued.Data.Session.Id,
                Reason = "operator:admin-001",
            });
            var oldValidation = await service.ValidateTokenAsync(issued.Data.Token);
            var session = await store.FindAsync(issued.Data.Session.Id);

            // Assert
            Assert.True(kickResult.Kicked);
            Assert.False(oldValidation.IsSuccess);
            Assert.Equal(OnlineSessionState.Kicked, session.State);

            var kickEvents = recorder.Filter(OnlineSessionEvents.SessionKicked);
            var kickEvent = Assert.Single(kickEvents);
            Assert.Equal(issued.Data.Session.Id, kickEvent.PayloadAuditFields["SessionId"]);
            Assert.Equal("operator:admin-001", kickEvent.PayloadAuditFields["Reason"]);
        }

        /// <summary>
        /// 验证踢下线与吊销对不存在会话幂等（false 不报错）。
        /// </summary>
        [Fact]
        public async Task KickAndRevoke_OnMissingSession_ShouldReturnFalse()
        {
            // Arrange
            var service = CreateService(out var _);
            var scope = CreatePlayerScope();

            // Act
            var kick = await service.KickAsync(new OnlineTokenKickRequest { Scope = scope, SessionId = "sess-missing" });
            var revoke = await service.RevokeAsync(new OnlineTokenRevokeRequest { Scope = scope, SessionId = "sess-missing" });

            // Assert
            Assert.False(kick.Kicked);
            Assert.False(revoke.Revoked);
        }

        /// <summary>
        /// 验证吊销后 Token 失效、会话转 Closed/Revoked。
        /// </summary>
        [Fact]
        public async Task RevokeAsync_ShouldCloseSessionAndInvalidateToken()
        {
            // Arrange
            var service = CreateService(out var _);
            var scope = CreatePlayerScope();
            var issued = await service.IssueSessionAsync(scope, 3600, OnlineMultiDevicePolicy.LatestWins, null);

            // Act
            var revokeResult = await service.RevokeAsync(new OnlineTokenRevokeRequest
            {
                Scope = scope,
                SessionId = issued.Data.Session.Id,
                Reason = "logout",
            });
            var validation = await service.ValidateTokenAsync(issued.Data.Token);

            // Assert
            Assert.True(revokeResult.Revoked);
            Assert.False(validation.IsSuccess);
            Assert.Equal(OnlineSessionState.Closed, issued.Data.Session.State);
            Assert.Equal(OnlineSessionCloseReason.Revoked, issued.Data.Session.CloseReason);
        }

        /// <summary>
        /// 验证跨租户/跨区服刷新被拒绝（作用域一致性）。
        /// </summary>
        [Fact]
        public async Task RefreshAsync_ScopeMismatch_ShouldBeDenied()
        {
            // Arrange
            var service = CreateService(out var _);
            var scope = CreatePlayerScope();
            var issued = await service.IssueSessionAsync(scope, 3600, OnlineMultiDevicePolicy.LatestWins, null);

            // Act
            var crossTenant = await service.RefreshSessionTokenAsync(new OnlineScope(TenantId + 1, AppId, ServerId, PlayerId), issued.Data.Token);
            var crossServer = await service.RefreshSessionTokenAsync(new OnlineScope(TenantId, AppId, ServerId + 1, PlayerId), issued.Data.Token);

            // Assert
            Assert.False(crossTenant.IsSuccess);
            Assert.Equal(OnlineErrorCode.CrossTenantDenied, crossTenant.Code);
            Assert.False(crossServer.IsSuccess);
            Assert.Equal(OnlineErrorCode.ServerScopeDenied, crossServer.Code);
        }

        /// <summary>
        /// 验证 SingleDevice 策略拒绝第二次登录（VC-2.4）。
        /// </summary>
        [Fact]
        public async Task IssueSessionAsync_SingleDevice_ShouldRejectSecondLogin()
        {
            // Arrange
            var service = CreateService(out var _);
            var scope = CreatePlayerScope();
            var first = await service.IssueSessionAsync(scope, 3600, OnlineMultiDevicePolicy.SingleDevice, "device-a");

            // Act
            var second = await service.IssueSessionAsync(scope, 3600, OnlineMultiDevicePolicy.SingleDevice, "device-b");

            // Assert
            Assert.True(first.IsSuccess);
            Assert.False(second.IsSuccess);
            Assert.Equal(OnlineErrorCode.StateOperationForbidden, second.Code);
        }

        /// <summary>
        /// 验证 LatestWins 策略顶替旧会话（旧 Token 失效、被顶替清单可见），Coexist 策略并存（VC-2.4）。
        /// </summary>
        [Fact]
        public async Task IssueSessionAsync_LatestWinsAndCoexist_ShouldAdjudicateByPolicy()
        {
            // Arrange
            var latestWinsService = CreateService(out var _);
            var scope = CreatePlayerScope();
            var first = await latestWinsService.IssueSessionAsync(scope, 3600, OnlineMultiDevicePolicy.LatestWins, "device-a");

            // Act（LatestWins）
            var second = await latestWinsService.IssueSessionAsync(scope, 3600, OnlineMultiDevicePolicy.LatestWins, "device-b");
            var oldValidation = await latestWinsService.ValidateTokenAsync(first.Data.Token);
            var newValidation = await latestWinsService.ValidateTokenAsync(second.Data.Token);

            // Assert（LatestWins）
            Assert.True(second.IsSuccess);
            Assert.Equal(OnlineSessionState.Kicked, first.Data.Session.State);
            Assert.Equal(OnlineSessionCloseReason.ReplacedByNewSession, first.Data.Session.CloseReason);
            Assert.Single(second.Data.ReplacedSessionIds, first.Data.Session.Id);
            Assert.False(oldValidation.IsSuccess);
            Assert.True(newValidation.IsSuccess);

            // Act（Coexist）
            var coexistService = CreateService(out var _);
            var coexistFirst = await coexistService.IssueSessionAsync(scope, 3600, OnlineMultiDevicePolicy.Coexist, "device-a");
            var coexistSecond = await coexistService.IssueSessionAsync(scope, 3600, OnlineMultiDevicePolicy.Coexist, "device-b");

            // Assert（Coexist）
            Assert.True(coexistFirst.IsSuccess);
            Assert.True(coexistSecond.IsSuccess);
            Assert.Empty(coexistSecond.Data.ReplacedSessionIds);
            Assert.True((await coexistService.ValidateTokenAsync(coexistFirst.Data.Token)).IsSuccess);
            Assert.True((await coexistService.ValidateTokenAsync(coexistSecond.Data.Token)).IsSuccess);
        }

        /// <summary>
        /// 验证 C93 契约签发入口返回完整 DTO（Token/SessionId/ExpiresAtTime）。
        /// </summary>
        [Fact]
        public async Task IssueAsync_ContractAdapter_ShouldReturnDto()
        {
            // Arrange
            var service = CreateService(out var _);

            // Act
            var result = await service.IssueAsync(new OnlineTokenIssueRequest
            {
                Scope = CreatePlayerScope(),
                TimeToLiveSeconds = 120,
            });
            var validation = await service.ValidateTokenAsync(result.Token);

            // Assert
            Assert.StartsWith("sess-", result.SessionId);
            Assert.True(validation.IsSuccess);
            Assert.True(result.ExpiresAtTime > DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }

        /// <summary>
        /// 验证过期清理：Token 到期的非终态会话转 Expired（VC-2.14 兜底）。
        /// </summary>
        [Fact]
        public async Task SweepExpiredAsync_ShouldExpireSessionsPastTokenLifetime()
        {
            // Arrange
            var service = CreateService(out var recorder);
            var scope = CreatePlayerScope();
            var issued = await service.IssueSessionAsync(scope, 1, OnlineMultiDevicePolicy.LatestWins, null);

            // Act：有效期 1 秒，基准时刻推到 2 秒后。
            var futureNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 2_000;
            var swept = await service.SweepExpiredAsync(futureNow);
            var validation = await service.ValidateTokenAsync(issued.Data.Token);

            // Assert
            Assert.Equal(1, swept);
            Assert.Equal(OnlineSessionState.Expired, issued.Data.Session.State);
            Assert.False(validation.IsSuccess);
            Assert.Single(recorder.Filter(OnlineSessionEvents.SessionExpired));
        }

        /// <summary>
        /// 构造被测服务与事件记录桩。
        /// </summary>
        /// <param name="recorder">事件记录桩。</param>
        /// <returns>Token 服务实例。</returns>
        private static OnlineSessionTokenService CreateService(out OnlineEventRecorder recorder)
        {
            return CreateService(out recorder, out var _);
        }

        /// <summary>
        /// 构造被测服务、事件记录桩与共享存储（终态落库断言用）。
        /// </summary>
        /// <param name="recorder">事件记录桩。</param>
        /// <param name="store">会话存储。</param>
        /// <returns>Token 服务实例。</returns>
        private static OnlineSessionTokenService CreateService(out OnlineEventRecorder recorder, out InMemoryOnlineSessionStore store)
        {
            recorder = new OnlineEventRecorder();
            store = new InMemoryOnlineSessionStore();
            return new OnlineSessionTokenService(store, recorder);
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
