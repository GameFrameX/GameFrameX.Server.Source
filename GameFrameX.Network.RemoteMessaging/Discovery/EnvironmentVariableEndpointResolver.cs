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


using System.Collections;

namespace GameFrameX.Network.RemoteMessaging.Discovery;

/// <summary>
/// 基于环境变量的服务端点解析器。扫描 <c>services__{ServiceName}__{scheme}__{index}</c> 格式的键，
/// 第三段 scheme 支持 tcp/kcp/ws/wss（大小写不敏感），键值必须为合法的 <c>scheme://host:port</c> 端点 URI。
/// </summary>
/// <remarks>
/// Environment variable-based service endpoint resolver. Scans keys of the form
/// <c>services__{ServiceName}__{scheme}__{index}</c> where the third segment scheme is one of
/// tcp/kcp/ws/wss (case-insensitive) and the value must be a valid <c>scheme://host:port</c>
/// endpoint URI. Values are validated at resolution (dial) time through the authoritative
/// <c>EndpointParser</c> — a structurally invalid value throws <c>EndpointFormatException</c>
/// instead of silently falling back to another scheme (fail fast on misconfiguration).
/// </remarks>
internal sealed class EnvironmentVariableEndpointResolver : IServiceEndpointResolver
{
    /// <summary>
    /// 多 scheme 键并存时的确定性偏好序（tcp 为通用回落，固定排最后）。
    /// </summary>
    /// <remarks>
    /// The deterministic preference order when several scheme keys coexist for one service
    /// (tcp is the universal fallback and is always ordered last).
    /// </remarks>
    private static readonly string[] PreferredSchemeOrder = { "kcp", "ws", "wss", "tcp" };

    /// <summary>
    /// 解析指定服务的端点 URI：单次扫描环境变量，按偏好序 kcp &gt; ws &gt; wss &gt; tcp 取最优 scheme 键。
    /// </summary>
    /// <remarks>
    /// Resolves the endpoint URI for the specified service: a single pass over the environment
    /// keeps the best key by the preference order kcp &gt; ws &gt; wss &gt; tcp (ties keep the
    /// first match in enumeration order). A deployment that only configures
    /// <c>services__{ServiceName}__tcp__0</c> resolves exactly that value, byte for byte identical
    /// to the previous tcp-only behavior. The selected value is validated with the authoritative
    /// <c>EndpointParser</c> before being returned.
    /// </remarks>
    /// <param name="serviceName">目标服务名 / The target service name</param>
    /// <returns>未解析端点 URI 字符串；未找到时返回空字符串 / The unparsed endpoint URI string; returns an empty string when not found</returns>
    /// <exception cref="EndpointFormatException">选中键值结构非法时抛出 / Thrown when the selected key value is structurally invalid</exception>
    public string ResolveEndpoint(string serviceName)
    {
        var variables = Environment.GetEnvironmentVariables();
        string bestEndpoint = null;
        var bestSchemeRank = int.MaxValue;
        foreach (DictionaryEntry item in variables)
        {
            if (!TryMatchServiceKey(item, serviceName, out var rawValue, out var schemeRank)
                || schemeRank >= bestSchemeRank)
            {
                continue;
            }

            bestEndpoint = rawValue;
            bestSchemeRank = schemeRank;
        }

        if (bestEndpoint == null)
        {
            return string.Empty;
        }

        EndpointParser.Parse(bestEndpoint);
        return bestEndpoint;
    }

    /// <summary>
    /// 解析指定服务在指定 scheme（tcp/kcp/ws/wss）下的端点 URI。
    /// </summary>
    /// <remarks>
    /// Resolves the endpoint URI of the specified service for the given scheme
    /// (tcp/kcp/ws/wss, compared case-insensitively); returns an empty string when the
    /// service declares no endpoint of that scheme. The selected value is validated with the
    /// authoritative <c>EndpointParser</c> before being returned.
    /// </remarks>
    /// <param name="serviceName">目标服务名 / The target service name</param>
    /// <param name="scheme">端点 scheme / The endpoint scheme</param>
    /// <returns>未解析端点 URI 字符串；该 scheme 未声明时返回空字符串 / The unparsed endpoint URI string; returns an empty string when no endpoint of that scheme is declared</returns>
    /// <exception cref="EndpointFormatException">选中键值结构非法时抛出 / Thrown when the selected key value is structurally invalid</exception>
    public string ResolveEndpoint(string serviceName, string scheme)
    {
        ArgumentException.ThrowIfNullOrEmpty(scheme);

        var normalizedScheme = scheme.Trim().ToLowerInvariant();
        if (!TransportSchemes.IsSupported(normalizedScheme))
        {
            return string.Empty;
        }

        var targetSchemeRank = GetSchemePreferenceRank(normalizedScheme);
        var variables = Environment.GetEnvironmentVariables();
        foreach (DictionaryEntry item in variables)
        {
            if (!TryMatchServiceKey(item, serviceName, out var rawValue, out var schemeRank)
                || schemeRank != targetSchemeRank)
            {
                continue;
            }

            EndpointParser.Parse(rawValue);
            return rawValue;
        }

        return string.Empty;
    }

    /// <summary>
    /// 判断环境变量项是否为目标服务的受支持 scheme 键，并给出其偏好序排名。
    /// </summary>
    /// <remarks>
    /// Checks whether an environment entry is a supported-scheme key of the target service and
    /// yields its preference rank.
    /// </remarks>
    /// <param name="item">环境变量项 / The environment entry</param>
    /// <param name="serviceName">目标服务名 / The target service name</param>
    /// <param name="rawValue">原始键值（已 Trim）/ The raw trimmed value</param>
    /// <param name="schemeRank">键 scheme 的偏好序排名 / The preference rank of the key's scheme</param>
    /// <returns>命中返回 true；否则 false / true on a match; otherwise false</returns>
    private static bool TryMatchServiceKey(DictionaryEntry item, string serviceName, out string rawValue, out int schemeRank)
    {
        rawValue = null;
        schemeRank = -1;
        if (item.Key is not string rawKey || item.Value is not string value || string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var key = rawKey.Replace("__", ":", StringComparison.Ordinal).Trim();
        if (!key.StartsWith("services:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var segments = key.Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 4
            || !string.Equals(segments[1], serviceName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        schemeRank = GetSchemePreferenceRank(segments[2]);
        if (schemeRank < 0)
        {
            return false;
        }

        rawValue = value.Trim();
        return true;
    }

    /// <summary>
    /// 计算 scheme 在偏好序中的排名（0 最优；不受支持返回 -1）。
    /// </summary>
    /// <remarks>
    /// Computes the scheme's rank in the preference order (0 is best; -1 when unsupported).
    /// </remarks>
    /// <param name="scheme">键第三段 scheme / The key's third-segment scheme</param>
    /// <returns>偏好序排名；不受支持为 -1 / The preference rank; -1 when unsupported</returns>
    private static int GetSchemePreferenceRank(string scheme)
    {
        for (var index = 0; index < PreferredSchemeOrder.Length; index++)
        {
            if (string.Equals(PreferredSchemeOrder[index], scheme, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }
}