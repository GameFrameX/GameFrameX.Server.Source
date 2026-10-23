// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and other rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the PRC and applicable international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with relevant laws, regulations and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file at the source code root for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切纠纷与责任，
//   Any disputes or liabilities arising from secondary development based on this project
//   本组织不承担任何责任，开发者应自行承担。
//   The organization and contributors shall not be responsible; developers shall bear the responsibility themselves.
// ==========================================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Scope;
using GameFrameX.Capability.Online.Matchmaking;

namespace GameFrameX.Capability.Online.Match;

/// <summary>
/// 对局样例桥服务（RPS 垂直切片）：<see cref="IOnlineMatchGameBridge"/> 的对局域实现——
/// 房间制协议入口（房间标识 + 局号 + 房间成员表）到平台对局运行时的 create-or-reuse 桥接。
/// <para>
/// 维护约束（红线）：① 对局标识确定性派生（<c>rps-s{serverId}-r{roomId}-g{round}</c>）——同房间同局号
/// 重复开局幂等复用既有对局，重启 / 重开 = 新局号新对局，旧局终态不复活；② 请求者身份一律取
/// 作用域玩家位（会话主体），请求体字段不可影响；③ 出拳保密——视图只经
/// <see cref="OnlineRockPaperScissorsGame.BuildBridgeView"/> 保密投影产出，本服务不拼装玩法字段；
/// ④ Ack 拒绝一律映射为段化错误码返回，不抛未分类异常。
/// </para>
/// <para>
/// 开局编排：create-or-reuse → 终态拒绝 → 成员表核对 →（未开局时）逐活跃成员就绪 + 房主启动 → 视图。
/// </para>
/// </summary>
/// <remarks>
/// The sample match bridge service (the RPS vertical slice): the match-domain implementation of
/// <see cref="IOnlineMatchGameBridge"/> — a create-or-reuse bridging from the room-shaped protocol entries
/// (room id + round + room member list) into the platform match runtime. The match id is derived
/// deterministically so repeated starts of the same room/round reuse the existing match idempotently,
/// while a restart uses a new round and therefore a new match (ended matches never revive); the requester
/// identity always comes from the scope player position (the session subject); gesture secrecy is enforced
/// by routing every view through the game plugin's secrecy projection; and input ack rejections are mapped
/// to segmented error codes instead of uncategorized exceptions.
/// </remarks>
public sealed class OnlineMatchGameBridgeService : IOnlineMatchGameBridge
{
    /// <summary>对局运行时（Actor 唯一属主）。</summary>
    private readonly OnlineMatchRuntime _runtime;

    /// <summary>样例玩法（视图保密投影属主）。</summary>
    private readonly OnlineRockPaperScissorsGame _game;

