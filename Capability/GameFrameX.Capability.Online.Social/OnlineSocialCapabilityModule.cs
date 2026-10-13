// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and other rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and other international regulations.
//   使用本项目须严格遵守相关法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with relevant laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please refer to the full license text in the LICENSE file located at the root of the source code.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activity that endangers national security, disrupts social order,
//   侵犯公民合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of citizens as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development,
//   本组织不承担任何责任，开发者应自行承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Hosting.AdminApi;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Events;
using GameFrameX.Capability.Online.Abstractions.Scope;
using GameFrameX.Capability.Online.Foundation.Session;
using GameFrameX.Capability.Online.Social.AdminApi;

namespace GameFrameX.Capability.Online.Social;

/// <summary>
/// 社交能力（好友 / 群组 / 聊天 / 通知 / 处罚 / 举报裁决 + 聊天审计投影）的自装配模块（C231 P4 前为 P3 T4 迁入）。
/// <para>
/// 维护约束（服务共享协议）：模块无参构造（扫描可发现）；<c>Register</c> 自装配本域——六个 store 经
/// <see cref="CapabilityRegistrationContext.TryGetService{TService}"/> 探测替换缝，未命中构造 InMemory 默认；
/// 跨域硬依赖（事件出口 / Foundation 会话面）经上下文解析；对外产物（store 接口 + 裁决 / 处罚 / 通知服务 +
/// <see cref="IOnlineSocialGate"/> 裁决面 + 聊天审计投影）注册回共享面。
/// </para>
/// </summary>
/// <remarks>
/// The self-assembling module of the social capability (friends / groups / chat / notifications / punishments /
/// report adjudication plus the chat-audit projection). The module has a parameterless constructor (discoverable
/// by scanning); <c>Register</c> self-assembles the domain — the six stores probe the replacement seams via
/// <see cref="CapabilityRegistrationContext.TryGetService{TService}"/> with in-memory fallbacks; cross-domain
/// hard dependencies (the event outlet / the Foundation session face) resolve through the context; outward
/// products (store interfaces, the adjudication / punishment / notification services, the
/// <see cref="IOnlineSocialGate"/> face and the chat-audit projection) are registered back into the shared face.
/// </remarks>
public sealed class OnlineSocialCapabilityModule : ICapabilityModule, IAdminActionContributor
{
    /// <summary>
    /// 社交能力清单（Id 冻结 = 终态包名；action 集合与幂等包裹位逐字迁移自壳过渡模块，含 match_isolation）。
    /// </summary>
    public static readonly CapabilityManifest ManifestInstance = new(
        "online-social",
        "1.0.0",
        new[] { "kick_player", "revoke_token", "mute_player", "query_report_cases", "update_report_case", "send_admin_notification", "query_chat_messages", "match_isolation" },
        new[] { "online-foundation" });

    /// <summary>
    /// 社交裁决（禁言 / 举报 / 拉黑）。
    /// </summary>
    private OnlineSocialDecisionService _socialDecisions;

    /// <summary>
    /// 处罚（Mute / Ban 事实源）。
    /// </summary>
    private OnlinePunishmentService _punishments;

    /// <summary>
    /// 举报存储（Register 期解析或构造；handler 查询面用）。
    /// </summary>
    private IOnlineReportStore _reportStore;

    /// <summary>
    /// 通知。
    /// </summary>
    private OnlineNotificationService _notifications;

    /// <summary>
    /// 聊天审计投影（订阅事件总线的 chat 审计查询面）。
    /// </summary>
    private OnlineChatAuditProjection _chatAudit;

    /// <summary>
    /// 会话存储（Foundation 面；kick / revoke 逐会话执行）。
    /// </summary>
    private IOnlineSessionStore _sessionStore;

    /// <summary>
    /// 会话令牌（Foundation 面）。
    /// </summary>
    private OnlineSessionTokenService _tokens;

    /// <inheritdoc />
    public CapabilityManifest Manifest
    {
        get { return ManifestInstance; }
    }

    /// <inheritdoc />
    public void Register(CapabilityRegistrationContext context)
    {
        var eventPublisher = context.GetService<IOnlineEventPublisher>();
        _sessionStore = context.GetService<IOnlineSessionStore>();
        _tokens = context.GetService<OnlineSessionTokenService>();
        var chatStore = ResolveStore<IOnlineChatStore>(context, static () => new InMemoryOnlineChatStore());
        var friendStore = ResolveStore<IOnlineFriendStore>(context, static () => new InMemoryOnlineFriendStore());
        var groupStore = ResolveStore<IOnlineGroupStore>(context, static () => new InMemoryOnlineGroupStore());
        var notificationStore = ResolveStore<IOnlineNotificationStore>(context, static () => new InMemoryOnlineNotificationStore());
        _reportStore = ResolveStore<IOnlineReportStore>(context, static () => new InMemoryOnlineReportStore());
        var socialGraphStore = ResolveStore<IOnlineSocialGraphStore>(context, static () => new InMemoryOnlineSocialGraphStore());

        _socialDecisions = new OnlineSocialDecisionService(socialGraphStore, _reportStore, eventPublisher);
        _punishments = new OnlinePunishmentService(socialGraphStore, eventPublisher);
        _notifications = new OnlineNotificationService(notificationStore, eventPublisher);
        _chatAudit = new OnlineChatAuditProjection(chatStore);
        context.AddService(_socialDecisions);
        context.AddService(_punishments);
        context.AddService(_notifications);
        context.AddService(_chatAudit);
        context.AddService<IOnlineSocialGate>(_socialDecisions);

        var scope = context.GetService<OnlineScope>();
        context.AddTickJob("notification-expiry-sweep", async ct =>
        {
            await _notifications.SweepExpiredAsync(scope.TenantId, scope.AppId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), ct).ConfigureAwait(false);
        });
    }

    /// <inheritdoc />
    public void RegisterAdminActions(AdminActionRegistry registry)
    {
        var handlers = new OnlineAdminSocialHandlers(_sessionStore, _tokens, _punishments, _socialDecisions, _notifications, _reportStore, _chatAudit);
        handlers.Register(registry);
        handlers.RegisterMatchIsolation(registry);
    }

    /// <summary>
    /// 解析存储（替换缝探测——命中用组合根注入实例，未命中构造 InMemory 默认并注册回共享面）。
    /// </summary>
    /// <typeparam name="TStore">存储接口。</typeparam>
    /// <param name="context">注册上下文。</param>
    /// <param name="defaultFactory">默认构造。</param>
    /// <returns>存储实例。</returns>
    private static TStore ResolveStore<TStore>(CapabilityRegistrationContext context, Func<TStore> defaultFactory)
        where TStore : class
    {
        if (context.TryGetService(out TStore store))
        {
            return store;
        }

        store = defaultFactory();
        context.AddService(store);
        return store;
    }
}
