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
/// 社交关系图谱存储的单进程默认实现（持久化与多实例协调归 Server 仓运行时装配，X4）。
/// <para>
/// 维护约束：<see cref="IsBlockedEitherWayAsync"/> 的两个方向查询与
/// <see cref="ListActivePunishmentsAsync"/> 的生效过滤都在**同一临界区内**完成——
/// 这两处是裁决正确性的地基，拆开加锁会让并发写入挤进判定中段（见接口上的红线说明）。
/// </para>
/// <para>
/// 天花板（ponytail）：全局单锁 + 全表线性扫描。处罚表随封禁量增长，裁决路径又会每帧命中，
/// 量大后需换按玩家分片的二级索引与带生效区间的有序索引；当前量级下正确性优先。
/// </para>
/// </summary>
public sealed class InMemoryOnlineSocialGraphStore : IOnlineSocialGraphStore
{
    /// <summary>并发保护锁。</summary>
    private readonly object _syncRoot = new object();

    /// <summary>屏蔽表（键 = 作用域 + 归属玩家 + 被屏蔽玩家）。</summary>
    private readonly Dictionary<string, OnlineBlockEntry> _blocksByDirection = new Dictionary<string, OnlineBlockEntry>(StringComparer.Ordinal);

    /// <summary>静音表（键 = 作用域 + 归属玩家 + 被静音玩家）。</summary>
    private readonly Dictionary<string, OnlineMuteEntry> _mutesByDirection = new Dictionary<string, OnlineMuteEntry>(StringComparer.Ordinal);

