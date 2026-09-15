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
using GameFrameX.Foundation.Idempotency;
using GameFrameX.Online.Events;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// OnlineEventEnvelopeMapper 事件信封双向映射测试（作用域经 Attributes 稳定键承载、CorrelationId 贯穿，VC-1.14）。
    /// </summary>
    public class OnlineEventEnvelopeMapperTests
    {
        /// <summary>
        /// 构造完整 Online 事件样本。
        /// </summary>
        private static OnlineEvent CreateSampleEvent()
        {
            return new OnlineEvent
            {
                EventId = "evt-0001",
                EventType = "online.session.created",
                OccurredTime = 1760000000123,
                SchemaVersion = 1,
                TenantId = 1,
                AppId = 10,
                ServerId = 100,
                PlayerId = 10001,
                Source = "online-identity",
                CorrelationId = "req-0001",
                Payload = new byte[] { 1, 2, 3 },
            };
        }

        /// <summary>
        /// 验证正向映射：作用域四字段经 Attributes 稳定键承载、通用字段原样传递。
        /// </summary>
        [Fact]
        public void ToEnvelope_ShouldCarryScopeViaStableAttributeKeys()
        {
            // Arrange
            var onlineEvent = CreateSampleEvent();

            // Act
            var envelope = OnlineEventEnvelopeMapper.ToEnvelope(onlineEvent);

            // Assert
            Assert.Equal("evt-0001", envelope.EventId);
            Assert.Equal("online.session.created", envelope.EventType);
            Assert.Equal(1760000000123, envelope.OccurredTime);
            Assert.Equal(1, envelope.SchemaVersion);
            Assert.Equal("online-identity", envelope.Source);
            Assert.Equal("req-0001", envelope.CorrelationId);
            Assert.Equal(new byte[] { 1, 2, 3 }, envelope.Payload.ToArray());
            Assert.Equal("1", envelope.Attributes[OnlineEventEnvelopeMapper.TenantAttributeKey]);
            Assert.Equal("10", envelope.Attributes[OnlineEventEnvelopeMapper.AppAttributeKey]);
            Assert.Equal("100", envelope.Attributes[OnlineEventEnvelopeMapper.ServerAttributeKey]);
            Assert.Equal("10001", envelope.Attributes[OnlineEventEnvelopeMapper.PlayerAttributeKey]);
        }

        /// <summary>
        /// 验证反向映射：信封还原为等价 Online 事件（往返无损）。
        /// </summary>
        [Fact]
        public void FromEnvelope_AfterToEnvelope_ShouldRoundTripAllFields()
        {
            // Arrange
            var original = CreateSampleEvent();

            // Act
            var roundTripped = OnlineEventEnvelopeMapper.FromEnvelope(OnlineEventEnvelopeMapper.ToEnvelope(original));

            // Assert
            Assert.Equal(original.EventId, roundTripped.EventId);
            Assert.Equal(original.EventType, roundTripped.EventType);
            Assert.Equal(original.OccurredTime, roundTripped.OccurredTime);
            Assert.Equal(original.SchemaVersion, roundTripped.SchemaVersion);
            Assert.Equal(original.TenantId, roundTripped.TenantId);
            Assert.Equal(original.AppId, roundTripped.AppId);
            Assert.Equal(original.ServerId, roundTripped.ServerId);
            Assert.Equal(original.PlayerId, roundTripped.PlayerId);
            Assert.Equal(original.Source, roundTripped.Source);
            Assert.Equal(original.CorrelationId, roundTripped.CorrelationId);
            Assert.Equal(original.Payload.ToArray(), roundTripped.Payload.ToArray());
        }

        /// <summary>
        /// 验证反向映射：缺失作用域稳定键时作用域字段回落为 0（不抛异常）。
        /// </summary>
        [Fact]
        public void FromEnvelope_WithoutScopeAttributes_ShouldFallBackToZero()
        {
            // Arrange
            var envelope = new EventEnvelope("evt-0002", "online.test", 1760000000456, 1, "online-test", "req-0002", new byte[] { 4 }, new Dictionary<string, string>());

            // Act
            var onlineEvent = OnlineEventEnvelopeMapper.FromEnvelope(envelope);

            // Assert
            Assert.Equal("evt-0002", onlineEvent.EventId);
            Assert.Equal(0, onlineEvent.TenantId);
            Assert.Equal(0, onlineEvent.AppId);
            Assert.Equal(0, onlineEvent.ServerId);
            Assert.Equal(0, onlineEvent.PlayerId);
        }

        /// <summary>
        /// 验证载荷语义字段投影不进入传输信封（审计专用，红线：不参与传输）。
        /// </summary>
        [Fact]
        public void ToEnvelope_ShouldNotCarryPayloadAuditFields()
        {
            // Arrange
            var onlineEvent = CreateSampleEvent();
            onlineEvent.PayloadAuditFields = new Dictionary<string, string>
            {
                { "AccessToken", "secret-value" },
            };

            // Act
            var envelope = OnlineEventEnvelopeMapper.ToEnvelope(onlineEvent);

            // Assert
            Assert.DoesNotContain(envelope.Attributes, pair => pair.Key.IndexOf("token", System.StringComparison.OrdinalIgnoreCase) >= 0);
            Assert.DoesNotContain("secret-value", System.Text.Encoding.UTF8.GetString(envelope.Payload.ToArray()));
        }
    }
}
