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
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:  https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using System.Collections.Concurrent;
using System.Reflection;
using GameFrameX.DataBase.Abstractions;
using GameFrameX.Foundation.Orm.Attribute;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace GameFrameX.DataBase.Mongo;

/// <summary>
/// MongoDB服务连接类，实现了
/// <see>
///     <cref>IDatabaseService</cref>
/// </see>
/// 接口。
/// </summary>
/// <remarks>
/// MongoDB service connection class that implements the
/// <see>
///     <cref>IDatabaseService</cref>
/// </see>
/// interface.
/// </remarks>
public sealed partial class MongoDbService
{
    private readonly ConcurrentDictionary<string, bool> _indexCache = new();

    /// <summary>
    /// 索引引导的进程级异步单飞锁（冷缓存并发首调串行化：先到者建索引，后到者等待后见缓存即跳过，索引存在性对并发调用者可判）。
    /// </summary>
    /// <remarks>
    /// Process-wide asynchronous single-flight gate for index bootstrap (concurrent cold-cache
    /// first calls are serialized: the first caller builds the index and later callers observe
    /// the cache afterwards, so index existence is deterministically observable for concurrent
    /// callers). Waiters suspend asynchronously instead of blocking request threads while the
    /// bootstrap performs driver I/O.
    /// </remarks>
    private readonly SemaphoreSlim _indexCreationLock = new(1, 1);

    /// <summary>
    /// 检查待创建的索引与已创建的索引是否一致。
    /// </summary>
    /// <remarks>
    /// Checks whether the indexes to be created are consistent with the existing indexes.
    /// </remarks>
    /// <typeparam name="T">文档类型 / Document type</typeparam>
    /// <param name="toBeCreatedIndexes">待创建的索引列表 / List of indexes to be created</param>
    /// <param name="createdIndexes">已创建的索引列表 / List of existing indexes</param>
    /// <returns>如果索引一致则返回 <c>true</c>；否则返回 <c>false</c> / <c>true</c> if indexes are consistent; otherwise <c>false</c></returns>
    private static bool AreIndexesConsistent<T>(List<CreateIndexModel<T>> toBeCreatedIndexes, List<BsonDocument> createdIndexes)
    {
        var targetIndexNames = toBeCreatedIndexes
            .Select(i => i.Options.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet();
        var createdIndexMap = createdIndexes
            .Where(i => i.Contains("name"))
            .ToDictionary(i => i["name"].AsString, i => i);
        foreach (var indexInfo in toBeCreatedIndexes)
        {
            var indexName = indexInfo.Options.Name;
            if (string.IsNullOrWhiteSpace(indexName))
            {
                return false;
            }

            if (!createdIndexMap.TryGetValue(indexName, out var correspondingCreatedIndex))
            {
                return false;
            }

            var uniqueAttribute = indexInfo.Options.Unique ?? false;
            var createdIndexUnique = correspondingCreatedIndex.Contains("unique") && correspondingCreatedIndex["unique"].AsBoolean;
            if (uniqueAttribute != createdIndexUnique)
            {
                return false;
            }
        }

        return targetIndexNames.All(createdIndexMap.ContainsKey);
    }

    /// <summary>
    /// 为指定集合创建属性声明的索引（异步单飞：并发冷缓存首调在 <see cref="_indexCreationLock"/> 上异步串行化，索引建成才落缓存；失败不缓存、后续调用重试）。
    /// </summary>
    /// <remarks>
    /// Creates the attribute-declared indexes for the specified collection. Bootstrap is
    /// single-flighted on <see cref="_indexCreationLock"/>: concurrent cold-cache first calls
    /// asynchronously wait for the winner, and the per-type cache entry is published only after a
    /// successful build — a failed build (e.g. a unique index colliding with pre-existing duplicates)
    /// leaves the cache untouched so a later call retries. Without this serialization, a concurrent
    /// first-touch could pass the cache check while the index build is still in flight and insert
    /// documents the unique index was meant to reject. All bootstrap driver I/O is asynchronous
    /// (<c>ListAsync</c> / <c>CreateManyAsync</c>) so cold-path waiters never block request threads.
    /// </remarks>
    /// <typeparam name="T">文档类型 / Document type</typeparam>
    /// <param name="collection">MongoDB集合 / MongoDB collection</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    private async Task CreateIndexesAsync<T>(IMongoCollection<T> collection, CancellationToken cancellationToken)
    {
        var entityType = typeof(T);
        if (_indexCache.ContainsKey(entityType.Name))
        {
            return;
        }

        await _indexCreationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // 双检：等待期间先到者可能已完成本类型索引引导。
            // Double check: the lock winner may have completed this type's bootstrap while we waited.
            if (_indexCache.ContainsKey(entityType.Name))
            {
                return;
            }

            var properties = entityType.GetProperties();
            // 索引列表
            List<BsonDocument> result;
            using (var cursor = await collection.Indexes.ListAsync(cancellationToken).ConfigureAwait(false))
            {
                result = await cursor.ToListAsync(cancellationToken).ConfigureAwait(false);
            }

            var indexModels = new List<CreateIndexModel<T>>();
            foreach (var property in properties)
            {
                var indexAttribute = property.GetCustomAttribute<EntityIndexAttribute>();
                if (indexAttribute != null)
                {
                    var indexKeys = indexAttribute.IsAscending ? Builders<T>.IndexKeys.Ascending(property.Name) : Builders<T>.IndexKeys.Descending(property.Name);

                    var indexModel = new CreateIndexModel<T>(indexKeys, new CreateIndexOptions
                    {
                        Unique = indexAttribute.Unique,
                        Name = indexAttribute.Name,
                    });
                    indexModels.Add(indexModel);
                }
            }

            if (indexModels.Count > 0 && !AreIndexesConsistent(indexModels, result))
            {
                await collection.Indexes.CreateManyAsync(indexModels, cancellationToken).ConfigureAwait(false);
            }

            // 建成才落缓存（失败路径不缓存：下次调用重试引导）。
            // Publish only after a successful build (a failure leaves the cache untouched so the next call retries).
            _indexCache.TryAdd(entityType.Name, true);
        }
        finally
        {
            _indexCreationLock.Release();
        }
    }

