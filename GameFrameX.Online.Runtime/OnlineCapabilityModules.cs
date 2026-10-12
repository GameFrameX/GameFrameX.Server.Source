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

using GameFrameX.Capability.Hosting;
using GameFrameX.Capability.Hosting.AdminApi;
using GameFrameX.Online.Runtime.AdminApi;

namespace GameFrameX.Online.Runtime;

/// <summary>
/// Online 能力簇过渡模块集（P1 过渡组合根：单体尚未拆包，manifest 与调度注册先行按终态包粒度声明；
/// P3/P4 各域迁包时随域实现迁走，机制包零改动）。
/// <para>
/// 维护约束：manifest Id = 终态能力包名（P1 冻结）；<c>Requires</c> 为类型级依赖的如实声明；
/// 滴答 job 归属其服务域；admin action 归属表见 C228 change.md（29 action）。
/// </para>
/// </summary>
public static class OnlineCapabilityModules
{
    /// <summary>
    /// 过渡装配全部模块（拓扑插入序；装配序由 Requires 拓扑定序决定）。
    /// </summary>
    /// <param name="host">过渡组合根（typed 服务图）。</param>
    /// <returns>模块集合。</returns>
    public static IReadOnlyList<ICapabilityModule> CreateAll(OnlineRuntimeHost host)
    {
        if (host == null)
        {
            throw new ArgumentNullException(nameof(host));
        }

        return new ICapabilityModule[]
        {
            new OnlineFoundationCapabilityModule(host),
            new OnlineMatchCapabilityModule(host),
            new OnlineCompetitionCapabilityModule(host),
            new OnlineOperationsCapabilityModule(host),
        };
    }

    /// <summary>
    /// 能力目录（admin-only 形态的未安装判定数据面：全部 Online 能力的 manifest 声明；
    /// P3 起叶子能力 manifest 随包，来源程序集如实归属）。
    /// </summary>
    /// <returns>manifest 目录（manifest + 来源程序集名）。</returns>
    public static IReadOnlyList<(CapabilityManifest Manifest, string SourceAssemblyName)> Catalog()
    {
        return new (CapabilityManifest, string)[]
        {
            (GameFrameX.Capability.Online.Assets.OnlineAssetsCapabilityModule.ManifestInstance,
                typeof(GameFrameX.Capability.Online.Assets.OnlineAssetsCapabilityModule).Assembly.GetName().Name),
            (OnlineFoundationCapabilityModule.ManifestInstance, typeof(OnlineCapabilityModules).Assembly.GetName().Name),
            (GameFrameX.Capability.Online.Social.OnlineSocialCapabilityModule.ManifestInstance,
                typeof(GameFrameX.Capability.Online.Social.OnlineSocialCapabilityModule).Assembly.GetName().Name),
            (GameFrameX.Capability.Online.Matchmaking.OnlineMatchmakingCapabilityModule.ManifestInstance,
                typeof(GameFrameX.Capability.Online.Matchmaking.OnlineMatchmakingCapabilityModule).Assembly.GetName().Name),
            (OnlineMatchCapabilityModule.ManifestInstance, typeof(OnlineCapabilityModules).Assembly.GetName().Name),
            (OnlineCompetitionCapabilityModule.ManifestInstance, typeof(OnlineCapabilityModules).Assembly.GetName().Name),
            (OnlineOperationsCapabilityModule.ManifestInstance, typeof(OnlineCapabilityModules).Assembly.GetName().Name),
        };
    }
}

/// <summary>
/// 平台地基能力（Identity/Session/Presence/Storage 四域的服务与清扫）。
/// </summary>
internal sealed class OnlineFoundationCapabilityModule : ICapabilityModule, IAdminActionContributor
{
    /// <summary>
    /// 平台地基能力清单。
    /// </summary>
    public static readonly CapabilityManifest ManifestInstance = new(
        "online-foundation",
        "1.0.0",
        new[] { "suspend_resume_player", });

    /// <summary>
    /// 过渡组合根。
    /// </summary>
    private readonly OnlineRuntimeHost _host;

    /// <summary>
    /// 初始化模块。
    /// </summary>
    /// <param name="host">过渡组合根。</param>
    public OnlineFoundationCapabilityModule(OnlineRuntimeHost host)
    {
        _host = host;
    }

    /// <inheritdoc />
    public CapabilityManifest Manifest
    {
        get { return ManifestInstance; }
    }

