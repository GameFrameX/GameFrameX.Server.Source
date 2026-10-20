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
/// 匹配域服务（C246）：<see cref="IOnlineMatchDomain"/> 的本域实现——旧 420 协议族语义
///（入队 / 取消 / 状态 / 入场）到票据撮合域与对局运行时的桥接编排。
/// <para>
/// 维护约束（红线）：
/// ① 匹配池唯一权威 = 票据域——入队经 <see cref="OnlineMatchTicketService"/>、撮合经
/// <see cref="OnlineMatchmakerCoordinator"/>（同一票据存储），本类不持有任何队列或池副本；
/// ② 即时窗口兼容：入队建票后同步执行一轮撮合（对手已在池时本轮即成组，响应即刻带数值对局标识，
/// 等价旧 FIFO 链第二人入队即配对的可观测行为）；等待方经状态轮询 / 通知获知；
/// ③ 数值对局标识登记表（assignment → 协议数值 id 单射）是本类唯一的派生状态——InMemory、
/// 与票据存储同生命周期（重启即失效，C245 榜单同款易失边界），计数种子取构造时刻避免跨重启撞号；
/// ④ 入场 = create-or-reuse：以 <c>matched-s{区服}-m{数值}</c> 确定性派生运行时对局标识，
/// 复用 <see cref="OnlineMatchRuntime"/> 既有 Actor 生命周期（C245 房间桥同型），不发明对局玩法新面；
/// ⑤ 模式标签（1/2）只是撮合池分区键，入场绑定的玩法模式取构造注入的样例玩法
///（与 C245 房间桥「协议形状 → 玩法模式」解耦口径一致）。
/// </para>
/// </summary>
public sealed class OnlineMatchDomainService : IOnlineMatchDomain
{
    /// <summary>评分基础窗（旧 FIFO 链 MatchRules.BaseWindowRating 同值：评分差 ±100）。</summary>
    public const int BaseRatingWindow = 100;

    /// <summary>单人票据缺省区域（与 C245 房间桥 Region=1 口径一致）。</summary>
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

    /// <summary>
    /// 初始化 <see cref="OnlineMatchDomainService"/>。
    /// </summary>
    /// <param name="store">票据存储。</param>
    /// <param name="tickets">票据服务。</param>
    /// <param name="coordinator">撮合协调器。</param>
    /// <param name="runtime">对局运行时。</param>
    /// <param name="game">入场绑定的玩法实现。</param>
    public OnlineMatchDomainService(IOnlineMatchTicketStore store, OnlineMatchTicketService tickets, OnlineMatchmakerCoordinator coordinator, OnlineMatchRuntime runtime, IOnlineMatchGame game)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _tickets = tickets ?? throw new ArgumentNullException(nameof(tickets));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _game = game ?? throw new ArgumentNullException(nameof(game));
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
    public async Task<OnlineResult<OnlineMatchDomainEntry>> EnterAsync(OnlineScope scope, long matchId, CancellationToken cancellationToken = default)
    {
        var failureCode = ValidateScope(scope);
        if (failureCode.HasValue)
        {
            return OnlineResult<OnlineMatchDomainEntry>.Fail(failureCode.Value.Code, failureCode.Value.Message);
        }

        if (matchId <= 0)
        {
            return OnlineResult<OnlineMatchDomainEntry>.Fail(OnlineErrorCode.ParameterInvalid, "对局标识非法");
        }

        var registration = FindRegistration(matchId);
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

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var runtimeMatchId = DeriveRuntimeMatchId(scope.ServerId, matchId);
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
                return OnlineResult<OnlineMatchDomainEntry>.Fail(created.Code, created.Message);
            }

            // 并发同键创建：store 唯一约束裁决后重解析一次（幂等并发路径，C245 同型）。
            actor = await _runtime.ResolveActorAsync(scope.TenantId, scope.AppId, runtimeMatchId, cancellationToken).ConfigureAwait(false);
            if (actor == null)
            {
                return OnlineResult<OnlineMatchDomainEntry>.Fail(created.IsSuccess ? OnlineErrorCode.InternalError : created.Code, created.Message);
            }
        }

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
            foreach (var member in snapshot.Members)
            {
                if (member == null || member.State == OnlineMatchMemberState.Left || member.State == OnlineMatchMemberState.Kicked)
                {
                    continue;
                }

                await actor.SetReadyAsync(WithPlayer(scope, member.PlayerId), true, now, cancellationToken).ConfigureAwait(false);
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
        }

        var finalSnapshot = actor.GetSnapshot();
        return OnlineResult<OnlineMatchDomainEntry>.Ok(new OnlineMatchDomainEntry
        {
            MatchId = matchId,
            Started = finalSnapshot.State == OnlineMatchState.Running,
            PlayerIds = new List<long>(registration.PlayerIds),
        });
    }

    /// <inheritdoc />
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
    /// 确定性派生运行时对局标识（同数值标识恒同值；跨区服不撞，C245 房间桥同型）。
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
