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
using GameFrameX.Online.Events;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// OnlineEventSanitizer 事件审计脱敏测试（敏感字段掩码、无明文泄露，VC-1.17）。
    /// </summary>
    public class OnlineEventSanitizerTests
    {
        /// <summary>
        /// 构造带语义字段投影的事件样本。
        /// </summary>
        private static OnlineEvent CreateEventWithAuditFields(IDictionary<string, string> auditFields)
        {
            return new OnlineEvent
            {
                EventId = "evt-0003",
                EventType = "online.session.created",
                OccurredTime = 1760000000789,
                SchemaVersion = 1,
                TenantId = 1,
                AppId = 10,
                ServerId = 100,
                PlayerId = 10001,
                Source = "online-identity",
                CorrelationId = "req-0003",
                PayloadAuditFields = auditFields == null ? null : new Dictionary<string, string>(auditFields),
            };
        }

        /// <summary>
        /// 验证默认敏感键命中（大小写不敏感包含匹配）被整体掩码。
        /// </summary>
        [Theory]
        [InlineData("AccessToken", "raw-jwt")]
        [InlineData("refresh_token", "raw-refresh")]
        [InlineData("userPassword", "raw-password")]
        [InlineData("ContactPhone", "13800000000")]
        [InlineData("playerEmail", "a@example.com")]
        public void CreateAuditView_WithSensitiveFields_ShouldMaskValues(string fieldName, string rawValue)
        {
            // Arrange
            var sanitizer = new OnlineEventSanitizer();
            var onlineEvent = CreateEventWithAuditFields(new Dictionary<string, string>
            {
                { fieldName, rawValue },
                { "level", "10" },
            });

            // Act
            var view = sanitizer.CreateAuditView(onlineEvent);

            // Assert
            Assert.Equal(OnlineEventSanitizer.MaskedValue, view.SanitizedFields[fieldName]);
            Assert.Equal("10", view.SanitizedFields["level"]);
        }

        /// <summary>
        /// 验证装配期追加敏感键同样生效（扩充不需回安全评审，收窄才需要）。
        /// </summary>
        [Fact]
        public void CreateAuditView_WithAdditionalSensitiveKeys_ShouldMaskCustomFields()
        {
            // Arrange
            var sanitizer = new OnlineEventSanitizer("balance", "orderNo");
            var onlineEvent = CreateEventWithAuditFields(new Dictionary<string, string>
            {
                { "goldBalance", "999999" },
                { "orderNo", "ORD-1" },
                { "nickname", "player-1" },
            });

            // Act
            var view = sanitizer.CreateAuditView(onlineEvent);

            // Assert
            Assert.Equal(OnlineEventSanitizer.MaskedValue, view.SanitizedFields["goldBalance"]);
            Assert.Equal(OnlineEventSanitizer.MaskedValue, view.SanitizedFields["orderNo"]);
            Assert.Equal("player-1", view.SanitizedFields["nickname"]);
        }

        /// <summary>
        /// 验证审计视图保留信封元数据（作用域、CorrelationId 可追溯，敏感值不出现）。
        /// </summary>
        [Fact]
        public void CreateAuditView_ShouldKeepEnvelopeMetadataButDropRawSecrets()
        {
            // Arrange
            var sanitizer = new OnlineEventSanitizer();
            var onlineEvent = CreateEventWithAuditFields(new Dictionary<string, string>
            {
                { "sessionToken", "plain-secret-should-not-leak" },
            });

            // Act
            var view = sanitizer.CreateAuditView(onlineEvent);

            // Assert
            Assert.Equal("evt-0003", view.EventId);
            Assert.Equal("online.session.created", view.EventType);
            Assert.Equal(1, view.TenantId);
            Assert.Equal(10, view.AppId);
            Assert.Equal(100, view.ServerId);
            Assert.Equal(10001, view.PlayerId);
            Assert.Equal("req-0003", view.CorrelationId);
            Assert.Equal(OnlineEventSanitizer.MaskedValue, view.SanitizedFields["sessionToken"]);
        }

        /// <summary>
        /// 验证空投影事件生成空脱敏视图（不抛异常）。
        /// </summary>
        [Fact]
        public void CreateAuditView_WithoutAuditFields_ShouldReturnEmptyFields()
        {
            // Arrange
            var sanitizer = new OnlineEventSanitizer();
            var onlineEvent = CreateEventWithAuditFields(null);

            // Act
            var view = sanitizer.CreateAuditView(onlineEvent);

            // Assert
            Assert.Empty(view.SanitizedFields);
            Assert.Equal("evt-0003", view.EventId);
        }
    }
}
