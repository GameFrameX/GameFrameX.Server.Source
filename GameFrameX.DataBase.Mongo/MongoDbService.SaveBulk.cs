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
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
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


using GameFrameX.DataBase.Abstractions;
using GameFrameX.Foundation.Logger;
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
    /// <summary>
    /// 按批次大小分批执行批量 upsert 保存（迁移自 StateComponent.ExecuteBatchWritesAsync，C159）。
    /// </summary>
    /// <remarks>
    /// Bulk-upserts the given states in batches of <paramref name="batchSize"/> — migrated verbatim
    /// from <c>StateComponent.ExecuteBatchWritesAsync</c> (C159) so the shutdown-save path keeps its
    /// exact write mechanism: <see cref="ReplaceOneModel{BsonDocument}"/> with an <c>_id</c> equality
    /// filter and <c>IsUpsert=true</c>, unordered <see cref="MongoDbService.BulkWriteOptions"/>, and
    /// per-batch ack/exception isolation (a failed batch is logged and the remaining batches still
    /// execute). Unlike <c>AddOrUpdateListAsync</c> this deliberately does NOT touch
    /// <c>CreatedTime/UpdateTime/UpdateCount</c>: state timestamps are persisted exactly as handed in
    /// (the caller owns timestamp semantics).
    /// </remarks>
    /// <typeparam name="TState">数据类型，必须继承自 BaseCacheState / Data type, must inherit from BaseCacheState</typeparam>
    /// <param name="states">待保存的状态集合 / The states to save</param>
    /// <param name="batchSize">每批数量（&lt;= 0 抛出 ArgumentOutOfRangeException） / Batch size (values &lt;= 0 throw)</param>
    /// <returns>全部成功 ack 批次的状态列表 / The states of all acknowledged batches</returns>
    /// <exception cref="ArgumentOutOfRangeException">当 <paramref name="batchSize"/> &lt;= 0 时抛出 / Thrown when batchSize is &lt;= 0</exception>
    public async Task<IReadOnlyList<TState>> SaveBulkAsync<TState>(IEnumerable<TState> states, int batchSize) where TState : BaseCacheState, new()
    {
        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), batchSize, "Batch size must be greater than zero.");
        }

        EnsureInitialized();
        var stateList = states as IReadOnlyList<TState> ?? states?.ToList();
        if (stateList == null || stateList.Count == 0)
        {
            return Array.Empty<TState>();
        }

        var stateName = typeof(TState).Name;
        var collection = CurrentDatabase.GetCollection<BsonDocument>(stateName);
        var acknowledgedStates = new List<TState>(stateList.Count);

        for (var index = 0; index < stateList.Count; index += batchSize)
        {
            var batchCount = Math.Min(batchSize, stateList.Count - index);
            var writeModels = new List<ReplaceOneModel<BsonDocument>>(batchCount);
            for (var offset = 0; offset < batchCount; offset++)
            {
                var state = stateList[index + offset];
                var bsonDocument = state.ToBsonDocument();
                var filter = Builders<BsonDocument>.Filter.Eq("_id", state.Id);
                writeModels.Add(new ReplaceOneModel<BsonDocument>(filter, bsonDocument) { IsUpsert = true, });
            }

            try
            {
                var result = await collection.BulkWriteAsync(writeModels, BulkWriteOptions).ConfigureAwait(false);
                if (result.IsAcknowledged)
                {
                    for (var offset = 0; offset < batchCount; offset++)
                    {
                        acknowledgedStates.Add(stateList[index + offset]);
                    }
                }
                else
                {
                    LogHelper.Error("MongoDbService.SaveBulkAsync batch not acknowledged. StateName: {stateName} , BatchIndex: {batchIndex} , BatchCount: {batchCount}", stateName, index / batchSize + 1, batchCount);
                }
            }
            catch (Exception exception)
            {
                // 逐批异常隔离（C159 迁移语义）：失败批次记日志后继续后续批次，避免单批故障放大为整批丢失
                // Per-batch exception isolation (migrated semantics): a failed batch is logged and the loop continues with the remaining batches.
                LogHelper.Error("MongoDbService.SaveBulkAsync batch failed. StateName: {stateName} , BatchIndex: {batchIndex} , BatchCount: {batchCount} , Error: {error}", stateName, index / batchSize + 1, batchCount, exception);
            }
        }

        return acknowledgedStates;
    }
}
