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


using System.Reflection;
using GameFrameX.Foundation.Extensions;
using GameFrameX.Foundation.Logger;
using GameFrameX.Foundation.Logger.GrafanaLoki;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Foundation.Localization.Providers;
using GameFrameX.Foundation.Options;
using GameFrameX.Foundation.Options.Attributes;
using GameFrameX.Foundation.Utility;
using GameFrameX.Localization;
using GameFrameX.StartUp.Abstractions;
using GameFrameX.StartUp.Configuration;
using GameFrameX.StartUp.Options;
using GameFrameX.Utility;
using GameFrameX.Utility.Setting;

namespace GameFrameX.StartUp;

/// <summary>
/// 游戏应用程序入口类。
/// </summary>
/// <remarks>
/// Game application entry point class.
/// This class provides the startup entry point for game servers, responsible for initializing various server components,
/// including logging system, configuration management, startup type discovery, and server startup flow.
/// Supports startup and management of multiple server types.
/// </remarks>
/// <example>
/// <code>
/// // 启动游戏服务器
/// await GameApp.Entry(args, () => {
///     // 初始化协议注册等
/// }, logOptions => {
///     // 配置日志选项
///     logOptions.IsConsole = true;
/// });
/// </code>
/// </example>
public static class GameApp
{
    /// <summary>
    /// 当前服务器实例的启动任务。
    /// </summary>
    /// <remarks>
    /// The launch task for the current server instance.
    /// Used to track the execution status of the server startup task.
    /// </remarks>
    private static Task _launchTask;
    private static int _entryActive;

    /// <summary>
    /// 命令行参数解析失败时的进程退出码。
    /// </summary>
    /// <remarks>
    /// The process exit code used when launcher argument parsing fails.
    /// A non-zero code allows systemd / Docker / Kubernetes restart policies to detect the failure and raise alerts.
    /// </remarks>
    internal const int InvalidLauncherArgumentsExitCode = 1;

    /// <summary>
    /// 进程中止动作（<see cref="Environment.Exit(int)"/> 的可替换退出缝）。
    /// internal static 属性形态（S2223 收口）：保持属性，勿回退为可见可变静态字段。
    /// </summary>
    /// <remarks>
    /// The process abort action (a replaceable seam over <see cref="Environment.Exit(int)"/>).
    /// The production default terminates the process; tests substitute this delegate to capture the exit code
    /// instead of killing the test host. Kept as an internal static property for S2223 (do not regress to a
    /// visible mutable static field); registered in the startup spec static-state red line (C420).
    /// </remarks>
    internal static Action<int> AbortProcess { get; set; } = static exitCode => Environment.Exit(exitCode);

    /// <summary>
    /// 解析启动器选项。
    /// </summary>
    /// <remarks>
    /// Parse launcher options.
    /// 解析失败时不再静默回落全缺省实例（C211 fail-soft 语义已随 C363 退役）：错误信息写标准错误流并以非零退出码终止进程，
    /// 供守护进程重启策略捕获告警 / Parse failures abort the process with a non-zero exit code after writing the
    /// error to standard error (no silent fallback to an all-defaults instance).
    /// </remarks>
    /// <param name="args">命令行参数 / Command line arguments</param>
    /// <returns>解析后的启动器选项 / The parsed launcher options</returns>
    internal static StartupOptions ParseLauncherOptions(string[] args)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(args);
            var options = OptionsBuilder.CreateWithDebug<StartupOptions>(args);
            if (options.ServerType?.Contains(',') == true || args.Any(argument =>
                string.Equals(argument.Split('=')[0].Replace("-", "").Replace("_", ""), "AllInOne", StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException("Only one application startup is supported.", nameof(args));
            }

            return options;
        }
        catch (Exception e)
        {
            AbortStartupForInvalidArguments(e);

            // 中止动作生产实现为 Environment.Exit，本行不可达；仅保持方法返回流完整
            throw;
        }
    }

