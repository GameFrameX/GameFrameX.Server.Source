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

using System.Threading;
using System.Threading.Tasks;
using MongoDB.Driver;

namespace GameFrameX.DataBase.Mongo;

/// <summary>
/// MongoDbService 环境会话助手（事务壳内集合操作自动加入事务；壳外为普通无会话调用）。
/// </summary>
/// <remarks>
/// Ambient-session helpers for <see cref="MongoDbService"/>: when <c>ExecuteInTransactionAsync</c>
/// holds an ambient session, every collection operation routed through this family joins the shell's
/// transaction via the driver's session-aware overloads; outside a shell the plain session-less
/// overloads run unchanged.
/// </remarks>
public sealed partial class MongoDbService
{
    /// <summary>
    /// 环境会话访问器（事务壳内非空，壳外恒 null）。
    /// </summary>
    /// <remarks>
    /// The ambient session accessor (non-null inside the transaction shell, always null outside).
    /// </remarks>
    private IClientSessionHandle AmbientSession => _ambientSession.Value;

    /// <summary>
    /// 插入单个文档（环境会话存在时加入事务）。
    /// </summary>
    /// <remarks>
    /// Inserts a single document, joining the ambient transaction's session when one is active.
    /// </remarks>
    /// <typeparam name="TDocument">文档类型 / The document type</typeparam>
    /// <param name="collection">目标集合 / The target collection</param>
    /// <param name="document">要插入的文档 / The document to insert</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>异步任务 / The async task</returns>
    private Task InsertOneWithAmbientSessionAsync<TDocument>(IMongoCollection<TDocument> collection, TDocument document, CancellationToken cancellationToken)
    {
        var session = AmbientSession;
        return session != null
            ? collection.InsertOneAsync(session, document, cancellationToken: cancellationToken)
            : collection.InsertOneAsync(document, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// 插入多个文档（环境会话存在时加入事务）。
    /// </summary>
    /// <remarks>
    /// Inserts many documents, joining the ambient transaction's session when one is active.
    /// </remarks>
    /// <typeparam name="TDocument">文档类型 / The document type</typeparam>
    /// <param name="collection">目标集合 / The target collection</param>
    /// <param name="documents">要插入的文档集合 / The documents to insert</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>异步任务 / The async task</returns>
    private Task InsertManyWithAmbientSessionAsync<TDocument>(IMongoCollection<TDocument> collection, System.Collections.Generic.IEnumerable<TDocument> documents, CancellationToken cancellationToken)
    {
        var session = AmbientSession;
        return session != null
            ? collection.InsertManyAsync(session, documents, cancellationToken: cancellationToken)
            : collection.InsertManyAsync(documents, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// 替换单个文档（环境会话存在时加入事务）。
    /// </summary>
    /// <remarks>
    /// Replaces a single document, joining the ambient transaction's session when one is active.
    /// </remarks>
    /// <typeparam name="TDocument">文档类型 / The document type</typeparam>
    /// <param name="collection">目标集合 / The target collection</param>
    /// <param name="filter">过滤条件 / The filter</param>
    /// <param name="replacement">替换文档 / The replacement document</param>
    /// <param name="options">替换选项 / The replace options</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>替换结果 / The replace result</returns>
    private Task<ReplaceOneResult> ReplaceOneWithAmbientSessionAsync<TDocument>(IMongoCollection<TDocument> collection, FilterDefinition<TDocument> filter, TDocument replacement, ReplaceOptions options, CancellationToken cancellationToken)
    {
        var session = AmbientSession;
        return session != null
            ? collection.ReplaceOneAsync(session, filter, replacement, options, cancellationToken)
            : collection.ReplaceOneAsync(filter, replacement, options, cancellationToken);
    }

    /// <summary>
    /// 批量写入（环境会话存在时加入事务）。
    /// </summary>
    /// <remarks>
    /// Executes a bulk write, joining the ambient transaction's session when one is active.
    /// </remarks>
    /// <typeparam name="TDocument">文档类型 / The document type</typeparam>
    /// <param name="collection">目标集合 / The target collection</param>
    /// <param name="requests">写入模型集合 / The write models</param>
    /// <param name="options">批量写选项 / The bulk-write options</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>批量写结果 / The bulk-write result</returns>
    private Task<BulkWriteResult<TDocument>> BulkWriteWithAmbientSessionAsync<TDocument>(IMongoCollection<TDocument> collection, System.Collections.Generic.IEnumerable<WriteModel<TDocument>> requests, BulkWriteOptions options, CancellationToken cancellationToken = default)
    {
        var session = AmbientSession;
        return session != null
            ? collection.BulkWriteAsync(session, requests, options, cancellationToken)
            : collection.BulkWriteAsync(requests, options, cancellationToken);
    }

    /// <summary>
    /// 更新单个文档（环境会话存在时加入事务）。
    /// </summary>
    /// <remarks>
    /// Updates a single document, joining the ambient transaction's session when one is active.
    /// </remarks>
    /// <typeparam name="TDocument">文档类型 / The document type</typeparam>
    /// <param name="collection">目标集合 / The target collection</param>
    /// <param name="filter">过滤条件 / The filter</param>
    /// <param name="update">更新定义 / The update definition</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>更新结果 / The update result</returns>
    private Task<UpdateResult> UpdateOneWithAmbientSessionAsync<TDocument>(IMongoCollection<TDocument> collection, FilterDefinition<TDocument> filter, UpdateDefinition<TDocument> update, CancellationToken cancellationToken)
    {
        var session = AmbientSession;
        return session != null
            ? collection.UpdateOneAsync(session, filter, update, cancellationToken: cancellationToken)
            : collection.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// 更新多个文档（环境会话存在时加入事务）。
    /// </summary>
    /// <remarks>
    /// Updates many documents, joining the ambient transaction's session when one is active.
    /// </remarks>
    /// <typeparam name="TDocument">文档类型 / The document type</typeparam>
    /// <param name="collection">目标集合 / The target collection</param>
    /// <param name="filter">过滤条件 / The filter</param>
    /// <param name="update">更新定义 / The update definition</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>更新结果 / The update result</returns>
    private Task<UpdateResult> UpdateManyWithAmbientSessionAsync<TDocument>(IMongoCollection<TDocument> collection, FilterDefinition<TDocument> filter, UpdateDefinition<TDocument> update, CancellationToken cancellationToken)
    {
        var session = AmbientSession;
        return session != null
            ? collection.UpdateManyAsync(session, filter, update, cancellationToken: cancellationToken)
            : collection.UpdateManyAsync(filter, update, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// 删除多个文档（环境会话存在时加入事务）。
    /// </summary>
    /// <remarks>
    /// Deletes many documents, joining the ambient transaction's session when one is active.
    /// </remarks>
    /// <typeparam name="TDocument">文档类型 / The document type</typeparam>
    /// <param name="collection">目标集合 / The target collection</param>
    /// <param name="filter">过滤条件 / The filter</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>删除结果 / The delete result</returns>
    private Task<DeleteResult> DeleteManyWithAmbientSessionAsync<TDocument>(IMongoCollection<TDocument> collection, FilterDefinition<TDocument> filter, CancellationToken cancellationToken)
    {
        var session = AmbientSession;
        return session != null
            ? collection.DeleteManyAsync(session, filter, null, cancellationToken)
            : collection.DeleteManyAsync(filter, cancellationToken);
    }

    /// <summary>
    /// 查找并替换单个文档（环境会话存在时加入事务）。
    /// </summary>
    /// <remarks>
    /// Finds one document and replaces it, joining the ambient transaction's session when one is active.
    /// </remarks>
    /// <typeparam name="TDocument">文档类型 / The document type</typeparam>
    /// <param name="collection">目标集合 / The target collection</param>
    /// <param name="filter">过滤条件 / The filter</param>
    /// <param name="replacement">替换文档 / The replacement document</param>
    /// <param name="options">查找替换选项 / The find-and-replace options</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>替换前的文档 / The document before the replacement</returns>
    private Task<TDocument> FindOneAndReplaceWithAmbientSessionAsync<TDocument>(IMongoCollection<TDocument> collection, FilterDefinition<TDocument> filter, TDocument replacement, FindOneAndReplaceOptions<TDocument> options, CancellationToken cancellationToken)
    {
        var session = AmbientSession;
        return session != null
            ? collection.FindOneAndReplaceAsync(session, filter, replacement, options, cancellationToken)
            : collection.FindOneAndReplaceAsync(filter, replacement, options, cancellationToken);
    }

    /// <summary>
    /// 查找并更新单个文档（环境会话存在时加入事务）。
    /// </summary>
    /// <remarks>
    /// Finds one document and updates it, joining the ambient transaction's session when one is active.
    /// </remarks>
    /// <typeparam name="TDocument">文档类型 / The document type</typeparam>
    /// <param name="collection">目标集合 / The target collection</param>
    /// <param name="filter">过滤条件 / The filter</param>
    /// <param name="update">更新定义 / The update definition</param>
    /// <param name="options">查找更新选项 / The find-and-update options</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>更新前的文档 / The document before the update</returns>
    private Task<TDocument> FindOneAndUpdateWithAmbientSessionAsync<TDocument>(IMongoCollection<TDocument> collection, FilterDefinition<TDocument> filter, UpdateDefinition<TDocument> update, FindOneAndUpdateOptions<TDocument> options, CancellationToken cancellationToken)
    {
        var session = AmbientSession;
        return session != null
            ? collection.FindOneAndUpdateAsync(session, filter, update, options, cancellationToken)
            : collection.FindOneAndUpdateAsync(filter, update, options, cancellationToken);
    }

    /// <summary>
    /// 构建查找游标（环境会话存在时加入事务，保证读到本事务未提交写）。
    /// </summary>
    /// <remarks>
    /// Starts a find fluent, joining the ambient transaction's session when one is active so reads
    /// see the transaction's own uncommitted writes.
    /// </remarks>
    /// <typeparam name="TDocument">文档类型 / The document type</typeparam>
    /// <param name="collection">目标集合 / The target collection</param>
    /// <param name="filter">过滤条件 / The filter</param>
    /// <returns>查找游标 / The find fluent</returns>
    private IFindFluent<TDocument, TDocument> FindWithAmbientSession<TDocument>(IMongoCollection<TDocument> collection, FilterDefinition<TDocument> filter)
    {
        var session = AmbientSession;
        return session != null ? collection.Find(session, filter) : collection.Find(filter);
    }

    /// <summary>
    /// 构建可查询 LINQ 根（环境会话存在时加入事务，保证读到本事务未提交写）。
    /// </summary>
    /// <remarks>
    /// Starts a queryable LINQ root, joining the ambient transaction's session when one is active so
    /// reads see the transaction's own uncommitted writes.
    /// </remarks>
    /// <typeparam name="TDocument">文档类型 / The document type</typeparam>
    /// <param name="collection">目标集合 / The target collection</param>
    /// <returns>可查询根 / The queryable root</returns>
    private System.Linq.IQueryable<TDocument> AsQueryableWithAmbientSession<TDocument>(IMongoCollection<TDocument> collection)
    {
        var session = AmbientSession;
        return session != null ? collection.AsQueryable(session) : collection.AsQueryable();
    }

    /// <summary>
    /// 统计文档数（环境会话存在时加入事务）。
    /// </summary>
    /// <remarks>
    /// Counts documents, joining the ambient transaction's session when one is active.
    /// </remarks>
    /// <typeparam name="TDocument">文档类型 / The document type</typeparam>
    /// <param name="collection">目标集合 / The target collection</param>
    /// <param name="filter">过滤条件 / The filter</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>文档数 / The document count</returns>
    private Task<long> CountDocumentsWithAmbientSessionAsync<TDocument>(IMongoCollection<TDocument> collection, FilterDefinition<TDocument> filter, CancellationToken cancellationToken)
    {
        var session = AmbientSession;
        return session != null
            ? collection.CountDocumentsAsync(session, filter, cancellationToken: cancellationToken)
            : collection.CountDocumentsAsync(filter, cancellationToken: cancellationToken);
    }
}
