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

namespace GameFrameX.NetWork.RemoteMessaging.Discovery;

/// <summary>
/// PostgreSQL 心跳存储适配（C167：<see cref="IHeartbeatStore"/> 的参数化 SQL 实现）。
/// </summary>
/// <remarks>
/// The PostgreSQL implementation of the heartbeat storage seam (C167): the
/// parameterized-SQL counterpart consumed by the generic
/// <see cref="DiscoveryRegistry"/> / <see cref="DiscoveryWatcher"/>. PostgreSQL
/// has no native TTL index, so <see cref="DeleteExpiredAsync"/> executes the
/// real DELETE — driven by the registry's cleanup loop, with removal relaxed
/// to within one cleanup period (the watcher's three-period staleness check
/// is the primary liveness signal and never depends on row disappearance).
/// </remarks>
public sealed class PostgreSqlHeartbeatStore : IHeartbeatStore
{
    /// <summary>
    /// 建表与索引 DDL（幂等：CREATE TABLE / INDEX IF NOT EXISTS）。
    /// </summary>
    /// <remarks>
    /// The idempotent schema DDL for the heartbeat table (plus the last_heartbeat index used by the cleanup scans).
    /// </remarks>
    internal const string EnsureHeartbeatSchemaSql = @"
CREATE TABLE IF NOT EXISTS server_heartbeat (
    instance_id text PRIMARY KEY,
    role text NOT NULL,
    advertise_endpoint text NOT NULL,
    status text NOT NULL,
    load integer NOT NULL,
    address_kind text NOT NULL,
    incarnation bigint NOT NULL,
    last_heartbeat timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_server_heartbeat_last_heartbeat ON server_heartbeat (last_heartbeat);";

    /// <summary>
    /// 全行 upsert（ON CONFLICT (instance_id) DO UPDATE）。
    /// </summary>
    /// <remarks>
    /// The full-row upsert (ON CONFLICT (instance_id) DO UPDATE).
    /// </remarks>
    private const string UpsertHeartbeatSql = @"
INSERT INTO server_heartbeat (instance_id, role, advertise_endpoint, status, load, address_kind, incarnation, last_heartbeat)
VALUES ($1, $2, $3, $4, $5, $6, $7, $8)
ON CONFLICT (instance_id) DO UPDATE SET
    role = EXCLUDED.role,
    advertise_endpoint = EXCLUDED.advertise_endpoint,
    status = EXCLUDED.status,
    load = EXCLUDED.load,
    address_kind = EXCLUDED.address_kind,
    incarnation = EXCLUDED.incarnation,
    last_heartbeat = EXCLUDED.last_heartbeat;";

    /// <summary>
    /// 全量读取。
    /// </summary>
    /// <remarks>
    /// The full-table read.
    /// </remarks>
    private const string QueryAllSql = "SELECT instance_id, role, advertise_endpoint, status, load, address_kind, incarnation, last_heartbeat FROM server_heartbeat;";

    /// <summary>
    /// 过期行删除（截止时间参数化）。
    /// </summary>
    /// <remarks>
    /// The expired-row delete (cutoff passed as a parameter).
    /// </remarks>
    private const string DeleteExpiredSql = "DELETE FROM server_heartbeat WHERE last_heartbeat < $1;";

    /// <summary>
    /// 数据源（池化）。
    /// </summary>
    /// <remarks>
    /// The pooled data source.
    /// </remarks>
    private readonly NpgsqlDataSource _dataSource;

    /// <summary>
    /// 初始化 PostgreSQL 心跳存储适配。
    /// </summary>
    /// <remarks>
    /// Initializes the store. Call <see cref="EnsureSchemaAsync"/> before the
    /// first poll (PostgreSQL never creates missing relations lazily).
    /// </remarks>
    /// <param name="dataSource">控制库数据源 / The control-database data source</param>
    public PostgreSqlHeartbeatStore(NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource, nameof(dataSource));
        _dataSource = dataSource;
    }

