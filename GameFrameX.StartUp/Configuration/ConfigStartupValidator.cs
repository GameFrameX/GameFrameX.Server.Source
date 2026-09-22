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

/// <summary>
/// 启动期配置校验器（C143f D4 配置单源收口，fail fast）。
/// </summary>
/// <remarks>
/// Validates the configuration layers of a process at startup (C143f D4 single source of truth, fail fast):
/// <list type="number">
/// <item><see cref="ConfigConflictKind.MissingSection"/>：多 Role 显式形态（--AllInOne 或复数 --ServerType）下，
/// 所选 ServerType 必须在文件层（Configs/app_config.json）有配置段；单值/缺省形态保留现状回落，不报冲突（AC-3）。</item>
/// <item><see cref="ConfigConflictKind.PortConflict"/>：同进程 ≥2 个选中 Role 的文件段之间，
/// 会实际 bind 的监听端点（使能开关全开 + 非零端口，按传输协议 TCP/UDP 区分，跨字段统一比对）相同即冲突；
/// 同一 Role 的 InnerPort/OuterPort 共用监听是既有合法形态。</item>
/// <item><see cref="ConfigConflictKind.ProcessFieldConflict"/>：同进程 ≥2 个选中 Role 的文件段之间，
/// SettingFieldLevel(ProcessLevel) 字段规范化后的值不一致即冲突——共享内核会收到互相矛盾的进程配置。</item>
/// <item><see cref="ConfigConflictKind.CliFileValueConflict"/>：CLI 显式提供（从原始参数提取实际出现的选项键判定，
/// 显式传等于默认值的值同样参与比较）且与所用文件段值不同的 AppSetting 字段即冲突——文件段是单源，
/// CLI 角色级/进程级字段仅在无段回落形态生效；
/// ServerType（段选择键）与 IsAllInOne/IsSingleMode（开关，声明于 StartupOptions）豁免。</item>
/// </list>
/// 检出任何冲突即抛 <see cref="ConfigConflictException"/>，消息列出全部冲突字段与来源段。
/// </remarks>
public static class ConfigStartupValidator
{
    /// <summary>
    /// 参与端口冲突检测的端口字段、其使能开关与传输协议
    /// （开关全开才视为会实际 bind；0 视为未设置跳过；KCP 为 UDP，其余为 TCP）。
    /// </summary>
    /// <remarks>
    /// The port fields participating in the endpoint-conflict check, their enable switches and transports:
    /// a port counts only when every enable switch is on (disabled listeners never bind, so equal default
    /// values of disabled ports are not conflicts); 0 counts as unset and is skipped; KCP listens on UDP
    /// while every other listener listens on TCP, so a KCP port may legally share its number with a TCP port.
    /// </remarks>
    private static readonly (string PortFieldName, string[] EnableFieldNames, string Transport)[] PortChecks =
    {
        (nameof(AppSetting.KcpPort), new[] { nameof(AppSetting.IsEnableKcp) }, TransportUdp),
        (nameof(AppSetting.InnerPort), new[] { nameof(AppSetting.IsEnableTcp) }, TransportTcp),
        (nameof(AppSetting.OuterPort), new[] { nameof(AppSetting.IsEnableTcp) }, TransportTcp),
        (nameof(AppSetting.HttpPort), new[] { nameof(AppSetting.IsEnableHttp) }, TransportTcp),
        (nameof(AppSetting.HttpsPort), new[] { nameof(AppSetting.IsEnableHttp) }, TransportTcp),
        (nameof(AppSetting.OnlineAdminPort), new[] { nameof(AppSetting.IsEnableOnlineAdmin) }, TransportTcp),
        (nameof(AppSetting.MetricsPort), new[] { nameof(AppSetting.IsOpenTelemetry), nameof(AppSetting.IsOpenTelemetryMetrics) }, TransportTcp),
        (nameof(AppSetting.WsPort), new[] { nameof(AppSetting.IsEnableWebSocket) }, TransportTcp),
        (nameof(AppSetting.WssPort), new[] { nameof(AppSetting.IsEnableWebSocket) }, TransportTcp),
    };

    /// <summary>
    /// TCP 传输协议标识（SuperSocket TCP/WS、Kestrel HTTP/HTTPS、Online 管理面与独立 metrics 端口）。
    /// </summary>
    private const string TransportTcp = "TCP";

