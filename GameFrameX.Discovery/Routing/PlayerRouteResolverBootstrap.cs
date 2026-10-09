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


namespace GameFrameX.Discovery.Routing;

/// <summary>
/// 通用玩家路由层装配点（C167：自 Mongo / PG 平行 Bootstrap 归一，消费 <see cref="IPlayerRouteStore"/>）。
/// </summary>
/// <remarks>
/// The generic player-route layer wiring point (C167, unified from the Mongo /
/// PostgreSQL parallel bootstraps). The launch flow calls <see cref="Attach"/>
/// once after the control database is registered: it ensures the
/// <c>player_route</c> schema (idempotent — the driver's index / DDL
/// bootstrap), constructs the resolver and sync target as process singletons,
/// and exposes them through <see cref="Resolver"/> / <see cref="SyncTarget"/>.
/// Idempotent: only the first call wires anything. The bootstrap itself does
/// not touch the host application layer — the caller is responsible for the
/// cross-package wiring.
/// </remarks>
public static class PlayerRouteResolverBootstrap
{

    /// <summary>
    /// 离线路由 TTL（30 天，秒数；清理循环与 TTL 索引共用同值）。
    /// </summary>
    /// <remarks>
    /// The TTL window for an offline route record (30 days, in seconds; shared by the
    /// cleanup loop and the server-side TTL index).
    /// </remarks>
    public const long RouteTimeToLiveSeconds = 30L * 24L * 60L * 60L;

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
    public static PlayerRouteResolver Resolver { get; private set; }

    /// <summary>
    /// 已装配的同步目标（Attach 之前为 null）。
    /// </summary>
    /// <remarks>
    /// The wired sync target (null before <see cref="Attach"/>).
    /// </remarks>
    public static PlayerRouteSyncTarget SyncTarget { get; private set; }

    /// <summary>
    /// 激活玩家路由层：建 schema / 索引 + 构造 Resolver 与 SyncTarget 单例。
    /// </summary>
    /// <remarks>
    /// Activates the player-route layer: ensures the schema (idempotent), constructs
    /// the resolver and sync target, and keeps references so the launch flow can
    /// wire <see cref="SyncTarget"/> into SessionManager and <see cref="Resolver"/>
    /// into <c>UnifiedMessageSenderHolder</c>.
    /// </remarks>
    /// <param name="store">玩家路由存储适配 / The player-route storage seam</param>
    /// <param name="fastPath">Tier 1 快路径提供方（null 跳过 Tier 1）/ Tier 1 fast path (null skips Tier 1)</param>
    /// <returns>是否为本进程首次装配（false 表示已激活，本次调用为 no-op） / true on first attach, false on subsequent calls</returns>
    public static async Task<bool> Attach(IPlayerRouteStore store, IPlayerRouteFastPath fastPath = null)
    {
        ArgumentNullException.ThrowIfNull(store, nameof(store));

        if (Interlocked.CompareExchange(ref _attached, 1, 0) != 0)
        {
            return false;
        }

        await store.EnsureSchemaAsync().ConfigureAwait(false);

        Resolver = new PlayerRouteResolver(store, fastPath);
        SyncTarget = new PlayerRouteSyncTarget(store);

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
