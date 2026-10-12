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
//   禁止利用本项目实施危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or to infringe the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB 仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Localization;
using GameFrameX.Utility.Setting;

namespace GameFrameX.Discovery;

/// <summary>
/// 发现层提供者约定的反射解析器：按 <see cref="DiscoveryActivationOptions.Provider"/> 经显式映射表激活
/// <c>*DiscoveryRuntime</c>（C213，镜像 C177 <c>DbProviderResolver</c> 模板）。
/// </summary>
/// <remarks>
/// FreeSql-style convention resolution mirroring the C177 <c>DbProviderResolver</c> template: the
/// discovery assembly references no provider project, and the enum-to-assembly-qualified-type-name
/// table below is the single string-coupling point of the whole discovery layer. Renaming a provider
/// project/class/namespace therefore breaks here (and in the guard test
/// <c>DiscoveryRuntimeResolverTests</c>) instead of scattering composition-root ternaries. Unlike
/// the C177 resolver (internal, wrapped behind the non-generic <c>GameDb.Init(DbOptions)</c>
/// overload), this one is public on purpose: the host composition root calls it directly, so there
/// is no facade to hide behind. Hosts wanting an explicit runtime keep the direct
/// <c>*DiscoveryRuntime.Activate</c> escape hatch (which ignores <c>Provider</c>).
/// </remarks>
public static class DiscoveryRuntimeResolver
{
    /// <summary>
    /// 枚举值到程序集限定类型名的显式映射表——全仓发现层唯一字符串耦合点（不拼串，枚举改名不会静默产出错误字符串）。
    /// </summary>
    /// <remarks>
    /// Explicit enum-to-assembly-qualified-type-name mapping. A new <see cref="DatabaseProviderType"/>
    /// member without an entry here fails fast with Database.Discovery.ProviderNotSupported during
    /// activation; the guard test walks every enum member so such an omission turns red in CI.
    /// </remarks>
    private static readonly IReadOnlyDictionary<DatabaseProviderType, string> ProviderTypeNames = new Dictionary<DatabaseProviderType, string>
    {
        [DatabaseProviderType.Mongo] = "GameFrameX.DataBase.Mongo.Discovery.MongoDiscoveryRuntime, GameFrameX.DataBase.Mongo",
        [DatabaseProviderType.PostgreSql] = "GameFrameX.DataBase.PostgreSql.Discovery.PostgreSqlDiscoveryRuntime, GameFrameX.DataBase.PostgreSql",
    };

    /// <summary>
    /// 已解析激活方法的按枚举缓存：每进程每枚举只反射一次（解析失败缓存 null——程序集不会中途出现，且调用方 fail-fast）。
    /// </summary>
    /// <remarks>
    /// Per-provider cache of the resolved <c>Activate</c> <see cref="MethodInfo"/> so reflection runs
    /// at most once per provider per process. Unresolvable results (missing assembly or missing
    /// entry-point signature) are cached as null: the assembly cannot appear later, and the caller
    /// aborts startup anyway.
    /// </remarks>
    private static readonly ConcurrentDictionary<DatabaseProviderType, MethodInfo> ResolvedActivators = new();

