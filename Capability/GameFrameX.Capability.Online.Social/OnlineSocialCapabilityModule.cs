// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and other rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and other international regulations.
//   使用本项目须严格遵守相关法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the full license text in the LICENSE file located at the root of the source code.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activity that endangers national security, disrupts social order,
//   侵犯公民合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others as prohibited by laws and regulations!
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
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.Collections.Generic;
using GameFrameX.Capability.Online.Abstractions;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Hosting.AdminApi;
using GameFrameX.Capability.Online.Abstractions.Events;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Scope;
using GameFrameX.Capability.Online.Foundation.Session;
using GameFrameX.Capability.Online.Social.AdminApi;

namespace GameFrameX.Capability.Online.Social;

/// <summary>
/// 社交能力（好友 / 群组 / 聊天 / 通知 / 处罚 / 举报裁决 + 聊天审计投影）的自装配模块（C294 机制化改写）。
/// <para>
/// 维护约束（三段生命周期）：模块无参构造（扫描可发现）；<c>Register</c> 自装配本域——六个 store 经
/// 懒默认注册 + <c>GetService</c> 解析（族 / 宿主 Override 恒胜出，模块不感知）；
/// <see cref="IOnlinePlayerDirectory"/> 玩家目录经 <c>RequireHostService</c> 声明宿主扩展点（缺失即装配期
/// fail-fast）；<see cref="IOnlineSocialPresenceProbe"/> 在线探针经 <c>GetService</c> 解析（必选
/// online-foundation 提供）；跨域硬依赖（事件出口 / Foundation 会话面）经上下文解析；对外产物（store 接口 +
/// 好友 / 裁决 / 处罚 / 通知 / 聊天 / 群组服务 + <see cref="IOnlineSocialGate"/> 裁决面 + 聊天审计投影）注册回共享面；
/// 两个过期清扫滴答 job（通知 + 好友请求）经族基建 <see cref="OnlineScope"/> 锚定租户 / App。
/// </para>
/// <para>
/// <c>ActivateAsync</c>：发布 <see cref="IOnlineFriendDomain"/> 好友域桥契约面（<see cref="OnlineFriendDomainFace"/>
/// 薄编排实现）；枚举 <see cref="IOnlineChannelMembershipProbe"/> 扩展点注入聊天服务（群组探针以 "group"
/// 命名注册，兄弟域如 matchmaking 的 "party" 探针同面注入）；按 <see cref="OnlineFriendOptions.MigrateLegacyFriendData"/>
/// 触发旧好友数据一次性迁移（<see cref="OnlineFriendLegacyMigration"/>）。
/// </para>
/// <para>
/// 钩子声明：<see cref="IOnlineChatModerationHook"/> 聊天内容审核扩展点以默认放行策略声明
///（<see cref="OnlineChatModerationAllowAllHook"/>），热更侧经 <c>CapabilityHook&lt;T&gt;.Attach</c> 原子替换。
/// </para>
/// </summary>
/// <remarks>
/// The self-assembling module of the social capability (friends / groups / chat / notifications / punishments /
/// report adjudication plus the chat-audit projection), rewritten onto the three-phase capability mechanism (C294).
/// <c>Register</c> self-assembles the domain — the six stores go through lazy-default registration plus
/// <c>GetService</c> resolution (family / host overrides always win); the player directory is declared as a host
/// extension point via <c>RequireHostService</c>; the presence probe resolves through the required online-foundation
/// dependency; outward products are registered back into the shared face; two expiry-sweep tick jobs anchor their
/// tenant / app on the family <see cref="OnlineScope"/> infrastructure. <c>ActivateAsync</c> publishes the
/// <see cref="IOnlineFriendDomain"/> bridge face, injects channel-membership probes into the chat service by
/// enumerating the extension point, and triggers the one-shot legacy friend-data migration when the option is on.
/// The chat moderation extension point is declared with the default allow-all strategy, replaced atomically by the
/// hot-update side through <c>CapabilityHook&lt;T&gt;.Attach</c>.
/// </remarks>
public sealed class OnlineSocialCapabilityModule : ICapabilityModule, IAdminActionContributor
{
    /// <summary>
    /// 社交能力清单（Id 冻结 = 终态包名；必选 online-foundation；Options 根类型 <see cref="OnlineSocialOptions"/>；
    /// action 集合与幂等包裹位逐字迁移自壳过渡模块，含 match_isolation）。
    /// </summary>
    public static readonly CapabilityManifest ManifestInstance = new(
        "online-social",
        "1.0.0",
        new[] { "kick_player", "revoke_token", "mute_player", "query_report_cases", "update_report_case", "send_admin_notification", "query_chat_messages", "match_isolation" },
        new[] { "online-foundation" },
        null,
        typeof(OnlineSocialOptions));

