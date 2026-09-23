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
using GameFrameX.Online.Scope;
using GameFrameX.Online.Social;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 好友服务测试（vault:C7 S6.2 好友域：关系生命周期唯一写者、反预言答复权、
    /// 状态机收敛与可重新发起、超期扫描、列表与搜索上限、在线状态读取时事实、社交变更事件）。
    /// </summary>
    public class OnlineFriendServiceTests
    {
        /// <summary>测试用租户标识。</summary>
        private const long TenantId = 1;

        /// <summary>测试用应用标识。</summary>
        private const long AppId = 10;

        /// <summary>测试用区服标识。</summary>
        private const long ServerId = 100;

        /// <summary>测试用玩家一标识（多数用例中的请求发起方）。</summary>
        private const long PlayerOne = 1001;

        /// <summary>测试用玩家二标识（多数用例中的被请求方）。</summary>
        private const long PlayerTwo = 1002;

        /// <summary>测试用玩家三标识（与关系无关的第三方）。</summary>
        private const long PlayerThree = 1003;

        /// <summary>测试用玩家四标识（好友列表上限用例的第三名好友）。</summary>
        private const long PlayerFour = 1004;

        /// <summary>
        /// 验证发起好友请求生成唯一的待答复记录（方向事实、无向对规范化、有效期），
        /// 且重复发起收敛到同一标识、不新建第二条、不重复发事件（VC-6.1）。
        /// </summary>
        [Fact]
        public async Task RequestAsync_ShouldCreateSinglePendingRecordAndConvergeOnRepeat()
        {
            // Arrange
            var service = CreateService(out var recorder, out var store);
            var requesterScope = CreatePlayerScope(PlayerOne);

            // Act
            var outcome = await service.RequestAsync(requesterScope, PlayerTwo, "corr-request");
            var repeated = await service.RequestAsync(requesterScope, PlayerTwo);

            // Assert
            Assert.True(outcome.IsSuccess);
            Assert.Equal(OnlineFriendshipState.Requested, outcome.Data.State);
            Assert.Equal(PlayerOne, outcome.Data.RequesterId);
            Assert.Equal(PlayerTwo, outcome.Data.AddresseeId);
            Assert.Equal(PlayerOne, outcome.Data.LowPlayerId);
            Assert.Equal(PlayerTwo, outcome.Data.HighPlayerId);
            Assert.True(outcome.Data.ExpiresAtTime > outcome.Data.CreatedAtTime);

            Assert.True(repeated.IsSuccess);
            Assert.Equal(outcome.Data.FriendshipId, repeated.Data.FriendshipId);
            Assert.Single(await store.ListByPlayerAsync(TenantId, AppId, PlayerOne));
            Assert.Equal(1, recorder.Count);

            // 待答复列表只在被请求方（AddresseeId）一侧可见。
            var pendingForAddressee = await service.ListPendingRequestsAsync(CreatePlayerScope(PlayerTwo));
            Assert.True(pendingForAddressee.IsSuccess);
            Assert.Single(pendingForAddressee.Data);
            Assert.Equal(outcome.Data.FriendshipId, pendingForAddressee.Data[0].FriendshipId);

            var pendingForRequester = await service.ListPendingRequestsAsync(requesterScope);
            Assert.True(pendingForRequester.IsSuccess);
            Assert.Empty(pendingForRequester.Data);
        }

        /// <summary>
        /// 验证反向重复申请不自动接受：被请求方反向再发起时返回同一条待答复记录，
        /// 状态不前进、方向事实不翻转，双方好友列表仍为空（VC-6.1：答复权只属于被请求方）。
        /// </summary>
        [Fact]
        public async Task RequestAsync_WhenAddresseeRequestsBack_ShouldNotAutoAccept()
        {
            // Arrange
            var service = CreateService(out var recorder, out _);
            var request = await RequestAsync(service, PlayerOne, PlayerTwo);

            // Act
            var reverse = await service.RequestAsync(CreatePlayerScope(PlayerTwo), PlayerOne);

            // Assert
            Assert.True(reverse.IsSuccess);
            Assert.Equal(request.FriendshipId, reverse.Data.FriendshipId);
            Assert.Equal(OnlineFriendshipState.Requested, reverse.Data.State);
            Assert.Equal(PlayerOne, reverse.Data.RequesterId);
            Assert.Equal(PlayerTwo, reverse.Data.AddresseeId);
            Assert.Equal(1, recorder.Count);

            var pendingForAddressee = await service.ListPendingRequestsAsync(CreatePlayerScope(PlayerTwo));
            Assert.True(pendingForAddressee.IsSuccess);
            Assert.Single(pendingForAddressee.Data);

            var friendsOfRequester = await service.ListFriendsAsync(CreatePlayerScope(PlayerOne));
            Assert.True(friendsOfRequester.IsSuccess);
            Assert.Empty(friendsOfRequester.Data);

            var friendsOfAddressee = await service.ListFriendsAsync(CreatePlayerScope(PlayerTwo));
            Assert.True(friendsOfAddressee.IsSuccess);
            Assert.Empty(friendsOfAddressee.Data);
        }

        /// <summary>
        /// 验证被请求方接受后关系进入 Accepted，双方好友列表各出现一条条目，
        /// 待答复列表清空（VC-6.2：Requested → Accepted 是唯一迁移路径）。
        /// </summary>
        [Fact]
        public async Task AcceptAsync_WhenAddresseeAnswers_ShouldEstablishFriendshipOnBothSides()
        {
            // Arrange
            var service = CreateService(out _, out _);
            var request = await RequestAsync(service, PlayerOne, PlayerTwo);

            // Act
            var outcome = await service.AcceptAsync(CreatePlayerScope(PlayerTwo), request.FriendshipId);

            // Assert
            Assert.True(outcome.IsSuccess);
            Assert.Equal(OnlineFriendshipState.Accepted, outcome.Data.State);
            Assert.Equal(PlayerOne, outcome.Data.RequesterId);
            Assert.True(outcome.Data.RespondedAtTime > 0);

            var friendsOfRequester = await service.ListFriendsAsync(CreatePlayerScope(PlayerOne));
            Assert.True(friendsOfRequester.IsSuccess);
            var summary = Assert.Single(friendsOfRequester.Data);
            Assert.Equal(PlayerTwo, summary.PlayerId);
            Assert.Equal("1002", summary.Name);
            Assert.False(summary.Online);
            Assert.Equal(outcome.Data.RespondedAtTime, summary.EstablishedAtTime);

            var friendsOfAddressee = await service.ListFriendsAsync(CreatePlayerScope(PlayerTwo));
            Assert.True(friendsOfAddressee.IsSuccess);
            Assert.Equal(PlayerOne, Assert.Single(friendsOfAddressee.Data).PlayerId);

            var pending = await service.ListPendingRequestsAsync(CreatePlayerScope(PlayerTwo));
            Assert.True(pending.IsSuccess);
            Assert.Empty(pending.Data);
        }

        /// <summary>
        /// 验证发起方答复自己的请求拿到 ResourceNotFound（反预言：答复权只属于被请求方，
        /// 发起方看不到这条请求可被答复），失败不改变状态、不发布事件（VC-6.2）。
        /// </summary>
        [Fact]
        public async Task AcceptAsync_WhenRequesterAnswersOwnRequest_ShouldReturnResourceNotFound()
        {
            // Arrange
            var service = CreateService(out var recorder, out _);
            var request = await RequestAsync(service, PlayerOne, PlayerTwo);

            // Act
            var outcome = await service.AcceptAsync(CreatePlayerScope(PlayerOne), request.FriendshipId);

            // Assert
            Assert.False(outcome.IsSuccess);
            Assert.Equal(OnlineErrorCode.ResourceNotFound, outcome.Code);
            Assert.Null(outcome.Data);
            Assert.Equal(1, recorder.Count);

            var pending = await service.ListPendingRequestsAsync(CreatePlayerScope(PlayerTwo));
            Assert.True(pending.IsSuccess);
            Assert.Equal(OnlineFriendshipState.Requested, pending.Data[0].State);

            var friends = await service.ListFriendsAsync(CreatePlayerScope(PlayerOne));
            Assert.True(friends.IsSuccess);
            Assert.Empty(friends.Data);
        }

        /// <summary>
        /// 验证无关第三方答复他人的请求同样拿到 ResourceNotFound（反预言：不泄露他人关系的存在性），
        /// 且请求维持待答复（VC-6.2）。
        /// </summary>
        [Fact]
        public async Task RejectAsync_WhenUnrelatedPlayerAnswers_ShouldReturnResourceNotFound()
        {
            // Arrange
            var service = CreateService(out var recorder, out _);
            var request = await RequestAsync(service, PlayerOne, PlayerTwo);

            // Act
            var outcome = await service.RejectAsync(CreatePlayerScope(PlayerThree), request.FriendshipId);

            // Assert
            Assert.False(outcome.IsSuccess);
            Assert.Equal(OnlineErrorCode.ResourceNotFound, outcome.Code);
            Assert.Equal(1, recorder.Count);

            var pending = await service.ListPendingRequestsAsync(CreatePlayerScope(PlayerTwo));
            Assert.True(pending.IsSuccess);
            Assert.Single(pending.Data);
            Assert.Equal(OnlineFriendshipState.Requested, pending.Data[0].State);
        }

        /// <summary>
        /// 验证被请求方拒绝后关系进入 Rejected、不出现在好友列表，且重复拒绝幂等
        /// 返回同一快照、不重复发事件（VC-6.2）。
        /// </summary>
        [Fact]
        public async Task RejectAsync_WhenAddresseeRejects_ShouldSetRejectedAndHideFromFriendList()
        {
            // Arrange
            var service = CreateService(out var recorder, out _);
            var request = await RequestAsync(service, PlayerOne, PlayerTwo);

            // Act
            var outcome = await service.RejectAsync(CreatePlayerScope(PlayerTwo), request.FriendshipId);
            var repeated = await service.RejectAsync(CreatePlayerScope(PlayerTwo), request.FriendshipId);

            // Assert
            Assert.True(outcome.IsSuccess);
            Assert.Equal(OnlineFriendshipState.Rejected, outcome.Data.State);
            Assert.True(outcome.Data.RespondedAtTime > 0);

            Assert.True(repeated.IsSuccess);
            Assert.Equal(OnlineFriendshipState.Rejected, repeated.Data.State);
            Assert.Equal(outcome.Data.RespondedAtTime, repeated.Data.RespondedAtTime);
            Assert.Equal(2, recorder.Count);

            var friends = await service.ListFriendsAsync(CreatePlayerScope(PlayerOne));
            Assert.True(friends.IsSuccess);
            Assert.Empty(friends.Data);

            var pending = await service.ListPendingRequestsAsync(CreatePlayerScope(PlayerTwo));
            Assert.True(pending.IsSuccess);
            Assert.Empty(pending.Data);
        }

        /// <summary>
        /// 验证被拒绝后可重新发起：复用同一条记录回到 Requested，方向翻转为本次发起人
        /// （Rejected → Requested 是合法边，不新建第二条记录，VC-6.2）。
        /// </summary>
        [Fact]
        public async Task RequestAsync_AfterRejected_ShouldReopenExistingRecordWithNewRequester()
        {
            // Arrange
            var service = CreateService(out _, out var store);
            var request = await RequestAsync(service, PlayerOne, PlayerTwo);
            var rejected = await service.RejectAsync(CreatePlayerScope(PlayerTwo), request.FriendshipId);
            Assert.True(rejected.IsSuccess);

            // Act
            var reopened = await service.RequestAsync(CreatePlayerScope(PlayerTwo), PlayerOne);

            // Assert
            Assert.True(reopened.IsSuccess);
            Assert.Equal(request.FriendshipId, reopened.Data.FriendshipId);
            Assert.Equal(OnlineFriendshipState.Requested, reopened.Data.State);
            Assert.Equal(PlayerTwo, reopened.Data.RequesterId);
            Assert.Equal(PlayerOne, reopened.Data.AddresseeId);
            Assert.Single(await store.ListByPlayerAsync(TenantId, AppId, PlayerOne));

            var pendingForNewAddressee = await service.ListPendingRequestsAsync(CreatePlayerScope(PlayerOne));
            Assert.True(pendingForNewAddressee.IsSuccess);
            Assert.Equal(PlayerTwo, Assert.Single(pendingForNewAddressee.Data).RequesterId);
        }

        /// <summary>
        /// 验证删除好友：Accepted → Removed，关系双方的好友列表均清空，且删除不是一次答复
        /// （不改写答复时刻，VC-6.2）。
        /// </summary>
        [Fact]
        public async Task RemoveAsync_WhenEstablished_ShouldSetRemovedForBothSides()
        {
            // Arrange
            var service = CreateService(out var recorder, out _);
            var accepted = await EstablishFriendshipAsync(service, PlayerOne, PlayerTwo);

            // Act
            var outcome = await service.RemoveAsync(CreatePlayerScope(PlayerOne), PlayerTwo);

            // Assert
            Assert.True(outcome.IsSuccess);
            Assert.Equal(OnlineFriendshipState.Removed, outcome.Data.State);
            Assert.Equal(accepted.RespondedAtTime, outcome.Data.RespondedAtTime);
            Assert.Equal(3, recorder.Count);

            var friendsOfRemover = await service.ListFriendsAsync(CreatePlayerScope(PlayerOne));
            Assert.True(friendsOfRemover.IsSuccess);
            Assert.Empty(friendsOfRemover.Data);

            var friendsOfOther = await service.ListFriendsAsync(CreatePlayerScope(PlayerTwo));
            Assert.True(friendsOfOther.IsSuccess);
            Assert.Empty(friendsOfOther.Data);
        }

        /// <summary>
        /// 验证不存在的关系上执行删除返回 ResourceNotFound（VC-6.2：无关系不得伪造成功，
        /// 也不得凭空发事件）。
        /// </summary>
        [Fact]
        public async Task RemoveAsync_WhenFriendshipMissing_ShouldReturnResourceNotFound()
        {
            // Arrange
            var service = CreateService(out var recorder, out _);

            // Act
            var outcome = await service.RemoveAsync(CreatePlayerScope(PlayerOne), PlayerTwo);

            // Assert
            Assert.False(outcome.IsSuccess);
            Assert.Equal(OnlineErrorCode.ResourceNotFound, outcome.Code);
            Assert.Null(outcome.Data);
            Assert.Equal(0, recorder.Count);
        }

        /// <summary>
        /// 验证删除后可重新添加：复用同一条记录回到 Requested，方向翻转为本次发起人
        /// （Removed → Requested 是合法边，VC-6.2 状态机末段）。
        /// </summary>
        [Fact]
        public async Task RequestAsync_AfterRemoved_ShouldReopenExistingRecordWithNewRequester()
        {
            // Arrange
            var service = CreateService(out _, out var store);
            var accepted = await EstablishFriendshipAsync(service, PlayerOne, PlayerTwo);
            var removed = await service.RemoveAsync(CreatePlayerScope(PlayerOne), PlayerTwo);
            Assert.True(removed.IsSuccess);

            // Act
            var reopened = await service.RequestAsync(CreatePlayerScope(PlayerTwo), PlayerOne);

            // Assert
            Assert.True(reopened.IsSuccess);
            Assert.Equal(accepted.FriendshipId, reopened.Data.FriendshipId);
            Assert.Equal(OnlineFriendshipState.Requested, reopened.Data.State);
            Assert.Equal(PlayerTwo, reopened.Data.RequesterId);
            Assert.Equal(PlayerOne, reopened.Data.AddresseeId);
            Assert.Single(await store.ListByPlayerAsync(TenantId, AppId, PlayerOne));
        }

        /// <summary>
        /// 验证超期扫描：未到期不收敛，到期收敛为 Expired 并返回本次终结条数，
        /// 重复扫描返回 0（幂等），终态后待答复列表清空（VC-6.2）。
        /// </summary>
        [Fact]
        public async Task SweepExpiredAsync_WhenRequestOverdue_ShouldExpireAndReturnAffectedCount()
        {
            // Arrange
            var service = CreateService(out var recorder, out var store);
            var request = await RequestAsync(service, PlayerOne, PlayerTwo);

            // Act
            var notDue = await service.SweepExpiredAsync(TenantId, AppId, request.ExpiresAtTime - 1);
            var outcome = await service.SweepExpiredAsync(TenantId, AppId, request.ExpiresAtTime + 1);
            var repeated = await service.SweepExpiredAsync(TenantId, AppId, request.ExpiresAtTime + 1);

            // Assert
            Assert.True(notDue.IsSuccess);
            Assert.Equal(0, notDue.Data);

            Assert.True(outcome.IsSuccess);
            Assert.Equal(1, outcome.Data);
            Assert.Equal(0, repeated.Data);

            var stored = await store.FindAsync(TenantId, AppId, PlayerOne, PlayerTwo);
            Assert.NotNull(stored);
            Assert.Equal(OnlineFriendshipState.Expired, stored.State);

            var events = recorder.Filter(OnlineSocialEvents.FriendshipChanged);
            Assert.Equal(2, events.Count);
            Assert.Equal(OnlineSocialEvents.FriendshipRequested, events[0].PayloadAuditFields["Action"]);
            Assert.Equal(OnlineSocialEvents.FriendshipExpired, events[1].PayloadAuditFields["Action"]);

            var pending = await service.ListPendingRequestsAsync(CreatePlayerScope(PlayerTwo));
            Assert.True(pending.IsSuccess);
            Assert.Empty(pending.Data);
        }

        /// <summary>
        /// 验证好友数量上限（friendLimit）在**写入侧**强制：达到上限后再接受好友请求返回状态不允许，
        /// 关系不落库，读取侧也不做任何截断。
        /// <para>
        /// 只做读取侧截断的危害在于「看起来一切正常」：接受请求返回成功、关系也进了库，
        /// 但好友列表里看不到对方——玩家无法分辨是丢了好友还是自己记错了。
        /// 宁可当场明确拒绝，也不静默丢弃。
        /// </para>
        /// <para>
        /// 上限同时判**请求方与答复方两侧**：本例中超限的是发起方（<c>PlayerOne</c>），
        /// 而点「接受」的是未超限的答复方；若只判答复方，这条路径就成了绕过上限的口子。
        /// </para>
        /// </summary>
        [Fact]
        public async Task AcceptAsync_WhenFriendLimitReached_ShouldRejectAndNotPersist()
        {
            // Arrange
            var service = CreateService(out _, out var store, friendLimit: 2);
            await EstablishFriendshipAsync(service, PlayerOne, PlayerTwo);
            await EstablishFriendshipAsync(service, PlayerOne, PlayerThree);
            var third = await RequestAsync(service, PlayerOne, PlayerFour);

            // Act
            var outcome = await service.AcceptAsync(CreatePlayerScope(PlayerFour), third.FriendshipId);

            // Assert
            Assert.False(outcome.IsSuccess);
            Assert.Equal(OnlineErrorCode.StateOperationForbidden, outcome.Code);

            // 请求仍然停在待答复态，没有变成好友（拒绝必须是干净的）。
            var rows = await store.ListByPlayerAsync(TenantId, AppId, PlayerFour);
            var stillPending = Assert.Single(rows);
            Assert.Equal(third.FriendshipId, stillPending.FriendshipId);
            Assert.Equal(OnlineFriendshipState.Requested, stillPending.State);

            // 读取侧不做任何截断：上限内已建立的两条好友关系一条不少。
            var listed = await service.ListFriendsAsync(CreatePlayerScope(PlayerOne));
            Assert.True(listed.IsSuccess);
            Assert.Equal(2, listed.Data.Count);
        }

        /// <summary>
        /// 验证好友数已满时**重放**一次已成功的接受仍然成功（重放与首次答复必须同解）。
        /// <para>
        /// 上限判定若排在「该状态是否还需要迁移」之前，重放会先撞上限并拿到
        /// <see cref="OnlineErrorCode.StateOperationForbidden"/> —— 客户端据此判定「加好友失败」，
        /// 但关系其实早已建立；玩家视角就是「好友列表里明明有他，界面却提示失败」。
        /// </para>
        /// </summary>
        [Fact]
        public async Task AcceptAsync_WhenLimitReachedButAlreadyAccepted_ShouldStayIdempotent()
        {
            // Arrange
            var service = CreateService(out _, out _, friendLimit: 2);
            var established = await EstablishFriendshipAsync(service, PlayerOne, PlayerTwo);
            await EstablishFriendshipAsync(service, PlayerOne, PlayerThree);

            // Act：关系已 Accepted，且玩家现在已顶到上限，此时重放接受。
            var replay = await service.AcceptAsync(CreatePlayerScope(PlayerTwo), established.FriendshipId);

            // Assert
            Assert.True(replay.IsSuccess);
            Assert.Equal(OnlineFriendshipState.Accepted, replay.Data.State);
            Assert.Equal(established.FriendshipId, replay.Data.FriendshipId);
        }

        /// <summary>
        /// 验证搜索把去空白的关键字与配置的 searchLimit 传给玩家目录、返回匹配条目并过滤掉自己
        /// （vault:C7 S6.2「好友支持搜索」；源码未标注专属 VC 编号）。
        /// </summary>
        [Fact]
        public async Task SearchAsync_ShouldHonorSearchLimitAndExcludeSelf()
        {
            // Arrange
            var directory = new StubPlayerDirectory();
            directory.Add(PlayerOne, "Alice");
            directory.Add(PlayerTwo, "AliceTwo");
            directory.Add(PlayerThree, "AliceThree");
            var service = CreateService(out _, out _, directory: directory, searchLimit: 2);

            // Act
            var outcome = await service.SearchAsync(CreatePlayerScope(PlayerOne), "  Ali  ");

            // Assert
            Assert.True(outcome.IsSuccess);
            Assert.Equal(2, directory.LastSearchLimit);
            Assert.Equal("Ali", directory.LastKeyword);

            // 目录先按 limit 取前两条（第一条是自己），服务层再过滤掉自己。
            var entry = Assert.Single(outcome.Data);
            Assert.Equal(PlayerTwo, entry.PlayerId);
            Assert.Equal("AliceTwo", entry.Name);
        }

        /// <summary>
        /// 验证未装配玩家目录时搜索降级为空结果而非报错（vault:C7 S6.2：搜索不可用不阻断其他社交能力）。
        /// </summary>
        [Fact]
        public async Task SearchAsync_WhenDirectoryMissing_ShouldReturnEmptyResult()
        {
            // Arrange
            var service = CreateService(out _, out _);

            // Act
            var outcome = await service.SearchAsync(CreatePlayerScope(PlayerOne), "Ali");

            // Assert
            Assert.True(outcome.IsSuccess);
            Assert.NotNull(outcome.Data);
            Assert.Empty(outcome.Data);
        }

        /// <summary>
        /// 验证在线状态是读取时事实：探针状态变化后两次列表结果跟着变，
        /// 且关系存储里的记录在两次读取后逐字段不变（在线状态不落库；
        /// vault:C7 S6.2，源码未标注专属 VC 编号）。
        /// </summary>
        [Fact]
        public async Task ListFriendsAsync_ShouldReadPresenceLiveWithoutPersistingIt()
        {
            // Arrange
            var probe = new StubPresenceProbe();
            var service = CreateService(out _, out var store, presenceProbe: probe);
            await EstablishFriendshipAsync(service, PlayerOne, PlayerTwo);
            var storedBefore = await store.FindAsync(TenantId, AppId, PlayerOne, PlayerTwo);

            // Act
            var offline = await service.ListFriendsAsync(CreatePlayerScope(PlayerOne));
            probe.SetOnline(PlayerTwo);
            var online = await service.ListFriendsAsync(CreatePlayerScope(PlayerOne));

            // Assert
            Assert.True(offline.IsSuccess);
            Assert.True(online.IsSuccess);
            Assert.False(Assert.Single(offline.Data).Online);
            Assert.True(Assert.Single(online.Data).Online);

            // 在线事实不落库：OnlineFriendship 无在线字段，落库记录亦未被读取行为改写。
            var storedAfter = await store.FindAsync(TenantId, AppId, PlayerOne, PlayerTwo);
            Assert.NotNull(storedAfter);
            Assert.Equal(storedBefore.State, storedAfter.State);
            Assert.Equal(storedBefore.RespondedAtTime, storedAfter.RespondedAtTime);
            Assert.Equal(storedBefore.UpdatedAtTime, storedAfter.UpdatedAtTime);
            Assert.Equal(storedBefore.ExpiresAtTime, storedAfter.ExpiresAtTime);
        }

        /// <summary>
        /// 验证未装配在线探针时好友列表的在线标记恒为 false（降级为「未知」而非误报在线；
        /// vault:C7 S6.2，源码未标注专属 VC 编号）。
        /// </summary>
        [Fact]
        public async Task ListFriendsAsync_WhenPresenceProbeMissing_ShouldReportOffline()
        {
            // Arrange
            var service = CreateService(out _, out _);
            await EstablishFriendshipAsync(service, PlayerOne, PlayerTwo);

            // Act
            var outcome = await service.ListFriendsAsync(CreatePlayerScope(PlayerOne));

            // Assert
            Assert.True(outcome.IsSuccess);
            Assert.False(Assert.Single(outcome.Data).Online);
        }

        /// <summary>
        /// 验证好友生命周期各步都发布 FriendshipChanged 事件并携带对应 Action 与关联标识，
        /// 信封玩家位取关系发起方（vault:C7 S6.3 社交域事件）。
        /// </summary>
        [Fact]
        public async Task FriendshipLifecycle_ShouldPublishChangedEventsWithAction()
        {
            // Arrange
            var service = CreateService(out var recorder, out _);
            var request = await service.RequestAsync(CreatePlayerScope(PlayerOne), PlayerTwo, "corr-1");

            // Act
            var accepted = await service.AcceptAsync(CreatePlayerScope(PlayerTwo), request.Data.FriendshipId, "corr-2");
            var removed = await service.RemoveAsync(CreatePlayerScope(PlayerTwo), PlayerOne, "corr-3");

            // Assert
            Assert.True(request.IsSuccess);
            Assert.True(accepted.IsSuccess);
            Assert.True(removed.IsSuccess);

            var events = recorder.Filter(OnlineSocialEvents.FriendshipChanged);
            Assert.Equal(3, events.Count);
            Assert.Equal(OnlineSocialEvents.FriendshipRequested, events[0].PayloadAuditFields["Action"]);
            Assert.Equal(OnlineSocialEvents.FriendshipAccepted, events[1].PayloadAuditFields["Action"]);
            Assert.Equal(OnlineSocialEvents.FriendshipRemoved, events[2].PayloadAuditFields["Action"]);
            Assert.Equal("corr-1", events[0].CorrelationId);
            Assert.Equal("corr-3", events[2].CorrelationId);

            // 关系是双边的，信封只放得下一个玩家位，恒取关系发起方——
            // 被请求方答复（接受）不翻转 RequesterId，因此三步事件的玩家位都是发起方。
            Assert.Equal(PlayerOne, events[0].PlayerId);
            Assert.Equal(PlayerOne, events[1].PlayerId);
            Assert.Equal(PlayerOne, events[2].PlayerId);
            Assert.Equal(request.Data.FriendshipId, events[0].PayloadAuditFields["FriendshipId"]);
            Assert.Equal(TenantId, events[2].TenantId);
            Assert.Equal(AppId, events[2].AppId);
            Assert.Equal(ServerId, events[2].ServerId);
        }

        /// <summary>
        /// 验证拒绝好友请求发布 Action = Rejected 的 FriendshipChanged 事件，且载荷状态同步为
        /// Rejected（vault:C7 S6.3 社交域事件）。
        /// </summary>
        [Fact]
        public async Task RejectAsync_ShouldPublishRejectedEvent()
        {
            // Arrange
            var service = CreateService(out var recorder, out _);
            var request = await RequestAsync(service, PlayerOne, PlayerTwo);

            // Act
            var outcome = await service.RejectAsync(CreatePlayerScope(PlayerTwo), request.FriendshipId);

            // Assert
            Assert.True(outcome.IsSuccess);
            var events = recorder.Filter(OnlineSocialEvents.FriendshipChanged);
            Assert.Equal(2, events.Count);
            Assert.Equal(OnlineSocialEvents.FriendshipRejected, events[1].PayloadAuditFields["Action"]);
            Assert.Equal("Rejected", events[1].PayloadAuditFields["State"]);
            Assert.Equal(PlayerTwo.ToString(), events[1].PayloadAuditFields["AddresseeId"]);
        }

        /// <summary>
        /// 创建被测好友服务（可注入玩家目录 / 在线探针 / 各项上限）。
        /// </summary>
        /// <param name="recorder">事件记录桩（输出）。</param>
        /// <param name="store">关系存储（输出，供断言落库事实）。</param>
        /// <param name="directory">玩家目录（可空）。</param>
        /// <param name="presenceProbe">在线事实探针（可空）。</param>
        /// <param name="searchLimit">搜索返回条数上限。</param>
        /// <param name="friendLimit">好友列表返回条数上限。</param>
        /// <returns>好友服务实例。</returns>
        private static OnlineFriendService CreateService(
            out OnlineEventRecorder recorder,
            out InMemoryOnlineFriendStore store,
            IOnlinePlayerDirectory directory = null,
            IOnlineSocialPresenceProbe presenceProbe = null,
            int searchLimit = 20,
            int friendLimit = 200)
        {
            recorder = new OnlineEventRecorder();
            store = new InMemoryOnlineFriendStore();
            return new OnlineFriendService(store, recorder, directory, presenceProbe, 604800, searchLimit, friendLimit);
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
        /// 发起好友请求并断言成功。
        /// </summary>
        /// <param name="service">好友服务。</param>
        /// <param name="requesterId">发起方标识。</param>
        /// <param name="targetPlayerId">目标玩家标识。</param>
        /// <returns>生效的关系记录。</returns>
        private static async Task<OnlineFriendship> RequestAsync(OnlineFriendService service, long requesterId, long targetPlayerId)
        {
            var outcome = await service.RequestAsync(CreatePlayerScope(requesterId), targetPlayerId);
            Assert.True(outcome.IsSuccess);
            return outcome.Data;
        }

        /// <summary>
        /// 走完「请求 + 接受」把两名玩家变成好友并断言成功。
        /// </summary>
        /// <param name="service">好友服务。</param>
        /// <param name="requesterId">发起方标识。</param>
        /// <param name="addresseeId">被请求方标识。</param>
        /// <returns>接受后的关系记录。</returns>
        private static async Task<OnlineFriendship> EstablishFriendshipAsync(OnlineFriendService service, long requesterId, long addresseeId)
        {
            var request = await RequestAsync(service, requesterId, addresseeId);
            var outcome = await service.AcceptAsync(CreatePlayerScope(addresseeId), request.FriendshipId);
            Assert.True(outcome.IsSuccess);
            return outcome.Data;
        }

        /// <summary>
        /// 玩家目录桩：按名称子串匹配、记录最近一次关键字与上限、履行 limit 约束。
        /// </summary>
        private sealed class StubPlayerDirectory : IOnlinePlayerDirectory
        {
            /// <summary>目录条目。</summary>
            private readonly List<OnlinePlayerDirectoryEntry> _entries = new List<OnlinePlayerDirectoryEntry>();

            /// <summary>
            /// 获取最近一次搜索使用的关键字。
            /// </summary>
            public string LastKeyword
            {
                get;
                private set;
            }

            /// <summary>
            /// 获取最近一次搜索使用的条数上限。
            /// </summary>
            public int LastSearchLimit
            {
                get;
                private set;
            }

            /// <summary>
            /// 登记一名玩家（归属区服取测试常量）。
            /// </summary>
            /// <param name="playerId">玩家标识。</param>
            /// <param name="name">展示名。</param>
            public void Add(long playerId, string name)
            {
                _entries.Add(new OnlinePlayerDirectoryEntry
                {
                    PlayerId = playerId,
                    Name = name,
                    ServerId = ServerId,
                });
            }

            /// <summary>
            /// 记录关键字与条数上限后，返回展示名含关键字的登记条目并截取至条数上限。
            /// </summary>
            /// <remarks>
            /// Records the keyword and limit, then returns the registered entries whose
            /// display name contains the keyword, truncated to the limit.
            /// </remarks>
            /// <param name="tenantId">租户标识 / Tenant id</param>
            /// <param name="appId">App 标识 / App id</param>
            /// <param name="keyword">名称关键字 / The name keyword</param>
            /// <param name="limit">返回条数上限 / The maximum number of entries</param>
            /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
            /// <returns>名称含关键字的目录条目（至多 limit 条）/ Directory entries whose name contains the keyword (at most limit)</returns>
            public Task<IReadOnlyList<OnlinePlayerDirectoryEntry>> SearchByNameAsync(long tenantId, long appId, string keyword, int limit, CancellationToken cancellationToken = default)
            {
                LastKeyword = keyword;
                LastSearchLimit = limit;
                var matched = new List<OnlinePlayerDirectoryEntry>();
                foreach (var entry in _entries)
                {
                    if (matched.Count >= limit)
                    {
                        break;
                    }

                    if (entry.Name.Contains(keyword))
                    {
                        matched.Add(entry);
                    }
                }

                return Task.FromResult<IReadOnlyList<OnlinePlayerDirectoryEntry>>(matched);
            }

            /// <summary>
            /// 返回登记条目中玩家标识命中给定集合的部分，不存在的标识不出现在结果中。
            /// </summary>
            /// <remarks>
            /// Returns the registered entries whose player id appears in the given set;
            /// unknown ids are simply absent from the result.
            /// </remarks>
            /// <param name="tenantId">租户标识 / Tenant id</param>
            /// <param name="appId">App 标识 / App id</param>
            /// <param name="playerIds">玩家标识集合 / The player ids to resolve</param>
            /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
            /// <returns>命中标识的目录条目 / The matched directory entries</returns>
            public Task<IReadOnlyList<OnlinePlayerDirectoryEntry>> FindAsync(long tenantId, long appId, IReadOnlyList<long> playerIds, CancellationToken cancellationToken = default)
            {
                var matched = new List<OnlinePlayerDirectoryEntry>();
                foreach (var entry in _entries)
                {
                    foreach (var playerId in playerIds)
                    {
                        if (playerId == entry.PlayerId)
                        {
                            matched.Add(entry);
                            break;
                        }
                    }
                }

                return Task.FromResult<IReadOnlyList<OnlinePlayerDirectoryEntry>>(matched);
            }
        }

        /// <summary>
        /// 在线事实探针桩：白名单内玩家视为在线，可在用例中途改写。
        /// </summary>
        private sealed class StubPresenceProbe : IOnlineSocialPresenceProbe
        {
            /// <summary>在线玩家集合。</summary>
            private readonly HashSet<long> _online = new HashSet<long>();

            /// <summary>
            /// 标记玩家在线。
            /// </summary>
            /// <param name="playerId">玩家标识。</param>
            public void SetOnline(long playerId)
            {
                _online.Add(playerId);
            }

            /// <summary>
            /// 仅判定玩家是否在在线白名单集合中（可用 <see cref="SetOnline"/> 在用例中途改写）。
            /// </summary>
            /// <remarks>
            /// Only checks whether the player is in the online whitelist set
            /// (mutable mid-test via <see cref="SetOnline"/>).
            /// </remarks>
            /// <param name="tenantId">租户标识 / Tenant id</param>
            /// <param name="appId">App 标识 / App id</param>
            /// <param name="playerId">玩家标识 / Player id</param>
            /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
            /// <returns>在白名单中返回 true / True when present in the whitelist</returns>
            public Task<bool> IsOnlineAsync(long tenantId, long appId, long playerId, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(_online.Contains(playerId));
            }
        }
    }
}
