// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规及开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
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
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:  https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using System.Net;
using System.Net.Sockets;
using System.Buffers.Binary;
using GameFrameX.NetWork.Messages;
using GameFrameX.NetWork.RemoteMessaging.Discovery;
using GameFrameX.NetWork.RemoteMessaging.Routing;
using GameFrameX.ProtoBuf.Net;
using ProtoBuf;

namespace GameFrameX.Tests.Discovery;

/// <summary>
/// MongoDiscoveryRemoteRoleRouter 的选实例与转发语义测试（C143d D3 case 2/3）。
/// </summary>
/// <remarks>
/// Selection and forwarding semantics of MongoDiscoveryRemoteRoleRouter (C143d D3
/// case 2/3) without any Mongo dependency: the dual-view table comes from a fixed
/// provider and the send channel from a recording fake. Also covers the real TCP
/// forwarder's wire format against a local TcpListener.
/// </remarks>
public sealed class MongoDiscoveryRemoteRoleRouterTests
{
    /// <summary>
    /// 固定路由表提供者（测试替身）。
    /// </summary>
    private sealed class FixedTableProvider : IRoleRouteTableProvider
    {
        public FixedTableProvider(RoleRouteTable table)
        {
            Current = table;
        }

        public RoleRouteTable Current { get; }
    }

    /// <summary>
    /// 记录型转发器（测试替身）。
    /// </summary>
    private sealed class RecordingForwarder : IEnvelopeForwarder
    {
        public List<KeyValuePair<ParsedEndpoint, MessageEnvelope>> Forwards { get; } = new List<KeyValuePair<ParsedEndpoint, MessageEnvelope>>();

