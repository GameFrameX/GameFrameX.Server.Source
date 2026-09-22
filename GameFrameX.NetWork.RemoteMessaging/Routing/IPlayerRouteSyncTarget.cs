// ==========================================================================================
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   This project is licensed solely under the Apache License 2.0,
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   Any legal disputes and liabilities arising from secondary development based on this project
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//  ==========================================================================================


namespace GameFrameX.NetWork.RemoteMessaging.Routing;

/// <summary>
/// 玩家路由外发同步目标（C143e D21：SessionManager 钩子的可注入端）。
/// </summary>
/// <remarks>
/// The outbound sync target for the SessionManager player-route hooks (C143e D21).
/// The hook is async-by-design so the Mongo implementation can <c>await</c>
/// the CAS upsert; the NoOp default returns immediately. Callers (SessionManager)
/// must catch and swallow exceptions themselves — the inline hook contract is
/// "best effort, never throws". This interface deliberately exposes primitive
/// fields instead of a struct/DTO to keep GameFrameX.Apps free of Mongo types
/// (the SyncTarget type lives in RemoteMessaging, which Apps already references).
/// </remarks>
public interface IPlayerRouteSyncTarget
{
    /// <summary>
    /// 把 (playerId, instanceId, role, version) 原子写入控制库 player_route（version CAS）。
    /// </summary>
    /// <remarks>
    /// Atomically writes the (playerId, instanceId, role, version) tuple into the
    /// control-database <c>player_route</c> collection using <c>version</c> as the
    /// CAS key. The Mongo implementation throws <see cref="PlayerRouteStaleException"/>
    /// when the persisted version is already ahead of the supplied value.
    /// </remarks>
    /// <param name="playerId">玩家 ID / Player id</param>
    /// <param name="instanceId">实例 ID / Instance id</param>
    /// <param name="role">Role 名 / Role name</param>
    /// <param name="version">顶号版本号 / Kick/relogin version</param>
    /// <returns>异步任务 / Async task</returns>
    Task UpsertAsync(long playerId, string instanceId, string role, long version);

    /// <summary>
    /// 从控制库 player_route 删除该玩家路由。
    /// </summary>
    /// <remarks>
    /// Removes the player's route document from the control-database
    /// <c>player_route</c> collection. Missing documents are silently ignored
    /// (idempotent semantics; a stale cleanup is harmless).
    /// </remarks>
    /// <param name="playerId">玩家 ID / Player id</param>
    /// <returns>异步任务 / Async task</returns>
    Task DeleteAsync(long playerId);
}
