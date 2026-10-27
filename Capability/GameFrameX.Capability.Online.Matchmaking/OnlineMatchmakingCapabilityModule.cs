// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and other rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and other international regulations.
//   使用本项目须严格遵守相关法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with relevant laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please refer to the full license text in the LICENSE file located at the root of the source code.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activity that endangers national security, disrupts social order,
//   侵犯公民合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of citizens as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development,
//   本组织不承担任何责任，开发者应自行承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Hosting.AdminApi;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Events;
using GameFrameX.Capability.Online.Matchmaking.AdminApi;
using GameFrameX.Capability.Online.Matchmaking.Party;
using GameFrameX.Capability.Online.Abstractions.Scope;

namespace GameFrameX.Capability.Online.Matchmaking;

/// <summary>
/// 匹配能力（大厅 / 票据 / 撮合 / 队伍——Matchmaking+Party 同簇同包，C227 蓝图裁定）的自装配模块（C231 P3 迁入，C294 改写三段生命周期）。
/// <para>
/// 维护约束（服务共享协议）：模块无参构造（扫描可发现）；<c>Register</c> 自装配本域——票据 / 大厅 / 队伍三个 store
/// 走「懒默认注册 + 解析」替换缝（族 / 宿主 Override 恒胜出，模块不再写探测-回退分支）；
/// Options 经 <see cref="CapabilityRegistrationContext.GetOptions{TOptions}"/> 取机制合并终值（必填传入服务构造）；
/// 跨域硬依赖（事件出口 / 作用域 / 社交裁决 gate 面）经上下文解析（拓扑序保证先序在场）；
/// 对外产物（store 接口 + 票据服务 + 撮合协调器 + 队列观察器 + 队伍服务与命名成员资格探针）注册回共享面；
/// 队伍在线探针是非直接依赖扩展点，Activate 期经 <c>GetServices</c> 枚举后注入。
/// </para>
/// </summary>
/// <remarks>
/// The self-assembling module of the matchmaking capability (listings / tickets / the matchmaker coordinator /
/// parties — the Matchmaking+Party same-cluster merge ruled by the C227 blueprint), rewritten for the
/// three-phase lifecycle in C294. The module is parameterless (discoverable by scanning);
/// <c>Register</c> self-assembles the domain — the ticket / listing / party stores ride the
/// lazy-default-then-resolve seam (family / host overrides always win); options come from the mechanism merge;
/// cross-domain hard dependencies (the event outlet, the scope, the social adjudication gate face) resolve
/// through the context; outward products (store interfaces, the ticket service, the matchmaker coordinator,
/// the queue observer, the party service and its named membership probe) are registered back into the shared
/// face; the party presence probe is a non-direct extension point enumerated and injected during activation.
/// </remarks>
public sealed class OnlineMatchmakingCapabilityModule : ICapabilityModule, IAdminActionContributor
{
    /// <summary>
    /// 匹配能力清单（Id 冻结 = 终态包名；action 集合与幂等包裹位逐字迁移自壳过渡模块；
    /// 必选依赖 online-social；Options 根类型 = <see cref="OnlineMatchmakingOptions"/>）。
    /// </summary>
    public static readonly CapabilityManifest ManifestInstance = new(
        "online-matchmaking",
        "1.0.0",
        new[] { "query_match_queue_overview", "query_match_ticket_failures", "query_match_abnormal_tickets", "cancel_match_ticket" },
        new[] { "online-social" },
        null,
        typeof(OnlineMatchmakingOptions));

    /// <summary>
    /// 匹配票据存储（Register 期解析）。
    /// </summary>
    private IOnlineMatchTicketStore _matchTicketStore;

    /// <summary>
    /// 匹配票据服务（Register 期构造）。
    /// </summary>
    private OnlineMatchTicketService _matchTickets;

    /// <summary>
    /// 撮合协调器（Register 期构造）。
    /// </summary>
    private OnlineMatchmakerCoordinator _matchmaker;

    /// <summary>
    /// 匹配队列观察器（Register 期构造）。
    /// </summary>
    private OnlineMatchQueueObserver _matchQueueObserver;

    /// <summary>
    /// 队伍服务（Register 期构造；Activate 期回填在线探针）。
    /// </summary>
    private OnlinePartyService _partyService;

    /// <summary>
    /// 获取在线撮合能力的静态清单（单例实例）。
    /// </summary>
    /// <remarks>
    /// Gets the static manifest singleton of the online matchmaking capability.
    /// </remarks>
    /// <value>能力清单单例 / Capability manifest singleton</value>
    public CapabilityManifest Manifest
    {
        get { return ManifestInstance; }
    }

