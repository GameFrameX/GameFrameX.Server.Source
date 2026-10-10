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


using GameFrameX.DataBase.Abstractions;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Foundation.Logger;
using GameFrameX.Localization;
using Microsoft.EntityFrameworkCore;

namespace GameFrameX.DataBase.PostgreSql;

public sealed partial class PostgreSqlDbService
{
    /// <summary>
    /// 增加或更新数据（整文档替换 upsert，对齐 Mongo ReplaceOne+IsUpsert；主键并发竞争自动重试一次）。
    /// </summary>
    /// <remarks>
    /// Adds or updates a single document (whole-document replacement, aligned with Mongo's ReplaceOne + IsUpsert
    /// semantics). The insert-or-update decision is not a single atomic statement under EF, so a concurrent
    /// insert racing the existence probe surfaces as a unique-key violation (SQLSTATE 23505) which is retried
    /// once via reload-and-replace — converging to the last-writer-wins outcome of the former
    /// <c>INSERT ... ON CONFLICT DO UPDATE</c>.
    /// </remarks>
    /// <typeparam name="TState">状态类型 / The state type</typeparam>
    /// <param name="state">要增加或更新的状态对象 / The state object to add or update</param>
    /// <returns>已持久化的状态对象 / The persisted state object</returns>
    public async Task<TState> AddOrUpdateAsync<TState>(TState state) where TState : BaseCacheState, new()
    {
        return await AddOrUpdateAsync(state, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 增加或更新数据（整文档替换 upsert，对齐 Mongo ReplaceOne+IsUpsert；主键并发竞争自动重试一次）。
    /// </summary>
    /// <remarks>
    /// Adds or updates a single document (whole-document replacement, aligned with Mongo's ReplaceOne + IsUpsert
    /// semantics). The insert-or-update decision is not a single atomic statement under EF, so a concurrent
    /// insert racing the existence probe surfaces as a unique-key violation (SQLSTATE 23505) which is retried
    /// once via reload-and-replace — converging to the last-writer-wins outcome of the former
    /// <c>INSERT ... ON CONFLICT DO UPDATE</c>.
    /// </remarks>
    /// <typeparam name="TState">状态类型 / The state type</typeparam>
    /// <param name="state">要增加或更新的状态对象 / The state object to add or update</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>已持久化的状态对象 / The persisted state object</returns>
    public async Task<TState> AddOrUpdateAsync<TState>(TState state, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();

        var currentTime = GetCurrentTimestamp();

        // 如果是新对象（没有创建时间），设置创建时间（对齐 Mongo）
        if (state.CreatedTime == 0)
        {
            state.CreatedTime = currentTime;
        }

        state.UpdateTime = currentTime;
        state.UpdateCount = (state.UpdateCount ?? 0) + 1;

        await ExecuteWriteWithRetryAsync(async token =>
        {
            await EnsureTableAsync<TState>(token).ConfigureAwait(false);
            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    using var context = CreateContext<TState>();
                    await StageUpsertAsync(context, new[] { state, }, token).ConfigureAwait(false);
                    await context.SaveChangesAsync(token).ConfigureAwait(false);
                    return true;
                }
                catch (DbUpdateException exception) when (attempt == 0 && PostgreSqlSqlState.IsUniqueViolation(exception))
                {
                    // 并发插入同 id 竞争失败：重载后按更新收敛（对齐原 upsert 的 last-writer-wins）
                    // Lost the insert race for the same id: reload and converge as an update (former upsert's last-writer-wins).
                }
            }
            return true;
        }, cancellationToken, nameof(AddOrUpdateAsync), true).ConfigureAwait(false);

        state.SaveToDbPostHandler();
        return state;
    }

