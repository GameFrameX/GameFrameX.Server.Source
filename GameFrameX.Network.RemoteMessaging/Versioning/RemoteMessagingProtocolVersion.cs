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
/// RemoteMessaging 连接级协议版本的单一事实源。握手阶段交换此版本；Major 不一致显式拒绝。
/// 演进规则：<see cref="Current"/> 的 Major 递增表示 wire 契约不兼容变更（禁止跨 Major 混布），
/// Minor 递增表示向后兼容演进（protobuf 字段规则，见 <see cref="ProtocolVersionAttribute"/>）。
/// </summary>
/// <remarks>
/// The single source of truth for the connection-level RemoteMessaging protocol version. This version is
/// exchanged during the connection handshake; differing majors are explicitly rejected. Evolution rules:
/// incrementing <see cref="Current"/>'s major marks a breaking wire-contract change (cross-major mixed
/// deployments are forbidden); incrementing the minor marks backward-compatible evolution (the protobuf
/// field rules, see <see cref="ProtocolVersionAttribute"/>).
/// </remarks>
public static class RemoteMessagingProtocolVersion
{
    /// <summary>
    /// 本程序集实现的 RemoteMessaging 协议版本。C252 协商能力首版为 (1,0)。
    /// </summary>
    /// <remarks>
    /// The RemoteMessaging protocol version implemented by this assembly. The first negotiated
    /// version (C252) is (1,0).
    /// </remarks>
    public static ProtocolVersion Current => new(1, 0);

    /// <summary>
    /// 协商能力落地前的部署的等效版本。握手超时（对端为未实现协商的旧节点）时按此版本放行；
    /// 其 wire 格式与 (1,0) 相同（14 字节帧自始未变）。
    /// </summary>
    /// <remarks>
    /// The equivalent version for deployments predating the negotiation capability. When the handshake
    /// times out (the peer is an old node without negotiation), the connection is allowed under this
    /// version; its wire format is identical to (1,0) (the 14-byte frame has never changed).
    /// </remarks>
    public static ProtocolVersion Legacy => new(1, 0);
}
