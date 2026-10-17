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
using GameFrameX.Foundation.Orm.Entity;
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
        if (state.Id != id)
        {
            throw new ArgumentException(LocalizationService.GetString(Localization.Keys.Database.ConditionalWriteStateIdMismatch, state.Id, id), nameof(state));
        }

        // 版本准备：先对齐到期望版本，再吃 checked 递增语义（MaxValue 溢出在写库前抛出）。
        // 失败（false）路径回滚内存版本——写前递增若不回滚会污染调用方 state.RowVersion，后续 CAS 持续 Stale；
        // 异常路径不回滚（写结果未知，回滚可能反向脱节）。
        // Version preparation: align to the expected version first, then take the checked increment (MaxValue
        // overflows before any write). The pre-write increment is rolled back on the false path — leaving it in
        // place would pollute the caller's state.RowVersion and starve every later CAS; it is NOT rolled back on
        // exceptions (the write outcome is unknown, and rolling back could desync the other way).
        var originalRowVersion = state.RowVersion;
        state.RowVersion = expectedRowVersion;
        state.IncrementRowVersion();
        var documentJson = JsonSerializer.Serialize(state, DocumentJsonOptions);
        var replaced = await ExecuteWriteWithRetryAsync(async token =>
        {
            await EnsureTableAsync<TState>(token).ConfigureAwait(false);
            await using var connection = DataSource.CreateConnection();
            await connection.OpenAsync(token).ConfigureAwait(false);
            // 版本谓词用数值比较：doc->>'RowVersion' 是 jsonb 文本投影，text=text 等值对非规范表示（如前导零）误判；
            // ::bigint 两侧数值化后与 @expected（bigint）严格等值。
            // The version predicate compares numerically: doc->>'RowVersion' is the jsonb text projection, and a
            // text=text equality misjudges non-canonical representations (leading zeros and the like); ::bigint
            // coerces both sides to numbers compared strictly against @expected (bigint).
            await using var command = new NpgsqlCommand($"UPDATE {GetTableName<TState>()} SET doc = @doc::jsonb WHERE id = @id AND (doc->>'RowVersion')::bigint = @expected", connection);
            command.Parameters.Add(new NpgsqlParameter("doc", NpgsqlDbType.Jsonb) { Value = documentJson, });
            command.Parameters.AddWithValue("id", id);
            command.Parameters.AddWithValue("expected", expectedRowVersion);
            var affectedRows = await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            return affectedRows == 1;
        }, cancellationToken, nameof(UpdateIfRowVersionAsync), false).ConfigureAwait(false);
        if (replaced)
        {
            state.SaveToDbPostHandler();
            return true;
        }

        // 版本不符或行缺失：未写库，回滚写前递增的内存版本。
        // Version mismatch or missing row: nothing was written — roll back the pre-write in-memory increment.
        state.RowVersion = originalRowVersion;
        return false;
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
            using var context = CreateContext<TState>();
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
            await using var connection = DataSource.CreateConnection();
            await connection.OpenAsync(token).ConfigureAwait(false);
            await using var command = new NpgsqlCommand(
                $"INSERT INTO {QuoteIdentifier(SequenceTableName)} (\"key\", \"value\") VALUES (@key, 1) ON CONFLICT (\"key\") DO UPDATE SET \"value\" = {QuoteIdentifier(SequenceTableName)}.\"value\" + 1 RETURNING \"value\"",
                connection);
            command.Parameters.AddWithValue("key", counterKey);
            var result = await command.ExecuteScalarAsync(token).ConfigureAwait(false);
            return Convert.ToInt64(result, CultureInfo.InvariantCulture);
        }, cancellationToken, nameof(NextSequenceAsync), false).ConfigureAwait(false);
    }

    /// <summary>
    /// 确保序列计数表就绪（经 <c>_schemaCache</c> 单飞，进程内仅建表一次；失败逐出允许重试）。
    /// </summary>
    /// <remarks>
    /// Ensures the sequence counter table exists (single-flight via <c>_schemaCache</c>, created once per
    /// process; a faulted entry is evicted so a later call can retry — same semantics as
    /// <see cref="EnsureTableAsync{TState}"/>).
    /// </remarks>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    private async Task EnsureSequenceTableAsync(CancellationToken cancellationToken)
    {
        var entry = _schemaCache.GetOrAdd(SequenceTableName, _ => new Lazy<Task>(() => EnsureSequenceTableCoreAsync(cancellationToken), LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            await entry.Value.ConfigureAwait(false);
        }
        catch
        {
            // 单飞任务失败（faulted/canceled）：仅当缓存仍是本条目时逐出，允许后续调用重新发起。
            // The single-flight task failed (faulted/canceled): evict only if the cache still holds this exact entry, allowing later calls to retry.
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
        await using var connection = DataSource.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await ExecuteNonQueryAsync(connection, $"CREATE TABLE IF NOT EXISTS {QuoteIdentifier(SequenceTableName)} (\"key\" text PRIMARY KEY, \"value\" bigint NOT NULL)", cancellationToken).ConfigureAwait(false);
    }

    #endregion 条件写
}
