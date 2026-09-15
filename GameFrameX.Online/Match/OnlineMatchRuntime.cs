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
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Online.Contracts;
using GameFrameX.Online.Events;
using GameFrameX.Online.Matchmaking;

namespace GameFrameX.Online.Match;

/// <summary>
/// Match Runtime（vault:C6 S5.1「Match Actor 所有权、Tick/Timer 与清理策略」的宿主）。
/// <para>
/// 维护约束（红线）：运行时不持有任何对局**状态**——它只是 Actor 的登记处与调度器，
/// 状态一律由 Actor 独占并通过存储 CAS 落库。因此运行时可以安全地并发 Tick，
/// 且单个 Actor 的失败不会让其他对局状态不一致。
/// </para>
/// <para>
/// 清理策略：<see cref="OnlineMatchState.Closed"/> 是可被释放的唯一终态，
/// <see cref="TickAllAsync"/> 发现终态即从登记处摘除并删除落库记录——
/// 这是 VC-5.11「僵尸 Match 数量 = 0」的实现路径。
/// </para>
/// </summary>
public sealed class OnlineMatchRuntime
{
    /// <summary>登记处同步锁（仅保护字典本身，不保护对局状态）。</summary>
    private readonly object _syncRoot = new object();

    /// <summary>对局存储。</summary>
    private readonly IOnlineMatchActorStore _store;

    /// <summary>事件发布器（可空）。</summary>
    private readonly IOnlineEventPublisher _eventPublisher;

    /// <summary>运行参数。</summary>
    private readonly OnlineMatchRuntimeOptions _options;

    /// <summary>玩法注册表：键 = 模式标识。</summary>
    private readonly Dictionary<int, IOnlineMatchGame> _games = new Dictionary<int, IOnlineMatchGame>();

    /// <summary>Actor 登记表：键 = (TenantId, AppId, MatchId)。</summary>
    private readonly Dictionary<string, OnlineMatchActor> _actors = new Dictionary<string, OnlineMatchActor>();

    /// <summary>已释放 Actor 计数（可观测性：VC-5.11 对账用）。</summary>
    private int _releasedActorCount;

    /// <summary>
    /// 初始化 Match Runtime。
    /// </summary>
    /// <param name="store">对局存储。</param>
    /// <param name="eventPublisher">事件发布器（可空）。</param>
    /// <param name="options">运行参数（可空，取默认值）。</param>
    public OnlineMatchRuntime(IOnlineMatchActorStore store, IOnlineEventPublisher eventPublisher, OnlineMatchRuntimeOptions options = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _eventPublisher = eventPublisher;
        _options = options == null ? new OnlineMatchRuntimeOptions() : options;
    }

    /// <summary>
    /// 获取当前在册 Actor 数。
    /// </summary>
    public int ActiveActorCount
    {
        get
        {
            lock (_syncRoot)
            {
                return _actors.Count;
            }
        }
    }

    /// <summary>
    /// 获取累计已释放 Actor 数（VC-5.11 对账依据）。
    /// </summary>
    public int ReleasedActorCount
    {
        get
        {
            lock (_syncRoot)
            {
                return _releasedActorCount;
            }
        }
    }

    /// <summary>
    /// 注册玩法实现（每个模式至多一个；重复注册以后者为准）。
    /// </summary>
    /// <param name="game">玩法实现。</param>
    public void RegisterGame(IOnlineMatchGame game)
    {
        if (game == null)
        {
            throw new ArgumentNullException(nameof(game));
        }

        lock (_syncRoot)
        {
            _games[game.Mode] = game;
        }
    }

