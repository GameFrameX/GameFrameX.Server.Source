// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规及开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes and liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:  https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using Npgsql;

namespace GameFrameX.DataBase.PostgreSql.Discovery;

/// <summary>
/// PostgreSQL 发现层激活参数对象（控制库载体为 NpgsqlDataSource）。
/// </summary>
/// <remarks>
/// Parameter object for PostgreSQL discovery activation: the same shape as
/// <c>DiscoveryActivationOptions</c> with the control-database carrier swapped
/// to <see cref="NpgsqlDataSource"/>. Set <see cref="DataSource"/> for direct
/// package consumers, or <see cref="ConnectionName"/> for launch flows that
/// resolve the control database through the unified <c>GameDb</c> entry
/// (<c>GameDb.As&lt;PostgreSqlDbService&gt;(name).DataSource</c>); when both are
/// null activation throws <see cref="ArgumentException"/>.
/// </remarks>
public sealed class PostgreSqlDiscoveryActivationOptions
{
    /// <summary>
    /// 获取或设置控制库数据源；与 <see cref="ConnectionName"/> 二选一。
    /// </summary>
    /// <remarks>
    /// Gets or sets the control-database data source; mutually exclusive with <see cref="ConnectionName"/>.
    /// </remarks>
    public NpgsqlDataSource DataSource { get; init; }

    /// <summary>
    /// 获取或设置控制库注册名（经统一入口 GameDb 解析）；与 <see cref="DataSource"/> 二选一。
    /// </summary>
    /// <remarks>
    /// Gets or sets the control-database registry name (resolved through the unified GameDb entry); mutually exclusive with <see cref="DataSource"/>.
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
    /// Gets or sets the Tier 1 player-route fast-path provider (the apps-side SessionManager adapter); null skips Tier 1.
    /// </remarks>
    public IPlayerRouteFastPath PlayerRouteFastPath { get; init; }

    /// <summary>
    /// 获取或设置 TTL 清理周期（缺省 5s；测试可收缩）。
    /// </summary>
    /// <remarks>
    /// Gets or sets the TTL cleanup period (defaults to 5 s; shrinkable in tests).
    /// Drives the generic registry's cleanup loop over both stores.
    /// </remarks>
    public TimeSpan? TtlCleanupInterval { get; init; }
}