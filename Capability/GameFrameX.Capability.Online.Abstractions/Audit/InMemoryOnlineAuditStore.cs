// ==========================================================================================
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
//   Any disputes or liabilities arising from secondary development based on this project
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


using GameFrameX.Capability.Online.Abstractions.Contracts;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GameFrameX.Capability.Online.Abstractions.Audit;

/// <summary>
/// 统一审计内存存储（单进程默认实现；生产持久化实现归运行时装配）。
/// <para>
/// 维护约束：「EventId 判重 + 落档」在 <see cref="_gate"/> 锁内**同一临界区**完成（幂等半边的
/// 结构性保证）；作用域索引键为 (TenantId, AppId) 两键——审计是跨区服检索域，区服过滤随查询载荷下推；
/// 每作用域维护按跨域检索全序（OccurredTime 倒序, EventId 序数升序）排列的有序索引，
/// <see cref="AppendAsync"/> 在同一临界区内同步插入（只插不删——记录只追加不淘汰，
/// EventId 全局唯一保证键无重复）；出入参防御性拷贝（持久化行语义，调用方引用不得别名存储内部状态）。
/// </para>
/// <para>
/// 检索路径（C393 下推）：<see cref="QueryAsync"/> 在索引上二分定位起点（游标严格之后；
/// 无游标则全序头部）后有界顺序扫描——命中即收、页满后再探一行定 <c>HasMore</c> 即停，
/// OccurredTime 低于时间窗起点即提前终止（全序倒序保证其后更旧）；防御性拷贝只发生在返回页行上。
/// </para>
/// <para>
/// 天花板（ponytail）：单锁串行化全部写入，审计量到十万级后写入争用会显著退化；
/// 审计记录只追加不淘汰，内存无上限增长——持久化实现必须在运行时装配处提供容量与归档策略。
/// 分片/持久化归运行时装配。
/// </para>
/// </summary>
public sealed class InMemoryOnlineAuditStore : IOnlineAuditStore
{
    /// <summary>
    /// 全局写入门（判重与落档的同一临界区；覆盖全部作用域——EventId 全局唯一）。
    /// </summary>
    private readonly object _gate = new();

    /// <summary>
    /// 已落档 EventId 集合（全局判重索引，不按作用域分片）。
    /// </summary>
    private readonly HashSet<string> _knownEventIds = new(StringComparer.Ordinal);

    /// <summary>
    /// 作用域级有序索引（键 = "TenantId|AppId"；值 = 按跨域检索全序排列的落档记录，
    /// 正序枚举即 (OccurredTime 倒序, EventId 序数升序)——检索与游标定位的唯一排序事实来源）。
    /// </summary>
    private readonly Dictionary<string, SortedList<OnlineAuditSortKey, OnlineAuditRecord>> _recordsByScope = new(StringComparer.Ordinal);

    /// <summary>
    /// 追加一条审计记录（锁内判重 + 落档 + 有序索引插入同一临界区）。
    /// </summary>
    /// <param name="record">待落档记录。</param>
    /// <param name="cancellationToken">取消令牌（内存实现无异步等待，令牌仅契约对齐）。</param>
    /// <returns>新落档返回 <see langword="true"/>；EventId 已存在返回 <see langword="false"/>（不落档、无副作用）。</returns>
    public Task<bool> AppendAsync(OnlineAuditRecord record, CancellationToken cancellationToken = default)
    {
        if (record == null)
        {
            throw new ArgumentNullException(nameof(record));
        }

        lock (_gate)
        {
            if (!_knownEventIds.Add(record.EventId))
            {
                return Task.FromResult(false);
            }

            var scopeKey = BuildScopeKey(record.TenantId, record.AppId);
            if (!_recordsByScope.TryGetValue(scopeKey, out var bucket))
            {
                bucket = new SortedList<OnlineAuditSortKey, OnlineAuditRecord>(OnlineAuditSortKeyComparer.Instance);
                _recordsByScope[scopeKey] = bucket;
            }

            bucket.Add(new OnlineAuditSortKey(record.OccurredTime, record.EventId), CloneRecord(record));
            return Task.FromResult(true);
        }
    }

