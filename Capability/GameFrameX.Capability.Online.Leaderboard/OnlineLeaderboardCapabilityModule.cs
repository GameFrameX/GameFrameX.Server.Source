//  ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
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


using System;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Hosting.AdminApi;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Events;
using GameFrameX.Capability.Online.Abstractions.Idempotency;
using GameFrameX.Capability.Online.Abstractions.Scope;
using GameFrameX.Capability.Online.Competition;
using GameFrameX.Capability.Online.Competition.AdminApi;
using GameFrameX.Capability.Online.Competition.Leaderboard;
using GameFrameX.Capability.Online.Match;
using GameFrameX.Localization;

namespace GameFrameX.Capability.Online.Leaderboard;

/// <summary>
/// 排行榜能力（榜单生命周期 / 分数提交 / 查询 / 对局结算投影）的自装配模块（自竞技能力包拆出独立承载：
/// 按需引入 + Options 回调定制榜单参数）。
/// <para>
/// 维护约束（服务共享协议）：模块无参构造（扫描可发现）；<c>Register</c> 自装配本域——榜单 store 以懒默认
/// 描述符注册（族 / 宿主覆盖恒胜出，模块不感知）；<see cref="OnlineCompetitionOptions"/> 为唯一参数面
/// （含榜单组件参数与 Match.Settled 投影配置）：投影榜 id 非空白且对局结果存储（online-match 可选产物）
/// 在场时装配 Match.Settled 排行榜投影（机制总线订阅 + 失败重投 tick）；
/// <c>ActivateAsync</c> 在投影榜 id 非空白时经榜单服务预建目标榜（查询-创建，见方法注释）。
/// </para>
/// </summary>
public sealed class OnlineLeaderboardCapabilityModule : ICapabilityModule, IAdminActionContributor
{
    /// <summary>
    /// 排行榜能力清单（Id 冻结 = 终态包名；query_leaderboard 自竞技能力清单迁入；可选依赖 online-match
    /// 经 <see cref="CapabilityOptionalDependency"/> 声明，缺席时披露对应缺失功能名）。
    /// </summary>
    public static readonly CapabilityManifest ManifestInstance = new(
        "online-leaderboard",
        "1.0.0",
        new[] { "query_leaderboard" },
        null,
        new[]
        {
            new CapabilityOptionalDependency("online-match", Keys.Capability.OnlineModule.Leaderboard.OptionalMatchAbsent),
        },
        typeof(OnlineCompetitionOptions));

    /// <summary>
    /// 排行榜服务（Register 期构造；admin 面 handler、投影链与激活期榜单预建取用）。
    /// </summary>
    private OnlineLeaderboardService _leaderboards;

    /// <summary>
    /// Match.Settled 排行榜投影器（Register 期装配；投影配置空白或 online-match 缺席时为 null）。
    /// </summary>
    private OnlineMatchSettledLeaderboardProjector _projectionProjector;

    /// <summary>
    /// Match.Settled 投影目标榜单标识（Register 期从 Options 读出；空白 = 不投影不预建；ActivateAsync 预建判据）。
    /// </summary>
    private string _projectionBoardId;

    /// <summary>
    /// 获取排行榜能力清单，固定返回静态实例 <see cref="ManifestInstance"/>。
    /// </summary>
    /// <remarks>
    /// Gets the leaderboard capability manifest; always returns the static <see cref="ManifestInstance"/>.
    /// </remarks>
    /// <value>排行榜能力清单 / Leaderboard capability manifest</value>
    public CapabilityManifest Manifest
    {
        get { return ManifestInstance; }
    }