    #region 索引

    /// <summary>
    /// 创建索引。
    /// </summary>
    /// <remarks>
    /// Creates an index.
    /// </remarks>
    /// <param name="collectionName">集合名称 / Collection name</param>
    /// <param name="definition">索引定义（键名、方向，预留唯一 / TTL 位）/ Index definition (key, direction, with unique / TTL slots reserved)</param>
    /// <returns>创建的索引名称，如果索引已存在则返回空字符串 / The created index name, or empty string if index already exists</returns>
    public string CreateIndex(string collectionName, IndexDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var mgr = GetCollection(collectionName).Indexes;
        var indexKeys = definition.Ascending ? Builders<BsonDocument>.IndexKeys.Ascending(doc => doc[definition.Key]) : Builders<BsonDocument>.IndexKeys.Descending(doc => doc[definition.Key]);
        var expectedName = ResolveExpectedIndexName(indexKeys, definition.Name, BsonSerializer.SerializerRegistry.GetSerializer<BsonDocument>());

        // 精确名匹配判重：遍历完整游标，任一批命中期望名即视为已存在（不建、返回空字符串）。
        // Exact-name dedup: traverse the full cursor; a match in any batch means the index exists
        // (skip creation and return the empty string).
        using (var cursor = mgr.List())
        {
            while (cursor.MoveNext())
            {
                if (cursor.Current.Any(doc => doc.Contains("name") && doc["name"].AsString == expectedName))
                {
                    return string.Empty;
                }
            }
        }

        return mgr.CreateOne(new CreateIndexModel<BsonDocument>(indexKeys, BuildIndexOptions(definition)));
    }

    /// <summary>
    /// 异步创建索引。
    /// </summary>
    /// <remarks>
    /// Asynchronously creates an index.
    /// </remarks>
    /// <param name="collectionName">集合名称 / Collection name</param>
    /// <param name="definition">索引定义（键名、方向，预留唯一 / TTL 位）/ Index definition (key, direction, with unique / TTL slots reserved)</param>
    /// <returns>创建的索引名称，如果索引已存在则返回空字符串 / The created index name, or empty string if index already exists</returns>
    public async Task<string> CreateIndexAsync(string collectionName, IndexDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var mgr = GetCollection(collectionName).Indexes;
        var indexKeys = definition.Ascending ? Builders<BsonDocument>.IndexKeys.Ascending(doc => doc[definition.Key]) : Builders<BsonDocument>.IndexKeys.Descending(doc => doc[definition.Key]);
        var expectedName = ResolveExpectedIndexName(indexKeys, definition.Name, BsonSerializer.SerializerRegistry.GetSerializer<BsonDocument>());

        // 精确名匹配判重：遍历完整游标，任一批命中期望名即视为已存在（不建、返回空字符串）。
        // Exact-name dedup: traverse the full cursor; a match in any batch means the index exists
        // (skip creation and return the empty string).
        using (var cursor = await mgr.ListAsync().ConfigureAwait(false))
        {
            while (await cursor.MoveNextAsync().ConfigureAwait(false))
            {
                if (cursor.Current.Any(doc => doc.Contains("name") && doc["name"].AsString == expectedName))
                {
                    return string.Empty;
                }
            }
        }

        return await mgr.CreateOneAsync(new CreateIndexModel<BsonDocument>(indexKeys, BuildIndexOptions(definition))).ConfigureAwait(false);
    }

