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
using GameFrameX.Foundation.Extensions;
using GameFrameX.Foundation.Options;
using GameFrameX.StartUp.Options;
using GameFrameX.Utility.Setting;

namespace GameFrameX.StartUp.Configuration;

/// <summary>
/// 启动期配置校验器（C143f D4 配置单源收口，fail fast）。
/// </summary>
/// <remarks>
/// Validates the configuration layers of a process at startup (C143f D4 single source of truth, fail fast):
/// <list type="number">
/// <item><see cref="ConfigConflictKind.MissingSection"/>：多 Role 显式形态（--AllInOne 或复数 --ServerType）下，
/// 所选 ServerType 必须在文件层（Configs/app_config.json）有配置段；单值/缺省形态保留现状回落，不报冲突（AC-3）。</item>
/// <item><see cref="ConfigConflictKind.PortConflict"/>：同进程 ≥2 个选中 Role 的文件段之间，
/// 同一端口字段的非零值相同即冲突（跨字段碰撞不在检测范围，bind 期报错兜底）。</item>
/// <item><see cref="ConfigConflictKind.CliFileValueConflict"/>：CLI 显式设置（与全缺省 StartupOptions 相异判定）
/// 且与所用文件段值不同的 AppSetting 字段即冲突——文件段是单源，CLI 角色级/进程级字段仅在无段回落形态生效；
/// ServerType（段选择键）与 IsAllInOne/IsSingleMode（开关，声明于 StartupOptions）豁免。</item>
/// </list>
/// 检出任何冲突即抛 <see cref="ConfigConflictException"/>，消息列出全部冲突字段与来源段。
/// </remarks>
public static class ConfigStartupValidator
{
    /// <summary>
    /// 参与端口冲突检测的端口字段及其使能开关（开关全开才视为会实际 bind；0 视为未设置跳过）。
    /// </summary>
    /// <remarks>
    /// The port fields participating in the port-conflict check together with their enable switches:
    /// a port counts only when every enable switch is on (disabled listeners never bind, so equal default
    /// values of disabled ports are not conflicts); 0 counts as unset and is skipped.
    /// </remarks>
    private static readonly (string PortFieldName, string[] EnableFieldNames)[] PortChecks =
    {
        (nameof(AppSetting.KcpPort), new[] { nameof(AppSetting.IsEnableKcp) }),
        (nameof(AppSetting.InnerPort), new[] { nameof(AppSetting.IsEnableTcp) }),
        (nameof(AppSetting.OuterPort), new[] { nameof(AppSetting.IsEnableTcp) }),
        (nameof(AppSetting.HttpPort), new[] { nameof(AppSetting.IsEnableHttp) }),
        (nameof(AppSetting.HttpsPort), new[] { nameof(AppSetting.IsEnableHttp) }),
        (nameof(AppSetting.OnlineAdminPort), new[] { nameof(AppSetting.IsEnableOnlineAdmin) }),
        (nameof(AppSetting.MetricsPort), new[] { nameof(AppSetting.IsOpenTelemetry), nameof(AppSetting.IsOpenTelemetryMetrics) }),
        (nameof(AppSetting.WsPort), new[] { nameof(AppSetting.IsEnableWebSocket) }),
        (nameof(AppSetting.WssPort), new[] { nameof(AppSetting.IsEnableWebSocket) }),
    };

    /// <summary>
    /// 参与 CLI-文件冲突检测的 AppSetting 公共读写属性。
    /// ServerType 为段选择键、ServerName 为其 init setter 派生标签（复数 CLI 值 "Game,Social" 永不与逐段值一致），二者豁免；
    /// IsAllInOne/IsSingleMode 声明于 StartupOptions，天然不在 typeof(AppSetting) 属性集内。
    /// </summary>
    /// <remarks>
    /// The public read-write AppSetting properties participating in the CLI-file conflict check.
    /// ServerType is the section selection key and ServerName is a label derived from it by its init setter
    /// (a plural CLI value "Game,Social" never matches a per-section value), so both are exempt;
    /// IsAllInOne/IsSingleMode are declared on StartupOptions and never appear in the typeof(AppSetting) property set.
    /// </remarks>
    private static readonly PropertyInfo[] MergeCheckProperties = typeof(AppSetting)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(property => property.CanRead && property.CanWrite
                           && property.Name != nameof(AppSetting.ServerType)
                           && property.Name != nameof(AppSetting.ServerName))
        .ToArray();