    /// <summary>
    /// UDP 传输协议标识（KCP 监听）。
    /// </summary>
    private const string TransportUdp = "UDP";

    /// <summary>
    /// 进程级字段的反射缓存（带 <see cref="SettingFieldLevelAttribute"/> 且级别为
    /// <see cref="SettingFieldLevel.ProcessLevel"/> 的公共属性，与 GlobalSettings 的进程级分区一致）。
    /// </summary>
    /// <remarks>
    /// Reflection cache of the process-level properties (annotated as ProcessLevel), mirroring the
    /// process-level partition used by GlobalSettings for its runtime consistency check.
    /// </remarks>
    private static readonly PropertyInfo[] ProcessLevelProperties = typeof(AppSetting)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(property => SettingFieldLevelAttribute.GetLevel(property) == SettingFieldLevel.ProcessLevel)
        .ToArray();

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
    /// sections, the parsed CLI options together with the raw arguments, and the all-in-one parse result.
    /// </remarks>
    /// <param name="selectedServerTypes">选中的 Role 名集合（优先级序）/ The selected role names in priority order</param>
    /// <param name="fileSettings">文件层配置段（Configs/app_config.json 反序列化结果）/ The file-layer sections deserialized from Configs/app_config.json</param>
    /// <param name="launcherOptions">CLI 解析结果；解析失败为 null 时跳过 CLI-文件冲突检测 / The parsed CLI options; the CLI-file check is skipped when null (argument parsing failed)</param>
    /// <param name="args">原始命令行参数，用于判定 CLI 字段是否显式提供 / The raw command-line arguments, used to determine which CLI fields were explicitly supplied</param>
    /// <param name="allInOneOptions">多 Role / All-in-One 解析结果 / The all-in-one parse result</param>
    /// <exception cref="ConfigConflictException">检出任何冲突时抛出 / Thrown when any conflict is detected</exception>
    public static void Validate(IReadOnlyList<string> selectedServerTypes, IEnumerable<AppSetting> fileSettings, StartupOptions launcherOptions, string[] args, AllInOneOptions allInOneOptions)
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

        var selectedSections = selectedServerTypes
            .Where(sectionsByServerType.ContainsKey)
            .Select(serverType => (ServerType: serverType, Setting: sectionsByServerType[serverType]))
            .ToList();

        CollectMissingSectionConflicts(conflicts, selectedServerTypes, sectionsByServerType, allInOneOptions);
        CollectPortConflicts(conflicts, selectedSections);
        CollectProcessFieldConflicts(conflicts, selectedSections);
        CollectCliFileConflicts(conflicts, selectedServerTypes, sectionsByServerType, launcherOptions, args);

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
    /// 同进程监听端点冲突检测：先展开所有会实际 bind 的端点（使能开关全开 + 非零端口），
    /// 再按（传输协议, 端口）统一分组比对，跨字段碰撞（如 Game.InnerPort == Social.HttpPort）与
    /// 同字段碰撞走同一条检测路径；仅使能该监听器的 Role 参与分组（禁用方不再掩盖其余 Role 的冲突）。
    /// </summary>
    /// <remarks>
    /// Same-process listening-endpoint conflict detection: every endpoint that would actually bind
    /// (enable switches all on + non-zero port) is expanded first, then grouped by (transport, port),
    /// so cross-field collisions (e.g. Game.InnerPort == Social.HttpPort) are detected by the same path as
    /// same-field collisions; only roles that enable the listener take part in a group, so a disabled role
    /// no longer masks the conflict of the remaining ones.
    /// The address dimension collapses to the wildcard: every listener binds a wildcard address at runtime
    /// (SuperSocket Ip="Any", Kestrel ListenAnyIP, likewise the Online admin and standalone metrics ports),
    /// so the endpoint key is transport + port; KCP listens on UDP and may share its number with a TCP port.
    /// Extracted endpoint expansion and clash reporting into helper methods to keep cognitive complexity
    /// under the Sonar S3776 threshold.
    /// </remarks>
    /// <param name="conflicts">冲突收集列表 / The conflict sink</param>
    /// <param name="selectedSections">选中 Role 的文件段列表 / The selected roles' file sections</param>
    private static void CollectPortConflicts(List<ConfigFieldConflict> conflicts, IReadOnlyList<(string ServerType, AppSetting Setting)> selectedSections)
    {
        if (selectedSections.Count < 2)
        {
            return;
        }

        var endpoints = ExpandEnabledEndpoints(selectedSections);
        ReportEndpointClashes(conflicts, endpoints);
    }