    /// <summary>
    /// 更新索引。
    /// </summary>
    /// <remarks>
    /// Updates an index.
    /// </remarks>
    /// <param name="collectionName">集合名称 / Collection name</param>
    /// <param name="definition">索引定义（键名、方向，预留唯一 / TTL 位）/ Index definition (key, direction, with unique / TTL slots reserved)</param>
    /// <returns>创建的索引名称 / The created index name</returns>
    public string UpdateIndex(string collectionName, IndexDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var mgr = GetCollection(collectionName).Indexes;
        return mgr.CreateOne(new CreateIndexModel<BsonDocument>(definition.Ascending ? Builders<BsonDocument>.IndexKeys.Ascending(doc => doc[definition.Key]) : Builders<BsonDocument>.IndexKeys.Descending(doc => doc[definition.Key]), BuildIndexOptions(definition)));
    }

    /// <summary>
    /// 异步更新索引。
    /// </summary>
    /// <remarks>
    /// Asynchronously updates an index.
    /// </remarks>
    /// <param name="collectionName">集合名称 / Collection name</param>
    /// <param name="definition">索引定义（键名、方向，预留唯一 / TTL 位）/ Index definition (key, direction, with unique / TTL slots reserved)</param>
    /// <returns>创建的索引名称 / The created index name</returns>
    public async Task<string> UpdateIndexAsync(string collectionName, IndexDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var mgr = GetCollection(collectionName).Indexes;
        return await mgr.CreateOneAsync(new CreateIndexModel<BsonDocument>(definition.Ascending ? Builders<BsonDocument>.IndexKeys.Ascending(doc => doc[definition.Key]) : Builders<BsonDocument>.IndexKeys.Descending(doc => doc[definition.Key]), BuildIndexOptions(definition)));
    }

    /// <summary>
    /// 删除索引。
    /// </summary>
    /// <remarks>
    /// Drops an index.
    /// </remarks>
    /// <param name="collectionName">集合名称 / Collection name</param>
    /// <param name="index">索引名称 / Index name</param>
    public void DropIndex(string collectionName, string index)
    {
        GetCollection(collectionName).Indexes.DropOne(index);
    }

    /// <summary>
    /// 异步删除索引。
    /// </summary>
    /// <remarks>
    /// Asynchronously drops an index.
    /// </remarks>
    /// <param name="collectionName">集合名称 / Collection name</param>
    /// <param name="index">索引名称 / Index name</param>
    /// <returns>表示异步操作的任务 / A task representing the asynchronous operation</returns>
    public Task DropIndexAsync(string collectionName, string index)
    {
        return GetCollection(collectionName).Indexes.DropOneAsync(index);
    }

    /// <summary>
    /// 创建泛型索引。
    /// </summary>
    /// <remarks>
    /// Creates a generic index.
    /// </remarks>
    /// <typeparam name="TState">文档类型，必须实现 ICacheState 接口 / Document type, must implement ICacheState interface</typeparam>
    /// <param name="definition">索引定义（名称、键表达式、方向，预留唯一 / TTL 位）/ Index definition (name, key expression, direction, with unique / TTL slots reserved)</param>
    /// <returns>创建的索引名称，如果索引已存在则返回空字符串 / The created index name, or empty string if index already exists</returns>
    public string CreateIndex<TState>(IndexDefinition<TState> definition) where TState : class, ICacheState, new()
    {
        ArgumentNullException.ThrowIfNull(definition);
        var mgr = GetCollection<TState>().Indexes;
        var indexKeys = definition.Ascending ? Builders<TState>.IndexKeys.Ascending(definition.Key) : Builders<TState>.IndexKeys.Descending(definition.Key);
        var expectedName = ResolveExpectedIndexName(indexKeys, definition.Name, BsonSerializer.SerializerRegistry.GetSerializer<TState>());

        // 精确名匹配判重：遍历完整游标，任一批命中期望名即视为已存在（不建、返回空字符串）。
        // Exact-name dedup: traverse the full cursor; a match in any batch means the index exists
        // (skip creation and return the empty string).
        using (var cursor = mgr.List())
        {
            while (cursor.MoveNext())
            {
                if (cursor.Current.Any(doc => doc.Contains("name") && doc["name"].AsString == expectedName))
                {
                    return string.Empty;
                }
            }
        }

        return mgr.CreateOne(new CreateIndexModel<TState>(indexKeys, BuildIndexOptions(definition)));
    }