    /// <summary>
    /// 命令行参数解析失败的中止处理：错误写标准错误流与日志，再以非零退出码终止进程。
    /// </summary>
    /// <remarks>
    /// Abort handling for launcher argument parsing failures: writes the localized error (carrying the underlying
    /// parse exception message) to standard error and the log system, then terminates the process with
    /// <see cref="InvalidLauncherArgumentsExitCode"/>. Standard error is the operations channel: at this point the
    /// logging system is not configured yet, while container logs / journald capture stderr immediately; the non-zero
    /// exit code lets systemd / Docker / Kubernetes restart policies detect the misconfiguration instead of the
    /// server silently running under default configuration.
    /// </remarks>
    /// <param name="exception">解析异常 / The parse exception</param>
    internal static void AbortStartupForInvalidArguments(Exception exception)
    {
        // Localization: StartUp.InvalidLauncherArguments - 启动参数解析失败，拒绝以默认配置启动（退出码 {1}）。错误：{0}
        var message = LocalizationService.GetString(Keys.StartUp.InvalidLauncherArguments, exception.Message, InvalidLauncherArgumentsExitCode);
        Console.Error.WriteLine(message);
        LogHelper.Error(message);
        AbortProcess(InvalidLauncherArgumentsExitCode);
    }

    /// <summary>
    /// 收集 Grafana Loki 标签。
    /// </summary>
    /// <remarks>
    /// Collect Grafana Loki labels.
    /// 标签来源 = <see cref="StartupOptions"/> 属性上 <see cref="GrafanaLokiLabelTagAttribute"/> 标注的反射扫描结果；
    /// 收集到本地字典返回，由 <see cref="ConfigureLogOptions"/> 注册 Loki sink 时作为 Labels 传入
    /// （Foundation 5.0.0 起 Loki 参数已随 sink 组件化从 <see cref="LogOptions"/> 移除，不再写入全局日志选项）。
    /// </remarks>
    /// <param name="launcherOptions">启动器选项 / Launcher options</param>
    /// <returns>标签字典；启动器选项为 null 或标注属性无有效值时返回空字典 / The label dictionary; empty when launcher options is null or no attributed property has a value</returns>
    internal static Dictionary<string, string> CollectGrafanaLokiLabels(StartupOptions launcherOptions)
    {
        var labels = new Dictionary<string, string>();

        if (launcherOptions == null)
        {
            return labels;
        }

        var properties = typeof(StartupOptions).GetProperties();
        foreach (var property in properties)
        {
            var grafanaLokiLabelTagAttribute = property.GetCustomAttribute<GrafanaLokiLabelTagAttribute>();
            if (grafanaLokiLabelTagAttribute == null)
            {
                continue;
            }

            var value = property.GetValue(launcherOptions)?.ToString();
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            if (!labels.TryAdd(property.Name, value))
            {
                // Localization: StartUp.GrafanaLokiLabelExists - Grafana Loki标签已存在。标签：{0}。
                LogHelper.Warning(LocalizationService.GetString(Keys.StartUp.GrafanaLokiLabelExists, property.Name));
            }
        }

        return labels;
    }

