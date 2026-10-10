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
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes and liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository:  https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:   https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:     https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.Collections.Concurrent;
using GameFrameX.Utility.Setting;

namespace GameFrameX.Discovery.Routing;

/// <summary>
/// 通用三级玩家路由解析器（自 Mongo / PG 平行实现归一，消费 <see cref="IPlayerRouteStore"/>）。
/// </summary>
/// <remarks>
/// The generic three-tier player route resolver (unified from the Mongo /
/// PostgreSQL parallel implementations; consumes <see cref="IPlayerRouteStore"/>
/// and never branches on the backend). Tier 1 is the in-process fast path
/// (skipped when not injected; a negative answer falls through). Tier 2 reads
/// the control-database <c>player_route</c> table through a 30-second
/// per-player cache so cross-process hot players do not re-query on every
/// message; negative tier-2 answers are cached as well (the 30 s TTL bounds
/// the retry pressure). Tier 3 is the offline fallback.
/// </remarks>
public sealed class PlayerRouteResolver : IPlayerRouteResolver
{
    /// <summary>
    /// Tier 2 控制库缓存 TTL（30s）。
    /// </summary>
    /// <remarks>
    /// The tier-2 control-database cache TTL (30 s).
    /// </remarks>
    private const int ControlCacheTtlMs = 30_000;

    /// <summary>
    /// 玩家路由存储适配。
    /// </summary>
    /// <remarks>
    /// The player-route storage seam.
    /// </remarks>
    private readonly IPlayerRouteStore _store;

    /// <summary>
    /// Tier 1 快路径提供方（null 跳过 Tier 1）。
    /// </summary>
    /// <remarks>
    /// The tier-1 fast path (null skips tier 1).
    /// </remarks>
    private readonly IPlayerRouteFastPath _fastPath;

    /// <summary>
    /// Tier 2 每玩家缓存。
    /// </summary>
    /// <remarks>
    /// The per-player tier-2 cache.
    /// </remarks>
    private readonly ConcurrentDictionary<long, ControlCacheEntry> _controlCache = new ConcurrentDictionary<long, ControlCacheEntry>();

    /// <summary>
    /// 初始化三级玩家路由解析器。
    /// </summary>
    /// <remarks>
    /// Initializes the three-tier resolver. Schema creation is the bootstrap's
    /// responsibility; the resolver only reads. <paramref name="fastPath"/> is
    /// the in-process Tier 1 lookup; when null the resolver skips Tier 1 and
    /// goes straight to the control database.
    /// </remarks>
    /// <param name="store">玩家路由存储适配 / The player-route storage seam</param>
    /// <param name="fastPath">Tier 1 快路径提供方（null 跳过 Tier 1）/ Tier 1 fast path (null skips Tier 1)</param>
    public PlayerRouteResolver(IPlayerRouteStore store, IPlayerRouteFastPath fastPath = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
        _fastPath = fastPath;
    }

    /// <summary>
    /// 按三级策略解析玩家当前路由位置。
    /// </summary>
    /// <remarks>
    /// Resolves the player's current routing location through the three tiers:
    /// non-positive ids return <see cref="PlayerRouteInfo.Offline"/> immediately;
    /// Tier 1 consults the injected fast path (skipped when not injected, and a
    /// negative answer falls through); Tier 2 reads the control-database
    /// <c>player_route</c> table through the 30-second per-player cache; a
    /// miss anywhere falls through to the Tier 3 offline answer.
    /// </remarks>
    /// <param name="playerId">玩家 ID / The player id</param>
    /// <returns>在线时含 role + serverId 的路由信息，否则离线标记 / The online route info carrying role + serverId, or the offline marker</returns>
    public async Task<PlayerRouteInfo> ResolveAsync(long playerId)
    {
        if (playerId <= 0)
        {
            return PlayerRouteInfo.Offline();
        }

        // Tier 1：进程内快路径（注入的 fast-path 提供方；未注入则跳过）。
        if (_fastPath != null && _fastPath.TryGetOnline(playerId, out var localInfo) && localInfo.IsOnline)
        {
            return localInfo;
        }

        // Tier 2：控制库 player_route + 30s TTL 缓存。
        var controlResult = await ResolveFromControlAsync(playerId).ConfigureAwait(false);
        if (controlResult != null)
        {
            return controlResult;
        }

        // Tier 3：离线。
        return PlayerRouteInfo.Offline();
    }

