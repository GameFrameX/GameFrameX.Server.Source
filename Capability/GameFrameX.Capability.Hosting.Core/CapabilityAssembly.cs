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

using System.Reflection;
using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Foundation.Localization.Core;

namespace GameFrameX.Capability.Hosting.Core;

/// <summary>
/// 能力自动装配扫描器（机制层统一扫描约定：部署目录命名约定圈定 <c>{前缀}.*.dll</c> + 反射发现
/// <see cref="ICapabilityModule"/> 实现——机制开放红线的发现面，机制包不含任何能力硬编码清单）。
/// <para>
/// 维护约束：只认非抽象、含公共无参构造的实现类型；同一 Id 多实现 = 装配失败（fail-fast，
/// 不再静默取首）；.NET 懒加载下未被代码触碰的能力包程序集不在 AppDomain 已加载集——
/// 按部署目录命名约定圈定扫描面：新增能力 = 部署产物落地即被发现，宿主零改动。
/// </para>
/// </summary>
public static class CapabilityAssembly
{
    /// <summary>
    /// 扫描指定程序集集合（缺省 = 当前 AppDomain 全部已加载程序集；同 Id 多实现 fail-fast）。
    /// </summary>
    /// <param name="assemblies">程序集集合（null = 全部已加载）。</param>
    /// <returns>发现的能力模块（按程序集名 + 类型名稳定排序；同 Id 多实现抛 <see cref="InvalidOperationException"/>）。</returns>
    public static IReadOnlyList<ICapabilityModule> DiscoverModules(IEnumerable<Assembly> assemblies = null)
    {
        var targetAssemblies = assemblies ?? AppDomain.CurrentDomain.GetAssemblies();
        var moduleTypes = new List<Type>();
        foreach (var assembly in targetAssemblies)
        {
            if (!assembly.IsDynamic)
            {
                CollectModuleTypes(assembly, moduleTypes);
            }
        }

        moduleTypes.Sort((left, right) => string.Compare(LeftKey(left), LeftKey(right), StringComparison.Ordinal));
        var modules = new List<ICapabilityModule>(moduleTypes.Count);
        var modulesById = new Dictionary<string, ICapabilityModule>(StringComparer.Ordinal);
        foreach (var moduleType in moduleTypes)
        {
            var module = (ICapabilityModule)Activator.CreateInstance(moduleType);
            if (modulesById.TryGetValue(module.Manifest.Id, out var existing))
            {
                // Localization: Capability.Hosting.DuplicateCapability - 能力 Id 重复注册。
                throw new InvalidOperationException(string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.DuplicateCapability), module.Manifest.Id + " (" + existing.GetType().Assembly.GetName().Name + " / " + moduleType.Assembly.GetName().Name + ")"));
            }

            modulesById.Add(module.Manifest.Id, module);
            modules.Add(module);
        }

        return modules;
    }

    /// <summary>
    /// 加载部署目录内符合能力包命名约定（<c>{前缀}.*.dll</c>）的全部程序集（扫描数据源）。
    /// </summary>
    /// <param name="prefix">部署程序集前缀（如 <c>GameFrameX.Capability.Online</c>；非空）。</param>
    /// <returns>部署目录内可加载的能力包程序集集合。</returns>
    public static IReadOnlyList<Assembly> LoadDeploymentAssemblies(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            throw new ArgumentException("Deployment assembly prefix must be a non-empty string.", nameof(prefix));
        }

        var assemblies = new List<Assembly>();
        foreach (var path in Directory.GetFiles(AppContext.BaseDirectory, prefix.Trim() + ".*.dll"))
        {
            try
            {
                assemblies.Add(Assembly.Load(AssemblyName.GetAssemblyName(path)));
            }
            catch (Exception)
            {
                // 解析失败的文件（原生库 / 损坏产物）跳过——扫描面只关心能力包程序集。
            }
        }

        return assemblies;
    }

    /// <summary>
    /// 从部署目录按命名约定发现能力模块（加载 + 扫描 + 同 Id fail-fast；组合器的生产数据源）。
    /// </summary>
    /// <param name="prefix">部署程序集前缀（非空）。</param>
    /// <returns>发现的能力模块。</returns>
    public static IReadOnlyList<ICapabilityModule> DiscoverFromDeploymentDirectory(string prefix)
    {
        return DiscoverModules(LoadDeploymentAssemblies(prefix));
    }

    /// <summary>
    /// 收集单个程序集内的模块实现类型。
    /// </summary>
    /// <param name="assembly">程序集。</param>
    /// <param name="moduleTypes">收集目标。</param>
    private static void CollectModuleTypes(Assembly assembly, List<Type> moduleTypes)
    {
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException)
        {
            // 无法完整加载类型的程序集跳过（如仅资源程序集或依赖缺失的动态引用）。
            return;
        }

        foreach (var type in types)
        {
            if (type.IsInterface || type.IsAbstract)
            {
                continue;
            }

            if (!typeof(ICapabilityModule).IsAssignableFrom(type))
            {
                continue;
            }

            if (type.GetConstructor(Type.EmptyTypes) == null)
            {
                continue;
            }

            moduleTypes.Add(type);
        }
    }

    /// <summary>
    /// 类型稳定排序键（程序集名 + 类型名）。
    /// </summary>
    /// <param name="type">类型。</param>
    /// <returns>排序键。</returns>
    private static string LeftKey(Type type)
    {
        return type.Assembly.GetName().Name + "|" + type.FullName;
    }
}
