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

namespace GameFrameX.NetWork;

/// <summary>
/// 网络包头布局常量。
/// </summary>
public static class PacketHeaderLayout
{
    /// <summary>
    /// 基础包头长度。
    /// </summary>
    public const int BaseHeaderLength = 16;

    /// <summary>
    /// 可靠扩展头长度。
    /// </summary>
    public const int ReliableExtensionLength = 24;

    /// <summary>
    /// 完整可靠包头长度。
    /// </summary>
    public const int ReliableHeaderLength = BaseHeaderLength + ReliableExtensionLength;

    /// <summary>
    /// 协议版本掩码。
    /// </summary>
    public const ushort ProtocolVersionMask = 0x000F;

    /// <summary>
    /// 静默恢复窗口秒数。
    /// </summary>
    public const int SilentResumeWindowSeconds = 10;

    /// <summary>
    /// 服务端可靠会话 TTL 秒数。
    /// </summary>
    public const int ServerSessionTtlSeconds = 30;

    /// <summary>
    /// 响应缓存 TTL 秒数。
    /// </summary>
    public const int ResponseCacheTtlSeconds = 30;

    /// <summary>
    /// 可靠重试次数上限。
    /// </summary>
    public const int ReliableRetryLimit = 5;

    /// <summary>
    /// 待处理队列数量上限。
    /// </summary>
    public const int PendingQueueMaxCount = 1024;

    /// <summary>
    /// 待处理队列字节上限。
    /// </summary>
    public const int PendingQueueMaxBytes = 4 * 1024 * 1024;

    /// <summary>
    /// 单条待处理消息字节上限。
    /// </summary>
    public const int PendingMessageMaxBytes = 256 * 1024;

    /// <summary>
    /// PacketLength 字段偏移。
    /// </summary>
    public const int PacketLengthOffset = 0;

    /// <summary>
    /// OperationType 字段偏移。
    /// </summary>
    public const int OperationTypeOffset = 4;

    /// <summary>
    /// ZipFlag 字段偏移。
    /// </summary>
    public const int ZipFlagOffset = 5;

    /// <summary>
    /// HeaderFlags 字段偏移。
    /// </summary>
    public const int HeaderFlagsOffset = 6;

    /// <summary>
    /// UniqueId 字段偏移。
    /// </summary>
    public const int UniqueIdOffset = 8;

    /// <summary>
    /// MessageId 字段偏移。
    /// </summary>
    public const int MessageIdOffset = 12;

    /// <summary>
    /// SessionId 字段偏移。
    /// </summary>
    public const int SessionIdOffset = 16;

    /// <summary>
    /// ReliableSequence 字段偏移。
    /// </summary>
    public const int ReliableSequenceOffset = 24;

    /// <summary>
    /// AckSequence 字段偏移。
    /// </summary>
    public const int AckSequenceOffset = 32;

    /// <summary>
    /// 判断头标记是否包含可靠扩展。
    /// </summary>
    /// <param name="headerFlags">头标记 / Header flags</param>
    /// <returns>包含可靠扩展返回 true / true if reliable extension is present</returns>
    public static bool HasReliableExtension(ushort headerFlags)
    {
        return (headerFlags & (ushort)ReliableHeaderFlags.Reliable) != 0;
    }

    /// <summary>
    /// 判断头标记是否表示可靠控制包。
    /// </summary>
    /// <param name="headerFlags">头标记 / Header flags</param>
    /// <returns>控制包返回 true / true if packet is a reliable control packet</returns>
    public static bool HasControlFlag(ushort headerFlags)
    {
        const ushort controlMask = (ushort)(ReliableHeaderFlags.Ack | ReliableHeaderFlags.Control | ReliableHeaderFlags.Resume);
        return (headerFlags & controlMask) != 0;
    }

    /// <summary>
    /// 根据头标记获取包头长度。
    /// </summary>
    /// <param name="headerFlags">头标记 / Header flags</param>
    /// <returns>包头长度 / Header length</returns>
    public static int GetHeaderLength(ushort headerFlags)
    {
        return HasReliableExtension(headerFlags) ? ReliableHeaderLength : BaseHeaderLength;
    }
}
