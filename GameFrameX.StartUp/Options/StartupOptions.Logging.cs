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

using GameFrameX.Foundation.Options.Attributes;
using Serilog;
using Serilog.Events;

namespace GameFrameX.StartUp.Options;

/// <summary>
/// GameFrameX 服务器启动日志配置选项
/// </summary>
/// <remarks>
/// Logging startup options for configuring log sinks, levels, rolling, and retention.
/// </remarks>
public partial class StartupOptions
{
    /// <summary>
    /// 是否输出到控制台
    /// </summary>
    /// <value>如果启用控制台日志则为 <c>true</c>；否则为 <c>false</c>。默认值为 <c>true</c> / <c>true</c> if console logging is enabled; otherwise, <c>false</c>. Default is <c>true</c></value>
    /// <remarks>
    /// Whether to output to console. Default is <c>true</c>. Controls whether logs are displayed in the console for development and debugging convenience.
    /// </remarks>
    [Option(nameof(LogIsConsole), DefaultValue = true, Description = "是否输出到控制台,默认为 true。")]
    public bool LogIsConsole { get; set; } = true;

    /// <summary>
    /// 是否将日志输出到文件
    /// </summary>
    /// <value>如果启用日志文件写入则为 <c>true</c>；否则为 <c>false</c>。默认值为 <c>true</c> / <c>true</c> if log file writing is enabled; otherwise, <c>false</c>. Default is <c>true</c></value>
    /// <remarks>
    /// Whether to output logs to files. Default is <c>true</c>. When enabled, log information will be written to the local file system for long-term storage and subsequent analysis.
    /// This configuration is typically used together with log rolling and file size limit settings to control log file generation and management.
    /// It is recommended to keep this enabled in production environments to ensure critical log information is persistently recorded.
    /// </remarks>
    [Option(nameof(LogIsWriteToFile), DefaultValue = true, Description = "是否将日志输出到文件,默认为 true。")]
    public bool LogIsWriteToFile { get; set; } = true;

    /// <summary>
    /// 是否输出到 GrafanaLoki
    /// </summary>
    /// <value>如果启用 GrafanaLoki 日志则为 <c>true</c>；否则为 <c>false</c>。默认值为 <c>false</c> / <c>true</c> if GrafanaLoki logging is enabled; otherwise, <c>false</c>. Default is <c>false</c></value>
    /// <remarks>
    /// Whether to output to GrafanaLoki. Default is <c>false</c>.
    /// </remarks>
    [Option(nameof(LogIsGrafanaLoki), DefaultValue = false, Description = "是否输出到 GrafanaLoki,默认为 false。")]
    public bool LogIsGrafanaLoki { get; set; }

    /// <summary>
    /// GrafanaLoki 服务地址
    /// </summary>
    /// <value>GrafanaLoki 服务的 URL。默认值为 "http://localhost:3100" / The URL of the GrafanaLoki service. Default is "http://localhost:3100"</value>
    /// <remarks>
    /// GrafanaLoki service address. Default is http://localhost:3100. Effective when LogIsGrafanaLoki is <c>true</c>.
    /// </remarks>
    [Option(nameof(LogGrafanaLokiUrl), DefaultValue = "http://localhost:3100", Description = "GrafanaLoki 服务地址,默认为 http://localhost:3100。当LogIsGrafanaLoki为true时生效。")]
    public string LogGrafanaLokiUrl { get; set; } = "http://localhost:3100";

    /// <summary>
    /// GrafanaLoki 用户名或 Email
    /// </summary>
    /// <value>GrafanaLoki 认证的用户名或邮箱 / The username or email for GrafanaLoki authentication</value>
    /// <remarks>
    /// GrafanaLoki username or email. Effective when LogIsGrafanaLoki is <c>true</c>.
    /// </remarks>
    [Option(nameof(LogGrafanaLokiUserName), Description = "GrafanaLoki 用户名或Email,当LogIsGrafanaLoki为true时生效。")]
    public string LogGrafanaLokiUserName { get; set; }

