// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
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
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Hosting.Core;
using GameFrameX.Foundation.Localization.Core;

namespace GameFrameX.Capability.Hosting.AdminApi;

/// <summary>
/// 管理面组装工厂（自组合根上移进机制：遍历启用模块的 <see cref="IAdminActionContributor"/> 注册 action、
/// 双向对账、装配适配器与管理面服务；业务面与 admin-only 形态共用同一次组合结果）。
/// <para>
/// 维护约束：action 名跨能力全宿主唯一（同名注册 fail-fast）；对账双向——manifest 声明 ⊆ 实注册 且
/// 实注册 ⊆ manifest 声明（多报漏报均装配失败，一次报全）；作用域 / 幂等 / 错误码适配器经宿主共享服务面
/// 解析（族基建注册；缺失即装配失败，幂等适配器可空）。
/// </para>
/// </summary>
public static class AdminApiComposition
{
    /// <summary>
    /// 启用管理面（Hosting.AdminApi 包的组合接线扩展：设置管理面工厂；机制组合时构建管理面）。
    /// </summary>
    /// <param name="options">组合选项。</param>
    /// <param name="configure">管理面监听选项改写回调（可空 = 全默认）。</param>
    /// <returns>组合选项（链式）。</returns>
    public static CapabilityCompositionOptions UseAdminApi(this CapabilityCompositionOptions options, Action<AdminApiOptions> configure = null)
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        var adminApiOptions = new AdminApiOptions();
        configure?.Invoke(adminApiOptions);
        options.SetAdminApiPlaneFactory(host => CreateAdminApiPlane(host, adminApiOptions));
        return options;
    }

    /// <summary>
    /// 组装管理面（遍历模块贡献 action → 双向对账 → 解析适配器 → 调度器 + HTTP 服务）。
    /// </summary>
    /// <param name="host">已装配能力宿主。</param>
    /// <param name="options">管理面监听选项。</param>
    /// <returns>管理面（宿主启停编排承载）。</returns>
    public static ICapabilityAdminApiPlane CreateAdminApiPlane(CapabilityHost host, AdminApiOptions options)
    {
        if (host == null)
        {
            throw new ArgumentNullException(nameof(host));
        }

        if (options == null || !options.IsValid())
        {
            // Localization: Capability.Hosting.AdminApi.AdminApiOptionsInvalid - 管理面监听选项非法。
            throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.Capability.Hosting.AdminApi.AdminApiOptionsInvalid));
        }

        var registry = new AdminActionRegistry();
        foreach (var module in host.Modules)
        {
            if (module is IAdminActionContributor contributor)
            {
                registry.SetCurrentOwnerCapabilityId(module.Manifest.Id);
                contributor.RegisterAdminActions(registry);
            }
        }

        registry.SetCurrentOwnerCapabilityId(null);
        ReconcileDeclaredActions(registry, host.Report);

        var errorCodes = ResolveRequiredService<AdminApiErrorCodes>(host);
        var scopeAdapter = ResolveRequiredService<IAdminApiScopeAdapter>(host);
        var idempotencyAdapter = host.Services.GetOptionalService<IAdminApiIdempotencyAdapter>();
        var dispatcher = new AdminApiDispatcher(
            registry,
            errorCodes,
            scopeAdapter,
            new AdminApiClaimedScope(host.TenantId, host.AppId, host.ServerId),
            idempotencyAdapter,
            host.Report,
            host.Services);
        return new AdminApiServer(options, dispatcher);
    }

    /// <summary>
    /// 解析族基建必须提供的管理面服务（缺失即装配失败）。
    /// </summary>
    /// <typeparam name="TService">服务类型键。</typeparam>
    /// <param name="host">能力宿主。</param>
    /// <returns>服务实例。</returns>
    private static TService ResolveRequiredService<TService>(CapabilityHost host) where TService : class
    {
        var service = host.Services.GetOptionalService<TService>();
        if (service == null)
        {
            // Localization: Capability.Hosting.AdminApi.AdapterMissing - 管理面缺少必需适配器服务。
            throw new InvalidOperationException(string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.AdminApi.AdapterMissing), typeof(TService).FullName));
        }

        return service;
    }

    /// <summary>
    /// 组合期双向对账（按能力分组：每个启用能力的 manifest 声明集 == 该能力名下的实注册集；问题一次报全后 fail-fast）。
    /// <para>
    /// 维护约束：归属以注册时的贡献者能力（<see cref="AdminActionRegistration.OwnerCapabilityId"/>）为准，
    /// 不能用全宿主汇总集合比对——否则「A 声明却忘注册、B 注册却未声明」的跨能力冒名会互相抵消、
    /// 把调用路由到错误的域处理器。元端点在对账之后注册，不参与对账。
    /// </para>
    /// </summary>
    /// <param name="registry">action 注册表（模块贡献完毕）。</param>
    /// <param name="report">能力报告。</param>
    private static void ReconcileDeclaredActions(AdminActionRegistry registry, CapabilityReport report)
    {
        var problems = new List<string>();
        var declaredActionsByCapability = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var allDeclaredActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in report.Entries)
        {
            CollectDeclaredActions(entry, registry, declaredActionsByCapability, allDeclaredActions, problems);
        }

        ReportUndeclaredRegisteredActions(registry, declaredActionsByCapability, allDeclaredActions, problems);

        if (problems.Count > 0)
        {
            // Localization: Capability.Hosting.AdminApi.ReconcileFailed - Admin action 对账失败。
            var message = string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.AdminApi.ReconcileFailed), problems.Count) + Environment.NewLine + string.Join(Environment.NewLine, problems);
            throw new InvalidOperationException(message);
        }
    }

    /// <summary>
    /// 声明侧对账（收集启用能力的 manifest 声明集，并校验「声明未注册 / 被其它能力注册」两类问题）。
    /// </summary>
    /// <param name="entry">能力报告条目。</param>
    /// <param name="registry">action 注册表（模块贡献完毕）。</param>
    /// <param name="declaredActionsByCapability">按能力收集的声明集（Ordinal 键）。</param>
    /// <param name="allDeclaredActions">全宿主声明全集（OrdinalIgnoreCase）。</param>
    /// <param name="problems">对账问题累积列表。</param>
    private static void CollectDeclaredActions(CapabilityReportEntry entry, AdminActionRegistry registry, Dictionary<string, HashSet<string>> declaredActionsByCapability, HashSet<string> allDeclaredActions, List<string> problems)
    {
        if (entry.State != CapabilityFilterState.Enabled)
        {
            return;
        }

        var declaredActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        declaredActionsByCapability[entry.Manifest.Id] = declaredActions;
        foreach (var action in entry.Manifest.Actions)
        {
            declaredActions.Add(action);
            allDeclaredActions.Add(action);
            var registration = registry.Find(action);
            if (registration == null)
            {
                // Localization: Capability.Hosting.AdminApi.ActionNotRegistered - 能力声明的 action 未注册。
                problems.Add(string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.AdminApi.ActionNotRegistered), action, entry.Manifest.Id));
            }
            else if (!string.Equals(registration.OwnerCapabilityId, entry.Manifest.Id, StringComparison.Ordinal))
            {
                // Localization: Capability.Hosting.AdminApi.ActionOwnerMismatch - 能力声明的 action 被其它能力注册（跨能力冒名）。
                problems.Add(string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.AdminApi.ActionOwnerMismatch), action, entry.Manifest.Id, registration.OwnerCapabilityId));
            }
        }
    }

    /// <summary>
    /// 注册侧对账（校验「能力注册的 action 未在其 manifest 声明」；其它能力已声明的情形在声明侧按冒名报出，不重复报）。
    /// </summary>
    /// <param name="registry">action 注册表（模块贡献完毕）。</param>
    /// <param name="declaredActionsByCapability">按能力收集的声明集（Ordinal 键）。</param>
    /// <param name="allDeclaredActions">全宿主声明全集（OrdinalIgnoreCase）。</param>
    /// <param name="problems">对账问题累积列表。</param>
    private static void ReportUndeclaredRegisteredActions(AdminActionRegistry registry, Dictionary<string, HashSet<string>> declaredActionsByCapability, HashSet<string> allDeclaredActions, List<string> problems)
    {
        foreach (var registeredAction in registry.ActionNames)
        {
            var ownerCapabilityId = registry.Find(registeredAction).OwnerCapabilityId;
            if (declaredActionsByCapability.TryGetValue(ownerCapabilityId, out var ownerDeclaredActions) && ownerDeclaredActions.Contains(registeredAction))
            {
                continue;
            }

            if (allDeclaredActions.Contains(registeredAction))
            {
                // 其它能力声明了该 action：已在声明侧按「跨能力冒名」报出，不重复报。
                continue;
            }

            // Localization: Capability.Hosting.AdminApi.ActionNotDeclared - 能力注册的 action 未在其 manifest 声明。
            problems.Add(string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.AdminApi.ActionNotDeclared), registeredAction, ownerCapabilityId));
        }
    }
}
