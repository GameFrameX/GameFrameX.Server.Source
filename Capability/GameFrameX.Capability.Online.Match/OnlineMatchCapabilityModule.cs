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
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Matchmaking;
using GameFrameX.Capability.Online.Abstractions.Events;
using GameFrameX.Capability.Online.Assets;
using GameFrameX.Capability.Online.Match.AdminApi;
using GameFrameX.Core.Session;
using GameFrameX.Localization;
using GameFrameX.Proto.Proto;

namespace GameFrameX.Capability.Online.Match;

/// <summary>
/// 对局能力（权威对局 Actor / 重连 / 幂等结算 / 样例玩法）的自装配模块（C232 P4 迁入，C294 改写三段生命周期）。
/// <para>
/// 维护约束（服务共享协议）：模块无参构造（扫描可发现）；<c>Register</c> 自装配本域——对局 Actor / 结果
/// 两个 store 走「懒默认注册 + 解析」替换缝（族 / 宿主 Override 恒胜出）；Options 经
/// <see cref="CapabilityRegistrationContext.GetOptions{TOptions}"/> 取机制合并终值（必填传入运行时构造）；
/// 可选依赖（online-assets 的发奖、online-matchmaking 的票据服务与存储）经 <c>GetOptionalService</c> 消费——
/// 缺席 = 不装配对应链路且不报错（manifest 已声明可选缺席为合法态）；对局桥 / 匹配域桥迁 Activate 期发布；
/// 撮合成功通知订阅迁 Register 期机制事件总线（域服务在场才订阅；订阅体异常由机制记日志继续，不再自吞）。
/// </para>
/// </summary>
/// <remarks>
/// The self-assembling module of the match capability (authoritative match actor, reconnect, idempotent
/// settlement and the sample game; migrated by C232 P4, rewritten for the three-phase lifecycle in C294).
/// The module has a parameterless constructor (discoverable by scanning); <c>Register</c> self-assembles the
/// domain — the match actor / result stores ride the lazy-default-then-resolve seam; options come from the
/// mechanism merge; optional dependencies (the assets grant and the matchmaking ticket service and store) are consumed via
/// <c>GetOptionalService</c> — absence means the corresponding chain is simply not assembled; the game and
/// match-domain bridges publish during activation; the match-made notification subscription moved onto the
/// mechanism event bus during Register (only when the domain service is assembled).
/// </remarks>
public sealed class OnlineMatchCapabilityModule : ICapabilityModule, IAdminActionContributor
{
    /// <summary>
    /// 对局能力清单（Id 冻结 = 终态包名；action 集合与幂等包裹位逐字迁移自壳过渡模块；
    /// 无必选依赖；可选 online-assets / online-matchmaking，缺席按本地化键披露缺失功能；
    /// Options 根类型 = <see cref="OnlineMatchOptions"/>）。
    /// </summary>
    public static readonly CapabilityManifest ManifestInstance = new(
        "online-match",
        "1.0.0",
        new[] { "end_abnormal_match", "query_match_settlement" },
        null,
        new[]
        {
            new CapabilityOptionalDependency("online-assets", Keys.Capability.OnlineModule.Match.OptionalAssetsAbsent),
            new CapabilityOptionalDependency("online-matchmaking", Keys.Capability.OnlineModule.Match.OptionalMatchmakingAbsent),
        },
        typeof(OnlineMatchOptions));

    /// <summary>
    /// 对局运行时（Register 期构造；样例玩法随迁移注册，与壳时代装配语句逐语句等价）。
    /// </summary>
    private OnlineMatchRuntime _matchRuntime;

    /// <summary>
    /// 结算管线（Register 期构造；tick 驱动「结算 → 发奖 → 失败重投」，C245）。
    /// </summary>
    private OnlineMatchSettlementPipeline _settlementPipeline;

    /// <summary>
    /// 对局桥服务（Register 期构造；对 <c>IOnlineMatchGameBridge</c> 契约面的本域实现，Activate 期发布桥，C245）。
    /// </summary>
    private OnlineMatchGameBridgeService _bridgeService;