    /// <summary>
    /// 注册撮合能力：注册内存票据存储、对局列表存储与队伍存储，构造并注册票据服务、撮合协调器与队列观察器；
    /// 构造队伍服务（在线探针暂留空，激活期回填）并以命名 <c>party</c> 注册为频道成员资格探针；
    /// 注册撮合执行与过期票据清理两个 tick 任务。
    /// </summary>
    /// <remarks>
    /// Registers in-memory ticket, listing and party stores, builds and registers the ticket service, matchmaker coordinator and queue observer,
    /// builds the party service (presence probe attached later at activation) and registers it as the named <c>party</c> channel membership probe,
    /// and adds the matchmaker run and expired ticket sweep tick jobs.
    /// </remarks>
    /// <param name="context">能力注册上下文 / Capability registration context</param>
    public void Register(CapabilityRegistrationContext context)
    {
        var options = context.GetOptions<OnlineMatchmakingOptions>();
        var eventPublisher = context.GetService<IOnlineEventPublisher>();
        var socialGate = context.GetService<IOnlineSocialGate>();
        var scope = context.GetService<OnlineScope>();
        context.AddService<IOnlineMatchEventOutbox>(static _ => new InMemoryOnlineMatchEventOutbox());
        var matchEventOutbox = context.GetService<IOnlineMatchEventOutbox>();
        context.AddService<IOnlineMatchTicketStore>(static _ => new InMemoryOnlineMatchTicketStore());
        _matchTicketStore = context.GetService<IOnlineMatchTicketStore>();
        context.AddService<IOnlineMatchListingStore>(static _ => new InMemoryOnlineMatchListingStore());
        var matchListingStore = context.GetService<IOnlineMatchListingStore>();
        context.AddService<IOnlinePartyStore>(static _ => new InMemoryOnlinePartyStore());
        var partyStore = context.GetService<IOnlinePartyStore>();

        _matchTickets = new OnlineMatchTicketService(_matchTicketStore, eventPublisher, options.Matchmaker);
        _matchmaker = new OnlineMatchmakerCoordinator(_matchTicketStore, _matchTickets, eventPublisher, options.Matchmaker, socialGate, matchEventOutbox);
        _matchQueueObserver = new OnlineMatchQueueObserver(_matchTicketStore);
        context.AddService(_matchTickets);
        context.AddService(_matchmaker);
        context.AddService(_matchQueueObserver);

        // 队伍域装配（此前壳组合根未装配）：构造期在线探针留空（Foundation 非直接依赖，Register 期不可越界解析），
        // Activate 期经 GetServices 枚举扩展点后 AttachPresenceProbe 回填；命名 "party" 成员资格探针供聊天域队伍频道判定。
        _partyService = new OnlinePartyService(partyStore, eventPublisher, presenceProbe: null, socialGate: socialGate);
        context.AddService(_partyService);
        context.AddService("party", (IOnlineChannelMembershipProbe)_partyService);
        _ = matchListingStore;

        context.AddTickJob("matchmaker-run", async ct =>
        {
            await _matchmaker.RunOnceAsync(scope.TenantId, scope.AppId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), ct).ConfigureAwait(false);
        });
        context.AddTickJob("match-ticket-expiry-sweep", async ct =>
        {
            await _matchTickets.SweepExpiredAsync(scope.TenantId, scope.AppId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), ct).ConfigureAwait(false);
        });
    }

    /// <summary>
    /// 激活撮合能力：枚举队伍在线探针扩展点，存在实现时将第一个回填到队伍服务；无实现则保持探针为空。
    /// </summary>
    /// <remarks>
    /// Enumerates party presence probe implementations and attaches the first one to the party service; leaves it unset when none exist.
    /// </remarks>
    /// <param name="context">能力激活上下文 / Capability activation context</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>已完成的任务，激活过程为同步操作 / A completed task, as activation is synchronous</returns>
    public Task ActivateAsync(CapabilityActivationContext context, CancellationToken cancellationToken)
    {
        // 在线事实探针扩展点注入：实现常由非直接依赖能力（online-foundation）提供，只能 Activate 期枚举。
        var presenceProbe = context.Services.GetServices<IOnlinePartyPresenceProbe>();
        if (presenceProbe.Count > 0)
        {
            _partyService.AttachPresenceProbe(presenceProbe[0]);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 停用撮合能力：本实现不做任何处理，因为存储为内存实现、tick 任务由宿主管理，模块不持有需要释放的资源。
    /// </summary>
    /// <remarks>
    /// Performs no work, because stores are in-memory, tick jobs are host-managed and the module holds no resources to release.
    /// </remarks>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>已完成的任务 / A completed task</returns>
    public Task DeactivateAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// 注册撮合管理动作：基于队列观察器、票据存储与票据服务构造撮合管理处理器并注册到管理动作注册表。
    /// </summary>
    /// <remarks>
    /// Registers matchmaking admin handlers built from the queue observer, ticket store and ticket service.
    /// </remarks>
    /// <param name="registry">管理动作注册表 / Admin action registry</param>
    public void RegisterAdminActions(AdminActionRegistry registry)
    {
        new OnlineAdminMatchmakingHandlers(_matchQueueObserver, _matchTicketStore, _matchTickets).Register(registry);
    }
}
