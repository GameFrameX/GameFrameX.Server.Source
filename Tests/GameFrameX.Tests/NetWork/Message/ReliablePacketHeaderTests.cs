// ==========================================================================================
//  GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//  GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//  均受中华人民共和国及相关国际法律法规保护。
//  are protected by the laws of the People's Republic of China and relevant international regulations.
//
//  使用本项目须严格遵守相应法律法规及开源许可证之规定。
//  Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//
//  本项目采用 Apache License 2.0 单协议分发，
//  This project is licensed solely under the Apache License 2.0,
//  完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//  please refer to the LICENSE file in the root directory of the source code for the full license text.
//
//  禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//  It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//  侵犯他人合法权益等法律法规所禁止的行为！
//  or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//  因基于本项目二次开发所产生的一切法律纠纷与责任，
//  Any legal disputes and liabilities arising from secondary development based on this project
//  本项目组织与贡献者概不承担。
//  shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//
//  GitHub 仓库：https://github.com/GameFrameX
//  GitHub Repository: https://github.com/GameFrameX
//  Gitee  仓库：https://gitee.com/GameFrameX
//  Gitee Repository:  https://gitee.com/GameFrameX
//  官方文档：https://gameframex.doc.alianblank.com/
//  Official Documentation: https://gameframex.doc.alianblank.com/
// ==========================================================================================

using System.Buffers.Binary;
using System.Net;
using GameFrameX.NetWork;
using GameFrameX.NetWork.Abstractions;
using GameFrameX.NetWork.Message;
using GameFrameX.NetWork.Messages;
using GameFrameX.Proto.Proto;
using GameFrameX.ProtoBuf.Net;
using GameFrameX.StartUp;
using GameFrameX.SuperSocket.Server.Abstractions.Session;
using GameFrameX.Utility.Setting;

namespace GameFrameX.Tests.NetWork.Message;

public sealed class ReliablePacketHeaderTests
{
    public ReliablePacketHeaderTests()
    {
        MessageProtoHelper.Init(typeof(ReqHeartBeat).Assembly);
        MessageHelper.SetMessageEncoderHandler(new DefaultMessageEncoderHandler(), null);
        MessageHelper.SetMessageDecoderHandler(new DefaultMessageDecoderHandler(), null);
    }

    [Fact]
    public void PacketHeaderLayout_ShouldMatchReliableFifoProtocol()
    {
        Assert.Equal(16, PacketHeaderLayout.BaseHeaderLength);
        Assert.Equal(24, PacketHeaderLayout.ReliableExtensionLength);
        Assert.Equal(40, PacketHeaderLayout.ReliableHeaderLength);
        Assert.Equal(6, PacketHeaderLayout.HeaderFlagsOffset);
        Assert.Equal(16, PacketHeaderLayout.SessionIdOffset);
        Assert.Equal(24, PacketHeaderLayout.ReliableSequenceOffset);
        Assert.Equal(32, PacketHeaderLayout.AckSequenceOffset);
    }

    [Fact]
    public void Encoder_ShouldWriteSixteenByteBaseHeader()
    {
        var encoder = new DefaultMessageEncoderHandler();
        var message = new ReqHeartBeat { Timestamp = 123 };
        message.SetUniqueId(456);

        var encoded = encoder.Handler(message);

        Assert.NotNull(encoded);
        Assert.Equal(PacketHeaderLayout.BaseHeaderLength, encoder.PackageHeaderLength);
        Assert.Equal((uint)encoded.Length, BinaryPrimitives.ReadUInt32BigEndian(encoded.AsSpan(PacketHeaderLayout.PacketLengthOffset)));
        Assert.Equal((byte)MessageOperationType.HeartBeat, encoded[PacketHeaderLayout.OperationTypeOffset]);
        Assert.Equal(0, encoded[PacketHeaderLayout.ZipFlagOffset]);
        Assert.Equal((ushort)ReliableHeaderFlags.ProtocolVersion1, BinaryPrimitives.ReadUInt16BigEndian(encoded.AsSpan(PacketHeaderLayout.HeaderFlagsOffset)));
        Assert.Equal(456, BinaryPrimitives.ReadInt32BigEndian(encoded.AsSpan(PacketHeaderLayout.UniqueIdOffset)));
        Assert.Equal(((10) << 16) + 10, BinaryPrimitives.ReadInt32BigEndian(encoded.AsSpan(PacketHeaderLayout.MessageIdOffset)));
    }