    /// <summary>
    /// 社交裁决（禁言 / 举报 / 拉黑）。
    /// </summary>
    private OnlineSocialDecisionService _socialDecisions;

    /// <summary>
    /// 处罚（Mute / Ban 事实源）。
    /// </summary>
    private OnlinePunishmentService _punishments;

    /// <summary>
    /// 举报存储（Register 期解析；handler 查询面用）。
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
    /// 好友服务（好友关系生命周期的唯一写者；激活期执行旧数据迁移的真实写路径）。
    /// </summary>
    private OnlineFriendService _friends;

    /// <summary>
    /// 聊天服务（四类频道 + 审核钩子 + 成员资格探针集合，激活期注入探针）。
    /// </summary>
    private OnlineChatService _chat;

    /// <summary>
    /// 会话存储（Foundation 面；kick / revoke 逐会话执行）。
    /// </summary>
    private IOnlineSessionStore _sessionStore;

    /// <summary>
    /// 会话令牌（Foundation 面）。
    /// </summary>
    private OnlineSessionTokenService _tokens;

    /// <summary>
    /// 合并校验后的社交 Options（Register 期取用；激活期迁移开关读取面）。
    /// </summary>
    private OnlineSocialOptions _options;

    /// <summary>
    /// 族基建作用域（Register 期解析；迁移玩家作用域三元组来源）。
    /// </summary>
    private OnlineScope _scope;

    /// <inheritdoc />
    public CapabilityManifest Manifest
    {
        get { return ManifestInstance; }
    }

