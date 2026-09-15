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
    /// OnlineEventPublisher / OnlineEventConsumer 事件发布与消费去重基座测试（Foundation InMemory 传输；
    /// 同一 EventId 投递两次只处理一次，VC-1.13）。
    /// </summary>
    public class OnlineEventPublisherConsumerTests
    {
        /// <summary>
        /// 构造完整 Online 事件样本。
        /// </summary>
        private static OnlineEvent CreateSampleEvent()
        {
            return new OnlineEvent
            {
                EventId = "evt-0004",
                EventType = "online.session.kicked",
                OccurredTime = 1760000000999,
                SchemaVersion = 1,
                TenantId = 1,
                AppId = 10,
                ServerId = 100,
                PlayerId = 10001,
                Source = "online-identity",
                CorrelationId = "req-0004",
                Payload = new byte[] { 7, 8, 9 },
            };
        }

        /// <summary>
        /// 验证经发布契约发布的事件以 Foundation 信封形态到达订阅方，作用域经稳定键承载。
        /// </summary>
        [Fact]
        public void PublishAsync_ShouldDeliverEnvelopeWithScopeAttributes()
        {
            // Arrange
            var delivered = new List<EventEnvelope>();
            var transport = new InMemoryEventPublisher();
            transport.Subscribe(delegate (EventEnvelope envelope)
            {
                delivered.Add(envelope);
            });
            IOnlineEventPublisher publisher = new OnlineEventPublisher(transport);

            // Act
            publisher.PublishAsync(CreateSampleEvent()).GetAwaiter().GetResult();

            // Assert
            var envelope = Assert.Single(delivered);
            Assert.Equal("evt-0004", envelope.EventId);
            Assert.Equal("req-0004", envelope.CorrelationId);
            Assert.Equal("1", envelope.Attributes[OnlineEventEnvelopeMapper.TenantAttributeKey]);
            Assert.Equal("10", envelope.Attributes[OnlineEventEnvelopeMapper.AppAttributeKey]);
            Assert.Equal("100", envelope.Attributes[OnlineEventEnvelopeMapper.ServerAttributeKey]);
            Assert.Equal("10001", envelope.Attributes[OnlineEventEnvelopeMapper.PlayerAttributeKey]);
        }

        /// <summary>
        /// 验证消费去重：同一 EventId 首见被消费、重复被拦截（VC-1.13）。
        /// </summary>
        [Fact]
        public void TryConsume_WithDuplicateEventId_ShouldRejectSecondDelivery()
        {
            // Arrange
            var consumer = new OnlineEventConsumer(new InMemoryEventDeduplicator());
            var onlineEvent = CreateSampleEvent();
            var redelivered = CreateSampleEvent();

            // Act
            var firstDelivery = consumer.TryConsume(onlineEvent);
            var secondDelivery = consumer.TryConsume(redelivered);

            // Assert
            Assert.True(firstDelivery);
            Assert.False(secondDelivery);
        }

        /// <summary>
        /// 验证不同 EventId 互不影响消费判定。
        /// </summary>
        [Fact]
        public void TryConsume_WithDifferentEventIds_ShouldConsumeBoth()
        {
            // Arrange
            var consumer = new OnlineEventConsumer(new InMemoryEventDeduplicator());
            var first = CreateSampleEvent();
            var second = CreateSampleEvent();
            second.EventId = "evt-0005";

            // Act
            var firstResult = consumer.TryConsume(first);
            var secondResult = consumer.TryConsume(second);

            // Assert
            Assert.True(firstResult);
            Assert.True(secondResult);
        }
    }
}
