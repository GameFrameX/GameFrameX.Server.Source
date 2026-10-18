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


using System.Linq.Expressions;
using System.Threading;

namespace GameFrameX.DataBase.Abstractions;

/// <summary>
/// 数据库服务接口。
/// </summary>
/// <remarks>
/// Database service interface.
/// </remarks>
public interface IDatabaseService
{
    /// <summary>
    /// 连接数据库。
    /// </summary>
    /// <remarks>
    /// Connect to the database.
    /// </remarks>
    /// <param name="dbOptions">数据库配置选项 / Database configuration options</param>
    /// <returns>返回数据库是否初始化成功 / Returns whether the database was initialized successfully</returns>
    Task<bool> Open(DbOptions dbOptions);

    /// <summary>
    /// 关闭数据库连接。
    /// </summary>
    /// <remarks>
    /// Close the database connection.
    /// </remarks>
    /// <returns>异步任务 / Async task</returns>
    Task Close();

    /// <summary>
    /// 查询单条数据。
    /// </summary>
    /// <remarks>
    /// Query a single record by ID.
    /// </remarks>
    /// <param name="id">数据的唯一ID / Unique ID of the data</param>
    /// <param name="filter">查询条件 / Query condition</param>
    /// <param name="isCreateIfNotExists">是否创建不存在的文档 / Whether to create the document if it does not exist</param>
    /// <typeparam name="TState">实现ICacheState接口的类型 / Type implementing ICacheState interface</typeparam>
    /// <returns>返回符合条件的数据对象 / Returns the matching data object</returns>
    Task<TState> FindAsync<TState>(long id, Expression<Func<TState, bool>> filter = null, bool isCreateIfNotExists = true) where TState : BaseCacheState, new();

    /// <summary>
    /// 查询单条数据。
    /// </summary>
    /// <remarks>
    /// Query a single record by ID.
    /// </remarks>
    /// <param name="id">数据的唯一ID / Unique ID of the data</param>
    /// <param name="filter">查询条件 / Query condition</param>
    /// <param name="isCreateIfNotExists">是否创建不存在的文档 / Whether to create the document if it does not exist</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <typeparam name="TState">实现ICacheState接口的类型 / Type implementing ICacheState interface</typeparam>
    /// <returns>返回符合条件的数据对象 / Returns the matching data object</returns>
    Task<TState> FindAsync<TState>(long id, Expression<Func<TState, bool>> filter, bool isCreateIfNotExists, CancellationToken cancellationToken) where TState : BaseCacheState, new();

    /// <summary>
    /// 查询单条数据。
    /// </summary>
    /// <remarks>
    /// Query a single record by filter.
    /// </remarks>
    /// <param name="filter">查询条件 / Query condition</param>
    /// <param name="isCreateIfNotExists">是否创建不存在的文档 / Whether to create the document if it does not exist</param>
    /// <typeparam name="TState">实现ICacheState接口的类型 / Type implementing ICacheState interface</typeparam>
    /// <returns>返回符合条件的数据对象 / Returns the matching data object</returns>
    Task<TState> FindAsync<TState>(Expression<Func<TState, bool>> filter, bool isCreateIfNotExists = true) where TState : BaseCacheState, new();

    /// <summary>
    /// 查询单条数据。
    /// </summary>
    /// <remarks>
    /// Query a single record by filter.
    /// </remarks>
    /// <param name="filter">查询条件 / Query condition</param>
    /// <param name="isCreateIfNotExists">是否创建不存在的文档 / Whether to create the document if it does not exist</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <typeparam name="TState">实现ICacheState接口的类型 / Type implementing ICacheState interface</typeparam>
    /// <returns>返回符合条件的数据对象 / Returns the matching data object</returns>
    Task<TState> FindAsync<TState>(Expression<Func<TState, bool>> filter, bool isCreateIfNotExists, CancellationToken cancellationToken) where TState : BaseCacheState, new();

    /// <summary>
    /// 查询数据列表。
    /// </summary>
    /// <remarks>
    /// Query a list of data by filter.
    /// </remarks>
    /// <param name="filter">查询条件 / Query condition</param>
    /// <typeparam name="TState">实现ICacheState接口的类型 / Type implementing ICacheState interface</typeparam>
    /// <returns>返回符合条件的数据列表 / Returns the list of matching data</returns>
    Task<List<TState>> FindListAsync<TState>(Expression<Func<TState, bool>> filter) where TState : BaseCacheState, new();

    /// <summary>
    /// 查询数据列表。
    /// </summary>
    /// <remarks>
    /// Query a list of data by filter.
    /// </remarks>
    /// <param name="filter">查询条件 / Query condition</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <typeparam name="TState">实现ICacheState接口的类型 / Type implementing ICacheState interface</typeparam>
    /// <returns>返回符合条件的数据列表 / Returns the list of matching data</returns>
    Task<List<TState>> FindListAsync<TState>(Expression<Func<TState, bool>> filter, CancellationToken cancellationToken) where TState : BaseCacheState, new();

    /// <summary>
    /// 根据ID列表查询数据列表。
    /// </summary>
    /// <remarks>
    /// Query data list by ID list.
    /// </remarks>
    /// <param name="ids">ID列表 / ID list</param>
    /// <typeparam name="TState">实现ICacheState接口的类型 / Type implementing ICacheState interface</typeparam>
    /// <returns>返回符合条件的数据列表 / Returns the list of matching data</returns>
    Task<List<TState>> FindByIdsAsync<TState>(IEnumerable<long> ids) where TState : BaseCacheState, new();

    /// <summary>
    /// 根据ID列表查询数据列表。
    /// </summary>
    /// <remarks>
    /// Query data list by ID list.
    /// </remarks>
    /// <param name="ids">ID列表 / ID list</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <typeparam name="TState">实现ICacheState接口的类型 / Type implementing ICacheState interface</typeparam>
    /// <returns>返回符合条件的数据列表 / Returns the list of matching data</returns>
    Task<List<TState>> FindByIdsAsync<TState>(IEnumerable<long> ids, CancellationToken cancellationToken) where TState : BaseCacheState, new();

