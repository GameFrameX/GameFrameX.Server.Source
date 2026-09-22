// ==========================================================================================
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   This project is licensed solely under the Apache License 2.0,
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   ==========================================================================================


namespace GameFrameX.NetWork.RemoteMessaging.Routing;

/// <summary>
/// 玩家路由同步目标的 NoOp 默认实现（C143e D21）。
/// </summary>
/// <remarks>
/// The default NoOp <see cref="IPlayerRouteSyncTarget"/>. When the launch flow
/// has not wired a real sync target (e.g. a single-process local test) the
/// SessionManager hooks still execute, but produce no cross-process side
/// effects — the local PlayerRouteMap remains the single source of truth.
/// </remarks>
public sealed class NullPlayerRouteSyncTarget : IPlayerRouteSyncTarget
{
    /// <summary>
    /// 全进程共享的单例。
    /// </summary>
    /// <remarks>
    /// The process-wide shared singleton.
    /// </remarks>
    public static readonly NullPlayerRouteSyncTarget Instance = new NullPlayerRouteSyncTarget();

    private NullPlayerRouteSyncTarget()
    {
    }

    /// <inheritdoc />
    public Task UpsertAsync(long playerId, string instanceId, string role, long version)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteAsync(long playerId)
    {
        return Task.CompletedTask;
    }
}