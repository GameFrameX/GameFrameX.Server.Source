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


using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GameFrameX.NetWork.RemoteMessaging.Discovery;

/// <summary>
/// server_heartbeat 表的 EF 关系实体（C168 发现层 EF 化；表 / 列形态与 C167 逐字一致，存量库零迁移）。
/// </summary>
/// <remarks>
/// The EF relational entity for the <c>server_heartbeat</c> table (C168 discovery EF migration; table and column
/// shapes identical to C167 — zero migration for existing control databases, including the
/// <c>server_heartbeat_pkey</c> primary-key constraint name). Enum-ish columns stay as raw
/// strings so the watcher's defensive parsing (unknown names skipped) is preserved byte-for-byte.
/// </remarks>
[Table("server_heartbeat")]
public sealed class ServerHeartbeatRow
{
    /// <summary>
    /// 实例 ID（主键，列 instance_id）。
    /// </summary>
    /// <remarks>
    /// The instance id (primary key, column instance_id).
    /// </remarks>
    [Column("instance_id")]
    public string InstanceId { get; set; }

    /// <summary>
    /// 角色名（列 role）。
    /// </summary>
    /// <remarks>
    /// The role name (column role).
    /// </remarks>
    [Column("role")]
    public string Role { get; set; }

    /// <summary>
    /// 广播端点（列 advertise_endpoint）。
    /// </summary>
    /// <remarks>
    /// The advertise endpoint (column advertise_endpoint).
    /// </remarks>
    [Column("advertise_endpoint")]
    public string AdvertiseEndpoint { get; set; }

    /// <summary>
    /// 状态名（列 status；字符串形态，解析防御在读取侧）。
    /// </summary>
    /// <remarks>
    /// The status name (column status; kept as a raw string, parsed defensively on read).
    /// </remarks>
    [Column("status")]
    public string Status { get; set; }

    /// <summary>
    /// 负载值（列 load）。
    /// </summary>
    /// <remarks>
    /// The load value (column load).
    /// </remarks>
    [Column("load")]
    public int Load { get; set; }

    /// <summary>
    /// 地址类型名（列 address_kind；字符串形态，解析防御在读取侧）。
    /// </summary>
    /// <remarks>
    /// The address-kind name (column address_kind; kept as a raw string, parsed defensively on read).
    /// </remarks>
    [Column("address_kind")]
    public string AddressKind { get; set; }

    /// <summary>
    /// 代次号（列 incarnation）。
    /// </summary>
    /// <remarks>
    /// The incarnation number (column incarnation).
    /// </remarks>
    [Column("incarnation")]
    public long Incarnation { get; set; }

    /// <summary>
    /// 最近心跳时间（UTC，列 last_heartbeat，timestamptz）。
    /// </summary>
    /// <remarks>
    /// The last heartbeat time (UTC, column last_heartbeat, timestamptz).
    /// </remarks>
    [Column("last_heartbeat")]
    public DateTime LastHeartbeat { get; set; }
}

/// <summary>
/// player_route 表的 EF 关系实体（C168 发现层 EF 化；表 / 列形态与 C167 逐字一致，存量库零迁移）。
/// </summary>
/// <remarks>
/// EF relational entity for the <c>player_route</c> table (C168 discovery EF migration; table and column
/// shapes identical to C167). The <c>xmin</c> system column is mapped as the optimistic concurrency token —
/// the CAS guard replacing the former hand-written atomic upsert SQL.
/// </remarks>
[Table("player_route")]
public sealed class PlayerRouteRow
{
    /// <summary>
    /// 玩家 ID（主键，列 player_id）。
    /// </summary>
    /// <remarks>
    /// The player id (primary key, column player_id).
    /// </remarks>
    [Column("player_id")]
    public long PlayerId { get; set; }

    /// <summary>
    /// 实例 ID（列 instance_id）。
    /// </summary>
    /// <remarks>
    /// The instance id (column instance_id).
    /// </remarks>
    [Column("instance_id")]
    public string InstanceId { get; set; }

    /// <summary>
    /// 角色名（列 role）。
    /// </summary>
    /// <remarks>
    /// The role name (column role).
    /// </remarks>
    [Column("role")]
    public string Role { get; set; }

    /// <summary>
    /// 路由版本号（列 version；CAS 语义的业务载体）。
    /// </summary>
    /// <remarks>
    /// The route version (column version; the business carrier of the CAS semantics).
    /// </remarks>
    [Column("version")]
    public long Version { get; set; }

    /// <summary>
    /// 最近活跃时间（UTC，列 last_seen_at，timestamptz）。
    /// </summary>
    /// <remarks>
    /// The last-seen time (UTC, column last_seen_at, timestamptz).
    /// </remarks>
    [Column("last_seen_at")]
    public DateTime LastSeenAt { get; set; }
}

/// <summary>
/// 发现层控制库的 EF 上下文（C168：心跳 + 玩家路由两张关系表；表结构经 EF 模型 API 创建，适配器内不再有建表 SQL）。
/// </summary>
/// <remarks>
/// The EF context for the discovery control database (C168): the heartbeat and player-route relational
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
    /// 配置发现层两张表的映射（列名 / 索引与 C167 逐字一致；xmin 乐观并发令牌承载 CAS）。
    /// </summary>
    /// <remarks>
    /// Configures both discovery tables (column names and indexes identical to C167). The player-route
    /// <c>xmin</c> system column is the concurrency token: any concurrent row change makes the guarded
    /// update match zero rows, which the store maps onto the existing
    /// <see cref="GameFrameX.NetWork.RemoteMessaging.Routing.PlayerRouteCasOutcome"/> semantics.
    /// </remarks>
    /// <param name="modelBuilder">模型构建器 / The model builder</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var heartbeat = modelBuilder.Entity<ServerHeartbeatRow>();
        heartbeat.HasKey(static row => row.InstanceId).HasName("server_heartbeat_pkey");
        heartbeat.HasIndex(static row => row.LastHeartbeat).HasDatabaseName("ix_server_heartbeat_last_heartbeat");

        var playerRoute = modelBuilder.Entity<PlayerRouteRow>();
        playerRoute.HasKey(static row => row.PlayerId).HasName("player_route_pkey");
        playerRoute.HasIndex(static row => row.Role).HasDatabaseName("ix_player_route_role");
        // xmin 系统列映射为乐观并发令牌（provider 10 起 UseXminAsConcurrencyToken 扩展已移除，采用标准 shadow 属性形态）。
        // The xmin system column is the concurrency token (the UseXminAsConcurrencyToken extension was removed
        // in provider 10; the canonical shadow-property form is used instead).
        playerRoute.Property<uint>("xmin").IsRowVersion();
    }
}
