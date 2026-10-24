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
/// 实现，机制零改动；装配问题逐条打日志后抛汇总异常（一次报全）；业务面与管理面至少启用其一。
/// </para>
/// </summary>
public static class CapabilityComposition
{
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

        var modules = CapabilityAssembly.DiscoverFromDeploymentDirectory(family.DeploymentAssemblyPrefix);

        var builder = new CapabilityHostBuilder(services, eventBus, family, options);
        CapabilityHost host;
        try
        {
            host = builder.Build(modules);
        }
        catch (CapabilityAssemblyException exception)
        {
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
}
