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
using GameFrameX.Capability.Online.Abstractions.Events;
using GameFrameX.Capability.Online.Assets;
using GameFrameX.Capability.Online.Match.AdminApi;

namespace GameFrameX.Capability.Online.Match;

/// <summary>
/// 对局能力（权威对局 Actor / 重连 / 幂等结算 / 样例玩法）的自装配模块（C232 P4 迁入）。
/// <para>
/// 维护约束（服务共享协议）：模块无参构造（扫描可发现）；<c>Register</c> 自装配本域——对局 Actor / 结果
/// 两个 store 经 <see cref="CapabilityRegistrationContext.TryGetService{TService}"/> 探测替换缝，未命中构造
/// InMemory 默认；跨域硬依赖（事件出口）经上下文解析；对外产物（store 接口 + 对局运行时）注册回共享面
/// （Timeline / Overview 消费 <see cref="IOnlineMatchActorStore"/> 读取面）。
/// </para>
/// </summary>
/// <remarks>
/// The self-assembling module of the match capability (authoritative match actor, reconnect, idempotent
/// settlement and the sample game; migrated by C232 P4). The module has a parameterless constructor
/// (discoverable by scanning); <c>Register</c> self-assembles the domain — the match actor / result stores
/// probe the replacement seams with in-memory fallbacks; the cross-domain hard dependency (the event outlet)
/// resolves through the context; outward products (store interfaces and the match runtime) are registered
/// back into the shared face (Timeline / Overview consume the <see cref="IOnlineMatchActorStore"/> read face).
/// </remarks>
public sealed class OnlineMatchCapabilityModule : ICapabilityModule, IAdminActionContributor
{
    /// <summary>
    /// 对局能力清单（Id 冻结 = 终态包名；action 集合与幂等包裹位逐字迁移自壳过渡模块）。
    /// </summary>
    public static readonly CapabilityManifest ManifestInstance = new(
        "online-match",
        "1.0.0",
        new[] { "end_abnormal_match" },
        new[] { "online-assets" });

    /// <summary>
    /// 对局运行时（Register 期构造；样例玩法随迁移注册，与壳时代装配语句逐语句等价）。
    /// </summary>
    private OnlineMatchRuntime _matchRuntime;

    /// <summary>
    /// 结算管线（Register 期构造；tick 驱动「结算 → 发奖 → 失败重投」，C245）。
    /// </summary>
    private OnlineMatchSettlementPipeline _settlementPipeline;

    /// <summary>
    /// 对局桥服务（Register 期构造；对 <c>IOnlineMatchGameBridge</c> 契约面的本域实现，C245）。
    /// </summary>
    private OnlineMatchGameBridgeService _bridgeService;

    /// <inheritdoc />
    public CapabilityManifest Manifest
    {
        get { return ManifestInstance; }
    }

    /// <inheritdoc />
    public void Register(CapabilityRegistrationContext context)
    {
        var eventPublisher = context.GetService<IOnlineEventPublisher>();
        var matchActorStore = ResolveStore<IOnlineMatchActorStore>(context, static () => new InMemoryOnlineMatchActorStore());
        var matchResultStore = ResolveStore<IOnlineMatchResultStore>(context, static () => new InMemoryOnlineMatchResultStore());

        var rpsGame = new OnlineRockPaperScissorsGame();
        _matchRuntime = new OnlineMatchRuntime(matchActorStore, eventPublisher);
        _matchRuntime.RegisterGame(rpsGame);
        context.AddService(_matchRuntime);
        context.AddTickJob("match-tick", ct => _matchRuntime.TickAllAsync(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), ct));

        // 结算编排（C245）：settlement + 可空 dispatcher（assets 缺席仅结算）+ tick 重投；桥服务注册回共享面。
        var grantService = context.TryGetService(out OnlineGrantService grant) ? grant : null;
        var settlementService = new OnlineMatchSettlementService(_matchRuntime, matchResultStore, eventPublisher);
        var dispatcher = grantService == null ? null : new OnlineMatchResultDispatcher(grantService);
        _settlementPipeline = new OnlineMatchSettlementPipeline(_matchRuntime, settlementService, dispatcher);
        context.AddTickJob("match-settlement-tick", ct => _settlementPipeline.TickAsync(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), ct));

        _bridgeService = new OnlineMatchGameBridgeService(_matchRuntime, rpsGame);
        context.AddService<IOnlineMatchGameBridge>(_bridgeService);
    }

    /// <inheritdoc />
    public void RegisterAdminActions(AdminActionRegistry registry)
    {
        OnlineAdminMatchHandlers.RegisterEndAbnormalMatch(registry);
        OnlineAdminMatchSettlementHandlers.RegisterMatchSettlementQuery(registry, _settlementPipeline);
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
