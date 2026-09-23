// ==========================================================================================
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   This project is licensed solely under the Apache License 2.0,
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   Any legal disputes and liabilities arising from secondary development based on this project
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
// ==========================================================================================


using MongoDB.Driver;

namespace GameFrameX.NetWork.RemoteMessaging.Discovery;

/// <summary>
/// Mongo 发现层激活参数对象（C154）：合并原 IMongoDatabase 直入与 C159 按名解析两种重载。
/// </summary>
/// <remarks>
/// Parameter object for Mongo discovery activation (C154): merges the former
/// direct-<see cref="IMongoDatabase"/> overload and the C159 name-based overload into a single shape.
/// Set <see cref="ControlDatabase"/> for direct package consumers, or <see cref="ConnectionName"/>
/// for launch flows that resolve the control database through the unified <c>GameDb</c> entry;
/// when both are null activation throws <see cref="ArgumentException"/>.
/// </remarks>
public sealed class DiscoveryActivationOptions
{
    /// <summary>
    /// 获取或设置控制库实例（gameframex_control）；与 <see cref="ConnectionName"/> 二选一。
    /// </summary>
    /// <remarks>
    /// Gets or sets the control database instance (gameframex_control); mutually exclusive with <see cref="ConnectionName"/>.
    /// </remarks>
    public IMongoDatabase ControlDatabase { get; init; }

    /// <summary>
    /// 获取或设置控制库注册名（<c>GameDb.ControlDatabaseName</c>，经统一入口解析）；与 <see cref="ControlDatabase"/> 二选一。
    /// </summary>
    /// <remarks>
    /// Gets or sets the control database registry name (<c>GameDb.ControlDatabaseName</c>, resolved through the
    /// unified GameDb entry); mutually exclusive with <see cref="ControlDatabase"/>.
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
}
