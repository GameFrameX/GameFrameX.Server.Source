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

namespace GameFrameX.Capability.Online.Foundation.Presence;

/// <summary>
/// 在线状态内存默认存储（单进程/测试默认实现；生产装配以持久化实现替换）。
/// <para>
/// 维护约束（天花板）：全量驻留内存，超窗/关闭清理经 <c>OnlinePresenceService</c> 主动触发；
/// 全局锁保护——读写均为轻量操作，粗粒度锁足够；进程重启即全部 Offline（重启失效的内存形态）。
/// </para>
/// </summary>
public sealed class InMemoryOnlinePresenceStore : IOnlinePresenceStore
{
    /// <summary>全局读写锁。</summary>
    private readonly object _syncRoot = new();

    /// <summary>在线状态表：键 = (TenantId, AppId, PlayerId)。</summary>
    private readonly Dictionary<string, OnlinePresenceRecord> _records = new();

    /// <summary>清扫索引：Reconnecting 记录按重连截止时刻有序（索引项含记录键保证唯一）。</summary>
    private readonly SortedSet<PresenceSweepIndexEntry> _reconnectDeadlines = new();

    /// <summary>清扫索引：Online 记录按最近心跳时刻有序（索引项含记录键保证唯一）。</summary>
    private readonly SortedSet<PresenceSweepIndexEntry> _onlineHeartbeats = new();

    /// <summary>记录键 → 当前所在清扫索引项（无索引项则不存在；SetAsync/RemoveAsync 同步维护）。</summary>
    private readonly Dictionary<string, PresenceSweepIndexEntry> _sweepEntryByRecordKey = new();

    /// <summary>写入或覆盖在线状态记录。</summary>
    /// <param name="record">在线状态记录。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task SetAsync(OnlinePresenceRecord record, CancellationToken cancellationToken = default)
    {
        if (record == null)
        {
            throw new ArgumentNullException(nameof(record));
        }

        lock (_syncRoot)
        {
            var recordKey = BuildKey(record.TenantId, record.AppId, record.PlayerId);
            _records[recordKey] = record;
            RebuildSweepIndexEntry(recordKey, record);
        }

        return Task.CompletedTask;
    }