    /// <summary>
    /// 由匹配分配创建对局（vault:C5 assignment → 阶段 5 Match）。
    /// <para>成员表按 assignment 的玩家集合预置（分配即成员已定），对局进入等待阶段。</para>
    /// </summary>
    /// <param name="assignment">上游分配（不可变事实）。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>创建后的快照；失败返回对应错误码。</returns>
    public async Task<OnlineResult<OnlineMatchSnapshot>> CreateFromAssignmentAsync(OnlineMatchAssignment assignment, long nowUnixMilliseconds, CancellationToken cancellationToken = default)
    {
        if (assignment == null)
        {
            return OnlineResult<OnlineMatchSnapshot>.Fail(OnlineErrorCode.ParameterInvalid, "分配不能为空");
        }

        if (string.IsNullOrEmpty(assignment.MatchId))
        {
            return OnlineResult<OnlineMatchSnapshot>.Fail(OnlineErrorCode.ParameterInvalid, "分配缺少对局标识");
        }

        IOnlineMatchGame game;
        lock (_syncRoot)
        {
            if (!_games.TryGetValue(assignment.Mode, out game))
            {
                return OnlineResult<OnlineMatchSnapshot>.Fail(OnlineErrorCode.ParameterInvalid, "未注册该模式的玩法实现");
            }
        }

        var match = new OnlineMatch
        {
            TenantId = assignment.TenantId,
            AppId = assignment.AppId,
            ServerId = assignment.ServerId,
            MatchId = assignment.MatchId,
            AssignmentId = assignment.AssignmentId ?? string.Empty,
            Mode = assignment.Mode,
            Region = assignment.Region,
            RuleSnapshot = assignment.RuleSnapshot == null ? null : assignment.RuleSnapshot.Copy(),
            State = OnlineMatchState.Created,
            Members = new List<OnlineMatchMember>(),
            Events = new List<OnlineMatchServerEvent>(),
            CreatedTime = nowUnixMilliseconds,
            StateChangedTime = nowUnixMilliseconds,
            DeadlineTime = nowUnixMilliseconds + (_options.WaitingTimeoutSeconds * 1000L),
            Version = 0,
        };

        if (assignment.PlayerIds != null)
        {
            foreach (var playerId in assignment.PlayerIds)
            {
                match.Members.Add(new OnlineMatchMember
                {
                    PlayerId = playerId,
                    State = OnlineMatchMemberState.Joined,
                    JoinedTime = nowUnixMilliseconds,
                    StateChangedTime = nowUnixMilliseconds,
                });
            }
        }

        if (match.Members.Count > 0)
        {
            match.State = OnlineMatchState.Waiting;
        }

        var persisted = await _store.CreateAsync(match, cancellationToken).ConfigureAwait(false);
        if (persisted == null)
        {
            return OnlineResult<OnlineMatchSnapshot>.Fail(OnlineErrorCode.VersionConflict, "对局已存在");
        }

        var actor = new OnlineMatchActor(persisted, game, _store, _eventPublisher, _options);
        lock (_syncRoot)
        {
            _actors[BuildKey(persisted.TenantId, persisted.AppId, persisted.MatchId)] = actor;
        }

        return OnlineResult<OnlineMatchSnapshot>.Ok(actor.GetSnapshot());
    }

    /// <summary>
    /// 取得（必要时装载）指定对局的 Actor。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="matchId">对局标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>对局 Actor；不存在或未注册玩法返回 null。</returns>
    public async Task<OnlineMatchActor> ResolveActorAsync(long tenantId, long appId, string matchId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(matchId))
        {
            return null;
        }

        var key = BuildKey(tenantId, appId, matchId);
        lock (_syncRoot)
        {
            if (_actors.TryGetValue(key, out var cached))
            {
                return cached;
            }
        }

        var match = await _store.FindAsync(tenantId, appId, matchId, cancellationToken).ConfigureAwait(false);
        if (match == null)
        {
            return null;
        }

        IOnlineMatchGame game;
        lock (_syncRoot)
        {
            if (!_games.TryGetValue(match.Mode, out game))
            {
                return null;
            }
        }

        var actor = new OnlineMatchActor(match, game, _store, _eventPublisher, _options);
        lock (_syncRoot)
        {
            _actors[key] = actor;
        }

        return actor;
    }

    /// <summary>
    /// 推进全部在册对局，并释放已进入终态的 Actor（VC-5.7 / VC-5.11 / VC-5.12）。
    /// </summary>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>各对局的 Tick 结果。</returns>
    public async Task<IReadOnlyList<OnlineMatchTickResult>> TickAllAsync(long nowUnixMilliseconds, CancellationToken cancellationToken = default)
    {
        List<OnlineMatchActor> snapshot;
        lock (_syncRoot)
        {
            snapshot = new List<OnlineMatchActor>(_actors.Values);
        }

        var results = new List<OnlineMatchTickResult>();
        foreach (var actor in snapshot)
        {
            var result = await actor.TickAsync(nowUnixMilliseconds, cancellationToken).ConfigureAwait(false);
            results.Add(result);

            if (!result.Closable)
            {
                continue;
            }

            var key = BuildKey(actor.TenantId, actor.AppId, actor.MatchId);
            lock (_syncRoot)
            {
                if (_actors.TryGetValue(key, out var current) && ReferenceEquals(current, actor))
                {
                    _actors.Remove(key);
                    _releasedActorCount++;
                }
            }

            await _store.DeleteAsync(actor.TenantId, actor.AppId, actor.MatchId, cancellationToken).ConfigureAwait(false);
        }

        return results;
    }

    /// <summary>
    /// 构造作用域隔离键。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="matchId">对局标识。</param>
    /// <returns>复合键。</returns>
    private static string BuildKey(long tenantId, long appId, string matchId)
    {
        return string.Concat(
            tenantId.ToString(CultureInfo.InvariantCulture),
            ":",
            appId.ToString(CultureInfo.InvariantCulture),
            ":",
            matchId ?? string.Empty);
    }
}
