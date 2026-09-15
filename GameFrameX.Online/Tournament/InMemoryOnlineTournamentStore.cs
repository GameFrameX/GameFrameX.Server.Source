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

    /// <inheritdoc />
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

    /// <inheritdoc />
    public Task<OnlineTournament> FindAsync(long tenantId, long appId, string tournamentId, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            return Task.FromResult(_tournaments.TryGetValue(BuildKey(tenantId, appId, tournamentId), out var stored) ? stored.Copy() : null);
        }
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
    public Task<OnlineTournamentRegistration> FindRegistrationAsync(long tenantId, long appId, string tournamentId, long playerId, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            return Task.FromResult(_registrations.TryGetValue(BuildRegistrationKey(tenantId, appId, tournamentId, playerId), out var stored) ? stored.Copy() : null);
        }
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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