    /// <summary>
    /// 异步创建泛型索引。
    /// </summary>
    /// <remarks>
    /// Asynchronously creates a generic index.
    /// </remarks>
    /// <typeparam name="TState">文档类型，必须实现 ICacheState 接口 / Document type, must implement ICacheState interface</typeparam>
    /// <param name="definition">索引定义（名称、键表达式、方向，预留唯一 / TTL 位）/ Index definition (name, key expression, direction, with unique / TTL slots reserved)</param>
    /// <returns>创建的索引名称，如果索引已存在则返回空字符串 / The created index name, or empty string if index already exists</returns>
    public async Task<string> CreateIndexAsync<TState>(IndexDefinition<TState> definition) where TState : class, ICacheState, new()
    {
        ArgumentNullException.ThrowIfNull(definition);
        var collection = GetCollection<TState>();

        // 属性声明的索引引导先于显式索引操作（沿用原类型化入口的引导语义，异步单飞）。
        // Attribute-declared index bootstrap runs before the explicit index operation
        // (the former typed-entry bootstrap semantics, now an asynchronous single flight).
        await CreateIndexesAsync(collection, CancellationToken.None).ConfigureAwait(false);

        var mgr = collection.Indexes;
        var indexKeys = definition.Ascending ? Builders<TState>.IndexKeys.Ascending(definition.Key) : Builders<TState>.IndexKeys.Descending(definition.Key);
        var expectedName = ResolveExpectedIndexName(indexKeys, definition.Name, BsonSerializer.SerializerRegistry.GetSerializer<TState>());

        // 精确名匹配判重：遍历完整游标，任一批命中期望名即视为已存在（不建、返回空字符串）。
        // Exact-name dedup: traverse the full cursor; a match in any batch means the index exists
        // (skip creation and return the empty string).
        using (var cursor = await mgr.ListAsync().ConfigureAwait(false))
        {
            while (await cursor.MoveNextAsync().ConfigureAwait(false))
            {
                if (cursor.Current.Any(doc => doc.Contains("name") && doc["name"].AsString == expectedName))
                {
                    return string.Empty;
                }
            }
        }

        return await mgr.CreateOneAsync(new CreateIndexModel<TState>(indexKeys, BuildIndexOptions(definition))).ConfigureAwait(false);
    }

    /// <summary>
    /// 更新泛型索引。
    /// </summary>
    /// <remarks>
    /// Updates a generic index.
    /// </remarks>
    /// <typeparam name="TState">文档类型，必须实现 ICacheState 接口 / Document type, must implement ICacheState interface</typeparam>
    /// <param name="definition">索引定义（键表达式、方向，预留唯一 / TTL 位；名称缺省由数据库生成）/ Index definition (key expression, direction, with unique / TTL slots reserved; name defaults to database-generated)</param>
    /// <returns>创建的索引名称 / The created index name</returns>
    public string UpdateIndex<TState>(IndexDefinition<TState> definition) where TState : class, ICacheState, new()
    {
        ArgumentNullException.ThrowIfNull(definition);
        var mgr = GetCollection<TState>().Indexes;
        return mgr.CreateOne(new CreateIndexModel<TState>(definition.Ascending ? Builders<TState>.IndexKeys.Ascending(definition.Key) : Builders<TState>.IndexKeys.Descending(definition.Key), BuildIndexOptions(definition)));
    }

