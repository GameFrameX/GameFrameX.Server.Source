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
/// PostgreSQL 玩家路由层装配点（C166 T8：与 <c>MongoPlayerRouteResolverBootstrap</c> 形态对齐）。
/// </summary>
/// <remarks>
/// The PostgreSQL player-route layer wiring point (C166 T8), shaped after
/// <c>MongoPlayerRouteResolverBootstrap</c>. The launch flow calls
/// <see cref="Attach"/> once after the control database is registered: it creates
/// the <c>player_route</c> schema (idempotent CREATE TABLE / INDEX IF NOT EXISTS —
/// the PostgreSQL counterpart of the Mongo index bootstrap, minus the TTL index
/// which the <c>PostgreSqlTtlCleanupJob</c> replaces), constructs the resolver and
/// sync target as process singletons, and exposes them through
/// <see cref="Resolver"/> / <see cref="SyncTarget"/>. Idempotent: only the first
/// call wires anything.
/// </remarks>
public static class PostgreSqlPlayerRouteResolverBootstrap
{
    /// <summary>
    /// 装配互斥标志（首调胜出）。
    /// </summary>
    /// <remarks>
    /// The idempotence flag (first call wins).
    /// </remarks>
    private static int _attached;

    /// <summary>
    /// 已装配的玩家路由解析器（Attach 之前为 null）。
    /// </summary>
    /// <remarks>
    /// The wired resolver (null before <see cref="Attach"/>).
    /// </remarks>
    /// <value>已装配的解析器实例；Attach 之前为 null / The wired resolver instance; null before <see cref="Attach"/></value>
    public static PostgreSqlPlayerRouteResolver Resolver { get; private set; }

    /// <summary>
    /// 已装配的同步目标（Attach 之前为 null）。
    /// </summary>
    /// <remarks>
    /// The wired sync target (null before <see cref="Attach"/>).
    /// </remarks>
    /// <value>已装配的同步目标实例；Attach 之前为 null / The wired sync-target instance; null before <see cref="Attach"/></value>
    public static PostgreSqlPlayerRouteSyncTarget SyncTarget { get; private set; }

    /// <summary>
    /// 激活玩家路由层：建表 / 建索引 + 构造 resolver 与 SyncTarget 单例。
    /// </summary>
    /// <remarks>
    /// Activates the player-route layer: ensures the schema (idempotent), constructs
    /// the resolver and sync target, and keeps references for the launch flow to
    /// wire <see cref="SyncTarget"/> into SessionManager and <see cref="Resolver"/>
    /// into <c>UnifiedMessageSenderHolder</c>. The bootstrap itself does not touch
    /// the host application layer.
    /// </remarks>
    /// <param name="dataSource">控制库数据源 / The control-database data source</param>
    /// <param name="fastPath">Tier 1 快路径提供方（null 跳过 Tier 1）/ Tier 1 fast path (null skips Tier 1)</param>
    /// <returns>是否为本进程首次装配（false 表示已激活，本次调用为 no-op） / true on first attach, false on subsequent calls</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="dataSource"/> 为 null 时抛出 / Thrown when <paramref name="dataSource"/> is null</exception>
    public static async Task<bool> Attach(NpgsqlDataSource dataSource, IPlayerRouteFastPath fastPath = null)
    {
        ArgumentNullException.ThrowIfNull(dataSource, nameof(dataSource));

        if (Interlocked.CompareExchange(ref _attached, 1, 0) != 0)
        {
            return false;
        }

        await PostgreSqlPlayerRouteSyncTarget.EnsureSchemaAsync(dataSource).ConfigureAwait(false);

        Resolver = new PostgreSqlPlayerRouteResolver(dataSource, fastPath);
        SyncTarget = new PostgreSqlPlayerRouteSyncTarget(dataSource);

        return true;
    }

    /// <summary>
    /// 重置装配状态（仅测试隔离使用）。
    /// </summary>
    /// <remarks>
    /// Resets the wiring state for test isolation. Test-only: production code never calls this.
    /// </remarks>
    internal static void ResetForTest()
    {
        _attached = 0;
        Resolver = null;
        SyncTarget = null;
    }
}