    /// <summary>
    /// 展开所有会实际 bind 的监听端点：遍历端口字段 × 选中 Role，
    /// 使能开关全开且端口非零的才收集（禁用监听器永不 bind，零端口视为未设置跳过）。
    /// </summary>
    /// <remarks>
    /// Expands every endpoint that would actually bind: iterating the port fields over the selected
    /// roles, only an endpoint whose enable switches are all on and whose port is non-zero is collected
    /// (a disabled listener never binds and a zero port counts as unset).
    /// Extracted from <see cref="CollectPortConflicts"/> to keep cognitive complexity under the Sonar S3776 threshold.
    /// </remarks>
    /// <param name="selectedSections">选中 Role 的文件段列表 / The selected roles' file sections</param>
    /// <returns>会实际 bind 的端点列表 / The endpoints that would actually bind</returns>
    private static List<(string ServerType, string FieldName, string Transport, long Port)> ExpandEnabledEndpoints(IReadOnlyList<(string ServerType, AppSetting Setting)> selectedSections)
    {
        var endpoints = new List<(string ServerType, string FieldName, string Transport, long Port)>();
        foreach (var portCheck in PortChecks)
        {
            var portProperty = typeof(AppSetting).GetProperty(portCheck.PortFieldName, BindingFlags.Public | BindingFlags.Instance);
            if (portProperty == null)
            {
                continue;
            }

            foreach (var role in selectedSections)
            {
                if (!AreAllEnableSwitchesOn(role.Setting, portCheck.EnableFieldNames))
                {
                    continue;
                }

                var port = Convert.ToInt64(portProperty.GetValue(role.Setting));
                if (port != 0)
                {
                    endpoints.Add((role.ServerType, portCheck.PortFieldName, portCheck.Transport, port));
                }
            }
        }

        return endpoints;
    }

    /// <summary>
    /// 按（传输协议, 端口）分组报告冲突：组内相邻端点两两成对报告，
    /// 同一 Role 的合法共享监听对（InnerPort 与 OuterPort 同值）豁免。
    /// </summary>
    /// <remarks>
    /// Reports the conflicts grouped by (transport, port): the adjacent endpoints of a group are
    /// reported pairwise, and the legal same-role shared-listener pairs (InnerPort equal to OuterPort)
    /// are exempt.
    /// Extracted from <see cref="CollectPortConflicts"/> to keep cognitive complexity under the Sonar S3776 threshold.
    /// </remarks>
    /// <param name="conflicts">冲突收集列表 / The conflict sink</param>
    /// <param name="endpoints">会实际 bind 的端点列表 / The endpoints that would actually bind</param>
    private static void ReportEndpointClashes(List<ConfigFieldConflict> conflicts, List<(string ServerType, string FieldName, string Transport, long Port)> endpoints)
    {
        foreach (var group in endpoints.GroupBy(endpoint => (endpoint.Transport, endpoint.Port)).Where(group => group.Count() > 1))
        {
            // 同组内相邻端点两两成对报告（典型两 Role 场景恰为一条；链式覆盖保证每个冲突 Role 至少出现一次）
            var clashes = group.ToList();
            for (var index = 1; index < clashes.Count; index++)
            {
                var previous = clashes[index - 1];
                var current = clashes[index];
                if (IsLegalSharedListenerPair(previous, current))
                {
                    continue;
                }

                ReportEndpointPairConflict(conflicts, previous, current);
            }
        }
    }