    /// <summary>
    /// 配置日志选项。
    /// </summary>
    /// <remarks>
    /// Configure log options.
    /// Grafana Loki 接线（Foundation 5.0.0 sink 组件化后）：仅当 <c>LogIsGrafanaLoki=true</c> 时经
    /// <see cref="LogOptionsGrafanaLokiExtensions.UseGrafanaLoki(GameFrameX.Foundation.Logger.LogOptions, System.Action{GrafanaLokiSinkOptions})"/> 注册组件 sink——Url/用户名/密码取自启动器 CLI 选项
    /// （选项面与默认值不变），Labels 取自 <see cref="CollectGrafanaLokiLabels"/> 的反射收集结果；
    /// 为 false 时不注册任何 Loki sink，等价 4.1.1 <c>IsGrafanaLoki=false</c> 的默认关闭语义。
    /// </remarks>
    /// <param name="launcherOptions">启动器选项 / Launcher options</param>
    /// <param name="grafanaLokiLabels">反射收集的 Loki 标签字典 / The reflected Loki label dictionary</param>
    internal static void ConfigureLogOptions(StartupOptions launcherOptions, Dictionary<string, string> grafanaLokiLabels)
    {
        if (launcherOptions == null)
        {
            return;
        }

        // 只有显式启用时区设置时才调用 SetTimeZone
        if (launcherOptions.IsUseTimeZone)
        {
            TimerHelper.SetTimeZone(launcherOptions.TimeZone);
        }

        // 设置日志配置信息
        LogOptions.Default.IsConsole = launcherOptions.LogIsConsole;
        LogOptions.Default.IsWriteToFile = launcherOptions.LogIsWriteToFile;

        // Foundation 5.0.0 起 Loki sink 组件化拆分：仅显式开启时经 UseGrafanaLoki 注册组件 sink，
        // LogIsGrafanaLoki=false 时不注册任何 Loki sink（等价 4.1.1 IsGrafanaLoki=false 默认关闭）。
        if (launcherOptions.LogIsGrafanaLoki)
        {
            LogOptions.Default.UseGrafanaLoki(sinkOptions =>
            {
                sinkOptions.Url = launcherOptions.LogGrafanaLokiUrl;
                sinkOptions.UserName = launcherOptions.LogGrafanaLokiUserName;
                sinkOptions.Password = launcherOptions.LogGrafanaLokiPassword;
                sinkOptions.Labels = grafanaLokiLabels;
            });
        }
        LogOptions.Default.RetainedFileCountLimit = launcherOptions.LogRetainedFileCountLimit;
        LogOptions.Default.IsFileSizeLimit = launcherOptions.LogIsFileSizeLimit;
        LogOptions.Default.FileSizeLimitBytes = launcherOptions.LogFileSizeLimitBytes;
        LogOptions.Default.LogEventLevel = launcherOptions.LogEventLevel;
        LogOptions.Default.RollingInterval = launcherOptions.LogRollingInterval;

        // 构建LogType，当值为空或默认值时不拼接
        var logTypeParts = new List<string>();

        if (launcherOptions.ServerId > 0)
        {
            logTypeParts.Add(launcherOptions.ServerId.ToString());
        }

        if (launcherOptions.ServerInstanceId > 0)
        {
            logTypeParts.Add(launcherOptions.ServerInstanceId.ToString());
        }

        // 设置 LogTagName（按优先级：TagName > Note > Label > Description）
        if (launcherOptions.TagName.IsNotNullOrWhiteSpace())
        {
            LogOptions.Default.LogTagName = launcherOptions.TagName;
        }
        else if (launcherOptions.Note.IsNotNullOrWhiteSpace())
        {
            LogOptions.Default.LogTagName = launcherOptions.Note;
        }
        else if (launcherOptions.Label.IsNotNullOrWhiteSpace())
        {
            LogOptions.Default.LogTagName = launcherOptions.Label;
        }
        else if (launcherOptions.Description.IsNotNullOrWhiteSpace())
        {
            LogOptions.Default.LogTagName = launcherOptions.Description;
        }

        if (LogOptions.Default.LogTagName.IsNullOrEmpty() && logTypeParts.Count > 0)
        {
            LogOptions.Default.LogTagName = string.Join("_", logTypeParts);
        }
    }

