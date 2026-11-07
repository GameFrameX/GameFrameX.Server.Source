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
using GameFrameX.Foundation.Extensions;
using GameFrameX.Network.Abstractions;
using GameFrameX.SuperSocket.ProtoBase;

namespace GameFrameX.Network.Message;

/// <summary>
/// 消息对象流水线过滤处理器
/// </summary>
public sealed class MessageObjectPipelineFilter : PipelineFilterBase<IMessage>
{
    /// <summary>
    /// 解析函数。按 4 字节大端总长切帧：
    /// 缓冲不足时返回 null 且不消费字节（等待管线累积后续数据后重入，部分帧安全化），
    /// 仅在长度前缀真为 0 时跳过全部已缓冲字节（沿用历史零长丢弃行为）。
    /// </summary>
    /// <remarks>
    /// Frame parsing by the 4-byte big-endian total length: when the buffer holds fewer bytes
    /// than a full frame (or fewer than the 4 header bytes), returns null WITHOUT consuming so the
    /// pipeline waits for more data and re-enters (partial-frame safety). Skipping the whole
    /// buffered sequence only happens for a genuine zero length prefix (legacy behavior).
    /// <para>此前实现对部分帧先按 totalLength 切片（越界抛
    /// <see cref="ArgumentOutOfRangeException"/>）或 AdvanceToEnd（吞掉半个帧导致后续字节错位），
    /// 分段/分块到达后成为必现路径（发现期形态：调试链路曾以 StreamMode 分块送达复现，块边界任意、
    /// 接收侧连续排水；终版为消息模式，缺陷对 TCP 分段到达仍真实存在）。该修复同时消除
    /// TCP 路径下分段到达时丢半帧的潜在反序列化错位。</para>
    /// </remarks>
    /// <param name="reader">序列读取器 / The sequence reader</param>
    /// <returns>解码出的消息；缓冲不足时为 null / The decoded message, or null while awaiting more data</returns>
    public override IMessage Filter(ref SequenceReader<byte> reader)
    {
        // 头部不足 4 字节：等待管线累积，不消费（消费会吞掉半个长度前缀）
        if (reader.Remaining < sizeof(uint))
        {
            return default;
        }

        reader.TryPeekBigEndianValue(out uint totalLength);
        if (totalLength == 0)
        {
            reader.AdvanceToEnd();
            return default;
        }

        // 整帧未到齐：等待管线累积，不消费（此前实现在此处越界切片或整段吞掉）
        if (reader.Remaining < totalLength)
        {
            return default;
        }

        var pack = reader.Sequence;
        var readBuffer = pack.Slice(pack.Start, totalLength);
        reader.Advance(totalLength);
        return MessageHelper.DecoderHandler.Handler(ref readBuffer);
    }
}