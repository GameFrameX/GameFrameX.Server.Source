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
/// 群组存储的单进程默认实现（持久化与多实例协调归 Server 仓运行时装配，X4）。
/// <para>
/// 维护约束：整条群记录的 CAS 在同一临界区内完成——<see cref="ReplaceAsync"/> 的「比对版本号 + 落库」
/// 不可拆分，否则并发写入会双双通过版本校验而互相覆盖（成员上限与角色唯一性同时失效）。
/// </para>
/// <para>
/// 天花板（ponytail）：全局单锁 + 全员线性扫描，群组上万后需换分片锁 + 按玩家索引；
/// 当前量级下正确性优先。
/// </para>
/// </summary>
public sealed class InMemoryOnlineGroupStore : IOnlineGroupStore
{
    /// <summary>并发保护锁。</summary>
    private readonly object _syncRoot = new object();

    /// <summary>群组表（键 = 作用域 + 群组标识）。</summary>
    private readonly Dictionary<string, OnlineGroup> _groupsById = new Dictionary<string, OnlineGroup>(StringComparer.Ordinal);

    /// <summary>
    /// 以群组标识为唯一键在内存表内「不存在则创建」：键已存在时返回既有记录副本且不写入，否则存入入参的防御性副本。
    /// </summary>
    /// <remarks>
    /// Creates the group in the in-memory table if the key (scope + group id) is absent;
    /// returns a copy of the existing record without writing when the key already exists,
    /// otherwise stores a defensive copy of the input.
    /// </remarks>
    /// <param name="group">待创建的群记录 / The group record to create</param>
    /// <param name="cancellationToken">取消令牌（本实现同步完成，不消费该令牌）/ Cancellation token (unused; this implementation completes synchronously)</param>
    /// <returns>当前生效的群记录副本（既有记录或刚落库的入参）/ Copy of the currently effective group record (existing record or just-stored input)</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="group"/> 为 null 时抛出 / Thrown when <paramref name="group"/> is null</exception>
    public Task<OnlineGroup> SaveIfAbsentAsync(OnlineGroup group, CancellationToken cancellationToken = default)
    {
        if (group == null)
        {
            throw new ArgumentNullException(nameof(group));
        }

        var key = BuildKey(group.TenantId, group.AppId, group.GroupId);
        lock (_syncRoot)
        {
            OnlineGroup existing;
            if (_groupsById.TryGetValue(key, out existing))
            {
                return Task.FromResult(existing.Copy());
            }

            var stored = group.Copy();
            _groupsById[key] = stored;
            return Task.FromResult(stored.Copy());
        }
    }

    /// <summary>
    /// 按作用域与群组标识在内存表内查找群记录。
    /// </summary>
    /// <remarks>
    /// Finds a group record in the in-memory table by scope and group id.
    /// </remarks>
    /// <param name="tenantId">租户标识 / Tenant id</param>
    /// <param name="appId">App 标识 / App id</param>
    /// <param name="groupId">群组标识 / Group id</param>
    /// <param name="cancellationToken">取消令牌（本实现同步完成，不消费该令牌）/ Cancellation token (unused; this implementation completes synchronously)</param>
    /// <returns>群记录副本；不存在返回 null / Copy of the group record, or null if absent</returns>
    public Task<OnlineGroup> FindAsync(long tenantId, long appId, string groupId, CancellationToken cancellationToken = default)
    {
        var key = BuildKey(tenantId, appId, groupId);
        lock (_syncRoot)
        {
            OnlineGroup found;
            if (_groupsById.TryGetValue(key, out found))
            {
                return Task.FromResult(found.Copy());
            }

            return Task.FromResult<OnlineGroup>(null);
        }
    }

