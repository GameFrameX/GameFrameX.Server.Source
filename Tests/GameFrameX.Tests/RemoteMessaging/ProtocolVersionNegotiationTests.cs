using GameFrameX.Network.RemoteMessaging.Contracts;
using GameFrameX.Network.RemoteMessaging.Routing;
using GameFrameX.Network.RemoteMessaging.Versioning;
using GameFrameX.ProtoBuf.Net;

namespace GameFrameX.Tests.RemoteMessaging;

/// <summary>
/// C252 协议版本协商契约测试：版本来源模型（连接级常量 + 消息级标注）、协商器 major 比较语义、
/// 握手消息对形状（ID 段位 / Proto 往返 / 请求响应标记）与首批标注守护。
/// </summary>
/// <remarks>
/// Contract tests for the C252 protocol version negotiation: the version source model (the
/// connection-level constant plus message-level attributes), the negotiator's major-comparison
/// semantics, the handshake message pair shape (id segment, protobuf round trip, request/response
/// markers), and the first-batch annotation guard.
/// </remarks>
public sealed class ProtocolVersionNegotiationTests
{
    [Fact]
    public void ConnectionCompatibility_SameMajor_IsCompatible()
    {
        var negotiator = new DefaultProtocolVersionNegotiator();

        Assert.True(negotiator.IsConnectionCompatible(new ProtocolVersion(1, 0), new ProtocolVersion(1, 0)));
        Assert.True(negotiator.IsConnectionCompatible(new ProtocolVersion(1, 0), new ProtocolVersion(1, 9)));
        Assert.True(negotiator.IsConnectionCompatible(new ProtocolVersion(2, 3), new ProtocolVersion(2, 0)));
    }

    [Fact]
    public void ConnectionCompatibility_CrossMajor_IsRejected()
    {
        var negotiator = new DefaultProtocolVersionNegotiator();

        // 拒绝语义无方向豁免：任一侧更高 major 都判不兼容
        Assert.False(negotiator.IsConnectionCompatible(new ProtocolVersion(1, 0), new ProtocolVersion(2, 0)));
        Assert.False(negotiator.IsConnectionCompatible(new ProtocolVersion(2, 0), new ProtocolVersion(1, 0)));
    }

    [Fact]
    public void MessageCompatibility_Unannotated_IsAlwaysCompatible()
    {
        var negotiator = new DefaultProtocolVersionNegotiator();

        // 未标注消息 = 基础契约，对任意远端版本放行（既有消息路径行为不变红线）
        Assert.True(negotiator.IsMessageCompatible(typeof(UnannotatedDummyMessage), new ProtocolVersion(1, 0)));
        Assert.True(negotiator.IsMessageCompatible(typeof(UnannotatedDummyMessage), new ProtocolVersion(9, 9)));
    }

    [Fact]
    public void MessageCompatibility_Annotated_FollowsRemoteMajor()
    {
        var negotiator = new DefaultProtocolVersionNegotiator();

        // 消息标注 major <= 远端协商 major 即兼容（minor 不阻断：protobuf 向后兼容演进）
        Assert.True(negotiator.IsMessageCompatible(typeof(RoleRouteEnvelopeMessage), new ProtocolVersion(1, 0)));
        Assert.True(negotiator.IsMessageCompatible(typeof(AnnotatedV1DummyMessage), new ProtocolVersion(2, 3)));

        // 消息比远端新（major 更高）→ 拒绝
        Assert.False(negotiator.IsMessageCompatible(typeof(AnnotatedV2DummyMessage), new ProtocolVersion(1, 9)));
    }

    [Fact]
    public void GetVersion_ReturnsAttributeVersion_OrZeroDotZero()
    {
        var negotiator = new DefaultProtocolVersionNegotiator();

        Assert.Equal("1.0", negotiator.GetVersion(typeof(RoleRouteEnvelopeMessage)));
        Assert.Equal("0.0", negotiator.GetVersion(typeof(UnannotatedDummyMessage)));
    }

    [Fact]
    public void ProtocolVersionConstants_SingleSourceOfTruth()
    {
        // 连接级协议版本单一事实源：协商能力首版 = (1,0)；协商能力落地前的部署等效版本同为 (1,0)
        Assert.Equal(new ProtocolVersion(1, 0), RemoteMessagingProtocolVersion.Current);
        Assert.Equal(new ProtocolVersion(1, 0), RemoteMessagingProtocolVersion.Legacy);
    }

    [Fact]
    public void FirstBatchContractMessages_CarryProtocolVersionAttribute()
    {
        // 首批标注：跨进程路由信封 + 握手消息对，全部 (1,0)
        AssertVersionAttribute(RoleRouteEnvelopeMessage.ReservedMessageId, 1, 0, typeof(RoleRouteEnvelopeMessage));
        AssertVersionAttribute(ReqProtocolVersionNegotiation.ReservedMessageId, 1, 0, typeof(ReqProtocolVersionNegotiation));
        AssertVersionAttribute(RespProtocolVersionNegotiation.ReservedMessageId, 1, 0, typeof(RespProtocolVersionNegotiation));
    }

