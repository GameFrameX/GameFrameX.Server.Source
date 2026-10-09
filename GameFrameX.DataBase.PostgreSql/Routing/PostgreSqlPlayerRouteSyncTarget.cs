// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规及开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes and liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using Npgsql;

namespace GameFrameX.NetWork.RemoteMessaging.Routing;

/// <summary>
/// 基于 PostgreSQL 控制库的 IPlayerRouteSyncTarget（C166 T8：与 <c>MongoPlayerRouteSyncTarget</c> 的 CAS 语义逐分支对齐）。
/// </summary>
/// <remarks>
/// The PostgreSQL-backed implementation of the player-route sync hook (C166 T8),
/// a branch-by-branch parallel of <c>MongoPlayerRouteSyncTarget</c> over
/// the <c>player_route</c> table. Each upsert is a guarded UPDATE (compare-and-swap
/// on <c>version = supplied - 1</c>); a miss re-reads the latest row and applies
/// the same three-branch policy: missing row + first login (version &lt;= 1)
/// inserts unconditionally (INSERT ... ON CONFLICT DO UPDATE, then re-judged);
/// missing row + version &gt; 1 returns silently (the next SetOnline retries); an
/// existing row whose version does not satisfy current+1 throws
/// <see cref="PlayerRouteStaleException"/> for the SessionManager hook to swallow.
/// </remarks>
public sealed class PostgreSqlPlayerRouteSyncTarget : IPlayerRouteSyncTarget
{
    /// <summary>
    /// player_route 表名（D18 全名约定；稳定契约）。
    /// </summary>
    /// <remarks>
    /// The player_route table name (D18 no-abbreviation rule; a stable contract).
    /// </remarks>
    public const string TableName = "player_route";

    /// <summary>
    /// 建表与索引 DDL（幂等；player_id 主键天然唯一，role 索引供解析侧扫描）。
    /// </summary>
    /// <remarks>
    /// The idempotent schema DDL (the player_id primary key makes upserts idempotent; the role index serves resolver scans).
    /// </remarks>
    internal const string EnsurePlayerRouteSchemaSql = @"
CREATE TABLE IF NOT EXISTS player_route (
    player_id bigint PRIMARY KEY,
    instance_id text NOT NULL,
    role text NOT NULL,
    version bigint NOT NULL,
    last_seen_at timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_player_route_role ON player_route (role);";

    /// <summary>
    /// CAS 更新（$5 = version - 1 的版本守卫）。
    /// </summary>
    /// <remarks>
    /// The CAS update ($5 carries the version guard: supplied version minus one).
    /// </remarks>
    private const string CasUpdateSql = @"
UPDATE player_route
SET instance_id = $2, role = $3, version = $4, last_seen_at = now()
WHERE player_id = $1 AND version = $5;";

    /// <summary>
    /// 回读最新行。
    /// </summary>
    /// <remarks>
    /// Re-reads the latest row (instance id, role, version) after a CAS miss.
    /// </remarks>
    private const string SelectLatestSql = "SELECT instance_id, role, version FROM player_route WHERE player_id = $1;";

    /// <summary>
    /// 首登无条件插入（与 Mongo ReplaceOneAsync(IsUpsert) 的 last-writer-wins 对齐，冲突时后写者覆盖）。
    /// </summary>
    /// <remarks>
    /// The unconditional first-login insert, aligned with the Mongo ReplaceOneAsync(IsUpsert)
    /// last-writer-wins semantics (ON CONFLICT DO UPDATE lets the later writer win).
    /// </remarks>
    private const string InsertFirstLoginSql = @"
INSERT INTO player_route (player_id, instance_id, role, version, last_seen_at)
VALUES ($1, $2, $3, 1, now())
ON CONFLICT (player_id) DO UPDATE SET instance_id = EXCLUDED.instance_id, role = EXCLUDED.role, version = EXCLUDED.version, last_seen_at = EXCLUDED.last_seen_at;";

    /// <summary>
    /// 删除行（幂等）。
    /// </summary>
    /// <remarks>
    /// The idempotent delete.
    /// </remarks>
    private const string DeleteSql = "DELETE FROM player_route WHERE player_id = $1;";

    /// <summary>
    /// 数据源（池化）。
    /// </summary>
    /// <remarks>
    /// The pooled data source.
    /// </remarks>
    private readonly NpgsqlDataSource _dataSource;

    /// <summary>
    /// 初始化基于 PostgreSQL 控制库的同步目标。
    /// </summary>
    /// <remarks>
    /// Initializes the PostgreSQL sync target. Schema creation is the bootstrap's
    /// responsibility; the sync target only writes.
    /// </remarks>
    /// <param name="dataSource">控制库数据源 / The control-database data source</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="dataSource"/> 为 null 时抛出 / Thrown when <paramref name="dataSource"/> is null</exception>
    public PostgreSqlPlayerRouteSyncTarget(NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource, nameof(dataSource));
        _dataSource = dataSource;
    }

