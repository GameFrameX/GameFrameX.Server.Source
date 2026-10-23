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

using System.IO;
using System.Reflection;
using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Hosting.Core;
using GameFrameX.Capability.Hosting.AdminApi;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Events;
using GameFrameX.Capability.Online.Abstractions.Idempotency;
using GameFrameX.Capability.Online.Competition.Leaderboard;
using GameFrameX.Capability.Online.Operations;
using GameFrameX.Capability.Online.Operations.HotfixRollback;
using GameFrameX.Capability.Online.Operations.Audit;
using GameFrameX.Capability.Online.Persistence;
using GameFrameX.Capability.Online.Abstractions.Scope;
using GameFrameX.Foundation.Idempotency;
using GameFrameX.Foundation.Localization.Core;

namespace GameFrameX.Launcher.Online;

/// <summary>
/// Online 能力组合产物（业务运行时与管理面一次组装的双面产物）。
/// <para>启动 / 停止由组合根按序编排：业务先启后停、管理面后启先停；本产物只承载装配结果。</para>
/// </summary>
/// <remarks>
/// The composed product of a single <see cref="AddOnlineCapabilities"/> call: the business
/// runtime and the admin plane are assembled together, each face remaining null when its gate is off.
/// Start/stop orchestration stays with the composition root (business first to start and last to
/// stop); this type only carries the assembled result.
/// </remarks>
public sealed class OnlineCapabilityComposition
{
    /// <summary>
    /// 业务能力宿主（<see cref="OnlineCapabilityCompositionOptions.EnableRuntime"/> 为 false 时为 null；
    /// 真扫描产物——模块发现经 <see cref="CapabilityAssembly"/>，启动 / 停止语义与壳时代一致）。
    /// </summary>
    /// <remarks>
    /// The business capability host; null when <see cref="OnlineCapabilityCompositionOptions.EnableRuntime"/>
    /// is false. It is the product of real scanning (module discovery via
    /// <see cref="CapabilityAssembly"/>) with start/stop semantics unchanged from the shell era.
    /// </remarks>
    public CapabilityHost Runtime { get; }

    /// <summary>
    /// 管理面 HTTP 服务（<see cref="OnlineCapabilityCompositionOptions.EnableAdminApi"/> 为 false 时为 null；
    /// 业务运行时在则全能力形态，不在则 admin-only 形态）。
    /// </summary>
    /// <remarks>
    /// The admin HTTP service; null when <see cref="OnlineCapabilityCompositionOptions.EnableAdminApi"/>
    /// is false. Full-capability shape when the runtime is assembled, admin-only shape otherwise.
    /// </remarks>
    public AdminApiServer AdminApi { get; }

    /// <summary>
    /// 初始化组合产物（仅经 <see cref="AddOnlineCapabilities"/> 构造）。
    /// </summary>
    /// <param name="runtime">业务能力宿主（可为 null）。</param>
    /// <param name="adminApi">管理面 HTTP 服务（可为 null）。</param>
    private OnlineCapabilityComposition(CapabilityHost runtime, AdminApiServer adminApi)
    {
        Runtime = runtime;
        AdminApi = adminApi;
    }