    /// <summary>
    /// 查询分页数据，并返回总数。
    /// </summary>
    /// <remarks>
    /// Query paginated data and return total count.
    /// </remarks>
    /// <typeparam name="TState">实现ICacheState接口的类型 / Type implementing ICacheState interface</typeparam>
    /// <param name="filter">过滤表达式 / Filter expression</param>
    /// <param name="sortExpression">排序字段表达式 / Sort field expression</param>
    /// <param name="descending">是否降序 / Whether descending</param>
    /// <param name="pageIndex">页码，从0开始 / Page index, starting from 0</param>
    /// <param name="pageSize">每页数量 / Page size</param>
    /// <returns>分页数据和总数 / Paged items and total count</returns>
    Task<(List<TState> Items, long Total)> FindPageAsync<TState>(Expression<Func<TState, bool>> filter, Expression<Func<TState, object>> sortExpression, bool descending, int pageIndex, int pageSize) where TState : BaseCacheState, new();

    /// <summary>
    /// 查询分页数据，并返回总数。
    /// </summary>
    /// <remarks>
    /// Query paginated data and return total count.
    /// </remarks>
    /// <typeparam name="TState">实现ICacheState接口的类型 / Type implementing ICacheState interface</typeparam>
    /// <param name="filter">过滤表达式 / Filter expression</param>
    /// <param name="sortExpression">排序字段表达式 / Sort field expression</param>
    /// <param name="descending">是否降序 / Whether descending</param>
    /// <param name="pageIndex">页码，从0开始 / Page index, starting from 0</param>
    /// <param name="pageSize">每页数量 / Page size</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>分页数据和总数 / Paged items and total count</returns>
    Task<(List<TState> Items, long Total)> FindPageAsync<TState>(Expression<Func<TState, bool>> filter, Expression<Func<TState, object>> sortExpression, bool descending, int pageIndex, int pageSize, CancellationToken cancellationToken) where TState : BaseCacheState, new();

    /// <summary>
    /// 投影查询数据列表。
    /// </summary>
    /// <remarks>
    /// Query projected data list.
    /// </remarks>
    /// <typeparam name="TState">源数据类型 / Source data type</typeparam>
    /// <typeparam name="TResult">结果数据类型 / Result data type</typeparam>
    /// <param name="filter">过滤表达式 / Filter expression</param>
    /// <param name="selector">投影表达式 / Projection expression</param>
    /// <returns>投影后的数据列表 / Projected data list</returns>
    Task<List<TResult>> FindProjectedAsync<TState, TResult>(Expression<Func<TState, bool>> filter, Expression<Func<TState, TResult>> selector) where TState : BaseCacheState, new();

    /// <summary>
    /// 投影查询数据列表。
    /// </summary>
    /// <remarks>
    /// Query projected data list.
    /// </remarks>
    /// <typeparam name="TState">源数据类型 / Source data type</typeparam>
    /// <typeparam name="TResult">结果数据类型 / Result data type</typeparam>
    /// <param name="filter">过滤表达式 / Filter expression</param>
    /// <param name="selector">投影表达式 / Projection expression</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>投影后的数据列表 / Projected data list</returns>
    Task<List<TResult>> FindProjectedAsync<TState, TResult>(Expression<Func<TState, bool>> filter, Expression<Func<TState, TResult>> selector, CancellationToken cancellationToken) where TState : BaseCacheState, new();

    /// <summary>
    /// 以升序方式查找符合条件的第一个元素。
    /// </summary>
    /// <remarks>
    /// Find the first element matching the condition in ascending order.
    /// </remarks>
    /// <typeparam name="TState">实现ICacheState接口的类型 / Type implementing ICacheState interface</typeparam>
    /// <param name="filter">过滤表达式 / Filter expression</param>
    /// <param name="sortExpression">排序字段表达式 / Sort field expression</param>
    /// <returns>符合条件的第一个元素 / The first matching element</returns>
    Task<TState> FindSortAscendingFirstOneAsync<TState>(Expression<Func<TState, bool>> filter, Expression<Func<TState, object>> sortExpression) where TState : BaseCacheState, new();

    /// <summary>
    /// 以升序方式查找符合条件的第一个元素。
    /// </summary>
    /// <remarks>
    /// Find the first element matching the condition in ascending order.
    /// </remarks>
    /// <typeparam name="TState">实现ICacheState接口的类型 / Type implementing ICacheState interface</typeparam>
    /// <param name="filter">过滤表达式 / Filter expression</param>
    /// <param name="sortExpression">排序字段表达式 / Sort field expression</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>符合条件的第一个元素 / The first matching element</returns>
    Task<TState> FindSortAscendingFirstOneAsync<TState>(Expression<Func<TState, bool>> filter, Expression<Func<TState, object>> sortExpression, CancellationToken cancellationToken) where TState : BaseCacheState, new();

    /// <summary>
    /// 以降序方式查找符合条件的第一个元素。
    /// </summary>
    /// <remarks>
    /// Find the first element matching the condition in descending order.
    /// </remarks>
    /// <typeparam name="TState">实现ICacheState接口的类型 / Type implementing ICacheState interface</typeparam>
    /// <param name="filter">过滤表达式 / Filter expression</param>
    /// <param name="sortExpression">排序字段表达式 / Sort field expression</param>
    /// <returns>符合条件的第一个元素 / The first matching element</returns>
    Task<TState> FindSortDescendingFirstOneAsync<TState>(Expression<Func<TState, bool>> filter, Expression<Func<TState, object>> sortExpression) where TState : BaseCacheState, new();

    /// <summary>
    /// 以降序方式查找符合条件的第一个元素。
    /// </summary>
    /// <remarks>
    /// Find the first element matching the condition in descending order.
    /// </remarks>
    /// <typeparam name="TState">实现ICacheState接口的类型 / Type implementing ICacheState interface</typeparam>
    /// <param name="filter">过滤表达式 / Filter expression</param>
    /// <param name="sortExpression">排序字段表达式 / Sort field expression</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>符合条件的第一个元素 / The first matching element</returns>
    Task<TState> FindSortDescendingFirstOneAsync<TState>(Expression<Func<TState, bool>> filter, Expression<Func<TState, object>> sortExpression, CancellationToken cancellationToken) where TState : BaseCacheState, new();

