//  ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please refer to the LICENSE file at the source code root for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯公民合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any disputes and liabilities arising from secondary development,
//   本组织不承担任何责任，开发者须自行承担。
//   The organization shall not be responsible for any disputes; developers shall bear the responsibility.
//  ==========================================================================================

using System;
using System.Threading.Tasks;
using GameFrameX.Capability.Hosting.Core;
using GameFrameX.Capability.Online.Abstractions.Bridges;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Events;
using GameFrameX.Capability.Online.Abstractions.Scope;
using GameFrameX.Capability.Online.Matchmaking;
using GameFrameX.Core.Session;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Proto.Proto;
using GameFrameX.Utility;

namespace GameFrameX.Launcher.Online;

/// <summary>
/// 匹配域桥组合根接线（C246，沿 C244 <see cref="OnlineFriendDomainBridgeWiring"/> 形态）：
/// Game 型启动链的域桥生命周期编排——业务运行时启动后发布租约、订阅撮合分配事件做匹配成功通知推送，
/// 进程停止路径撤销租约。
/// <para>
/// 维护约束：发布 / 撤销必须成对出现在同一组合根（AppStartUpGame）；契约面或作用域缺席
///（online-match / online-matchmaking 被排除 / 未装配）按 Error 披露并返回 null（不抛出——缺席态由
/// 420 匹配消息层显式失败承载，无回落目标）；通知推送 best-effort——离线方无会话即静默跳过，
/// 经状态轮询兜底；订阅体异常吞掉不阻断事件分发（C245 排行投影订阅先例）。
/// </para>
/// </summary>
/// <remarks>
/// The match-domain bridge composition-root wiring (C246, following the C244 friend-domain shape):
/// the bridge lifecycle orchestration for the Game-shape startup chain — lease publication after the
/// business runtime starts, an assignment-event subscription for the match-made notification push, and
/// lease retraction on the shutdown path. Publish and retract stay paired inside the same composition
/// root (AppStartUpGame); a missing contract face or scope (online-match / online-matchmaking excluded)
/// discloses an Error and returns null without throwing (the absence state is carried by explicit
/// domain-420 message failures — there is no fallback target); the notification push is best-effort —
/// offline players without sessions are skipped silently and fall back to status polling; subscriber
/// exceptions are swallowed so event dispatch never breaks (the C245 leaderboard-projection precedent).
/// </remarks>
public static class OnlineMatchDomainBridgeWiring
{
    /// <summary>
    /// 发布匹配域桥租约（业务运行时启动后调用）：从宿主共享面解析域契约面与授权作用域并发布；
    /// 契约面或作用域缺席按 Error 披露并返回 null（不抛出）。
    /// </summary>
    /// <param name="runtime">业务能力宿主。</param>
    /// <returns>发布的租约；契约面缺席返回 null。</returns>
    public static OnlineMatchDomainBridgeLease PublishMatchDomainBridge(CapabilityHost runtime)
    {
        if (!runtime.Services.TryGetService<IOnlineMatchDomain>(out var domain) || !runtime.Services.TryGetService<OnlineScope>(out var authorizedScope))
        {
            // Localization: Launcher.OnlineMatchDomainBridgeFaceMissing - Online 匹配域桥契约面不可解析（online-match/matchmaking 未装配或被排除），420 匹配消息将显式失败
            LogHelper.Error(LocalizationService.GetString(Localization.Keys.Launcher.OnlineMatchDomainBridgeFaceMissing));
            return null;
        }

        var lease = OnlineMatchDomainBridgeHolder.Publish(domain, authorizedScope);
        // Localization: Launcher.OnlineMatchDomainBridgePublished - Online 匹配域桥已发布（租户 {0}，应用 {1}，区服 {2}）...
        LogHelper.Info(LocalizationService.GetString(Localization.Keys.Launcher.OnlineMatchDomainBridgePublished, authorizedScope.TenantId, authorizedScope.AppId, authorizedScope.ServerId));
        return lease;
    }

