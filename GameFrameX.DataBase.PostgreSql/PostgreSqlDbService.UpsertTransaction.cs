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


using GameFrameX.DataBase.Abstractions;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Foundation.Logger;
using GameFrameX.Localization;
using Npgsql;

namespace GameFrameX.DataBase.PostgreSql;

public sealed partial class PostgreSqlDbService
{
    /// <summary>
    /// 增加或更新数据（单语句 upsert：INSERT ... ON CONFLICT (id) DO UPDATE，整文档替换，对齐 Mongo ReplaceOne+IsUpsert）。
    /// </summary>
    /// <remarks>
    /// Adds or updates a single document via a one-statement upsert (INSERT ... ON CONFLICT (id) DO UPDATE,
    /// whole-document replacement), aligned with Mongo's ReplaceOne + IsUpsert semantics.
    /// </remarks>
    /// <typeparam name="TState">状态类型 / The state type</typeparam>
    /// <param name="state">要增加或更新的状态对象 / The state object to add or update</param>
    /// <returns>已持久化的状态对象 / The persisted state object</returns>
    public async Task<TState> AddOrUpdateAsync<TState>(TState state) where TState : BaseCacheState, new()
    {
        return await AddOrUpdateAsync(state, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 增加或更新数据（单语句 upsert：INSERT ... ON CONFLICT (id) DO UPDATE，整文档替换，对齐 Mongo ReplaceOne+IsUpsert）。
    /// </summary>
    /// <remarks>
    /// Adds or updates a single document via a one-statement upsert (INSERT ... ON CONFLICT (id) DO UPDATE,
    /// whole-document replacement), aligned with Mongo's ReplaceOne + IsUpsert semantics.
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

        var (sql, parameters) = BuildUpsertSql(state);
        await ExecuteWriteWithRetryAsync(async token => await ExecuteWriteCommandAsync<TState>(sql, parameters, token).ConfigureAwait(false), cancellationToken, nameof(AddOrUpdateAsync), true).ConfigureAwait(false);

        state.SaveToDbPostHandler();
        return state;
    }

    /// <summary>
    /// 批量增加或更新数据（单语句多值 upsert；返回处理记录数，对齐 Mongo BulkWrite ReplaceOne 语义）。
    /// </summary>
    /// <remarks>
    /// Bulk adds or updates documents with a single multi-value upsert statement; returns the number of processed
    /// records, aligned with Mongo's BulkWrite ReplaceOne semantics.
    /// </remarks>
    /// <typeparam name="TState">状态类型 / The state type</typeparam>
    /// <param name="states">要增加或更新的状态集合 / The states to add or update</param>
    /// <returns>处理的记录数 / The number of processed records</returns>
    public async Task<long> AddOrUpdateListAsync<TState>(IEnumerable<TState> states) where TState : BaseCacheState, new()
    {
        return await AddOrUpdateListAsync(states, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 批量增加或更新数据（单语句多值 upsert；返回处理记录数，对齐 Mongo BulkWrite ReplaceOne 语义）。
    /// </summary>
    /// <remarks>
    /// Bulk adds or updates documents with a single multi-value upsert statement; returns the number of processed
    /// records, aligned with Mongo's BulkWrite ReplaceOne semantics.
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
            await using var connection = DataSource.CreateConnection();
            await connection.OpenAsync(token).ConfigureAwait(false);
            await EnsureTableAsync<TState>(connection, token).ConfigureAwait(false);
            await using var command = BuildUpsertManyCommand<TState>(connection, stateArray);
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
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
    /// Bulk-upserts in batches of <paramref name="batchSize"/> via <c>INSERT ... ON CONFLICT (id) DO UPDATE</c>
    /// (C166): per-batch ack and exception isolation — a failed batch is logged and the remaining batches still
    /// execute. Unlike <c>AddOrUpdateListAsync</c> this deliberately does NOT touch
    /// <c>CreatedTime/UpdateTime/UpdateCount</c>: state timestamps are persisted exactly as handed in (the caller
    /// owns timestamp semantics, migrated verbatim from the Mongo adapter C159).
    /// </remarks>
    /// <param name="states">待保存的状态集合 / The states to save</param>
    /// <param name="batchSize">每批数量（&lt;= 0 抛出 ArgumentOutOfRangeException）/ Batch size (values &lt;= 0 throw)</param>
    /// <returns>全部成功 ack 批次的状态列表 / The states of all acknowledged batches</returns>
    /// <exception cref="ArgumentOutOfRangeException">当 <paramref name="batchSize"/> &lt;= 0 时抛出 / Thrown when batchSize is &lt;= 0</exception>
    public async Task<IReadOnlyList<TState>> SaveBulkAsync<TState>(IEnumerable<TState> states, int batchSize) where TState : BaseCacheState, new()
    {
        if (batchSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchSize), batchSize, "Batch size must be greater than zero.");
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
                var acknowledged = false;
                await using (var connection = DataSource.CreateConnection())
                {
                    await connection.OpenAsync(CancellationToken.None).ConfigureAwait(false);
                    await EnsureTableAsync<TState>(connection, CancellationToken.None).ConfigureAwait(false);
                    await using (var command = BuildUpsertManyCommand<TState>(connection, batch))
                    {
                        var affected = await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
                        // ON CONFLICT DO UPDATE 对插入与更新各计一行，受影响行数 >= 批大小即视为整批 ack
                        // ON CONFLICT DO UPDATE counts one row per insert and update; affected >= batch size means the whole batch was acknowledged.
                        acknowledged = affected >= batchCount;
                    }
                }

                if (acknowledged)
                {
                    acknowledgedStates.AddRange(batch);
                }
                else
                {
                    LogHelper.Error("PostgreSqlDbService.SaveBulkAsync batch not acknowledged. StateName: {stateName} , BatchIndex: {batchIndex} , BatchCount: {batchCount}", stateName, index / batchSize + 1, batchCount);
                }
            }
            catch (Exception exception)
            {
                // 逐批异常隔离（C159 迁移语义）：失败批次记日志后继续后续批次，避免单批故障放大为整批丢失
                // Per-batch exception isolation (migrated semantics): a failed batch is logged and the loop continues with the remaining batches.
                LogHelper.Error("PostgreSqlDbService.SaveBulkAsync batch failed. StateName: {stateName} , BatchIndex: {batchIndex} , BatchCount: {batchCount} , Error: {error}", stateName, index / batchSize + 1, batchCount, exception);
            }
        }

        return acknowledgedStates;
    }

