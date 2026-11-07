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
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository:  https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:   https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:     https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using System.Reflection;
using System.Text;
using GameFrameX.Foundation.Extensions;
using GameFrameX.Foundation.Options;
using GameFrameX.StartUp.Options;
using GameFrameX.Utility.Setting;

namespace GameFrameX.StartUp.Configuration;

/// <summary>单宿主配置、命令行一致性与监听端口校验。</summary>
public static class ConfigStartupValidator
{
    /// <summary>在单一配置上验证实际启用的监听端点。</summary>
    public static void Validate(AppSetting setting)
    {
        ArgumentNullException.ThrowIfNull(setting);
        if (string.IsNullOrWhiteSpace(setting.ServerType) || setting.ServerType.Contains(','))
        {
            throw new ArgumentException("ServerType must identify one local application.", nameof(setting));
        }

        var endpoints = new List<(string Field, string Transport, int Port)>();
        void AddEndpoint(string field, string transport, int port, bool enabled)
        {
            if (!enabled || port == 0)
            {
                return;
            }

            if (port < 0 || port > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(field, port, "Listener port must be between 1 and 65535.");
            }

            endpoints.Add((field, transport, port));
        }

        // OuterPort 是公布地址，不建立第二个套接字；可选 UDP 替换 TCP 传输。
        AddEndpoint(nameof(setting.InnerPort), setting.IsEnableUdp ? "UDP" : "TCP", setting.InnerPort, setting.IsEnableTcp);
        AddEndpoint(nameof(setting.KcpPort), "UDP", setting.KcpPort, setting.IsEnableKcp);
        AddEndpoint(nameof(setting.HttpPort), "TCP", setting.HttpPort, setting.IsEnableHttp);
        AddEndpoint(nameof(setting.HttpsPort), "TCP", setting.HttpsPort, setting.IsEnableHttp);
        AddEndpoint(nameof(setting.WsPort), "TCP", setting.WsPort, setting.IsEnableWebSocket);
        AddEndpoint(nameof(setting.WssPort), "TCP", setting.WssPort, setting.IsEnableWebSocket);
        AddEndpoint(nameof(setting.MetricsPort), "TCP", setting.MetricsPort, setting.IsEnableHttp && setting.IsOpenTelemetry && setting.IsOpenTelemetryMetrics);
        AddEndpoint(nameof(setting.OnlineAdminPort), "TCP", setting.OnlineAdminPort, setting.Online?.EnableAdminApi == true);

        var conflicts = new List<ConfigFieldConflict>();
        foreach (var group in endpoints.GroupBy(endpoint => (endpoint.Transport, endpoint.Port)).Where(group => group.Count() > 1))
        {
            var baseline = group.First();
            foreach (var endpoint in group.Skip(1))
            {
                conflicts.Add(new ConfigFieldConflict(ConfigConflictKind.PortConflict,
                    $"{baseline.Field}/{endpoint.Field}", baseline.Field, baseline.Port, endpoint.Field, endpoint.Port));
            }
        }

        if (conflicts.Count > 0)
        {
            throw new ConfigConflictException(conflicts);
        }
    }

    /// <summary>校验唯一应用配置，保留配置文件优先及显式命令行冲突拒启。</summary>
    public static void Validate(IReadOnlyList<string> selectedServerTypes, IEnumerable<AppSetting> fileSettings, StartupOptions launcherOptions, string[] args)
    {
        ArgumentNullException.ThrowIfNull(selectedServerTypes);
        if (selectedServerTypes.Count != 1)
        {
            throw new ArgumentException("Exactly one application startup is required.", nameof(selectedServerTypes));
        }

        var settings = fileSettings?.ToList() ?? new List<AppSetting>();
        if (settings.Count > 1)
        {
            throw new ArgumentException("Exactly one application configuration is supported.", nameof(fileSettings));
        }

        var serverType = selectedServerTypes[0];
        var section = settings.SingleOrDefault();
        if (section != null && section.ServerType != serverType)
        {
            throw new ArgumentException("Configuration ServerType does not match the selected application.", nameof(fileSettings));
        }

        var setting = GameApp.ResolveAppSetting(serverType, settings, launcherOptions, false);
        Validate(setting);
        if (section == null || launcherOptions == null)
        {
            return;
        }

        var explicitNames = CollectExplicitCliOptionNames(args);
        var conflicts = new List<ConfigFieldConflict>();
        foreach (var property in typeof(AppSetting).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanRead || !property.CanWrite || !explicitNames.Contains(property.Name))
            {
                continue;
            }

            var commandLineValue = property.GetValue(launcherOptions);
            var fileValue = property.GetValue(section);
            if (!Equals(commandLineValue, fileValue))
            {
                conflicts.Add(new ConfigFieldConflict(ConfigConflictKind.CliFileValueConflict,
                    property.Name, "CLI", commandLineValue, "file", fileValue));
            }
        }

