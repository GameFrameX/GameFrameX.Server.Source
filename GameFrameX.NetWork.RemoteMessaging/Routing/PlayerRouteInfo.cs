// ==========================================================================================
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   This project is licensed solely under the Apache License 2.0,
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   ==========================================================================================


namespace GameFrameX.NetWork.RemoteMessaging.Routing;

/// <summary>
/// 玩家路由解析返回值（C143e D21）。
/// </summary>
/// <remarks>
/// The player-route resolver's value object (C143e D21). When
/// <see cref="IsOnline"/> is true, <see cref="ServerType"/> + <see cref="ServerId"/>
/// describe the target instance; <see cref="Version"/> is the route-version
/// observed at resolve time so callers can detect a routing change that
/// happened between resolve and dispatch (kick/relogin).
/// </remarks>
public sealed class PlayerRouteInfo
{
    /// <summary>
    /// 是否在线。
    /// </summary>
    /// <remarks>
    /// Whether the player is currently online.
    /// </remarks>
    public bool IsOnline { get; set; }

    /// <summary>
    /// 服务类型（在线时填写）。
    /// </summary>
    /// <remarks>
    /// The hosting role name (set when online).
    /// </remarks>
    public string ServerType { get; set; }

    /// <summary>
    /// 服务 ID（在线时填写）。
    /// </summary>
    /// <remarks>
    /// The hosting numeric server id (set when online).
    /// </remarks>
    public int ServerId { get; set; }

    /// <summary>
    /// 路由版本（在线时用最新版本号；离线时用 1）。
    /// </summary>
    /// <remarks>
    /// The route version seen at resolve time (the latest online version when
    /// online; 1 when offline).
    /// </remarks>
    public long Version { get; set; }

    /// <summary>
    /// 构造在线态路由信息。
    /// </summary>
    /// <param name="serverType">服务类型。</param>
    /// <param name="serverId">服务 ID。</param>
    /// <param name="version">版本号。</param>
    /// <returns>在线态路由信息。</returns>
    public static PlayerRouteInfo Online(string serverType, int serverId, long version)
    {
        return new PlayerRouteInfo
        {
            IsOnline = true,
            ServerType = serverType,
            ServerId = serverId,
            Version = version,
        };
    }

    /// <summary>
    /// 构造离线态路由信息。
    /// </summary>
    /// <returns>离线态路由信息。</returns>
    public static PlayerRouteInfo Offline()
    {
        return new PlayerRouteInfo
        {
            IsOnline = false,
            ServerType = null,
            ServerId = 0,
            Version = 1,
        };
    }
}