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
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.DataBase.Abstractions;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Foundation.Orm.Attribute;
using GameFrameX.Localization;
using Microsoft.Data.Sqlite;

namespace GameFrameX.DataBase.Sqlite;

/// <summary>
/// SQLite 条件写原语（CAS 乐观锁整档写、唯一性插入、命名序列发号）。
/// </summary>
/// <remarks>
/// SQLite conditional-write primitives: CAS optimistic-lock whole-document write, unique-filter
/// insert, and named sequence allocation. All paths reuse the shared single-connection gate and
/// the shared resilience engine; SQL text builders keep the invariant that table names
/// are compile-time-closed, quote-escaped type names (never runtime input). The unique insert
/// prefers the engine path — a single <c>INSERT … ON CONFLICT DO NOTHING</c> statement backed by the
/// declared unique expression index (see <c>SqliteDbService.Write.cs</c>) — and falls back to the
/// legacy table scan when the predicate cannot be reduced to a declared unique member.
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

        return await RowVersionCasWriteHelper.WriteAsync(id, expectedRowVersion, state, async () =>
        {
            var doc = SerializeDocument(state);
            var updated = await ExecuteWriteWithRetryAsync(async _ => await ExecuteGatedAsync(async () =>
            {
                await EnsureTableAsync<TState>(cancellationToken).ConfigureAwait(false);
                using (var command = _connection.CreateCommand())
                {
                    command.CommandText = BuildCasUpdateCommandText<TState>();
                    command.Parameters.AddWithValue("$doc", doc);
                    command.Parameters.AddWithValue("$id", id);
                    command.Parameters.AddWithValue("$expected", expectedRowVersion);
                    return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
                }
            }), cancellationToken, nameof(UpdateIfRowVersionAsync), false).ConfigureAwait(false);
            return updated;
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// 条件写：仅当存量文档缺失行版本号（字段缺位或值为 null）时整档写入（存量迁移回填原语）。
    /// </summary>
    /// <remarks>
    /// Conditional write: replaces the whole document only when the stored <c>RowVersion</c> is
    /// missing or null. Implemented as a single-statement SQLite
    /// <c>UPDATE ... WHERE json_extract(doc, '$.RowVersion') IS NULL</c> — <c>json_extract</c> yields
    /// SQL NULL both for an absent path and for a stored JSON null, so the compare and the swap are
    /// atomic under the single-connection gate; the incoming state's version is bumped from the
    /// null backfill baseline -1 to 0 before persisting (rows with a non-null version never match). See
    /// <see cref="IDatabaseService.UpdateIfRowVersionIsNullAsync{TState}(long, TState)"/> for the
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="id">数据ID / The document ID</param>
    /// <param name="state">待写入的新档 / The new state to persist</param>
    /// <returns>存量缺失行版本且写入成功返回 <c>true</c>；存量 <c>RowVersion</c> 非 null 或行缺失返回 <c>false</c> / <c>true</c> when the stored version was missing and the row was written; <c>false</c> on a non-null stored version or a missing row</returns>
    /// <exception cref="System.ArgumentException">当 <paramref name="state"/> 的 Id 与 <paramref name="id"/> 不一致时抛出（写库前检查）/ Thrown when <paramref name="state"/>'s Id differs from <paramref name="id"/> (checked before touching the store)</exception>
    public Task<bool> UpdateIfRowVersionIsNullAsync<TState>(long id, TState state) where TState : BaseCacheState, new()
    {
        return UpdateIfRowVersionIsNullAsync(id, state, CancellationToken.None);
    }

    /// <summary>
    /// 条件写：仅当存量文档缺失行版本号（字段缺位或值为 null）时整档写入（存量迁移回填原语）。
    /// </summary>
    /// <remarks>
    /// Conditional write: replaces the whole document only when the stored <c>RowVersion</c> is
    /// missing or null. Implemented as a single-statement SQLite
    /// <c>UPDATE ... WHERE json_extract(doc, '$.RowVersion') IS NULL</c> so the compare and the swap
    /// are atomic under the single-connection gate; the incoming state's version is bumped from the
    /// null backfill baseline -1 to 0 before persisting. See
    /// <see cref="IDatabaseService.UpdateIfRowVersionIsNullAsync{TState}(long, TState, CancellationToken)"/> for the
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="id">数据ID / The document ID</param>
    /// <param name="state">待写入的新档 / The new state to persist</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>存量缺失行版本且写入成功返回 <c>true</c>；存量 <c>RowVersion</c> 非 null 或行缺失返回 <c>false</c> / <c>true</c> when the stored version was missing and the row was written; <c>false</c> on a non-null stored version or a missing row</returns>
    /// <exception cref="System.ArgumentException">当 <paramref name="state"/> 的 Id 与 <paramref name="id"/> 不一致时抛出（写库前检查）/ Thrown when <paramref name="state"/>'s Id differs from <paramref name="id"/> (checked before touching the store)</exception>
    public async Task<bool> UpdateIfRowVersionIsNullAsync<TState>(long id, TState state, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        ArgumentNullException.ThrowIfNull(state);
        cancellationToken.ThrowIfCancellationRequested();
        EnsureOpen();

        return await RowVersionBackfillWriteHelper.WriteAsync(id, state, async () =>
        {
            var doc = SerializeDocument(state);
            var updated = await ExecuteWriteWithRetryAsync(async _ => await ExecuteGatedAsync(async () =>
            {
                await EnsureTableAsync<TState>(cancellationToken).ConfigureAwait(false);
                using (var command = _connection.CreateCommand())
                {
                    command.CommandText = BuildRowVersionIsNullUpdateCommandText<TState>();
                    command.Parameters.AddWithValue("$doc", doc);
                    command.Parameters.AddWithValue("$id", id);
                    return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
                }
            }), cancellationToken, nameof(UpdateIfRowVersionIsNullAsync), false).ConfigureAwait(false);
            return updated;
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// 唯一性插入：仅当无既有文档满足唯一性条件时插入（冲突返回 false 而非异常）。
    /// </summary>
    /// <remarks>
    /// Unique insert: two paths, one contract. Fast path — when the uniqueness predicate reduces to
    /// <c>x.Member == value</c> over a property carrying a declared unique index
    /// (<c>[EntityIndex(Unique = true)]</c>, materialized as a <c>json_extract</c> expression unique index
    /// when the table is created), the insert runs as a single <c>INSERT … ON CONFLICT DO NOTHING</c>
    /// statement: no table scan is executed, the statement is atomic by itself (so the uniqueness check
    /// holds across processes) and a uniqueness or Id conflict surfaces as zero affected rows translated
    /// to <c>false</c> instead of an exception. Fallback path — every other predicate shape (composite
    /// predicates, undeclared members, a predicate comparing against another value, a null member value)
    /// keeps the previous behavior: the full table is read inside a <c>BEGIN</c> transaction
    /// (soft-deleted documents included — uniqueness ignores <c>IsDeleted</c>), rolled back and returning
    /// <c>false</c> when any stored document matches <paramref name="uniqueFilter"/> or already occupies
    /// <paramref name="state"/>'s Id, and otherwise inserted and committed; a SQLite unique-constraint
    /// violation (error codes 19 / 2067) is translated to <c>false</c> instead of propagating. Both paths
    /// run through <c>ExecuteWriteWithRetryAsync</c> with <c>isIdempotent: false</c> — inserts are not
    /// blindly replayed, and if the engine does retry after a transient failure the fallback attempt
    /// restarts the complete transaction, whose pre-check then observes the committed row and returns
    /// <c>false</c>, so the semantics stay safe either way. See
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
    /// Unique insert: two paths, one contract. Fast path — when the uniqueness predicate reduces to
    /// <c>x.Member == value</c> over a property carrying a declared unique index
    /// (<c>[EntityIndex(Unique = true)]</c>, materialized as a <c>json_extract</c> expression unique index
    /// when the table is created), the insert runs as a single <c>INSERT … ON CONFLICT DO NOTHING</c>
    /// statement: no table scan is executed, the statement is atomic by itself (so the uniqueness check
    /// holds across processes) and a uniqueness or Id conflict surfaces as zero affected rows translated
    /// to <c>false</c> instead of an exception. Fallback path — every other predicate shape (composite
    /// predicates, undeclared members, a predicate comparing against another value, a null member value)
    /// keeps the previous behavior: the full table is read inside a <c>BEGIN</c> transaction
    /// (soft-deleted documents included — uniqueness ignores <c>IsDeleted</c>), rolled back and returning
    /// <c>false</c> when any stored document matches <paramref name="uniqueFilter"/> or already occupies
    /// <paramref name="state"/>'s Id, and otherwise inserted and committed; a SQLite unique-constraint
    /// violation (error codes 19 / 2067) is translated to <c>false</c> instead of propagating. Both paths
    /// run through <c>ExecuteWriteWithRetryAsync</c> with <c>isIdempotent: false</c> — inserts are not
    /// blindly replayed, and if the engine does retry after a transient failure the fallback attempt
    /// restarts the complete transaction, whose pre-check then observes the committed row and returns
    /// <c>false</c>, so the semantics stay safe either way. See
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
        // 快路径判定（谓词归约 + 声明式唯一索引 + 自身值等价守卫）：命中走单语句插入，未命中回落全表扫描。
        // Fast-path decision (predicate reduction + declared unique index + self-value equivalence guard):
        // a hit takes the single-statement insert; a miss falls back to the full-table scan.
        var fastPathProperty = ResolveUniqueInsertFastPathProperty(uniqueFilter, state, predicate);
        return await ExecuteWriteWithRetryAsync(async _ => await ExecuteGatedAsync(() => fastPathProperty != null
            ? ExecuteTryAddUniqueIndexInsertAsync<TState>(state, cancellationToken)
            : ExecuteTryAddUniqueScanCoreAsync<TState>(state, predicate, cancellationToken)), cancellationToken, nameof(TryAddUniqueAsync), false).ConfigureAwait(false);
    }

    /// <summary>
    /// TryAddUniqueAsync 扫描回落核心（读全表冲突扫描 → 命中回滚返 false → 插入提交；唯一约束冲突 19/2067 转译 false）。
    /// </summary>
    /// <remarks>
    /// The gated transaction fallback core of TryAddUniqueAsync: scans the full table (soft-deleted documents
    /// included) for an Id occupant or uniqueness-predicate match and rolls back to <c>false</c> on a
    /// hit; otherwise inserts and commits. A SQLite unique-constraint violation (error codes 19 / 2067)
    /// is translated to <c>false</c> instead of propagating. The caller must hold <see cref="_gate"/>
    /// (via <c>ExecuteGatedAsync</c>) and run under <c>ExecuteWriteWithRetryAsync</c> with
    /// <c>isIdempotent: false</c> — the pre-check-then-insert core restarts as a whole on retry.
    /// This path is taken when the uniqueness predicate cannot be reduced to a declared unique member
    /// (see <see cref="ResolveUniqueInsertFastPathProperty{TState}"/>).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="state">待插入的新档 / The new state to insert</param>
    /// <param name="predicate">已编译的唯一性判定 / The compiled uniqueness predicate</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>插入成功返回 <c>true</c>；唯一键冲突或 Id 已占用返回 <c>false</c> / <c>true</c> when inserted; <c>false</c> on a unique-key or Id conflict</returns>
    private async Task<bool> ExecuteTryAddUniqueScanCoreAsync<TState>(TState state, Func<TState, bool> predicate, CancellationToken cancellationToken) where TState : BaseCacheState, new()
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

            using (var insertCommand = BuildInsertCommand<TState>(state))
            {
                insertCommand.Transaction = transaction;
                await insertCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                if (owned)
                {
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                }

                return true;
            }
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
    /// TryAddUniqueAsync 引擎快路径核心（单语句 <c>INSERT … ON CONFLICT DO NOTHING</c>，零全表读，语句级原子）。
    /// </summary>
    /// <remarks>
    /// The gated fast-path core of TryAddUniqueAsync: a single <c>INSERT … ON CONFLICT DO NOTHING</c>
    /// statement whose uniqueness is enforced by the declared unique expression index (bootstrapped by
    /// <c>CreateTableAsync</c>). No table scan is executed, the statement is atomic by itself (SQLite
    /// autocommit semantics) and therefore safe across processes, and a uniqueness or primary-key
    /// conflict surfaces as zero affected rows (translated to <c>false</c>) instead of an exception.
    /// Inside an ambient transaction the statement joins it (the transaction shell owns the commit).
    /// The caller must hold <see cref="_gate"/> and run under <c>ExecuteWriteWithRetryAsync</c>.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="state">待插入的新档 / The new state to insert</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>插入成功返回 <c>true</c>；唯一键冲突或 Id 已占用返回 <c>false</c> / <c>true</c> when inserted; <c>false</c> on a unique-key or Id conflict</returns>
    private async Task<bool> ExecuteTryAddUniqueIndexInsertAsync<TState>(TState state, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        await EnsureTableAsync<TState>(cancellationToken).ConfigureAwait(false);
        using (var insertCommand = BuildInsertOnConflictDoNothingCommand<TState>(state))
        {
            // 环境事务存在时加入之（提交权归事务壳）；无环境事务时语句自提交（单语句即原子单元）。
            // Join the ambient transaction when present (the shell owns the commit); without one the
            // statement commits on its own (a single statement is its own atomic unit).
            insertCommand.Transaction = AmbientTransaction.Value?.Transaction;
            return await insertCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
        }
    }

    /// <summary>
    /// 构建唯一插入快路径命令（<c>ON CONFLICT DO NOTHING</c>；id/doc 均经绑定参数传递）。
    /// </summary>
    /// <remarks>
    /// Builds the unique-insert fast-path command. The table name is the quote-escaped
    /// <c>typeof(TState).Name</c> — a compile-time-closed identifier, never runtime input (Sonar S2077);
    /// the id and doc values are bound as parameters.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="state">待插入的状态 / The state to insert</param>
    /// <returns>插入命令 / The insert command</returns>
    private SqliteCommand BuildInsertOnConflictDoNothingCommand<TState>(TState state) where TState : BaseCacheState, new()
    {
        var command = _connection.CreateCommand();
        command.CommandText = BuildInsertOnConflictDoNothingCommandText<TState>();
        command.Parameters.AddWithValue("$id", state.Id);
        command.Parameters.AddWithValue("$doc", SerializeDocument(state));
        return command;
    }

    /// <summary>
    /// 获取指定状态类型命名序列的下一个值（原子单调递增）。
    /// </summary>
    /// <remarks>
    /// Allocates the next value of a named sequence. The counter lives in the shared
    /// <c>__sequences</c> table and is advanced by a single-statement
    /// <c>INSERT ... ON CONFLICT DO UPDATE SET value = value + 1 RETURNING value</c>, so the
    /// read-modify-write is atomic and monotonically increasing (first value is 1; retries may
    /// leave holes but never repeat or regress a value). Transient busy/locked/timeout failures are
    /// retried through the shared resilience engine with <c>isIdempotent: true</c> — replaying the
    /// single atomic statement can at most leave a hole, never a duplicate. See
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
    /// leave holes but never repeat or regress a value). Transient busy/locked/timeout failures are
    /// retried through the shared resilience engine with <c>isIdempotent: true</c> — replaying the
    /// single atomic statement can at most leave a hole, never a duplicate. See
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
            using (var command = _connection.CreateCommand())
            {
                command.CommandText = BuildNextSequenceCommandText();
                command.Parameters.AddWithValue("$key", key);
                var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                return (long)result;
            }
        }), cancellationToken, nameof(NextSequenceAsync), true).ConfigureAwait(false);
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
    /// 构建行版本缺失回填更新 SQL 文本（表名 = 编译期封闭类型名的双引号转义，非运行期输入）。
    /// </summary>
    /// <remarks>
    /// Builds the row-version-null backfill update SQL text. The table name is the quote-escaped
    /// <c>typeof(TState).Name</c> — a compile-time-closed identifier, never runtime input; SQL
    /// identifiers cannot be bound parameters, so the composition is centralized in this dedicated
    /// builder with that invariant stated (Sonar S2077). <c>json_extract</c> yields SQL NULL both for
    /// an absent <c>RowVersion</c> path and for a stored JSON null, so <c>IS NULL</c> matches exactly
    /// the missing-version legacy rows; the compare-and-swap happens entirely inside the single
    /// statement (rows with a non-null version never match).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <returns>SQL 文本 / The SQL text</returns>
    private static string BuildRowVersionIsNullUpdateCommandText<TState>() where TState : BaseCacheState, new()
    {
        return "UPDATE " + QuoteTableName<TState>() + " SET doc = $doc WHERE id = $id AND json_extract(doc, '$.RowVersion') IS NULL;";
    }

    /// <summary>
    /// 构建唯一插入快路径 SQL 文本（<c>ON CONFLICT DO NOTHING</c>；表名 = 编译期封闭类型名的双引号转义，非运行期输入）。
    /// </summary>
    /// <remarks>
    /// Builds the unique-insert fast-path SQL text. The table name is the quote-escaped
    /// <c>typeof(TState).Name</c> — a compile-time-closed identifier, never runtime input; SQL identifiers
    /// cannot be bound parameters, so the composition is centralized in this dedicated builder with that
    /// invariant stated (Sonar S2077). The <c>ON CONFLICT DO NOTHING</c> clause (without a conflict target)
    /// swallows every uniqueness violation — the declared unique expression index and the primary key — and
    /// reports it as zero affected rows instead of an exception.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <returns>SQL 文本 / The SQL text</returns>
    private static string BuildInsertOnConflictDoNothingCommandText<TState>() where TState : BaseCacheState, new()
    {
        return "INSERT INTO " + QuoteTableName<TState>() + " (id, doc) VALUES ($id, $doc) ON CONFLICT DO NOTHING;";
    }

    /// <summary>
    /// 判定唯一性谓词能否走引擎唯一索引快路径：归约为「参数成员 == 参数无关常量」+ 成员已声明唯一索引 + 自身值等价守卫。
    /// </summary>
    /// <remarks>
    /// Decides whether the uniqueness predicate can take the engine-index fast path. The predicate must
    /// reduce to <c>x.Member == value</c> (either orientation) where <c>value</c> does not depend on the
    /// predicate parameter, <c>Member</c> must be a public instance property carrying a declared unique
    /// index (<c>[EntityIndex(Unique = true)]</c> with an indexable type), and the equivalence guard must
    /// hold: the predicate must be true for <paramref name="state"/> itself (so <c>state.Member == value</c>)
    /// and <c>state.Member</c> must be non-null (a NULL key never matches the index). Under those
    /// conditions, "the index rejects the insert because an existing row carries the same
    /// <c>state.Member</c> value" is semantically identical to "the predicate matches an existing row",
    /// so the engine check replaces the table scan. Anything else (composite predicates, nested members,
    /// two member operands, an undeclared member, a predicate comparing against another value, a null
    /// member value, a custom <c>operator ==</c>) returns <c>null</c> and the caller falls back to the
    /// scan path.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="uniqueFilter">唯一性判定表达式 / The uniqueness predicate expression</param>
    /// <param name="state">待插入的新档 / The new state to insert</param>
    /// <param name="predicate">已编译的唯一性判定 / The compiled uniqueness predicate</param>
    /// <returns>可走快路径时返回承载唯一索引的属性；否则返回 <c>null</c> / The unique-indexed property when the fast path applies; otherwise <c>null</c></returns>
    internal static PropertyInfo ResolveUniqueInsertFastPathProperty<TState>(Expression<Func<TState, bool>> uniqueFilter, TState state, Func<TState, bool> predicate) where TState : BaseCacheState, new()
    {
        var property = TryResolveUniquePredicateMemberProperty(uniqueFilter);
        if (property == null)
        {
            return null;
        }

        if (!HasDeclaredUniqueIndex<TState>(property))
        {
            // 该属性未声明唯一索引（或声明被跳过：类型不可索引 / JsonIgnore / 名称不可转义）→ 不走快路径。
            // The property carries no usable unique index declaration — no fast path.
            return null;
        }

        // 等价守卫：谓词须对 state 自身为真（state.Member == value）且自身值非 null。
        // Equivalence guard: the predicate must hold for the state itself (state.Member == value)
        // and the member value must be non-null.
        if (property.GetValue(state) == null)
        {
            return null;
        }

        try
        {
            if (!predicate(state))
            {
                return null;
            }
        }
        catch
        {
            // 谓词对自身求值抛错（扫描路径从不对新档求值）→ 保守回落，不改变既有行为。
            // The predicate throws for the state itself (the scan path never evaluates it against the new
            // state) — fall back conservatively and keep the previous behavior.
            return null;
        }

        return property;
    }

    /// <summary>
    /// 把唯一性谓词归约为「谓词参数直接成员 == 参数无关值」形态并返回该成员属性（公共实例属性才可承载唯一索引）。
    /// </summary>
    /// <remarks>
    /// Reduces the uniqueness predicate to the <c>parameter.Member == value</c> shape (either orientation)
    /// and returns that member property. <c>value</c> must not depend on the predicate parameter (see
    /// <see cref="IsParameterIndependentValue(Expression)"/>); a custom <c>operator ==</c> cannot be reasoned
    /// about against the json_extract index domain (only built-in and string equality are accepted); the
    /// member must be a public instance property directly rooted at the predicate parameter with a public
    /// getter. Anything else (composite predicates, nested members, two member operands, a field, a static
    /// or non-public property) returns <c>null</c> and the caller falls back to the scan path.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="uniqueFilter">唯一性判定表达式 / The uniqueness predicate expression</param>
    /// <returns>归约成立时返回成员属性；否则返回 <c>null</c> / The member property when the reduction holds; otherwise <c>null</c></returns>
    private static PropertyInfo TryResolveUniquePredicateMemberProperty<TState>(Expression<Func<TState, bool>> uniqueFilter) where TState : BaseCacheState, new()
    {
        var body = uniqueFilter.Body as BinaryExpression;
        if (body == null || body.NodeType != ExpressionType.Equal)
        {
            return null;
        }

        // 自定义 operator== 的等值语义无法与 json_extract 索引口径互推（仅接受内建等值与字符串等值）。
        // A custom operator== cannot be reasoned about against the json_extract index domain
        // (only built-in and string equality are accepted).
        if (body.Method != null && body.Method.DeclaringType != typeof(string))
        {
            return null;
        }

        var left = StripConvertNodes(body.Left);
        var right = StripConvertNodes(body.Right);
        var memberExpression = left as MemberExpression ?? right as MemberExpression;
        if (memberExpression == null || memberExpression.Expression != uniqueFilter.Parameters[0])
        {
            // 成员侧不是直接挂在谓词参数上的属性（嵌套成员 / 两侧皆非常量成员）→ 不走快路径。
            // The member side is not a property directly rooted at the predicate parameter
            // (nested member / neither side a member) — no fast path.
            return null;
        }

        var valueExpression = ReferenceEquals(memberExpression, left) ? right : left;
        if (!IsParameterIndependentValue(valueExpression))
        {
            return null;
        }

        if (memberExpression.Member is not PropertyInfo property || property.GetMethod == null || !property.GetMethod.IsPublic || property.GetMethod.IsStatic)
        {
            return null;
        }

        return property;
    }

    /// <summary>
    /// 判定属性在状态类型的声明式唯一索引集中有名字命中（命中即存在已物化的可用唯一索引）。
    /// </summary>
    /// <remarks>
    /// Decides whether the property has a name hit in the state type's declared unique-index set. Skipped
    /// declarations never enter the set (see <see cref="ResolveUniqueIndexDeclarations{TState}"/>: blank
    /// declaration names, non-indexable types, <c>[JsonIgnore]</c>, unescapable JSON names), and every
    /// entry's JSON name is non-null (it falls back to the property name), so a name hit implies a usable
    /// materialized unique index.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="property">待判定的属性 / The property to look up</param>
    /// <returns>命中返回 <c>true</c>；未声明或声明被跳过返回 <c>false</c> / <c>true</c> on a hit; <c>false</c> when undeclared or skipped</returns>
    private static bool HasDeclaredUniqueIndex<TState>(PropertyInfo property) where TState : BaseCacheState, new()
    {
        foreach (var declaration in ResolveUniqueIndexDeclarations<TState>())
        {
            if (declaration.Property.Name == property.Name)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 剥离表达式上的类型转换节点（<c>Convert</c> / <c>ConvertChecked</c>），保留可归约的原始成员与常量。
    /// </summary>
    /// <remarks>
    /// Strips the type-conversion nodes (<c>Convert</c> / <c>ConvertChecked</c>) from an expression, keeping
    /// the underlying member or constant reducible.
    /// </remarks>
    /// <param name="expression">待剥离的表达式 / The expression to strip</param>
    /// <returns>剥离转换后的表达式 / The expression without conversion nodes</returns>
    private static Expression StripConvertNodes(Expression expression)
    {
        while (expression is UnaryExpression unary && (unary.NodeType == ExpressionType.Convert || unary.NodeType == ExpressionType.ConvertChecked))
        {
            expression = unary.Operand;
        }

        return expression;
    }

    /// <summary>
    /// 判定表达式是否为「与谓词参数无关的值」（常量 / 闭包捕获 / 静态成员链）。
    /// </summary>
    /// <remarks>
    /// Decides whether an expression is a value independent of the predicate parameter (a constant, a
    /// closure capture or a static member chain).
    /// </remarks>
    /// <param name="expression">待判定表达式 / The expression to inspect</param>
    /// <returns>与参数无关返回 <c>true</c>；否则返回 <c>false</c> / <c>true</c> when independent of the parameter; otherwise <c>false</c></returns>
    private static bool IsParameterIndependentValue(Expression expression)
    {
        if (expression is ConstantExpression)
        {
            return true;
        }

        if (expression is MemberExpression member)
        {
            // 闭包捕获或静态成员：成员链的根须为常量（静态成员的 Expression 为 null）。
            // Closure capture or static member: the member chain must be rooted at a constant
            // (a static member has a null Expression).
            var current = (Expression)member;
            while (current is MemberExpression currentMember)
            {
                current = currentMember.Expression;
                if (current == null)
                {
                    return true;
                }
            }

            return current is ConstantExpression;
        }

        return false;
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
        using (var command = _connection.CreateCommand())
        {
            command.CommandText = "CREATE TABLE IF NOT EXISTS \"__sequences\" (\"key\" TEXT PRIMARY KEY, \"value\" INTEGER NOT NULL);";
            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }
}
