using GameFrameX.Network.RemoteMessaging.Contracts;
using GameFrameX.Network.RemoteMessaging.Routing;
using GameFrameX.Network.RemoteMessaging.Versioning;
using GameFrameX.ProtoBuf.Net;

namespace GameFrameX.Tests.RemoteMessaging;

/// <summary>
/// 协议版本握手契约测试：版本来源模型（连接级常量）、握手消息对形状（ID 段位 / Proto 往返 / 请求响应标记）。
/// 多版本协商灰度面（协商器抽象 + 消息级版本标注）已收缩移除，仅保留握手帧最小版本号校验。
/// </summary>
/// <remarks>
/// Contract tests for the protocol version handshake: the version source model (the
/// connection-level constant) and the handshake message pair shape (id segment, protobuf round
/// trip, request/response markers). The multi-version negotiation surface (the negotiator
/// abstraction plus message-level version attributes) has been removed by the in-place shrink;
/// only the handshake frame's minimal version check remains.
/// </remarks>
public sealed class ProtocolVersionNegotiationTests
{
    [Fact]
    public void ProtocolVersionConstants_SingleSourceOfTruth()
    {
        Assert.Equal(1, RemoteMessagingProtocolVersion.Current.Major);
        Assert.Equal(0, RemoteMessagingProtocolVersion.Current.Minor);
        Assert.Equal(RemoteMessagingProtocolVersion.Current, RemoteMessagingProtocolVersion.Legacy);
    }

    [Fact]
    public void HandshakeMessageIds_OccupyInnerSegmentMinus130_SubIds11And12()
    {
        Assert.Equal(-130, ReqProtocolVersionNegotiation.ReservedMessageId >> 16);
        Assert.Equal(11, ReqProtocolVersionNegotiation.ReservedMessageId & 0xFFFF);
        Assert.Equal(-130, RespProtocolVersionNegotiation.ReservedMessageId >> 16);
        Assert.Equal(12, RespProtocolVersionNegotiation.ReservedMessageId & 0xFFFF);
    }

    [Fact]
    public void HandshakeMessages_ImplementRequestResponseMarkers()
    {
        Assert.IsAssignableFrom<GameFrameX.Network.Abstractions.IRequestMessage>(new ReqProtocolVersionNegotiation());
        Assert.IsAssignableFrom<GameFrameX.Network.Abstractions.IResponseMessage>(new RespProtocolVersionNegotiation());
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
    public void HandshakeRejection_SameMajorOnly_MajorMismatchRejected()
    {
        var local = RemoteMessagingProtocolVersion.Current;
        var sameMajorPeer = new ProtocolVersion(local.Major, local.Minor + 1);
        var crossMajorPeer = new ProtocolVersion(local.Major + 1, local.Minor);

        Assert.Equal(sameMajorPeer.Major, local.Major);
        Assert.NotEqual(crossMajorPeer.Major, local.Major);

        var replySameMajor = ProtocolVersionNegotiationServer.BuildReply(
            new ReqProtocolVersionNegotiation { Major = sameMajorPeer.Major, Minor = sameMajorPeer.Minor }, local);
        var replyCrossMajor = ProtocolVersionNegotiationServer.BuildReply(
            new ReqProtocolVersionNegotiation { Major = crossMajorPeer.Major, Minor = crossMajorPeer.Minor }, local);

        Assert.True(replySameMajor.Accepted);
        Assert.False(replyCrossMajor.Accepted);
    }

    [Fact]
    public void RemoteStatusCode_ExposesProtocolVersionIncompatible()
    {
        // 握手被拒仍以该状态码回执调用方（跨 major 混布禁止）
        Assert.Equal(12, (int)RemoteStatusCode.ProtocolVersionIncompatible);
    }
}