    /// <summary>
    /// 按约定激活发现层 Runtime（读 <paramref name="options"/> 的 Provider 成员分派，组合根唯一激活入口）。
    /// </summary>
    /// <remarks>
    /// Activates the discovery runtime selected by <c>Provider</c> through the mapping table.
    /// Three failure branches: enum member without a mapping (Database.Discovery.ProviderNotSupported,
    /// lists the supported members), host process not referencing the provider project or the runtime
    /// no longer exposing <c>public static void Activate(DiscoveryActivationOptions)</c>
    /// (Database.Discovery.ProviderNotInstalled, FreeSql-style self-diagnosis naming the expected
    /// assembly-qualified type and the direct <c>*DiscoveryRuntime.Activate</c> escape hatch).
    /// Exceptions thrown by the runtime itself (e.g. the localized ConnectionName validation or
    /// GameDb resolution failures) propagate unchanged, unwrapped from the reflection layer.
    /// </remarks>
    /// <param name="options">激活参数（Provider 决定目标 Runtime，其余成员原样透传）/ Activation options (Provider selects the target runtime; the rest pass through verbatim)</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="options"/> 为 null 时抛出 / Thrown when <paramref name="options"/> is null</exception>
    /// <exception cref="InvalidOperationException">枚举未配映射、宿主缺 Provider 工程引用或激活入口签名不符时抛出 / Thrown when the enum is unmapped, the provider assembly is not referenced by the host, or the activation entry-point signature does not match</exception>
    [RequiresUnreferencedCode("按命名约定反射激活发现层；AOT/裁剪场景请直调 *DiscoveryRuntime.Activate 显式激活")]
    public static void Activate(DiscoveryActivationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var provider = options.Provider;
        if (!ProviderTypeNames.TryGetValue(provider, out var typeName))
        {
            // Localization: Database.Discovery.ProviderNotSupported - 发现层提供者枚举值“{0}”未配置解析映射。已支持：[{1}]。
            throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.Database.DiscoveryProviderNotSupported, provider.ToString(), string.Join(", ", ProviderTypeNames.Keys)));
        }

        var activator = ResolvedActivators.GetOrAdd(provider, static (_, name) => ResolveActivate(name), typeName);
        if (activator == null)
        {
            // Localization: Database.Discovery.ProviderNotInstalled - 无法定位发现层提供者“{0}”的激活入口“{1}”（含工程引用与直调逃生门指引）。
            throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.Database.DiscoveryProviderNotInstalled, provider.ToString(), typeName));
        }

        InvokeActivator(activator, options);
    }

    /// <summary>
    /// 按程序集限定类型名直接解析并激活（internal 测试钩子：绕过枚举映射表注入坏字符串，验证 NotInstalled 分支）。
    /// </summary>
    /// <remarks>
    /// Test hook resolving an arbitrary assembly-qualified type name so the guard test can drive the
    /// Database.Discovery.ProviderNotInstalled branch with an injected bad string without touching the
    /// real mapping table. Non-test callers use <see cref="Activate(DiscoveryActivationOptions)"/>.
    /// </remarks>
    /// <param name="typeName">程序集限定类型名 / Assembly-qualified type name</param>
    /// <param name="options">激活参数（原样透传给解析到的 Runtime）/ Activation options passed through to the resolved runtime</param>
    /// <exception cref="InvalidOperationException">激活入口不可定位时抛出 / Thrown when the activation entry point cannot be located</exception>
    internal static void ActivateFromTypeName(string typeName, DiscoveryActivationOptions options)
    {
        var activator = ResolveActivate(typeName);
        if (activator == null)
        {
            // Localization: Database.Discovery.ProviderNotInstalled - 无法定位发现层提供者“{0}”的激活入口“{1}”（含工程引用与直调逃生门指引）。
            throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.Database.DiscoveryProviderNotInstalled, "<test-injected>", typeName));
        }

        InvokeActivator(activator, options);
    }

    /// <summary>
    /// 解析类型的 <c>public static void Activate(DiscoveryActivationOptions)</c> 激活入口（类型或入口缺失返回 null，由调用方 fail-fast）。
    /// </summary>
    /// <remarks>
    /// Resolves the <c>Activate</c> entry point with the exact expected signature. A null return
    /// means either the assembly-qualified type did not load or the type no longer exposes the
    /// entry point — both surface as the localized NotInstalled self-diagnosis carrying the type name.
    /// </remarks>
    /// <param name="typeName">程序集限定类型名 / Assembly-qualified type name</param>
    /// <returns>激活方法信息；不可定位时 null / The activation method info, or null when unresolvable</returns>
    private static MethodInfo ResolveActivate(string typeName)
    {
        var type = Type.GetType(typeName, throwOnError: false);
        if (type == null)
        {
            return null;
        }

        return type.GetMethod("Activate", BindingFlags.Public | BindingFlags.Static, binder: null, new[] { typeof(DiscoveryActivationOptions) }, modifiers: null);
    }

    /// <summary>
    /// 反射调用激活入口（<see cref="TargetInvocationException"/> 解包为内层异常上抛——运行时自身的本地化校验异常按直调语义原样传播）。
    /// </summary>
    /// <remarks>
    /// Invokes the resolved entry point. Reflection wraps callee exceptions in
    /// <see cref="TargetInvocationException"/>; unwrapping preserves the direct-call semantics so
    /// the runtimes' own localized failures (ConnectionName validation, GameDb resolution) reach
    /// the composition root with their original types and messages.
    /// </remarks>
    /// <param name="activator">已解析的激活方法 / The resolved activation method</param>
    /// <param name="options">激活参数 / Activation options</param>
    private static void InvokeActivator(MethodInfo activator, DiscoveryActivationOptions options)
    {
        try
        {
            activator.Invoke(null, new object[] { options });
        }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            // 解包反射包装层：直调 *DiscoveryRuntime.Activate 时异常原样上抛，约定激活路径保持同形。
            throw exception.InnerException;
        }
    }
}