    /// <summary>
    /// 以 version CAS 语义把玩家路由原子写入控制库 player_route 表。
    /// </summary>
    /// <remarks>
    /// Atomically writes the player route into the <c>player_route</c> table. The
    /// CAS guard requires the persisted version to equal the supplied version
    /// minus one; on a miss the latest row is re-read and the Mongo-aligned
    /// branches apply (first-login insert / silent return / stale exception).
    /// </remarks>
    /// <param name="record">待写入的玩家路由记录 / The player-route record to write</param>
    /// <returns>异步任务 / Async task</returns>
    /// <exception cref="ArgumentException">当 <paramref name="record"/> 为 null 或 <c>InstanceId</c> 为空白时抛出 / Thrown when record is null or InstanceId is blank</exception>
    /// <exception cref="PlayerRouteStaleException">当控制库中的版本已不满足 current+1 时抛出 / Thrown when the persisted version no longer satisfies current+1</exception>
    public async Task UpsertAsync(PlayerRouteRecord record)
    {
        ArgumentNullException.ThrowIfNull(record, nameof(record));

        var playerId = record.PlayerId;
        var instanceId = record.InstanceId;
        var role = record.Role;
        var version = record.Version;

        if (playerId <= 0)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(instanceId))
        {
            throw new ArgumentException("Instance id must not be empty.", nameof(record));
        }

        // 统一 CAS：送入 version 必须等于当前 version + 1（旧行 version == version-1）。
        // Unified CAS: the supplied version must equal the current version + 1 (the old row version == version-1).
        await using (var updateCommand = _dataSource.CreateCommand(CasUpdateSql))
        {
            updateCommand.Parameters.AddWithValue(playerId);
            updateCommand.Parameters.AddWithValue(instanceId);
            updateCommand.Parameters.AddWithValue(role ?? string.Empty);
            updateCommand.Parameters.AddWithValue(version);
            updateCommand.Parameters.AddWithValue(version - 1);
            if (await updateCommand.ExecuteNonQueryAsync().ConfigureAwait(false) == 1)
            {
                return;
            }
        }

        // 未命中：可能行被删（首登/重试），可能版本竞争失败。读最新行区分处理。
        // No match: the row is missing or the version guard failed; read the latest.
        await using (var selectCommand = _dataSource.CreateCommand(SelectLatestSql))
        {
            selectCommand.Parameters.AddWithValue(playerId);
            await using var reader = await selectCommand.ExecuteReaderAsync().ConfigureAwait(false);
            if (await reader.ReadAsync().ConfigureAwait(false))
            {
                var currentVersion = reader.GetInt64(2);
                throw new PlayerRouteStaleException(playerId, version, currentVersion);
            }
        }

        if (version <= 1)
        {
            // 首登（version<=1 且行缺失）无条件插入；并发首登与 Mongo ReplaceOneAsync(IsUpsert) 的 last-writer-wins 对齐（后写者胜出）。
            // First login (missing row with version<=1) inserts unconditionally; concurrent first logins follow the Mongo ReplaceOneAsync(IsUpsert) last-writer-wins policy (the later writer wins).
            await using var insertCommand = _dataSource.CreateCommand(InsertFirstLoginSql);
            insertCommand.Parameters.AddWithValue(playerId);
            insertCommand.Parameters.AddWithValue(instanceId);
            insertCommand.Parameters.AddWithValue(role ?? string.Empty);
            await insertCommand.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        // 行缺失且 version>1：静默返回（下一轮 SetOnline 重试）。
        // Missing row with version>1: return silently (the next SetOnline retries).
    }

    /// <summary>
    /// 从控制库 player_route 表删除该玩家的路由行。
    /// </summary>
    /// <remarks>
    /// Deletes the player's route row; non-positive ids are ignored and a missing
    /// row is not an error (idempotent semantics).
    /// </remarks>
    /// <param name="playerId">玩家 ID / Player id</param>
    /// <returns>异步任务 / Async task</returns>
    public async Task DeleteAsync(long playerId)
    {
        if (playerId <= 0)
        {
            return;
        }

        await using var command = _dataSource.CreateCommand(DeleteSql);
        command.Parameters.AddWithValue(playerId);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// 幂等建表 / 建索引（供装配层与测试复用）。
    /// </summary>
    /// <remarks>
    /// Idempotently creates the player_route table and its role index.
    /// </remarks>
    /// <param name="dataSource">数据源 / The data source</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>异步任务 / Async task</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="dataSource"/> 为 null 时抛出 / Thrown when <paramref name="dataSource"/> is null</exception>
    public static Task EnsureSchemaAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource, nameof(dataSource));
        return PostgreSqlEndpointRegistry.ExecuteNonQueryAsync(dataSource, EnsurePlayerRouteSchemaSql, cancellationToken);
    }
}