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


using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq.Expressions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.DataBase.Abstractions;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Localization;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace GameFrameX.DataBase.PostgreSql;

public sealed partial class PostgreSqlDbService
{
    /// <summary>
    /// CAS 原生路径的整档序列化选项（纯默认 STJ：PascalCase、枚举数值、写 null，与 EF ToJson 读侧兼容）。
    /// </summary>
    /// <remarks>
    /// The whole-document serialization options for the raw-SQL CAS path — plain default
    /// <see cref="JsonSerializerOptions"/> (PascalCase, numeric enums, nulls written), compatible with the
    /// EF ToJson read side. <see cref="GameFrameX.Foundation.Json"/> helper's defaults (nulls ignored,
    /// string enums) are deliberately NOT used — they diverge from the EF ToJson stored shape.
    /// </remarks>
    private static readonly JsonSerializerOptions DocumentJsonOptions = new() { IncludeFields = false, };

    /// <summary>
    /// 序列计数表名（未加引号常量，使用处经 <see cref="QuoteIdentifier"/> 包引号）。
    /// </summary>
    /// <remarks>
    /// The sequence counter table name (unquoted constant; quoted via <see cref="QuoteIdentifier"/> at use sites).
    /// </remarks>
    private const string SequenceTableName = "__sequences";

    #region 条件写