    /// <inheritdoc />
    public void Register(CapabilityRegistrationContext context)
    {
        context.AddTickJob("presence-timeout-sweep", async ct =>
        {
            var scope = _host.AuthorizedScope;
            await _host.Presence.SweepTimeoutsAsync(scope.TenantId, scope.AppId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), ct).ConfigureAwait(false);
        });
        context.AddTickJob("session-token-expiry-sweep", ct => _host.Tokens.SweepExpiredAsync(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), ct));
    }

    /// <inheritdoc />
    public void RegisterAdminActions(AdminActionRegistry registry)
    {
        new OnlineAdminMatchHandlers(_host).RegisterSuspendResume(registry);
    }
}
/// <summary>
/// 对局能力（权威对局 Actor / 重连 / 幂等结算）。
/// </summary>
internal sealed class OnlineMatchCapabilityModule : ICapabilityModule, IAdminActionContributor
{
    /// <summary>
    /// 对局能力清单。
    /// </summary>
    public static readonly CapabilityManifest ManifestInstance = new(
        "online-match",
        "1.0.0",
        new[] { "end_abnormal_match" },
        new[] { "online-assets" });

    /// <summary>
    /// 过渡组合根。
    /// </summary>
    private readonly OnlineRuntimeHost _host;

    /// <summary>
    /// 初始化模块。
    /// </summary>
    /// <param name="host">过渡组合根。</param>
    public OnlineMatchCapabilityModule(OnlineRuntimeHost host)
    {
        _host = host;
    }

    /// <inheritdoc />
    public CapabilityManifest Manifest
    {
        get { return ManifestInstance; }
    }

    /// <inheritdoc />
    public void Register(CapabilityRegistrationContext context)
    {
        context.AddTickJob("match-tick", ct => _host.MatchRuntime.TickAllAsync(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), ct));
    }

    /// <inheritdoc />
    public void RegisterAdminActions(AdminActionRegistry registry)
    {
        new OnlineAdminMatchHandlers(_host).RegisterEndAbnormalMatch(registry);
    }
}

/// <summary>
/// 竞技能力（榜单 / 赛季 / 锦标赛 / LiveOps 承载）。
/// </summary>
internal sealed class OnlineCompetitionCapabilityModule : ICapabilityModule, IAdminActionContributor
{
    /// <summary>
    /// 竞技能力清单。
    /// </summary>
    public static readonly CapabilityManifest ManifestInstance = new(
        "online-competition",
        "1.0.0",
        new[] { "publishRemoteConfig", "syncDeviceGroup", "syncScheduledTask", "pushAnnouncement", "syncPlayerSegment", "publishConfigRollout", "rollbackConfigRollout" },
        new[] { "online-assets" });

    /// <summary>
    /// 过渡组合根。
    /// </summary>
    private readonly OnlineRuntimeHost _host;

    /// <summary>
    /// 初始化模块。
    /// </summary>
    /// <param name="host">过渡组合根。</param>
    public OnlineCompetitionCapabilityModule(OnlineRuntimeHost host)
    {
        _host = host;
    }

    /// <inheritdoc />
    public CapabilityManifest Manifest
    {
        get { return ManifestInstance; }
    }

    /// <inheritdoc />
    public void Register(CapabilityRegistrationContext context)
    {
    }

    /// <inheritdoc />
    public void RegisterAdminActions(AdminActionRegistry registry)
    {
        new OnlineAdminLiveOpsHandlers(_host).Register(registry);
    }
}

/// <summary>
/// 运营聚合能力（审计 / 总览 / 时间线）。
/// </summary>
internal sealed class OnlineOperationsCapabilityModule : ICapabilityModule, IAdminActionContributor
{
    /// <summary>
    /// 运营聚合能力清单。
    /// </summary>
    public static readonly CapabilityManifest ManifestInstance = new(
        "online-operations",
        "1.0.0",
        new[] { "query_online_overview", "query_player_timeline", "query_online_audit_events" },
        new[] { "online-foundation", "online-assets", "online-social", "online-match", "online-matchmaking" });

    /// <summary>
    /// 过渡组合根。
    /// </summary>
    private readonly OnlineRuntimeHost _host;

    /// <summary>
    /// 初始化模块。
    /// </summary>
    /// <param name="host">过渡组合根。</param>
    public OnlineOperationsCapabilityModule(OnlineRuntimeHost host)
    {
        _host = host;
    }

    /// <inheritdoc />
    public CapabilityManifest Manifest
    {
        get { return ManifestInstance; }
    }

    /// <inheritdoc />
    public void Register(CapabilityRegistrationContext context)
    {
    }

    /// <inheritdoc />
    public void RegisterAdminActions(AdminActionRegistry registry)
    {
        new OnlineAdminConsoleHandlers(_host).Register(registry);
    }
}