    /// <summary>
    /// Tier 2 读取（30s 每玩家缓存，含 negative answer 缓存）。
    /// </summary>
    /// <remarks>
    /// The tier-2 read through the 30-second per-player cache (negative answers cached too,
    /// avoiding repeated control-database misses for hot offline players; retried after the TTL).
    /// </remarks>
    /// <param name="playerId">玩家 ID / The player id</param>
    /// <returns>路由信息 / The route info</returns>
    private async Task<PlayerRouteInfo> ResolveFromControlAsync(long playerId)
    {
        var now = Environment.TickCount64;
        if (_controlCache.TryGetValue(playerId, out var cached) && now - cached.SnapshotTicks <= ControlCacheTtlMs)
        {
            return cached.Info;
        }

        var record = await _store.GetAsync(playerId).ConfigureAwait(false);

        PlayerRouteInfo info;
        if (record == null)
        {
            // 缓存 negative answer 也吃下：避免热玩家的反复控制库 miss 抖动。30s 后重试一次。
            info = PlayerRouteInfo.Offline();
        }
        else
        {
            var serverType = string.IsNullOrEmpty(record.Role) ? GlobalSettings.CurrentSetting?.ServerType ?? GameServerConst.Game.Name : record.Role;
            info = PlayerRouteInfo.Online(serverType, ExtractServerId(record.InstanceId), record.Version);
        }

        _controlCache[playerId] = new ControlCacheEntry(now, info);
        return info;
    }

    /// <summary>
    /// 从 instanceId 尾段提取数字部分作为 ServerId（不可解析时退回 Game.Id）。
    /// </summary>
    /// <remarks>
    /// Extracts the trailing integer suffix from an instance id
    /// (<c>role-...</c> in the instance id format has no integer; the
    /// fallback keeps the resolver non-throwing for legacy instance ids).
    /// </remarks>
    /// <param name="instanceId">实例 ID / The instance id</param>
    /// <returns>ServerId / The server id</returns>
    private static int ExtractServerId(string instanceId)
    {
        if (string.IsNullOrEmpty(instanceId))
        {
            return GameServerConst.Game.Id;
        }

        var separatorIndex = instanceId.LastIndexOf('-');
        var candidate = separatorIndex >= 0 && separatorIndex + 1 < instanceId.Length
                            ? instanceId.Substring(separatorIndex + 1)
                            : instanceId;

        if (int.TryParse(candidate, out var parsed))
        {
            return parsed;
        }

        return GameServerConst.Game.Id;
    }

    /// <summary>
    /// Tier 2 缓存条目（快照时间 + 路由信息）。
    /// </summary>
    /// <remarks>
    /// The tier-2 cache entry (snapshot ticks + route info).
    /// </remarks>
    private readonly struct ControlCacheEntry
    {
        /// <summary>
        /// 初始化缓存条目。
        /// </summary>
        /// <remarks>
        /// Initializes the entry.
        /// </remarks>
        /// <param name="snapshotTicks">快照时间（TickCount64）/ The snapshot ticks</param>
        /// <param name="info">路由信息 / The route info</param>
        public ControlCacheEntry(long snapshotTicks, PlayerRouteInfo info)
        {
            SnapshotTicks = snapshotTicks;
            Info = info;
        }

        /// <summary>快照时间 / The snapshot ticks</summary>
        public long SnapshotTicks { get; }

        /// <summary>路由信息 / The route info</summary>
        public PlayerRouteInfo Info { get; }
    }
}
