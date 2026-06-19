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

using System.Collections.Concurrent;
using System.Buffers.Binary;
using GameFrameX.NetWork;

namespace GameFrameX.NetWork.Abstractions;

/// <summary>
/// 可靠会话状态。
/// </summary>
public sealed class ReliableSessionState
{
    private readonly ConcurrentDictionary<ulong, CachedResponse> _responseCache = new();
    private readonly ConcurrentQueue<ulong> _responseOrder = new();

    /// <summary>
    /// 通道数据键。
    /// </summary>
    public const string ChannelDataKey = "__ReliableSessionState";

    /// <summary>
    /// 获取会话ID。
    /// </summary>
    public ulong SessionId { get; private set; }

    /// <summary>
    /// 获取最后已处理的客户端序列号。
    /// </summary>
    public ulong LastProcessedClientSequence { get; private set; }

    /// <summary>
    /// 获取最后 ACK 的客户端序列号。
    /// </summary>
    public ulong LastAckedClientSequence { get; private set; }

    /// <summary>
    /// 获取过期时间。
    /// </summary>
    public DateTime ExpireAt { get; private set; } = DateTime.UtcNow.AddSeconds(PacketHeaderLayout.ServerSessionTtlSeconds);

    /// <summary>
    /// 获取待响应缓存数量。
    /// </summary>
    public int PendingResponseCacheCount
    {
        get { return _responseCache.Count; }
    }

    /// <summary>
    /// 处理入站可靠头。
    /// </summary>
    /// <param name="header">消息头 / Message header</param>
    /// <returns>处理结果 / Process result</returns>
    public ReliablePacketProcessResult ProcessInbound(INetworkMessageHeader header)
    {
        ArgumentNullException.ThrowIfNull(header, nameof(header));

        if (!header.HasReliableExtension)
        {
            return ReliablePacketProcessResult.Unsupported;
        }

        if (PacketHeaderLayout.HasControlFlag(header.HeaderFlags))
        {
            if (SessionId == 0)
            {
                SessionId = header.SessionId;
            }
            else if (header.SessionId != 0 && header.SessionId != SessionId)
            {
                return ReliablePacketProcessResult.Gap;
            }

            ExpireAt = DateTime.UtcNow.AddSeconds(PacketHeaderLayout.ServerSessionTtlSeconds);
            header.AckSequence = LastAckedClientSequence;
            return ReliablePacketProcessResult.Control;
        }

        if (SessionId == 0)
        {
            SessionId = header.SessionId;
        }
        else if (header.SessionId != 0 && header.SessionId != SessionId)
        {
            return ReliablePacketProcessResult.Gap;
        }

        var result = MessageProtoHelper.ClassifyReliableSequence(header, LastProcessedClientSequence);
        if (result == ReliablePacketProcessResult.InOrder)
        {
            LastProcessedClientSequence = header.ReliableSequence;
            LastAckedClientSequence = header.ReliableSequence;
        }

        ExpireAt = DateTime.UtcNow.AddSeconds(PacketHeaderLayout.ServerSessionTtlSeconds);
        header.AckSequence = LastAckedClientSequence;
        return result;
    }

    /// <summary>
    /// 缓存可靠响应。
    /// </summary>
    /// <param name="sequence">客户端可靠序列号 / Client reliable sequence</param>
    /// <param name="responseData">已编码响应数据 / Encoded response data</param>
    public void CacheResponse(ulong sequence, byte[] responseData)
    {
        ArgumentNullException.ThrowIfNull(responseData, nameof(responseData));
        _responseCache[sequence] = new CachedResponse(responseData.ToArray(), DateTime.UtcNow.AddSeconds(PacketHeaderLayout.ResponseCacheTtlSeconds));
        _responseOrder.Enqueue(sequence);
        TrimResponseCache();
    }

    /// <summary>
    /// 尝试获取缓存响应。
    /// </summary>
    /// <param name="sequence">客户端可靠序列号 / Client reliable sequence</param>
    /// <param name="responseData">已编码响应数据 / Encoded response data</param>
    /// <returns>命中返回 true / true if cached response exists</returns>
    public bool TryGetCachedResponse(ulong sequence, out byte[] responseData)
    {
        if (_responseCache.TryGetValue(sequence, out var cached))
        {
            if (cached.ExpireAt < DateTime.UtcNow)
            {
                _responseCache.TryRemove(sequence, out _);
                responseData = default;
                return false;
            }

            responseData = cached.Data.ToArray();
            return true;
        }

        responseData = default;
        return false;
    }

    /// <summary>
    /// 构造 ACK 控制包。
    /// </summary>
    /// <param name="header">入站消息头 / Inbound message header</param>
    /// <returns>ACK 控制包字节数组 / Encoded ACK control packet</returns>
    public byte[] BuildAckPacket(INetworkMessageHeader header)
    {
        ArgumentNullException.ThrowIfNull(header, nameof(header));

        var packet = new byte[PacketHeaderLayout.ReliableHeaderLength];
        BinaryPrimitives.WriteUInt32BigEndian(packet.AsSpan(PacketHeaderLayout.PacketLengthOffset), PacketHeaderLayout.ReliableHeaderLength);
        packet[PacketHeaderLayout.OperationTypeOffset] = header.OperationType;
        packet[PacketHeaderLayout.ZipFlagOffset] = header.ZipFlag;
        var headerFlags = (ushort)(ReliableHeaderFlags.ProtocolVersion1 | ReliableHeaderFlags.Reliable | ReliableHeaderFlags.Ack | ReliableHeaderFlags.Control);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(PacketHeaderLayout.HeaderFlagsOffset), headerFlags);
        BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(PacketHeaderLayout.UniqueIdOffset), header.UniqueId);
        BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(PacketHeaderLayout.MessageIdOffset), header.MessageId);
        BinaryPrimitives.WriteUInt64BigEndian(packet.AsSpan(PacketHeaderLayout.SessionIdOffset), SessionId == 0 ? header.SessionId : SessionId);
        BinaryPrimitives.WriteUInt64BigEndian(packet.AsSpan(PacketHeaderLayout.ReliableSequenceOffset), 0);
        BinaryPrimitives.WriteUInt64BigEndian(packet.AsSpan(PacketHeaderLayout.AckSequenceOffset), LastAckedClientSequence);
        return packet;
    }

    private void TrimResponseCache()
    {
        while (_responseCache.Count > PacketHeaderLayout.PendingQueueMaxCount && _responseOrder.TryDequeue(out var oldSequence))
        {
            _responseCache.TryRemove(oldSequence, out _);
        }
    }

    private sealed record CachedResponse(byte[] Data, DateTime ExpireAt);
}
