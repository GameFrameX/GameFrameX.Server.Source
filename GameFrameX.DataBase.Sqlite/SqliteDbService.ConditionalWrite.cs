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

using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.DataBase.Abstractions;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Foundation.Orm.Entity;
using GameFrameX.Localization;
using Microsoft.Data.Sqlite;

namespace GameFrameX.DataBase.Sqlite;

/// <summary>
/// SQLite 条件写原语（CAS 乐观锁整档写、唯一性插入、命名序列发号）。
/// </summary>
/// <remarks>
/// SQLite conditional-write primitives: CAS optimistic-lock whole-document write, unique-filter
/// insert, and named sequence allocation. All paths reuse the shared single-connection gate and
/// the C253 resilience engine; SQL text builders keep the C225/C226 invariant that table names
/// are compile-time-closed, quote-escaped type names (never runtime input).
/// </remarks>
public sealed partial class SqliteDbService
{
    /// <summary>
    /// 命名序列计数表名（全库共享一张键值计数表）。
    /// </summary>
    /// <remarks>
    /// The shared key/value counter table name for named sequences.
    /// </remarks>
    private const string SequenceTableName = "__sequences";

    /// <summary>
    /// 条件写：仅当存量文档的行版本号与期望值一致时整档写入（CAS / 乐观锁原语）。
    /// </summary>
    /// <remarks>
    /// Conditional write: replaces the whole document only when the stored <c>RowVersion</c> equals
    /// <paramref name="expectedRowVersion"/>. Implemented as a single-statement SQLite
    /// <c>UPDATE ... WHERE json_extract(doc, '$.RowVersion') = $expected</c> so the compare and the
    /// swap are atomic under the single-connection gate; the incoming state's version is bumped
    /// before persisting (missing rows and <c>null</c> versions never match). See
    /// <see cref="IDatabaseService.UpdateIfRowVersionAsync{TState}(long, long, TState)"/> for the
    /// full contract.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="id">数据ID / The document ID</param>
    /// <param name="expectedRowVersion">期望的存量行版本号 / The expected stored row version</param>
    /// <param name="state">待写入的新档 / The new state to persist</param>
    /// <returns>写入成功返回 <c>true</c>；版本不符或行缺失返回 <c>false</c> / <c>true</c> when written; <c>false</c> on a version mismatch or a missing row</returns>
    /// <exception cref="System.ArgumentException">当 <paramref name="state"/> 的 Id 与 <paramref name="id"/> 不一致时抛出（写库前检查）/ Thrown when <paramref name="state"/>'s Id differs from <paramref name="id"/> (checked before touching the store)</exception>
    /// <exception cref="System.OverflowException">当 <paramref name="expectedRowVersion"/> 为 <c>long.MaxValue</c>（checked 递增溢出，不写库）/ Thrown when <paramref name="expectedRowVersion"/> is <c>long.MaxValue</c> (checked increment overflows; nothing is written)</exception>
    public Task<bool> UpdateIfRowVersionAsync<TState>(long id, long expectedRowVersion, TState state) where TState : BaseCacheState, new()
    {
        return UpdateIfRowVersionAsync(id, expectedRowVersion, state, CancellationToken.None);
    }

