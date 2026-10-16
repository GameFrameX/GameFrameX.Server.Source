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
/// 协议版本握手被拒绝（major 不符）。客户端据此显式失败并失效连接（禁止半开复用）。
/// </summary>
/// <remarks>
/// Thrown when the protocol version handshake is rejected (a major mismatch). The client
/// fails the call explicitly and invalidates the connection on this exception (half-open
/// reuse is forbidden).
/// </remarks>
internal sealed class ProtocolVersionMismatchException : Exception
{
    /// <summary>
    /// 初始化协议版本不匹配异常。
    /// </summary>
    /// <remarks>
    /// Initializes the protocol version mismatch exception.
    /// </remarks>
    /// <param name="message">本地化错误消息 / The localized error message</param>
    /// <param name="local">本端协议版本 / The local protocol version</param>
    /// <param name="remote">远端协议版本 / The remote protocol version</param>
    public ProtocolVersionMismatchException(string message, ProtocolVersion local, ProtocolVersion remote)
        : base(message)
    {
        Local = local;
        Remote = remote;
    }

    /// <summary>
    /// 获取本端协议版本。
    /// </summary>
    /// <remarks>
    /// Gets the local protocol version.
    /// </remarks>
    public ProtocolVersion Local { get; }

    /// <summary>
    /// 获取远端协议版本。
    /// </summary>
    /// <remarks>
    /// Gets the remote protocol version.
    /// </remarks>
    public ProtocolVersion Remote { get; }
}
