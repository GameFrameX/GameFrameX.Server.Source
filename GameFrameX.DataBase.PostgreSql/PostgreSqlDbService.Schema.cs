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


using System.Collections.Concurrent;
using System.Reflection;
using System.Text.RegularExpressions;
using GameFrameX.DataBase.Abstractions;
using GameFrameX.Foundation.Orm.Attribute;
using GameFrameX.Foundation.Utility;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GameFrameX.DataBase.PostgreSql;

public sealed partial class PostgreSqlDbService
{
    /// <summary>
    /// 表结构就绪缓存（表名 → 单飞初始化任务），对齐 Mongo 适配器的 <c>_indexCache</c> 语义。
    /// </summary>
    /// <remarks>
    /// Schema-readiness cache (table name to its single-flight initialization task), aligned with the Mongo adapter's
    /// <c>_indexCache</c> semantics. Cold-cache concurrent first calls coalesce onto one DDL execution;
    /// a faulted task is evicted so the next caller can retry.
    /// </remarks>
    private readonly ConcurrentDictionary<string, Lazy<Task>> _schemaCache = new();

    /// <summary>
    /// 每 TState 的 EF 上下文选项缓存（绑定当前数据源；数据源重建时整体失效）。
    /// </summary>
    /// <remarks>
    /// Per-TState EF context options cache bound to the current data source. Options (and the internal service
    /// provider they carry) are reused across operations; the cache is cleared whenever the data source is
    /// rebuilt so a reopened service rebinds to the new pool.
    /// </remarks>
    private readonly ConcurrentDictionary<Type, object> _contextOptionsCache = new();

    /// <summary>
    /// 获取当前时间戳（毫秒）。
    /// </summary>
    /// <remarks>
    /// Gets the current timestamp in milliseconds.
    /// If <see cref="DbOptions.IsUseTimeZone"/> is <c>true</c>, returns the timestamp with time zone offset;
    /// otherwise, returns the standard UTC timestamp（与 Mongo 适配器逐字对齐）.
    /// </remarks>
    /// <returns>
    /// 返回当前时间的 Unix 时间戳（毫秒） / Returns the current Unix timestamp in milliseconds
    /// </returns>
    private long GetCurrentTimestamp()
    {
        if (Options.IsUseTimeZone)
        {
            return TimerHelper.UnixTimeMillisecondsWithTimeZoneOffset();
        }

        return TimerHelper.UnixTimeMilliseconds();
    }

    /// <summary>
    /// 创建短生命周期 EF 上下文（每次数据库操作一个；选项按 TState 缓存）。
    /// </summary>
    /// <remarks>
    /// Creates a short-lived EF context — one per database operation (C168 D6: a context is not reusable after a
    /// failed operation, so contexts are never shared). Options are cached per <c>TState</c> and bound to the
    /// current pooled <see cref="DataSource"/>, reusing EF's internal service provider across operations.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <returns>就绪的 EF 上下文 / The ready EF context</returns>
    private PostgreSqlDbContext<TState> CreateContext<TState>() where TState : BaseCacheState, new()
    {
        EnsureInitialized();
        var options = (DbContextOptions<PostgreSqlDbContext<TState>>)_contextOptionsCache.GetOrAdd(
            typeof(TState),
            _ => new DbContextOptionsBuilder<PostgreSqlDbContext<TState>>().UseNpgsql(DataSource).Options);
        return new PostgreSqlDbContext<TState>(options);
    }

