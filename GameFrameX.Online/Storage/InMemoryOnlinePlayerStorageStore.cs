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

namespace GameFrameX.Online.Storage;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// 玩家云存储内存默认实现（vault:C3 S2.6：单进程/测试默认实现；生产装配以持久化实现替换）。
/// <para>
/// 维护约束（天花板）：CAS 与列举在全局锁内原子完成——并发写恰一成功（VC-2.10 的内存形态）；
/// 全量驻留内存，过期清理经 <c>OnlinePlayerStorageService.SweepExpiredAsync</c> 主动触发；
/// 负载数组按引用存储不防御性拷贝（调用方不得复用写入缓冲；持久化实现按序列化边界隔离）。
/// </para>
/// </summary>
public sealed class InMemoryOnlinePlayerStorageStore : IOnlinePlayerStorageStore
{
    /// <summary>全局读写锁。</summary>
    private readonly object _syncRoot = new object();

    /// <summary>条目表：键 = (TenantId, AppId, PlayerId, Collection, Key) 复合字符串。</summary>
    private readonly Dictionary<string, OnlinePlayerStorageEntry> _entries = new Dictionary<string, OnlinePlayerStorageEntry>();

    /// <summary>
    /// 各键当前已提交的版本（CAS 判定的事实源）。
    /// ponytail: 调用方可能原位变更条目对象后回写（引用共享），不能拿实体当前 Version 反推已提交版本，必须另行记账。
    /// </summary>
    private readonly Dictionary<string, long> _committedVersionByKey = new Dictionary<string, long>();

    /// <summary>按键查找条目。</summary>
    /// <param name="key">条目键载荷。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>条目；不存在返回 null。</returns>
    public Task<OnlinePlayerStorageEntry> FindAsync(OnlineStorageEntryKey key, CancellationToken cancellationToken = default)
    {
        var tenantId = key.TenantId;
        var appId = key.AppId;
        var playerId = key.PlayerId;
        var collection = key.Collection;
        var entryKey = key.Key;
        lock (_syncRoot)
        {
            _entries.TryGetValue(BuildKey(tenantId, appId, playerId, collection, entryKey), out var entry);
            return Task.FromResult(entry);
        }
    }

    /// <summary>原子比较交换写入。</summary>
    /// <param name="entry">待写入条目。</param>
    /// <param name="expectedVersion">期望版本（0 = 仅创建）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>交换成功返回 true；版本不匹配或创建冲突返回 false。</returns>
    public Task<bool> UpsertAsync(OnlinePlayerStorageEntry entry, long expectedVersion, CancellationToken cancellationToken = default)
    {
        if (entry == null)
        {
            throw new ArgumentNullException(nameof(entry));
        }

        lock (_syncRoot)
        {
            var storageKey = BuildKey(entry.TenantId, entry.AppId, entry.PlayerId, entry.Collection, entry.Key);
            var exists = _entries.ContainsKey(storageKey);
            if (expectedVersion == 0)
            {
                if (exists)
                {
                    return Task.FromResult(false);
                }
            }
            else
            {
                var committedVersion = exists ? _committedVersionByKey[storageKey] : 0;
                if (!exists || committedVersion != expectedVersion)
                {
                    return Task.FromResult(false);
                }
            }

            _entries[storageKey] = entry;
            _committedVersionByKey[storageKey] = entry.Version;
            return Task.FromResult(true);
        }
    }

    /// <summary>按键字典序列举集合内非软删条目。</summary>
    /// <param name="query">列举查询载荷。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>非软删条目列表（按键字典序）。</returns>
    public Task<IReadOnlyList<OnlinePlayerStorageEntry>> ListAsync(OnlineStorageListQuery query, CancellationToken cancellationToken = default)
    {
        var tenantId = query.TenantId;
        var appId = query.AppId;
        var playerId = query.PlayerId;
        var collection = query.Collection;
        var afterKey = query.AfterKey;
        var maxCount = query.MaxCount;
        lock (_syncRoot)
        {
            var prefix = BuildPrefix(tenantId, appId, playerId, collection);
            var matches = new List<OnlinePlayerStorageEntry>();
            foreach (var pair in _entries)
            {
                if (!pair.Key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                var entryKey = pair.Value.Key;
                if (!string.IsNullOrEmpty(afterKey) && string.CompareOrdinal(entryKey, afterKey) <= 0)
                {
                    continue;
                }

                if (pair.Value.DeletedAtTime > 0)
                {
                    continue;
                }

                matches.Add(pair.Value);
            }

            matches.Sort((left, right) => string.CompareOrdinal(left.Key, right.Key));
            var result = matches.Count > maxCount ? matches.GetRange(0, maxCount) : matches;
            return Task.FromResult<IReadOnlyList<OnlinePlayerStorageEntry>>(result);
        }
    }

    /// <summary>统计集合内活跃（非软删）键数。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">应用标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="collection">集合名。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>活跃键数。</returns>
    public Task<int> CountActiveKeysAsync(long tenantId, long appId, long playerId, string collection, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            var prefix = BuildPrefix(tenantId, appId, playerId, collection);
            var count = 0;
            foreach (var pair in _entries)
            {
                if (pair.Key.StartsWith(prefix, StringComparison.Ordinal) && pair.Value.DeletedAtTime == 0)
                {
                    count++;
                }
            }

            return Task.FromResult(count);
        }
    }

    /// <summary>列出全部条目（含软删）。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>全部条目列表。</returns>
    public Task<IReadOnlyList<OnlinePlayerStorageEntry>> ListAllAsync(CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            return Task.FromResult<IReadOnlyList<OnlinePlayerStorageEntry>>(new List<OnlinePlayerStorageEntry>(_entries.Values));
        }
    }

    /// <summary>构造条目完整键。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">应用标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="collection">集合名。</param>
    /// <param name="key">条目键。</param>
    /// <returns>复合键字符串。</returns>
    private static string BuildKey(long tenantId, long appId, long playerId, string collection, string key)
    {
        return tenantId + ":" + appId + ":" + playerId + ":" + collection + ":" + key;
    }

    /// <summary>构造集合前缀键（列举与计数用）。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">应用标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="collection">集合名。</param>
    /// <returns>前缀字符串。</returns>
    private static string BuildPrefix(long tenantId, long appId, long playerId, string collection)
    {
        return tenantId + ":" + appId + ":" + playerId + ":" + collection + ":";
    }
}
