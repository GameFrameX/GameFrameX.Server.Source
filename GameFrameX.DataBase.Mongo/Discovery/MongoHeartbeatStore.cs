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

namespace GameFrameX.DataBase.Mongo.Discovery;

/// <summary>
/// Mongo 心跳存储适配（<see cref="IHeartbeatStore"/> 的 MQL 实现）。
/// </summary>
/// <remarks>
/// The Mongo implementation of the heartbeat storage seam: the MQL
/// counterpart consumed by the generic <see cref="DiscoveryRegistry"/> /
/// <see cref="DiscoveryWatcher"/>. Expiry deletion is client-side:
/// <see cref="DeleteExpiredAsync"/> executes the real delete driven by the
/// registry's cleanup loop (the watcher's three-period staleness check
/// remains the primary liveness signal); schema bootstrap keeps a plain
/// lastHeartbeat index for the sweep and removes the legacy server-side TTL index.
/// </remarks>
public sealed class MongoHeartbeatStore : IHeartbeatStore
{
    /// <summary>
    /// 心跳集合。
    /// </summary>
    /// <remarks>
    /// The heartbeat collection.
    /// </remarks>
    private readonly IMongoCollection<ServerHeartbeatDocument> _collection;

    /// <summary>
    /// 初始化 Mongo 心跳存储适配。
    /// </summary>
    /// <remarks>
    /// Initializes the store over the control database's
    /// <see cref="DiscoveryStorageNaming.TableName{TEntity}"/> collection.
    /// </remarks>
    /// <param name="controlDatabase">控制库（gameframex_control）/ The control database</param>
    public MongoHeartbeatStore(IMongoDatabase controlDatabase)
    {
        ArgumentNullException.ThrowIfNull(controlDatabase, nameof(controlDatabase));
        _collection = controlDatabase.GetCollection<ServerHeartbeatDocument>(DiscoveryStorageNaming.TableName<ServerHeartbeatEntity>());
    }

    /// <summary>
    /// lastHeartbeat 普通索引名（统一命名规则：{element}_idx）。
    /// </summary>
    /// <remarks>
    /// The plain-index name on lastHeartbeat (unified naming rule: {element}_idx).
    /// </remarks>
    private static string PlainIndexName => DiscoveryStorageNaming.PlainIndexName(DiscoveryStorageNaming.CamelCase(nameof(ServerHeartbeatEntity.LastHeartbeat)));

    /// <summary>
    /// 建 lastHeartbeat 普通索引（幂等：lastHeartbeat_idx）并清除遗留 TTL 索引（存量迁移）。
    /// </summary>
    /// <remarks>
    /// Creates the plain lastHeartbeat index (idempotent; serves the cleanup
    /// DELETE's range scan) and drops the legacy server-side TTL index left by
    /// the former server-side-expiry design (idempotent via
    /// <see cref="MongoLegacyTtlIndexes.DropAllAsync{TDocument}"/>).
    /// </remarks>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>异步任务 / Async task</returns>
    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        var indexKeys = Builders<ServerHeartbeatDocument>.IndexKeys.Ascending(document => document.LastHeartbeat);
        var indexOptions = new CreateIndexOptions { Name = PlainIndexName };
        await _collection.Indexes.CreateOneAsync(new CreateIndexModel<ServerHeartbeatDocument>(indexKeys, indexOptions), cancellationToken: cancellationToken).ConfigureAwait(false);
        await MongoLegacyTtlIndexes.DropAllAsync(_collection, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 全文档 upsert 一条心跳（ReplaceOne + IsUpsert，写入时刻打 lastHeartbeat 戳）。
    /// </summary>
    /// <remarks>
    /// Upserts the full heartbeat document (ReplaceOne with IsUpsert; always the
    /// complete latest state, stamped with the current UTC time).
    /// </remarks>
    /// <param name="instance">实例描述符 / The instance descriptor</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>异步任务 / Async task</returns>
    public Task UpsertAsync(InstanceDescriptor instance, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance, nameof(instance));

        var document = new ServerHeartbeatDocument(
            instance.InstanceId,
            instance.Role,
            instance.AdvertiseEndpoint,
            instance.Status.ToString(),
            instance.Load,
            instance.AddressKind.ToString(),
            instance.Incarnation,
            DateTime.UtcNow);
        return _collection.ReplaceOneAsync(
            Builders<ServerHeartbeatDocument>.Filter.Eq(candidate => candidate.InstanceId, instance.InstanceId),
            document,
            new ReplaceOptions { IsUpsert = true },
            cancellationToken);
    }

    /// <summary>
    /// 拉取全部心跳文档（防御性解析：无法解析的文档被静默跳过）。
    /// </summary>
    /// <remarks>
    /// Queries every heartbeat document; documents with unknown enum names or
    /// blank fields (defensive against a different version's writes) are skipped.
    /// </remarks>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>全部可解析的实例描述符 / Every parsable instance descriptor</returns>
    public async Task<IReadOnlyList<InstanceDescriptor>> QueryAllAsync(CancellationToken cancellationToken = default)
    {
        var documents = await _collection.Find(FilterDefinition<ServerHeartbeatDocument>.Empty).ToListAsync(cancellationToken).ConfigureAwait(false);
        var descriptors = new List<InstanceDescriptor>(documents.Count);
        foreach (var document in documents)
        {
            var descriptor = TryToDescriptor(document);
            if (descriptor != null)
            {
                descriptors.Add(descriptor);
            }
        }

        return descriptors;
    }

    /// <summary>
    /// 删除过期心跳文档（lastHeartbeat 早于 now − heartbeatTimeToLive；清理循环驱动）。
    /// </summary>
    /// <remarks>
    /// Deletes heartbeat documents whose <c>lastHeartbeat</c> is older than
    /// <paramref name="heartbeatTimeToLive"/> — the client-side TTL equivalent,
    /// driven by the registry's cleanup loop (same semantics as the PostgreSQL
    /// store; removal relaxed to within one cleanup period, the watcher's
    /// three-period staleness check remains the primary liveness signal).
    /// </remarks>
    /// <param name="heartbeatTimeToLive">心跳保存窗口 / The expire-after window</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>异步任务 / Async task</returns>
    public Task DeleteExpiredAsync(TimeSpan heartbeatTimeToLive, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow - heartbeatTimeToLive;
        return _collection.DeleteManyAsync(document => document.LastHeartbeat < cutoff, cancellationToken);
    }

    /// <summary>
    /// 心跳文档 → 实例描述符（未知枚举名或空白字段返回 null，防御旧版本文档）。
    /// </summary>
    /// <remarks>
    /// Converts a heartbeat document to a descriptor; returns null on an unknown enum
    /// name or blank field (defensive against documents written by a different version).
    /// </remarks>
    /// <param name="document">心跳文档 / The heartbeat document</param>
    /// <returns>实例描述符；无法转换时为 null / The descriptor, or null when unparsable</returns>
    private static InstanceDescriptor TryToDescriptor(ServerHeartbeatDocument document)
    {
        if (string.IsNullOrWhiteSpace(document.InstanceId) || string.IsNullOrWhiteSpace(document.Role) || string.IsNullOrWhiteSpace(document.AdvertiseEndpoint))
        {
            return null;
        }

        if (!Enum.TryParse<InstanceStatus>(document.Status, false, out var status) || !Enum.TryParse<EndpointAddressKind>(document.AddressKind, false, out var addressKind))
        {
            return null;
        }

        return new InstanceDescriptor(document.Role, document.InstanceId, document.AdvertiseEndpoint, status, document.Load, addressKind, document.Incarnation, document.LastHeartbeat);
    }
}
