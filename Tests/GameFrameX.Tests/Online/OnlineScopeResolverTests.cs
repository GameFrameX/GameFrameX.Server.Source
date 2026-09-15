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
//   Any legal disputes and liabilities arising from secondary development based on this project
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
//  ==========================================================================================

using System.Collections.Generic;
using GameFrameX.Online.Contracts;
using GameFrameX.Online.Scope;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// OnlineScopeResolver 作用域解析测试（鉴权上下文优先、客户端字段不可覆盖，VC-1.8；缺失拒 3xxx）。
    /// </summary>
    public class OnlineScopeResolverTests
    {
        /// <summary>
        /// 验证 GET 查询参数 camelCase 形态提取后仍以鉴权上下文为准。
        /// </summary>
        [Fact]
        public void TryResolve_WithCamelCaseParameters_ShouldReturnAuthorizedScope()
        {
            // Arrange
            var authorized = new OnlineScope(1, 10, 100, 10001);
            var parameters = new Dictionary<string, object>
            {
                { "tenantId", 1L },
                { "appId", 10L },
                { "serverId", 100L },
            };

            // Act
            var resolved = OnlineScopeResolver.TryResolve(parameters, authorized, out var error);

            // Assert
            Assert.Equal(OnlineErrorCode.None, error);
            Assert.NotNull(resolved);
            Assert.Equal(1, resolved.TenantId);
            Assert.Equal(10, resolved.AppId);
            Assert.Equal(100, resolved.ServerId);
            Assert.Equal(10001, resolved.PlayerId);
        }

        /// <summary>
        /// 验证 POST body PascalCase 形态字段同样可提取（键大小写不敏感）。
        /// </summary>
        [Fact]
        public void TryResolve_WithPascalCaseParameters_ShouldMatchCaseInsensitively()
        {
            // Arrange
            var authorized = new OnlineScope(2, 20, 200);
            var parameters = new Dictionary<string, object>
            {
                { "TenantId", "2" },
                { "AppId", "20" },
                { "ServerId", "200" },
            };

            // Act
            var resolved = OnlineScopeResolver.TryResolve(parameters, authorized, out var error);

            // Assert
            Assert.Equal(OnlineErrorCode.None, error);
            Assert.NotNull(resolved);
            Assert.Equal(2, resolved.TenantId);
            Assert.Equal(20, resolved.AppId);
            Assert.Equal(200, resolved.ServerId);
        }

        /// <summary>
        /// 验证客户端伪造的作用域字段被鉴权上下文覆盖，不产生越权生效值（VC-1.8）。
        /// </summary>
        [Fact]
        public void TryResolve_WithForgedClientFields_ShouldEnforceAuthorizedValues()
        {
            // Arrange
            var authorized = new OnlineScope(1, 10, 100, 10001);
            var parameters = new Dictionary<string, object>
            {
                { "tenantId", 999L },
                { "appId", 999L },
                { "serverId", 999L },
            };

            // Act
            var resolved = OnlineScopeResolver.TryResolve(parameters, authorized, out var error);

            // Assert
            Assert.Equal(OnlineErrorCode.None, error);
            Assert.NotNull(resolved);
            Assert.Equal(1, resolved.TenantId);
            Assert.Equal(10, resolved.AppId);
            Assert.Equal(100, resolved.ServerId);
            Assert.Equal(10001, resolved.PlayerId);
        }

        /// <summary>
        /// 验证缺失鉴权上下文时返回作用域缺失错误。
        /// </summary>
        [Fact]
        public void TryResolve_WithoutAuthorizedScope_ShouldReturnScopeMissing()
        {
            // Act
            var resolved = OnlineScopeResolver.TryResolve(new Dictionary<string, object>(), null, out var error);

            // Assert
            Assert.Equal(OnlineErrorCode.ScopeMissing, error);
            Assert.Null(resolved);
        }

        /// <summary>
        /// 验证鉴权上下文三元组不完整（非正值）时返回作用域缺失错误。
        /// </summary>
        [Theory]
        [InlineData(0, 10, 100)]
        [InlineData(1, 0, 100)]
        [InlineData(1, 10, 0)]
        public void TryResolve_WithIncompleteAuthorizedScope_ShouldReturnScopeMissing(long tenantId, long appId, long serverId)
        {
            // Arrange
            var authorized = new OnlineScope(tenantId, appId, serverId);

            // Act
            var resolved = OnlineScopeResolver.TryResolve(null, authorized, out var error);

            // Assert
            Assert.Equal(OnlineErrorCode.ScopeMissing, error);
            Assert.Null(resolved);
        }

        /// <summary>
        /// 验证 EnforceAuthorized 恒返回鉴权上下文值（客户端声称被整体忽略）。
        /// </summary>
        [Fact]
        public void EnforceAuthorized_ShouldAlwaysReturnAuthorizedScope()
        {
            // Arrange
            var authorized = new OnlineScope(1, 10, 100, 10001);
            var claimed = new OnlineScope(999, 999, 999, 999);

            // Act
            var enforced = OnlineScopeResolver.EnforceAuthorized(authorized, claimed);

            // Assert
            Assert.Same(authorized, enforced);
        }

        /// <summary>
        /// 验证作用域键格式：含玩家位与不含玩家位两种形态。
        /// </summary>
        [Fact]
        public void ToScopeKey_ShouldIncludePlayerSegmentOnlyWhenRequested()
        {
            // Arrange
            var scope = new OnlineScope(1, 10, 100, 10001);

            // Act
            var withoutPlayer = scope.ToScopeKey(false);
            var withPlayer = scope.ToScopeKey(true);

            // Assert
            Assert.Equal("online:1:10:100", withoutPlayer);
            Assert.Equal("online:1:10:100:10001", withPlayer);
        }
    }
}
