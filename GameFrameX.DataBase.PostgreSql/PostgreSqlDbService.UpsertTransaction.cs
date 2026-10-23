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
using Microsoft.EntityFrameworkCore.Storage;

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

        // 写库前捕获字节快照（bump 后），成功后按该快照精确提交基线，
        // 避免参数less 提交读到并发检查推进后的未落库 ToSaveHash。
        var persistedData = state.ToBytes();

        await ExecuteWriteWithRetryAsync(async token =>
        {
            await EnsureTableAsync<TState>(token).ConfigureAwait(false);
            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    using (var context = CreateContext<TState>())
                    {
                        JoinAmbientTransaction(context);
                        await StageUpsertAsync(context, new[] { state, }, token).ConfigureAwait(false);
                        await context.SaveChangesAsync(token).ConfigureAwait(false);
                        return true;
                    }
                }
                catch (DbUpdateException exception) when (attempt == 0 && PostgreSqlSqlState.IsUniqueViolation(exception))
                {
                    // 并发插入同 id 竞争失败：重载后按更新收敛（对齐原 upsert 的 last-writer-wins）
                    // Lost the insert race for the same id: reload and converge as an update (former upsert's last-writer-wins).
                }
            }
            return true;
        }, cancellationToken, nameof(AddOrUpdateAsync), true).ConfigureAwait(false);

        state.SaveToDbPostHandler(persistedData);
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
        // 写库前逐个捕获字节快照（bump 后），成功后按快照精确提交基线
        var persistedSnapshots = new List<(TState State, byte[] Data)>(stateArray.Length);
        foreach (var state in stateArray)
        {
            if (state.CreatedTime == 0)
            {
                state.CreatedTime = currentTime;
            }

            state.UpdateTime = currentTime;
            state.UpdateCount = (state.UpdateCount ?? 0) + 1;
            persistedSnapshots.Add((state, state.ToBytes()));
        }

        await ExecuteWriteWithRetryAsync(async token =>
        {
            await EnsureTableAsync<TState>(token).ConfigureAwait(false);
            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    using (var context = CreateContext<TState>())
                    {
                        JoinAmbientTransaction(context);
                        await StageUpsertAsync(context, stateArray, token).ConfigureAwait(false);
                        await context.SaveChangesAsync(token).ConfigureAwait(false);
                        return true;
                    }
                }
                catch (DbUpdateException exception) when (attempt == 0 && PostgreSqlSqlState.IsUniqueViolation(exception))
                {
                    // 批内 id 与并发插入竞争：整批重探一次收敛
                    // A batch id raced a concurrent insert: re-probe the whole batch once.
                }
            }
            return true;
        }, cancellationToken, nameof(AddOrUpdateListAsync), true).ConfigureAwait(false);
        foreach (var (state, persistedData) in persistedSnapshots)
        {
            state.SaveToDbPostHandler(persistedData);
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
    /// Acknowledgement = one affected row per staged (deduplicated) document; duplicate ids within a batch
    /// collapse onto one staged row (last-writer-wins) and count once.
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
            var batch = await SaveBulkBatchAsync<TState>(stateList, index, batchCount, stateName, index / batchSize + 1).ConfigureAwait(false);
            acknowledgedStates.AddRange(batch);
        }

        return acknowledgedStates;
    }

    /// <summary>
    /// SaveBulk 单批执行体（暂存整文档替换 upsert 并按去重后 id 数判 ack；未确认/失败批次记日志并返回空批——异常隔离由本方法承接，外层循环继续后续批次）。
    /// </summary>
    /// <remarks>
    /// Executes one SaveBulk batch: stages the whole-document-replacement upsert and acknowledges when the
    /// affected row count covers the deduplicated id set (duplicate ids within a batch collapse onto one
    /// staged row). An unacknowledged or failed batch is logged here and yields an empty batch (per-batch
    /// exception isolation — the caller's loop continues with the remaining batches). Acknowledged batches
    /// commit their pre-captured byte snapshots via <c>SaveToDbPostHandler</c> before being returned.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="stateList">全部待保存状态 / All states to save</param>
    /// <param name="index">本批起始下标 / The batch start index</param>
    /// <param name="batchCount">本批数量 / The batch size</param>
    /// <param name="stateName">状态类型名（日志用）/ The state type name (for logging)</param>
    /// <param name="batchNumber">批次序号（日志用，1 起）/ The 1-based batch number (for logging)</param>
    /// <returns>本批确认成功的状态；未确认/失败返回空数组 / The acknowledged states of this batch; empty when unacknowledged or failed</returns>
    private async Task<TState[]> SaveBulkBatchAsync<TState>(IReadOnlyList<TState> stateList, int index, int batchCount, string stateName, int batchNumber) where TState : BaseCacheState, new()
    {
        var batch = new TState[batchCount];
        // 写库前逐状态捕获送库字节快照，ack 批次内逐状态精确提交基线：
        // 提交权从 Core 的 NotifyBatchSaved 收进 Provider，且只提交实际送库快照的 hash。
        var batchSnapshots = new (TState State, byte[] Data)[batchCount];
        for (var offset = 0; offset < batchCount; offset++)
        {
            batch[offset] = stateList[index + offset];
            batchSnapshots[offset] = (batch[offset], batch[offset].ToBytes());
        }

        try
        {
            await EnsureTableAsync<TState>(CancellationToken.None).ConfigureAwait(false);
            var acknowledged = false;
            using (var context = CreateContext<TState>())
            {
                JoinAmbientTransaction(context);
                await StageUpsertAsync(context, batch, CancellationToken.None).ConfigureAwait(false);
                var affected = await context.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
                // 每个 staged（去重后）文档各计一行；受影响行数 >= 去重后 id 数即视为整批 ack
                //（批内重复 id 已在暂存时合并为单行，按批元素数比较会把已写库的批次误判为未确认）
                // Each staged (deduplicated) document counts one row; affected >= the deduplicated id count means the
                // whole batch was acknowledged (duplicate ids collapse onto one staged row — comparing against the raw
                // element count would flag written batches as unacknowledged).
                var distinctIdCount = 0;
                var seenIds = new HashSet<long>(batchCount);
                for (var offset = 0; offset < batchCount; offset++)
                {
                    if (seenIds.Add(batch[offset].Id))
                    {
                        distinctIdCount++;
                    }
                }

                acknowledged = affected >= distinctIdCount;
            }

            if (acknowledged)
            {
                foreach (var (state, persistedData) in batchSnapshots)
                {
                    state.SaveToDbPostHandler(persistedData);
                }

                return batch;
            }

            // Localization: Database.PostgreSql.SaveBulkBatchNotAcknowledged - PostgreSqlDbService.SaveBulkAsync 批次未被确认。状态名：{0}，批次索引：{1}，批次总数：{2}
            LogHelper.Error(LocalizationService.GetString(Localization.Keys.Database.PostgreSql.SaveBulkBatchNotAcknowledged, stateName, batchNumber, batchCount));
            return Array.Empty<TState>();
        }
        catch (Exception exception)
        {
            // 逐批异常隔离：失败批次记日志后继续后续批次，避免单批故障放大为整批丢失
            // Per-batch exception isolation (migrated semantics): a failed batch is logged and the loop continues with the remaining batches.
            // Localization: Database.PostgreSql.SaveBulkBatchFailed - PostgreSqlDbService.SaveBulkAsync 批次失败。状态名：{0}，批次索引：{1}，批次总数：{2}，错误：{3}
            LogHelper.Error(LocalizationService.GetString(Localization.Keys.Database.PostgreSql.SaveBulkBatchFailed, stateName, batchNumber, batchCount, exception));
            return Array.Empty<TState>();
        }
    }

    /// <summary>
    /// 在事务中执行操作（EF 事务壳 BEGIN → action → COMMIT；action 内全部操作上下文经 UseTransaction 加入本事务，壳唯一负责提交；SQLSTATE 40001/40P01 重试分类，对齐 Mongo 事务语义）。
    /// </summary>
    /// <remarks>
    /// Executes the action inside a transaction shell (<c>BeginTransactionAsync</c> → action → commit),
    /// retrying on PostgreSQL serialization failures (SQLSTATE <c>40001</c> serialization_failure /
    /// <c>40P01</c> deadlock_detected — aligned with Mongo's <c>TransientTransactionError</c> retry
    /// classification). Every context the action creates enlists in the shell's transaction via the
    /// ambient transaction (<c>UseTransaction</c>), so the shell is the single commit point;
    /// <c>SaveChanges</c> failures roll back to an automatic savepoint, keeping the 23505 one-shot
    /// upsert retry working inside the transaction.
    /// </remarks>
    /// <param name="action">要在事务中执行的操作 / The action to execute inside the transaction</param>
    /// <exception cref="DatabaseUnavailableException">当事务重试全部失败后抛出 / Thrown when all transaction retry attempts fail</exception>
    public async Task ExecuteInTransactionAsync(Func<Task> action)
    {
        await ExecuteInTransactionAsync(action, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 在事务中执行操作（EF 事务壳 BEGIN → action → COMMIT；action 内全部操作上下文经 UseTransaction 加入本事务，壳唯一负责提交；SQLSTATE 40001/40P01 重试分类，对齐 Mongo 事务语义）。
    /// </summary>
    /// <remarks>
    /// Executes the action inside a transaction shell (<c>BeginTransactionAsync</c> → action → commit),
    /// retrying on PostgreSQL serialization failures (SQLSTATE <c>40001</c> serialization_failure /
    /// <c>40P01</c> deadlock_detected — aligned with Mongo's <c>TransientTransactionError</c> retry
    /// classification). Every context the action creates enlists in the shell's transaction via the
    /// ambient transaction (<c>UseTransaction</c>), so the shell is the single commit point;
    /// <c>SaveChanges</c> failures roll back to an automatic savepoint, keeping the 23505 one-shot
    /// upsert retry working inside the transaction.
    /// </remarks>
    /// <param name="action">要在事务中执行的操作 / The action to execute inside the transaction</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="action"/> 为 null 时抛出 / Thrown when <paramref name="action"/> is null</exception>
    /// <exception cref="DatabaseUnavailableException">当事务重试全部失败后抛出 / Thrown when all transaction retry attempts fail</exception>
    public async Task ExecuteInTransactionAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();
        ArgumentNullException.ThrowIfNull(action);
        if (_ambientTransaction.Value != null)
        {
            // 嵌套事务壳：加入环境事务（操作上下文已绑同一事务，提交权归最外层壳）。
            // Nested shell: join the ambient transaction (the outermost shell owns the commit).
            await action().ConfigureAwait(false);
            return;
        }

        Exception lastException = null;
        for (var attempt = 0; attempt <= _transactionRetryDelaysMilliseconds.Length; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using (var context = CreateShellContext())
                {
                    await using (var transaction = await context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false))
                    {
                        _ambientTransaction.Value = transaction.GetDbTransaction();
                        try
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            await action().ConfigureAwait(false);
                            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                        }
                        finally
                        {
                            _ambientTransaction.Value = null;
                        }

                        return;
                    }
                }
            }
            catch (Exception exception) when (ExceptionClassifier.ShouldRetryTransaction(exception) && attempt < _transactionRetryDelaysMilliseconds.Length)
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
    /// 暂存一批整文档替换 upsert（既有行桩更新 doc 列，缺失行 Add；调用方负责 SaveChanges）。
    /// </summary>
    /// <remarks>
    /// Stages a whole-document-replacement upsert for a batch. Existence is probed with a non-tracking
    /// <c>id</c>-only projection — a full-row <c>ToDictionaryAsync</c> would read the whole jsonb
    /// document batch back (an N× read amplification on the SaveBulk/shutdown path). Existing rows
    /// (bypassing the soft-delete filter — upsert intentionally revives soft-deleted documents, aligned
    /// with Mongo ReplaceOne semantics) are updated via a stub entity: <c>Attach</c> + owned entry
    /// <see cref="EntityState.Modified"/> emits <c>UPDATE ... SET doc = @p WHERE id = @id</c> without
    /// reading the original document. Duplicate ids within the batch collapse onto one staged row
    /// (last-writer-wins, matching the former tracked-row behavior). The caller owns the atomic
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

        var existingIds = await context.Rows.IgnoreQueryFilters().Where(row => ids.Contains(row.Id)).Select(row => row.Id).ToHashSetAsync(cancellationToken).ConfigureAwait(false);
        var stagedRows = new Dictionary<long, StateRow<TState>>(states.Count);
        foreach (var state in states)
        {
            if (stagedRows.TryGetValue(state.Id, out var staged))
            {
                // 批内重复 id：后写覆盖（last-writer-wins），与既有整批回读版行为一致
                // Duplicate id within the batch: last writer wins, matching the former full-row-read behavior.
                staged.Doc = state;
                continue;
            }

            if (existingIds.Contains(state.Id))
            {
                // 存在行：桩实体 Attach + owned 整档 entry 置 Modified → UPDATE 仅 doc 列，无需回读原 jsonb
                // Existing row: attach a stub and mark the owned document entry Modified → UPDATE touches only the doc column, no original-jsonb readback.
                var stub = new StateRow<TState> { Id = state.Id, Doc = state, };
                context.Attach(stub);
                context.Entry(stub).Reference(row => row.Doc).TargetEntry.State = EntityState.Modified;
                stagedRows.Add(state.Id, stub);
            }
            else
            {
                var added = new StateRow<TState> { Id = state.Id, Doc = state, };
                context.Rows.Add(added);
                stagedRows.Add(state.Id, added);
            }
        }
    }
}