    /// <summary>
    /// 按查询载荷检索审计页：索引上二分定位起点后有界扫描，过滤 + keyset 游标 + 全序 + 页大小全部在本实现内完成。
    /// </summary>
    /// <param name="query">存储层查询载荷（作用域 / 过滤维度 / 已解码游标 / 页大小已由调用方校验）。</param>
    /// <param name="cancellationToken">取消令牌（内存实现无异步等待，令牌仅契约对齐）。</param>
    /// <returns>按全序排列、至多 PageSize 行的结果页（行集为防御性拷贝）；作用域外结构性不可见。</returns>
    public Task<OnlineAuditStorePage> QueryAsync(OnlineAuditStoreQuery query, CancellationToken cancellationToken = default)
    {
        if (query == null)
        {
            throw new ArgumentNullException(nameof(query));
        }

        var pageSize = query.PageSize > 0 ? query.PageSize : 1;
        var records = new List<OnlineAuditRecord>(Math.Min(pageSize, 64));
        var hasMore = false;
        lock (_gate)
        {
            if (_recordsByScope.TryGetValue(BuildScopeKey(query.TenantId, query.AppId), out var bucket))
            {
                hasMore = CollectPageFromBucket(bucket, query, pageSize, records);
            }
        }

        return Task.FromResult(new OnlineAuditStorePage(records, hasMore));
    }

    /// <summary>
    /// 在作用域索引上有界顺序扫描一页：游标起点定位后命中过滤即收，页满后再探到一行命中即判
    /// HasMore 并停止；OccurredTime 低于时间窗起点即提前终止（全序倒序保证其后更旧，其后无更多行）。
    /// 须在 <see cref="_gate"/> 临界区内调用（与追加共用同一索引快照）；收入页行的均为防御性拷贝。
    /// </summary>
    /// <param name="bucket">作用域有序索引（全序正序排列）。</param>
    /// <param name="query">存储层查询载荷（过滤维度 / 已解码游标 / 时间窗）。</param>
    /// <param name="pageSize">规范化后的页大小（≥1，由调用方保证）。</param>
    /// <param name="records">接收页行的目标列表（由调用方按页大小预分配）。</param>
    /// <returns>存在被页大小截断的更多候选行返回 <see langword="true"/>（HasMore）；扫描耗尽或时间窗提前终止返回 <see langword="false"/>。</returns>
    private static bool CollectPageFromBucket(SortedList<OnlineAuditSortKey, OnlineAuditRecord> bucket, OnlineAuditStoreQuery query, int pageSize, List<OnlineAuditRecord> records)
    {
        var startIndex = query.HasCursor
            ? FindFirstAfterCursor(bucket, query.CursorOccurredTime, query.CursorEventId)
            : 0;
        for (var index = startIndex; index < bucket.Count; index++)
        {
            var record = bucket.Values[index];

            // 全序为发生时刻倒序：一旦低于时间窗起点，其后记录只会更旧，可提前终止。
            if (query.StartTime.HasValue && record.OccurredTime < query.StartTime.Value)
            {
                return false;
            }

            if (!MatchesFilter(record, query))
            {
                continue;
            }

            if (records.Count >= pageSize)
            {
                return true;
            }

            records.Add(CloneRecord(record));
        }

        return false;
    }

