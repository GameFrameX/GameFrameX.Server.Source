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
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Online.Contracts;
using GameFrameX.Online.Events;
using GameFrameX.Online.Party;
using GameFrameX.Online.Scope;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 队伍服务测试（vault:C5 VC-4.1/VC-4.3/VC-4.6/VC-4.7 + S4.3/S4.7：队伍生命周期、整队完整、队长退出与离线清理、终态不可复活）。
    /// </summary>
    public class OnlinePartyServiceTests
    {
        /// <summary>测试用租户标识。</summary>
        private const long TenantId = 1;

        /// <summary>测试用应用标识。</summary>
        private const long AppId = 10;

        /// <summary>测试用区服标识。</summary>
        private const long ServerId = 100;

        /// <summary>测试用玩家一标识（默认队长）。</summary>
        private const long PlayerOne = 1001;

        /// <summary>测试用玩家二标识。</summary>
        private const long PlayerTwo = 1002;

        /// <summary>
        /// 验证创建队伍后发起人即队长且已是首个成员（VC-4.1）。
        /// </summary>
        [Fact]
        public async Task CreateAsync_ShouldCreatePartyWithLeaderAsFirstMember()
        {
            // Arrange
            var service = CreateService(out _);

            // Act
            var outcome = await service.CreateAsync(CreatePlayerScope(PlayerOne));

            // Assert
            Assert.True(outcome.IsSuccess);
            Assert.Equal(OnlinePartyState.Created, outcome.Data.State);
            Assert.Equal(PlayerOne, outcome.Data.LeaderId);
            Assert.Single(outcome.Data.Members);
            Assert.Equal(PlayerOne, outcome.Data.Members[0].PlayerId);
        }

        /// <summary>
        /// 验证重复创建返回既有队伍而非第二支队伍（幂等，VC-4.1）。
        /// </summary>
        [Fact]
        public async Task CreateAsync_WhenAlreadyInParty_ShouldReturnExistingParty()
        {
            // Arrange
            var service = CreateService(out _);
            var scope = CreatePlayerScope(PlayerOne);

            // Act
            var first = await service.CreateAsync(scope);
            var second = await service.CreateAsync(scope);

            // Assert
            Assert.True(second.IsSuccess);
            Assert.Equal(first.Data.PartyId, second.Data.PartyId);
        }

        /// <summary>
        /// 验证邀请被接受后队伍成型（Created/Inviting → Formed，vault:C5 S4.3）。
        /// </summary>
        [Fact]
        public async Task AnswerInviteAsync_WhenAccepted_ShouldFormParty()
        {
            // Arrange
            var service = CreateService(out _);
            var party = await CreatePartyAsync(service, PlayerOne);

            // Act
            var invite = await service.InviteAsync(CreatePlayerScope(PlayerOne), party.PartyId, PlayerTwo);
            var outcome = await service.AnswerInviteAsync(CreatePlayerScope(PlayerTwo), invite.Data.InviteId, true);

            // Assert
            Assert.True(invite.IsSuccess);
            Assert.True(outcome.IsSuccess);
            Assert.Equal(OnlinePartyState.Formed, outcome.Data.State);
            Assert.Equal(2, outcome.Data.Members.Count);
        }

        /// <summary>
        /// 验证拒绝邀请不产生成员（VC-4.1：未接受者不进入队伍）。
        /// </summary>
        [Fact]
        public async Task AnswerInviteAsync_WhenRejected_ShouldNotAddMember()
        {
            // Arrange
            var service = CreateService(out _);
            var party = await CreatePartyAsync(service, PlayerOne);
            var invite = await service.InviteAsync(CreatePlayerScope(PlayerOne), party.PartyId, PlayerTwo);

            // Act
            var outcome = await service.AnswerInviteAsync(CreatePlayerScope(PlayerTwo), invite.Data.InviteId, false);

            // Assert
            Assert.True(outcome.IsSuccess);
            Assert.Single(outcome.Data.Members);
        }

        /// <summary>
        /// 验证全员就绪后队伍进入 Ready（vault:C5 S4.3：全员就绪才进 Ready）。
        /// </summary>
        [Fact]
        public async Task SetReadyAsync_WhenAllReady_ShouldReachReady()
        {
            // Arrange
            var service = CreateService(out _);
            var party = await FormPartyAsync(service, PlayerOne, PlayerTwo);

            // Act
            await service.SetReadyAsync(CreatePlayerScope(PlayerOne), true);
            var outcome = await service.SetReadyAsync(CreatePlayerScope(PlayerTwo), true);

            // Assert
            Assert.True(outcome.IsSuccess);
            Assert.Equal(OnlinePartyState.Ready, outcome.Data.State);
        }

        /// <summary>
        /// 验证成员退队致人数不足时回落 Left，且队伍保留可恢复（vault:C5 S4.7 边界规则）。
        /// </summary>
        [Fact]
        public async Task LeaveAsync_WhenBelowMinMembers_ShouldReachLeft()
        {
            // Arrange
            var service = CreateService(out _);
            var party = await FormPartyAsync(service, PlayerOne, PlayerTwo);

            // Act
            var outcome = await service.LeaveAsync(CreatePlayerScope(PlayerTwo));

            // Assert
            Assert.True(outcome.IsSuccess);
            Assert.Equal(OnlinePartyState.Left, outcome.Data.State);
            Assert.Single(outcome.Data.Members);
            Assert.False(OnlinePartyStateMachine.IsTerminal(outcome.Data.State));
        }

        /// <summary>
        /// 验证队长退出后队长位转移给加入最早者，且队伍不因队长离开而失效（VC-4.6：绝不产生孤儿队长）。
        /// </summary>
        [Fact]
        public async Task LeaveAsync_WhenLeaderLeaves_ShouldTransferToEarliestJoinedMember()
        {
            // Arrange
            var service = CreateService(out _);
            await FormPartyAsync(service, PlayerOne, PlayerTwo);

            // Act
            var outcome = await service.LeaveAsync(CreatePlayerScope(PlayerOne));

            // Assert
            Assert.True(outcome.IsSuccess);
            Assert.Equal(PlayerTwo, outcome.Data.LeaderId);
            Assert.Single(outcome.Data.Members);
            Assert.False(OnlinePartyStateMachine.IsTerminal(outcome.Data.State));
        }

        /// <summary>
        /// 验证最后一名成员离开时队伍解散（VC-4.6：无人可转移则解散，不留空壳队伍）。
        /// </summary>
        [Fact]
        public async Task LeaveAsync_WhenLastMemberLeaves_ShouldDisband()
        {
            // Arrange
            var service = CreateService(out _);
            var party = await FormPartyAsync(service, PlayerOne, PlayerTwo);
            await service.LeaveAsync(CreatePlayerScope(PlayerTwo));

            // Act
            var outcome = await service.LeaveAsync(CreatePlayerScope(PlayerOne));

            // Assert
            Assert.True(outcome.IsSuccess);
            Assert.Empty(outcome.Data.Members);
            Assert.Equal(OnlinePartyState.Disbanded, outcome.Data.State);
            Assert.True(OnlinePartyStateMachine.IsTerminal(outcome.Data.State));
            Assert.Equal(party.PartyId, outcome.Data.PartyId);
        }

        /// <summary>
        /// 验证队长转移生效（VC-4.6：队长是队伍的单点事实源）。
        /// </summary>
        [Fact]
        public async Task TransferLeaderAsync_ShouldSwitchLeader()
        {
            // Arrange
            var service = CreateService(out _);
            var party = await FormPartyAsync(service, PlayerOne, PlayerTwo);

            // Act
            var outcome = await service.TransferLeaderAsync(CreatePlayerScope(PlayerOne), party.PartyId, PlayerTwo);

            // Assert
            Assert.True(outcome.IsSuccess);
            Assert.Equal(PlayerTwo, outcome.Data.LeaderId);
        }

        /// <summary>
        /// 验证取消队伍进入终态并发布状态变更事件（vault:C5 S4.3：终态唯一）。
        /// </summary>
        [Fact]
        public async Task CancelAsync_ShouldReachCancelledAndPublishEvent()
        {
            // Arrange
            var service = CreateService(out var recorder);
            var party = await FormPartyAsync(service, PlayerOne, PlayerTwo);
            var before = recorder.Filter(OnlinePartyEvents.PartyChanged).Count;

            // Act
            var outcome = await service.CancelAsync(CreatePlayerScope(PlayerOne));

            // Assert
            Assert.True(outcome.IsSuccess);
            Assert.Equal(OnlinePartyState.Cancelled, outcome.Data.State);
            Assert.True(OnlinePartyStateMachine.IsTerminal(outcome.Data.State));
            Assert.True(recorder.Filter(OnlinePartyEvents.PartyChanged).Count > before);
            Assert.Equal(party.PartyId, outcome.Data.PartyId);
        }

        /// <summary>
        /// 验证已进入终态的队伍不可复活（vault:C5 S4.3：取消/解散后状态唯一且不可逆）。
        /// </summary>
        [Fact]
        public async Task BindMatchStateAsync_AfterTerminalState_ShouldBeRejected()
        {
            // Arrange
            var service = CreateService(out _);
            var party = await FormPartyAsync(service, PlayerOne, PlayerTwo);
            await service.CancelAsync(CreatePlayerScope(PlayerOne));

            // Act
            var outcome = await service.BindMatchStateAsync(CreatePlayerScope(PlayerOne), party.PartyId, OnlinePartyState.Ready);

            // Assert
            Assert.False(outcome.IsSuccess);
            Assert.Equal(OnlineErrorCode.StateEnded, outcome.Code);
        }

        /// <summary>
        /// 验证离线成员被清理（VC-4.7：离线成员不再占位，队伍回落 Left 等待补充）。
        /// </summary>
        [Fact]
        public async Task PruneOfflineMembersAsync_ShouldRemoveOfflineMember()
        {
            // Arrange
            var service = CreateService(out _, new StubPresenceProbe(PlayerOne));
            var party = await FormPartyAsync(service, PlayerOne, PlayerTwo);

            // Act
            var outcome = await service.PruneOfflineMembersAsync(CreatePlayerScope(PlayerOne), party.PartyId);

            // Assert
            Assert.True(outcome.IsSuccess);
            Assert.Single(outcome.Data.Members);
            Assert.Equal(PlayerOne, outcome.Data.Members[0].PlayerId);
            Assert.Equal(OnlinePartyState.Left, outcome.Data.State);
        }

        /// <summary>
        /// 创建被测服务。
        /// </summary>
        /// <param name="recorder">事件记录桩。</param>
        /// <param name="probe">在线事实探针（可空）。</param>
        /// <returns>队伍服务实例。</returns>
        private static OnlinePartyService CreateService(out OnlineEventRecorder recorder, IOnlinePartyPresenceProbe probe = null)
        {
            recorder = new OnlineEventRecorder();
            return new OnlinePartyService(new InMemoryOnlinePartyStore(), recorder, 2, 8, 120, 1800, probe);
        }

        /// <summary>
        /// 创建一支单人队伍。
        /// </summary>
        /// <param name="service">队伍服务。</param>
        /// <param name="leaderId">队长标识。</param>
        /// <returns>队伍。</returns>
        private static async Task<OnlineParty> CreatePartyAsync(OnlinePartyService service, long leaderId)
        {
            var outcome = await service.CreateAsync(CreatePlayerScope(leaderId));
            Assert.True(outcome.IsSuccess);
            return outcome.Data;
        }

        /// <summary>
        /// 创建一支由两人组成的队伍（邀请 + 接受）。
        /// </summary>
        /// <param name="service">队伍服务。</param>
        /// <param name="leaderId">队长标识。</param>
        /// <param name="memberId">成员标识。</param>
        /// <returns>成型后的队伍。</returns>
        private static async Task<OnlineParty> FormPartyAsync(OnlinePartyService service, long leaderId, long memberId)
        {
            var party = await CreatePartyAsync(service, leaderId);
            var invite = await service.InviteAsync(CreatePlayerScope(leaderId), party.PartyId, memberId);
            Assert.True(invite.IsSuccess);
            var outcome = await service.AnswerInviteAsync(CreatePlayerScope(memberId), invite.Data.InviteId, true);
            Assert.True(outcome.IsSuccess);
            return outcome.Data;
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

        /// <summary>
        /// 在线事实探针桩：仅白名单内玩家视为在线。
        /// </summary>
        private sealed class StubPresenceProbe : IOnlinePartyPresenceProbe
        {
            /// <summary>在线玩家集合。</summary>
            private readonly HashSet<long> _online;

            /// <summary>
            /// 初始化 <see cref="StubPresenceProbe"/>。
            /// </summary>
            /// <param name="onlinePlayerIds">在线玩家标识集合。</param>
            public StubPresenceProbe(params long[] onlinePlayerIds)
            {
                _online = new HashSet<long>(onlinePlayerIds);
            }

            /// <inheritdoc />
            public Task<bool> IsOnlineAsync(long tenantId, long appId, long playerId, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(_online.Contains(playerId));
            }
        }
    }
}
