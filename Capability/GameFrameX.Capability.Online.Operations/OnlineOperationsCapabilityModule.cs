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
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Hosting.AdminApi;
using GameFrameX.Capability.Online.Assets;
using GameFrameX.Capability.Online.Foundation.Identity;
using GameFrameX.Capability.Online.Foundation.Presence;
using GameFrameX.Capability.Online.Foundation.Session;
using GameFrameX.Capability.Online.Abstractions.Audit;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Events;
using GameFrameX.Capability.Online.Abstractions.Idempotency;
using GameFrameX.Capability.Online.Match;
using GameFrameX.Capability.Online.Matchmaking;
using GameFrameX.Capability.Online.Matchmaking.Party;
using GameFrameX.Capability.Online.Operations.AdminApi;
using GameFrameX.Capability.Online.Operations.HotfixRollback;
using GameFrameX.Capability.Online.Operations.Overview;
using GameFrameX.Capability.Online.Operations.Timeline;
using GameFrameX.Capability.Online.Social;
using GameFrameX.Foundation.Idempotency;

namespace GameFrameX.Capability.Online.Operations;

/// <summary>
/// 运营聚合能力（审计 / 游戏事件 / Hotfix 回滚 / 总览 / 时间线 + 事件桥/审计桥）的自装配模块
/// （C294 迁入新机制三段生命周期）。
/// <para>
/// 维护约束（服务共享协议）：模块无参构造（扫描可发现）；<c>Register</c> 自装配本域——统一审计与幂等
/// 为族基建（<c>OnlineCapabilityFamily</c> 预置，具型 / 读面同源），直接 GetService；Hotfix 回滚的版本切换
/// 执行器是宿主扩展点（<see cref="CapabilityRegistrationContext.RequireHostService{TService}"/> 声明，
/// 宿主经 Override 提供缺即装配失败），制品获取器为包内默认（Options 必填化构造）；总览 / 时间线探测
/// 可选叶子产物（assets / social / match / matchmaking），缺席置 null（handler 不可达语义对齐壳时代）；
/// 地基三 store（presence / session / identity）来自必选 online-foundation 的默认注册，直接 GetService。
/// </para>
/// <para>
/// 事件桥 / 审计桥（C105 语义不变红线）：对全部已知 Online 事件主题逐个经机制事件总线订阅
/// （Register 期声明；单个订阅体异常由机制记日志后继续分发，业务侧不再自行吞异常），信封去重后做
/// 聊天审计投影与统一审计转接，桥体逐语句迁移自壳 <c>OnlineRuntimeHost.StartAsync</c>。
/// </para>
/// </summary>
public sealed class OnlineOperationsCapabilityModule : ICapabilityModule, IAdminActionContributor
{
    /// <summary>
    /// 运营聚合能力清单（Id 冻结 = 终态包名；查询域 action 迁移自壳过渡模块，Hotfix 受控操作族为 C256 新增；
    /// 必选 online-foundation；可选 assets / social / match / matchmaking 经
    /// <see cref="CapabilityOptionalDependency"/> 声明，缺席时披露对应缺失功能名）。
    /// </summary>
    public static readonly CapabilityManifest ManifestInstance = new(
        "online-operations",
        "1.0.0",
        new[] { "query_online_overview", "query_player_timeline", "query_online_audit_events", "register_hotfix_manifest", "activate_hotfix_version", "check_hotfix_compatibility", "rollback_hotfix_version" },
        new[] { "online-foundation" },
        new[]
        {
            new CapabilityOptionalDependency("online-assets", Localization.Keys.Capability.OnlineModule.Operations.OptionalAssetsAbsent),
            new CapabilityOptionalDependency("online-social", Localization.Keys.Capability.OnlineModule.Operations.OptionalSocialAbsent),
            new CapabilityOptionalDependency("online-match", Localization.Keys.Capability.OnlineModule.Operations.OptionalMatchAbsent),
            new CapabilityOptionalDependency("online-matchmaking", Localization.Keys.Capability.OnlineModule.Operations.OptionalMatchmakingAbsent),
        },
        typeof(OnlineOperationsOptions));

    /// <summary>
    /// 统一审计服务（Register 期解析族基建；admin 查询面与桥的写入面同源）。
    /// </summary>
    private OnlineAuditService _audit;

    /// <summary>
    /// 在线总览（Register 期构造；叶子能力缺席时为 null，handler 不可达语义对齐壳时代）。
    /// </summary>
    private OnlineOverviewService _overview;

