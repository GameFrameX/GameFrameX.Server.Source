// ==========================================================================================
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   This project is licensed solely under the Apache License 2.0,
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   ==========================================================================================


using GameFrameX.Discovery.Routing;

namespace GameFrameX.Core.Session;

/// <summary>
/// PlayerSessionManager Tier 1 快路径适配器。
/// </summary>
/// <remarks>
/// The Tier 1 player-route fast-path adapter. Wraps the
/// <see cref="PlayerSessionManager"/>-owned player-route snapshot in the <see cref="IPlayerRouteFastPath"/>
/// contract so the Mongo player-route resolver can be activated from
/// inside <c>GameFrameX.NetWork.RemoteMessaging</c> without taking a project
/// reference on <c>GameFrameX.Core</c>（原反向引用 <c>GameFrameX.Apps</c> 的接缝成本随 C192 下沉消除）。
/// </remarks>
public sealed class PlayerSessionFastPathAdapter : IPlayerRouteFastPath
{
    /// <summary>
    /// 适配器单例入口（装配槽位 <c>DiscoveryActivationOptions.PlayerRouteFastPath</c> 按 <see cref="IPlayerRouteFastPath"/> 接口引用）。
    /// </summary>
    public static readonly PlayerSessionFastPathAdapter Instance = new PlayerSessionFastPathAdapter();

    private PlayerSessionFastPathAdapter()
    {
    }

    /// <summary>
    /// 查询玩家是否在本进程在线。命中 <c>PlayerSessionManager.PlayerRouteMap</c> 且快照在线时包装为在线路由信息返回 true；玩家 ID 非法、未命中或离线时返回离线路由信息与 false，使解析器落入 Tier 2 查询。
    /// </summary>
    /// <remarks>
    /// Checks whether the player is online in the current process. When the PlayerSessionManager.PlayerRouteMap entry exists and the snapshot is online, wraps it into an online route info and returns true; returns offline route info and false when the player id is invalid, missing, or offline, so the resolver falls through to the Tier 2 lookup.
    /// </remarks>
    /// <param name="playerId">玩家 ID / The player id</param>
    /// <param name="info">在线态返回本地缓存的路由信息，离线态为离线占位信息 / The locally cached route info when online, or an offline placeholder when offline</param>
    /// <returns>是否本地在线 / Whether the player is locally online</returns>
    public bool TryGetOnline(long playerId, out PlayerRouteInfo info)
    {
        if (playerId > 0 && PlayerSessionManager.Instance.TryGetPlayerRoute(playerId, out var snapshot) && snapshot.IsOnline)
        {
            info = PlayerRouteInfo.Online(snapshot.ServerType, snapshot.ServerId, snapshot.Version);
            return true;
        }

        info = PlayerRouteInfo.Offline();
        return false;
    }
}
