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

using GameFrameX.Foundation.Idempotency;
using GameFrameX.Online.Assets;
using GameFrameX.Online.Audit;
using GameFrameX.Online.Events;
using GameFrameX.Online.GameEvents;
using GameFrameX.Online.HotfixRollback;
using GameFrameX.Online.Identity;
using GameFrameX.Online.Idempotency;
using GameFrameX.Online.Leaderboard;
using GameFrameX.Online.Match;
using GameFrameX.Online.Matchmaking;
using GameFrameX.Online.Overview;
using GameFrameX.Online.Party;
using GameFrameX.Online.Presence;
using GameFrameX.Online.Runtime.AdminApi;
using GameFrameX.Online.Scope;
using GameFrameX.Online.Season;
using GameFrameX.Online.Session;
using GameFrameX.Online.Social;
using GameFrameX.Online.Storage;
using GameFrameX.Online.Timeline;
using GameFrameX.Online.Tournament;

namespace GameFrameX.Online.Runtime;

/// <summary>
/// Online Runtime 组合根（change C122：把 C93～C106 已交付的 Online 能力装配为可运行整体——
/// InMemory 存储 + 全域服务 + 事件桥 + 幂等 + 审计桥 + 后台调度器 + LiveOps 最小承载）。
/// <para>
/// 维护约束（装配红线）：单一接缝集中 <c>new</c> 全部 store 与服务（C94「内存存储为单进程默认实现」），
/// 生产级 Mongo/PSQL 持久化实现登记为后续 change；事件出口统一 Foundation <see cref="InMemoryEventPublisher"/>
/// （订阅端做审计投影与聊天审计投影）；副作用命令经 <see cref="OnlineIdempotencyService"/>
/// （<see cref="IdempotencyOptions.FailedReplayPolicy"/> = <see cref="FailedReplayPolicy.ReExecute"/>）；
/// Hotfix 回滚执行器为可选注入位（admin 面暂无触发 action，默认空实现）。
/// </para>
/// </summary>
public sealed class OnlineRuntimeHost
{
    /// <summary>
    /// 装配选项。
    /// </summary>
    public OnlineRuntimeOptions Options
    {
        get;
    }

    /// <summary>
    /// 获取运行时授权作用域（本进程的服务端权威三元组）。
    /// </summary>
    public OnlineScope AuthorizedScope
    {
        get;
    }

    /// <summary>
    /// Foundation 进程内事件总线（Online 事件出口的传输实现）。
    /// </summary>
    public InMemoryEventPublisher EventBus
    {
        get;
    } = new InMemoryEventPublisher();

    /// <summary>
    /// 获取时间源。
    /// </summary>
    public IClock Clock
    {
        get;
    } = SystemClock.Instance;

    // ---- 存储（C94：InMemory 为单进程默认实现；生产持久化实现为后续 change）----

    /// <summary>资产存储（钱包 / 库存 / 账本）。</summary>
    public InMemoryOnlineAssetStore AssetStore
    {
        get;
    } = new InMemoryOnlineAssetStore();

    /// <summary>资产交易存储。</summary>
    public InMemoryOnlineAssetTransactionStore AssetTransactionStore
    {
        get;
    } = new InMemoryOnlineAssetTransactionStore();

    /// <summary>统一审计存储。</summary>
    public InMemoryOnlineAuditStore AuditStore
    {
        get;
    } = new InMemoryOnlineAuditStore();

    /// <summary>游戏事件存储。</summary>
    public InMemoryOnlineGameEventStore GameEventStore
    {
        get;
    } = new InMemoryOnlineGameEventStore();

    /// <summary>游戏事件死信出口。</summary>
    public InMemoryOnlineGameEventDeadLetterSink GameEventDeadLetterSink
    {
        get;
    } = new InMemoryOnlineGameEventDeadLetterSink();

    /// <summary>Hotfix 版本存储。</summary>
    public InMemoryOnlineHotfixVersionStore HotfixVersionStore
    {
        get;
    } = new InMemoryOnlineHotfixVersionStore();

    /// <summary>身份存储。</summary>
    public InMemoryOnlineIdentityStore IdentityStore
    {
        get;
    } = new InMemoryOnlineIdentityStore();

    /// <summary>排行榜存储。</summary>
    public InMemoryOnlineLeaderboardStore LeaderboardStore
    {
        get;
    } = new InMemoryOnlineLeaderboardStore();

    /// <summary>对局 Actor 存储。</summary>
    public InMemoryOnlineMatchActorStore MatchActorStore
    {
        get;
    } = new InMemoryOnlineMatchActorStore();

    /// <summary>对局结果存储。</summary>
    public InMemoryOnlineMatchResultStore MatchResultStore
    {
        get;
    } = new InMemoryOnlineMatchResultStore();

    /// <summary>匹配大厅存储。</summary>
    public InMemoryOnlineMatchListingStore MatchListingStore
    {
        get;
    } = new InMemoryOnlineMatchListingStore();

    /// <summary>匹配票据存储。</summary>
    public InMemoryOnlineMatchTicketStore MatchTicketStore
    {
        get;
    } = new InMemoryOnlineMatchTicketStore();