    /// <summary>
    /// 幂等建表 / 建索引（供装配层与测试复用）。
    /// </summary>
    /// <remarks>
    /// Idempotently creates the heartbeat table and its index.
    /// </remarks>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>异步任务 / Async task</returns>
    public Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        return ExecuteNonQueryAsync(EnsureHeartbeatSchemaSql, cancellationToken);
    }

    /// <summary>
    /// 全行 upsert 一条心跳（写入时刻打 last_heartbeat 戳）。
    /// </summary>
    /// <remarks>
    /// Upserts the full heartbeat row, stamped with the current UTC time.
    /// </remarks>
    /// <param name="instance">实例描述符 / The instance descriptor</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>异步任务 / Async task</returns>
    public async Task UpsertAsync(InstanceDescriptor instance, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance, nameof(instance));

        await using var command = _dataSource.CreateCommand(UpsertHeartbeatSql);
        command.Parameters.AddWithValue(instance.InstanceId);
        command.Parameters.AddWithValue(instance.Role);
        command.Parameters.AddWithValue(instance.AdvertiseEndpoint);
        command.Parameters.AddWithValue(instance.Status.ToString());
        command.Parameters.AddWithValue(instance.Load);
        command.Parameters.AddWithValue(instance.AddressKind.ToString());
        command.Parameters.AddWithValue(instance.Incarnation);
        command.Parameters.AddWithValue(DateTime.UtcNow);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 拉取全部心跳行（防御性解析：无法解析的行被静默跳过）。
    /// </summary>
    /// <remarks>
    /// Queries every heartbeat row; rows with unknown enum names or blank
    /// fields (defensive against a different version's writes) are skipped.
    /// </remarks>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>全部可解析的实例描述符 / Every parsable instance descriptor</returns>
    public async Task<IReadOnlyList<InstanceDescriptor>> QueryAllAsync(CancellationToken cancellationToken = default)
    {
        var descriptors = new List<InstanceDescriptor>();
        await using var command = _dataSource.CreateCommand(QueryAllSql);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var descriptor = TryToDescriptor(reader);
            if (descriptor != null)
            {
                descriptors.Add(descriptor);
            }
        }

        return descriptors;
    }

    /// <summary>
    /// 删除过期心跳行（DELETE ... WHERE last_heartbeat &lt; 截止时间）。
    /// </summary>
    /// <remarks>
    /// Deletes heartbeat rows older than the cutoff (now minus the supplied
    /// window). Driven by the registry's cleanup loop; removal is relaxed to
    /// within one cleanup period.
    /// </remarks>
    /// <param name="heartbeatTimeToLive">心跳保存窗口 / The expire-after window</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>异步任务 / Async task</returns>
    public async Task DeleteExpiredAsync(TimeSpan heartbeatTimeToLive, CancellationToken cancellationToken = default)
    {
        await using var command = _dataSource.CreateCommand(DeleteExpiredSql);
        command.Parameters.AddWithValue(DateTime.UtcNow - heartbeatTimeToLive);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 执行无参 DDL / SQL。
    /// </summary>
    /// <remarks>
    /// Executes a parameter-less DDL statement.
    /// </remarks>
    internal async Task ExecuteNonQueryAsync(string sql, CancellationToken cancellationToken)
    {
        await using var command = _dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 行 → 实例描述符（未知枚举名或空白字段返回 null，防御旧版本行）。
    /// </summary>
    /// <remarks>
    /// Converts a row to a descriptor; returns null on an unknown enum name or blank field.
    /// </remarks>
    /// <param name="reader">数据读取器 / The data reader</param>
    /// <returns>实例描述符；无法转换时为 null / The descriptor, or null when unparsable</returns>
    private static InstanceDescriptor TryToDescriptor(Npgsql.NpgsqlDataReader reader)
    {
        var instanceId = reader.GetString(0);
        var role = reader.GetString(1);
        var advertiseEndpoint = reader.GetString(2);
        if (string.IsNullOrWhiteSpace(instanceId) || string.IsNullOrWhiteSpace(role) || string.IsNullOrWhiteSpace(advertiseEndpoint))
        {
            return null;
        }

        if (!Enum.TryParse<InstanceStatus>(reader.GetString(3), false, out var status) || !Enum.TryParse<EndpointAddressKind>(reader.GetString(5), false, out var addressKind))
        {
            return null;
        }

        return new InstanceDescriptor(role, instanceId, advertiseEndpoint, status, reader.GetInt32(4), addressKind, reader.GetInt64(6), reader.GetDateTime(7).ToUniversalTime());
    }
}
