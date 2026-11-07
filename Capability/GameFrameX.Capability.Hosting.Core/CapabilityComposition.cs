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
using GameFrameX.Foundation.Localization.Core;

namespace GameFrameX.Capability.Hosting.Core;

/// <summary>
/// 能力组合入口（机制层唯一组合面：族骨架 + 部署目录扫描 + 过滤 + 依赖检查 + 定序 + Options 合并 +
/// 逐模块 Register + 产出 <see cref="CapabilityRuntime"/>——「每域手工六件套」降为「每域一个契约接口」后，
/// 编排责任全部收回机制层）。
/// <para>
/// 维护约束（机制开放红线）：机制包零能力硬编码——第二个能力族只需提供 <see cref="ICapabilityFamily"/>
/// 实现，机制零改动；装配问题逐条打日志后抛汇总异常（一次报全）；业务面与管理面至少启用其一；
/// 宿主基础设施模块（<see cref="CapabilityCompositionOptions.AddHostModule"/>）与能力模块进入同一宿主，
/// 由机制统一拓扑定序与启停——组合根唯一，不承认任何静态组合面。
/// </para>
/// </summary>
public static class CapabilityComposition
{
    /// <summary>
    /// 无族组合（宿主基础设施模块 only）的族标识占位：服务覆盖与解析来源标记使用，
    /// 不对应任何部署扫描面或配置段。
    /// </summary>
    internal const string HostOnlyFamilyId = "host";

