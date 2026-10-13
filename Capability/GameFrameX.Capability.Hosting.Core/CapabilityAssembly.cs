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
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.Reflection;
using GameFrameX.Capability.Hosting.Abstractions;

namespace GameFrameX.Capability.Hosting.Core;

/// <summary>
/// 能力自动装配扫描器（组合根反射扫描已加载程序集发现 <see cref="ICapabilityModule"/> 实现；
/// 机制开放红线的发现面——机制包不含任何能力硬编码清单）。
/// <para>
/// 维护约束：只认非抽象、含公共无参构造的实现类型；同 Id 多实现取首个发现（按程序集名 + 类型名稳定排序），
/// 其余忽略——扫描序不构成装配序（装配序由 Requires 拓扑 + 加入序决定）。
/// </para>
/// </summary>
public static class CapabilityAssembly
{
    /// <summary>
    /// 扫描指定程序集集合（缺省 = 当前 AppDomain 全部已加载程序集）。
    /// </summary>
    /// <param name="assemblies">程序集集合（null = 全部已加载）。</param>
    /// <returns>发现的能力模块（按程序集名 + 类型名稳定排序）。</returns>
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
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var moduleType in moduleTypes)
        {
            var module = (ICapabilityModule)Activator.CreateInstance(moduleType);
            if (seenIds.Add(module.Manifest.Id))
            {
                modules.Add(module);
            }
        }

        return modules;
    }

    /// <summary>
    /// 扫描并装配（发现 → 过滤器 → 构建；组合根一行装配形态）。
    /// </summary>
    /// <param name="options">宿主选项。</param>
    /// <param name="filter">装配过滤器（null = 全部启用）。</param>
    /// <returns>能力宿主。</returns>
    public static CapabilityHost BuildHost(CapabilityHostOptions options, CapabilityAssemblyFilter filter = null)
    {
        var builder = new CapabilityHostBuilder();
        foreach (var module in DiscoverModules())
        {
            builder.AddModule(module);
        }

        if (filter != null)
        {
            builder.SetFilter(filter);
        }

        return builder.Build(options);
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
