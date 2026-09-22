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

    /// <inheritdoc />
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