    /// <summary>
    /// 批量增加或更新数据（单次 SaveChanges 原子批次；返回处理记录数，对齐 Mongo BulkWrite ReplaceOne 语义）。
    /// </summary>
    /// <remarks>
    /// Bulk adds or updates documents as one atomic <c>SaveChanges</c> batch (whole-document replacement);
    /// returns the number of processed records, aligned with Mongo's BulkWrite ReplaceOne semantics.
    /// </remarks>
    /// <typeparam name="TState">状态类型 / The state type</typeparam>
    /// <param name="states">要增加或更新的状态集合 / The states to add or update</param>
    /// <returns>处理的记录数 / The number of processed records</returns>
    public async Task<long> AddOrUpdateListAsync<TState>(IEnumerable<TState> states) where TState : BaseCacheState, new()
    {
        return await AddOrUpdateListAsync(states, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 批量增加或更新数据（单次 SaveChanges 原子批次；返回处理记录数，对齐 Mongo BulkWrite ReplaceOne 语义）。
    /// </summary>
    /// <remarks>
    /// Bulk adds or updates documents as one atomic <c>SaveChanges</c> batch (whole-document replacement);
    /// returns the number of processed records, aligned with Mongo's BulkWrite ReplaceOne semantics.
    /// </remarks>
    /// <typeparam name="TState">状态类型 / The state type</typeparam>
    /// <param name="states">要增加或更新的状态集合 / The states to add or update</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>处理的记录数 / The number of processed records</returns>
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
        foreach (var state in stateArray)
        {
            if (state.CreatedTime == 0)
            {
                state.CreatedTime = currentTime;
            }

            state.UpdateTime = currentTime;
            state.UpdateCount = (state.UpdateCount ?? 0) + 1;
        }

        await ExecuteWriteWithRetryAsync(async token =>
        {
            await EnsureTableAsync<TState>(token).ConfigureAwait(false);
            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    using var context = CreateContext<TState>();
                    await StageUpsertAsync(context, stateArray, token).ConfigureAwait(false);
                    await context.SaveChangesAsync(token).ConfigureAwait(false);
                    return true;
                }
                catch (DbUpdateException exception) when (attempt == 0 && PostgreSqlSqlState.IsUniqueViolation(exception))
                {
                    // 批内 id 与并发插入竞争：整批重探一次收敛
                    // A batch id raced a concurrent insert: re-probe the whole batch once.
                }
            }
            return true;
        }, cancellationToken, nameof(AddOrUpdateListAsync), true).ConfigureAwait(false);
        foreach (var state in stateArray)
        {
            state.SaveToDbPostHandler();
        }

