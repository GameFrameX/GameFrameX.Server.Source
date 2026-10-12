// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
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

using GameFrameX.Capability.Online.Contracts;
using GameFrameX.Capability.Hosting;
using GameFrameX.Foundation.Idempotency;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Capability.Online.Assets;
using GameFrameX.Online.Audit;
using GameFrameX.Capability.Online.Events;
using GameFrameX.Online.GameEvents;
using GameFrameX.Online.HotfixRollback;
using GameFrameX.Capability.Online.Identity;
using GameFrameX.Capability.Online.Idempotency;
using GameFrameX.Online.Leaderboard;
using GameFrameX.Online.Match;
using GameFrameX.Capability.Online.Matchmaking;
using GameFrameX.Online.Overview;
using GameFrameX.Capability.Online.Party;
using GameFrameX.Capability.Online.Presence;
using GameFrameX.Online.Runtime.AdminApi;
using GameFrameX.Capability.Online.Scope;
using GameFrameX.Online.Season;
using GameFrameX.Capability.Online.Session;
using GameFrameX.Capability.Online.Social;
using GameFrameX.Capability.Online.Storage;
using GameFrameX.Online.Timeline;
using GameFrameX.Online.Tournament;

namespace GameFrameX.Online.Runtime;

/// <summary>
/// Online Runtime 过渡组合根（P1：机制半边已迁出至 <see cref="CapabilityHost"/>，本类保留
/// typed 服务图与装配语句；P3/P4 各域随能力包迁出后本工程删除）。
/// <para>
/// 维护约束（装配红线）：单一接缝集中 <c>new</c> 全部 store 与服务（内存存储为单进程默认实现），
/// 生产级 Mongo/PSQL 持久化实现另行演进；事件出口统一 Foundation <see cref="InMemoryEventPublisher"/>
/// （订阅端做审计投影与聊天审计投影）；副作用命令经 <see cref="OnlineIdempotencyService"/>
/// （<see cref="IdempotencyOptions.FailedReplayPolicy"/> = <see cref="FailedReplayPolicy.ReExecute"/>）；
/// Hotfix 回滚执行器为可选注入位（admin 面暂无触发 action，默认空实现）；
/// 后台调度经能力模块注册进 <see cref="CapabilityHost"/>（显式链式装配）。
/// </para>
/// </summary>
public sealed class OnlineRuntimeHost
{
    /// <summary>
    /// 装配选项（作用域三元组与调度间隔）。
    /// </summary>
    public CapabilityHostOptions Options { get; }

    /// <summary>
    /// 获取机制宿主（注册表 / 调度壳 / 能力报告 / 事件爆发口）。
    /// </summary>
    public CapabilityHost CapabilityHost { get; }

    /// <summary>
    /// 获取运行时授权作用域（本进程的服务端权威三元组）。
    /// </summary>
    public OnlineScope AuthorizedScope { get; }

    /// <summary>
    /// Foundation 进程内事件总线（Online 事件出口的传输实现）。
    /// </summary>
    public InMemoryEventPublisher EventBus { get; } = new();

    /// <summary>
    /// 获取时间源。
    /// </summary>
    public IClock Clock { get; } = SystemClock.Instance;

    // ---- 存储（InMemory 为单进程默认实现；生产持久化实现另行演进）----


    /// <summary>统一审计存储。</summary>
    public InMemoryOnlineAuditStore AuditStore { get; } = new();

    /// <summary>游戏事件存储。</summary>
    public InMemoryOnlineGameEventStore GameEventStore { get; } = new();

    /// <summary>游戏事件死信出口。</summary>
    public InMemoryOnlineGameEventDeadLetterSink GameEventDeadLetterSink { get; } = new();

    /// <summary>Hotfix 版本存储。</summary>
    public InMemoryOnlineHotfixVersionStore HotfixVersionStore { get; } = new();

    /// <summary>身份存储。</summary>
    public InMemoryOnlineIdentityStore IdentityStore { get; } = new();

    /// <summary>排行榜存储。</summary>
    public InMemoryOnlineLeaderboardStore LeaderboardStore { get; } = new();

    /// <summary>对局 Actor 存储。</summary>
    public InMemoryOnlineMatchActorStore MatchActorStore { get; } = new();

    /// <summary>对局结果存储。</summary>
    public InMemoryOnlineMatchResultStore MatchResultStore { get; } = new();

    /// <summary>在线状态存储。</summary>
    public InMemoryOnlinePresenceStore PresenceStore { get; } = new();

    /// <summary>赛季存储。</summary>
    public InMemoryOnlineSeasonStore SeasonStore { get; } = new();

    /// <summary>会话存储。</summary>
    public InMemoryOnlineSessionStore SessionStore { get; } = new();