    /// <summary>
    /// 报告一对冲突端点：同字段的冲突字段名取单一字段名、来源段不带字段后缀；
    /// 跨字段的字段名取「前/后」拼接、来源段各带自己的字段后缀。
    /// </summary>
    /// <remarks>
    /// Reports one clashing endpoint pair: a same-field clash reports the single field name with the bare
    /// section sources, while a cross-field clash reports the joined "previous/current" field name with the
    /// section sources suffixed by their own field.
    /// Extracted from <see cref="CollectPortConflicts"/> to keep cognitive complexity under the Sonar S3776 threshold.
    /// </remarks>
    /// <param name="conflicts">冲突收集列表 / The conflict sink</param>
    /// <param name="previous">前一个端点 / The previous endpoint</param>
    /// <param name="current">当前端点 / The current endpoint</param>
    private static void ReportEndpointPairConflict(List<ConfigFieldConflict> conflicts, (string ServerType, string FieldName, string Transport, long Port) previous, (string ServerType, string FieldName, string Transport, long Port) current)
    {
        var sameField = previous.FieldName == current.FieldName;
        conflicts.Add(new ConfigFieldConflict(
            ConfigConflictKind.PortConflict,
            sameField ? previous.FieldName : $"{previous.FieldName}/{current.FieldName}",
            sameField ? $"file[{previous.ServerType}]" : $"file[{previous.ServerType}].{previous.FieldName}",
            previous.Port,
            sameField ? $"file[{current.ServerType}]" : $"file[{current.ServerType}].{current.FieldName}",
            current.Port));
    }

    /// <summary>
    /// 判断同组内相邻两个端点是否为同一 Role 的合法共享监听（InnerPort 与 OuterPort 同值共用监听口径）。
    /// </summary>
    /// <remarks>
    /// Determines whether two adjacent endpoints of one group form the legal same-role shared-listener form
    /// (InnerPort and OuterPort sharing one value): OuterPort is the advertised counterpart of the inner
    /// listener and does not bind a second socket, so the pair is not a conflict.
    /// </remarks>
    /// <param name="previous">前一个端点 / The previous endpoint</param>
    /// <param name="current">当前端点 / The current endpoint</param>
    /// <returns>合法共享返回 <c>true</c> / <c>true</c> when the pair is a legal shared listener</returns>
    private static bool IsLegalSharedListenerPair((string ServerType, string FieldName, string Transport, long Port) previous, (string ServerType, string FieldName, string Transport, long Port) current)
    {
        if (previous.ServerType != current.ServerType)
        {
            return false;
        }

        return (previous.FieldName == nameof(AppSetting.InnerPort) && current.FieldName == nameof(AppSetting.OuterPort))
               || (previous.FieldName == nameof(AppSetting.OuterPort) && current.FieldName == nameof(AppSetting.InnerPort));
    }

    /// <summary>
    /// 同进程进程级字段一致性检测：≥2 个选中 Role 的文件段之间，
    /// SettingFieldLevel(ProcessLevel) 字段按运行期规范化规则取比较值，不一致即冲突。
    /// </summary>
    /// <remarks>
    /// Same-process process-level field consistency detection: among the selected roles' file sections,
    /// every SettingFieldLevel(ProcessLevel) field is compared using the runtime normalization rules, and
    /// any disagreement conflicts — the shared kernel would otherwise receive contradictory process
    /// configuration. This brings the runtime GlobalSettings.SetCurrentSetting check forward to fail-fast
    /// time and lists every inconsistent field at once.
    /// </remarks>
    /// <param name="conflicts">冲突收集列表 / The conflict sink</param>
    /// <param name="selectedSections">选中 Role 的文件段列表 / The selected roles' file sections</param>
    private static void CollectProcessFieldConflicts(List<ConfigFieldConflict> conflicts, IReadOnlyList<(string ServerType, AppSetting Setting)> selectedSections)
    {
        if (selectedSections.Count < 2)
        {
            return;
        }

        foreach (var property in ProcessLevelProperties)
        {
            var baselineRole = selectedSections[0];
            var baselineValue = GetComparableProcessLevelValue(property, baselineRole.Setting);
            for (var index = 1; index < selectedSections.Count; index++)
            {
                var role = selectedSections[index];
                if (Equals(baselineValue, GetComparableProcessLevelValue(property, role.Setting)))
                {
                    continue;
                }

                // 每个不一致字段一条报告：以首段（优先级序）为基线，值取原始值以便消息贴近配置原文
                conflicts.Add(new ConfigFieldConflict(
                    ConfigConflictKind.ProcessFieldConflict,
                    property.Name,
                    $"file[{baselineRole.ServerType}]",
                    property.GetValue(baselineRole.Setting),
                    $"file[{role.ServerType}]",
                    property.GetValue(role.Setting)));
                break;
            }
        }
    }