    /// <summary>
    /// 匹配域桥服务（Register 期构造；对 <c>IOnlineMatchDomain</c> 契约面的本域实现，Activate 期发布桥，C246；
    /// online-matchmaking 缺席时为 null——不装配不报错，420 协议面经桥得显式失败）。
    /// </summary>
    private IOnlineMatchDomain _matchDomain;

    /// <summary>
    /// 获取在线对局能力的静态清单（单例实例），声明能力标识、依赖与可选的撮合能力。
    /// </summary>
    /// <remarks>
    /// Gets the static manifest singleton of the online match capability, declaring its identity, dependencies and the optional matchmaking capability.
    /// </remarks>
    /// <value>能力清单单例 / Capability manifest singleton</value>
    public CapabilityManifest Manifest
    {
        get { return ManifestInstance; }
    }

    /// <summary>
    /// 注册对局能力：注册内存对局 Actor 存储与结果存储，构造对局运行时并注册石头剪刀布玩法与对局 tick 任务；
    /// 装配结算服务与结算管线（资产发放服务缺席时只结算不派发）并注册结算 tick 任务；构造并注册对局桥服务；
    /// 仅当票据服务与票据存储均在场时才装配匹配域服务并订阅撮合分配事件，缺席时不装配也不报错。
    /// </summary>
    /// <remarks>
    /// Registers in-memory match actor and result stores, builds the match runtime with the rock-paper-scissors game and a match tick job,
    /// wires the settlement service and pipeline (settle-only when the grant service is absent) with a settlement tick job, registers the game bridge service,
    /// and only when ticket service and ticket store are present builds the match domain service and subscribes to assignment-created events; otherwise it silently skips.
    /// </remarks>
    /// <param name="context">能力注册上下文 / Capability registration context</param>
    public void Register(CapabilityRegistrationContext context)
    {
        var options = context.GetOptions<OnlineMatchOptions>();
        var eventPublisher = context.GetService<IOnlineEventPublisher>();
        context.AddService<IOnlineMatchActorStore>(static _ => new InMemoryOnlineMatchActorStore());
        var matchActorStore = context.GetService<IOnlineMatchActorStore>();
        context.AddService<IOnlineMatchResultStore>(static _ => new InMemoryOnlineMatchResultStore());
        var matchResultStore = context.GetService<IOnlineMatchResultStore>();

        var rpsGame = new OnlineRockPaperScissorsGame();
        _matchRuntime = new OnlineMatchRuntime(matchActorStore, eventPublisher, options.Runtime);
        _matchRuntime.RegisterGame(rpsGame);
        context.AddService(_matchRuntime);
        context.AddTickJob("match-tick", ct => _matchRuntime.TickAllAsync(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), ct));