        public Task ForwardAsync(ParsedEndpoint endpoint, MessageEnvelope envelope, CancellationToken cancellationToken = default)
        {
            Forwards.Add(new KeyValuePair<ParsedEndpoint, MessageEnvelope>(endpoint, envelope));
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// wire 格式用可序列化测试载荷（内嵌消息走真实 protobuf 契约；Equivalence 套件的载荷无 ProtoContract，不可复用）。
    /// </summary>
    [ProtoContract]
    private sealed class WirePayloadMessage : MessageObject
    {
        [ProtoMember(1)]
        public long PlayerId { get; set; }

        /// <summary>
        /// 清除测试载荷内容：重置 <see cref="PlayerId"/> 为 0。
        /// </summary>
        /// <remarks>
        /// Clears the test payload content by resetting <see cref="PlayerId"/> to 0.
        /// </remarks>
        public override void Clear()
        {
            PlayerId = 0;
        }
    }

    /// <summary>
    /// 测试载荷的固定消息 Id（模拟 MessageProtoHelper 注册时统一分配的 Id）。
    /// </summary>
    private const int WirePayloadMessageId = 0x4321;

    /// <summary>
    /// 构造一个双实例表：Game 角色 Active + Draining，Social 角色 Active 单实例。
    /// </summary>
    private static RoleRouteTable BuildTable()
    {
        var now = DateTime.UtcNow;
        return RoleRouteTable.FromInstances(new List<InstanceDescriptor>
        {
            new InstanceDescriptor("Game", "game-active-1", "tcp://10.0.0.1:7001", InstanceStatus.Active, 10, EndpointAddressKind.IPv4, 101, now),
            new InstanceDescriptor("Game", "game-draining-1", "tcp://10.0.0.2:7002", InstanceStatus.Draining, 20, EndpointAddressKind.IPv4, 102, now),
            new InstanceDescriptor("Social", "social-active-1", "tcp://social.internal:7101", InstanceStatus.Active, 30, EndpointAddressKind.DnsName, 103, now),
        });
    }

    private static MessageEnvelope BuildEnvelope(string targetRole, string targetInstanceId = null)
    {
        var payload = new WirePayloadMessage { PlayerId = 42 };
        payload.SetMessageId(WirePayloadMessageId);
        return new MessageEnvelope(targetRole, payload, 99, targetInstanceId);
    }

    [Fact]
    public async Task ForwardAsync_Case3_ShouldPickFirstActiveInstanceAndSkipDraining()
    {
        var forwarder = new RecordingForwarder();
        var router = new MongoDiscoveryRemoteRoleRouter(new FixedTableProvider(BuildTable()), forwarder);

        var delivery = await router.ForwardAsync(BuildEnvelope("Game"));

        Assert.Equal(RoleRouteDelivery.RemoteForwarded, delivery);
        var forward = Assert.Single(forwarder.Forwards);
        Assert.Equal("game-active-1", forward.Value.TargetInstanceId);
        Assert.Equal("10.0.0.1", forward.Key.Host);
        Assert.Equal(7001, forward.Key.Port);
        Assert.Equal(EndpointAddressKind.IPv4, forward.Key.AddressKind);
    }

    [Fact]
    public async Task ForwardAsync_Case2_ShouldResolveKnownInstanceIdIncludingDraining()
    {
        var forwarder = new RecordingForwarder();
        var router = new MongoDiscoveryRemoteRoleRouter(new FixedTableProvider(BuildTable()), forwarder);

        await router.ForwardAsync(BuildEnvelope("Game", "game-draining-1"));

        var forward = Assert.Single(forwarder.Forwards);
        Assert.Equal("game-draining-1", forward.Value.TargetInstanceId);
        Assert.Equal("10.0.0.2", forward.Key.Host);
        Assert.Equal(7002, forward.Key.Port);
    }

    [Fact]
    public async Task ForwardAsync_Case2_WithUnknownInstanceId_ShouldThrowRouteNotFound()
    {
        var forwarder = new RecordingForwarder();
        var router = new MongoDiscoveryRemoteRoleRouter(new FixedTableProvider(BuildTable()), forwarder);

        await Assert.ThrowsAsync<RouteNotFoundException>(() => router.ForwardAsync(BuildEnvelope("Game", "game-unknown-9")));
    }

    [Fact]
    public async Task ForwardAsync_Case3_WithNoActiveInstance_ShouldThrowRouteNotFound()
    {
        var forwarder = new RecordingForwarder();
        var router = new MongoDiscoveryRemoteRoleRouter(new FixedTableProvider(BuildTable()), forwarder);

        await Assert.ThrowsAsync<RouteNotFoundException>(() => router.ForwardAsync(BuildEnvelope("Match")));
    }

    [Fact]
    public async Task ForwardAsync_WithMalformedAdvertiseEndpoint_ShouldThrowEndpointFormat()
    {
        var table = RoleRouteTable.FromInstances(new List<InstanceDescriptor>
        {
            new InstanceDescriptor("Broken", "broken-1", "tcp://missing-port", InstanceStatus.Active, 0, EndpointAddressKind.DnsName, 104, DateTime.UtcNow),
        });
        var forwarder = new RecordingForwarder();
        var router = new MongoDiscoveryRemoteRoleRouter(new FixedTableProvider(table), forwarder);

        await Assert.ThrowsAsync<EndpointFormatException>(() => router.ForwardAsync(BuildEnvelope("Broken")));
    }

    [Fact]
    public async Task ForwardAsync_WithRealTcpForwarder_ShouldWriteStandardFrameWithEnvelopePayload()
    {
        using (var listener = new TcpListener(IPAddress.Loopback, 0))
        {
            listener.Start();
            var receiveTask = ReceiveOneFrameAsync(listener);
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var table = RoleRouteTable.FromInstances(new List<InstanceDescriptor>
            {
                new InstanceDescriptor("Game", "game-tcp-1", $"tcp://127.0.0.1:{port}", InstanceStatus.Active, 0, EndpointAddressKind.IPv4, 105, DateTime.UtcNow),
            });

            using (var forwarder = new TcpEnvelopeForwarder())
            {
                var router = new MongoDiscoveryRemoteRoleRouter(new FixedTableProvider(table), forwarder);
                var delivery = await router.ForwardAsync(BuildEnvelope("Game", "game-tcp-1"));
                Assert.Equal(RoleRouteDelivery.RemoteForwarded, delivery);
            }

            var envelope = await receiveTask;
            Assert.Equal("Game", envelope.TargetRole);
            Assert.Equal(99, envelope.TargetActorId);
            Assert.Equal("game-tcp-1", envelope.TargetInstanceId);
            Assert.Equal(WirePayloadMessageId, envelope.InnerMessageId);
            var innerMessage = Assert.IsType<WirePayloadMessage>(ProtoBufSerializerHelper.Deserialize(envelope.InnerMessageBytes, typeof(WirePayloadMessage)));
            Assert.Equal(42, innerMessage.PlayerId);
        }
    }

    [Fact]
    public async Task ForwardAsync_WithConcurrentCallsOnSameEndpoint_ShouldNeverInterleaveFrames()
    {
        using (var listener = new TcpListener(IPAddress.Loopback, 0))
        {
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            const int frameCount = 32;
            var receiveTask = ReceiveFramesAsync(listener, frameCount);
            var table = RoleRouteTable.FromInstances(new List<InstanceDescriptor>
            {
                new InstanceDescriptor("Game", "game-tcp-1", $"tcp://127.0.0.1:{port}", InstanceStatus.Active, 0, EndpointAddressKind.IPv4, 106, DateTime.UtcNow),
            });

            using (var forwarder = new TcpEnvelopeForwarder())
            {
                var router = new MongoDiscoveryRemoteRoleRouter(new FixedTableProvider(table), forwarder);
                var sends = new List<Task<RoleRouteDelivery>>();
                for (var index = 0; index < frameCount; index++)
                {
                    sends.Add(router.ForwardAsync(BuildEnvelope("Game", "game-tcp-1")));
                }

                await Task.WhenAll(sends);
            }

            // 同端点并发整帧写入必须串行化：每一帧的长度前缀与载荷都完整可解析，不允许交错破坏。
            var completed = await Task.WhenAny(receiveTask, Task.Delay(TimeSpan.FromSeconds(15)));
            Assert.Same(receiveTask, completed);
            var envelopes = await receiveTask;
            Assert.Equal(frameCount, envelopes.Count);
            foreach (var envelope in envelopes)
            {
                Assert.Equal("Game", envelope.TargetRole);
                Assert.Equal("game-tcp-1", envelope.TargetInstanceId);
                var innerMessage = Assert.IsType<WirePayloadMessage>(ProtoBufSerializerHelper.Deserialize(envelope.InnerMessageBytes, typeof(WirePayloadMessage)));
                Assert.Equal(42, innerMessage.PlayerId);
            }
        }
    }

    /// <summary>
    /// 接收并解码指定数量的标准帧（每帧 4B 总长 + 10B 头 + protobuf 载荷，帧间不允许交错）。
    /// </summary>
    private static async Task<List<RoleRouteEnvelopeMessage>> ReceiveFramesAsync(TcpListener listener, int frameCount)
    {
        var envelopes = new List<RoleRouteEnvelopeMessage>(frameCount);
        using (var client = await listener.AcceptTcpClientAsync())
        using (var stream = client.GetStream())
        {
            for (var index = 0; index < frameCount; index++)
            {
                var header = new byte[14];
                await ReadExactAsync(stream, header);
                var totalLength = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(0, 4));
                var messageId = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(10, 4));
                Assert.Equal(RoleRouteEnvelopeMessage.ReservedMessageId, messageId);

                var payloadLength = totalLength - header.Length;
                Assert.True(payloadLength > 0, $"The frame length prefix is corrupted: total length {totalLength}.");
                var payload = new byte[payloadLength];
                await ReadExactAsync(stream, payload);
                envelopes.Add((RoleRouteEnvelopeMessage)ProtoBufSerializerHelper.Deserialize(payload, typeof(RoleRouteEnvelopeMessage)));
            }

            listener.Stop();
        }