    /// <summary>处罚表（键 = 作用域 + 处罚标识）。</summary>
    private readonly Dictionary<string, OnlinePunishment> _punishmentsById = new Dictionary<string, OnlinePunishment>(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task<OnlineBlockEntry> SaveBlockIfAbsentAsync(OnlineBlockEntry entry, CancellationToken cancellationToken = default)
    {
        if (entry == null)
        {
            throw new ArgumentNullException(nameof(entry));
        }

        var key = BuildDirectionKey("blk", entry.TenantId, entry.AppId, entry.OwnerId, entry.BlockedPlayerId);
        lock (_syncRoot)
        {
            OnlineBlockEntry existing;
            if (_blocksByDirection.TryGetValue(key, out existing))
            {
                return Task.FromResult(existing.Copy());
            }

            // 存档存副本而非调用方实例（调用方复用该实例不会回写库内记录）。
            var stored = entry.Copy();
            _blocksByDirection[key] = stored;
            return Task.FromResult(stored.Copy());
        }
    }

    /// <inheritdoc />
    public Task<OnlineBlockEntry> FindBlockAsync(long tenantId, long appId, long ownerId, long blockedPlayerId, CancellationToken cancellationToken = default)
    {
        var key = BuildDirectionKey("blk", tenantId, appId, ownerId, blockedPlayerId);
        lock (_syncRoot)
        {
            OnlineBlockEntry found;
            if (_blocksByDirection.TryGetValue(key, out found))
            {
                return Task.FromResult(found.Copy());
            }

            return Task.FromResult<OnlineBlockEntry>(null);
        }
    }

    /// <inheritdoc />
    public Task<bool> RemoveBlockAsync(long tenantId, long appId, long ownerId, long blockedPlayerId, CancellationToken cancellationToken = default)
    {
        var key = BuildDirectionKey("blk", tenantId, appId, ownerId, blockedPlayerId);
        lock (_syncRoot)
        {
            return Task.FromResult(_blocksByDirection.Remove(key));
        }
    }

    /// <inheritdoc />
    public Task<bool> IsBlockedEitherWayAsync(long tenantId, long appId, long leftPlayerId, long rightPlayerId, CancellationToken cancellationToken = default)
    {
        var forwardKey = BuildDirectionKey("blk", tenantId, appId, leftPlayerId, rightPlayerId);
        var backwardKey = BuildDirectionKey("blk", tenantId, appId, rightPlayerId, leftPlayerId);
        lock (_syncRoot)
        {
            // 两个方向必须同临界区判定：拆开会让并发屏蔽挤进两次查询之间，产生放行窗口。
            return Task.FromResult(_blocksByDirection.ContainsKey(forwardKey) || _blocksByDirection.ContainsKey(backwardKey));
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OnlineBlockEntry>> ListBlocksAsync(long tenantId, long appId, long ownerId, CancellationToken cancellationToken = default)
    {
        var result = new List<OnlineBlockEntry>();
        lock (_syncRoot)
        {
            foreach (var pair in _blocksByDirection)
            {
                var entry = pair.Value;
                if (entry.TenantId == tenantId && entry.AppId == appId && entry.OwnerId == ownerId)
                {
                    result.Add(entry.Copy());
                }
            }
        }

        return Task.FromResult<IReadOnlyList<OnlineBlockEntry>>(result);
    }

    /// <inheritdoc />
    public Task<OnlineMuteEntry> SaveMuteIfAbsentAsync(OnlineMuteEntry entry, CancellationToken cancellationToken = default)
    {
        if (entry == null)
        {
            throw new ArgumentNullException(nameof(entry));
        }

        var key = BuildDirectionKey("mut", entry.TenantId, entry.AppId, entry.OwnerId, entry.MutedPlayerId);
        lock (_syncRoot)
        {
            OnlineMuteEntry existing;
            if (_mutesByDirection.TryGetValue(key, out existing))
            {
                return Task.FromResult(existing.Copy());
            }

            // 存档存副本而非调用方实例（调用方复用该实例不会回写库内记录）。
            var stored = entry.Copy();
            _mutesByDirection[key] = stored;
            return Task.FromResult(stored.Copy());
        }
    }

    /// <inheritdoc />
    public Task<OnlineMuteEntry> FindMuteAsync(long tenantId, long appId, long ownerId, long mutedPlayerId, CancellationToken cancellationToken = default)
    {
        var key = BuildDirectionKey("mut", tenantId, appId, ownerId, mutedPlayerId);
        lock (_syncRoot)
        {
            OnlineMuteEntry found;
            if (_mutesByDirection.TryGetValue(key, out found))
            {
                return Task.FromResult(found.Copy());
            }

            return Task.FromResult<OnlineMuteEntry>(null);
        }
    }

    /// <inheritdoc />
    public Task<bool> RemoveMuteAsync(long tenantId, long appId, long ownerId, long mutedPlayerId, CancellationToken cancellationToken = default)
    {
        var key = BuildDirectionKey("mut", tenantId, appId, ownerId, mutedPlayerId);
        lock (_syncRoot)
        {
            return Task.FromResult(_mutesByDirection.Remove(key));
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OnlineMuteEntry>> ListMutesAsync(long tenantId, long appId, long ownerId, CancellationToken cancellationToken = default)
    {
        var result = new List<OnlineMuteEntry>();
        lock (_syncRoot)
        {
            foreach (var pair in _mutesByDirection)
            {
                var entry = pair.Value;
                if (entry.TenantId == tenantId && entry.AppId == appId && entry.OwnerId == ownerId)
                {
                    result.Add(entry.Copy());
                }
            }
        }

        return Task.FromResult<IReadOnlyList<OnlineMuteEntry>>(result);
    }

    /// <inheritdoc />
    public Task<OnlinePunishment> SavePunishmentIfAbsentAsync(OnlinePunishment punishment, CancellationToken cancellationToken = default)
    {
        if (punishment == null)
        {
            throw new ArgumentNullException(nameof(punishment));
        }

        var key = BuildPunishmentKey(punishment.TenantId, punishment.AppId, punishment.PunishmentId);
        lock (_syncRoot)
        {
            OnlinePunishment existing;
            if (_punishmentsById.TryGetValue(key, out existing))
            {
                return Task.FromResult(existing.Copy());
            }

            // 存档存副本而非调用方实例（调用方复用该实例不会回写库内记录）。
            var stored = punishment.Copy();
            _punishmentsById[key] = stored;
            return Task.FromResult(stored.Copy());
        }
    }

    /// <inheritdoc />
    public Task<OnlinePunishment> FindPunishmentAsync(long tenantId, long appId, string punishmentId, CancellationToken cancellationToken = default)
    {
        var key = BuildPunishmentKey(tenantId, appId, punishmentId);
        lock (_syncRoot)
        {
            OnlinePunishment found;
            if (_punishmentsById.TryGetValue(key, out found))
            {
                return Task.FromResult(found.Copy());
            }

            return Task.FromResult<OnlinePunishment>(null);
        }
    }

    /// <inheritdoc />
    public Task<OnlinePunishment> ReplacePunishmentAsync(OnlinePunishment punishment, int expectedRevision, CancellationToken cancellationToken = default)
    {
        if (punishment == null)
        {
            throw new ArgumentNullException(nameof(punishment));
        }

        var key = BuildPunishmentKey(punishment.TenantId, punishment.AppId, punishment.PunishmentId);
        lock (_syncRoot)
        {
            OnlinePunishment current;
            if (!_punishmentsById.TryGetValue(key, out current))
            {
                return Task.FromResult<OnlinePunishment>(null);
            }

            if (current.Revision != expectedRevision)
            {
                // CAS 失败：不留任何写入痕迹，调用方据 null 判定「版本已被并发改写」。
                return Task.FromResult<OnlinePunishment>(null);
            }

            punishment.Revision = expectedRevision + 1;
            // 存档存副本而非调用方实例（调用方复用该实例不会回写库内记录）。
            var stored = punishment.Copy();
            _punishmentsById[key] = stored;
            return Task.FromResult(stored.Copy());
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OnlinePunishment>> ListActivePunishmentsAsync(long tenantId, long appId, long playerId, long nowUnixMilliseconds, CancellationToken cancellationToken = default)
    {
        var result = new List<OnlinePunishment>();
        lock (_syncRoot)
        {
            // 生效过滤同临界区完成：调用方不得拿到全量处罚后自行拼装生效条件。
            foreach (var pair in _punishmentsById)
            {
                var punishment = pair.Value;
                if (punishment.TenantId != tenantId || punishment.AppId != appId || punishment.PlayerId != playerId)
                {
                    continue;
                }

                if (punishment.IsActiveAt(nowUnixMilliseconds))
                {
                    result.Add(punishment.Copy());
                }
            }
        }

        return Task.FromResult<IReadOnlyList<OnlinePunishment>>(result);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OnlinePunishment>> ListPunishmentsByPlayerAsync(long tenantId, long appId, long playerId, CancellationToken cancellationToken = default)
    {
        var result = new List<OnlinePunishment>();
        lock (_syncRoot)
        {
            foreach (var pair in _punishmentsById)
            {
                var punishment = pair.Value;
                if (punishment.TenantId == tenantId && punishment.AppId == appId && punishment.PlayerId == playerId)
                {
                    result.Add(punishment.Copy());
                }
            }
        }

        return Task.FromResult<IReadOnlyList<OnlinePunishment>>(result);
    }

    /// <summary>
    /// 构造方向性记录索引键（作用域 + 归属玩家 + 目标玩家）。
    /// </summary>
    /// <param name="prefix">记录类别前缀（屏蔽 / 静音）。</param>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="ownerId">归属玩家。</param>
    /// <param name="targetPlayerId">目标玩家。</param>
    /// <returns>索引键。</returns>
    private static string BuildDirectionKey(string prefix, long tenantId, long appId, long ownerId, long targetPlayerId)
    {
        return prefix + ":" + tenantId + ":" + appId + ":" + ownerId + ":" + targetPlayerId;
    }

    /// <summary>
    /// 构造处罚索引键（作用域 + 处罚标识）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="punishmentId">处罚标识。</param>
    /// <returns>索引键。</returns>
    private static string BuildPunishmentKey(long tenantId, long appId, string punishmentId)
    {
        return "pun:" + tenantId + ":" + appId + ":" + (punishmentId ?? string.Empty);
    }
}
