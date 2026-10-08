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
using GameFrameX.DataBase.Abstractions;
using GameFrameX.Utility;
using Npgsql;

namespace GameFrameX.DataBase.PostgreSql;

public sealed partial class PostgreSqlDbService
{
    /// <summary>
    /// 异步加载指定ID的缓存状态；未找到且允许创建时返回新实例（对齐 Mongo 语义）。
    /// </summary>
    /// <remarks>
    /// Asynchronously loads the cache state with the specified ID; returns a new instance when not found and creation is allowed (aligned with Mongo semantics).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="id">数据ID / The document ID</param>
    /// <param name="filter">附加过滤表达式 / An additional filter expression</param>
    /// <param name="isCreateIfNotExists">未找到时是否创建新实例 / Whether to create a new instance when not found</param>
    /// <returns>缓存状态实例 / The cache state instance</returns>
    public async Task<TState> FindAsync<TState>(long id, Expression<Func<TState, bool>> filter = null, bool isCreateIfNotExists = true) where TState : BaseCacheState, new()
    {
        return await FindAsync(id, filter, isCreateIfNotExists, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 异步加载指定ID的缓存状态；未找到且允许创建时返回新实例（对齐 Mongo 语义）。
    /// </summary>
    /// <remarks>
    /// Asynchronously loads the cache state with the specified ID; returns a new instance when not found and creation is allowed (aligned with Mongo semantics).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="id">数据ID / The document ID</param>
    /// <param name="filter">附加过滤表达式 / An additional filter expression</param>
    /// <param name="isCreateIfNotExists">未找到时是否创建新实例 / Whether to create a new instance when not found</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>缓存状态实例 / The cache state instance</returns>
    public async Task<TState> FindAsync<TState>(long id, Expression<Func<TState, bool>> filter, bool isCreateIfNotExists, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();
        var (whereSql, parameters) = BuildWhere(filter, true, id);
        var sql = $"SELECT doc FROM {GetTableName<TState>()} WHERE {whereSql} LIMIT 1";
        var state = await ExecuteReadWithRetryAsync(async token => await ExecuteSingleReadAsync<TState>(sql, parameters, token).ConfigureAwait(false), cancellationToken, nameof(FindAsync)).ConfigureAwait(false);

        return await MaterializeOrCreateAsync(state, isCreateIfNotExists, id, true, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 异步查找满足指定条件的缓存状态；未找到且允许创建时返回新实例（对齐 Mongo 语义）。
    /// </summary>
    /// <remarks>
    /// Asynchronously finds the first cache state matching the filter; returns a new instance when not found and creation is allowed (aligned with Mongo semantics).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <param name="isCreateIfNotExists">未找到时是否创建新实例 / Whether to create a new instance when not found</param>
    /// <returns>缓存状态实例 / The cache state instance</returns>
    public async Task<TState> FindAsync<TState>(Expression<Func<TState, bool>> filter, bool isCreateIfNotExists = true) where TState : BaseCacheState, new()
    {
        return await FindAsync(filter, isCreateIfNotExists, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 异步查找满足指定条件的缓存状态；未找到且允许创建时返回新实例（对齐 Mongo 语义）。
    /// </summary>
    /// <remarks>
    /// Asynchronously finds the first cache state matching the filter; returns a new instance when not found and creation is allowed (aligned with Mongo semantics).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <param name="isCreateIfNotExists">未找到时是否创建新实例 / Whether to create a new instance when not found</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>缓存状态实例 / The cache state instance</returns>
    public async Task<TState> FindAsync<TState>(Expression<Func<TState, bool>> filter, bool isCreateIfNotExists, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();
        var (whereSql, parameters) = BuildWhere(filter, true);
        var sql = $"SELECT doc FROM {GetTableName<TState>()} WHERE {whereSql} LIMIT 1";
        var state = await ExecuteReadWithRetryAsync(async token => await ExecuteSingleReadAsync<TState>(sql, parameters, token).ConfigureAwait(false), cancellationToken, nameof(FindAsync)).ConfigureAwait(false);

        return await MaterializeOrCreateAsync(state, isCreateIfNotExists, 0, false, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 异步查找满足指定条件的缓存状态列表。
    /// </summary>
    /// <remarks>
    /// Asynchronously finds the list of cache states matching the filter.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <returns>缓存状态列表 / The list of cache states</returns>
    public async Task<List<TState>> FindListAsync<TState>(Expression<Func<TState, bool>> filter) where TState : BaseCacheState, new()
    {
        return await FindListAsync(filter, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 异步查找满足指定条件的缓存状态列表。
    /// </summary>
    /// <remarks>
    /// Asynchronously finds the list of cache states matching the filter.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>缓存状态列表 / The list of cache states</returns>
    public async Task<List<TState>> FindListAsync<TState>(Expression<Func<TState, bool>> filter, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();
        var (whereSql, parameters) = BuildWhere(filter, true);
        var sql = $"SELECT doc FROM {GetTableName<TState>()} WHERE {whereSql}";
        var result = await ExecuteReadWithRetryAsync(async token => await ExecuteListReadAsync<TState>(sql, parameters, token).ConfigureAwait(false), cancellationToken, nameof(FindListAsync), () => new List<TState>()).ConfigureAwait(false);
        foreach (var state in result)
        {
            state?.LoadFromDbPostHandler();
        }

        return result;
    }

    /// <summary>
    /// 根据ID列表查询数据列表。
    /// </summary>
    /// <remarks>
    /// Finds the list of documents with the specified IDs.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="ids">数据ID列表 / The document IDs</param>
    /// <returns>缓存状态列表 / The list of cache states</returns>
    public async Task<List<TState>> FindByIdsAsync<TState>(IEnumerable<long> ids) where TState : BaseCacheState, new()
    {
        return await FindByIdsAsync<TState>(ids, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 根据ID列表查询数据列表。
    /// </summary>
    /// <remarks>
    /// Finds the list of documents with the specified IDs.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="ids">数据ID列表 / The document IDs</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>缓存状态列表 / The list of cache states</returns>
    public async Task<List<TState>> FindByIdsAsync<TState>(IEnumerable<long> ids, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();
        var idArray = ids?.Distinct().ToArray();
        if (idArray == null || idArray.Length == 0)
        {
            return new List<TState>();
        }

        var sql = $"SELECT doc FROM {GetTableName<TState>()} WHERE id = ANY(@idList) AND ({PostgreSqlJsonbAccess.SoftDeleteFilter})";
        var parameters = new List<NpgsqlParameter> { new("idList", idArray), };
        var result = await ExecuteReadWithRetryAsync(async token => await ExecuteListReadAsync<TState>(sql, parameters, token).ConfigureAwait(false), cancellationToken, nameof(FindByIdsAsync), () => new List<TState>()).ConfigureAwait(false);
        foreach (var state in result)
        {
            state?.LoadFromDbPostHandler();
        }

        return result;
    }

    /// <summary>
    /// 查询分页数据，并返回总数。
    /// </summary>
    /// <remarks>
    /// Queries a page of data and returns the total count.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <param name="sortExpression">排序表达式 / The sort expression</param>
    /// <param name="descending">是否降序 / Whether to sort descending</param>
    /// <param name="pageIndex">页索引（从 0 开始） / The page index (0-based)</param>
    /// <param name="pageSize">页大小 / The page size</param>
    /// <returns>数据列表与总数 / The item list and total count</returns>
    public async Task<(List<TState> Items, long Total)> FindPageAsync<TState>(Expression<Func<TState, bool>> filter, Expression<Func<TState, object>> sortExpression, bool descending, int pageIndex, int pageSize) where TState : BaseCacheState, new()
    {
        return await FindPageAsync<TState>(filter, sortExpression, descending, pageIndex, pageSize, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 查询分页数据，并返回总数。
    /// </summary>
    /// <remarks>
    /// Queries a page of data and returns the total count.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <param name="sortExpression">排序表达式 / The sort expression</param>
    /// <param name="descending">是否降序 / Whether to sort descending</param>
    /// <param name="pageIndex">页索引（从 0 开始） / The page index (0-based)</param>
    /// <param name="pageSize">页大小 / The page size</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>数据列表与总数 / The item list and total count</returns>
    public async Task<(List<TState> Items, long Total)> FindPageAsync<TState>(Expression<Func<TState, bool>> filter, Expression<Func<TState, object>> sortExpression, bool descending, int pageIndex, int pageSize, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();
        if (pageIndex < 0)
        {
            pageIndex = 0;
        }

        if (pageSize <= 0)
        {
            pageSize = 10;
        }

        var (whereSql, parameters) = BuildWhere(filter, true);
        var sortSql = PostgreSqlExpressionTranslator.TranslateSort(sortExpression, descending);
        var tableName = GetTableName<TState>();
        var totalScalar = await ExecuteReadWithRetryAsync(async token => await ExecuteScalarReadAsync<TState>($"SELECT COUNT(*)::bigint FROM {tableName} WHERE {whereSql}", parameters, token).ConfigureAwait(false), cancellationToken, nameof(FindPageAsync), () => (object)0L).ConfigureAwait(false);
        var total = Convert.ToInt64(totalScalar, System.Globalization.CultureInfo.InvariantCulture);
        var itemsSql = $"SELECT doc FROM {tableName} WHERE {whereSql} ORDER BY {sortSql} OFFSET {pageIndex * pageSize} LIMIT {pageSize}";
        var items = await ExecuteReadWithRetryAsync(async token => await ExecuteListReadAsync<TState>(itemsSql, parameters, token).ConfigureAwait(false), cancellationToken, nameof(FindPageAsync), () => new List<TState>()).ConfigureAwait(false);
        foreach (var state in items)
        {
            state?.LoadFromDbPostHandler();
        }

        return (items, total);
    }

    /// <summary>
    /// 以升序方式查找符合条件的第一个元素。
    /// </summary>
    /// <remarks>
    /// Finds the first element matching the filter in ascending order.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <param name="sortExpression">排序表达式 / The sort expression</param>
    /// <returns>第一个匹配的元素 / The first matching element</returns>
    public async Task<TState> FindSortAscendingFirstOneAsync<TState>(Expression<Func<TState, bool>> filter, Expression<Func<TState, object>> sortExpression) where TState : BaseCacheState, new()
    {
        return await FindSortAscendingFirstOneAsync(filter, sortExpression, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 以升序方式查找符合条件的第一个元素。
    /// </summary>
    /// <remarks>
    /// Finds the first element matching the filter in ascending order.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <param name="sortExpression">排序表达式 / The sort expression</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>第一个匹配的元素 / The first matching element</returns>
    public async Task<TState> FindSortAscendingFirstOneAsync<TState>(Expression<Func<TState, bool>> filter, Expression<Func<TState, object>> sortExpression, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        return await FindSortFirstOneAsync<TState>(filter, sortExpression, false, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 以降序方式查找符合条件的第一个元素。
    /// </summary>
    /// <remarks>
    /// Finds the first element matching the filter in descending order.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <param name="sortExpression">排序表达式 / The sort expression</param>
    /// <returns>第一个匹配的元素 / The first matching element</returns>
    public async Task<TState> FindSortDescendingFirstOneAsync<TState>(Expression<Func<TState, bool>> filter, Expression<Func<TState, object>> sortExpression) where TState : BaseCacheState, new()
    {
        return await FindSortDescendingFirstOneAsync(filter, sortExpression, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 以降序方式查找符合条件的第一个元素。
    /// </summary>
    /// <remarks>
    /// Finds the first element matching the filter in descending order.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <param name="sortExpression">排序表达式 / The sort expression</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>第一个匹配的元素 / The first matching element</returns>
    public async Task<TState> FindSortDescendingFirstOneAsync<TState>(Expression<Func<TState, bool>> filter, Expression<Func<TState, object>> sortExpression, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        return await FindSortFirstOneAsync<TState>(filter, sortExpression, true, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 以指定方向查找符合条件的第一个元素（内部共用实现）。
    /// </summary>
    /// <remarks>
    /// Finds the first element matching the filter in the given direction (shared internal implementation).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <param name="sortExpression">排序表达式 / The sort expression</param>
    /// <param name="descending">是否降序 / Whether to sort descending</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>第一个匹配的元素 / The first matching element</returns>
    private async Task<TState> FindSortFirstOneAsync<TState>(Expression<Func<TState, bool>> filter, Expression<Func<TState, object>> sortExpression, bool descending, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();
        var (whereSql, parameters) = BuildWhere(filter, true);
        var sortSql = PostgreSqlExpressionTranslator.TranslateSort(sortExpression, descending);
        var sql = $"SELECT doc FROM {GetTableName<TState>()} WHERE {whereSql} ORDER BY {sortSql} LIMIT 1";
        var state = await ExecuteReadWithRetryAsync(async token => await ExecuteSingleReadAsync<TState>(sql, parameters, token).ConfigureAwait(false), cancellationToken, descending ? nameof(FindSortDescendingFirstOneAsync) : nameof(FindSortAscendingFirstOneAsync)).ConfigureAwait(false);
        state?.LoadFromDbPostHandler();
        return state;
    }

    /// <summary>
    /// 以降序方式查找符合条件的元素并进行分页。
    /// </summary>
    /// <remarks>
    /// Finds matching elements in descending order with paging.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <param name="sortExpression">排序表达式 / The sort expression</param>
    /// <param name="pageIndex">页索引（从 0 开始） / The page index (0-based)</param>
    /// <param name="pageSize">页大小 / The page size</param>
    /// <returns>缓存状态列表 / The list of cache states</returns>
    public async Task<List<TState>> FindSortDescendingAsync<TState>(Expression<Func<TState, bool>> filter, Expression<Func<TState, object>> sortExpression, int pageIndex = 0, int pageSize = 10) where TState : BaseCacheState, new()
    {
        return await FindSortDescendingAsync(filter, sortExpression, pageIndex, pageSize, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 以降序方式查找符合条件的元素并进行分页。
    /// </summary>
    /// <remarks>
    /// Finds matching elements in descending order with paging.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <param name="sortExpression">排序表达式 / The sort expression</param>
    /// <param name="pageIndex">页索引（从 0 开始） / The page index (0-based)</param>
    /// <param name="pageSize">页大小 / The page size</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>缓存状态列表 / The list of cache states</returns>
    public async Task<List<TState>> FindSortDescendingAsync<TState>(Expression<Func<TState, bool>> filter, Expression<Func<TState, object>> sortExpression, int pageIndex, int pageSize, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        return await FindSortPagedAsync<TState>(filter, sortExpression, true, pageIndex, pageSize, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 以升序方式查找符合条件的元素并进行分页。
    /// </summary>
    /// <remarks>
    /// Finds matching elements in ascending order with paging.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <param name="sortExpression">排序表达式 / The sort expression</param>
    /// <param name="pageIndex">页索引（从 0 开始） / The page index (0-based)</param>
    /// <param name="pageSize">页大小 / The page size</param>
    /// <returns>缓存状态列表 / The list of cache states</returns>
    public async Task<List<TState>> FindSortAscendingAsync<TState>(Expression<Func<TState, bool>> filter, Expression<Func<TState, object>> sortExpression, int pageIndex = 0, int pageSize = 10) where TState : BaseCacheState, new()
    {
        return await FindSortAscendingAsync(filter, sortExpression, pageIndex, pageSize, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 以升序方式查找符合条件的元素并进行分页。
    /// </summary>
    /// <remarks>
    /// Finds matching elements in ascending order with paging.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <param name="sortExpression">排序表达式 / The sort expression</param>
    /// <param name="pageIndex">页索引（从 0 开始） / The page index (0-based)</param>
    /// <param name="pageSize">页大小 / The page size</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>缓存状态列表 / The list of cache states</returns>
    public async Task<List<TState>> FindSortAscendingAsync<TState>(Expression<Func<TState, bool>> filter, Expression<Func<TState, object>> sortExpression, int pageIndex, int pageSize, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        return await FindSortPagedAsync<TState>(filter, sortExpression, false, pageIndex, pageSize, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 以指定方向分页查找（内部共用实现）。
    /// </summary>
    /// <remarks>
    /// Finds matching elements with paging in the given direction (shared internal implementation).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <param name="sortExpression">排序表达式 / The sort expression</param>
    /// <param name="descending">是否降序 / Whether to sort descending</param>
    /// <param name="pageIndex">页索引（从 0 开始） / The page index (0-based)</param>
    /// <param name="pageSize">页大小 / The page size</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>缓存状态列表 / The list of cache states</returns>
    private async Task<List<TState>> FindSortPagedAsync<TState>(Expression<Func<TState, bool>> filter, Expression<Func<TState, object>> sortExpression, bool descending, int pageIndex, int pageSize, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();
        if (pageIndex < 0)
        {
            pageIndex = 0;
        }

        if (pageSize <= 0)
        {
            pageSize = 10;
        }

        var (whereSql, parameters) = BuildWhere(filter, true);
        var sortSql = PostgreSqlExpressionTranslator.TranslateSort(sortExpression, descending);
        var sql = $"SELECT doc FROM {GetTableName<TState>()} WHERE {whereSql} ORDER BY {sortSql} OFFSET {pageIndex * pageSize} LIMIT {pageSize}";
        var result = await ExecuteReadWithRetryAsync(async token => await ExecuteListReadAsync<TState>(sql, parameters, token).ConfigureAwait(false), cancellationToken, descending ? nameof(FindSortDescendingAsync) : nameof(FindSortAscendingAsync), () => new List<TState>()).ConfigureAwait(false);
        foreach (var state in result)
        {
            state?.LoadFromDbPostHandler();
        }

        return result;
    }

    /// <summary>
    /// 查询数据数量。
    /// </summary>
    /// <remarks>
    /// Counts the documents matching the filter (soft-deleted documents excluded).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <returns>数据数量 / The document count</returns>
    public async Task<long> CountAsync<TState>(Expression<Func<TState, bool>> filter) where TState : BaseCacheState, new()
    {
        return await CountAsync(filter, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 查询数据数量。
    /// </summary>
    /// <remarks>
    /// Counts the documents matching the filter (soft-deleted documents excluded).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>数据数量 / The document count</returns>
    public async Task<long> CountAsync<TState>(Expression<Func<TState, bool>> filter, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        return await CountCoreAsync<TState>(filter, false, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 查询数据数量。
    /// </summary>
    /// <remarks>
    /// Counts the documents matching the filter, optionally including soft-deleted ones.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <param name="includeDeleted">是否包含软删数据 / Whether to include soft-deleted documents</param>
    /// <returns>数据数量 / The document count</returns>
    public async Task<long> CountAsync<TState>(Expression<Func<TState, bool>> filter, bool includeDeleted) where TState : BaseCacheState, new()
    {
        return await CountAsync(filter, includeDeleted, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 查询数据数量。
    /// </summary>
    /// <remarks>
    /// Counts the documents matching the filter, optionally including soft-deleted ones.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <param name="includeDeleted">是否包含软删数据 / Whether to include soft-deleted documents</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>数据数量 / The document count</returns>
    public async Task<long> CountAsync<TState>(Expression<Func<TState, bool>> filter, bool includeDeleted, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        return await CountCoreAsync<TState>(filter, includeDeleted, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 计数共用实现（includeDeleted 时跳过软删过滤，对齐 Mongo）。
    /// </summary>
    /// <remarks>
    /// Shared count implementation (skips the soft-delete filter when includeDeleted is set, aligned with Mongo).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <param name="includeDeleted">是否包含软删数据 / Whether to include soft-deleted documents</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>数据数量 / The document count</returns>
    private async Task<long> CountCoreAsync<TState>(Expression<Func<TState, bool>> filter, bool includeDeleted, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();
        var (whereSql, parameters) = BuildWhere(filter, !includeDeleted);
        var sql = $"SELECT COUNT(*)::bigint FROM {GetTableName<TState>()} WHERE {whereSql}";
        var countScalar = await ExecuteReadWithRetryAsync(async token => await ExecuteScalarReadAsync<TState>(sql, parameters, token).ConfigureAwait(false), cancellationToken, nameof(CountAsync), () => (object)0L).ConfigureAwait(false);
        var count = Convert.ToInt64(countScalar, System.Globalization.CultureInfo.InvariantCulture);
        return count;
    }

    /// <summary>
    /// 投影查询数据列表（服务端过滤 + 内存投影；投影限成员绑定，语义与 Mongo 投影一致）。
    /// </summary>
    /// <remarks>
    /// Finds a projected list (server-side filtering + in-memory projection; projections are limited to member binding, consistent with Mongo projection semantics).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <typeparam name="TResult">投影结果类型 / The projection result type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <param name="selector">投影选择器 / The projection selector</param>
    /// <returns>投影结果列表 / The projected result list</returns>
    public async Task<List<TResult>> FindProjectedAsync<TState, TResult>(Expression<Func<TState, bool>> filter, Expression<Func<TState, TResult>> selector) where TState : BaseCacheState, new()
    {
        return await FindProjectedAsync<TState, TResult>(filter, selector, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 投影查询数据列表（服务端过滤 + 内存投影；投影限成员绑定，语义与 Mongo 投影一致）。
    /// </summary>
    /// <remarks>
    /// Finds a projected list (server-side filtering + in-memory projection; projections are limited to member binding, consistent with Mongo projection semantics).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <typeparam name="TResult">投影结果类型 / The projection result type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <param name="selector">投影选择器 / The projection selector</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>投影结果列表 / The projected result list</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="selector"/> 为 null 时抛出 / Thrown when <paramref name="selector"/> is null</exception>
    public async Task<List<TResult>> FindProjectedAsync<TState, TResult>(Expression<Func<TState, bool>> filter, Expression<Func<TState, TResult>> selector, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();
        ArgumentNullException.ThrowIfNull(selector, nameof(selector));
        var (whereSql, parameters) = BuildWhere(filter, true);
        var sql = $"SELECT doc FROM {GetTableName<TState>()} WHERE {whereSql}";
        var states = await ExecuteReadWithRetryAsync(async token => await ExecuteListReadAsync<TState>(sql, parameters, token).ConfigureAwait(false), cancellationToken, nameof(FindProjectedAsync), () => new List<TState>()).ConfigureAwait(false);
        var projector = selector.Compile();
        var result = new List<TResult>(states.Count);
        foreach (var state in states)
        {
            result.Add(projector(state));
        }

        return result;
    }

    /// <summary>
    /// 判断是否存在符合条件的数据。
    /// </summary>
    /// <remarks>
    /// Determines whether any document matches the filter.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <returns>是否存在 / Whether any match exists</returns>
    public async Task<bool> AnyAsync<TState>(Expression<Func<TState, bool>> filter) where TState : BaseCacheState, new()
    {
        return await AnyAsync(filter, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 判断是否存在符合条件的数据。
    /// </summary>
    /// <remarks>
    /// Determines whether any document matches the filter.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>是否存在 / Whether any match exists</returns>
    public async Task<bool> AnyAsync<TState>(Expression<Func<TState, bool>> filter, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();
        var (whereSql, parameters) = BuildWhere(filter, true);
        var sql = $"SELECT EXISTS(SELECT 1 FROM {GetTableName<TState>()} WHERE {whereSql})";
        var anyScalar = await ExecuteReadWithRetryAsync(async token => await ExecuteScalarReadAsync<TState>(sql, parameters, token).ConfigureAwait(false), cancellationToken, nameof(AnyAsync), () => (object)false).ConfigureAwait(false);
        var result = anyScalar is true;
        return result;
    }

    /// <summary>
    /// 根据ID判断数据是否存在。
    /// </summary>
    /// <remarks>
    /// Determines whether a document with the specified ID exists.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="id">数据ID / The document ID</param>
    /// <returns>是否存在 / Whether the document exists</returns>
    public Task<bool> ExistsByIdAsync<TState>(long id) where TState : BaseCacheState, new()
    {
        return ExistsByIdAsync<TState>(id, CancellationToken.None);
    }

    /// <summary>
    /// 根据ID判断数据是否存在。
    /// </summary>
    /// <remarks>
    /// Determines whether a document with the specified ID exists.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="id">数据ID / The document ID</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>是否存在 / Whether the document exists</returns>
    public async Task<bool> ExistsByIdAsync<TState>(long id, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();
        var sql = $"SELECT EXISTS(SELECT 1 FROM {GetTableName<TState>()} WHERE id = @id AND ({PostgreSqlJsonbAccess.SoftDeleteFilter}))";
        var parameters = new List<NpgsqlParameter> { new("id", id), };
        var existsScalar = await ExecuteReadWithRetryAsync(async token => await ExecuteScalarReadAsync<TState>(sql, parameters, token).ConfigureAwait(false), cancellationToken, nameof(ExistsByIdAsync), () => (object)false).ConfigureAwait(false);
        var result = existsScalar is true;
        return result;
    }

    /// <summary>
    /// 构建软删过滤后的 WHERE 片段（对齐 Mongo GetDefaultFindExpression 语义）。
    /// </summary>
    /// <typeparam name="TState">状态类型 / State type</typeparam>
    /// <param name="filter">用户过滤表达式 / User filter expression</param>
    /// <param name="includeSoftDeleteFilter">是否附加软删过滤 / Whether to append the soft-delete filter</param>
    /// <param name="idFilter">可选 id 等值过滤 / Optional id equality filter</param>
    /// <returns>WHERE 片段与参数 / WHERE fragment and parameters</returns>
    private static (string WhereSql, List<NpgsqlParameter> Parameters) BuildWhere<TState>(Expression<Func<TState, bool>> filter, bool includeSoftDeleteFilter, long? idFilter = null) where TState : BaseCacheState, new()
    {
        var parts = new List<string>();
        var parameters = new List<NpgsqlParameter>();
        if (idFilter.HasValue)
        {
            parts.Add("id = @id");
            parameters.Add(new NpgsqlParameter("id", idFilter.Value));
        }

        if (includeSoftDeleteFilter)
        {
            parts.Add($"({PostgreSqlJsonbAccess.SoftDeleteFilter})");
        }

        if (filter != null)
        {
            var (filterSql, filterParameters) = PostgreSqlExpressionTranslator.TranslateFilter(filter);
            if (!string.Equals(filterSql, "TRUE", StringComparison.Ordinal))
            {
                parts.Add($"({filterSql})");
                parameters.AddRange(filterParameters);
            }
        }

        return (parts.Count > 0 ? string.Join(" AND ", parts) : "TRUE", parameters);
    }

    /// <summary>
    /// 单行读取（无行返回 null；含表结构确保）。
    /// </summary>
    /// <remarks>
    /// Reads a single row (returns null when no row exists; includes table-structure ensuring).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="sql">SQL 语句 / The SQL statement</param>
    /// <param name="parameters">参数列表 / The parameter list</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>读取到的状态，无行时为 null / The deserialized state, or null when no row exists</returns>
    private async Task<TState> ExecuteSingleReadAsync<TState>(string sql, IReadOnlyList<NpgsqlParameter> parameters, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        await using var connection = DataSource.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnsureTableAsync<TState>(connection, cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var parameter in parameters)
        {
            // Clone：参数列表可能跨命令/重试复用（NpgsqlParameter 绑定后不可再入集合）
            command.Parameters.Add(parameter.Clone());
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return PostgreSqlJsonDocumentSerializer.Deserialize<TState>(reader.GetString(0));
    }

    /// <summary>
    /// 多行读取（含表结构确保）。
    /// </summary>
    /// <remarks>
    /// Reads multiple rows (includes table-structure ensuring).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="sql">SQL 语句 / The SQL statement</param>
    /// <param name="parameters">参数列表 / The parameter list</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>读取到的状态列表 / The deserialized state list</returns>
    private async Task<List<TState>> ExecuteListReadAsync<TState>(string sql, IReadOnlyList<NpgsqlParameter> parameters, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        await using var connection = DataSource.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnsureTableAsync<TState>(connection, cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var parameter in parameters)
        {
            // Clone：参数列表可能跨命令/重试复用（NpgsqlParameter 绑定后不可再入集合）
            command.Parameters.Add(parameter.Clone());
        }

        var result = new List<TState>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result.Add(PostgreSqlJsonDocumentSerializer.Deserialize<TState>(reader.GetString(0)));
        }

        return result;
    }

    /// <summary>
    /// 标量读取（含表结构确保）。
    /// </summary>
    /// <remarks>
    /// Reads a scalar value (includes table-structure ensuring).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="sql">SQL 语句 / The SQL statement</param>
    /// <param name="parameters">参数列表 / The parameter list</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>标量结果 / The scalar result</returns>
    private async Task<object> ExecuteScalarReadAsync<TState>(string sql, IReadOnlyList<NpgsqlParameter> parameters, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        await using var connection = DataSource.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnsureTableAsync<TState>(connection, cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var parameter in parameters)
        {
            // Clone：参数列表可能跨命令/重试复用（NpgsqlParameter 绑定后不可再入集合）
            command.Parameters.Add(parameter.Clone());
        }

        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 物化或创建状态实例（对齐 Mongo FindAsync 的 isCreateIfNotExists 语义）。
    /// </summary>
    /// <remarks>
    /// Materializes or creates a state instance (aligned with the isCreateIfNotExists semantics of Mongo FindAsync).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="state">读取到的状态，可为 null / The loaded state, may be null</param>
    /// <param name="isCreateIfNotExists">未找到时是否创建新实例 / Whether to create a new instance when not found</param>
    /// <param name="id">数据ID / The document ID</param>
    /// <param name="createWithExplicitId">创建时是否使用显式 ID / Whether to create with an explicit ID</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>物化或创建的状态实例 / The materialized or created state instance</returns>
    private Task<TState> MaterializeOrCreateAsync<TState>(TState state, bool isCreateIfNotExists, long id, bool createWithExplicitId, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!isCreateIfNotExists)
        {
            return Task.FromResult(state);
        }

        var isNew = state == null;
        if (state == null)
        {
            state = createWithExplicitId
                        ? new TState { Id = id, CreatedTime = GetCurrentTimestamp(), }
                        : new TState { Id = IdGenerator.GetNextUniqueId(), CreatedTime = GetCurrentTimestamp(), };
        }

        state.LoadFromDbPostHandler(isNew);
        return Task.FromResult(state);
    }
}