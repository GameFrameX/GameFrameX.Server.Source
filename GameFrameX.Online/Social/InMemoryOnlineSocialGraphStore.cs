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

    /// <summary>
    /// 以（归属玩家，被屏蔽玩家）方向键在内存屏蔽表中「不存在则创建」屏蔽记录：键已存在时返回既有记录且不写入，存档与返回值均为防御性副本。
    /// </summary>
    /// <remarks>
    /// Creates the block record in the in-memory block table if the (owner, blocked player) direction key is absent; returns the existing record without writing when the key already exists, and both the stored record and the return value are defensive copies.
    /// </remarks>
    /// <param name="entry">待创建的屏蔽记录（作用域与方向已由调用方落定） / Block entry to create (scope and direction already settled by the caller)</param>
    /// <param name="cancellationToken">取消令牌（同步内存实现不使用） / Cancellation token (unused by the synchronous in-memory implementation)</param>
    /// <returns>当前生效的屏蔽记录副本（既有记录或刚落库的入参副本） / Copy of the currently effective block entry (the existing record or a copy of the just-stored input)</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="entry"/> 为 null 时抛出 / Thrown when <paramref name="entry"/> is null</exception>
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

    /// <summary>
    /// 按方向键在内存屏蔽表中查找屏蔽记录，命中返回防御性副本，未命中返回 null。
    /// </summary>
    /// <remarks>
    /// Looks up the block record by direction key in the in-memory block table; returns a defensive copy on hit and null on miss.
    /// </remarks>
    /// <param name="tenantId">租户标识 / Tenant identifier</param>
    /// <param name="appId">App 标识 / App identifier</param>
    /// <param name="ownerId">屏蔽发起人 / Block owner</param>
    /// <param name="blockedPlayerId">被屏蔽玩家 / Blocked player</param>
    /// <param name="cancellationToken">取消令牌（同步内存实现不使用） / Cancellation token (unused by the synchronous in-memory implementation)</param>
    /// <returns>屏蔽记录副本；不存在返回 null / Copy of the block entry; null when absent</returns>
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

    /// <summary>
    /// 按方向键从内存屏蔽表中移除屏蔽记录并返回是否实际删除；原本不存在时不报错（幂等）。
    /// </summary>
    /// <remarks>
    /// Removes the block record by direction key from the in-memory block table and reports whether a record was actually deleted; a missing record is not an error (idempotent).
    /// </remarks>
    /// <param name="tenantId">租户标识 / Tenant identifier</param>
    /// <param name="appId">App 标识 / App identifier</param>
    /// <param name="ownerId">屏蔽发起人 / Block owner</param>
    /// <param name="blockedPlayerId">被屏蔽玩家 / Blocked player</param>
    /// <param name="cancellationToken">取消令牌（同步内存实现不使用） / Cancellation token (unused by the synchronous in-memory implementation)</param>
    /// <returns>实际删除了记录返回 true；原本不存在返回 false / true if a record was actually removed; false if it did not exist</returns>
    public Task<bool> RemoveBlockAsync(long tenantId, long appId, long ownerId, long blockedPlayerId, CancellationToken cancellationToken = default)
    {
        var key = BuildDirectionKey("blk", tenantId, appId, ownerId, blockedPlayerId);
        lock (_syncRoot)
        {
            return Task.FromResult(_blocksByDirection.Remove(key));
        }
    }

    /// <summary>
    /// 在同一临界区内检查内存屏蔽表的双方向键，判定两名玩家之间任一方向是否存在屏蔽（裁决的唯一原子判据）。
    /// </summary>
    /// <remarks>
    /// Checks both direction keys of the in-memory block table inside a single critical section to decide whether either player has blocked the other (the sole atomic criterion for adjudication).
    /// </remarks>
    /// <param name="tenantId">租户标识 / Tenant identifier</param>
    /// <param name="appId">App 标识 / App identifier</param>
    /// <param name="leftPlayerId">一侧玩家标识 / One player of the pair</param>
    /// <param name="rightPlayerId">另一侧玩家标识 / The other player of the pair</param>
    /// <param name="cancellationToken">取消令牌（同步内存实现不使用） / Cancellation token (unused by the synchronous in-memory implementation)</param>
    /// <returns>任一方向存在屏蔽返回 true / true if a block exists in either direction</returns>
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

    /// <summary>
    /// 全表线性扫描内存屏蔽表，列出某归属玩家发起的全部屏蔽并返回防御性副本列表。
    /// </summary>
    /// <remarks>
    /// Linearly scans the whole in-memory block table to list every block initiated by the given owner and returns a list of defensive copies.
    /// </remarks>
    /// <param name="tenantId">租户标识 / Tenant identifier</param>
    /// <param name="appId">App 标识 / App identifier</param>
    /// <param name="ownerId">屏蔽发起人 / Block owner</param>
    /// <param name="cancellationToken">取消令牌（同步内存实现不使用） / Cancellation token (unused by the synchronous in-memory implementation)</param>
    /// <returns>屏蔽记录副本列表（无记录时为空列表） / List of block entry copies (empty when none)</returns>
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

    /// <summary>
    /// 以（归属玩家，被静音玩家）方向键在内存静音表中「不存在则创建」静音记录：键已存在时返回既有记录且不写入，存档与返回值均为防御性副本。
    /// </summary>
    /// <remarks>
    /// Creates the mute record in the in-memory mute table if the (owner, muted player) direction key is absent; returns the existing record without writing when the key already exists, and both the stored record and the return value are defensive copies.
    /// </remarks>
    /// <param name="entry">待创建的静音记录 / Mute entry to create</param>
    /// <param name="cancellationToken">取消令牌（同步内存实现不使用） / Cancellation token (unused by the synchronous in-memory implementation)</param>
    /// <returns>当前生效的静音记录副本（既有记录或刚落库的入参副本） / Copy of the currently effective mute entry (the existing record or a copy of the just-stored input)</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="entry"/> 为 null 时抛出 / Thrown when <paramref name="entry"/> is null</exception>
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

    /// <summary>
    /// 按方向键在内存静音表中查找静音记录，命中返回防御性副本，未命中返回 null。
    /// </summary>
    /// <remarks>
    /// Looks up the mute record by direction key in the in-memory mute table; returns a defensive copy on hit and null on miss.
    /// </remarks>
    /// <param name="tenantId">租户标识 / Tenant identifier</param>
    /// <param name="appId">App 标识 / App identifier</param>
    /// <param name="ownerId">静音发起人 / Mute owner</param>
    /// <param name="mutedPlayerId">被静音玩家 / Muted player</param>
    /// <param name="cancellationToken">取消令牌（同步内存实现不使用） / Cancellation token (unused by the synchronous in-memory implementation)</param>
    /// <returns>静音记录副本；不存在返回 null / Copy of the mute entry; null when absent</returns>
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

    /// <summary>
    /// 按方向键从内存静音表中移除静音记录并返回是否实际删除；原本不存在时不报错（幂等）。
    /// </summary>
    /// <remarks>
    /// Removes the mute record by direction key from the in-memory mute table and reports whether a record was actually deleted; a missing record is not an error (idempotent).
    /// </remarks>
    /// <param name="tenantId">租户标识 / Tenant identifier</param>
    /// <param name="appId">App 标识 / App identifier</param>
    /// <param name="ownerId">静音发起人 / Mute owner</param>
    /// <param name="mutedPlayerId">被静音玩家 / Muted player</param>
    /// <param name="cancellationToken">取消令牌（同步内存实现不使用） / Cancellation token (unused by the synchronous in-memory implementation)</param>
    /// <returns>实际删除了记录返回 true；原本不存在返回 false / true if a record was actually removed; false if it did not exist</returns>
    public Task<bool> RemoveMuteAsync(long tenantId, long appId, long ownerId, long mutedPlayerId, CancellationToken cancellationToken = default)
    {
        var key = BuildDirectionKey("mut", tenantId, appId, ownerId, mutedPlayerId);
        lock (_syncRoot)
        {
            return Task.FromResult(_mutesByDirection.Remove(key));
        }
    }

    /// <summary>
    /// 全表线性扫描内存静音表，列出某归属玩家发起的全部静音并返回防御性副本列表。
    /// </summary>
    /// <remarks>
    /// Linearly scans the whole in-memory mute table to list every mute initiated by the given owner and returns a list of defensive copies.
    /// </remarks>
    /// <param name="tenantId">租户标识 / Tenant identifier</param>
    /// <param name="appId">App 标识 / App identifier</param>
    /// <param name="ownerId">静音发起人 / Mute owner</param>
    /// <param name="cancellationToken">取消令牌（同步内存实现不使用） / Cancellation token (unused by the synchronous in-memory implementation)</param>
    /// <returns>静音记录副本列表（无记录时为空列表） / List of mute entry copies (empty when none)</returns>
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

    /// <summary>
    /// 以（作用域，处罚标识）为唯一键在内存处罚表中「不存在则创建」处罚记录：键已存在时返回既有记录且不写入，存档与返回值均为防御性副本。
    /// </summary>
    /// <remarks>
    /// Creates the punishment record in the in-memory punishment table if the (scope, punishment identifier) key is absent; returns the existing record without writing when the key already exists, and both the stored record and the return value are defensive copies.
    /// </remarks>
    /// <param name="punishment">待创建的处罚 / Punishment to create</param>
    /// <param name="cancellationToken">取消令牌（同步内存实现不使用） / Cancellation token (unused by the synchronous in-memory implementation)</param>
    /// <returns>当前生效的处罚副本（既有记录或刚落库的入参副本） / Copy of the currently effective punishment (the existing record or a copy of the just-stored input)</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="punishment"/> 为 null 时抛出 / Thrown when <paramref name="punishment"/> is null</exception>
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

    /// <summary>
    /// 按（作用域，处罚标识）键在内存处罚表中查找处罚，命中返回防御性副本，未命中返回 null。
    /// </summary>
    /// <remarks>
    /// Looks up the punishment by (scope, punishment identifier) key in the in-memory punishment table; returns a defensive copy on hit and null on miss.
    /// </remarks>
    /// <param name="tenantId">租户标识 / Tenant identifier</param>
    /// <param name="appId">App 标识 / App identifier</param>
    /// <param name="punishmentId">处罚标识 / Punishment identifier</param>
    /// <param name="cancellationToken">取消令牌（同步内存实现不使用） / Cancellation token (unused by the synchronous in-memory implementation)</param>
    /// <returns>处罚副本；不存在返回 null / Copy of the punishment; null when absent</returns>
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

    /// <summary>
    /// 以 CAS 语义整体替换内存处罚表中的既有处罚：记录不存在或版本号与期望不一致时返回 null 且不留任何写入痕迹，成功时把入参版本号递增一后存为副本并返回。
    /// </summary>
    /// <remarks>
    /// Replaces the existing punishment in the in-memory punishment table with CAS semantics: returns null without leaving any write trace when the record is missing or its revision does not match the expected one; on success bumps the input's revision by one, stores a copy, and returns it.
    /// </remarks>
    /// <param name="punishment">替换后的处罚（其标识与作用域须与既有记录一致） / The replacement punishment (its identifier and scope must match the existing record)</param>
    /// <param name="expectedRevision">期望的当前版本号；与既有记录不匹配即失败 / Expected current revision; a mismatch with the existing record fails the replace</param>
    /// <param name="cancellationToken">取消令牌（同步内存实现不使用） / Cancellation token (unused by the synchronous in-memory implementation)</param>
    /// <returns>更新后的处罚副本；CAS 失败或记录不存在返回 null / Copy of the updated punishment; null on CAS failure or when the record does not exist</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="punishment"/> 为 null 时抛出 / Thrown when <paramref name="punishment"/> is null</exception>
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

    /// <summary>
    /// 在同一临界区内全表扫描内存处罚表，按给定时刻的生效条件（未撤销、已到生效时刻、未失效）过滤出该玩家确实生效的处罚并返回防御性副本列表。
    /// </summary>
    /// <remarks>
    /// Linearly scans the whole in-memory punishment table inside a single critical section, filters the player's punishments that are genuinely in effect at the given moment (not revoked, already effective, not yet expired), and returns a list of defensive copies.
    /// </remarks>
    /// <param name="tenantId">租户标识 / Tenant identifier</param>
    /// <param name="appId">App 标识 / App identifier</param>
    /// <param name="query">生效处罚查询载荷（玩家标识与判定时刻） / Active punishment query payload (player id and judgment moment)</param>
    /// <param name="cancellationToken">取消令牌（同步内存实现不使用） / Cancellation token (unused by the synchronous in-memory implementation)</param>
    /// <returns>生效中的处罚副本列表（已撤销、未到生效时刻、已失效的均不返回） / List of copies of the punishments in effect (revoked, not-yet-effective, and expired ones are excluded)</returns>
    public Task<IReadOnlyList<OnlinePunishment>> ListActivePunishmentsAsync(long tenantId, long appId, ActivePunishmentQuery query, CancellationToken cancellationToken = default)
    {
        var playerId = query.PlayerId;
        var nowUnixMilliseconds = query.NowUnixMilliseconds;
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

    /// <summary>
    /// 全表线性扫描内存处罚表，列出某玩家的全部处罚记录（含已撤销与已失效）并返回防御性副本列表。
    /// </summary>
    /// <remarks>
    /// Linearly scans the whole in-memory punishment table to list every punishment of the given player (including revoked and expired ones) and returns a list of defensive copies.
    /// </remarks>
    /// <param name="tenantId">租户标识 / Tenant identifier</param>
    /// <param name="appId">App 标识 / App identifier</param>
    /// <param name="playerId">被处罚玩家 / Punished player</param>
    /// <param name="cancellationToken">取消令牌（同步内存实现不使用） / Cancellation token (unused by the synchronous in-memory implementation)</param>
    /// <returns>处罚副本列表（无记录时为空列表） / List of punishment copies (empty when none)</returns>
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
