// ==========================================================================================
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   This project is licensed solely under the Apache License 2.0,
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//  ==========================================================================================


namespace GameFrameX.NetWork.RemoteMessaging.Routing;

/// <summary>
/// 玩家路由版本号过期异常（C143e D21 顶号竞态）。
/// </summary>
/// <remarks>
/// Thrown when the persisted route document already carries a newer
/// <see cref="PlayerRouteDocument.Version"/> than the value supplied by
/// <see cref="MongoPlayerRouteSyncTarget.UpsertAsync"/>. The CAS loss means a
/// concurrent session has already pushed a higher version (typical scenario:
/// player A logs in on instance X, then immediately logs in on instance Y;
/// the X-side upsert races and arrives second, so X's "version+1" is stale).
/// Callers must re-read the document and decide whether to retry or drop the
/// local PlayerRouteMap entry — never silently succeed.
/// </remarks>
public sealed class PlayerRouteStaleException : Exception
{
    /// <summary>
    /// 玩家 ID。
    /// </summary>
    /// <remarks>
    /// The player id whose upsert lost the CAS race.
    /// </remarks>
    public long PlayerId { get; }

    /// <summary>
    /// 调用方送入的 version（已落后于持久化值）。
    /// </summary>
    /// <remarks>
    /// The version supplied by the caller; already behind the persisted value.
    /// </remarks>
    public long SuppliedVersion { get; }

    /// <summary>
    /// 控制库中的最新 version。
    /// </summary>
    /// <remarks>
    /// The latest version already persisted in the control database.
    /// </remarks>
    public long CurrentVersion { get; }

    /// <summary>
    /// 构造顶号竞态异常。
    /// </summary>
    /// <remarks>
    /// Builds the CAS-loss exception with the player id and the supplied/current versions.
    /// </remarks>
    /// <param name="playerId">玩家 ID / Player id</param>
    /// <param name="suppliedVersion">送入的 version / Supplied version</param>
    /// <param name="currentVersion">控制库最新 version / Latest persisted version</param>
    public PlayerRouteStaleException(long playerId, long suppliedVersion, long currentVersion)
        : base($"Player route version for player {playerId} is stale: supplied={suppliedVersion}, current={currentVersion}.")
    {
        PlayerId = playerId;
        SuppliedVersion = suppliedVersion;
        CurrentVersion = currentVersion;
    }
}
