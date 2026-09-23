// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
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

namespace GameFrameX.Online.Tournament;

/// <summary>
/// 赛事内存存储（单进程默认实现；生产持久化 / 跨实例协调归 Server 仓运行时装配，X4）。
/// <para>
/// 维护约束（红线）：赛事定义、报名登记与冻结成绩分别建表、互不写入——成绩一经落档不会被状态推进覆盖
/// （结果查询长期可回溯）；报名登记的「判定重复 + 落档」在同一把锁内完成（并发报名不产生重复登记）；
/// 出入参防御性拷贝，存储内对象与外界无别名。
/// </para>
/// </summary>
public sealed class InMemoryOnlineTournamentStore : IOnlineTournamentStore
{
    /// <summary>全局锁（三张表共用，保证出入参一致性与报名的临界区语义）。</summary>
    private readonly object _syncRoot = new object();

    /// <summary>赛事定义表：键 = (TenantId, AppId, TournamentId)。</summary>
    private readonly Dictionary<string, OnlineTournament> _tournaments = new Dictionary<string, OnlineTournament>();

    /// <summary>报名登记表：键 = (TenantId, AppId, TournamentId, PlayerId)。</summary>
    private readonly Dictionary<string, OnlineTournamentRegistration> _registrations = new Dictionary<string, OnlineTournamentRegistration>();

    /// <summary>冻结成绩表：键 = (TenantId, AppId, TournamentId)。</summary>
    private readonly Dictionary<string, OnlineTournamentStandings> _standings = new Dictionary<string, OnlineTournamentStandings>();

    /// <summary>
    /// 初始化 <see cref="InMemoryOnlineTournamentStore"/>。
    /// </summary>
    public InMemoryOnlineTournamentStore()
    {
    }

    /// <summary>
    /// 创建赛事（内存实现：在全局锁内完成重复判定与落档，同作用域同标识已存在时返回 null 且不覆盖，出入参均为防御性副本）。
    /// </summary>
    /// <remarks>
    /// Creates a tournament (in-memory implementation: duplicate check and persistence complete inside the global lock; returns null without overwriting when the same identifier already exists in the same scope; inputs and outputs are defensive copies).
    /// </remarks>
    /// <param name="tournament">赛事定义（作用域取其 TenantId / AppId）/ The tournament definition (scope taken from its TenantId / AppId)</param>
    /// <param name="cancellationToken">取消令牌（内存实现忽略）/ The cancellation token (ignored by the in-memory implementation)</param>
    /// <returns>创建后的赛事副本；同作用域同标识已存在时为 null / A copy of the created tournament; null when the identifier already exists in the same scope</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="tournament"/> 为 null 时抛出 / Thrown when <paramref name="tournament"/> is null</exception>
    public Task<OnlineTournament> CreateAsync(OnlineTournament tournament, CancellationToken cancellationToken = default)
    {
        if (tournament == null)
        {
            throw new ArgumentNullException(nameof(tournament));
        }

        lock (_syncRoot)
        {
            var key = BuildKey(tournament.TenantId, tournament.AppId, tournament.TournamentId);
            if (_tournaments.ContainsKey(key))
            {
                return Task.FromResult<OnlineTournament>(null);
            }

            _tournaments[key] = tournament.Copy();
            return Task.FromResult(tournament.Copy());
        }
    }