    /// <inheritdoc />
    public void Register(CapabilityRegistrationContext context)
    {
        _options = context.GetOptions<OnlineSocialOptions>();
        var eventPublisher = context.GetService<IOnlineEventPublisher>();
        _scope = context.GetService<OnlineScope>();
        _sessionStore = context.GetService<IOnlineSessionStore>();
        _tokens = context.GetService<OnlineSessionTokenService>();

        // 六 store 懒默认注册 + 解析：族 / 宿主 Override 恒胜出，本模块不写探测-回退分支。
        context.AddService<IOnlineChatStore>(static _ => new InMemoryOnlineChatStore());
        context.AddService<IOnlineFriendStore>(static _ => new InMemoryOnlineFriendStore());
        context.AddService<IOnlineGroupStore>(static _ => new InMemoryOnlineGroupStore());
        context.AddService<IOnlineNotificationStore>(static _ => new InMemoryOnlineNotificationStore());
        context.AddService<IOnlineReportStore>(static _ => new InMemoryOnlineReportStore());
        context.AddService<IOnlineSocialGraphStore>(static _ => new InMemoryOnlineSocialGraphStore());
        var chatStore = context.GetService<IOnlineChatStore>();
        var friendStore = context.GetService<IOnlineFriendStore>();
        var groupStore = context.GetService<IOnlineGroupStore>();
        var notificationStore = context.GetService<IOnlineNotificationStore>();
        _reportStore = context.GetService<IOnlineReportStore>();
        var socialGraphStore = context.GetService<IOnlineSocialGraphStore>();

        // 好友服务：玩家目录是宿主扩展点（缺失即装配期 fail-fast）；在线探针由必选 online-foundation 提供。
        context.RequireHostService<IOnlinePlayerDirectory>();
        var directory = context.GetService<IOnlinePlayerDirectory>();
        var presenceProbe = context.GetService<IOnlineSocialPresenceProbe>();
        _friends = new OnlineFriendService(friendStore, eventPublisher, _options.Friend, directory, presenceProbe);

        _socialDecisions = new OnlineSocialDecisionService(socialGraphStore, _reportStore, eventPublisher);
        _punishments = new OnlinePunishmentService(socialGraphStore, eventPublisher);
        _notifications = new OnlineNotificationService(notificationStore, eventPublisher);
        _chatAudit = new OnlineChatAuditProjection(chatStore);

        // 聊天服务：审核钩子以默认放行策略声明（热更侧可原子替换）；服务持有句柄并逐调用读取 Current，
        // 不缓存实现实例（缓存会让热更挂载失效并钉住旧热更程序集）。成员资格探针 Register 期传空集合，
        // Activate 期枚举扩展点注入（含本包 "group" 命名注册的群组探针与兄弟域探针）。
        var moderationHook = context.DeclareHook<IOnlineChatModerationHook>(new OnlineChatModerationAllowAllHook());
        _chat = new OnlineChatService(chatStore, eventPublisher, _socialDecisions, _options.Chat, new List<IOnlineChannelMembershipProbe>(), () => moderationHook.Current);

        // 群组服务（首次纳入装配面）：本身实现 IOnlineChannelMembershipProbe，以 "group" 命名注册进探针扩展点。
        var groups = new OnlineGroupService(groupStore, eventPublisher);
        context.AddService(groups);
        context.AddService<IOnlineChannelMembershipProbe>("group", groups);

        context.AddService(_friends);
        context.AddService(_socialDecisions);
        context.AddService(_punishments);
        context.AddService(_notifications);
        context.AddService(_chatAudit);
        context.AddService(_chat);
        context.AddService<IOnlineSocialGate>(_socialDecisions);

        var scope = _scope;
        context.AddTickJob("notification-expiry-sweep", async ct =>
        {
            await _notifications.SweepExpiredAsync(scope.TenantId, scope.AppId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), ct).ConfigureAwait(false);
        });
        context.AddTickJob("friend-request-expiry-sweep", async ct =>
        {
            await _friends.SweepExpiredAsync(scope.TenantId, scope.AppId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), ct).ConfigureAwait(false);
        });
    }

    /// <inheritdoc />
    public async Task ActivateAsync(CapabilityActivationContext context, CancellationToken cancellationToken)
    {
        // 好友域桥契约面发布（发布者身份 = 本能力；撤销由宿主停止期自动完成）。
        context.Bridges.Publish<IOnlineFriendDomain>(new OnlineFriendDomainFace(_friends));

        // 频道成员资格探针注入（Activate 期才允许枚举扩展点；群组 "group" + 兄弟域 "party" 等一并注入）。
        _chat.AttachChannelMembershipProbes(context.Services.GetServices<IOnlineChannelMembershipProbe>());

        // 旧好友数据一次性迁移（显式开关，默认关闭；只读单向 + 幂等 + 逐条 containment + 汇总）。
        if (_options.Friend.MigrateLegacyFriendData)
        {
            await OnlineFriendLegacyMigration.MigrateAsync(_friends, _scope.TenantId, _scope.AppId, _scope.ServerId, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public Task DeactivateAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void RegisterAdminActions(AdminActionRegistry registry)
    {
        var handlers = new OnlineAdminSocialHandlers(_sessionStore, _tokens, _punishments, _socialDecisions, _notifications, _reportStore, _chatAudit);
        handlers.Register(registry);
        handlers.RegisterMatchIsolation(registry);
    }
}
