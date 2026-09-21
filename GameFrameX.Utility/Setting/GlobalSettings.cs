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
using GameFrameX.Foundation.Json;
using GameFrameX.Foundation.Logger;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Foundation.Utility.DistributedSystem.Snowflake;

namespace GameFrameX.Utility.Setting;

/// <summary>
/// 全局设置
/// </summary>
/// <remarks>
/// Global settings class that manages application configuration.
/// </remarks>
public static class GlobalSettings
{
    /// <summary>
    /// 存储应用设置的列表
    /// </summary>
    /// <remarks>
    /// List storing application settings.
    /// </remarks>
    private static readonly List<AppSetting> Settings = new(16);

    /// <summary>
    /// 进程级字段的反射缓存（C143a D19：带 <see cref="SettingFieldLevelAttribute"/> 且级别为 <see cref="SettingFieldLevel.ProcessLevel"/> 的公共属性）。
    /// </summary>
    /// <remarks>
    /// Reflection cache of process-level properties (C143a D19: public properties annotated as ProcessLevel).
    /// </remarks>
    private static readonly PropertyInfo[] ProcessLevelProperties = typeof(AppSetting)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(property => SettingFieldLevelAttribute.GetLevel(property) == SettingFieldLevel.ProcessLevel)
        .ToArray();

    /// <summary>
    /// 获取当前应用程序设置
    /// </summary>
    /// <remarks>
    /// Gets the current application settings.
    /// Can only be set through SetCurrentSetting method to ensure configuration security.
    /// Stores the currently active application configuration information.
    /// </remarks>
    public static AppSetting CurrentSetting { get; private set; }

    /// <summary>
    /// 加载启动配置
    /// </summary>
    /// <remarks>
    /// Loads startup configuration from the specified file path.
    /// </remarks>
    /// <param name="path">配置文件路径 / Configuration file path</param>
    /// <exception cref="InvalidOperationException">当配置文件解析失败时抛出 / Thrown when configuration file parsing fails</exception>
    /// <exception cref="Exception">当服务器ID不在合法范围内时抛出 / Thrown when server ID is not within valid range</exception>
    public static void Load(string path)
    {
        Settings.Clear();
        if (path.IsNullOrEmptyOrWhiteSpace())
        {
            return;
        }

        var fullPath = Path.GetFullPath(path);
        LogHelper.ShowOption(LocalizationService.GetString(Localization.Keys.Utility.GlobalSettings.LoadGlobalSettings), fullPath);
        if (!File.Exists(path))
        {
            LogHelper.ShowOption(LocalizationService.GetString(Localization.Keys.Utility.GlobalSettings.LoadGlobalSettingsFailed), LocalizationService.GetString(Localization.Keys.Utility.Settings.LoadConfigurationFailed));
            return;
        }

        var configJson = File.ReadAllText(path);
        var settings = JsonHelper.Deserialize<List<AppSetting>>(configJson) ?? throw new InvalidOperationException();

        foreach (var setting in settings)
        {
            setting.ServerId.IsRange(GlobalConst.MinServerId, GlobalConst.MaxServerId);
            Settings.Add(setting);
        }
    }

