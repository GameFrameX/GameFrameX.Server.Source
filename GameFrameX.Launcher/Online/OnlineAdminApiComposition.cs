// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the PRC and relevant international regulations.
//   使用本项目须严格遵守相关法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file at the root of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development,
//   本组织与贡献者概不承担任何责任。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://github.com/GameFrameX
//   CNB  仓库：https://www.cnb.cool/GameFrameX
//   CNB Repository: https://www.cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Hosting.Core;
using GameFrameX.Capability.Hosting.AdminApi;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Idempotency;
using GameFrameX.Capability.Online.Abstractions.Scope;
using GameFrameX.Foundation.Localization.Core;

namespace GameFrameX.Launcher.Online;

/// <summary>
/// Online 管理面组合工厂（注册反转的组合点：能力模块贡献 action → 通用管道 + Online 适配器组装；
/// 业务与管理面可独立启停——admin-only 形态按能力目录全部「未装配」裁决）。
/// <para>
/// 维护约束：C229 起 Launcher 经 <c>AddOnlineCapabilities()</c> 单入口装配，本工厂由入口内部调用；
/// C232 P6 随 Runtime 壳退役迁入 Launcher——业务形态签名改为机制 <see cref="CapabilityHost"/>（作用域 /
/// 幂等经宿主共享服务面解析），admin-only 目录由 <see cref="CapabilityAssembly.DiscoverModules"/> 扫描驱动
/// （手工 Catalog 随壳退役；manifest 与来源程序集等价）。
/// 组合期对账 fail-fast——启用能力的 manifest 声明 action 必须全部实际注册。
/// </para>
/// </summary>
/// <remarks>
/// The Online admin-plane composition factory (the registration-inversion composition point:
/// capability modules contribute actions, the generic pipeline plus Online adapters assemble around
/// them; business and admin planes can start and stop independently — the admin-only shape rules
/// every catalog capability as not installed). Since C229 the Launcher assembles through the
/// <c>AddOnlineCapabilities()</c> single entry which invokes this factory; C232 P6 relocates it into
/// the Launcher along with the Runtime-shell retirement — the full-capability signature now takes the
/// mechanism <see cref="CapabilityHost"/> (scope / idempotency resolve through the host's shared
/// service face), and the admin-only catalog is driven by <see cref="CapabilityAssembly.DiscoverModules"/>
/// scanning (the hand-written catalog retired with the shell; manifests and source assemblies
/// equivalent). Composition-time reconciliation fails fast — every action declared by an enabled
/// manifest must actually be registered.
/// </remarks>
public static class OnlineAdminApiComposition
{
    /// <summary>
    /// 组装管理面调度器（业务运行时形态：全部能力经模块注册，报告取自宿主）。
    /// </summary>
    /// <param name="host">业务能力宿主（已装配；作用域与幂等经共享服务面解析）。</param>
    /// <returns>通用管理面调度器。</returns>
    public static AdminApiDispatcher CreateDispatcher(CapabilityHost host)
    {
        if (host == null)
        {
            throw new ArgumentNullException(nameof(host));
        }

        var registry = new AdminActionRegistry();
        foreach (var module in host.Modules)
        {
            if (module is IAdminActionContributor contributor)
            {
                contributor.RegisterAdminActions(registry);
            }
        }

        ReconcileDeclaredActions(registry, host.Report);

        return new AdminApiDispatcher(
            registry,
            OnlineAdminApiAdapters.ErrorCodes,
            OnlineAdminApiAdapters.CreateScopeAdapter(host.Services.GetService<OnlineScope>()),
            new AdminApiClaimedScope(host.TenantId, host.AppId, host.ServerId),
            OnlineAdminApiAdapters.CreateIdempotencyAdapter(host.Services.GetService<OnlineIdempotencyService>()),
            host.Report);
    }