    /// <summary>
    /// 校验启动期配置，冲突时抛 <see cref="ConfigConflictException"/>（fail fast）。
    /// </summary>
    /// <remarks>
    /// Validates the startup configuration and throws <see cref="ConfigConflictException"/> on any conflict (fail fast).
    /// Runs before any role instantiation: the caller passes the selected role collection, the file-layer
    /// sections, the parsed CLI options, and the all-in-one parse result.
    /// </remarks>
    /// <param name="selectedServerTypes">选中的 Role 名集合（优先级序）/ The selected role names in priority order</param>
    /// <param name="fileSettings">文件层配置段（Configs/app_config.json 反序列化结果）/ The file-layer sections deserialized from Configs/app_config.json</param>
    /// <param name="launcherOptions">CLI 解析结果；解析失败为 null 时跳过 CLI-文件冲突检测 / The parsed CLI options; the CLI-file check is skipped when null (argument parsing failed)</param>
    /// <param name="allInOneOptions">多 Role / All-in-One 解析结果 / The all-in-one parse result</param>
    /// <exception cref="ConfigConflictException">检出任何冲突时抛出 / Thrown when any conflict is detected</exception>
    public static void Validate(IReadOnlyList<string> selectedServerTypes, IEnumerable<AppSetting> fileSettings, StartupOptions launcherOptions, AllInOneOptions allInOneOptions)
    {
        ArgumentNullException.ThrowIfNull(selectedServerTypes, nameof(selectedServerTypes));

        var conflicts = new List<ConfigFieldConflict>();
        var sectionsByServerType = new Dictionary<string, AppSetting>(StringComparer.Ordinal);
        if (fileSettings != null)
        {
            foreach (var setting in fileSettings)
            {
                if (setting == null || setting.ServerType.IsNullOrEmpty())
                {
                    continue;
                }

                // 同名段保留首个（与 GlobalSettings.GetSettings → ResolveAppSetting 的 FirstOrDefault 语义一致）
                sectionsByServerType.TryAdd(setting.ServerType, setting);
            }
        }

        CollectMissingSectionConflicts(conflicts, selectedServerTypes, sectionsByServerType, allInOneOptions);
        CollectPortConflicts(conflicts, selectedServerTypes, sectionsByServerType);
        CollectCliFileConflicts(conflicts, selectedServerTypes, sectionsByServerType, launcherOptions);

        if (conflicts.Count > 0)
        {
            throw new ConfigConflictException(conflicts);
        }
    }

    /// <summary>
    /// 段缺失检测：仅多 Role 显式形态（--AllInOne 或复数 --ServerType）强校验；单值/缺省形态保留现状回落（AC-3）。
    /// </summary>
    /// <remarks>
    /// Missing-section detection: enforced only under an explicit multi-role form (--AllInOne or a plural --ServerType);
    /// single-value/default forms keep the current fallback behaviour (AC-3).
    /// </remarks>
    /// <param name="conflicts">冲突收集列表 / The conflict sink</param>
    /// <param name="selectedServerTypes">选中的 Role 名集合 / The selected role names</param>
    /// <param name="sectionsByServerType">文件层段索引 / The file-layer section index</param>
    /// <param name="allInOneOptions">多 Role / All-in-One 解析结果 / The all-in-one parse result</param>
    private static void CollectMissingSectionConflicts(List<ConfigFieldConflict> conflicts, IReadOnlyList<string> selectedServerTypes, Dictionary<string, AppSetting> sectionsByServerType, AllInOneOptions allInOneOptions)
    {
        var isMultiRoleExplicitForm = allInOneOptions != null && (allInOneOptions.IsAllInOne || allInOneOptions.ServerTypes.Count > 1);
        if (!isMultiRoleExplicitForm)
        {
            return;
        }

        foreach (var serverType in selectedServerTypes)
        {
            if (sectionsByServerType.ContainsKey(serverType))
            {
                continue;
            }

            conflicts.Add(new ConfigFieldConflict(
                ConfigConflictKind.MissingSection,
                nameof(AppSetting.ServerType),
                "CLI(--ServerType/--AllInOne)",
                serverType,
                "file(Configs/app_config.json)",
                "(missing)"));
        }
    }

