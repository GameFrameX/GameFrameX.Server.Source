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


using GameFrameX.DataBase.PostgreSql.Discovery;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GameFrameX.DataBase.PostgreSql.Routing;

/// <summary>
/// PostgreSQL 玩家路由存储适配（<see cref="IPlayerRouteStore"/> 的 EF 关系实现，CAS 经 xmin 乐观并发令牌）。
/// </summary>
/// <remarks>
/// The PostgreSQL implementation of the player-route storage seam (EF-based):
/// consumed by the generic resolver / sync target / bootstrap. The former hand-written guarded-update SQL is
/// replaced by EF's <c>xmin</c> optimistic concurrency token — a concurrent row change makes the guarded
/// save match zero rows, mapping onto the existing <see cref="PlayerRouteCasOutcome"/> semantics unchanged.
/// <see cref="DeleteExpiredAsync"/> executes the real delete (the 30-day offline garbage window) — driven
/// by the registry's cleanup loop.
/// </remarks>
public sealed class PostgreSqlPlayerRouteStore : IPlayerRouteStore
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
    /// 初始化 PostgreSQL 玩家路由存储适配。
    /// </summary>
    /// <remarks>
    /// Initializes the store. Schema creation happens in <see cref="EnsureSchemaAsync"/>.
    /// </remarks>
    /// <param name="dataSource">控制库数据源 / The control-database data source</param>
    public PostgreSqlPlayerRouteStore(NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        _dataSource = dataSource;
    }

    /// <summary>
    /// 幂等建表 / 建索引（EF 模型 DDL；表已存在视为成功）。
    /// </summary>
    /// <remarks>
    /// Idempotently creates the player-route table and its role index (EF model DDL; pre-existing tables
    /// count as success). The discovery model covers both control tables, so this also ensures the
    /// heartbeat shape when called first.
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
        catch (PostgresException exception) when (exception.SqlState == PostgreSqlSqlState.DuplicateTable)
        {
            // 表已存在（存量控制库 / 并发装配）：视作成功。
            // Tables already exist: success.
        }
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
        using var context = CreateContext();
        var row = await context.PlayerRoutes.AsNoTracking().FirstOrDefaultAsync(row => row.PlayerId == playerId, cancellationToken).ConfigureAwait(false);
        return row == null ? null : ToRecord(row);
    }

    /// <summary>
    /// 以 version 守卫更新路由（version-1 命中，xmin 令牌防并发；未命中回读区分 Missing / Stale）。
    /// </summary>
    /// <remarks>
    /// Attempts the guarded update (matching playerId with the stored version equal to the supplied version
    /// minus one; the <c>xmin</c> concurrency token guards against concurrent writers). On a miss the latest
    /// row is re-read to distinguish <see cref="PlayerRouteCasOutcome.Missing"/> from
    /// <see cref="PlayerRouteCasOutcome.Stale"/>; the branch policy itself lives in the generic sync target.
    /// </remarks>
    /// <param name="record">待写入的路由记录 / The record to write</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>CAS 结果 / The CAS outcome</returns>
    public async Task<PlayerRouteCasOutcome> CasUpsertAsync(PlayerRouteRecord record, CancellationToken cancellationToken = default)
    {
        bool guarded;
        using (var context = CreateContext())
        {
            var row = await context.PlayerRoutes.FirstOrDefaultAsync(row => row.PlayerId == record.PlayerId, cancellationToken).ConfigureAwait(false);
            guarded = row != null && row.Version == record.Version - 1;
            if (guarded)
            {
                ApplyRecord(row, record);
                try
                {
                    await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    return PlayerRouteCasOutcome.Updated;
                }
                catch (DbUpdateConcurrencyException)
                {
                    // xmin 守卫命中并发写：按竞争失败处理，走回读分类。
                    // The xmin token caught a concurrent write: treat as a lost race and classify below.
                    guarded = false;
                }
            }
        }

        if (!guarded)
        {
            // 未命中：可能行被删（首登/重试），可能版本竞争失败。读最新行区分处理。
            // No match: the row is missing or the version guard failed; read the latest.
            var latest = await GetAsync(record.PlayerId, cancellationToken).ConfigureAwait(false);
            return latest == null ? PlayerRouteCasOutcome.Missing : PlayerRouteCasOutcome.Stale;
        }

        return PlayerRouteCasOutcome.Stale;
    }

    /// <summary>
    /// 首登无条件写入（version=1，后写者胜出；主键 / 并发竞争自动重试一次）。
    /// </summary>
    /// <remarks>
    /// Writes the first-login row unconditionally (version = 1; under concurrent first logins the later
    /// writer wins). Insert races surface as a primary-key violation and update races as concurrency-token
    /// conflicts; both are retried once through reload-and-replace, converging to one row.
    /// </remarks>
    /// <param name="record">首登记录 / The first-login record</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>异步任务 / Async task</returns>
    public async Task InsertFirstLoginAsync(PlayerRouteRecord record, CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                using var context = CreateContext();
                var row = await context.PlayerRoutes.FirstOrDefaultAsync(row => row.PlayerId == record.PlayerId, cancellationToken).ConfigureAwait(false);
                if (row == null)
                {
                    context.PlayerRoutes.Add(new PlayerRouteRow { PlayerId = record.PlayerId, InstanceId = record.InstanceId ?? string.Empty, Role = record.Role ?? string.Empty, Version = 1, LastSeenAt = DateTime.UtcNow, });
                }
                else
                {
                    row.InstanceId = record.InstanceId ?? string.Empty;
                    row.Role = record.Role ?? string.Empty;
                    row.Version = 1;
                    row.LastSeenAt = DateTime.UtcNow;
                }

                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (DbUpdateException exception) when (attempt == 0 && (PostgreSqlSqlState.IsUniqueViolation(exception) || exception is DbUpdateConcurrencyException))
            {
                // 并发首登竞争：重载后按后写者胜出收敛。
                // Concurrent first login: reload and converge last-writer-wins.
            }
        }
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
        using var context = CreateContext();
        await context.PlayerRoutes.Where(row => row.PlayerId == playerId).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 删除过期路由行（last_seen_at &lt; 截止时间；30 天窗口）。
    /// </summary>
    /// <remarks>
    /// Deletes route rows older than the cutoff (now minus the supplied window). Driven by the registry's
    /// cleanup loop; removal is relaxed to within one cleanup period.
    /// </remarks>
    /// <param name="timeToLive">路由保存窗口 / The expire-after window</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>异步任务 / Async task</returns>
    public async Task DeleteExpiredAsync(TimeSpan timeToLive, CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow - timeToLive;
        using var context = CreateContext();
        await context.PlayerRoutes.Where(row => row.LastSeenAt < cutoff).ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 把路由记录应用到既有行（version / 时间戳按 CAS 语义写入）。
    /// </summary>
    /// <remarks>
    /// Applies the record onto an existing row (version and timestamp written per the CAS semantics).
    /// </remarks>
    /// <param name="row">目标行 / The target row</param>
    /// <param name="record">路由记录 / The record</param>
    private static void ApplyRecord(PlayerRouteRow row, PlayerRouteRecord record)
    {
        row.InstanceId = record.InstanceId ?? string.Empty;
        row.Role = record.Role ?? string.Empty;
        row.Version = record.Version;
        row.LastSeenAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 行 → 路由记录。
    /// </summary>
    /// <remarks>
    /// Converts a row to a route record.
    /// </remarks>
    /// <param name="row">行 / The row</param>
    /// <returns>路由记录 / The record</returns>
    private static PlayerRouteRecord ToRecord(PlayerRouteRow row)
    {
        return new PlayerRouteRecord { PlayerId = row.PlayerId, InstanceId = row.InstanceId, Role = row.Role, Version = row.Version, };
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
}