// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or to violate the legal rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Network.RemoteMessaging.Transport;

/// <summary>
/// 服务器间传输 scheme 词汇表（沿用 <see cref="EndpointParser"/> 支持集：tcp/kcp/ws/wss）。
/// </summary>
/// <remarks>
/// The inter-server transport scheme vocabulary (mirroring the <see cref="EndpointParser"/>
/// supported set: tcp/kcp/ws/wss). Schemes are compared case-insensitively and normalized to
/// lower case; a bare <c>udp</c> scheme is rejected at the parsing layer
/// (<c>EndpointFormatException</c> — UnsupportedScheme). <see cref="PreferenceOrder"/> fixes the
/// deterministic multi-scheme selection with tcp as the universal fallback.
/// </remarks>
internal static class TransportSchemes
{
    /// <summary>
    /// TCP scheme（通用回落传输）。
    /// </summary>
    /// <remarks>
    /// The TCP scheme (the universal fallback transport).
    /// </remarks>
    public const string Tcp = "tcp";

    /// <summary>
    /// KCP scheme。
    /// </summary>
    /// <remarks>
    /// The KCP scheme.
    /// </remarks>
    public const string Kcp = "kcp";

    /// <summary>
    /// WebSocket scheme。
    /// </summary>
    /// <remarks>
    /// The WebSocket scheme.
    /// </remarks>
    public const string WebSocket = "ws";

    /// <summary>
    /// 安全 WebSocket scheme。
    /// </summary>
    /// <remarks>
    /// The secure WebSocket scheme.
    /// </remarks>
    public const string SecureWebSocket = "wss";

    /// <summary>
    /// 多 scheme 端点并存时的确定性偏好序（kcp &gt; ws &gt; wss &gt; tcp，tcp 为通用回落固定最后）。
    /// </summary>
    /// <remarks>
    /// The deterministic preference order when several scheme endpoints coexist for one service
    /// (kcp &gt; ws &gt; wss &gt; tcp; tcp is the universal fallback and always ordered last).
    /// </remarks>
    public static readonly string[] PreferenceOrder = { Kcp, WebSocket, SecureWebSocket, Tcp };

    /// <summary>
    /// 判断 scheme 是否受支持（大小写不敏感）。
    /// </summary>
    /// <remarks>
    /// Returns whether the scheme is supported (case-insensitive).
    /// </remarks>
    /// <param name="scheme">待判断的 scheme / The scheme to check</param>
    /// <returns>受支持返回 true；否则 false / true when supported; otherwise false</returns>
    public static bool IsSupported(string scheme)
    {
        return string.Equals(scheme, Tcp, StringComparison.OrdinalIgnoreCase)
               || string.Equals(scheme, Kcp, StringComparison.OrdinalIgnoreCase)
               || string.Equals(scheme, WebSocket, StringComparison.OrdinalIgnoreCase)
               || string.Equals(scheme, SecureWebSocket, StringComparison.OrdinalIgnoreCase);
    }
}
