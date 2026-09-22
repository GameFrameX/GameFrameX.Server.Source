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


using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace GameFrameX.NetWork.RemoteMessaging.Discovery;

/// <summary>
/// 统一端点地址解析器（C143d D15 / AC-4a）。
/// </summary>
/// <remarks>
/// The single unified endpoint parser (C143d D15 / AC-4a).
/// Every endpoint in the topology is stored as an unparsed <c>scheme://host:port</c>
/// string and parsed only at connect time by this class, so heartbeat documents,
/// <c>services__*</c> environment variables, and advertise addresses all share one
/// validation surface. Supported host shapes: domain names, container names,
/// Kubernetes Service names, IPv4 literals, and bracketed IPv6 literals
/// (<c>[::1]</c>). <see cref="System.Uri"/> is intentionally not used: it fills
/// scheme default ports for ws/wss, which would silently mask a missing port
/// (a structural error this parser must report).
/// </remarks>
public static class EndpointParser
{
    /// <summary>
    /// scheme 与 authority 之间的分隔符。
    /// </summary>
    /// <remarks>
    /// The separator between the scheme and the authority part.
    /// </remarks>
    private const string SchemeSeparator = "://";

    /// <summary>
    /// 解析统一格式的端点地址。
    /// </summary>
    /// <remarks>
    /// Parses a unified <c>scheme://host:port</c> endpoint string.
    /// The scheme must be one of tcp/kcp/ws/wss (lower or upper case; compared
    /// ordinally ignore-case and returned lower-cased); the host may be a domain,
    /// container name, Kubernetes Service name, IPv4 literal, or a bracketed IPv6
    /// literal; the port must be 1-65535. Any structural violation throws
    /// <see cref="EndpointFormatException"/> — never a partially-filled result.
    /// </remarks>
    /// <param name="endpoint">端点地址字符串（scheme://host:port）/ The endpoint string (scheme://host:port)</param>
    /// <returns>解析后的三元组 / The parsed endpoint triple</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="endpoint"/> 为 null 时抛出 / Thrown when endpoint is null</exception>
    /// <exception cref="EndpointFormatException">当 scheme 缺失/不受支持、host 缺失、端口缺失或越界时抛出 / Thrown on missing/unsupported scheme, missing host, or missing/out-of-range port</exception>
    public static ParsedEndpoint Parse(string endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint, nameof(endpoint));

        var trimmed = endpoint.Trim();
        if (trimmed.Length == 0)
        {
            throw new EndpointFormatException("The endpoint string is empty. Expected the unified 'scheme://host:port' format (e.g. 'tcp://game-1.gameframex:7777').");
        }

        var separatorIndex = trimmed.IndexOf(SchemeSeparator, StringComparison.Ordinal);
        if (separatorIndex <= 0 || separatorIndex + SchemeSeparator.Length >= trimmed.Length)
        {
            throw new EndpointFormatException($"The endpoint '{endpoint}' does not contain the required 'scheme://host:port' structure: the '://' separator with a non-empty scheme and authority is missing.");
        }

        var scheme = trimmed.Substring(0, separatorIndex).ToLowerInvariant();
        if (!IsSupportedScheme(scheme))
        {
            throw new EndpointFormatException($"The endpoint '{endpoint}' uses the unsupported scheme '{scheme}'. Supported schemes: tcp, kcp, ws, wss.");
        }

