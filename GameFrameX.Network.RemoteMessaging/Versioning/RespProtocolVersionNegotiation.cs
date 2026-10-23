// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
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
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:   https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using GameFrameX.Network.Abstractions;
using GameFrameX.Network.Messages;
using ProtoBuf;

namespace GameFrameX.Network.RemoteMessaging.Versioning;

/// <summary>
/// 协议版本握手响应。服务端漏斗短路回包：携带服务端协议版本与 major 比对结论；
/// <see cref="Accepted"/> 为 false 时服务端在回包后关闭会话（无半开连接残留）。
/// </summary>
/// <remarks>
/// The protocol version handshake response. Replied by the server funnel's short circuit: it carries
/// the server's protocol version and the major-comparison verdict. When <see cref="Accepted"/> is
/// false, the server closes the session right after replying (no half-open connection residue).
/// </remarks>
[ProtoContract]
[MessageTypeHandler(ReservedMessageId)]
[ProtocolVersion(1, 0)]
public sealed class RespProtocolVersionNegotiation : MessageObject, IResponseMessage
{
    /// <summary>
    /// 预留消息 Id（负数内部段 -130 的子号 12）。
    /// </summary>
    /// <remarks>
    /// The reserved message id (inner segment -130, sub id 12).
    /// </remarks>
    public const int ReservedMessageId = unchecked((int)((-130 << 16) + 12));

    /// <summary>
    /// 获取或设置服务端协议 Major 版本。
    /// </summary>
    /// <remarks>
    /// Gets or sets the server's protocol major version.
    /// </remarks>
    [ProtoMember(1)]
    public int Major { get; set; }

    /// <summary>
    /// 获取或设置服务端协议 Minor 版本。
    /// </summary>
    /// <remarks>
    /// Gets or sets the server's protocol minor version.
    /// </remarks>
    [ProtoMember(2)]
    public int Minor { get; set; }

    /// <summary>
    /// 获取或设置服务端 major 比对结论；false 表示对端 major 与本端不一致，连接将被拒绝。
    /// </summary>
    /// <remarks>
    /// Gets or sets the server's major-comparison verdict; false means the peer's major differs
    /// from the local one and the connection will be rejected.
    /// </remarks>
    [ProtoMember(3)]
    public bool Accepted { get; set; }

    /// <summary>
    /// 获取或设置错误码（IResponseMessage 契约）：0 表示接受；1 表示 major 不符拒绝；2 表示节点间鉴权失败拒绝（C272：共享密钥已配置而对端凭据缺失或验签失败）。
    /// </summary>
    /// <remarks>
    /// Gets or sets the error code (the IResponseMessage contract): 0 means accepted;
    /// 1 means rejected due to a major mismatch; 2 means rejected due to inter-node authentication
    /// failure (C272: the shared secret is configured but the peer's credential is missing or
    /// failed verification).
    /// </remarks>
    [ProtoMember(4)]
    public int ErrorCode { get; set; }

    /// <summary>
    /// 清空消息内容。重置 Major、Minor 为 0，Accepted 为 false，ErrorCode 为 0。
    /// </summary>
    /// <remarks>
    /// Clears the message content. Resets Major and Minor to 0, Accepted to false, and ErrorCode to 0.
    /// </remarks>
    public override void Clear()
    {
        Major = 0;
        Minor = 0;
        Accepted = false;
        ErrorCode = 0;
    }
}
