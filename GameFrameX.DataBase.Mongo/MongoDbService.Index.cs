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
    /// 为指定集合创建索引。
    /// </summary>
    /// <remarks>
    /// Creates indexes for the specified collection.
    /// </remarks>
    /// <typeparam name="T">文档类型 / Document type</typeparam>
    /// <param name="collection">MongoDB集合 / MongoDB collection</param>
    private void CreateIndexes<T>(IMongoCollection<T> collection)
    {
        var entityType = typeof(T);
        if (_indexCache.ContainsKey(entityType.Name))
        {
            return;
        }

        _indexCache.TryAdd(entityType.Name, true);
        var properties = entityType.GetProperties();
        // 索引列表
        var result = collection.Indexes.List().ToList();

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
            collection.Indexes.CreateMany(indexModels);
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
        ArgumentNullException.ThrowIfNull(definition, nameof(definition));
        var mgr = GetCollection(collectionName).Indexes;
        var list = mgr.List();
        while (list.MoveNext())
        {
            if (!list.Current.Any(doc => doc["name"].AsString.StartsWith(definition.Key)))
            {
                return mgr.CreateOne(new CreateIndexModel<BsonDocument>(definition.Ascending ? Builders<BsonDocument>.IndexKeys.Ascending(doc => doc[definition.Key]) : Builders<BsonDocument>.IndexKeys.Descending(doc => doc[definition.Key]), BuildIndexOptions(definition)));
            }
        }

        return string.Empty;
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
        ArgumentNullException.ThrowIfNull(definition, nameof(definition));
        var mgr = GetCollection(collectionName).Indexes;
        var list = await mgr.ListAsync();
        while (await list.MoveNextAsync())
        {
            if (!list.Current.Any(doc => doc["name"].AsString.StartsWith(definition.Key)))
            {
                return await mgr.CreateOneAsync(new CreateIndexModel<BsonDocument>(definition.Ascending ? Builders<BsonDocument>.IndexKeys.Ascending(doc => doc[definition.Key]) : Builders<BsonDocument>.IndexKeys.Descending(doc => doc[definition.Key]), BuildIndexOptions(definition)));
            }
        }

        return string.Empty;
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
        ArgumentNullException.ThrowIfNull(definition, nameof(definition));
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
        ArgumentNullException.ThrowIfNull(definition, nameof(definition));
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
        ArgumentNullException.ThrowIfNull(definition, nameof(definition));
        var mgr = GetCollection<TState>().Indexes;
        var list = mgr.List();
        while (list.MoveNext())
        {
            if (!list.Current.Any(doc => doc["name"].AsString.StartsWith(definition.Name)))
            {
                return mgr.CreateOne(new CreateIndexModel<TState>(definition.Ascending ? Builders<TState>.IndexKeys.Ascending(definition.Key) : Builders<TState>.IndexKeys.Descending(definition.Key), BuildIndexOptions(definition)));
            }
        }

        return string.Empty;
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
        ArgumentNullException.ThrowIfNull(definition, nameof(definition));
        var mgr = GetCollection<TState>().Indexes;
        var list = await mgr.ListAsync();
        while (await list.MoveNextAsync())
        {
            if (!list.Current.Any(doc => doc["name"].AsString.StartsWith(definition.Name)))
            {
                return await mgr.CreateOneAsync(new CreateIndexModel<TState>(definition.Ascending ? Builders<TState>.IndexKeys.Ascending(definition.Key) : Builders<TState>.IndexKeys.Descending(definition.Key), BuildIndexOptions(definition)));
            }
        }

        return string.Empty;
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
        ArgumentNullException.ThrowIfNull(definition, nameof(definition));
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
        ArgumentNullException.ThrowIfNull(definition, nameof(definition));
        var mgr = GetCollection<TState>().Indexes;
        return await mgr.CreateOneAsync(new CreateIndexModel<TState>(definition.Ascending ? Builders<TState>.IndexKeys.Ascending(definition.Key) : Builders<TState>.IndexKeys.Descending(definition.Key), BuildIndexOptions(definition)));
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