    /// <summary>
    /// 以降序方式查找符合条件的元素并进行分页。
    /// </summary>
    /// <remarks>
    /// Find elements matching the condition in descending order with pagination.
    /// </remarks>
    /// <typeparam name="TState">实现ICacheState接口的类型 / Type implementing ICacheState interface</typeparam>
    /// <param name="filter">过滤表达式 / Filter expression</param>
    /// <param name="sortExpression">排序字段表达式 / Sort field expression</param>
    /// <param name="pageIndex">页码，从0开始 / Page index, starting from 0</param>
    /// <param name="pageSize">每页数量，默认为10 / Number of items per page, default is 10</param>
    /// <returns>符合条件的元素列表 / List of matching elements</returns>
    Task<List<TState>> FindSortDescendingAsync<TState>(Expression<Func<TState, bool>> filter, Expression<Func<TState, object>> sortExpression, int pageIndex = 0, int pageSize = 10) where TState : BaseCacheState, new();

    /// <summary>
    /// 以降序方式查找符合条件的元素并进行分页。
    /// </summary>
    /// <remarks>
    /// Find elements matching the condition in descending order with pagination.
    /// </remarks>
    /// <typeparam name="TState">实现ICacheState接口的类型 / Type implementing ICacheState interface</typeparam>
    /// <param name="filter">过滤表达式 / Filter expression</param>
    /// <param name="sortExpression">排序字段表达式 / Sort field expression</param>
    /// <param name="pageIndex">页码，从0开始 / Page index, starting from 0</param>
    /// <param name="pageSize">每页数量 / Number of items per page</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>符合条件的元素列表 / List of matching elements</returns>
    Task<List<TState>> FindSortDescendingAsync<TState>(Expression<Func<TState, bool>> filter, Expression<Func<TState, object>> sortExpression, int pageIndex, int pageSize, CancellationToken cancellationToken) where TState : BaseCacheState, new();

    /// <summary>
    /// 以升序方式查找符合条件的元素并进行分页。
    /// </summary>
    /// <remarks>
    /// Find elements matching the condition in ascending order with pagination.
    /// </remarks>
    /// <typeparam name="TState">实现ICacheState接口的类型 / Type implementing ICacheState interface</typeparam>
    /// <param name="filter">过滤表达式 / Filter expression</param>
    /// <param name="sortExpression">排序字段表达式 / Sort field expression</param>
    /// <param name="pageIndex">页码，从0开始 / Page index, starting from 0</param>
    /// <param name="pageSize">每页数量，默认为10 / Number of items per page, default is 10</param>
    /// <returns>符合条件的元素列表 / List of matching elements</returns>
    Task<List<TState>> FindSortAscendingAsync<TState>(Expression<Func<TState, bool>> filter, Expression<Func<TState, object>> sortExpression, int pageIndex = 0, int pageSize = 10) where TState : BaseCacheState, new();

    /// <summary>
    /// 以升序方式查找符合条件的元素并进行分页。
    /// </summary>
    /// <remarks>
    /// Find elements matching the condition in ascending order with pagination.
    /// </remarks>
    /// <typeparam name="TState">实现ICacheState接口的类型 / Type implementing ICacheState interface</typeparam>
    /// <param name="filter">过滤表达式 / Filter expression</param>
    /// <param name="sortExpression">排序字段表达式 / Sort field expression</param>
    /// <param name="pageIndex">页码，从0开始 / Page index, starting from 0</param>
    /// <param name="pageSize">每页数量 / Number of items per page</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>符合条件的元素列表 / List of matching elements</returns>
    Task<List<TState>> FindSortAscendingAsync<TState>(Expression<Func<TState, bool>> filter, Expression<Func<TState, object>> sortExpression, int pageIndex, int pageSize, CancellationToken cancellationToken) where TState : BaseCacheState, new();

    /// <summary>
    /// 查询数据数量。
    /// </summary>
    /// <remarks>
    /// Count the number of records matching the filter.
    /// </remarks>
    /// <param name="filter">查询条件 / Query condition</param>
    /// <typeparam name="TState">实现ICacheState接口的类型 / Type implementing ICacheState interface</typeparam>
    /// <returns>返回数据的数量 / Returns the count of data</returns>
    Task<long> CountAsync<TState>(Expression<Func<TState, bool>> filter) where TState : BaseCacheState, new();

    /// <summary>
    /// 查询数据数量。
    /// </summary>
    /// <remarks>
    /// Count the number of records matching the filter.
    /// </remarks>
    /// <param name="filter">查询条件 / Query condition</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <typeparam name="TState">实现ICacheState接口的类型 / Type implementing ICacheState interface</typeparam>
    /// <returns>返回数据的数量 / Returns the count of data</returns>
    Task<long> CountAsync<TState>(Expression<Func<TState, bool>> filter, CancellationToken cancellationToken) where TState : BaseCacheState, new();

    /// <summary>
    /// 查询数据数量。
    /// </summary>
    /// <remarks>
    /// Count the number of records matching the filter.
    /// </remarks>
    /// <param name="filter">查询条件 / Query condition</param>
    /// <param name="includeDeleted">是否包含已删除数据 / Whether to include deleted data</param>
    /// <typeparam name="TState">实现ICacheState接口的类型 / Type implementing ICacheState interface</typeparam>
    /// <returns>返回数据的数量 / Returns the count of data</returns>
    Task<long> CountAsync<TState>(Expression<Func<TState, bool>> filter, bool includeDeleted) where TState : BaseCacheState, new();

    /// <summary>
    /// 查询数据数量。
    /// </summary>
    /// <remarks>
    /// Count the number of records matching the filter.
    /// </remarks>
    /// <param name="filter">查询条件 / Query condition</param>
    /// <param name="includeDeleted">是否包含已删除数据 / Whether to include deleted data</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <typeparam name="TState">实现ICacheState接口的类型 / Type implementing ICacheState interface</typeparam>
    /// <returns>返回数据的数量 / Returns the count of data</returns>
    Task<long> CountAsync<TState>(Expression<Func<TState, bool>> filter, bool includeDeleted, CancellationToken cancellationToken) where TState : BaseCacheState, new();

