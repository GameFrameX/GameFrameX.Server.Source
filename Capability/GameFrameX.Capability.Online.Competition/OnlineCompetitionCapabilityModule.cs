// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the PRC and relevant international regulations.
//   使用本项目须严格遵守相关法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with relevant laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file at the root of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://www.cnb.cool/GameFrameX
//   CNB Repository: https://www.cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System;
using System.Threading.Tasks;
using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Hosting.AdminApi;
using GameFrameX.Capability.Online.Assets;
using GameFrameX.Capability.Online.Competition.AdminApi;
using GameFrameX.Capability.Online.Competition.Leaderboard;
using GameFrameX.Capability.Online.Competition.LiveOps;
using GameFrameX.Capability.Online.Competition.Season;
using GameFrameX.Capability.Online.Competition.Tournament;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Events;
using GameFrameX.Capability.Online.Abstractions.Idempotency;
using GameFrameX.Capability.Online.Match;
using GameFrameX.Foundation.Idempotency;

namespace GameFrameX.Capability.Online.Competition;

/// <summary>
/// 竞技能力（榜单 / 赛季 / 锦标赛 / LiveOps 承载）的自装配模块。
/// <para>
/// 维护约束（服务共享协议）：模块无参构造（扫描可发现）；<c>Register</c> 自装配本域——榜单 / 赛季 /
/// 锦标赛三个 store 经 <see cref="CapabilityRegistrationContext.TryGetService{TService}"/> 探测替换缝，未命中构造
/// InMemory 默认；赛季 / 锦标赛对发放服务（<see cref="OnlineGrantService"/>，assets 域产物）的依赖经上下文探测，
/// 缺席（assets 能力被过滤）时不装配两者（壳时代 Seasons / Tournaments 置 null 的模块化等价）；对外产物
/// （榜单 / 赛季 / 锦标赛服务与 LiveOps 注册表）注册回共享面。组合根预置
/// <see cref="OnlineLeaderboardProjectionOptions"/>（board id 非空白）且对局结果存储（online-match 域产物）与幂等
/// 服务探测命中时装配 Match.Settled 排行榜投影（订阅 + 失败重投 tick），未预置 / 依赖缺席不装配不报错
/// （与赛季 / 锦标赛缺席装配同口径的模块化等价）。
/// </para>
/// </summary>
/// <remarks>
/// The self-assembling module of the competition capability (leaderboards, seasons, tournaments and the
/// LiveOps registry). The module has a parameterless constructor (discoverable by
/// scanning); <c>Register</c> self-assembles the domain — the leaderboard / season / tournament stores
/// probe the replacement seams with in-memory fallbacks; the season / tournament services probe the grant
/// service (an assets-domain product) through the context and stay unassembled when absent (the modular
/// equivalent of the shell-era null seasons / tournaments); outward products are registered back into the
/// shared face. A Match.Settled leaderboard projection (event-bus subscription plus a
/// failure-retry tick job) is assembled when the composition root pre-seeds projection options with a
/// non-blank board id and the match result store / idempotency service are probeable; otherwise it stays
/// unassembled without error.
/// </remarks>
public sealed class OnlineCompetitionCapabilityModule : ICapabilityModule, IAdminActionContributor
{
    /// <summary>
    /// 竞技能力清单（Id 冻结 = 终态包名；action 集合与幂等包裹位逐字迁移自壳过渡模块；
    /// 依赖追加 online-match——Match.Settled 投影真实依赖对局结果存储，Match 不引用 Competition，拓扑无环）。
    /// </summary>
    public static readonly CapabilityManifest ManifestInstance = new(
        "online-competition",
        "1.0.0",
        new[] { "publishRemoteConfig", "syncDeviceGroup", "syncScheduledTask", "pushAnnouncement", "syncPlayerSegment", "publishConfigRollout", "rollbackConfigRollout" },
        new[] { "online-assets", "online-match" });

    /// <summary>
    /// LiveOps 注册表（Register 期构造；admin 面 handler 取用）。
    /// </summary>
    private OnlineLiveOpsRegistry _liveOps;

    /// <summary>
    /// 排行榜服务（Register 期构造；admin 面 handler 与投影链取用）。
    /// </summary>
    private OnlineLeaderboardService _leaderboards;

    /// <summary>
    /// Match.Settled 排行榜投影器（Register 期探测装配；未预置投影选项或依赖缺席时为 null）。
    /// </summary>
    private OnlineMatchSettledLeaderboardProjector _projectionProjector;

