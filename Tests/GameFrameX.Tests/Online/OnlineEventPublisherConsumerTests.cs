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

using System;
using System.Collections.Generic;
using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Foundation.Idempotency;
using GameFrameX.Capability.Online.Abstractions.Events;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// OnlineEventPublisher / OnlineEventConsumer 事件发布与消费去重基座测试（C294：发布基座委托机制事件发布口
    /// <see cref="ICapabilityEventPublisher"/>，主题 = EventType、载荷 = OnlineEvent 原件；同一 EventId 投递两次只处理一次）。
    /// </summary>
    public class OnlineEventPublisherConsumerTests
    {
        /// <summary>
        /// 机制事件发布口记录桩（按发布序记录主题与载荷，供断言委托口径；不做分发）。
        /// </summary>
        private sealed class RecordingCapabilityEventPublisher : ICapabilityEventPublisher
        {
            /// <summary>已发布主题（发布序）。</summary>
            public readonly List<string> Topics = new();

            /// <summary>已发布载荷（发布序，装箱原件）。</summary>
            public readonly List<object> Payloads = new();

            /// <inheritdoc />
            public void Publish<TEvent>(string topic, TEvent eventPayload)
            {
                Topics.Add(topic);
                Payloads.Add(eventPayload);
            }
        }

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
        /// 验证经发布契约发布的事件原样委托机制发布口：主题 = 事件类型字符串，载荷 = 同一 OnlineEvent 实例（作用域字段随载荷承载）。
        /// </summary>
        [Fact]
        public void PublishAsync_ShouldDelegateToCapabilityPublisherWithEventTypeAsTopic()
        {
            // Arrange
            var transport = new RecordingCapabilityEventPublisher();
            IOnlineEventPublisher publisher = new OnlineEventPublisher(transport);
            var onlineEvent = CreateSampleEvent();

            // Act
            publisher.PublishAsync(onlineEvent).GetAwaiter().GetResult();

            // Assert
            var topic = Assert.Single(transport.Topics);
            Assert.Equal("online.session.kicked", topic);
            Assert.Equal(onlineEvent.EventType, topic);
            var payload = Assert.IsType<OnlineEvent>(Assert.Single(transport.Payloads));
            Assert.Same(onlineEvent, payload);
            Assert.Equal("evt-0004", payload.EventId);
            Assert.Equal("req-0004", payload.CorrelationId);
            Assert.Equal(1, payload.TenantId);
            Assert.Equal(10, payload.AppId);
            Assert.Equal(100, payload.ServerId);
            Assert.Equal(10001, payload.PlayerId);
        }

        /// <summary>
        /// 验证事件类型（即总线主题）为空时拒绝发布且不触达机制发布口（主题是订阅匹配键，空主题无法投递）。
        /// </summary>
        [Fact]
        public void PublishAsync_WithEmptyEventType_ShouldThrowWithoutPublishing()
        {
            // Arrange
            var transport = new RecordingCapabilityEventPublisher();
            IOnlineEventPublisher publisher = new OnlineEventPublisher(transport);
            var onlineEvent = CreateSampleEvent();
            onlineEvent.EventType = " ";

            // Act / Assert
            Assert.Throws<ArgumentException>(() => publisher.PublishAsync(onlineEvent).GetAwaiter().GetResult());
            Assert.Empty(transport.Topics);
        }

        /// <summary>
        /// 验证空事件拒绝发布（业务方漏传事件属编程错误，显式失败）。
        /// </summary>
        [Fact]
        public void PublishAsync_WithNullEvent_ShouldThrow()
        {
            // Arrange
            var transport = new RecordingCapabilityEventPublisher();
            IOnlineEventPublisher publisher = new OnlineEventPublisher(transport);

            // Act / Assert
            Assert.Throws<ArgumentNullException>(() => publisher.PublishAsync(null).GetAwaiter().GetResult());
            Assert.Empty(transport.Topics);
        }

        /// <summary>
        /// 验证消费去重：同一 EventId 首见被消费、重复被拦截。
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