    /// <summary>
    /// 异步更新泛型索引。
    /// </summary>
    /// <remarks>
    /// Asynchronously updates a generic index.
    /// </remarks>
    /// <typeparam name="TState">文档类型，必须实现 ICacheState 接口 / Document type, must implement ICacheState interface</typeparam>
    /// <param name="definition">索引定义（键表达式、方向，预留唯一 / TTL 位；名称缺省由数据库生成）/ Index definition (key expression, direction, with unique / TTL slots reserved; name defaults to database-generated)</param>
    /// <returns>创建的索引名称 / The created index name</returns>
    public async Task<string> UpdateIndexAsync<TState>(IndexDefinition<TState> definition) where TState : class, ICacheState, new()
    {
        ArgumentNullException.ThrowIfNull(definition);
        var collection = GetCollection<TState>();

        // 属性声明的索引引导先于显式索引操作（沿用原类型化入口的引导语义，异步单飞）。
        // Attribute-declared index bootstrap runs before the explicit index operation
        // (the former typed-entry bootstrap semantics, now an asynchronous single flight).
        await CreateIndexesAsync(collection, CancellationToken.None).ConfigureAwait(false);

        return await collection.Indexes.CreateOneAsync(new CreateIndexModel<TState>(definition.Ascending ? Builders<TState>.IndexKeys.Ascending(definition.Key) : Builders<TState>.IndexKeys.Descending(definition.Key), BuildIndexOptions(definition))).ConfigureAwait(false);
    }

    /// <summary>
    /// 解析索引判重用的期望索引名：显式名优先；无名定义按渲染后的键文档推导服务端默认名（单键 <c>字段_1</c> / <c>字段_-1</c>，多键以下划线连接）。
    /// </summary>
    /// <remarks>
    /// Resolves the expected index name for exact-name dedup: the declared name wins; for
    /// unnamed definitions the server-side default name is derived from the rendered key
    /// document (single key <c>field_1</c> / <c>field_-1</c>, multiple keys joined by underscores).
    /// </remarks>
    /// <typeparam name="TDocument">文档类型 / Document type</typeparam>
    /// <param name="indexKeys">索引键定义 / The index key definition</param>
    /// <param name="declaredName">显式声明的索引名，可为空 / The declared index name, possibly empty</param>
    /// <param name="serializer">文档序列化器 / The document serializer</param>
    /// <returns>判重用的期望索引名 / The expected index name used for dedup</returns>
    private static string ResolveExpectedIndexName<TDocument>(IndexKeysDefinition<TDocument> indexKeys, string declaredName, IBsonSerializer<TDocument> serializer)
    {
        if (!string.IsNullOrWhiteSpace(declaredName))
        {
            return declaredName;
        }

        var renderedKeys = indexKeys.Render(new RenderArgs<TDocument>(serializer, BsonSerializer.SerializerRegistry));
        return string.Join("_", renderedKeys.Elements.Select(element => $"{element.Name}_{element.Value}"));
    }

    /// <summary>
    /// 依据索引定义生成数据库索引选项；未启用任何可选项时返回 null 以保持默认行为。
    /// </summary>
    /// <remarks>
    /// Builds the database index options from the definition; returns null to keep the default behaviour when no option is set.
    /// </remarks>
    /// <param name="definition">索引定义 / Index definition</param>
    /// <returns>索引选项，或 null / Index options, or null</returns>
    private static CreateIndexOptions BuildIndexOptions(IndexDefinition definition)
    {
        if (!definition.Unique && !definition.ExpireAfter.HasValue && string.IsNullOrWhiteSpace(definition.Name))
        {
            return null;
        }

        return new CreateIndexOptions
        {
            Unique = definition.Unique ? true : null,
            ExpireAfter = definition.ExpireAfter,
            Name = definition.Name,
        };
    }

    /// <summary>
    /// 依据泛型索引定义生成数据库索引选项；未启用任何可选项时返回 null 以保持默认行为。
    /// </summary>
    /// <remarks>
    /// Builds the database index options from the generic definition; returns null to keep the default behaviour when no option is set.
    /// </remarks>
    /// <param name="definition">索引定义 / Index definition</param>
    /// <returns>索引选项，或 null / Index options, or null</returns>
    private static CreateIndexOptions BuildIndexOptions<TState>(IndexDefinition<TState> definition) where TState : class, ICacheState
    {
        if (!definition.Unique && !definition.ExpireAfter.HasValue && string.IsNullOrWhiteSpace(definition.Name))
        {
            return null;
        }

        return new CreateIndexOptions
        {
            Unique = definition.Unique ? true : null,
            ExpireAfter = definition.ExpireAfter,
            Name = definition.Name,
        };
    }

    #endregion 索引
}