        return envelopes;
    }

    /// <summary>
    /// 接收并解码一帧标准格式信封消息（4B 总长 + 10B 头 + protobuf 载荷）。
    /// </summary>
    private static async Task<RoleRouteEnvelopeMessage> ReceiveOneFrameAsync(TcpListener listener)
    {
        using (var client = await listener.AcceptTcpClientAsync())
        using (var stream = client.GetStream())
        {
            var header = new byte[14];
            await ReadExactAsync(stream, header);
            var totalLength = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(0, 4));
            var messageId = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(10, 4));
            Assert.Equal(RoleRouteEnvelopeMessage.ReservedMessageId, messageId);

            var payloadLength = totalLength - header.Length;
            var payload = new byte[payloadLength];
            await ReadExactAsync(stream, payload);
            listener.Stop();
            return (RoleRouteEnvelopeMessage)ProtoBufSerializerHelper.Deserialize(payload, typeof(RoleRouteEnvelopeMessage));
        }
    }

    /// <summary>
    /// 精确读取指定字节数。
    /// </summary>
    private static async Task ReadExactAsync(NetworkStream stream, byte[] buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer, offset, buffer.Length - offset);
            if (read == 0)
            {
                throw new IOException("The sender closed the connection before the frame was complete.");
            }

            offset += read;
        }
    }

}
