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
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://www.cnb.cool/GameFrameX
//   CNB Repository: https://www.cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Hosting.AdminApi;
using GameFrameX.Capability.Online.Assets;
using GameFrameX.Capability.Online.Abstractions.Events;
using GameFrameX.Capability.Online.Foundation.Identity;
using GameFrameX.Capability.Online.Foundation.Presence;
using GameFrameX.Capability.Online.Foundation.Session;
using GameFrameX.Capability.Online.Abstractions.Idempotency;
using GameFrameX.Capability.Online.Match;
using GameFrameX.Capability.Online.Matchmaking;
using GameFrameX.Capability.Online.Operations.AdminApi;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Operations.Audit;
using GameFrameX.Capability.Online.Operations.HotfixRollback;
using GameFrameX.Capability.Online.Operations.Overview;
using GameFrameX.Capability.Online.Operations.Timeline;
using GameFrameX.Capability.Online.Matchmaking.Party;
using GameFrameX.Capability.Online.Social;
using GameFrameX.Foundation.Idempotency;

namespace GameFrameX.Capability.Online.Operations;

/// <summary>
/// 运营聚合能力（审计 / 游戏事件 / Hotfix 回滚 / 总览 / 时间线 + C105 事件桥/审计桥）的自装配模块
/// （C232 P5 迁入）。
/// <para>
/// 维护约束（服务共享协议）：模块无参构造（扫描可发现）；<c>Register</c> 自装配本域——审计服务经具型探测缝
/// （组合根预置命中共享同一实例——Assets 模块的审计留痕与桥的审计写入保持同源；未命中构造 InMemory 默认并回注
/// 具型 + <see cref="IOnlineAuditReader"/> 读面）；Hotfix 回滚依赖幂等基建（组合根预置），缺席不装配（壳时代
/// 「下游置 null」的模块化等价）；总览 / 时间线探测叶子产物（matchmaking / party / match / assets / social），
/// 缺席置 null（handler 不可达语义对齐壳时代）；地基三 store（presence / session / identity）经替换缝探测，
/// 未命中构造 InMemory 默认。
/// </para>
/// <para>
/// 事件桥 / 审计桥（C105 语义不变红线）：桥体逐语句迁移自壳 <c>OnlineRuntimeHost.StartAsync</c>——
/// 订阅 Foundation 事件总线，信封解映射 + 去重后做聊天审计投影与统一审计转接，投影侧异常吞掉不阻断业务分发；
/// 订阅时点从启动期提前到装配期（机制无 Start 钩子，行为等价已裁定披露）。
/// </para>
/// </summary>
/// <remarks>
/// The self-assembling module of the operations capability (audit, game events, hotfix rollback, overview,
/// timeline and the C105 event/audit bridges; migrated by C232 P5). The module has a parameterless
/// constructor (discoverable by scanning); <c>Register</c> self-assembles the domain — the audit service
/// resolves through a typed probe seam (sharing the composition-root instance so the Assets module's audit
/// trail and the bridge's audit ingestion stay on the same instance, with an in-memory fallback registered
/// back under both the concrete type and the <see cref="IOnlineAuditReader"/> read face); the hotfix
/// rollback service depends on the idempotency infrastructure and stays unassembled when absent (the
/// modular equivalent of the shell-era null downstream); the overview / timeline services probe leaf
/// products and stay null when absent; the three foundation stores probe replacement seams with
/// in-memory fallbacks. The event/audit bridges migrate statement-by-statement from the shell's
/// <c>StartAsync</c> with the subscription point moved from start-up to assembly time (adjudicated as
/// behavior-equivalent).
/// </remarks>
public sealed class OnlineOperationsCapabilityModule : ICapabilityModule, IAdminActionContributor
{
    /// <summary>
    /// 运营聚合能力清单（Id 冻结 = 终态包名；action 集合与幂等包裹位逐字迁移自壳过渡模块）。
    /// </summary>
    public static readonly CapabilityManifest ManifestInstance = new(
        "online-operations",
        "1.0.0",
        new[] { "query_online_overview", "query_player_timeline", "query_online_audit_events" },
        new[] { "online-foundation", "online-assets", "online-social", "online-match", "online-matchmaking" });

    /// <summary>
    /// 统一审计服务（Register 期解析；admin 查询面与桥的写入面同源）。
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

    /// <inheritdoc />
    public CapabilityManifest Manifest
    {
        get { return ManifestInstance; }
    }