    /// <summary>
    /// GrafanaLoki 密码
    /// </summary>
    /// <value>GrafanaLoki 认证的密码 / The password for GrafanaLoki authentication</value>
    /// <remarks>
    /// GrafanaLoki password. Effective when LogIsGrafanaLoki is <c>true</c>.
    /// </remarks>
    [Option(nameof(LogGrafanaLokiPassword), Description = "GrafanaLoki 密码,当LogIsGrafanaLoki为true时生效。")]
    public string LogGrafanaLokiPassword { get; set; }

    /// <summary>
    /// 日志滚动间隔
    /// </summary>
    /// <value>日志文件滚动间隔。默认值为 RollingInterval.Day / The interval for log file rolling. Default is RollingInterval.Day</value>
    /// <remarks>
    /// Log rolling interval. Default is Day. Determines the time interval for creating new log files, which can be hour, day, month, etc.
    /// </remarks>
    [Option(nameof(LogRollingInterval), DefaultValue = RollingInterval.Day, Description = "日志滚动间隔,默认为每天(Day),日志滚动间隔(可选值：Minute[分], Hour[时], Day[天], Month[月], Year[年], Infinite[无限])")]
    public RollingInterval LogRollingInterval { get; set; } = RollingInterval.Day;

    /// <summary>
    /// 日志输出级别
    /// </summary>
    /// <value>要记录的最低日志事件级别。默认值为 LogEventLevel.Debug / The minimum log event level to be recorded. Default is LogEventLevel.Debug</value>
    /// <remarks>
    /// Log output level. Default is Debug. Controls the minimum level of log output. Logs below this level will not be recorded.
    /// </remarks>
    [Option(nameof(LogEventLevel), DefaultValue = LogEventLevel.Debug, Description = "日志输出级别,默认为 Debug,日志级别(可选值：Verbose[详细], Debug[调试], Information[信息], Warning[警告], Error[错误], Fatal[致命])")]
    public LogEventLevel LogEventLevel { get; set; } = LogEventLevel.Debug;

    /// <summary>
    /// 是否限制单个日志文件大小
    /// </summary>
    /// <value>如果启用日志文件大小限制则为 <c>true</c>；否则为 <c>false</c>。默认值为 <c>true</c> / <c>true</c> if log file size limit is enabled; otherwise, <c>false</c>. Default is <c>true</c></value>
    /// <remarks>
    /// Whether to limit individual file size. Default is <c>true</c>. Enabling this option can prevent individual log files from becoming too large.
    /// </remarks>
    [Option(nameof(LogIsFileSizeLimit), DefaultValue = true, Description = "是否限制单个文件大小,默认为 true。")]
    public bool LogIsFileSizeLimit { get; set; } = true;

    /// <summary>
    /// 日志单个文件大小限制（字节）
    /// </summary>
    /// <value>单个日志文件的最大大小（字节）。默认值为 104857600（100MB） / The maximum size of a single log file in bytes. Default is 104857600 (100MB)</value>
    /// <remarks>
    /// Log file size limit. Default is 100MB. Effective when IsFileSizeLimit is <c>true</c>. When a log file reaches this size limit, a new log file will be created to continue writing.
    /// </remarks>
    [Option(nameof(LogFileSizeLimitBytes), DefaultValue = 104857600, Description = "日志单个文件大小限制,默认为 100MB。当 LogIsFileSizeLimit 为 true 时有效。")]
    public int LogFileSizeLimitBytes { get; set; } = 104857600;

    /// <summary>
    /// 日志文件保留数量限制
    /// </summary>
    /// <value>要保留的日志文件最大数量。默认值为 31 / The maximum number of log files to retain. Default is 31</value>
    /// <remarks>
    /// Log file retention count limit. Default is 31 files, representing 31 days of log files. When set to null, there is no file count limit. Used to control the number of historical log files to prevent excessive disk space usage.
    /// </remarks>
    [Option(nameof(LogRetainedFileCountLimit), DefaultValue = 31, Description = "日志文件保留数量限制 默认为 31 个文件,即 31 天的日志文件")]
    public int LogRetainedFileCountLimit { get; set; } = 31;
}
