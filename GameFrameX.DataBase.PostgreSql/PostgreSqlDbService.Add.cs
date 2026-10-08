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
    #region 插入

    /// <summary>
    /// 增加一条数据（适配器补齐时间戳与更新计数，对齐 Mongo）。
    /// </summary>
    public async Task AddAsync<TState>(TState state) where TState : BaseCacheState, new()
    {
        await AddAsync(state, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 增加一条数据（适配器补齐时间戳与更新计数，对齐 Mongo）。
    /// </summary>
    public async Task AddAsync<TState>(TState state, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();
        var currentTime = GetCurrentTimestamp();
        state.CreatedTime = currentTime;
        state.UpdateTime = currentTime;
        state.UpdateCount ??= 0;
        var sql = $"INSERT INTO {GetTableName<TState>()} (id, doc) VALUES (@id, @doc)";
        await ExecuteWriteWithRetryAsync(async token =>
        {
            await using var connection = DataSource.CreateConnection();
            await connection.OpenAsync(token).ConfigureAwait(false);
            await EnsureTableAsync<TState>(connection, token).ConfigureAwait(false);
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.Add(new NpgsqlParameter("id", state.Id));
            command.Parameters.Add(CreateJsonParameter("doc", PostgreSqlJsonDocumentSerializer.Serialize(state)));
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            return true;
        }, cancellationToken, nameof(AddAsync), false).ConfigureAwait(false);
    }

    /// <summary>
    /// 增加一个列表数据（单语句多值插入，整体原子；空集合直接返回，对齐 Mongo）。
    /// </summary>
    public async Task AddListAsync<TState>(IEnumerable<TState> states) where TState : BaseCacheState, new()
    {
        await AddListAsync(states, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// 增加一个列表数据（单语句多值插入，整体原子；空集合直接返回，对齐 Mongo）。
    /// </summary>
    public async Task AddListAsync<TState>(IEnumerable<TState> states, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureInitialized();
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

        await ExecuteWriteWithRetryAsync(async token =>
        {
            await using var connection = DataSource.CreateConnection();
            await connection.OpenAsync(token).ConfigureAwait(false);
            await EnsureTableAsync<TState>(connection, token).ConfigureAwait(false);
            await using var command = BuildInsertManyCommand<TState>(connection, cacheStates);
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            return true;
        }, cancellationToken, nameof(AddListAsync), false).ConfigureAwait(false);
    }

    /// <summary>
    /// 构建多值插入命令（INSERT INTO ... VALUES (...), (...)）。
    /// </summary>
    private static NpgsqlCommand BuildInsertManyCommand<TState>(NpgsqlConnection connection, IReadOnlyList<TState> states) where TState : BaseCacheState, new()
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

        command.CommandText = $"INSERT INTO {GetTableName<TState>()} (id, doc) VALUES {string.Join(", ", valueFragments)}";
        return command;
    }

    #endregion 插入
}
