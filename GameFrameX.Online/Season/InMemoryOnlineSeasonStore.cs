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

namespace GameFrameX.Online.Season;

/// <summary>
/// 赛季内存存储（单进程默认实现；生产持久化 / 跨实例协调归 Server 仓运行时装配，X4）。
/// <para>
/// 维护约束（红线）：赛季定义与快照分别建表、互不写入——快照一经落档不会被状态推进覆盖
/// （重置后历史仍可回溯，VC-7.5）；出入参防御性拷贝，存储内对象与外界无别名。
/// </para>
/// </summary>
public sealed class InMemoryOnlineSeasonStore : IOnlineSeasonStore
{
    /// <summary>全局锁（赛季定义与快照共用，保证出入参一致性）。</summary>
    private readonly object _syncRoot = new object();

    /// <summary>赛季定义表：键 = (TenantId, AppId, SeasonId)。</summary>
    private readonly Dictionary<string, OnlineSeason> _seasons = new Dictionary<string, OnlineSeason>();

    /// <summary>历史快照表：键 = (TenantId, AppId, SeasonId)。</summary>
    private readonly Dictionary<string, OnlineSeasonSnapshot> _snapshots = new Dictionary<string, OnlineSeasonSnapshot>();

    /// <summary>
    /// 初始化 <see cref="InMemoryOnlineSeasonStore"/>。
    /// </summary>
    public InMemoryOnlineSeasonStore()
    {
    }

    /// <summary>
    /// 深拷贝赛季后写入内存赛季表：同作用域同标识已存在时拒绝并返回 null、不覆盖；否则落档并返回创建后的副本。
    /// </summary>
    /// <remarks>
    /// Stores a defensive copy of the season into the in-memory season table: rejects with null and does not overwrite when the same season id already exists within the scope; otherwise archives it and returns a copy of the created season.
    /// </remarks>
    /// <param name="season">赛季定义（作用域取其 TenantId / AppId）/ The season definition (scope taken from its TenantId / AppId)</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>创建后的赛季副本；已存在返回 null / A copy of the created season, or null when it already exists</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="season"/> 为 null 时抛出 / Thrown when <paramref name="season"/> is null</exception>
    public Task<OnlineSeason> CreateAsync(OnlineSeason season, CancellationToken cancellationToken = default)
    {
        if (season == null)
        {
            throw new ArgumentNullException(nameof(season));
        }

        lock (_syncRoot)
        {
            var key = BuildKey(season.TenantId, season.AppId, season.SeasonId);
            if (_seasons.ContainsKey(key))
            {
                return Task.FromResult<OnlineSeason>(null);
            }

            _seasons[key] = season.Copy();
            return Task.FromResult(season.Copy());
        }
    }

    /// <summary>
    /// 按作用域与赛季标识从内存赛季表读取赛季定义副本。
    /// </summary>
    /// <remarks>
    /// Reads a copy of the season definition from the in-memory season table by scope and season id.
    /// </remarks>
    /// <param name="tenantId">租户标识 / Tenant id</param>
    /// <param name="appId">App 标识 / App id</param>
    /// <param name="seasonId">赛季标识 / Season id</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>赛季副本；不存在或跨作用域返回 null / A copy of the season, or null when not found or out of scope</returns>
    public Task<OnlineSeason> FindAsync(long tenantId, long appId, string seasonId, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            return Task.FromResult(_seasons.TryGetValue(BuildKey(tenantId, appId, seasonId), out var stored) ? stored.Copy() : null);
        }
    }

    /// <summary>
    /// 深拷贝后按作用域与赛季标识覆盖保存赛季定义：赛季必须已经 CreateAsync 建立，不存在时抛出 <see cref="InvalidOperationException"/>，不会隐式创建。
    /// </summary>
    /// <remarks>
    /// Overwrites the season definition with a defensive copy, keyed by scope and season id: the season must already exist via CreateAsync; throws <see cref="InvalidOperationException"/> when it does not, and never creates it implicitly.
    /// </remarks>
    /// <param name="season">赛季定义（含推进后的状态与时间戳）/ The season definition (with the advanced state and timestamps)</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>完成通知 / Completion notification</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="season"/> 为 null 时抛出 / Thrown when <paramref name="season"/> is null</exception>
    /// <exception cref="InvalidOperationException">当赛季不存在时抛出 / Thrown when the season does not exist</exception>
    public Task SaveAsync(OnlineSeason season, CancellationToken cancellationToken = default)
    {
        if (season == null)
        {
            throw new ArgumentNullException(nameof(season));
        }

        lock (_syncRoot)
        {
            var key = BuildKey(season.TenantId, season.AppId, season.SeasonId);
            if (!_seasons.ContainsKey(key))
            {
                throw new InvalidOperationException("赛季不存在，保存前必须先经 CreateAsync 建立");
            }

            _seasons[key] = season.Copy();
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// 深拷贝后按作用域与赛季标识覆盖写入内存快照表（快照一经落档不受后续赛季状态推进影响）。
    /// </summary>
    /// <remarks>
    /// Overwrites the in-memory snapshot table with a defensive copy, keyed by scope and season id (an archived snapshot is never affected by later season state advances).
    /// </remarks>
    /// <param name="snapshot">历史快照 / The season snapshot</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>完成通知 / Completion notification</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="snapshot"/> 为 null 时抛出 / Thrown when <paramref name="snapshot"/> is null</exception>
    public Task SaveSnapshotAsync(OnlineSeasonSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        if (snapshot == null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        lock (_syncRoot)
        {
            _snapshots[BuildKey(snapshot.TenantId, snapshot.AppId, snapshot.SeasonId)] = snapshot.Copy();
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// 按作用域与赛季标识从内存快照表读取历史快照副本。
    /// </summary>
    /// <remarks>
    /// Reads a copy of the season snapshot from the in-memory snapshot table by scope and season id.
    /// </remarks>
    /// <param name="tenantId">租户标识 / Tenant id</param>
    /// <param name="appId">App 标识 / App id</param>
    /// <param name="seasonId">赛季标识 / Season id</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>快照副本；不存在或跨作用域返回 null / A copy of the snapshot, or null when not found or out of scope</returns>
    public Task<OnlineSeasonSnapshot> FindSnapshotAsync(long tenantId, long appId, string seasonId, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            return Task.FromResult(_snapshots.TryGetValue(BuildKey(tenantId, appId, seasonId), out var stored) ? stored.Copy() : null);
        }
    }

    /// <summary>
    /// 构造存储键（作用域 + 赛季标识；跨作用域访问因键不同而自然隔离）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="seasonId">赛季标识。</param>
    /// <returns>存储键。</returns>
    private static string BuildKey(long tenantId, long appId, string seasonId)
    {
        return tenantId.ToString(CultureInfo.InvariantCulture) + ":" + appId.ToString(CultureInfo.InvariantCulture) + ":" + (seasonId ?? string.Empty);
    }
}
