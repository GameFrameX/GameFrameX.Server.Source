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
//   please refer to the LICENSE file at the root directory of the source code for the full license text.
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


using System.Linq.Expressions;
using System.Text.Json;
using GameFrameX.DataBase.Abstractions;
using GameFrameX.Utility;
using Microsoft.Data.Sqlite;

namespace GameFrameX.DataBase.Sqlite;

/// <summary>
/// SQLite 读路径（id 等值提取下推 + 剩余 filter 客户端求值 + 软删可见性对齐）。
/// </summary>
/// <remarks>
/// Read paths: an id equality of the filter — the whole body (<c>x.Id == c</c> / <c>c == x.Id</c>)
/// or any top-level AndAlso conjunct of it (C265) — is pushed down as <c>WHERE id = ?</c>;
/// everything else evaluates client-side via <c>filter.Compile()</c> (full C# semantics, a superset
/// of the Mongo/PG translatable set — C222; the client-side predicate stays authoritative so the
/// result set is unchanged). The id-IN lookup chunks at <see cref="MaxIdsPerInCommand"/> bound
/// parameters per command (C265). Soft-delete visibility matches Mongo's
/// <c>IsDeleted == null || IsDeleted == false</c> wrapper.
/// </remarks>
public sealed partial class SqliteDbService
{
    /// <summary>
    /// id IN 下推的单命令宿参分块上限（旧/自定义原生构建 SQLITE_MAX_VARIABLE_NUMBER=999，现代构建 32766——取保守兼容值）。
    /// </summary>
    /// <remarks>
    /// The per-command host-parameter chunk bound for id-IN pushdown (legacy/custom native builds cap
    /// SQLITE_MAX_VARIABLE_NUMBER at 999, modern builds at 32766 — the conservative compatible value).
    /// </remarks>
    private const int MaxIdsPerInCommand = 999;

