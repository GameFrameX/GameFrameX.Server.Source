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

using GameFrameX.Online.Contracts;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// OnlineErrorCode 协议派生规则测试（错误码分段、MessageKey 规范与未知码兜底，VC-1.10 Server 半边）。
    /// </summary>
    public class OnlineErrorCodeTests
    {
        /// <summary>
        /// 验证各段代表成员的 MessageKey 符合 Online.Error.{段名}.{成员名} 规范。
        /// </summary>
        [Theory]
        [InlineData(OnlineErrorCode.InternalError, "Online.Error.System.InternalError")]
        [InlineData(OnlineErrorCode.NetworkTimeout, "Online.Error.System.NetworkTimeout")]
        [InlineData(OnlineErrorCode.TokenExpired, "Online.Error.Session.TokenExpired")]
        [InlineData(OnlineErrorCode.ScopeDenied, "Online.Error.Scope.ScopeDenied")]
        [InlineData(OnlineErrorCode.ScopeMissing, "Online.Error.Scope.ScopeMissing")]
        [InlineData(OnlineErrorCode.ParameterInvalid, "Online.Error.Param.ParameterInvalid")]
        [InlineData(OnlineErrorCode.ResourceNotFound, "Online.Error.Param.ResourceNotFound")]
        [InlineData(OnlineErrorCode.StateNotReady, "Online.Error.Business.StateNotReady")]
        [InlineData(OnlineErrorCode.DuplicateRequest, "Online.Error.Idempotency.DuplicateRequest")]
        [InlineData(OnlineErrorCode.VersionConflict, "Online.Error.Idempotency.VersionConflict")]
        [InlineData(OnlineErrorCode.RateLimitExceeded, "Online.Error.RateLimit.RateLimitExceeded")]
        [InlineData(OnlineErrorCode.ServiceBusy, "Online.Error.Transient.ServiceBusy")]
        [InlineData(OnlineErrorCode.DependencyUnavailable, "Online.Error.Transient.DependencyUnavailable")]
        public void ToMessageKey_ShouldFollowSegmentMemberConvention(OnlineErrorCode errorCode, string expected)
        {
            // Act
            var messageKey = errorCode.ToMessageKey();

            // Assert
            Assert.Equal(expected, messageKey);
        }

        /// <summary>
        /// 验证成功码的 MessageKey 为空字符串。
        /// </summary>
        [Fact]
        public void ToMessageKey_None_ShouldReturnEmpty()
        {
            // Act
            var messageKey = OnlineErrorCode.None.ToMessageKey();

            // Assert
            Assert.Equal(string.Empty, messageKey);
        }

        /// <summary>
        /// 验证错误码的段号判定与八段分层一致。
        /// </summary>
        [Theory]
        [InlineData(OnlineErrorCode.InternalError, 1)]
        [InlineData(OnlineErrorCode.TokenExpired, 2)]
        [InlineData(OnlineErrorCode.ScopeDenied, 3)]
        [InlineData(OnlineErrorCode.ParameterInvalid, 4)]
        [InlineData(OnlineErrorCode.StateNotReady, 5)]
        [InlineData(OnlineErrorCode.VersionConflict, 6)]
        [InlineData(OnlineErrorCode.RateLimitExceeded, 7)]
        [InlineData(OnlineErrorCode.ServiceBusy, 8)]
        public void ToSegment_ShouldMatchEightSegmentLayout(OnlineErrorCode errorCode, int expectedSegment)
        {
            // Act
            var segment = errorCode.ToSegment();

            // Assert
            Assert.Equal(expectedSegment, segment);
        }

        /// <summary>
        /// 验证整型协议码解析：0 映射成功码，已登记码原样返回。
        /// </summary>
        [Theory]
        [InlineData(0, OnlineErrorCode.None)]
        [InlineData(1001, OnlineErrorCode.InternalError)]
        [InlineData(3005, OnlineErrorCode.ScopeMissing)]
        [InlineData(6002, OnlineErrorCode.VersionConflict)]
        [InlineData(8002, OnlineErrorCode.DependencyUnavailable)]
        public void Resolve_WithRegisteredCode_ShouldReturnOriginal(int code, OnlineErrorCode expected)
        {
            // Act
            var resolved = OnlineErrorCodeExtensions.Resolve(code);

            // Assert
            Assert.Equal(expected, resolved);
        }

        /// <summary>
        /// 验证未知码兜底：段外码与段内未登记码都映射 InternalError，不误判成功（VC-1.10）。
        /// </summary>
        [Theory]
        [InlineData(9000)]
        [InlineData(-1)]
        [InlineData(1099)]
        [InlineData(3999)]
        public void Resolve_WithUnknownCode_ShouldFallbackToInternalError(int code)
        {
            // Act
            var resolved = OnlineErrorCodeExtensions.Resolve(code);

            // Assert
            Assert.Equal(OnlineErrorCode.InternalError, resolved);
        }

        /// <summary>
        /// 验证全量成员的协议数值快照（23 个错误成员与 Admin 错误码表快照一一同构，段内顺延，禁止漂移）。
        /// </summary>
        [Fact]
        public void OnlineErrorCode_ProtocolValues_ShouldMatchSnapshot()
        {
            // Assert
            Assert.Equal(0, (int)OnlineErrorCode.None);
            Assert.Equal(1001, (int)OnlineErrorCode.InternalError);
            Assert.Equal(1002, (int)OnlineErrorCode.NetworkTimeout);
            Assert.Equal(1003, (int)OnlineErrorCode.NetworkError);
            Assert.Equal(2001, (int)OnlineErrorCode.TokenExpired);
            Assert.Equal(2002, (int)OnlineErrorCode.TokenRevoked);
            Assert.Equal(2003, (int)OnlineErrorCode.SessionInvalid);
            Assert.Equal(3001, (int)OnlineErrorCode.ScopeDenied);
            Assert.Equal(3002, (int)OnlineErrorCode.CrossTenantDenied);
            Assert.Equal(3003, (int)OnlineErrorCode.CrossAppDenied);
            Assert.Equal(3004, (int)OnlineErrorCode.ServerScopeDenied);
            Assert.Equal(3005, (int)OnlineErrorCode.ScopeMissing);
            Assert.Equal(4001, (int)OnlineErrorCode.ParameterInvalid);
            Assert.Equal(4002, (int)OnlineErrorCode.ResourceNotFound);
            Assert.Equal(5001, (int)OnlineErrorCode.StateNotReady);
            Assert.Equal(5002, (int)OnlineErrorCode.StateEnded);
            Assert.Equal(5003, (int)OnlineErrorCode.StateOperationForbidden);
            Assert.Equal(6001, (int)OnlineErrorCode.DuplicateRequest);
            Assert.Equal(6002, (int)OnlineErrorCode.VersionConflict);
            Assert.Equal(7001, (int)OnlineErrorCode.RateLimitExceeded);
            Assert.Equal(7002, (int)OnlineErrorCode.RiskControlRejected);
            Assert.Equal(7003, (int)OnlineErrorCode.AccountBanned);
            Assert.Equal(8001, (int)OnlineErrorCode.ServiceBusy);
            Assert.Equal(8002, (int)OnlineErrorCode.DependencyUnavailable);
            Assert.Equal(23, Enum.GetValues(typeof(OnlineErrorCode)).Length - 1);
        }
    }
}
