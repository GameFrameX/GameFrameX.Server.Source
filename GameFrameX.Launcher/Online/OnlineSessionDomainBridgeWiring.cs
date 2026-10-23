//  ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and other related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws and regulations of the People's Republic of China and international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file at the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯公民合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations.

using System;
using GameFrameX.Capability.Hosting.Core;
using GameFrameX.Capability.Online.Abstractions.Bridges;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Scope;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Foundation.Logger;

namespace GameFrameX.Launcher.Online;

/// <summary>
/// 会话域桥组合根接线（沿 <see cref="OnlineMatchDomainBridgeWiring"/> 形态）：
/// Game 型启动链的会话域桥生命周期编排——业务运行时启动后发布租约，进程停止路径撤销租约。
/// <para>
/// 维护约束：发布 / 撤销必须成对出现在同一组合根（AppStartUpGame）；契约面或作用域缺席
///（online-foundation 被排除 / 未装配）按 Error 披露并返回 null（不抛出——缺席态由登录不下发凭据、
/// 恢复请求显式失败承载，无回落目标）。
/// </para>
/// </summary>
/// <remarks>
/// The session-domain bridge composition-root wiring (following the match-domain shape):
/// lease publication after the business runtime starts and retraction on the shutdown path. Publish and
/// retract stay paired inside the same composition root (AppStartUpGame); a missing contract face or
/// scope (online-foundation excluded) discloses an Error and returns null without throwing — the absence
/// state is carried by login issuing no credential and resume requests failing explicitly (there is no
/// fallback target).
/// </remarks>
public static class OnlineSessionDomainBridgeWiring
{
    /// <summary>
    /// 发布会话域桥租约（业务运行时启动后调用）：从宿主共享面解析会话恢复契约面与授权作用域并发布；
    /// 契约面或作用域缺席按 Error 披露并返回 null（不抛出）。
    /// </summary>
    /// <param name="runtime">业务能力宿主。</param>
    /// <returns>发布的租约；契约面缺席返回 null。</returns>
    public static OnlineSessionDomainBridgeLease PublishSessionDomainBridge(CapabilityHost runtime)
    {
        if (!runtime.Services.TryGetService<IOnlineSessionReconnectContract>(out var contract) || !runtime.Services.TryGetService<OnlineScope>(out var authorizedScope))
        {
            // Localization: Launcher.OnlineSessionDomainBridgeFaceMissing - Online 会话域桥契约面不可解析（online-foundation 未装配或被排除），会话恢复不可用
            LogHelper.Error(LocalizationService.GetString(Localization.Keys.Launcher.OnlineSessionDomainBridgeFaceMissing));
            return null;
        }

        var lease = OnlineSessionDomainBridgeHolder.Publish(contract, authorizedScope);
        // Localization: Launcher.OnlineSessionDomainBridgePublished - Online 会话域桥已发布（租户 {0}，应用 {1}，区服 {2}）
        LogHelper.Info(LocalizationService.GetString(Localization.Keys.Launcher.OnlineSessionDomainBridgePublished, authorizedScope.TenantId, authorizedScope.AppId, authorizedScope.ServerId));
        return lease;
    }

    /// <summary>
    /// 撤销会话域桥租约（进程停止路径调用；未发布时为空操作）。
    /// </summary>
    public static void RetractSessionDomainBridge()
    {
        if (!OnlineSessionDomainBridgeHolder.IsPublished)
        {
            return;
        }

        OnlineSessionDomainBridgeHolder.Retract();
        // Localization: Launcher.OnlineSessionDomainBridgeRetracted - Online 会话域桥已撤销
        LogHelper.Info(LocalizationService.GetString(Localization.Keys.Launcher.OnlineSessionDomainBridgeRetracted));
    }
}
