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

using System.Collections.Generic;
using System.Threading.Tasks;
using GameFrameX.Online.Contracts;
using GameFrameX.Online.Events;
using GameFrameX.Online.Matchmaking;
using GameFrameX.Online.Scope;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 匹配票据服务测试（vault:C5 VC-4.2/VC-4.4/VC-4.9/VC-4.11/VC-4.12：入队与重复保护、限流、取消与过期）。
    /// </summary>
    public class OnlineMatchTicketServiceTests
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
        /// 验证入队生成排队中的票据并带上存活时长（VC-4.9）。
        /// </summary>
        [Fact]
        public async Task EnqueueAsync_ShouldCreateQueuedTicketWithExpiry()
        {
            // Arrange
            var service = CreateService(out _, out _, new OnlineMatchmakerOptions { TicketTimeToLiveSeconds = 300 });
            var scope = CreatePlayerScope(PlayerOne);

            // Act
            var outcome = await service.EnqueueAsync(scope, CreateEnqueueRequest(PlayerOne));

            // Assert
            Assert.True(outcome.IsSuccess);
            Assert.Equal(OnlineMatchTicketState.Queued, outcome.Data.State);
            Assert.Equal(new List<long> { PlayerOne }, outcome.Data.PlayerIds);
            Assert.Equal(300000L, outcome.Data.ExpiresAtTime - outcome.Data.CreatedAtTime);
            Assert.Equal(OnlineMatchFailureReason.None, outcome.Data.FailureReason);
        }

        /// <summary>
        /// 验证同一玩家重复入队被拒，不产生第二张票据（VC-4.4 重复入队保护 / VC-4.12 唯一性）。
        /// </summary>
        [Fact]
        public async Task EnqueueAsync_WhenPlayerAlreadyQueued_ShouldBeRejected()
        {
            // Arrange
            var service = CreateService(out var store, out _);
            var scope = CreatePlayerScope(PlayerOne);
            await service.EnqueueAsync(scope, CreateEnqueueRequest(PlayerOne));

            // Act
            var outcome = await service.EnqueueAsync(scope, CreateEnqueueRequest(PlayerOne));

            // Assert
            Assert.False(outcome.IsSuccess);
            Assert.Equal(OnlineErrorCode.DuplicateRequest, outcome.Code);
            var queued = await store.ListQueuedAsync(TenantId, AppId);
            Assert.Single(queued);
        }

        /// <summary>
        /// 验证同一队伍重复入队被拒（VC-4.4 重复入队保护：队伍维度）。
        /// </summary>
        [Fact]
        public async Task EnqueueAsync_WhenPartyAlreadyQueued_ShouldBeRejected()
        {
            // Arrange
            var service = CreateService(out _, out _);
            var request = CreateEnqueueRequest(PlayerOne);
            request.PartyId = "pty-1";
            await service.EnqueueAsync(CreatePlayerScope(PlayerOne), request);
            var second = CreateEnqueueRequest(PlayerTwo);
            second.PartyId = "pty-1";

            // Act
            var outcome = await service.EnqueueAsync(CreatePlayerScope(PlayerTwo), second);

            // Assert
            Assert.False(outcome.IsSuccess);
            Assert.Equal(OnlineErrorCode.DuplicateRequest, outcome.Code);
        }

        /// <summary>
        /// 验证高频入队被限流（VC-4.11），且阈值内的正常入队不受影响。
        /// </summary>
        [Fact]
        public async Task EnqueueAsync_WhenFlooding_ShouldBeRateLimited()
        {
            // Arrange
            var options = new OnlineMatchmakerOptions { RateLimitMaxOperations = 2, RateLimitWindowSeconds = 600 };
            var service = CreateService(out _, out _, options);
            var scope = CreatePlayerScope(PlayerOne);
            await service.EnqueueAsync(scope, CreateEnqueueRequest(PlayerOne, teamSize: 2));
            await service.CancelAsync(scope, (await service.GetActiveAsync(scope)).Data.TicketId);

            // Act：第三次操作超限
            var outcome = await service.EnqueueAsync(scope, CreateEnqueueRequest(PlayerOne, teamSize: 2));

            // Assert
            Assert.False(outcome.IsSuccess);
            Assert.Equal(OnlineErrorCode.RateLimitExceeded, outcome.Code);
        }

        /// <summary>
        /// 验证取消排队中的票据后状态为 Cancelled 且发布事件（VC-4.2）。
        /// </summary>
        [Fact]
        public async Task CancelAsync_ShouldCancelTicketAndPublishEvent()
        {
            // Arrange
            var service = CreateService(out _, out var recorder);
            var scope = CreatePlayerScope(PlayerOne);
            var enqueued = await service.EnqueueAsync(scope, CreateEnqueueRequest(PlayerOne));

            // Act
            var outcome = await service.CancelAsync(scope, enqueued.Data.TicketId);

            // Assert
            Assert.True(outcome.IsSuccess);
            Assert.Equal(OnlineMatchTicketState.Cancelled, outcome.Data.State);
            Assert.Equal(OnlineMatchFailureReason.CancelledByPlayer, outcome.Data.FailureReason);
            Assert.Single(recorder.Filter(OnlineMatchEvents.TicketChanged));
        }

        /// <summary>
        /// 验证已匹配的票据不接受取消改写，而是返回匹配终态回执（VC-4.2：已成立的结果不被回滚）。
        /// </summary>
        [Fact]
        public async Task CancelAsync_WhenAlreadyMatched_ShouldReturnTerminalReceipt()
        {
            // Arrange
            var service = CreateService(out var store, out _);
            var scope = CreatePlayerScope(PlayerOne);
            var enqueued = await service.EnqueueAsync(scope, CreateEnqueueRequest(PlayerOne));
            var assignment = new OnlineMatchAssignment
            {
                AssignmentId = "asg-fixed",
                MatchId = "match-fixed",
                PlayerIds = new List<long> { PlayerOne },
                TicketIds = new List<string> { enqueued.Data.TicketId },
                TenantId = TenantId,
                AppId = AppId,
                ServerId = ServerId,
            };
            var committed = await store.CommitMatchAsync(assignment, new List<string> { enqueued.Data.TicketId }, OnlineMatchTicketState.Queued);

            // Act
            var outcome = await service.CancelAsync(scope, enqueued.Data.TicketId);

            // Assert
            Assert.NotNull(committed);
            Assert.True(outcome.IsSuccess);
            Assert.Equal(OnlineMatchTicketState.Matched, outcome.Data.State);
            Assert.Equal("asg-fixed", outcome.Data.AssignmentId);
        }

        /// <summary>
        /// 验证无法取消他人的票据（作用域与成员双重校验，防枚举）。
        /// </summary>
        [Fact]
        public async Task CancelAsync_WhenTicketBelongsToOtherPlayer_ShouldBeRejected()
        {
            // Arrange
            var service = CreateService(out _, out _);
            var enqueued = await service.EnqueueAsync(CreatePlayerScope(PlayerOne), CreateEnqueueRequest(PlayerOne));

            // Act
            var outcome = await service.CancelAsync(CreatePlayerScope(PlayerTwo), enqueued.Data.TicketId);

            // Assert
            Assert.False(outcome.IsSuccess);
            Assert.Equal(OnlineErrorCode.ResourceNotFound, outcome.Code);
        }

        /// <summary>
        /// 验证取消后玩家的活跃票据查询返回空（VC-4.10：排队状态可确定性获知）。
        /// </summary>
        [Fact]
        public async Task GetActiveAsync_AfterCancel_ShouldReturnNull()
        {
            // Arrange
            var service = CreateService(out _, out _);
            var scope = CreatePlayerScope(PlayerOne);
            var enqueued = await service.EnqueueAsync(scope, CreateEnqueueRequest(PlayerOne));

            // Act
            var before = await service.GetActiveAsync(scope);
            await service.CancelAsync(scope, enqueued.Data.TicketId);
            var after = await service.GetActiveAsync(scope);

            // Assert
            Assert.NotNull(before.Data);
            Assert.Null(after.Data);
        }

        /// <summary>
        /// 验证超过存活时长的票据被扫描过期并发布事件（VC-4.9）。
        /// </summary>
        [Fact]
        public async Task SweepExpiredAsync_ShouldExpireOverdueTicket()
        {
            // Arrange
            var service = CreateService(out var store, out var recorder, new OnlineMatchmakerOptions { TicketTimeToLiveSeconds = 1 });
            var enqueued = await service.EnqueueAsync(CreatePlayerScope(PlayerOne), CreateEnqueueRequest(PlayerOne));

            // Act
            var outcome = await service.SweepExpiredAsync(TenantId, AppId, enqueued.Data.ExpiresAtTime + 1);

            // Assert
            Assert.True(outcome.IsSuccess);
            Assert.Equal(1, outcome.Data);
            var expired = await store.FindAsync(TenantId, AppId, enqueued.Data.TicketId);
            Assert.Equal(OnlineMatchTicketState.Expired, expired.State);
            Assert.Equal(OnlineMatchFailureReason.WaitTimeout, expired.FailureReason);
            Assert.Single(recorder.Filter(OnlineMatchEvents.TicketChanged));
        }

        /// <summary>
        /// 验证过期票据释放玩家索引，玩家可再次入队（VC-4.12：终态不占位）。
        /// </summary>
        [Fact]
        public async Task EnqueueAsync_AfterTicketExpired_ShouldBeAllowed()
        {
            // Arrange
            var service = CreateService(out _, out _, new OnlineMatchmakerOptions { TicketTimeToLiveSeconds = 1 });
            var scope = CreatePlayerScope(PlayerOne);
            var enqueued = await service.EnqueueAsync(scope, CreateEnqueueRequest(PlayerOne));
            await service.SweepExpiredAsync(TenantId, AppId, enqueued.Data.ExpiresAtTime + 1);

            // Act
            var outcome = await service.EnqueueAsync(scope, CreateEnqueueRequest(PlayerOne));

            // Assert
            Assert.True(outcome.IsSuccess);
            Assert.NotEqual(enqueued.Data.TicketId, outcome.Data.TicketId);
        }

        /// <summary>
        /// 创建被测服务。
        /// </summary>
        /// <param name="store">票据存储。</param>
        /// <param name="recorder">事件记录桩。</param>
        /// <param name="options">匹配与限流可配置项（可空）。</param>
        /// <returns>票据服务实例。</returns>
        private static OnlineMatchTicketService CreateService(out InMemoryOnlineMatchTicketStore store, out OnlineEventRecorder recorder, OnlineMatchmakerOptions options = null)
        {
            store = new InMemoryOnlineMatchTicketStore();
            recorder = new OnlineEventRecorder();
            return new OnlineMatchTicketService(store, recorder, options);
        }

        /// <summary>
        /// 构造入队请求。
        /// </summary>
        /// <param name="playerId">玩家标识。</param>
        /// <param name="teamSize">目标对局规模。</param>
        /// <returns>入队请求。</returns>
        private static OnlineMatchTicketEnqueueRequest CreateEnqueueRequest(long playerId, int teamSize = 2)
        {
            return new OnlineMatchTicketEnqueueRequest
            {
                PartyId = string.Empty,
                PlayerIds = new List<long> { playerId },
                Mode = 1,
                Region = 1,
                SkillRange = new OnlineMatchSkillRange { Min = 100, Max = 200 },
                TeamSize = teamSize,
                LatencyRequirement = 0,
                CustomProperties = new Dictionary<string, string> { { "rank", "gold" } },
            };
        }

        /// <summary>
        /// 构造玩家作用域。
        /// </summary>
        /// <param name="playerId">玩家标识。</param>
        /// <returns>作用域。</returns>
        private static OnlineScope CreatePlayerScope(long playerId)
        {
            return new OnlineScope(TenantId, AppId, ServerId, playerId);
        }
    }
}
