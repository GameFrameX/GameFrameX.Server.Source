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
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Npgsql;

namespace GameFrameX.DataBase.PostgreSql.Discovery;

/// <summary>
/// server_heartbeat 表的 EF 关系实体（发现层 EF 化）；属性形态继承自 <see cref="ServerHeartbeatEntity"/>。
/// </summary>
/// <remarks>
/// The EF relational entity for the <c>server_heartbeat</c> table. Properties are
/// declared once on the shared <see cref="ServerHeartbeatEntity"/> base; the snake_case column mapping lives in
/// <see cref="PostgreSqlDiscoveryDbContext.OnModelCreating"/> and is kept byte-identical — zero migration for
/// existing control databases, including the <c>server_heartbeat_pkey</c> primary-key constraint name.
/// Enum-ish columns stay as raw strings so the watcher's defensive parsing (unknown names skipped) is preserved.
/// </remarks>
public sealed class ServerHeartbeatRow : ServerHeartbeatEntity
{
}

/// <summary>
/// player_route 表的 EF 关系实体（发现层 EF 化）；属性形态继承自 <see cref="PlayerRouteEntity"/>。
/// </summary>
/// <remarks>
/// EF relational entity for the <c>player_route</c> table. Properties are declared
/// once on the shared <see cref="PlayerRouteEntity"/> base; the snake_case column mapping lives in
/// <see cref="PostgreSqlDiscoveryDbContext.OnModelCreating"/>. The <c>xmin</c> system
/// column is mapped as the optimistic concurrency token — the CAS guard replacing the former hand-written
/// atomic upsert SQL.
/// </remarks>
public sealed class PlayerRouteRow : PlayerRouteEntity
{
}

/// <summary>
/// 发现层控制库的 EF 上下文（心跳 + 玩家路由两张关系表；表结构经 EF 模型 API 创建，适配器内不再有建表 SQL）。
/// </summary>
/// <remarks>
/// The EF context for the discovery control database: the heartbeat and player-route relational
/// tables, schema-created through the EF model API. The context binds to the shared pooled
/// <see cref="NpgsqlDataSource"/> (the same pool the health checks probe) and is short-lived — one per
/// store operation.
/// </remarks>
public sealed class PostgreSqlDiscoveryDbContext : DbContext
{
    /// <summary>
    /// 初始化发现层上下文。
    /// </summary>
    /// <remarks>
    /// Initializes the discovery context with data-source-bound options.
    /// </remarks>
    /// <param name="options">数据源绑定的上下文选项 / The data-source-bound context options</param>
    public PostgreSqlDiscoveryDbContext(DbContextOptions<PostgreSqlDiscoveryDbContext> options) : base(options)
    {
    }

    /// <summary>
    /// 心跳表实体集。
    /// </summary>
    /// <remarks>
    /// The heartbeat table entity set.
    /// </remarks>
    public DbSet<ServerHeartbeatRow> Heartbeats { get; set; }

    /// <summary>
    /// 玩家路由表实体集。
    /// </summary>
    /// <remarks>
    /// The player-route table entity set.
    /// </remarks>
    public DbSet<PlayerRouteRow> PlayerRoutes { get; set; }

    /// <summary>
    /// 配置发现层两张表的映射（列名 / 索引与既有存储模型逐字一致；xmin 乐观并发令牌承载 CAS）。
    /// </summary>
    /// <remarks>
    /// Configures both discovery tables (column names and indexes identical to C167). The player-route
    /// <c>xmin</c> system column is the concurrency token: any concurrent row change makes the guarded
    /// update match zero rows, which the store maps onto the existing
    /// <see cref="GameFrameX.Discovery.Routing.PlayerRouteCasOutcome"/> semantics.
    /// </remarks>
    /// <param name="modelBuilder">模型构建器 / The model builder</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var heartbeatTable = DiscoveryStorageNaming.TableName<ServerHeartbeatEntity>();
        var heartbeat = modelBuilder.Entity<ServerHeartbeatRow>();
        heartbeat.ToTable(heartbeatTable);
        heartbeat.HasKey(static row => row.InstanceId).HasName(DiscoveryStorageNaming.PrimaryKeyName(heartbeatTable));
        heartbeat.HasIndex(static row => row.LastHeartbeat).HasDatabaseName(DiscoveryStorageNaming.TableIndexName(heartbeatTable, DiscoveryStorageNaming.SnakeCase(nameof(ServerHeartbeatEntity.LastHeartbeat))));
        MapSnakeCaseColumns<ServerHeartbeatEntity>(heartbeat);

        var playerRouteTable = DiscoveryStorageNaming.TableName<PlayerRouteEntity>();
        var playerRoute = modelBuilder.Entity<PlayerRouteRow>();
        playerRoute.ToTable(playerRouteTable);
        playerRoute.HasKey(static row => row.PlayerId).HasName(DiscoveryStorageNaming.PrimaryKeyName(playerRouteTable));
        playerRoute.HasIndex(static row => row.Role).HasDatabaseName(DiscoveryStorageNaming.TableIndexName(playerRouteTable, DiscoveryStorageNaming.SnakeCase(nameof(PlayerRouteEntity.Role))));
        MapSnakeCaseColumns<PlayerRouteEntity>(playerRoute);
        // xmin 系统列映射为乐观并发令牌（provider 10 起 UseXminAsConcurrencyToken 扩展已移除，采用标准 shadow 属性形态）。
        // The xmin system column is the concurrency token (the UseXminAsConcurrencyToken extension was removed
        // in provider 10; the canonical shadow-property form is used instead).
        playerRoute.Property<uint>("xmin").IsRowVersion();
    }

    /// <summary>
    /// 按统一规则把实体基类的全部属性映射为 snake_case 列（零列名字面量）。
    /// </summary>
    /// <remarks>
    /// Maps every property of the entity base to its snake_case column through the
    /// unified naming rule (zero column-name literals).
    /// </remarks>
    /// <typeparam name="TEntity">实体基类 / The entity base</typeparam>
    /// <param name="entityTypeBuilder">实体构建器 / The entity type builder</param>
    private static void MapSnakeCaseColumns<TEntity>(EntityTypeBuilder entityTypeBuilder)
    {
        foreach (var property in typeof(TEntity).GetProperties())
        {
            entityTypeBuilder.Property(property.Name).HasColumnName(DiscoveryStorageNaming.SnakeCase(property.Name));
        }
    }
}