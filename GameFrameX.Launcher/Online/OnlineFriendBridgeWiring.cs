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

using System.Collections.Generic;
using System.Threading.Tasks;
using GameFrameX.Capability.Hosting.Core;
using GameFrameX.Capability.Online.Abstractions.Bridges;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Scope;
using GameFrameX.Capability.Online.Social;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Utility.Setting;

namespace GameFrameX.Launcher.Online;

/// <summary>
/// 好友查询桥组合根接线（C243）：Game 型启动链的桥接生命周期编排——
/// 可选验证种子（经真实 <see cref="OnlineFriendService"/> 写路径预置非空好友状态）。
/// <para>
/// 维护约束：发布 / 撤销必须成对出现在同一组合根（AppStartUpGame）；
/// 不支持拓扑（业务运行时关、作用域缺失、online-social 能力被过滤器排除）一律启动期显式失败，
/// 不降级、不空转；种子仅供部署验证，生产常开须知情（Warning 披露）。
/// </para>
/// </summary>
/// <remarks>
/// The friend-query bridge composition-root wiring (C243): the bridge lifecycle orchestration for the
/// Game-shape startup chain — fail-fast before assembly (a switch contradicting the topology aborts
/// startup), lease publication after the business runtime starts, retraction on the shutdown path,
/// and the optional verification seed (presetting non-empty friend state through the real
/// <see cref="OnlineFriendService"/> write path). Publish and retract must stay paired inside the
/// same composition root (AppStartUpGame); unsupported topologies (runtime gate off, scope missing,
/// online-social excluded by filters) fail explicitly at startup — no degradation, no silent idle;
/// the seed is for deployment verification only and keeping it on in production requires informed
/// consent (disclosed by a Warning).
/// </remarks>
public static class OnlineFriendBridgeWiring
{
    /// <summary>
    /// 校验好友查询桥配置（组合前 fail-fast）：开关开启要求业务运行时开关开启且授权作用域三元组完整；
    /// 开关关闭（缺省）恒通过——旧行为边界不动。
    /// </summary>
    /// <param name="setting">进程设置。</param>
    /// <exception cref="InvalidOperationException">开关与拓扑矛盾时抛出。</exception>
    public static void EnsureFriendQueryBridgeConfigured(AppSetting setting)
    {
        if (setting?.Online?.EnableFriendQueryBridge != true)
        {
            return;
        }

        if (!setting.IsEnableOnline)
        {
            throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.Launcher.OnlineFriendBridgeRuntimeGateMissing));
        }

        if (setting.OnlineTenantId <= 0 || setting.OnlineAppId <= 0 || setting.ServerId <= 0)
        {
            throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.Launcher.OnlineFriendBridgeScopeMissing));
        }
    }

    /// <summary>
    /// 发布好友查询桥租约（业务运行时启动后调用）：从宿主共享面解析契约面与授权作用域并发布；
    /// 契约面缺失（online-social 被排除 / 未装配）即启动期显式失败。
    /// </summary>
    /// <param name="runtime">业务能力宿主。</param>
    /// <returns>发布的租约。</returns>
    /// <exception cref="InvalidOperationException">契约面或授权作用域不可解析时抛出。</exception>
    public static OnlineFriendBridgeLease PublishFriendQueryBridge(CapabilityHost runtime)
    {
        var query = runtime.Services.GetService<IOnlineFriendQuery>();
        var authorizedScope = runtime.Services.GetService<OnlineScope>();
        if (query == null || authorizedScope == null)
        {
            throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.Launcher.OnlineFriendBridgeFaceMissing));
        }

        var lease = OnlineFriendBridgeHolder.Publish(query, authorizedScope);
        // Localization: Launcher.OnlineFriendBridgePublished - Online 好友查询桥已发布（租户 {0}，应用 {1}，区服 {2}）...
        LogHelper.Info(LocalizationService.GetString(Localization.Keys.Launcher.OnlineFriendBridgePublished, authorizedScope.TenantId, authorizedScope.AppId, authorizedScope.ServerId));
        return lease;
    }

    /// <summary>
    /// 撤销好友查询桥租约（进程停止路径调用；未发布时为空操作）。
    /// </summary>
    public static void RetractFriendQueryBridge()
    {
        if (!OnlineFriendBridgeHolder.IsPublished)
        {
            return;
        }

        OnlineFriendBridgeHolder.Retract();
        // Localization: Launcher.OnlineFriendBridgeRetracted - Online 好友查询桥已撤销...
        LogHelper.Info(LocalizationService.GetString(Localization.Keys.Launcher.OnlineFriendBridgeRetracted));
    }

    /// <summary>
    /// 建立好友查询桥验证种子（业务运行时启动后调用；空 / 单元素列表为空操作）：
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
    /// <exception cref="InvalidOperationException">种子已配置但好友域服务不可解析时抛出（配置矛盾）。</exception>
    public static async Task SeedFriendQueryBridgeVerificationAsync(CapabilityHost runtime, IReadOnlyList<long> friendPlayerIds)
    {
        if (friendPlayerIds == null || friendPlayerIds.Count < 2)
        {
            return;
        }

        var friends = runtime.Services.GetService<OnlineFriendService>();
        if (friends == null)
        {
            throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.Launcher.OnlineFriendBridgeFaceMissing));
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
                // Localization: Hotfix.Friend.FriendListBridgeQueryFailed - 好友查询桥返回失败, Code: {0}, Message: {1}, ActorId: {2}
                LogHelper.Error(LocalizationService.GetString(Localization.Keys.Hotfix.Friend.FriendListBridgeQueryFailed, requested.Code, requested.Message, requesterId));
                continue;
            }

            var accepted = await friends.AcceptAsync(new OnlineScope(runtime.TenantId, runtime.AppId, runtime.ServerId, friendPlayerId), requested.Data.FriendshipId).ConfigureAwait(false);
            if (!accepted.IsSuccess && accepted.Code != OnlineErrorCode.StateOperationForbidden)
            {
                // 已建立（重复种子幂等命中既有关系）不算失败；其余失败按桥失败口径披露。
                // Localization: Hotfix.Friend.FriendListBridgeQueryFailed - 好友查询桥返回失败, Code: {0}, Message: {1}, ActorId: {2}
                LogHelper.Error(LocalizationService.GetString(Localization.Keys.Hotfix.Friend.FriendListBridgeQueryFailed, accepted.Code, accepted.Message, friendPlayerId));
                continue;
            }

            established++;
        }

        // Localization: Launcher.OnlineFriendBridgeSeeded - Online 好友查询桥验证种子已建立（仅供部署验证）：请求主体 {0}，种子好友数 {1}
        LogHelper.Warning(LocalizationService.GetString(Localization.Keys.Launcher.OnlineFriendBridgeSeeded, requesterId, established));
    }
}
