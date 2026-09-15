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

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GameFrameX.Online.Audit;

/// <summary>
/// 统一审计内存存储（单进程默认实现；生产持久化实现归运行时装配，X4）。
/// <para>
/// 维护约束：「EventId 判重 + 落档」在 <see cref="_gate"/> 锁内**同一临界区**完成（VC-8.4 幂等半边的
/// 结构性保证）；作用域索引键为 (TenantId, AppId) 两键——审计是跨区服检索域，区服过滤由服务层承担；
/// 出入参防御性拷贝（持久化行语义，调用方引用不得别名存储内部状态）。
/// </para>
/// <para>
/// 天花板（ponytail）：单锁串行化全部写入 + <see cref="ListAsync"/> 返回作用域级全量快照，
/// 审计量到十万级后写入争用与快照成本会显著退化；升级路径 = 存储层按 (作用域, 时间) 索引 +
/// 游标下推查询（届时 <c>OnlineAuditService</c> 的全序过滤逻辑迁移到存储实现内），分片/持久化归运行时装配。
/// 审计记录只追加不淘汰，内存无上限增长——持久化实现必须在运行时装配处提供容量与归档策略。
/// </para>
/// </summary>
public sealed class InMemoryOnlineAuditStore : IOnlineAuditStore
{
    /// <summary>
    /// 全局写入门（判重与落档的同一临界区；覆盖全部作用域——EventId 全局唯一）。
    /// </summary>
    private readonly object _gate = new object();

    /// <summary>
    /// 已落档 EventId 集合（全局判重索引，不按作用域分片）。
    /// </summary>
    private readonly HashSet<string> _knownEventIds = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// 作用域级记录索引（键 = "TenantId|AppId"；值为该作用域内的落档序列）。
    /// </summary>
    private readonly Dictionary<string, List<OnlineAuditRecord>> _recordsByScope = new Dictionary<string, List<OnlineAuditRecord>>(StringComparer.Ordinal);

    /// <summary>
    /// 追加一条审计记录（锁内判重 + 落档同一临界区）。
    /// </summary>
    /// <param name="record">待落档记录。</param>
    /// <param name="cancellationToken">取消令牌（内存实现无异步等待，令牌仅契约对齐）。</param>
    /// <returns>新落档返回 <c>true</c>；EventId 已存在返回 <c>false</c>（不落档、无副作用）。</returns>
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

            var scopeKey = record.TenantId.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + record.AppId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!_recordsByScope.TryGetValue(scopeKey, out var bucket))
            {
                bucket = new List<OnlineAuditRecord>();
                _recordsByScope[scopeKey] = bucket;
            }

            bucket.Add(CloneRecord(record));
            return Task.FromResult(true);
        }
    }

    /// <summary>
    /// 列出作用域内全部审计记录（快照拷贝，调用方修改不影响存储内部状态）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="cancellationToken">取消令牌（内存实现无异步等待，令牌仅契约对齐）。</param>
    /// <returns>该作用域内的记录快照；无数据返回空列表。</returns>
    public Task<IReadOnlyList<OnlineAuditRecord>> ListAsync(long tenantId, long appId, CancellationToken cancellationToken = default)
    {
        var scopeKey = tenantId.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + appId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        lock (_gate)
        {
            if (!_recordsByScope.TryGetValue(scopeKey, out var bucket))
            {
                return Task.FromResult<IReadOnlyList<OnlineAuditRecord>>(new List<OnlineAuditRecord>());
            }

            var snapshot = new List<OnlineAuditRecord>(bucket.Count);
            foreach (var record in bucket)
            {
                snapshot.Add(CloneRecord(record));
            }

            return Task.FromResult<IReadOnlyList<OnlineAuditRecord>>(snapshot);
        }
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
}
