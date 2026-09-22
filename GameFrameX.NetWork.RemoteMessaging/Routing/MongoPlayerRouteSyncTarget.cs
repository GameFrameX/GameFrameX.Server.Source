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
/// 基于 Mongo 控制库的 IPlayerRouteSyncTarget（C143e D21 控制库落点）。
/// </summary>
/// <remarks>
/// The control-database-backed implementation of the player-route sync hook
/// (C143e D21). Each call is a single CAS upsert: on first sight the document
/// is inserted with version=1; on subsequent calls the existing version is
/// compared and the upsert is rejected (returning <see cref="PlayerRouteStaleException"/>)
/// when the supplied version has already been overtaken. The session manager
/// catches that exception at the hook boundary and swallows it — the local
/// PlayerRouteMap stays consistent and the next SetOnline converges.
/// </remarks>
public sealed class MongoPlayerRouteSyncTarget : IPlayerRouteSyncTarget
{
    private readonly IMongoCollection<PlayerRouteDocument> _collection;

    /// <summary>
    /// 初始化基于 Mongo 控制库的同步目标。
    /// </summary>
    /// <remarks>
    /// Initializes the Mongo sync target. Indexes are the bootstrap's
    /// responsibility (see <see cref="PlayerRouteCollection.EnsureIndexesAsync"/>);
    /// the SyncTarget only writes.
    /// </remarks>
    /// <param name="controlDatabase">控制库（gameframex_control）/ The control database</param>
    public MongoPlayerRouteSyncTarget(IMongoDatabase controlDatabase)
    {
        ArgumentNullException.ThrowIfNull(controlDatabase, nameof(controlDatabase));
        _collection = controlDatabase.GetCollection<PlayerRouteDocument>(PlayerRouteCollection.CollectionName);
    }

    /// <inheritdoc />
    public async Task UpsertAsync(long playerId, string instanceId, string role, long version)
    {
        if (playerId <= 0)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(instanceId))
        {
            throw new ArgumentException("Instance id must not be empty.", nameof(instanceId));
        }

        // 首登（version=1）走无条件 upsert；后续顶号走 CAS：当前 version 必须等于送入 version，否则抛 PlayerRouteStaleException。
        if (version <= 1)
        {
            var firstTime = PlayerRouteCollection.CreateOnline(playerId, instanceId, role);
            await _collection.ReplaceOneAsync(
                Builders<PlayerRouteDocument>.Filter.Eq(candidate => candidate.PlayerId, playerId),
                firstTime,
                new ReplaceOptions { IsUpsert = true }).ConfigureAwait(false);
            return;
        }

        var filter = Builders<PlayerRouteDocument>.Filter.And(
            Builders<PlayerRouteDocument>.Filter.Eq(candidate => candidate.PlayerId, playerId),
            Builders<PlayerRouteDocument>.Filter.Eq(candidate => candidate.Version, version));

        var update = Builders<PlayerRouteDocument>.Update
            .Set(candidate => candidate.InstanceId, instanceId)
            .Set(candidate => candidate.Role, role)
            .Set(candidate => candidate.Version, version)
            .Set(candidate => candidate.LastSeenAt, DateTime.UtcNow);

        var result = await _collection.UpdateOneAsync(filter, update, new UpdateOptions { IsUpsert = false }).ConfigureAwait(false);

        if (result.MatchedCount == 0)
        {
            // 未命中：可能文档被删，可能 version 已经更新。先读最新 version 区分两种情况。
            var latest = await _collection.Find(Builders<PlayerRouteDocument>.Filter.Eq(candidate => candidate.PlayerId, playerId))
                .FirstOrDefaultAsync()
                .ConfigureAwait(false);

            if (latest == null)
            {
                // 文档缺失：让 SessionManager 钩子在下一轮 SetOnline 重试即可（不必抛）。
                return;
            }

            throw new PlayerRouteStaleException(playerId, version, latest.Version);
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(long playerId)
    {
        if (playerId <= 0)
        {
            return;
        }

        await _collection.DeleteOneAsync(Builders<PlayerRouteDocument>.Filter.Eq(candidate => candidate.PlayerId, playerId)).ConfigureAwait(false);
    }
}