    /// <summary>队伍存储。</summary>
    public InMemoryOnlinePartyStore PartyStore
    {
        get;
    } = new InMemoryOnlinePartyStore();

    /// <summary>在线状态存储。</summary>
    public InMemoryOnlinePresenceStore PresenceStore
    {
        get;
    } = new InMemoryOnlinePresenceStore();

    /// <summary>赛季存储。</summary>
    public InMemoryOnlineSeasonStore SeasonStore
    {
        get;
    } = new InMemoryOnlineSeasonStore();

    /// <summary>会话存储。</summary>
    public InMemoryOnlineSessionStore SessionStore
    {
        get;
    } = new InMemoryOnlineSessionStore();

    /// <summary>聊天存储。</summary>
    public InMemoryOnlineChatStore ChatStore
    {
        get;
    } = new InMemoryOnlineChatStore();

    /// <summary>好友存储。</summary>
    public InMemoryOnlineFriendStore FriendStore
    {
        get;
    } = new InMemoryOnlineFriendStore();

    /// <summary>群组存储。</summary>
    public InMemoryOnlineGroupStore GroupStore
    {
        get;
    } = new InMemoryOnlineGroupStore();

    /// <summary>通知存储。</summary>
    public InMemoryOnlineNotificationStore NotificationStore
    {
        get;
    } = new InMemoryOnlineNotificationStore();

    /// <summary>举报存储。</summary>
    public InMemoryOnlineReportStore ReportStore
    {
        get;
    } = new InMemoryOnlineReportStore();

    /// <summary>社交关系存储（处罚读取面）。</summary>
    public InMemoryOnlineSocialGraphStore SocialGraphStore
    {
        get;
    } = new InMemoryOnlineSocialGraphStore();

    /// <summary>玩家 KV 存储器。</summary>
    public InMemoryOnlinePlayerStorageStore PlayerStorageStore
    {
        get;
    } = new InMemoryOnlinePlayerStorageStore();

    /// <summary>锦标赛存储。</summary>
    public InMemoryOnlineTournamentStore TournamentStore
    {
        get;
    } = new InMemoryOnlineTournamentStore();

    // ---- 服务 ----

    /// <summary>Online 事件出口（映射 Foundation 信封后分发）。</summary>
    public IOnlineEventPublisher EventPublisher
    {
        get;
    }

    /// <summary>幂等服务（admin 层副作用命令包裹）。</summary>
    public OnlineIdempotencyService Idempotency
    {
        get;
    }

    /// <summary>资产发放统一入口（内部自带幂等流）。</summary>
    public OnlineGrantService GrantService
    {
        get;
    }

    /// <summary>资产查询（钱包 / 账本 / 交易明细）。</summary>
    public OnlineAssetQueryService AssetQuery
    {
        get;
    }

    /// <summary>统一审计服务。</summary>
    public OnlineAuditService Audit
    {
        get;
    }

    /// <summary>身份服务。</summary>
    public OnlineIdentityService Identity
    {
        get;
    }

    /// <summary>会话管理。</summary>
    public OnlineSessionManager Sessions
    {
        get;
    }

    /// <summary>会话令牌（签发 / 吊销 / 强踢）。</summary>
    public OnlineSessionTokenService Tokens
    {
        get;
    }

    /// <summary>在线状态。</summary>
    public OnlinePresenceService Presence
    {
        get;
    }

    /// <summary>社交裁决（禁言 / 举报 / 拉黑）。</summary>
    public OnlineSocialDecisionService SocialDecisions
    {
        get;
    }

    /// <summary>处罚（Mute / Ban 事实源）。</summary>
    public OnlinePunishmentService Punishments
    {
        get;
    }

    /// <summary>通知。</summary>
    public OnlineNotificationService Notifications
    {
        get;
    }

    /// <summary>匹配票据。</summary>
    public OnlineMatchTicketService MatchTickets
    {
        get;
    }

    /// <summary>撮合协调器。</summary>
    public OnlineMatchmakerCoordinator Matchmaker
    {
        get;
    }

    /// <summary>匹配队列观察器。</summary>
    public OnlineMatchQueueObserver MatchQueueObserver
    {
        get;
    }

    /// <summary>对局运行时。</summary>
    public OnlineMatchRuntime MatchRuntime
    {
        get;
    }

    /// <summary>在线总览。</summary>
    public OnlineOverviewService Overview
    {
        get;
    }

    /// <summary>玩家时间线。</summary>
    public OnlinePlayerTimelineService Timeline
    {
        get;
    }

    /// <summary>排行榜。</summary>
    public OnlineLeaderboardService Leaderboards
    {
        get;
    }

    /// <summary>赛季。</summary>
    public OnlineSeasonService Seasons
    {
        get;
    }

    /// <summary>锦标赛。</summary>
    public OnlineTournamentService Tournaments
    {
        get;
    }

    /// <summary>Hotfix 回滚。</summary>
    public OnlineHotfixRollbackService HotfixRollback
    {
        get;
    }

    /// <summary>聊天审计投影（订阅事件总线的 chat 审计查询面）。</summary>
    public OnlineChatAuditProjection ChatAudit
    {
        get;
    }