    /// <summary>玩家 KV 存储器。</summary>
    public InMemoryOnlinePlayerStorageStore PlayerStorageStore { get; } = new();

    /// <summary>锦标赛存储。</summary>
    public InMemoryOnlineTournamentStore TournamentStore { get; } = new();

    // ---- 服务 ----

    /// <summary>Online 事件出口（映射 Foundation 信封后分发）。</summary>
    public IOnlineEventPublisher EventPublisher { get; }

    /// <summary>幂等服务（admin 层副作用命令包裹）。</summary>
    public OnlineIdempotencyService Idempotency { get; }


    /// <summary>统一审计服务。</summary>
    public OnlineAuditService Audit { get; }

    /// <summary>身份服务。</summary>
    public OnlineIdentityService Identity { get; }

    /// <summary>会话管理。</summary>
    public OnlineSessionManager Sessions { get; }

    /// <summary>会话令牌（签发 / 吊销 / 强踢）。</summary>
    public OnlineSessionTokenService Tokens { get; }

    /// <summary>在线状态。</summary>
    public OnlinePresenceService Presence { get; }

    /// <summary>对局运行时。</summary>
    public OnlineMatchRuntime MatchRuntime { get; }

    /// <summary>在线总览。</summary>
    public OnlineOverviewService Overview { get; }

    /// <summary>玩家时间线。</summary>
    public OnlinePlayerTimelineService Timeline { get; }

    /// <summary>排行榜。</summary>
    public OnlineLeaderboardService Leaderboards { get; }

    /// <summary>赛季。</summary>
    public OnlineSeasonService Seasons { get; }

    /// <summary>锦标赛。</summary>
    public OnlineTournamentService Tournaments { get; }

    /// <summary>Hotfix 回滚。</summary>
    public OnlineHotfixRollbackService HotfixRollback { get; }

    /// <summary>LiveOps 最小承载（InMemory 版本登记表）。</summary>
    public OnlineLiveOpsRegistry LiveOps { get; }

    /// <summary>后台调度壳（机制宿主承载；一次性生命周期）。</summary>
    public CapabilityScheduler Scheduler
    {
        get { return CapabilityHost.Scheduler; }
    }

