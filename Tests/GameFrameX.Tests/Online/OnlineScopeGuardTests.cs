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

using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Scope;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// OnlineScopeGuard 作用域越界判定测试（跨服 / 缺失稳定拒绝 3xxx）。
    /// </summary>
    public class OnlineScopeGuardTests
    {
        /// <summary>
        /// 验证一致作用域被接纳（返回 null）。
        /// </summary>
        [Fact]
        public void Validate_WithMatchingScope_ShouldAccept()
        {
            // Arrange
            var target = new OnlineScope(100, 10001);
            var actual = new OnlineScope(100, 10001);

            // Act
            var error = OnlineScopeGuard.Validate(target, actual);

            // Assert
            Assert.Null(error);
        }

        /// <summary>
        /// 验证区服不一致映射区服作用域拒绝码。
        /// </summary>
        [Fact]
        public void Validate_WithDifferentServer_ShouldReturnServerScopeDenied()
        {
            // Arrange
            var target = new OnlineScope(101);
            var actual = new OnlineScope(100);

            // Act
            var error = OnlineScopeGuard.Validate(target, actual);

            // Assert
            Assert.Equal(OnlineErrorCode.ServerScopeDenied, error);
        }

        /// <summary>
        /// 验证单键化后守卫只映射跨服（3004）与缺失（3005）两种拒绝；3002/3003 保留枚举位但不再产生。
        /// </summary>
        [Fact]
        public void Validate_WithAnyScopeMismatch_ShouldNeverProduceCrossTenantOrCrossApp()
        {
            // Arrange：玩家位差异不是越界（同区服不同主体合法）；区服位差异唯一越界源。
            var sameServerDifferentPlayer = OnlineScopeGuard.Validate(new OnlineScope(100), new OnlineScope(100, 10001));
            var crossServer = OnlineScopeGuard.Validate(new OnlineScope(101), new OnlineScope(100));

            // Assert
            Assert.Null(sameServerDifferentPlayer);
            Assert.Equal(OnlineErrorCode.ServerScopeDenied, crossServer);
        }

        /// <summary>
        /// 验证缺失任一作用域（null 或区服位非正值）映射作用域缺失码。
        /// </summary>
        [Theory]
        [InlineData(true, false)]
        [InlineData(false, true)]
        public void Validate_WithMissingScope_ShouldReturnScopeMissing(bool targetNull, bool actualNull)
        {
            // Arrange
            var target = targetNull ? null : new OnlineScope(100);
            var actual = actualNull ? null : new OnlineScope(100);

            // Act
            var error = OnlineScopeGuard.Validate(target, actual);

            // Assert
            Assert.Equal(OnlineErrorCode.ScopeMissing, error);
        }
    }
}