    /// <summary>
    /// 玩家时间线（Register 期构造；叶子能力缺席时为 null，handler 不可达语义对齐壳时代）。
    /// </summary>
    private OnlinePlayerTimelineService _timeline;

    /// <summary>
    /// Hotfix 回滚受控服务（Register 期构造；幂等基建为族基建恒在）。
    /// </summary>
    private OnlineHotfixRollbackService _hotfixRollback;

    /// <summary>
    /// 获取运营聚合能力的静态清单（单例实例）。
    /// </summary>
    /// <remarks>
    /// Gets the static manifest singleton of the online operations capability.
    /// </remarks>
    /// <value>能力清单单例 / Capability manifest singleton</value>
    public CapabilityManifest Manifest
    {
        get { return ManifestInstance; }
    }

    /// <summary>
    /// 注册运营能力：解析统一审计服务；注册内存热更版本存储，要求宿主提供版本切换执行器，按选项注册制品获取器，
    /// 注册回滚状态轨迹日志的空实现缺省（宿主覆盖恒胜出）并装配热更回滚服务；
    /// 仅在撮合票据、队伍与对局 Actor 存储均在场时装配总览服务，仅在资产查询、社交关系存储与对局 Actor 存储均在场时装配玩家时间线服务；
    /// 最后对全部已知在线事件主题注册事件与审计桥。
    /// </summary>
    /// <remarks>
    /// Resolves the unified audit service, registers the in-memory hotfix version store, requires the host rollback executor, registers the artifact fetcher from options,
    /// registers the silent default of the rollback trace log (host overrides always win) and builds the hotfix rollback service.
    /// The overview service is built only when ticket, party and match actor stores exist; the player timeline only when asset query, social graph and match actor stores exist.
    /// Finally subscribes the event and audit bridge to all known online event topics.
    /// </remarks>
    /// <param name="context">能力注册上下文 / Capability registration context</param>
    public void Register(CapabilityRegistrationContext context)
    {
        var options = context.GetOptions<OnlineOperationsOptions>();
        var eventPublisher = context.GetService<IOnlineEventPublisher>();

        // 统一审计：族基建预置（具型 + IOnlineAuditReader 读面同源单例），直接解析，无探测回退。
        var audit = context.GetService<OnlineAuditService>();
        _audit = audit;

        // Hotfix 回滚：幂等为族基建恒在；版本切换执行器为宿主扩展点（缺宿主 Override = 装配期 fail-fast）；
        // 制品获取器为包内默认（下载参数必填自 Options）；版本 store 懒默认描述符（族 / 宿主覆盖恒胜出）。
        context.AddService<IOnlineHotfixVersionStore>(static _ => new InMemoryOnlineHotfixVersionStore());
        var hotfixVersionStore = context.GetService<IOnlineHotfixVersionStore>();
        var idempotency = context.GetService<OnlineIdempotencyService>();
        context.RequireHostService<IOnlineHotfixRollbackExecutor>();
        var rollbackExecutor = context.GetService<IOnlineHotfixRollbackExecutor>();
        context.AddService<IOnlineHotfixRollbackLog>(static _ => new NullOnlineHotfixRollbackLog());
        var rollbackLog = context.GetService<IOnlineHotfixRollbackLog>();
        context.AddService<IOnlineHotfixArtifactFetcher>(resolver => new OnlineHotfixArtifactFetcher(options.ArtifactFetch));
        var artifactFetcher = context.GetService<IOnlineHotfixArtifactFetcher>();
        _hotfixRollback = new OnlineHotfixRollbackService(hotfixVersionStore, idempotency, audit, rollbackExecutor, artifactFetcher, eventPublisher, rollbackLog);
        context.AddService(_hotfixRollback);

        // 总览 / 时间线（可选叶子产物经 manifest 可选声明 + GetOptionalService，缺席 = null 不装配；
        // 地基三 store 来自必选 online-foundation 的默认注册，直接 GetService，本包不再注册默认避免同键第二默认）。
        var assetQuery = context.GetOptionalService<OnlineAssetQueryService>();
        var socialGraphStore = context.GetOptionalService<IOnlineSocialGraphStore>();
        var matchTicketStore = context.GetOptionalService<IOnlineMatchTicketStore>();
        var partyStore = context.GetOptionalService<IOnlinePartyStore>();
        var matchActorStore = context.GetOptionalService<IOnlineMatchActorStore>();
        var presenceStore = context.GetService<IOnlinePresenceStore>();
        var sessionStore = context.GetService<IOnlineSessionStore>();
        var identityStore = context.GetService<IOnlineIdentityStore>();
        _overview = matchTicketStore != null && partyStore != null && matchActorStore != null
            ? new OnlineOverviewService(presenceStore, sessionStore, partyStore, matchTicketStore, matchActorStore)
            : null;
        _timeline = assetQuery != null && socialGraphStore != null && matchActorStore != null
            ? new OnlinePlayerTimelineService(identityStore, sessionStore, assetQuery, matchActorStore, socialGraphStore)
            : null;

        RegisterEventBridge(context);
    }