    /// <summary>
    /// 设置当前应用程序设置
    /// </summary>
    /// <remarks>
    /// Sets the current application settings.
    /// This method is used to update the global current settings.
    /// Typically called during application startup or when switching configurations.
    /// Features:
    /// 1. Process-level field normalization runs first (SaveDataInterval / HttpUrl / NetWorkSendTimeOutSeconds / ActorRecycleTime),
    ///    then repeated settings perform process-level field consistency validation (C143a D19) on the normalized values:
    ///    identical process-level fields pass through idempotently, conflicting ones fail fast with <see cref="SettingConflictException"/>;
    ///    role-level fields accept the latest value, so equivalent multi-Role configurations are not reported as conflicts
    /// 2. Null values are not allowed
    /// 3. Automatically corrects SaveDataInterval if less than 5000ms
    /// </remarks>
    /// <param name="setting">要设置的应用程序配置对象 / Application configuration object to set</param>
    /// <exception cref="ArgumentNullException">当传入的setting参数为null时抛出此异常 / Thrown when the setting parameter is null</exception>
    /// <exception cref="SettingConflictException">当与当前设置存在进程级字段冲突时抛出 / Thrown when process-level fields conflict with the current setting</exception>
    public static void SetCurrentSetting(AppSetting setting)
    {
        ArgumentNullException.ThrowIfNull(setting, nameof(setting));

        if (setting.SaveDataInterval < 5000)
        {
            LogHelper.Warning<string>("GlobalSettings.SetCurrentSetting {setting}", LocalizationService.GetString(Localization.Keys.Utility.Settings.SaveDataIntervalTooSmall, GlobalConst.SaveIntervalInMilliSeconds));
            setting.SaveDataInterval = GlobalConst.SaveIntervalInMilliSeconds;
        }

        if (setting.HttpUrl.IsNullOrEmptyOrWhiteSpace())
        {
            LogHelper.Warning<string>("GlobalSettings.SetCurrentSetting {setting}", LocalizationService.GetString(Localization.Keys.Utility.GlobalSettings.HttpUrlEmptyUseDefault, "/game/api/"));
            setting.HttpUrl = "/game/api/";
        }

        if (setting.NetWorkSendTimeOutSeconds < 1)
        {
            LogHelper.Warning<string>("GlobalSettings.SetCurrentSetting {setting}", LocalizationService.GetString(Localization.Keys.Utility.GlobalSettings.NetworkTimeoutTooShort, 5));
            setting.NetWorkSendTimeOutSeconds = 5;
        }

        if (setting.ActorRecycleTime < 1)
        {
            LogHelper.Warning<string>("GlobalSettings.SetCurrentSetting {setting}", LocalizationService.GetString(Localization.Keys.Utility.GlobalSettings.ActorRecycleTimeTooShort, 5));
            setting.ActorRecycleTime = 5;
        }

        // C143a D19 修复：先完成上面的进程级字段规范化，再用规范化后的值做一致性校验，
        // 避免等价的多 Role 配置（规范化后相同）被误判为冲突。
        if (CurrentSetting.IsNotNull())
        {
            var conflicts = CollectProcessLevelConflicts(CurrentSetting, setting);
            if (conflicts.Count > 0)
            {
                throw new SettingConflictException(conflicts);
            }
        }

        // 创建ID生成器配置，WorkerId设为0
        if (setting.WorkerId > 0)
        {
            SnowFlakeIdHelper.WorkId = setting.WorkerId;
        }

        // 设置数据中心ID
        if (setting.DataCenterId > 0)
        {
            SnowFlakeIdHelper.DataCenterId = setting.DataCenterId;
        }

        CurrentSetting = setting;
    }

    /// <summary>
    /// 比对两份设置的进程级字段，返回全部冲突项（C143a D19）。
    /// </summary>
    /// <remarks>
    /// Compares process-level fields between two settings and returns every conflict (C143a D19).
    /// Role-level fields are ignored; null and null are treated as equal.
    /// </remarks>
    /// <param name="currentSetting">当前生效的设置 / The currently effective setting</param>
    /// <param name="incomingSetting">新传入的设置 / The incoming setting</param>
    /// <returns>进程级字段冲突列表；无冲突时为空 / The list of process-level field conflicts; empty when none</returns>
    private static List<SettingFieldConflict> CollectProcessLevelConflicts(AppSetting currentSetting, AppSetting incomingSetting)
    {
        var conflicts = new List<SettingFieldConflict>();
        foreach (var property in ProcessLevelProperties)
        {
            var currentValue = property.GetValue(currentSetting);
            var incomingValue = property.GetValue(incomingSetting);
            if (Equals(currentValue, incomingValue))
            {
                continue;
            }

            conflicts.Add(new SettingFieldConflict(property.Name, currentSetting.ServerType, currentValue, incomingSetting.ServerType, incomingValue));
        }

        return conflicts;
    }

    /// <summary>
    /// 重置当前应用程序设置（仅供单元测试隔离静态状态使用）。
    /// </summary>
    /// <remarks>
    /// Resets the current application setting. For unit test isolation of static state only.
    /// </remarks>
    internal static void ResetCurrentSetting()
    {
        CurrentSetting = null;
    }

    /// <summary>
    /// 获取所有设置
    /// </summary>
    /// <remarks>
    /// Gets all application settings.
    /// </remarks>
    /// <returns>返回所有设置的列表 / List of all settings</returns>
    public static List<AppSetting> GetSettings()
    {
        return Settings.ToList();
    }

