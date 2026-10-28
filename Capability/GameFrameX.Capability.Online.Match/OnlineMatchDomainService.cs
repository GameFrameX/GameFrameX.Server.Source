//  ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please refer to the LICENSE file at the source code root for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯公民合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any disputes and liabilities arising from secondary development,
//   本组织不承担任何责任，开发者须自行承担。
//   The organization shall not be responsible for any disputes; developers shall bear the responsibility.
//  ==========================================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Scope;
using GameFrameX.Capability.Online.Matchmaking;

namespace GameFrameX.Capability.Online.Match;

/// <summary>
/// 匹配域服务：<see cref="IOnlineMatchDomain"/> 的本域实现——旧 420 协议族语义
///（入队 / 取消 / 状态 / 入场）到票据撮合域与对局运行时的桥接编排。
/// <para>
/// 维护约束（红线）：
/// ① 匹配池唯一权威 = 票据域——入队经 <see cref="OnlineMatchTicketService"/>、撮合经
/// <see cref="OnlineMatchmakerCoordinator"/>（同一票据存储），本类不持有任何队列或池副本；
/// ② 即时窗口兼容：入队建票后同步执行一轮撮合（对手已在池时本轮即成组，响应即刻带数值对局标识，
/// 等价旧 FIFO 链第二人入队即配对的可观测行为）；等待方经状态轮询 / 通知获知；
/// ③ 数值对局标识登记表（assignment → 协议数值 id 单射）是本类唯一的派生状态——InMemory、
/// 重启即失效（与榜单同款易失边界）；对局终态后按 <see cref="OnlineMatchRegistryOptions"/> 的生命周期
/// 过期清扫（登记表访问时惰性触发，不设独立定时器），过期项移除后懒登记重建将分配新数值标识
///（与重启后语义一致）；计数种子取构造时刻避免跨重启撞号；
/// ④ 入场 = create-or-reuse：以 <c>matched-s{区服}-m{数值}</c> 确定性派生运行时对局标识，
/// 复用 <see cref="OnlineMatchRuntime"/> 既有 Actor 生命周期（与房间桥同型），不发明对局玩法新面；
/// ⑤ 模式标签（1/2）只是撮合池分区键，入场绑定的玩法模式取构造注入的样例玩法
///（与房间桥「协议形状 → 玩法模式」解耦口径一致）。
/// </para>
/// </summary>
public sealed class OnlineMatchDomainService : IOnlineMatchDomain
{
    /// <summary>评分基础窗（旧 FIFO 链 MatchRules.BaseWindowRating 同值：评分差 ±100）。</summary>
    public const int BaseRatingWindow = 100;

    /// <summary>单人票据缺省区域（与房间桥 Region=1 口径一致）。</summary>
    public const int DefaultRegion = 1;

    /// <summary>单人票据目标规模（旧链无论 1v1/2v2 恒配 2 人——可观测行为保持；模式只是池标签）。</summary>
    public const int SoloTeamSize = 2;

    /// <summary>票据存储（数值登记与最新票据读面）。</summary>
    private readonly IOnlineMatchTicketStore _store;

    /// <summary>票据服务（入队 / 取消的唯一裁决入口）。</summary>
    private readonly OnlineMatchTicketService _tickets;

    /// <summary>撮合协调器（即时窗口一轮撮合的执行者）。</summary>
    private readonly OnlineMatchmakerCoordinator _coordinator;

    /// <summary>对局运行时（入场 create-or-reuse 的 Actor 属主）。</summary>
    private readonly OnlineMatchRuntime _runtime;

    /// <summary>重连服务（会话中介断线标记 / 恢复的唯一令牌签发与快照增量组装点）。</summary>
    private readonly OnlineMatchReconnectService _reconnect;

    /// <summary>入场绑定的玩法实现（模式与最小人数取自玩法声明）。</summary>
    private readonly IOnlineMatchGame _game;

    /// <summary>登记表锁（数值分配与双向索引的临界区）。</summary>
    private readonly object _registryLock = new object();
    /// <summary>assignment → 登记项。</summary>
    private readonly Dictionary<string, AssignmentRegistration> _byAssignment = new Dictionary<string, AssignmentRegistration>(StringComparer.Ordinal);

    /// <summary>数值对局标识 → 登记项。</summary>
    private readonly Dictionary<long, AssignmentRegistration> _byNumeric = new Dictionary<long, AssignmentRegistration>();

    /// <summary>数值分配计数器（种子取构造时刻秒级时间戳，跨重启不回卷撞号）。</summary>
    private long _nextNumeric = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    /// <summary>登记表清扫参数（生命周期与节流间隔；构造时归一——非正值回退默认）。</summary>
    private readonly OnlineMatchRegistryOptions _registry;

    /// <summary>上次清扫时刻（UTC 毫秒；锁内读写，与登记表同临界区）。</summary>
    private long _lastSweepTimeUnixMilliseconds;

    /// <summary>
    /// 初始化 <see cref="OnlineMatchDomainService"/>。
    /// </summary>
    /// <param name="store">票据存储。</param>
    /// <param name="tickets">票据服务。</param>
    /// <param name="coordinator">撮合协调器。</param>
    /// <param name="runtime">对局运行时。</param>
    /// <param name="game">入场绑定的玩法实现。</param>
    /// <param name="registry">登记表清扫参数（空引用时取默认；生命周期非正值回退默认，节流间隔负值回退默认、零表示禁用节流）。</param>
    public OnlineMatchDomainService(IOnlineMatchTicketStore store, OnlineMatchTicketService tickets, OnlineMatchmakerCoordinator coordinator, OnlineMatchRuntime runtime, IOnlineMatchGame game, OnlineMatchRegistryOptions registry)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _tickets = tickets ?? throw new ArgumentNullException(nameof(tickets));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _reconnect = new OnlineMatchReconnectService(runtime);
        _game = game ?? throw new ArgumentNullException(nameof(game));