    [Fact]
    public void Decoder_ShouldReadReliableExtensionHeader()
    {
        var payload = ProtoBufSerializerHelper.Serialize(new ReqHeartBeat { Timestamp = 789 });
        var packet = new byte[PacketHeaderLayout.ReliableHeaderLength + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(PacketHeaderLayout.PacketLengthOffset), (uint)packet.Length);
        packet[PacketHeaderLayout.OperationTypeOffset] = (byte)MessageOperationType.HeartBeat;
        packet[PacketHeaderLayout.ZipFlagOffset] = 0;
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(PacketHeaderLayout.HeaderFlagsOffset), (ushort)(ReliableHeaderFlags.ProtocolVersion1 | ReliableHeaderFlags.Reliable));
        BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(PacketHeaderLayout.UniqueIdOffset), 111);
        BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(PacketHeaderLayout.MessageIdOffset), ((10) << 16) + 10);
        BinaryPrimitives.WriteUInt64BigEndian(packet.AsSpan(PacketHeaderLayout.SessionIdOffset), 222);
        BinaryPrimitives.WriteUInt64BigEndian(packet.AsSpan(PacketHeaderLayout.ReliableSequenceOffset), 333);
        BinaryPrimitives.WriteUInt64BigEndian(packet.AsSpan(PacketHeaderLayout.AckSequenceOffset), 444);
        payload.CopyTo(packet.AsSpan(PacketHeaderLayout.ReliableHeaderLength));

        var decoded = Assert.IsType<NetworkMessagePackage>(new DefaultMessageDecoderHandler().Handler(packet));

        Assert.Equal((ushort)(ReliableHeaderFlags.ProtocolVersion1 | ReliableHeaderFlags.Reliable), decoded.Header.HeaderFlags);
        Assert.True(decoded.Header.HasReliableExtension);
        Assert.Equal(1, decoded.Header.ProtocolVersion);
        Assert.Equal(222UL, decoded.Header.SessionId);
        Assert.Equal(333UL, decoded.Header.ReliableSequence);
        Assert.Equal(444UL, decoded.Header.AckSequence);
        Assert.False(decoded.Header.IsDuplicate);
    }

    [Fact]
    public void Decoder_ShouldAllowReliableControlPacketWithoutMessageType()
    {
        var packet = new byte[PacketHeaderLayout.ReliableHeaderLength];
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(PacketHeaderLayout.PacketLengthOffset), (uint)packet.Length);
        packet[PacketHeaderLayout.OperationTypeOffset] = (byte)MessageOperationType.Game;
        packet[PacketHeaderLayout.ZipFlagOffset] = 0;
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(PacketHeaderLayout.HeaderFlagsOffset), (ushort)(ReliableHeaderFlags.ProtocolVersion1 | ReliableHeaderFlags.Reliable | ReliableHeaderFlags.Control | ReliableHeaderFlags.Resume));
        BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(PacketHeaderLayout.UniqueIdOffset), 111);
        BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(PacketHeaderLayout.MessageIdOffset), -1);
        BinaryPrimitives.WriteUInt64BigEndian(packet.AsSpan(PacketHeaderLayout.SessionIdOffset), 222);
        BinaryPrimitives.WriteUInt64BigEndian(packet.AsSpan(PacketHeaderLayout.ReliableSequenceOffset), 0);
        BinaryPrimitives.WriteUInt64BigEndian(packet.AsSpan(PacketHeaderLayout.AckSequenceOffset), 7);

        var decoded = Assert.IsType<NetworkMessagePackage>(new DefaultMessageDecoderHandler().Handler(packet));

        Assert.Null(decoded.MessageType);
        Assert.Empty(decoded.MessageData);
        Assert.Equal(222UL, decoded.Header.SessionId);
        Assert.Equal(7UL, decoded.Header.AckSequence);
    }

    [Fact]
    public void Encoder_ShouldWriteReliableExtensionHeader_WhenMessageCarriesReliableMetadata()
    {
        var encoder = new DefaultMessageEncoderHandler();
        var message = new ReqHeartBeat { Timestamp = 123 };
        message.SetUniqueId(456);
        message.SetReliableHeader((ushort)(ReliableHeaderFlags.ProtocolVersion1 | ReliableHeaderFlags.Reliable | ReliableHeaderFlags.Ack), 222, 333, 444);

        var encoded = encoder.Handler(message);

        Assert.NotNull(encoded);
        Assert.Equal((ushort)(ReliableHeaderFlags.ProtocolVersion1 | ReliableHeaderFlags.Reliable | ReliableHeaderFlags.Ack), BinaryPrimitives.ReadUInt16BigEndian(encoded.AsSpan(PacketHeaderLayout.HeaderFlagsOffset)));
        Assert.Equal(222UL, BinaryPrimitives.ReadUInt64BigEndian(encoded.AsSpan(PacketHeaderLayout.SessionIdOffset)));
        Assert.Equal(333UL, BinaryPrimitives.ReadUInt64BigEndian(encoded.AsSpan(PacketHeaderLayout.ReliableSequenceOffset)));
        Assert.Equal(444UL, BinaryPrimitives.ReadUInt64BigEndian(encoded.AsSpan(PacketHeaderLayout.AckSequenceOffset)));
    }

    [Fact]
    public async Task NetWorkChannel_ShouldWriteAckReliableHeader_FromSessionState()
    {
        var state = new ReliableSessionState();
        var inboundHeader = new MessageObjectHeader
        {
            HeaderFlags = (ushort)(ReliableHeaderFlags.ProtocolVersion1 | ReliableHeaderFlags.Reliable),
            SessionId = 222,
            ReliableSequence = 1,
        };
        Assert.Equal(ReliablePacketProcessResult.InOrder, state.ProcessInbound(inboundHeader));

        var sender = new CapturingNetWorkSender();
        var channel = new DefaultNetWorkChannel(new TestGameAppSession(), new AppSetting { NetWorkSendTimeOutSeconds = 1 }, sender);
        channel.SetData(ReliableSessionState.ChannelDataKey, state);

        await channel.WriteAsync(new NotifyHeartBeat { Timestamp = 789 });

        Assert.NotNull(sender.LastMessageData);
        Assert.Equal((ushort)(ReliableHeaderFlags.ProtocolVersion1 | ReliableHeaderFlags.Reliable | ReliableHeaderFlags.Ack), BinaryPrimitives.ReadUInt16BigEndian(sender.LastMessageData.AsSpan(PacketHeaderLayout.HeaderFlagsOffset)));
        Assert.Equal(222UL, BinaryPrimitives.ReadUInt64BigEndian(sender.LastMessageData.AsSpan(PacketHeaderLayout.SessionIdOffset)));
        Assert.Equal(0UL, BinaryPrimitives.ReadUInt64BigEndian(sender.LastMessageData.AsSpan(PacketHeaderLayout.ReliableSequenceOffset)));
        Assert.Equal(1UL, BinaryPrimitives.ReadUInt64BigEndian(sender.LastMessageData.AsSpan(PacketHeaderLayout.AckSequenceOffset)));
    }

    [Fact]
    public void ReliableSessionState_ShouldAcceptResumeControlWithoutAdvancingClientSequence()
    {
        var state = new ReliableSessionState();
        var inboundHeader = new MessageObjectHeader
        {
            HeaderFlags = (ushort)(ReliableHeaderFlags.ProtocolVersion1 | ReliableHeaderFlags.Reliable),
            SessionId = 222,
            ReliableSequence = 1,
        };
        Assert.Equal(ReliablePacketProcessResult.InOrder, state.ProcessInbound(inboundHeader));

        var resumeHeader = new MessageObjectHeader
        {
            HeaderFlags = (ushort)(ReliableHeaderFlags.ProtocolVersion1 | ReliableHeaderFlags.Reliable | ReliableHeaderFlags.Control | ReliableHeaderFlags.Resume),
            SessionId = 222,
            ReliableSequence = 99,
            AckSequence = 0,
        };

        Assert.Equal(ReliablePacketProcessResult.Control, state.ProcessInbound(resumeHeader));
        Assert.Equal(1UL, state.LastProcessedClientSequence);
        Assert.Equal(1UL, resumeHeader.AckSequence);
    }

    [Fact]
    public void ReliableSessionState_ShouldBuildAckControlPacket()
    {
        var state = new ReliableSessionState();
        Assert.Equal(ReliablePacketProcessResult.InOrder, state.ProcessInbound(new MessageObjectHeader
        {
            HeaderFlags = (ushort)(ReliableHeaderFlags.ProtocolVersion1 | ReliableHeaderFlags.Reliable),
            SessionId = 222,
            ReliableSequence = 1,
        }));

        var ackPacket = state.BuildAckPacket(new MessageObjectHeader
        {
            OperationType = (byte)MessageOperationType.Game,
            HeaderFlags = (ushort)(ReliableHeaderFlags.ProtocolVersion1 | ReliableHeaderFlags.Reliable),
            SessionId = 222,
            UniqueId = 333,
            MessageId = 444,
            ReliableSequence = 9,
        });

        Assert.Equal(PacketHeaderLayout.ReliableHeaderLength, ackPacket.Length);
        Assert.Equal((uint)PacketHeaderLayout.ReliableHeaderLength, BinaryPrimitives.ReadUInt32BigEndian(ackPacket.AsSpan(PacketHeaderLayout.PacketLengthOffset)));
        Assert.Equal((ushort)(ReliableHeaderFlags.ProtocolVersion1 | ReliableHeaderFlags.Reliable | ReliableHeaderFlags.Ack | ReliableHeaderFlags.Control), BinaryPrimitives.ReadUInt16BigEndian(ackPacket.AsSpan(PacketHeaderLayout.HeaderFlagsOffset)));
        Assert.Equal(222UL, BinaryPrimitives.ReadUInt64BigEndian(ackPacket.AsSpan(PacketHeaderLayout.SessionIdOffset)));
        Assert.Equal(0UL, BinaryPrimitives.ReadUInt64BigEndian(ackPacket.AsSpan(PacketHeaderLayout.ReliableSequenceOffset)));
        Assert.Equal(1UL, BinaryPrimitives.ReadUInt64BigEndian(ackPacket.AsSpan(PacketHeaderLayout.AckSequenceOffset)));
    }

    [Fact]
    public async Task Startup_ShouldResendCachedReliableResponse_ForDuplicateSequence()
    {
        var state = new ReliableSessionState();
        var inboundHeader = new MessageObjectHeader
        {
            HeaderFlags = (ushort)(ReliableHeaderFlags.ProtocolVersion1 | ReliableHeaderFlags.Reliable),
            SessionId = 222,
            ReliableSequence = 1,
        };
        Assert.Equal(ReliablePacketProcessResult.InOrder, state.ProcessInbound(inboundHeader));

        var cachedResponse = new byte[] { 1, 2, 3, 4 };
        state.CacheResponse(1, cachedResponse);
        var sender = new CapturingNetWorkSender();
        var channel = new DefaultNetWorkChannel(new TestGameAppSession(), new AppSetting { NetWorkSendTimeOutSeconds = 1 }, sender);
        channel.SetData(ReliableSessionState.ChannelDataKey, state);
        var duplicatePackage = NetworkMessagePackage.Create(new MessageObjectHeader
        {
            HeaderFlags = (ushort)(ReliableHeaderFlags.ProtocolVersion1 | ReliableHeaderFlags.Reliable),
            SessionId = 222,
            ReliableSequence = 1,
            MessageId = ((10) << 16) + 10,
        }, Array.Empty<byte>(), typeof(ReqHeartBeat));

        var dispatched = await new TestAppStartUp().ShouldDispatchReliableMessageForTest(channel, duplicatePackage);

        Assert.False(dispatched);
        Assert.Equal(cachedResponse, sender.LastMessageData);
    }

    [Fact]
    public void Decoder_ShouldRejectLegacyFourteenByteHeader()
    {
        var packet = new byte[14];
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(0), (uint)packet.Length);

        var decoded = new DefaultMessageDecoderHandler().Handler(packet);

        Assert.Null(decoded);
    }

    [Fact]
    public void MessageObjectHeader_ShouldClassifyReliableSequence()
    {
        var header = new MessageObjectHeader
        {
            HeaderFlags = (ushort)(ReliableHeaderFlags.ProtocolVersion1 | ReliableHeaderFlags.Reliable),
            ReliableSequence = 7,
        };

        Assert.Equal(ReliablePacketProcessResult.InOrder, MessageProtoHelper.ClassifyReliableSequence(header, 6));
        Assert.Equal(ReliablePacketProcessResult.Duplicate, MessageProtoHelper.ClassifyReliableSequence(header, 7));
        Assert.Equal(ReliablePacketProcessResult.Gap, MessageProtoHelper.ClassifyReliableSequence(header, 5));
    }

    private sealed class TestAppStartUp : AppStartUpBase
    {
        public override Task StartAsync()
        {
            return Task.CompletedTask;
        }

        public ValueTask<bool> ShouldDispatchReliableMessageForTest(INetWorkChannel channel, INetworkMessagePackage package)
        {
            return ShouldDispatchReliableMessageAsync(channel, package);
        }
    }

    private sealed class CapturingNetWorkSender : INetWorkSender
    {
        public byte[] LastMessageData { get; private set; }

        public ValueTask SendAsync(byte[] messageData, CancellationToken cancellationToken)
        {
            LastMessageData = messageData;
            return ValueTask.CompletedTask;
        }

        public ValueTask SendAsync(ReadOnlyMemory<byte> messageData, CancellationToken cancellationToken)
        {
            LastMessageData = messageData.ToArray();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestGameAppSession : IGameAppSession
    {
        public string SessionID => "test";

        public bool IsConnected => true;

        public EndPoint RemoteEndPoint => new IPEndPoint(IPAddress.Loopback, 1);

        public ValueTask SendAsync(byte[] data, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default)
        {
            return ValueTask.CompletedTask;
        }

        public void Close()
        {
        }
    }
}
