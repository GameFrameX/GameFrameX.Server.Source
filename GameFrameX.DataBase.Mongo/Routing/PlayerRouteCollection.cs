// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规及开源许可证之规定。
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
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/gameframex
//   Gitee Repository:  https://gitee.com/gameframex
//   CNB  仓库：https://cnb.cool/gameframex
//   CNB Repository: https://cnb.cool/gameframex
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using GameFrameX.Discovery.Routing;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;


namespace GameFrameX.DataBase.Mongo.Routing;

/// <summary>
/// 跨服玩家路由控制文档；属性形态继承自 <see cref="PlayerRouteEntity"/>。
/// </summary>
/// <remarks>
/// The cross-server player route document in the control database.
/// Properties are declared once on the shared <see cref="PlayerRouteEntity"/> base;
/// the BSON wire mapping (camelCase elements) lives in
/// <see cref="MongoDiscoverySerialization"/> and is byte-identical to the former
/// attribute form. One document per player; the unique <see cref="PlayerRouteEntity.PlayerId"/>
/// index is the lookup key, and the TTL on <see cref="PlayerRouteEntity.LastSeenAt"/> keeps
/// long-offline players from accumulating forever. <see cref="PlayerRouteEntity.Version"/> is
/// the CAS counter for the "踢号 + 重登" sequence: a new login must carry
/// <c>oldVersion + 1</c>, otherwise <see>
///     <cref>MongoPlayerRouteSyncTarget</cref>
/// </see>
/// refuses
/// the upsert and throws <see cref="PlayerRouteStaleException"/>.
/// </remarks>
[BsonIgnoreExtraElements]
public sealed class PlayerRouteDocument : PlayerRouteEntity
{
}

/// <summary>
/// player_route 集合契约（全名约定 + 索引工具）。
/// </summary>
/// <remarks>
/// The player_route collection contract (the no-abbreviation naming rule plus the
/// index bootstrap utility). The unique index on <c>playerId</c> is what makes
/// upsert idempotent; the TTL index on <c>lastSeenAt</c> bounds the offline
/// garbage window to 30 days so the collection never grows unbounded for
/// churned players.
/// </remarks>
public static class PlayerRouteCollection
{

    /// <summary>
    /// playerId 唯一索引名（统一命名规则：{element}_unique）。
    /// </summary>
    /// <remarks>
    /// The unique-index name on playerId (unified naming rule: {element}_unique).
    /// </remarks>
    private static string UniqueIndexName => DiscoveryStorageNaming.UniqueIndexName(DiscoveryStorageNaming.CamelCase(nameof(PlayerRouteEntity.PlayerId)));

    /// <summary>
    /// lastSeenAt TTL 索引名（统一命名规则：{element}_ttl_{window}）。
    /// </summary>
    /// <remarks>
    /// The TTL-index name on lastSeenAt (unified naming rule: {element}_ttl_{window}).
    /// </remarks>
    private static string TtlIndexName => DiscoveryStorageNaming.TtlIndexName(DiscoveryStorageNaming.CamelCase(nameof(PlayerRouteEntity.LastSeenAt)), TimeSpan.FromSeconds(TtlSeconds));

    /// <summary>
    /// 离线路由 TTL（30 天）。
    /// </summary>
    /// <remarks>
    /// The TTL window for an offline route document (30 days).
    /// </remarks>
    public const long TtlSeconds = 30L * 24L * 60L * 60L;

    /// <summary>
    /// 建立 player_route 索引（playerId 唯一 + lastSeenAt TTL）。幂等：已存在则 no-op。
    /// </summary>
    /// <remarks>
    /// Creates the player_route indexes (unique on playerId, TTL on lastSeenAt).
    /// Idempotent: existing indexes are a no-op.
    /// </remarks>
    /// <param name="collection">目标集合 / The target collection</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>异步任务 / Async task</returns>
    public static async Task EnsureIndexesAsync(IMongoCollection<PlayerRouteDocument> collection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collection, nameof(collection));

        var uniqueIndex = new CreateIndexModel<PlayerRouteDocument>(
            Builders<PlayerRouteDocument>.IndexKeys.Ascending(document => document.PlayerId),
            new CreateIndexOptions { Unique = true, Name = UniqueIndexName });

        var ttlIndex = new CreateIndexModel<PlayerRouteDocument>(
            Builders<PlayerRouteDocument>.IndexKeys.Ascending(document => document.LastSeenAt),
            new CreateIndexOptions { ExpireAfter = TimeSpan.FromSeconds(TtlSeconds), Name = TtlIndexName });

        await collection.Indexes.CreateManyAsync(new[] { uniqueIndex, ttlIndex }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 构造"首次出现的 player"文档（version=1）；new SetPlayerRouteOnline(playerId) 默认起点。
    /// </summary>
    /// <remarks>
    /// Builds the first-time player document (version = 1); the default starting point for a fresh SetPlayerRouteOnline.
    /// </remarks>
    /// <param name="playerId">玩家 ID / Player id</param>
    /// <param name="instanceId">实例 ID / Instance id</param>
    /// <param name="role">Role 名 / Role name</param>
    /// <returns>新文档 / The new document</returns>
    public static PlayerRouteDocument CreateOnline(long playerId, string instanceId, string role)
    {
        return new PlayerRouteDocument
        {
            PlayerId = playerId,
            InstanceId = instanceId,
            Role = role,
            Version = 1,
            LastSeenAt = DateTime.UtcNow,
        };
    }
}