        var effectiveRegistry = registry ?? new OnlineMatchRegistryOptions();
        _registry = new OnlineMatchRegistryOptions
        {
            AssignmentRegistrationLifetimeSeconds = effectiveRegistry.AssignmentRegistrationLifetimeSeconds > 0
                ? effectiveRegistry.AssignmentRegistrationLifetimeSeconds
                : OnlineMatchRegistryOptions.DefaultAssignmentRegistrationLifetimeSeconds,
            RegistrationSweepIntervalSeconds = effectiveRegistry.RegistrationSweepIntervalSeconds >= 0
                ? effectiveRegistry.RegistrationSweepIntervalSeconds
                : OnlineMatchRegistryOptions.DefaultRegistrationSweepIntervalSeconds,
        };
    }

    /// <summary>
    /// 获取当前登记表条目数（锁内读；登记表驻留有界性的对账观测面）。
    /// </summary>
    /// <remarks>
    /// Gets the current number of assignment registrations (read under the registry lock),
    /// as the reconciliation surface for the bounded residency of the registry.
    /// </remarks>
    public int RegistrationCount
    {
        get
        {
            lock (_registryLock)
            {
                return _byAssignment.Count;
            }
        }
    }

    /// <summary>
    /// 为玩家创建单人匹配票据：依次校验作用域、模式（仅允许 1 或 2）与评分（须大于 0），以默认区域和评分上下窗口建票；
    /// 建票成功后同步执行一轮撮合，再按玩家最新票据解析已撮合的数值对局标识（未撮合时为 0）。
    /// </summary>
    /// <remarks>
    /// Validates scope, mode (only 1 or 2) and rating (must be positive), enqueues a solo ticket in the default region with a rating window,
    /// runs one immediate matchmaking pass, then resolves the numeric match id from the player's latest ticket (0 when not yet matched).
    /// Invalid mode or rating fails with <c>ParameterInvalid</c>; ticket service failures are propagated.
    /// </remarks>
    /// <param name="scope">玩家作用域 / Player scope</param>
    /// <param name="mode">匹配模式，仅允许 1 或 2 / Match mode, only 1 or 2 allowed</param>
    /// <param name="rating">玩家评分，须大于 0 / Player rating, must be positive</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>入队结果，成功时携带已撮合的对局标识（未撮合为 0） / Enqueue result carrying the matched id (0 if not matched) on success</returns>
    public async Task<OnlineResult<OnlineMatchDomainEnqueue>> EnqueueAsync(OnlineScope scope, int mode, int rating, CancellationToken cancellationToken = default)
    {
        var enqueueScopeFailure = ValidateScope(scope);
        if (enqueueScopeFailure.HasValue)
        {
            return OnlineResult<OnlineMatchDomainEnqueue>.Fail(enqueueScopeFailure.Value.Code, enqueueScopeFailure.Value.Message);
        }

        if (mode != 1 && mode != 2)
        {
            return OnlineResult<OnlineMatchDomainEnqueue>.Fail(OnlineErrorCode.ParameterInvalid, "匹配模式非法");
        }

        if (rating <= 0)
        {
            return OnlineResult<OnlineMatchDomainEnqueue>.Fail(OnlineErrorCode.ParameterInvalid, "评分非法（须大于 0）");
        }

        var request = new OnlineMatchTicketEnqueueRequest
        {
            Mode = mode,
            Region = DefaultRegion,
            TeamSize = SoloTeamSize,
            SkillRange = new OnlineMatchSkillRange { Min = Math.Max(1, rating - BaseRatingWindow), Max = rating + BaseRatingWindow },
        };

        var enqueued = await _tickets.EnqueueAsync(scope, request, cancellationToken).ConfigureAwait(false);
        if (!enqueued.IsSuccess)
        {
            return OnlineResult<OnlineMatchDomainEnqueue>.Fail(enqueued.Code, enqueued.Message);
        }

        // 即时窗口：建票后同步跑一轮撮合（同一票据存储 = 同一池权威；tick 任务与本轮互为冗余兜底）。
        await _coordinator.RunOnceAsync(scope.TenantId, scope.AppId, 0, cancellationToken).ConfigureAwait(false);

        var latest = await _store.FindLatestByPlayerAsync(scope.TenantId, scope.AppId, scope.PlayerId, cancellationToken).ConfigureAwait(false);
        var matchId = await ResolveMatchedNumericAsync(latest, cancellationToken).ConfigureAwait(false);
        return OnlineResult<OnlineMatchDomainEnqueue>.Ok(new OnlineMatchDomainEnqueue { MatchId = matchId });
    }

    /// <summary>
    /// 取消玩家当前排队中的票据：校验作用域后查找活跃票据；无活跃票据时返回成功且 <c>Cancelled = false</c>，不视为失败；
    /// 票据服务取消失败时透传失败码，成功时仅当票据状态变为已取消才标记已取消。
    /// </summary>
    /// <remarks>
    /// Validates scope and looks up the active ticket; no active ticket yields a successful receipt with <c>Cancelled = false</c>.
    /// Ticket service failures are propagated; <c>Cancelled</c> is true only when the ticket ends in the cancelled state.
    /// </remarks>
    /// <param name="scope">玩家作用域 / Player scope</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>取消回执结果 / Cancel receipt result</returns>
    public async Task<OnlineResult<OnlineMatchDomainCancel>> CancelAsync(OnlineScope scope, CancellationToken cancellationToken = default)
    {
        var failureCode = ValidateScope(scope);
        if (failureCode.HasValue)
        {
            return OnlineResult<OnlineMatchDomainCancel>.Fail(failureCode.Value.Code, failureCode.Value.Message);
        }

        var active = await _store.FindActiveByPlayerAsync(scope.TenantId, scope.AppId, scope.PlayerId, cancellationToken).ConfigureAwait(false);
        if (active == null)
        {
            // 无排队票据 = 终态回执（未在排队），不算失败——协议面映射旧 420003。
            return OnlineResult<OnlineMatchDomainCancel>.Ok(new OnlineMatchDomainCancel { Cancelled = false });
        }

        var cancelled = await _tickets.CancelAsync(scope, active.TicketId, cancellationToken).ConfigureAwait(false);
        if (!cancelled.IsSuccess)
        {
            return OnlineResult<OnlineMatchDomainCancel>.Fail(cancelled.Code, cancelled.Message);
        }

        return OnlineResult<OnlineMatchDomainCancel>.Ok(new OnlineMatchDomainCancel
        {
            Cancelled = cancelled.Data != null && cancelled.Data.State == OnlineMatchTicketState.Cancelled,
        });
    }

    /// <summary>
    /// 查询玩家匹配状态：校验作用域后读取最新票据；无票据返回未排队未撮合，票据排队中返回排队中，
    /// 其余状态解析数值对局标识，非 0 即视为已撮合。
    /// </summary>
    /// <remarks>
    /// Validates scope and reads the latest ticket: none means neither queuing nor matched, a queued ticket means queuing,
    /// otherwise the numeric match id is resolved and a non-zero id means matched.
    /// </remarks>
    /// <param name="scope">玩家作用域 / Player scope</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>匹配状态结果 / Match status result</returns>
    public async Task<OnlineResult<OnlineMatchDomainStatus>> GetStatusAsync(OnlineScope scope, CancellationToken cancellationToken = default)
    {
        var failureCode = ValidateScope(scope);
        if (failureCode.HasValue)
        {
            return OnlineResult<OnlineMatchDomainStatus>.Fail(failureCode.Value.Code, failureCode.Value.Message);
        }

        var latest = await _store.FindLatestByPlayerAsync(scope.TenantId, scope.AppId, scope.PlayerId, cancellationToken).ConfigureAwait(false);
        if (latest == null)
        {
            return OnlineResult<OnlineMatchDomainStatus>.Ok(new OnlineMatchDomainStatus { Queuing = false, Matched = false, MatchId = 0 });
        }

        if (latest.State == OnlineMatchTicketState.Queued)
        {
            return OnlineResult<OnlineMatchDomainStatus>.Ok(new OnlineMatchDomainStatus { Queuing = true, Matched = false, MatchId = 0 });
        }

        var matchId = await ResolveMatchedNumericAsync(latest, cancellationToken).ConfigureAwait(false);
        return OnlineResult<OnlineMatchDomainStatus>.Ok(new OnlineMatchDomainStatus
        {
            Queuing = false,
            Matched = matchId != 0,
            MatchId = matchId,
        });
    }

    /// <summary>
    /// 玩家进入已撮合对局：校验入场请求与分配登记后，按服务器标识派生运行时对局标识并获取或创建对局 Actor；
    /// 对局已结束返回 <c>StateEnded</c>，成员表与分配成员不一致返回 <c>ParameterInvalid</c>；
    /// 对局处于创建、等待或就绪状态时将入场玩家本人置为就绪，全员就绪时才开局；最终返回是否已进入运行状态及成员列表。
    /// </summary>
    /// <remarks>
    /// Validates the entry and assignment registration, derives the runtime match id from the server id and gets or creates the match actor.
    /// Ended matches fail with <c>StateEnded</c>, member mismatches fail with <c>ParameterInvalid</c>; in created, waiting or ready states only the entering player is marked ready and the match starts once every seat is ready.
    /// Returns whether the match is running and the member list.
    /// </remarks>
    /// <param name="scope">玩家作用域 / Player scope</param>
    /// <param name="matchId">数值对局标识 / Numeric match id</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>入场结果 / Entry result</returns>
    public async Task<OnlineResult<OnlineMatchDomainEntry>> EnterAsync(OnlineScope scope, long matchId, CancellationToken cancellationToken = default)
    {
        var invalidEntry = ValidateEntry(scope, matchId, out var registration);
        if (invalidEntry != null)
        {
            return invalidEntry;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var runtimeMatchId = DeriveRuntimeMatchId(scope.ServerId, matchId);
        var actorResult = await ResolveOrCreateActorAsync(scope, registration, runtimeMatchId, now, cancellationToken).ConfigureAwait(false);
        if (!actorResult.IsSuccess)
        {
            return OnlineResult<OnlineMatchDomainEntry>.Fail(actorResult.Code, actorResult.Message);
        }

        var actor = actorResult.Data;
        var snapshot = actor.GetSnapshot();
        if (OnlineMatchStateMachine.IsEnded(snapshot.State))
        {
            return OnlineResult<OnlineMatchDomainEntry>.Fail(OnlineErrorCode.StateEnded, "对局已结束，不可入场");
        }

        if (!MembersMatch(snapshot, registration.PlayerIds))
        {
            return OnlineResult<OnlineMatchDomainEntry>.Fail(OnlineErrorCode.ParameterInvalid, "成员表与分配成员不一致");
        }

        if (snapshot.State == OnlineMatchState.Created || snapshot.State == OnlineMatchState.Waiting || snapshot.State == OnlineMatchState.Ready)
        {
            var earlyResult = await MarkMembersReadyAndStartAsync(scope, actor, snapshot, registration, matchId, now, cancellationToken).ConfigureAwait(false);
            if (earlyResult != null)
            {
                return earlyResult;
            }
        }

        var finalSnapshot = actor.GetSnapshot();
        return OnlineResult<OnlineMatchDomainEntry>.Ok(new OnlineMatchDomainEntry
        {
            MatchId = matchId,
            Started = finalSnapshot.State == OnlineMatchState.Running,
            PlayerIds = new List<long>(registration.PlayerIds),
        });
    }

    /// <summary>
    /// 将撮合分配标识解析为数值对局标识、模式与成员列表：分配标识为空返回 <c>ParameterInvalid</c>；
    /// 优先使用内存登记，未登记时从票据存储读取分配并登记，分配不存在返回 <c>ResourceNotFound</c>。
    /// </summary>
    /// <remarks>
    /// Resolves an assignment id to numeric match id, mode and players. Empty id fails with <c>ParameterInvalid</c>;
    /// the in-memory registration is preferred, otherwise the assignment is loaded from the ticket store and registered; a missing assignment fails with <c>ResourceNotFound</c>.
    /// </remarks>
    /// <param name="tenantId">租户标识 / Tenant id</param>
    /// <param name="appId">应用标识 / Application id</param>
    /// <param name="assignmentId">撮合分配标识 / Matchmaking assignment id</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>分配解析结果 / Assignment resolution result</returns>
    public async Task<OnlineResult<OnlineMatchDomainAssignment>> ResolveAssignmentAsync(long tenantId, long appId, string assignmentId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(assignmentId))
        {
            return OnlineResult<OnlineMatchDomainAssignment>.Fail(OnlineErrorCode.ParameterInvalid, "缺少分配标识");
        }

        var registration = FindRegistration(assignmentId);
        if (registration == null)
        {
            var assignment = await _store.FindAssignmentAsync(tenantId, appId, assignmentId, cancellationToken).ConfigureAwait(false);
            if (assignment == null)
            {
                return OnlineResult<OnlineMatchDomainAssignment>.Fail(OnlineErrorCode.ResourceNotFound, "分配不存在");
            }

            registration = RegisterAssignment(assignment);
        }

        return OnlineResult<OnlineMatchDomainAssignment>.Ok(new OnlineMatchDomainAssignment
        {
            MatchId = registration.NumericMatchId,
            Mode = registration.Mode,
            PlayerIds = new List<long>(registration.PlayerIds),
        });
    }

    /// <summary>
    /// 标记玩家在对局中掉线：校验作用域后按票据、分配登记与运行时成员表定位玩家所属对局；
    /// 找不到对局、对局已结束或成员缺失、已离开、被踢出时返回成功且 <c>Marked = false</c>；
    /// 否则经重连服务标记掉线（重连令牌留存在成员表、不下发），失败透传失败码。
    /// </summary>
    /// <remarks>
    /// Validates scope and locates the player's match via ticket, assignment registration and runtime member table.
    /// Returns success with <c>Marked = false</c> when no match is found, the match has ended, or the member is missing, left or kicked;
    /// otherwise marks the disconnect via the reconnect service (the reconnect token stays server-side) and propagates its failure.
    /// </remarks>
    /// <param name="scope">玩家作用域 / Player scope</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>掉线标记结果 / Disconnect marking result</returns>
    public async Task<OnlineResult<OnlineMatchDomainDisconnect>> NotifyPlayerDisconnectedAsync(OnlineScope scope, CancellationToken cancellationToken = default)
    {
        var scopeFailure = ValidateScope(scope);
        if (scopeFailure.HasValue)
        {
            return OnlineResult<OnlineMatchDomainDisconnect>.Fail(scopeFailure.Value.Code, scopeFailure.Value.Message);
        }

        // 归属定位：票据 → 分配登记 → 运行时成员表（不以最新票据直接冒充归属）。
        var located = await LocatePlayerMatchAsync(scope, cancellationToken).ConfigureAwait(false);
        if (located == null)
        {
            return OnlineResult<OnlineMatchDomainDisconnect>.Ok(new OnlineMatchDomainDisconnect { Marked = false });
        }

        var (numericMatchId, actor, member) = located.Value;
        var snapshot = actor.GetSnapshot();
        if (OnlineMatchStateMachine.IsEnded(snapshot.State))
        {
            return OnlineResult<OnlineMatchDomainDisconnect>.Ok(new OnlineMatchDomainDisconnect { Marked = false });
        }

        if (member == null || member.State == OnlineMatchMemberState.Left || member.State == OnlineMatchMemberState.Kicked)
        {
            return OnlineResult<OnlineMatchDomainDisconnect>.Ok(new OnlineMatchDomainDisconnect { Marked = false });
        }

        // rt- 令牌经重连服务唯一签发点产生并留存成员表，不下发客户端（通道已断）。
        var marked = await _reconnect.DisconnectAsync(scope, actor.MatchId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), cancellationToken).ConfigureAwait(false);
        if (!marked.IsSuccess)
        {
            return OnlineResult<OnlineMatchDomainDisconnect>.Fail(marked.Code, marked.Message);
        }

        return OnlineResult<OnlineMatchDomainDisconnect>.Ok(new OnlineMatchDomainDisconnect { Marked = true, MatchId = numericMatchId });
    }

    /// <summary>
    /// 为重新登录的玩家恢复对局：校验作用域并定位所属对局，无对局时返回成功且 <c>Resumed = false</c>；
    /// 成员缺失、已离开或被踢出返回 <c>ScopeDenied</c>，对局已结束返回 <c>StateEnded</c>；
    /// 掉线成员用服务端留存的重连令牌经重连服务恢复，在线成员做幂等确认直接以当前快照作为恢复结果；
    /// 返回对局状态、服务器序号、成员状态、玩法状态以及是否有增量可用。
    /// </summary>
    /// <remarks>
    /// Validates scope and locates the match; none yields success with <c>Resumed = false</c>. Missing, left or kicked members fail with <c>ScopeDenied</c>, ended matches with <c>StateEnded</c>.
    /// Disconnected members reconnect through the reconnect service using the server-held token; online members get an idempotent confirmation based on the current snapshot.
    /// Returns match state, server sequence, member states, game state and whether a delta is available.
    /// </remarks>
    /// <param name="scope">玩家作用域 / Player scope</param>
    /// <param name="lastAcknowledgedSequence">客户端最后确认的序号 / Last sequence acknowledged by the client</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>对局恢复结果 / Match resume result</returns>
    public async Task<OnlineResult<OnlineMatchDomainResume>> TryResumeForPlayerAsync(OnlineScope scope, long lastAcknowledgedSequence, CancellationToken cancellationToken = default)
    {
        var scopeFailure = ValidateScope(scope);
        if (scopeFailure.HasValue)
        {
            return OnlineResult<OnlineMatchDomainResume>.Fail(scopeFailure.Value.Code, scopeFailure.Value.Message);
        }

        var located = await LocatePlayerMatchAsync(scope, cancellationToken).ConfigureAwait(false);
        if (located == null)
        {
            // 无对局可恢复 = 身份恢复不受影响的空回执（Resumed=false 的成功）。
            return OnlineResult<OnlineMatchDomainResume>.Ok(new OnlineMatchDomainResume { Resumed = false });
        }

        var (numericMatchId, actor, member) = located.Value;
        if (member == null || member.State == OnlineMatchMemberState.Left || member.State == OnlineMatchMemberState.Kicked)
        {
            return OnlineResult<OnlineMatchDomainResume>.Fail(OnlineErrorCode.ScopeDenied, "玩家不在对局中或已退出");
        }

        var snapshot = actor.GetSnapshot();
        if (OnlineMatchStateMachine.IsEnded(snapshot.State))
        {
            return OnlineResult<OnlineMatchDomainResume>.Fail(OnlineErrorCode.StateEnded, "对局已结束");
        }

        OnlineMatchReconnectResult resumed;
        if (member.State == OnlineMatchMemberState.Disconnected)
        {
            // 服务端留存令牌回灌：成员表令牌只回灌给 Actor 的既有三重校验，身份证明由更高层会话凭据承担。
            var context = new OnlineMatchReconnectContext
            {
                MatchId = actor.MatchId,
                PlayerId = scope.PlayerId,
                ReconnectToken = member.ReconnectToken,
                LastAcknowledgedSequence = lastAcknowledgedSequence,
            };
            var outcome = await _reconnect.ReconnectAsync(scope, context, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), cancellationToken).ConfigureAwait(false);
            if (!outcome.IsSuccess)
            {
                return OnlineResult<OnlineMatchDomainResume>.Fail(outcome.Code, outcome.Message);
            }

            resumed = outcome.Data;
        }
        else
        {
            // 活跃成员的重复恢复：幂等确认（不动状态机，快照即恢复产物）。
            resumed = new OnlineMatchReconnectResult
            {
                Succeeded = true,
                MatchId = actor.MatchId,
                State = snapshot.State,
                ServerSequence = snapshot.ServerSequence,
                Snapshot = snapshot,
                Delta = null,
                Instruction = "成员在线，快照即恢复产物",
            };
        }

        var resultSnapshot = resumed.Snapshot ?? actor.GetSnapshot();
        var members = new List<OnlineMatchDomainResumeMember>();
        if (resultSnapshot.Members != null)
        {
            foreach (var resumeMember in resultSnapshot.Members)
            {
                if (resumeMember == null)
                {
                    continue;
                }

                members.Add(new OnlineMatchDomainResumeMember { PlayerId = resumeMember.PlayerId, State = (int)resumeMember.State });
            }
        }

        return OnlineResult<OnlineMatchDomainResume>.Ok(new OnlineMatchDomainResume
        {
            Resumed = true,
            MatchId = numericMatchId,
            MatchState = (int)resultSnapshot.State,
            ServerSequence = resultSnapshot.ServerSequence,
            DeltaAvailable = resumed.Delta != null,
            Members = members,
            GameState = resultSnapshot.GameState,
            Instruction = resumed.Instruction,
        });
    }

    /// <summary>
    /// 校验入场请求（求值顺序固定：作用域 → 对局标识 → 登记存在 → 入场者成员身份 → 成员数与玩法最小人数一致）。
    /// </summary>
    /// <param name="scope">请求作用域。</param>
    /// <param name="matchId">数值对局标识。</param>
    /// <param name="registration">登记项（校验通过时非空）。</param>
    /// <returns>校验失败返回失败结果；通过返回 <c>null</c>。</returns>
    private OnlineResult<OnlineMatchDomainEntry> ValidateEntry(OnlineScope scope, long matchId, out AssignmentRegistration registration)
    {
        registration = null;
        var failureCode = ValidateScope(scope);
        if (failureCode.HasValue)
        {
            return OnlineResult<OnlineMatchDomainEntry>.Fail(failureCode.Value.Code, failureCode.Value.Message);
        }

        if (matchId <= 0)
        {
            return OnlineResult<OnlineMatchDomainEntry>.Fail(OnlineErrorCode.ParameterInvalid, "对局标识非法");
        }

        registration = FindRegistration(matchId);
        if (registration == null)
        {
            return OnlineResult<OnlineMatchDomainEntry>.Fail(OnlineErrorCode.ResourceNotFound, "对局标识不存在或已失效");
        }

        if (!registration.PlayerIds.Contains(scope.PlayerId))
        {
            return OnlineResult<OnlineMatchDomainEntry>.Fail(OnlineErrorCode.ScopeDenied, "入场者不在本局玩家集合中");
        }

        if (registration.PlayerIds.Count != _game.MinPlayers)
        {
            return OnlineResult<OnlineMatchDomainEntry>.Fail(OnlineErrorCode.ParameterInvalid, "对局成员数与玩法最小人数不一致");
        }

        return null;
    }

    /// <summary>
    /// 解析或创建对局 Actor（create-or-reuse：并发同键创建经 store 唯一约束裁决后重解析一次）。
    /// </summary>
    /// <param name="scope">请求作用域。</param>
    /// <param name="registration">分配登记项。</param>
    /// <param name="runtimeMatchId">运行时对局标识。</param>
    /// <param name="now">当前毫秒时间戳。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>对局 Actor 结果（失败携带错误码与描述）。</returns>
    private async Task<OnlineResult<OnlineMatchActor>> ResolveOrCreateActorAsync(OnlineScope scope, AssignmentRegistration registration, string runtimeMatchId, long now, CancellationToken cancellationToken)
    {
        var actor = await _runtime.ResolveActorAsync(scope.TenantId, scope.AppId, runtimeMatchId, cancellationToken).ConfigureAwait(false);
        if (actor == null)
        {
            var assignment = new OnlineMatchAssignment
            {
                TenantId = scope.TenantId,
                AppId = scope.AppId,
                ServerId = scope.ServerId,
                MatchId = runtimeMatchId,
                AssignmentId = registration.AssignmentId,
                Mode = _game.Mode,
                Region = registration.Region,
                CreatedAtTime = registration.CreatedAtTime,
                TicketIds = new List<string>(registration.TicketIds),
                PlayerIds = new List<long>(registration.PlayerIds),
            };

            var created = await _runtime.CreateFromAssignmentAsync(assignment, now, cancellationToken).ConfigureAwait(false);
            if (!created.IsSuccess && created.Code != OnlineErrorCode.VersionConflict)
            {
                return OnlineResult<OnlineMatchActor>.Fail(created.Code, created.Message);
            }

            // 并发同键创建：store 唯一约束裁决后重解析一次（幂等并发路径）。
            actor = await _runtime.ResolveActorAsync(scope.TenantId, scope.AppId, runtimeMatchId, cancellationToken).ConfigureAwait(false);
            if (actor == null)
            {
                return OnlineResult<OnlineMatchActor>.Fail(created.IsSuccess ? OnlineErrorCode.InternalError : created.Code, created.Message);
            }
        }

        return OnlineResult<OnlineMatchActor>.Ok(actor);
    }

    /// <summary>
    /// 只将入场玩家本人置为就绪；全员就绪（对局进入 <c>Ready</c> 态）时才尝试开局，未全员就绪直接交回调用方以最终快照收口。
    /// </summary>
    /// <param name="scope">请求作用域。</param>
    /// <param name="actor">对局 Actor。</param>
    /// <param name="snapshot">对局快照（前置状态判断来源）。</param>
    /// <param name="registration">分配登记项。</param>
    /// <param name="matchId">数值对局标识。</param>
    /// <param name="now">当前毫秒时间戳。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>定论结果（并发竞态幂等成功或启动失败）；启动成功返回 <c>null</c>，由调用方以最终快照收口。</returns>
    private static async Task<OnlineResult<OnlineMatchDomainEntry>> MarkMembersReadyAndStartAsync(OnlineScope scope, OnlineMatchActor actor, OnlineMatchSnapshot snapshot, AssignmentRegistration registration, long matchId, long now, CancellationToken cancellationToken)
    {
        // 仅置入场玩家本人就绪（GFX-1099）：不得替未到场的对手置 Ready，否则首入场者会单方面触发开局。
        await actor.SetReadyAsync(scope, true, now, cancellationToken).ConfigureAwait(false);

        var afterReady = actor.GetSnapshot();
        if (afterReady.State != OnlineMatchState.Ready)
        {
            // 尚有座位未就绪（对手未入场或未确认）：不开局，等待后续入场或超时弃权。
            return null;
        }

        var started = await actor.StartAsync(WithPlayer(scope, registration.PlayerIds[0]), now, cancellationToken).ConfigureAwait(false);
        if (!started.IsSuccess)
        {
            // 并发入场竞态：对手已抢先完成开局（对局已 Running）= 双方均已到达，按幂等成功收敛，
            // 不把「已被开局」误报为失败（双端同时入场是常态路径，非异常）。
            var afterStart = actor.GetSnapshot();
            if (afterStart.State == OnlineMatchState.Running)
            {
                return OnlineResult<OnlineMatchDomainEntry>.Ok(new OnlineMatchDomainEntry
                {
                    MatchId = matchId,
                    Started = true,
                    PlayerIds = new List<long>(registration.PlayerIds),
                });
            }

            return OnlineResult<OnlineMatchDomainEntry>.Fail(started.Code, started.Message);
        }

        return null;
    }

    /// <summary>
    /// 定位玩家当前对局（票据 → 分配登记 → 运行时 Actor → 成员表核对）。
    /// </summary>
    /// <param name="scope">生效作用域（含玩家主体位）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>(数值对局标识, Actor, 成员)；无归属返回 null。</returns>
    private async Task<(long NumericMatchId, OnlineMatchActor Actor, OnlineMatchMember Member)?> LocatePlayerMatchAsync(OnlineScope scope, CancellationToken cancellationToken)
    {
        var latest = await _store.FindLatestByPlayerAsync(scope.TenantId, scope.AppId, scope.PlayerId, cancellationToken).ConfigureAwait(false);
        if (latest == null || latest.State != OnlineMatchTicketState.Matched || string.IsNullOrEmpty(latest.AssignmentId))
        {
            return null;
        }

        var registration = FindRegistration(latest.AssignmentId);
        if (registration == null || !registration.PlayerIds.Contains(scope.PlayerId))
        {
            return null;
        }

        var runtimeMatchId = DeriveRuntimeMatchId(scope.ServerId, registration.NumericMatchId);
        var actor = await _runtime.ResolveActorAsync(scope.TenantId, scope.AppId, runtimeMatchId, cancellationToken).ConfigureAwait(false);
        if (actor == null)
        {
            return null;
        }

        return (registration.NumericMatchId, actor, FindMember(actor.GetSnapshot(), scope.PlayerId));
    }

    /// <summary>
    /// 从快照成员表中定位玩家成员。
    /// </summary>
    /// <param name="snapshot">对局快照。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <returns>成员；不在表中返回 null。</returns>
    private static OnlineMatchMember FindMember(OnlineMatchSnapshot snapshot, long playerId)
    {
        if (snapshot?.Members == null)
        {
            return null;
        }

        foreach (var member in snapshot.Members)
        {
            if (member != null && member.PlayerId == playerId)
            {
                return member;
            }
        }

        return null;
    }

    /// <summary>
    /// 解析已撮合票据的数值对局标识（未撮合 / 分配缺席为 0；必要时懒登记）。
    /// </summary>
    /// <param name="ticket">玩家最近票据。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>数值对局标识；未撮合为 0。</returns>
    private async Task<long> ResolveMatchedNumericAsync(OnlineMatchTicket ticket, CancellationToken cancellationToken)
    {
        if (ticket == null || ticket.State != OnlineMatchTicketState.Matched || string.IsNullOrEmpty(ticket.AssignmentId))
        {
            return 0;
        }

        var registration = FindRegistration(ticket.AssignmentId);
        if (registration != null)
        {
            return registration.NumericMatchId;
        }

        // 懒登记：撮合可能发生在后台 tick（等待方首次查询 / 通知推送前于此收敛为同一数值标识）。
        var assignment = await _store.FindAssignmentAsync(ticket.TenantId, ticket.AppId, ticket.AssignmentId, cancellationToken).ConfigureAwait(false);
        return assignment == null ? 0 : RegisterAssignment(assignment).NumericMatchId;
    }

    /// <summary>
    /// 按分配标识查登记（锁内读；未登记返回 null）。
    /// </summary>
    /// <param name="assignmentId">分配标识。</param>
    /// <returns>登记项。</returns>
    private AssignmentRegistration FindRegistration(string assignmentId)
    {
        SweepExpiredRegistrations();
        lock (_registryLock)
        {
            return _byAssignment.TryGetValue(assignmentId, out var registration) ? registration : null;
        }
    }

    /// <summary>
    /// 按数值对局标识查登记（锁内读；未登记返回 null）。
    /// </summary>
    /// <param name="matchId">数值对局标识。</param>
    /// <returns>登记项。</returns>
    private AssignmentRegistration FindRegistration(long matchId)
    {
        SweepExpiredRegistrations();
        lock (_registryLock)
        {
            return _byNumeric.TryGetValue(matchId, out var registration) ? registration : null;
        }
    }

    /// <summary>
    /// 登记分配（幂等：已登记返回既有项；新登记分配数值标识并建立双向索引）。
    /// </summary>
    /// <param name="assignment">匹配分配。</param>
    /// <returns>登记项。</returns>
    private AssignmentRegistration RegisterAssignment(OnlineMatchAssignment assignment)
    {
        SweepExpiredRegistrations();
        lock (_registryLock)
        {
            if (_byAssignment.TryGetValue(assignment.AssignmentId, out var existing))
            {
                return existing;
            }

            var registration = new AssignmentRegistration
            {
                AssignmentId = assignment.AssignmentId,
                NumericMatchId = _nextNumeric++,
                Mode = assignment.Mode,
                Region = assignment.Region,
                CreatedAtTime = assignment.CreatedAtTime,
                PlayerIds = assignment.PlayerIds == null ? new List<long>() : new List<long>(assignment.PlayerIds),
                TicketIds = assignment.TicketIds == null ? new List<string>() : new List<string>(assignment.TicketIds),
            };

            _byAssignment[registration.AssignmentId] = registration;
            _byNumeric[registration.NumericMatchId] = registration;
            return registration;
        }
    }

    /// <summary>
    /// 惰性清扫过期登记项（登记表访问路径触发；节流间隔内跳过）：
    /// 分配时刻距今超过生命周期的登记项从双向索引移除——此时对局必然早已终态释放
    ///（生命周期必须大于单局全生命周期上界，见 <see cref="OnlineMatchRegistryOptions"/> 类约束）。
    /// <para>
    /// 过期项移除后，票据仍处已匹配态的玩家再查询会经懒登记重建并分配新数值标识——与进程重启后语义一致。
    /// 全程持 <see cref="_registryLock"/>，与查找 / 登记同临界区，无新增竞态面。
    /// </para>
    /// </summary>
    private void SweepExpiredRegistrations()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        lock (_registryLock)
        {
            if (now - _lastSweepTimeUnixMilliseconds < _registry.RegistrationSweepIntervalSeconds * 1000L)
            {
                return;
            }

            _lastSweepTimeUnixMilliseconds = now;
            var lifetimeMilliseconds = _registry.AssignmentRegistrationLifetimeSeconds * 1000L;
            List<string> expiredAssignmentIds = null;
            foreach (var pair in _byAssignment)
            {
                if (now - pair.Value.CreatedAtTime > lifetimeMilliseconds)
                {
                    if (expiredAssignmentIds == null)
                    {
                        expiredAssignmentIds = new List<string>();
                    }

                    expiredAssignmentIds.Add(pair.Key);
                }
            }

            if (expiredAssignmentIds == null)
            {
                return;
            }

            foreach (var assignmentId in expiredAssignmentIds)
            {
                if (_byAssignment.Remove(assignmentId, out var registration))
                {
                    _byNumeric.Remove(registration.NumericMatchId);
                }
            }
        }
    }

    /// <summary>
    /// 确定性派生运行时对局标识（同数值标识恒同值；跨区服不撞，与房间桥同型）。
    /// </summary>
    /// <param name="serverId">区服标识。</param>
    /// <param name="matchId">数值对局标识。</param>
    /// <returns>运行时对局标识。</returns>
    private static string DeriveRuntimeMatchId(long serverId, long matchId)
    {
        return string.Concat(
            "matched-s",
            serverId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-m",
            matchId.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// 校验对局活跃成员集合与分配成员表一致（集合相等，忽略顺序）。
    /// </summary>
    /// <param name="snapshot">对局快照。</param>
    /// <param name="playerIds">分配成员表。</param>
    /// <returns>一致返回 <c>true</c>。</returns>
    private static bool MembersMatch(OnlineMatchSnapshot snapshot, IReadOnlyList<long> playerIds)
    {
        var active = new HashSet<long>();
        foreach (var member in snapshot.Members)
        {
            if (member == null || member.State == OnlineMatchMemberState.Left || member.State == OnlineMatchMemberState.Kicked)
            {
                continue;
            }

            active.Add(member.PlayerId);
        }

        if (active.Count != playerIds.Count)
        {
            return false;
        }

        foreach (var playerId in playerIds)
        {
            if (!active.Contains(playerId))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 以既有作用域三元组派生玩家位作用域。
    /// </summary>
    /// <param name="scope">请求作用域。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <returns>玩家级作用域。</returns>
    private static OnlineScope WithPlayer(OnlineScope scope, long playerId)
    {
        return new OnlineScope(scope.TenantId, scope.AppId, scope.ServerId, playerId);
    }

    /// <summary>
    /// 校验作用域与玩家主体位。
    /// </summary>
    /// <param name="scope">生效作用域。</param>
    /// <returns>成功返回 null，失败返回错误结果。</returns>
    private static (OnlineErrorCode Code, string Message)? ValidateScope(OnlineScope scope)
    {
        if (scope == null)
        {
            return (OnlineErrorCode.ParameterInvalid, "缺少作用域");
        }

        if (scope.PlayerId <= 0)
        {
            return (OnlineErrorCode.ParameterInvalid, "匹配操作必须具备玩家主体位");
        }

        return null;
    }

    /// <summary>
    /// 匹配分配登记项（assignment ↔ 协议数值对局标识 ↔ 成员表）。
    /// </summary>
    private sealed class AssignmentRegistration
    {
        /// <summary>分配标识。</summary>
        public string AssignmentId { get; set; }

        /// <summary>协议数值对局标识。</summary>
        public long NumericMatchId { get; set; }

        /// <summary>匹配模式（协议标签）。</summary>
        public int Mode { get; set; }

        /// <summary>区域。</summary>
        public int Region { get; set; }

        /// <summary>分配时刻（UTC 毫秒）。</summary>
        public long CreatedAtTime { get; set; }

        /// <summary>成员表。</summary>
        public List<long> PlayerIds { get; set; }

        /// <summary>分配消费的票据标识。</summary>
        public List<string> TicketIds { get; set; }
    }
}
