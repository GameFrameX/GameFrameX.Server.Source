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


using MongoDB.Bson;
using MongoDB.Driver;
using ProtoBuf;

namespace GameFrameX.NetWork.RemoteMessaging.Routing;

/// <summary>
/// 跨服玩家路由控制文档（C143e D21）。
/// </summary>
/// <remarks>
/// The cross-server player route document in the control database (C143e D21).
/// One document per player; the unique <see cref="PlayerId"/> index is the lookup
/// key, and the TTL on <see cref="LastSeenAt"/> keeps long-offline players from
/// accumulating forever. <see cref="Version"/> is the CAS counter for the
/// "踢号 + 重登" sequence: a new login must carry <c>oldVersion + 1</c>, otherwise
/// <see cref="MongoPlayerRouteSyncTarget"/> refuses the upsert and throws
/// <see cref="PlayerRouteStaleException"/>.
/// </remarks>
[ProtoContract]
public sealed class PlayerRouteDocument
{
    /// <summary>
    /// 玩家 ID（业务键）。
    /// </summary>
    /// <remarks>
    /// The player id (the business key).
    /// </remarks>
    [ProtoMember(1)]
    public long PlayerId { get; set; }

    /// <summary>
    /// 玩家当前所在的实例 ID（Mongo 发现层 instanceId）。
    /// </summary>
    /// <remarks>
    /// The player's current owning instance id (the Mongo discovery-layer instanceId).
    /// </remarks>
    [ProtoMember(2)]
    public string InstanceId { get; set; }

    /// <summary>
    /// 玩家当前所在的 Role 名（如 Game / Social）。
    /// </summary>
    /// <remarks>
    /// The player's current owning role name (e.g. Game / Social).
    /// </remarks>
    [ProtoMember(3)]
    public string Role { get; set; }

    /// <summary>
    /// 顶号单调递增版本号（CAS 字段）。
    /// </summary>
    /// <remarks>
    /// The monotonic kick/relogin version used for compare-and-set on upsert.
    /// </remarks>
    [ProtoMember(4)]
    public long Version { get; set; }

    /// <summary>
    /// 最近一次写入时间（TTL 索引依据）。
    /// </summary>
    /// <remarks>
    /// The last write timestamp; the TTL index expires documents 30 days after this point.
    /// </remarks>
    [ProtoMember(5)]
    public DateTime LastSeenAt { get; set; }
}

/// <summary>
/// player_route 集合契约（D18 全名约定 + 索引工具）。
/// </summary>
/// <remarks>
/// The player_route collection contract (D18 no-abbreviation rule plus the
/// index bootstrap utility). The unique index on <c>playerId</c> is what makes
/// upsert idempotent; the TTL index on <c>lastSeenAt</c> bounds the offline
/// garbage window to 30 days so the collection never grows unbounded for
/// churned players.
/// </remarks>
public static class PlayerRouteCollection
{
    /// <summary>
    /// 集合名（控制库 gameframex_control 内的子集合，D18 全名约定）。
    /// </summary>
    /// <remarks>
    /// The collection name (a child collection inside the control database
    /// <c>gameframex_control</c>; the D18 no-abbreviation rule keeps the long
    /// name even though <c>player_routes</c> would also be valid English).
    /// </remarks>
    public const string CollectionName = "player_route";

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
            new CreateIndexOptions { Unique = true, Name = "playerId_unique" });

        var ttlIndex = new CreateIndexModel<PlayerRouteDocument>(
            Builders<PlayerRouteDocument>.IndexKeys.Ascending(document => document.LastSeenAt),
            new CreateIndexOptions { ExpireAfter = TimeSpan.FromSeconds(TtlSeconds), Name = "lastSeenAt_ttl_30d" });

        await collection.Indexes.CreateManyAsync(new[] { uniqueIndex, ttlIndex }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// BSON 形态读取的 playerId 字段名（用于 CAS 过滤器与日志）。
    /// </summary>
    /// <remarks>
    /// The BSON-side playerId field name (used for CAS filters and log lines).
    /// </remarks>
    public const string PlayerIdField = "playerId";

    /// <summary>
    /// BSON 形态读取的 version 字段名。
    /// </summary>
    /// <remarks>
    /// The BSON-side version field name.
    /// </remarks>
    public const string VersionField = "version";

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

    /// <summary>
    /// 把 <see cref="PlayerRouteDocument"/> 转 BSON 字典（写日志/调试用）。
    /// </summary>
    /// <remarks>
    /// Renders the document as a BSON dictionary for log/inspection use.
    /// </remarks>
    /// <param name="document">文档 / The document</param>
    /// <returns>BSON 表示 / The BSON representation</returns>
    public static BsonDocument ToBson(PlayerRouteDocument document)
    {
        ArgumentNullException.ThrowIfNull(document, nameof(document));
        return new BsonDocument
        {
            { PlayerIdField, document.PlayerId },
            { "instanceId", document.InstanceId ?? string.Empty },
            { "role", document.Role ?? string.Empty },
            { VersionField, document.Version },
            { "lastSeenAt", document.LastSeenAt },
        };
    }
}