    /// <summary>
    /// 组合一个能力族（族基建 → 宿主覆盖 → 部署目录扫描 → 同 Id 校验 → 过滤 → 依赖检查 → 定序 →
    /// Options 合并 → Register → 宿主 / 管理面 / 报告）。
    /// </summary>
    /// <param name="family">能力族。</param>
    /// <param name="options">组合选项。</param>
    /// <returns>双面组合产物（未启用的面为 null）。</returns>
    public static CapabilityRuntime Compose(ICapabilityFamily family, CapabilityCompositionOptions options)
    {
        if (family == null)
        {
            throw new ArgumentNullException(nameof(family));
        }

        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (!options.EnableRuntime && options.AdminApiPlaneFactory == null)
        {
            // Localization: Capability.Hosting.Composition.NoPlaneEnabled - 未启用任何能力面。
            throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.Capability.Hosting.Composition.NoPlaneEnabled));
        }

        if (options.HostOptions == null)
        {
            options.HostOptions = new CapabilityHostOptions();
        }

        var hostOptions = options.HostOptions;
        if (!hostOptions.IsValid())
        {
            // Localization: Capability.Hosting.OptionsInvalid - 能力宿主选项非法（作用域三元组 / 调度间隔）。
            throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.Capability.Hosting.OptionsInvalid));
        }

        var services = new CapabilityServices();
        services.SetFamilyCapabilityId(family.FamilyId);
        var eventBus = new CapabilityEventBus(options.AssemblyLog);
        var familyContext = new CapabilityFamilyContext(family.FamilyId, options.FamilyConfigurationSection, eventBus, services);
        family.RegisterInfrastructure(familyContext);

        var overrideEntries = options.OverrideEntries;
        for (var i = 0; i < overrideEntries.Count; i++)
        {
            var entry = overrideEntries[i];
            services.AddOverride(new CapabilityServiceRegistration(entry.ServiceType, string.Empty, entry.Factory, family.FamilyId, true));
        }

        var builder = new CapabilityHostBuilder(services, eventBus, family, options);
        CapabilityHost host;
        try
        {
            var discoveredModules = CapabilityAssembly.DiscoverFromDeploymentDirectory(family.DeploymentAssemblyPrefix);
            host = builder.Build(MergeHostModules(discoveredModules, options));
        }
        catch (CapabilityAssemblyException exception)
        {
            // 部署目录发现期与装配期的汇总问题统一走这里：抛出前逐条打 Error（一次报全）。
            CapabilityAssemblyLogRenderer.RenderFailures(exception.Problems, options.AssemblyLog);
            throw;
        }

        CapabilityAssemblyLogRenderer.RenderSuccess(host.Report, options.AssemblyLog);

        ICapabilityAdminApiPlane adminApiPlane = null;
        if (options.AdminApiPlaneFactory != null)
        {
            try
            {
                adminApiPlane = options.AdminApiPlaneFactory(host);
            }
            catch (Exception)
            {
                // 管理面组装失败 = 组合失败：宿主不会交给调用方，Register 期钩子声明必须就地释放。
                host.ReleaseHookDeclarations();
                throw;
            }
        }

        if (!options.EnableRuntime)
        {
            // 仅管理面形态：业务宿主不对外交付、永不启停，钩子声明无人消费，立即释放进程级槽位。
            host.ReleaseHookDeclarations();
        }

        return new CapabilityRuntime(options.EnableRuntime ? host : null, adminApiPlane, host.Report);
    }

    /// <summary>
    /// 无族组合（宿主基础设施模块 only）：不执行族基建注册、部署目录扫描、管理面组装与成功报告渲染，
    /// 仅把 <see cref="CapabilityCompositionOptions.HostModules"/> 组装为业务宿主——供未启用任何能力族的
    /// 进程 / Role 承载宿主侧固定启停序列（组合根仍唯一收敛于机制）。
    /// <para>
    /// 维护约束：至少声明一个宿主模块；<c>EnableRuntime</c> 必须为 true（无族形态无管理面可退）；
    /// 管理面工厂不得设置（管理面属族组合产物，需与 <see cref="ICapabilityFamily"/> 同用）。
    /// </para>
    /// </summary>
    /// <param name="options">组合选项（须含至少一个宿主模块）。</param>
    /// <returns>业务宿主组合产物（管理面为 null）。</returns>
    public static CapabilityRuntime Compose(CapabilityCompositionOptions options)
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (options.HostModules.Count == 0)
        {
            // Localization: Capability.Hosting.Composition.HostModulesRequired - 无族组合缺少宿主模块。
            throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.Capability.Hosting.Composition.HostModulesRequired));
        }

        if (options.AdminApiPlaneFactory != null)
        {
            // Localization: Capability.Hosting.Composition.AdminApiPlaneRequiresFamily - 管理面工厂需与能力族同用。
            throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.Capability.Hosting.Composition.AdminApiPlaneRequiresFamily));
        }

        if (!options.EnableRuntime)
        {
            // Localization: Capability.Hosting.Composition.NoPlaneEnabled - 未启用任何能力面。
            throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.Capability.Hosting.Composition.NoPlaneEnabled));
        }

        if (options.HostOptions == null)
        {
            options.HostOptions = new CapabilityHostOptions();
        }

        if (!options.HostOptions.IsValid())
        {
            // Localization: Capability.Hosting.OptionsInvalid - 能力宿主选项非法（作用域三元组 / 调度间隔）。
            throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.Capability.Hosting.OptionsInvalid));
        }

        var services = new CapabilityServices();
        services.SetFamilyCapabilityId(HostOnlyFamilyId);
        var eventBus = new CapabilityEventBus(options.AssemblyLog);

        var overrideEntries = options.OverrideEntries;
        for (var i = 0; i < overrideEntries.Count; i++)
        {
            var entry = overrideEntries[i];
            services.AddOverride(new CapabilityServiceRegistration(entry.ServiceType, string.Empty, entry.Factory, HostOnlyFamilyId, true));
        }

        var builder = new CapabilityHostBuilder(services, eventBus, new HostOnlyCapabilityFamily(), options);
        var hostModules = new List<ICapabilityModule>(options.HostModules.Count);
        for (var i = 0; i < options.HostModules.Count; i++)
        {
            hostModules.Add(options.HostModules[i].Module);
        }

        var host = builder.Build(hostModules);
        return new CapabilityRuntime(host, null, host.Report);
    }

    /// <summary>
    /// 合并宿主基础设施模块到发现序模块表（宿主模块追加于尾部；同 Id 查重在装配表构建时统一执行）。
    /// </summary>
    /// <param name="discoveredModules">部署目录发现的能力模块。</param>
    /// <param name="options">组合选项（宿主模块来源）。</param>
    /// <returns>合并后的模块表。</returns>
    private static IReadOnlyList<ICapabilityModule> MergeHostModules(IReadOnlyList<ICapabilityModule> discoveredModules, CapabilityCompositionOptions options)
    {
        if (options.HostModules.Count == 0)
        {
            return discoveredModules;
        }

        var merged = new List<ICapabilityModule>(discoveredModules.Count + options.HostModules.Count);
        for (var i = 0; i < discoveredModules.Count; i++)
        {
            merged.Add(discoveredModules[i]);
        }

        for (var i = 0; i < options.HostModules.Count; i++)
        {
            merged.Add(options.HostModules[i].Module);
        }

        return merged;
    }

    /// <summary>
    /// 无族组合的占位族（族标识 / 配置段名 / 部署前缀均为宿主占位语义；不注册任何族基建）。
    /// </summary>
    private sealed class HostOnlyCapabilityFamily : ICapabilityFamily
    {
        /// <summary>获取族标识（宿主占位）。</summary>
        public string FamilyId
        {
            get { return HostOnlyFamilyId; }
        }

        /// <summary>获取配置段名（宿主占位；错误路径前缀不出现）。</summary>
        public string ConfigurationSectionName
        {
            get { return "Host"; }
        }

        /// <summary>获取部署程序集前缀（占位值；无族组合不执行部署扫描）。</summary>
        public string DeploymentAssemblyPrefix
        {
            get { return "GameFrameX.Host"; }
        }

        /// <summary>无族基建可注册（空实现）。</summary>
        /// <param name="context">族基建注册面（不使用）。</param>
        public void RegisterInfrastructure(CapabilityFamilyContext context)
        {
        }
    }
}
