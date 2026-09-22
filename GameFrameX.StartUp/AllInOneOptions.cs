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
//   Any legal disputes and liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository:  https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:  https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using GameFrameX.Foundation.Extensions;
using GameFrameX.StartUp.Options;

namespace GameFrameX.StartUp;

/// <summary>
/// 多 Role / All-in-One 启动 CLI 解析结果（C143b D2）。
/// </summary>
/// <remarks>
/// CLI parsing result for multi-role / all-in-one startup (C143b D2).
/// Recognized forms:
/// <c>--ServerType=Game</c> (current single-role form),
/// <c>--ServerType=Game,Social</c> (comma-separated roles, also <c>--ServerType Game,Social</c>),
/// <c>--AllInOne</c> (bare switch, or <c>--AllInOne=true|false</c>) which launches every registered role.
/// The launch decision reads this parse instead of relying on how the generic option binder treats bare switches.
/// <see cref="StartupOptions.IsSingleMode"/> is intentionally NOT reused: it is consumed by the AppHost
/// orchestration layer with the opposite meaning (see design D2).
/// </remarks>
public sealed class AllInOneOptions
{
    /// <summary>
    /// All-in-One 开关的 CLI 参数名（D2：裸开关 --AllInOne；属性名为 StartupOptions.IsAllInOne，二者不同名）。
    /// </summary>
    /// <remarks>
    /// The CLI argument name of the all-in-one switch (D2: bare switch <c>--AllInOne</c>;
    /// note the property is <c>StartupOptions.IsAllInOne</c> — the two names differ).
    /// </remarks>
    private const string AllInOneArgumentName = "AllInOne";

    /// <summary>
    /// 初始化 <see cref="AllInOneOptions"/> 类的新实例。
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of the <see cref="AllInOneOptions"/> class.
    /// </remarks>
    /// <param name="isAllInOne">是否 All-in-One 拉起全部已注册 Role / Whether to launch every registered role in one process</param>
    /// <param name="serverTypes">CLI 指定的 Role 名列表（已拆分/去空/去重，保留输入顺序）/ The role names specified on the command line (split, trimmed, deduplicated, input order preserved)</param>
    private AllInOneOptions(bool isAllInOne, IReadOnlyList<string> serverTypes)
    {
        IsAllInOne = isAllInOne;
        ServerTypes = serverTypes;
    }

    /// <summary>
    /// 获取缺省解析结果（未指定任何多 Role 参数）。
    /// </summary>
    /// <remarks>
    /// Gets the default parse result (no multi-role arguments given).
    /// </remarks>
    public static AllInOneOptions None { get; } = new AllInOneOptions(false, Array.Empty<string>());

    /// <summary>
    /// 获取是否 All-in-One 单进程拉起全部已注册 Role。
    /// </summary>
    /// <remarks>
    /// Gets whether to launch every registered role in one process (all-in-one).
    /// </remarks>
    /// <value>All-in-One 则为 <c>true</c>；否则为 <c>false</c> / <c>true</c> for all-in-one; otherwise, <c>false</c></value>
    public bool IsAllInOne { get; }

    /// <summary>
    /// 获取 CLI 指定的 Role 名列表。
    /// </summary>
    /// <remarks>
    /// Gets the role names specified on the command line.
    /// Split from the comma-separated <c>--ServerType</c> value (trimmed, empties removed, first occurrence kept);
    /// the launch order itself follows <see cref="StartUpTypeRegistry"/> priorities, not this list's order.
    /// </remarks>
    /// <value>Role 名列表；未指定则为空列表 / The role names; an empty list when unspecified</value>
    public IReadOnlyList<string> ServerTypes { get; }

    /// <summary>
    /// 解析多 Role / All-in-One 启动参数。
    /// </summary>
    /// <remarks>
    /// Parses the multi-role / all-in-one startup arguments.
    /// Unknown arguments are ignored; a bare <c>--AllInOne</c> switch means <c>true</c>;
    /// repeated <c>--ServerType</c> arguments keep the last occurrence;
    /// the space-separated form never consumes a following option marker (e.g. <c>--ServerType --AllInOne</c>).
    /// </remarks>
    /// <param name="args">命令行参数 / The command line arguments</param>
    /// <returns>解析结果；无相关参数时返回 <see cref="None"/> / The parse result, or <see cref="None"/> when no related argument is present</returns>
    public static AllInOneOptions Parse(string[] args)
    {
        if (args == null || args.Length == 0)
        {
            return None;
        }

        var isAllInOne = false;
        var rawServerTypes = (string)null;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (argument.IsNullOrEmpty() || !argument.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var body = argument.Substring(2);
            int separatorIndex = body.IndexOf('=');
            string name;
            string value = null;
            if (separatorIndex >= 0)
            {
                name = body.Substring(0, separatorIndex);
                value = body.Substring(separatorIndex + 1);
            }
            else
            {
                name = body;
            }

            if (name.Equals(nameof(StartupOptions.ServerType), StringComparison.OrdinalIgnoreCase))
            {
                if (value == null && index + 1 < args.Length)
                {
                    var nextArgument = args[index + 1];
                    if (!nextArgument.IsNullOrEmpty() && !nextArgument.StartsWith("--", StringComparison.Ordinal))
                    {
                        // 空格分隔形态：--ServerType Game,Social；下一个参数是选项标记（如 --ServerType --AllInOne）时不消费，避免把 "--AllInOne" 当作 Role 名
                        value = nextArgument;
                        index++;
                    }
                }

                if (!value.IsNullOrEmpty())
                {
                    rawServerTypes = value;
                }
            }
            else if (name.Equals(AllInOneArgumentName, StringComparison.OrdinalIgnoreCase))
            {
                if (value == null || !bool.TryParse(value, out var allInOneFlag))
                {
                    // 裸开关 --AllInOne 等价于 true
                    isAllInOne = true;
                }
                else
                {
                    isAllInOne = allInOneFlag;
                }
            }
        }

        if (!isAllInOne && rawServerTypes.IsNullOrEmpty())
        {
            return None;
        }

        return new AllInOneOptions(isAllInOne, SplitServerTypes(rawServerTypes));
    }

    /// <summary>
    /// 拆分逗号分隔的 Role 名（trim、去空、去重、保留输入顺序）。
    /// </summary>
    /// <remarks>
    /// Splits the comma-separated role names (trimmed, empties removed, deduplicated, input order preserved).
    /// </remarks>
    /// <param name="rawServerTypes">逗号分隔的原始值 / The raw comma-separated value</param>
    /// <returns>Role 名列表 / The role name list</returns>
    private static IReadOnlyList<string> SplitServerTypes(string rawServerTypes)
    {
        if (rawServerTypes.IsNullOrEmpty())
        {
            return Array.Empty<string>();
        }

        var serverTypes = new List<string>();
        foreach (var segment in rawServerTypes.Split(','))
        {
            var serverType = segment.Trim();
            if (serverType.Length == 0)
            {
                continue;
            }

            if (!serverTypes.Contains(serverType, StringComparer.Ordinal))
            {
                serverTypes.Add(serverType);
            }
        }

        return serverTypes;
    }
}