    /// <summary>
    /// 组装管理面调度器（admin-only 形态：零能力装配，action 一律按未安装裁决）。
    /// </summary>
    /// <param name="scopeOptions">授权作用域选项（必须合法，否则抛 <see cref="InvalidOperationException"/>）。</param>
    /// <param name="filter">能力装配过滤器（null = 目录全部 NotAssembled；传入时排除/白名单外条目如实反映配置排除，其余仍为未装配，C229）。</param>
    /// <returns>通用管理面调度器。</returns>
    public static AdminApiDispatcher CreateAdminOnlyDispatcher(CapabilityHostOptions scopeOptions, CapabilityAssemblyFilter filter = null)
    {
        if (scopeOptions == null)
        {
            throw new ArgumentNullException(nameof(scopeOptions));
        }

        if (!scopeOptions.IsValid())
        {
            // Localization: Capability.Hosting.OptionsInvalid - 能力宿主选项非法（作用域三元组 / 调度间隔）。
            throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.Capability.Hosting.OptionsInvalid));
        }

        var entries = CapabilityAssembly.DiscoverModules(OnlineCapabilityComposition.LoadCapabilityAssemblies())
            .Select(module => new CapabilityReportEntry(module.Manifest, module.GetType().Assembly.GetName().Name, MapCatalogState(filter, module.Manifest)))
            .ToArray();
        var report = new CapabilityReport(entries, Array.Empty<string>());
        return new AdminApiDispatcher(
            new AdminActionRegistry(),
            OnlineAdminApiAdapters.ErrorCodes,
            OnlineAdminApiAdapters.CreateScopeAdapter(new OnlineScope(scopeOptions.TenantId, scopeOptions.AppId, scopeOptions.ServerId)),
            new AdminApiClaimedScope(scopeOptions.TenantId, scopeOptions.AppId, scopeOptions.ServerId),
            null,
            report);
    }

    /// <summary>
    /// 组装管理面 HTTP 服务（业务运行时形态）。
    /// </summary>
    /// <param name="host">业务能力宿主。</param>
    /// <param name="options">管理面监听选项。</param>
    /// <returns>管理面 HTTP 服务。</returns>
    public static AdminApiServer CreateServer(CapabilityHost host, AdminApiOptions options)
    {
        ValidateAdminOptions(options);
        return new AdminApiServer(options, CreateDispatcher(host));
    }

    /// <summary>
    /// 组装管理面 HTTP 服务（admin-only 形态）。
    /// </summary>
    /// <param name="scopeOptions">授权作用域选项。</param>
    /// <param name="options">管理面监听选项。</param>
    /// <param name="filter">能力装配过滤器（null = 目录全部 NotAssembled；传入时配置排除/白名单外条目如实反映，其余仍为未装配，C229）。</param>
    /// <returns>管理面 HTTP 服务。</returns>
    public static AdminApiServer CreateAdminOnlyServer(CapabilityHostOptions scopeOptions, AdminApiOptions options, CapabilityAssemblyFilter filter = null)
    {
        ValidateAdminOptions(options);
        return new AdminApiServer(options, CreateAdminOnlyDispatcher(scopeOptions, filter));
    }

    /// <summary>
    /// 校验管理面监听选项（端口范围与前缀；fail-fast，等价拆分前宿主选项校验的管理面半边）。
    /// </summary>
    /// <param name="options">管理面监听选项。</param>
    private static void ValidateAdminOptions(AdminApiOptions options)
    {
        if (options == null || !options.IsValid())
        {
            // Localization: Capability.Hosting.AdminApi.OptionsInvalid - 管理面监听选项非法（端口 / 前缀）。
            throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.Capability.Hosting.AdminApi.AdminApiOptionsInvalid));
        }
    }

    /// <summary>
    /// admin-only 目录状态映射：过滤器判定为排除/白名单外的条目如实反映配置排除；
    /// 未被排除的条目仍为未装配（本进程未启动业务运行时，C229）。
    /// </summary>
    /// <param name="filter">能力装配过滤器（可为 null）。</param>
    /// <param name="manifest">目录 manifest。</param>
    /// <returns>报告条目状态。</returns>
    private static CapabilityFilterState MapCatalogState(CapabilityAssemblyFilter filter, CapabilityManifest manifest)
    {
        var matched = filter?.Match(manifest) ?? CapabilityFilterState.NotAssembled;
        return matched switch
        {
            CapabilityFilterState.ExcludedByDisableList => CapabilityFilterState.ExcludedByDisableList,
            CapabilityFilterState.NotInEnableList => CapabilityFilterState.NotInEnableList,
            _ => CapabilityFilterState.NotAssembled,
        };
    }


    /// <summary>
    /// 组合期对账（声明 ⊆ 实注册；fail-fast）。
    /// </summary>
    /// <param name="registry">action 注册表。</param>
    /// <param name="report">能力报告。</param>
    private static void ReconcileDeclaredActions(AdminActionRegistry registry, CapabilityReport report)
    {
        foreach (var entry in report.Entries)
        {
            if (entry.State != CapabilityFilterState.Enabled)
            {
                continue;
            }

            foreach (var action in entry.Manifest.Actions)
            {
                if (registry.Find(action) == null)
                {
                    // Localization: Capability.Hosting.AdminApi.ActionNotRegistered - 能力声明的 action 未注册。
                    throw new InvalidOperationException(string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.AdminApi.ActionNotRegistered), action, entry.Manifest.Id));
                }
            }
        }
    }
}