    /// <summary>
    /// 删除数据。
    /// </summary>
    /// <remarks>
    /// Delete data matching the filter.
    /// </remarks>
    /// <param name="filter">查询条件 / Query condition</param>
    /// <typeparam name="TState">实现ICacheState接口的类型 / Type implementing ICacheState interface</typeparam>
    /// <returns>返回删除的条数 / Returns the number of deleted records</returns>
    Task<long> DeleteAsync<TState>(Expression<Func<TState, bool>> filter) where TState : BaseCacheState, new();

    /// <summary>
    /// 删除数据。
    /// </summary>
    /// <remarks>
    /// Delete data matching the filter.
    /// </remarks>
    /// <param name="filter">查询条件 / Query condition</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <typeparam name="TState">实现ICacheState接口的类型 / Type implementing ICacheState interface</typeparam>
    /// <returns>返回删除的条数 / Returns the number of deleted records</returns>
    Task<long> DeleteAsync<TState>(Expression<Func<TState, bool>> filter, CancellationToken cancellationToken) where TState : BaseCacheState, new();

    /// <summary>
    /// 删除数据。
    /// </summary>
    /// <remarks>
    /// Delete the specified data object.
    /// </remarks>
    /// <param name="state">数据对象 / Data object</param>
    /// <typeparam name="TState">实现ICacheState接口的类型 / Type implementing ICacheState interface</typeparam>
    /// <returns>返回删除的条数 / Returns the number of deleted records</returns>
    Task<long> DeleteAsync<TState>(TState state) where TState : BaseCacheState, new();

    /// <summary>
    /// 删除数据。
    /// </summary>
    /// <remarks>
    /// Delete the specified data object.
    /// </remarks>
    /// <param name="state">数据对象 / Data object</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <typeparam name="TState">实现ICacheState接口的类型 / Type implementing ICacheState interface</typeparam>
    /// <returns>返回删除的条数 / Returns the number of deleted records</returns>
    Task<long> DeleteAsync<TState>(TState state, CancellationToken cancellationToken) where TState : BaseCacheState, new();

    /// <summary>
    /// 根据条件物理删除数据。
    /// </summary>
    /// <remarks>
    /// Physically delete data by condition.
    /// </remarks>
    /// <param name="filter">查询条件表达式 / Query condition expression</param>
    /// <typeparam name="TState">数据类型,必须继承自BaseCacheState / Data type, must inherit from BaseCacheState</typeparam>
    /// <returns>返回删除的记录数 / Returns the number of deleted records</returns>
    Task<long> HardDeleteAsync<TState>(Expression<Func<TState, bool>> filter) where TState : BaseCacheState, new();

    /// <summary>
    /// 根据条件物理删除数据。
    /// </summary>
    /// <remarks>
    /// Physically delete data by condition.
    /// </remarks>
    /// <param name="filter">查询条件表达式 / Query condition expression</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <typeparam name="TState">数据类型,必须继承自BaseCacheState / Data type, must inherit from BaseCacheState</typeparam>
    /// <returns>返回删除的记录数 / Returns the number of deleted records</returns>
    Task<long> HardDeleteAsync<TState>(Expression<Func<TState, bool>> filter, CancellationToken cancellationToken) where TState : BaseCacheState, new();

    /// <summary>
    /// 根据条件恢复软删除数据。
    /// </summary>
    /// <remarks>
    /// Restore soft deleted data by condition.
    /// </remarks>
    /// <param name="filter">查询条件表达式 / Query condition expression</param>
    /// <typeparam name="TState">数据类型,必须继承自BaseCacheState / Data type, must inherit from BaseCacheState</typeparam>
    /// <returns>返回恢复的记录数 / Returns the number of restored records</returns>
    Task<long> RestoreAsync<TState>(Expression<Func<TState, bool>> filter) where TState : BaseCacheState, new();

    /// <summary>
    /// 根据条件恢复软删除数据。
    /// </summary>
    /// <remarks>
    /// Restore soft deleted data by condition.
    /// </remarks>
    /// <param name="filter">查询条件表达式 / Query condition expression</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <typeparam name="TState">数据类型,必须继承自BaseCacheState / Data type, must inherit from BaseCacheState</typeparam>
    /// <returns>返回恢复的记录数 / Returns the number of restored records</returns>
    Task<long> RestoreAsync<TState>(Expression<Func<TState, bool>> filter, CancellationToken cancellationToken) where TState : BaseCacheState, new();

    /// <summary>
    /// 根据条件批量删除数据(软删除)。
    /// </summary>
    /// <remarks>
    /// Batch delete data by condition (soft delete).
    /// </remarks>
    /// <param name="filter">查询条件表达式 / Query condition expression</param>
    /// <typeparam name="TState">数据类型,必须继承自BaseCacheState / Data type, must inherit from BaseCacheState</typeparam>
    /// <returns>返回修改的记录数 / Returns the number of modified records</returns>
    Task<long> DeleteListAsync<TState>(Expression<Func<TState, bool>> filter) where TState : BaseCacheState, new();

    /// <summary>
    /// 根据条件批量删除数据(软删除)。
    /// </summary>
    /// <remarks>
    /// Batch delete data by condition (soft delete).
    /// </remarks>
    /// <param name="filter">查询条件表达式 / Query condition expression</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <typeparam name="TState">数据类型,必须继承自BaseCacheState / Data type, must inherit from BaseCacheState</typeparam>
    /// <returns>返回修改的记录数 / Returns the number of modified records</returns>
    Task<long> DeleteListAsync<TState>(Expression<Func<TState, bool>> filter, CancellationToken cancellationToken) where TState : BaseCacheState, new();

    /// <summary>
    /// 根据ID列表批量删除数据(软删除)。
    /// </summary>
    /// <remarks>
    /// Batch delete data by ID list (soft delete).
    /// </remarks>
    /// <param name="ids">要删除的ID列表 / List of IDs to delete</param>
    /// <typeparam name="TState">数据类型,必须继承自BaseCacheState / Data type, must inherit from BaseCacheState</typeparam>
    /// <returns>返回修改的记录数 / Returns the number of modified records</returns>
    Task<long> DeleteListIdAsync<TState>(IEnumerable<long> ids) where TState : BaseCacheState, new();

