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
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Foundation.Localization.Providers;
using GameFrameX.Foundation.Options;
using GameFrameX.Foundation.Options.Attributes;
using GameFrameX.Foundation.Utility;
using GameFrameX.Localization;
using GameFrameX.NetWork.RemoteMessaging.Routing;
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

    /// <summary>
    /// 解析启动器选项。
    /// </summary>
    /// <remarks>
    /// Parse launcher options.
    /// </remarks>
    /// <param name="args">命令行参数 / Command line arguments</param>
    /// <returns>解析后的启动器选项 / Parsed launcher options</returns>
    private static StartupOptions ParseLauncherOptions(string[] args)
    {
        try
        {
            return OptionsBuilder.CreateWithDebug<StartupOptions>(args);
        }
        catch (Exception e)
        {
            LogHelper.Error(e.Message);
            return null;
        }
    }

    /// <summary>
    /// 配置 Grafana Loki 标签。
    /// </summary>
    /// <remarks>
    /// Configure Grafana Loki labels.
    /// </remarks>
    /// <param name="launcherOptions">启动器选项 / Launcher options</param>
    private static void ConfigureGrafanaLokiLabels(StartupOptions launcherOptions)
    {
        LogOptions.Default.GrafanaLokiLabels = new Dictionary<string, string>();

        if (launcherOptions == null)
        {
            return;
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

            if (!LogOptions.Default.GrafanaLokiLabels.TryAdd(property.Name, value))
            {
                LogHelper.Warning(LocalizationService.GetString(Keys.StartUp.GrafanaLokiLabelExists, property.Name));
            }
        }
    }

    /// <summary>
    /// 配置日志选项。
    /// </summary>
    /// <remarks>
    /// Configure log options.
    /// </remarks>
    /// <param name="launcherOptions">启动器选项 / Launcher options</param>
    private static void ConfigureLogOptions(StartupOptions launcherOptions)
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
        LogOptions.Default.IsGrafanaLoki = launcherOptions.LogIsGrafanaLoki;
        LogOptions.Default.GrafanaLokiUrl = launcherOptions.LogGrafanaLokiUrl;
        LogOptions.Default.GrafanaLokiUserName = launcherOptions.LogGrafanaLokiUserName;
        LogOptions.Default.GrafanaLokiPassword = launcherOptions.LogGrafanaLokiPassword;
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
    public static async Task Entry(string[] args, Action initAction, Action<LogOptions> logConfiguration = null)
    {
        LocalizationService.Instance.RegisterProvider(new AssemblyResourceProvider(typeof(Keys).Assembly));

        // 1. 解析启动参数
        var launcherOptions = ParseLauncherOptions(args);

        // 2. 输出服务器类型日志
        var serverType = launcherOptions?.ServerType;
        if (!serverType.IsNullOrEmpty())
        {
            // LogHelper.Info(LocalizationService.GetString(Keys.StartUp.LaunchServerType, serverType));
        }

        // 3. 配置日志
        ConfigureGrafanaLokiLabels(launcherOptions);
        ConfigureLogOptions(launcherOptions);
        logConfiguration?.Invoke(LogOptions.Default);

        // 4. 初始化
        GlobalSettings.Load("Configs/app_config.json");
        initAction?.Invoke();

        // 5. 发现并启动服务器（C143b D2：--ServerType 复数 + --AllInOne 开关）
        StartUpTypeRegistry.Instance.DiscoverAndRegister();
        var sortedStartUpTypes = StartUpTypeRegistry.Instance.GetSortedByPriority();
        var allInOneOptions = AllInOneOptions.Parse(args);
        TryLaunchServer(args, allInOneOptions, sortedStartUpTypes, launcherOptions);

        LogHelper.Info(LocalizationService.GetString(Keys.StartUp.StartupOver));
        ConsoleHelper.ConsoleLogo();

        // 6. 等待启动任务完成
        if (_launchTask == null)
        {
            var message = LocalizationService.GetString(Keys.StartUp.NoStartupTaskFound);
            LogHelper.Warning(message);
            return;
        }

        await _launchTask;
    }

    /// <summary>
    /// 为选定的 Role 集合启动启动任务（C143b D7：按优先级序拉起，逆序停机由 AppEnter 负责）。
    /// </summary>
    /// <remarks>
    /// Launches the startup task for the selected role collection.
    /// Builds one startup instance per selected role (priority order), resolves each role's configuration
    /// (config file section first, launcher options as the default fallback), reports priority conflicts,
    /// publishes the <see cref="RoleSet"/> snapshot, and hands the host collection to
    /// <see cref="AppEnter.Entry(IReadOnlyList{IAppStartUp})"/> which starts them in order and stops them in reverse order.
    /// </remarks>
    /// <param name="args">命令行参数 / Command line arguments</param>
    /// <param name="startUpTypes">按优先级排序选定的启动类型集合 / The selected startup types in priority order</param>
    /// <param name="appSettings">配置中的应用程序设置集合 / Collection of application settings from configuration</param>
    /// <param name="launcherOptions">用于默认配置的启动器选项 / Launcher options for default configuration</param>
    /// <param name="warnOnMissingConfiguration">缺省回退形态下对无配置段 Role 记 Warning（保留现状行为）/ Whether to warn about roles without a config section in the default fallback form (current behaviour preserved)</param>
    private static void Launcher(string[] args, IReadOnlyList<KeyValuePair<Type, StartUpTagAttribute>> startUpTypes, IEnumerable<AppSetting> appSettings, StartupOptions launcherOptions, bool warnOnMissingConfiguration)
    {
        if (startUpTypes.Count == 0)
        {
            return;
        }

        LogPriorityConflicts(startUpTypes);

        var appStartUps = new List<IAppStartUp>(startUpTypes.Count);
        var startedStartUpTypes = new List<KeyValuePair<Type, StartUpTagAttribute>>(startUpTypes.Count);
        foreach (var keyValuePair in startUpTypes)
        {
            var appSetting = ResolveAppSetting(keyValuePair.Value.ServerType, appSettings, launcherOptions, warnOnMissingConfiguration);
            var appStartUp = CreateStartUp(args, keyValuePair.Key, keyValuePair.Value.ServerType, appSetting);
            if (appStartUp == null)
            {
                continue;
            }

            appStartUps.Add(appStartUp);
            startedStartUpTypes.Add(keyValuePair);
        }

        if (appStartUps.Count == 0)
        {
            return;
        }

        RoleSet.Current = new RoleSet(startedStartUpTypes);

        // C143c D3：Role 快照发布后装配跨 Role 路由缝。本地投递器暂不配置（随 C143e 接入 Actor 投递），
        // 远程转发用 C143d 前的占位实现——过早路由会在缝上显式抛异常，不会静默丢消息。
        RoleRouterHolder.Initialize(new InProcessRoleRouter(RoleSet.Current, null, new RemoteRoleRouter()));
        _launchTask = AppEnter.Entry(appStartUps);
    }

    /// <summary>
    /// 创建并初始化单个 Role 的启动实例。
    /// </summary>
    /// <remarks>
    /// Creates and initializes the startup instance of a single role.
    /// Instantiates the startup class, fixes the process-level log type, and initializes the instance
    /// (the shared kernel runs through <see cref="AppBootstrapper.EnsureInitialized"/>, C143b D5).
    /// Returns null when the class cannot be instantiated or initialization fails.
    /// </remarks>
    /// <param name="args">命令行参数 / Command line arguments</param>
    /// <param name="appStartUpType">启动类的类型 / The type of the startup class</param>
    /// <param name="serverType">服务器类型标识符 / The server type identifier</param>
    /// <param name="setting">服务器的应用程序设置 / Application settings for the server</param>
    /// <returns>启动实例；无法创建或初始化失败时为 null / The startup instance, or null when creation or initialization fails</returns>
    private static IAppStartUp CreateStartUp(string[] args, Type appStartUpType, string serverType, AppSetting setting)
    {
        var startUp = (IAppStartUp)Activator.CreateInstance(appStartUpType);
        if (startUp == null)
        {
            return null;
        }

        SetLogTypeOnce(serverType);
        var isSuccess = startUp.Init(serverType, setting, args);
        if (!isSuccess)
        {
            return null;
        }

        LogHelper.ShowOption(LocalizationService.GetString(Keys.StartUp.StartingServerWithConfiguration, serverType), startUp.Setting.ToFormatString());
        return startUp;
    }

    /// <summary>
    /// 解析指定 Role 的应用程序配置（配置段优先，回落启动器选项的独立副本）。
    /// </summary>
    /// <remarks>
    /// Resolves the application settings for the given role: the matching config file section wins;
    /// otherwise each role gets an independent copy of the launcher options with
    /// <see cref="AppSetting.ServerType"/> fixed to that role — multiple roles never share one
    /// mutable <see cref="AppSetting"/> instance, and configuration/logging/event flows that read
    /// <c>Setting.ServerType</c> always observe the single current role name instead of the raw
    /// comma-separated CLI value (e.g. "Game,Social"). When the launcher options themselves are
    /// unavailable (argument parsing failed), <c>null</c> is returned so the role's
    /// <see cref="AppStartUpBase.Init"/> override creates its own role-level defaults.
    /// </remarks>
    /// <param name="serverType">服务器类型标识符 / The server type identifier</param>
    /// <param name="appSettings">配置中的应用程序设置集合 / Collection of application settings from configuration</param>
    /// <param name="launcherOptions">用于默认配置的启动器选项 / Launcher options for default configuration</param>
    /// <param name="warnOnMissingConfiguration">无配置段时是否记 Warning / Whether to warn when the config section is missing</param>
    /// <returns>该 Role 的应用程序配置；启动器选项不可用时为 null / The application settings for the role, or null when the launcher options are unavailable</returns>
    internal static AppSetting ResolveAppSetting(string serverType, IEnumerable<AppSetting> appSettings, StartupOptions launcherOptions, bool warnOnMissingConfiguration)
    {
        var appSetting = appSettings.FirstOrDefault(m => m.ServerType == serverType);
        if (appSetting != null)
        {
            return appSetting;
        }

        if (warnOnMissingConfiguration)
        {
            LogHelper.Warning(LocalizationService.GetString(Keys.StartUp.NoConfigurationUseDefault, serverType));
        }

        // 启动器选项解析失败：返回 null，让各 Role 的 Init 用自己的缺省配置
        if (launcherOptions == null)
        {
            return null;
        }

        return CreateRoleDefaultSetting(serverType, launcherOptions);
    }

    /// <summary>
    /// 从启动器选项构建指定 Role 的独立默认配置（C143b：缺失配置段的 Role 各持一份配置实例）。
    /// </summary>
    /// <remarks>
    /// Builds an independent default <see cref="AppSetting"/> for the given role from the launcher options
    /// (C143b: every role missing a config section holds its own instance). Copies the AppSetting-level
    /// property values of the launcher options, then fixes <see cref="AppSetting.ServerType"/> to the
    /// current role name (its init-only setter also updates <c>ServerName</c>), never leaking the raw
    /// comma-separated CLI value ("Game,Social") into a per-role setting.
    /// </remarks>
    /// <param name="serverType">服务器类型标识符 / The server type identifier</param>
    /// <param name="launcherOptions">启动器选项 / The launcher options</param>
    /// <returns>该 Role 的独立默认配置 / The independent default settings for the role</returns>
    private static AppSetting CreateRoleDefaultSetting(string serverType, StartupOptions launcherOptions)
    {
        var roleSetting = new AppSetting();
        foreach (var property in typeof(AppSetting).GetProperties())
        {
            if (!property.CanRead || !property.CanWrite)
            {
                continue;
            }

            if (property.Name == nameof(AppSetting.ServerType))
            {
                // ServerType 为 init-only 且必须使用当前 Role 名，不能沿用 "Game,Social" 组合值（init 限制仅在编译期，反射赋值合法）
                property.SetValue(roleSetting, serverType);
                continue;
            }

            property.SetValue(roleSetting, property.GetValue(launcherOptions));
        }

        return roleSetting;
    }

    /// <summary>
    /// 输出选定集合内的启动优先级冲突表（C143b：重复优先级导致多 Role 拉起顺序不稳定）。
    /// </summary>
    /// <remarks>
    /// Reports a conflict table when multiple selected roles share the same startup priority,
    /// because the multi-role launch order is unstable between roles of equal priority.
    /// Single-role launches never report (a group of one cannot conflict).
    /// </remarks>
    /// <param name="startUpTypes">按优先级排序选定的启动类型集合 / The selected startup types in priority order</param>
    private static void LogPriorityConflicts(IReadOnlyList<KeyValuePair<Type, StartUpTagAttribute>> startUpTypes)
    {
        var conflictTable = GetPriorityConflictTable(startUpTypes);
        if (conflictTable == null)
        {
            return;
        }

        LogHelper.Warning($"Duplicate startup priorities detected; multi-role launch order is unstable between these roles (C143b) — {conflictTable}");
    }

    /// <summary>
    /// 构建选定集合内的启动优先级冲突表。
    /// </summary>
    /// <remarks>
    /// Builds the priority conflict table of the selected collection:
    /// groups roles by priority and renders every group holding more than one role
    /// (e.g. "priority 1000: Game,Social"); returns null when no conflict exists.
    /// </remarks>
    /// <param name="startUpTypes">按优先级排序选定的启动类型集合 / The selected startup types in priority order</param>
    /// <returns>冲突表文本；无冲突时为 null / The conflict table text, or null when there is no conflict</returns>
    internal static string GetPriorityConflictTable(IReadOnlyList<KeyValuePair<Type, StartUpTagAttribute>> startUpTypes)
    {
        var conflictEntries = startUpTypes.GroupBy(pair => pair.Value.Priority)
            .Where(group => group.Count() > 1)
            .Select(group =>
            {
                var roleNames = string.Join(",", group.Select(pair => pair.Value.ServerType));
                return $"priority {group.Key}: {roleNames}";
            })
            .ToList();
        if (conflictEntries.Count == 0)
        {
            return null;
        }

        return string.Join("; ", conflictEntries);
    }

    /// <summary>
    /// 设置进程级固定日志标识（C143a D20#4：LogType 只在首次设置时生效，不再随最后启动的 Role 漂移）。
    /// </summary>
    /// <remarks>
    /// Sets the process-level fixed log type (C143a D20#4): the log type is assigned only on the first
    /// call, so it no longer drifts to the last started role in a multi-role process.
    /// Single-role startup calls this exactly once, keeping current behaviour unchanged.
    /// </remarks>
    /// <param name="serverType">服务器类型标识符 / The server type identifier</param>
    internal static void SetLogTypeOnce(string serverType)
    {
        if (!LogOptions.Default.LogType.IsNullOrEmptyOrWhiteSpace())
        {
            return;
        }

        LogOptions.Default.LogType = serverType;
    }

    /// <summary>
    /// 尝试按 All-in-One 选项选择并启动 Role 集合（C143b D2）。
    /// </summary>
    /// <remarks>
    /// Attempts to select and launch the role collection according to the all-in-one options:
    /// <c>--AllInOne</c> launches every registered role; a plural <c>--ServerType</c> launches the matching
    /// roles in priority order; a single value or no value keeps the current single-role behaviour unchanged.
    /// </remarks>
    /// <param name="args">命令行参数 / Command line arguments</param>
    /// <param name="allInOneOptions">多 Role / All-in-One 解析结果 / The all-in-one parse result</param>
    /// <param name="sortedStartUpTypes">按优先级排序的启动类型集合 / Collection of startup types sorted by priority</param>
    /// <param name="launcherOptions">包含默认配置的启动器选项 / Launcher options containing default configuration</param>
    private static void TryLaunchServer(string[] args, AllInOneOptions allInOneOptions, IEnumerable<KeyValuePair<Type, StartUpTagAttribute>> sortedStartUpTypes, StartupOptions launcherOptions)
    {
        var appSettings = GlobalSettings.GetSettings();
        var selectedStartUpTypes = SelectStartUpTypes(allInOneOptions, sortedStartUpTypes);

        // C143f D4：Role 实例化前的唯一收口点做启动期校验（选中集合 + 文件段 + CLI 选项 + 原始参数齐备），
        // 冲突 fail fast 抛 ConfigConflictException，先于任何 Role 拉起
        ConfigStartupValidator.Validate(
            selectedStartUpTypes.Select(pair => pair.Value.ServerType).ToList(),
            appSettings,
            launcherOptions,
            args,
            allInOneOptions);

        // 缺省回退形态（无 --ServerType）保留现状：对首个可用 Role 无配置段记 Warning 并使用默认配置
        var warnOnMissingConfiguration = !allInOneOptions.IsAllInOne && allInOneOptions.ServerTypes.Count == 0;

        Launcher(args, selectedStartUpTypes, appSettings, launcherOptions, warnOnMissingConfiguration);
    }

    /// <summary>
    /// 按 All-in-One 选项选定要拉起的 Role 集合（结果保持优先级序）。
    /// </summary>
    /// <remarks>
    /// Selects the roles to launch according to the all-in-one options; the result always keeps the
    /// <see cref="StartUpTypeRegistry"/> priority order (higher priority first), regardless of the CLI input order.
    /// <list type="bullet">
    /// <item><c>--AllInOne</c> → every registered role.</item>
    /// <item>plural <c>--ServerType</c> (more than one name) → the registered matches, unregistered names are skipped with a warning.</item>
    /// <item>single <c>--ServerType</c> → the single match, or nothing when unregistered (current behaviour: silent skip).</item>
    /// <item>no value → the first available role in priority order (current behaviour).</item>
    /// </list>
    /// </remarks>
    /// <param name="allInOneOptions">多 Role / All-in-One 解析结果 / The all-in-one parse result</param>
    /// <param name="sortedStartUpTypes">按优先级排序的启动类型集合 / Collection of startup types sorted by priority</param>
    /// <returns>选定的启动类型集合（优先级序）/ The selected startup types in priority order</returns>
    internal static IReadOnlyList<KeyValuePair<Type, StartUpTagAttribute>> SelectStartUpTypes(AllInOneOptions allInOneOptions, IEnumerable<KeyValuePair<Type, StartUpTagAttribute>> sortedStartUpTypes)
    {
        ArgumentNullException.ThrowIfNull(allInOneOptions, nameof(allInOneOptions));

        var sortedList = sortedStartUpTypes as IReadOnlyList<KeyValuePair<Type, StartUpTagAttribute>> ?? sortedStartUpTypes.ToList();

        if (allInOneOptions.IsAllInOne)
        {
            // All-in-One：拉起全部已注册 Role（D2）
            return sortedList;
        }

        if (allInOneOptions.ServerTypes.Count > 1)
        {
            // 复数形态：按名匹配，保持优先级序；未注册名跳过并记 Warning
            var serverTypeNames = new HashSet<string>(allInOneOptions.ServerTypes, StringComparer.Ordinal);
            var selected = sortedList.Where(pair => serverTypeNames.Contains(pair.Value.ServerType)).ToList();
            foreach (var serverTypeName in allInOneOptions.ServerTypes)
            {
                if (selected.All(pair => pair.Value.ServerType != serverTypeName))
                {
                    LogHelper.Warning($"No registered startup type found for server type '{serverTypeName}' (C143b multi-role selection); skipped it.");
                }
            }

            return selected;
        }

        if (allInOneOptions.ServerTypes.Count == 1)
        {
            // 单 Role 现状形态：未注册时静默跳过（保留现状行为）
            var startKv = sortedList.FirstOrDefault(m => m.Value.ServerType == allInOneOptions.ServerTypes[0]);
            if (startKv.Value == null)
            {
                return Array.Empty<KeyValuePair<Type, StartUpTagAttribute>>();
            }

            return new List<KeyValuePair<Type, StartUpTagAttribute>> { startKv };
        }

        // 缺省：启动第一个可用的服务器（现状行为）
        if (sortedList.Count == 0)
        {
            return Array.Empty<KeyValuePair<Type, StartUpTagAttribute>>();
        }

        return new List<KeyValuePair<Type, StartUpTagAttribute>> { sortedList[0] };
    }
}
