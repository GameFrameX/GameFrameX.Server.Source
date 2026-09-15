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
    /// OnlineResponse 与 OnlinePageCursor 公共响应/分页契约测试（响应五字段完整性，VC-1.2）。
    /// </summary>
    public class OnlineResponseTests
    {
        /// <summary>
        /// 验证成功响应包含全部公共字段（Code=0、MessageKey 为空、回显 RequestId、携带 Data 与 ServerTime）。
        /// </summary>
        [Fact]
        public void Success_ShouldCarryAllCommonFields()
        {
            // Arrange
            const string requestId = "req-0001";
            const long serverTime = 1760000000123;

            // Act
            var response = OnlineResponse<string>.Success(requestId, serverTime, "payload");

            // Assert
            Assert.Equal(0, response.Code);
            Assert.Equal(string.Empty, response.MessageKey);
            Assert.Equal(requestId, response.RequestId);
            Assert.Equal(serverTime, response.ServerTime);
            Assert.Equal("payload", response.Data);
        }

        /// <summary>
        /// 验证失败响应：Code 为错误码数值、MessageKey 由错误码派生、Data 为默认值。
        /// </summary>
        [Fact]
        public void Fail_ShouldCarryErrorCodeAndDerivedMessageKey()
        {
            // Arrange
            const string requestId = "req-0002";
            const long serverTime = 1760000000456;

            // Act
            var response = OnlineResponse<object>.Fail(OnlineErrorCode.VersionConflict, requestId, serverTime);

            // Assert
            Assert.Equal((int)OnlineErrorCode.VersionConflict, response.Code);
            Assert.Equal("Online.Error.Idempotency.VersionConflict", response.MessageKey);
            Assert.Equal(requestId, response.RequestId);
            Assert.Equal(serverTime, response.ServerTime);
            Assert.Null(response.Data);
        }

        /// <summary>
        /// 验证分页游标承载 Cursor 与 HasMore 语义。
        /// </summary>
        [Fact]
        public void OnlinePageCursor_ShouldCarryCursorAndHasMore()
        {
            // Arrange
            var hasMore = new OnlinePageCursor("b64:next", true);
            var lastPage = new OnlinePageCursor(string.Empty, false);

            // Assert
            Assert.Equal("b64:next", hasMore.Cursor);
            Assert.True(hasMore.HasMore);
            Assert.Equal(string.Empty, lastPage.Cursor);
            Assert.False(lastPage.HasMore);
        }
    }
}