    /// <inheritdoc />
    public void Register(CapabilityRegistrationContext context)
    {
        var eventPublisher = context.GetService<IOnlineEventPublisher>();

        // 审计（具型探测缝——组合根预置命中共享同一实例；未命中构造 InMemory 默认并回注具型 + 读面）。
        OnlineAuditService audit;
        if (context.TryGetService(out OnlineAuditService preloadedAudit))
        {
            audit = preloadedAudit;
        }
        else
        {
            var auditStore = ResolveStore<IOnlineAuditStore>(context, static () => new InMemoryOnlineAuditStore());
            audit = new OnlineAuditService(auditStore);
            context.AddService(audit);
            context.AddService<IOnlineAuditReader>(audit);
        }

        _audit = audit;

        // Hotfix 回滚（幂等基建缺席时不装配——壳「下游置 null」的模块化等价；执行器与制品获取器为可选注入位，C248）。
        if (context.TryGetService(out OnlineIdempotencyService idempotency))
        {
            var hotfixVersionStore = ResolveStore<IOnlineHotfixVersionStore>(context, static () => new InMemoryOnlineHotfixVersionStore());
            var rollbackExecutor = context.TryGetService(out IOnlineHotfixRollbackExecutor hotfixRollbackExecutor) ? hotfixRollbackExecutor : null;
            var artifactFetcher = context.TryGetService(out IOnlineHotfixArtifactFetcher hotfixArtifactFetcher) ? hotfixArtifactFetcher : null;
            context.AddService(new OnlineHotfixRollbackService(hotfixVersionStore, idempotency, audit, rollbackExecutor, artifactFetcher, eventPublisher));
        }

        // 总览 / 时间线（叶子产物探测；地基三 store 经替换缝探测，未命中构造 InMemory 默认）。
        var hasAssetQuery = context.TryGetService(out OnlineAssetQueryService assetQuery);
        var hasSocialGraphStore = context.TryGetService(out IOnlineSocialGraphStore socialGraphStore);
        var hasMatchTicketStore = context.TryGetService(out IOnlineMatchTicketStore matchTicketStore);
        var hasPartyStore = context.TryGetService(out IOnlinePartyStore partyStore);
        var hasMatchActorStore = context.TryGetService(out IOnlineMatchActorStore matchActorStore);
        var presenceStore = ResolveStore<IOnlinePresenceStore>(context, static () => new InMemoryOnlinePresenceStore());
        var sessionStore = ResolveStore<IOnlineSessionStore>(context, static () => new InMemoryOnlineSessionStore());
        var identityStore = ResolveStore<IOnlineIdentityStore>(context, static () => new InMemoryOnlineIdentityStore());
        _overview = hasMatchTicketStore && hasPartyStore && hasMatchActorStore
            ? new OnlineOverviewService(presenceStore, sessionStore, partyStore, matchTicketStore, matchActorStore)
            : null;
        _timeline = hasAssetQuery && hasSocialGraphStore && hasMatchActorStore
            ? new OnlinePlayerTimelineService(identityStore, sessionStore, assetQuery, matchActorStore, socialGraphStore)
            : null;

        // 事件桥 / 审计桥（C105 语义不变红线；桥体逐语句迁移自壳 StartAsync，订阅时点装配期提前已裁定披露）。
        var eventBus = context.GetService<InMemoryEventPublisher>();
        var chatAudit = context.TryGetService(out OnlineChatAuditProjection chatAuditProjection) ? chatAuditProjection : null;
        var consumer = new OnlineEventConsumer(new InMemoryEventDeduplicator());
        var sanitizer = new OnlineEventSanitizer();
        eventBus.Subscribe(envelope =>
        {
            try
            {
                var onlineEvent = OnlineEventEnvelopeMapper.FromEnvelope(envelope);
                if (!consumer.TryConsume(onlineEvent))
                {
                    return;
                }

                chatAudit?.OnEvent(onlineEvent);
                IngestAuditFromEvent(onlineEvent, sanitizer, audit);
            }
            catch (Exception)
            {
                // 审计桥异常不得阻断业务事件分发（InMemoryEventPublisher 异常策略是向上传播，
                // 这里吞掉投影侧异常以保证宿主写链路不受投影缺陷影响；投影片段丢失由审计查询面暴露）。
            }
        });
    }

    /// <inheritdoc />
    public void RegisterAdminActions(AdminActionRegistry registry)
    {
        OnlineAdminConsoleHandlers.RegisterConsoleActions(registry, _overview, _timeline, _audit);
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