        return stateArray.Length;
    }

    /// <summary>
    /// 按批次大小分批执行批量 upsert 保存（关服保存路径，逐批 ack / 异常隔离，对齐 Mongo SaveBulkAsync）。
    /// </summary>
    /// <remarks>
    /// Bulk-upserts in batches of <paramref name="batchSize"/> (per-batch contract): per-batch ack and exception
    /// isolation — a failed batch is logged and the remaining batches still execute. Unlike
    /// <c>AddOrUpdateListAsync</c> this deliberately does NOT touch <c>CreatedTime/UpdateTime/UpdateCount</c>:
    /// state timestamps are persisted exactly as handed in (the caller owns timestamp semantics, migrated
    /// verbatim from the Mongo adapter).
    /// one affected row per staged document.
    /// </remarks>
    /// <param name="states">待保存的状态集合 / The states to save</param>
    /// <param name="batchSize">每批数量（&lt;= 0 抛出 ArgumentOutOfRangeException）/ Batch size (values &lt;= 0 throw)</param>
    /// <returns>全部成功 ack 批次的状态列表 / The states of all acknowledged batches</returns>
    /// <exception cref="ArgumentOutOfRangeException">当 <paramref name="batchSize"/> &lt;= 0 时抛出 / Thrown when batchSize is &lt;= 0</exception>
    public async Task<IReadOnlyList<TState>> SaveBulkAsync<TState>(IEnumerable<TState> states, int batchSize) where TState : BaseCacheState, new()
    {
        if (batchSize <= 0)
        {
            // Localization: Database.BatchSizeInvalid - 批处理大小必须大于 0。
            throw new ArgumentOutOfRangeException(nameof(batchSize), batchSize, LocalizationService.GetString(Keys.Database.BatchSizeInvalid));
        }

        EnsureInitialized();
        var stateList = states as IReadOnlyList<TState> ?? states?.ToList();
        if (stateList == null || stateList.Count == 0)
        {
            return Array.Empty<TState>();
        }

        var stateName = typeof(TState).Name;
        var acknowledgedStates = new List<TState>(stateList.Count);

        for (var index = 0; index < stateList.Count; index += batchSize)
        {
            var batchCount = Math.Min(batchSize, stateList.Count - index);
            var batch = new TState[batchCount];
            for (var offset = 0; offset < batchCount; offset++)
            {
                batch[offset] = stateList[index + offset];
            }

            try
            {
                await EnsureTableAsync<TState>(CancellationToken.None).ConfigureAwait(false);
                var acknowledged = false;
                using (var context = CreateContext<TState>())
                {
                    await StageUpsertAsync(context, batch, CancellationToken.None).ConfigureAwait(false);
                    var affected = await context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
                    // 每条 staged 文档各计一行；受影响行数 >= 批大小即视为整批 ack
                    // Each staged document counts one row; affected >= batch size means the whole batch was acknowledged.
                    acknowledged = affected >= batchCount;
                }

                if (acknowledged)
                {
                    acknowledgedStates.AddRange(batch);
                }
                else
                {
                    // Localization: Database.PostgreSql.SaveBulkBatchNotAcknowledged - PostgreSqlDbService.SaveBulkAsync 批次未被确认。状态名：{0}，批次索引：{1}，批次总数：{2}
                    LogHelper.Error(LocalizationService.GetString(Localization.Keys.Database.PostgreSql.SaveBulkBatchNotAcknowledged, stateName, index / batchSize + 1, batchCount));
                }
            }
            catch (Exception exception)
            {
                // 逐批异常隔离：失败批次记日志后继续后续批次，避免单批故障放大为整批丢失
                // Per-batch exception isolation (migrated semantics): a failed batch is logged and the loop continues with the remaining batches.
                // Localization: Database.PostgreSql.SaveBulkBatchFailed - PostgreSqlDbService.SaveBulkAsync 批次失败。状态名：{0}，批次索引：{1}，批次总数：{2}，错误：{3}
                LogHelper.Error(LocalizationService.GetString(Localization.Keys.Database.PostgreSql.SaveBulkBatchFailed, stateName, index / batchSize + 1, batchCount, exception));
            }
        }

        return acknowledgedStates;
    }

    /// <summary>
    /// 在事务中执行操作（EF 事务壳 BEGIN → action → COMMIT；SQLSTATE 40001/40P01 重试分类，对齐 Mongo 事务语义）。
    /// </summary>
    /// <remarks>
    /// Executes the action inside a transaction shell (<c>BeginTransactionAsync</c> → action → commit),
    /// retrying on PostgreSQL serialization failures (SQLSTATE <c>40001</c> serialization_failure /
    /// <c>40P01</c> deadlock_detected — aligned with Mongo's <c>TransientTransactionError</c> retry
    /// classification). As with the Mongo adapter, the action's own operations use their pooled connections
    /// and are not bound to this transaction's snapshot.
    /// </remarks>
    /// <param name="action">要在事务中执行的操作 / The action to execute inside the transaction</param>
    /// <exception cref="DatabaseUnavailableException">当事务重试全部失败后抛出 / Thrown when all transaction retry attempts fail</exception>
    public async Task ExecuteInTransactionAsync(Func<Task> action)
    {
        await ExecuteInTransactionAsync(action, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 在事务中执行操作（EF 事务壳 BEGIN → action → COMMIT；SQLSTATE 40001/40P01 重试分类，对齐 Mongo 事务语义）。
    /// </summary>
    /// <remarks>
    /// Executes the action inside a transaction shell (<c>BeginTransactionAsync</c> → action → commit),
    /// retrying on PostgreSQL serialization failures (SQLSTATE <c>40001</c> serialization_failure /
    /// <c>40P01</c> deadlock_detected — aligned with Mongo's <c>TransientTransactionError</c> retry
    /// classification). As with the Mongo adapter, the action's own operations use their pooled connections
    /// and are not bound to this transaction's snapshot.
    /// </remarks>
    /// <param name="action">要在事务中执行的操作 / The action to execute inside the transaction</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="action"/> 为 null 时抛出 / Thrown when <paramref name="action"/> is null</exception>
    /// <exception cref="DatabaseUnavailableException">当事务重试全部失败后抛出 / Thrown when all transaction retry attempts fail</exception>
    public async Task ExecuteInTransactionAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();
        ArgumentNullException.ThrowIfNull(action, nameof(action));
        Exception lastException = null;
        for (var attempt = 0; attempt <= _transactionRetryDelaysMilliseconds.Length; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var context = CreateShellContext();
                await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                await action().ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception exception) when (ShouldRetryTransactionException(exception) && attempt < _transactionRetryDelaysMilliseconds.Length)
            {
                lastException = exception;
                var delay = _transactionRetryDelaysMilliseconds[attempt] + Random.Shared.Next(0, 120);
                // Localization: Database.PostgreSql.ExecuteInTransactionTransientError - PostgreSqlDbService.ExecuteInTransactionAsync 瞬时错误，重试 {0}/{1}。error={2}
                LogHelper.Warning(LocalizationService.GetString(Localization.Keys.Database.PostgreSql.ExecuteInTransactionTransientError, attempt + 1, _transactionRetryDelaysMilliseconds.Length, exception.Message));
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                lastException = exception;
                break;
            }
        }

        // Localization: Database.PostgreSql.ExecuteInTransactionFailed - ExecuteInTransactionAsync重试失败，未知异常
        throw new DatabaseUnavailableException(LocalizationService.GetString(Keys.Database.PostgreSqlExecuteInTransactionFailed), lastException ?? new InvalidOperationException(LocalizationService.GetString(Keys.Database.PostgreSqlExecuteInTransactionFailed)));
    }

    /// <summary>
    /// 暂存一批整文档替换 upsert（既有行替换 Doc 引用，缺失行 Add；调用方负责 SaveChanges）。
    /// </summary>
    /// <remarks>
    /// Stages a whole-document-replacement upsert for a batch: existing rows (bypassing the soft-delete filter —
    /// upsert intentionally revives soft-deleted documents, aligned with Mongo ReplaceOne semantics) get their
    /// <c>Doc</c> reference replaced; missing rows are staged as inserts. The caller owns the atomic
    /// <c>SaveChangesAsync</c>.
    /// </remarks>
    /// <typeparam name="TState">状态类型 / The state type</typeparam>
    /// <param name="context">EF 上下文 / The EF context</param>
    /// <param name="states">待 upsert 的状态集合 / The states to upsert</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    private static async Task StageUpsertAsync<TState>(PostgreSqlDbContext<TState> context, IReadOnlyList<TState> states, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        var ids = new long[states.Count];
        for (var index = 0; index < states.Count; index++)
        {
            ids[index] = states[index].Id;
        }

        var existingRows = await context.Rows.IgnoreQueryFilters().Where(row => ids.Contains(row.Id)).ToDictionaryAsync(row => row.Id, cancellationToken).ConfigureAwait(false);
        foreach (var state in states)
        {
            if (existingRows.TryGetValue(state.Id, out var row))
            {
                row.Doc = state;
            }
            else
            {
                context.Rows.Add(new StateRow<TState> { Id = state.Id, Doc = state, });
            }
        }
    }
}