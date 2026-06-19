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

using System.Buffers;
using System.Buffers.Binary;
using GameFrameX.NetWork;
using GameFrameX.NetWork.Abstractions;
using GameFrameX.NetWork.Messages;
using GameFrameX.Foundation.Logger;

namespace GameFrameX.NetWork.Message;

/// <summary>
/// 基础消息解码处理器
/// </summary>
public class DefaultMessageDecoderHandler : BaseMessageDecoderHandler
{
    /// <summary>
    /// 消息头长度
    /// </summary>
    public override ushort PackageHeaderLength { get; } = PacketHeaderLayout.BaseHeaderLength;

    /// <summary>
    /// 消息解码
    /// </summary>
    /// <param name="sequence"></param>
    /// <returns></returns>
    public override IMessage Handler(ref ReadOnlySequence<byte> sequence)
    {
        try
        {
            var data = sequence.ToArray();
            if (data.Length < PacketHeaderLayout.BaseHeaderLength)
            {
                return null;
            }

            // 消息总长度
            var totalLength = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(PacketHeaderLayout.PacketLengthOffset));
            if (totalLength < PacketHeaderLayout.BaseHeaderLength || data.Length < totalLength)
            {
                return null;
            }

            // 操作类型
            var operationType = data[PacketHeaderLayout.OperationTypeOffset];
            // 压缩标记
            var zipFlag = data[PacketHeaderLayout.ZipFlagOffset];
            // 头标记
            var headerFlags = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(PacketHeaderLayout.HeaderFlagsOffset));
            if ((headerFlags & PacketHeaderLayout.ProtocolVersionMask) != (ushort)ReliableHeaderFlags.ProtocolVersion1)
            {
                return null;
            }

            // 唯一ID
            var uniqueId = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(PacketHeaderLayout.UniqueIdOffset));
            // 消息ID
            var messageId = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(PacketHeaderLayout.MessageIdOffset));
            var headerLength = PacketHeaderLayout.GetHeaderLength(headerFlags);
            if (headerLength > totalLength)
            {
                return null;
            }

            // 消息对象头
            var messageObjectHeader = new MessageObjectHeader
            {
                OperationType = operationType,
                ZipFlag = zipFlag,
                UniqueId = uniqueId,
                MessageId = messageId,
                HeaderFlags = headerFlags,
                SessionId = headerLength >= PacketHeaderLayout.ReliableHeaderLength ? BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan(PacketHeaderLayout.SessionIdOffset)) : default,
                ReliableSequence = headerLength >= PacketHeaderLayout.ReliableHeaderLength ? BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan(PacketHeaderLayout.ReliableSequenceOffset)) : default,
                AckSequence = headerLength >= PacketHeaderLayout.ReliableHeaderLength ? BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan(PacketHeaderLayout.AckSequenceOffset)) : default,
            };
            // 消息内容
            var messageData = data.AsSpan(headerLength, (int)totalLength - headerLength).ToArray();
            if (messageObjectHeader.ZipFlag > 0)
            {
                ArgumentNullException.ThrowIfNull(DecompressHandler, nameof(DecompressHandler));
                messageData = DecompressHandler.Handler(messageData);
            }

            var messageType = MessageProtoHelper.GetMessageTypeById(messageObjectHeader.MessageId);
            if (messageType == null && !PacketHeaderLayout.HasControlFlag(messageObjectHeader.HeaderFlags))
            {
                return null;
            }

            if (messageObjectHeader.MessageId >= 0)
            {
                // 外部消息
                return NetworkMessagePackage.Create(messageObjectHeader, messageData, messageType);
            }

            // 内部消息
            return NetworkMessagePackage.Create(messageObjectHeader, messageData, messageType);
        }
        catch (Exception e)
        {
            LogHelper.Fatal<string>("MessageObjectDecodeException: {exception}", e.ToString());
            return null;
        }
    }
}
