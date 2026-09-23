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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// 通知存储的单进程默认实现（持久化与多实例协调归 Server 仓运行时装配，X4）。
/// <para>
/// 维护约束：去重键唯一性与状态 CAS 都在同一临界区内完成——<see cref="SaveIfAbsentAsync"/> 的
/// 「查 + 双索引写」不可拆分，否则并发入队会落出两条同去重键的通知，「业务只执行一次」随之失效（VC-6.12）。
/// </para>
/// <para>
/// 天花板（ponytail）：全局单锁 + 全表线性扫描，且过期记录不归档，内存随玩家历史线性增长；
/// 通知量上万后需换按接收者分片的锁 + 过期归档（或改持久化实现并按索引查询）。当前量级下正确性优先。
/// </para>
/// </summary>
public sealed class InMemoryOnlineNotificationStore : IOnlineNotificationStore
{
    /// <summary>并发保护锁。</summary>
    private readonly object _syncRoot = new object();

    /// <summary>通知表（键 = 作用域 + 接收者 + 通知标识）。</summary>
    private readonly Dictionary<string, OnlineNotification> _notificationsByIdKey = new Dictionary<string, OnlineNotification>(StringComparer.Ordinal);

    /// <summary>去重索引（键 = 作用域 + 接收者 + 去重键，值 = 通知表键）。</summary>
    private readonly Dictionary<string, string> _idKeyByDedupeKey = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// 以去重键为唯一键在内存表内「不存在则创建」：命中去重索引时返回既有通知副本且不写入，否则在同一临界区内同时落定标识索引与去重索引。
    /// </summary>
    /// <remarks>
    /// Creates the notification in the in-memory table if the dedupe key (scope + recipient + dedupe key) is absent;
    /// a dedupe hit returns a copy of the existing record without writing, otherwise the id index and the dedupe
    /// index are settled together in one critical section.
    /// </remarks>
    /// <param name="notification">待创建的通知 / The notification to create</param>
    /// <param name="cancellationToken">取消令牌（本实现同步完成，不消费该令牌）/ Cancellation token (unused; this implementation completes synchronously)</param>
    /// <returns>当前生效的通知副本（既有记录或刚落库的入参）/ Copy of the currently effective notification (existing record or just-stored input)</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="notification"/> 为 null 时抛出 / Thrown when <paramref name="notification"/> is null</exception>
    public Task<OnlineNotification> SaveIfAbsentAsync(OnlineNotification notification, CancellationToken cancellationToken = default)
    {
        if (notification == null)
        {
            throw new ArgumentNullException(nameof(notification));
        }

        var idKey = BuildIdKey(notification.TenantId, notification.AppId, notification.PlayerId, notification.NotificationId);
        var dedupeKey = BuildDedupeKey(notification.TenantId, notification.AppId, notification.PlayerId, notification.DedupeKey);
        lock (_syncRoot)
        {
            string existingIdKey;
            if (_idKeyByDedupeKey.TryGetValue(dedupeKey, out existingIdKey))
            {
                OnlineNotification existing;
                if (_notificationsByIdKey.TryGetValue(existingIdKey, out existing))
                {
                    return Task.FromResult(existing.Copy());
                }
            }

            // 两条索引在同一临界区落定：只写其一会让「去重命中」与「按标识读取」指向不同记录。
            // 存档存副本而非调用方实例（调用方复用该实例不会回写库内记录）。
            var stored = notification.Copy();
            _notificationsByIdKey[idKey] = stored;
            _idKeyByDedupeKey[dedupeKey] = idKey;
            return Task.FromResult(stored.Copy());
        }
    }

    /// <summary>
    /// 按「作用域 + 接收者 + 通知标识」在内存表内查找通知。
    /// </summary>
    /// <remarks>
    /// Finds a notification in the in-memory table by scope, recipient and notification id.
    /// </remarks>
    /// <param name="tenantId">租户标识 / Tenant id</param>
    /// <param name="appId">App 标识 / App id</param>
    /// <param name="playerId">接收者玩家标识 / Recipient player id</param>
    /// <param name="notificationId">通知标识 / Notification id</param>
    /// <param name="cancellationToken">取消令牌（本实现同步完成，不消费该令牌）/ Cancellation token (unused; this implementation completes synchronously)</param>
    /// <returns>通知副本；不存在返回 null / Copy of the notification, or null if absent</returns>
    public Task<OnlineNotification> FindAsync(long tenantId, long appId, long playerId, string notificationId, CancellationToken cancellationToken = default)
    {
        var idKey = BuildIdKey(tenantId, appId, playerId, notificationId);
        lock (_syncRoot)
        {
            OnlineNotification found;
            if (_notificationsByIdKey.TryGetValue(idKey, out found))
            {
                return Task.FromResult(found.Copy());
            }

            return Task.FromResult<OnlineNotification>(null);
        }
    }