    /// <summary>
    /// 根据ID列表批量删除数据(软删除)。
    /// </summary>
    /// <remarks>
    /// Batch delete data by ID list (soft delete).
    /// </remarks>
    /// <param name="ids">要删除的ID列表 / List of IDs to delete</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <typeparam name="TState">数据类型,必须继承自BaseCacheState / Data type, must inherit from BaseCacheState</typeparam>
    /// <returns>返回修改的记录数 / Returns the number of modified records</returns>
    Task<long> DeleteListIdAsync<TState>(IEnumerable<long> ids, CancellationToken cancellationToken) where TState : BaseCacheState, new();

    /// <summary>
    /// 保存数据。
    /// </summary>
    /// <remarks>
    /// Save data to the database.
    /// </remarks>
    /// <param name="state">数据对象 / Data object</param>
    /// <typeparam name="TState">实现ICacheState接口的类型 / Type implementing ICacheState interface</typeparam>
    /// <returns>异步任务 / Async task</returns>
    Task AddAsync<TState>(TState state) where TState : BaseCacheState, new();

    /// <summary>
    /// 保存数据。
    /// </summary>
    /// <remarks>
    /// Save data to the database.
    /// </remarks>
    /// <param name="state">数据对象 / Data object</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <typeparam name="TState">实现ICacheState接口的类型 / Type implementing ICacheState interface</typeparam>
    /// <returns>异步任务 / Async task</returns>
    Task AddAsync<TState>(TState state, CancellationToken cancellationToken) where TState : BaseCacheState, new();

    /// <summary>
    /// 增加或更新数据。
    /// </summary>
    /// <remarks>
    /// Add or update data.
    /// </remarks>
    /// <param name="state">数据对象 / Data object</param>
    /// <typeparam name="TState">实现ICacheState接口的类型 / Type implementing ICacheState interface</typeparam>
    /// <returns>修改后的数据对象 / The modified data object</returns>
    Task<TState> AddOrUpdateAsync<TState>(TState state) where TState : BaseCacheState, new();

    /// <summary>
    /// 增加或更新数据。
    /// </summary>
    /// <remarks>
    /// Add or update data.
    /// </remarks>
    /// <param name="state">数据对象 / Data object</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <typeparam name="TState">实现ICacheState接口的类型 / Type implementing ICacheState interface</typeparam>
    /// <returns>修改后的数据对象 / The modified data object</returns>
    Task<TState> AddOrUpdateAsync<TState>(TState state, CancellationToken cancellationToken) where TState : BaseCacheState, new();

    /// <summary>
    /// 批量增加或更新数据。
    /// </summary>
    /// <remarks>
    /// Batch add or update data.
    /// </remarks>
    /// <param name="states">数据对象集合 / Collection of data objects</param>
    /// <typeparam name="TState">实现ICacheState接口的类型 / Type implementing ICacheState interface</typeparam>
    /// <returns>返回处理的记录数 / Returns the number of processed records</returns>
    Task<long> AddOrUpdateListAsync<TState>(IEnumerable<TState> states) where TState : BaseCacheState, new();

    /// <summary>
    /// 批量增加或更新数据。
    /// </summary>
    /// <remarks>
    /// Batch add or update data.
    /// </remarks>
    /// <param name="states">数据对象集合 / Collection of data objects</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <typeparam name="TState">实现ICacheState接口的类型 / Type implementing ICacheState interface</typeparam>
    /// <returns>返回处理的记录数 / Returns the number of processed records</returns>
    Task<long> AddOrUpdateListAsync<TState>(IEnumerable<TState> states, CancellationToken cancellationToken) where TState : BaseCacheState, new();

    /// <summary>
    /// 按批次大小分批执行批量 upsert 保存（`_id` 等值过滤 + 整文档替换 + IsUpsert）。
    /// </summary>
    /// <remarks>
    /// Bulk-upserts the given states in batches of <paramref name="batchSize"/> (`_id` equality
    /// filter, whole-document replace, upsert). Each batch is acknowledged and exception-isolated
    /// independently: a failed batch is logged and execution continues with the remaining batches,
    /// and the returned list contains exactly the states of the acknowledged batches. Unlike
    /// <see cref="AddOrUpdateListAsync{TState}(IEnumerable{TState})"/> this contract does NOT touch
    /// state timestamps or <c>UpdateCount</c> — it persists the states exactly as handed in
    /// (migrated verbatim from <c>StateComponent.ExecuteBatchWritesAsync</c> to keep the
    /// shutdown-save path behaviour-identical).
    /// </remarks>
    /// <param name="states">待保存的状态集合 / The states to save</param>
    /// <param name="batchSize">每批数量（&lt;= 0 抛异常） / Batch size (values &lt;= 0 throw)</param>
    /// <typeparam name="TState">实现ICacheState接口的类型 / Type implementing ICacheState interface</typeparam>
    /// <returns>全部成功 ack 批次的状态列表 / The states of all acknowledged batches</returns>
    /// <exception cref="ArgumentOutOfRangeException">当 <paramref name="batchSize"/> &lt;= 0 时抛出 / Thrown when batchSize is &lt;= 0</exception>
    Task<IReadOnlyList<TState>> SaveBulkAsync<TState>(IEnumerable<TState> states, int batchSize) where TState : BaseCacheState, new();

    /// <summary>
    /// 批量保存数据。
    /// </summary>
    /// <remarks>
    /// Batch save multiple data objects.
    /// </remarks>
    /// <param name="states">数据对象集合 / Collection of data objects</param>
    /// <typeparam name="TState">实现ICacheState接口的类型 / Type implementing ICacheState interface</typeparam>
    /// <returns>异步任务 / Async task</returns>
    Task AddListAsync<TState>(IEnumerable<TState> states) where TState : BaseCacheState, new();

    /// <summary>
    /// 批量保存数据。
    /// </summary>
    /// <remarks>
    /// Batch save multiple data objects.
    /// </remarks>
    /// <param name="states">数据对象集合 / Collection of data objects</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <typeparam name="TState">实现ICacheState接口的类型 / Type implementing ICacheState interface</typeparam>
    /// <returns>异步任务 / Async task</returns>
    Task AddListAsync<TState>(IEnumerable<TState> states, CancellationToken cancellationToken) where TState : BaseCacheState, new();

