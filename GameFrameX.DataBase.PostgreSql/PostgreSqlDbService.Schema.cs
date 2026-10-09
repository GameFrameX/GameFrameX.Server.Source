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


using System.Collections.Concurrent;
using System.Reflection;
using GameFrameX.DataBase.Abstractions;
using GameFrameX.Foundation.Orm.Attribute;
using GameFrameX.Foundation.Utility;
using Npgsql;
using NpgsqlTypes;

namespace GameFrameX.DataBase.PostgreSql;

public sealed partial class PostgreSqlDbService
{
    /// <summary>
    /// 表结构就绪缓存（表名 → 单飞 DDL 任务），对齐 Mongo 适配器的 <c>_indexCache</c> 语义。
    /// </summary>
    /// <remarks>
    /// Schema-readiness cache (table name to its single-flight DDL task), aligned with the Mongo adapter's
    /// <c>_indexCache</c> semantics. Cold-cache concurrent first calls coalesce onto one DDL execution;
    /// a faulted task is evicted so the next caller can retry.
    /// </remarks>
    private readonly ConcurrentDictionary<string, Lazy<Task>> _schemaCache = new();

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
    /// 确保指定状态类型的 jsonb 文档表与表达式索引就绪（幂等，进程内每类型仅执行一次；冷缓存并发首调单飞合并为一次 DDL）。
    /// </summary>
    /// <remarks>
    /// Ensures the jsonb document table and its expression indexes exist for the given state type
    /// (idempotent; executed once per type per process, mirroring the Mongo adapter's index-on-first-collection-access).
    /// Single-flight: concurrent cold-cache first calls publish one <see cref="Lazy{T}"/> DDL task via
    /// <c>GetOrAdd</c> and all await it; if that task faults or is canceled, the entry is evicted so a
    /// later call retries the DDL instead of caching the failure forever.
    /// <para>
    /// Mongo 免费赠送的集合自动创建与索引同步在 PG 侧由本方法自建（C166）：DDL 为
    /// <c>CREATE TABLE IF NOT EXISTS (id bigint PRIMARY KEY, doc jsonb NOT NULL)</c>；<c>[EntityIndexAttribute]</c>
    /// 映射为 jsonb 表达式索引，表达式与查询翻译器生成的谓词逐字一致（保证可走索引）。
    /// </para>
    /// </remarks>
    /// <typeparam name="TState">文档类型 / Document type</typeparam>
    /// <param name="connection">活动连接 / Active connection</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    private async Task EnsureTableAsync<TState>(NpgsqlConnection connection, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        var entityType = typeof(TState);
        var entry = _schemaCache.GetOrAdd(entityType.Name, _ => new Lazy<Task>(() => EnsureTableCoreAsync<TState>(connection, cancellationToken), LazyThreadSafetyMode.ExecutionAndPublication));
        try
        {
            await entry.Value.ConfigureAwait(false);
        }
        catch
        {
            // 单飞任务失败（faulted/canceled）：仅当缓存仍是本条目时逐出，允许后续调用重新发起 DDL
            // The single-flight task failed (faulted/canceled): evict only if the cache still holds this exact entry, allowing later calls to retry the DDL.
            _schemaCache.TryRemove(new KeyValuePair<string, Lazy<Task>>(entityType.Name, entry));
            throw;
        }
    }

    /// <summary>
    /// 执行建表与索引同步的 DDL 核心（由 <see cref="EnsureTableAsync"/> 单飞发布，仅首调连接上运行一次）。
    /// </summary>
    /// <remarks>
    /// Runs the table-creation and index-synchronization DDL core; published single-flight by
    /// <see cref="EnsureTableAsync"/> and executed exactly once on the first caller's connection.
    /// All DDL statements are idempotent (<c>IF NOT EXISTS</c> / drop-and-recreate on shape mismatch),
    /// so coalescing concurrent callers onto the first caller's connection is safe.
    /// </remarks>
    /// <typeparam name="TState">文档类型 / Document type</typeparam>
    /// <param name="connection">首调传入的活动连接 / The active connection supplied by the first caller</param>
    /// <param name="cancellationToken">首调传入的取消令牌 / The cancellation token supplied by the first caller</param>
    private static async Task EnsureTableCoreAsync<TState>(NpgsqlConnection connection, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        var entityType = typeof(TState);
        var tableName = GetTableName<TState>();
        await ExecuteNonQueryAsync(connection, $"CREATE TABLE IF NOT EXISTS {tableName} (id bigint PRIMARY KEY, doc jsonb NOT NULL)", cancellationToken).ConfigureAwait(false);
        await EnsureIndexesAsync(connection, tableName, entityType, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 同步指定实体类型的表达式索引（同名异构时重建，对齐 Mongo 索引一致性检查语义）。
    /// </summary>
    /// <remarks>
    /// Synchronizes the expression indexes for the given entity type (rebuilding on same-name-different-shape
    /// mismatches, mirroring the Mongo adapter's index consistency check).
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

            var expression = BuildIndexExpression(property.Name, property.PropertyType, attribute.IsAscending);
            var quotedIndexName = QuoteIdentifier(indexName);
            existingIndexes.TryGetValue(indexName, out var existingDefinition);
            if (existingDefinition != null)
            {
                var existingIsUnique = existingDefinition.StartsWith("CREATE UNIQUE", StringComparison.OrdinalIgnoreCase);
                if (existingIsUnique == attribute.Unique)
                {
                    continue;
                }

                // 同名异构（唯一性冲突）：重建以对齐实体声明（对齐 Mongo AreIndexesConsistent 失败即重建的意图）
                // Same name but different shape (unique mismatch): rebuild to match the entity declaration (mirroring Mongo's recreate-on-inconsistent behavior).
                await ExecuteNonQueryAsync(connection, $"DROP INDEX IF EXISTS {quotedIndexName}", cancellationToken).ConfigureAwait(false);
            }

            var uniquePrefix = attribute.Unique ? "UNIQUE " : string.Empty;
            await ExecuteNonQueryAsync(connection, $"CREATE {uniquePrefix}INDEX IF NOT EXISTS {quotedIndexName} ON {tableName} ({expression})", cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 查询表上现有索引定义（索引名 → indexdef）。
    /// </summary>
    /// <remarks>
    /// Lists the index definitions on the table (index name to indexdef).
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
    /// 构建 jsonb 表达式索引片段（与查询翻译器谓词逐字一致）。
    /// </summary>
    /// <remarks>
    /// Builds the jsonb expression index fragment (byte-for-byte identical to the predicates produced by the
    /// query translator, so the planner can match indexes).
    /// </remarks>
    /// <param name="propertyName">属性名 / Property name</param>
    /// <param name="propertyType">属性 CLR 类型 / Property CLR type</param>
    /// <param name="isAscending">是否升序 / Whether ascending</param>
    /// <returns>索引表达式 / The index expression</returns>
    internal static string BuildIndexExpression(string propertyName, Type propertyType, bool isAscending)
    {
        return $"({PostgreSqlJsonbAccess.GetTypedAccessor(propertyName, propertyType)} {(isAscending ? "ASC" : "DESC")})";
    }

    /// <summary>
    /// 在活动连接上执行无结果命令。
    /// </summary>
    /// <remarks>
    /// Executes a no-result command on the active connection.
    /// </remarks>
    private static async Task<int> ExecuteNonQueryAsync(NpgsqlConnection connection, string commandText, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(commandText, connection);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 创建 jsonb 参数。
    /// </summary>
    /// <remarks>
    /// Creates a jsonb parameter.
    /// </remarks>
    private static NpgsqlParameter CreateJsonParameter(string parameterName, string json)
    {
        return new NpgsqlParameter(parameterName, NpgsqlDbType.Jsonb) { Value = json, };
    }
}