        if (conflicts.Count > 0)
        {
            throw new ConfigConflictException(conflicts);
        }
    }

    /// <summary>
    /// 从原始命令行参数提取实际出现的选项键集合（大小写不敏感）。
    /// </summary>
    /// <remarks>
    /// Extracts the set of option keys that actually appeared in the raw arguments
    /// (ordinal-ignore-case): the tokens are standardized with the same
    /// <see cref="CommandLineArgumentConverter"/> used by <see cref="OptionsBuilder"/>, so key extraction
    /// matches the parser's interpretation of every supported form (--key=value, --key value, boolean flags).
    /// Foundation is frozen (no supplied-keys API on OptionsBuilder), so this presence scan is the
    /// only way to detect explicitly-supplied-equals-default values; it checks key presence only and
    /// never re-parses values.
    /// </remarks>
    /// <param name="args">原始命令行参数 / The raw command-line arguments</param>
    /// <returns>实际出现的选项键集合 / The set of explicitly supplied option keys</returns>
    private static HashSet<string> CollectExplicitCliOptionNames(string[] args)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (args == null || args.Length == 0)
        {
            return names;
        }

        var standardArgs = new CommandLineArgumentConverter().ConvertToStandardFormat(args);
        foreach (var token in standardArgs)
        {
            if (string.IsNullOrEmpty(token))
            {
                continue;
            }

            var key = token;
            var separatorIndex = token.IndexOf('=');
            if (separatorIndex >= 0)
            {
                key = token.Substring(0, separatorIndex);
            }

            // 分离形态的值 token（紧跟键之后的普通参数）不含前缀，跳过
            if (!key.StartsWith("-", StringComparison.Ordinal))
            {
                continue;
            }

            var name = NormalizeOptionKeyName(key);
            if (name.Length > 0)
            {
                names.Add(name);
            }
        }

        return names;
    }

    /// <summary>
    /// 选项键 → 属性名：去前缀后按 '-'/'_' 分段 PascalCase 拼接，与 OptionsBuilder 的键归一化同构。
    /// </summary>
    /// <remarks>
    /// Maps an option key to its property name: after stripping the leading dashes, keys containing
    /// '-'/'_' are joined as PascalCase — the same normalization the OptionsBuilder applies when
    /// resolving keys, so alias spellings (e.g. --inner-port) resolve to the same property name.
    /// </remarks>
    /// <param name="key">标准化后的选项键 / The standardized option key</param>
    /// <returns>归一化属性名；无效键返回空串 / The normalized property name, or an empty string for an invalid key</returns>
    private static string NormalizeOptionKeyName(string key)
    {
        var trimmed = key.TrimStart('-');
        if (trimmed.Length == 0 || (trimmed.IndexOf('-') < 0 && trimmed.IndexOf('_') < 0))
        {
            return trimmed;
        }

        var segments = trimmed.Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(segments[0]);
        for (var index = 1; index < segments.Length; index++)
        {
            builder.Append(char.ToUpperInvariant(segments[index][0]));
            if (segments[index].Length > 1)
            {
                builder.Append(segments[index], 1, segments[index].Length - 1);
            }
        }

        return builder.ToString();
    }
}