    /// <summary>
    /// 激活运营能力：本实现不做任何处理，因为所有服务与事件订阅已在注册期完成装配，且本能力不发布桥。
    /// </summary>
    /// <remarks>
    /// Performs no work, because all services and event subscriptions are wired during registration and this capability publishes no bridges.
    /// </remarks>
    /// <param name="context">能力激活上下文 / Capability activation context</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>已完成的任务 / A completed task</returns>
    public Task ActivateAsync(CapabilityActivationContext context, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// 停用运营能力：本实现不做任何处理，因为事件订阅由机制事件总线管理，模块不持有需要释放的资源。
    /// </summary>
    /// <remarks>
    /// Performs no work, because event subscriptions are owned by the mechanism event bus and the module holds no resources to release.
    /// </remarks>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>已完成的任务 / A completed task</returns>
    public Task DeactivateAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// 注册运营管理动作：控制台动作（总览、时间线与审计，未装配的服务以空值传入）与热更回滚动作。
    /// </summary>
    /// <remarks>
    /// Registers console actions (overview, timeline and audit; unassembled services are passed as null) and hotfix rollback actions.
    /// </remarks>
    /// <param name="registry">管理动作注册表 / Admin action registry</param>
    public void RegisterAdminActions(AdminActionRegistry registry)
    {
        OnlineAdminConsoleHandlers.RegisterConsoleActions(registry, _overview, _timeline, _audit);
        OnlineAdminHotfixHandlers.RegisterHotfixActions(registry, _hotfixRollback);
    }

    /// <summary>
    /// 注册事件桥 / 审计桥（C105 语义不变红线）：对全部已知 Online 事件主题逐个经机制事件总线订阅；
    /// 订阅体 = 信封去重（EventId）→ 聊天审计投影（可选社交产物，缺席跳过）→ 统一审计转接，
    /// 逐语句迁移自壳 <c>StartAsync</c>；单个订阅体异常由机制记日志后继续分发（业务侧不再吞异常）。
    /// </summary>
    /// <param name="context">注册上下文。</param>
    private void RegisterEventBridge(CapabilityRegistrationContext context)
    {
        var chatAudit = context.GetOptionalService<OnlineChatAuditProjection>();
        var consumer = new OnlineEventConsumer(new InMemoryEventDeduplicator());
        var sanitizer = new OnlineEventSanitizer();

        void HandleBridgeEvent(OnlineEvent onlineEvent)
        {
            if (!consumer.TryConsume(onlineEvent))
            {
                return;
            }

            chatAudit?.OnEvent(onlineEvent);
            IngestAuditFromEvent(onlineEvent, sanitizer, _audit);
        }

        // 地基（online-foundation）。
        context.Subscribe<OnlineEvent>(OnlinePresenceEvents.PresenceChanged, HandleBridgeEvent);
        context.Subscribe<OnlineEvent>(OnlineSessionEvents.SessionCreated, HandleBridgeEvent);
        context.Subscribe<OnlineEvent>(OnlineSessionEvents.SessionAuthenticated, HandleBridgeEvent);
        context.Subscribe<OnlineEvent>(OnlineSessionEvents.SessionConnected, HandleBridgeEvent);
        context.Subscribe<OnlineEvent>(OnlineSessionEvents.SessionActive, HandleBridgeEvent);
        context.Subscribe<OnlineEvent>(OnlineSessionEvents.SessionReconnecting, HandleBridgeEvent);
        context.Subscribe<OnlineEvent>(OnlineSessionEvents.SessionClosed, HandleBridgeEvent);
        context.Subscribe<OnlineEvent>(OnlineSessionEvents.SessionKicked, HandleBridgeEvent);
        context.Subscribe<OnlineEvent>(OnlineSessionEvents.SessionExpired, HandleBridgeEvent);
        context.Subscribe<OnlineEvent>(OnlineSessionEvents.SessionTokenRefreshed, HandleBridgeEvent);

        // 资产（online-assets）。
        context.Subscribe<OnlineEvent>(OnlineAssetEvents.AssetGranted, HandleBridgeEvent);
        context.Subscribe<OnlineEvent>(OnlineAssetEvents.AssetDeducted, HandleBridgeEvent);
        context.Subscribe<OnlineEvent>(OnlineAssetEvents.AssetAdjusted, HandleBridgeEvent);

        // 社交（online-social）。
        context.Subscribe<OnlineEvent>(OnlineChatEvents.MessageSent, HandleBridgeEvent);
        context.Subscribe<OnlineEvent>(OnlineChatEvents.MessageRecalled, HandleBridgeEvent);
        context.Subscribe<OnlineEvent>(OnlineChatEvents.ReadMarkUpdated, HandleBridgeEvent);
        context.Subscribe<OnlineEvent>(OnlineChatEvents.ModerationFailed, HandleBridgeEvent);
        context.Subscribe<OnlineEvent>(OnlineNotificationEvents.NotificationChanged, HandleBridgeEvent);
        context.Subscribe<OnlineEvent>(OnlineSocialEvents.FriendshipChanged, HandleBridgeEvent);
        context.Subscribe<OnlineEvent>(OnlineSocialEvents.BlockChanged, HandleBridgeEvent);
        context.Subscribe<OnlineEvent>(OnlineSocialEvents.MuteChanged, HandleBridgeEvent);
        context.Subscribe<OnlineEvent>(OnlineSocialEvents.ReportChanged, HandleBridgeEvent);
        context.Subscribe<OnlineEvent>(OnlineSocialEvents.PunishmentChanged, HandleBridgeEvent);
        context.Subscribe<OnlineEvent>(OnlineSocialEvents.GroupChanged, HandleBridgeEvent);

        // 匹配与队伍（online-matchmaking）。
        context.Subscribe<OnlineEvent>(OnlineMatchEvents.TicketChanged, HandleBridgeEvent);
        context.Subscribe<OnlineEvent>(OnlineMatchEvents.AssignmentCreated, HandleBridgeEvent);
        context.Subscribe<OnlineEvent>(OnlinePartyEvents.PartyChanged, HandleBridgeEvent);
        context.Subscribe<OnlineEvent>(OnlinePartyEvents.PartyInviteChanged, HandleBridgeEvent);

        // 对局（online-match）。
        context.Subscribe<OnlineEvent>(OnlineMatchRuntimeEvents.MatchStateChanged, HandleBridgeEvent);
        context.Subscribe<OnlineEvent>(OnlineMatchRuntimeEvents.MatchSettled, HandleBridgeEvent);

        // 竞技（online-competition，非依赖边——本包不引用其程序集，topic 为全族冻结命名约定字面量，
        // 见各能力包 *Events 常量；字段变化须与其同步）。
        context.Subscribe<OnlineEvent>("online.leaderboard.created", HandleBridgeEvent);
        context.Subscribe<OnlineEvent>("online.leaderboard.scoreupdated", HandleBridgeEvent);
        context.Subscribe<OnlineEvent>("online.season.created", HandleBridgeEvent);
        context.Subscribe<OnlineEvent>("online.season.started", HandleBridgeEvent);
        context.Subscribe<OnlineEvent>("online.season.ended", HandleBridgeEvent);
        context.Subscribe<OnlineEvent>("online.season.settled", HandleBridgeEvent);
        context.Subscribe<OnlineEvent>("online.tournament.created", HandleBridgeEvent);
        context.Subscribe<OnlineEvent>("online.tournament.started", HandleBridgeEvent);
        context.Subscribe<OnlineEvent>("online.tournament.registered", HandleBridgeEvent);
        context.Subscribe<OnlineEvent>("online.tournament.ended", HandleBridgeEvent);
        context.Subscribe<OnlineEvent>("online.tournament.settled", HandleBridgeEvent);

        // Hotfix（本包）。
        context.Subscribe<OnlineEvent>(OnlineHotfixEvents.VersionRegistered, HandleBridgeEvent);
        context.Subscribe<OnlineEvent>(OnlineHotfixEvents.RolledBack, HandleBridgeEvent);
    }

    /// <summary>
    /// 审计桥：把 Online 事件自动转接为统一审计记录（admin 命令 → 审计留痕）——逐语句迁移自壳
    /// <c>OnlineRuntimeHost</c>（OperatorId 来源标识逐字节冻结）。
    /// </summary>
    /// <param name="onlineEvent">Online 事件。</param>
    /// <param name="sanitizer">脱敏器。</param>
    /// <param name="audit">统一审计服务。</param>
    private static void IngestAuditFromEvent(OnlineEvent onlineEvent, OnlineEventSanitizer sanitizer, OnlineAuditService audit)
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
        var _ = audit.IngestAsync(entry);
    }
}