    /// <summary>
    /// 在同一临界区内以「期望状态 + 期望尝试次数」双判据 CAS 整体替换通知记录，任一不匹配或记录不存在即整体失败且不留写入痕迹。
    /// </summary>
    /// <remarks>
    /// Replaces the whole notification record under one lock via CAS on both the expected state and the
    /// expected attempt count; any mismatch or a missing record fails as a whole, leaving no trace.
    /// </remarks>
    /// <param name="notification">替换后的通知快照 / The replacement notification snapshot</param>
    /// <param name="expectedState">期望的当前状态 / The expected current state</param>
    /// <param name="expectedAttemptCount">期望的当前尝试次数 / The expected current attempt count</param>
    /// <param name="cancellationToken">取消令牌（本实现同步完成，不消费该令牌）/ Cancellation token (unused; this implementation completes synchronously)</param>
    /// <returns>替换后的通知副本；CAS 失败或记录不存在返回 null / Copy of the replaced notification, or null on CAS failure or missing record</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="notification"/> 为 null 时抛出 / Thrown when <paramref name="notification"/> is null</exception>
    public Task<OnlineNotification> ReplaceAsync(OnlineNotification notification, OnlineNotificationState expectedState, int expectedAttemptCount, CancellationToken cancellationToken = default)
    {
        if (notification == null)
        {
            throw new ArgumentNullException(nameof(notification));
        }

        var idKey = BuildIdKey(notification.TenantId, notification.AppId, notification.PlayerId, notification.NotificationId);
        lock (_syncRoot)
        {
            OnlineNotification current;
            if (!_notificationsByIdKey.TryGetValue(idKey, out current))
            {
                return Task.FromResult<OnlineNotification>(null);
            }

            if (current.State != expectedState || current.AttemptCount != expectedAttemptCount)
            {
                // CAS 失败：不留任何写入痕迹，调用方据 null 判定「状态或尝试次数已被并发改写」。
                return Task.FromResult<OnlineNotification>(null);
            }

            // 防御性深拷贝：存档存副本而非调用方实例，调用方后续复用该实例（如落定快照）不会回写库内记录。
            var stored = notification.Copy();
            _notificationsByIdKey[idKey] = stored;
            return Task.FromResult(stored.Copy());
        }
    }

    /// <summary>
    /// 全表线性扫描列出某接收者的全部通知（任意状态）。
    /// </summary>
    /// <remarks>
    /// Linearly scans the whole table to list all notifications of a recipient in any state.
    /// </remarks>
    /// <param name="tenantId">租户标识 / Tenant id</param>
    /// <param name="appId">App 标识 / App id</param>
    /// <param name="playerId">接收者玩家标识 / Recipient player id</param>
    /// <param name="cancellationToken">取消令牌（本实现同步完成，不消费该令牌）/ Cancellation token (unused; this implementation completes synchronously)</param>
    /// <returns>通知副本列表 / List of notification copies</returns>
    public Task<IReadOnlyList<OnlineNotification>> ListByPlayerAsync(long tenantId, long appId, long playerId, CancellationToken cancellationToken = default)
    {
        var result = new List<OnlineNotification>();
        lock (_syncRoot)
        {
            foreach (var pair in _notificationsByIdKey)
            {
                var notification = pair.Value;
                if (notification.TenantId == tenantId && notification.AppId == appId && notification.PlayerId == playerId)
                {
                    result.Add(notification.Copy());
                }
            }
        }

        return Task.FromResult<IReadOnlyList<OnlineNotification>>(result);
    }

    /// <summary>
    /// 全表线性扫描列出作用域内处于指定状态的全部通知。
    /// </summary>
    /// <remarks>
    /// Linearly scans the whole table to list all notifications in the scope with the given state.
    /// </remarks>
    /// <param name="tenantId">租户标识 / Tenant id</param>
    /// <param name="appId">App 标识 / App id</param>
    /// <param name="state">通知状态 / Notification state</param>
    /// <param name="cancellationToken">取消令牌（本实现同步完成，不消费该令牌）/ Cancellation token (unused; this implementation completes synchronously)</param>
    /// <returns>通知副本列表 / List of notification copies</returns>
    public Task<IReadOnlyList<OnlineNotification>> ListByStateAsync(long tenantId, long appId, OnlineNotificationState state, CancellationToken cancellationToken = default)
    {
        var result = new List<OnlineNotification>();
        lock (_syncRoot)
        {
            foreach (var pair in _notificationsByIdKey)
            {
                var notification = pair.Value;
                if (notification.TenantId == tenantId && notification.AppId == appId && notification.State == state)
                {
                    result.Add(notification.Copy());
                }
            }
        }

        return Task.FromResult<IReadOnlyList<OnlineNotification>>(result);
    }

    /// <summary>
    /// 构造通知标识索引键（作用域 + 接收者 + 通知标识）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="playerId">接收者玩家标识。</param>
    /// <param name="notificationId">通知标识。</param>
    /// <returns>索引键。</returns>
    private static string BuildIdKey(long tenantId, long appId, long playerId, string notificationId)
    {
        return "ntf:" + tenantId + ":" + appId + ":" + playerId + ":" + (notificationId ?? string.Empty);
    }

    /// <summary>
    /// 构造去重索引键（作用域 + 接收者 + 去重键）。
    /// <para>
    /// 键含接收者是刻意为之：同一去重键对不同接收者是两条合法通知（一次业务动作通知多人），
    /// 去重只约束「同一接收者的同一业务动作」。
    /// </para>
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="playerId">接收者玩家标识。</param>
    /// <param name="dedupeKey">去重键。</param>
    /// <returns>索引键。</returns>
    private static string BuildDedupeKey(long tenantId, long appId, long playerId, string dedupeKey)
    {
        return "ntfd:" + tenantId + ":" + appId + ":" + playerId + ":" + (dedupeKey ?? string.Empty);
    }
}
