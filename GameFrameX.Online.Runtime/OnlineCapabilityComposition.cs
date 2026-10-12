// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the PRC and relevant international regulations.
//   使用本项目须严格遵守相关法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please refer to the LICENSE file at the root of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development,
//   本组织与贡献者概不承担任何责任。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://www.cnb.cool/GameFrameX
//   CNB Repository: https://www.cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using GameFrameX.Capability.Hosting;
using GameFrameX.Capability.Hosting.AdminApi;
using GameFrameX.Foundation.Localization.Core;

namespace GameFrameX.Online.Runtime;

/// <summary>
/// Online 能力组合编排选项（<see cref="OnlineCapabilityComposition.AddOnlineCapabilities"/> 回调承载面，C229）。
/// <para>作用域三元组、调度间隔、双闸（EnableRuntime / EnableAdminApi）、管理面监听与能力过滤器
/// （Capabilities 白名单 / Disable 排除表）统一经回调写入（.NET <c>AddXxx(options =&gt; ...)</c> 惯例）。</para>
/// </summary>
/// <remarks>
/// Orchestration options carried by the <see cref="OnlineCapabilityComposition.AddOnlineCapabilities"/>
/// callback (C229): the scope triple, scheduler interval, the two enable gates
/// (<see cref="EnableRuntime"/> / <see cref="EnableAdminApi"/>), the admin listening options and the
/// capability filter (<see cref="Capabilities"/> whitelist / <see cref="Disable"/> exclusion list) all
/// arrive through the callback, following the .NET <c>AddXxx(options =&gt; ...)</c> convention.
/// </remarks>
public sealed class OnlineCapabilityCompositionOptions
{
    /// <summary>
    /// 授权租户标识（作用域三元组成员；跨租户请求按 3002 拒绝）
    /// </summary>
    /// <remarks>
    /// Authorized tenant id (part of the scope triple; cross-tenant requests are rejected with 3002).
    /// </remarks>
    public long TenantId { get; set; }

    /// <summary>
    /// 授权应用标识（作用域三元组成员；跨 App 请求按 3003 拒绝）
    /// </summary>
    /// <remarks>
    /// Authorized app id (part of the scope triple; cross-app requests are rejected with 3003).
    /// </remarks>
    public long AppId { get; set; }

    /// <summary>
    /// 宿主服务器标识（作用域三元组成员；跨服请求按 3004 拒绝）
    /// </summary>
    /// <remarks>
    /// Host server id (part of the scope triple; cross-server requests are rejected with 3004).
    /// </remarks>
    public long ServerId { get; set; }

    /// <summary>
    /// 调度滴答间隔（毫秒，默认 1000；小于 100 由调度壳钳制为 100）
    /// </summary>
    /// <remarks>
    /// Scheduler tick interval in milliseconds (default 1000; values below 100 are clamped to 100 by the scheduler).
    /// </remarks>
    public int SchedulerIntervalMilliseconds { get; set; } = 1000;

    /// <summary>
    /// 是否装配业务运行时（对应 <c>AppSetting.IsEnableOnline</c>；false 时不构造 OnlineRuntimeHost）
    /// </summary>
    /// <remarks>
    /// Whether to assemble the business runtime (mirrors <c>AppSetting.IsEnableOnline</c>);
    /// when false no OnlineRuntimeHost is constructed at all.
    /// </remarks>
    public bool EnableRuntime { get; set; }

    /// <summary>
    /// 是否装配管理面（对应 <c>AppSetting.IsEnableOnlineAdmin</c>；业务运行时在则全能力形态，不在则 admin-only 形态）
    /// </summary>
    /// <remarks>
    /// Whether to assemble the admin plane (mirrors <c>AppSetting.IsEnableOnlineAdmin</c>);
    /// full-capability shape when the runtime is assembled, admin-only shape otherwise.
    /// </remarks>
    public bool EnableAdminApi { get; set; }

