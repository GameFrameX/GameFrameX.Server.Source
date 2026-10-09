// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes and liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository:  https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:   https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:     https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using MongoDB.Driver;

namespace GameFrameX.DataBase.Mongo.Routing;

/// <summary>
/// Mongo 玩家路由存储适配（<see cref="IPlayerRouteStore"/> 的 MQL 实现）。
/// </summary>
/// <remarks>
/// The Mongo implementation of the player-route storage seam: the MQL
/// counterpart consumed by the generic resolver / sync target / bootstrap.
/// Schema bootstrap delegates to <see cref="PlayerRouteCollection.EnsureIndexesAsync"/>
/// (playerId unique + lastSeenAt 30-day TTL); <see cref="DeleteExpiredAsync"/>
/// is therefore a no-op — the server-side TTL index is the backstop.
/// </remarks>
public sealed class MongoPlayerRouteStore : IPlayerRouteStore
{
    /// <summary>
    /// player_route 集合。
    /// </summary>
    /// <remarks>
    /// The player_route collection.
    /// </remarks>
    private readonly IMongoCollection<PlayerRouteDocument> _collection;

    /// <summary>
    /// 初始化 Mongo 玩家路由存储适配。
    /// </summary>
    /// <remarks>
    /// Initializes the store over the control database's
    /// <see cref="PlayerRouteEntity.TableName"/> collection.
    /// </remarks>
    /// <param name="controlDatabase">控制库（gameframex_control）/ The control database</param>
    public MongoPlayerRouteStore(IMongoDatabase controlDatabase)
    {
        ArgumentNullException.ThrowIfNull(controlDatabase, nameof(controlDatabase));
        _collection = controlDatabase.GetCollection<PlayerRouteDocument>(DiscoveryStorageNaming.TableName<PlayerRouteEntity>());
    }

    /// <summary>
    /// 建 player_route 索引（playerId 唯一 + lastSeenAt 30 天 TTL；幂等）。
    /// </summary>
    /// <remarks>
    /// Creates the player_route indexes (playerId unique + lastSeenAt TTL), idempotently.
    /// </remarks>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>异步任务 / Async task</returns>
    public Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        return PlayerRouteCollection.EnsureIndexesAsync(_collection, cancellationToken);
    }

    /// <summary>
    /// 读取单个玩家的路由文档。
    /// </summary>
    /// <remarks>
    /// Reads one player's route document; null when absent.
    /// </remarks>
    /// <param name="playerId">玩家 ID / The player id</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>路由记录；缺失时为 null / The record, or null when absent</returns>
    public async Task<PlayerRouteRecord> GetAsync(long playerId, CancellationToken cancellationToken = default)
    {
        var document = await _collection.Find(Builders<PlayerRouteDocument>.Filter.Eq(candidate => candidate.PlayerId, playerId))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return document == null ? null : ToRecord(document);
    }

    /// <summary>
    /// 以 version 守卫更新路由（playerId + version-1 命中；未命中回读区分 Missing / Stale）。
    /// </summary>
    /// <remarks>
    /// Attempts the guarded update (matching playerId and version =
    /// supplied − 1). On a miss the latest document is re-read to
    /// distinguish <see cref="PlayerRouteCasOutcome.Missing"/> from
    /// <see cref="PlayerRouteCasOutcome.Stale"/>; the branch policy itself
    /// lives in the generic sync target.
    /// </remarks>
    /// <param name="record">待写入的路由记录 / The record to write</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>CAS 结果 / The CAS outcome</returns>
    public async Task<PlayerRouteCasOutcome> CasUpsertAsync(PlayerRouteRecord record, CancellationToken cancellationToken = default)
    {
        var filter = Builders<PlayerRouteDocument>.Filter.And(
            Builders<PlayerRouteDocument>.Filter.Eq(candidate => candidate.PlayerId, record.PlayerId),
            Builders<PlayerRouteDocument>.Filter.Eq(candidate => candidate.Version, record.Version - 1));

        var update = Builders<PlayerRouteDocument>.Update
            .Set(candidate => candidate.InstanceId, record.InstanceId)
            .Set(candidate => candidate.Role, record.Role)
            .Set(candidate => candidate.Version, record.Version)
            .Set(candidate => candidate.LastSeenAt, DateTime.UtcNow);

        var result = await _collection.UpdateOneAsync(filter, update, new UpdateOptions { IsUpsert = false }, cancellationToken).ConfigureAwait(false);
        if (result.MatchedCount > 0)
        {
            return PlayerRouteCasOutcome.Updated;
        }

        var latest = await _collection.Find(Builders<PlayerRouteDocument>.Filter.Eq(candidate => candidate.PlayerId, record.PlayerId))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return latest == null ? PlayerRouteCasOutcome.Missing : PlayerRouteCasOutcome.Stale;
    }

    /// <summary>
    /// 首登无条件写入（ReplaceOne + IsUpsert，并发首登 last-writer-wins）。
    /// </summary>
    /// <remarks>
    /// Writes the first-login document unconditionally (ReplaceOne with
    /// IsUpsert; under concurrent first logins the later writer wins).
    /// </remarks>
    /// <param name="record">首登记录 / The first-login record</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>异步任务 / Async task</returns>
    public Task InsertFirstLoginAsync(PlayerRouteRecord record, CancellationToken cancellationToken = default)
    {
        var firstTime = PlayerRouteCollection.CreateOnline(record.PlayerId, record.InstanceId, record.Role);
        return _collection.ReplaceOneAsync(
            Builders<PlayerRouteDocument>.Filter.Eq(candidate => candidate.PlayerId, record.PlayerId),
            firstTime,
            new ReplaceOptions { IsUpsert = true },
            cancellationToken);
    }

    /// <summary>
    /// 删除该玩家的路由文档（幂等）。
    /// </summary>
    /// <remarks>
    /// Deletes the player's route document (idempotent; a missing document is not an error).
    /// </remarks>
    /// <param name="playerId">玩家 ID / Player id</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>异步任务 / Async task</returns>
    public Task DeleteAsync(long playerId, CancellationToken cancellationToken = default)
    {
        return _collection.DeleteOneAsync(Builders<PlayerRouteDocument>.Filter.Eq(candidate => candidate.PlayerId, playerId), cancellationToken);
    }

    /// <summary>
    /// no-op：Mongo 服务端 lastSeenAt TTL 索引兜底过期清除。
    /// </summary>
    /// <remarks>
    /// No-op: the server-side lastSeenAt TTL index (30 days, created by
    /// <see cref="EnsureSchemaAsync"/>) already removes offline-route documents.
    /// </remarks>
    /// <param name="timeToLive">路由保存窗口（Mongo 路径忽略）/ The expire-after window (ignored on Mongo)</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>已完成的任务 / A completed task</returns>
    public Task DeleteExpiredAsync(TimeSpan timeToLive, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// 文档 → 路由记录。
    /// </summary>
    /// <remarks>
    /// Converts a document to a record.
    /// </remarks>
    private static PlayerRouteRecord ToRecord(PlayerRouteDocument document)
    {
        return new PlayerRouteRecord { PlayerId = document.PlayerId, InstanceId = document.InstanceId, Role = document.Role, Version = document.Version };
    }
}
