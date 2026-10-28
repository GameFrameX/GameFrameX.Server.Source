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
/// 撮合成功通知订阅迁 Register 期机制事件总线（域服务在场才订阅并持有退订句柄）：
/// 订阅体只入队不做异步 I/O，通知由 match-assignment-notification-tick 任务异步推送（C407）；
/// 停用收口 = 关闭入队 + 退订 + 清空待处理 + 等待在途，返回后旧模块零通知。
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
/// mechanism event bus during Register (only when the domain service is assembled, holding the unsubscribe handle):
/// the subscriber only enqueues (no async I/O) and notifications are pushed asynchronously by the
/// match-assignment-notification-tick job (C407); deactivation closes the enqueue gate, unsubscribes, drains pending
/// notifications and waits for the in-flight processing, after which the stale module never notifies again.
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
    /// 撮合通知收口闸（保护 <see cref="_pendingNotifications"/> / <see cref="_notifyClosed"/> / <see cref="_inFlightNotifyTask"/>）。
    /// </summary>
    private readonly object _notifyGate = new();

    /// <summary>
    /// 待处理撮合通知队列（订阅回调只入队，异步 tick 有限批次消费；C407）。
    /// </summary>
    private readonly Queue<OnlineEvent> _pendingNotifications = new();

    /// <summary>
    /// 撮合分配事件订阅句柄（Register 期取得；停用收口释放，热更后旧回调不再触发）。
    /// </summary>
    private IDisposable _assignmentSubscription;

    /// <summary>
    /// 通知入队关闭标记（停用后晚到的总线回调不得再入队）。
    /// </summary>
    private bool _notifyClosed;

    /// <summary>
    /// 在途通知处理任务（当前正在执行的 tick 批次；停用收口等待其结束后才返回）。
    /// </summary>
    private Task _inFlightNotifyTask = Task.CompletedTask;

    /// <summary>
    /// 单轮 tick 最多处理的通知条数（有限批次：坏消息风暴不独占当轮调度；剩余条目下一轮续处理）。
    /// </summary>
    private const int MaxNotificationsPerTickRound = 64;

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
    /// 仅当票据服务与票据存储均在场时才装配匹配域服务、订阅撮合分配事件（持有退订句柄）并注册通知处理
    /// tick 任务，缺席时不装配也不报错。
    /// </summary>
    /// <remarks>
    /// Registers in-memory match actor and result stores, builds the match runtime with the rock-paper-scissors game and a match tick job,
    /// wires the settlement service and pipeline (settle-only when the grant service is absent) with a settlement tick job, registers the game bridge service,
    /// and only when ticket service and ticket store are present builds the match domain service, subscribes to assignment-created events
    /// (keeping the unsubscribe handle) and registers the notify tick job; otherwise it silently skips.
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
            _assignmentSubscription = context.Subscribe<OnlineEvent>(OnlineMatchEvents.AssignmentCreated, OnAssignmentCreated);
            context.AddTickJob("match-assignment-notification-tick", cancellationToken => ProcessAssignmentNotificationsAsync(cancellationToken));
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
    /// 停用对局能力：桥与 tick 任务由宿主统一管理（宿主停止序为先停调度器再逆序停用模块）；
    /// 模块自身收口撮合通知链路——关闭入队、退订撮合分配事件（热更后旧回调不再触发）、
    /// 清空未处理通知并等待在途处理结束后返回（此后旧模块不再产生任何通知）。
    /// </summary>
    /// <remarks>
    /// Bridges and tick jobs are owned by the host (the host stops the scheduler before deactivating modules);
    /// the module itself closes the assignment-notification chain — it stops accepting enqueues, unsubscribes from
    /// assignment-created events (stale bridging callbacks no longer fire after a hot swap), drains unprocessed
    /// notifications and waits for the in-flight processing to finish before returning.
    /// </remarks>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>收口完成的任务 / A task that completes when the closeout is done</returns>
    public async Task DeactivateAsync(CancellationToken cancellationToken)
    {
        IDisposable subscription;
        Task inFlight;
        lock (_notifyGate)
        {
            _notifyClosed = true;
            _pendingNotifications.Clear();
            subscription = _assignmentSubscription;
            _assignmentSubscription = null;
            inFlight = _inFlightNotifyTask;
        }

        // 退订不等待已在发布快照内进入分发的回调：回调尾部的入队闸保证晚到回调不能入队。
        subscription?.Dispose();

        if (!inFlight.IsCompleted)
        {
            try
            {
                // 在途批次的异常已由调度器经既有日志路径记录；停用链不得被单模块停用失败打断，此处不再外抛。
                await inFlight.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // 吞没：等待在途结束是目的，异常去向已是调度器观测出口。
            }
        }
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
    /// 撮合分配事件回调体（原 Launcher <c>OnlineMatchDomainBridgeWiring.HandleMatchAssignmentEvent</c> 迁入，C294；
    /// C407 改为纯入队）：<c>online.match.assignmentcreated</c> 只解析载荷并进入待处理队列，
    /// 不执行任何异步 I/O——同步总线回调内阻塞等待（原 <c>GetAwaiter().GetResult()</c> 两处）
    /// 在存在 SynchronizationContext 的环境会死锁。解析与推送由
    /// <see cref="ProcessAssignmentNotificationsAsync"/> 在调度 tick 内异步完成。
    /// <para>
    /// 维护约束：同步订阅体（机制事件总线约定）；载荷不可解析 / 分配标识为空静默丢弃；
    /// 停用后（入队闸关闭）晚到回调直接丢弃。
    /// </para>
    /// </summary>
    /// <param name="onlineEvent">机制事件载荷。</param>
    private void OnAssignmentCreated(OnlineEvent onlineEvent)
    {
        var payload = OnlineMatchEvents.ReadAssignmentCreated(onlineEvent);
        if (payload == null || string.IsNullOrEmpty(payload.AssignmentId))
        {
            return;
        }

        lock (_notifyGate)
        {
            if (_notifyClosed)
            {
                return;
            }

            _pendingNotifications.Enqueue(onlineEvent);
        }
    }

    /// <summary>
    /// 撮合通知处理 tick 任务（C407）：单轮最多取 <see cref="MaxNotificationsPerTickRound"/> 条待处理事件，
    /// 逐条经域服务解析数值对局标识并向双方在线会话 best-effort 推送 <see cref="NotifyMatchMade"/>；
    /// 停用后队列已清空且入队已关闭，本轮直接返回。
    /// <para>
    /// 维护约束：本方法在调度器 tick 上下文执行——条目级异常记录后继续当轮剩余条目，
    /// 首个异常在批次结束后重抛交调度器既有日志路径（per-job 隔离）；停止取消即抛
    /// <see cref="OperationCanceledException"/> 让当轮立即中断。解析失败 / 校验失败静默跳过；
    /// 离线方无会话跳过（等待方经状态轮询兜底）。
    /// </para>
    /// </summary>
    /// <param name="cancellationToken">停止取消令牌。</param>
    /// <returns>本轮处理任务。</returns>
    private async Task ProcessAssignmentNotificationsAsync(CancellationToken cancellationToken)
    {
        List<OnlineEvent> batch;
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_notifyGate)
        {
            if (_notifyClosed || _pendingNotifications.Count == 0)
            {
                return;
            }

            var take = Math.Min(_pendingNotifications.Count, MaxNotificationsPerTickRound);
            batch = new List<OnlineEvent>(take);
            while (batch.Count < take)
            {
                batch.Add(_pendingNotifications.Dequeue());
            }

            // 在途标记与批次出队同临界区发布且先于任何处理开始——停用读取恒见当前批次，
            // 不存在「批次已开工而标记仍是上一轮已完成任务」的窗口（标记任务只以 TrySetResult 收口，不承载异常）。
            _inFlightNotifyTask = completion.Task;
        }

        try
        {
            await ProcessNotificationBatchAsync(batch, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            completion.TrySetResult(true);
        }
    }

    /// <summary>
    /// 顺序处理一批撮合通知（解析 → 推送）；条目级异常记录首个、跳过当条继续，
    /// 批次结束重抛首个异常（交调度器日志路径），停止取消即中断。
    /// </summary>
    /// <param name="batch">待处理事件批次（已出队）。</param>
    /// <param name="cancellationToken">停止取消令牌。</param>
    /// <returns>批次处理任务。</returns>
    private async Task ProcessNotificationBatchAsync(List<OnlineEvent> batch, CancellationToken cancellationToken)
    {
        Exception firstFailure = null;
        for (var i = 0; i < batch.Count; i++)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await DispatchAssignmentNotificationAsync(batch[i], cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                // 条目级隔离：坏事件不拖垮同批其余通知；首个异常批次末尾重抛交调度器日志。
                firstFailure ??= exception;
            }
        }

        if (firstFailure != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(firstFailure).Throw();
        }
    }

    /// <summary>
    /// 单条撮合通知的分发（原 <c>HandleMatchAssignmentEvent</c> 主体，C294 迁入；C407 移除同步阻塞）：
    /// 域服务解析数值对局标识 → 向双方在线会话 best-effort 推送 <see cref="NotifyMatchMade"/>；
    /// 过滤 / 校验失败即静默返回；离线方无会话跳过（等待方经状态轮询兜底）。
    /// </summary>
    /// <param name="onlineEvent">机制事件载荷。</param>
    /// <param name="cancellationToken">停止取消令牌。</param>
    private async Task DispatchAssignmentNotificationAsync(OnlineEvent onlineEvent, CancellationToken cancellationToken)
    {
        var payload = OnlineMatchEvents.ReadAssignmentCreated(onlineEvent);
        if (payload == null || string.IsNullOrEmpty(payload.AssignmentId))
        {
            return;
        }

        var resolved = await _matchDomain.ResolveAssignmentAsync(onlineEvent.TenantId, onlineEvent.AppId, payload.AssignmentId, cancellationToken).ConfigureAwait(false);
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

            await session.WriteAsync(notify).ConfigureAwait(false);
        }
    }
}