    /// <summary>按 (TenantId, AppId, PlayerId) 查找在线状态记录。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">应用标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>在线状态记录；无记录返回 null。</returns>
    public Task<OnlinePresenceRecord> FindAsync(long tenantId, long appId, long playerId, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            _records.TryGetValue(BuildKey(tenantId, appId, playerId), out var record);
            return Task.FromResult(record);
        }
    }

    /// <summary>移除在线状态记录（即 Offline）。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">应用标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task RemoveAsync(long tenantId, long appId, long playerId, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            var recordKey = BuildKey(tenantId, appId, playerId);
            _records.Remove(recordKey);
            RemoveSweepIndexEntry(recordKey);
        }
        return Task.CompletedTask;
    }

    /// <summary>列出 (TenantId, AppId) 下全部在线状态记录。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">应用标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>在线状态记录列表。</returns>
    public Task<IReadOnlyList<OnlinePresenceRecord>> ListByAppAsync(long tenantId, long appId, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            var result = new List<OnlinePresenceRecord>();
            foreach (var record in _records.Values)
            {
                if (record.TenantId == tenantId && record.AppId == appId)
                {
                    result.Add(record);
                }
            }

            return Task.FromResult<IReadOnlyList<OnlinePresenceRecord>>(result);
        }
    }

    /// <summary>统计作用域 (TenantId, AppId, ServerId) 内的在线玩家数（Blocked 不计入；表内扫描不产出实体副本）。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">应用标识。</param>
    /// <param name="serverId">区服标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>在线玩家数。</returns>
    public Task<int> CountOnlineByServerAsync(long tenantId, long appId, long serverId, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            var count = 0;
            foreach (var record in _records.Values)
            {
                if (record.TenantId == tenantId && record.AppId == appId && record.ServerId == serverId && record.State != OnlinePresenceState.Blocked)
                {
                    count++;
                }
            }

            return Task.FromResult(count);
        }
    }

    /// <summary>列出已到期的清扫候选，按到期时间升序取头部（两类到期合并快照返回）。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">应用标识。</param>
    /// <param name="nowUnixMilliseconds">判定基准时刻（Unix 毫秒）。</param>
    /// <param name="idleTimeoutMilliseconds">空闲判定阈值（毫秒）。</param>
    /// <param name="maxCount">最多返回条数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>到期候选记录快照；无候选返回空列表。</returns>
    public Task<IReadOnlyList<OnlinePresenceRecord>> ListSweepCandidatesAsync(long tenantId, long appId, long nowUnixMilliseconds, long idleTimeoutMilliseconds, int maxCount, CancellationToken cancellationToken = default)
    {
        var result = new List<OnlinePresenceRecord>(maxCount);
        lock (_syncRoot)
        {
            // 上界排他：与服务层严格比较对齐（Reconnecting 需 now > 截止、Online 需 now - 心跳 > 阈值），
            // 边界相等的记录本轮不算到期，避免成为永远不被消费的常驻候选。
            // The upper bound is exclusive, aligned with the service's strict comparisons
            // (Reconnecting needs now > deadline, Online needs now - heartbeat > threshold): a
            // boundary-equal record is not due yet and must not become a permanent candidate.
            foreach (var entry in _reconnectDeadlines.GetViewBetween(new PresenceSweepIndexEntry(long.MinValue, string.Empty), new PresenceSweepIndexEntry(nowUnixMilliseconds - 1, PresenceSweepIndexEntry.MaximumRecordKey)))
            {
                if (result.Count >= maxCount)
                {
                    break;
                }

                AppendSweepCandidate(result, tenantId, appId, entry.RecordKey);
            }

            var heartbeatDeadline = nowUnixMilliseconds - idleTimeoutMilliseconds;
            foreach (var entry in _onlineHeartbeats.GetViewBetween(new PresenceSweepIndexEntry(long.MinValue, string.Empty), new PresenceSweepIndexEntry(heartbeatDeadline - 1, PresenceSweepIndexEntry.MaximumRecordKey)))
            {
                if (result.Count >= maxCount)
                {
                    break;
                }

                AppendSweepCandidate(result, tenantId, appId, entry.RecordKey);
            }
        }

        return Task.FromResult<IReadOnlyList<OnlinePresenceRecord>>(result);
    }

    /// <summary>向候选列表追加索引项对应的记录（记录不存在或租户/应用不匹配则跳过）。</summary>
    /// <param name="result">候选输出列表。</param>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">应用标识。</param>
    /// <param name="recordKey">记录键。</param>
    private void AppendSweepCandidate(List<OnlinePresenceRecord> result, long tenantId, long appId, string recordKey)
    {
        OnlinePresenceRecord record;
        if (!_records.TryGetValue(recordKey, out record) || record.TenantId != tenantId || record.AppId != appId)
        {
            return;
        }

        result.Add(record);
    }

    /// <summary>按记录当前状态重建清扫索引项（Reconnecting 入截止索引、Online 入心跳索引、其余无索引）。</summary>
    /// <param name="recordKey">记录键。</param>
    /// <param name="record">记录。</param>
    private void RebuildSweepIndexEntry(string recordKey, OnlinePresenceRecord record)
    {
        RemoveSweepIndexEntry(recordKey);

        if (record.State == OnlinePresenceState.Reconnecting && record.ReconnectDeadlineAtTime > 0)
        {
            var entry = new PresenceSweepIndexEntry(record.ReconnectDeadlineAtTime, recordKey);
            _reconnectDeadlines.Add(entry);
            _sweepEntryByRecordKey[recordKey] = entry;
        }
        else if (record.State == OnlinePresenceState.Online)
        {
            var entry = new PresenceSweepIndexEntry(record.LastHeartbeatAtTime, recordKey);
            _onlineHeartbeats.Add(entry);
            _sweepEntryByRecordKey[recordKey] = entry;
        }
    }

    /// <summary>移除记录当前的清扫索引项（若有）。</summary>
    /// <param name="recordKey">记录键。</param>
    private void RemoveSweepIndexEntry(string recordKey)
    {
        if (_sweepEntryByRecordKey.TryGetValue(recordKey, out var entry))
        {
            _reconnectDeadlines.Remove(entry);
            _onlineHeartbeats.Remove(entry);
            _sweepEntryByRecordKey.Remove(recordKey);
        }
    }

    /// <summary>构造在线状态记录键。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">应用标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <returns>复合键字符串。</returns>
    private static string BuildKey(long tenantId, long appId, long playerId)
    {
        return tenantId + ":" + appId + ":" + playerId;
    }
}