    /// <summary>
    /// 在事务中执行操作（独立连接 BEGIN → action → COMMIT；SQLSTATE 40001/40P01 重试分类，对齐 Mongo 事务语义）。
    /// </summary>
    /// <remarks>
    /// Executes the action inside a transaction shell on a dedicated connection
    /// (BEGIN → action → COMMIT), retrying on PostgreSQL serialization failures
    /// (SQLSTATE <c>40001</c> serialization_failure / <c>40P01</c> deadlock_detected — aligned with Mongo's
    /// <c>TransientTransactionError</c> retry classification). As with the Mongo adapter, the action's own
    /// operations use their pooled connections and are not bound to this transaction's snapshot.
    /// </remarks>
    /// <param name="action">要在事务中执行的操作 / The action to execute inside the transaction</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="action"/> 为 null 时抛出 / Thrown when <paramref name="action"/> is null</exception>
    /// <exception cref="DatabaseUnavailableException">当事务重试全部失败后抛出 / Thrown when all transaction retry attempts fail</exception>
    public async Task ExecuteInTransactionAsync(Func<Task> action)
    {
        await ExecuteInTransactionAsync(action, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 在事务中执行操作（独立连接 BEGIN → action → COMMIT；SQLSTATE 40001/40P01 重试分类，对齐 Mongo 事务语义）。
    /// </summary>
    /// <remarks>
    /// Executes the action inside a transaction shell on a dedicated connection
    /// (BEGIN → action → COMMIT), retrying on PostgreSQL serialization failures
    /// (SQLSTATE <c>40001</c> serialization_failure / <c>40P01</c> deadlock_detected — aligned with Mongo's
    /// <c>TransientTransactionError</c> retry classification). As with the Mongo adapter, the action's own
    /// operations use their pooled connections and are not bound to this transaction's snapshot.
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
                await using var connection = DataSource.CreateConnection();
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                await action().ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception exception) when (ShouldRetryTransactionException(exception) && attempt < _transactionRetryDelaysMilliseconds.Length)
            {
                lastException = exception;
                var delay = _transactionRetryDelaysMilliseconds[attempt] + Random.Shared.Next(0, 120);
                LogHelper.Warning("PostgreSqlDbService.ExecuteInTransactionAsync transient error, retry {attempt}/{maxRetry}. error={error}", attempt + 1, _transactionRetryDelaysMilliseconds.Length, exception.Message);
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
    /// 构建单行 upsert 语句。
    /// </summary>
    /// <remarks>
    /// Builds the single-row upsert statement and its parameters.
    /// </remarks>
    private static (string Sql, List<NpgsqlParameter> Parameters) BuildUpsertSql<TState>(TState state) where TState : BaseCacheState, new()
    {
        var sql = $"INSERT INTO {GetTableName<TState>()} (id, doc) VALUES (@id, @doc) ON CONFLICT (id) DO UPDATE SET doc = EXCLUDED.doc";
        var parameters = new List<NpgsqlParameter>
        {
            new("id", state.Id),
            CreateJsonParameter("doc", PostgreSqlJsonDocumentSerializer.Serialize(state)),
        };
        return (sql, parameters);
    }

    /// <summary>
    /// 构建多行 upsert 命令（VALUES (...), (...) + ON CONFLICT DO UPDATE；统一形态：接收连接并在内部赋值，与 <c>BuildInsertManyCommand</c> 一致）。
    /// </summary>
    /// <remarks>
    /// Builds the multi-row upsert command (VALUES (...), (...) + ON CONFLICT DO UPDATE); unified shape:
    /// takes the connection and assigns it internally, matching <c>BuildInsertManyCommand</c>.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="connection">数据库连接 / The database connection</param>
    /// <param name="states">要保存的数据列表 / The states to save</param>
    /// <returns>构建好的 upsert 命令 / The built upsert command</returns>
    private static NpgsqlCommand BuildUpsertManyCommand<TState>(NpgsqlConnection connection, IReadOnlyList<TState> states) where TState : BaseCacheState, new()
    {
        var valueFragments = new List<string>(states.Count);
        var command = new NpgsqlCommand();
        command.Connection = connection;
        for (var index = 0; index < states.Count; index++)
        {
            var state = states[index];
            var idParameterName = $"id{index}";
            var docParameterName = $"doc{index}";
            valueFragments.Add($"(@{idParameterName}, @{docParameterName})");
            command.Parameters.Add(new NpgsqlParameter(idParameterName, state.Id));
            command.Parameters.Add(CreateJsonParameter(docParameterName, PostgreSqlJsonDocumentSerializer.Serialize(state)));
        }

        command.CommandText = $"INSERT INTO {GetTableName<TState>()} (id, doc) VALUES {string.Join(", ", valueFragments)} ON CONFLICT (id) DO UPDATE SET doc = EXCLUDED.doc";
        return command;
    }
}