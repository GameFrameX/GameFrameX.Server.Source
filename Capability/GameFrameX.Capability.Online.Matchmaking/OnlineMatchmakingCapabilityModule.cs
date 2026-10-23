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
/// 匹配能力（大厅 / 票据 / 撮合 / 队伍——Matchmaking+Party 同簇同包）的自装配模块。
/// <para>
/// 维护约束（服务共享协议）：模块无参构造（扫描可发现）；<c>Register</c> 自装配本域——票据 / 大厅 / 队伍三个 store
/// 经 <see cref="CapabilityRegistrationContext.TryGetService{TService}"/> 探测替换缝，未命中构造 InMemory 默认；
/// 跨域硬依赖（事件出口 / 作用域 / 社交裁决 gate 面）经上下文解析（拓扑序保证先序在场）；
/// 对外产物（store 接口 + 票据服务 + 撮合协调器 + 队列观察器）注册回共享面。
/// </para>
/// </summary>
/// <remarks>
/// The self-assembling module of the matchmaking capability (listings / tickets / the matchmaker coordinator /
/// parties — the Matchmaking+Party same-cluster merge). The module is parameterless
/// (discoverable by scanning); <c>Register</c> self-assembles the domain — the ticket / listing / party stores
/// probe the replacement seams with in-memory fallbacks; cross-domain hard dependencies (the event outlet, the
/// scope, the social adjudication gate face) resolve through the context (topological ordering guarantees the
/// producers ran first); outward products (store interfaces, the ticket service, the matchmaker coordinator and
/// the queue observer) are registered back into the shared face.
/// </remarks>
public sealed class OnlineMatchmakingCapabilityModule : ICapabilityModule, IAdminActionContributor
{
    /// <summary>
    /// 匹配能力清单（Id 冻结 = 终态包名；action 集合与幂等包裹位逐字迁移自壳过渡模块）。
    /// </summary>
    public static readonly CapabilityManifest ManifestInstance = new(
        "online-matchmaking",
        "1.0.0",
        new[] { "query_match_queue_overview", "query_match_ticket_failures", "query_match_abnormal_tickets", "cancel_match_ticket" },
        new[] { "online-social" });

    /// <summary>
    /// 匹配票据存储（Register 期解析或构造）。
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

    /// <inheritdoc />
    public CapabilityManifest Manifest
    {
        get { return ManifestInstance; }
    }

    /// <inheritdoc />
    public void Register(CapabilityRegistrationContext context)
    {
        var eventPublisher = context.GetService<IOnlineEventPublisher>();
        var socialGate = context.GetService<IOnlineSocialGate>();
        var scope = context.GetService<OnlineScope>();
        _matchTicketStore = ResolveStore<IOnlineMatchTicketStore>(context, static () => new InMemoryOnlineMatchTicketStore());
        var matchListingStore = ResolveStore<IOnlineMatchListingStore>(context, static () => new InMemoryOnlineMatchListingStore());
        var partyStore = ResolveStore<IOnlinePartyStore>(context, static () => new InMemoryOnlinePartyStore());

        _matchTickets = new OnlineMatchTicketService(_matchTicketStore, eventPublisher);
        _matchmaker = new OnlineMatchmakerCoordinator(_matchTicketStore, _matchTickets, eventPublisher, null, socialGate);
        _matchQueueObserver = new OnlineMatchQueueObserver(_matchTicketStore);
        context.AddService(_matchTickets);
        context.AddService(_matchmaker);
        context.AddService(_matchQueueObserver);
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

    /// <inheritdoc />
    public void RegisterAdminActions(AdminActionRegistry registry)
    {
        new OnlineAdminMatchmakingHandlers(_matchQueueObserver, _matchTicketStore, _matchTickets).Register(registry);
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
