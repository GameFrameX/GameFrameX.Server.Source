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
using GameFrameX.Foundation.Localization.Core;

namespace GameFrameX.Discovery;

/// <summary>
/// 统一端点地址解析器。
/// </summary>
/// <remarks>
/// The single unified endpoint parser.
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
        ArgumentNullException.ThrowIfNull(endpoint);

        var trimmed = endpoint.Trim();
        if (trimmed.Length == 0)
        {
            // Localization: Discovery.Endpoint.EmptyString - 端点字符串为空；期望统一的 scheme://host:port 格式（如 tcp://game-1.gameframex:7777）
            throw new EndpointFormatException(LocalizationService.GetString(Localization.Keys.Discovery.Endpoint.EmptyString));
        }

        var separatorIndex = trimmed.IndexOf(SchemeSeparator, StringComparison.Ordinal);
        if (separatorIndex <= 0 || separatorIndex + SchemeSeparator.Length >= trimmed.Length)
        {
            // Localization: Discovery.Endpoint.MissingSchemeSeparator - 端点 '{0}' 缺少必需的 scheme://host:port 结构：缺少带非空 scheme 与 authority 的 '://' 分隔符
            throw new EndpointFormatException(LocalizationService.GetString(Localization.Keys.Discovery.Endpoint.MissingSchemeSeparator, endpoint));
        }

        var scheme = trimmed.Substring(0, separatorIndex).ToLowerInvariant();
        if (!IsSupportedScheme(scheme))
        {
            // Localization: Discovery.Endpoint.UnsupportedScheme - 端点 '{0}' 使用了不受支持的 scheme '{1}'。支持的 scheme：{2}。
            throw new EndpointFormatException(LocalizationService.GetString(Localization.Keys.Discovery.Endpoint.UnsupportedScheme, endpoint, scheme, "tcp, kcp, ws, wss"));
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
            // Localization: Discovery.Endpoint.EmptyHostAfterScheme - 端点 '{0}' 在 scheme 之后 host 为空
            throw new EndpointFormatException(LocalizationService.GetString(Localization.Keys.Discovery.Endpoint.EmptyHostAfterScheme, originalEndpoint));
        }

        // IPv6 方括号字面量：[::1]:port
        if (authority[0] == '[')
        {
            return ParseBracketedIpv6Authority(originalEndpoint, scheme, authority);
        }

        return ParseHostPortAuthority(originalEndpoint, scheme, authority);
    }

    /// <summary>
    /// 解析方括号 IPv6 字面量形式的 authority（[ipv6 literal]:port）。
    /// </summary>
    /// <remarks>
    /// Parses the bracketed IPv6 literal form of authority.
    /// </remarks>
    /// <param name="originalEndpoint">原始输入（仅用于异常消息）/ The original input (used in exception messages only)</param>
    /// <param name="scheme">已校验的小写 scheme / The validated lower-cased scheme</param>
    /// <param name="authority">以 <c>[</c> 开头的 authority 段 / The authority part starting with <c>[</c></param>
    /// <returns>解析后的三元组 / The parsed endpoint triple</returns>
    /// <exception cref="EndpointFormatException">当结构违规时抛出 / Thrown on structural violations</exception>
    private static ParsedEndpoint ParseBracketedIpv6Authority(string originalEndpoint, string scheme, string authority)
    {
        var closingBracketIndex = authority.IndexOf(']');
        if (closingBracketIndex < 0 || closingBracketIndex == 1)
        {
            // Localization: Discovery.Endpoint.BracketedIpv6Malformed - 端点 '{0}' 的方括号 IPv6 host 格式错误：期望 '[IPv6 字面量]:端口'
            throw new EndpointFormatException(LocalizationService.GetString(Localization.Keys.Discovery.Endpoint.BracketedIpv6Malformed, originalEndpoint));
        }

        var ipv6Host = authority.Substring(1, closingBracketIndex - 1);
        if (!IPAddress.TryParse(ipv6Host, out var bracketedAddress) || bracketedAddress.AddressFamily != AddressFamily.InterNetworkV6)
        {
            // Localization: Discovery.Endpoint.BracketedHostNotIpv6 - 端点 '{0}' 的方括号 host '{1}' 不是合法的 IPv6 字面量
            throw new EndpointFormatException(LocalizationService.GetString(Localization.Keys.Discovery.Endpoint.BracketedHostNotIpv6, originalEndpoint, ipv6Host));
        }

        var remainder = authority.Substring(closingBracketIndex + 1);
        if (remainder.Length == 0 || remainder[0] != ':')
        {
            // Localization: Discovery.Endpoint.MissingPortAfterBracketedIpv6 - 端点 '{0}' 在方括号 IPv6 host 之后缺少端口：期望 '[IPv6 字面量]:端口'
            throw new EndpointFormatException(LocalizationService.GetString(Localization.Keys.Discovery.Endpoint.MissingPortAfterBracketedIpv6, originalEndpoint));
        }

        var port = ParsePort(originalEndpoint, remainder.Substring(1));
        return new ParsedEndpoint(scheme, ipv6Host, port, EndpointAddressKind.IPv6);
    }

    /// <summary>
    /// 解析非方括号形式的 authority（域名/容器名/Service 名/IPv4，最后一个冒号分隔端口）。
    /// </summary>
    /// <remarks>
    /// Parses the non-bracketed form of authority (domain / container / Service name
    /// / IPv4 literal), splitting on the last colon into host and port.
    /// </remarks>
    /// <param name="originalEndpoint">原始输入（仅用于异常消息）/ The original input (used in exception messages only)</param>
    /// <param name="scheme">已校验的小写 scheme / The validated lower-cased scheme</param>
    /// <param name="authority">非方括号开头的 authority 段 / The authority part not starting with <c>[</c></param>
    /// <returns>解析后的三元组 / The parsed endpoint triple</returns>
    /// <exception cref="EndpointFormatException">当结构违规时抛出 / Thrown on structural violations</exception>
    private static ParsedEndpoint ParseHostPortAuthority(string originalEndpoint, string scheme, string authority)
    {
        var lastColonIndex = authority.LastIndexOf(':');
        if (lastColonIndex < 0 || lastColonIndex == authority.Length - 1)
        {
            // Localization: Discovery.Endpoint.MissingPort - 端点 '{0}' 缺少端口：期望 'scheme://host:port'
            throw new EndpointFormatException(LocalizationService.GetString(Localization.Keys.Discovery.Endpoint.MissingPort, originalEndpoint));
        }

        var host = authority.Substring(0, lastColonIndex);
        if (host.Length == 0)
        {
            // Localization: Discovery.Endpoint.EmptyHostBeforePort - 端点 '{0}' 在端口之前 host 为空
            throw new EndpointFormatException(LocalizationService.GetString(Localization.Keys.Discovery.Endpoint.EmptyHostBeforePort, originalEndpoint));
        }

        if (IPAddress.TryParse(host, out var address))
        {
            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                // Localization: Discovery.Endpoint.UnbracketedIpv6 - 端点 '{0}' 使用了未加方括号的 IPv6 字面量 '{1}'；IPv6 host 必须写成 '[IPv6 字面量]:端口'
                throw new EndpointFormatException(LocalizationService.GetString(Localization.Keys.Discovery.Endpoint.UnbracketedIpv6, originalEndpoint, host));
            }

            return new ParsedEndpoint(scheme, host, ParsePort(originalEndpoint, authority.Substring(lastColonIndex + 1)), EndpointAddressKind.IPv4);
        }

        ValidateDnsHost(originalEndpoint, host);
        return new ParsedEndpoint(scheme, host, ParsePort(originalEndpoint, authority.Substring(lastColonIndex + 1)), EndpointAddressKind.DnsName);
    }

    /// <summary>
    /// DNS/容器名/Service 名中不合法的字符（URI 分隔符与端口分隔符）。
    /// </summary>
    /// <remarks>
    /// Characters never legal inside a DNS, container, or Service name
    /// (URI delimiters and the port separator).
    /// </remarks>
    private const string InvalidHostCharacters = "/?#@:";

    /// <summary>
    /// 校验非 IP host 是合法的 DNS/容器名/Service 名（空白与 URI 分隔符立即拒绝）。
    /// </summary>
    /// <remarks>
    /// Validates a non-IP host as a DNS, container, or Service name: whitespace
    /// and the URI delimiter characters (<c>/ ? # @ :</c>) are rejected at parse
    /// time, so malformed inputs such as <c>tcp://user@host:7777</c> or
    /// <c>tcp://host/path:7777</c> fail fast here instead of surfacing as an
    /// unexplainable connect failure later.
    /// </remarks>
    /// <param name="originalEndpoint">原始输入（仅用于异常消息）/ The original input (used in exception messages only)</param>
    /// <param name="host">待校验的 host / The host to validate</param>
    /// <exception cref="EndpointFormatException">当 host 含空白或 URI 分隔符时抛出 / Thrown when the host contains whitespace or a URI delimiter</exception>
    private static void ValidateDnsHost(string originalEndpoint, string host)
    {
        foreach (var character in host)
        {
            if (char.IsWhiteSpace(character) || InvalidHostCharacters.IndexOf(character) >= 0)
            {
                // Localization: Discovery.Endpoint.InvalidHostCharacters - 端点 '{0}' 的 host '{1}' 非法：DNS、容器名或 Service 名中不允许空白以及 URI 分隔符 '/'、'?'、'#'、'@'、':'
                throw new EndpointFormatException(LocalizationService.GetString(Localization.Keys.Discovery.Endpoint.InvalidHostCharacters, originalEndpoint, host));
            }
        }
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
            // Localization: Discovery.Endpoint.InvalidPort - 端点 '{0}' 的端口 '{1}' 非法：期望 1-65535 之间的整数
            throw new EndpointFormatException(LocalizationService.GetString(Localization.Keys.Discovery.Endpoint.InvalidPort, originalEndpoint, portText));
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