    /// <summary>
    /// 构建数据库连接目标标识（脱敏）。
    /// </summary>
    /// <remarks>
    /// Builds a desensitized database connection target identifier.
    /// </remarks>
    /// <param name="connectionString">连接字符串 / Connection string</param>
    /// <param name="databaseName">数据库名称 / Database name</param>
    /// <returns>脱敏后的连接目标标识 / Desensitized connection target identifier</returns>
    private static string BuildConnectionTargetTag(string connectionString, string databaseName)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return $"db={databaseName ?? "unknown"}";
        }

        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            var hostPart = string.IsNullOrWhiteSpace(builder.Host) ? "unknown-host" : builder.Host;
            var dbPart = string.IsNullOrWhiteSpace(builder.Database) ? databaseName : builder.Database;
            return $"host={hostPart};db={dbPart ?? "unknown"}";
        }
        catch
        {
            return $"db={databaseName ?? "unknown"}";
        }
    }

    /// <summary>
    /// 创建无模型的事务壳上下文（仅承载 BEGIN/COMMIT 事务语义，不映射任何实体）。
    /// </summary>
    /// <remarks>
    /// Creates a model-less shell context bound to the pooled data source — used solely by
    /// <c>ExecuteInTransactionAsync</c> to hold the transaction shell. No entity model is built for it, so it
    /// never triggers per-state-type model compilation.
    /// </remarks>
    /// <returns>壳上下文 / The shell context</returns>
    private DbContext CreateShellContext()
    {
        EnsureInitialized();
        var options = (DbContextOptions<DbContext>)_contextOptionsCache.GetOrAdd(
            typeof(DbContext),
            _ => new DbContextOptionsBuilder<DbContext>().UseNpgsql(DataSource).Options);
        return new DbContext(options);
    }

    /// <summary>
    /// 获取文档表名（带引号标识符，表名 = 状态类型名，对齐 Mongo 集合名契约）。
    /// </summary>
    /// <remarks>
    /// Gets the document table name as a quoted identifier (table name = state type name, mirroring the Mongo collection-name contract).
    /// </remarks>
    /// <typeparam name="TState">文档类型 / Document type</typeparam>
    /// <returns>带引号的表名 / The quoted table name</returns>
    private static string GetTableName<TState>()
    {
        return QuoteIdentifier(typeof(TState).Name);
    }

    /// <summary>
    /// 转义并引用 SQL 标识符。
    /// </summary>
    /// <remarks>
    /// Escapes and quotes a SQL identifier.
    /// </remarks>
    /// <param name="identifier">原始标识符 / Raw identifier</param>
    /// <returns>带引号标识符 / Quoted identifier</returns>
    private static string QuoteIdentifier(string identifier)
    {
        return "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    /// <summary>
    /// 确保指定状态类型的 jsonb 文档表与表达式索引就绪（幂等，进程内每类型仅执行一次；冷缓存并发首调单飞合并为一次）。
    /// </summary>
    /// <remarks>
    /// Ensures the jsonb document table and its expression indexes exist for the given state type
    /// (idempotent; executed once per type per process, mirroring the Mongo adapter's index-on-first-collection-access).
    /// Single-flight: concurrent cold-cache first calls publish one <see cref="Lazy{T}"/> task via
    /// <c>GetOrAdd</c> and all await it; if that task faults or is canceled, the entry is evicted so a
    /// later call retries instead of caching the failure forever.
    /// </remarks>
    /// <typeparam name="TState">文档类型 / Document type</typeparam>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    private async Task EnsureTableAsync<TState>(CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        var entityType = typeof(TState);
        var entry = _schemaCache.GetOrAdd(entityType.Name, _ => new Lazy<Task>(() => EnsureTableCoreAsync<TState>(cancellationToken), LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            await entry.Value.ConfigureAwait(false);
        }
        catch
        {
            // 单飞任务失败（faulted/canceled）：仅当缓存仍是本条目时逐出，允许后续调用重新发起
            // The single-flight task failed (faulted/canceled): evict only if the cache still holds this exact entry, allowing later calls to retry.
            _schemaCache.TryRemove(new KeyValuePair<string, Lazy<Task>>(entityType.Name, entry));
            throw;
        }
    }

    /// <summary>
    /// 建表 + 索引同步核心（由 <see cref="EnsureTableAsync"/> 单飞发布，仅首调执行一次）。
    /// </summary>
    /// <remarks>
    /// Table creation uses the EF model-generated DDL (<c>GenerateCreateScript</c>, zero hand-written SQL in code);
    /// a pre-existing table (SQLSTATE 42P07, e.g. created by C166 or a concurrent process) counts as success —
    /// this keeps per-type lazy creation working in shared databases where <c>EnsureCreated</c> would no-op
    /// on the first foreign table. The jsonb expression indexes are then synchronized through the whitelisted
    /// bootstrap DDL (the only raw-statement category left in this adapter, C168 D4).
    /// </remarks>
    /// <typeparam name="TState">文档类型 / Document type</typeparam>
    /// <param name="cancellationToken">首调传入的取消令牌 / The cancellation token supplied by the first caller</param>
    private async Task EnsureTableCoreAsync<TState>(CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        using (var context = CreateContext<TState>())
        {
            var createScript = context.Database.GenerateCreateScript();
            try
            {
                await context.Database.ExecuteSqlRawAsync(createScript, cancellationToken).ConfigureAwait(false);
            }
            catch (PostgresException exception) when (exception.SqlState == PostgreSqlSqlState.DuplicateTable)
            {
                // 表已存在（C166 存量库 / 并发进程先行创建）：视作成功，存量表结构零迁移。
                // Table already exists (C166-stored database or a concurrent process won the race): success — existing tables are never migrated.
            }
        }

        await using var connection = DataSource.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnsureIndexesAsync(connection, GetTableName<TState>(), typeof(TState), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 同步指定实体类型的 jsonb 表达式索引（同名异构时重建；cast 矩阵与 EF 翻译器生成逐字一致，C168 D4）。
    /// </summary>
    /// <remarks>
    /// Synchronizes the jsonb expression indexes for the given entity type. The cast matrix is aligned verbatim
    /// with what the EF translator emits (bool→boolean、byte/short→smallint、int→integer、long→bigint、float→real、
    /// double→double precision、decimal→numeric、DateTime/DateTimeOffset→timestamp with time zone、Guid→uuid、
    /// enum 按底层整型、string 无 cast)，so predicates can hit the indexes. Same-name indexes whose stored
    /// definition drifted (C166's int→bigint matrix, unique-flag changes) are rebuilt in place — the rolling
    /// migration path from C166 indexes (breaking change per C168 spec).
    /// </remarks>
    /// <param name="connection">活动连接 / Active connection</param>
    /// <param name="tableName">表名 / Table name</param>
    /// <param name="entityType">实体类型 / Entity type</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    private static async Task EnsureIndexesAsync(NpgsqlConnection connection, string tableName, Type entityType, CancellationToken cancellationToken)
    {
        var indexAttributes = entityType.GetProperties()
                                        .Select(property => (Property: property, Attribute: property.GetCustomAttribute<EntityIndexAttribute>()))
                                        .Where(static entry => entry.Attribute != null)
                                        .ToList();
        if (indexAttributes.Count == 0)
        {
            return;
        }

        var existingIndexes = await ListIndexDefinitionsAsync(connection, tableName, cancellationToken).ConfigureAwait(false);
        foreach (var (property, attribute) in indexAttributes)
        {
            var indexName = attribute.Name;
            if (string.IsNullOrWhiteSpace(indexName))
            {
                continue;
            }

            var quotedIndexName = QuoteIdentifier(indexName);
            var expectedDefinition = BuildExpectedIndexDefinition(tableName, quotedIndexName, property.Name, property.PropertyType, attribute.Unique, attribute.IsAscending);
            existingIndexes.TryGetValue(indexName, out var existingDefinition);
            if (existingDefinition != null && NormalizeIndexDefinition(expectedDefinition) == NormalizeIndexDefinition(existingDefinition))
            {
                continue;
            }

            if (existingDefinition != null)
            {
                // 同名异构（cast 矩阵升级 / 唯一性冲突）：重建以对齐实体声明（C166 存量索引滚动迁移路径）。
                // Same name but drifted shape (cast-matrix upgrade / unique mismatch): rebuild to match the declaration (the rolling path off C166 indexes).
                await ExecuteNonQueryAsync(connection, $"DROP INDEX IF EXISTS {quotedIndexName}", cancellationToken).ConfigureAwait(false);
            }

            var uniquePrefix = attribute.Unique ? "UNIQUE " : string.Empty;
            var directionSuffix = attribute.IsAscending ? string.Empty : " DESC";
            var expression = $"{BuildIndexExpressionCore(property.Name, property.PropertyType)}{directionSuffix}";
            await ExecuteNonQueryAsync(connection, $"CREATE {uniquePrefix}INDEX IF NOT EXISTS {quotedIndexName} ON {tableName} USING btree ({expression})", cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 查询表上现有索引定义（索引名 → indexdef；pg_indexes 探测，白名单内语句）。
    /// </summary>
    /// <remarks>
    /// Lists the index definitions on the table (index name to indexdef; pg_indexes probe, whitelisted).
    /// </remarks>
    private static async Task<Dictionary<string, string>> ListIndexDefinitionsAsync(NpgsqlConnection connection, string tableName, CancellationToken cancellationToken)
    {
        var unquotedTableName = tableName.Trim('"');
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        await using var command = new NpgsqlCommand("SELECT indexname, indexdef FROM pg_indexes WHERE tablename = @tableName", connection);
        command.Parameters.AddWithValue("tableName", unquotedTableName);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            result[reader.GetString(0)] = reader.GetString(1);
        }

        return result;
    }

    /// <summary>
    /// 构建期望中的索引定义文本（与 pg_indexes 回显形态一致，用于异构判定）。
    /// </summary>
    /// <remarks>
    /// Builds the expected index definition in the same shape <c>pg_indexes</c> echoes back (default ASC is
    /// omitted by PostgreSQL), used for drift detection via normalized comparison.
    /// </remarks>
    /// <param name="tableName">表名 / Table name</param>
    /// <param name="quotedIndexName">带引号索引名 / The quoted index name</param>
    /// <param name="propertyName">属性名 / Property name</param>
    /// <param name="propertyType">属性 CLR 类型 / Property CLR type</param>
    /// <param name="unique">是否唯一 / Whether unique</param>
    /// <param name="isAscending">是否升序 / Whether ascending</param>
    /// <returns>期望的索引定义文本 / The expected index definition text</returns>
    private static string BuildExpectedIndexDefinition(string tableName, string quotedIndexName, string propertyName, Type propertyType, bool unique, bool isAscending)
    {
        var uniquePrefix = unique ? "UNIQUE " : string.Empty;
        var directionSuffix = isAscending ? string.Empty : " DESC";
        return $"CREATE {uniquePrefix}INDEX {quotedIndexName} ON {tableName} USING btree ({BuildIndexExpressionCore(propertyName, propertyType)}{directionSuffix})";
    }

    /// <summary>
    /// 构建 jsonb 表达式索引片段（cast 与 EF 翻译器谓词逐字一致，方向后缀由调用方追加）。
    /// </summary>
    /// <remarks>
    /// Builds the jsonb expression index fragment (cast aligned verbatim with EF-translated predicates; the
    /// direction suffix is appended by the caller). PostgreSQL matches <c>CAST(doc->>'X' AS T)</c> predicates
    /// against <c>(doc->>'X')::T</c> indexes — the parser normalizes both to the same tree.
    /// </remarks>
    /// <param name="propertyName">属性名 / Property name</param>
    /// <param name="propertyType">属性 CLR 类型 / Property CLR type</param>
    /// <returns>索引表达式 / The index expression</returns>
    private static string BuildIndexExpressionCore(string propertyName, Type propertyType)
    {
        var accessor = "doc->>'" + propertyName.Replace("'", "''", StringComparison.Ordinal) + "'";
        var cast = GetIndexCastSuffix(propertyType);
        return $"(({accessor}){cast})";
    }

    /// <summary>
    /// 获取属性类型对应的索引 cast 后缀（与 EF 翻译器实测输出逐字对齐；空串表示按文本）。
    /// </summary>
    /// <remarks>
    /// Gets the index cast suffix for the property type, aligned verbatim with the EF translator's live output
    /// (C168 实测锁定): bool→boolean、byte/short→smallint、int→integer、long→bigint、float→real、
    /// double→double precision、decimal→numeric、DateTime/DateTimeOffset→timestamp with time zone、Guid→uuid、
    /// 枚举按底层整型；string 与 EF 不可翻译类型无 cast（按文本）。任何 cast 变化都会使同名索引异构并触发重建。
    /// </remarks>
    /// <param name="propertyType">属性 CLR 类型（可空类型自动解包）/ Property CLR type (nullable types unwrapped)</param>
    /// <returns>cast 后缀；空串表示无 / The cast suffix, or empty for none</returns>
    private static string GetIndexCastSuffix(Type propertyType)
    {
        var underlying = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        if (underlying.IsEnum)
        {
            underlying = Enum.GetUnderlyingType(underlying);
        }

        if (underlying == typeof(bool))
        {
            return "::boolean";
        }

        if (underlying == typeof(byte) || underlying == typeof(short))
        {
            return "::smallint";
        }

        if (underlying == typeof(int))
        {
            return "::integer";
        }

        if (underlying == typeof(long))
        {
            return "::bigint";
        }

        if (underlying == typeof(float))
        {
            return "::real";
        }

        if (underlying == typeof(double))
        {
            return "::double precision";
        }

        if (underlying == typeof(decimal))
        {
            return "::numeric";
        }

        if (underlying == typeof(DateTime) || underlying == typeof(DateTimeOffset))
        {
            return "::timestamp with time zone";
        }

        if (underlying == typeof(Guid))
        {
            return "::uuid";
        }

        return string.Empty;
    }

    /// <summary>
    /// 归一化索引定义文本用于异构比较（去模式限定 / 去 <c>::text</c> / 去空白 / 大小写折叠）。
    /// </summary>
    /// <remarks>
    /// Normalizes an index definition for drift comparison: strips the schema qualifier (varies with
    /// <c>search_path</c>), the <c>::text</c> casts PostgreSQL appends to <c>-&gt;&gt;</c> accessors, all
    /// whitespace, and folds case — leaving a stable shape for same-name drift detection.
    /// </remarks>
    /// <param name="definition">索引定义文本 / The index definition text</param>
    /// <returns>归一化文本 / The normalized text</returns>
    private static string NormalizeIndexDefinition(string definition)
    {
        var withoutSchema = Regex.Replace(definition, @"(?i)\bON\s+[^\s]+\.", "ON ", RegexOptions.CultureInvariant);
        var withoutTextCast = withoutSchema.Replace("::text", string.Empty, StringComparison.OrdinalIgnoreCase);
        return Regex.Replace(withoutTextCast, @"\s+", string.Empty).ToLowerInvariant();
    }

    /// <summary>
    /// 在活动连接上执行无结果命令（白名单：索引引导 DDL 与 pg_indexes 探测）。
    /// </summary>
    /// <remarks>
    /// Executes a no-result command on the active connection (whitelisted: index-bootstrap DDL and the pg_indexes probe).
    /// </remarks>
    private static async Task<int> ExecuteNonQueryAsync(NpgsqlConnection connection, string commandText, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(commandText, connection);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}