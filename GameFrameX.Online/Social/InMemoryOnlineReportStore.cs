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

namespace GameFrameX.Online.Social;

/// <summary>
/// 举报案件存储的单进程默认实现（持久化与多实例协调归 Server 仓运行时装配，X4）。
/// <para>
/// 维护约束：CAS 判据取「期望状态」而非版本号——举报案件的并发写者只有「玩家撤回」与「Admin 裁决」
/// 两方，状态本身就能唯一区分它们，多引一个版本号只是徒增一次比对。比对与写入必须在同一临界区完成。
/// </para>
/// <para>
/// 天花板（ponytail）：全局单锁 + 全表线性扫描（Admin 待办队列按状态过滤会随案件量线性变慢）；
/// 案件量上万后需换按状态分桶的二级索引。
/// </para>
/// </summary>
public sealed class InMemoryOnlineReportStore : IOnlineReportStore
{
    /// <summary>并发保护锁。</summary>
    private readonly object _syncRoot = new object();

    /// <summary>案件表（键 = 作用域 + 案件标识）。</summary>
    private readonly Dictionary<string, OnlineReportCase> _casesById = new Dictionary<string, OnlineReportCase>(StringComparer.Ordinal);

    /// <summary>
    /// 以案件标识为唯一键在内存表内「不存在则创建」：键已存在时返回既有案件副本且不写入，否则存入入参的防御性副本。
    /// </summary>
    /// <remarks>
    /// Creates the report case in the in-memory table if the key (scope + case id) is absent;
    /// returns a copy of the existing record without writing when the key already exists,
    /// otherwise stores a defensive copy of the input.
    /// </remarks>
    /// <param name="reportCase">待创建的案件 / The report case to create</param>
    /// <param name="cancellationToken">取消令牌（本实现同步完成，不消费该令牌）/ Cancellation token (unused; this implementation completes synchronously)</param>
    /// <returns>当前生效的案件副本（既有记录或刚落库的入参）/ Copy of the currently effective report case (existing record or just-stored input)</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="reportCase"/> 为 null 时抛出 / Thrown when <paramref name="reportCase"/> is null</exception>
    public Task<OnlineReportCase> SaveIfAbsentAsync(OnlineReportCase reportCase, CancellationToken cancellationToken = default)
    {
        if (reportCase == null)
        {
            throw new ArgumentNullException(nameof(reportCase));
        }

        var key = BuildKey(reportCase.TenantId, reportCase.AppId, reportCase.ReportId);
        lock (_syncRoot)
        {
            OnlineReportCase existing;
            if (_casesById.TryGetValue(key, out existing))
            {
                return Task.FromResult(existing.Copy());
            }

            // 存档存副本而非调用方实例（调用方复用该实例不会回写库内记录）。
            var stored = reportCase.Copy();
            _casesById[key] = stored;
            return Task.FromResult(stored.Copy());
        }
    }

    /// <summary>
    /// 按作用域与案件标识在内存表内查找案件。
    /// </summary>
    /// <remarks>
    /// Finds a report case in the in-memory table by scope and case id.
    /// </remarks>
    /// <param name="tenantId">租户标识 / Tenant id</param>
    /// <param name="appId">App 标识 / App id</param>
    /// <param name="reportId">案件标识 / Case id</param>
    /// <param name="cancellationToken">取消令牌（本实现同步完成，不消费该令牌）/ Cancellation token (unused; this implementation completes synchronously)</param>
    /// <returns>案件副本；不存在返回 null / Copy of the report case, or null if absent</returns>
    public Task<OnlineReportCase> FindAsync(long tenantId, long appId, string reportId, CancellationToken cancellationToken = default)
    {
        var key = BuildKey(tenantId, appId, reportId);
        lock (_syncRoot)
        {
            OnlineReportCase found;
            if (_casesById.TryGetValue(key, out found))
            {
                return Task.FromResult(found.Copy());
            }

            return Task.FromResult<OnlineReportCase>(null);
        }
    }

