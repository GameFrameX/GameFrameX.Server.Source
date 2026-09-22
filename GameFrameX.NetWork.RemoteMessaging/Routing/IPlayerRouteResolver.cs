// ==========================================================================================
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   This project is licensed solely under the Apache License 2.0,
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   ==========================================================================================


namespace GameFrameX.NetWork.RemoteMessaging.Routing;

/// <summary>
/// 玩家路由解析抽象（C143e D21）。
/// </summary>
/// <remarks>
/// The player-route resolver contract (C143e D21). Implementations resolve a
/// player's current logical location (which <c>role</c> + numeric <c>serverId</c>
/// holds them, or that they are offline) by looking through some set of tiers —
/// typically in-process memory first, then a shared control store. The default
/// Mongo-backed implementation is <see cref="MongoPlayerRouteResolver"/>; the
/// bootstrap swaps it in once the control database is registered. Returning a
/// cached or negative answer is allowed: the caller treats
/// <see cref="PlayerRouteInfo.IsOnline"/> as the binding signal.
/// </remarks>
public interface IPlayerRouteResolver
{
    /// <summary>
    /// 解析玩家当前路由位置。
    /// </summary>
    /// <remarks>
    /// Resolves the player's current routing location.
    /// </remarks>
    /// <param name="playerId">玩家 ID / The player id</param>
    /// <returns>玩家路由信息 — 在线时含 role + serverId；离线时仅 IsOnline=false / The route info — online carries role + serverId; offline returns IsOnline=false</returns>
    Task<PlayerRouteInfo> ResolveAsync(long playerId);
}