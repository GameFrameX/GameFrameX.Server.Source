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

    /// <summary>
    /// NoOp 实现：不写入控制库，直接返回已完成任务。
    /// </summary>
    /// <remarks>
    /// NoOp implementation: writes nothing to any control store and returns an
    /// already-completed task, so the local PlayerRouteMap stays the single
    /// source of truth.
    /// </remarks>
    /// <param name="record">待写入的玩家路由记录（本实现不读取）/ The player-route record (not read by this implementation)</param>
    /// <returns>已完成的任务 / The completed task</returns>
    public Task UpsertAsync(PlayerRouteRecord record)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// NoOp 实现：不删除任何文档，直接返回已完成任务。
    /// </summary>
    /// <remarks>
    /// NoOp implementation: deletes nothing and returns an already-completed
    /// task.
    /// </remarks>
    /// <param name="playerId">玩家 ID / Player id</param>
    /// <returns>已完成的任务 / The completed task</returns>
    public Task DeleteAsync(long playerId)
    {
        return Task.CompletedTask;
    }
}