        var authority = trimmed.Substring(separatorIndex + SchemeSeparator.Length);
        return ParseAuthority(endpoint, scheme, authority);
    }

    /// <summary>
    /// 解析 authority 段（host[:port] 或 [ipv6]:port）。
    /// </summary>
    /// <remarks>
    /// Parses the authority part. Bracketed hosts are IPv6 literals; everything else
    /// splits on the last colon into host and port.
    /// </remarks>
    /// <param name="originalEndpoint">原始输入（仅用于异常消息）/ The original input (used in exception messages only)</param>
    /// <param name="scheme">已校验的小写 scheme / The validated lower-cased scheme</param>
    /// <param name="authority">authority 段 / The authority part</param>
    /// <returns>解析后的三元组 / The parsed endpoint triple</returns>
    /// <exception cref="EndpointFormatException">当结构违规时抛出 / Thrown on structural violations</exception>
    private static ParsedEndpoint ParseAuthority(string originalEndpoint, string scheme, string authority)
    {
        if (authority.Length == 0)
        {
            throw new EndpointFormatException($"The endpoint '{originalEndpoint}' has an empty host after the scheme.");
        }

        // IPv6 方括号字面量：[::1]:port
        if (authority[0] == '[')
        {
            var closingBracketIndex = authority.IndexOf(']');
            if (closingBracketIndex < 0 || closingBracketIndex == 1)
            {
                throw new EndpointFormatException($"The endpoint '{originalEndpoint}' has a malformed bracketed IPv6 host: expected '[<ipv6 literal>]:<port>'.");
            }

            var ipv6Host = authority.Substring(1, closingBracketIndex - 1);
            if (!IPAddress.TryParse(ipv6Host, out var bracketedAddress) || bracketedAddress.AddressFamily != AddressFamily.InterNetworkV6)
            {
                throw new EndpointFormatException($"The endpoint '{originalEndpoint}' has a bracketed host '{ipv6Host}' that is not a valid IPv6 literal.");
            }

            var remainder = authority.Substring(closingBracketIndex + 1);
            if (remainder.Length == 0 || remainder[0] != ':')
            {
                throw new EndpointFormatException($"The endpoint '{originalEndpoint}' is missing the port after the bracketed IPv6 host: expected '[<ipv6 literal>]:<port>'.");
            }

            var port = ParsePort(originalEndpoint, remainder.Substring(1));
            return new ParsedEndpoint(scheme, ipv6Host, port, EndpointAddressKind.IPv6);
        }

        // 域名/容器名/Service 名/IPv4：最后一个冒号分隔端口
        var lastColonIndex = authority.LastIndexOf(':');
        if (lastColonIndex < 0 || lastColonIndex == authority.Length - 1)
        {
            throw new EndpointFormatException($"The endpoint '{originalEndpoint}' is missing the port: expected 'scheme://host:port'.");
        }

        var host = authority.Substring(0, lastColonIndex);
        if (host.Length == 0)
        {
            throw new EndpointFormatException($"The endpoint '{originalEndpoint}' has an empty host before the port.");
        }

        if (IPAddress.TryParse(host, out var address))
        {
            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                throw new EndpointFormatException($"The endpoint '{originalEndpoint}' uses an unbracketed IPv6 literal '{host}'; IPv6 hosts must be bracketed as '[<ipv6 literal>]:<port>'.");
            }

            return new ParsedEndpoint(scheme, host, ParsePort(originalEndpoint, authority.Substring(lastColonIndex + 1)), EndpointAddressKind.IPv4);
        }

        return new ParsedEndpoint(scheme, host, ParsePort(originalEndpoint, authority.Substring(lastColonIndex + 1)), EndpointAddressKind.DnsName);
    }

    /// <summary>
    /// 解析并校验端口（1–65535）。
    /// </summary>
    /// <remarks>
    /// Parses and validates the port (1-65535). A port of 0 is rejected on purpose:
    /// it is never a routable advertise port, only a bind-side wildcard.
    /// </remarks>
    /// <param name="originalEndpoint">原始输入（仅用于异常消息）/ The original input (used in exception messages only)</param>
    /// <param name="portText">端口文本 / The port text</param>
    /// <returns>端口值 / The port value</returns>
    /// <exception cref="EndpointFormatException">当端口非数字或越界时抛出 / Thrown when the port is not numeric or out of range</exception>
    private static int ParsePort(string originalEndpoint, string portText)
    {
        if (!int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port < 1 || port > 65535)
        {
            throw new EndpointFormatException($"The endpoint '{originalEndpoint}' has the invalid port '{portText}': expected an integer between 1 and 65535.");
        }

        return port;
    }

    /// <summary>
    /// 判断 scheme 是否受支持（tcp/kcp/ws/wss）。
    /// </summary>
    /// <remarks>
    /// Determines whether the scheme is supported (tcp/kcp/ws/wss).
    /// </remarks>
    /// <param name="scheme">小写 scheme / The lower-cased scheme</param>
    /// <returns>受支持返回 true；否则 false / true when supported; otherwise false</returns>
    private static bool IsSupportedScheme(string scheme)
    {
        return string.Equals(scheme, "tcp", StringComparison.Ordinal)
               || string.Equals(scheme, "kcp", StringComparison.Ordinal)
               || string.Equals(scheme, "ws", StringComparison.Ordinal)
               || string.Equals(scheme, "wss", StringComparison.Ordinal);
    }
}