    /// <summary>
    /// 按作用域查找赛事定义（内存实现：全局锁内按作用域 + 赛事标识查表，返回防御性副本；不存在返回 null）。
    /// </summary>
    /// <remarks>
    /// Finds a tournament definition by scope (in-memory implementation: looks the table up by scope plus tournament identifier inside the global lock and returns a defensive copy; null when absent).
    /// </remarks>
    /// <param name="tenantId">租户标识 / The tenant identifier</param>
    /// <param name="appId">App 标识 / The app identifier</param>
    /// <param name="tournamentId">赛事标识 / The tournament identifier</param>
    /// <param name="cancellationToken">取消令牌（内存实现忽略）/ The cancellation token (ignored by the in-memory implementation)</param>
    /// <returns>赛事副本；不存在时为 null / A copy of the tournament; null when absent</returns>
    public Task<OnlineTournament> FindAsync(long tenantId, long appId, string tournamentId, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            return Task.FromResult(_tournaments.TryGetValue(BuildKey(tenantId, appId, tournamentId), out var stored) ? stored.Copy() : null);
        }
    }

    /// <summary>
    /// 保存赛事定义（内存实现：全局锁内覆盖既有赛事的存储副本；赛事未经 CreateAsync 建立时抛出 <see cref="InvalidOperationException"/>）。
    /// </summary>
    /// <remarks>
    /// Saves a tournament definition (in-memory implementation: overwrites the stored copy of the existing tournament inside the global lock; throws <see cref="InvalidOperationException"/> when the tournament was not established via CreateAsync first).
    /// </remarks>
    /// <param name="tournament">赛事定义（含推进后的状态与时间戳）/ The tournament definition (with the advanced state and timestamps)</param>
    /// <param name="cancellationToken">取消令牌（内存实现忽略）/ The cancellation token (ignored by the in-memory implementation)</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="tournament"/> 为 null 时抛出 / Thrown when <paramref name="tournament"/> is null</exception>
    /// <exception cref="InvalidOperationException">当同作用域同标识的赛事不存在时抛出 / Thrown when the tournament with the same identifier in the same scope does not exist</exception>
    public Task SaveAsync(OnlineTournament tournament, CancellationToken cancellationToken = default)
    {
        if (tournament == null)
        {
            throw new ArgumentNullException(nameof(tournament));
        }

        lock (_syncRoot)
        {
            var key = BuildKey(tournament.TenantId, tournament.AppId, tournament.TournamentId);
            if (!_tournaments.ContainsKey(key))
            {
                throw new InvalidOperationException("赛事不存在，保存前必须先经 CreateAsync 建立");
            }

            _tournaments[key] = tournament.Copy();
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// 尝试登记报名（内存实现：判定重复与落档在同一全局锁临界区内完成，同键重复报名返回既有登记且 <see cref="OnlineTournamentRegistrationResult.IsNew"/> 为 false）。
    /// </summary>
    /// <remarks>
    /// Tries to register a player (in-memory implementation: duplicate check and persistence complete inside the same global-lock critical section; a repeated registration under the same key returns the existing registration with <see cref="OnlineTournamentRegistrationResult.IsNew"/> false).
    /// </remarks>
    /// <param name="registration">报名登记 / The registration</param>
    /// <param name="cancellationToken">取消令牌（内存实现忽略）/ The cancellation token (ignored by the in-memory implementation)</param>
    /// <returns>落档结果（<see cref="OnlineTournamentRegistrationResult.IsNew"/> 标出本次是否新登记，非新登记时携带既有登记副本）/ The persisted result (<see cref="OnlineTournamentRegistrationResult.IsNew"/> tells whether this is a new registration; carries a copy of the existing registration otherwise)</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="registration"/> 为 null 时抛出 / Thrown when <paramref name="registration"/> is null</exception>
    public Task<OnlineTournamentRegistrationResult> TryRegisterAsync(OnlineTournamentRegistration registration, CancellationToken cancellationToken = default)
    {
        if (registration == null)
        {
            throw new ArgumentNullException(nameof(registration));
        }

        lock (_syncRoot)
        {
            var key = BuildRegistrationKey(registration.TenantId, registration.AppId, registration.TournamentId, registration.PlayerId);
            if (_registrations.TryGetValue(key, out var existing))
            {
                return Task.FromResult(new OnlineTournamentRegistrationResult { IsNew = false, Registration = existing.Copy() });
            }

            _registrations[key] = registration.Copy();
            return Task.FromResult(new OnlineTournamentRegistrationResult { IsNew = true, Registration = registration.Copy() });
        }
    }

    /// <summary>
    /// 查找单个玩家的报名登记（内存实现：全局锁内按作用域 + 赛事标识 + 玩家标识查表，返回防御性副本；未报名返回 null）。
    /// </summary>
    /// <remarks>
    /// Finds a single player's registration (in-memory implementation: looks the table up by scope, tournament identifier and player identifier inside the global lock and returns a defensive copy; null when not registered).
    /// </remarks>
    /// <param name="tenantId">租户标识 / The tenant identifier</param>
    /// <param name="appId">App 标识 / The app identifier</param>
    /// <param name="tournamentId">赛事标识 / The tournament identifier</param>
    /// <param name="playerId">玩家标识 / The player identifier</param>
    /// <param name="cancellationToken">取消令牌（内存实现忽略）/ The cancellation token (ignored by the in-memory implementation)</param>
    /// <returns>登记副本；未报名时为 null / A copy of the registration; null when not registered</returns>
    public Task<OnlineTournamentRegistration> FindRegistrationAsync(long tenantId, long appId, string tournamentId, long playerId, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            return Task.FromResult(_registrations.TryGetValue(BuildRegistrationKey(tenantId, appId, tournamentId, playerId), out var stored) ? stored.Copy() : null);
        }
    }

    /// <summary>
    /// 列出赛事的全部报名登记（内存实现：全局锁内按赛事键前缀扫描登记表，逐条拷贝并按报名时刻升序排序）。
    /// </summary>
    /// <remarks>
    /// Lists all registrations of a tournament (in-memory implementation: scans the registration table by tournament-key prefix inside the global lock, copies each entry and sorts by registration time ascending).
    /// </remarks>
    /// <param name="tenantId">租户标识 / The tenant identifier</param>
    /// <param name="appId">App 标识 / The app identifier</param>
    /// <param name="tournamentId">赛事标识 / The tournament identifier</param>
    /// <param name="cancellationToken">取消令牌（内存实现忽略）/ The cancellation token (ignored by the in-memory implementation)</param>
    /// <returns>按报名时刻升序的登记副本列表；无报名时为空列表 / The list of registration copies sorted by registration time ascending; an empty list when none</returns>
    public Task<List<OnlineTournamentRegistration>> ListRegistrationsAsync(long tenantId, long appId, string tournamentId, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            var prefix = BuildKey(tenantId, appId, tournamentId) + ":";
            var registrations = new List<OnlineTournamentRegistration>();
            foreach (var pair in _registrations)
            {
                if (pair.Key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    registrations.Add(pair.Value.Copy());
                }
            }

            registrations.Sort((left, right) => left.RegisteredTime.CompareTo(right.RegisteredTime));
            return Task.FromResult(registrations);
        }
    }

    /// <summary>
    /// 保存冻结成绩（内存实现：全局锁内按作用域 + 赛事标识覆盖写入成绩表的防御性副本，独立于赛事定义表）。
    /// </summary>
    /// <remarks>
    /// Saves frozen standings (in-memory implementation: overwrites the standings table with a defensive copy keyed by scope plus tournament identifier inside the global lock, independently of the tournament table).
    /// </remarks>
    /// <param name="standings">冻结成绩 / The frozen standings</param>
    /// <param name="cancellationToken">取消令牌（内存实现忽略）/ The cancellation token (ignored by the in-memory implementation)</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="standings"/> 为 null 时抛出 / Thrown when <paramref name="standings"/> is null</exception>
    public Task SaveStandingsAsync(OnlineTournamentStandings standings, CancellationToken cancellationToken = default)
    {
        if (standings == null)
        {
            throw new ArgumentNullException(nameof(standings));
        }

        lock (_syncRoot)
        {
            _standings[BuildKey(standings.TenantId, standings.AppId, standings.TournamentId)] = standings.Copy();
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// 按作用域查找冻结成绩（内存实现：全局锁内查冻结成绩表，返回防御性副本；不存在返回 null）。
    /// </summary>
    /// <remarks>
    /// Finds frozen standings by scope (in-memory implementation: looks up the frozen-standings table inside the global lock and returns a defensive copy; null when absent).
    /// </remarks>
    /// <param name="tenantId">租户标识 / The tenant identifier</param>
    /// <param name="appId">App 标识 / The app identifier</param>
    /// <param name="tournamentId">赛事标识 / The tournament identifier</param>
    /// <param name="cancellationToken">取消令牌（内存实现忽略）/ The cancellation token (ignored by the in-memory implementation)</param>
    /// <returns>成绩副本；不存在时为 null / A copy of the standings; null when absent</returns>
    public Task<OnlineTournamentStandings> FindStandingsAsync(long tenantId, long appId, string tournamentId, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            return Task.FromResult(_standings.TryGetValue(BuildKey(tenantId, appId, tournamentId), out var stored) ? stored.Copy() : null);
        }
    }

    /// <summary>
    /// 构造赛事级存储键（作用域 + 赛事标识；跨作用域访问因键不同而自然隔离）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="tournamentId">赛事标识。</param>
    /// <returns>存储键。</returns>
    private static string BuildKey(long tenantId, long appId, string tournamentId)
    {
        return tenantId.ToString(CultureInfo.InvariantCulture) + ":" + appId.ToString(CultureInfo.InvariantCulture) + ":" + (tournamentId ?? string.Empty);
    }

    /// <summary>
    /// 构造报名登记存储键（赛事键 + 玩家标识；同一玩家在同一赛事内只有一键）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="tournamentId">赛事标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <returns>存储键。</returns>
    private static string BuildRegistrationKey(long tenantId, long appId, string tournamentId, long playerId)
    {
        return BuildKey(tenantId, appId, tournamentId) + ":" + playerId.ToString(CultureInfo.InvariantCulture);
    }
}