    /// <summary>
    /// 条件写：仅当存量文档的行版本号与期望值一致时整档写入（CAS / 乐观锁原语）。
    /// </summary>
    /// <remarks>
    /// Conditional write: replaces the whole document only when the stored <c>RowVersion</c> equals
    /// <paramref name="expectedRowVersion"/>. Implemented as a single-statement SQLite
    /// <c>UPDATE ... WHERE json_extract(doc, '$.RowVersion') = $expected</c> so the compare and the
    /// swap are atomic under the single-connection gate; the incoming state's version is bumped
    /// before persisting (missing rows and <c>null</c> versions never match). See
    /// <see cref="IDatabaseService.UpdateIfRowVersionAsync{TState}(long, long, TState)"/> for the
    /// full contract.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="id">数据ID / The document ID</param>
    /// <param name="expectedRowVersion">期望的存量行版本号 / The expected stored row version</param>
    /// <param name="state">待写入的新档 / The new state to persist</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>写入成功返回 <c>true</c>；版本不符或行缺失返回 <c>false</c> / <c>true</c> when written; <c>false</c> on a version mismatch or a missing row</returns>
    /// <exception cref="System.ArgumentException">当 <paramref name="state"/> 的 Id 与 <paramref name="id"/> 不一致时抛出（写库前检查）/ Thrown when <paramref name="state"/>'s Id differs from <paramref name="id"/> (checked before touching the store)</exception>
    /// <exception cref="System.OverflowException">当 <paramref name="expectedRowVersion"/> 为 <c>long.MaxValue</c>（checked 递增溢出，不写库）/ Thrown when <paramref name="expectedRowVersion"/> is <c>long.MaxValue</c> (checked increment overflows; nothing is written)</exception>
    public async Task<bool> UpdateIfRowVersionAsync<TState>(long id, long expectedRowVersion, TState state, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        ArgumentNullException.ThrowIfNull(state);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureOpen();
        if (state.Id != id)
        {
            throw new ArgumentException(LocalizationService.GetString(Localization.Keys.Database.ConditionalWriteStateIdMismatch, state.Id, id), nameof(state));
        }

        // 版本准备：先对齐到期望版本，再吃 checked 递增语义；失败（false）路径回滚内存版本——写前递增若不回滚
        // 会污染调用方 state.RowVersion，后续 CAS 持续 Stale；异常路径不回滚（写结果未知，回滚可能反向脱节）。
        // Version preparation: align to the expected version first, then take the checked increment. The
        // pre-write increment is rolled back on the false path — leaving it in place would pollute the caller's
        // state.RowVersion and starve every later CAS; it is NOT rolled back on exceptions (the write outcome
        // is unknown, and rolling back could desync the other way).
        var originalRowVersion = state.RowVersion;
        state.RowVersion = expectedRowVersion;
        VersionedEntityHelper.IncrementRowVersion(state);
        // 写库前捕获字节快照（行版本递增后），成功后按该快照精确提交基线（C263）
        var persistedData = state.ToBytes();
        var doc = SerializeDocument(state);
        var updated = await ExecuteWriteWithRetryAsync(async _ => await ExecuteGatedAsync(async () =>
        {
            await EnsureTableAsync<TState>(cancellationToken).ConfigureAwait(false);
            using var command = _connection.CreateCommand();
            command.CommandText = BuildCasUpdateCommandText<TState>();
            command.Parameters.AddWithValue("$doc", doc);
            command.Parameters.AddWithValue("$id", id);
            command.Parameters.AddWithValue("$expected", expectedRowVersion);
            return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
        }), cancellationToken, nameof(UpdateIfRowVersionAsync), false).ConfigureAwait(false);
        if (updated)
        {
            state.SaveToDbPostHandler(persistedData);
            return updated;
        }

        // 版本不符或行缺失：未写库，回滚写前递增的内存版本。
        // Version mismatch or missing row: nothing was written — roll back the pre-write in-memory increment.
        state.RowVersion = originalRowVersion;
        return false;
    }

    /// <summary>
    /// 唯一性插入：仅当无既有文档满足唯一性条件时插入（冲突返回 false 而非异常）。
    /// </summary>
    /// <remarks>
    /// Unique insert: reads the full table (soft-deleted documents included — uniqueness ignores
    /// <c>IsDeleted</c>) inside a <c>BEGIN</c> transaction, rolls back and returns <c>false</c> when
    /// any stored document matches <paramref name="uniqueFilter"/> or already occupies
    /// <paramref name="state"/>'s Id, and otherwise inserts and commits. A SQLite unique-constraint
    /// violation (error codes 19 / 2067) is translated to <c>false</c> instead of propagating. The
    /// whole "gate + transaction + check + insert" core runs through
    /// <c>ExecuteWriteWithRetryAsync</c> with <c>isIdempotent: false</c> — inserts are not blindly
    /// replayed, and if the engine does retry after a transient failure the attempt restarts the
    /// complete transaction, whose pre-check then observes the committed row and returns <c>false</c>,
    /// so the semantics stay safe either way. See
    /// <see cref="IDatabaseService.TryAddUniqueAsync{TState}(TState, Expression{Func{TState, bool}})"/>
    /// for the full contract.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="state">待插入的新档 / The new state to insert</param>
    /// <param name="uniqueFilter">唯一性判定表达式 / The uniqueness predicate</param>
    /// <returns>插入成功返回 <c>true</c>；唯一键冲突或 Id 已占用返回 <c>false</c> / <c>true</c> when inserted; <c>false</c> on a unique-key or Id conflict</returns>
    public Task<bool> TryAddUniqueAsync<TState>(TState state, Expression<Func<TState, bool>> uniqueFilter) where TState : BaseCacheState, new()
    {
        return TryAddUniqueAsync(state, uniqueFilter, CancellationToken.None);
    }