    /// <summary>
    /// 启动游戏应用程序的主入口点。
    /// </summary>
    /// <remarks>
    /// Main entry point for starting the game application.
    /// This method is the startup entry point for the entire game server, performing the following main steps:
    /// 1. Parse startup arguments
    /// 2. Configure logging system
    /// 3. Load global settings
    /// 4. Discover and register startup types
    /// 5. Start corresponding services based on server type
    /// 6. Wait for all startup tasks to complete
    /// </remarks>
    /// <param name="args">命令行参数 / Command line arguments</param>
    /// <param name="initAction">在启动服务器之前执行的初始化操作，用于外部协议注册 / Initialization action executed before starting the server, used for external protocol registration</param>
    /// <param name="logConfiguration">日志系统初始化回调，可以重写参数 / Callback for log system initialization, allows overriding parameters</param>
    /// <returns>表示异步操作的任务 / A task representing the asynchronous operation</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="args"/> 为 null 时抛出 / Thrown when <paramref name="args"/> is null</exception>
    /// <exception cref="InvalidOperationException">当启动配置无效时抛出 / Thrown when startup configuration is invalid</exception>
    /// <example>
    /// <code>
    /// // 基本启动
    /// await GameApp.Entry(args, null);
    ///
    /// // 带初始化和日志配置的启动
    /// await GameApp.Entry(args,
    ///     () => ProtocolManager.RegisterAll(),
    ///     logOptions => {
    ///         logOptions.IsConsole = true;
    ///         logOptions.LogEventLevel = LogEventLevel.Debug;
    ///     });
    /// </code>
    /// </example>
    public static async Task EntryAsync(string[] args, Action initAction, Action<LogOptions> logConfiguration = null)
    {
        if (Interlocked.CompareExchange(ref _entryActive, 1, 0) != 0)
        {
            throw new InvalidOperationException("An application entry is already active.");
        }

        try
        {
            LocalizationService.Instance.RegisterProvider(new AssemblyResourceProvider(typeof(Keys).Assembly));

            // 1. 解析启动参数
            var launcherOptions = ParseLauncherOptions(args);

            // 2. 配置日志
            var grafanaLokiLabels = CollectGrafanaLokiLabels(launcherOptions);
            ConfigureLogOptions(launcherOptions, grafanaLokiLabels);
            logConfiguration?.Invoke(LogOptions.Default);

            // 3. 初始化
            GlobalSettings.Load("Configs/app_config.json");
            initAction?.Invoke();

            // 4. 解析唯一应用配置和启动类型。
            StartUpTypeRegistry.Instance.DiscoverAndRegister();
            var sortedStartUpTypes = StartUpTypeRegistry.Instance.GetSortedByPriority();
            TryLaunchServer(args, sortedStartUpTypes, launcherOptions);

            ConsoleHelper.ConsoleLogo();

            // 5. 等待启动任务完成
            if (_launchTask == null)
            {
                var message = LocalizationService.GetString(Keys.StartUp.NoStartupTaskFound);
                LogHelper.Warning(message);
                return;
            }

            await _launchTask;
        }
        finally
        {
            _launchTask = null;
            Volatile.Write(ref _entryActive, 0);
        }
    }

    /// <summary>初始化并运行唯一应用；初始化失败也执行有界清理。</summary>
    private static async Task LaunchAsync(string[] args, Type startUpType, string serverType, AppSetting setting)
    {
        var startUp = (IAppStartUp)Activator.CreateInstance(startUpType)
            ?? throw new InvalidOperationException("Cannot create the application startup.");
        SetLogTypeOnce(serverType);
        try
        {
            if (!startUp.Init(serverType, setting, args))
            {
                throw new InvalidOperationException("Application initialization failed.");
            }
        }
        catch
        {
            if (!await AppEnter.StopHostWithTimeoutAsync(startUp, "Application initialization failed", AppEnter.GracefulShutdownTotalTimeout))
            {
                LogHelper.Error("Application initialization cleanup timed out.");
            }
            throw;
        }

        LogHelper.ShowOption(LocalizationService.GetString(Keys.StartUp.StartingServerWithConfiguration, serverType), startUp.Setting.ToFormatString());
        await AppEnter.EntryAsync(startUp);
    }

