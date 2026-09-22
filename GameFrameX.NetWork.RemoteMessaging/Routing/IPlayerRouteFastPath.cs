// ==========================================================================================
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   This project is licensed solely under the Apache License 2.0,
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   ==========================================================================================


namespace GameFrameX.NetWork.RemoteMessaging.Routing;

/// <summary>
/// 玩家路由 Tier 1 快路径提供方（C143e D21）。
/// </summary>
/// <remarks>
/// The Tier 1 player-route fast-path provider (C143e D21). The default
/// implementation in <c>GameFrameX.Apps</c> is the in-process
/// <c>SessionManager.PlayerRouteMap</c>; it is injected into
/// <see cref="MongoPlayerRouteResolver"/> at launch time so the resolver does
/// not depend on the host application layer. Returning <c>false</c> (or
/// <paramref name="info"/> with <see cref="PlayerRouteInfo.IsOnline"/> = false)
/// makes the resolver fall through to the Tier 2 control-database lookup.
/// </remarks>
public interface IPlayerRouteFastPath
{
    /// <summary>
    /// 查询玩家是否在本进程在线。
    /// </summary>
    /// <remarks>
    /// Checks whether the player is locally online. Returning false makes
    /// the resolver fall through to Tier 2.
    /// </remarks>
    /// <param name="playerId">玩家 ID / The player id</param>
    /// <param name="info">在线态返回本地缓存的路由信息 / The locally cached route info when online</param>
    /// <returns>是否本地在线 / Whether the player is locally online</returns>
    bool TryGetOnline(long playerId, out PlayerRouteInfo info);
}