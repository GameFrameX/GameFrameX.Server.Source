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
using Npgsql;

namespace GameFrameX.DataBase.PostgreSql;

public sealed partial class PostgreSqlDbService
{
    /// <summary>
    /// 更新时保留的存储字段（对齐 Mongo BuildUpdateDefinition 的排除集：Id/CreatedTime/CreatedId/IsDeleted/DeleteTime）。
    /// </summary>
    /// <remarks>
    /// Storage-side fields preserved on update — mirroring Mongo's <c>$set</c> exclusion set.
    /// PG jsonb 整文档替换通过 <c>(doc - 'Id') || (newDoc - 保留字段)</c> 合成：存储侧保留字段优先，其余以新文档为准。
    /// </remarks>
    private static readonly string[] PreservedOnUpdateFields =
    {
        nameof(BaseCacheState.Id),
        nameof(BaseCacheState.CreatedTime),
        nameof(BaseCacheState.CreatedId),
        nameof(BaseCacheState.IsDeleted),
        nameof(BaseCacheState.DeleteTime),
    };

    /// <summary>
    /// 保存数据（仅当 StateHash 判定已修改才写库；更新保留字段语义见 <see cref="PreservedOnUpdateFields"/>）。
    /// </summary>
    public async Task<TState> UpdateAsync<TState>(TState state) where TState : BaseCacheState, new()
    {
        return await UpdateAsync(state, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 保存数据（仅当 StateHash 判定已修改才写库；更新保留字段语义见 <see cref="PreservedOnUpdateFields"/>）。
    /// </summary>
    public async Task<TState> UpdateAsync<TState>(TState state, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();
        var isChanged = state.IsModify();
        if (isChanged)
        {
            state.UpdateTime = GetCurrentTimestamp();
            state.UpdateCount = (state.UpdateCount ?? 0) + 1;

            var (sql, parameters) = BuildUpdatePreservingSql<TState>(state);
            var result = await ExecuteWriteWithRetryAsync(async token =>
            {
                var rowCount = await ExecuteWriteCommandAsync<TState>(sql, parameters, token).ConfigureAwait(false);
                return rowCount;
            }, cancellationToken, nameof(UpdateAsync), true).ConfigureAwait(false);
            if (result > 0)
            {
                state.SaveToDbPostHandler();
            }
        }

        return state;
    }

    /// <summary>
    /// 保存多条数据（返回实际更新行数；未变更项跳过，对齐 Mongo BulkWrite 语义）。
    /// </summary>
    public async Task<long> UpdateAsync<TState>(IEnumerable<TState> stateList) where TState : BaseCacheState, new()
    {
        return await UpdateAsync(stateList, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 保存多条数据（返回实际更新行数；未变更项跳过，对齐 Mongo BulkWrite 语义）。
    /// </summary>
    public async Task<long> UpdateAsync<TState>(IEnumerable<TState> stateList, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();
        var cacheStates = stateList as TState[] ?? stateList?.ToArray();
        if (cacheStates == null || cacheStates.Length == 0)
        {
            return 0;
        }

        var changedStates = new List<TState>(cacheStates.Length);
        var currentTime = GetCurrentTimestamp();
        foreach (var state in cacheStates)
        {
            if (state.IsModify())
            {
                state.UpdateTime = currentTime;
                state.UpdateCount = (state.UpdateCount ?? 0) + 1;
                changedStates.Add(state);
            }
        }

        if (changedStates.Count == 0)
        {
            return 0;
        }

        long modifiedCount = 0;
        await ExecuteWriteWithRetryAsync(async token =>
        {
            modifiedCount = 0;
            await using var connection = DataSource.CreateConnection();
            await connection.OpenAsync(token).ConfigureAwait(false);
            await EnsureTableAsync<TState>(connection, token).ConfigureAwait(false);
            foreach (var state in changedStates)
            {
                var (sql, parameters) = BuildUpdatePreservingSql<TState>(state);
                await using var command = new NpgsqlCommand(sql, connection);
                foreach (var parameter in parameters)
                {
                    // Clone：参数列表可能跨命令/重试复用（NpgsqlParameter 绑定后不可再入集合）
                command.Parameters.Add(parameter.Clone());
                }

                modifiedCount += await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            }

            return true;
        }, cancellationToken, nameof(UpdateAsync), true).ConfigureAwait(false);
        if (modifiedCount > 0)
        {
            foreach (var state in cacheStates)
            {
                state.SaveToDbPostHandler();
            }
        }

        return changedStates.Count;
    }

    /// <summary>
    /// 根据ID部分更新数据（服务器端 jsonb 合并 + UpdateTime 写入 + UpdateCount 自增，单语句原子）。
    /// </summary>
    /// <remarks>
    /// Partially updates by ID (server-side jsonb merge + UpdateTime write + UpdateCount increment, atomic in one statement).
    /// 对齐 Mongo 语义：null 值字段移除（$unset）、非 null 字段写入（$set）；Id/CreatedTime/CreatedId 不可更新；
    /// 过滤附带软删默认过滤；返回值对齐 ModifiedCount（值未变不计行）。
    /// </remarks>
    public async Task<long> UpdatePartialAsync<TState>(long id, IReadOnlyDictionary<string, object> updateFields) where TState : BaseCacheState, new()
    {
        return await UpdatePartialAsync<TState>(id, updateFields, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 根据ID部分更新数据（服务器端 jsonb 合并 + UpdateTime 写入 + UpdateCount 自增，单语句原子）。
    /// </summary>
    public async Task<long> UpdatePartialAsync<TState>(long id, IReadOnlyDictionary<string, object> updateFields, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();
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

        var tableName = GetTableName<TState>();
        var setsJson = PostgreSqlJsonDocumentSerializer.SerializeFields(setFields);
        var sql = $@"
WITH newdoc AS (
    SELECT t.id AS id, jsonb_set(jsonb_set(
        (t.doc || @sets::jsonb) - @removedKeys::text[],
        '{{UpdateTime}}', to_jsonb(@updateTime::bigint)),
        '{{UpdateCount}}', to_jsonb(COALESCE(NULLIF(t.doc->>'UpdateCount','')::bigint, 0) + 1)) AS d
    FROM {tableName} t
    WHERE t.id = @id AND ({PostgreSqlJsonbAccess.SoftDeleteFilter})
)
UPDATE {tableName} t SET doc = newdoc.d
FROM newdoc
WHERE t.id = newdoc.id AND t.doc IS DISTINCT FROM newdoc.d";
        var parameters = new List<NpgsqlParameter>
        {
            new("id", id),
            CreateJsonParameter("sets", setsJson),
            new("removedKeys", removedKeys.ToArray()),
            new("updateTime", GetCurrentTimestamp()),
        };
        var rowCount = await ExecuteWriteWithRetryAsync(async token => await ExecuteWriteCommandAsync<TState>(sql, parameters, token).ConfigureAwait(false), cancellationToken, nameof(UpdatePartialAsync), false).ConfigureAwait(false);
        return rowCount;
    }

    /// <summary>
    /// 构建保留字段的更新语句（存储侧 Id/CreatedTime/CreatedId/IsDeleted/DeleteTime 优先，值未变不计行）。
    /// </summary>
    private static (string Sql, List<NpgsqlParameter> Parameters) BuildUpdatePreservingSql<TState>(TState state) where TState : BaseCacheState, new()
    {
        var newDocJson = PostgreSqlJsonDocumentSerializer.Serialize(state);
        // 根因修复（notes.md C166）：join(",") 会把键列表拼成 row 构造器（record），PG 报 jsonb || record；
        // 正确形态是 jsonb - text[] 数组删键（键为编译期常量，无注入面）。
        var removeKeysArray = "ARRAY[" + string.Join(", ", PreservedOnUpdateFields.Skip(1).Select(static field => $"'{field}'")) + "]";
        var sql = $"UPDATE {GetTableName<TState>()} SET doc = (doc - 'Id') || (@doc::jsonb - {removeKeysArray}) WHERE id = @id AND doc IS DISTINCT FROM ((doc - 'Id') || (@doc::jsonb - {removeKeysArray}))";
        var parameters = new List<NpgsqlParameter>
        {
            new("id", state.Id),
            CreateJsonParameter("doc", newDocJson),
        };
        return (sql, parameters);
    }

    /// <summary>
    /// 执行写命令（含表结构确保），返回受影响行数。
    /// </summary>
    private async Task<int> ExecuteWriteCommandAsync<TState>(string sql, IReadOnlyList<NpgsqlParameter> parameters, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        await using var connection = DataSource.CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnsureTableAsync<TState>(connection, cancellationToken).ConfigureAwait(false);
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var parameter in parameters)
        {
            // Clone：参数列表可能跨命令/重试复用（NpgsqlParameter 绑定后不可再入集合）
                command.Parameters.Add(parameter.Clone());
        }

        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