    /// <summary>
    /// 保存数据,如果数据已经存在则更新,如果不存在则插入。
    /// </summary>
    /// <remarks>
    /// Save data, update if exists, insert if not.
    /// </remarks>
    /// <param name="state">数据对象 / Data object</param>
    /// <typeparam name="TState">实现ICacheState接口的类型 / Type implementing ICacheState interface</typeparam>
    /// <returns>返回更新后的数据对象 / Returns the updated data object</returns>
    Task<TState> UpdateAsync<TState>(TState state) where TState : BaseCacheState, new();

    /// <summary>
    /// 保存数据,如果数据已经存在则更新,如果不存在则插入。
    /// </summary>
    /// <remarks>
    /// Save data, update if exists, insert if not.
    /// </remarks>
    /// <param name="state">数据对象 / Data object</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <typeparam name="TState">实现ICacheState接口的类型 / Type implementing ICacheState interface</typeparam>
    /// <returns>返回更新后的数据对象 / Returns the updated data object</returns>
    Task<TState> UpdateAsync<TState>(TState state, CancellationToken cancellationToken) where TState : BaseCacheState, new();

    /// <summary>
    /// 批量更新数据。
    /// </summary>
    /// <remarks>
    /// Batch update multiple data objects.
    /// </remarks>
    /// <param name="stateList">数据列表对象 / List of data objects</param>
    /// <returns>返回更新成功的数量 / Returns the number of successfully updated records</returns>
    Task<long> UpdateAsync<TState>(IEnumerable<TState> stateList) where TState : BaseCacheState, new();

    /// <summary>
    /// 批量更新数据。
    /// </summary>
    /// <remarks>
    /// Batch update multiple data objects.
    /// </remarks>
    /// <param name="stateList">数据列表对象 / List of data objects</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>返回更新成功的数量 / Returns the number of successfully updated records</returns>
    Task<long> UpdateAsync<TState>(IEnumerable<TState> stateList, CancellationToken cancellationToken) where TState : BaseCacheState, new();

    /// <summary>
    /// 根据ID部分更新数据。
    /// </summary>
    /// <remarks>
    /// Partially update data by ID.
    /// </remarks>
    /// <param name="id">数据ID / Data ID</param>
    /// <param name="updateFields">更新字段集合 / Update field dictionary</param>
    /// <typeparam name="TState">实现ICacheState接口的类型 / Type implementing ICacheState interface</typeparam>
    /// <returns>返回更新成功的数量 / Returns the number of successfully updated records</returns>
    Task<long> UpdatePartialAsync<TState>(long id, IReadOnlyDictionary<string, object> updateFields) where TState : BaseCacheState, new();

    /// <summary>
    /// 根据ID部分更新数据。
    /// </summary>
    /// <remarks>
    /// Partially update data by ID.
    /// </remarks>
    /// <param name="id">数据ID / Data ID</param>
    /// <param name="updateFields">更新字段集合 / Update field dictionary</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <typeparam name="TState">实现ICacheState接口的类型 / Type implementing ICacheState interface</typeparam>
    /// <returns>返回更新成功的数量 / Returns the number of successfully updated records</returns>
    Task<long> UpdatePartialAsync<TState>(long id, IReadOnlyDictionary<string, object> updateFields, CancellationToken cancellationToken) where TState : BaseCacheState, new();

    /// <summary>
    /// 在事务中执行操作。
    /// </summary>
    /// <remarks>
    /// Execute operation in a transaction.
    /// </remarks>
    /// <param name="action">事务内执行逻辑 / Action to execute within transaction</param>
    /// <returns>异步任务 / Async task</returns>
    Task ExecuteInTransactionAsync(Func<Task> action);

    /// <summary>
    /// 在事务中执行操作。
    /// </summary>
    /// <remarks>
    /// Execute operation in a transaction.
    /// </remarks>
    /// <param name="action">事务内执行逻辑 / Action to execute within transaction</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>异步任务 / Async task</returns>
    Task ExecuteInTransactionAsync(Func<Task> action, CancellationToken cancellationToken);

    /// <summary>
    /// 根据ID判断数据是否存在。
    /// </summary>
    /// <remarks>
    /// Determines whether data exists by ID.
    /// </remarks>
    /// <param name="id">数据ID / Data ID</param>
    /// <typeparam name="TState">实现ICacheState接口的类型 / Type implementing ICacheState interface</typeparam>
    /// <returns>返回是否存在值,true表示存在，false表示不存在 / Returns whether data exists, true if exists, false if not</returns>
    Task<bool> ExistsByIdAsync<TState>(long id) where TState : BaseCacheState, new();

    /// <summary>
    /// 根据ID判断数据是否存在。
    /// </summary>
    /// <remarks>
    /// Determines whether data exists by ID.
    /// </remarks>
    /// <param name="id">数据ID / Data ID</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <typeparam name="TState">实现ICacheState接口的类型 / Type implementing ICacheState interface</typeparam>
    /// <returns>返回是否存在值,true表示存在，false表示不存在 / Returns whether data exists, true if exists, false if not</returns>
    Task<bool> ExistsByIdAsync<TState>(long id, CancellationToken cancellationToken) where TState : BaseCacheState, new();

    /// <summary>
    /// 查询符合条件的数据是否存在。
    /// </summary>
    /// <remarks>
    /// Check if any data matching the condition exists.
    /// </remarks>
    /// <param name="filter">查询条件 / Query condition</param>
    /// <typeparam name="TState">实现ICacheState接口的类型 / Type implementing ICacheState interface</typeparam>
    /// <returns>返回是否存在值,true表示存在，false表示不存在 / Returns whether data exists, true if exists, false if not</returns>
    Task<bool> AnyAsync<TState>(Expression<Func<TState, bool>> filter) where TState : BaseCacheState, new();

