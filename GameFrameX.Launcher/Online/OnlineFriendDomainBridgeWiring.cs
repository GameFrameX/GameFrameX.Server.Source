// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
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
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations.
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织及其贡献者不承担任何责任，开发者应自行承担。
//   The organization and contributors assume no responsibility; developers shall bear the responsibility themselves.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:    https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.Collections.Generic;
using System.Threading.Tasks;
using GameFrameX.Capability.Hosting.Core;
using GameFrameX.Capability.Online.Abstractions.Bridges;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Scope;
using GameFrameX.Capability.Online.Social;
using GameFrameX.Foundation.Localization.Core;

namespace GameFrameX.Launcher.Online;

/// <summary>
/// 好友域桥组合根接线（全家族桥接，替代早期查询桥）：Game 型启动链的桥接生命周期编排——
/// 发布（业务运行时启动后）/ 撤销（退出路径）/ 可选验证种子（经真实写路径预置非空好友状态）。
/// <para>
/// 维护约束：发布 / 撤销必须成对出现在同一组合根（AppStartUpGame）；域桥无条件于运行时启动后发布
///（旧 120/660 链已随本切换退役，无回落目标）——契约面缺席（online-social 被过滤器排除）按 Error
/// 披露后跳过发布，好友消息届时显式 <see cref="OnlineErrorCode.DependencyUnavailable"/> 失败，
/// 不降级、不空转；种子仅供部署验证，生产常开须知情（Warning 披露）。
/// </para>
/// </summary>
/// <remarks>
/// The friend-domain bridge composition-root wiring for the whole family: the bridge lifecycle
/// orchestration for the Game-shape startup chain — publish (after the business runtime starts) /
/// retract (on the shutdown path) / the optional verification seed (presetting non-empty friend
/// state through the real write path).
/// Publish and retract must stay paired inside the same composition root (AppStartUpGame); the
/// domain bridge is published unconditionally after the runtime starts (the legacy domain-120/660
/// chains retired with this cutover — there is no fallback target) — a missing contract face
/// (online-social excluded by filters) is disclosed with an Error log and skips publishing, after
/// which friend messages fail explicitly with <see cref="OnlineErrorCode.DependencyUnavailable"/>;
/// no degradation, no silent idle. The seed is for deployment verification only; keeping it on in
/// production requires informed consent (disclosed by a Warning).
/// </remarks>
public static class OnlineFriendDomainBridgeWiring
{
    /// <summary>
    /// 发布好友域桥租约（业务运行时启动后调用）：从宿主共享面解析域契约面与授权作用域并发布；
    /// 契约面或作用域缺席（online-social 被排除 / 未装配）按 Error 披露并返回 null（不抛出——
    /// Online 运行时是可选拓扑，缺席态由好友消息层显式失败承载）。
    /// </summary>
    /// <param name="runtime">业务能力宿主。</param>
    /// <returns>发布的租约；契约面缺席返回 null。</returns>
    public static OnlineFriendDomainBridgeLease PublishFriendDomainBridge(CapabilityHost runtime)
    {
        if (!runtime.Services.TryGetService<IOnlineFriendDomain>(out var domain) || !runtime.Services.TryGetService<OnlineScope>(out var authorizedScope))
        {
            // Localization: Launcher.OnlineFriendBridgeFaceMissing - Online 好友域桥契约面不可解析（online-social 未装配或被排除），好友消息将显式失败
            LogHelper.Error(LocalizationService.GetString(Localization.Keys.Launcher.OnlineFriendBridgeFaceMissing));
            return null;
        }
        var lease = OnlineFriendDomainBridgeHolder.Publish(domain, authorizedScope);
        // Localization: Launcher.OnlineFriendBridgePublished - Online 好友查询桥已发布（租户 {0}，应用 {1}，区服 {2}）...
        LogHelper.Info(LocalizationService.GetString(Localization.Keys.Launcher.OnlineFriendBridgePublished, authorizedScope.TenantId, authorizedScope.AppId, authorizedScope.ServerId));
        return lease;
    }

