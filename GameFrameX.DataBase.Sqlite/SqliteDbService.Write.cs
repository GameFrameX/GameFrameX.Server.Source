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
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameFrameX.DataBase.Abstractions;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Foundation.Logger;
using GameFrameX.Foundation.Orm.Attribute;
using GameFrameX.Localization;
using Microsoft.Data.Sqlite;

namespace GameFrameX.DataBase.Sqlite;

/// <summary>
/// SQLite 写路径（整文档写回 + 时间戳语义对齐 Mongo/PG；事务壳单连接单事务，嵌套加入环境事务）。
/// </summary>
/// <remarks>
/// Write paths: whole-document write-back with the Mongo/PG timestamp semantics (Add stamps both
/// times and seeds UpdateCount; Update advances them; upserts replace whole documents and revive
/// soft-deleted rows; SaveBulkAsync persists timestamps verbatim; DeleteListAsync marks its matched
/// rows in one gated single-transaction batch). Transactions run on the single
/// connection — a nested transaction shell joins the ambient transaction instead of opening a
/// second one (SQLite cannot nest); data operations retry via the shared resilience engine
/// while the transaction shell itself stays single-pass (ambient joins make blind retries unsafe).
/// </remarks>
public sealed partial class SqliteDbService
{
    /// <summary>
    /// 增加一条数据（适配器补齐时间戳与更新计数，对齐 Mongo/PG；主键冲突原样上抛）。
    /// </summary>
    /// <remarks>
    /// Adds a single document (the adapter fills timestamps and update count; a primary-key conflict
    /// propagates as a SQLite unique-constraint error, mirroring Mongo/PG insert semantics).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="state">要新增的数据 / The state to add</param>
    /// <returns>异步任务 / Async task</returns>
    public async Task AddAsync<TState>(TState state) where TState : BaseCacheState, new()
    {
        await AddAsync(state, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 增加一条数据（适配器补齐时间戳与更新计数，对齐 Mongo/PG；主键冲突原样上抛）。
    /// </summary>
    /// <remarks>
    /// Adds a single document (the adapter fills timestamps and update count; a primary-key conflict
    /// propagates as a SQLite unique-constraint error, mirroring Mongo/PG insert semantics).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="state">要新增的数据 / The state to add</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>异步任务 / Async task</returns>
    public async Task AddAsync<TState>(TState state, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureOpen();
        var currentTime = GetCurrentTimestamp();
        state.CreatedTime = currentTime;
        state.UpdateTime = currentTime;
        state.UpdateCount ??= 0;
        await ExecuteWriteWithRetryAsync(async _ => await ExecuteGatedAsync(async () =>
        {
            await EnsureTableAsync<TState>(cancellationToken).ConfigureAwait(false);
            using (var command = BuildInsertCommand<TState>(state))
            {
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }), cancellationToken, nameof(AddAsync), false).ConfigureAwait(false);
    }

    /// <summary>
    /// 增加一个列表数据（单事务整批原子；空集合直接返回，对齐 Mongo/PG）。
    /// </summary>
    /// <remarks>
    /// Adds a list of documents — one implicit transaction wraps every insert so the batch is
    /// atomic; empty collections return directly.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="states">要新增的数据列表 / The states to add</param>
    /// <returns>异步任务 / Async task</returns>
    public async Task AddListAsync<TState>(IEnumerable<TState> states) where TState : BaseCacheState, new()
    {
        await AddListAsync(states, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 增加一个列表数据（单事务整批原子；空集合直接返回，对齐 Mongo/PG）。
    /// </summary>
    /// <remarks>
    /// Adds a list of documents — one implicit transaction wraps every insert so the batch is
    /// atomic; empty collections return directly.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="states">要新增的数据列表 / The states to add</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>异步任务 / Async task</returns>
    public async Task AddListAsync<TState>(IEnumerable<TState> states, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureOpen();
        var cacheStates = states?.ToList();
        if (cacheStates == null || cacheStates.Count == 0)
        {
            return;
        }

        var currentTime = GetCurrentTimestamp();
        foreach (var cacheState in cacheStates)
        {
            cacheState.CreatedTime = currentTime;
            cacheState.UpdateTime = currentTime;
            cacheState.UpdateCount ??= 0;
        }

        await ExecuteWriteWithRetryAsync(async _ => await ExecuteGatedAsync(async () =>
        {
            await EnsureTableAsync<TState>(cancellationToken).ConfigureAwait(false);
            var (transaction, owned) = await BeginOrJoinTransactionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                foreach (var cacheState in cacheStates)
                {
                    using (var command = BuildInsertCommand<TState>(cacheState))
                    {
                        command.Transaction = transaction;
                        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    }
                }

                if (owned)
                {
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                if (owned)
                {
                    await transaction.DisposeAsync().ConfigureAwait(false);
                }
            }
        }), cancellationToken, nameof(AddListAsync), false).ConfigureAwait(false);
    }

    /// <summary>
    /// 增加或更新数据（原子 upsert 整文档替换，软删行复活，对齐 Mongo ReplaceOne+IsUpsert）。
    /// </summary>
    /// <remarks>
    /// Adds or updates a single document via the atomic <c>INSERT ... ON CONFLICT(id) DO UPDATE</c>
    /// whole-document replacement (soft-deleted rows revive, aligned with Mongo's ReplaceOne + IsUpsert).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="state">要增加或更新的状态对象 / The state to upsert</param>
    /// <returns>已持久化的状态对象 / The persisted state</returns>
    public async Task<TState> AddOrUpdateAsync<TState>(TState state) where TState : BaseCacheState, new()
    {
        return await AddOrUpdateAsync(state, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 增加或更新数据（原子 upsert 整文档替换，软删行复活，对齐 Mongo ReplaceOne+IsUpsert）。
    /// </summary>
    /// <remarks>
    /// Adds or updates a single document via the atomic <c>INSERT ... ON CONFLICT(id) DO UPDATE</c>
    /// whole-document replacement (soft-deleted rows revive, aligned with Mongo's ReplaceOne + IsUpsert).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="state">要增加或更新的状态对象 / The state to upsert</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>已持久化的状态对象 / The persisted state</returns>
    public async Task<TState> AddOrUpdateAsync<TState>(TState state, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureOpen();
        var currentTime = GetCurrentTimestamp();
        if (state.CreatedTime == 0)
        {
            state.CreatedTime = currentTime;
        }

        state.UpdateTime = currentTime;
        state.UpdateCount = (state.UpdateCount ?? 0) + 1;

        // 写库前捕获字节快照（bump 后），成功后按该快照精确提交基线，
        // 避免参数less 提交读到并发检查推进后的未落库 ToSaveHash。
        var persistedData = state.ToBytes();
        await ExecuteWriteWithRetryAsync(async _ => await ExecuteGatedAsync(async () =>
        {
            await EnsureTableAsync<TState>(cancellationToken).ConfigureAwait(false);
            using (var command = BuildUpsertCommand<TState>(state))
            {
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }), cancellationToken, nameof(AddOrUpdateAsync), true).ConfigureAwait(false);
        state.SaveToDbPostHandler(persistedData);
        return state;
    }

    /// <summary>
    /// 批量增加或更新数据（单事务原子批次；返回处理记录数，对齐 Mongo BulkWrite ReplaceOne 语义）。
    /// </summary>
    /// <remarks>
    /// Bulk adds or updates documents as one atomic transaction (whole-document replacement);
    /// returns the number of processed records.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="states">要增加或更新的状态集合 / The states to upsert</param>
    /// <returns>处理的记录数 / The processed count</returns>
    public async Task<long> AddOrUpdateListAsync<TState>(IEnumerable<TState> states) where TState : BaseCacheState, new()
    {
        return await AddOrUpdateListAsync(states, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 批量增加或更新数据（单事务原子批次；返回处理记录数，对齐 Mongo BulkWrite ReplaceOne 语义）。
    /// </summary>
    /// <remarks>
    /// Bulk adds or updates documents as one atomic transaction (whole-document replacement);
    /// returns the number of processed records.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="states">要增加或更新的状态集合 / The states to upsert</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>处理的记录数 / The processed count</returns>
    public async Task<long> AddOrUpdateListAsync<TState>(IEnumerable<TState> states, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureOpen();
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

        await ExecuteWriteWithRetryAsync(async _ => await ExecuteGatedAsync(async () =>
        {
            await EnsureTableAsync<TState>(cancellationToken).ConfigureAwait(false);
            var (transaction, owned) = await BeginOrJoinTransactionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                foreach (var state in stateArray)
                {
                    using (var command = BuildUpsertCommand<TState>(state))
                    {
                        command.Transaction = transaction;
                        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    }
                }

                if (owned)
                {
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                if (owned)
                {
                    await transaction.DisposeAsync().ConfigureAwait(false);
                }
            }
        }), cancellationToken, nameof(AddOrUpdateListAsync), true).ConfigureAwait(false);
        foreach (var (state, persistedData) in persistedSnapshots)
        {
            state.SaveToDbPostHandler(persistedData);
        }

        return stateArray.Length;
    }

    /// <summary>
    /// 按批次大小分批执行批量 upsert 保存（关服保存路径，逐批 ack / 异常隔离，时间戳不触碰，对齐 Mongo/PG SaveBulkAsync）。
    /// </summary>
    /// <remarks>
    /// Bulk-upserts in batches of <paramref name="batchSize"/> with per-batch ack and exception
    /// isolation — a failed batch is logged and the remaining batches still execute. This deliberately
    /// does NOT touch <c>CreatedTime/UpdateTime/UpdateCount</c>: state timestamps are persisted exactly
    /// as handed in (the caller owns timestamp semantics, migrated verbatim from the Mongo/PG adapters).
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

        EnsureOpen();
        var stateList = states as IReadOnlyList<TState> ?? states?.ToList();
        if (stateList == null || stateList.Count == 0)
        {
            return Array.Empty<TState>();
        }

        var stateName = typeof(TState).Name;
        var acknowledgedStates = new List<TState>(stateList.Count);
        await ExecuteGatedAsync(async () =>
        {
            await EnsureTableAsync<TState>(CancellationToken.None).ConfigureAwait(false);
            for (var index = 0; index < stateList.Count; index += batchSize)
            {
                var batchCount = Math.Min(batchSize, stateList.Count - index);
                var batch = await SaveBulkBatchAsync<TState>(stateList, index, batchCount, stateName, index / batchSize + 1).ConfigureAwait(false);
                acknowledgedStates.AddRange(batch);
            }
        }).ConfigureAwait(false);

        return acknowledgedStates;
    }

    /// <summary>
    /// SaveBulk 单批执行体（事务内逐条 upsert 并计数；未确认/失败批次记日志并返回空批——异常隔离由本方法承接，外层循环继续后续批次）。
    /// </summary>
    /// <remarks>
    /// Executes one SaveBulk batch inside its own transaction, counting acknowledged upserts; an
    /// unacknowledged or failed batch is logged here and yields an empty batch (per-batch exception
    /// isolation — the caller's loop continues with the remaining batches).
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
        try
        {
            var acknowledged = 0;
            // 写库前逐状态捕获送库字节快照，ack 批次内逐状态精确提交基线：
            // 提交权从 Core 的 NotifyBatchSaved 收进 Provider，且只提交实际送库快照的 hash。
            var batchSnapshots = new (TState State, byte[] Data)[batchCount];
            var (transaction, owned) = await BeginOrJoinTransactionAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                for (var offset = 0; offset < batchCount; offset++)
                {
                    var state = stateList[index + offset];
                    batchSnapshots[offset] = (state, state.ToBytes());
                    using (var command = BuildUpsertCommand<TState>(state))
                    {
                        command.Transaction = transaction;
                        acknowledged += await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                }

                if (owned)
                {
                    await transaction.CommitAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
            finally
            {
                if (owned)
                {
                    await transaction.DisposeAsync().ConfigureAwait(false);
                }
            }

            // 每条 upsert 计一行；受影响行数 >= 批大小即视为整批 ack
            // Each upsert counts one row; affected >= batch size means the whole batch was acknowledged.
            if (acknowledged >= batchCount)
            {
                var batch = new TState[batchCount];
                for (var offset = 0; offset < batchCount; offset++)
                {
                    batch[offset] = stateList[index + offset];
                }

                foreach (var (state, persistedData) in batchSnapshots)
                {
                    state.SaveToDbPostHandler(persistedData);
                }

                return batch;
            }

            // Localization: Database.Sqlite.SaveBulkBatchNotAcknowledged - SqliteDbService.SaveBulkAsync 批次未被确认。状态名：{0}，批次索引：{1}，批次总数：{2}
            LogHelper.Error(LocalizationService.GetString(Localization.Keys.Database.Sqlite.SaveBulkBatchNotAcknowledged, stateName, batchNumber, batchCount));
            return Array.Empty<TState>();
        }
        catch (Exception exception)
        {
            // 逐批异常隔离：失败批次记日志后继续后续批次，避免单批故障放大为整批丢失
            // Per-batch exception isolation: a failed batch is logged and the loop continues with the remaining batches.
            // Localization: Database.Sqlite.SaveBulkBatchFailed - SqliteDbService.SaveBulkAsync 批次失败。状态名：{0}，批次索引：{1}，批次总数：{2}，错误：{3}
            LogHelper.Error(LocalizationService.GetString(Localization.Keys.Database.Sqlite.SaveBulkBatchFailed, stateName, batchNumber, batchCount, exception));
            return Array.Empty<TState>();
        }
    }

    /// <summary>
    /// 保存数据（仅当 StateHash 判定已修改才写库；整文档重写，新档为准；行缺失静默 no-op，对齐 Mongo/PG）。
    /// </summary>
    /// <remarks>
    /// Saves a document (only writes when the StateHash marks it modified). The whole document is
    /// rewritten from the state object; a missing row is a silent no-op (aligned with the
    /// <c>UPDATE ... WHERE id</c> row-count behavior of the Mongo/PG adapters).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="state">要保存的数据 / The state to save</param>
    /// <returns>保存后的数据 / The saved state</returns>
    public async Task<TState> UpdateAsync<TState>(TState state) where TState : BaseCacheState, new()
    {
        return await UpdateAsync(state, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 保存数据（仅当 StateHash 判定已修改才写库；整文档重写，新档为准；行缺失静默 no-op，对齐 Mongo/PG）。
    /// </summary>
    /// <remarks>
    /// Saves a document (only writes when the StateHash marks it modified). The whole document is
    /// rewritten from the state object; a missing row is a silent no-op (aligned with the
    /// <c>UPDATE ... WHERE id</c> row-count behavior of the Mongo/PG adapters).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="state">要保存的数据 / The state to save</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>保存后的数据 / The saved state</returns>
    public async Task<TState> UpdateAsync<TState>(TState state, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureOpen();
        var (isChanged, changedData) = state.IsChanged();
        if (isChanged)
        {
            state.UpdateTime = GetCurrentTimestamp();
            state.UpdateCount = (state.UpdateCount ?? 0) + 1;
            var updated = await ExecuteWriteWithRetryAsync(async _ => await ExecuteGatedAsync(async () =>
            {
                await EnsureTableAsync<TState>(cancellationToken).ConfigureAwait(false);
                using (var command = BuildReplaceDocumentCommand<TState>(state))
                {
                    return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
                }
            }), cancellationToken, nameof(UpdateAsync), true).ConfigureAwait(false);
            if (updated)
            {
                // 精确提交：以检查时快照推进基线，避免并发 IsChanged 把基线推进到未落库快照
                state.SaveToDbPostHandler(changedData);
            }
        }

        return state;
    }

    /// <summary>
    /// 保存多条数据（返回实际变更数；未变更项跳过，单事务原子，对齐 Mongo/PG 批量语义）。
    /// </summary>
    /// <remarks>
    /// Saves multiple documents (returns the actually-changed count; unchanged items are skipped).
    /// All writes share one transaction; when at least one row was written, each changed state receives
    /// the exact-snapshot <c>SaveToDbPostHandler(byte[])</c> commit (aligned with the Mongo/PG flow).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="stateList">要保存的数据列表 / The states to save</param>
    /// <returns>实际更新的数量 / The updated count</returns>
    public async Task<long> UpdateAsync<TState>(IEnumerable<TState> stateList) where TState : BaseCacheState, new()
    {
        return await UpdateAsync(stateList, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 保存多条数据（返回实际变更数；未变更项跳过，单事务原子，对齐 Mongo/PG 批量语义）。
    /// </summary>
    /// <remarks>
    /// Saves multiple documents (returns the actually-changed count; unchanged items are skipped).
    /// All writes share one transaction; when at least one row was written, each changed state receives
    /// the exact-snapshot <c>SaveToDbPostHandler(byte[])</c> commit (aligned with the Mongo/PG flow).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="stateList">要保存的数据列表 / The states to save</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>实际更新的数量 / The updated count</returns>
    public async Task<long> UpdateAsync<TState>(IEnumerable<TState> stateList, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureOpen();
        var cacheStates = stateList as TState[] ?? stateList?.ToArray();
        if (cacheStates == null || cacheStates.Length == 0)
        {
            return 0;
        }

        var changedStates = new List<(TState State, byte[] Data)>(cacheStates.Length);
        var currentTime = GetCurrentTimestamp();
        foreach (var state in cacheStates)
        {
            var (isChanged, changedData) = state.IsChanged();
            if (isChanged)
            {
                state.UpdateTime = currentTime;
                state.UpdateCount = (state.UpdateCount ?? 0) + 1;
                changedStates.Add((state, changedData));
            }
        }

        if (changedStates.Count == 0)
        {
            return 0;
        }

        var anyRowWritten = await ExecuteWriteWithRetryAsync(async _ => await ExecuteGatedAsync(async () =>
        {
            await EnsureTableAsync<TState>(cancellationToken).ConfigureAwait(false);
            var written = false;
            var (transaction, owned) = await BeginOrJoinTransactionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                foreach (var (state, _) in changedStates)
                {
                    using (var command = BuildReplaceDocumentCommand<TState>(state))
                    {
                        command.Transaction = transaction;
                        written |= await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) > 0;
                    }
                }

                if (owned)
                {
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                }

                return written;
            }
            finally
            {
                if (owned)
                {
                    await transaction.DisposeAsync().ConfigureAwait(false);
                }
            }
        }), cancellationToken, nameof(UpdateAsync), true).ConfigureAwait(false);
        if (anyRowWritten)
        {
            foreach (var (state, changedData) in changedStates)
            {
                // 精确提交：只提交实际送库快照，未变更项不再触碰基线
                state.SaveToDbPostHandler(changedData);
            }
        }

        return changedStates.Count;
    }

    /// <summary>
    /// 根据ID部分更新数据（读改写整档：字段写入 / null 移除 + UpdateTime 写入 + UpdateCount 自增；仅可见行，对齐 Mongo/PG）。
    /// </summary>
    /// <remarks>
    /// Partially updates by ID (load-modify-save whole document: non-null fields written, null-valued
    /// fields removed/zeroed; <c>UpdateTime</c> stamped and <c>UpdateCount</c> incremented).
    /// <c>Id/CreatedTime/CreatedId</c> are not updatable; only visible (non-soft-deleted) rows update;
    /// the return value aligns with ModifiedCount — 1 when a visible row was written, 0 otherwise.
    /// The null-removal of value-type members lands as the CLR default (JSON <c>0/false</c> reads
    /// equivalent to a missing key).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="id">数据ID / The document ID</param>
    /// <param name="updateFields">要更新的字段字典 / The fields to update</param>
    /// <returns>实际更新的行数 / The updated count</returns>
    public async Task<long> UpdatePartialAsync<TState>(long id, IReadOnlyDictionary<string, object> updateFields) where TState : BaseCacheState, new()
    {
        return await UpdatePartialAsync<TState>(id, updateFields, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 根据ID部分更新数据（读改写整档：字段写入 / null 移除 + UpdateTime 写入 + UpdateCount 自增；仅可见行，对齐 Mongo/PG）。
    /// </summary>
    /// <remarks>
    /// Partially updates by ID (load-modify-save whole document: non-null fields written, null-valued
    /// fields removed/zeroed; <c>UpdateTime</c> stamped and <c>UpdateCount</c> incremented).
    /// <c>Id/CreatedTime/CreatedId</c> are not updatable; only visible (non-soft-deleted) rows update;
    /// the return value aligns with ModifiedCount — 1 when a visible row was written, 0 otherwise.
    /// The null-removal of value-type members lands as the CLR default (JSON <c>0/false</c> reads
    /// equivalent to a missing key).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="id">数据ID / The document ID</param>
    /// <param name="updateFields">要更新的字段字典 / The fields to update</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>实际更新的行数 / The updated count</returns>
    public async Task<long> UpdatePartialAsync<TState>(long id, IReadOnlyDictionary<string, object> updateFields, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureOpen();
        if (updateFields == null || updateFields.Count == 0)
        {
            return 0;
        }

        var excludedKeys = new HashSet<string>
        {
            nameof(BaseCacheState.Id),
            nameof(BaseCacheState.CreatedTime),
            nameof(BaseCacheState.CreatedId),
        };
        var setFields = new Dictionary<string, object>(StringComparer.Ordinal);
        var removedKeys = new List<string>();
        foreach (var item in updateFields)
        {
            if (string.IsNullOrWhiteSpace(item.Key) || excludedKeys.Contains(item.Key))
            {
                continue;
            }

            if (item.Value == null)
            {
                removedKeys.Add(item.Key);
                continue;
            }

            setFields[item.Key] = item.Value;
        }

        if (setFields.Count == 0 && removedKeys.Count == 0)
        {
            return 0;
        }

        return await ExecuteWriteWithRetryAsync(async _ => await ExecuteGatedAsync(() => ExecuteUpdatePartialCoreAsync<TState>(id, setFields, removedKeys, cancellationToken)), cancellationToken, nameof(UpdatePartialAsync), true).ConfigureAwait(false);
    }

    /// <summary>
    /// UpdatePartialAsync 读改写核心（可见行读入 → set/removed 字段写入 → 时间戳推进 → 整档写回；行缺失/软删行返回 0）。
    /// </summary>
    /// <remarks>
    /// The load-modify-save core of UpdatePartialAsync: loads the visible row, applies set/removed
    /// fields, advances UpdateTime/UpdateCount and writes the whole document back; a missing or
    /// soft-deleted row returns 0.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="id">数据ID / The document ID</param>
    /// <param name="setFields">待写入字段 / The fields to set</param>
    /// <param name="removedKeys">待移除字段 / The keys to remove</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>实际更新的行数 / The updated count</returns>
    private async Task<long> ExecuteUpdatePartialCoreAsync<TState>(long id, Dictionary<string, object> setFields, List<string> removedKeys, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        await EnsureTableAsync<TState>(cancellationToken).ConfigureAwait(false);
        // 软删默认过滤生效：仅可见行可被部分更新
        // The soft-delete filter applies: only visible rows can be partially updated.
        var rows = await ReadRowsRawAsync<TState>(BuildIdEqualsCommand<TState>(id), cancellationToken).ConfigureAwait(false);
        if (rows.Count == 0)
        {
            return 0;
        }

        var document = rows[0];
        // 软删默认过滤生效：仅可见行可被部分更新（对齐 Mongo GetDefaultFindExpression / PG global query filter）。
        // The soft-delete filter applies: only visible rows can be partially updated (aligned with the Mongo default
        // find expression and the PG global query filter).
        if (document.IsDeleted == true)
        {
            return 0;
        }

        var stateType = typeof(TState);
        foreach (var item in setFields)
        {
            ApplyFieldValue(stateType, document, item.Key, item.Value);
        }

        foreach (var removedKey in removedKeys)
        {
            ApplyFieldValue(stateType, document, removedKey, null);
        }

        // UpdateTime/UpdateCount 恒推进：命中可见行且字段非空即视为修改（对齐 Mongo/PG 三方一致口径）。
        // UpdateTime/UpdateCount always advance: a visible row with non-empty fields always counts as modified.
        document.UpdateTime = GetCurrentTimestamp();
        document.UpdateCount = (document.UpdateCount ?? 0) + 1;
        using (var command = BuildReplaceDocumentCommand<TState>(document))
        {
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return 1L;
        }
    }

    /// <summary>
    /// 根据条件删除单条数据（软删除；先查再改，重复删除幂等返回 0，对齐 Mongo/PG）。
    /// </summary>
    /// <remarks>
    /// Soft-deletes the first visible document matching the filter (find-then-mark; repeated
    /// deletes idempotently return 0).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <returns>实际删除的数量 / The deleted count</returns>
    public async Task<long> DeleteAsync<TState>(Expression<Func<TState, bool>> filter) where TState : BaseCacheState, new()
    {
        return await DeleteAsync(filter, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 根据条件删除单条数据（软删除；先查再改，重复删除幂等返回 0，对齐 Mongo/PG）。
    /// </summary>
    /// <remarks>
    /// Soft-deletes the first visible document matching the filter (find-then-mark; repeated
    /// deletes idempotently return 0).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>实际删除的数量 / The deleted count</returns>
    public async Task<long> DeleteAsync<TState>(Expression<Func<TState, bool>> filter, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureOpen();
        var state = await FindAsync(filter, false, cancellationToken).ConfigureAwait(false);
        if (state == null)
        {
            return 0;
        }

        state.DeleteTime = GetCurrentTimestamp();
        state.IsDeleted = true;
        return await ExecuteSoftDeleteByIdAsync<TState>(state.Id, state.DeleteTime.GetValueOrDefault(), cancellationToken, nameof(DeleteAsync)).ConfigureAwait(false);
    }

    /// <summary>
    /// 删除指定对象（软删除）。
    /// </summary>
    /// <remarks>
    /// Soft-deletes the given state object.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="state">要删除的数据 / The state to delete</param>
    /// <returns>实际删除的数量 / The deleted count</returns>
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
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="state">要删除的数据 / The state to delete</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>实际删除的数量 / The deleted count</returns>
    public async Task<long> DeleteAsync<TState>(TState state, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureOpen();
        state.DeleteTime = GetCurrentTimestamp();
        state.IsDeleted = true;
        return await ExecuteSoftDeleteByIdAsync<TState>(state.Id, state.DeleteTime.GetValueOrDefault(), cancellationToken, nameof(DeleteAsync)).ConfigureAwait(false);
    }

    /// <summary>
    /// 根据条件批量删除数据（软删除；先查再单闸单事务批删，对齐 Mongo/PG 流程）。
    /// </summary>
    /// <remarks>
    /// Soft-deletes the visible documents matching the filter (find, then one gated single-transaction batch).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <returns>实际删除的数量 / The deleted count</returns>
    public async Task<long> DeleteListAsync<TState>(Expression<Func<TState, bool>> filter) where TState : BaseCacheState, new()
    {
        return await DeleteListAsync(filter, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 根据条件批量删除数据（软删除；先查再单闸单事务批删，对齐 Mongo/PG 流程）。
    /// </summary>
    /// <remarks>
    /// Soft-deletes the visible documents matching the filter (find, then one gated single-transaction batch).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>实际删除的数量 / The deleted count</returns>
    public async Task<long> DeleteListAsync<TState>(Expression<Func<TState, bool>> filter, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureOpen();
        var list = await FindListAsync(filter, cancellationToken).ConfigureAwait(false);
        if (list == null || list.Count == 0)
        {
            return 0;
        }

        var ids = new long[list.Count];
        for (var index = 0; index < list.Count; index++)
        {
            ids[index] = list[index].Id;
        }

        var deleteTime = GetCurrentTimestamp();
        return await ExecuteSoftDeleteBatchAsync<TState>(ids, deleteTime, cancellationToken, nameof(DeleteListAsync)).ConfigureAwait(false);
    }

    /// <summary>
    /// 根据ID列表批量删除数据（软删除；单事务批次，时间戳每次变化故重复调用仍计行，对齐 Mongo/PG）。
    /// </summary>
    /// <remarks>
    /// Soft-deletes documents by ID list (one transaction; the fresh timestamp means repeated
    /// calls still count rows, consistent with Mongo $set / PG SaveChanges behavior).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="ids">要删除的数据ID列表 / The IDs to delete</param>
    /// <returns>实际删除的数量 / The deleted count</returns>
    public async Task<long> DeleteListIdAsync<TState>(IEnumerable<long> ids) where TState : BaseCacheState, new()
    {
        return await DeleteListIdAsync<TState>(ids, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 根据ID列表批量删除数据（软删除；单事务批次，时间戳每次变化故重复调用仍计行，对齐 Mongo/PG）。
    /// </summary>
    /// <remarks>
    /// Soft-deletes documents by ID list (one transaction; the fresh timestamp means repeated
    /// calls still count rows, consistent with Mongo $set / PG SaveChanges behavior).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="ids">要删除的数据ID列表 / The IDs to delete</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>实际删除的数量 / The deleted count</returns>
    public async Task<long> DeleteListIdAsync<TState>(IEnumerable<long> ids, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureOpen();
        var idArray = ids as long[] ?? ids?.ToArray();
        if (idArray == null || idArray.Length == 0)
        {
            return 0;
        }

        var distinctIds = idArray.Distinct().ToArray();
        var deleteTime = GetCurrentTimestamp();
        return await ExecuteWriteWithRetryAsync(async _ => await ExecuteGatedAsync(async () =>
        {
            await EnsureTableAsync<TState>(cancellationToken).ConfigureAwait(false);
            var rows = await ReadRowsByIdsAsync<TState>(distinctIds, cancellationToken).ConfigureAwait(false);
            if (rows.Count == 0)
            {
                return 0L;
            }

            var (transaction, owned) = await BeginOrJoinTransactionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                foreach (var document in rows)
                {
                    document.IsDeleted = true;
                    document.DeleteTime = deleteTime;
                    using (var command = BuildReplaceDocumentCommand<TState>(document))
                    {
                        command.Transaction = transaction;
                        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    }
                }

                if (owned)
                {
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                }

                return (long)rows.Count;
            }
            finally
            {
                if (owned)
                {
                    await transaction.DisposeAsync().ConfigureAwait(false);
                }
            }
        }), cancellationToken, nameof(DeleteListIdAsync), true).ConfigureAwait(false);
    }

    /// <summary>
    /// 根据条件物理删除数据（忽略软删可见性，客户端求值 filter）。
    /// </summary>
    /// <remarks>
    /// Hard-deletes ALL rows matching the filter (soft-deleted included).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <returns>实际删除的数量 / The deleted count</returns>
    public async Task<long> HardDeleteAsync<TState>(Expression<Func<TState, bool>> filter) where TState : BaseCacheState, new()
    {
        return await HardDeleteAsync<TState>(filter, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 根据条件物理删除数据（忽略软删可见性，客户端求值 filter）。
    /// </summary>
    /// <remarks>
    /// Hard-deletes ALL rows matching the filter (soft-deleted included).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>实际删除的数量 / The deleted count</returns>
    public async Task<long> HardDeleteAsync<TState>(Expression<Func<TState, bool>> filter, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureOpen();
        var predicate = BuildPredicate(filter, includeDeleted: true);
        return await ExecuteWriteWithRetryAsync(async _ => await ExecuteGatedAsync(() => ExecuteHardDeleteCoreAsync<TState>(predicate, cancellationToken)), cancellationToken, nameof(HardDeleteAsync), true).ConfigureAwait(false);
    }

    /// <summary>
    /// HardDeleteAsync 核心执行体（读全表 → 客户端谓词筛 id → 事务内逐条物理删除）。
    /// </summary>
    /// <remarks>
    /// The core of HardDeleteAsync: reads all rows, selects matching ids via the client-side
    /// predicate (soft-deleted rows included), then hard-deletes each one inside a begin-or-join
    /// transaction.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="predicate">客户端求值谓词 / The client-side predicate (includes soft-deleted rows)</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>实际删除的数量 / The deleted count</returns>
    private async Task<long> ExecuteHardDeleteCoreAsync<TState>(Func<TState, bool> predicate, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        await EnsureTableAsync<TState>(cancellationToken).ConfigureAwait(false);
        var rows = await ReadRowsRawAsync<TState>(BuildSelectAllCommand<TState>(), cancellationToken).ConfigureAwait(false);
        var ids = new List<long>();
        foreach (var state in rows)
        {
            if (predicate(state))
            {
                ids.Add(state.Id);
            }
        }

        if (ids.Count == 0)
        {
            return 0L;
        }

        var (transaction, owned) = await BeginOrJoinTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var id in ids)
            {
                using (var command = _connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = BuildDeleteByIdCommandText<TState>();
                    command.Parameters.AddWithValue("$id", id);
                    await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
            }

            if (owned)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }

            return (long)ids.Count;
        }
        finally
        {
            if (owned)
            {
                await transaction.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// 根据条件恢复软删除数据（IsDeleted 置 false 且 DeleteTime 置 null，对齐 Mongo $unset）。
    /// </summary>
    /// <remarks>
    /// Restores soft-deleted documents matching the filter (sets IsDeleted to false and DeleteTime
    /// to null). Only rows currently marked deleted are touched (restore must see deleted rows).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <returns>实际恢复的数量 / The restored count</returns>
    public async Task<long> RestoreAsync<TState>(Expression<Func<TState, bool>> filter) where TState : BaseCacheState, new()
    {
        return await RestoreAsync<TState>(filter, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 根据条件恢复软删除数据（IsDeleted 置 false 且 DeleteTime 置 null，对齐 Mongo $unset）。
    /// </summary>
    /// <remarks>
    /// Restores soft-deleted documents matching the filter (sets IsDeleted to false and DeleteTime
    /// to null). Only rows currently marked deleted are touched (restore must see deleted rows).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="filter">过滤表达式 / The filter expression</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>实际恢复的数量 / The restored count</returns>
    public async Task<long> RestoreAsync<TState>(Expression<Func<TState, bool>> filter, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureOpen();
        var compiled = filter?.Compile();
        return await ExecuteWriteWithRetryAsync(async _ => await ExecuteGatedAsync(() => ExecuteRestoreCoreAsync<TState>(compiled, cancellationToken)), cancellationToken, nameof(RestoreAsync), true).ConfigureAwait(false);
    }

    /// <summary>
    /// RestoreAsync 核心执行体（读全表 → 软删行按谓词筛入 → 事务内整档写回复活）。
    /// </summary>
    /// <remarks>
    /// The core of RestoreAsync: reads all rows, selects soft-deleted rows matching the compiled
    /// filter (a null filter matches every soft-deleted row), then writes each restored document
    /// back inside a begin-or-join transaction.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="compiled">已编译过滤表达式；null 表示匹配全部软删行 / The compiled filter; null matches every soft-deleted row</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>实际恢复的数量 / The restored count</returns>
    private async Task<long> ExecuteRestoreCoreAsync<TState>(Func<TState, bool> compiled, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        await EnsureTableAsync<TState>(cancellationToken).ConfigureAwait(false);
        var rows = await ReadRowsRawAsync<TState>(BuildSelectAllCommand<TState>(), cancellationToken).ConfigureAwait(false);
        var restored = new List<TState>();
        foreach (var state in rows)
        {
            if (state.IsDeleted == true && (compiled == null || compiled(state)))
            {
                restored.Add(state);
            }
        }

        if (restored.Count == 0)
        {
            return 0L;
        }

        var (transaction, owned) = await BeginOrJoinTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            foreach (var document in restored)
            {
                document.IsDeleted = false;
                document.DeleteTime = null;
                using (var command = BuildReplaceDocumentCommand<TState>(document))
                {
                    command.Transaction = transaction;
                    await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
            }

            if (owned)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }

            return (long)restored.Count;
        }
        finally
        {
            if (owned)
            {
                await transaction.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// 在事务中执行操作（单连接事务壳 BEGIN → action → COMMIT；action 内全部数据操作经环境事务加入本事务，壳唯一负责提交；嵌套事务壳加入环境事务）。限制：action 内禁止 await 同一实例不经环境事务放行的方法（如 OpenAsync / CloseAsync）——连接闸不可重入会死锁，等待超时后抛 TimeoutException。
    /// </summary>
    /// <remarks>
    /// Executes the action inside a transaction on the single connection (BEGIN → action → COMMIT).
    /// Every data operation issued by the action joins the shell's transaction through the ambient
    /// transaction (owned write paths skip their own BEGIN/COMMIT and enlist in it), so the shell is
    /// the single commit point. A nested shell inside an active transaction simply joins it (SQLite
    /// cannot nest transactions). The shell itself runs single-pass by design — retrying a shell
    /// would replay the action; the data operations it wraps retry individually via the shared
    /// resilience engine.
    /// Restriction: never await a method of the same <see cref="SqliteDbService"/> instance that
    /// bypasses the ambient transaction (e.g. <see cref="OpenAsync"/>/<see cref="CloseAsync"/>) inside
    /// the action — the gate semaphore is not reentrant and this deadlocks (it fails with a
    /// <see cref="TimeoutException"/> after <see cref="GateWaitTimeout"/> instead of hanging).
    /// </remarks>
    /// <param name="action">要在事务中执行的操作 / The action to execute inside the transaction</param>
    /// <returns>异步任务 / Async task</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="action"/> 为 null 时抛出 / Thrown when action is null</exception>
    /// <exception cref="TimeoutException">当连接闸在 <see cref="GateWaitTimeout"/> 内不可得时抛出 / Thrown when the connection gate is not acquired within <see cref="GateWaitTimeout"/></exception>
    public async Task ExecuteInTransactionAsync(Func<Task> action)
    {
        await ExecuteInTransactionAsync(action, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 在事务中执行操作（单连接事务壳 BEGIN → action → COMMIT；action 内全部数据操作经环境事务加入本事务，壳唯一负责提交；嵌套事务壳加入环境事务）。限制：action 内禁止 await 同一实例不经环境事务放行的方法（如 OpenAsync / CloseAsync）——连接闸不可重入会死锁，等待超时后抛 TimeoutException。
    /// </summary>
    /// <remarks>
    /// Executes the action inside a transaction on the single connection (BEGIN → action → COMMIT).
    /// Every data operation issued by the action joins the shell's transaction through the ambient
    /// transaction (owned write paths skip their own BEGIN/COMMIT and enlist in it), so the shell is
    /// the single commit point. A nested shell inside an active transaction simply joins it (SQLite
    /// cannot nest transactions). The shell itself runs single-pass by design — retrying a shell
    /// would replay the action; the data operations it wraps retry individually via the shared
    /// resilience engine.
    /// Restriction: never await a method of the same <see cref="SqliteDbService"/> instance that
    /// bypasses the ambient transaction (e.g. <see cref="OpenAsync"/>/<see cref="CloseAsync"/>) inside
    /// the action — the gate semaphore is not reentrant and this deadlocks (it fails with a
    /// <see cref="TimeoutException"/> after <see cref="GateWaitTimeout"/> instead of hanging).
    /// </remarks>
    /// <param name="action">要在事务中执行的操作 / The action to execute inside the transaction</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>异步任务 / Async task</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="action"/> 为 null 时抛出 / Thrown when action is null</exception>
    /// <exception cref="TimeoutException">当连接闸在 <see cref="GateWaitTimeout"/> 内不可得时抛出 / Thrown when the connection gate is not acquired within <see cref="GateWaitTimeout"/></exception>
    public async Task ExecuteInTransactionAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureOpen();
        ArgumentNullException.ThrowIfNull(action);
        if (AmbientTransaction.Value?.Transaction != null)
        {
            // 嵌套事务壳：加入环境事务（操作已绑同一事务，提交权归最外层壳）。
            // Nested shell: join the ambient transaction (the outermost shell owns the commit).
            await action().ConfigureAwait(false);
            return;
        }

        await WaitGateAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using (var transaction = (SqliteTransaction)await _connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false))
            {
                var holder = new AmbientTransactionHolder { Transaction = transaction, };
                AmbientTransaction.Value = holder;
                var committed = false;
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await action().ConfigureAwait(false);
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    committed = true;
                }
                finally
                {
                    // 先清持有对象内部字段（派生异步流共享同一引用，同步看到 null），再清当前流的 AsyncLocal 槽。
                    // Clear the holder's field first (derived async flows share the same reference and
                    // observe null immediately), then clear this flow's AsyncLocal slot.
                    holder.Transaction = null;
                    AmbientTransaction.Value = null;
                    if (!committed)
                    {
                        // 未提交即回滚：壳内首建的表与唯一索引 DDL 随之撤销，就绪缓存须整体失效——否则后续操作
                        // 撞「no such table」，或唯一索引缺失而快路径静默放行重复键。DDL 均 IF NOT EXISTS，重建幂等；
                        // 壳全程持有 _gate，清理无竞态。
                        // Not committed means rolled back: DDL first issued inside the shell (tables and unique
                        // indexes) is undone with it, so the readiness cache must be invalidated — otherwise later
                        // operations hit "no such table", or a missing unique index lets the fast path silently
                        // admit duplicate keys. Every DDL is IF NOT EXISTS (idempotent rebuild); the shell holds
                        // _gate throughout, so the clear is race-free.
                        _tableReady.Clear();
                    }
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 按 id 软删除（读改写整档；重复同毫秒删除幂等返回 0，对齐 Mongo ModifiedCount / PG 变更检测）。
    /// </summary>
    /// <remarks>
    /// Soft-deletes by id: the stored document is loaded (bypassing visibility) and its
    /// IsDeleted/DeleteTime set; a same-timestamp repeat against already-identical values counts as
    /// unchanged and returns 0; a fresh delete timestamp always counts. Missing rows return 0.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="id">数据ID / The document ID</param>
    /// <param name="deleteTime">删除时间戳 / The deletion timestamp</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <param name="operationName">操作名称（指标与日志）/ Operation name for metrics and logging</param>
    /// <returns>实际删除的数量 / The deleted count</returns>
    private async Task<long> ExecuteSoftDeleteByIdAsync<TState>(long id, long deleteTime, CancellationToken cancellationToken, string operationName) where TState : BaseCacheState, new()
    {
        return await ExecuteWriteWithRetryAsync(async _ => await ExecuteGatedAsync(async () =>
        {
            await EnsureTableAsync<TState>(cancellationToken).ConfigureAwait(false);
            var rows = await ReadRowsRawAsync<TState>(BuildIdEqualsCommand<TState>(id), cancellationToken).ConfigureAwait(false);
            if (rows.Count == 0)
            {
                return 0L;
            }

            var document = rows[0];
            if (document.IsDeleted == true && document.DeleteTime == deleteTime)
            {
                // 同毫秒重复删除：文档不变返回 0（幂等，对齐 Mongo ModifiedCount / EF 变更检测）。
                // Same-timestamp repeat: the document is unchanged and counts as 0 (idempotent).
                return 0L;
            }

            document.IsDeleted = true;
            document.DeleteTime = deleteTime;
            using (var command = BuildReplaceDocumentCommand<TState>(document))
            {
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                return 1L;
            }
            }), cancellationToken, operationName, true).ConfigureAwait(false);
    }

    /// <summary>
    /// 按 id 批量软删除（单闸单事务批写：闸内按 id 分块重读 → 同毫秒幂等跳过 → 单事务写回；逐条计数语义与单条路径一致）。
    /// </summary>
    /// <remarks>
    /// Soft-deletes the given ids as ONE gated, single-transaction batch: the rows are re-read by id
    /// inside the gate (chunked id-IN), a same-timestamp already-deleted row counts as unchanged (0),
    /// and every other re-read row is marked and written inside one transaction — the per-row counting
    /// semantics are identical to the single-row path.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="ids">去重后的ID集合 / The distinct IDs</param>
    /// <param name="deleteTime">删除时间戳 / The deletion timestamp</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <param name="operationName">操作名称（指标与日志）/ Operation name for metrics and logging</param>
    /// <returns>实际删除的数量 / The deleted count</returns>
    private async Task<long> ExecuteSoftDeleteBatchAsync<TState>(long[] ids, long deleteTime, CancellationToken cancellationToken, string operationName) where TState : BaseCacheState, new()
    {
        return await ExecuteWriteWithRetryAsync(async _ => await ExecuteGatedAsync(async () =>
        {
            await EnsureTableAsync<TState>(cancellationToken).ConfigureAwait(false);
            var rows = await ReadRowsByIdsAsync<TState>(ids, cancellationToken).ConfigureAwait(false);
            if (rows.Count == 0)
            {
                return 0L;
            }

            long modifiedCount = 0;
            await using (var transaction = (SqliteTransaction)await _connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false))
            {
                foreach (var document in rows)
                {
                    if (document.IsDeleted == true && document.DeleteTime == deleteTime)
                    {
                        // 同毫秒重复删除：文档不变计 0（幂等，对齐单条路径）。
                        // Same-timestamp repeat: the document is unchanged and counts as 0 (idempotent, single-row parity).
                        continue;
                    }

                    document.IsDeleted = true;
                    document.DeleteTime = deleteTime;
                    using (var command = BuildReplaceDocumentCommand<TState>(document))
                    {
                        command.Transaction = transaction;
                        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                        modifiedCount++;
                    }
                }

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return modifiedCount;
            }
        }), cancellationToken, operationName, true).ConfigureAwait(false);
    }

    /// <summary>
    /// 反射写入文档字段值（null → 引用/可空置 null，值类型置 CLR 默认值；类型不匹配时按 IConvertible 转换；对齐 PG）。
    /// </summary>
    /// <remarks>
    /// Reflectively assigns a document field (null assigns null to reference/nullable members and the CLR
    /// default to value types; mismatched runtime types convert via <see cref="IConvertible"/>). Unknown
    /// property names are ignored. Migrated verbatim from the PostgreSQL adapter.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="stateType">状态类型 / The state type</param>
    /// <param name="document">文档实例 / The document instance</param>
    /// <param name="fieldName">字段名 / The field name</param>
    /// <param name="value">字段值 / The field value</param>
    private static void ApplyFieldValue<TState>(Type stateType, TState document, string fieldName, object value) where TState : BaseCacheState, new()
    {
        var property = stateType.GetProperty(fieldName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.FlattenHierarchy);
        if (property == null || !property.CanWrite)
        {
            return;
        }

        var propertyType = property.PropertyType;
        var underlyingType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        if (value == null)
        {
            property.SetValue(document, propertyType.IsValueType && Nullable.GetUnderlyingType(propertyType) == null ? Activator.CreateInstance(propertyType) : null);
            return;
        }

        var valueType = Nullable.GetUnderlyingType(value.GetType()) ?? value.GetType();
        if (valueType != underlyingType)
        {
            if (value is IConvertible && typeof(IConvertible).IsAssignableFrom(underlyingType))
            {
                value = Convert.ChangeType(value, underlyingType, System.Globalization.CultureInfo.InvariantCulture);
            }
            else
            {
                return;
            }
        }

        property.SetValue(document, value);
    }

    /// <summary>
    /// 获取连接闸（带 <see cref="GateWaitTimeout"/> 超时；超时抛带定位提示的 TimeoutException——事务壳持闸期间 action 内 await 同实例不经环境事务放行的方法会不可重入死锁，限时失败而非永久挂起）。
    /// </summary>
    /// <remarks>
    /// Acquires the connection gate with the <see cref="GateWaitTimeout"/> timeout; a timeout throws
    /// a <see cref="TimeoutException"/> carrying a locating hint — while a transaction shell holds
    /// the gate, awaiting a same-instance method that bypasses the ambient transaction (e.g.
    /// OpenAsync/CloseAsync) deadlocks on the non-reentrant semaphore, so the acquisition fails
    /// within the timeout instead of hanging forever.
    /// </remarks>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <exception cref="TimeoutException">当闸在 <see cref="GateWaitTimeout"/> 内不可得时抛出 / Thrown when the gate is not acquired within <see cref="GateWaitTimeout"/></exception>
    /// <returns>异步任务 / Async task</returns>
    private async Task WaitGateAsync(CancellationToken cancellationToken)
    {
        var acquired = await _gate.WaitAsync(GateWaitTimeout, cancellationToken).ConfigureAwait(false);
        if (!acquired)
        {
            throw new SqliteGateWaitTimeoutException(LocalizationService.GetString(Keys.Database.Sqlite.TransactionGateWaitTimeout, GateWaitTimeout.TotalSeconds));
        }
    }

    /// <summary>
    /// 在串行闸下执行操作（环境事务内直接放行——事务壳已持有闸）。
    /// </summary>
    /// <remarks>
    /// Runs the operation under the serialization gate; inside an ambient transaction it runs
    /// directly (the transaction shell already holds the gate).
    /// </remarks>
    /// <param name="operation">要执行的操作 / The operation</param>
    /// <returns>异步任务 / Async task</returns>
    private async Task ExecuteGatedAsync(Func<Task> operation)
    {
        if (AmbientTransaction.Value?.Transaction != null)
        {
            await operation().ConfigureAwait(false);
            return;
        }

        await WaitGateAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            await operation().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 在串行闸下执行带返回值的操作（环境事务内直接放行）。
    /// </summary>
    /// <remarks>
    /// Runs the value-returning operation under the serialization gate; inside an ambient
    /// transaction it runs directly.
    /// </remarks>
    /// <typeparam name="T">返回值类型 / The result type</typeparam>
    /// <param name="operation">要执行的操作 / The operation</param>
    /// <returns>操作结果 / The result</returns>
    private async Task<T> ExecuteGatedAsync<T>(Func<Task<T>> operation)
    {
        if (AmbientTransaction.Value?.Transaction != null)
        {
            return await operation().ConfigureAwait(false);
        }

        await WaitGateAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            return await operation().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 获取本操作应使用的事务：环境事务存在则加入（不 BEGIN/COMMIT，提交权归事务壳），否则新开自持事务。
    /// </summary>
    /// <remarks>
    /// Resolves the transaction for the calling write path: inside an ambient transaction shell the
    /// ambient transaction is joined (no BEGIN/COMMIT — the shell owns the final commit); otherwise a
    /// new owned transaction is started and the caller must commit and dispose it.
    /// </remarks>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>事务与是否自持（自持时调用方负责 Commit 与 Dispose）/ The transaction and whether the caller owns it</returns>
    private async Task<(SqliteTransaction Transaction, bool Owned)> BeginOrJoinTransactionAsync(CancellationToken cancellationToken)
    {
        var ambient = AmbientTransaction.Value?.Transaction;
        if (ambient != null)
        {
            return (ambient, false);
        }

        return ((SqliteTransaction)await _connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false), true);
    }

    /// <summary>
    /// 惰性建表（首访问单飞；CREATE TABLE IF NOT EXISTS，对齐 PG 的 42P07 容忍语义）。
    /// </summary>
    /// <remarks>
    /// Lazily creates the state table on first access (single-flight per table; the
    /// IF NOT EXISTS clause makes a concurrent create a no-op, the SQLite equivalent of the
    /// PostgreSQL adapter's duplicate-table tolerance).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>异步任务 / Async task</returns>
    private Task EnsureTableAsync<TState>(CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        var tableName = typeof(TState).Name;
        var lazy = _tableReady.GetOrAdd(tableName, _ => new Lazy<Task>(CreateTableAsync<TState>, LazyThreadSafetyMode.ExecutionAndPublication));
        if (lazy.Value.IsFaulted)
        {
            // 失败任务驱逐：下次调用重建单飞（对齐 PG 适配器的故障重试语义）。
            // Evict the faulted task so the next call retries (aligned with the PG adapter).
            _tableReady.TryRemove(new KeyValuePair<string, Lazy<Task>>(tableName, lazy));
            lazy = _tableReady.GetOrAdd(tableName, _ => new Lazy<Task>(CreateTableAsync<TState>, LazyThreadSafetyMode.ExecutionAndPublication));
        }

        return lazy.Value;
    }

    /// <summary>
    /// 建表 DDL 执行（调用方须持有 <see cref="_gate"/>；CREATE TABLE IF NOT EXISTS 幂等），并引导声明式唯一索引。
    /// </summary>
    /// <remarks>
    /// Runs the CREATE TABLE IF NOT EXISTS DDL (the caller must hold <see cref="_gate"/>), then bootstraps the
    /// declared unique expression indexes: each public instance property carrying
    /// <c>[EntityIndex(Unique = true)]</c> is materialized as a
    /// <c>json_extract(doc, '$.「JSON属性名」')</c> UNIQUE index, mirroring Mongo's
    /// <c>CreateIndexesAsync</c> and PG's <c>EnsureIndexesAsync</c> so uniqueness is enforced by the engine
    /// at the index layer (see C326). A bootstrap failure (e.g. pre-existing duplicate values) faults the
    /// single-flight task so the next access retries; the raw <see cref="SqliteException"/> propagates.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <returns>异步任务 / Async task</returns>
    private async Task CreateTableAsync<TState>() where TState : BaseCacheState, new()
    {
        using (var command = _connection.CreateCommand())
        {
            command.CommandText = BuildCreateTableCommandText<TState>();
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        foreach (var declaration in ResolveUniqueIndexDeclarations<TState>())
        {
            string storedDefinition;
            using (var probeCommand = _connection.CreateCommand())
            {
                probeCommand.CommandText = "SELECT sql FROM sqlite_master WHERE type = 'index' AND name = $name;";
                probeCommand.Parameters.AddWithValue("$name", declaration.IndexName);
                storedDefinition = await probeCommand.ExecuteScalarAsync().ConfigureAwait(false) as string;
            }

            if (storedDefinition != null && IsUniqueIndexDefinitionCurrent(storedDefinition, declaration.JsonName))
            {
                continue;
            }

            if (storedDefinition != null)
            {
                // 同名异构（属性改名 / JsonPropertyName 变更 / 唯一性变化）：IF NOT EXISTS 对同名索引为 no-op，旧索引会
                // 原样残留在旧 JSON 路径上——新文档该路径为 NULL，唯一约束形同虚设、快路径静默放行重复键。先删后建对齐声明
                // （同 PG 适配器同名异构重建语义）。
                // Same name, drifted shape (renamed property / changed JsonPropertyName / uniqueness change): IF NOT
                // EXISTS is a no-op for a same-name index, so the stale index would stay on the old JSON path — new
                // documents carry NULL there, voiding the constraint and letting the fast path silently admit
                // duplicates. Drop and recreate to match the declaration (same as the PG adapter's drift rebuild).
                using (var dropCommand = _connection.CreateCommand())
                {
                    dropCommand.CommandText = "DROP INDEX IF EXISTS " + QuoteIdentifier(declaration.IndexName) + ";";
                    await dropCommand.ExecuteNonQueryAsync().ConfigureAwait(false);
                }
            }

            using (var indexCommand = _connection.CreateCommand())
            {
                indexCommand.CommandText = BuildUniqueIndexCommandText<TState>(declaration.IndexName, declaration.JsonName);
                await indexCommand.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// 判定已存储的索引定义是否与当前唯一索引声明一致（唯一 + 同一 JSON 路径表达式）。
    /// </summary>
    /// <remarks>
    /// Decides whether a stored index definition (the <c>sqlite_master.sql</c> text, which SQLite keeps
    /// verbatim minus the <c>IF NOT EXISTS</c> clause) still matches the current unique-index declaration:
    /// it must be a UNIQUE index over exactly the expected <c>json_extract</c> expression.
    /// </remarks>
    /// <param name="storedDefinition">已存储的索引定义 / The stored index definition</param>
    /// <param name="jsonPropertyName">JSON 属性名 / The JSON property name</param>
    /// <returns>一致返回 <c>true</c>；否则返回 <c>false</c> / <c>true</c> when current; otherwise <c>false</c></returns>
    private static bool IsUniqueIndexDefinitionCurrent(string storedDefinition, string jsonPropertyName)
    {
        return storedDefinition.StartsWith("CREATE UNIQUE INDEX ", StringComparison.OrdinalIgnoreCase)
               && storedDefinition.EndsWith("(" + BuildUniqueIndexExpression(jsonPropertyName) + ")", StringComparison.Ordinal);
    }

    /// <summary>
    /// 构建唯一索引的 <c>json_extract</c> 表达式片段（JSON 名内单引号按 SQL 字面量规则双写）。
    /// </summary>
    /// <remarks>
    /// Builds the <c>json_extract</c> expression fragment of a unique index (single quotes inside the JSON
    /// name are doubled per SQL string-literal rules). Shared by the DDL builder and the drift check so both
    /// always agree on the exact text.
    /// </remarks>
    /// <param name="jsonPropertyName">JSON 属性名 / The JSON property name</param>
    /// <returns>表达式片段 / The expression fragment</returns>
    private static string BuildUniqueIndexExpression(string jsonPropertyName)
    {
        return "json_extract(doc, '$.\"" + jsonPropertyName.Replace("'", "''", StringComparison.Ordinal) + "\"')";
    }

    /// <summary>
    /// 解析状态类型的声明式唯一索引集（公共实例属性上的 <c>[EntityIndex(Unique = true)]</c> → 索引名 / JSON 属性名）。
    /// </summary>
    /// <remarks>
    /// Resolves the declared unique-index set of a state type: public instance properties annotated with
    /// <c>[EntityIndex(Unique = true)]</c> map to (property, index name, JSON property name) triplets.
    /// The index name is composed as <c>{状态类型名}_{声明名}</c> — SQLite index names are schema-global, so the
    /// type prefix makes cross-table name collisions structurally impossible. Declarations are skipped (not
    /// indexed, fast path disabled — preserving the pre-change scan semantics) when: the declared name is blank
    /// (same tolerance as the PG adapter), the property type is not in the indexable set (ulong's full range
    /// exceeds SQLite's INTEGER and degrades to REAL under <c>json_extract</c>; float/double/decimal/DateTime/Guid
    /// equality semantics cannot be reasoned about against the json_extract value domain), the property carries
    /// <c>[JsonIgnore]</c> (never written into <c>doc</c> — the expression would be permanently NULL and UNIQUE
    /// admits NULLs, silently voiding the constraint), or the JSON name contains a double quote / backslash
    /// (unescapable inside a SQLite JSON path quoted label).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <returns>唯一索引声明三元组 / The unique-index declaration triplets</returns>
    private static (PropertyInfo Property, string IndexName, string JsonName)[] ResolveUniqueIndexDeclarations<TState>() where TState : BaseCacheState, new()
    {
        // 每状态类型反射一次：快路径判定在每次 TryAddUniqueAsync 上执行，不得重复反射。
        // Reflect once per state type: the fast-path decision runs on every TryAddUniqueAsync call.
        return UniqueIndexDeclarationCache.GetOrAdd(typeof(TState), static _ => ReflectUniqueIndexDeclarations<TState>());
    }

    /// <summary>
    /// 反射构建状态类型的唯一索引声明集（结果由 <see cref="UniqueIndexDeclarationCache"/> 按类型缓存）。
    /// </summary>
    /// <remarks>
    /// Reflects the unique-index declaration set of a state type; the result is cached per type by
    /// <see cref="UniqueIndexDeclarationCache"/> (see <see cref="ResolveUniqueIndexDeclarations{TState}"/>
    /// for the skip rules).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <returns>唯一索引声明三元组 / The unique-index declaration triplets</returns>
    private static (PropertyInfo Property, string IndexName, string JsonName)[] ReflectUniqueIndexDeclarations<TState>() where TState : BaseCacheState, new()
    {
        var properties = typeof(TState).GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var declarations = new List<(PropertyInfo, string, string)>();
        foreach (var property in properties)
        {
            var indexAttribute = property.GetCustomAttribute<EntityIndexAttribute>();
            if (indexAttribute == null || !indexAttribute.Unique || string.IsNullOrWhiteSpace(indexAttribute.Name))
            {
                continue;
            }

            if (!IsIndexableUniquePropertyType(property.PropertyType))
            {
                continue;
            }

            if (property.GetCustomAttribute<JsonIgnoreAttribute>() != null)
            {
                continue;
            }

            var jsonName = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name;
            if (string.IsNullOrEmpty(jsonName))
            {
                jsonName = property.Name;
            }

            if (jsonName.Contains('\"') || jsonName.Contains('\\'))
            {
                continue;
            }

            declarations.Add((property, typeof(TState).Name + "_" + indexAttribute.Name, jsonName));
        }

        return declarations.ToArray();
    }

    /// <summary>
    /// 判定唯一索引属性类型是否可物化为 <c>json_extract</c> 表达式索引（CLR 等值语义与引擎值域无漂移面）。
    /// </summary>
    /// <remarks>
    /// Decides whether a unique-index property type can be materialized as a <c>json_extract</c> expression
    /// index without a semantic drift gap between CLR equality and the engine value domain: strings (ordinal
    /// equality ≡ SQLite BINARY collation), <c>bool</c> and the signed/unsigned integral family except
    /// <c>ulong</c> (JSON integer → SQLite INTEGER, exact), and enums (serialized as their underlying integer).
    /// Excluded: <c>ulong</c> (values beyond int64 degrade to lossy REAL), <c>char</c>, floating point and
    /// <c>decimal</c> (text/precision drift), <c>DateTime</c>/<c>DateTimeOffset</c> (Kind/offset representation
    /// drift), <c>Guid</c> and any other shape — such declarations keep the legacy scan semantics.
    /// </remarks>
    /// <param name="propertyType">属性 CLR 类型 / The property CLR type</param>
    /// <returns>可索引返回 <c>true</c>；否则返回 <c>false</c> / <c>true</c> when indexable; otherwise <c>false</c></returns>
    private static bool IsIndexableUniquePropertyType(Type propertyType)
    {
        var effectiveType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        if (effectiveType.IsEnum)
        {
            return true;
        }

        return effectiveType == typeof(string)
               || effectiveType == typeof(bool)
               || effectiveType == typeof(int)
               || effectiveType == typeof(uint)
               || effectiveType == typeof(long)
               || effectiveType == typeof(short)
               || effectiveType == typeof(ushort)
               || effectiveType == typeof(byte)
               || effectiveType == typeof(sbyte);
    }

    /// <summary>
    /// 引用任意标识符（双引号转义，同 <see cref="QuoteTableName{TState}"/> 的不变量）。
    /// </summary>
    /// <remarks>
    /// Quotes an arbitrary identifier as a SQLite identifier (same invariant as
    /// <see cref="QuoteTableName{TState}"/>).
    /// </remarks>
    /// <param name="identifier">标识符 / The identifier</param>
    /// <returns>转义后的标识符 / The quoted identifier</returns>
    private static string QuoteIdentifier(string identifier)
    {
        return "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    /// <summary>
    /// 构建声明式唯一索引 DDL 文本（索引名 = 编译期封闭的类型名 + 声明名，JSON 属性名 = 属性名或 <c>[JsonPropertyName]</c>，均非运行期输入）。
    /// </summary>
    /// <remarks>
    /// Builds the CREATE UNIQUE INDEX IF NOT EXISTS text. The index name composes the compile-time closed
    /// type name and the declared attribute name; the JSON property name is the property name or its
    /// <c>[JsonPropertyName]</c> override — neither is runtime input; SQLite identifiers and JSON path labels
    /// cannot be bound as parameters, so the composition is centralized in this builder with the invariant
    /// stated (Sonar S2077). Embedded single quotes in the JSON name are doubled for the SQL string literal.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="indexName">索引名（已含类型前缀）/ The index name (type-prefixed)</param>
    /// <param name="jsonPropertyName">JSON 属性名 / The JSON property name</param>
    /// <returns>DDL 文本 / The DDL text</returns>
    private static string BuildUniqueIndexCommandText<TState>(string indexName, string jsonPropertyName) where TState : BaseCacheState, new()
    {
        return "CREATE UNIQUE INDEX IF NOT EXISTS " + QuoteIdentifier(indexName) + " ON " + QuoteTableName<TState>() + " (" + BuildUniqueIndexExpression(jsonPropertyName) + ");";
    }

    /// <summary>
    /// 引用状态类型表名（双引号转义标识符）。
    /// </summary>
    /// <remarks>
    /// Quotes the state-type table name as a SQLite identifier.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <returns>转义后的表名 / The quoted table name</returns>
    private static string QuoteTableName<TState>() where TState : BaseCacheState, new()
    {
        return "\"" + typeof(TState).Name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    /// <summary>
    /// 构建建表 DDL 文本（表名 = 编译期封闭类型名双引号转义，非运行期输入；标识符不可绑定参数，SQL 文本组合集中于此）。
    /// </summary>
    /// <remarks>
    /// Builds the CREATE TABLE IF NOT EXISTS text. The table name is the compile-time closed generic
    /// type name double-quote escaped (never runtime input); SQLite identifiers cannot be bound as
    /// parameters, so SQL text composition is centralized in these builders.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <returns>DDL 文本 / The DDL text</returns>
    private static string BuildCreateTableCommandText<TState>() where TState : BaseCacheState, new()
    {
        return "CREATE TABLE IF NOT EXISTS " + QuoteTableName<TState>() + " (id INTEGER PRIMARY KEY, doc TEXT NOT NULL);";
    }

    /// <summary>
    /// 构建按 id 物理删除的 SQL 文本（表名不变量同上，id 值经绑定参数传递）。
    /// </summary>
    /// <remarks>
    /// Builds the DELETE-by-id text (same table-name invariant as above; the id value is bound as a parameter).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <returns>SQL 文本 / The SQL text</returns>
    private static string BuildDeleteByIdCommandText<TState>() where TState : BaseCacheState, new()
    {
        return "DELETE FROM " + QuoteTableName<TState>() + " WHERE id = $id;";
    }

    /// <summary>
    /// 构建插入 SQL 文本（表名不变量同上，id/doc 均经绑定参数传递）。
    /// </summary>
    /// <remarks>
    /// Builds the INSERT text (same table-name invariant as above; id and doc are bound as parameters).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <returns>SQL 文本 / The SQL text</returns>
    private static string BuildInsertCommandText<TState>() where TState : BaseCacheState, new()
    {
        return "INSERT INTO " + QuoteTableName<TState>() + " (id, doc) VALUES ($id, $doc);";
    }

    /// <summary>
    /// 构建原子 upsert SQL 文本（表名不变量同上，id/doc 均经绑定参数传递）。
    /// </summary>
    /// <remarks>
    /// Builds the atomic INSERT ... ON CONFLICT(id) DO UPDATE text (same table-name invariant as
    /// above; id and doc are bound as parameters).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <returns>SQL 文本 / The SQL text</returns>
    private static string BuildUpsertCommandText<TState>() where TState : BaseCacheState, new()
    {
        return "INSERT INTO " + QuoteTableName<TState>() + " (id, doc) VALUES ($id, $doc) ON CONFLICT(id) DO UPDATE SET doc = excluded.doc;";
    }

    /// <summary>
    /// 构建整文档替换 SQL 文本（表名不变量同上，id/doc 均经绑定参数传递）。
    /// </summary>
    /// <remarks>
    /// Builds the whole-document UPDATE text (same table-name invariant as above; id and doc are
    /// bound as parameters).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <returns>SQL 文本 / The SQL text</returns>
    private static string BuildReplaceDocumentCommandText<TState>() where TState : BaseCacheState, new()
    {
        return "UPDATE " + QuoteTableName<TState>() + " SET doc = $doc WHERE id = $id;";
    }

    /// <summary>
    /// 构建插入命令（主键冲突原样上抛）。
    /// </summary>
    /// <remarks>
    /// Builds the INSERT command (unique-constraint conflicts propagate).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="state">要插入的状态 / The state to insert</param>
    /// <returns>插入命令 / The insert command</returns>
    private SqliteCommand BuildInsertCommand<TState>(TState state) where TState : BaseCacheState, new()
    {
        var command = _connection.CreateCommand();
        command.CommandText = BuildInsertCommandText<TState>();
        command.Parameters.AddWithValue("$id", state.Id);
        command.Parameters.AddWithValue("$doc", SerializeDocument(state));
        return command;
    }

    /// <summary>
    /// 构建原子 upsert 命令（整文档替换，软删行复活）。
    /// </summary>
    /// <remarks>
    /// Builds the atomic INSERT ... ON CONFLICT(id) DO UPDATE whole-document replacement
    /// (soft-deleted rows revive).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="state">要 upsert 的状态 / The state to upsert</param>
    /// <returns>upsert 命令 / The upsert command</returns>
    private SqliteCommand BuildUpsertCommand<TState>(TState state) where TState : BaseCacheState, new()
    {
        var command = _connection.CreateCommand();
        command.CommandText = BuildUpsertCommandText<TState>();
        command.Parameters.AddWithValue("$id", state.Id);
        command.Parameters.AddWithValue("$doc", SerializeDocument(state));
        return command;
    }

    /// <summary>
    /// 构建整文档替换命令（UPDATE ... WHERE id，行缺失返回 0）。
    /// </summary>
    /// <remarks>
    /// Builds the whole-document UPDATE ... WHERE id command (returns 0 affected for a missing row).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="state">要写回的状态 / The state to write back</param>
    /// <returns>替换命令 / The replace command</returns>
    private SqliteCommand BuildReplaceDocumentCommand<TState>(TState state) where TState : BaseCacheState, new()
    {
        var command = _connection.CreateCommand();
        command.CommandText = BuildReplaceDocumentCommandText<TState>();
        command.Parameters.AddWithValue("$id", state.Id);
        command.Parameters.AddWithValue("$doc", SerializeDocument(state));
        return command;
    }

    /// <summary>
    /// 序列化状态对象为 doc 文本。
    /// </summary>
    /// <remarks>
    /// Serializes a state object to its doc text.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="state">状态对象 / The state</param>
    /// <returns>doc 文本 / The doc text</returns>
    private static string SerializeDocument<TState>(TState state) where TState : BaseCacheState, new()
    {
        return JsonSerializer.Serialize(state, DocumentJsonOptions);
    }
}