    /// <summary>LiveOps 最小承载（InMemory 版本登记表）。</summary>
    public OnlineLiveOpsRegistry LiveOps
    {
        get;
    }

    /// <summary>后台调度器。</summary>
    public OnlineRuntimeScheduler Scheduler
    {
        get;
    }

    /// <summary>
    /// 初始化 <see cref="OnlineRuntimeHost"/>（组合根：集中装配全部存储与服务）。
    /// </summary>
    /// <param name="options">装配选项（必须合法，否则抛 <see cref="InvalidOperationException"/>）。</param>
    /// <param name="hotfixRollbackExecutor">Hotfix 回滚执行器（可选；admin 面暂无触发 action，默认空实现）。</param>
    public OnlineRuntimeHost(OnlineRuntimeOptions options, IOnlineHotfixRollbackExecutor hotfixRollbackExecutor = null)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        if (!options.IsValid())
        {
            throw new InvalidOperationException("Online runtime options are invalid (scope triple / port / prefix).");
        }

        AuthorizedScope = new OnlineScope(options.TenantId, options.AppId, options.ServerId);
        EventPublisher = new OnlineEventPublisher(EventBus);

        var coordinator = new IdempotencyCoordinator(new InMemoryIdempotencyStore(), Clock,
            new IdempotencyOptions { FailedReplayPolicy = FailedReplayPolicy.ReExecute, });
        Idempotency = new OnlineIdempotencyService(coordinator);

        AssetQuery = new OnlineAssetQueryService(AssetStore, AssetTransactionStore);
        GrantService = new OnlineGrantService(AssetStore, AssetTransactionStore, Idempotency, EventPublisher);
        Audit = new OnlineAuditService(AuditStore);
        Identity = new OnlineIdentityService(IdentityStore);
        Sessions = new OnlineSessionManager(SessionStore, EventPublisher);
        Tokens = new OnlineSessionTokenService(SessionStore, EventPublisher);
        Presence = new OnlinePresenceService(PresenceStore, EventPublisher);
        SocialDecisions = new OnlineSocialDecisionService(SocialGraphStore, ReportStore, EventPublisher);
        Punishments = new OnlinePunishmentService(SocialGraphStore, EventPublisher);
        Notifications = new OnlineNotificationService(NotificationStore, EventPublisher);
        MatchTickets = new OnlineMatchTicketService(MatchTicketStore, EventPublisher);
        Matchmaker = new OnlineMatchmakerCoordinator(MatchTicketStore, MatchTickets, EventPublisher, null, SocialDecisions);
        MatchQueueObserver = new OnlineMatchQueueObserver(MatchTicketStore);
        MatchRuntime = new OnlineMatchRuntime(MatchActorStore, EventPublisher);
        MatchRuntime.RegisterGame(new OnlineRockPaperScissorsGame());
        Overview = new OnlineOverviewService(PresenceStore, SessionStore, PartyStore, MatchTicketStore, MatchActorStore);
        Timeline = new OnlinePlayerTimelineService(IdentityStore, SessionStore, AssetQuery, MatchActorStore, SocialGraphStore);
        Leaderboards = new OnlineLeaderboardService(LeaderboardStore, null, EventPublisher);
        Seasons = new OnlineSeasonService(SeasonStore, Leaderboards, GrantService, EventPublisher);
        Tournaments = new OnlineTournamentService(TournamentStore, Leaderboards, GrantService, EventPublisher);
        HotfixRollback = new OnlineHotfixRollbackService(HotfixVersionStore, Idempotency, Audit, hotfixRollbackExecutor, EventPublisher);

        ChatAudit = new OnlineChatAuditProjection(ChatStore);
        LiveOps = new OnlineLiveOpsRegistry();
        Scheduler = new OnlineRuntimeScheduler(this);
    }

    /// <summary>
    /// 启动运行时（事件桥接线：C105 审计桥 + 聊天审计投影；随后启动后台调度器）。
    /// <para>可重复调用（幂等）：重复启动不重复接线。</para>
    /// </summary>
    /// <returns>异步任务。</returns>
    public Task StartAsync()
    {
        if (_started)
        {
            return Task.CompletedTask;
        }

        _started = true;
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

                ChatAudit.OnEvent(onlineEvent);
                IngestAuditFromEvent(onlineEvent, sanitizer);
            }
            catch (Exception)
            {
                // 审计桥异常不得阻断业务事件分发（InMemoryEventPublisher 异常策略是向上传播，
                // 这里吞掉投影侧异常以保证宿主写链路不受投影缺陷影响；投影片段丢失由审计查询面暴露）。
            }
        });
        Scheduler.Start();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 停止运行时（停调度器；事件总线保留——进程退出即消亡）。
    /// </summary>
    /// <returns>异步任务。</returns>
    public async Task StopAsync()
    {
        _started = false;
        await Scheduler.StopAsync();
    }

    /// <summary>
    /// 启动标记（重复启动保护）。
    /// </summary>
    private bool _started;

    /// <summary>
    /// C105 审计桥：把 Online 事件自动转接为统一审计记录（admin 命令 → 审计留痕）。
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
