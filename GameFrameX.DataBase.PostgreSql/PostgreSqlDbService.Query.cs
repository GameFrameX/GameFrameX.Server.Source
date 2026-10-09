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
//   or violate the legal rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:     https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using System.Linq.Expressions;
using System.Threading;
using GameFrameX.DataBase.Abstractions;
using GameFrameX.Utility;
using Microsoft.EntityFrameworkCore;

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
        var state = await ExecuteReadWithRetryAsync(async token =>
        {
            await EnsureTableAsync<TState>(token).ConfigureAwait(false);
            using var context = CreateContext<TState>();
            var query = BuildRowQuery<TState>(context, filter, token);
            var row = await query.FirstOrDefaultAsync(row => row.Id == id, token).ConfigureAwait(false);
            return row?.Doc;
        }, cancellationToken, nameof(FindAsync)).ConfigureAwait(false);

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
        var state = await ExecuteReadWithRetryAsync(async token =>
        {
            await EnsureTableAsync<TState>(token).ConfigureAwait(false);
            using var context = CreateContext<TState>();
            var query = BuildRowQuery<TState>(context, filter, token);
            var row = await query.FirstOrDefaultAsync(token).ConfigureAwait(false);
            return row?.Doc;
        }, cancellationToken, nameof(FindAsync)).ConfigureAwait(false);

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
        var result = await ExecuteReadWithRetryAsync(async token => await QueryDocumentListAsync<TState>(context =>
        {
            var query = BuildRowQuery<TState>(context, filter, token);
            return query;
        }, token).ConfigureAwait(false), cancellationToken, nameof(FindListAsync), () => new List<TState>()).ConfigureAwait(false);
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

        var result = await ExecuteReadWithRetryAsync(async token => await QueryDocumentListAsync<TState>(context => BuildRowQuery<TState>(context, null, token).Where(row => idArray.Contains(row.Id)), token).ConfigureAwait(false), cancellationToken, nameof(FindByIdsAsync), () => new List<TState>()).ConfigureAwait(false);
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
        NormalizePaging(ref pageIndex, ref pageSize);

        var total = await ExecuteReadWithRetryAsync(async token =>
        {
            await EnsureTableAsync<TState>(token).ConfigureAwait(false);
            using var context = CreateContext<TState>();
            return await BuildRowQuery<TState>(context, filter, token).LongCountAsync(token).ConfigureAwait(false);
        }, cancellationToken, nameof(FindPageAsync), () => 0L).ConfigureAwait(false);

        var items = await ExecuteReadWithRetryAsync(async token => await QueryDocumentListAsync<TState>(context => ApplySort(BuildRowQuery<TState>(context, filter, token), sortExpression, descending).Skip(pageIndex * pageSize).Take(pageSize), token).ConfigureAwait(false), cancellationToken, nameof(FindPageAsync), () => new List<TState>()).ConfigureAwait(false);
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
        var state = await ExecuteReadWithRetryAsync(async token =>
        {
            await EnsureTableAsync<TState>(token).ConfigureAwait(false);
            using var context = CreateContext<TState>();
            var row = await ApplySort(BuildRowQuery<TState>(context, filter, token), sortExpression, descending).FirstOrDefaultAsync(token).ConfigureAwait(false);
            return row?.Doc;
        }, cancellationToken, descending ? nameof(FindSortDescendingFirstOneAsync) : nameof(FindSortAscendingFirstOneAsync)).ConfigureAwait(false);
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
        NormalizePaging(ref pageIndex, ref pageSize);

        var result = await ExecuteReadWithRetryAsync(async token => await QueryDocumentListAsync<TState>(context => ApplySort(BuildRowQuery<TState>(context, filter, token), sortExpression, descending).Skip(pageIndex * pageSize).Take(pageSize), token).ConfigureAwait(false), cancellationToken, descending ? nameof(FindSortDescendingAsync) : nameof(FindSortAscendingAsync), () => new List<TState>()).ConfigureAwait(false);
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
        return await ExecuteReadWithRetryAsync(async token =>
        {
            await EnsureTableAsync<TState>(token).ConfigureAwait(false);
            using var context = CreateContext<TState>();
            var query = BuildRowQuery<TState>(context, filter, token, includeDeleted);
            return await query.LongCountAsync(token).ConfigureAwait(false);
        }, cancellationToken, nameof(CountAsync), () => 0L).ConfigureAwait(false);
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
        var states = await ExecuteReadWithRetryAsync(async token => await QueryDocumentListAsync<TState>(context => BuildRowQuery<TState>(context, filter, token), token).ConfigureAwait(false), cancellationToken, nameof(FindProjectedAsync), () => new List<TState>()).ConfigureAwait(false);
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
        return await ExecuteReadWithRetryAsync(async token =>
        {
            await EnsureTableAsync<TState>(token).ConfigureAwait(false);
            using var context = CreateContext<TState>();
            return await BuildRowQuery<TState>(context, filter, token).AnyAsync(token).ConfigureAwait(false);
        }, cancellationToken, nameof(AnyAsync), () => false).ConfigureAwait(false);
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
        return await ExecuteReadWithRetryAsync(async token =>
        {
            await EnsureTableAsync<TState>(token).ConfigureAwait(false);
            using var context = CreateContext<TState>();
            return await BuildRowQuery<TState>(context, null, token).AnyAsync(row => row.Id == id, token).ConfigureAwait(false);
        }, cancellationToken, nameof(ExistsByIdAsync), () => false).ConfigureAwait(false);
    }

    /// <summary>
    /// 构建行查询（无跟踪 + 软删全局过滤器默认生效；includeDeleted 时跳过过滤器）。
    /// </summary>
    /// <remarks>
    /// Builds the row query (no-tracking, soft-delete query filter applied by default; skipped when
    /// <paramref name="includeDeleted"/> is set). The contract filter is rewritten onto the wrapper entity
    /// (<c>row.Doc</c>) and translated by the EF translator — an untranslatable node fails explicitly
    /// (<see cref="InvalidOperationException"/>), never silently falling back to in-memory filtering.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="context">EF 上下文 / The EF context</param>
    /// <param name="filter">契约过滤表达式 / The contract filter expression</param>
    /// <param name="cancellationToken">取消令牌（未使用，仅保持签名一致） / Cancellation token (unused)</param>
    /// <param name="includeDeleted">是否包含软删数据 / Whether to include soft-deleted rows</param>
    /// <returns>行查询 / The row query</returns>
    private static IQueryable<StateRow<TState>> BuildRowQuery<TState>(PostgreSqlDbContext<TState> context, Expression<Func<TState, bool>> filter, CancellationToken cancellationToken, bool includeDeleted = false) where TState : BaseCacheState, new()
    {
        var query = context.Rows.AsNoTracking();
        if (includeDeleted)
        {
            query = query.IgnoreQueryFilters();
        }

        if (filter != null)
        {
            query = query.Where(PostgreSqlStateRowExpressionRewriter.RewriteFilter(filter));
        }

        return query;
    }

    /// <summary>
    /// 物化文档列表查询（无跟踪读取 + doc 物化为状态对象）。
    /// </summary>
    /// <remarks>
    /// Materializes a document list (no-tracking read, doc column shaped into state objects).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="buildQuery">查询构建器 / The query builder</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>状态对象列表 / The state list</returns>
    private async Task<List<TState>> QueryDocumentListAsync<TState>(Func<PostgreSqlDbContext<TState>, IQueryable<StateRow<TState>>> buildQuery, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        await EnsureTableAsync<TState>(cancellationToken).ConfigureAwait(false);
        using var context = CreateContext<TState>();
        var rows = await buildQuery(context).ToListAsync(cancellationToken).ConfigureAwait(false);
        var result = new List<TState>(rows.Count);
        foreach (var row in rows)
        {
            result.Add(row.Doc);
        }

        return result;
    }

    /// <summary>
    /// 应用排序（null 位置对齐 Mongo：升序 null/缺失在前、降序在后；排序键 null 辅助键 + 本键双段翻译）。
    /// </summary>
    /// <remarks>
    /// Applies the sort with Mongo-aligned null placement (nulls/missing first ascending, last descending).
    /// For nullable sort keys an auxiliary <c>IS NULL</c> ordering key precedes the value key so PostgreSQL's
    /// default null ordering (nulls last ascending) does not diverge; non-nullable keys sort directly.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="source">行查询 / The row query</param>
    /// <param name="sortExpression">契约排序表达式 / The contract sort expression</param>
    /// <param name="descending">是否降序 / Whether to sort descending</param>
    /// <returns>排序后的查询 / The ordered query</returns>
    private static IOrderedQueryable<StateRow<TState>> ApplySort<TState>(IQueryable<StateRow<TState>> source, Expression<Func<TState, object>> sortExpression, bool descending) where TState : BaseCacheState, new()
    {
        ArgumentNullException.ThrowIfNull(sortExpression, nameof(sortExpression));
        var sortSelector = PostgreSqlStateRowExpressionRewriter.RewriteSelector(sortExpression);
        var sortMember = sortSelector.Body is UnaryExpression { NodeType: ExpressionType.Convert, } convert ? convert.Operand : sortSelector.Body;
        var isNullableSortKey = !sortMember.Type.IsValueType || Nullable.GetUnderlyingType(sortMember.Type) != null;
        if (isNullableSortKey)
        {
            // 升序 null 在前：null 判定键降序（true=null 排最前）；降序 null 在后：null 判定键升序（true=null 排最后）。
            // Ascending puts nulls first (null-flag descending); descending puts nulls last (null-flag ascending).
            var nullFlag = Expression.Lambda<Func<StateRow<TState>, bool>>(Expression.Equal(sortMember, Expression.Constant(null, sortMember.Type)), sortSelector.Parameters[0]);
            var ordered = descending ? source.OrderBy(nullFlag) : source.OrderByDescending(nullFlag);
            return descending ? ordered.ThenByDescending(sortSelector) : ordered.ThenBy(sortSelector);
        }

        return descending ? source.OrderByDescending(sortSelector) : source.OrderBy(sortSelector);
    }

    /// <summary>
    /// 归一化分页参数（负索引归零，非正页大小归 10）。
    /// </summary>
    /// <remarks>
    /// Normalizes paging arguments (negative index to zero, non-positive size to 10).
    /// </remarks>
    /// <param name="pageIndex">页索引 / The page index</param>
    /// <param name="pageSize">页大小 / The page size</param>
    private static void NormalizePaging(ref int pageIndex, ref int pageSize)
    {
        if (pageIndex < 0)
        {
            pageIndex = 0;
        }

        if (pageSize <= 0)
        {
            pageSize = 10;
        }
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