    /// <summary>
    /// 初始化 <see cref="OnlineRuntimeHost"/>（组合根：集中装配全部存储与服务）。
    /// </summary>
    /// <param name="options">装配选项（必须合法，否则抛 <see cref="InvalidOperationException"/>）。</param>
    /// <param name="filter">能力装配过滤器（null = 全装配；来自 "Online" 配置段，C229 起经 AddOnlineCapabilities 注入）。</param>
    /// <param name="hotfixRollbackExecutor">Hotfix 回滚执行器（可选；admin 面暂无触发 action，默认空实现）。</param>
    public OnlineRuntimeHost(CapabilityHostOptions options, CapabilityAssemblyFilter filter = null, IOnlineHotfixRollbackExecutor hotfixRollbackExecutor = null)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        if (!options.IsValid())
        {
            // Localization: Capability.Hosting.OptionsInvalid - 能力宿主选项非法（作用域三元组 / 调度间隔）。
            throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.Capability.Hosting.OptionsInvalid));
        }

        AuthorizedScope = new OnlineScope(options.TenantId, options.AppId, options.ServerId);
        EventPublisher = new OnlineEventPublisher(EventBus);

        var coordinator = new IdempotencyCoordinator(new InMemoryIdempotencyStore(), Clock,
                                                     new IdempotencyOptions { FailedReplayPolicy = FailedReplayPolicy.ReExecute, });
        Idempotency = new OnlineIdempotencyService(coordinator);

        Audit = new OnlineAuditService(AuditStore);
        Identity = new OnlineIdentityService(IdentityStore);
        Sessions = new OnlineSessionManager(SessionStore, EventPublisher);
        Tokens = new OnlineSessionTokenService(SessionStore, EventPublisher);
        Presence = new OnlinePresenceService(PresenceStore, EventPublisher);
        MatchRuntime = new OnlineMatchRuntime(MatchActorStore, EventPublisher);
        MatchRuntime.RegisterGame(new OnlineRockPaperScissorsGame());
        Leaderboards = new OnlineLeaderboardService(LeaderboardStore, null, EventPublisher);
        HotfixRollback = new OnlineHotfixRollbackService(HotfixVersionStore, Idempotency, Audit, hotfixRollbackExecutor, EventPublisher);

        LiveOps = new OnlineLiveOpsRegistry();

        // 能力装配（P3 起：叶子能力经包扩展自装配，跨域依赖经共享服务面；剩余域由壳构造并取用叶子产物）。
        var builder = new CapabilityHostBuilder();
        builder.UseService(AuthorizedScope);
        builder.UseService<IOnlineEventPublisher>(EventPublisher);
        builder.UseService(Idempotency);
        builder.UseService<IOnlineAuditReader>(Audit);
        builder.UseService<IOnlineSessionStore>(SessionStore);
        builder.UseService(Tokens);
        builder.AddOnlineAssetsRuntime();
        builder.AddOnlineSocialRuntime();
        builder.AddOnlineMatchmakingRuntime();
        foreach (var module in OnlineCapabilityModules.CreateAll(this))
        {
            builder.AddModule(module);
        }

        builder.SetFilter(filter);
        CapabilityHost = builder.Build(options);

        // 叶子产物取用（assets / social / matchmaking(+Party) 域由能力包装配；闭包校验保证启用模块的依赖产物在场，
        // 禁用能力的下游服务置 null——其 handler/job 均随模块禁用，不可达）。
        var hasGrantService = CapabilityHost.Services.TryGetService(out OnlineGrantService grantService);
        var hasAssetQuery = CapabilityHost.Services.TryGetService(out OnlineAssetQueryService assetQuery);
        var hasSocialGraphStore = CapabilityHost.Services.TryGetService(out IOnlineSocialGraphStore socialGraphStore);
        var hasMatchTicketStore = CapabilityHost.Services.TryGetService(out IOnlineMatchTicketStore matchTicketStore);
        var hasPartyStore = CapabilityHost.Services.TryGetService(out IOnlinePartyStore partyStore);
        Overview = hasMatchTicketStore && hasPartyStore
            ? new OnlineOverviewService(PresenceStore, SessionStore, partyStore, matchTicketStore, MatchActorStore)
            : null;
        Timeline = hasAssetQuery && hasSocialGraphStore ? new OnlinePlayerTimelineService(IdentityStore, SessionStore, assetQuery, MatchActorStore, socialGraphStore) : null;
        Seasons = hasGrantService ? new OnlineSeasonService(SeasonStore, Leaderboards, grantService, EventPublisher) : null;
        Tournaments = hasGrantService ? new OnlineTournamentService(TournamentStore, Leaderboards, grantService, EventPublisher) : null;
    }

    /// <summary>
    /// 启动运行时（事件桥接线：C105 审计桥 + 聊天审计投影；随后启动机制宿主与调度壳）。
    /// <para>可重复调用（幂等）：重复启动不重复接线。</para>
    /// </summary>
    /// <returns>异步任务。</returns>
    public async Task StartAsync()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        var chatAudit = CapabilityHost.Services.TryGetService(out OnlineChatAuditProjection chatAuditProjection) ? chatAuditProjection : null;
        var consumer = new OnlineEventConsumer(new InMemoryEventDeduplicator());
        var sanitizer = new OnlineEventSanitizer();
        EventBus.Subscribe(envelope =>
        {
            try
            {
                var onlineEvent = OnlineEventEnvelopeMapper.FromEnvelope(envelope);
                if (!consumer.TryConsume(onlineEvent))
                {
                    return;
                }

                chatAudit?.OnEvent(onlineEvent);
                IngestAuditFromEvent(onlineEvent, sanitizer);
            }
            catch (Exception)
            {
                // 审计桥异常不得阻断业务事件分发（InMemoryEventPublisher 异常策略是向上传播，
                // 这里吞掉投影侧异常以保证宿主写链路不受投影缺陷影响；投影片段丢失由审计查询面暴露）。
            }
        });
        await CapabilityHost.StartAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// 停止运行时（停机制宿主（含调度壳，等待当轮完成）；事件总线保留——进程退出即消亡）。
    /// </summary>
    /// <returns>异步任务。</returns>
    public async Task StopAsync()
    {
        _started = false;
        await CapabilityHost.StopAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// 启动标记（重复启动保护）。
    /// </summary>
    private bool _started;

    /// <summary>
    /// 审计桥：把 Online 事件自动转接为统一审计记录（admin 命令 → 审计留痕）。
    /// </summary>
    /// <param name="onlineEvent">Online 事件。</param>
    /// <param name="sanitizer">脱敏器。</param>
    private void IngestAuditFromEvent(OnlineEvent onlineEvent, OnlineEventSanitizer sanitizer)
    {
        var view = sanitizer.CreateAuditView(onlineEvent);
        var entry = new OnlineAuditEntry
        {
            Domain = OnlineAuditDomain.Operation,
            EventId = view.EventId,
            EventType = view.EventType,
            OccurredTime = view.OccurredTime,
            TenantId = view.TenantId,
            AppId = view.AppId,
            ServerId = view.ServerId,
            PlayerId = view.PlayerId,
            OperatorId = "online-runtime",
            Source = view.Source,
            CorrelationId = view.CorrelationId,
            PayloadAuditFields = view.SanitizedFields,
        };
        var _ = Audit.IngestAsync(entry);
    }
}