    /// <summary>
    /// 同进程端口冲突检测：≥2 个选中 Role 的文件段之间，同一使能开关全开的端口字段的非零值相同即冲突。
    /// </summary>
    /// <remarks>
    /// Same-process port-conflict detection: two selected roles whose file sections share the same
    /// non-zero value of one port field conflict — only ports whose enable switches are all on count,
    /// because a disabled listener never binds (equal defaults of disabled ports are not conflicts).
    /// ponytail: 跨字段碰撞（如 Game.InnerPort == Social.HttpPort）不在检测范围——bind 期报错兜底；
    /// 升级路径是按 Role 汇总全端口位图后统一比对。
    /// </remarks>
    /// <param name="conflicts">冲突收集列表 / The conflict sink</param>
    /// <param name="selectedServerTypes">选中的 Role 名集合 / The selected role names</param>
    /// <param name="sectionsByServerType">文件层段索引 / The file-layer section index</param>
    private static void CollectPortConflicts(List<ConfigFieldConflict> conflicts, IReadOnlyList<string> selectedServerTypes, Dictionary<string, AppSetting> sectionsByServerType)
    {
        var selectedSections = selectedServerTypes
            .Where(sectionsByServerType.ContainsKey)
            .Select(serverType => (ServerType: serverType, Setting: sectionsByServerType[serverType]))
            .ToList();
        if (selectedSections.Count < 2)
        {
            return;
        }

        foreach (var portCheck in PortChecks)
        {
            var portProperty = typeof(AppSetting).GetProperty(portCheck.PortFieldName, BindingFlags.Public | BindingFlags.Instance);
            if (portProperty == null || !selectedSections.All(role => AreAllEnableSwitchesOn(role.Setting, portCheck.EnableFieldNames)))
            {
                continue;
            }

            var portGroups = selectedSections
                .Select(role => (Role: role, Port: portProperty.GetValue(role.Setting)))
                .Where(entry => entry.Port != null && Convert.ToInt64(entry.Port) != 0)
                .GroupBy(entry => entry.Port)
                .Where(group => group.Count() > 1);
            foreach (var group in portGroups)
            {
                // 同组内相邻 Role 两两成对报告（典型两 Role 场景恰为一条）
                var roles = group.ToList();
                for (var index = 1; index < roles.Count; index++)
                {
                    conflicts.Add(new ConfigFieldConflict(
                        ConfigConflictKind.PortConflict,
                        portCheck.PortFieldName,
                        $"file[{roles[index - 1].Role.ServerType}]",
                        roles[index - 1].Port,
                        $"file[{roles[index].Role.ServerType}]",
                        roles[index].Port));
                }
            }
        }
    }

    /// <summary>
    /// 判断该 Role 的全部使能开关是否开启（任一开关字段缺失视为关闭）。
    /// </summary>
    /// <remarks>
    /// Determines whether every enable switch of the role is on (a missing switch field counts as off).
    /// </remarks>
    /// <param name="setting">该 Role 的文件段 / The role's file section</param>
    /// <param name="enableFieldNames">使能开关字段名集合 / The enable switch field names</param>
    /// <returns>全部开启则为 <c>true</c> / <c>true</c> when every switch is on</returns>
    private static bool AreAllEnableSwitchesOn(AppSetting setting, string[] enableFieldNames)
    {
        foreach (var enableFieldName in enableFieldNames)
        {
            var enableProperty = typeof(AppSetting).GetProperty(enableFieldName, BindingFlags.Public | BindingFlags.Instance);
            if (enableProperty == null || !Equals(enableProperty.GetValue(setting), true))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// CLI-文件冲突检测：CLI 显式设置且与所用文件段值不同的字段即冲突（文件段是单源，CLI 值会被丢弃）。
    /// </summary>
    /// <remarks>
    /// CLI-file conflict detection: a field explicitly set on the command line whose value differs from the
    /// file section in use conflicts, because the file section is the single source and the CLI value would
    /// be silently dropped. "Explicitly set" means the value differs from an all-default
    /// <see cref="StartupOptions"/> instance — re-passing the exact default value is indistinguishable from
    /// not passing it and is therefore not reported.
    /// </remarks>
    /// <param name="conflicts">冲突收集列表 / The conflict sink</param>
    /// <param name="selectedServerTypes">选中的 Role 名集合 / The selected role names</param>
    /// <param name="sectionsByServerType">文件层段索引 / The file-layer section index</param>
    /// <param name="launcherOptions">CLI 解析结果 / The parsed CLI options</param>
    private static void CollectCliFileConflicts(List<ConfigFieldConflict> conflicts, IReadOnlyList<string> selectedServerTypes, Dictionary<string, AppSetting> sectionsByServerType, StartupOptions launcherOptions)
    {
        if (launcherOptions == null)
        {
            return;
        }

        // 全缺省基线：与 CreateWithDebug(args) 同一构造路径，未传参时二者属性值一致
        var defaultOptions = OptionsBuilder.CreateDefault<StartupOptions>();
        foreach (var serverType in selectedServerTypes)
        {
            if (!sectionsByServerType.TryGetValue(serverType, out var section))
            {
                continue;
            }

            foreach (var property in MergeCheckProperties)
            {
                var cliValue = property.GetValue(launcherOptions);
                if (Equals(cliValue, property.GetValue(defaultOptions)))
                {
                    continue;
                }

                var fileValue = property.GetValue(section);
                if (Equals(cliValue, fileValue))
                {
                    continue;
                }

                conflicts.Add(new ConfigFieldConflict(
                    ConfigConflictKind.CliFileValueConflict,
                    property.Name,
                    "CLI",
                    cliValue,
                    $"file[{serverType}]",
                    fileValue));
            }
        }
    }
}
