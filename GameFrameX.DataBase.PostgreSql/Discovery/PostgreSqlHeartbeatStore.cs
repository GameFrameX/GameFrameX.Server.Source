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


using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GameFrameX.DataBase.PostgreSql.Discovery;

/// <summary>
/// PostgreSQL 心跳存储适配（C167 契约 / C168 EF 化：<see cref="IHeartbeatStore"/> 的 EF 关系实现，零手写 SQL）。
/// </summary>
/// <remarks>
/// The PostgreSQL implementation of the heartbeat storage seam (C167 contract, EF-based since C168 — zero
/// hand-written SQL) consumed by the generic <see cref="DiscoveryRegistry"/> / <see cref="DiscoveryWatcher"/>.
/// PostgreSQL has no native TTL index, so <see cref="DeleteExpiredAsync"/> executes the real delete —
/// driven by the registry's cleanup loop, with removal relaxed to within one cleanup period (the watcher's
/// three-period staleness check is the primary liveness signal and never depends on row disappearance).
/// </remarks>
public sealed class PostgreSqlHeartbeatStore : IHeartbeatStore
{
    /// <summary>
    /// 数据源（池化，与主服务共用）。
    /// </summary>
    /// <remarks>
    /// The pooled data source (shared with the main database service).
    /// </remarks>
    private readonly NpgsqlDataSource _dataSource;

    /// <summary>
    /// 上下文选项（绑定数据源，进程内惰性构建一次）。
    /// </summary>
    /// <remarks>
    /// The data-source-bound context options (lazily built once per store instance).
    /// </remarks>
    private DbContextOptions<PostgreSqlDiscoveryDbContext> _contextOptions;

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
    /// 幂等建表 / 建索引（EF 模型 DDL；表已存在视为成功，供装配层与测试复用）。
    /// </summary>
    /// <remarks>
    /// Idempotently creates the discovery schema (heartbeat + player-route tables and their indexes) through
    /// the EF model-generated DDL; pre-existing tables count as success. The model covers both discovery
    /// tables, so the first store's bootstrap creates the full control-schema shape.
    /// </remarks>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>异步任务 / Async task</returns>
    public async Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
    {
        using var context = CreateContext();
        var createScript = context.Database.GenerateCreateScript();
        try
        {
            await context.Database.ExecuteSqlRawAsync(createScript, cancellationToken).ConfigureAwait(false);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresDuplicateTableSqlState)
        {
            // 表已存在（存量控制库 / 并发装配）：视作成功，存量表结构零迁移。
            // Tables already exist (stored control database or concurrent bootstrap): success.
        }
    }

