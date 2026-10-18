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


using GameFrameX.DataBase.Abstractions;
using GameFrameX.Foundation.Logger;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Localization;
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
    /// 替换选项，用于替换文档。设置
    /// <see>
    ///     <cref>IsUpsert</cref>
    /// </see>
    /// 属性为 true 可以在文档不存在时插入新文档。
    /// </summary>
    /// <remarks>
    /// Replace options for replacing documents. Setting the
    /// <see>
    ///     <cref>IsUpsert</cref>
    /// </see>
    /// property to true enables inserting a new document if it doesn't exist.
    /// </remarks>
    public static readonly ReplaceOptions ReplaceOptions = new ReplaceOptions { IsUpsert = true };

    /// <summary>
    /// 增加或更新数据（使用 Upsert 优化，单次数据库操作）。
    /// </summary>
    /// <remarks>
    /// Adds or updates data (using Upsert optimization, single database operation).
    /// </remarks>
    /// <typeparam name="TState">数据类型，必须继承自 BaseCacheState / Data type, must inherit from BaseCacheState</typeparam>
    /// <param name="state">数据对象 / Data object</param>
    /// <returns>返回增加或更新后的数据对象 / The added or updated data object</returns>
    public async Task<TState> AddOrUpdateAsync<TState>(TState state) where TState : BaseCacheState, new()
    {
        return await AddOrUpdateAsync(state, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 增加或更新数据（使用 Upsert 优化，单次数据库操作）。
    /// </summary>
    /// <remarks>
    /// Adds or updates data (using Upsert optimization, single database operation).
    /// </remarks>
    /// <typeparam name="TState">数据类型，必须继承自 BaseCacheState / Data type, must inherit from BaseCacheState</typeparam>
    /// <param name="state">数据对象 / Data object</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>返回增加或更新后的数据对象 / The added or updated data object</returns>
    public async Task<TState> AddOrUpdateAsync<TState>(TState state, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();

        var currentTime = GetCurrentTimestamp();

        // 如果是新对象（没有创建时间），设置创建时间
        if (state.CreatedTime == 0)
        {
            state.CreatedTime = currentTime;
        }

        state.UpdateTime = currentTime;
        state.UpdateCount = (state.UpdateCount ?? 0) + 1;

        // 写库前捕获字节快照（bump 后），ack 后按该快照精确提交基线（C263），
        // 避免参数less 提交读到并发检查推进后的未落库 ToSaveHash。
        var persistedData = state.ToBytes();

        // 使用 ReplaceOne with Upsert - 单次数据库操作；类型化集合入口需先完成 ClassMap 懒注册
        // (typed collection entries need lazy ClassMap registration first).
        MongoSerializationRegistry.EnsureClassMapRegistered<TState>();
        var filter = Builders<TState>.Filter.Eq(m => m.Id, state.Id);
        await ExecuteWriteWithRetryAsync(token => ReplaceOneWithAmbientSessionAsync(CurrentDatabase.GetCollection<TState>(typeof(TState).Name), filter, state, ReplaceOptions, token), cancellationToken, nameof(AddOrUpdateAsync), true).ConfigureAwait(false);

        state.SaveToDbPostHandler(persistedData);
        return state;
    }

    /// <summary>
    /// 批量增加或更新数据（使用 Upsert 优化，批量数据库操作）。
    /// </summary>
    /// <remarks>
    /// Batch add or update data (using Upsert optimization, bulk database operation).
    /// </remarks>
    /// <typeparam name="TState">数据类型，必须继承自 BaseCacheState / Data type, must inherit from BaseCacheState</typeparam>
    /// <param name="states">数据对象集合 / Collection of data objects</param>
    /// <returns>返回实际确认的记录数 / The actually acknowledged record count</returns>
    public async Task<long> AddOrUpdateListAsync<TState>(IEnumerable<TState> states) where TState : BaseCacheState, new()
    {
        return await AddOrUpdateListAsync(states, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 批量增加或更新数据（使用 Upsert 优化，批量数据库操作；无序 BulkWrite 以整批计数判定确认面）。
    /// </summary>
    /// <remarks>
    /// Batch add or update data (using Upsert optimization, bulk database operation). The unordered
    /// <see cref="MongoDbService.BulkWriteOptions"/> batch may leave some documents unwritten without throwing
    /// (IsAcknowledged stays true), so the acknowledged surface is decided by batch-level counts: only a fully
    /// acknowledged batch commits <c>SaveToDbPostHandler</c> for every state and returns the batch size; a
    /// partially acknowledged batch commits no baseline (unwritten documents stay dirty and re-save next
    /// round) and returns the actually acknowledged count; an unacknowledged batch returns 0.
    /// </remarks>
    /// <typeparam name="TState">数据类型，必须继承自 BaseCacheState / Data type, must inherit from BaseCacheState</typeparam>
    /// <param name="states">数据对象集合 / Collection of data objects</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>返回实际确认的记录数 / The actually acknowledged record count</returns>
    public async Task<long> AddOrUpdateListAsync<TState>(IEnumerable<TState> states, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();
        var stateArray = states as TState[] ?? states?.ToArray();
        if (stateArray == null || stateArray.Length == 0)
        {
            return 0;
        }

        var currentTime = GetCurrentTimestamp();
        // 类型化集合入口需先完成 ClassMap 懒注册（与 MongoDbContext.GetCollection 合起来覆盖全部路径）。
        // Typed collection entries need lazy ClassMap registration first (with MongoDbContext.GetCollection covering the rest).
        MongoSerializationRegistry.EnsureClassMapRegistered<TState>();
        var collection = CurrentDatabase.GetCollection<TState>(typeof(TState).Name);
        var writeModels = new List<WriteModel<TState>>(stateArray.Length);
        // 写库前逐个捕获字节快照（bump 后），ack 后按快照精确提交基线（C263）
        var persistedSnapshots = new List<(TState State, byte[] Data)>(stateArray.Length);
        foreach (var state in stateArray)
        {
            if (state.CreatedTime == 0)
            {
                state.CreatedTime = currentTime;
            }

            state.UpdateTime = currentTime;
            state.UpdateCount = (state.UpdateCount ?? 0) + 1;
            var filter = Builders<TState>.Filter.Eq(m => m.Id, state.Id);
            writeModels.Add(new ReplaceOneModel<TState>(filter, state) { IsUpsert = true, });
            persistedSnapshots.Add((state, state.ToBytes()));
        }

        var result = await ExecuteWriteWithRetryAsync(token => BulkWriteWithAmbientSessionAsync(collection, writeModels, BulkWriteOptions, token), cancellationToken, nameof(AddOrUpdateListAsync), true).ConfigureAwait(false);
        if (result.IsAcknowledged)
        {
            // 无序 BulkWrite 部分文档失败不必然抛异常（IsAcknowledged 仍 true）：以整批计数判定真实确认面。
            // An unordered BulkWrite may leave some documents unwritten without throwing (IsAcknowledged
            // stays true): the batch-level counts define the actually acknowledged surface.
            var acknowledgedCount = result.MatchedCount + result.Upserts.Count;
            if (IsFullyAcknowledged(result, stateArray.Length))
            {
                foreach (var (state, persistedData) in persistedSnapshots)
                {
                    state.SaveToDbPostHandler(persistedData);
                }

                return stateArray.Length;
            }

            // 部分确认：全批不提交哈希基线（未落库文档保持脏、下轮回存），返回实际确认数。
            // Partially acknowledged: commit no hash baseline for the batch (unwritten documents stay dirty
            // and re-save next round) and return the actually acknowledged count.
            // Localization: Database.Mongo.BulkWritePartialAcknowledged - MongoDbService.{0} 无序批量写部分确认。状态名：{1}，期望：{2}，实际确认：{3}；未确认文档不提交哈希基线
            LogHelper.Warning(LocalizationService.GetString(Localization.Keys.Database.Mongo.BulkWritePartialAcknowledged, nameof(AddOrUpdateListAsync), typeof(TState).Name, stateArray.Length, acknowledgedCount));
            return acknowledgedCount;
        }

        return 0;
    }

    /// <summary>
    /// 判定无序 BulkWrite 整批确认（ReplaceOne+upsert 批次口径：RequestCount 与 Matched+Upserts 均达批大小）。
    /// </summary>
    /// <remarks>
    /// Determines whether an unordered BulkWrite batch of ReplaceOne-with-upsert models is fully
    /// acknowledged: processed requests and matched+upserted documents must both reach the batch size.
    /// Partial document failures (which keep <c>IsAcknowledged</c> true without throwing) fail this check.
    /// </remarks>
    /// <param name="result">BulkWrite 结果 / The bulk write result</param>
    /// <param name="expectedCount">批大小 / The expected batch size</param>
    /// <returns>整批确认返回 true / True when the whole batch is acknowledged</returns>
    private static bool IsFullyAcknowledged(BulkWriteResult result, int expectedCount)
    {
        return result.IsAcknowledged
               && result.RequestCount == expectedCount
               && result.MatchedCount + result.Upserts.Count == expectedCount;
    }

    /// <summary>
    /// 在事务中执行操作。
    /// </summary>
    /// <remarks>
    /// Execute operation in a transaction: the shell's session becomes the ambient session, every
    /// collection operation issued by the action joins the transaction, and the shell owns the single
    /// commit. On a standalone (non-replica-set) server the shell degrades to a single non-transactional
    /// pass (the ambient session is cleared first, so the action never executes twice).
    /// </remarks>
    /// <param name="action">事务内执行逻辑 / Action to execute within transaction</param>
    /// <returns>表示异步操作的任务 / Task representing asynchronous operation</returns>
    public async Task ExecuteInTransactionAsync(Func<Task> action)
    {
        await ExecuteInTransactionAsync(action, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 在事务中执行操作。
    /// </summary>
    /// <remarks>
    /// Execute operation in a transaction: the shell's session becomes the ambient session, every
    /// collection operation issued by the action joins the transaction, and the shell owns the single
    /// commit. On a standalone (non-replica-set) server the shell degrades to a single non-transactional
    /// pass (the ambient session is cleared first, so the action never executes twice).
    /// </remarks>
    /// <param name="action">事务内执行逻辑 / Action to execute within transaction</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>表示异步操作的任务 / Task representing asynchronous operation</returns>
    public async Task ExecuteInTransactionAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();
        ArgumentNullException.ThrowIfNull(action);
        if (_ambientSession.Value != null)
        {
            // 嵌套事务壳：加入环境会话事务（提交权归最外层壳）。
            // Nested shell: join the ambient session's transaction (the outermost shell owns the commit).
            await action().ConfigureAwait(false);
            return;
        }

        Exception lastException = null;
        for (var attempt = 0; attempt <= _transactionRetryDelaysMilliseconds.Length; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var session = await _mongoClient.StartSessionAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                session.StartTransaction();
                _ambientSession.Value = session;
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await action().ConfigureAwait(false);
                    await CommitTransactionWithRetryAsync(session, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    _ambientSession.Value = null;
                }

                return;
            }
            catch (Exception exception) when (IsStandaloneTransactionUnsupported(exception))
            {
                cancellationToken.ThrowIfCancellationRequested();
                // standalone（非副本集）回退：环境会话已清空，单次直通执行（首轮 session 绑定操作在 standalone 上已失败，无双写）。
                // Standalone (non-replica-set) fallback: the ambient session is already cleared, so this
                // single pass is the only execution (the session-bound first attempt failed outright).
                await action().ConfigureAwait(false);
                return;
            }
            catch (Exception exception) when (ExceptionClassifier.ShouldRetryTransaction(exception) && attempt < _transactionRetryDelaysMilliseconds.Length)
            {
                lastException = exception;
                var delay = _transactionRetryDelaysMilliseconds[attempt] + Random.Shared.Next(0, 120);
                // Localization: Database.Mongo.ExecuteInTransactionTransientError - MongoDbService.ExecuteInTransactionAsync 瞬时错误，重试 {0}/{1}。error={2}
                LogHelper.Warning(LocalizationService.GetString(Localization.Keys.Database.Mongo.ExecuteInTransactionTransientError, attempt + 1, _transactionRetryDelaysMilliseconds.Length, exception.Message));
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                lastException = exception;
                break;
            }
        }

        // Localization: Database.MongoDb.ExecuteInTransactionFailed - ExecuteInTransactionAsync重试失败，未知异常
        throw new DatabaseUnavailableException(LocalizationService.GetString(Keys.Database.MongoDbExecuteInTransactionFailed), lastException ?? new InvalidOperationException(LocalizationService.GetString(Keys.Database.MongoDbExecuteInTransactionFailed)));
    }

    /// <summary>
    /// 判定异常是否为 standalone（非副本集/非 mongos）不支持多文档事务的错误（客户端侧拓扑拒绝与服务端拒绝双形态）。
    /// </summary>
    /// <remarks>
    /// Detects the standalone-server multi-document-transaction rejection in both shapes: the
    /// driver's client-side topology check (<c>NotSupportedException: Standalone servers do not
    /// support transactions</c>, raised before any server round-trip) and the server-side rejection
    /// ("Transaction numbers are only allowed on a replica set member or mongos") surfaced by the
    /// first session-bound operation or the commit.
    /// </remarks>
    /// <param name="exception">待判定异常 / The exception to classify</param>
    /// <returns>是 standalone 事务不支持错误返回 true / True when the error is the standalone transaction rejection</returns>
    private static bool IsStandaloneTransactionUnsupported(Exception exception)
    {
        return (exception is NotSupportedException && exception.Message.Contains("Standalone servers do not support transactions", StringComparison.Ordinal))
            || (exception is MongoServerException && exception.Message.Contains("Transaction numbers are only allowed on a replica set member or mongos", StringComparison.Ordinal));
    }

    /// <summary>
    /// 带重试机制的事务提交方法。
    /// </summary>
    /// <remarks>
    /// Commits a transaction with automatic retry mechanism on transient failures.
    /// Uses exponential backoff with jitter for retry delays.
    /// </remarks>
    /// <param name="session">MongoDB 客户端会话句柄 / MongoDB client session handle</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>表示异步操作的任务 / Task representing asynchronous operation</returns>
    /// <exception cref="DatabaseUnavailableException">当所有重试都失败后抛出 / Thrown when all retry attempts fail</exception>
    private async Task CommitTransactionWithRetryAsync(IClientSessionHandle session, CancellationToken cancellationToken)
    {
        Exception lastException = null;
        for (var attempt = 0; attempt <= _transactionRetryDelaysMilliseconds.Length; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await session.CommitTransactionAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception exception) when (ExceptionClassifier.ShouldRetryTransactionCommit(exception) && attempt < _transactionRetryDelaysMilliseconds.Length)
            {
                lastException = exception;
                var delay = _transactionRetryDelaysMilliseconds[attempt] + Random.Shared.Next(0, 120);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                lastException = exception;
                break;
            }
        }

        // Localization: Database.MongoDb.CommitTransactionFailed - CommitTransaction重试失败，未知异常
        throw new DatabaseUnavailableException(LocalizationService.GetString(Keys.Database.MongoDbCommitTransactionFailed), lastException ?? new InvalidOperationException(LocalizationService.GetString(Keys.Database.MongoDbCommitTransactionFailed)));
    }
}
