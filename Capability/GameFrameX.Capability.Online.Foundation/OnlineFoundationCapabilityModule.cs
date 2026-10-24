// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the PRC and relevant international regulations.
//   使用本项目须严格遵守相关法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file at the root of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development,
//   本组织与贡献者概不承担任何责任。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://github.com/GameFrameX
//   CNB  仓库：https://www.cnb.cool/GameFrameX
//   CNB Repository: https://github.com/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Hosting.Core;
using GameFrameX.Capability.Hosting.AdminApi;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Events;
using GameFrameX.Capability.Online.Foundation.AdminApi;
using GameFrameX.Capability.Online.Foundation.Identity;
using GameFrameX.Capability.Online.Foundation.Presence;
using GameFrameX.Capability.Online.Abstractions.Scope;
using GameFrameX.Capability.Online.Foundation.Session;

namespace GameFrameX.Capability.Online.Foundation;

/// <summary>
/// 平台地基能力（Identity / Session / Token / Presence 四域的服务与清扫）的自装配模块
/// （C232 P6 迁入；C294 改写为三段生命周期机制模块）。
/// <para>
/// 维护约束（服务共享协议）：模块无参构造（<see cref="CapabilityAssembly"/> 扫描可发现）；
/// <c>Register</c> 自装配本域——三 store 以懒默认注册后取用（族 / 宿主 Override 自动胜出，模块不做
/// 探测回退），四个域服务（身份 / 会话管理 / 会话令牌 / 在线状态）构造后注册回共享面供后序能力取用；
/// 两个 Presence 探针实现以默认注册供消费方 Activate 期枚举；三个清扫滴答 job 归属地基域。
/// <c>ActivateAsync</c> 发布两座桥：授权作用域提供者（透出族预置 <see cref="OnlineScope"/>）与
/// 会话恢复契约；<c>DeactivateAsync</c> 无自持资源，默认空实现。
/// 玩家 KV 存储（Storage 域）为壳时代死装配（构造无消费），本模块仍不装配，消费方出现时再随装配演进
/// （<see cref="OnlineFoundationOptions.PlayerStorage"/> 已披露其参数面）。
/// </para>
/// </summary>
/// <remarks>
/// The self-assembling module of the platform foundation capability (identity / session / token /
/// presence domains and their sweeps; migrated by C232 P6, rewritten by C294 for the three-stage
/// lifecycle). The module has a parameterless constructor (discoverable by
/// <see cref="CapabilityAssembly"/> scanning); <c>Register</c> self-assembles the domain — the three
/// stores register lazy in-memory defaults and resolve through the shared face (family / host
/// overrides win automatically, no probing fallback), the four domain services are constructed and
/// registered back for later capabilities, the two presence probes register as defaults for
/// Activate-phase enumeration, and the three sweep tick jobs belong to the foundation domain.
/// <c>ActivateAsync</c> publishes two bridges: the authorized scope provider (exposing the
/// family-preset <see cref="OnlineScope"/>) and the session reconnect contract;
/// <c>DeactivateAsync</c> holds no resources of its own.
/// The player key-value storage stays unassembled (a dead assembly in the shell era, disclosed via
/// <see cref="OnlineFoundationOptions.PlayerStorage"/>).
/// </remarks>
public sealed class OnlineFoundationCapabilityModule : ICapabilityModule, IAdminActionContributor
{
    /// <summary>
    /// 平台地基能力清单（Id 冻结 = 终态包名；action 集合与幂等包裹位逐字迁移自壳过渡模块；
    /// Options 根类型 = <see cref="OnlineFoundationOptions"/>，无依赖声明）。
    /// </summary>
    public static readonly CapabilityManifest ManifestInstance = new(
        "online-foundation",
        "1.0.0",
        new[] { "suspend_resume_player", },
        null,
        null,
        typeof(OnlineFoundationOptions));

    /// <summary>
    /// 会话令牌（Register 期构造；清扫滴答 job 与 admin 管制命令的消费面）。
    /// </summary>
    private OnlineSessionTokenService _tokens;