    /// <summary>
    /// 异步加载指定ID的缓存状态；未找到且允许创建时返回新实例（对齐 Mongo/PG 语义）。
    /// </summary>
    /// <remarks>
    /// Loads the cache state with the specified ID; returns a new instance when not found and creation is allowed.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="id">数据ID / The document ID</param>
    /// <param name="filter">附加过滤表达式 / An additional filter expression</param>
    /// <param name="isCreateIfNotExists">未找到时是否创建新实例 / Whether to create a new instance when not found</param>
    /// <returns>缓存状态实例 / The state instance</returns>
    public async Task<TState> FindAsync<TState>(long id, Expression<Func<TState, bool>> filter = null, bool isCreateIfNotExists = true) where TState : BaseCacheState, new()
    {
        return await FindAsync(id, filter, isCreateIfNotExists, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 异步加载指定ID的缓存状态；未找到且允许创建时返回新实例（对齐 Mongo/PG 语义）。
    /// </summary>
    /// <remarks>
    /// Loads the cache state with the specified ID; returns a new instance when not found and creation is allowed.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="id">数据ID / The document ID</param>
    /// <param name="filter">附加过滤表达式 / An additional filter expression</param>
    /// <param name="isCreateIfNotExists">未找到时是否创建新实例 / Whether to create a new instance when not found</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>缓存状态实例 / The state instance</returns>
    public async Task<TState> FindAsync<TState>(long id, Expression<Func<TState, bool>> filter, bool isCreateIfNotExists, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureOpen();
        var predicate = BuildPredicate(filter, includeDeleted: false, out _);
        var states = await ReadRowsAsync<TState>(id, cancellationToken, nameof(FindAsync)).ConfigureAwait(false);
        TState state = null;
        foreach (var candidate in states)
        {
            if (predicate(candidate))
            {
                state = candidate;
                break;
            }
        }

        return await MaterializeOrCreateAsync(state, isCreateIfNotExists, id, createWithExplicitId: true, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 异步查找满足指定条件的缓存状态；未找到且允许创建时返回新实例（对齐 Mongo/PG 语义）。
    /// </summary>
    /// <remarks>
    /// Finds the first cache state matching the filter; returns a new instance when not found and creation is allowed.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <param name="isCreateIfNotExists">未找到时是否创建新实例 / Whether to create a new instance when not found</param>
    /// <returns>缓存状态实例 / The state instance</returns>
    public async Task<TState> FindAsync<TState>(Expression<Func<TState, bool>> filter, bool isCreateIfNotExists = true) where TState : BaseCacheState, new()
    {
        return await FindAsync(filter, isCreateIfNotExists, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 异步查找满足指定条件的缓存状态；未找到且允许创建时返回新实例（对齐 Mongo/PG 语义）。
    /// </summary>
    /// <remarks>
    /// Finds the first cache state matching the filter; returns a new instance when not found and creation is allowed.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <param name="isCreateIfNotExists">未找到时是否创建新实例 / Whether to create a new instance when not found</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>缓存状态实例 / The state instance</returns>
    public async Task<TState> FindAsync<TState>(Expression<Func<TState, bool>> filter, bool isCreateIfNotExists, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureOpen();
        var states = await ReadMatchingAsync(filter, includeDeleted: false, cancellationToken, nameof(FindAsync)).ConfigureAwait(false);
        TState state = null;
        if (states.Count > 0)
        {
            state = states[0];
        }

        return await MaterializeOrCreateAsync(state, isCreateIfNotExists, 0, createWithExplicitId: false, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 异步查找满足指定条件的缓存状态列表。
    /// </summary>
    /// <remarks>
    /// Finds the list of cache states matching the filter.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <returns>缓存状态列表 / The state list</returns>
    public async Task<List<TState>> FindListAsync<TState>(Expression<Func<TState, bool>> filter) where TState : BaseCacheState, new()
    {
        return await FindListAsync(filter, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 异步查找满足指定条件的缓存状态列表。
    /// </summary>
    /// <remarks>
    /// Finds the list of cache states matching the filter.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>缓存状态列表 / The state list</returns>
    public async Task<List<TState>> FindListAsync<TState>(Expression<Func<TState, bool>> filter, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureOpen();
        var result = await ReadMatchingAsync(filter, includeDeleted: false, cancellationToken, nameof(FindListAsync)).ConfigureAwait(false);
        foreach (var state in result)
        {
            state?.LoadFromDbPostHandler();
        }

        return result;
    }

    /// <summary>
    /// 根据ID列表查询数据列表（id IN 下推按宿参上限分块 + 软删可见性，空列表直返空表）。
    /// </summary>
    /// <remarks>
    /// Finds the list of documents with the specified (distinct) IDs; an empty list returns an empty
    /// result. The id-IN pushdown runs in chunks of at most <see cref="MaxIdsPerInCommand"/> bound
    /// parameters per command (C265), the union of the chunks being equivalent to a single IN.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="ids">数据ID列表 / The document IDs</param>
    /// <returns>缓存状态列表 / The state list</returns>
    public async Task<List<TState>> FindByIdsAsync<TState>(IEnumerable<long> ids) where TState : BaseCacheState, new()
    {
        return await FindByIdsAsync<TState>(ids, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 根据ID列表查询数据列表（id IN 下推按宿参上限分块 + 软删可见性，空列表直返空表）。
    /// </summary>
    /// <remarks>
    /// Finds the list of documents with the specified (distinct) IDs; an empty list returns an empty
    /// result. The id-IN pushdown runs in chunks of at most <see cref="MaxIdsPerInCommand"/> bound
    /// parameters per command (C265), the union of the chunks being equivalent to a single IN.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="ids">数据ID列表 / The document IDs</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>缓存状态列表 / The state list</returns>
    public async Task<List<TState>> FindByIdsAsync<TState>(IEnumerable<long> ids, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureOpen();
        var idArray = ids?.Distinct().ToArray();
        if (idArray == null || idArray.Length == 0)
        {
            return new List<TState>();
        }

        var visible = BuildPredicate<TState>(null, includeDeleted: false, out _);
        var result = new List<TState>(idArray.Length);
        await ExecuteReadWithRetryAsync(async _ => await ExecuteGatedAsync(async () =>
        {
            await EnsureTableAsync<TState>(cancellationToken).ConfigureAwait(false);
            var rows = await ReadRowsByIdsAsync<TState>(idArray, cancellationToken).ConfigureAwait(false);
            foreach (var state in rows)
            {
                if (visible(state))
                {
                    result.Add(state);
                }
            }
        }), cancellationToken, nameof(FindByIdsAsync)).ConfigureAwait(false);
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
    /// Queries a page of data and returns the total count (Mongo-aligned sort null placement).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <param name="sortExpression">排序字段表达式 / The sort expression</param>
    /// <param name="descending">是否降序 / Whether descending</param>
    /// <param name="pageIndex">页索引（从 0 开始）/ The page index (0-based)</param>
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
    /// Queries a page of data and returns the total count (Mongo-aligned sort null placement).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <param name="sortExpression">排序字段表达式 / The sort expression</param>
    /// <param name="descending">是否降序 / Whether descending</param>
    /// <param name="pageIndex">页索引（从 0 开始）/ The page index (0-based)</param>
    /// <param name="pageSize">页大小 / The page size</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>数据列表与总数 / The item list and total count</returns>
    public async Task<(List<TState> Items, long Total)> FindPageAsync<TState>(Expression<Func<TState, bool>> filter, Expression<Func<TState, object>> sortExpression, bool descending, int pageIndex, int pageSize, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureOpen();
        NormalizePaging(ref pageIndex, ref pageSize);
        var matched = await ReadMatchingAsync(filter, includeDeleted: false, cancellationToken, nameof(FindPageAsync)).ConfigureAwait(false);
        var total = matched.Count;
        var items = ApplySort(matched, sortExpression, descending).Skip(pageIndex * pageSize).Take(pageSize).ToList();
        foreach (var state in items)
        {
            state?.LoadFromDbPostHandler();
        }

        return (items, total);
    }

    /// <summary>
    /// 投影查询数据列表（客户端过滤 + 内存投影；投影限成员绑定，语义与 Mongo/PG 投影一致）。
    /// </summary>
    /// <remarks>
    /// Finds a projected list (client-side filtering + in-memory projection, consistent with the Mongo/PG projection semantics).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <typeparam name="TResult">投影结果类型 / The projection type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <param name="selector">投影选择器 / The projection selector</param>
    /// <returns>投影结果列表 / The projected list</returns>
    public async Task<List<TResult>> FindProjectedAsync<TState, TResult>(Expression<Func<TState, bool>> filter, Expression<Func<TState, TResult>> selector) where TState : BaseCacheState, new()
    {
        return await FindProjectedAsync<TState, TResult>(filter, selector, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 投影查询数据列表（客户端过滤 + 内存投影；投影限成员绑定，语义与 Mongo/PG 投影一致）。
    /// </summary>
    /// <remarks>
    /// Finds a projected list (client-side filtering + in-memory projection, consistent with the Mongo/PG projection semantics).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <typeparam name="TResult">投影结果类型 / The projection type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <param name="selector">投影选择器 / The projection selector</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>投影结果列表 / The projected list</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="selector"/> 为 null 时抛出 / Thrown when selector is null</exception>
    public async Task<List<TResult>> FindProjectedAsync<TState, TResult>(Expression<Func<TState, bool>> filter, Expression<Func<TState, TResult>> selector, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureOpen();
        ArgumentNullException.ThrowIfNull(selector);
        var states = await ReadMatchingAsync(filter, includeDeleted: false, cancellationToken, nameof(FindProjectedAsync)).ConfigureAwait(false);
        var projector = selector.Compile();
        var result = new List<TResult>(states.Count);
        foreach (var state in states)
        {
            result.Add(projector(state));
        }

        return result;
    }

    /// <summary>
    /// 以升序方式查找符合条件的第一个元素。
    /// </summary>
    /// <remarks>
    /// Finds the first element matching the filter in ascending order.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
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
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <param name="sortExpression">排序表达式 / The sort expression</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>第一个匹配的元素 / The first matching element</returns>
    public async Task<TState> FindSortAscendingFirstOneAsync<TState>(Expression<Func<TState, bool>> filter, Expression<Func<TState, object>> sortExpression, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        return await FindSortFirstOneAsync<TState>(filter, sortExpression, false, cancellationToken, nameof(FindSortAscendingFirstOneAsync)).ConfigureAwait(false);
    }

    /// <summary>
    /// 以降序方式查找符合条件的第一个元素。
    /// </summary>
    /// <remarks>
    /// Finds the first element matching the filter in descending order.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
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
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <param name="sortExpression">排序表达式 / The sort expression</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>第一个匹配的元素 / The first matching element</returns>
    public async Task<TState> FindSortDescendingFirstOneAsync<TState>(Expression<Func<TState, bool>> filter, Expression<Func<TState, object>> sortExpression, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        return await FindSortFirstOneAsync<TState>(filter, sortExpression, true, cancellationToken, nameof(FindSortDescendingFirstOneAsync)).ConfigureAwait(false);
    }

    /// <summary>
    /// 以降序方式查找符合条件的元素并进行分页。
    /// </summary>
    /// <remarks>
    /// Finds matching elements in descending order with paging.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <param name="sortExpression">排序表达式 / The sort expression</param>
    /// <param name="pageIndex">页索引（从 0 开始）/ The page index (0-based)</param>
    /// <param name="pageSize">页大小 / The page size</param>
    /// <returns>缓存状态列表 / The state list</returns>
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
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <param name="sortExpression">排序表达式 / The sort expression</param>
    /// <param name="pageIndex">页索引（从 0 开始）/ The page index (0-based)</param>
    /// <param name="pageSize">页大小 / The page size</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>缓存状态列表 / The state list</returns>
    public async Task<List<TState>> FindSortDescendingAsync<TState>(Expression<Func<TState, bool>> filter, Expression<Func<TState, object>> sortExpression, int pageIndex, int pageSize, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        return await FindSortPagedAsync<TState>(filter, sortExpression, true, pageIndex, pageSize, cancellationToken, nameof(FindSortDescendingAsync)).ConfigureAwait(false);
    }

    /// <summary>
    /// 以升序方式查找符合条件的元素并进行分页。
    /// </summary>
    /// <remarks>
    /// Finds matching elements in ascending order with paging.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <param name="sortExpression">排序表达式 / The sort expression</param>
    /// <param name="pageIndex">页索引（从 0 开始）/ The page index (0-based)</param>
    /// <param name="pageSize">页大小 / The page size</param>
    /// <returns>缓存状态列表 / The state list</returns>
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
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <param name="sortExpression">排序表达式 / The sort expression</param>
    /// <param name="pageIndex">页索引（从 0 开始）/ The page index (0-based)</param>
    /// <param name="pageSize">页大小 / The page size</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>缓存状态列表 / The state list</returns>
    public async Task<List<TState>> FindSortAscendingAsync<TState>(Expression<Func<TState, bool>> filter, Expression<Func<TState, object>> sortExpression, int pageIndex, int pageSize, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        return await FindSortPagedAsync<TState>(filter, sortExpression, false, pageIndex, pageSize, cancellationToken, nameof(FindSortAscendingAsync)).ConfigureAwait(false);
    }

    /// <summary>
    /// 查询数据数量（软删数据不计）。
    /// </summary>
    /// <remarks>
    /// Counts the documents matching the filter (soft-deleted documents excluded).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <returns>数据数量 / The count</returns>
    public async Task<long> CountAsync<TState>(Expression<Func<TState, bool>> filter) where TState : BaseCacheState, new()
    {
        return await CountAsync<TState>(filter, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 查询数据数量（软删数据不计）。
    /// </summary>
    /// <remarks>
    /// Counts the documents matching the filter (soft-deleted documents excluded).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>数据数量 / The count</returns>
    public async Task<long> CountAsync<TState>(Expression<Func<TState, bool>> filter, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        return await CountCoreAsync<TState>(filter, includeDeleted: false, cancellationToken, nameof(CountAsync)).ConfigureAwait(false);
    }

    /// <summary>
    /// 查询数据数量（可选包含软删数据）。
    /// </summary>
    /// <remarks>
    /// Counts the documents matching the filter, optionally including soft-deleted ones.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <param name="includeDeleted">是否包含软删数据 / Whether to include soft-deleted documents</param>
    /// <returns>数据数量 / The count</returns>
    public async Task<long> CountAsync<TState>(Expression<Func<TState, bool>> filter, bool includeDeleted) where TState : BaseCacheState, new()
    {
        return await CountAsync<TState>(filter, includeDeleted, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 查询数据数量（可选包含软删数据）。
    /// </summary>
    /// <remarks>
    /// Counts the documents matching the filter, optionally including soft-deleted ones.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <param name="includeDeleted">是否包含软删数据 / Whether to include soft-deleted documents</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>数据数量 / The count</returns>
    public async Task<long> CountAsync<TState>(Expression<Func<TState, bool>> filter, bool includeDeleted, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        return await CountCoreAsync<TState>(filter, includeDeleted, cancellationToken, nameof(CountAsync)).ConfigureAwait(false);
    }

    /// <summary>
    /// 判断是否存在符合条件的数据。
    /// </summary>
    /// <remarks>
    /// Determines whether any visible document matches the filter.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
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
    /// Determines whether any visible document matches the filter.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>是否存在 / Whether any match exists</returns>
    public async Task<bool> AnyAsync<TState>(Expression<Func<TState, bool>> filter, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureOpen();
        var states = await ReadMatchingAsync(filter, includeDeleted: false, cancellationToken, nameof(AnyAsync)).ConfigureAwait(false);
        return states.Count > 0;
    }

    /// <summary>
    /// 根据ID判断数据是否存在（软删数据不可见）。
    /// </summary>
    /// <remarks>
    /// Determines whether a visible document with the specified ID exists.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="id">数据ID / The document ID</param>
    /// <returns>是否存在 / Whether the document exists</returns>
    public Task<bool> ExistsByIdAsync<TState>(long id) where TState : BaseCacheState, new()
    {
        return ExistsByIdAsync<TState>(id, CancellationToken.None);
    }

    /// <summary>
    /// 根据ID判断数据是否存在（软删数据不可见）。
    /// </summary>
    /// <remarks>
    /// Determines whether a visible document with the specified ID exists.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="id">数据ID / The document ID</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>是否存在 / Whether the document exists</returns>
    public async Task<bool> ExistsByIdAsync<TState>(long id, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureOpen();
        var visible = BuildPredicate<TState>(null, includeDeleted: false, out _);
        var found = false;
        await ExecuteReadWithRetryAsync(async _ => await ExecuteGatedAsync(async () =>
        {
            await EnsureTableAsync<TState>(cancellationToken).ConfigureAwait(false);
            var rows = await ReadRowsRawAsync<TState>(BuildIdEqualsCommand<TState>(id), cancellationToken).ConfigureAwait(false);
            foreach (var state in rows)
            {
                if (visible(state))
                {
                    found = true;
                    break;
                }
            }
        }), cancellationToken, nameof(ExistsByIdAsync)).ConfigureAwait(false);
        return found;
    }

    /// <summary>
    /// 以指定方向查找符合条件的第一个元素（内部共用实现）。
    /// </summary>
    /// <remarks>
    /// Finds the first element matching the filter in the given direction (shared internal implementation).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <param name="sortExpression">排序表达式 / The sort expression</param>
    /// <param name="descending">是否降序 / Whether descending</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <param name="operationName">操作名称（指标与日志）/ Operation name for metrics and logging</param>
    private async Task<TState> FindSortFirstOneAsync<TState>(Expression<Func<TState, bool>> filter, Expression<Func<TState, object>> sortExpression, bool descending, CancellationToken cancellationToken, string operationName) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureOpen();
        var matched = await ReadMatchingAsync(filter, includeDeleted: false, cancellationToken, operationName).ConfigureAwait(false);
        var state = ApplySort(matched, sortExpression, descending).FirstOrDefault();
        state?.LoadFromDbPostHandler();
        return state;
    }

    /// <summary>
    /// 以指定方向分页查找（内部共用实现）。
    /// </summary>
    /// <remarks>
    /// Finds matching elements with paging in the given direction (shared internal implementation).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <param name="sortExpression">排序表达式 / The sort expression</param>
    /// <param name="descending">是否降序 / Whether descending</param>
    /// <param name="pageIndex">页索引（从 0 开始）/ The page index (0-based)</param>
    /// <param name="pageSize">页大小 / The page size</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <param name="operationName">操作名称（指标与日志）/ Operation name for metrics and logging</param>
    private async Task<List<TState>> FindSortPagedAsync<TState>(Expression<Func<TState, bool>> filter, Expression<Func<TState, object>> sortExpression, bool descending, int pageIndex, int pageSize, CancellationToken cancellationToken, string operationName) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureOpen();
        NormalizePaging(ref pageIndex, ref pageSize);
        var matched = await ReadMatchingAsync(filter, includeDeleted: false, cancellationToken, operationName).ConfigureAwait(false);
        var result = ApplySort(matched, sortExpression, descending).Skip(pageIndex * pageSize).Take(pageSize).ToList();
        foreach (var state in result)
        {
            state?.LoadFromDbPostHandler();
        }

        return result;
    }

    /// <summary>
    /// 计数共用实现（includeDeleted 时跳过软删过滤，对齐 Mongo/PG）。
    /// </summary>
    /// <remarks>
    /// Shared count implementation (skips the soft-delete filter when includeDeleted is set).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <param name="includeDeleted">是否包含软删数据 / Whether to include soft-deleted documents</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <param name="operationName">操作名称（指标与日志）/ Operation name for metrics and logging</param>
    private async Task<long> CountCoreAsync<TState>(Expression<Func<TState, bool>> filter, bool includeDeleted, CancellationToken cancellationToken, string operationName) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureOpen();
        var states = await ReadMatchingAsync(filter, includeDeleted, cancellationToken, operationName).ConfigureAwait(false);
        return states.Count;
    }

    /// <summary>
    /// 读取匹配行（id 等值可从整体或顶层合取链提取时下推 WHERE id = ?，否则整表读；随后客户端求值软删可见性 + filter）。
    /// </summary>
    /// <remarks>
    /// Reads the matching states: when an id equality is extractable from the whole filter body or
    /// any of its top-level AndAlso conjuncts (C265) it is pushed down as <c>WHERE id = ?</c>;
    /// otherwise the whole table is read. The client-side predicate then applies soft-delete
    /// visibility (unless <paramref name="includeDeleted"/>) plus the full filter.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="filter">契约过滤表达式 / The contract filter</param>
    /// <param name="includeDeleted">是否包含软删数据 / Whether to include soft-deleted documents</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <param name="operationName">操作名称（指标与日志）/ Operation name for metrics and logging</param>
    private async Task<List<TState>> ReadMatchingAsync<TState>(Expression<Func<TState, bool>> filter, bool includeDeleted, CancellationToken cancellationToken, string operationName) where TState : BaseCacheState, new()
    {
        var predicate = BuildPredicate(filter, includeDeleted, out var pushedDownId);
        List<TState> rows;
        if (pushedDownId.HasValue)
        {
            rows = await ExecuteReadWithRetryAsync(async _ => await ExecuteGatedAsync<List<TState>>(async () =>
            {
                await EnsureTableAsync<TState>(cancellationToken).ConfigureAwait(false);
                return await ReadRowsRawAsync<TState>(BuildIdEqualsCommand<TState>(pushedDownId.Value), cancellationToken).ConfigureAwait(false);
            }), cancellationToken, operationName).ConfigureAwait(false);
        }
        else
        {
            rows = await ExecuteReadWithRetryAsync(async _ => await ExecuteGatedAsync<List<TState>>(async () =>
            {
                await EnsureTableAsync<TState>(cancellationToken).ConfigureAwait(false);
                return await ReadRowsRawAsync<TState>(BuildSelectAllCommand<TState>(), cancellationToken).ConfigureAwait(false);
            }), cancellationToken, operationName).ConfigureAwait(false);
        }

        var result = new List<TState>(rows.Count);
        foreach (var state in rows)
        {
            if (predicate(state))
            {
                result.Add(state);
            }
        }

        return result;
    }

    /// <summary>
    /// 按 id 读取行（WHERE id = ?，客户端求值软删可见性 + filter）。
    /// </summary>
    /// <remarks>
    /// Reads rows by id (WHERE id = ?) then applies the client-side predicate.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="id">数据ID / The document ID</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <param name="operationName">操作名称（指标与日志）/ Operation name for metrics and logging</param>
    private async Task<List<TState>> ReadRowsAsync<TState>(long id, CancellationToken cancellationToken, string operationName) where TState : BaseCacheState, new()
    {
        return await ExecuteReadWithRetryAsync(async _ => await ExecuteGatedAsync<List<TState>>(async () =>
        {
            await EnsureTableAsync<TState>(cancellationToken).ConfigureAwait(false);
            return await ReadRowsRawAsync<TState>(BuildIdEqualsCommand<TState>(id), cancellationToken).ConfigureAwait(false);
        }), cancellationToken, operationName).ConfigureAwait(false);
    }

    /// <summary>
    /// 执行查询命令并物化 doc 文本为状态对象（调用方须持有 <see cref="_gate"/>）。
    /// </summary>
    /// <remarks>
    /// Executes the given command and materializes each doc text into a state object
    /// (the caller must hold <see cref="_gate"/>).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="command">已就绪的查询命令 / The prepared query command</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>物化后的状态列表 / The materialized states</returns>
    private async Task<List<TState>> ReadRowsRawAsync<TState>(SqliteCommand command, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        var result = new List<TState>();
        using (command)
        {
            command.Connection = _connection;
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var doc = reader.GetString(1);
                    var state = JsonSerializer.Deserialize<TState>(doc, DocumentJsonOptions);
                    result.Add(state);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// 构建整表读命令。
    /// </summary>
    /// <remarks>
    /// Builds the select-all command for the state table.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <returns>查询命令 / The query command</returns>
    private SqliteCommand BuildSelectAllCommand<TState>() where TState : BaseCacheState, new()
    {
        var command = _connection.CreateCommand();
        command.CommandText = BuildSelectAllCommandText<TState>();
        return command;
    }

    /// <summary>
    /// 构建整表读 SQL 文本（表名 = 编译期封闭类型名的双引号转义，非运行期输入）。
    /// </summary>
    /// <remarks>
    /// Builds the select-all SQL text. The table name is the quote-escaped <c>typeof(TState).Name</c> —
    /// a compile-time-closed identifier, never runtime input; SQL identifiers cannot be bound parameters,
    /// so the composition is centralized in this dedicated builder with that invariant stated (Sonar S2077).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <returns>SQL 文本 / The SQL text</returns>
    private static string BuildSelectAllCommandText<TState>() where TState : BaseCacheState, new()
    {
        return "SELECT id, doc FROM " + QuoteTableName<TState>() + ";";
    }

    /// <summary>
    /// 构建 id 等值命令。
    /// </summary>
    /// <remarks>
    /// Builds the WHERE id = ? command.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="id">数据ID / The document ID</param>
    /// <returns>查询命令 / The query command</returns>
    private SqliteCommand BuildIdEqualsCommand<TState>(long id) where TState : BaseCacheState, new()
    {
        var command = _connection.CreateCommand();
        command.CommandText = BuildIdEqualsCommandText<TState>();
        command.Parameters.AddWithValue("$id", id);
        return command;
    }

    /// <summary>
    /// 构建 id 等值 SQL 文本（表名 = 编译期封闭类型名的双引号转义，非运行期输入；id 值走 $id 参数绑定）。
    /// </summary>
    /// <remarks>
    /// Builds the WHERE id = ? SQL text. The table name is the quote-escaped <c>typeof(TState).Name</c> —
    /// a compile-time-closed identifier, never runtime input; SQL identifiers cannot be bound parameters,
    /// so the composition is centralized in this dedicated builder with that invariant stated (Sonar S2077).
    /// The id itself is bound via the <c>$id</c> parameter.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <returns>SQL 文本 / The SQL text</returns>
    private static string BuildIdEqualsCommandText<TState>() where TState : BaseCacheState, new()
    {
        return "SELECT id, doc FROM " + QuoteTableName<TState>() + " WHERE id = $id;";
    }

    /// <summary>
    /// 构建 id IN 命令（区间签名：绑定 [offset, offset+count) 的 id，count 须 ≤ <see cref="MaxIdsPerInCommand"/>）。
    /// </summary>
    /// <remarks>
    /// Builds the WHERE id IN (...) command for the slice <c>[offset, offset + count)</c> of the given
    /// (distinct) IDs; the slice length must stay within <see cref="MaxIdsPerInCommand"/> bound parameters.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="ids">去重后的ID集合 / The distinct IDs</param>
    /// <param name="offset">本块起始下标 / The chunk start index</param>
    /// <param name="count">本块数量 / The chunk size</param>
    /// <returns>查询命令 / The query command</returns>
    private SqliteCommand BuildIdInCommand<TState>(long[] ids, int offset, int count) where TState : BaseCacheState, new()
    {
        var command = _connection.CreateCommand();
        var parameterNames = new string[count];
        for (var index = 0; index < count; index++)
        {
            parameterNames[index] = "$id" + index;
            command.Parameters.AddWithValue(parameterNames[index], ids[offset + index]);
        }

        command.CommandText = BuildIdInCommandText<TState>(parameterNames);
        return command;
    }

    /// <summary>
    /// 按 id 集合分块读取行（每块 ≤ <see cref="MaxIdsPerInCommand"/> 宿参，跨块并集等价单 IN；调用方须持有 <see cref="_gate"/>）。
    /// </summary>
    /// <remarks>
    /// Reads the rows of the given ids in chunks of at most <see cref="MaxIdsPerInCommand"/> bound
    /// parameters per command, returning the union — equivalent to a single IN over all ids (C265).
    /// The caller must hold <see cref="_gate"/>.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="ids">去重后的ID集合 / The distinct IDs</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>命中的行 / The matched rows</returns>
    private async Task<List<TState>> ReadRowsByIdsAsync<TState>(long[] ids, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        var rows = new List<TState>();
        for (var offset = 0; offset < ids.Length; offset += MaxIdsPerInCommand)
        {
            var chunkCount = Math.Min(MaxIdsPerInCommand, ids.Length - offset);
            rows.AddRange(await ReadRowsRawAsync<TState>(BuildIdInCommand<TState>(ids, offset, chunkCount), cancellationToken).ConfigureAwait(false));
        }

        return rows;
    }

    /// <summary>
    /// 构建 id IN SQL 文本（表名 = 编译期封闭类型名的双引号转义；参数名由本类内部生成，id 值全走参数绑定）。
    /// </summary>
    /// <remarks>
    /// Builds the WHERE id IN (...) SQL text. The table name is the quote-escaped <c>typeof(TState).Name</c> —
    /// a compile-time-closed identifier, never runtime input; the listed names are internally generated
    /// parameter placeholders whose values are bound via <c>Parameters.AddWithValue</c> — no runtime value
    /// is ever formatted into the SQL text (Sonar S2077).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="parameterNames">内部生成的参数名集合 / The internally generated parameter names</param>
    /// <returns>SQL 文本 / The SQL text</returns>
    private static string BuildIdInCommandText<TState>(string[] parameterNames) where TState : BaseCacheState, new()
    {
        return "SELECT id, doc FROM " + QuoteTableName<TState>() + " WHERE id IN (" + string.Join(", ", parameterNames) + ");";
    }

    /// <summary>
    /// 构建客户端谓词（软删可见性 + filter.Compile()；id 等值可从合取链提取时顺带返回下推值）。
    /// </summary>
    /// <remarks>
    /// Builds the client-side predicate: soft-delete visibility (<c>IsDeleted == null || IsDeleted == false</c>,
    /// matching Mongo's wrapper) AND the compiled contract filter. When an id equality can be extracted
    /// from the whole filter body or any of its top-level AndAlso conjuncts, its value is also returned
    /// for SQL pushdown (the predicate still re-evaluates the full filter — authoritative, C265).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="filter">契约过滤表达式 / The contract filter</param>
    /// <param name="includeDeleted">是否包含软删数据（true 时不加可见性谓词）/ Whether to include soft-deleted documents</param>
    /// <param name="pushedDownId">id 等值可提取时的下推值 / The pushdown value when an id equality is extractable</param>
    /// <returns>客户端谓词 / The client-side predicate</returns>
    private static Func<TState, bool> BuildPredicate<TState>(Expression<Func<TState, bool>> filter, bool includeDeleted, out long? pushedDownId) where TState : BaseCacheState, new()
    {
        pushedDownId = TryExtractIdEquality(filter, out var extractedId) ? extractedId : null;
        var compiled = filter?.Compile();
        return state =>
        {
            if (!includeDeleted && state.IsDeleted == true)
            {
                return false;
            }

            return compiled == null || compiled(state);
        };
    }

    /// <summary>
    /// 提取 filter 顶层合取链中的 id 等值（整体或任一 AndAlso 合取项；x.Id == c / c == x.Id，闭包成员/常量/Convert 均可求值；提不出返回 false）。
    /// </summary>
    /// <remarks>
    /// Attempts to extract an id equality from the WHOLE filter body or any top-level AndAlso conjunct
    /// of it (nested parentheses included; either operand order; constants, Convert nodes and
    /// closure-captured members are evaluated). Pushing the extracted equality down only prunes rows
    /// that cannot satisfy that conjunct — the client-side predicate re-evaluates the full filter, so
    /// the result set is unchanged. Anything else returns false — the caller then falls back to a
    /// full table read.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="filter">契约过滤表达式 / The contract filter</param>
    /// <param name="id">提取出的ID / The extracted id</param>
    /// <returns>是否提取到 id 等值 / Whether an id equality was extracted</returns>
    private static bool TryExtractIdEquality<TState>(Expression<Func<TState, bool>> filter, out long id) where TState : BaseCacheState, new()
    {
        id = 0;
        return filter != null && TryExtractIdEqualityFromConjunct(filter.Body, filter.Parameters[0], out id);
    }

    /// <summary>
    /// 在顶层合取项（含嵌套 AndAlso）中递归提取 id 等值（首个命中即返回）。
    /// </summary>
    /// <remarks>
    /// Recursively extracts an id equality from a top-level conjunct: the body itself, or either
    /// operand of an AndAlso chain (nested parentheses included); the first hit wins.
    /// </remarks>
    /// <param name="body">当前合取项表达式 / The current conjunct expression</param>
    /// <param name="parameter">lambda 参数 / The lambda parameter</param>
    /// <param name="id">提取出的ID / The extracted id</param>
    /// <returns>是否提取到 id 等值 / Whether an id equality was extracted</returns>
    private static bool TryExtractIdEqualityFromConjunct(Expression body, ParameterExpression parameter, out long id)
    {
        if (body is BinaryExpression { NodeType: ExpressionType.AndAlso } andAlso)
        {
            return TryExtractIdEqualityFromConjunct(andAlso.Left, parameter, out id)
                || TryExtractIdEqualityFromConjunct(andAlso.Right, parameter, out id);
        }

        id = 0;
        if (body is not BinaryExpression { NodeType: ExpressionType.Equal } binary)
        {
            return false;
        }

        var leftIsId = IsParameterIdMember(binary.Left, parameter);
        var rightIsId = IsParameterIdMember(binary.Right, parameter);
        if (leftIsId == rightIsId)
        {
            return false;
        }

        return TryEvaluateLongConstant(leftIsId ? binary.Right : binary.Left, out id);
    }

    /// <summary>
    /// 判断表达式是否为参数的 Id 成员访问。
    /// </summary>
    /// <remarks>
    /// Determines whether the expression is a member access of Id on the lambda parameter.
    /// </remarks>
    /// <param name="expression">待检表达式 / The expression to inspect</param>
    /// <param name="parameter">lambda 参数 / The lambda parameter</param>
    /// <returns>是否为 x.Id / Whether it is x.Id</returns>
    private static bool IsParameterIdMember(Expression expression, ParameterExpression parameter)
    {
        if (expression is UnaryExpression { NodeType: ExpressionType.Convert } convert)
        {
            expression = convert.Operand;
        }

        return expression is MemberExpression member && member.Member.Name == nameof(BaseCacheState.Id) && member.Expression == parameter;
    }

    /// <summary>
    /// 求值常量表达式为 long（常量 / Convert / 闭包捕获成员）。
    /// </summary>
    /// <remarks>
    /// Evaluates a constant-shaped expression to a long (constants, Convert wrappers and
    /// closure-captured members). Returns false for anything not statically evaluable.
    /// </remarks>
    /// <param name="expression">待求值表达式 / The expression to evaluate</param>
    /// <param name="value">求值结果 / The evaluated value</param>
    /// <returns>是否可求值 / Whether it was evaluable</returns>
    private static bool TryEvaluateLongConstant(Expression expression, out long value)
    {
        value = 0;
        try
        {
            if (expression is UnaryExpression { NodeType: ExpressionType.Convert } convert)
            {
                expression = convert.Operand;
            }

            object constant = expression switch
            {
                ConstantExpression constantExpression => constantExpression.Value,
                MemberExpression { Expression: ConstantExpression closure } member => member.Member is System.Reflection.FieldInfo field
                    ? field.GetValue(closure.Value)
                    : (member.Member as System.Reflection.PropertyInfo)?.GetValue(closure.Value),
                _ => null,
            };

            if (constant is null or not IConvertible)
            {
                return false;
            }

            value = Convert.ToInt64(constant, System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception exception) when (exception is InvalidCastException or FormatException or OverflowException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// 应用排序（null 位置对齐 Mongo：升序 null/缺失在前、降序在后；LINQ OrderBy 稳定排序）。
    /// </summary>
    /// <remarks>
    /// Applies the sort with Mongo-aligned null placement (nulls first ascending, last descending);
    /// LINQ's OrderBy is stable so equal keys keep their read order.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="source">待排序列表 / The list to sort</param>
    /// <param name="sortExpression">排序表达式 / The sort expression</param>
    /// <param name="descending">是否降序 / Whether descending</param>
    /// <returns>排序后的序列 / The ordered sequence</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="sortExpression"/> 为 null 时抛出 / Thrown when sortExpression is null</exception>
    private static IOrderedEnumerable<TState> ApplySort<TState>(List<TState> source, Expression<Func<TState, object>> sortExpression, bool descending) where TState : BaseCacheState, new()
    {
        ArgumentNullException.ThrowIfNull(sortExpression);
        var keySelector = sortExpression.Compile();
        return descending
            ? source.OrderByDescending(keySelector, MongoAlignedNullLastComparer.Instance)
            : source.OrderBy(keySelector, MongoAlignedNullFirstComparer.Instance);
    }

    /// <summary>
    /// 升序比较器（null 最前，其余按自然序）。
    /// </summary>
    /// <remarks>
    /// Ascending comparer: nulls first, everything else in natural order.
    /// </remarks>
    private sealed class MongoAlignedNullFirstComparer : IComparer<object>
    {
        /// <summary>
        /// 单例。
        /// </summary>
        /// <remarks>
        /// The singleton instance.
        /// </remarks>
        public static readonly MongoAlignedNullFirstComparer Instance = new();

        /// <summary>
        /// 比较两个排序键（null 最前）。
        /// </summary>
        /// <remarks>
        /// Compares two sort keys (nulls first).
        /// </remarks>
        /// <param name="x">左值 / The left value</param>
        /// <param name="y">右值 / The right value</param>
        /// <returns>比较结果 / The comparison result</returns>
        public int Compare(object x, object y)
        {
            if (x == null && y == null)
            {
                return 0;
            }

            if (x == null)
            {
                return -1;
            }

            if (y == null)
            {
                return 1;
            }

            return Comparer<object>.Default.Compare(x, y);
        }
    }

    /// <summary>
    /// 归一化分页参数（负索引归零，非正页大小归 10，对齐 Mongo/PG）。
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
    /// 降序比较器（null 最后：OrderByDescending 反转序，null 判最小故落最后，其余按自然序）。
    /// </summary>
    /// <remarks>
    /// Descending comparer: nulls last. <c>OrderByDescending</c> reverses the order, so null must
    /// compare smallest to land last; everything else keeps the natural order.
    /// </remarks>
    private sealed class MongoAlignedNullLastComparer : IComparer<object>
    {
        /// <summary>
        /// 单例。
        /// </summary>
        /// <remarks>
        /// The singleton instance.
        /// </remarks>
        public static readonly MongoAlignedNullLastComparer Instance = new();

        /// <summary>
        /// 比较两个排序键（null 判最小，配合降序排序落最后）。
        /// </summary>
        /// <remarks>
        /// Compares two sort keys (null compares smallest so descending puts it last).
        /// </remarks>
        /// <param name="x">左值 / The left value</param>
        /// <param name="y">右值 / The right value</param>
        /// <returns>比较结果 / The comparison result</returns>
        public int Compare(object x, object y)
        {
            if (x == null && y == null)
            {
                return 0;
            }

            if (x == null)
            {
                return -1;
            }

            if (y == null)
            {
                return 1;
            }

            return Comparer<object>.Default.Compare(x, y);
        }
    }
    /// <summary>
    /// 物化或创建状态实例（对齐 Mongo 的 isCreateIfNotExists 语义；命中路径也重建脏检查基线）。
    /// </summary>
    /// <remarks>
    /// Materializes or creates a state instance (aligned with the Mongo adapter's isCreateIfNotExists
    /// semantics — a loaded hit also eagerly rebuilds its StateHash baseline so a later IsModify
    /// check compares against the loaded snapshot instead of lazily baselining after mutations).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="state">读取到的状态，可为 null / The loaded state, may be null</param>
    /// <param name="isCreateIfNotExists">未找到时是否创建新实例 / Whether to create when not found</param>
    /// <param name="id">数据ID / The document ID</param>
    /// <param name="createWithExplicitId">创建时是否使用显式 ID / Whether to create with an explicit ID</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>物化或创建的状态实例 / The materialized or created state</returns>
    private Task<TState> MaterializeOrCreateAsync<TState>(TState state, bool isCreateIfNotExists, long id, bool createWithExplicitId, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        var isNew = state == null;
        if (state == null)
        {
            if (!isCreateIfNotExists)
            {
                return Task.FromResult(state);
            }

            state = createWithExplicitId
                ? new TState { Id = id, CreatedTime = GetCurrentTimestamp(), }
                : new TState { Id = IdGenerator.GetNextUniqueId(), CreatedTime = GetCurrentTimestamp(), };
        }

        state.LoadFromDbPostHandler(isNew);
        return Task.FromResult(state);
    }
}