    /// <summary>
    /// 在同一临界区内以版本号 CAS 整体替换群记录：比对通过才落库并把版本号加一，任一不匹配即整体失败且不留写入痕迹。
    /// </summary>
    /// <remarks>
    /// Replaces the whole group record under one lock via revision CAS: the write lands only
    /// when the revision matches, bumping it by one; a mismatching revision or a missing
    /// record fails as a whole, leaving no trace.
    /// </remarks>
    /// <param name="group">替换后的群记录（其 GroupId/TenantId/AppId 定位目标行）/ The replacement group record (its GroupId/TenantId/AppId locate the target row)</param>
    /// <param name="expectedRevision">期望的当前版本号 / The expected current revision</param>
    /// <param name="cancellationToken">取消令牌（本实现同步完成，不消费该令牌）/ Cancellation token (unused; this implementation completes synchronously)</param>
    /// <returns>落库后的群记录副本；CAS 失败或记录不存在返回 null / Copy of the stored group record, or null on CAS failure or missing record</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="group"/> 为 null 时抛出 / Thrown when <paramref name="group"/> is null</exception>
    public Task<OnlineGroup> ReplaceAsync(OnlineGroup group, int expectedRevision, CancellationToken cancellationToken = default)
    {
        if (group == null)
        {
            throw new ArgumentNullException(nameof(group));
        }

        var key = BuildKey(group.TenantId, group.AppId, group.GroupId);
        lock (_syncRoot)
        {
            OnlineGroup current;
            if (!_groupsById.TryGetValue(key, out current))
            {
                return Task.FromResult<OnlineGroup>(null);
            }

            if (current.Revision != expectedRevision)
            {
                // CAS 失败：不留任何写入痕迹（内容与版本号都不动），调用方据 null 判定「已被并发改写」。
                return Task.FromResult<OnlineGroup>(null);
            }

            var stored = group.Copy();
            stored.Revision = expectedRevision + 1;
            stored.UpdatedAtTime = group.UpdatedAtTime > 0 ? group.UpdatedAtTime : Now();
            _groupsById[key] = stored;
            return Task.FromResult(stored.Copy());
        }
    }

    /// <summary>
    /// 全表线性扫描列出某玩家所属的全部群组（含已解散群组）。
    /// </summary>
    /// <remarks>
    /// Linearly scans the whole table to list all groups the player belongs to, including dissolved ones.
    /// </remarks>
    /// <param name="tenantId">租户标识 / Tenant id</param>
    /// <param name="appId">App 标识 / App id</param>
    /// <param name="playerId">玩家标识 / Player id</param>
    /// <param name="cancellationToken">取消令牌（本实现同步完成，不消费该令牌）/ Cancellation token (unused; this implementation completes synchronously)</param>
    /// <returns>群记录副本列表 / List of group record copies</returns>
    public Task<IReadOnlyList<OnlineGroup>> ListByPlayerAsync(long tenantId, long appId, long playerId, CancellationToken cancellationToken = default)
    {
        var result = new List<OnlineGroup>();
        lock (_syncRoot)
        {
            foreach (var pair in _groupsById)
            {
                var group = pair.Value;
                if (group.TenantId != tenantId || group.AppId != appId)
                {
                    continue;
                }

                if (group.Contains(playerId))
                {
                    result.Add(group.Copy());
                }
            }
        }

        return Task.FromResult<IReadOnlyList<OnlineGroup>>(result);
    }

    /// <summary>
    /// 全表线性扫描列出作用域内处于指定状态的全部群组。
    /// </summary>
    /// <remarks>
    /// Linearly scans the whole table to list all groups in the scope with the given state.
    /// </remarks>
    /// <param name="tenantId">租户标识 / Tenant id</param>
    /// <param name="appId">App 标识 / App id</param>
    /// <param name="state">群组状态 / Group state</param>
    /// <param name="cancellationToken">取消令牌（本实现同步完成，不消费该令牌）/ Cancellation token (unused; this implementation completes synchronously)</param>
    /// <returns>群记录副本列表 / List of group record copies</returns>
    public Task<IReadOnlyList<OnlineGroup>> ListByStateAsync(long tenantId, long appId, OnlineGroupState state, CancellationToken cancellationToken = default)
    {
        var result = new List<OnlineGroup>();
        lock (_syncRoot)
        {
            foreach (var pair in _groupsById)
            {
                var group = pair.Value;
                if (group.TenantId == tenantId && group.AppId == appId && group.State == state)
                {
                    result.Add(group.Copy());
                }
            }
        }

        return Task.FromResult<IReadOnlyList<OnlineGroup>>(result);
    }

    /// <summary>
    /// 构造群组索引键（作用域 + 群组标识）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="groupId">群组标识。</param>
    /// <returns>索引键。</returns>
    private static string BuildKey(long tenantId, long appId, string groupId)
    {
        return "group:" + tenantId + ":" + appId + ":" + (groupId ?? string.Empty);
    }

    /// <summary>取当前 UTC 毫秒时刻（入参未携带变更时刻时的兜底）。</summary>
    /// <returns>UTC 毫秒时间戳。</returns>
    private static long Now()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}
