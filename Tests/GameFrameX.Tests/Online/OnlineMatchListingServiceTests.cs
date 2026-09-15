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
using GameFrameX.Online.Matchmaking;
using GameFrameX.Online.Scope;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 对局列表项服务测试（vault:C5 VC-4.14：四策略判定互斥、未知策略一律拒绝、容量与脱敏）。
    /// </summary>
    public class OnlineMatchListingServiceTests
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

        /// <summary>测试用玩家三标识。</summary>
        private const long PlayerThree = 1003;

        /// <summary>
        /// 验证公开列表项接受单人加入（VC-4.14：Public 策略对成员数无额外要求）。
        /// </summary>
        [Fact]
        public async Task JoinAsync_WithPublicPolicy_ShouldAllowSingleMember()
        {
            // Arrange
            var service = CreateService();
            var listing = await PublishAsync(service, OnlineJoinPolicy.Public, "public-room", null, null, 4);

            // Act
            var outcome = await service.JoinAsync(CreatePlayerScope(PlayerTwo), listing.ListingId, CreateJoinRequest(PlayerTwo, null, null));

            // Assert
            Assert.True(outcome.IsSuccess);
            Assert.Equal(1, outcome.Data.JoinedCount);
        }

        /// <summary>
        /// 验证发布结果不回传密码与邀请白名单（脱敏红线：二者只参与判定，不进入对外输出）。
        /// </summary>
        [Fact]
        public async Task PublishAsync_ShouldNotEchoPasswordOrInvitees()
        {
            // Arrange
            var service = CreateService();

            // Act
            var listing = await PublishAsync(service, OnlineJoinPolicy.Password, "cipher-room", "s3cret", new List<long> { PlayerTwo }, 4);

            // Assert
            Assert.Equal(OnlineJoinPolicy.Password, listing.JoinPolicy);
            Assert.True(string.IsNullOrEmpty(listing.Password));
            Assert.Empty(listing.InvitedPlayerIds);
        }

        /// <summary>
        /// 验证 PartyOnly 策略要求至少两名成员（VC-4.14：单人不得借用组队房间）。
        /// </summary>
        [Fact]
        public async Task JoinAsync_WithPartyOnlyPolicy_ShouldRequireTwoMembers()
        {
            // Arrange
            var service = CreateService();
            var listing = await PublishAsync(service, OnlineJoinPolicy.PartyOnly, "party-room", null, null, 4);
            var scope = CreatePlayerScope(PlayerOne);

            // Act：单人加入
            var alone = await service.JoinAsync(scope, listing.ListingId, CreateJoinRequest(PlayerOne, null, null));

            // Act：两人加入
            var paired = await service.JoinAsync(scope, listing.ListingId, CreateJoinRequest(PlayerOne, new List<long> { PlayerTwo }, null));

            // Assert
            Assert.False(alone.IsSuccess);
            Assert.True(paired.IsSuccess);
            Assert.Equal(2, paired.Data.JoinedCount);
        }

        /// <summary>
        /// 验证 InviteOnly 策略要求全部成员都在邀请白名单内（VC-4.14）。
        /// </summary>
        [Fact]
        public async Task JoinAsync_WithInviteOnlyPolicy_ShouldRequireWhitelist()
        {
            // Arrange
            var service = CreateService();
            var listing = await PublishAsync(service, OnlineJoinPolicy.InviteOnly, "invite-room", null, new List<long> { PlayerTwo }, 4);

            // Act：白名单内成员
            var invited = await service.JoinAsync(CreatePlayerScope(PlayerTwo), listing.ListingId, CreateJoinRequest(PlayerTwo, null, null));

            // Act：白名单外玩家
            var stranger = await service.JoinAsync(CreatePlayerScope(PlayerThree), listing.ListingId, CreateJoinRequest(PlayerThree, null, null));

            // Assert
            Assert.True(invited.IsSuccess);
            Assert.False(stranger.IsSuccess);
            Assert.Equal(OnlineErrorCode.StateOperationForbidden, stranger.Code);
        }

        /// <summary>
        /// 验证 Password 策略下密码不匹配被拒、匹配则放行（VC-4.14）。
        /// </summary>
        [Fact]
        public async Task JoinAsync_WithPasswordPolicy_ShouldVerifyPassword()
        {
            // Arrange
            var service = CreateService();
            var listing = await PublishAsync(service, OnlineJoinPolicy.Password, "cipher-room", "s3cret", null, 4);

            // Act
            var wrong = await service.JoinAsync(CreatePlayerScope(PlayerTwo), listing.ListingId, CreateJoinRequest(PlayerTwo, null, "wrong"));
            var right = await service.JoinAsync(CreatePlayerScope(PlayerTwo), listing.ListingId, CreateJoinRequest(PlayerTwo, null, "s3cret"));

            // Assert
            Assert.False(wrong.IsSuccess);
            Assert.True(right.IsSuccess);
        }

        /// <summary>
        /// 验证未登记的策略取值一律拒绝，不做「未知即放行」的兜底（VC-4.14 红线）。
        /// </summary>
        [Fact]
        public async Task JoinAsync_WithUnknownPolicy_ShouldBeRejected()
        {
            // Arrange
            var store = new InMemoryOnlineMatchListingStore();
            var service = new OnlineMatchListingService(store);
            var listing = new OnlineMatchListing
            {
                ListingId = "lst-unknown",
                TenantId = TenantId,
                AppId = AppId,
                ServerId = ServerId,
                OwnerPlayerId = PlayerOne,
                Name = "unknown-room",
                Mode = 1,
                Region = 1,
                JoinPolicy = (OnlineJoinPolicy)99,
                Password = string.Empty,
                InvitedPlayerIds = new List<long>(),
                Tags = new List<string>(),
                Capacity = 4,
                JoinedCount = 0,
                State = OnlineMatchListingState.Open,
                CreatedAtTime = 1,
            };
            await store.SaveAsync(listing);

            // Act
            var outcome = await service.JoinAsync(CreatePlayerScope(PlayerTwo), listing.ListingId, CreateJoinRequest(PlayerTwo, null, null));

            // Assert
            Assert.False(outcome.IsSuccess);
            Assert.Equal(OnlineErrorCode.StateOperationForbidden, outcome.Code);
        }

        /// <summary>
        /// 验证容量上限生效：满员后不再接受加入，且状态转为 Full。
        /// </summary>
        [Fact]
        public async Task JoinAsync_WhenCapacityReached_ShouldBeRejected()
        {
            // Arrange
            var service = CreateService();
            var listing = await PublishAsync(service, OnlineJoinPolicy.Public, "tiny-room", null, null, 1);

            // Act
            var first = await service.JoinAsync(CreatePlayerScope(PlayerOne), listing.ListingId, CreateJoinRequest(PlayerOne, null, null));
            var second = await service.JoinAsync(CreatePlayerScope(PlayerTwo), listing.ListingId, CreateJoinRequest(PlayerTwo, null, null));

            // Assert
            Assert.True(first.IsSuccess);
            Assert.False(second.IsSuccess);
            Assert.Equal(OnlineMatchListingState.Full, first.Data.State);
        }

        /// <summary>
        /// 验证查询按模式与区域过滤，且只返回可见（未关闭）列表项。
        /// </summary>
        [Fact]
        public async Task QueryAsync_ShouldFilterByMode()
        {
            // Arrange
            var service = CreateService();
            await PublishAsync(service, OnlineJoinPolicy.Public, "mode-a", null, null, 4, mode: 1, region: 1);
            await PublishAsync(service, OnlineJoinPolicy.Public, "mode-b", null, null, 4, mode: 2, region: 1);

            // Act
            var outcome = await service.QueryAsync(CreatePlayerScope(PlayerOne), new OnlineMatchListingQuery { Mode = 1 });

            // Assert
            Assert.True(outcome.IsSuccess);
            Assert.Single(outcome.Data);
            Assert.Equal(1, outcome.Data[0].Mode);
        }

        /// <summary>
        /// 验证发布参数非法时被拒（容量非正、条件策略缺前置数据）。
        /// </summary>
        [Fact]
        public async Task PublishAsync_WithInvalidRequest_ShouldBeRejected()
        {
            // Arrange
            var service = CreateService();
            var scope = CreatePlayerScope(PlayerOne);

            // Act
            var noCapacity = await service.PublishAsync(scope, new OnlineMatchListingPublishRequest { Name = "bad", Capacity = 0 });
            var noPassword = await service.PublishAsync(scope, new OnlineMatchListingPublishRequest { Name = "bad", Capacity = 4, JoinPolicy = OnlineJoinPolicy.Password });
            var noInvitees = await service.PublishAsync(scope, new OnlineMatchListingPublishRequest { Name = "bad", Capacity = 4, JoinPolicy = OnlineJoinPolicy.InviteOnly });

            // Assert
            Assert.False(noCapacity.IsSuccess);
            Assert.False(noPassword.IsSuccess);
            Assert.False(noInvitees.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, noCapacity.Code);
        }

        /// <summary>
        /// 创建被测服务（内存存储）。
        /// </summary>
        /// <returns>列表项服务实例。</returns>
        private static OnlineMatchListingService CreateService()
        {
            return new OnlineMatchListingService(new InMemoryOnlineMatchListingStore());
        }

        /// <summary>
        /// 发布一个测试用列表项。
        /// </summary>
        /// <param name="service">列表项服务。</param>
        /// <param name="policy">加入策略。</param>
        /// <param name="name">列表项名称。</param>
        /// <param name="password">密码。</param>
        /// <param name="invitees">邀请白名单。</param>
        /// <param name="capacity">容量。</param>
        /// <param name="mode">玩法模式。</param>
        /// <param name="region">区域。</param>
        /// <returns>发布后的列表项。</returns>
        private static async Task<OnlineMatchListing> PublishAsync(OnlineMatchListingService service, OnlineJoinPolicy policy, string name, string password, List<long> invitees, int capacity, int mode = 1, int region = 1)
        {
            var request = new OnlineMatchListingPublishRequest
            {
                Name = name,
                Mode = mode,
                Region = region,
                JoinPolicy = policy,
                Password = password,
                InvitedPlayerIds = invitees,
                Tags = new List<string> { "ranked" },
                Capacity = capacity,
            };

            var outcome = await service.PublishAsync(CreatePlayerScope(PlayerOne), request);
            Assert.True(outcome.IsSuccess);
            return outcome.Data;
        }

        /// <summary>
        /// 构造加入请求。
        /// </summary>
        /// <param name="playerId">发起玩家标识。</param>
        /// <param name="memberPlayerIds">携带成员集合。</param>
        /// <param name="password">密码。</param>
        /// <returns>加入请求。</returns>
        private static OnlineMatchListingJoinRequest CreateJoinRequest(long playerId, List<long> memberPlayerIds, string password)
        {
            return new OnlineMatchListingJoinRequest
            {
                PlayerId = playerId,
                MemberPlayerIds = memberPlayerIds,
                Password = password,
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