    /// <summary>
    /// 唯一性插入：仅当无既有文档满足唯一性条件时插入（冲突返回 false 而非异常）。
    /// </summary>
    /// <remarks>
    /// Unique insert: reads the full table (soft-deleted documents included — uniqueness ignores
    /// <c>IsDeleted</c>) inside a <c>BEGIN</c> transaction, rolls back and returns <c>false</c> when
    /// any stored document matches <paramref name="uniqueFilter"/> or already occupies
    /// <paramref name="state"/>'s Id, and otherwise inserts and commits. A SQLite unique-constraint
    /// violation (error codes 19 / 2067) is translated to <c>false</c> instead of propagating. The
    /// whole "gate + transaction + check + insert" core runs through
    /// <c>ExecuteWriteWithRetryAsync</c> with <c>isIdempotent: false</c> — inserts are not blindly
    /// replayed, and if the engine does retry after a transient failure the attempt restarts the
    /// complete transaction, whose pre-check then observes the committed row and returns <c>false</c>,
    /// so the semantics stay safe either way. See
    /// <see cref="IDatabaseService.TryAddUniqueAsync{TState}(TState, Expression{Func{TState, bool}})"/>
    /// for the full contract.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="state">待插入的新档 / The new state to insert</param>
    /// <param name="uniqueFilter">唯一性判定表达式 / The uniqueness predicate</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>插入成功返回 <c>true</c>；唯一键冲突或 Id 已占用返回 <c>false</c> / <c>true</c> when inserted; <c>false</c> on a unique-key or Id conflict</returns>
    public async Task<bool> TryAddUniqueAsync<TState>(TState state, Expression<Func<TState, bool>> uniqueFilter, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(uniqueFilter);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureOpen();
        var currentTime = GetCurrentTimestamp();
        state.CreatedTime = currentTime;
        state.UpdateTime = currentTime;
        state.UpdateCount ??= 0;
        var predicate = uniqueFilter.Compile();
        return await ExecuteWriteWithRetryAsync(async _ => await ExecuteGatedAsync(() => ExecuteTryAddUniqueCoreAsync<TState>(state, predicate, cancellationToken)), cancellationToken, nameof(TryAddUniqueAsync), false).ConfigureAwait(false);
    }

    /// <summary>
    /// TryAddUniqueAsync 事务核心（读全表冲突扫描 → 命中回滚返 false → 插入提交；唯一约束冲突 19/2067 转译 false）。
    /// </summary>
    /// <remarks>
    /// The gated transaction core of TryAddUniqueAsync: scans the full table (soft-deleted documents
    /// included) for an Id occupant or uniqueness-predicate match and rolls back to <c>false</c> on a
    /// hit; otherwise inserts and commits. A SQLite unique-constraint violation (error codes 19 / 2067)
    /// is translated to <c>false</c> instead of propagating. The caller must hold <see cref="_gate"/>
    /// (via <c>ExecuteGatedAsync</c>) and run under <c>ExecuteWriteWithRetryAsync</c> with
    /// <c>isIdempotent: false</c> — the pre-check-then-insert core restarts as a whole on retry.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="state">待插入的新档 / The new state to insert</param>
    /// <param name="predicate">已编译的唯一性判定 / The compiled uniqueness predicate</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>插入成功返回 <c>true</c>；唯一键冲突或 Id 已占用返回 <c>false</c> / <c>true</c> when inserted; <c>false</c> on a unique-key or Id conflict</returns>
    private async Task<bool> ExecuteTryAddUniqueCoreAsync<TState>(TState state, Func<TState, bool> predicate, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        await EnsureTableAsync<TState>(cancellationToken).ConfigureAwait(false);
        var (transaction, owned) = await BeginOrJoinTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var rows = await ReadRowsRawAsync<TState>(BuildSelectAllCommand<TState>(), cancellationToken).ConfigureAwait(false);
            foreach (var existing in rows)
            {
                if (existing.Id == state.Id || predicate(existing))
                {
                    if (owned)
                    {
                        await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                    }

                    return false;
                }
            }

            using var insertCommand = BuildInsertCommand<TState>(state);
            insertCommand.Transaction = transaction;
            await insertCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (owned)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            }