    /// <summary>
    /// 撤销好友域桥租约（进程停止路径调用；未发布时为空操作）。
    /// </summary>
    public static void RetractFriendDomainBridge()
    {
        if (!OnlineFriendDomainBridgeHolder.IsPublished)
        {
            return;
        }

        OnlineFriendDomainBridgeHolder.Retract();
        // Localization: Launcher.OnlineFriendBridgeRetracted - Online 好友查询桥已撤销...
        LogHelper.Info(LocalizationService.GetString(Localization.Keys.Launcher.OnlineFriendBridgeRetracted));
    }

    /// <summary>
    /// 建立好友域桥验证种子（业务运行时启动后调用；空 / 单元素列表为空操作）：
    /// 列表首个标识为请求方、其余为好友，经真实 <see cref="OnlineFriendService"/> 写路径两两建立
    ///（幂等收敛——已是好友返回既有关系），使真实协议入口可以该请求方身份读到预置非空好友状态。
    /// <para>
    /// 请求方取配置首 id 而非进程 actor 派生 id——玩家型组件的 actor id 为多实例生成
    ///（<c>ActorTypePlayer &lt; ActorTypeSeparator</c>，逐次唯一），不存在可预置的确定性请求主体；
    /// 验证入口（HTTP <c>mode=online-bridge-friend</c>）以显式 playerId 复核同一主体。
    /// </para>
    /// </summary>
    /// <param name="runtime">业务能力宿主。</param>
    /// <param name="friendPlayerIds">种子标识列表（首元素 = 请求方，其余 = 好友）。</param>
    public static async Task SeedFriendDomainBridgeVerificationAsync(CapabilityHost runtime, IReadOnlyList<long> friendPlayerIds)
    {
        if (friendPlayerIds == null || friendPlayerIds.Count < 2)
        {
            return;
        }

        if (!runtime.Services.TryGetService<OnlineFriendService>(out var friends))
        {
            // Localization: Launcher.OnlineFriendBridgeFaceMissing - Online 好友域桥契约面不可解析（online-social 未装配或被排除），好友消息将显式失败
            LogHelper.Error(LocalizationService.GetString(Localization.Keys.Launcher.OnlineFriendBridgeFaceMissing));
            return;
        }

        var requesterId = friendPlayerIds[0];
        var requesterScope = new OnlineScope(runtime.TenantId, runtime.AppId, runtime.ServerId, requesterId);
        var established = 0;
        for (var index = 1; index < friendPlayerIds.Count; index++)
        {
            var friendPlayerId = friendPlayerIds[index];
            if (friendPlayerId <= 0 || friendPlayerId == requesterId)
            {
                continue;
            }

            var requested = await friends.RequestAsync(requesterScope, friendPlayerId).ConfigureAwait(false);
            if (!requested.IsSuccess)
            {
                // Localization: Hotfix.Friend.DomainBridgeFailed - 好友域桥返回失败, Code: {0}, Message: {1}, PlayerId: {2}
                LogHelper.Error(LocalizationService.GetString(Localization.Keys.Hotfix.Friend.DomainBridgeFailed, requested.Code, requested.Message, requesterId));
                continue;
            }

            var accepted = await friends.AcceptAsync(new OnlineScope(runtime.TenantId, runtime.AppId, runtime.ServerId, friendPlayerId), requested.Data.FriendshipId).ConfigureAwait(false);
            if (!accepted.IsSuccess && accepted.Code != OnlineErrorCode.StateOperationForbidden)
            {
                // 已建立（重复种子幂等命中既有关系）不算失败；其余失败按桥失败口径披露。
                // Localization: Hotfix.Friend.DomainBridgeFailed - 好友域桥返回失败, Code: {0}, Message: {1}, PlayerId: {2}
                LogHelper.Error(LocalizationService.GetString(Localization.Keys.Hotfix.Friend.DomainBridgeFailed, accepted.Code, accepted.Message, friendPlayerId));
                continue;
            }

            established++;
        }

        // Localization: Launcher.OnlineFriendBridgeSeeded - Online 好友查询桥验证种子已建立（仅供部署验证）：请求主体 {0}，种子好友数 {1}
        LogHelper.Warning(LocalizationService.GetString(Localization.Keys.Launcher.OnlineFriendBridgeSeeded, requesterId, established));
    }
}
