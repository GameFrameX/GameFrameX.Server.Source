// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相关法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   The full license text is available at the LICENSE file in the source root.
//   禁止利用本项目实施危害国家安全、破坏社会秩序、侵犯他人合法权益等法律法规所禁止的行为！
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   or infringe the lawful rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷及责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:   https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:     https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using GameFrameX.Discovery.Routing;

namespace GameFrameX.Discovery;

/// <summary>
/// 发现层激活参数对象（Mongo / PostgreSQL 两个 Runtime 共用的唯一形态，C185）。
/// </summary>
/// <remarks>
/// The single activation parameter object shared by both discovery runtimes
/// (<c>MongoDiscoveryRuntime.Activate</c> / <c>PostgreSqlDiscoveryRuntime.Activate</c>).
/// The former provider-specific variants (with the <c>IMongoDatabase</c> /
/// <c>NpgsqlDataSource</c> carrier members) were removed in C185: both runtimes
/// now resolve the control database by <see cref="ConnectionName"/> through the
/// unified <c>GameDb</c> entry, leaving no carrier direct-injection path.
/// </remarks>
public sealed class DiscoveryActivationOptions
{
    /// <summary>
    /// 获取或设置控制库注册名（必填）。
    /// 控制库必须先经统一入口 <c>GameDb.Init(DbOptions)</c> 注册（生产启动流固定使用
    /// <c>GameDb.ControlDatabaseName</c>，即 gameframex_control），激活时由 Runtime 内部
    /// <c>GameDb.As&lt;实现&gt;(name)</c> 按名解析载体；C185 已废除 IMongoDatabase /
    /// NpgsqlDataSource 载体直入路径，注册名是与 C159 统一入口铁律一致的唯一激活方式。
    /// 未设置时抛 <see cref="ArgumentException"/>；名称未注册时由 <c>GameDb.As</c> 抛
    /// <see cref="InvalidOperationException"/>（既有语义）。
    /// </summary>
    /// <remarks>
    /// Gets or sets the control-database registry name (required). The control database must be
    /// registered through the unified <c>GameDb.Init(DbOptions)</c> entry first (production launch
    /// flows always use <c>GameDb.ControlDatabaseName</c>, i.e. gameframex_control); the runtime then
    /// resolves the carrier via <c>GameDb.As&lt;T&gt;(name)</c>. C185 removed the carrier
    /// direct-injection path, so the registry name is the only activation route, aligned with the
    /// C159 unified-entry rule. Throws <see cref="ArgumentException"/> when unset, and
    /// <see cref="InvalidOperationException"/> (from <c>GameDb.As</c>) when the name is not registered.
    /// </remarks>
    public string ConnectionName { get; init; }

    /// <summary>
    /// 获取或设置本进程承载的 Role 名全集（RoleSet 快照）。
    /// </summary>
    /// <remarks>
    /// Gets or sets the full hosted role-name set (the RoleSet snapshot).
    /// </remarks>
    public IEnumerable<string> HostedRoleNames { get; init; }

    /// <summary>
    /// 获取或设置 Tier 1 玩家路由快路径提供方（apps 端 SessionManager 适配器）；null 则跳过 Tier 1。
    /// </summary>
    /// <remarks>
    /// Gets or sets the Tier 1 player-route fast-path provider (the apps-side SessionManager adapter);
    /// null skips Tier 1.
    /// </remarks>
    public IPlayerRouteFastPath PlayerRouteFastPath { get; init; }

    /// <summary>
    /// 获取或设置 TTL 清理周期（缺省 5s；测试可收缩）。
    /// </summary>
    /// <remarks>
    /// Gets or sets the TTL cleanup period (defaults to 5 s; shrinkable in tests).
    /// Drives the generic registry's cleanup loop over both stores (client-side
    /// expiry deletion for Mongo; the PostgreSQL AC-4 equivalent).
    /// </remarks>
    public TimeSpan? TtlCleanupInterval { get; init; }
}
