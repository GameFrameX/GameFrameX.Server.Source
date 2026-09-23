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


using GameFrameX.Foundation.Utility;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using System.Threading;
using GameFrameX.Utility;

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
    /// <summary>
    /// 保存数据。
    /// </summary>
    /// <remarks>
    /// Saves data.
    /// </remarks>
    /// <typeparam name="TState">数据类型，必须继承自 BaseCacheState / Data type, must inherit from BaseCacheState</typeparam>
    /// <param name="state">要保存的数据对象 / Data object to save</param>
    /// <returns>保存后的数据对象 / The saved data object</returns>
    public async Task<TState> UpdateAsync<TState>(TState state) where TState : BaseCacheState, new()
    {
        return await UpdateAsync(state, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 保存数据。
    /// </summary>
    /// <remarks>
    /// Saves data.
    /// </remarks>
    /// <typeparam name="TState">数据类型，必须继承自 BaseCacheState / Data type, must inherit from BaseCacheState</typeparam>
    /// <param name="state">要保存的数据对象 / Data object to save</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>保存后的数据对象 / The saved data object</returns>
    public async Task<TState> UpdateAsync<TState>(TState state, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();
        var isChanged = state.IsModify();
        if (isChanged)
        {
            state.UpdateTime = GetCurrentTimestamp();
            state.UpdateCount++;

            var collection = _mongoDbContext.GetCollection<TState>();
            var filter = Builders<TState>.Filter.Eq(m => m.Id, state.Id);

            // 构建更新定义，排除 CreatedTime, CreatedId, Id, IsDeleted, DeleteTime 字段
            var updateDefinition = BuildUpdateDefinition(state);

            var result = await ExecuteWriteWithRetryAsync(token => collection.UpdateOneAsync(filter, updateDefinition, cancellationToken: token), cancellationToken, nameof(UpdateAsync), true).ConfigureAwait(false);
            if (result.IsAcknowledged)
            {
                state.SaveToDbPostHandler();
            }
        }

        return state;
    }

    /// <summary>
    /// 保存多条数据。
    /// </summary>
    /// <remarks>
    /// Saves multiple data records.
    /// </remarks>
    /// <typeparam name="TState">数据类型，必须继承自 BaseCacheState / Data type, must inherit from BaseCacheState</typeparam>
    /// <param name="stateList">数据列表对象 / List of data objects</param>
    /// <returns>返回更新成功的数量 / The number of successfully updated records</returns>
    public async Task<long> UpdateAsync<TState>(IEnumerable<TState> stateList) where TState : BaseCacheState, new()
    {
        return await UpdateAsync(stateList, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 保存多条数据。
    /// </summary>
    /// <remarks>
    /// Saves multiple data records.
    /// </remarks>
    /// <typeparam name="TState">数据类型，必须继承自 BaseCacheState / Data type, must inherit from BaseCacheState</typeparam>
    /// <param name="stateList">数据列表对象 / List of data objects</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>返回更新成功的数量 / The number of successfully updated records</returns>
    public async Task<long> UpdateAsync<TState>(IEnumerable<TState> stateList, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();
        var cacheStates = stateList as TState[] ?? stateList?.ToArray();
        if (cacheStates == null || cacheStates.Length == 0)
        {
            return 0;
        }

        var writeModels = new List<WriteModel<TState>>();
        var currentTime = GetCurrentTimestamp();

        foreach (var state in cacheStates)
        {
            var isChanged = state.IsModify();
            if (isChanged)
            {
                state.UpdateTime = currentTime;
                state.UpdateCount++;

                var filter = Builders<TState>.Filter.Eq(m => m.Id, state.Id);
                var updateDefinition = BuildUpdateDefinition(state);
                writeModels.Add(new UpdateOneModel<TState>(filter, updateDefinition));
            }
        }

        if (writeModels.Count > 0)
        {
            var collection = _mongoDbContext.GetCollection<TState>();
            var result = await ExecuteWriteWithRetryAsync(token => collection.BulkWriteAsync(writeModels, BulkWriteOptions, token), cancellationToken, nameof(UpdateAsync), true).ConfigureAwait(false);
            if (result.IsAcknowledged)
            {
                foreach (var state in cacheStates)
                {
                    state.SaveToDbPostHandler();
                }
            }

            return writeModels.Count;
        }

        return 0;
    }

    /// <summary>
    /// 根据ID部分更新数据。
    /// </summary>
    /// <remarks>
    /// Partially update data by ID.
    /// </remarks>
    /// <typeparam name="TState">数据类型，必须继承自 BaseCacheState / Data type, must inherit from BaseCacheState</typeparam>
    /// <param name="id">数据ID / Data ID</param>
    /// <param name="updateFields">更新字段集合 / Update field dictionary</param>
    /// <returns>返回更新成功的数量 / The number of successfully updated records</returns>
    public async Task<long> UpdatePartialAsync<TState>(long id, IReadOnlyDictionary<string, object> updateFields) where TState : BaseCacheState, new()
    {
        return await UpdatePartialAsync<TState>(id, updateFields, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 根据ID部分更新数据。
    /// </summary>
    /// <remarks>
    /// Partially update data by ID.
    /// </remarks>
    /// <typeparam name="TState">数据类型，必须继承自 BaseCacheState / Data type, must inherit from BaseCacheState</typeparam>
    /// <param name="id">数据ID / Data ID</param>
    /// <param name="updateFields">更新字段集合 / Update field dictionary</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>返回更新成功的数量 / The number of successfully updated records</returns>
    public async Task<long> UpdatePartialAsync<TState>(long id, IReadOnlyDictionary<string, object> updateFields, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();
        if (updateFields == null || updateFields.Count == 0)
        {
            return 0;
        }

        var updates = new List<UpdateDefinition<TState>>();
        foreach (var item in updateFields)
        {
            if (string.IsNullOrWhiteSpace(item.Key))
            {
                continue;
            }

            if (item.Key == nameof(BaseCacheState.Id) || item.Key == nameof(BaseCacheState.CreatedTime) || item.Key == nameof(BaseCacheState.CreatedId))
            {
                continue;
            }

            if (item.Value == null)
            {
                updates.Add(Builders<TState>.Update.Unset(item.Key));
                continue;
            }

            updates.Add(Builders<TState>.Update.Set(item.Key, BsonValue.Create(item.Value)));
        }

        if (updates.Count == 0)
        {
            return 0;
        }

        var currentTime = GetCurrentTimestamp();
        updates.Add(Builders<TState>.Update.Set(m => m.UpdateTime, currentTime));

        var collection = _mongoDbContext.GetCollection<TState>();
        var filter = Builders<TState>.Filter.Eq(m => m.Id, id) & Builders<TState>.Filter.Where(GetDefaultFindExpression<TState>(null));

        // UpdateCount 在实体层为 int?（EntityBase），新增未经 AddOrUpdate 的文档该字段为 BSON null，
        // MongoDB 8.x 起 $inc 作用于 null 字段直接报错，因此改用管道更新：先经 $ifNull 兜底为 0 再 +1。
        // UpdateCount is int? at the entity level (EntityBase); documents inserted without AddOrUpdate hold BSON null
        // for this field, and MongoDB 8.x errors when $inc targets a null field — use a pipeline update instead:
        // normalize via $ifNull to 0 and then add 1 (requires MongoDB 4.2+).
        var renderArgs = new RenderArgs<TState>(collection.DocumentSerializer, BsonSerializer.SerializerRegistry);
        var updateCountElementName = Builders<TState>.Update.Set(m => m.UpdateCount, 0).Render(renderArgs)["$set"].AsBsonDocument.GetElement(0).Name;
        var rendered = (BsonDocument)Builders<TState>.Update.Combine(updates).Render(renderArgs);
        var stages = new List<BsonDocument>();
        foreach (var operation in rendered)
        {
            if (operation.Name == "$unset")
            {
                stages.Add(new BsonDocument("$unset", new BsonArray(operation.Value.AsBsonDocument.Names)));
            }
            else
            {
                stages.Add(new BsonDocument(operation.Name, operation.Value));
            }
        }

        stages.Add(new BsonDocument("$set", new BsonDocument(updateCountElementName, new BsonDocument("$add", new BsonArray { new BsonDocument("$ifNull", new BsonArray { "$" + updateCountElementName, 0 }), 1 }))));
        var update = new PipelineUpdateDefinition<TState>(new BsonDocumentStagePipelineDefinition<TState, TState>(stages, collection.DocumentSerializer));
        var result = await ExecuteWriteWithRetryAsync(token => collection.UpdateOneAsync(filter, update, cancellationToken: token), cancellationToken, nameof(UpdatePartialAsync), false).ConfigureAwait(false);
        return result.ModifiedCount;
    }

    /// <summary>
    /// 构建更新定义，排除特定字段。
    /// </summary>
    /// <remarks>
    /// Builds update definition, excluding specific fields.
    /// </remarks>
    /// <typeparam name="TState">数据类型 / Data type</typeparam>
    /// <param name="state">数据对象 / Data object</param>
    /// <returns>更新定义 / Update definition</returns>
    private static UpdateDefinition<TState> BuildUpdateDefinition<TState>(TState state) where TState : BaseCacheState, new()
    {
        var updateDefinitionBuilder = Builders<TState>.Update;
        var updates = new List<UpdateDefinition<TState>>();

        // 获取所有属性，排除不需要更新的字段
        var properties = typeof(TState).GetProperties();
        var excludeProperties = new HashSet<string>
        {
            nameof(BaseCacheState.Id),
            nameof(BaseCacheState.CreatedTime),
            nameof(BaseCacheState.CreatedId),
            nameof(BaseCacheState.IsDeleted),
            nameof(BaseCacheState.DeleteTime),
        };

        foreach (var property in properties)
        {
            if (property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            if (excludeProperties.Contains(property.Name))
            {
                continue;
            }

            var value = property.GetValue(state);
            if (value == null)
            {
                updates.Add(updateDefinitionBuilder.Unset(property.Name));
                continue;
            }

            updates.Add(updateDefinitionBuilder.Set(property.Name, value));
        }

        return updateDefinitionBuilder.Combine(updates);
    }
}
