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
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织不承担任何责任，开发者应自行承担。
//   The organization and contributors shall not be responsible; developers shall bear the responsibility themselves.
//  ==========================================================================================

using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Scope;

namespace GameFrameX.Capability.Online.Abstractions.Bridges;

/// <summary>
/// 好友查询桥发布器（C243）：进程级类型化 holder（RoleRouterHolder / UnifiedMessageSenderHolder 先例）——
/// 组合根（Game 型启动）在业务运行时启动后发布 <see cref="OnlineFriendBridgeLease"/>，
/// 进程停止前撤销；消费方（Hotfix 薄适配器）逐请求读取 <see cref="Current"/>，不缓存引用。
/// <para>
/// 维护约束：发布即替换（旧 lease 自动撤销）——宿主重启语义下旧引用不存活；
/// 撤销清空当前位并吊销全部既有 lease；本 holder 不做能力装配、不做路由，只承载「发布 / 撤销」这对生命周期动作。
/// </para>
/// </summary>
/// <remarks>
/// The friend-query bridge publisher (C243): the process-wide typed holder (following the
/// RoleRouterHolder / UnifiedMessageSenderHolder precedent) — the composition root (the Game-shape
/// startup) publishes an <see cref="OnlineFriendBridgeLease"/> after the business runtime starts and
/// retracts it before process shutdown; consumers (the Hotfix thin adapter) read
/// <see cref="Current"/> per request and never cache the reference. Publishing replaces (and revokes)
/// any previous lease so no stale reference survives a host restart; retracting clears the current
/// slot and revokes every lease ever handed out. The holder carries no capability assembly and no
/// routing — only the publish / retract lifecycle pair.
/// </remarks>
public static class OnlineFriendBridgeHolder
{
    /// <summary>当前租约（volatile 读；未发布 / 已撤销为 null）。</summary>
    private static volatile OnlineFriendBridgeLease current;

    /// <summary>
    /// 获取当前租约（未发布或已撤销时为 <c>null</c>；调用方不得跨请求缓存本值）。
    /// </summary>
    public static OnlineFriendBridgeLease Current
    {
        get { return current; }
    }

    /// <summary>
    /// 获取是否已发布有效租约。
    /// </summary>
    public static bool IsPublished
    {
        get { return current != null; }
    }

    /// <summary>
    /// 发布租约（组合根在业务运行时启动后调用）：替换并撤销既有租约，返回新租约。
    /// </summary>
    /// <param name="query">契约面。</param>
    /// <param name="authorizedScope">授权作用域（三元组）。</param>
    /// <returns>新发布的租约。</returns>
    public static OnlineFriendBridgeLease Publish(IOnlineFriendQuery query, OnlineScope authorizedScope)
    {
        var lease = new OnlineFriendBridgeLease(query, authorizedScope);
        var previous = current;
        current = lease;
        previous?.Deactivate();
        return lease;
    }

    /// <summary>
    /// 撤销租约（组合根在进程停止路径调用）：清空当前位并吊销全部既有租约（幂等）。
    /// </summary>
    public static void Retract()
    {
        var previous = current;
        current = null;
        previous?.Deactivate();
    }
}