    /// <summary>
    /// 管理面监听选项（null = 缺省 28090 / online-admin）
    /// </summary>
    /// <remarks>
    /// Admin listening options (null = defaults 28090 / online-admin).
    /// </remarks>
    public AdminApiOptions AdminApi { get; set; }

    /// <summary>
    /// 能力白名单（能力 Id 列表，如 online-assets）；null 或空 = 全装配；白名单未含依赖时组合期闭包校验 fail-fast
    /// </summary>
    /// <remarks>
    /// Capability enable whitelist (capability ids, e.g. online-assets); null or empty assembles
    /// everything, and a whitelist that omits a required dependency fails fast at composition time.
    /// </remarks>
    public IReadOnlyList<string> Capabilities { get; set; }

    /// <summary>
    /// 能力排除表（能力 Id 列表）；null 或空 = 无排除
    /// </summary>
    /// <remarks>
    /// Capability exclusion list (capability ids); null or empty excludes nothing.
    /// </remarks>
    public IReadOnlyList<string> Disable { get; set; }
}

/// <summary>
/// Online 能力组合产物（业务运行时与管理面一次组装的双面产物，C229）。
/// <para>启动 / 停止由组合根按序编排：业务先启后停、管理面后启先停；本产物只承载装配结果。</para>
/// </summary>
/// <remarks>
/// The composed product of a single <see cref="AddOnlineCapabilities"/> call (C229): the business
/// runtime and the admin plane are assembled together, each face remaining null when its gate is off.
/// Start/stop orchestration stays with the composition root (business first to start and last to
/// stop); this type only carries the assembled result.
/// </remarks>
public sealed class OnlineCapabilityComposition
{
    /// <summary>
    /// 业务运行时过渡组合根（<see cref="OnlineCapabilityCompositionOptions.EnableRuntime"/> 为 false 时为 null）。
    /// </summary>
    /// <remarks>
    /// The business runtime transitional composition root; null when
    /// <see cref="OnlineCapabilityCompositionOptions.EnableRuntime"/> is false.
    /// </remarks>
    public OnlineRuntimeHost Runtime { get; }

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
    /// <param name="runtime">业务运行时（可为 null）。</param>
    /// <param name="adminApi">管理面 HTTP 服务（可为 null）。</param>
    private OnlineCapabilityComposition(OnlineRuntimeHost runtime, AdminApiServer adminApi)
    {
        Runtime = runtime;
        AdminApi = adminApi;
    }

    /// <summary>
    /// Online 能力单入口：一次回调编排装配业务运行时与管理面（C229）。
    /// <para>P2 期入口内部为显式链式装配（过渡模块有参构造，反射扫描发现不了——模块无参化后同一入口
    /// 自然升级为真扫描，P4 终态 BuildHost 扫描 + 组合根零改动）。</para>
    /// <para>能力过滤器与运行时报告、admin-only 目录共享同一份配置；未知能力 Id / 白名单闭包缺口 / 选项非法
    /// 均组合期 fail-fast。</para>
    /// </summary>
    /// <remarks>
    /// The single Online capability entry point: one callback orchestrates both the business runtime
    /// and the admin plane (C229). During P2 the entry assembles explicitly (the transitional modules
    /// have parameterized constructors invisible to reflection scanning; once modules become
    /// parameterless the same entry upgrades to real scanning, with the P4 end state being
    /// BuildHost scanning plus a zero-change composition root). The filter is shared between the
    /// runtime report and the admin-only catalog; unknown capability ids, whitelist closure gaps and
    /// invalid options all fail fast at composition time.
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

        var runtime = options.EnableRuntime ? new OnlineRuntimeHost(hostOptions, filter) : null;
        var adminOptions = options.AdminApi ?? new AdminApiOptions();
        var adminApi = options.EnableAdminApi
            ? (runtime != null
                ? OnlineAdminApiComposition.CreateServer(runtime, adminOptions)
                : OnlineAdminApiComposition.CreateAdminOnlyServer(hostOptions, adminOptions, filter))
            : null;

        return new OnlineCapabilityComposition(runtime, adminApi);
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