    /// <summary>
    /// 读取排行榜能力选项，懒默认注册内存版榜单存储并解析生效实例，构造并注册排行榜服务；
    /// 投影榜单标识非空白且对局结果存储在场时，注册 Match.Settled 排行榜投影器、订阅对局结算事件并添加投影重试的 tick 任务。
    /// </summary>
    /// <remarks>
    /// Reads <see cref="OnlineCompetitionOptions"/>, registers the in-memory leaderboard store as a lazy default and
    /// resolves the effective instance, then builds and registers the leaderboard service. When the projection board id
    /// is not blank and the match result store is present, it registers the Match.Settled leaderboard projector,
    /// subscribes it to the match-settled event and adds the <c>leaderboard-projection-retry</c> tick job;
    /// otherwise the projection is silently skipped.
    /// </remarks>
    /// <param name="context">能力注册上下文 / Capability registration context</param>
    public void Register(CapabilityRegistrationContext context)
    {
        var options = context.GetOptions<OnlineCompetitionOptions>();
        var eventPublisher = context.GetService<IOnlineEventPublisher>();

        // 榜单 store 懒默认描述符（族 / 宿主覆盖恒胜出；GetService 命中覆盖或本默认）。
        context.AddService<IOnlineLeaderboardStore>(static _ => new InMemoryOnlineLeaderboardStore());
        var leaderboardStore = context.GetService<IOnlineLeaderboardStore>();

        var leaderboards = new OnlineLeaderboardService(leaderboardStore, options.Leaderboard, eventPublisher);
        _leaderboards = leaderboards;
        context.AddService(leaderboards);

        // Match.Settled → 排行榜投影：Options 投影榜 id 非空白（空白 = 不投影）且对局结果存储在场
        // （online-match 被过滤 → 不装配不报错）才装配；订阅走机制事件总线（Register 期声明，
        // 单个订阅体异常由机制记日志后继续分发，业务侧不再自行吞异常），失败经 tick job 重投。
        _projectionBoardId = options.LeaderboardProjection.MatchSettledBoardId;
        var projectionResultStore = context.GetOptionalService<IOnlineMatchResultStore>();
        if (!string.IsNullOrWhiteSpace(_projectionBoardId) && projectionResultStore != null)
        {
            var projectionIdempotency = context.GetService<OnlineIdempotencyService>();
            var projector = new OnlineMatchSettledLeaderboardProjector(projectionResultStore, leaderboards, projectionIdempotency, _projectionBoardId);
            _projectionProjector = projector;
            context.AddService(projector);
            context.Subscribe<OnlineEvent>(OnlineMatchRuntimeEvents.MatchSettled, onlineEvent => _projectionProjector.HandleEventAsync(onlineEvent).GetAwaiter().GetResult());
            context.AddTickJob("leaderboard-projection-retry", cancellationToken => _projectionProjector.RetryPendingAsync(cancellationToken));
        }
    }

    /// <summary>
    /// 投影榜单标识为空白时直接返回；否则以 Top 1 查询探测目标榜单，已存在则返回，返回资源不存在时按累加计分、降序排序创建该榜单。
    /// </summary>
    /// <remarks>
    /// Returns immediately when the projection board id is blank. Otherwise probes the target board with a top-1 query;
    /// if it already exists nothing else happens, and if the probe reports resource-not-found the board is created with
    /// the sum score-update policy and descending sort order. Any other failure aborts activation so the host can roll back.
    /// </remarks>
    /// <param name="context">能力激活上下文，用于解析 Online 作用域 / Capability activation context used to resolve the Online scope</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>表示异步激活操作的任务 / Task representing the asynchronous activation</returns>
    /// <exception cref="InvalidOperationException">探测返回资源不存在以外的失败码，或创建榜单失败 / The probe fails with a code other than resource-not-found, or creating the board fails</exception>
    public async Task ActivateAsync(CapabilityActivationContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_projectionBoardId))
        {
            return;
        }

        // 榜单预建（迁自竞技能力模块）：投影目标榜查询-创建——GetTop 探针命中即已存在；
        // ResourceNotFound 视作首建，按 Sum / Descending 创建；其余失败码或创建失败 = 激活失败
        // （抛 InvalidOperationException 由机制逆序回滚）。
        var scope = context.Services.GetService<OnlineScope>();
        var probe = await _leaderboards.GetTopAsync(scope, new OnlineLeaderboardTopQuery { LeaderboardId = _projectionBoardId, Count = 1, }, cancellationToken).ConfigureAwait(false);
        if (probe.IsSuccess)
        {
            return;
        }

        if (probe.Code != OnlineErrorCode.ResourceNotFound)
        {
            throw new InvalidOperationException("Leaderboard pre-build probe failed for board '" + _projectionBoardId + "' (code: " + probe.Code + ").");
        }

        var created = await _leaderboards.CreateAsync(scope, new OnlineLeaderboardCreateRequest
        {
            LeaderboardId = _projectionBoardId,
            SortOrder = OnlineLeaderboardSortOrder.Descending,
            ScoreUpdatePolicy = OnlineLeaderboardScoreUpdatePolicy.Sum,
        }, 0, cancellationToken).ConfigureAwait(false);
        if (!created.IsSuccess)
        {
            throw new InvalidOperationException("Leaderboard pre-build create failed for board '" + _projectionBoardId + "' (code: " + created.Code + ").");
        }
    }

    /// <summary>
    /// 停用阶段无需处理：事件订阅与 tick 任务由宿主机制在注册时托管，激活仅预建榜单而不持有运行资源，直接返回已完成任务。
    /// </summary>
    /// <remarks>
    /// Nothing to do on deactivation: the event subscription and tick job are declared to the host during registration,
    /// and activation only pre-creates the board without holding runtime resources, so a completed task is returned.
    /// </remarks>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>已完成的任务 / Completed task</returns>
    public Task DeactivateAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// 基于注册阶段构造的排行榜服务登记排行榜查询管理动作。
    /// </summary>
    /// <remarks>
    /// Registers the leaderboard query admin actions backed by the leaderboard service built during registration.
    /// </remarks>
    /// <param name="registry">管理动作注册表 / Admin action registry</param>
    public void RegisterAdminActions(AdminActionRegistry registry)
    {
        OnlineAdminLeaderboardHandlers.RegisterLeaderboardQuery(registry, _leaderboards);
    }
}