    /// <summary>
    /// 全行 upsert 一条心跳（写入时刻打 last_heartbeat 戳；加载-替换或新增，主键竞争自动重试一次）。
    /// </summary>
    /// <remarks>
    /// Upserts the full heartbeat row, stamped with the current UTC time (load-and-replace or insert; a
    /// primary-key race with a concurrent writer is retried once, converging on last-writer-wins).
    /// </remarks>
    /// <param name="instance">实例描述符 / The instance descriptor</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>异步任务 / Async task</returns>
    public async Task UpsertAsync(InstanceDescriptor instance, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance, nameof(instance));
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var context = CreateContext();
                var row = await context.Heartbeats.FirstOrDefaultAsync(row => row.InstanceId == instance.InstanceId, cancellationToken).ConfigureAwait(false);
                if (row == null)
                {
                    context.Heartbeats.Add(CreateRow(instance));
                }
                else
                {
                    CopyToRow(instance, row);
                }

                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (DbUpdateException exception) when (attempt == 0 && IsUniqueViolation(exception))
            {
                // 并发首写同 instance_id：重载后按替换收敛。
                // Concurrent first write of the same instance_id: reload and converge as a replacement.
            }
        }
    }

    /// <summary>
    /// 拉取全部心跳行（防御性解析：无法解析的行被静默跳过）。
    /// </summary>
    /// <remarks>
    /// Queries every heartbeat row; rows with unknown enum names or blank fields (defensive against a
    /// different version's writes) are skipped.
    /// </remarks>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>全部可解析的实例描述符 / Every parsable instance descriptor</returns>
    public async Task<IReadOnlyList<InstanceDescriptor>> QueryAllAsync(CancellationToken cancellationToken = default)
    {
        using var context = CreateContext();
        var rows = await context.Heartbeats.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        var descriptors = new List<InstanceDescriptor>(rows.Count);
        foreach (var row in rows)
        {
            var descriptor = TryToDescriptor(row);
            if (descriptor != null)
            {
                descriptors.Add(descriptor);
            }
        }

        return descriptors;
    }

    /// <summary>
    /// 删除过期心跳行（last_heartbeat &lt; 截止时间）。
    /// </summary>
    /// <remarks>
    /// Deletes heartbeat rows older than the cutoff (now minus the supplied window). Driven by the
    /// registry's cleanup loop; removal is relaxed to within one cleanup period.
    /// </remarks>
    /// <param name="heartbeatTimeToLive">心跳保存窗口 / The expire-after window</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>异步任务 / Async task</returns>
    public async Task DeleteExpiredAsync(TimeSpan heartbeatTimeToLive, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow - heartbeatTimeToLive;
        using var context = CreateContext();
        await context.Heartbeats.Where(row => row.LastHeartbeat < cutoff).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 创建 EF 上下文（选项惰性绑定数据源）。
    /// </summary>
    /// <remarks>
    /// Creates the EF context (options lazily bound to the data source).
    /// </remarks>
    /// <returns>EF 上下文 / The EF context</returns>
    private PostgreSqlDiscoveryDbContext CreateContext()
    {
        _contextOptions ??= new DbContextOptionsBuilder<PostgreSqlDiscoveryDbContext>().UseNpgsql(_dataSource).Options;
        return new PostgreSqlDiscoveryDbContext(_contextOptions);
    }

    /// <summary>
    /// 由实例描述符构建心跳行。
    /// </summary>
    /// <remarks>
    /// Builds a heartbeat row from an instance descriptor.
    /// </remarks>
    /// <param name="instance">实例描述符 / The instance descriptor</param>
    /// <returns>心跳行 / The heartbeat row</returns>
    private static ServerHeartbeatRow CreateRow(InstanceDescriptor instance)
    {
        return new ServerHeartbeatRow
        {
            InstanceId = instance.InstanceId,
            Role = instance.Role,
            AdvertiseEndpoint = instance.AdvertiseEndpoint,
            Status = instance.Status.ToString(),
            Load = instance.Load,
            AddressKind = instance.AddressKind.ToString(),
            Incarnation = instance.Incarnation,
            LastHeartbeat = DateTime.UtcNow,
        };
    }

    /// <summary>
    /// 把实例描述符字段复制到既有心跳行（保留主键）。
    /// </summary>
    /// <remarks>
    /// Copies an instance descriptor's fields onto an existing row (primary key preserved).
    /// </remarks>
    /// <param name="instance">实例描述符 / The instance descriptor</param>
    /// <param name="row">目标行 / The target row</param>
    private static void CopyToRow(InstanceDescriptor instance, ServerHeartbeatRow row)
    {
        row.Role = instance.Role;
        row.AdvertiseEndpoint = instance.AdvertiseEndpoint;
        row.Status = instance.Status.ToString();
        row.Load = instance.Load;
        row.AddressKind = instance.AddressKind.ToString();
        row.Incarnation = instance.Incarnation;
        row.LastHeartbeat = DateTime.UtcNow;
    }

    /// <summary>
    /// 行 → 实例描述符（未知枚举名或空白字段返回 null，防御旧版本行）。
    /// </summary>
    /// <remarks>
    /// Converts a row to a descriptor; returns null on an unknown enum name or blank field.
    /// </remarks>
    /// <param name="row">心跳行 / The heartbeat row</param>
    /// <returns>实例描述符；无法转换时为 null / The descriptor, or null when unparsable</returns>
    private static InstanceDescriptor TryToDescriptor(ServerHeartbeatRow row)
    {
        if (string.IsNullOrWhiteSpace(row.InstanceId) || string.IsNullOrWhiteSpace(row.Role) || string.IsNullOrWhiteSpace(row.AdvertiseEndpoint))
        {
            return null;
        }

        if (!Enum.TryParse<InstanceStatus>(row.Status, false, out var status) || !Enum.TryParse<EndpointAddressKind>(row.AddressKind, false, out var addressKind))
        {
            return null;
        }

        return new InstanceDescriptor(row.Role, row.InstanceId, row.AdvertiseEndpoint, status, row.Load, addressKind, row.Incarnation, row.LastHeartbeat.ToUniversalTime());
    }

    /// <summary>
    /// PostgreSQL「同名对象已存在」SQLSTATE（42P07 duplicate_table）。
    /// </summary>
    /// <remarks>
    /// The PostgreSQL "duplicate table" SQLSTATE (42P07).
    /// </remarks>
    private const string PostgresDuplicateTableSqlState = "42P07";

    /// <summary>
    /// 判断异常链中是否含主键唯一冲突（SQLSTATE 23505）。
    /// </summary>
    /// <remarks>
    /// Determines whether the exception chain contains a unique-constraint violation (SQLSTATE 23505).
    /// </remarks>
    /// <param name="exception">异常 / The exception</param>
    /// <returns>是否唯一冲突 / Whether a unique violation</returns>
    private static bool IsUniqueViolation(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: "23505", })
            {
                return true;
            }
        }

        return false;
    }
}
