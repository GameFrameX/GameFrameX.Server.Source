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

using System.Linq;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.Abstractions.Audit;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Foundation.Identity;
using GameFrameX.Capability.Online.Abstractions.Scope;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 身份服务测试（登录解析出 PlayerContext 主体、设备换绑、注销、账号合并）。
    /// </summary>
    public class OnlineIdentityServiceTests
    {
        /// <summary>测试用租户标识。</summary>
        private const long TenantId = 1;

        /// <summary>测试用应用标识。</summary>
        private const long AppId = 10;

        /// <summary>测试用区服标识。</summary>
        private const long ServerId = 100;

        /// <summary>
        /// 构造带内存审计链路的身份服务（常规用例不感知审计面）。
        /// </summary>
        /// <returns>身份服务实例。</returns>
        private static OnlineIdentityService CreateService()
        {
            return new OnlineIdentityService(new InMemoryOnlineIdentityStore(), new OnlineAuditService(new InMemoryOnlineAuditStore()));
        }

        /// <summary>
        /// 验证新身份自动注册并解析出完整三件套，可组装 PlayerContext。
        /// </summary>
        [Fact]
        public async Task ResolveLoginAsync_NewIdentity_ShouldAutoRegisterAndYieldContext()
        {
            // Arrange
            var service = CreateService();

            // Act
            var first = await service.ResolveLoginAsync(new OnlineLoginRequest { TenantId = TenantId, AppId = AppId, ServerId = ServerId, Kind = OnlineIdentityKind.UserName, Identifier = "alice" });
            var second = await service.ResolveLoginAsync(new OnlineLoginRequest { TenantId = TenantId, AppId = AppId, ServerId = ServerId, Kind = OnlineIdentityKind.UserName, Identifier = "alice" });

            // Assert
            Assert.True(first.IsSuccess);
            Assert.True(second.IsSuccess);
            Assert.Equal(first.Data.GameAccount.Id, second.Data.GameAccount.Id);
            Assert.Equal(first.Data.Player.Id, second.Data.Player.Id);
            Assert.Equal(OnlineGameAccountStatus.Active, first.Data.GameAccount.Status);

            var scope = new OnlineScope(TenantId, AppId, ServerId, first.Data.Player.Id);
            var context = OnlinePlayerContext.FromScope(scope, first.Data.GameAccount.Id, "sess-1");
            Assert.Equal(TenantId, context.TenantId);
            Assert.Equal(AppId, context.AppId);
            Assert.Equal(ServerId, context.ServerId);
            Assert.Equal(first.Data.Player.Id, context.PlayerId);
            Assert.Equal(first.Data.GameAccount.Id, context.GameAccountId);
        }

        /// <summary>
        /// 验证同身份跨区服登录复用账号但生成新区服玩家。
        /// </summary>
        [Fact]
        public async Task ResolveLoginAsync_NewServer_ShouldReuseAccountAndCreateServerPlayer()
        {
            // Arrange
            var service = CreateService();
            var first = await service.ResolveLoginAsync(new OnlineLoginRequest { TenantId = TenantId, AppId = AppId, ServerId = ServerId, Kind = OnlineIdentityKind.UserName, Identifier = "bob" });

            // Act
            var second = await service.ResolveLoginAsync(new OnlineLoginRequest { TenantId = TenantId, AppId = AppId, ServerId = ServerId + 1, Kind = OnlineIdentityKind.UserName, Identifier = "bob" });

            // Assert
            Assert.True(second.IsSuccess);
            Assert.Equal(first.Data.GameAccount.Id, second.Data.GameAccount.Id);
            Assert.NotEqual(first.Data.Player.Id, second.Data.Player.Id);
            Assert.Equal(ServerId + 1, second.Data.Player.ServerId);
        }

        /// <summary>
        /// 验证换绑后旧设备登录被拒绝、新设备可登录。
        /// </summary>
        [Fact]
        public async Task RebindDeviceAsync_OldDevice_ShouldBeRejectedOnNextLogin()
        {
            // Arrange
            var service = CreateService();
            var first = await service.ResolveLoginAsync(new OnlineLoginRequest { TenantId = TenantId, AppId = AppId, ServerId = ServerId, Kind = OnlineIdentityKind.UserName, Identifier = "carol", DeviceIdentifier = "device-old", DevicePlatform = "iOS" });

            // Act
            var rebind = await service.RebindDeviceAsync(first.Data.GameAccount.Id, "device-old", "device-new", "Android");
            var oldDeviceLogin = await service.ResolveLoginAsync(new OnlineLoginRequest { TenantId = TenantId, AppId = AppId, ServerId = ServerId, Kind = OnlineIdentityKind.UserName, Identifier = "carol", DeviceIdentifier = "device-old", DevicePlatform = "iOS" });
            var newDeviceLogin = await service.ResolveLoginAsync(new OnlineLoginRequest { TenantId = TenantId, AppId = AppId, ServerId = ServerId, Kind = OnlineIdentityKind.UserName, Identifier = "carol", DeviceIdentifier = "device-new", DevicePlatform = "Android" });

            // Assert
            Assert.True(rebind.IsSuccess);
            Assert.False(oldDeviceLogin.IsSuccess);
            Assert.Equal(OnlineErrorCode.StateOperationForbidden, oldDeviceLogin.Code);
            Assert.True(newDeviceLogin.IsSuccess);
        }

        /// <summary>
        /// 验证注销账号后登录被拒绝（保留期内）。
        /// </summary>
        [Fact]
        public async Task DeactivateAccountAsync_SubsequentLogin_ShouldBeRejected()
        {
            // Arrange
            var service = CreateService();
            var first = await service.ResolveLoginAsync(new OnlineLoginRequest { TenantId = TenantId, AppId = AppId, ServerId = ServerId, Kind = OnlineIdentityKind.UserName, Identifier = "dave" });

            // Act
            var deactivated = await service.DeactivateAccountAsync(first.Data.GameAccount.Id, 0);
            var login = await service.ResolveLoginAsync(new OnlineLoginRequest { TenantId = TenantId, AppId = AppId, ServerId = ServerId, Kind = OnlineIdentityKind.UserName, Identifier = "dave" });

            // Assert
            Assert.True(deactivated.IsSuccess);
            Assert.False(login.IsSuccess);
            Assert.Equal(OnlineErrorCode.StateOperationForbidden, login.Code);
        }

        /// <summary>
        /// 验证已解绑身份重新登录走复活路径：重绑回原账号，不新建账号（资产不成孤儿）。
        /// </summary>
        [Fact]
        public async Task ResolveLoginAsync_UnboundIdentity_ShouldReviveOriginalAccount()
        {
            // Arrange：账号绑定两个身份后解绑主身份（保留辅身份满足解绑前提）。
            var store = new InMemoryOnlineIdentityStore();
            var service = new OnlineIdentityService(store);
            var first = await service.ResolveLoginAsync(new OnlineLoginRequest { TenantId = TenantId, AppId = AppId, ServerId = ServerId, Kind = OnlineIdentityKind.UserName, Identifier = "grace" });
            var originalAccountId = first.Data.GameAccount.Id;
            Assert.True((await service.BindIdentityAsync(TenantId, AppId, originalAccountId, OnlineIdentityKind.Email, "grace@example.com")).IsSuccess);
            Assert.True((await service.UnbindIdentityAsync(TenantId, AppId, OnlineIdentityKind.UserName, "grace")).IsSuccess);

            // Act
            var reviveLogin = await service.ResolveLoginAsync(new OnlineLoginRequest { TenantId = TenantId, AppId = AppId, ServerId = ServerId, Kind = OnlineIdentityKind.UserName, Identifier = "grace" });

            // Assert：复用原账号与原玩家，未产生新账号。
            Assert.True(reviveLogin.IsSuccess);
            Assert.Equal(originalAccountId, reviveLogin.Data.GameAccount.Id);
            Assert.Equal(first.Data.Player.Id, reviveLogin.Data.Player.Id);
            var revivedIdentity = await store.FindIdentityAsync(TenantId, AppId, OnlineIdentityKind.UserName, "grace", CancellationToken.None);
            Assert.Equal(0, revivedIdentity.UnboundAtTime);
            Assert.Equal(originalAccountId, revivedIdentity.GameAccountId);
        }

        /// <summary>
        /// 验证已解绑身份的原账号注销保留期内：复活路径拒绝登录，不静默新建账号。
        /// </summary>
        [Fact]
        public async Task ResolveLoginAsync_UnboundIdentity_DeactivatedAccount_ShouldBeRejected()
        {
            // Arrange
            var service = new OnlineIdentityService(new InMemoryOnlineIdentityStore());
            var first = await service.ResolveLoginAsync(new OnlineLoginRequest { TenantId = TenantId, AppId = AppId, ServerId = ServerId, Kind = OnlineIdentityKind.UserName, Identifier = "heidi" });
            Assert.True((await service.BindIdentityAsync(TenantId, AppId, first.Data.GameAccount.Id, OnlineIdentityKind.Email, "heidi@example.com")).IsSuccess);
            Assert.True((await service.UnbindIdentityAsync(TenantId, AppId, OnlineIdentityKind.UserName, "heidi")).IsSuccess);
            Assert.True((await service.DeactivateAccountAsync(first.Data.GameAccount.Id, 0)).IsSuccess);

            // Act
            var login = await service.ResolveLoginAsync(new OnlineLoginRequest { TenantId = TenantId, AppId = AppId, ServerId = ServerId, Kind = OnlineIdentityKind.UserName, Identifier = "heidi" });

            // Assert
            Assert.False(login.IsSuccess);
            Assert.Equal(OnlineErrorCode.StateOperationForbidden, login.Code);
        }


        /// <summary>
        /// 验证账号合并：身份与玩家迁移到目标账号，源身份登录跟随目标（单跳）。
        /// </summary>
        [Fact]
        public async Task MergeAccountsAsync_SourceIdentities_ShouldFollowTargetAccount()
        {
            // Arrange
            var service = CreateService();
            var source = await service.ResolveLoginAsync(new OnlineLoginRequest { TenantId = TenantId, AppId = AppId, ServerId = ServerId, Kind = OnlineIdentityKind.UserName, Identifier = "erin" });
            var target = await service.ResolveLoginAsync(new OnlineLoginRequest { TenantId = TenantId, AppId = AppId, ServerId = ServerId, Kind = OnlineIdentityKind.UserName, Identifier = "frank" });

            // Act
            var merged = await service.MergeAccountsAsync(source.Data.GameAccount.Id, target.Data.GameAccount.Id);
            var sourceLogin = await service.ResolveLoginAsync(new OnlineLoginRequest { TenantId = TenantId, AppId = AppId, ServerId = ServerId, Kind = OnlineIdentityKind.UserName, Identifier = "erin" });

            // Assert：合并后源身份登录跟随目标账号，源玩家档案随合并迁移保留（归属目标账号）。
            Assert.True(merged.IsSuccess);
            Assert.True(sourceLogin.IsSuccess);
            Assert.Equal(target.Data.GameAccount.Id, sourceLogin.Data.GameAccount.Id);
            Assert.Equal(source.Data.Player.Id, sourceLogin.Data.Player.Id);
            Assert.Equal(target.Data.GameAccount.Id, sourceLogin.Data.Player.GameAccountId);
            Assert.Equal(OnlineGameAccountStatus.Merged, source.Data.GameAccount.Status);
        }

        /// <summary>
        /// 验证参数校验：未定义身份类型与空标识被拒绝。
        /// </summary>
        [Fact]
        public async Task ResolveLoginAsync_InvalidArguments_ShouldBeRejected()
        {
            // Arrange
            var service = CreateService();

            // Act
            var badKind = await service.ResolveLoginAsync(new OnlineLoginRequest { TenantId = TenantId, AppId = AppId, ServerId = ServerId, Kind = (OnlineIdentityKind)99, Identifier = "alice" });
            var emptyIdentifier = await service.ResolveLoginAsync(new OnlineLoginRequest { TenantId = TenantId, AppId = AppId, ServerId = ServerId, Kind = OnlineIdentityKind.UserName, Identifier = string.Empty });

            // Assert
            Assert.False(badKind.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, badKind.Code);
            Assert.False(emptyIdentifier.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, emptyIdentifier.Code);
        }

        /// <summary>
        /// 验证已解绑身份跨账号改绑的完整授权链：无票据拒绝、有效票据成功且审计在档、同票据二次使用拒绝。
        /// </summary>
        [Fact]
        public async Task BindIdentityAsync_CrossAccountUnboundIdentity_TicketLifecycle_ShouldEnforceAuthorization()
        {
            // Arrange：账号 A（alice + 备用邮箱身份）、账号 B；邮箱身份解绑后归属仍为 A。
            var store = new InMemoryOnlineIdentityStore();
            var auditStore = new InMemoryOnlineAuditStore();
            var service = new OnlineIdentityService(store, new OnlineAuditService(auditStore));
            var accountA = await service.ResolveLoginAsync(new OnlineLoginRequest { TenantId = TenantId, AppId = AppId, ServerId = ServerId, Kind = OnlineIdentityKind.UserName, Identifier = "alice" });
            var accountB = await service.ResolveLoginAsync(new OnlineLoginRequest { TenantId = TenantId, AppId = AppId, ServerId = ServerId, Kind = OnlineIdentityKind.UserName, Identifier = "bobby" });
            var accountC = await service.ResolveLoginAsync(new OnlineLoginRequest { TenantId = TenantId, AppId = AppId, ServerId = ServerId, Kind = OnlineIdentityKind.UserName, Identifier = "carol" });
            Assert.True((await service.BindIdentityAsync(TenantId, AppId, accountA.Data.GameAccount.Id, OnlineIdentityKind.Email, "alice@example.com")).IsSuccess);
            Assert.True((await service.UnbindIdentityAsync(TenantId, AppId, OnlineIdentityKind.Email, "alice@example.com")).IsSuccess);

            // Act / Assert ①：无票据 → StateOperationForbidden，身份仍指向 A。
            var withoutTicket = await service.BindIdentityAsync(TenantId, AppId, accountB.Data.GameAccount.Id, OnlineIdentityKind.Email, "alice@example.com");
            Assert.False(withoutTicket.IsSuccess);
            Assert.Equal(OnlineErrorCode.StateOperationForbidden, withoutTicket.Code);
            Assert.Equal(accountA.Data.GameAccount.Id, (await store.FindIdentityAsync(TenantId, AppId, OnlineIdentityKind.Email, "alice@example.com")).GameAccountId);

            // Act / Assert ②：A 签发票据 → B 携票据改绑成功，身份指向 B，审计条目在档。
            var authorization = await service.IssueIdentityRebindAuthorizationAsync(TenantId, AppId, accountA.Data.GameAccount.Id, OnlineIdentityKind.Email, "alice@example.com");
            Assert.True(authorization.IsSuccess);
            var withTicket = await service.BindIdentityAsync(TenantId, AppId, accountB.Data.GameAccount.Id, OnlineIdentityKind.Email, "alice@example.com", authorization.Data.Code);
            Assert.True(withTicket.IsSuccess);
            var rebound = await store.FindIdentityAsync(TenantId, AppId, OnlineIdentityKind.Email, "alice@example.com");
            Assert.Equal(accountB.Data.GameAccount.Id, rebound.GameAccountId);
            Assert.Equal(0, rebound.UnboundAtTime);
            var auditRecords = await auditStore.ListAsync(TenantId, AppId);
            var auditRecord = auditRecords.Single(record => record.EventType == "online.identity.rebound");
            Assert.NotNull(auditRecord);
            Assert.Equal(OnlineAuditDomain.Operation, auditRecord.Domain);
            Assert.Equal(authorization.Data.TicketId, auditRecord.CorrelationId);
            Assert.Equal("game-account-" + accountA.Data.GameAccount.Id, auditRecord.OperatorId);
            Assert.Contains(auditRecord.SanitizedFields, pair => pair.Key == "originalGameAccountId" && pair.Value == accountA.Data.GameAccount.Id.ToString());
            Assert.Contains(auditRecord.SanitizedFields, pair => pair.Key == "newGameAccountId" && pair.Value == accountB.Data.GameAccount.Id.ToString());

            // Act / Assert ③：同一票据第二次使用被拒——B 解绑后第三方（carol）携已消费票据改绑 → 拒绝，身份仍归 B。
            Assert.True((await service.UnbindIdentityAsync(TenantId, AppId, OnlineIdentityKind.Email, "alice@example.com")).IsSuccess);
            var reusedTicket = await service.BindIdentityAsync(TenantId, AppId, accountC.Data.GameAccount.Id, OnlineIdentityKind.Email, "alice@example.com", authorization.Data.Code);
            Assert.False(reusedTicket.IsSuccess);
            Assert.Equal(OnlineErrorCode.StateOperationForbidden, reusedTicket.Code);
            Assert.Equal(accountB.Data.GameAccount.Id, (await store.FindIdentityAsync(TenantId, AppId, OnlineIdentityKind.Email, "alice@example.com")).GameAccountId);
        }

        /// <summary>
        /// 验证原账号重绑自己解绑过的身份无需票据即可成功。
        /// </summary>
        [Fact]
        public async Task BindIdentityAsync_OwnerRebindOwnUnboundIdentity_ShouldSucceedWithoutTicket()
        {
            // Arrange
            var store = new InMemoryOnlineIdentityStore();
            var service = new OnlineIdentityService(store, new OnlineAuditService(new InMemoryOnlineAuditStore()));
            var accountA = await service.ResolveLoginAsync(new OnlineLoginRequest { TenantId = TenantId, AppId = AppId, ServerId = ServerId, Kind = OnlineIdentityKind.UserName, Identifier = "alice" });
            Assert.True((await service.BindIdentityAsync(TenantId, AppId, accountA.Data.GameAccount.Id, OnlineIdentityKind.Email, "alice@example.com")).IsSuccess);
            Assert.True((await service.UnbindIdentityAsync(TenantId, AppId, OnlineIdentityKind.Email, "alice@example.com")).IsSuccess);

            // Act
            var rebound = await service.BindIdentityAsync(TenantId, AppId, accountA.Data.GameAccount.Id, OnlineIdentityKind.Email, "alice@example.com");

            // Assert
            Assert.True(rebound.IsSuccess);
            var identity = await store.FindIdentityAsync(TenantId, AppId, OnlineIdentityKind.Email, "alice@example.com");
            Assert.Equal(accountA.Data.GameAccount.Id, identity.GameAccountId);
            Assert.Equal(0, identity.UnboundAtTime);
        }

        /// <summary>
        /// 验证两个账号并发绑定同一全新身份恰一胜者（败者 VersionConflict 零覆写）。
        /// </summary>
        [Fact]
        public async Task BindIdentityAsync_ConcurrentSameNewIdentity_ExactlyOneWinner()
        {
            // Arrange
            var store = new InMemoryOnlineIdentityStore();
            var service = new OnlineIdentityService(store, new OnlineAuditService(new InMemoryOnlineAuditStore()));
            var accountA = await service.ResolveLoginAsync(new OnlineLoginRequest { TenantId = TenantId, AppId = AppId, ServerId = ServerId, Kind = OnlineIdentityKind.UserName, Identifier = "alice" });
            var accountB = await service.ResolveLoginAsync(new OnlineLoginRequest { TenantId = TenantId, AppId = AppId, ServerId = ServerId, Kind = OnlineIdentityKind.UserName, Identifier = "bobby" });

            // Act：真并发双绑定（InMemory 全锁原子，恰一胜者确定性成立）。
            var results = await Task.WhenAll(
                service.BindIdentityAsync(TenantId, AppId, accountA.Data.GameAccount.Id, OnlineIdentityKind.Email, "race@example.com"),
                service.BindIdentityAsync(TenantId, AppId, accountB.Data.GameAccount.Id, OnlineIdentityKind.Email, "race@example.com"));

            // Assert：恰一成功；败者拒绝码取决于交错时序——双查皆 miss 后唯一插入冲突为 VersionConflict，
            // 后查者见到已绑定行则按归属拒绝为 StateOperationForbidden，两者都是诚实的败者回执。
            Assert.Equal(1, results.Count(result => result.IsSuccess));
            var loser = results.Single(result => !result.IsSuccess);
            Assert.Contains(loser.Code, new[] { OnlineErrorCode.VersionConflict, OnlineErrorCode.StateOperationForbidden });
            var identity = await store.FindIdentityAsync(TenantId, AppId, OnlineIdentityKind.Email, "race@example.com");
            Assert.Contains(identity.GameAccountId, new[] { accountA.Data.GameAccount.Id, accountB.Data.GameAccount.Id });
        }

        /// <summary>
        /// 验证过期票据被拒绝（有效期 0 = 签发即过期）。
        /// </summary>
        [Fact]
        public async Task BindIdentityAsync_ExpiredTicket_ShouldBeRejected()
        {
            // Arrange
            var store = new InMemoryOnlineIdentityStore();
            var service = new OnlineIdentityService(store, new OnlineAuditService(new InMemoryOnlineAuditStore()), 0);
            var accountA = await service.ResolveLoginAsync(new OnlineLoginRequest { TenantId = TenantId, AppId = AppId, ServerId = ServerId, Kind = OnlineIdentityKind.UserName, Identifier = "alice" });
            var accountB = await service.ResolveLoginAsync(new OnlineLoginRequest { TenantId = TenantId, AppId = AppId, ServerId = ServerId, Kind = OnlineIdentityKind.UserName, Identifier = "bobby" });
            Assert.True((await service.BindIdentityAsync(TenantId, AppId, accountA.Data.GameAccount.Id, OnlineIdentityKind.Email, "alice@example.com")).IsSuccess);
            Assert.True((await service.UnbindIdentityAsync(TenantId, AppId, OnlineIdentityKind.Email, "alice@example.com")).IsSuccess);
            var authorization = await service.IssueIdentityRebindAuthorizationAsync(TenantId, AppId, accountA.Data.GameAccount.Id, OnlineIdentityKind.Email, "alice@example.com");
            Assert.True(authorization.IsSuccess);

            // Act
            var expired = await service.BindIdentityAsync(TenantId, AppId, accountB.Data.GameAccount.Id, OnlineIdentityKind.Email, "alice@example.com", authorization.Data.Code);

            // Assert
            Assert.False(expired.IsSuccess);
            Assert.Equal(OnlineErrorCode.StateOperationForbidden, expired.Code);
            Assert.Equal(accountA.Data.GameAccount.Id, (await store.FindIdentityAsync(TenantId, AppId, OnlineIdentityKind.Email, "alice@example.com")).GameAccountId);
        }

        /// <summary>
        /// 验证原账号注销后跨账号改绑被拒（票据已签发也不例外——走客服/管理后台）。
        /// </summary>
        [Fact]
        public async Task BindIdentityAsync_OriginalAccountDeactivated_ShouldBeRejected()
        {
            // Arrange
            var store = new InMemoryOnlineIdentityStore();
            var service = new OnlineIdentityService(store, new OnlineAuditService(new InMemoryOnlineAuditStore()));
            var accountA = await service.ResolveLoginAsync(new OnlineLoginRequest { TenantId = TenantId, AppId = AppId, ServerId = ServerId, Kind = OnlineIdentityKind.UserName, Identifier = "alice" });
            var accountB = await service.ResolveLoginAsync(new OnlineLoginRequest { TenantId = TenantId, AppId = AppId, ServerId = ServerId, Kind = OnlineIdentityKind.UserName, Identifier = "bobby" });
            Assert.True((await service.BindIdentityAsync(TenantId, AppId, accountA.Data.GameAccount.Id, OnlineIdentityKind.Email, "alice@example.com")).IsSuccess);
            Assert.True((await service.UnbindIdentityAsync(TenantId, AppId, OnlineIdentityKind.Email, "alice@example.com")).IsSuccess);
            var authorization = await service.IssueIdentityRebindAuthorizationAsync(TenantId, AppId, accountA.Data.GameAccount.Id, OnlineIdentityKind.Email, "alice@example.com");
            Assert.True(authorization.IsSuccess);
            Assert.True((await service.DeactivateAccountAsync(accountA.Data.GameAccount.Id, 0)).IsSuccess);

            // Act
            var withTicket = await service.BindIdentityAsync(TenantId, AppId, accountB.Data.GameAccount.Id, OnlineIdentityKind.Email, "alice@example.com", authorization.Data.Code);
            var withoutTicket = await service.BindIdentityAsync(TenantId, AppId, accountB.Data.GameAccount.Id, OnlineIdentityKind.Email, "alice@example.com");

            // Assert
            Assert.False(withTicket.IsSuccess);
            Assert.Equal(OnlineErrorCode.StateOperationForbidden, withTicket.Code);
            Assert.False(withoutTicket.IsSuccess);
            Assert.Equal(OnlineErrorCode.StateOperationForbidden, withoutTicket.Code);
            Assert.Equal(accountA.Data.GameAccount.Id, (await store.FindIdentityAsync(TenantId, AppId, OnlineIdentityKind.Email, "alice@example.com")).GameAccountId);
        }

        /// <summary>
        /// 验证票据签发的归属校验：非身份归属账号签发被拒、身份不存在被拒、票据作用域不匹配的改绑被拒。
        /// </summary>
        [Fact]
        public async Task IssueIdentityRebindAuthorizationAsync_OwnershipAndScope_ShouldBeEnforced()
        {
            // Arrange
            var store = new InMemoryOnlineIdentityStore();
            var service = new OnlineIdentityService(store, new OnlineAuditService(new InMemoryOnlineAuditStore()));
            var accountA = await service.ResolveLoginAsync(new OnlineLoginRequest { TenantId = TenantId, AppId = AppId, ServerId = ServerId, Kind = OnlineIdentityKind.UserName, Identifier = "alice" });
            var accountB = await service.ResolveLoginAsync(new OnlineLoginRequest { TenantId = TenantId, AppId = AppId, ServerId = ServerId, Kind = OnlineIdentityKind.UserName, Identifier = "bobby" });
            Assert.True((await service.BindIdentityAsync(TenantId, AppId, accountA.Data.GameAccount.Id, OnlineIdentityKind.Email, "alice@example.com")).IsSuccess);
            Assert.True((await service.BindIdentityAsync(TenantId, AppId, accountA.Data.GameAccount.Id, OnlineIdentityKind.Email, "alice-alt@example.com")).IsSuccess);
            Assert.True((await service.UnbindIdentityAsync(TenantId, AppId, OnlineIdentityKind.Email, "alice@example.com")).IsSuccess);
            Assert.True((await service.UnbindIdentityAsync(TenantId, AppId, OnlineIdentityKind.Email, "alice-alt@example.com")).IsSuccess);

            // Act / Assert ①：非归属账号（B）签发 → 拒绝。
            var nonOwner = await service.IssueIdentityRebindAuthorizationAsync(TenantId, AppId, accountB.Data.GameAccount.Id, OnlineIdentityKind.Email, "alice@example.com");
            Assert.False(nonOwner.IsSuccess);
            Assert.Equal(OnlineErrorCode.StateOperationForbidden, nonOwner.Code);

            // Act / Assert ②：身份不存在 → 拒绝。
            var unknownIdentity = await service.IssueIdentityRebindAuthorizationAsync(TenantId, AppId, accountA.Data.GameAccount.Id, OnlineIdentityKind.Email, "nobody@example.com");
            Assert.False(unknownIdentity.IsSuccess);
            Assert.Equal(OnlineErrorCode.ResourceNotFound, unknownIdentity.Code);

            // Act / Assert ③：A 为 alice@example.com 签发票据，B 拿它改绑另一个身份 → 作用域不匹配拒绝。
            var authorization = await service.IssueIdentityRebindAuthorizationAsync(TenantId, AppId, accountA.Data.GameAccount.Id, OnlineIdentityKind.Email, "alice@example.com");
            Assert.True(authorization.IsSuccess);
            var mismatched = await service.BindIdentityAsync(TenantId, AppId, accountB.Data.GameAccount.Id, OnlineIdentityKind.Email, "alice-alt@example.com", authorization.Data.Code);
            Assert.False(mismatched.IsSuccess);
            Assert.Equal(OnlineErrorCode.StateOperationForbidden, mismatched.Code);
        }

        /// <summary>
        /// 验证 InMemory 身份存储条件写原语：唯一插入冲突零覆写；CAS 匹配写入并推进版本、过期版本拒绝零写入。
        /// </summary>
        [Fact]
        public async Task InMemoryStore_IdentityConditionalWrites_ShouldMatchContract()
        {
            // Arrange
            var store = new InMemoryOnlineIdentityStore();
            var identity = new OnlineIdentity
            {
                Id = "ident-1",
                TenantId = TenantId,
                AppId = AppId,
                Kind = OnlineIdentityKind.Email,
                Identifier = "primitive@example.com",
                GameAccountId = 101,
                CreatedAtTime = 1000,
                UpdatedAtTime = 1000,
            };

            // Act / Assert ①：唯一插入成功建档版本 1；同键再插 false 且零覆写。
            Assert.True(await store.TryAddIdentityAsync(identity));
            var winner = await store.FindIdentityAsync(TenantId, AppId, OnlineIdentityKind.Email, "primitive@example.com");
            Assert.Equal(1, winner.RowVersion);
            var contender = new OnlineIdentity { Id = "ident-2", TenantId = TenantId, AppId = AppId, Kind = OnlineIdentityKind.Email, Identifier = "primitive@example.com", GameAccountId = 202 };
            Assert.False(await store.TryAddIdentityAsync(contender));
            Assert.Equal(101, (await store.FindIdentityAsync(TenantId, AppId, OnlineIdentityKind.Email, "primitive@example.com")).GameAccountId);

            // Act / Assert ②：CAS 匹配版本写入并 +1；过期版本拒绝零写入。
            var rebound = new OnlineIdentity { Id = "ident-1", TenantId = TenantId, AppId = AppId, Kind = OnlineIdentityKind.Email, Identifier = "primitive@example.com", GameAccountId = 202 };
            Assert.True(await store.TryUpdateIdentityIfRowVersionAsync(rebound, winner.RowVersion));
            var updated = await store.FindIdentityAsync(TenantId, AppId, OnlineIdentityKind.Email, "primitive@example.com");
            Assert.Equal(202, updated.GameAccountId);
            Assert.Equal(2, updated.RowVersion);
            var staleAttempt = new OnlineIdentity { Id = "ident-1", TenantId = TenantId, AppId = AppId, Kind = OnlineIdentityKind.Email, Identifier = "primitive@example.com", GameAccountId = 303 };
            Assert.False(await store.TryUpdateIdentityIfRowVersionAsync(staleAttempt, 1));
            Assert.Equal(202, (await store.FindIdentityAsync(TenantId, AppId, OnlineIdentityKind.Email, "primitive@example.com")).GameAccountId);

            // Act / Assert ③：盲写保留存量版本号（不推进，不回退）。
            var blind = new OnlineIdentity { Id = "ident-1", TenantId = TenantId, AppId = AppId, Kind = OnlineIdentityKind.Email, Identifier = "primitive@example.com", GameAccountId = 404 };
            await store.AddOrUpdateIdentityAsync(blind);
            var afterBlindWrite = await store.FindIdentityAsync(TenantId, AppId, OnlineIdentityKind.Email, "primitive@example.com");
            Assert.Equal(404, afterBlindWrite.GameAccountId);
            Assert.Equal(2, afterBlindWrite.RowVersion);
        }
    }
}
