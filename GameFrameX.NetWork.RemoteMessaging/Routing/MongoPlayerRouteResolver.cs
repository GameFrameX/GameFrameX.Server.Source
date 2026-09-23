// ==========================================================================================
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   This project is licensed solely under the Apache License 2.0,
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//  ==========================================================================================


using System.Collections.Concurrent;
using GameFrameX.NetWork.Abstractions;
using GameFrameX.Utility.Setting;
using MongoDB.Driver;

namespace GameFrameX.NetWork.RemoteMessaging.Routing;

/// <summary>
/// 三级玩家路由解析器（C143e D21：内存快路径 → 控制库 30s TTL 缓存 → 离线）。
/// </summary>
/// <remarks>
/// The three-tier player route resolver (C143e D21). Tier 1 is the in-process
/// <c>SessionManager.PlayerRouteMap</c> fast path — single-process topologies
/// always hit it and never touch Mongo. Tier 2 is the control database
/// <c>player_route</c> collection read with a 30-second per-player cache, so
/// cross-process hot players do not re-query Mongo on every message. Tier 3
/// is the offline fallback (driven by the configured <see cref="Unified.PlayerOfflineStrategy"/>).
/// Tier 1 negative answers still fall through to tier 2 (the in-process map is
/// authoritative for "this process knows", but a stale entry may outlive a
/// real login on another process; the control database is the cross-process
/// tiebreaker).
/// </remarks>
public sealed class MongoPlayerRouteResolver : IPlayerRouteResolver
{
    private const int ControlCacheTtlMs = 30_000;

    private readonly IMongoCollection<PlayerRouteDocument> _collection;
    private readonly IPlayerRouteFastPath _fastPath;
    private readonly ConcurrentDictionary<long, ControlCacheEntry> _controlCache = new ConcurrentDictionary<long, ControlCacheEntry>();

    /// <summary>
    /// 初始化三级玩家路由解析器。
    /// </summary>
    /// <remarks>
    /// Initializes the three-tier resolver. <paramref name="controlDatabase"/>
    /// must already be the registered <c>gameframex_control</c> database; the
    /// resolver does not own index creation (the bootstrap call does it once
    /// at startup). <paramref name="fastPath"/> is the in-process Tier 1 lookup;
    /// when null the resolver skips Tier 1 and goes straight to the control
    /// database (the launch flow in <c>GameFrameX.Apps</c> injects its
    /// <c>PlayerRouteMap</c>-backed adapter).
    /// </remarks>
    /// <param name="controlDatabase">控制库 / The control database</param>
    /// <param name="fastPath">Tier 1 快路径提供方（null 跳过 Tier 1）/ Tier 1 fast path (null skips Tier 1)</param>
    public MongoPlayerRouteResolver(IMongoDatabase controlDatabase, IPlayerRouteFastPath fastPath = null)
    {
        ArgumentNullException.ThrowIfNull(controlDatabase, nameof(controlDatabase));
        _collection = controlDatabase.GetCollection<PlayerRouteDocument>(PlayerRouteCollection.CollectionName);
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
    /// <c>player_route</c> collection through the 30-second per-player cache; a
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

    private async Task<PlayerRouteInfo> ResolveFromControlAsync(long playerId)
    {
        var now = Environment.TickCount64;
        if (_controlCache.TryGetValue(playerId, out var cached) && now - cached.SnapshotTicks <= ControlCacheTtlMs)
        {
            return cached.Info;
        }

        var document = await _collection.Find(Builders<PlayerRouteDocument>.Filter.Eq(candidate => candidate.PlayerId, playerId))
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);

        if (document == null)
        {
            // 缓存 negative answer 也吃下：避免热玩家的反复控制库 miss 抖动。30s 后重试一次。
            _controlCache[playerId] = new ControlCacheEntry(now, PlayerRouteInfo.Offline());
            return PlayerRouteInfo.Offline();
        }

        var serverType = string.IsNullOrEmpty(document.Role) ? (GlobalSettings.CurrentSetting?.ServerType ?? GameServerConst.Game.Name) : document.Role;
        var serverId = ExtractServerId(document.InstanceId);
        var info = PlayerRouteInfo.Online(serverType, serverId, document.Version);

        _controlCache[playerId] = new ControlCacheEntry(now, info);
        return info;
    }

    /// <summary>
    /// 从 instanceId 中提取数字部分作为 ServerId（不可解析时退回 GameConst.Id）。
    /// </summary>
    /// <remarks>
    /// Extracts the trailing integer suffix from an instance id
    /// (<c>role-...</c> in the C143d instance id format has no integer; the
    /// fallback keeps the resolver non-throwing for legacy instance ids).
    /// </remarks>
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

    private readonly struct ControlCacheEntry
    {
        public ControlCacheEntry(long snapshotTicks, PlayerRouteInfo info)
        {
            SnapshotTicks = snapshotTicks;
            Info = info;
        }

        public long SnapshotTicks { get; }
        public PlayerRouteInfo Info { get; }
    }
}
