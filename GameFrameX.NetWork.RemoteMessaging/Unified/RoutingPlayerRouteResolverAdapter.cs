// ==========================================================================================
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   This project is licensed solely under the Apache License 2.0,
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//  ==========================================================================================


using GameFrameX.NetWork.RemoteMessaging.Routing;

namespace GameFrameX.NetWork.RemoteMessaging.Unified;

/// <summary>
/// Routing 侧玩家路由解析器到 Unified 消息契约的适配器（C152 装配接线）。
/// </summary>
/// <remarks>
/// Adapts the C143e <see cref="Routing.IPlayerRouteResolver"/> (the control-database
/// player-route read side, exposed through <c>MongoPlayerRouteResolverBootstrap.Resolver</c>)
/// to the Unified-messaging resolver contract consumed by
/// <see cref="UnifiedMessageSenderHolder"/>. The two hierarchies evolved separately
/// (Unified: pre-C143e sender stack; Routing: C143e <c>player_route</c> read side);
/// this adapter maps <see cref="Routing.PlayerRouteInfo"/> field-by-field instead of
/// forcing a breaking interface merge. Offline-state convention differs slightly
/// (Routing normalizes <c>Version</c> to 1, Unified leaves 0); mapping goes through
/// each side's factories so both conventions stay intact.
/// </remarks>
public sealed class RoutingPlayerRouteResolverAdapter : IPlayerRouteResolver
{
    /// <summary>
    /// 被适配的 Routing 侧解析器。
    /// </summary>
    /// <remarks>
    /// The wrapped Routing-side resolver.
    /// </remarks>
    private readonly Routing.IPlayerRouteResolver _innerResolver;

    /// <summary>
    /// 初始化适配器。
    /// </summary>
    /// <remarks>
    /// Initializes the adapter over the Routing-side resolver.
    /// </remarks>
    /// <param name="innerResolver">Routing 侧解析器（控制库读侧实现）/ The Routing-side resolver (the control-database read side)</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="innerResolver"/> 为 null 时抛出 / Thrown when innerResolver is null</exception>
    public RoutingPlayerRouteResolverAdapter(Routing.IPlayerRouteResolver innerResolver)
    {
        ArgumentNullException.ThrowIfNull(innerResolver, nameof(innerResolver));
        _innerResolver = innerResolver;
    }

    /// <summary>
    /// 解析玩家路由信息。委托内部 Routing 侧解析器查询，并将结果逐字段映射为 Unified 契约：
    /// 在线路由转换为 <see cref="PlayerRouteInfo.Online"/>，离线路由转换为 <see cref="PlayerRouteInfo.Offline"/>，路由缺失时返回 null。
    /// </summary>
    /// <remarks>
    /// Resolves player route information. Delegates to the wrapped Routing-side resolver and maps the result field-by-field into the Unified contract:
    /// online routes become <see cref="PlayerRouteInfo.Online"/>, offline routes become <see cref="PlayerRouteInfo.Offline"/>, and a missing route returns null.
    /// </remarks>
    /// <param name="playerId">玩家ID / Player ID</param>
    /// <returns>Unified 路由信息，null 表示路由缺失 / Unified route information, null indicates route missing</returns>
    public async Task<PlayerRouteInfo> ResolveAsync(long playerId)
    {
        var route = await _innerResolver.ResolveAsync(playerId).ConfigureAwait(false);
        if (route == null)
        {
            return null;
        }

        if (route.IsOnline)
        {
            return PlayerRouteInfo.Online(route.ServerType, route.ServerId, route.Version);
        }

        return PlayerRouteInfo.Offline();
    }
}
