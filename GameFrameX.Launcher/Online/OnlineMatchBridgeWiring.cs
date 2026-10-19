// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and applicable international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织不承担任何责任，开发者应自行承担。
//   The organization and contributors shall not be responsible; developers shall bear the responsibility themselves.
//  ==========================================================================================

using System.Threading.Tasks;
using GameFrameX.Capability.Hosting.Core;
using GameFrameX.Capability.Online.Abstractions.Bridges;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Scope;
using GameFrameX.Capability.Online.Competition.Leaderboard;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Utility.Setting;

namespace GameFrameX.Launcher.Online;

/// <summary>
/// 对局样例桥组合根接线（C245，D4 RPS 垂直切片；沿 C243 <see cref="OnlineFriendBridgeWiring"/> 形态）：
/// Game 型启动链的桥接生命周期编排——组合前 fail-fast、业务运行时启动后发布租约与确保写榜目标榜单、
/// 进程停止路径撤销租约。
/// <para>
/// 维护约束：发布 / 撤销必须成对出现在同一组合根（AppStartUpGame）；不支持拓扑（业务运行时关、作用域缺失、
/// online-match 能力被过滤器排除）一律启动期显式失败，不降级、不空转；榜单确保经真实
/// <see cref="OnlineLeaderboardService"/> 写路径执行（已存在幂等跳过），非空配置但 online-competition 能力
/// 被排除视为配置矛盾（启动终止）。
/// </para>
/// </summary>
/// <remarks>
/// The sample match bridge composition-root wiring (C245, the D4 RPS vertical slice; following the
/// C243 <see cref="OnlineFriendBridgeWiring"/> shape): the bridge lifecycle orchestration for the
/// Game-shape startup chain — fail-fast before assembly, lease publication plus leaderboard-board
/// ensuring after the business runtime starts, and lease retraction on the shutdown path. Publish
/// and retract must stay paired inside the same composition root (AppStartUpGame); unsupported
/// topologies (runtime gate off, scope missing, online-match excluded by filters) fail explicitly
/// at startup — no degradation, no silent idle. The board ensure goes through the real
/// <see cref="OnlineLeaderboardService"/> write path (idempotent skip when the board exists);
/// a non-empty board id with the online-competition capability excluded is a configuration
/// contradiction (startup aborted).
/// </remarks>
public static class OnlineMatchBridgeWiring
{
    /// <summary>
    /// 校验对局桥配置（组合前 fail-fast）：开关开启要求业务运行时开关开启且授权作用域三元组完整；
    /// 开关关闭（缺省）恒通过——旧行为边界不动。
    /// </summary>
    /// <param name="setting">进程设置。</param>
    /// <exception cref="InvalidOperationException">开关与拓扑矛盾时抛出。</exception>
    public static void EnsureMatchGameBridgeConfigured(AppSetting setting)
    {
        if (setting?.Online?.EnableMatchGameBridge != true)
        {
            return;
        }

        if (!setting.IsEnableOnline)
        {
            throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.Launcher.OnlineMatchBridgeRuntimeGateMissing));
        }

        if (setting.OnlineTenantId <= 0 || setting.OnlineAppId <= 0 || setting.ServerId <= 0)
        {
            throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.Launcher.OnlineMatchBridgeScopeMissing));
        }
    }

    /// <summary>
    /// 发布对局桥租约（业务运行时启动后调用）：从宿主共享面解析契约面与授权作用域并发布；
    /// 契约面缺失（online-match 被排除 / 未装配）即启动期显式失败。
    /// </summary>
    /// <param name="runtime">业务能力宿主。</param>
    /// <returns>发布的租约。</returns>
    /// <exception cref="InvalidOperationException">契约面或授权作用域不可解析时抛出。</exception>
    public static OnlineMatchGameBridgeLease PublishMatchGameBridge(CapabilityHost runtime)
    {
        var bridge = runtime.Services.GetService<IOnlineMatchGameBridge>();
        var authorizedScope = runtime.Services.GetService<OnlineScope>();
        if (bridge == null || authorizedScope == null)
        {
            throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.Launcher.OnlineMatchBridgeFaceMissing));
        }

        var lease = OnlineMatchGameBridgeHolder.Publish(bridge, authorizedScope);
        // Localization: Launcher.OnlineMatchBridgePublished - Online 对局桥已发布（租户 {0}，应用 {1}，区服 {2}）...
        LogHelper.Info(LocalizationService.GetString(Localization.Keys.Launcher.OnlineMatchBridgePublished, authorizedScope.TenantId, authorizedScope.AppId, authorizedScope.ServerId));
        return lease;
    }

    /// <summary>
    /// 确保对局桥写榜目标榜单存在（业务运行时启动后调用；榜单标识空白为空操作）：经真实
    /// <see cref="OnlineLeaderboardService"/> 查询-创建路径执行——已存在幂等跳过，不存在则创建样例榜单
    ///（累计策略 = 胜局总和，降序）。榜单服务不可解析（online-competition 被排除 / 未装配）或创建失败
    /// 均为配置矛盾，启动期显式失败。
    /// </summary>
    /// <param name="runtime">业务能力宿主。</param>
    /// <param name="leaderboardId">榜单标识（空白 = 不写榜，空操作）。</param>
    /// <returns>表示异步操作的任务 / A task representing the asynchronous operation</returns>
    /// <exception cref="InvalidOperationException">榜单服务不可解析或确保失败时抛出。</exception>
    public static async Task EnsureMatchLeaderboardBoardAsync(CapabilityHost runtime, string leaderboardId)
    {
        if (string.IsNullOrWhiteSpace(leaderboardId))
        {
            return;
        }

        var leaderboards = runtime.Services.GetService<OnlineLeaderboardService>();
        var authorizedScope = runtime.Services.GetService<OnlineScope>();
        if (leaderboards == null || authorizedScope == null)
        {
            throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.Launcher.OnlineMatchBridgeFaceMissing));
        }

        var probe = await leaderboards.GetTopAsync(authorizedScope, new OnlineLeaderboardTopQuery
        {
            LeaderboardId = leaderboardId,
            Count = 1,
        }).ConfigureAwait(false);
        if (probe.IsSuccess)
        {
            return;
        }

        if (probe.Code != OnlineErrorCode.ResourceNotFound)
        {
            throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.Launcher.OnlineMatchBridgeBoardEnsureFailed, leaderboardId, (int)probe.Code, probe.Message));
        }

        var created = await leaderboards.CreateAsync(authorizedScope, new OnlineLeaderboardCreateRequest
        {
            LeaderboardId = leaderboardId,
            SortOrder = OnlineLeaderboardSortOrder.Descending,
            ScoreUpdatePolicy = OnlineLeaderboardScoreUpdatePolicy.Sum,
        }).ConfigureAwait(false);
        if (!created.IsSuccess && created.Code != OnlineErrorCode.ParameterInvalid)
        {
            throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.Launcher.OnlineMatchBridgeBoardEnsureFailed, leaderboardId, (int)created.Code, created.Message));
        }

        // Localization: Launcher.OnlineMatchBridgeBoardEnsured - Online 对局桥榜单已确保存在（仅供样例验证）：{0}
        LogHelper.Warning(LocalizationService.GetString(Localization.Keys.Launcher.OnlineMatchBridgeBoardEnsured, leaderboardId));
    }

    /// <summary>
    /// 撤销对局桥租约（进程停止路径调用；未发布时为空操作）。
    /// </summary>
    public static void RetractMatchGameBridge()
    {
        if (!OnlineMatchGameBridgeHolder.IsPublished)
        {
            return;
        }

        OnlineMatchGameBridgeHolder.Retract();
        // Localization: Launcher.OnlineMatchBridgeRetracted - Online 对局桥已撤销...
        LogHelper.Info(LocalizationService.GetString(Localization.Keys.Launcher.OnlineMatchBridgeRetracted));
    }
}
