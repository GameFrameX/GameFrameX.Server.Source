// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes and liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository:  https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:   https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:     https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using Npgsql;

namespace GameFrameX.NetWork.RemoteMessaging.Routing;

/// <summary>
/// PostgreSQL 玩家路由存储适配（C167：<see cref="IPlayerRouteStore"/> 的参数化 SQL 实现）。
/// </summary>
/// <remarks>
/// The PostgreSQL implementation of the player-route storage seam (C167): the
/// parameterized-SQL counterpart consumed by the generic resolver / sync
/// target / bootstrap. <see cref="DeleteExpiredAsync"/> executes the real
/// DELETE (the 30-day offline garbage window) — driven by the registry's
/// cleanup loop in place of the former standalone TTL cleanup job.
/// </remarks>
public sealed class PostgreSqlPlayerRouteStore : IPlayerRouteStore
{
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
    /// 单行读取。
    /// </summary>
    /// <remarks>
    /// The single-row read.
    /// </remarks>
    private const string SelectSql = "SELECT instance_id, role, version FROM player_route WHERE player_id = $1;";

    /// <summary>
    /// CAS 更新（$4 = version - 1 的版本守卫）。
    /// </summary>
    /// <remarks>
    /// The CAS update ($4 carries the version guard: supplied version minus one).
    /// </remarks>
    private const string CasUpdateSql = @"
UPDATE player_route
SET instance_id = $2, role = $3, version = $4, last_seen_at = now()
WHERE player_id = $1 AND version = $5;";

    /// <summary>
    /// 首登无条件插入（ON CONFLICT DO UPDATE，后写者胜出）。
    /// </summary>
    /// <remarks>
    /// The unconditional first-login insert, aligned with the Mongo ReplaceOne(IsUpsert)
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
    /// 过期行删除（截止时间参数化）。
    /// </summary>
    /// <remarks>
    /// The expired-row delete (cutoff passed as a parameter).
    /// </remarks>
    private const string DeleteExpiredSql = "DELETE FROM player_route WHERE last_seen_at < $1;";

    /// <summary>
    /// 数据源（池化）。
    /// </summary>
    /// <remarks>
    /// The pooled data source.
    /// </remarks>
    private readonly NpgsqlDataSource _dataSource;

    /// <summary>
    /// 初始化 PostgreSQL 玩家路由存储适配。
    /// </summary>
    /// <remarks>
    /// Initializes the store. Schema creation happens in <see cref="EnsureSchemaAsync"/>.
    /// </remarks>
    /// <param name="dataSource">控制库数据源 / The control-database data source</param>
    public PostgreSqlPlayerRouteStore(NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource, nameof(dataSource));
        _dataSource = dataSource;
    }

    /// <summary>
    /// 幂等建表 / 建索引。
    /// </summary>
    /// <remarks>
    /// Idempotently creates the player_route table and its role index.
    /// </remarks>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>异步任务 / Async task</returns>
    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        await using var command = _dataSource.CreateCommand(EnsurePlayerRouteSchemaSql);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 读取单个玩家的路由行。
    /// </summary>
    /// <remarks>
    /// Reads one player's route row; null when absent.
    /// </remarks>
    /// <param name="playerId">玩家 ID / The player id</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>路由记录；缺失时为 null / The record, or null when absent</returns>
    public async Task<PlayerRouteRecord> GetAsync(long playerId, CancellationToken cancellationToken = default)
    {
        await using var command = _dataSource.CreateCommand(SelectSql);
        command.Parameters.AddWithValue(playerId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new PlayerRouteRecord { PlayerId = playerId, InstanceId = reader.GetString(0), Role = reader.GetString(1), Version = reader.GetInt64(2) };
    }

    /// <summary>
    /// 以 version 守卫更新路由（version-1 命中；未命中回读区分 Missing / Stale）。
    /// </summary>
    /// <remarks>
    /// Attempts the guarded update (matching playerId and version =
    /// supplied − 1). On a miss the latest row is re-read to distinguish
    /// <see cref="PlayerRouteCasOutcome.Missing"/> from
    /// <see cref="PlayerRouteCasOutcome.Stale"/>; the branch policy itself
    /// lives in the generic sync target.
    /// </remarks>
    /// <param name="record">待写入的路由记录 / The record to write</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>CAS 结果 / The CAS outcome</returns>
    public async Task<PlayerRouteCasOutcome> CasUpsertAsync(PlayerRouteRecord record, CancellationToken cancellationToken = default)
    {
        await using (var updateCommand = _dataSource.CreateCommand(CasUpdateSql))
        {
            updateCommand.Parameters.AddWithValue(record.PlayerId);
            updateCommand.Parameters.AddWithValue(record.InstanceId);
            updateCommand.Parameters.AddWithValue(record.Role ?? string.Empty);
            updateCommand.Parameters.AddWithValue(record.Version);
            updateCommand.Parameters.AddWithValue(record.Version - 1);
            if (await updateCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1)
            {
                return PlayerRouteCasOutcome.Updated;
            }
        }

        // 未命中：可能行被删（首登/重试），可能版本竞争失败。读最新行区分处理。
        // No match: the row is missing or the version guard failed; read the latest.
        var latest = await GetAsync(record.PlayerId, cancellationToken).ConfigureAwait(false);
        return latest == null ? PlayerRouteCasOutcome.Missing : PlayerRouteCasOutcome.Stale;
    }

    /// <summary>
    /// 首登无条件写入（ON CONFLICT DO UPDATE，并发首登 last-writer-wins）。
    /// </summary>
    /// <remarks>
    /// Writes the first-login row unconditionally (version = 1; under concurrent
    /// first logins the later writer wins).
    /// </remarks>
    /// <param name="record">首登记录 / The first-login record</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>异步任务 / Async task</returns>
    public async Task InsertFirstLoginAsync(PlayerRouteRecord record, CancellationToken cancellationToken = default)
    {
        await using var command = _dataSource.CreateCommand(InsertFirstLoginSql);
        command.Parameters.AddWithValue(record.PlayerId);
        command.Parameters.AddWithValue(record.InstanceId);
        command.Parameters.AddWithValue(record.Role ?? string.Empty);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 删除该玩家的路由行（幂等）。
    /// </summary>
    /// <remarks>
    /// Deletes the player's route row (idempotent; a missing row is not an error).
    /// </remarks>
    /// <param name="playerId">玩家 ID / Player id</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>异步任务 / Async task</returns>
    public async Task DeleteAsync(long playerId, CancellationToken cancellationToken = default)
    {
        await using var command = _dataSource.CreateCommand(DeleteSql);
        command.Parameters.AddWithValue(playerId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 删除过期路由行（DELETE ... WHERE last_seen_at &lt; 截止时间；30 天窗口）。
    /// </summary>
    /// <remarks>
    /// Deletes route rows older than the cutoff (now minus the supplied
    /// window). Driven by the registry's cleanup loop; removal is relaxed to
    /// within one cleanup period.
    /// </remarks>
    /// <param name="timeToLive">路由保存窗口 / The expire-after window</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>异步任务 / Async task</returns>
    public async Task DeleteExpiredAsync(TimeSpan timeToLive, CancellationToken cancellationToken = default)
    {
        await using var command = _dataSource.CreateCommand(DeleteExpiredSql);
        command.Parameters.AddWithValue(DateTime.UtcNow - timeToLive);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
