// ==========================================================================================
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   This project is licensed solely under the Apache License 2.0,
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//  ==========================================================================================


using MongoDB.Driver;

namespace GameFrameX.NetWork.RemoteMessaging.Routing;

/// <summary>
/// 玩家路由层装配点（C143e：建索引 + 装 SyncTarget + 暴露 resolver）。
/// </summary>
/// <remarks>
/// The player-route layer wiring point (C143e). The launch flow calls
/// <see cref="Attach"/> once after the control database is registered: it
/// creates the <c>player_route</c> indexes (idempotent), installs the
/// <see cref="MongoPlayerRouteSyncTarget"/> as the SessionManager sync hook,
/// and exposes the <see cref="MongoPlayerRouteResolver"/> through
/// <see cref="Resolver"/> so the launch flow (or a test) can rewire
/// <see cref="Unified.UnifiedMessageSenderHolder"/> to use it in place of the
/// default Hotfix <c>DefaultPlayerRouteResolver</c>. Idempotent: only the
/// first call wires anything; later calls are no-ops (so per-role startups in
/// a multi-role process never double-build indexes).
/// </remarks>
public static class MongoPlayerRouteResolverBootstrap
{
    private static int _attached;
    private static MongoPlayerRouteResolver _resolver;
    private static MongoPlayerRouteSyncTarget _syncTarget;

    /// <summary>
    /// 已装配的玩家路由解析器（Attach 之前为 null）。
    /// </summary>
    /// <remarks>
    /// The wired resolver (null before <see cref="Attach"/>).
    /// </remarks>
    public static MongoPlayerRouteResolver Resolver
    {
        get { return _resolver; }
    }

    /// <summary>
    /// 已装配的同步目标（Attach 之前为 null）。
    /// </summary>
    /// <remarks>
    /// The wired sync target (null before <see cref="Attach"/>).
    /// </remarks>
    public static MongoPlayerRouteSyncTarget SyncTarget
    {
        get { return _syncTarget; }
    }

    /// <summary>
    /// 激活玩家路由层：建索引 + 装 SyncTarget。
    /// </summary>
    /// <remarks>
    /// Activates the player-route layer. Builds the indexes (idempotent),
    /// constructs the resolver and sync target, and keeps references so the
    /// launch flow can wire <see cref="SyncTarget"/> into SessionManager and
    /// <see cref="Resolver"/> into <c>UnifiedMessageSenderHolder</c>. The
    /// bootstrap itself does not touch the host application layer — the
    /// caller is responsible for the cross-package wiring.
    /// </remarks>
    /// <param name="controlDatabase">控制库（gameframex_control）/ The control database</param>
    /// <param name="fastPath">Tier 1 快路径提供方（null 跳过 Tier 1）/ Tier 1 fast path (null skips Tier 1)</param>
    /// <returns>是否为本进程首次装配（false 表示已激活，本次调用为 no-op） / true on first attach, false on subsequent calls</returns>
    public static async Task<bool> Attach(IMongoDatabase controlDatabase, IPlayerRouteFastPath fastPath = null)
    {
        ArgumentNullException.ThrowIfNull(controlDatabase, nameof(controlDatabase));

        if (Interlocked.CompareExchange(ref _attached, 1, 0) != 0)
        {
            return false;
        }

        var collection = controlDatabase.GetCollection<PlayerRouteDocument>(PlayerRouteCollection.CollectionName);
        await PlayerRouteCollection.EnsureIndexesAsync(collection).ConfigureAwait(false);

        _resolver = new MongoPlayerRouteResolver(controlDatabase, fastPath);
        _syncTarget = new MongoPlayerRouteSyncTarget(controlDatabase);

        return true;
    }
}
