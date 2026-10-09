// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and applicable international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or to infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
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


using System.Collections.Concurrent;
using System.Collections.Generic;
using GameFrameX.DataBase.Abstractions;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Localization;
using GameFrameX.Utility.Setting;

namespace GameFrameX.DataBase;

/// <summary>
/// 数据库提供者约定的反射解析器：按 <see cref="DatabaseProviderType"/> 经显式映射表创建实现实例（C177）。
/// </summary>
/// <remarks>
/// FreeSql-style convention resolution: the core project references no provider project, and the
/// enum-to-assembly-qualified-type-name table below is the single string-coupling point of the whole
/// repository. Renaming a provider project/class/namespace therefore breaks here (and in the guard
/// test <c>DbProviderResolverTests</c>) instead of scattering composition-root ternaries.
/// internal on purpose: the resolution hook is an implementation detail of the non-generic
/// <c>GameDb.Init(DatabaseProviderType, string, DbOptions)</c> overload; hosts wanting an explicit
/// type keep using the generic <c>Init&lt;T&gt;</c> escape hatch.
/// </remarks>
internal static class DbProviderResolver
{
    /// <summary>
    /// 枚举值到程序集限定类型名的显式映射表——全仓唯一字符串耦合点（不拼串，枚举改名不会静默产出错误字符串）。
    /// </summary>
    /// <remarks>
    /// Explicit enum-to-assembly-qualified-type-name mapping. A new <see cref="DatabaseProviderType"/>
    /// member without an entry here fails fast with Database.Provider.NotSupported during Init; the
    /// guard test walks every enum member so such an omission turns red in CI.
    /// </remarks>
    private static readonly IReadOnlyDictionary<DatabaseProviderType, string> ProviderTypeNames = new Dictionary<DatabaseProviderType, string>
    {
        [DatabaseProviderType.Mongo] = "GameFrameX.DataBase.Mongo.MongoDbService, GameFrameX.DataBase.Mongo",
        [DatabaseProviderType.PostgreSql] = "GameFrameX.DataBase.PostgreSql.PostgreSqlDbService, GameFrameX.DataBase.PostgreSql",
    };

    /// <summary>
    /// 已解析类型的按枚举缓存：每进程每枚举只反射一次（解析失败缓存 null——程序集不会中途出现，且调用方 fail-fast）。
    /// </summary>
    /// <remarks>
    /// Per-provider cache of the resolved <see cref="Type"/> so reflection runs at most once per
    /// provider per process. Unresolvable results are cached as null: the assembly cannot appear
    /// later, and the caller aborts startup anyway.
    /// </remarks>
    private static readonly ConcurrentDictionary<DatabaseProviderType, Type> ResolvedTypes = new();

    /// <summary>
    /// 按提供者枚举创建数据库实现实例（Init 在进程启动第一段调用，解析失败立即抛出，保留 fail-fast 语义）。
    /// </summary>
    /// <remarks>
    /// Creates the provider implementation for the given enum member. Three failure branches:
    /// enum member without a mapping (Database.Provider.NotSupported, lists the supported members),
    /// host process not referencing the provider project (Database.Provider.NotInstalled, FreeSql-style
    /// self-diagnosis naming the expected assembly-qualified type and the generic <c>Init&lt;T&gt;</c>
    /// escape hatch), and instantiation/cast failure (localized self-diagnosis carrying the type name).
    /// </remarks>
    /// <param name="provider">提供者枚举值 / Provider enum member</param>
    /// <exception cref="InvalidOperationException">枚举未配映射、宿主缺 Provider 工程引用或实例化失败时抛出 / Thrown when the enum is unmapped, the provider assembly is not referenced by the host, or instantiation fails</exception>
    /// <returns>新创建的数据库实现实例 / A newly created database service instance</returns>
    internal static IDatabaseService Create(DatabaseProviderType provider)
    {
        if (!ProviderTypeNames.TryGetValue(provider, out var typeName))
        {
            // Localization: Database.Provider.NotSupported - 数据库提供者枚举值“{0}”未配置解析映射。已支持：[{1}]。
            throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.Database.ProviderNotSupported, provider.ToString(), string.Join(", ", ProviderTypeNames.Keys)));
        }

        var type = ResolvedTypes.GetOrAdd(provider, static (_, name) => Type.GetType(name, throwOnError: false), typeName);
        if (type == null)
        {
            // Localization: Database.Provider.NotInstalled - 无法解析数据库提供者“{0}”的实现类型“{1}”（含工程引用与 Init<T> 逃生门指引）。
            throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.Database.ProviderNotInstalled, provider.ToString(), typeName));
        }

        return Instantiate(type);
    }

    /// <summary>
    /// 按程序集限定类型名直接解析并实例化（internal 测试钩子：绕过枚举映射表注入坏字符串，验证 NotInstalled 分支）。
    /// </summary>
    /// <remarks>
    /// Test hook resolving an arbitrary assembly-qualified type name so the guard test can drive the
    /// Database.Provider.NotInstalled branch with an injected bad string without touching the real
    /// mapping table. Non-test callers use <see cref="Create(DatabaseProviderType)"/>.
    /// </remarks>
    /// <param name="typeName">程序集限定类型名 / Assembly-qualified type name</param>
    /// <exception cref="InvalidOperationException">类型不可解析或实例化失败时抛出 / Thrown when the type cannot be resolved or instantiated</exception>
    /// <returns>新创建的数据库实现实例 / A newly created database service instance</returns>
    internal static IDatabaseService CreateFromTypeName(string typeName)
    {
        var type = Type.GetType(typeName, throwOnError: false);
        if (type == null)
        {
            // Localization: Database.Provider.NotInstalled - 无法解析数据库提供者“{0}”的实现类型“{1}”（含工程引用与 Init<T> 逃生门指引）。
            throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.Database.ProviderNotInstalled, "<test-injected>", typeName));
        }

        return Instantiate(type);
    }

    /// <summary>
    /// 实例化已解析类型并校验 <see cref="IDatabaseService"/> 契约（实现类依赖隐式无参构造，与泛型 Init&lt;T&gt; 的 new() 约束同源）。
    /// </summary>
    /// <remarks>
    /// Instantiates the resolved type and validates the <see cref="IDatabaseService"/> contract.
    /// Any instantiation failure (e.g. a missing public parameterless constructor) and any
    /// non-IDatabaseService result surface as localized InvalidOperationExceptions carrying
    /// the assembly-qualified type name for diagnosis.
    /// </remarks>
    /// <param name="type">已解析的实现类型 / The resolved implementation type</param>
    /// <exception cref="InvalidOperationException">实例化失败或结果未实现 <see cref="IDatabaseService"/> 时抛出 / Thrown when instantiation fails or the result does not implement <see cref="IDatabaseService"/></exception>
    /// <returns>新创建的数据库实现实例 / A newly created database service instance</returns>
    private static IDatabaseService Instantiate(Type type)
    {
        object instance;
        try
        {
            instance = Activator.CreateInstance(type);
        }
        catch (Exception exception)
        {
            // Localization: Database.Provider.ActivationFailed - 创建数据库提供者实例失败：类型“{0}”（原因：{1}）。
            throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.Database.ProviderActivationFailed, type.AssemblyQualifiedName, exception.Message), exception);
        }

        if (instance is not IDatabaseService service)
        {
            // Localization: Database.Provider.NotDatabaseService - 数据库提供者类型“{0}”未实现 IDatabaseService 接口。
            throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.Database.ProviderNotDatabaseService, type.AssemblyQualifiedName));
        }

        return service;
    }
}
