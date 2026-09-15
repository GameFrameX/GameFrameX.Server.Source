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

using System.Threading.Tasks;
using GameFrameX.Online.Contracts;
using GameFrameX.Online.Identity;
using GameFrameX.Online.Scope;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 身份服务测试（vault:C3 VC-2.1/2.13：登录解析出 PlayerContext 主体、设备换绑、注销、账号合并）。
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
        /// 验证新身份自动注册并解析出完整三件套，可组装 PlayerContext（VC-2.1）。
        /// </summary>
        [Fact]
        public async Task ResolveLoginAsync_NewIdentity_ShouldAutoRegisterAndYieldContext()
        {
            // Arrange
            var service = new OnlineIdentityService(new InMemoryOnlineIdentityStore());

            // Act
            var first = await service.ResolveLoginAsync(TenantId, AppId, ServerId, OnlineIdentityKind.UserName, "alice");
            var second = await service.ResolveLoginAsync(TenantId, AppId, ServerId, OnlineIdentityKind.UserName, "alice");

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
            var service = new OnlineIdentityService(new InMemoryOnlineIdentityStore());
            var first = await service.ResolveLoginAsync(TenantId, AppId, ServerId, OnlineIdentityKind.UserName, "bob");

            // Act
            var second = await service.ResolveLoginAsync(TenantId, AppId, ServerId + 1, OnlineIdentityKind.UserName, "bob");

            // Assert
            Assert.True(second.IsSuccess);
            Assert.Equal(first.Data.GameAccount.Id, second.Data.GameAccount.Id);
            Assert.NotEqual(first.Data.Player.Id, second.Data.Player.Id);
            Assert.Equal(ServerId + 1, second.Data.Player.ServerId);
        }

        /// <summary>
        /// 验证换绑后旧设备登录被拒绝、新设备可登录（VC-2.13）。
        /// </summary>
        [Fact]
        public async Task RebindDeviceAsync_OldDevice_ShouldBeRejectedOnNextLogin()
        {
            // Arrange
            var service = new OnlineIdentityService(new InMemoryOnlineIdentityStore());
            var first = await service.ResolveLoginAsync(TenantId, AppId, ServerId, OnlineIdentityKind.UserName, "carol", null, "device-old", "iOS");

            // Act
            var rebind = await service.RebindDeviceAsync(first.Data.GameAccount.Id, "device-old", "device-new", "Android");
            var oldDeviceLogin = await service.ResolveLoginAsync(TenantId, AppId, ServerId, OnlineIdentityKind.UserName, "carol", null, "device-old", "iOS");
            var newDeviceLogin = await service.ResolveLoginAsync(TenantId, AppId, ServerId, OnlineIdentityKind.UserName, "carol", null, "device-new", "Android");

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
            var service = new OnlineIdentityService(new InMemoryOnlineIdentityStore());
            var first = await service.ResolveLoginAsync(TenantId, AppId, ServerId, OnlineIdentityKind.UserName, "dave");

            // Act
            var deactivated = await service.DeactivateAccountAsync(first.Data.GameAccount.Id, 0);
            var login = await service.ResolveLoginAsync(TenantId, AppId, ServerId, OnlineIdentityKind.UserName, "dave");

            // Assert
            Assert.True(deactivated.IsSuccess);
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
            var service = new OnlineIdentityService(new InMemoryOnlineIdentityStore());
            var source = await service.ResolveLoginAsync(TenantId, AppId, ServerId, OnlineIdentityKind.UserName, "erin");
            var target = await service.ResolveLoginAsync(TenantId, AppId, ServerId, OnlineIdentityKind.UserName, "frank");

            // Act
            var merged = await service.MergeAccountsAsync(source.Data.GameAccount.Id, target.Data.GameAccount.Id);
            var sourceLogin = await service.ResolveLoginAsync(TenantId, AppId, ServerId, OnlineIdentityKind.UserName, "erin");

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
            var service = new OnlineIdentityService(new InMemoryOnlineIdentityStore());

            // Act
            var badKind = await service.ResolveLoginAsync(TenantId, AppId, ServerId, (OnlineIdentityKind)99, "alice");
            var emptyIdentifier = await service.ResolveLoginAsync(TenantId, AppId, ServerId, OnlineIdentityKind.UserName, string.Empty);

            // Assert
            Assert.False(badKind.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, badKind.Code);
            Assert.False(emptyIdentifier.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, emptyIdentifier.Code);
        }
    }
}
