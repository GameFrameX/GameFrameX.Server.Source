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


namespace GameFrameX.NetWork.RemoteMessaging.Routing;

/// <summary>
/// 玩家路由写入侧参数对象（C154）：一条待同步的玩家路由记录。
/// </summary>
/// <remarks>
/// Parameter object for the player-route write side (C154): one player-route record to sync.
/// Replaces the former four-primitive <c>UpsertAsync(playerId, instanceId, role, version)</c>
/// signature so the <c>player_route</c> fields can evolve without breaking the interface,
/// its implementations, or callers; the record lives in RemoteMessaging to keep
/// GameFrameX.Apps free of Mongo types.
/// </remarks>
public sealed class PlayerRouteRecord
{
    /// <summary>
    /// 获取或设置玩家 ID；非正数将被同步目标静默忽略。
    /// </summary>
    /// <remarks>
    /// Gets or sets the player id; non-positive ids are silently ignored by the sync target.
    /// </remarks>
    public long PlayerId { get; init; }

    /// <summary>
    /// 获取或设置实例 ID；空白时 Mongo 实现抛 <see cref="ArgumentException"/>。
    /// </summary>
    /// <remarks>
    /// Gets or sets the instance id; the Mongo implementation throws <see cref="ArgumentException"/> when blank.
    /// </remarks>
    public string InstanceId { get; init; }

    /// <summary>
    /// 获取或设置 Role 名。
    /// </summary>
    /// <remarks>
    /// Gets or sets the role name.
    /// </remarks>
    public string Role { get; init; }

    /// <summary>
    /// 获取或设置顶号版本号：1 = 首登无条件写入；&gt;1 = CAS 顶号版本（须等于当前版本 + 1）。
    /// </summary>
    /// <remarks>
    /// Gets or sets the kick/relogin version: 1 = first login, written unconditionally;
    /// &gt;1 = CAS version and must equal the current version + 1.
    /// </remarks>
    public long Version { get; init; }
}
