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


using GameFrameX.Foundation.Extensions;
using GameFrameX.Foundation.Options.Attributes;
using GameFrameX.Utility.Setting;

namespace GameFrameX.StartUp.Options;

/// <summary>
/// GameFrameX 服务器启动配置选项
/// </summary>
/// <remarks>
/// Startup configuration options for GameFrameX server startup, containing various configuration options required for host and server startup.
/// </remarks>
public partial class StartupOptions : AppSetting
{
    /// <summary>
    /// 是否 All-in-One 单进程拉起全部已注册 Role。
    /// </summary>
    /// <value>All-in-One 则为 <c>true</c>；否则为 <c>false</c>。默认值为 <c>false</c> / <c>true</c> for all-in-one; otherwise, <c>false</c>. Default is <c>false</c></value>
    /// <remarks>
    /// Whether to launch every registered role in one process (all-in-one). This flag is
    /// process-level: every role of the process shares one kernel.
    /// The property name is the CLI switch name: the binder recognizes the bare <c>--AllInOne</c>
    /// switch (and <c>--AllInOne=true|false</c>) directly; command-line arguments are parsed
    /// exactly once by <c>OptionsBuilder</c> — there is no secondary raw-args parsing.
    /// When both <c>--AllInOne</c> and <c>--ServerType</c> are given, All-in-One overrides
    /// the selection and a warning is logged.
    /// </remarks>
    [Option(nameof(AllInOne), DefaultValue = false, Description = "是否单进程拉起全部已注册 Role(All-in-One),默认值为false")]
    [SettingFieldLevel(SettingFieldLevel.ProcessLevel)]
    public bool AllInOne { get; set; }

    /// <summary>
    /// 获取由 <see cref="AppSetting.ServerType"/> 逗号拆分出的 Role 名列表。
    /// </summary>
    /// <remarks>
    /// Derived view of the comma-separated <c>--ServerType</c> value (e.g. "Game,Social" → ["Game", "Social"]).
    /// The value itself is bound once by <c>OptionsBuilder</c>; this projection only splits the
    /// already-bound string (trim, drop empties, deduplicate, preserve input order) — it never
    /// re-scans the command line (no secondary parsing).
    /// Modeled as a method rather than a property because each call derives a new array (Sonar S2365:
    /// properties should not copy collections).
    /// </remarks>
    /// <returns>Role 名数组（trim、去空、去重）；未指定时为空数组 / The role names (trimmed, non-empty, deduplicated); an empty array when unspecified</returns>
    public string[] GetServerTypes()
    {
        if (ServerType.IsNullOrEmpty())
        {
            return Array.Empty<string>();
        }

        return ServerType.Split(',').Select(serverType => serverType.Trim())
            .Where(serverType => serverType.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }
}
