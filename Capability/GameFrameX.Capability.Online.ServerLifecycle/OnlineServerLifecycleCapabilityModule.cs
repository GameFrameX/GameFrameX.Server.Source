// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and other rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Capability.Online.ServerLifecycle;

using System;
using GameFrameX.Capability.Hosting.AdminApi;
using System.Threading.Tasks;
using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Online.Abstractions.Events;
using GameFrameX.Capability.Online.ServerLifecycle;
using GameFrameX.Capability.Online.ServerLifecycle.AdminApi;
using GameFrameX.Capability.Online.ServerLifecycle.ServerMerge;

/// <summary>
/// 区服生命周期能力（开服锚点 / 开服日历 / ServerOpened 事件 / 合服原语）的自装配模块
/// （Standard 第 12 席：单职责、按需引入——不要合服的部署不装本包）。
/// <para>
/// 维护约束（服务共享协议）：模块无参构造（扫描可发现）；<see cref="Register"/> 自装配本域——
/// 开服锚点 store、归并计划 store、PlayerRoute 重指向端口、区服域行键重写端口、榜单封档端口、
/// 在途对局强制结算端口均以懒默认描述符注册（族 / 宿主覆盖恒胜出：发现层 / 持久化层 / 榜单域 /
/// 对局域部署绑定直接覆盖默认空实现）；合服参与者 = 内置五步 + 玩法侧经
/// <c>AddService&lt;IServerMergeParticipant&gt;(name, instance)</c> 追加（named 注册按名聚合，执行序 = 注册序）。
/// </para>
/// </summary>
public sealed class OnlineServerLifecycleCapabilityModule : ICapabilityModule, IAdminActionContributor
{
    /// <summary>
    /// 区服生命周期能力清单（Id 冻结 = 终态包名；admin actions：register_server_open /
    /// query_server_calendar / plan_server_merge / dry_run_server_merge / execute_server_merge）。
    /// </summary>
    public static readonly CapabilityManifest ManifestInstance = new(
        "online-server-lifecycle",
        "1.0.0",
        new[] { "register_server_open", "query_server_calendar", "plan_server_merge", "dry_run_server_merge", "execute_server_merge" },
        null,
        null,
        typeof(OnlineServerLifecycleOptions));

    /// <summary>
    /// 合服服务（Register 期构造；admin 面 handler 取用）。
    /// </summary>
    private OnlineServerMergeService _mergeService;

    /// <summary>
    /// 开服日历服务（Register 期构造；admin 面 handler 取用）。
    /// </summary>
    private OnlineServerLifecycleService _calendarService;

    /// <summary>
    /// 获取区服生命周期能力清单，固定返回静态实例 <see cref="ManifestInstance"/>。
    /// </summary>
    public CapabilityManifest Manifest
    {
        get { return ManifestInstance; }
    }

    /// <summary>
    /// 读取选项，懒默认注册本域 store / 端口并解析生效实例，构造开服日历服务与合服服务，
    /// 聚合内置参与者与玩法侧命名注册的 <see cref="IServerMergeParticipant"/>。
    /// </summary>
    /// <param name="context">能力注册上下文。</param>
    public void Register(CapabilityRegistrationContext context)
    {
        var options = context.GetOptions<OnlineServerLifecycleOptions>();
        var eventPublisher = context.GetService<IOnlineEventPublisher>();

        // 懒默认描述符（族 / 宿主覆盖恒胜出）。
        context.AddService<IOnlineServerCalendarStore>(static _ => new InMemoryOnlineServerCalendarStore());
        context.AddService<IOnlineServerMergeStore>(static _ => new InMemoryOnlineServerMergeStore());
        context.AddService<IOnlinePlayerRouteRewriter>(static _ => new NullOnlinePlayerRouteRewriter());
        context.AddService<IOnlineServerMergeRowRewriter>(static _ => new NullOnlineServerMergeRowRewriter());
        context.AddService<IOnlineLeaderboardSealTarget>(static _ => new NullOnlineLeaderboardSealTarget());
        context.AddService<IOnlineInFlightMatchSettler>(static _ => new NullOnlineInFlightMatchSettler());

        var calendarStore = context.GetService<IOnlineServerCalendarStore>();
        var lifecycleService = new OnlineServerLifecycleService(calendarStore, eventPublisher);
        context.AddService<IOnlineServerCalendar>(lifecycleService);
        context.AddService(lifecycleService);
        _calendarService = lifecycleService;

        var participants = new List<IServerMergeParticipant>
        {
            new OnlineServerAnchorMergeParticipant(calendarStore),
            new OnlineLeaderboardSealMergeParticipant(context.GetService<IOnlineLeaderboardSealTarget>()),
            new OnlineInFlightMatchSettlementMergeParticipant(context.GetService<IOnlineInFlightMatchSettler>()),
            new OnlinePlayerRouteMergeParticipant(context.GetService<IOnlinePlayerRouteRewriter>()),
            new OnlineServerRowKeyRewriteParticipant(context.GetService<IOnlineServerMergeRowRewriter>(), options.Merge.RowRewriteBatchSize),
        };
        // 玩法侧扩展：直接以宿主 / 族覆盖注册 OnlineServerMergeService（携带追加的参与者集合）——
        // 服务共享协议：覆盖恒胜出，模块默认装配的内置五步参与者即缺省形态。
        var mergeService = new OnlineServerMergeService(context.GetService<IOnlineServerMergeStore>(), participants, options.Merge, eventPublisher);
        _mergeService = mergeService;
        context.AddService(mergeService);
    }

    /// <summary>
    /// 激活期无动作（锚点登记由部署 / admin 驱动，不预建）。
    /// </summary>
    public Task ActivateAsync(CapabilityActivationContext context, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// 停用期无动作（本域无后台 job / 无外部资源）。
    /// </summary>
    public Task DeactivateAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// 注册区服生命周期 admin actions（开服登记 / 日历查询 / 合服计划 / 预演 / 执行）。
    /// </summary>
    /// <param name="registry">动作注册表。</param>
    public void RegisterAdminActions(AdminActionRegistry registry)
    {
        OnlineAdminServerLifecycleHandlers.RegisterServerLifecycleActions(registry, _calendarService, _mergeService);
    }
}