    /// <summary>
    /// 初始化对局桥服务。
    /// </summary>
    /// <param name="runtime">对局运行时。</param>
    /// <param name="game">样例玩法。</param>
    public OnlineMatchGameBridgeService(OnlineMatchRuntime runtime, OnlineRockPaperScissorsGame game)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _game = game ?? throw new ArgumentNullException(nameof(game));
    }

    /// <inheritdoc />
    public async Task<OnlineResult<OnlineMatchBridgeGameView>> StartRoomGameAsync(OnlineScope scope, long roomId, int round, IReadOnlyList<long> playerIds, CancellationToken cancellationToken = default)
{
        var invalidRequest = ValidateStartRequest(scope, roomId, round, playerIds);
        if (invalidRequest != null)
        {
            return invalidRequest;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var matchId = DeriveMatchId(scope.ServerId, roomId, round);
        var actorResult = await ResolveOrCreateActorAsync(scope, roomId, matchId, playerIds, now, cancellationToken).ConfigureAwait(false);
        if (!actorResult.IsSuccess)
        {
            return Fail(actorResult.Code, actorResult.Message);
        }

        var actor = actorResult.Data;
        var snapshot = actor.GetSnapshot();
        if (OnlineMatchStateMachine.IsEnded(snapshot.State))
        {
            return Fail(OnlineErrorCode.StateEnded, "对局已结束，须以新局号开局");
        }

        if (!MembersMatch(snapshot, playerIds))
        {
            return Fail(OnlineErrorCode.ParameterInvalid, "成员表与房间成员不一致");
        }

        if (snapshot.State == OnlineMatchState.Created || snapshot.State == OnlineMatchState.Waiting || snapshot.State == OnlineMatchState.Ready)
        {
            var prepared = await PrepareMembersAndStartAsync(actor, snapshot, scope, playerIds, now, cancellationToken).ConfigureAwait(false);
            if (!prepared.IsSuccess)
            {
                return Fail(prepared.Code, prepared.Message);
            }
        }

        return OnlineResult<OnlineMatchBridgeGameView>.Ok(BuildView(roomId, actor));
    }

    /// <inheritdoc />
    public async Task<OnlineResult<OnlineMatchBridgeGameView>> SubmitGestureAsync(OnlineScope scope, long roomId, int round, int gesture, CancellationToken cancellationToken = default)
    {
        if (scope == null || scope.PlayerId <= 0)
        {
            return Fail(OnlineErrorCode.ScopeMissing, "作用域缺失或缺少玩家标识");
        }

        if (gesture < (int)OnlineRockPaperScissorsMove.Rock || gesture > (int)OnlineRockPaperScissorsMove.Paper)
        {
            return Fail(OnlineErrorCode.ParameterInvalid, "非法出拳");
        }

        var matchId = DeriveMatchId(scope.ServerId, roomId, round);
        var actor = await _runtime.ResolveActorAsync(scope.TenantId, scope.AppId, matchId, cancellationToken).ConfigureAwait(false);
        if (actor == null)
        {
            return Fail(OnlineErrorCode.ResourceNotFound, "对局不存在");
        }

        // 客户端序号由服务端从快照派生（LastClientSequence + 1），请求体不可携带。
        var snapshot = actor.GetSnapshot();
        var clientSequence = 1L;
        foreach (var member in snapshot.Members)
        {
            if (member != null && member.PlayerId == scope.PlayerId)
            {
                clientSequence = member.LastClientSequence + 1;
                break;
            }
        }

        var input = new OnlineMatchInput
        {
            PlayerId = scope.PlayerId,
            ClientSequence = clientSequence,
            ActionId = gesture,
            ClientTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };

        var submitted = await actor.SubmitInputAsync(scope, input, input.ClientTime, cancellationToken).ConfigureAwait(false);
        if (!submitted.IsSuccess)
        {
            return Fail(submitted.Code, submitted.Message);
        }

        var ack = submitted.Data;
        if (ack == null)
        {
            return Fail(OnlineErrorCode.InternalError, "提交未返回确认");
        }

        if (!ack.Accepted)
        {
            return MapRejection(ack);
        }

        return OnlineResult<OnlineMatchBridgeGameView>.Ok(BuildView(roomId, actor));
    }

    /// <inheritdoc />
    public async Task<OnlineResult<OnlineMatchBridgeGameView>> GetGameInfoAsync(OnlineScope scope, long roomId, int round, CancellationToken cancellationToken = default)
    {
        if (scope == null || scope.PlayerId <= 0)
        {
            return Fail(OnlineErrorCode.ScopeMissing, "作用域缺失或缺少玩家标识");
        }

        var matchId = DeriveMatchId(scope.ServerId, roomId, round);
        var actor = await _runtime.ResolveActorAsync(scope.TenantId, scope.AppId, matchId, cancellationToken).ConfigureAwait(false);
        if (actor == null)
        {
            return Fail(OnlineErrorCode.ResourceNotFound, "对局不存在");
        }

        var snapshot = actor.GetSnapshot();
        var memberFound = false;
        foreach (var member in snapshot.Members)
        {
            if (member != null && member.PlayerId == scope.PlayerId && member.State != OnlineMatchMemberState.Left && member.State != OnlineMatchMemberState.Kicked)
            {
                memberFound = true;
                break;
            }
        }

        if (!memberFound)
        {
            return Fail(OnlineErrorCode.ScopeDenied, "查询者不是本对局成员");
        }

        return OnlineResult<OnlineMatchBridgeGameView>.Ok(BuildView(roomId, actor));
    }

    /// <summary>
    /// 校验开局请求（求值顺序固定：作用域 → 房间参数 → 请求者成员身份）。
    /// </summary>
    /// <param name="scope">请求作用域。</param>
    /// <param name="roomId">房间标识。</param>
    /// <param name="round">局号。</param>
    /// <param name="playerIds">房间成员表。</param>
    /// <returns>校验失败返回失败结果；通过返回 <c>null</c>。</returns>
    private OnlineResult<OnlineMatchBridgeGameView> ValidateStartRequest(OnlineScope scope, long roomId, int round, IReadOnlyList<long> playerIds)
    {
        if (scope == null || scope.PlayerId <= 0)
        {
            return Fail(OnlineErrorCode.ScopeMissing, "作用域缺失或缺少玩家标识");
        }

        if (roomId <= 0 || round <= 0 || playerIds == null || playerIds.Count != _game.MinPlayers)
        {
            return Fail(OnlineErrorCode.ParameterInvalid, "房间标识、局号非法或玩家数不是两人");
        }

        if (!playerIds.Contains(scope.PlayerId))
        {
            return Fail(OnlineErrorCode.ScopeDenied, "开局请求者不在本局玩家集合中");
        }

        return null;
    }

    /// <summary>
    /// 解析或创建对局 Actor（create-or-reuse：并发同键创建经 store 唯一约束裁决后重解析一次）。
    /// </summary>
    /// <param name="scope">请求作用域。</param>
    /// <param name="roomId">房间标识。</param>
    /// <param name="matchId">对局标识。</param>
    /// <param name="playerIds">房间成员表。</param>
    /// <param name="now">当前毫秒时间戳。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>对局 Actor 结果（失败携带错误码与描述）。</returns>
    private async Task<OnlineResult<OnlineMatchActor>> ResolveOrCreateActorAsync(OnlineScope scope, long roomId, string matchId, IReadOnlyList<long> playerIds, long now, CancellationToken cancellationToken)
    {
        var actor = await _runtime.ResolveActorAsync(scope.TenantId, scope.AppId, matchId, cancellationToken).ConfigureAwait(false);
        if (actor == null)
        {
            var assignment = new OnlineMatchAssignment
            {
                TenantId = scope.TenantId,
                AppId = scope.AppId,
                ServerId = scope.ServerId,
                MatchId = matchId,
                AssignmentId = BuildAssignmentId(scope.ServerId, roomId),
                Mode = OnlineRockPaperScissorsGame.MatchMode,
                Region = 1,
                CreatedAtTime = now,
                TicketIds = new List<string>(),
                PlayerIds = new List<long>(playerIds),
            };

            var created = await _runtime.CreateFromAssignmentAsync(assignment, now, cancellationToken).ConfigureAwait(false);
            if (!created.IsSuccess)
            {
                // 并发同键创建：store 唯一约束裁决后重解析一次（幂等并发路径）。
                if (created.Code != OnlineErrorCode.VersionConflict)
                {
                    return OnlineResult<OnlineMatchActor>.Fail(created.Code, created.Message);
                }
            }

            actor = await _runtime.ResolveActorAsync(scope.TenantId, scope.AppId, matchId, cancellationToken).ConfigureAwait(false);
            if (actor == null)
            {
                return OnlineResult<OnlineMatchActor>.Fail(created.IsSuccess ? OnlineErrorCode.InternalError : created.Code, created.Message);
            }
        }

        return OnlineResult<OnlineMatchActor>.Ok(actor);
    }

    /// <summary>
    /// 未开局对局逐活跃成员就绪并由房主启动（活跃成员 = 非 null 且非 Left/Kicked；启动失败即失败返回）。
    /// </summary>
    /// <param name="actor">对局 Actor。</param>
    /// <param name="snapshot">对局快照。</param>
    /// <param name="scope">请求作用域。</param>
    /// <param name="playerIds">房间成员表。</param>
    /// <param name="now">当前毫秒时间戳。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>启动结果（成功携带对局 Actor，失败携带错误码与描述）。</returns>
    private static async Task<OnlineResult<OnlineMatchActor>> PrepareMembersAndStartAsync(OnlineMatchActor actor, OnlineMatchSnapshot snapshot, OnlineScope scope, IReadOnlyList<long> playerIds, long now, CancellationToken cancellationToken)
    {
        foreach (var member in snapshot.Members)
        {
            if (member == null || member.State == OnlineMatchMemberState.Left || member.State == OnlineMatchMemberState.Kicked)
            {
                continue;
            }

            await actor.SetReadyAsync(WithPlayer(scope, member.PlayerId), true, now, cancellationToken).ConfigureAwait(false);
        }

        var started = await actor.StartAsync(WithPlayer(scope, playerIds[0]), now, cancellationToken).ConfigureAwait(false);
        if (!started.IsSuccess)
        {
            return OnlineResult<OnlineMatchActor>.Fail(started.Code, started.Message);
        }

        return OnlineResult<OnlineMatchActor>.Ok(actor);
    }

    /// <summary>
    /// 组装对局视图（统一私有口径：快照 + 全量事件流 → 玩法保密投影）。
    /// </summary>
    /// <param name="roomId">房间标识。</param>
    /// <param name="actor">对局 Actor。</param>
    /// <returns>桥接视图。</returns>
    private OnlineMatchBridgeGameView BuildView(long roomId, OnlineMatchActor actor)
    {
        return _game.BuildBridgeView(roomId, actor.GetSnapshot(), actor.BuildDelta(0).Events);
    }

    /// <summary>
    /// 映射输入拒绝确认（玩法/门卫拒绝 → 段化错误码，不抛异常）。
    /// </summary>
    /// <param name="ack">拒绝确认。</param>
    /// <returns>失败结果。</returns>
    private static OnlineResult<OnlineMatchBridgeGameView> MapRejection(OnlineMatchInputAck ack)
    {
        switch (ack.Rejection)
        {
            case OnlineMatchInputRejection.Duplicate:
                return Fail(OnlineErrorCode.DuplicateRequest, "本局已提交过出拳");
            case OnlineMatchInputRejection.NotMember:
            case OnlineMatchInputRejection.MemberLeft:
                return Fail(OnlineErrorCode.ScopeDenied, "提交者不是本对局有效成员");
            case OnlineMatchInputRejection.MemberDisconnected:
            case OnlineMatchInputRejection.StateNotPlayable:
                return Fail(OnlineErrorCode.StateOperationForbidden, "当前状态不可提交出拳");
            case OnlineMatchInputRejection.MatchEnded:
                return Fail(OnlineErrorCode.StateEnded, "对局已结束");
            default:
                return Fail(OnlineErrorCode.ParameterInvalid, string.IsNullOrEmpty(ack.Message) ? "非法出拳意图" : ack.Message);
        }
    }

    /// <summary>
    /// 校验对局活跃成员集合与房间成员表一致（集合相等，忽略顺序）。
    /// </summary>
    /// <param name="snapshot">对局快照。</param>
    /// <param name="playerIds">房间成员表。</param>
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
    /// 确定性派生对局标识（同房间同局号恒同值；跨区服不撞）。
    /// </summary>
    /// <param name="serverId">区服标识。</param>
    /// <param name="roomId">房间标识。</param>
    /// <param name="round">局号。</param>
    /// <returns>对局标识。</returns>
    private static string DeriveMatchId(long serverId, long roomId, int round)
    {
        return string.Concat(
            "rps-s",
            serverId.ToString(CultureInfo.InvariantCulture),
            "-r",
            roomId.ToString(CultureInfo.InvariantCulture),
            "-g",
            round.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// 构造分配追溯标识（房间维度恒定，关联同一房间的全部对局）。
    /// </summary>
    /// <param name="serverId">区服标识。</param>
    /// <param name="roomId">房间标识。</param>
    /// <returns>分配标识。</returns>
    private static string BuildAssignmentId(long serverId, long roomId)
    {
        return string.Concat(
            "room-",
            serverId.ToString(CultureInfo.InvariantCulture),
            "-",
            roomId.ToString(CultureInfo.InvariantCulture));
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
    /// 构造失败结果。
    /// </summary>
    /// <param name="code">错误码。</param>
    /// <param name="message">失败描述。</param>
    /// <returns>失败结果。</returns>
    private static OnlineResult<OnlineMatchBridgeGameView> Fail(OnlineErrorCode code, string message)
    {
        return OnlineResult<OnlineMatchBridgeGameView>.Fail(code, message);
    }
}
