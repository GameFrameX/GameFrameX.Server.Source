// ==========================================================================================
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   This project is licensed solely under the Apache License 2.0,
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   ==========================================================================================


using GameFrameX.NetWork.RemoteMessaging.Routing;

namespace GameFrameX.Apps.Common.Session;

/// <summary>
/// SessionManager Tier 1 快路径适配器（C143e D21）。
/// </summary>
/// <remarks>
/// The Tier 1 player-route fast-path adapter (C143e D21). Wraps
/// <c>SessionManager.PlayerRouteMap</c> in the <see cref="IPlayerRouteFastPath"/>
/// contract so <see cref="MongoPlayerRouteResolver"/> can be activated from
/// inside <c>GameFrameX.NetWork.RemoteMessaging</c> without taking a reverse
/// project reference on <c>GameFrameX.Apps</c>.
/// </remarks>
public sealed class SessionManagerFastPathAdapter : IPlayerRouteFastPath
{
    public static readonly SessionManagerFastPathAdapter Instance = new SessionManagerFastPathAdapter();

    private SessionManagerFastPathAdapter()
    {
    }

    /// <summary>
    /// 查询玩家是否在本进程在线。命中 <c>SessionManager.PlayerRouteMap</c> 且快照在线时包装为在线路由信息返回 true；玩家 ID 非法、未命中或离线时返回离线路由信息与 false，使解析器落入 Tier 2 查询。
    /// </summary>
    /// <remarks>
    /// Checks whether the player is online in the current process. When the SessionManager.PlayerRouteMap entry exists and the snapshot is online, wraps it into an online route info and returns true; returns offline route info and false when the player id is invalid, missing, or offline, so the resolver falls through to the Tier 2 lookup.
    /// </remarks>
    /// <param name="playerId">玩家 ID / The player id</param>
    /// <param name="info">在线态返回本地缓存的路由信息，离线态为离线占位信息 / The locally cached route info when online, or an offline placeholder when offline</param>
    /// <returns>是否本地在线 / Whether the player is locally online</returns>
    public bool TryGetOnline(long playerId, out PlayerRouteInfo info)
    {
        if (playerId > 0 && SessionManager.TryGetPlayerRoute(playerId, out var snapshot) && snapshot.IsOnline)
        {
            info = PlayerRouteInfo.Online(snapshot.ServerType, snapshot.ServerId, snapshot.Version);
            return true;
        }

        info = PlayerRouteInfo.Offline();
        return false;
    }
}