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

namespace GameFrameX.Network.RemoteMessaging.Versioning;

/// <summary>
/// 协议版本协商器。提供连接级 major 比较与消息级 <see cref="ProtocolVersionAttribute"/> 消费语义：
/// 连接握手交换 <see cref="RemoteMessagingProtocolVersion"/>，major 不一致显式拒绝；
/// 消息级检查按「未标注恒兼容；标注则消息 major 不超过远端协商 major」判定。
/// </summary>
/// <remarks>
/// Protocol version negotiator. Provides the connection-level major comparison and the
/// message-level <see cref="ProtocolVersionAttribute"/> consumption semantics: the connection
/// handshake exchanges <see cref="RemoteMessagingProtocolVersion"/> and rejects on differing
/// majors; the message-level check follows "unannotated is always compatible; an annotated
/// message's major must not exceed the negotiated remote major".
/// </remarks>
public interface IProtocolVersionNegotiator
{
    /// <summary>
    /// 检查连接级协议版本兼容性：major 一致即兼容，与 minor 无关。
    /// </summary>
    /// <remarks>
    /// Checks connection-level protocol version compatibility: matching majors are compatible,
    /// regardless of minor versions.
    /// </remarks>
    /// <param name="local">本端协议版本 / The local protocol version</param>
    /// <param name="remote">远端协议版本 / The remote protocol version</param>
    /// <returns>true 兼容；false 不兼容（应拒绝连接）/ true if compatible; false if incompatible (the connection should be rejected)</returns>
    bool IsConnectionCompatible(ProtocolVersion local, ProtocolVersion remote);

    /// <summary>
    /// 检查指定消息类型对远端协商版本是否兼容：未标注的消息（基础契约）恒兼容；
    /// 标注 <see cref="ProtocolVersionAttribute"/> 的消息要求其 major 不超过远端协商 major
    /// （minor 不阻断——protobuf 向后兼容演进规则）。
    /// </summary>
    /// <remarks>
    /// Checks whether the specified message type is compatible with the negotiated remote version:
    /// unannotated messages (the base contract) are always compatible; messages annotated with
    /// <see cref="ProtocolVersionAttribute"/> require their major not to exceed the negotiated
    /// remote major (minor never blocks — the protobuf backward-compatible evolution rules).
    /// </remarks>
    /// <param name="messageType">消息类型 / The message type to check</param>
    /// <param name="remoteVersion">远端协商版本 / The negotiated remote version</param>
    /// <returns>true 兼容；false 不兼容（该消息不应发往此对端）/ true if compatible; false if incompatible (the message must not be sent to this peer)</returns>
    bool IsMessageCompatible(Type messageType, ProtocolVersion remoteVersion);

    /// <summary>
    /// 获取指定消息类型的协议版本信息。
    /// </summary>
    /// <remarks>
    /// Gets the protocol version information for the specified message type.
    /// </remarks>
    /// <param name="messageType">消息类型 / The message type to query</param>
    /// <returns>版本字符串；未标注特性时返回 "0.0" / The version string; returns "0.0" when no attribute is present</returns>
    string GetVersion(Type messageType);
}