    [Fact]
    public void HandshakeMessageIds_OccupyInnerSegmentMinus130_SubIds11And12()
    {
        // -130 内部段：子号 10 = 路由信封（既有），11/12 = 握手 Req/Resp（C252 分配）
        Assert.Equal(unchecked((int)((-130 << 16) + 11)), ReqProtocolVersionNegotiation.ReservedMessageId);
        Assert.Equal(unchecked((int)((-130 << 16) + 12)), RespProtocolVersionNegotiation.ReservedMessageId);
        Assert.NotEqual(RoleRouteEnvelopeMessage.ReservedMessageId, ReqProtocolVersionNegotiation.ReservedMessageId);
        Assert.NotEqual(RoleRouteEnvelopeMessage.ReservedMessageId, RespProtocolVersionNegotiation.ReservedMessageId);
    }

    [Fact]
    public void HandshakeMessages_ImplementRequestResponseMarkers()
    {
        Assert.True(typeof(GameFrameX.Network.Abstractions.IRequestMessage).IsAssignableFrom(typeof(ReqProtocolVersionNegotiation)));
        Assert.True(typeof(GameFrameX.Network.Abstractions.IResponseMessage).IsAssignableFrom(typeof(RespProtocolVersionNegotiation)));
    }

    [Fact]
    public void HandshakeMessages_RoundTripPreservesVersionIdentity()
    {
        // 数据身份断言：序列化往返必须保留版本字段与拒绝旗标的精确值
        var request = new ReqProtocolVersionNegotiation { Major = 2, Minor = 7 };
        var decodedRequest = ProtoBufSerializerHelper.Deserialize<ReqProtocolVersionNegotiation>(ProtoBufSerializerHelper.Serialize(request));
        Assert.Equal(2, decodedRequest.Major);
        Assert.Equal(7, decodedRequest.Minor);

        var response = new RespProtocolVersionNegotiation { Major = 1, Minor = 3, Accepted = false, ErrorCode = 1 };
        var decodedResponse = ProtoBufSerializerHelper.Deserialize<RespProtocolVersionNegotiation>(ProtoBufSerializerHelper.Serialize(response));
        Assert.Equal(1, decodedResponse.Major);
        Assert.Equal(3, decodedResponse.Minor);
        Assert.False(decodedResponse.Accepted);
        Assert.Equal(1, decodedResponse.ErrorCode);
    }

    [Fact]
    public void AnnotatedMessageMajor_NeverExceedsCurrentProtocolMajor()
    {
        // 标注守护：本程序集内任何消息标注的 major 都不得超过协议当前 major（防标注跑在常量前面）
        foreach (var type in typeof(RoleRouteEnvelopeMessage).Assembly.GetTypes())
        {
            var attribute = (ProtocolVersionAttribute?)Attribute.GetCustomAttribute(type, typeof(ProtocolVersionAttribute));
            if (attribute != null)
            {
                Assert.True(attribute.Major <= RemoteMessagingProtocolVersion.Current.Major,
                    $"{type.FullName} annotates major {attribute.Major} beyond current protocol major {RemoteMessagingProtocolVersion.Current.Major}");
            }
        }
    }

    [Fact]
    public void RemoteStatusCode_ExposesProtocolVersionIncompatible()
    {
        // 拒绝语义的显式可观测状态码（区别于 UnexpectedResponse 等泛化错误）
        Assert.Equal(12, (int)RemoteStatusCode.ProtocolVersionIncompatible);
    }

    private static void AssertVersionAttribute(int messageId, int expectedMajor, int expectedMinor, Type type)
    {
        var attribute = (ProtocolVersionAttribute?)Attribute.GetCustomAttribute(type, typeof(ProtocolVersionAttribute));
        Assert.NotNull(attribute);
        Assert.Equal(expectedMajor, attribute.Major);
        Assert.Equal(expectedMinor, attribute.Minor);

        // 消息 ID 与标注同时存在（[MessageTypeHandler] 负责注册表可解码性）
        var handler = (GameFrameX.Network.Abstractions.MessageTypeHandlerAttribute?)Attribute.GetCustomAttribute(type, typeof(GameFrameX.Network.Abstractions.MessageTypeHandlerAttribute));
        Assert.NotNull(handler);
        Assert.Equal(messageId, handler.MessageId);
    }

    [ProtocolVersion(1, 2)]
    private sealed class AnnotatedV1DummyMessage : GameFrameX.Network.Messages.MessageObject
    {
        public override void Clear()
        {
        }
    }

    [ProtocolVersion(2, 0)]
    private sealed class AnnotatedV2DummyMessage : GameFrameX.Network.Messages.MessageObject
    {
        public override void Clear()
        {
        }
    }

    private sealed class UnannotatedDummyMessage : GameFrameX.Network.Messages.MessageObject
    {
        public override void Clear()
        {
        }
    }
}
