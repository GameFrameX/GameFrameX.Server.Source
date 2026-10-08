// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and applicable international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file at the root of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using System.Linq.Expressions;
using System.Threading;
using Npgsql;

namespace GameFrameX.DataBase.PostgreSql;

public sealed partial class PostgreSqlDbService
{
    /// <summary>
    /// 根据条件删除单条数据（软删除；先查再改，重复删除幂等返回 0，对齐 Mongo）。
    /// </summary>
    /// <remarks>
    /// Soft-deletes a single document matching the filter (find-then-mark; repeated deletes idempotently return 0, aligned with Mongo).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <returns>实际删除的数量 / The number of actually deleted documents</returns>
    public async Task<long> DeleteAsync<TState>(Expression<Func<TState, bool>> filter) where TState : BaseCacheState, new()
    {
        return await DeleteAsync(filter, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 根据条件删除单条数据（软删除；先查再改，重复删除幂等返回 0，对齐 Mongo）。
    /// </summary>
    /// <remarks>
    /// Soft-deletes a single document matching the filter (find-then-mark; repeated deletes idempotently return 0, aligned with Mongo).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>实际删除的数量 / The number of actually deleted documents</returns>
    public async Task<long> DeleteAsync<TState>(Expression<Func<TState, bool>> filter, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();
        var state = await FindAsync(filter, false, cancellationToken).ConfigureAwait(false);
        if (state == null)
        {
            return 0;
        }

        state.DeleteTime = GetCurrentTimestamp();
        state.IsDeleted = true;
        return await ExecuteSoftDeleteByIdAsync<TState>(state.Id, state.DeleteTime.GetValueOrDefault(), nameof(DeleteAsync), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 删除指定对象（软删除）。
    /// </summary>
    /// <remarks>
    /// Soft-deletes the given state object.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="state">要删除的数据 / The state to delete</param>
    /// <returns>实际删除的数量 / The number of actually deleted documents</returns>
    public async Task<long> DeleteAsync<TState>(TState state) where TState : BaseCacheState, new()
    {
        return await DeleteAsync(state, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 删除指定对象（软删除）。
    /// </summary>
    /// <remarks>
    /// Soft-deletes the given state object.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="state">要删除的数据 / The state to delete</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>实际删除的数量 / The number of actually deleted documents</returns>
    public async Task<long> DeleteAsync<TState>(TState state, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();
        state.DeleteTime = GetCurrentTimestamp();
        state.IsDeleted = true;
        return await ExecuteSoftDeleteByIdAsync<TState>(state.Id, state.DeleteTime.GetValueOrDefault(), nameof(DeleteAsync), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 根据条件批量删除数据（软删除；先查再逐条改，对齐 Mongo 先 FindList 再 BulkWrite 的流程）。
    /// </summary>
    /// <remarks>
    /// Soft-deletes documents matching the filter (find-then-mark each, aligned with Mongo's FindList-then-BulkWrite flow).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <returns>实际删除的数量 / The number of actually deleted documents</returns>
    public async Task<long> DeleteListAsync<TState>(Expression<Func<TState, bool>> filter) where TState : BaseCacheState, new()
    {
        return await DeleteListAsync(filter, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 根据条件批量删除数据（软删除；先查再逐条改，对齐 Mongo 先 FindList 再 BulkWrite 的流程）。
    /// </summary>
    /// <remarks>
    /// Soft-deletes documents matching the filter (find-then-mark each, aligned with Mongo's FindList-then-BulkWrite flow).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>实际删除的数量 / The number of actually deleted documents</returns>
    public async Task<long> DeleteListAsync<TState>(Expression<Func<TState, bool>> filter, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();
        var list = await FindListAsync(filter, cancellationToken).ConfigureAwait(false);
        if (list == null || list.Count == 0)
        {
            return 0;
        }

        long modifiedCount = 0;
        var deleteTime = GetCurrentTimestamp();
        foreach (var state in list)
        {
            state.DeleteTime = deleteTime;
            state.IsDeleted = true;
            modifiedCount += await ExecuteSoftDeleteByIdAsync<TState>(state.Id, deleteTime, nameof(DeleteListAsync), cancellationToken).ConfigureAwait(false);
        }

        return modifiedCount;
    }

    /// <summary>
    /// 根据ID列表批量删除数据（软删除；单语句批量，时间戳每次变化故重复调用仍计行，与 Mongo $set 行为一致）。
    /// </summary>
    /// <remarks>
    /// Soft-deletes documents by ID list (single batched statement; the timestamp changes every call so repeated calls still count rows, consistent with Mongo $set).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="ids">要删除的数据ID列表 / The IDs to delete</param>
    /// <returns>实际删除的数量 / The number of actually deleted documents</returns>
    public async Task<long> DeleteListIdAsync<TState>(IEnumerable<long> ids) where TState : BaseCacheState, new()
    {
        return await DeleteListIdAsync<TState>(ids, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 根据ID列表批量删除数据（软删除；单语句批量，时间戳每次变化故重复调用仍计行，与 Mongo $set 行为一致）。
    /// </summary>
    /// <remarks>
    /// Soft-deletes documents by ID list (single batched statement; the timestamp changes every call so repeated calls still count rows, consistent with Mongo $set).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="ids">要删除的数据ID列表 / The IDs to delete</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>实际删除的数量 / The number of actually deleted documents</returns>
    public async Task<long> DeleteListIdAsync<TState>(IEnumerable<long> ids, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();
        var idArray = ids as long[] ?? ids?.ToArray();
        if (idArray == null || idArray.Length == 0)
        {
            return 0;
        }

        var sql = $"UPDATE {GetTableName<TState>()} SET doc = jsonb_set(jsonb_set(doc, '{{IsDeleted}}', 'true'::jsonb), '{{DeleteTime}}', to_jsonb(@deleteTime::bigint)) WHERE id = ANY(@ids) AND doc IS DISTINCT FROM jsonb_set(jsonb_set(doc, '{{IsDeleted}}', 'true'::jsonb), '{{DeleteTime}}', to_jsonb(@deleteTime::bigint))";
        var parameters = new List<NpgsqlParameter>
        {
            new("ids", idArray.Distinct().ToArray()),
            new("deleteTime", GetCurrentTimestamp()),
        };
        return await ExecuteWriteWithRetryAsync(async token => await ExecuteWriteCommandAsync<TState>(sql, parameters, token).ConfigureAwait(false), cancellationToken, nameof(DeleteListIdAsync), true).ConfigureAwait(false);
    }

    /// <summary>
    /// 根据条件物理删除数据。
    /// </summary>
    /// <remarks>
    /// Hard-deletes documents matching the filter.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <returns>实际删除的数量 / The number of actually deleted documents</returns>
    public async Task<long> HardDeleteAsync<TState>(Expression<Func<TState, bool>> filter) where TState : BaseCacheState, new()
    {
        return await HardDeleteAsync<TState>(filter, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 根据条件物理删除数据。
    /// </summary>
    /// <remarks>
    /// Hard-deletes documents matching the filter.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>实际删除的数量 / The number of actually deleted documents</returns>
    public async Task<long> HardDeleteAsync<TState>(Expression<Func<TState, bool>> filter, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();
        var (whereSql, parameters) = BuildWhere<TState>(filter, false);
        var sql = $"DELETE FROM {GetTableName<TState>()} WHERE {whereSql}";
        return await ExecuteWriteWithRetryAsync(async token => await ExecuteWriteCommandAsync<TState>(sql, parameters, token).ConfigureAwait(false), cancellationToken, nameof(HardDeleteAsync), true).ConfigureAwait(false);
    }

    /// <summary>
    /// 根据条件恢复软删除数据（IsDeleted 置 false 且移除 DeleteTime，对齐 Mongo $unset）。
    /// </summary>
    /// <remarks>
    /// Restores soft-deleted documents matching the filter (sets IsDeleted to false and removes DeleteTime, aligned with Mongo $unset).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <returns>实际恢复的数量 / The number of actually restored documents</returns>
    public async Task<long> RestoreAsync<TState>(Expression<Func<TState, bool>> filter) where TState : BaseCacheState, new()
    {
        return await RestoreAsync<TState>(filter, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 根据条件恢复软删除数据（IsDeleted 置 false 且移除 DeleteTime，对齐 Mongo $unset）。
    /// </summary>
    /// <remarks>
    /// Restores soft-deleted documents matching the filter (sets IsDeleted to false and removes DeleteTime, aligned with Mongo $unset).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>实际恢复的数量 / The number of actually restored documents</returns>
    public async Task<long> RestoreAsync<TState>(Expression<Func<TState, bool>> filter, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();
        // Mongo 语义：命中条件 + IsDeleted == true（恢复过滤不走软删默认过滤）
        // Mongo semantics: filter AND IsDeleted == true (restore intentionally bypasses the soft-delete default filter).
        var (filterSql, filterParameters) = PostgreSqlExpressionTranslator.TranslateFilter(MatchAll<TState>(filter));
        var sql = $"UPDATE {GetTableName<TState>()} SET doc = jsonb_set(doc, '{{IsDeleted}}', 'false'::jsonb) - 'DeleteTime' WHERE ({filterSql}) AND (doc->>'IsDeleted')::boolean = true";
        return await ExecuteWriteWithRetryAsync(async token => await ExecuteWriteCommandAsync<TState>(sql, filterParameters, token).ConfigureAwait(false), cancellationToken, nameof(RestoreAsync), true).ConfigureAwait(false);
    }

    /// <summary>
    /// 按 id 软删除（值未变不计行，对齐 Mongo ModifiedCount）。
    /// </summary>
    /// <remarks>
    /// Soft-deletes by id (unchanged values do not count as rows, aligned with Mongo ModifiedCount).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="id">数据ID / The document ID</param>
    /// <param name="deleteTime">删除时间戳 / The deletion timestamp</param>
    /// <param name="operationName">操作名（用于遥测） / The operation name (used for telemetry)</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>实际删除的数量 / The number of actually deleted documents</returns>
    private async Task<long> ExecuteSoftDeleteByIdAsync<TState>(long id, long deleteTime, string operationName, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        var sql = $"UPDATE {GetTableName<TState>()} SET doc = jsonb_set(jsonb_set(doc, '{{IsDeleted}}', 'true'::jsonb), '{{DeleteTime}}', to_jsonb(@deleteTime::bigint)) WHERE id = @id AND doc IS DISTINCT FROM jsonb_set(jsonb_set(doc, '{{IsDeleted}}', 'true'::jsonb), '{{DeleteTime}}', to_jsonb(@deleteTime::bigint))";
        var parameters = new List<NpgsqlParameter>
        {
            new("id", id),
            new("deleteTime", deleteTime),
        };
        return await ExecuteWriteWithRetryAsync(async token => await ExecuteWriteCommandAsync<TState>(sql, parameters, token).ConfigureAwait(false), cancellationToken, operationName, true).ConfigureAwait(false);
    }

    /// <summary>
    /// 归一化过滤（null → 恒真），供不需要软删默认过滤的路径复用。
    /// </summary>
    /// <remarks>
    /// Normalizes the filter (null → always-true) for code paths that do not need the soft-delete default filter.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <returns>归一化后的过滤表达式 / The normalized filter expression</returns>
    private static Expression<Func<TState, bool>> MatchAll<TState>(Expression<Func<TState, bool>> filter) where TState : BaseCacheState, new()
    {
        return filter ?? (Expression<Func<TState, bool>>)(_ => true);
    }
}