    /// <inheritdoc />
    public CapabilityManifest Manifest
    {
        get { return ManifestInstance; }
    }

    /// <inheritdoc />
    public void Register(CapabilityRegistrationContext context)
    {
        var eventPublisher = context.GetService<IOnlineEventPublisher>();
        var leaderboardStore = ResolveStore<IOnlineLeaderboardStore>(context, static () => new InMemoryOnlineLeaderboardStore());
        var seasonStore = ResolveStore<IOnlineSeasonStore>(context, static () => new InMemoryOnlineSeasonStore());
        var tournamentStore = ResolveStore<IOnlineTournamentStore>(context, static () => new InMemoryOnlineTournamentStore());

        var leaderboards = new OnlineLeaderboardService(leaderboardStore, null, eventPublisher);
        _leaderboards = leaderboards;
        context.AddService(leaderboards);

        _liveOps = new OnlineLiveOpsRegistry();
        context.AddService(_liveOps);

        if (context.TryGetService(out OnlineGrantService grantService))
        {
            context.AddService(new OnlineSeasonService(seasonStore, leaderboards, grantService, eventPublisher));
            context.AddService(new OnlineTournamentService(tournamentStore, leaderboards, grantService, eventPublisher));
        }

        // Match.Settled → 排行榜投影：组合根预置 OnlineLeaderboardProjectionOptions（board id 空白 = 不投影）
        // 且对局结果存储 / 幂等服务探测命中才装配；订阅事件总线同步投影（异常吞掉不阻断分发——Operations 审计桥先例），
        // 失败经 tick job leaderboard-projection-retry 重投；结果存储未命中（online-match 被过滤）→ 不装配不报错
        // （模块化等价先例 = 赛季 / 锦标赛对发放服务的缺席装配）。
        var projectionOptions = context.TryGetService(out OnlineLeaderboardProjectionOptions projectionOptionsValue) ? projectionOptionsValue : null;
        if (projectionOptions != null
            && !string.IsNullOrWhiteSpace(projectionOptions.MatchSettledBoardId)
            && context.TryGetService(out IOnlineMatchResultStore projectionResultStore)
            && context.TryGetService(out OnlineIdempotencyService projectionIdempotency))
        {
            _projectionProjector = new OnlineMatchSettledLeaderboardProjector(projectionResultStore, leaderboards, projectionIdempotency, projectionOptions.MatchSettledBoardId);
            context.AddService(_projectionProjector);
            var projectionEventBus = context.GetService<InMemoryEventPublisher>();
            projectionEventBus.Subscribe(envelope =>
            {
                try
                {
                    var onlineEvent = OnlineEventEnvelopeMapper.FromEnvelope(envelope);
                    if (onlineEvent.EventType == OnlineMatchRuntimeEvents.MatchSettled)
                    {
                        _projectionProjector.HandleEventAsync(onlineEvent).GetAwaiter().GetResult();
                    }
                }
                catch (Exception)
                {
                    // 投影订阅体异常不得阻断业务事件分发（InMemoryEventPublisher 异常策略是向上传播，
                    // 此处吞掉；失败已由投影器记待重试，经 QueryStatus / admin 查询面可观测）。
                }
            });
            context.AddTickJob("leaderboard-projection-retry", ct => _projectionProjector.RetryPendingAsync(ct));
        }
    }

    /// <inheritdoc />
    public void RegisterAdminActions(AdminActionRegistry registry)
    {
        OnlineAdminLiveOpsHandlers.RegisterLiveOps(registry, _liveOps);
        OnlineAdminLeaderboardHandlers.RegisterLeaderboardQuery(registry, _leaderboards);
    }

    /// <summary>
    /// 解析存储（替换缝探测——命中用组合根注入实例，未命中构造 InMemory 默认并注册回共享面）。
    /// </summary>
    /// <typeparam name="TStore">存储接口。</typeparam>
    /// <param name="context">注册上下文。</param>
    /// <param name="defaultFactory">默认构造。</param>
    /// <returns>存储实例。</returns>
    private static TStore ResolveStore<TStore>(CapabilityRegistrationContext context, Func<TStore> defaultFactory)
        where TStore : class
    {
        if (context.TryGetService(out TStore store))
        {
            return store;
        }

        store = defaultFactory();
        context.AddService(store);
        return store;
    }
}