    /// <summary>
    /// 根据服务器类型获取设置
    /// </summary>
    /// <remarks>
    /// Gets settings by server type.
    /// </remarks>
    /// <param name="serverType">服务器类型 / Server type</param>
    /// <returns>返回匹配的设置列表 / List of matching settings</returns>
    public static List<AppSetting> GetSettings(string serverType)
    {
        var result = new List<AppSetting>();
        foreach (var setting in Settings)
        {
            if (setting.ServerType == serverType)
            {
                result.Add(setting);
            }
        }

        return result;
    }

    /// <summary>
    /// 根据服务器名称获取特定类型的设置
    /// </summary>
    /// <remarks>
    /// Gets settings by server name.
    /// </remarks>
    /// <param name="serverName">服务器名称，用于匹配AppSetting中的ServerName属性 / Server name to match against AppSetting's ServerName property</param>
    /// <typeparam name="T">设置类型，用于类型安全检查，确保返回正确的设置类型 / Setting type for type safety, ensuring correct setting type is returned</typeparam>
    /// <returns>返回匹配的设置，如果没有找到则返回null / The matching setting, or null if not found</returns>
    /// <exception cref="ArgumentNullException">当serverName为null时抛出此异常 / Thrown when serverName is null</exception>
    public static AppSetting GetSettingByServerName<T>(string serverName)
    {
        ArgumentNullException.ThrowIfNull(serverName, nameof(serverName));
        foreach (var setting in Settings)
        {
            if (setting.ServerName == serverName)
            {
                return setting;
            }
        }

        return null;
    }

    /// <summary>
    /// 根据服务器Id获取特定类型的设置
    /// </summary>
    /// <remarks>
    /// Gets settings by server ID.
    /// This method does not validate the serverId; ensure the value is within valid range.
    /// </remarks>
    /// <param name="tagName">服务器Id，用于匹配AppSetting中的ServerId属性 / Server ID to match against AppSetting's ServerId property</param>
    /// <typeparam name="T">设置类型，用于类型安全检查，确保返回正确的设置类型 / Setting type for type safety, ensuring correct setting type is returned</typeparam>
    /// <returns>返回匹配的设置，如果没有找到则返回null / The matching setting, or null if not found</returns>
    public static AppSetting GetSettingByServerId<T>(int tagName)
    {
        foreach (var setting in Settings)
        {
            if (setting.ServerId == tagName)
            {
                return setting;
            }
        }

        return null;
    }

    /// <summary>
    /// 根据服务器标签名称获取特定类型的设置
    /// </summary>
    /// <remarks>
    /// Gets settings by tag name.
    /// </remarks>
    /// <param name="tagName">服务器标签名称，用于匹配AppSetting中的TagName属性 / Tag name to match against AppSetting's TagName property</param>
    /// <typeparam name="T">设置类型，用于类型安全检查，确保返回正确的设置类型 / Setting type for type safety, ensuring correct setting type is returned</typeparam>
    /// <returns>返回匹配的设置，如果没有找到则返回null / The matching setting, or null if not found</returns>
    /// <exception cref="ArgumentNullException">当tagName为null时抛出此异常 / Thrown when tagName is null</exception>
    public static AppSetting GetSettingByTagName<T>(string tagName)
    {
        ArgumentNullException.ThrowIfNull(tagName, nameof(tagName));
        foreach (var setting in Settings)
        {
            if (setting.TagName == tagName)
            {
                return setting;
            }
        }

        return null;
    }

    /// <summary>
    /// 根据服务器类型获取特定类型的设置
    /// </summary>
    /// <remarks>
    /// Gets the first setting matching the specified server type.
    /// </remarks>
    /// <param name="serverType">服务器类型 / Server type</param>
    /// <typeparam name="T">设置类型 / Setting type</typeparam>
    /// <returns>返回匹配的设置，如果没有找到则返回null / The matching setting, or null if not found</returns>
    public static AppSetting GetSetting<T>(string serverType)
    {
        foreach (var setting in Settings)
        {
            if (setting.ServerType == serverType)
            {
                return setting;
            }
        }

        return null;
    }
}