            return true;
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 19 or 2067)
        {
            // 唯一约束冲突：自持事务 Dispose 即回滚；环境事务保持（语句级失败不中止事务），转译为 false 而非上抛。
            // Unique-constraint conflict: an owned transaction rolls back on dispose; an ambient one stays usable (statement failure does not abort it) — translate to false.
            return false;
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
    /// 获取指定状态类型命名序列的下一个值（原子单调递增）。
    /// </summary>
    /// <remarks>
    /// Allocates the next value of a named sequence. The counter lives in the shared
    /// <c>__sequences</c> table and is advanced by a single-statement
    /// <c>INSERT ... ON CONFLICT DO UPDATE SET value = value + 1 RETURNING value</c>, so the
    /// read-modify-write is atomic and monotonically increasing (first value is 1; retries may
    /// leave holes but never repeat or regress a value). See
    /// <see cref="IDatabaseService.NextSequenceAsync{TState}(string)"/> for the full contract.
    /// </remarks>
    /// <typeparam name="TState">序列归属的缓存状态类型 / The owning state type</typeparam>
    /// <param name="sequenceName">序列名 / The sequence name</param>
    /// <returns>序列新值（首次为 1）/ The newly allocated value (starting at 1)</returns>
    /// <exception cref="System.ArgumentException">当 <paramref name="sequenceName"/> 为 null 或空白时抛出 / Thrown when <paramref name="sequenceName"/> is null or whitespace</exception>
    public Task<long> NextSequenceAsync<TState>(string sequenceName) where TState : BaseCacheState, new()
    {
        return NextSequenceAsync<TState>(sequenceName, CancellationToken.None);
    }

    /// <summary>
    /// 获取指定状态类型命名序列的下一个值（原子单调递增）。
    /// </summary>
    /// <remarks>
    /// Allocates the next value of a named sequence. The counter lives in the shared
    /// <c>__sequences</c> table and is advanced by a single-statement
    /// <c>INSERT ... ON CONFLICT DO UPDATE SET value = value + 1 RETURNING value</c>, so the
    /// read-modify-write is atomic and monotonically increasing (first value is 1; retries may
    /// leave holes but never repeat or regress a value). See
    /// <see cref="IDatabaseService.NextSequenceAsync{TState}(string)"/> for the full contract.
    /// </remarks>
    /// <typeparam name="TState">序列归属的缓存状态类型 / The owning state type</typeparam>
    /// <param name="sequenceName">序列名 / The sequence name</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>序列新值（首次为 1）/ The newly allocated value (starting at 1)</returns>
    /// <exception cref="System.ArgumentException">当 <paramref name="sequenceName"/> 为 null 或空白时抛出 / Thrown when <paramref name="sequenceName"/> is null or whitespace</exception>
    public async Task<long> NextSequenceAsync<TState>(string sequenceName, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureOpen();
        if (string.IsNullOrWhiteSpace(sequenceName))
        {
            throw new ArgumentException(LocalizationService.GetString(Localization.Keys.Database.ConditionalWriteSequenceNameRequired), nameof(sequenceName));
        }

        var key = typeof(TState).Name + ":" + sequenceName;
        return await ExecuteWriteWithRetryAsync(async _ => await ExecuteGatedAsync(async () =>
        {
            await EnsureSequenceTableAsync(cancellationToken).ConfigureAwait(false);
            using var command = _connection.CreateCommand();
            command.CommandText = BuildNextSequenceCommandText();
            command.Parameters.AddWithValue("$key", key);
            var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return (long)result;
        }), cancellationToken, nameof(NextSequenceAsync), false).ConfigureAwait(false);
    }

    /// <summary>
    /// 构建 CAS 更新 SQL 文本（表名 = 编译期封闭类型名的双引号转义，非运行期输入）。
    /// </summary>
    /// <remarks>
    /// Builds the CAS update SQL text. The table name is the quote-escaped <c>typeof(TState).Name</c> —
    /// a compile-time-closed identifier, never runtime input; SQL identifiers cannot be bound
    /// parameters, so the composition is centralized in this dedicated builder with that invariant
    /// stated (Sonar S2077). The compare-and-swap happens entirely inside the single statement:
    /// rows whose stored <c>RowVersion</c> JSON number differs (or is missing/null) are skipped.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <returns>SQL 文本 / The SQL text</returns>
    private static string BuildCasUpdateCommandText<TState>() where TState : BaseCacheState, new()
    {
        return "UPDATE " + QuoteTableName<TState>() + " SET doc = $doc WHERE id = $id AND json_extract(doc, '$.RowVersion') = $expected;";
    }

    /// <summary>
    /// 构建序列发号 SQL 文本（固定表名 <c>__sequences</c>，非运行期输入）。
    /// </summary>
    /// <remarks>
    /// Builds the sequence allocation SQL text. The table name is the fixed <c>__sequences</c>
    /// constant — never runtime input; SQL identifiers cannot be bound parameters, so the
    /// composition is centralized in this dedicated builder with that invariant stated (Sonar S2077).
    /// The single statement atomically inserts the first value (1) or increments and returns the
    /// new value.
    /// </remarks>
    /// <returns>SQL 文本 / The SQL text</returns>
    private static string BuildNextSequenceCommandText()
    {
        return "INSERT INTO \"__sequences\" (\"key\", \"value\") VALUES ($key, 1) ON CONFLICT(\"key\") DO UPDATE SET \"value\" = \"__sequences\".\"value\" + 1 RETURNING \"value\";";
    }

    /// <summary>
    /// 确保序列计数表存在（调用方须持有 <see cref="_gate"/>；走 <see cref="_tableReady"/> 单飞）。
    /// </summary>
    /// <remarks>
    /// Ensures the shared sequence counter table exists (the caller must hold <see cref="_gate"/>;
    /// creation is single-flighted through <see cref="_tableReady"/>, mirroring
    /// <c>EnsureTableAsync{TState}</c> which cannot be reused for the non-state table).
    /// </remarks>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>异步任务 / Async task</returns>
    private Task EnsureSequenceTableAsync(CancellationToken cancellationToken)
    {
        var lazy = _tableReady.GetOrAdd(SequenceTableName, _ => new Lazy<Task>(CreateSequenceTableAsync, System.Threading.LazyThreadSafetyMode.ExecutionAndPublication));
        if (lazy.Value.IsFaulted)
        {
            // 失败任务驱逐：下次调用重建单飞（对齐 EnsureTableAsync 的故障重试语义）。
            // Evict the faulted task so the next call retries (aligned with EnsureTableAsync).
            _tableReady.TryRemove(new KeyValuePair<string, Lazy<Task>>(SequenceTableName, lazy));
            lazy = _tableReady.GetOrAdd(SequenceTableName, _ => new Lazy<Task>(CreateSequenceTableAsync, System.Threading.LazyThreadSafetyMode.ExecutionAndPublication));
        }

        return lazy.Value;
    }

    /// <summary>
    /// 序列计数表 DDL 执行（调用方须持有 <see cref="_gate"/>；CREATE TABLE IF NOT EXISTS 幂等）。
    /// </summary>
    /// <remarks>
    /// Runs the sequence table CREATE TABLE IF NOT EXISTS DDL (the caller must hold <see cref="_gate"/>).
    /// </remarks>
    /// <returns>异步任务 / Async task</returns>
    private async Task CreateSequenceTableAsync()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS \"__sequences\" (\"key\" TEXT PRIMARY KEY, \"value\" INTEGER NOT NULL);";
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
}