    /// <summary>
    /// 查询符合条件的数据是否存在。
    /// </summary>
    /// <remarks>
    /// Check if any data matching the condition exists.
    /// </remarks>
    /// <param name="filter">查询条件 / Query condition</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <typeparam name="TState">实现ICacheState接口的类型 / Type implementing ICacheState interface</typeparam>
    /// <returns>返回是否存在值,true表示存在，false表示不存在 / Returns whether data exists, true if exists, false if not</returns>
    Task<bool> AnyAsync<TState>(Expression<Func<TState, bool>> filter, CancellationToken cancellationToken) where TState : BaseCacheState, new();

    /// <summary>
    /// 条件写：仅当存量文档的行版本号与期望值一致时整档写入（CAS / 乐观锁原语）。
    /// </summary>
    /// <remarks>
    /// Conditional write: replaces the whole document only when the stored <c>RowVersion</c> equals
    /// <paramref name="expectedRowVersion"/> (compare-and-swap / optimistic-lock primitive).
    /// Concurrency semantics (business-rule level, enforced by all providers):
    /// <list type="bullet">
    /// <item><description>成功时写入的文档携带 <c>RowVersion = expectedRowVersion + 1</c>（递增走
    /// <c>VersionedEntityHelper.IncrementRowVersion</c> 的 checked 语义，<c>long.MaxValue</c> 溢出抛出而非静默回绕）/
    /// On success the stored document carries <c>RowVersion = expectedRowVersion + 1</c> (the increment uses
    /// <c>VersionedEntityHelper.IncrementRowVersion</c>'s checked semantics — <c>long.MaxValue</c> overflows
    /// instead of silently wrapping)</description></item>
    /// <item><description>失败（<c>false</c>）路径不污染调用方对象：写前的临时版本递增在返回前回滚，
    /// <paramref name="state"/> 的 <c>RowVersion</c> 保持调用前的值；异常路径不回滚（写结果未知）/
    /// The <c>false</c> path does not pollute the caller's object: the temporary pre-write increment is
    /// rolled back before returning, so <paramref name="state"/>'s <c>RowVersion</c> keeps its pre-call value;
    /// exceptions do not roll back (the write outcome is unknown)</description></item>
    /// <item><description>版本不符或行缺失返回 <c>false</c>，不抛异常（并发竞态下双写仅一个成功）/
    /// A version mismatch or a missing row returns <c>false</c> without throwing (under a race exactly one of two writers succeeds)</description></item>
    /// <item><description>存量文档缺失 <c>RowVersion</c> 字段或值为 null 时恒不匹配 /
    /// A stored document whose <c>RowVersion</c> is missing or null never matches</description></item>
    /// <item><description>本原语按原样持久化传入状态（不触碰 <c>UpdateTime</c>/<c>UpdateCount</c>，对齐
    /// <see cref="SaveBulkAsync{TState}(IEnumerable{TState}, int)"/> 的口径），调用方自管审计字段 /
    /// The state is persisted exactly as handed in (<c>UpdateTime</c>/<c>UpdateCount</c> are NOT touched, aligned
    /// with <see cref="SaveBulkAsync{TState}(IEnumerable{TState}, int)"/>) — the caller owns the audit fields</description></item>
    /// </list>
    /// </remarks>
    /// <param name="id">数据ID（须与 <paramref name="state"/> 的 Id 一致）/ The document ID (must equal <paramref name="state"/>'s Id)</param>
    /// <param name="expectedRowVersion">期望的存量行版本号 / The expected stored row version</param>
    /// <param name="state">要写入的新状态对象 / The new state to write</param>
    /// <typeparam name="TState">实现ICacheState接口的类型 / Type implementing ICacheState interface</typeparam>
    /// <returns>版本匹配并写入返回 <c>true</c>；版本不符或行缺失返回 <c>false</c> / <c>true</c> when the version matched and the document was written; <c>false</c> on mismatch or missing row</returns>
    /// <exception cref="ArgumentException">当 <paramref name="state"/> 的 Id 与 <paramref name="id"/> 不一致时抛出 / Thrown when <paramref name="state"/>'s Id differs from <paramref name="id"/></exception>
    /// <exception cref="OverflowException">当 <paramref name="expectedRowVersion"/> 为 <c>long.MaxValue</c> 且版本匹配成功时抛出（checked 递增）/ Thrown when <paramref name="expectedRowVersion"/> is <c>long.MaxValue</c> and the version matches (checked increment)</exception>
    Task<bool> UpdateIfRowVersionAsync<TState>(long id, long expectedRowVersion, TState state) where TState : BaseCacheState, new();

    /// <summary>
    /// 条件写：仅当存量文档的行版本号与期望值一致时整档写入（CAS / 乐观锁原语）。
    /// </summary>
    /// <remarks>
    /// Conditional write: replaces the whole document only when the stored <c>RowVersion</c> equals
    /// <paramref name="expectedRowVersion"/> (compare-and-swap / optimistic-lock primitive). See the
    /// cancellation-free overload for the full concurrency contract.
    /// </remarks>
    /// <param name="id">数据ID（须与 <paramref name="state"/> 的 Id 一致）/ The document ID (must equal <paramref name="state"/>'s Id)</param>
    /// <param name="expectedRowVersion">期望的存量行版本号 / The expected stored row version</param>
    /// <param name="state">要写入的新状态对象 / The new state to write</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <typeparam name="TState">实现ICacheState接口的类型 / Type implementing ICacheState interface</typeparam>
    /// <returns>版本匹配并写入返回 <c>true</c>；版本不符或行缺失返回 <c>false</c> / <c>true</c> when the version matched and the document was written; <c>false</c> on mismatch or missing row</returns>
    /// <exception cref="ArgumentException">当 <paramref name="state"/> 的 Id 与 <paramref name="id"/> 不一致时抛出 / Thrown when <paramref name="state"/>'s Id differs from <paramref name="id"/></exception>
    /// <exception cref="OverflowException">当 <paramref name="expectedRowVersion"/> 为 <c>long.MaxValue</c> 且版本匹配成功时抛出（checked 递增）/ Thrown when <paramref name="expectedRowVersion"/> is <c>long.MaxValue</c> and the version matches (checked increment)</exception>
    Task<bool> UpdateIfRowVersionAsync<TState>(long id, long expectedRowVersion, TState state, CancellationToken cancellationToken) where TState : BaseCacheState, new();

