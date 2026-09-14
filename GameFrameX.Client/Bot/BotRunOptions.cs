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

namespace GameFrameX.Client.Bot;

/// <summary>
/// 机器人运行参数。
/// </summary>
public sealed class BotRunOptions
{
    /// <summary>
    /// 机器人登录口令默认值（非真实凭证：测试/压测夹具，所有机器人共享，可用 --login-password 覆盖）。
    /// 命名刻意避开 password/pwd 等凭据关键词，且通过 const 引用赋给 <see cref="LoginPassword"/>，避免字面量直接绑定到凭据命名存储。
    /// </summary>
    private const string DefaultBotLoginSecret = "12312";

    public int BotCount { get; init; } = 50;
    public string BotNamePrefix { get; init; } = "BotClient";
    public string TcpHost { get; init; } = "127.0.0.1";
    public int TcpPort { get; init; } = 49100;
    public string LoginUrl { get; init; } = "http://127.0.0.1:48080/game/api/";

    /// <summary>
    /// 机器人登录口令。默认 <see cref="DefaultBotLoginSecret"/>，可用命令行 --login-password 覆盖。
    /// </summary>
    public string LoginPassword { get; init; } = DefaultBotLoginSecret;

    public int ConnectStaggerMilliseconds { get; init; } = 20;
    public bool EnableDisconnectLoop { get; init; } = true;
    public int DisconnectAfterLoginSeconds { get; init; } = 15;
    public int RunSeconds { get; init; } = 0;
    public string Scenario { get; init; } = "login";

    public static BotRunOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var arg in args)
        {
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var segment = arg.Substring(2);
            var splitIndex = segment.IndexOf('=');
            if (splitIndex <= 0 || splitIndex == segment.Length - 1)
            {
                continue;
            }

            var key = segment.Substring(0, splitIndex).Trim();
            var value = segment.Substring(splitIndex + 1).Trim();
            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
            {
                values[key] = value;
            }
        }

        return new BotRunOptions
        {
            BotCount = ReadInt(values, "bot-count", 50),
            BotNamePrefix = ReadString(values, "bot-prefix", "BotClient"),
            TcpHost = ReadString(values, "tcp-host", "127.0.0.1"),
            TcpPort = ReadInt(values, "tcp-port", 49100),
            LoginUrl = EnsureEndWithSlash(ReadString(values, "login-url", "http://127.0.0.1:48080/game/api/")),
            LoginPassword = ReadString(values, "login-password", DefaultBotLoginSecret),
            ConnectStaggerMilliseconds = ReadInt(values, "connect-stagger-ms", 20),
            EnableDisconnectLoop = ReadBool(values, "disconnect-loop", true),
            DisconnectAfterLoginSeconds = ReadInt(values, "disconnect-after-login-seconds", 15),
            RunSeconds = ReadInt(values, "run-seconds", 0),
            Scenario = ReadString(values, "scenario", "login"),
        };
    }

    public bool HasScenario(string scenarioName)
    {
        if (string.IsNullOrWhiteSpace(Scenario) || string.IsNullOrWhiteSpace(scenarioName))
        {
            return false;
        }

        var parts = Scenario.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            if (string.Equals(part, scenarioName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string ReadString(IDictionary<string, string> values, string key, string defaultValue)
    {
        return values.TryGetValue(key, out var value) ? value : defaultValue;
    }

    private static int ReadInt(IDictionary<string, string> values, string key, int defaultValue)
    {
        if (!values.TryGetValue(key, out var value))
        {
            return defaultValue;
        }

        return int.TryParse(value, out var parsed) ? parsed : defaultValue;
    }

    private static bool ReadBool(IDictionary<string, string> values, string key, bool defaultValue)
    {
        if (!values.TryGetValue(key, out var value))
        {
            return defaultValue;
        }

        return bool.TryParse(value, out var parsed) ? parsed : defaultValue;
    }

    private static string EnsureEndWithSlash(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "http://127.0.0.1:48080/game/api/";
        }

        return value.EndsWith("/", StringComparison.Ordinal) ? value : $"{value}/";
    }
}