    /// <summary>
    /// 构造作用域索引键（"TenantId|AppId"；审计是跨区服检索域，区服维度不进作用域键）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <returns>作用域索引键。</returns>
    private static string BuildScopeKey(long tenantId, long appId)
    {
        return tenantId.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + appId.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 二分定位「严格位于游标位之后」的首个索引下标（keyset 语义；游标行自身若仍在档则被跳过）。
    /// </summary>
    /// <param name="bucket">作用域有序索引（全序正序排列）。</param>
    /// <param name="cursorOccurredTime">游标行的发生时刻（UTC 毫秒）。</param>
    /// <param name="cursorEventId">游标行的审计标识。</param>
    /// <returns>首个严格位于游标之后的下标；游标位于全序末尾之后时为 <see cref="SortedList{TKey,TValue}.Count"/>（空扫描）。</returns>
    private static int FindFirstAfterCursor(SortedList<OnlineAuditSortKey, OnlineAuditRecord> bucket, long cursorOccurredTime, string cursorEventId)
    {
        var cursorKey = new OnlineAuditSortKey(cursorOccurredTime, cursorEventId);
        var keys = bucket.Keys;
        var low = 0;
        var high = keys.Count;
        while (low < high)
        {
            var middle = low + ((high - low) >> 1);
            if (OnlineAuditSortKeyComparer.Instance.Compare(keys[middle], cursorKey) <= 0)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    /// <summary>
    /// 判断记录是否落在全部可选过滤内（合取；域集合为析取——跨域联查语义；时间窗为闭区间）。
    /// </summary>
    /// <param name="record">待判定记录。</param>
    /// <param name="query">查询条件。</param>
    /// <returns>命中过滤返回 <c>true</c>。</returns>
    private static bool MatchesFilter(OnlineAuditRecord record, OnlineAuditStoreQuery query)
    {
        if (!MatchesDomainFilter(record, query.Domains))
        {
            return false;
        }

        if (query.PlayerId.HasValue && record.PlayerId != query.PlayerId.Value)
        {
            return false;
        }

        if (!string.IsNullOrEmpty(query.OperatorId) && !string.Equals(record.OperatorId, query.OperatorId, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(query.EventType) && !string.Equals(record.EventType, query.EventType, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(query.CorrelationId) && !string.Equals(record.CorrelationId, query.CorrelationId, StringComparison.Ordinal))
        {
            return false;
        }

        if (query.ServerId.HasValue && record.ServerId != query.ServerId.Value)
        {
            return false;
        }

        if (query.EndTime.HasValue && record.OccurredTime > query.EndTime.Value)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// 判定域集合过滤是否命中（析取——跨域联查语义；集合为空即不限域）。
    /// </summary>
    /// <param name="record">待判定记录。</param>
    /// <param name="domains">域过滤集合。</param>
    /// <returns>命中返回 <c>true</c>。</returns>
    private static bool MatchesDomainFilter(OnlineAuditRecord record, IReadOnlyList<string> domains)
    {
        if (domains == null || domains.Count == 0)
        {
            return true;
        }

        foreach (var domain in domains)
        {
            if (string.Equals(record.Domain, domain, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 深拷贝审计记录（含 SanitizedFields 字典；持久化行语义——出入参不与存储内部状态别名）。
    /// </summary>
    /// <param name="record">源记录。</param>
    /// <returns>可独立变更的拷贝。</returns>
    private static OnlineAuditRecord CloneRecord(OnlineAuditRecord record)
    {
        Dictionary<string, string> sanitized;
        if (record.SanitizedFields == null || record.SanitizedFields.Count == 0)
        {
            sanitized = new Dictionary<string, string>();
        }
        else
        {
            sanitized = new Dictionary<string, string>(record.SanitizedFields.Count, StringComparer.Ordinal);
            foreach (var pair in record.SanitizedFields)
            {
                sanitized[pair.Key] = pair.Value;
            }
        }

        return new OnlineAuditRecord
        {
            EventId = record.EventId,
            Domain = record.Domain,
            EventType = record.EventType,
            OccurredTime = record.OccurredTime,
            TenantId = record.TenantId,
            AppId = record.AppId,
            ServerId = record.ServerId,
            PlayerId = record.PlayerId,
            OperatorId = record.OperatorId,
            OperatorName = record.OperatorName,
            Reason = record.Reason,
            Source = record.Source,
            CorrelationId = record.CorrelationId,
            SanitizedFields = sanitized,
        };
    }

    /// <summary>
    /// 审计全序排序键（(OccurredTime, EventId) 二元组；EventId 全局唯一保证键无重复）。
    /// </summary>
    private readonly struct OnlineAuditSortKey
    {
        /// <summary>发生时刻（UTC 毫秒；全序第一键）。</summary>
        private readonly long _occurredTime;

        /// <summary>审计标识（同毫秒内打破平局的第二键）。</summary>
        private readonly string _eventId;

        /// <summary>
        /// 构造排序键。
        /// </summary>
        /// <param name="occurredTime">发生时刻（UTC 毫秒）。</param>
        /// <param name="eventId">审计标识。</param>
        public OnlineAuditSortKey(long occurredTime, string eventId)
        {
            _occurredTime = occurredTime;
            _eventId = eventId;
        }

        /// <summary>
        /// 暴露发生时刻供比较器读取。
        /// </summary>
        public long OccurredTime => _occurredTime;

        /// <summary>
        /// 暴露审计标识供比较器读取。
        /// </summary>
        public string EventId => _eventId;
    }

    /// <summary>
    /// 审计全序比较器（跨域检索全序的唯一事实来源：OccurredTime **倒序**，同一毫秒内 EventId 序数**升序**——
    /// 正序枚举即分页顺序；换序 = 换全部查询口径，须先经设计评审）。
    /// </summary>
    private sealed class OnlineAuditSortKeyComparer : IComparer<OnlineAuditSortKey>
    {
        /// <summary>无状态单例。</summary>
        public static readonly OnlineAuditSortKeyComparer Instance = new OnlineAuditSortKeyComparer();

        /// <summary>
        /// 防止外部构造。
        /// </summary>
        private OnlineAuditSortKeyComparer()
        {
        }

        /// <summary>
        /// 比较两个排序键：发生时刻倒序，同一毫秒内按审计标识序数升序。
        /// </summary>
        /// <param name="left">左键。</param>
        /// <param name="right">右键。</param>
        /// <returns>比较结果。</returns>
        public int Compare(OnlineAuditSortKey left, OnlineAuditSortKey right)
        {
            if (left.OccurredTime != right.OccurredTime)
            {
                return right.OccurredTime.CompareTo(left.OccurredTime);
            }

            return string.CompareOrdinal(left.EventId, right.EventId);
        }
    }
}