    /// <summary>
    /// 唯一性插入：仅当无既有文档满足唯一性条件时插入（冲突返回 false 而非异常）。
    /// </summary>
    /// <remarks>
    /// Unique insert: inserts <paramref name="state"/> only when no existing document satisfies
    /// <paramref name="uniqueFilter"/> (a conflict returns <c>false</c> instead of throwing).
    /// Concurrency semantics (business-rule level, enforced by all providers):
    /// <list type="bullet">
    /// <item><description>唯一性判定无视软删可见性（物理唯一索引口径，软删行同样占用唯一键）/
    /// Uniqueness ignores soft-delete visibility (the physical unique-index scope — soft-deleted rows still occupy the key)</description></item>
    /// <item><description>唯一键命中或 <c>Id</c> 已占用返回 <c>false</c>；时间戳补齐对齐 <see cref="AddAsync{TState}(TState)"/> /
    /// A unique-key hit or an already-occupied <c>Id</c> returns <c>false</c>; timestamps are filled like <see cref="AddAsync{TState}(TState)"/></description></item>
    /// <item><description>Mongo/PostgreSQL 的完全竞态安全依赖 <c>EntityIndexAttribute(Unique = true)</c> 声明的
    /// provider 唯一索引（先查后插仅兜底顺序冲突）；SQLite 写事务内查插，天然串行安全 /
    /// Full race safety on Mongo/PostgreSQL relies on the provider unique index declared via
    /// <c>EntityIndexAttribute(Unique = true)</c> (the pre-check only covers sequential conflicts);
    /// SQLite checks and inserts inside its write transaction and is serialized by construction</description></item>
    /// </list>
    /// </remarks>
    /// <param name="state">要插入的数据对象 / The state to insert</param>
    /// <param name="uniqueFilter">唯一性判定条件（对既有文档求值）/ The uniqueness predicate (evaluated against existing documents)</param>
    /// <typeparam name="TState">实现ICacheState接口的类型 / Type implementing ICacheState interface</typeparam>
    /// <returns>插入成功返回 <c>true</c>；唯一键冲突或 Id 已占用返回 <c>false</c> / <c>true</c> when inserted; <c>false</c> on a unique-key or Id conflict</returns>
    Task<bool> TryAddUniqueAsync<TState>(TState state, Expression<Func<TState, bool>> uniqueFilter) where TState : BaseCacheState, new();

    /// <summary>
    /// 唯一性插入：仅当无既有文档满足唯一性条件时插入（冲突返回 false 而非异常）。
    /// </summary>
    /// <remarks>
    /// Unique insert: inserts <paramref name="state"/> only when no existing document satisfies
    /// <paramref name="uniqueFilter"/> (a conflict returns <c>false</c> instead of throwing). See the
    /// cancellation-free overload for the full concurrency contract.
    /// </remarks>
    /// <param name="state">要插入的数据对象 / The state to insert</param>
    /// <param name="uniqueFilter">唯一性判定条件（对既有文档求值）/ The uniqueness predicate (evaluated against existing documents)</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <typeparam name="TState">实现ICacheState接口的类型 / Type implementing ICacheState interface</typeparam>
    /// <returns>插入成功返回 <c>true</c>；唯一键冲突或 Id 已占用返回 <c>false</c> / <c>true</c> when inserted; <c>false</c> on a unique-key or Id conflict</returns>
    Task<bool> TryAddUniqueAsync<TState>(TState state, Expression<Func<TState, bool>> uniqueFilter, CancellationToken cancellationToken) where TState : BaseCacheState, new();

    /// <summary>
    /// 获取指定状态类型命名序列的下一个值（原子单调递增）。
    /// </summary>
    /// <remarks>
    /// Atomically increments and returns the next value of the named sequence for
    /// <typeparamref name="TState"/> (the counter key is <c>typeof(TState).Name + ":" + sequenceName</c>,
    /// so equal sequence names under different state types never collide). Concurrency semantics
    /// (business-rule level, enforced by all providers):
    /// <list type="bullet">
    /// <item><description>首次调用返回 1，此后单调递增；并发调用永不重复、永不回退 /
    /// The first call returns 1 and the value then increases monotonically; concurrent calls never
    /// hand out a duplicate and never regress</description></item>
    /// <item><description>失败重试可能产生空洞（某次尝试的值从未返回给调用方）/
    /// A retried failed attempt may leave a gap (that attempt's value was never returned to a caller)</description></item>
    /// </list>
    /// </remarks>
    /// <param name="sequenceName">序列名（与状态类型名联合键控）/ The sequence name (keyed together with the state type name)</param>
    /// <typeparam name="TState">实现ICacheState接口的类型 / Type implementing ICacheState interface</typeparam>
    /// <returns>序列的新值（首次为 1）/ The new sequence value (1 on first use)</returns>
    /// <exception cref="ArgumentException">当 <paramref name="sequenceName"/> 为 null 或空白时抛出 / Thrown when <paramref name="sequenceName"/> is null or whitespace</exception>
    Task<long> NextSequenceAsync<TState>(string sequenceName) where TState : BaseCacheState, new();

    /// <summary>
    /// 获取指定状态类型命名序列的下一个值（原子单调递增）。
    /// </summary>
    /// <remarks>
    /// Atomically increments and returns the next value of the named sequence for
    /// <typeparamref name="TState"/>. See the cancellation-free overload for the full concurrency contract.
    /// </remarks>
    /// <param name="sequenceName">序列名（与状态类型名联合键控）/ The sequence name (keyed together with the state type name)</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <typeparam name="TState">实现ICacheState接口的类型 / Type implementing ICacheState interface</typeparam>
    /// <returns>序列的新值（首次为 1）/ The new sequence value (1 on first use)</returns>
    /// <exception cref="ArgumentException">当 <paramref name="sequenceName"/> 为 null 或空白时抛出 / Thrown when <paramref name="sequenceName"/> is null or whitespace</exception>
    Task<long> NextSequenceAsync<TState>(string sequenceName, CancellationToken cancellationToken) where TState : BaseCacheState, new();
}