    /// <summary>
    /// 订阅撮合分配事件并推送匹配成功通知（业务运行时启动后调用）：
    /// <c>Online.Match.AssignmentCreated</c> → 域服务解析数值对局标识 → 向双方在线会话 best-effort 推送
    /// <see cref="NotifyMatchMade"/>。事件总线或契约面缺席按 Error 披露并跳过（不抛出）。
    /// </summary>
    /// <param name="runtime">业务能力宿主。</param>
    public static void SubscribeMatchMadeNotifications(CapabilityHost runtime)
    {
        var eventBus = runtime.Services.GetService<GameFrameX.Foundation.Idempotency.InMemoryEventPublisher>();
        if (!runtime.Services.TryGetService<IOnlineMatchDomain>(out var domain))
        {
            eventBus = null;
        }

        if (eventBus == null || domain == null)
        {
            // Localization: Launcher.OnlineMatchDomainBridgeNotifyMissing - Online 匹配成功通知未装配（事件总线或契约面不可解析），等待方经状态查询兜底
            LogHelper.Error(LocalizationService.GetString(Localization.Keys.Launcher.OnlineMatchDomainBridgeNotifyMissing));
            return;
        }

        eventBus.Subscribe(envelope => HandleMatchAssignmentEvent(domain, envelope));
    }

    /// <summary>
    /// 匹配分配事件回调体（订阅入口见 <see cref="SubscribeMatchMadeNotifications"/>）：
    /// <c>Online.Match.AssignmentCreated</c> → 域服务解析数值对局标识 → 向双方在线会话 best-effort 推送
    /// <see cref="NotifyMatchMade"/>；过滤/校验失败即静默跳过，订阅体异常吞掉不阻断事件分发。
    /// </summary>
    /// <param name="domain">匹配域契约面。</param>
    /// <param name="envelope">Foundation 事件信封。</param>
    private static void HandleMatchAssignmentEvent(IOnlineMatchDomain domain, GameFrameX.Foundation.Idempotency.EventEnvelope envelope)
    {
        try
        {
            var onlineEvent = OnlineEventEnvelopeMapper.FromEnvelope(envelope);
            if (onlineEvent == null || onlineEvent.EventType != OnlineMatchEvents.AssignmentCreated)
            {
                return;
            }

            var payload = OnlineMatchEvents.ReadAssignmentCreated(onlineEvent);
            if (payload == null || string.IsNullOrEmpty(payload.AssignmentId))
            {
                return;
            }

            var resolved = domain.ResolveAssignmentAsync(onlineEvent.TenantId, onlineEvent.AppId, payload.AssignmentId).GetAwaiter().GetResult();
            if (!resolved.IsSuccess || resolved.Data == null || resolved.Data.MatchId == 0)
            {
                return;
            }

            var notify = new NotifyMatchMade { MatchId = resolved.Data.MatchId, Mode = resolved.Data.Mode };
            if (resolved.Data.PlayerIds == null)
            {
                return;
            }

            foreach (var playerId in resolved.Data.PlayerIds)
            {
                var session = PlayerSessionManager.Instance.GetByPlayerId(playerId);
                if (session == null)
                {
                    // 离线方静默跳过：best-effort 推送，等待方经状态轮询兜底。
                    continue;
                }

                session.WriteAsync(notify).GetAwaiter().GetResult();
            }
        }
        catch (Exception)
        {
            // 订阅体异常不得阻断业务事件分发（InMemoryEventPublisher 异常策略向上传播，此处吞掉；
            // 通知是 best-effort 增益路径，失败不影响撮合事实，等待方经状态查询兜底）。
        }
    }

    /// <summary>
    /// 撤销匹配域桥租约（进程停止路径调用；未发布时为空操作）。
    /// </summary>
    public static void RetractMatchDomainBridge()
    {
        if (!OnlineMatchDomainBridgeHolder.IsPublished)
        {
            return;
        }

        OnlineMatchDomainBridgeHolder.Retract();
        // Localization: Launcher.OnlineMatchDomainBridgeRetracted - Online 匹配域桥已撤销...
        LogHelper.Info(LocalizationService.GetString(Localization.Keys.Launcher.OnlineMatchDomainBridgeRetracted));
    }
}