    /// <summary>
    /// 条件整档替换：仅当存量档 <c>RowVersion</c> 数值等值于期望版本时写入（成功档版本 = 期望 + 1）。
    /// </summary>
    /// <remarks>
    /// Conditionally replaces the whole document: the write lands only when the stored document's
    /// <c>RowVersion</c> equals <paramref name="expectedRowVersion"/>; on success the stored version becomes
    /// <paramref name="expectedRowVersion"/> + 1. 不触碰 UpdateTime/UpdateCount（按 state 原样持久化，调用方自管审计字段）；
    /// 成功后回调 <c>SaveToDbPostHandler</c>（对齐 <see cref="UpdateAsync{TState}(TState)"/> 行为）。
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="id">目标文档 Id / The target document id</param>
    /// <param name="expectedRowVersion">期望的存量版本号 / The expected stored row version</param>
    /// <param name="state">要写入的整档状态 / The whole state to write</param>
    /// <returns>成功替换返回 true；版本不符或行缺失返回 false（不抛异常）/ True when replaced; false on version mismatch or missing row (no exception)</returns>
    public async Task<bool> UpdateIfRowVersionAsync<TState>(long id, long expectedRowVersion, TState state) where TState : BaseCacheState, new()
    {
        return await UpdateIfRowVersionAsync(id, expectedRowVersion, state, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 条件整档替换：仅当存量档 <c>RowVersion</c> 数值等值于期望版本时写入（成功档版本 = 期望 + 1）。
    /// </summary>
    /// <remarks>
    /// Conditionally replaces the whole document: the write lands only when the stored document's
    /// <c>RowVersion</c> equals <paramref name="expectedRowVersion"/>; on success the stored version becomes
    /// <paramref name="expectedRowVersion"/> + 1 (checked increment — <c>long.MaxValue</c> overflows instead of
    /// silently wrapping). UpdateTime/UpdateCount are persisted as-is (the caller owns audit fields); on success the
    /// state receives <c>SaveToDbPostHandler</c> (aligned with <see cref="UpdateAsync{TState}(TState)"/>).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="id">目标文档 Id / The target document id</param>
    /// <param name="expectedRowVersion">期望的存量版本号 / The expected stored row version</param>
    /// <param name="state">要写入的整档状态 / The whole state to write</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>成功替换返回 true；版本不符或行缺失返回 false（不抛异常）/ True when replaced; false on version mismatch or missing row (no exception)</returns>
    /// <exception cref="ArgumentException">当 <paramref name="state"/> 的 Id 与 <paramref name="id"/> 不一致时抛出（写库前检查）/ Thrown when the state's Id differs from <paramref name="id"/> (checked before any write)</exception>
    /// <exception cref="OverflowException">当 <paramref name="expectedRowVersion"/> 为 <c>long.MaxValue</c> 时抛出（checked 递增，不写库）/ Thrown when the expected version is <c>long.MaxValue</c> (checked increment; nothing is written)</exception>
    public async Task<bool> UpdateIfRowVersionAsync<TState>(long id, long expectedRowVersion, TState state, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();

        return await RowVersionCasWriteHelper.WriteAsync(id, expectedRowVersion, state, async () =>
        {
            var documentJson = JsonSerializer.Serialize(state, DocumentJsonOptions);
            var replaced = await ExecuteWriteWithRetryAsync(async token =>
            {
                await EnsureTableAsync<TState>(token).ConfigureAwait(false);
                var (connection, ambientTransaction, ownedConnection) = await OpenConnectionForWriteAsync(token).ConfigureAwait(false);
                try
                {
                    // 版本谓词用数值比较：doc->>'RowVersion' 是 jsonb 文本投影，text=text 等值对非规范表示（如前导零）误判；
                    // ::bigint 两侧数值化后与 @expected（bigint）严格等值。
                    // The version predicate compares numerically: doc->>'RowVersion' is the jsonb text projection, and a
                    // text=text equality misjudges non-canonical representations (leading zeros and the like); ::bigint
                    // coerces both sides to numbers compared strictly against @expected (bigint).
                    await using (var command = new NpgsqlCommand($"UPDATE {GetTableName<TState>()} SET doc = @doc::jsonb WHERE id = @id AND (doc->>'RowVersion')::bigint = @expected", connection))
                    {
                        command.Transaction = ambientTransaction;
                        command.Parameters.Add(new NpgsqlParameter("doc", NpgsqlDbType.Jsonb) { Value = documentJson, });
                        command.Parameters.AddWithValue("id", id);
                        command.Parameters.AddWithValue("expected", expectedRowVersion);
                        var affectedRows = await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                        return affectedRows == 1;
                    }
                }
                finally
                {
                    if (ownedConnection)
                    {
                        await connection.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }, cancellationToken, nameof(UpdateIfRowVersionAsync), false).ConfigureAwait(false);
            return replaced;
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// 条件整档替换：仅当存量档 <c>RowVersion</c> 缺失（字段缺位或 JSON null）时写入（存量迁移回填原语；成功档版本 = 0）。
    /// </summary>
    /// <remarks>
    /// Conditionally replaces the whole document only when the stored <c>RowVersion</c> is missing
    /// or null (legacy-row migration / backfill primitive); on success the stored version becomes 0
    /// (the checked increment from the null backfill baseline -1 — the backfilled row shares the
    /// fresh-row baseline). The version predicate is
    /// <c>(doc->>'RowVersion') IS NULL</c> — the jsonb text projection is SQL NULL both for an absent
    /// key and for a stored JSON null, so exactly the missing-version legacy rows match; a non-null
    /// stored version or a missing row returns <c>false</c> without throwing. 不触碰
    /// UpdateTime/UpdateCount（按 state 原样持久化，调用方自管审计字段）；成功后回调
    /// <c>SaveToDbPostHandler</c>。失败（false）路径回滚写前的内存版本递增；异常路径不回滚。
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="id">目标文档 Id / The target document id</param>
    /// <param name="state">要写入的整档状态 / The whole state to write</param>
    /// <returns>存量缺失行版本且写入成功返回 true；存量 <c>RowVersion</c> 非 null 或行缺失返回 false（不抛异常）/ True when the stored version was missing and the row was written; false on a non-null stored version or a missing row (no exception)</returns>
    /// <exception cref="ArgumentException">当 <paramref name="state"/> 的 Id 与 <paramref name="id"/> 不一致时抛出（写库前检查）/ Thrown when the state's Id differs from <paramref name="id"/> (checked before any write)</exception>
    public async Task<bool> UpdateIfRowVersionIsNullAsync<TState>(long id, TState state) where TState : BaseCacheState, new()
    {
        return await UpdateIfRowVersionIsNullAsync(id, state, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 条件整档替换：仅当存量档 <c>RowVersion</c> 缺失（字段缺位或 JSON null）时写入（存量迁移回填原语；成功档版本 = 0）。
    /// </summary>
    /// <remarks>
    /// Conditionally replaces the whole document only when the stored <c>RowVersion</c> is missing
    /// or null (legacy-row migration / backfill primitive); on success the stored version becomes 0
    /// (the checked increment from the null backfill baseline -1). See the cancellation-free overload for
    /// the full concurrency contract.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="id">目标文档 Id / The target document id</param>
    /// <param name="state">要写入的整档状态 / The whole state to write</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>存量缺失行版本且写入成功返回 true；存量 <c>RowVersion</c> 非 null 或行缺失返回 false（不抛异常）/ True when the stored version was missing and the row was written; false on a non-null stored version or a missing row (no exception)</returns>
    /// <exception cref="ArgumentException">当 <paramref name="state"/> 的 Id 与 <paramref name="id"/> 不一致时抛出（写库前检查）/ Thrown when the state's Id differs from <paramref name="id"/> (checked before any write)</exception>
    public async Task<bool> UpdateIfRowVersionIsNullAsync<TState>(long id, TState state, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();

        return await RowVersionBackfillWriteHelper.WriteAsync(id, state, async () =>
        {
            var documentJson = JsonSerializer.Serialize(state, DocumentJsonOptions);
            var replaced = await ExecuteWriteWithRetryAsync(async token =>
            {
                await EnsureTableAsync<TState>(token).ConfigureAwait(false);
                var (connection, ambientTransaction, ownedConnection) = await OpenConnectionForWriteAsync(token).ConfigureAwait(false);
                try
                {
                    // 版本谓词用 IS NULL：doc->>'RowVersion' 对字段缺位与 JSON null 均投影 SQL NULL，
                    // 恰好命中「缺失行版本」的存量行；非 null 版本恒不匹配。
                    // The version predicate is IS NULL: doc->>'RowVersion' projects SQL NULL both for an
                    // absent key and for a stored JSON null — exactly the missing-version legacy rows;
                    // a non-null stored version never matches.
                    await using (var command = new NpgsqlCommand($"UPDATE {GetTableName<TState>()} SET doc = @doc::jsonb WHERE id = @id AND (doc->>'RowVersion') IS NULL", connection))
                    {
                        command.Transaction = ambientTransaction;
                        command.Parameters.Add(new NpgsqlParameter("doc", NpgsqlDbType.Jsonb) { Value = documentJson, });
                        command.Parameters.AddWithValue("id", id);
                        var affectedRows = await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                        return affectedRows == 1;
                    }
                }
                finally
                {
                    if (ownedConnection)
                    {
                        await connection.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }, cancellationToken, nameof(UpdateIfRowVersionIsNullAsync), false).ConfigureAwait(false);
            return replaced;
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// 唯一键条件插入：唯一性判定无视软删可见性，键命中或 Id 已占用返回 false。
    /// </summary>
    /// <remarks>
    /// Tries to insert the state when no existing document (soft-deleted included — visibility filters are
    /// bypassed) satisfies <paramref name="uniqueFilter"/> and the id is not taken. Timestamps/update count are
    /// filled exactly like <see cref="AddAsync{TState}(TState)"/>. A provider unique-index violation (SQLSTATE 23505, e.g. a
    /// concurrent insert that escaped the pre-check) is translated to false instead of surfacing.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="state">要插入的整档状态 / The whole state to insert</param>
    /// <param name="uniqueFilter">唯一键判定过滤表达式 / The unique-key membership filter</param>
    /// <returns>插入成功返回 true；唯一键命中或 Id 已占用返回 false（不抛异常）/ True when inserted; false on unique-key hit or taken id (no exception)</returns>
    public async Task<bool> TryAddUniqueAsync<TState>(TState state, Expression<Func<TState, bool>> uniqueFilter) where TState : BaseCacheState, new()
    {
        return await TryAddUniqueAsync(state, uniqueFilter, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 唯一键条件插入：唯一性判定无视软删可见性，键命中或 Id 已占用返回 false。
    /// </summary>
    /// <remarks>
    /// Tries to insert the state when no existing document (soft-deleted included — visibility filters are
    /// bypassed) satisfies <paramref name="uniqueFilter"/> and the id is not taken. Timestamps/update count are
    /// filled exactly like <see cref="AddAsync{TState}(TState)"/>. A provider unique-index violation (SQLSTATE 23505, e.g. a
    /// concurrent insert that escaped the pre-check) is translated to false instead of surfacing; the EF context
    /// is recreated per retry attempt (a context is not reusable after a failed operation).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="state">要插入的整档状态 / The whole state to insert</param>
    /// <param name="uniqueFilter">唯一键判定过滤表达式 / The unique-key membership filter</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>插入成功返回 true；唯一键命中或 Id 已占用返回 false（不抛异常）/ True when inserted; false on unique-key hit or taken id (no exception)</returns>
    public async Task<bool> TryAddUniqueAsync<TState>(TState state, Expression<Func<TState, bool>> uniqueFilter, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();
        var currentTime = GetCurrentTimestamp();
        state.CreatedTime = currentTime;
        state.UpdateTime = currentTime;
        state.UpdateCount ??= 0;
        var rowFilter = PostgreSqlStateRowExpressionRewriter.RewriteFilter(uniqueFilter);
        return await ExecuteWriteWithRetryAsync(async token =>
        {
            await EnsureTableAsync<TState>(token).ConfigureAwait(false);
            using (var context = CreateContext<TState>())
            {
                JoinAmbientTransaction(context);
                // 唯一性判定无视软删可见性：IgnoreQueryFilters 查全量档（物理唯一索引口径）。
                // Uniqueness ignores soft-delete visibility: IgnoreQueryFilters scans every row (physical unique-index semantics).
                if (await context.Rows.IgnoreQueryFilters().AnyAsync(rowFilter, token).ConfigureAwait(false))
                {
                    return false;
                }

                if (await context.Rows.IgnoreQueryFilters().AnyAsync(row => row.Id == state.Id, token).ConfigureAwait(false))
                {
                    return false;
                }

                context.Rows.Add(new StateRow<TState> { Id = state.Id, Doc = state, });
                try
                {
                    await context.SaveChangesAsync(token).ConfigureAwait(false);
                    return true;
                }
                catch (DbUpdateException exception) when (PostgreSqlSqlState.IsUniqueViolation(exception))
                {
                    // 预查与插入之间的竞态由 provider 唯一索引兜底：23505 转译为 false 而非上抛。
                    // The race between pre-check and insert is backstopped by the provider unique index: 23505 translates to false.
                    return false;
                }
                catch (PostgresException exception) when (exception.SqlState == PostgreSqlSqlState.UniqueViolation)
                {
                    return false;
                }
            }
        }, cancellationToken, nameof(TryAddUniqueAsync), false).ConfigureAwait(false);
    }

    /// <summary>
    /// 原子获取指定序列的下一个值（首次返回 1，单调递增；失败重试可产生空洞，永不重复、永不回退）。
    /// </summary>
    /// <remarks>
    /// Atomically yields the next value of the named sequence (first call returns 1; monotonically increasing).
    /// Retries after a failure may leave holes — values are never repeated and never regress.
    /// </remarks>
    /// <typeparam name="TState">参与计数键控的缓存状态类型 / The cache state type participating in the counter key</typeparam>
    /// <param name="sequenceName">序列名称 / The sequence name</param>
    /// <returns>序列新值（首次为 1）/ The new sequence value (1 on the first call)</returns>
    /// <exception cref="ArgumentException">当 <paramref name="sequenceName"/> 为 null 或空白时抛出 / Thrown when the sequence name is null or whitespace</exception>
    public async Task<long> NextSequenceAsync<TState>(string sequenceName) where TState : BaseCacheState, new()
    {
        return await NextSequenceAsync<TState>(sequenceName, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 原子获取指定序列的下一个值（首次返回 1，单调递增；失败重试可产生空洞，永不重复、永不回退）。
    /// </summary>
    /// <remarks>
    /// Atomically yields the next value of the named sequence (first call returns 1; monotonically increasing —
    /// a single <c>INSERT ... ON CONFLICT DO UPDATE ... RETURNING</c> statement). Retries after a failure may
    /// leave holes — values are never repeated and never regress.
    /// </remarks>
    /// <typeparam name="TState">参与计数键控的缓存状态类型 / The cache state type participating in the counter key</typeparam>
    /// <param name="sequenceName">序列名称 / The sequence name</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>序列新值（首次为 1）/ The new sequence value (1 on the first call)</returns>
    /// <exception cref="ArgumentException">当 <paramref name="sequenceName"/> 为 null 或空白时抛出 / Thrown when the sequence name is null or whitespace</exception>
    public async Task<long> NextSequenceAsync<TState>(string sequenceName, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();
        if (string.IsNullOrWhiteSpace(sequenceName))
        {
            throw new ArgumentException(LocalizationService.GetString(Localization.Keys.Database.ConditionalWriteSequenceNameRequired), nameof(sequenceName));
        }

        var counterKey = typeof(TState).Name + ":" + sequenceName;
        return await ExecuteWriteWithRetryAsync(async token =>
        {
            await EnsureSequenceTableAsync(token).ConfigureAwait(false);
            var (connection, ambientTransaction, ownedConnection) = await OpenConnectionForWriteAsync(token).ConfigureAwait(false);
            try
            {
                await using (var command = new NpgsqlCommand(
                    $"INSERT INTO {QuoteIdentifier(SequenceTableName)} (\"key\", \"value\") VALUES (@key, 1) ON CONFLICT (\"key\") DO UPDATE SET \"value\" = {QuoteIdentifier(SequenceTableName)}.\"value\" + 1 RETURNING \"value\"",
                    connection))
                {
                    command.Transaction = ambientTransaction;
                    command.Parameters.AddWithValue("key", counterKey);
                    var result = await command.ExecuteScalarAsync(token).ConfigureAwait(false);
                    return Convert.ToInt64(result, CultureInfo.InvariantCulture);
                }
            }
            finally
            {
                if (ownedConnection)
                {
                    await connection.DisposeAsync().ConfigureAwait(false);
                }
            }
        }, cancellationToken, nameof(NextSequenceAsync), false).ConfigureAwait(false);
    }

    /// <summary>
    /// 确保序列计数表就绪（经 <c>_schemaCache</c> 单飞，进程内仅建表一次；失败逐出允许重试）。
    /// </summary>
    /// <remarks>
    /// Ensures the sequence counter table exists (single-flight via <c>_schemaCache</c>, created once per
    /// process; a faulted entry is evicted so a later call can retry — same semantics as
    /// <see cref="EnsureTableAsync{TState}"/>). The single-flight task runs on
    /// <see cref="CancellationToken.None"/> so a first caller cancelling cannot cancel the shared
    /// DDL for every waiter; callers detach via <c>WaitAsync(cancellationToken)</c> and a waiter-side
    /// cancellation does not evict the entry.
    /// </remarks>
    /// <param name="cancellationToken">取消令牌（仅解除等待，不传入单飞任务）/ Cancellation token (detaches the waiter only; never flows into the single-flight task)</param>
    private async Task EnsureSequenceTableAsync(CancellationToken cancellationToken)
    {
        var entry = _schemaCache.GetOrAdd(SequenceTableName, _ => new Lazy<Task>(() => EnsureSequenceTableCoreAsync(CancellationToken.None), LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            await entry.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 等待方自身取消：单飞任务运行于 CancellationToken.None 不受牵连，缓存条目保留。
            // The waiter itself was cancelled: the single-flight task (running on CancellationToken.None) is unaffected; keep the cached entry.
            throw;
        }
        catch
        {
            // 单飞任务失败（faulted）：仅当缓存仍是本条目时逐出，允许后续调用重新发起。
            // 注意：此 bare catch 是有意的——上方 OCE 过滤分支已吸收等待方取消，到达这里的必为底层任务
            // 自身的真实故障；收紧为具型 catch 会破坏「故障逐出重试」契约。
            // The single-flight task failed (faulted): evict only if the cache still holds this exact entry, allowing later calls to retry.
            // Note: the bare catch is intentional — the OCE filter above absorbs waiter-side cancellation, so only genuine
            // task faults reach here; tightening it into typed clauses would break the eviction-on-fault contract.
            _schemaCache.TryRemove(new KeyValuePair<string, Lazy<Task>>(SequenceTableName, entry));
            throw;
        }
    }

    /// <summary>
    /// 序列计数表建表核心（CREATE TABLE IF NOT EXISTS，幂等）。
    /// </summary>
    /// <remarks>
    /// The sequence-table creation core (<c>CREATE TABLE IF NOT EXISTS</c>, idempotent).
    /// </remarks>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    private async Task EnsureSequenceTableCoreAsync(CancellationToken cancellationToken)
    {
        await using (var connection = DataSource.CreateConnection())
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await ExecuteNonQueryAsync(connection, $"CREATE TABLE IF NOT EXISTS {QuoteIdentifier(SequenceTableName)} (\"key\" text PRIMARY KEY, \"value\" bigint NOT NULL)", cancellationToken).ConfigureAwait(false);
        }
    }

    #endregion 条件写
}