    /// <summary>
    /// Online 能力单入口：一次回调编排装配业务运行时与管理面（编排位于 Launcher，真扫描生产采用）。
    /// <para>入口内部经 <see cref="BuildScannedRuntime"/> 装配：组合根基建预置（授权作用域 / 事件总线 /
    /// 事件出口 / 幂等 / 统一审计）+ <see cref="CapabilityAssembly.DiscoverModules"/> 扫描发现全部能力模块
    /// （扫描面 = 本组合根程序集引用面）+ 过滤器与拓扑闭包校验由机制 <c>Build</c> 承担。</para>
    /// <para>能力过滤器与运行时报告、admin-only 目录共享同一份配置；未知能力 Id / 白名单闭包缺口 / 选项非法
    /// 均组合期 fail-fast。</para>
    /// </summary>
    /// <remarks>
    /// The single Online capability entry point: one callback orchestrates both the business runtime
    /// and the admin plane (orchestrated in the Launcher with real-scanning production
    /// adoption). The entry assembles through <see cref="BuildScannedRuntime"/>: the
    /// composition-root infrastructure pre-seed (authorized scope / event bus / event outlet /
    /// idempotency / unified audit), module discovery via <see cref="CapabilityAssembly.DiscoverModules"/>
    /// over the composition-root assembly reference face, and filter plus topological closure validation
    /// handled by the mechanism's <c>Build</c>. The filter is shared between the runtime report and the
    /// admin-only catalog; unknown capability ids, whitelist closure gaps and invalid options all fail
    /// fast at composition time.
    /// </remarks>
    /// <param name="configure">编排回调（null 拒绝；至少启用一个面）。</param>
    /// <returns>双面组合产物（未启用的面为 null）。</returns>
    /// <exception cref="ArgumentNullException">configure 为 null。</exception>
    /// <exception cref="InvalidOperationException">双闸均未启用，或选项 / 过滤器未通过组合期校验。</exception>
    public static OnlineCapabilityComposition AddOnlineCapabilities(Action<OnlineCapabilityCompositionOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new OnlineCapabilityCompositionOptions();
        configure(options);

        if (!options.EnableRuntime && !options.EnableAdminApi)
        {
            // Localization: Capability.Hosting.Composition.NoPlaneEnabled - 双闸均未启用（业务与管理面至少启用其一）。
            throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.Capability.Hosting.Composition.NoPlaneEnabled));
        }

        var hostOptions = new CapabilityHostOptions
        {
            TenantId = options.TenantId,
            AppId = options.AppId,
            ServerId = options.ServerId,
            SchedulerIntervalMilliseconds = options.SchedulerIntervalMilliseconds,
        };
        var filter = BuildFilter(options.Capabilities, options.Disable);

        var runtime = options.EnableRuntime ? BuildScannedRuntime(hostOptions, filter, options.UseGameDbStores, options.MatchBridgeLeaderboardId) : null;
        var adminOptions = options.AdminApi ?? new AdminApiOptions();
        var adminApi = options.EnableAdminApi
            ? (runtime != null
                ? OnlineAdminApiComposition.CreateServer(runtime, adminOptions)
                : OnlineAdminApiComposition.CreateAdminOnlyServer(hostOptions, adminOptions, filter))
            : null;

        return new OnlineCapabilityComposition(runtime, adminApi);
    }

    /// <summary>
    /// 真扫描装配业务能力宿主：组合根基建预置 + 模块扫描发现 + 机制构建。
    /// <para>基建预置与壳时代 <c>OnlineRuntimeHost</c> 构造段逐语句等价——授权作用域 / 事件总线 /
    /// 事件出口 / 幂等 / 统一审计（具型 + 读面）；地基三 store 与会话令牌下沉 Foundation 模块自装配
    /// （共享面经 <c>AddService</c> 回注，后序能力探测同源）；Hotfix 切换执行器与登记期制品获取器
    /// 为生产装配面（注入 <c>HotfixManagerRollbackExecutor</c> / <c>OnlineHotfixArtifactFetcher</c>，
    /// Operations 模块探测即得非 null，替换缝仍开放）。</para>
    /// </summary>
    /// <remarks>
    /// Builds the business capability host through real scanning:
    /// composition-root infrastructure pre-seed, scanned module discovery, then the mechanism build.
    /// The pre-seed is statement-by-statement equivalent to the shell-era <c>OnlineRuntimeHost</c>
    /// constructor — the authorized scope, event bus, event outlet, idempotency and unified audit
    /// (concrete type plus read face); the three foundation stores and the session-token service
    /// sink into the Foundation module's self-assembly (registered back into the shared face so later
    /// capabilities resolve the same instances); the hotfix switch executor and the
    /// registration-time artifact fetcher are production-wired (<c>HotfixManagerRollbackExecutor</c> /
    /// <c>OnlineHotfixArtifactFetcher</c>, probed non-null by the Operations module, replacement
    /// seams still open).
    /// </remarks>
    /// <param name="hostOptions">宿主选项（已通过合法性校验）。</param>
    /// <param name="filter">能力装配过滤器（null = 全装配）。</param>
    /// <param name="useGameDbStores">是否预置一级 store 的 GameDb 持久化实现（false = InMemory 默认不变）。</param>
    /// <param name="matchBridgeLeaderboardId">对局结算写榜榜单标识（空白 = 不预置投影选项）。</param>
    /// <returns>业务能力宿主。</returns>
    private static CapabilityHost BuildScannedRuntime(CapabilityHostOptions hostOptions, CapabilityAssemblyFilter filter, bool useGameDbStores, string matchBridgeLeaderboardId)
    {
        var authorizedScope = new OnlineScope(hostOptions.TenantId, hostOptions.AppId, hostOptions.ServerId);
        var eventBus = new InMemoryEventPublisher();
        var eventPublisher = new OnlineEventPublisher(eventBus);
        var coordinator = new IdempotencyCoordinator(new InMemoryIdempotencyStore(), SystemClock.Instance,
                                                     new IdempotencyOptions { FailedReplayPolicy = FailedReplayPolicy.ReExecute, });
        var idempotency = new OnlineIdempotencyService(coordinator);
        var audit = new OnlineAuditService(new InMemoryOnlineAuditStore());

        var builder = new CapabilityHostBuilder();
        builder.UseService(authorizedScope);
        builder.UseService(eventBus);
        builder.UseService<IOnlineEventPublisher>(eventPublisher);
        builder.UseService(idempotency);
        builder.UseService(audit);
        builder.UseService<IOnlineAuditReader>(audit);
        // Hotfix 版本切换执行器 + 登记期制品获取器生产装配（消除「生产路径恒缺席」）——
        // Operations 模块 Register 期探测命中，切换命令不再恒 8002；切换时刻零下载红线由执行器路径静态保证。
        builder.UseOnlineHotfixRollbackExecutor(new HotfixManagerRollbackExecutor());
        builder.UseOnlineHotfixArtifactFetcher(new OnlineHotfixArtifactFetcher());
        if (useGameDbStores)
        {
            // 一级 store 持久化预置：模块 Register 期探测共享面命中即采用；Session / Presence 保持 InMemory。
            builder.UseOnlineGameDbStores();
        }

        if (!string.IsNullOrWhiteSpace(matchBridgeLeaderboardId))
        {
            // 对局结算写榜投影目标预置：online-competition 模块 Register 期探测命中即装配 Match.Settled 投影订阅。
            builder.UseService(new OnlineLeaderboardProjectionOptions { MatchSettledBoardId = matchBridgeLeaderboardId });
        }
        foreach (var module in CapabilityAssembly.DiscoverModules(LoadCapabilityAssemblies()))
        {
            builder.AddModule(module);
        }

        builder.SetFilter(filter);
        return builder.Build(hostOptions);
    }

    /// <summary>
    /// 加载部署目录内符合能力包命名约定（<c>GameFrameX.Capability.*.dll</c>）的全部程序集并返回
    /// （真扫描数据源）。
    /// <para>.NET 懒加载下未被代码触碰的能力包程序集不在 AppDomain 已加载集，
    /// 而程序集引用表（AssemblyRef）只记代码实际引用的类型，反射装配的组合根两处都靠不住；
    /// 按部署目录命名约定圈定扫描面——新增能力 = 部署产物落地即被发现，宿主零改动
    /// （机制开放红线的生产采用面）。</para>
    /// </summary>
    /// <remarks>
    /// Loads and returns every assembly in the deployment directory matching the capability-package
    /// naming convention (<c>GameFrameX.Capability.*.dll</c>), serving as the real-scanning source.
    /// Under .NET lazy loading, capability packages untouched by code stay out of the loaded set,
    /// and an assembly's reference table (AssemblyRef) only records types actually referenced by
    /// code — a reflection-based composition root can rely on neither. The scan surface is therefore
    /// pinned by the deployment-directory naming convention: a new capability is discovered by simply
    /// landing its build output, with zero host changes (the production adoption of the
    /// mechanism-openness guarantee).
    /// </remarks>
    /// <returns>部署目录内可加载的能力包程序集集合。</returns>
    internal static IReadOnlyList<Assembly> LoadCapabilityAssemblies()
    {
        var assemblies = new List<Assembly>();
        foreach (var path in Directory.GetFiles(AppContext.BaseDirectory, "GameFrameX.Capability.*.dll"))
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
    /// 由配置列表构建装配过滤器（null / 空 / 全空白 = 相应维度不过滤——缺省全装配红线）。
    /// </summary>
    /// <remarks>
    /// Builds the assembly filter from the configured lists; null, empty or all-blank lists skip the
    /// corresponding dimension (the empty-filter-assembles-all guarantee). Entries are trimmed and
    /// blank entries dropped; unknown ids fail fast inside the builder.
    /// </remarks>
    /// <param name="capabilities">能力白名单（可为 null）。</param>
    /// <param name="disable">能力排除表（可为 null）。</param>
    /// <returns>过滤器；两个维度都不过滤时为 null。</returns>
    private static CapabilityAssemblyFilter BuildFilter(IReadOnlyList<string> capabilities, IReadOnlyList<string> disable)
    {
        var enabledIds = NormalizeIds(capabilities);
        var disabledIds = NormalizeIds(disable) ?? Array.Empty<string>();

        return enabledIds == null && disabledIds.Length == 0 ? null : new CapabilityAssemblyFilter(enabledIds, disabledIds);
    }

    /// <summary>
    /// 规整能力 Id 列表（trim、丢弃空白项）；无有效条目时返回 null（该维度不过滤）。
    /// </summary>
    /// <remarks>
    /// Normalizes a capability id list (trim, drop blank entries); returns null when nothing
    /// meaningful remains, meaning the dimension does not filter.
    /// </remarks>
    /// <param name="source">原始列表（可为 null）。</param>
    /// <returns>规整后的数组；无有效条目时为 null。</returns>
    private static string[] NormalizeIds(IReadOnlyList<string> source)
    {
        if (source == null)
        {
            return null;
        }

        var ids = source.Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .ToArray();

        return ids.Length == 0 ? null : ids;
    }
}