    /// <summary>解析单一应用配置；缺省配置保留 Online 命令行投影。</summary>
    internal static AppSetting ResolveAppSetting(string serverType, IEnumerable<AppSetting> appSettings, StartupOptions launcherOptions, bool warnOnMissingConfiguration)
    {
        var settings = appSettings?.ToList() ?? new List<AppSetting>();
        if (settings.Count > 1)
        {
            throw new ArgumentException("Only one application configuration is supported.", nameof(appSettings));
        }

        if (settings.Count == 1)
        {
            var setting = settings[0];
            if (setting == null || setting.ServerType != serverType)
            {
                throw new ArgumentException("Configuration ServerType does not match the application.", nameof(appSettings));
            }

            return setting;
        }

        if (warnOnMissingConfiguration)
        {
            LogHelper.Warning(LocalizationService.GetString(Keys.StartUp.NoConfigurationUseDefault, serverType));
        }

        if (launcherOptions == null)
        {
            return null;
        }

        var applicationSetting = new AppSetting { ServerType = serverType };
        foreach (var property in typeof(AppSetting).GetProperties())
        {
            if (property.CanRead && property.CanWrite && property.Name != nameof(AppSetting.ServerType))
            {
                property.SetValue(applicationSetting, property.GetValue(launcherOptions));
            }
        }

        if (string.IsNullOrWhiteSpace(applicationSetting.ServerName))
        {
            applicationSetting.ServerName = serverType;
        }

        var capabilities = launcherOptions.GetOnlineCapabilities();
        var disabledCapabilities = launcherOptions.GetOnlineDisable();
        if (capabilities.Length > 0 || disabledCapabilities.Length > 0)
        {
            applicationSetting.Online = new OnlineCapabilityOptions
            {
                Capabilities = capabilities,
                Disable = disabledCapabilities,
            };
        }

        return applicationSetting;
    }

    /// <summary>设置本地应用固定日志标识。</summary>
    internal static void SetLogTypeOnce(string serverType)
    {
        if (LogOptions.Default.LogType.IsNullOrEmptyOrWhiteSpace())
        {
            LogOptions.Default.LogType = serverType;
        }
    }

    private static void TryLaunchServer(string[] args, IEnumerable<KeyValuePair<Type, StartUpTagAttribute>> registeredTypes, StartupOptions launcherOptions)
    {
        var settings = GlobalSettings.GetSettings();
        var selected = SelectStartUpTypes(launcherOptions, registeredTypes, settings.SingleOrDefault()?.ServerType);
        if (selected.Count == 0)
        {
            AbortStartupForUnresolvedServerTypes(launcherOptions);
            throw new InvalidOperationException("No application startup is registered.");
        }

        var application = selected[0];
        ConfigStartupValidator.Validate(new[] { application.Value.ServerType }, settings, launcherOptions, args);
        var setting = ResolveAppSetting(application.Value.ServerType, settings, launcherOptions, true);
        _launchTask = LaunchAsync(args, application.Key, application.Value.ServerType, setting);
    }

    internal const int UnresolvedServerTypeExitCode = 1;

    internal static bool ShouldAbortForUnresolvedServerTypes(StartupOptions launcherOptions)
    {
        ArgumentNullException.ThrowIfNull(launcherOptions);
        return !string.IsNullOrWhiteSpace(launcherOptions.ServerType);
    }

    internal static void AbortStartupForUnresolvedServerTypes(StartupOptions launcherOptions)
    {
        var message = LocalizationService.GetString(Keys.StartUp.Application.RequestedServerTypeNotRegistered, launcherOptions.ServerType);
        Console.Error.WriteLine(message);
        LogHelper.Error(message);
        AbortProcess(UnresolvedServerTypeExitCode);
    }

    /// <summary>选择唯一应用启动类；不按优先级隐式选择多个候选中的一个。</summary>
    internal static IReadOnlyList<KeyValuePair<Type, StartUpTagAttribute>> SelectStartUpTypes(StartupOptions launcherOptions, IEnumerable<KeyValuePair<Type, StartUpTagAttribute>> registeredTypes, string configuredServerType = null)
    {
        ArgumentNullException.ThrowIfNull(launcherOptions);
        var serverType = string.IsNullOrWhiteSpace(launcherOptions.ServerType) ? configuredServerType : launcherOptions.ServerType.Trim();
        if (serverType?.Contains(',') == true)
        {
            throw new ArgumentException("ServerType must identify one local application.", nameof(launcherOptions));
        }

        var selected = registeredTypes.Where(pair => string.IsNullOrWhiteSpace(serverType) || pair.Value.ServerType == serverType).ToList();
        if (selected.Count > 1)
        {
            throw new InvalidOperationException("Application startup is ambiguous; configure exactly one ServerType.");
        }

        return selected;
    }
}