        // 结算编排（C245）：settlement + 可空 dispatcher（assets 缺席仅结算）+ tick 重投；桥服务注册回共享面。
        var grantService = context.GetOptionalService<OnlineGrantService>();
        var settlementService = new OnlineMatchSettlementService(_matchRuntime, matchResultStore, eventPublisher);
        var dispatcher = grantService == null ? null : new OnlineMatchResultDispatcher(grantService);
        _settlementPipeline = new OnlineMatchSettlementPipeline(_matchRuntime, settlementService, dispatcher, options.Settlement);
        context.AddTickJob("match-settlement-tick", ct => _settlementPipeline.TickAsync(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), ct));

        _bridgeService = new OnlineMatchGameBridgeService(_matchRuntime, rpsGame);
        context.AddService<IOnlineMatchGameBridge>(_bridgeService);

        // 匹配域桥接票据入队 / 取消 / 状态与对局入场；撮合由匹配能力的定时任务统一驱动。
        // 票据服务与票据存储按可选依赖语义解析（manifest 已声明 online-matchmaking 可选）；
        // 在场才装配域服务并订阅撮合分配通知；缺席 = 不装配不报错——420 协议面经桥得显式失败。
        var domainTickets = context.GetOptionalService<OnlineMatchTicketService>();
        var domainTicketStore = context.GetOptionalService<IOnlineMatchTicketStore>();
        if (domainTickets != null && domainTicketStore != null)
        {
            _matchDomain = new OnlineMatchDomainService(domainTicketStore, domainTickets, _matchRuntime, rpsGame, options.Registry);
            context.AddService<IOnlineMatchDomain>(_matchDomain);
            context.Subscribe<OnlineEvent>(OnlineMatchEvents.AssignmentCreated, onlineEvent => HandleMatchAssignmentEvent(onlineEvent, _matchDomain));
        }
    }

    /// <summary>
    /// 激活对局能力：始终发布对局桥；仅当匹配域服务已在注册期装配时才发布匹配域桥。
    /// </summary>
    /// <remarks>
    /// Always publishes the match game bridge; publishes the match domain bridge only when the domain service was assembled during registration.
    /// </remarks>
    /// <param name="context">能力激活上下文 / Capability activation context</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>已完成的任务，激活过程为同步操作 / A completed task, as activation is synchronous</returns>
    public Task ActivateAsync(CapabilityActivationContext context, CancellationToken cancellationToken)
    {
        // 桥发布迁 Activate 期（C294）：对局桥恒发布；匹配域桥仅在场发布（缺席态由桥显式失败承载）。
        context.Bridges.Publish<IOnlineMatchGameBridge>(_bridgeService);
        if (_matchDomain != null)
        {
            context.Bridges.Publish<IOnlineMatchDomain>(_matchDomain);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 停用对局能力：本实现不做任何处理，因为桥与 tick 任务由宿主统一管理，模块自身不持有需要释放的资源。
    /// </summary>
    /// <remarks>
    /// Performs no work, because bridges and tick jobs are owned by the host and the module holds no resources to release.
    /// </remarks>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>已完成的任务 / A completed task</returns>
    public Task DeactivateAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// 注册对局相关管理动作：异常结束对局，以及基于结算管线的对局结算查询。
    /// </summary>
    /// <remarks>
    /// Registers match admin actions: abnormal match termination and match settlement query backed by the settlement pipeline.
    /// </remarks>
    /// <param name="registry">管理动作注册表 / Admin action registry</param>
    public void RegisterAdminActions(AdminActionRegistry registry)
    {
        OnlineAdminMatchHandlers.RegisterEndAbnormalMatch(registry);
        OnlineAdminMatchSettlementHandlers.RegisterMatchSettlementQuery(registry, _settlementPipeline);
    }

    /// <summary>
    /// 撮合分配事件回调体（原 Launcher <c>OnlineMatchDomainBridgeWiring.HandleMatchAssignmentEvent</c> 迁入，C294）：
    /// <c>online.match.assignmentcreated</c> → 域服务解析数值对局标识 → 向双方在线会话 best-effort 推送
    /// <see cref="NotifyMatchMade"/>；过滤 / 校验失败即静默返回；离线方无会话跳过（等待方经状态轮询兜底）。
    /// <para>
    /// 维护约束：同步订阅体（机制事件总线约定）；订阅体异常由机制记日志后继续分发，此处不得再自吞 try/catch。
    /// </para>
    /// </summary>
    /// <param name="onlineEvent">机制事件载荷。</param>
    /// <param name="domain">匹配域契约面。</param>
    private static void HandleMatchAssignmentEvent(OnlineEvent onlineEvent, IOnlineMatchDomain domain)
    {
        var payload = OnlineMatchEvents.ReadAssignmentCreated(onlineEvent);
        if (payload == null || string.IsNullOrEmpty(payload.AssignmentId))
        {
            return;
        }

        var resolved = domain.ResolveAssignmentAsync(onlineEvent.TenantId, onlineEvent.AppId, payload.AssignmentId).GetAwaiter().GetResult();
        if (!resolved.IsSuccess || resolved.Data == null || resolved.Data.MatchId == 0)
        {
            return;
        }

        if (resolved.Data.PlayerIds == null)
        {
            return;
        }

        var notify = new NotifyMatchMade { MatchId = resolved.Data.MatchId, Mode = resolved.Data.Mode };
        foreach (var playerId in resolved.Data.PlayerIds)
        {
            var session = PlayerSessionManager.Instance.GetByPlayerId(playerId);
            if (session == null)
            {
                // 离线方静默跳过：best-effort 推送，等待方经状态轮询兜底。
                continue;
            }

            session.WriteAsync(notify).GetAwaiter().GetResult();
        }
    }
}
