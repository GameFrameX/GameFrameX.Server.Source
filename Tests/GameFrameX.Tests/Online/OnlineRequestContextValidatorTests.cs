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
    /// OnlineRequestContextValidator 请求上下文校验测试（缺上下文拒 4xxx，副作用请求缺幂等键拒 4xxx，VC-1.1）。
    /// </summary>
    public class OnlineRequestContextValidatorTests
    {
        /// <summary>
        /// 构造一个字段完整的合法请求上下文。
        /// </summary>
        private static OnlineRequestContext CreateValidContext()
        {
            return new OnlineRequestContext
            {
                RequestId = "req-0001",
                ProtocolVersion = OnlineRequestContextValidator.MinimumProtocolVersion,
                ClientVersion = "1.0.0",
                Timestamp = 1760000000000,
                IdempotencyKey = "order-create-001",
            };
        }

        /// <summary>
        /// 验证合法上下文（副作用请求携带合法幂等键）校验通过。
        /// </summary>
        [Fact]
        public void Validate_WithValidContextAndSideEffect_ShouldPass()
        {
            // Arrange
            var context = CreateValidContext();

            // Act
            var error = OnlineRequestContextValidator.Validate(context, true);

            // Assert
            Assert.Null(error);
        }

        /// <summary>
        /// 验证缺失上下文（null）被拒为参数错误（VC-1.1 缺上下文）。
        /// </summary>
        [Fact]
        public void Validate_WithNullContext_ShouldReturnParameterInvalid()
        {
            // Act
            var error = OnlineRequestContextValidator.Validate(null, false);

            // Assert
            Assert.Equal(OnlineErrorCode.ParameterInvalid, error);
        }

        /// <summary>
        /// 验证缺失 RequestId 被拒。
        /// </summary>
        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void Validate_WithoutRequestId_ShouldReturnParameterInvalid(string requestId)
        {
            // Arrange
            var context = CreateValidContext();
            context.RequestId = requestId;

            // Act
            var error = OnlineRequestContextValidator.Validate(context, false);

            // Assert
            Assert.Equal(OnlineErrorCode.ParameterInvalid, error);
        }

        /// <summary>
        /// 验证低于最低协议版本的请求被拒。
        /// </summary>
        [Fact]
        public void Validate_WithProtocolVersionBelowMinimum_ShouldReturnParameterInvalid()
        {
            // Arrange
            var context = CreateValidContext();
            context.ProtocolVersion = OnlineRequestContextValidator.MinimumProtocolVersion - 1;

            // Act
            var error = OnlineRequestContextValidator.Validate(context, false);

            // Assert
            Assert.Equal(OnlineErrorCode.ParameterInvalid, error);
        }

        /// <summary>
        /// 验证副作用请求缺失幂等键被拒。
        /// </summary>
        [Fact]
        public void Validate_WithSideEffectButMissingIdempotencyKey_ShouldReturnParameterInvalid()
        {
            // Arrange
            var context = CreateValidContext();
            context.IdempotencyKey = null;

            // Act
            var error = OnlineRequestContextValidator.Validate(context, true);

            // Assert
            Assert.Equal(OnlineErrorCode.ParameterInvalid, error);
        }

        /// <summary>
        /// 验证非副作用请求不校验幂等键（查询类请求允许缺失）。
        /// </summary>
        [Fact]
        public void Validate_WithoutSideEffect_ShouldNotRequireIdempotencyKey()
        {
            // Arrange
            var context = CreateValidContext();
            context.IdempotencyKey = null;

            // Act
            var error = OnlineRequestContextValidator.Validate(context, false);

            // Assert
            Assert.Null(error);
        }

        /// <summary>
        /// 验证幂等键格式校验：合法字符集与边界长度。
        /// </summary>
        [Theory]
        [InlineData("abc-ABC_019", true)]
        [InlineData("a", true)]
        [InlineData(null, false)]
        [InlineData("", false)]
        [InlineData("bad key", false)]
        [InlineData("bad.key", false)]
        [InlineData("键", false)]
        public void IsIdempotencyKeyValid_ShouldMatchAllowedCharset(string key, bool expected)
        {
            // Act
            var result = OnlineRequestContextValidator.IsIdempotencyKeyValid(key);

            // Assert
            Assert.Equal(expected, result);
        }

        /// <summary>
        /// 验证幂等键长度上限（超长被拒）。
        /// </summary>
        [Fact]
        public void IsIdempotencyKeyValid_WhenExceedsMaxLength_ShouldReturnFalse()
        {
            // Arrange
            var tooLong = new string('a', OnlineRequestContextValidator.MaximumIdempotencyKeyLength + 1);

            // Act
            var result = OnlineRequestContextValidator.IsIdempotencyKeyValid(tooLong);

            // Assert
            Assert.False(result);
        }
    }
}