    /// <summary>
    /// 在同一临界区内以期望状态 CAS 改写案件：状态不匹配或记录不存在即整体失败且不留写入痕迹。
    /// </summary>
    /// <remarks>
    /// Rewrites the report case under one lock via expected-state CAS; a mismatching state or a
    /// missing record fails as a whole, leaving no trace.
    /// </remarks>
    /// <param name="reportCase">改写后的案件 / The rewritten report case</param>
    /// <param name="expectedState">期望的当前状态 / The expected current state</param>
    /// <param name="cancellationToken">取消令牌（本实现同步完成，不消费该令牌）/ Cancellation token (unused; this implementation completes synchronously)</param>
    /// <returns>更新后的案件副本；CAS 失败或记录不存在返回 null / Copy of the updated report case, or null on CAS failure or missing record</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="reportCase"/> 为 null 时抛出 / Thrown when <paramref name="reportCase"/> is null</exception>
    public Task<OnlineReportCase> ReplaceAsync(OnlineReportCase reportCase, OnlineReportState expectedState, CancellationToken cancellationToken = default)
    {
        if (reportCase == null)
        {
            throw new ArgumentNullException(nameof(reportCase));
        }

        var key = BuildKey(reportCase.TenantId, reportCase.AppId, reportCase.ReportId);
        lock (_syncRoot)
        {
            OnlineReportCase current;
            if (!_casesById.TryGetValue(key, out current))
            {
                return Task.FromResult<OnlineReportCase>(null);
            }

            if (current.State != expectedState)
            {
                // CAS 失败：不留任何写入痕迹（案件证据字段不得被半途覆盖）。
                return Task.FromResult<OnlineReportCase>(null);
            }

            // 存档存副本而非调用方实例（调用方复用该实例不会回写库内记录）。
            var stored = reportCase.Copy();
            _casesById[key] = stored;
            return Task.FromResult(stored.Copy());
        }
    }

    /// <summary>
    /// 全表线性扫描列出某举报人提交的全部案件。
    /// </summary>
    /// <remarks>
    /// Linearly scans the whole table to list all cases submitted by a reporter.
    /// </remarks>
    /// <param name="tenantId">租户标识 / Tenant id</param>
    /// <param name="appId">App 标识 / App id</param>
    /// <param name="reporterId">举报人标识 / Reporter id</param>
    /// <param name="cancellationToken">取消令牌（本实现同步完成，不消费该令牌）/ Cancellation token (unused; this implementation completes synchronously)</param>
    /// <returns>案件副本列表 / List of report case copies</returns>
    public Task<IReadOnlyList<OnlineReportCase>> ListByReporterAsync(long tenantId, long appId, long reporterId, CancellationToken cancellationToken = default)
    {
        var result = new List<OnlineReportCase>();
        lock (_syncRoot)
        {
            foreach (var pair in _casesById)
            {
                var reportCase = pair.Value;
                if (reportCase.TenantId == tenantId && reportCase.AppId == appId && reportCase.ReporterId == reporterId)
                {
                    result.Add(reportCase.Copy());
                }
            }
        }

        return Task.FromResult<IReadOnlyList<OnlineReportCase>>(result);
    }

    /// <summary>
    /// 全表线性扫描列出作用域内处于指定状态的案件（Admin 待办队列输入）。
    /// </summary>
    /// <remarks>
    /// Linearly scans the whole table to list cases in the scope with the given state (input for the admin pending queue).
    /// </remarks>
    /// <param name="tenantId">租户标识 / Tenant id</param>
    /// <param name="appId">App 标识 / App id</param>
    /// <param name="state">案件状态 / Case state</param>
    /// <param name="cancellationToken">取消令牌（本实现同步完成，不消费该令牌）/ Cancellation token (unused; this implementation completes synchronously)</param>
    /// <returns>案件副本列表 / List of report case copies</returns>
    public Task<IReadOnlyList<OnlineReportCase>> ListByStateAsync(long tenantId, long appId, OnlineReportState state, CancellationToken cancellationToken = default)
    {
        var result = new List<OnlineReportCase>();
        lock (_syncRoot)
        {
            foreach (var pair in _casesById)
            {
                var reportCase = pair.Value;
                if (reportCase.TenantId == tenantId && reportCase.AppId == appId && reportCase.State == state)
                {
                    result.Add(reportCase.Copy());
                }
            }
        }

        return Task.FromResult<IReadOnlyList<OnlineReportCase>>(result);
    }

    /// <summary>
    /// 构造案件索引键（作用域 + 案件标识）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="reportId">案件标识。</param>
    /// <returns>索引键。</returns>
    private static string BuildKey(long tenantId, long appId, string reportId)
    {
        return "rpt:" + tenantId + ":" + appId + ":" + (reportId ?? string.Empty);
    }
}