    /// <summary>
    /// 会话生命周期管理器（Register 期构造；C204 会话中介恢复与重连窗清扫的消费面）。
    /// </summary>
    private OnlineSessionManager _sessions;

    /// <summary>
    /// 在线状态（Register 期构造；清扫滴答 job、admin 管制命令与 Presence 探针的消费面）。
    /// </summary>
    private OnlinePresenceService _presence;

    /// <inheritdoc />
    public CapabilityManifest Manifest
    {
        get { return ManifestInstance; }
    }

    /// <inheritdoc />
    public void Register(CapabilityRegistrationContext context)
    {
        var eventPublisher = context.GetService<IOnlineEventPublisher>();
        var scope = context.GetService<OnlineScope>();

        // 三 store：懒默认注册后经共享面取用（族 / 宿主 GameDb Override 自动胜出；Operations 总览 / 时间线取同一实例）。
        context.AddService<IOnlineIdentityStore>(static _ => new InMemoryOnlineIdentityStore());
        context.AddService<IOnlinePresenceStore>(static _ => new InMemoryOnlinePresenceStore());
        context.AddService<IOnlineSessionStore>(static _ => new InMemoryOnlineSessionStore());
        var identityStore = context.GetService<IOnlineIdentityStore>();
        var presenceStore = context.GetService<IOnlinePresenceStore>();
        var sessionStore = context.GetService<IOnlineSessionStore>();

        // 四域服务构造并注册回共享面（与壳时代集中 new 逐语句等价）。
        context.AddService(new OnlineIdentityService(identityStore));
        _sessions = new OnlineSessionManager(sessionStore, eventPublisher);
        context.AddService(_sessions);
        _tokens = new OnlineSessionTokenService(sessionStore, eventPublisher);
        context.AddService(_tokens);
        _presence = new OnlinePresenceService(presenceStore, eventPublisher);
        context.AddService(_presence);

        // 会话恢复服务（C204）：Core 物理连接会话与 Online 逻辑会话协同的契约面实现，注册后在 Activate 期发桥。
        context.AddService<IOnlineSessionReconnectContract>(new OnlineSessionReconnectService(_tokens, _sessions, sessionStore));

        // Presence 探针（C294 契约迁入 Abstractions.Contracts）：包装本域在线状态读取，默认注册供
        // Social / Matchmaking 等消费方 Activate 期 GetServices 枚举。
        context.AddService<IOnlineSocialPresenceProbe>(new OnlineSocialPresenceProbe(_presence));
        context.AddService<IOnlinePartyPresenceProbe>(new OnlinePartyPresenceProbe(_presence));

        // 清扫滴答 job（归属地基域；作用域三元组经族预置的授权作用域解析——壳时代 host.AuthorizedScope 等价）。
        context.AddTickJob("presence-timeout-sweep", async ct =>
        {
            await _presence.SweepTimeoutsAsync(scope.TenantId, scope.AppId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), ct).ConfigureAwait(false);
        });
        context.AddTickJob("session-token-expiry-sweep", ct => _tokens.SweepExpiredAsync(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), ct));

        // 会话重连窗清扫（C204）：Reconnecting 超窗 → Closed（ReconnectWindowExpired），无永久 Reconnecting。
        context.AddTickJob("session-reconnect-window-sweep", ct => _sessions.SweepAsync(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), ct));
    }

    /// <inheritdoc />
    public Task ActivateAsync(CapabilityActivationContext context, CancellationToken cancellationToken)
    {
        // 授权作用域桥：透出族基建预置的 OnlineScope 三元组（全宿主唯一发布者；Hotfix 侧 TryAcquire 失败即显式 8002）。
        context.Bridges.Publish<IOnlineAuthorizedScopeProvider>(new OnlineAuthorizedScopeProvider(context.Services.GetService<OnlineScope>()));

        // 会话恢复桥：C204 契约面（Register 期注册的实现实例）。
        context.Bridges.Publish<IOnlineSessionReconnectContract>(context.Services.GetService<IOnlineSessionReconnectContract>());
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeactivateAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void RegisterAdminActions(AdminActionRegistry registry)
    {
        OnlineAdminFoundationHandlers.RegisterSuspendResume(registry, _presence);
    }
}