    /// <summary>
    /// 取进程级字段的比较值：对 <see cref="GlobalSettings.SetCurrentSetting"/> 启动期会规范化的字段
    /// 应用同一规范化规则，使“规范化后等价”的多段配置不被误报（与运行期一致性判定对齐）。
    /// </summary>
    /// <remarks>
    /// Returns the comparable value of a process-level field: the fields normalized by
    /// GlobalSettings.SetCurrentSetting at startup are normalized with the same rules here,
    /// so sections that are equivalent after normalization are not reported
    /// (keeping parity with the runtime consistency decision).
    /// </remarks>
    /// <param name="property">进程级字段 / The process-level property</param>
    /// <param name="setting">该 Role 的文件段 / The role's file section</param>
    /// <returns>规范化后的比较值 / The normalized comparable value</returns>
    private static object GetComparableProcessLevelValue(PropertyInfo property, AppSetting setting)
    {
        var value = property.GetValue(setting);
        if (value is int intValue)
        {
            // 镜像 GlobalSettings.SetCurrentSetting 的规范化：SaveDataInterval < 5000 时会被改写为
            // GlobalConst.SaveIntervalInMilliSeconds（internal，值为 300_000），此处按同一规则取比较值
            if (property.Name == nameof(AppSetting.SaveDataInterval) && intValue < 5000)
            {
                return 300_000;
            }

            if ((property.Name == nameof(AppSetting.NetWorkSendTimeOutSeconds) || property.Name == nameof(AppSetting.ActorRecycleTime)) && intValue < 1)
            {
                return 5;
            }
        }
        else if (value is string stringValue && property.Name == nameof(AppSetting.HttpUrl) && stringValue.IsNullOrEmptyOrWhiteSpace())
        {
            return "/game/api/";
        }

        return value;
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
    /// CLI-文件冲突检测：CLI 显式提供且与所用文件段值不同的字段即冲突（文件段是单源，CLI 值会被丢弃）。
    /// </summary>
    /// <remarks>
    /// CLI-file conflict detection: a field explicitly supplied on the command line whose value differs from
    /// the file section in use conflicts, because the file section is the single source and the CLI value
    /// would be silently dropped. "Explicitly supplied" is decided from the raw argument tokens — the set of
    /// option keys that actually appeared — so explicitly passing a value equal to the default (e.g.
    /// <c>--HttpPort=0</c>) is still compared and still fails fast on a mismatch.
    /// </remarks>
    /// <param name="conflicts">冲突收集列表 / The conflict sink</param>
    /// <param name="selectedServerTypes">选中的 Role 名集合 / The selected role names</param>
    /// <param name="sectionsByServerType">文件层段索引 / The file-layer section index</param>
    /// <param name="launcherOptions">CLI 解析结果 / The parsed CLI options</param>
    /// <param name="args">原始命令行参数 / The raw command-line arguments</param>
    private static void CollectCliFileConflicts(List<ConfigFieldConflict> conflicts, IReadOnlyList<string> selectedServerTypes, Dictionary<string, AppSetting> sectionsByServerType, StartupOptions launcherOptions, string[] args)
    {
        if (launcherOptions == null)
        {
            return;
        }

        // 显式提供判定：从原始参数提取实际出现的选项键（--key=value / --key value / 布尔标志），
        // 仅对这些字段执行 CLI-文件值比较，避免“显式传等于默认值的值”被默认值比较漏掉
        var explicitOptionNames = CollectExplicitCliOptionNames(args);
        foreach (var serverType in selectedServerTypes)
        {
            if (!sectionsByServerType.TryGetValue(serverType, out var section))
            {
                continue;
            }

            foreach (var property in MergeCheckProperties)
            {
                if (!explicitOptionNames.Contains(property.Name))
                {
                    continue;
                }

                var cliValue = property.GetValue(launcherOptions);
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

    /// <summary>
    /// 从原始命令行参数提取实际出现的选项键集合（大小写不敏感）。
    /// </summary>
    /// <remarks>
    /// Extracts the set of option keys that actually appeared in the raw arguments
    /// (ordinal-ignore-case): the tokens are standardized with the same
    /// <see cref="CommandLineArgumentConverter"/> used by <see cref="OptionsBuilder"/>, so key extraction
    /// matches the parser's interpretation of every supported form (--key=value, --key value, boolean flags).
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
