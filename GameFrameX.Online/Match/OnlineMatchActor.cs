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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Online.Contracts;
using GameFrameX.Online.Events;
using GameFrameX.Online.Scope;

namespace GameFrameX.Online.Match;

/// <summary>
/// 对局 Actor——单场对局的**唯一**状态持有者与改写者（vault:C6 S5.1「Match Actor 所有权」）。
/// <para>
/// 维护约束（红线）：① 所有状态改写都经过「门内改写候选副本 → 存储层 CAS → 成功才替换权威副本」
/// 三段式，CAS 失败即表示本 Actor 已失去所有权（另一 Actor 推进过同一对局），此时**不得**保留任何
/// 本地改写（VC-5.13 无串局）；② 客户端只能提交 <see cref="OnlineMatchInput"/> 意图，
/// 胜负与奖励一律由玩法实现依服务端状态裁决（VC-5.2）；③ 每个入口都校验
/// <see cref="OnlineScope"/>，跨租户/App/Server 一律 <see cref="OnlineErrorCode.ScopeDenied"/>。
/// </para>
/// <para>
/// 输入校验顺序固定为：成员身份 → 成员在线状态 → 对局阶段 → 重复包 → 乱序包 → 玩法裁决。
/// 前三步属于「谁在说话」，后两步属于「说的话算不算数」；被拒绝的输入一律不推进
/// <see cref="OnlineMatch.ServerSequence"/>，也不写事件日志（VC-5.3 / VC-5.4 / VC-5.5）。
/// </para>
/// </summary>
public sealed class OnlineMatchActor
{
    /// <summary>串行门（同一 Actor 的操作互斥；跨 Actor 互斥由存储 CAS 保证）。</summary>
    private readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);

    /// <summary>对局存储。</summary>
    private readonly IOnlineMatchActorStore _store;

    /// <summary>事件发布器。</summary>
    private readonly IOnlineEventPublisher _eventPublisher;

    /// <summary>玩法实现。</summary>
    private readonly IOnlineMatchGame _game;

    /// <summary>运行参数（按引用共享，装配后不建议改动）。</summary>
    private readonly OnlineMatchRuntimeOptions _options;

    /// <summary>权威对局状态（本 Actor 独占）。</summary>
    private OnlineMatch _match;

    /// <summary>
    /// 初始化对局 Actor。
    /// </summary>
    /// <param name="match">已落库的对局（权威副本由此派生）。</param>
    /// <param name="game">玩法实现。</param>
    /// <param name="store">对局存储。</param>
    /// <param name="eventPublisher">事件发布器（可空，测试可传空实现）。</param>
    /// <param name="options">运行参数（可空，取默认值）。</param>
    public OnlineMatchActor(OnlineMatch match, IOnlineMatchGame game, IOnlineMatchActorStore store, IOnlineEventPublisher eventPublisher, OnlineMatchRuntimeOptions options = null)
    {
        if (match == null)
        {
            throw new ArgumentNullException(nameof(match));
        }

        _match = match.Copy();
        _game = game ?? throw new ArgumentNullException(nameof(game));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _eventPublisher = eventPublisher;
        _options = options == null ? new OnlineMatchRuntimeOptions() : options;
    }

    /// <summary>
    /// 获取对局标识。
    /// </summary>
    public string MatchId
    {
        get { return _match.MatchId; }
    }

    /// <summary>
    /// 获取租户标识。
    /// </summary>
    public long TenantId
    {
        get { return _match.TenantId; }
    }

    /// <summary>
    /// 获取 App 标识。
    /// </summary>
    public long AppId
    {
        get { return _match.AppId; }
    }

    /// <summary>
    /// 获取当前生命周期状态。
    /// </summary>
    public OnlineMatchState State
    {
        get { return _match.State; }
    }

    /// <summary>
    /// 获取当前服务端权威序号。
    /// </summary>
    public long ServerSequence
    {
        get { return _match.ServerSequence; }
    }

    /// <summary>
    /// 获取是否已进入终态（可供运行时释放，VC-5.11）。
    /// </summary>
    public bool IsClosed
    {
        get { return _match.State == OnlineMatchState.Closed; }
    }

    /// <summary>
    /// 取对局快照（服务端权威状态在一个序号上的完整切片）。
    /// </summary>
    /// <returns>快照副本。</returns>
    public OnlineMatchSnapshot GetSnapshot()
    {
        var snapshot = BuildSnapshot(_match);
        return snapshot;
    }

    /// <summary>
    /// 判定能否从指定序号开始补发增量。
    /// </summary>
    /// <param name="fromSequence">客户端最后确认的服务器序号。</param>
    /// <returns>事件日志仍覆盖该序号之后全部事件时返回 <c>true</c>；否则调用方应回退全量快照。</returns>
    public bool CanServeDelta(long fromSequence)
    {
        if (_match.Events == null || _match.Events.Count == 0)
        {
            return true;
        }

        return fromSequence >= _match.Events[0].Sequence - 1;
    }

    /// <summary>
    /// 构造增量（左开右闭区间 <paramref name="fromSequence"/> ~ 当前序号）。
    /// </summary>
    /// <param name="fromSequence">客户端最后确认的服务器序号（不含）。</param>
    /// <returns>增量副本。</returns>
    public OnlineMatchDelta BuildDelta(long fromSequence)
    {
        var events = new List<OnlineMatchServerEvent>();
        if (_match.Events != null)
        {
            foreach (var item in _match.Events)
            {
                if (item != null && item.Sequence > fromSequence)
                {
                    events.Add(item.Copy());
                }
            }
        }

        return new OnlineMatchDelta
        {
            MatchId = _match.MatchId,
            FromSequence = fromSequence,
            ToSequence = _match.ServerSequence,
            Events = events,
        };
    }

    /// <summary>
    /// 加入对局（S5.3）。
    /// </summary>
    /// <param name="scope">请求作用域（玩家取自 <see cref="OnlineScope.PlayerId"/>）。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>加入后的快照；失败返回对应错误码。</returns>
    public Task<OnlineResult<OnlineMatchSnapshot>> JoinAsync(OnlineScope scope, long nowUnixMilliseconds, CancellationToken cancellationToken = default)
    {
        return MutateAsync(scope, nowUnixMilliseconds, cancellationToken, (match, actorScope) =>
        {
            if (OnlineMatchStateMachine.IsEnded(match.State))
            {
                return Fail<OnlineMatchSnapshot>(OnlineErrorCode.StateEnded, "对局已结束，无法加入");
            }

            if (match.State != OnlineMatchState.Created && match.State != OnlineMatchState.Waiting && match.State != OnlineMatchState.Ready)
            {
                return Fail<OnlineMatchSnapshot>(OnlineErrorCode.StateOperationForbidden, "对局已开始，无法加入");
            }

            var existing = match.FindMember(actorScope.PlayerId);
            if (existing != null && existing.State != OnlineMatchMemberState.Left && existing.State != OnlineMatchMemberState.Kicked)
            {
                return Fail<OnlineMatchSnapshot>(OnlineErrorCode.StateOperationForbidden, "玩家已在对局中");
            }

            if (match.CountActiveMembers() >= _game.MaxPlayers)
            {
                return Fail<OnlineMatchSnapshot>(OnlineErrorCode.StateOperationForbidden, "对局人数已满");
            }

            if (existing != null)
            {
                existing.State = OnlineMatchMemberState.Joined;
                existing.StateChangedTime = nowUnixMilliseconds;
                existing.DisconnectedTime = 0;
                existing.ReconnectDeadlineTime = 0;
                existing.ReconnectToken = null;
            }
            else
            {
                match.Members.Add(new OnlineMatchMember
                {
                    PlayerId = actorScope.PlayerId,
                    State = OnlineMatchMemberState.Joined,
                    JoinedTime = nowUnixMilliseconds,
                    StateChangedTime = nowUnixMilliseconds,
                });
            }

            if (match.State == OnlineMatchState.Created)
            {
                Transition(match, OnlineMatchState.Waiting, nowUnixMilliseconds);
            }

            AppendServerEvent(match, "MemberJoined", null, nowUnixMilliseconds);
            return Ok(BuildSnapshot(match));
        });
    }

    /// <summary>
    /// 退出对局（S5.3）。退出不影响他人结算（VC-5.14）。
    /// </summary>
    /// <param name="scope">请求作用域。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>退出后的快照；失败返回对应错误码。</returns>
    public Task<OnlineResult<OnlineMatchSnapshot>> LeaveAsync(OnlineScope scope, long nowUnixMilliseconds, CancellationToken cancellationToken = default)
    {
        return MutateAsync(scope, nowUnixMilliseconds, cancellationToken, (match, actorScope) =>
        {
            var member = match.FindMember(actorScope.PlayerId);
            if (member == null || member.State == OnlineMatchMemberState.Left || member.State == OnlineMatchMemberState.Kicked)
            {
                return Fail<OnlineMatchSnapshot>(OnlineErrorCode.ResourceNotFound, "玩家不在对局中");
            }

            member.State = OnlineMatchMemberState.Left;
            member.StateChangedTime = nowUnixMilliseconds;
            member.ReconnectToken = null;
            AppendServerEvent(match, "MemberLeft", null, nowUnixMilliseconds);

            if (match.CountActiveMembers() == 0 && !OnlineMatchStateMachine.IsEnded(match.State))
            {
                Transition(match, OnlineMatchState.Cancelled, nowUnixMilliseconds);
            }

            return Ok(BuildSnapshot(match));
        });
    }

    /// <summary>
    /// 踢出成员（S5.3，仅房主在开局前可执行；房主 = 最早加入的成员）。
    /// </summary>
    /// <param name="scope">请求作用域（房主）。</param>
    /// <param name="targetPlayerId">被踢出玩家标识。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>踢出后的快照；失败返回对应错误码。</returns>
    public Task<OnlineResult<OnlineMatchSnapshot>> KickAsync(OnlineScope scope, long targetPlayerId, long nowUnixMilliseconds, CancellationToken cancellationToken = default)
    {
        return MutateAsync(scope, nowUnixMilliseconds, cancellationToken, (match, actorScope) =>
        {
            if (match.State != OnlineMatchState.Created && match.State != OnlineMatchState.Waiting && match.State != OnlineMatchState.Ready)
            {
                return Fail<OnlineMatchSnapshot>(OnlineErrorCode.StateOperationForbidden, "仅开局前可踢出成员");
            }

            var requester = match.FindMember(actorScope.PlayerId);
            if (requester == null || requester.State == OnlineMatchMemberState.Left || requester.State == OnlineMatchMemberState.Kicked)
            {
                return Fail<OnlineMatchSnapshot>(OnlineErrorCode.ScopeDenied, "请求者不在对局中");
            }

            if (requester.PlayerId != FindHostPlayerId(match))
            {
                return Fail<OnlineMatchSnapshot>(OnlineErrorCode.StateOperationForbidden, "仅房主可踢出成员");
            }

            var target = match.FindMember(targetPlayerId);
            if (target == null || target.State == OnlineMatchMemberState.Left || target.State == OnlineMatchMemberState.Kicked)
            {
                return Fail<OnlineMatchSnapshot>(OnlineErrorCode.ResourceNotFound, "目标玩家不在对局中");
            }

            target.State = OnlineMatchMemberState.Kicked;
            target.StateChangedTime = nowUnixMilliseconds;
            target.ReconnectToken = null;
            AppendServerEvent(match, "MemberKicked", null, nowUnixMilliseconds);

            if (match.CountActiveMembers() == 0 && !OnlineMatchStateMachine.IsEnded(match.State))
            {
                Transition(match, OnlineMatchState.Cancelled, nowUnixMilliseconds);
            }

            return Ok(BuildSnapshot(match));
        });
    }

    /// <summary>
    /// 设置准备状态（S5.3）。全员准备且人数达标时进入 <see cref="OnlineMatchState.Ready"/>。
    /// </summary>
    /// <param name="scope">请求作用域。</param>
    /// <param name="ready">是否准备。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>设置后的快照；失败返回对应错误码。</returns>
    public Task<OnlineResult<OnlineMatchSnapshot>> SetReadyAsync(OnlineScope scope, bool ready, long nowUnixMilliseconds, CancellationToken cancellationToken = default)
    {
        return MutateAsync(scope, nowUnixMilliseconds, cancellationToken, (match, actorScope) =>
        {
            if (match.State != OnlineMatchState.Created && match.State != OnlineMatchState.Waiting && match.State != OnlineMatchState.Ready)
            {
                return Fail<OnlineMatchSnapshot>(OnlineErrorCode.StateOperationForbidden, "对局已开始，无法变更准备状态");
            }

            var member = match.FindMember(actorScope.PlayerId);
            if (member == null || (member.State != OnlineMatchMemberState.Joined && member.State != OnlineMatchMemberState.Ready))
            {
                return Fail<OnlineMatchSnapshot>(OnlineErrorCode.ResourceNotFound, "玩家不在可准备状态");
            }

            member.State = ready ? OnlineMatchMemberState.Ready : OnlineMatchMemberState.Joined;
            member.StateChangedTime = nowUnixMilliseconds;
            AppendServerEvent(match, ready ? "MemberReady" : "MemberUnready", null, nowUnixMilliseconds);

            var target = AreAllMembersReady(match) ? OnlineMatchState.Ready : OnlineMatchState.Waiting;
            if (match.State != target)
            {
                Transition(match, target, nowUnixMilliseconds);
            }

            return Ok(BuildSnapshot(match));
        });
    }

    /// <summary>
    /// 开始对局（S5.3，仅房主可执行）。
    /// </summary>
    /// <param name="scope">请求作用域（房主）。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>开始后的快照；失败返回对应错误码。</returns>
    public Task<OnlineResult<OnlineMatchSnapshot>> StartAsync(OnlineScope scope, long nowUnixMilliseconds, CancellationToken cancellationToken = default)
    {
        return MutateAsync(scope, nowUnixMilliseconds, cancellationToken, (match, actorScope) =>
        {
            if (match.State != OnlineMatchState.Ready)
            {
                return Fail<OnlineMatchSnapshot>(OnlineErrorCode.StateNotReady, "对局未处于可开始状态");
            }

            if (actorScope.PlayerId != FindHostPlayerId(match))
            {
                return Fail<OnlineMatchSnapshot>(OnlineErrorCode.StateOperationForbidden, "仅房主可开始对局");
            }

            if (match.CountActiveMembers() < _game.MinPlayers)
            {
                return Fail<OnlineMatchSnapshot>(OnlineErrorCode.StateNotReady, "参与人数未达到玩法下限");
            }

            Transition(match, OnlineMatchState.Running, nowUnixMilliseconds);
            foreach (var member in match.Members)
            {
                if (member != null && member.State == OnlineMatchMemberState.Ready)
                {
                    member.State = OnlineMatchMemberState.Playing;
                    member.StateChangedTime = nowUnixMilliseconds;
                }
            }

            match.GameState = _game.CreateInitialState(match);
            AppendServerEvent(match, "MatchStarted", null, nowUnixMilliseconds);
            return Ok(BuildSnapshot(match));
        });
    }

    /// <summary>
    /// 提交输入（S5.2 / S5.5）。被拒绝的输入不改写状态、不推进序号。
    /// </summary>
    /// <param name="scope">请求作用域。</param>
    /// <param name="input">输入意图。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>输入确认；成员或阶段类拒绝以确认形式返回，不视为异常。</returns>
    public Task<OnlineResult<OnlineMatchInputAck>> SubmitInputAsync(OnlineScope scope, OnlineMatchInput input, long nowUnixMilliseconds, CancellationToken cancellationToken = default)
    {
        if (input == null)
        {
            return Task.FromResult(Fail<OnlineMatchInputAck>(OnlineErrorCode.ParameterInvalid, "输入不能为空"));
        }

        return MutateAsync(scope, nowUnixMilliseconds, cancellationToken, (match, actorScope) => ApplyInput(match, actorScope, input, nowUnixMilliseconds));
    }

    /// <summary>
    /// 标记成员断线（S5.6：断线不立即等于退出，窗口内可重连）。
    /// </summary>
    /// <param name="scope">请求作用域。</param>
    /// <param name="reconnectToken">服务端签发的重连令牌。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>标记后的快照；失败返回对应错误码。</returns>
    public Task<OnlineResult<OnlineMatchSnapshot>> MarkDisconnectedAsync(OnlineScope scope, string reconnectToken, long nowUnixMilliseconds, CancellationToken cancellationToken = default)
    {
        return MutateAsync(scope, nowUnixMilliseconds, cancellationToken, (match, actorScope) =>
        {
            var member = match.FindMember(actorScope.PlayerId);
            if (member == null || member.State == OnlineMatchMemberState.Left || member.State == OnlineMatchMemberState.Kicked)
            {
                return Fail<OnlineMatchSnapshot>(OnlineErrorCode.ResourceNotFound, "玩家不在对局中");
            }

            if (OnlineMatchStateMachine.IsEnded(match.State))
            {
                return Fail<OnlineMatchSnapshot>(OnlineErrorCode.StateEnded, "对局已结束");
            }

            if (member.State == OnlineMatchMemberState.Disconnected)
            {
                return Ok(BuildSnapshot(match));
            }

            member.State = OnlineMatchMemberState.Disconnected;
            member.DisconnectedTime = nowUnixMilliseconds;
            member.ReconnectDeadlineTime = nowUnixMilliseconds + (_options.ReconnectWindowSeconds * 1000L);
            member.ReconnectToken = reconnectToken;
            member.StateChangedTime = nowUnixMilliseconds;
            AppendServerEvent(match, "MemberDisconnected", null, nowUnixMilliseconds);
            return Ok(BuildSnapshot(match));
        });
    }

    /// <summary>
    /// 窗口内重连（S5.6）：成员回到对局中，调用方随即补发快照 + 缺失增量。
    /// </summary>
    /// <param name="scope">请求作用域。</param>
    /// <param name="reconnectToken">重连令牌（必须与断线时签发的一致）。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>重连后的快照；失败返回对应错误码。</returns>
    public Task<OnlineResult<OnlineMatchSnapshot>> ResumeAsync(OnlineScope scope, string reconnectToken, long nowUnixMilliseconds, CancellationToken cancellationToken = default)
    {
        return MutateAsync(scope, nowUnixMilliseconds, cancellationToken, (match, actorScope) =>
        {
            var member = match.FindMember(actorScope.PlayerId);
            if (member == null)
            {
                return Fail<OnlineMatchSnapshot>(OnlineErrorCode.ResourceNotFound, "玩家不在对局中");
            }

            if (member.State != OnlineMatchMemberState.Disconnected)
            {
                return Fail<OnlineMatchSnapshot>(OnlineErrorCode.StateNotReady, "玩家未处于断线态");
            }

            if (string.IsNullOrEmpty(reconnectToken) || member.ReconnectToken != reconnectToken)
            {
                return Fail<OnlineMatchSnapshot>(OnlineErrorCode.TokenRevoked, "重连令牌无效");
            }

            if (nowUnixMilliseconds > member.ReconnectDeadlineTime)
            {
                return Fail<OnlineMatchSnapshot>(OnlineErrorCode.NetworkTimeout, "重连窗口已超时");
            }

            member.State = OnlineMatchStateMachine.IsPlayable(match.State) ? OnlineMatchMemberState.Playing : OnlineMatchMemberState.Ready;
            member.DisconnectedTime = 0;
            member.ReconnectDeadlineTime = 0;
            member.ReconnectToken = null;
            member.StateChangedTime = nowUnixMilliseconds;
            AppendServerEvent(match, "MemberResumed", null, nowUnixMilliseconds);
            return Ok(BuildSnapshot(match));
        });
    }

    /// <summary>
    /// 在结算阶段产出结算候选（S5.7：事实先落定，发奖解耦）。
    /// </summary>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>结算候选（<see cref="OnlineMatchResult.MatchResultId"/> 由本方法分配）；阶段不符返回失败。</returns>
    public Task<OnlineResult<OnlineMatchResult>> BuildResultAsync(long nowUnixMilliseconds, CancellationToken cancellationToken = default)
    {
        return MutateCoreAsync<OnlineMatchResult>(SystemScope(), nowUnixMilliseconds, cancellationToken, false, (match, actorScope) =>
        {
            if (match.State != OnlineMatchState.Settling)
            {
                return Fail<OnlineMatchResult>(OnlineErrorCode.StateNotReady, "对局未处于结算阶段");
            }

            var result = _game.BuildResult(match);
            if (result == null)
            {
                return Fail<OnlineMatchResult>(OnlineErrorCode.InternalError, "玩法未产出结算结果");
            }

            result.MatchResultId = "mrs-" + Guid.NewGuid().ToString("N");
            result.MatchId = match.MatchId;
            result.TenantId = match.TenantId;
            result.AppId = match.AppId;
            result.ServerId = match.ServerId;
            result.Mode = match.Mode;
            result.Region = match.Region;
            result.Outcome = OnlineMatchState.Completed;
            result.SettledTime = nowUnixMilliseconds;
            return Ok(result);
        });
    }

    /// <summary>
    /// 结算成功收口：<see cref="OnlineMatchState.Settling"/> → <see cref="OnlineMatchState.Completed"/>（S5.7）。
    /// </summary>
    /// <param name="result">已落定的结算结果（写入对局，作为不可改写的事实）。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>收口后的快照；已收口时原样返回当前快照。</returns>
    public Task<OnlineResult<OnlineMatchSnapshot>> CompleteSettlementAsync(OnlineMatchResult result, long nowUnixMilliseconds, CancellationToken cancellationToken = default)
    {
        return MutateCoreAsync<OnlineMatchSnapshot>(SystemScope(), nowUnixMilliseconds, cancellationToken, false, (match, actorScope) =>
        {
            if (match.State == OnlineMatchState.Completed || match.State == OnlineMatchState.Closed)
            {
                return Ok(BuildSnapshot(match));
            }

            if (match.State != OnlineMatchState.Settling)
            {
                return Fail<OnlineMatchSnapshot>(OnlineErrorCode.StateNotReady, "对局未处于结算阶段");
            }

            match.Result = result == null ? null : result.Copy();
            Transition(match, OnlineMatchState.Completed, nowUnixMilliseconds);
            AppendServerEvent(match, "MatchCompleted", null, nowUnixMilliseconds);
            return Ok(BuildSnapshot(match));
        });
    }

    /// <summary>
    /// 结算失败收口：<see cref="OnlineMatchState.Settling"/> → <see cref="OnlineMatchState.SettlementFailed"/>。
    /// <para>结算事实未成立（可重试），已结束的对局状态不受影响。</para>
    /// </summary>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>收口后的快照；阶段不符返回失败。</returns>
    public Task<OnlineResult<OnlineMatchSnapshot>> MarkSettlementFailedAsync(long nowUnixMilliseconds, CancellationToken cancellationToken = default)
    {
        return MutateCoreAsync<OnlineMatchSnapshot>(SystemScope(), nowUnixMilliseconds, cancellationToken, false, (match, actorScope) =>
        {
            if (match.State != OnlineMatchState.Settling)
            {
                return Fail<OnlineMatchSnapshot>(OnlineErrorCode.StateNotReady, "对局未处于结算阶段");
            }

            Transition(match, OnlineMatchState.SettlementFailed, nowUnixMilliseconds);
            AppendServerEvent(match, "MatchSettlementFailed", null, nowUnixMilliseconds);
            return Ok(BuildSnapshot(match));
        });
    }

    /// <summary>
    /// 时间推进：玩法超时判定、阶段超时、断线窗口超时与终态释放（S5.1 / S5.6 / VC-5.7 / VC-5.11 / VC-5.12）。
    /// </summary>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>Tick 结果。</returns>
    public async Task<OnlineMatchTickResult> TickAsync(long nowUnixMilliseconds, CancellationToken cancellationToken = default)
    {
        var result = new OnlineMatchTickResult
        {
            MatchId = _match.MatchId,
        };

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_match.State == OnlineMatchState.Closed)
            {
                result.Closable = true;
                return result;
            }

            var working = _match.Copy();
            var changed = AdvanceGameplay(working, result, nowUnixMilliseconds);

            if (ExpireDisconnectedMembers(working, nowUnixMilliseconds))
            {
                changed = true;
                result.ReconnectWindowExpired = true;

                if (working.CountActiveMembers() == 0 && !OnlineMatchStateMachine.IsEnded(working.State))
                {
                    Transition(working, OnlineMatchState.Cancelled, nowUnixMilliseconds);
                }
            }

            if (IsStageTimedOut(working, nowUnixMilliseconds))
            {
                Transition(working, OnlineMatchState.Timeout, nowUnixMilliseconds);
                changed = true;
                result.TimedOut = true;
            }

            if (IsRetentionElapsed(working, nowUnixMilliseconds))
            {
                Transition(working, OnlineMatchState.Closed, nowUnixMilliseconds);
                changed = true;
            }

            if (changed)
            {
                var previousState = _match.State;
                var persisted = await _store.UpdateAsync(working, cancellationToken).ConfigureAwait(false);
                if (persisted == null)
                {
                    return result;
                }

                _match = persisted;
                result.Changed = true;
                if (previousState != persisted.State)
                {
                    await PublishStateChangedAsync(previousState, persisted.State, nowUnixMilliseconds, cancellationToken).ConfigureAwait(false);
                }
            }

            result.Closable = _match.State == OnlineMatchState.Closed;
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 应用一次已通过前置校验的输入（<see cref="SubmitInputAsync"/> 的门内委托体）。
    /// <para>处理顺序固定：玩法裁决 → 状态替换 → 序号推进 → 产出事件回放 → InputAccepted → 完成进结算；被拒绝的输入不改写状态、不推进序号（VC-5.3 / VC-5.4）。</para>
    /// </summary>
    /// <param name="match">门内候选副本。</param>
    /// <param name="actorScope">请求作用域。</param>
    /// <param name="input">输入意图（已判非空）。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒）。</param>
    /// <returns>输入确认；拒绝以确认形式返回。</returns>
    private OnlineResult<OnlineMatchInputAck> ApplyInput(OnlineMatch match, OnlineScope actorScope, OnlineMatchInput input, long nowUnixMilliseconds)
    {
        var gate = ValidateInputGate(match, actorScope, input, out var member);
        if (gate != null)
        {
            return gate;
        }

        var step = _game.ApplyInput(match, member, input);
        if (step == null || !step.Accepted)
        {
            return Ok(RejectAck(match, input, step == null ? OnlineMatchInputRejection.IllegalAction : step.Rejection));
        }

        if (step.GameState != null)
        {
            match.GameState = step.GameState;
        }

        member.LastClientSequence = input.ClientSequence;
        AppendEmittedEvents(match, step.ServerEvents, nowUnixMilliseconds);
        member.LastAckSequence = AppendServerEvent(match, "InputAccepted", null, nowUnixMilliseconds);
        var ack = new OnlineMatchInputAck
        {
            Accepted = true,
            ServerSequence = member.LastAckSequence,
            ClientSequence = input.ClientSequence,
            Rejection = OnlineMatchInputRejection.None,
            IsDuplicate = false,
            Message = "已接受",
        };

        if (_game.IsCompleted(match) && OnlineMatchStateMachine.TryTransition(match.State, OnlineMatchState.Settling))
        {
            Transition(match, OnlineMatchState.Settling, nowUnixMilliseconds);
        }

        return Ok(ack);
    }

    /// <summary>
    /// 执行输入的六重前置校验，求值顺序固定（类头红线②）：
    /// 成员身份 → Left/Kicked → 断线 → 对局结束 → 阶段不可玩 → 重复包 → 乱序包。
    /// </summary>
    /// <param name="match">门内候选副本。</param>
    /// <param name="actorScope">请求作用域。</param>
    /// <param name="input">输入意图（已判非空）。</param>
    /// <param name="member">命中的成员（校验通过时非空）。</param>
    /// <returns>任一校验不通过时返回对应失败/拒绝结果；全部通过返回 <c>null</c> 继续裁决。</returns>
    private OnlineResult<OnlineMatchInputAck> ValidateInputGate(OnlineMatch match, OnlineScope actorScope, OnlineMatchInput input, out OnlineMatchMember member)
    {
        member = match.FindMember(actorScope.PlayerId);
        if (member == null)
        {
            return Fail<OnlineMatchInputAck>(OnlineErrorCode.ScopeDenied, "提交者不是本对局成员");
        }

        if (member.State == OnlineMatchMemberState.Left || member.State == OnlineMatchMemberState.Kicked)
        {
            return Ok(RejectAck(match, input, OnlineMatchInputRejection.MemberLeft));
        }

        if (member.State == OnlineMatchMemberState.Disconnected)
        {
            return Ok(RejectAck(match, input, OnlineMatchInputRejection.MemberDisconnected));
        }

        if (OnlineMatchStateMachine.IsEnded(match.State))
        {
            return Ok(RejectAck(match, input, OnlineMatchInputRejection.MatchEnded));
        }

        if (!OnlineMatchStateMachine.IsPlayable(match.State))
        {
            return Ok(RejectAck(match, input, OnlineMatchInputRejection.StateNotPlayable));
        }

        if (input.ClientSequence <= member.LastClientSequence)
        {
            var duplicateAck = RejectAck(match, input, OnlineMatchInputRejection.Duplicate);
            duplicateAck.IsDuplicate = true;
            return Ok(duplicateAck);
        }

        if (input.ClientSequence > member.LastClientSequence + 1)
        {
            return Ok(RejectAck(match, input, OnlineMatchInputRejection.OutOfOrder));
        }

        return null;
    }

    /// <summary>
    /// 回放玩法裁决产出的服务器事件（玩法事件不带序号，序号由 Actor 统一分配后入日志）。
    /// </summary>
    /// <param name="match">门内候选副本。</param>
    /// <param name="events">玩法产出事件（可空容器）。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒）。</param>
    /// <returns>追加了至少一条事件返回 <c>true</c>；容器为空引用或无有效事件返回 <c>false</c>。</returns>
    private bool AppendEmittedEvents(OnlineMatch match, List<OnlineMatchServerEvent> events, long nowUnixMilliseconds)
    {
        if (events == null)
        {
            return false;
        }

        var appended = false;
        foreach (var emitted in events)
        {
            if (emitted != null)
            {
                AppendServerEvent(match, emitted.EventType, emitted.Payload, nowUnixMilliseconds);
                appended = true;
            }
        }

        return appended;
    }

    /// <summary>
    /// Tick 的玩法推进段：阶段可玩时执行 <see cref="IOnlineMatchGame.Advance"/>、回放产出事件并判定完成进结算。
    /// <para>注意：「完成进结算」分支不置 changed（沿袭既有控制流，推进只反映在 <c>EnteredSettling</c>）。</para>
    /// </summary>
    /// <param name="working">门内候选副本。</param>
    /// <param name="result">Tick 结果（写入 EnteredSettling）。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒）。</param>
    /// <returns>Advance 或事件回放发生改写返回 <c>true</c>。</returns>
    private bool AdvanceGameplay(OnlineMatch working, OnlineMatchTickResult result, long nowUnixMilliseconds)
    {
        if (!OnlineMatchStateMachine.IsPlayable(working.State))
        {
            return false;
        }

        var changed = false;
        var emittedEvents = new List<OnlineMatchServerEvent>();
        if (_game.Advance(working, nowUnixMilliseconds, emittedEvents))
        {
            changed = true;
        }

        if (AppendEmittedEvents(working, emittedEvents, nowUnixMilliseconds))
        {
            changed = true;
        }

        if (_game.IsCompleted(working) && OnlineMatchStateMachine.TryTransition(working.State, OnlineMatchState.Settling))
        {
            Transition(working, OnlineMatchState.Settling, nowUnixMilliseconds);
            result.EnteredSettling = true;
        }

        return changed;
    }

    /// <summary>
    /// 判定已结束对局是否滞留满释放窗口（终态 → Closed 的入口条件，VC-5.11）。
    /// </summary>
    /// <param name="working">门内候选副本。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒）。</param>
    /// <returns>已结束、未 Closed 且滞留超窗返回 <c>true</c>。</returns>
    private bool IsRetentionElapsed(OnlineMatch working, long nowUnixMilliseconds)
    {
        return OnlineMatchStateMachine.IsEnded(working.State) && working.State != OnlineMatchState.Closed
               && nowUnixMilliseconds >= working.EndedTime + (_options.EndedRetentionSeconds * 1000L);
    }

    /// <summary>
    /// 在门内执行一次「候选副本改写 → CAS 落库 → 替换权威副本」。
    /// </summary>
    /// <typeparam name="TResult">返回数据类型。</typeparam>
    /// <param name="scope">请求作用域。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="mutate">改写委托（返回失败即整体放弃，不落库）。</param>
    /// <returns>改写结果。</returns>
    private Task<OnlineResult<TResult>> MutateAsync<TResult>(OnlineScope scope, long nowUnixMilliseconds, CancellationToken cancellationToken, Func<OnlineMatch, OnlineScope, OnlineResult<TResult>> mutate)
    {
        return MutateCoreAsync(scope, nowUnixMilliseconds, cancellationToken, true, mutate);
    }

    /// <summary>
    /// 构造系统作用域（Tick、结算收口等运行时驱动操作使用，不带玩家标识）。
    /// </summary>
    /// <returns>对局自身隔离键构成的作用域。</returns>
    private OnlineScope SystemScope()
    {
        return new OnlineScope(_match.TenantId, _match.AppId, _match.ServerId);
    }

    /// <summary>
    /// 在门内执行一次「候选副本改写 → CAS 落库 → 替换权威副本」。
    /// </summary>
    /// <typeparam name="TResult">返回数据类型。</typeparam>
    /// <param name="scope">请求作用域。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="requirePlayer">是否要求作用域携带玩家标识（玩家操作 true，运行时驱动操作 false）。</param>
    /// <param name="mutate">改写委托（返回失败即整体放弃，不落库）。</param>
    /// <returns>改写结果。</returns>
    private async Task<OnlineResult<TResult>> MutateCoreAsync<TResult>(OnlineScope scope, long nowUnixMilliseconds, CancellationToken cancellationToken, bool requirePlayer, Func<OnlineMatch, OnlineScope, OnlineResult<TResult>> mutate)
    {
        if (scope == null)
        {
            return Fail<TResult>(OnlineErrorCode.ScopeMissing, "作用域不能为空");
        }

        if (requirePlayer && scope.PlayerId <= 0)
        {
            return Fail<TResult>(OnlineErrorCode.ScopeMissing, "作用域缺少玩家标识");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!ScopeMatches(scope, _match))
            {
                return Fail<TResult>(OnlineErrorCode.ScopeDenied, "作用域与对局不匹配");
            }

            var previousState = _match.State;
            var working = _match.Copy();
            var outcome = mutate(working, scope);
            if (!outcome.IsSuccess)
            {
                return outcome;
            }

            var persisted = await _store.UpdateAsync(working, cancellationToken).ConfigureAwait(false);
            if (persisted == null)
            {
                return Fail<TResult>(OnlineErrorCode.VersionConflict, "对局已被其它 Actor 推进，本次改写未生效");
            }

            _match = persisted;
            if (previousState != persisted.State)
            {
                await PublishStateChangedAsync(previousState, persisted.State, nowUnixMilliseconds, cancellationToken).ConfigureAwait(false);
            }

            return outcome;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 发布对局生命周期变更事件（事实在状态落库之后发布；发布失败不回收已落定的状态）。
    /// </summary>
    /// <param name="fromState">原状态。</param>
    /// <param name="toState">新状态。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>完成通知。</returns>
    private async Task PublishStateChangedAsync(OnlineMatchState fromState, OnlineMatchState toState, long nowUnixMilliseconds, CancellationToken cancellationToken)
    {
        if (_eventPublisher == null)
        {
            return;
        }

        await _eventPublisher.PublishAsync(OnlineMatchRuntimeEvents.CreateMatchStateChanged(_match, fromState, toState), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 校验作用域与对局的三元隔离键一致。
    /// </summary>
    /// <param name="scope">请求作用域。</param>
    /// <param name="match">对局。</param>
    /// <returns>一致返回 <c>true</c>。</returns>
    private static bool ScopeMatches(OnlineScope scope, OnlineMatch match)
    {
        return scope.TenantId == match.TenantId
               && scope.AppId == match.AppId
               && scope.ServerId == match.ServerId;
    }

    /// <summary>
    /// 执行一次合法迁移并重算阶段截止时刻。
    /// </summary>
    /// <param name="match">对局。</param>
    /// <param name="to">目标状态。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒）。</param>
    private void Transition(OnlineMatch match, OnlineMatchState to, long nowUnixMilliseconds)
    {
        if (!OnlineMatchStateMachine.TryTransition(match.State, to))
        {
            return;
        }

        match.State = to;
        match.StateChangedTime = nowUnixMilliseconds;

        if (OnlineMatchStateMachine.IsEnded(to))
        {
            match.EndedTime = nowUnixMilliseconds;
            match.DeadlineTime = nowUnixMilliseconds + (_options.EndedRetentionSeconds * 1000L);
            return;
        }

        if (to == OnlineMatchState.Running)
        {
            match.DeadlineTime = nowUnixMilliseconds + (_options.RunningTimeoutSeconds * 1000L);
            return;
        }

        if (to == OnlineMatchState.Settling)
        {
            match.DeadlineTime = 0;
            return;
        }

        match.DeadlineTime = match.CreatedTime + (_options.WaitingTimeoutSeconds * 1000L);
    }

    /// <summary>
    /// 判定当前阶段是否已超时（等待/运行阶段）。
    /// </summary>
    /// <param name="match">对局。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒）。</param>
    /// <returns>超时返回 <c>true</c>。</returns>
    private bool IsStageTimedOut(OnlineMatch match, long nowUnixMilliseconds)
    {
        if (match.DeadlineTime <= 0 || OnlineMatchStateMachine.IsEnded(match.State))
        {
            return false;
        }

        var timedStage = match.State == OnlineMatchState.Created || match.State == OnlineMatchState.Waiting
                         || match.State == OnlineMatchState.Ready || match.State == OnlineMatchState.Running;
        return timedStage && nowUnixMilliseconds >= match.DeadlineTime;
    }

    /// <summary>
    /// 清理超过重连窗口的断线成员（VC-5.7：不出现永久 Reconnecting）。
    /// </summary>
    /// <param name="match">对局。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒）。</param>
    /// <returns>发生清理返回 <c>true</c>。</returns>
    private bool ExpireDisconnectedMembers(OnlineMatch match, long nowUnixMilliseconds)
    {
        var expired = false;
        foreach (var member in match.Members)
        {
            if (member == null || member.State != OnlineMatchMemberState.Disconnected)
            {
                continue;
            }

            if (member.ReconnectDeadlineTime > 0 && nowUnixMilliseconds >= member.ReconnectDeadlineTime)
            {
                member.State = OnlineMatchMemberState.Left;
                member.StateChangedTime = nowUnixMilliseconds;
                member.ReconnectToken = null;
                AppendServerEvent(match, "MemberReconnectExpired", null, nowUnixMilliseconds);
                expired = true;
            }
        }

        return expired;
    }

    /// <summary>
    /// 判定是否全员已准备且人数达标。
    /// </summary>
    /// <param name="match">对局。</param>
    /// <returns>可开始返回 <c>true</c>。</returns>
    private bool AreAllMembersReady(OnlineMatch match)
    {
        var active = match.CountActiveMembers();
        if (active < _game.MinPlayers)
        {
            return false;
        }

        foreach (var member in match.Members)
        {
            if (member == null || member.State == OnlineMatchMemberState.Left || member.State == OnlineMatchMemberState.Kicked)
            {
                continue;
            }

            if (member.State != OnlineMatchMemberState.Ready)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 取房主（最早加入且未退出的成员；无则返回 0）。
    /// </summary>
    /// <param name="match">对局。</param>
    /// <returns>房主玩家标识。</returns>
    private static long FindHostPlayerId(OnlineMatch match)
    {
        var host = 0L;
        var earliest = long.MaxValue;
        foreach (var member in match.Members)
        {
            if (member == null || member.State == OnlineMatchMemberState.Left || member.State == OnlineMatchMemberState.Kicked)
            {
                continue;
            }

            if (member.JoinedTime < earliest)
            {
                earliest = member.JoinedTime;
                host = member.PlayerId;
            }
        }

        return host;
    }

    /// <summary>
    /// 追加一条服务器事件并推进权威序号（序号只由服务端分配）。
    /// </summary>
    /// <param name="match">对局。</param>
    /// <param name="eventType">事件类型名。</param>
    /// <param name="payload">玩法私有载荷（可空）。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒）。</param>
    /// <returns>本次事件的服务器序号。</returns>
    private long AppendServerEvent(OnlineMatch match, string eventType, byte[] payload, long nowUnixMilliseconds)
    {
        match.ServerSequence++;
        match.Events.Add(new OnlineMatchServerEvent
        {
            Sequence = match.ServerSequence,
            EventType = eventType,
            Payload = payload,
            OccurredTime = nowUnixMilliseconds,
        });

        var overflow = match.Events.Count - _options.MaxServerEventsPerMatch;
        for (var index = 0; index < overflow; index++)
        {
            match.Events.RemoveAt(0);
        }

        return match.ServerSequence;
    }

    /// <summary>
    /// 构造拒绝确认（不推进序号、不写事件日志）。
    /// </summary>
    /// <param name="match">对局。</param>
    /// <param name="input">被拒输入。</param>
    /// <param name="rejection">拒绝原因。</param>
    /// <returns>输入确认。</returns>
    private static OnlineMatchInputAck RejectAck(OnlineMatch match, OnlineMatchInput input, OnlineMatchInputRejection rejection)
    {
        return new OnlineMatchInputAck
        {
            Accepted = false,
            ServerSequence = match.ServerSequence,
            ClientSequence = input.ClientSequence,
            Rejection = rejection,
            IsDuplicate = false,
            Message = rejection.ToString(),
        };
    }

    /// <summary>
    /// 构造对局快照。
    /// </summary>
    /// <param name="match">对局。</param>
    /// <returns>快照副本。</returns>
    private static OnlineMatchSnapshot BuildSnapshot(OnlineMatch match)
    {
        var members = new List<OnlineMatchMember>();
        foreach (var member in match.Members)
        {
            if (member != null)
            {
                members.Add(member.Copy());
            }
        }

        return new OnlineMatchSnapshot
        {
            MatchId = match.MatchId,
            State = match.State,
            ServerSequence = match.ServerSequence,
            Mode = match.Mode,
            Region = match.Region,
            CreatedTime = match.CreatedTime,
            UpdatedTime = match.StateChangedTime,
            Members = members,
            GameState = match.GameState == null ? null : (byte[])match.GameState.Clone(),
        };
    }

    /// <summary>
    /// 构造失败结果。
    /// </summary>
    /// <typeparam name="TResult">返回数据类型。</typeparam>
    /// <param name="code">错误码。</param>
    /// <param name="message">错误说明。</param>
    /// <returns>失败结果。</returns>
    private static OnlineResult<TResult> Fail<TResult>(OnlineErrorCode code, string message)
    {
        return OnlineResult<TResult>.Fail(code, message);
    }

    /// <summary>
    /// 构造成功结果。
    /// </summary>
    /// <typeparam name="TResult">返回数据类型。</typeparam>
    /// <param name="data">返回数据。</param>
    /// <returns>成功结果。</returns>
    private static OnlineResult<TResult> Ok<TResult>(TResult data)
    {
        return OnlineResult<TResult>.Ok(data);
    }
}
