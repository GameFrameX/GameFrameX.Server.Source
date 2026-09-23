//  ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
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

namespace GameFrameX.Online.Social;

using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Online.Contracts;
using GameFrameX.Online.Events;
using GameFrameX.Online.Scope;

/// <summary>
/// 聊天服务（vault:C7 S6.5～S6.7：四类频道、发送裁决、历史游标、离线补拉、已读、撤回、举报入口）。
/// <para>
/// 维护约束（发送主链路的固定顺序，不可调换）：
/// ① 频道解析与**成员资格校验**（非成员一律 <c>ResourceNotFound</c>，反预言）；
/// ② 内容校验（空、超长）；
/// ③ **重发去重**（去重键非空且命中即原样返回既有消息并短路——见 <see cref="SendAsync"/>）：
/// 重放不得再走裁决 / 频控 / 审核，否则「重发幂等」会在窗口期内超限或此刻被禁言时退化为「发送失败」；
/// ④ **社交裁决**（私聊走 <see cref="OnlineSocialDecisionService.EvaluateAsync"/> 带上对端做屏蔽双向判定，
/// 其余频道走 <see cref="OnlineSocialDecisionService.EvaluateSendAsync"/> 做禁言/封禁判定）——
/// 禁止在本类内自建屏蔽或处罚判断，那是 vault:C7 风险表首条要防的绕过；
/// ⑤ **频控**（在内容审核之前：先挡洪峰再让审核服务承压）；
/// ⑥ 内容审核扩展点（未装配 = 放行；插件异常 = 捕获放行 + 留痕，VC-6.15）；
/// ⑦ 落库与事件（事件**仅在真正新增**一条消息时发布：落库出口在并发重放下会返回既有消息，那不是新消息）。
/// </para>
/// <para>
/// 维护约束（频道标识确定性）：频道标识一律经 <see cref="BuildChannelId"/> 派生，
/// 保证 A→B 与 B→A 命中同一私聊频道、重复打开同一频道天然幂等。
/// </para>
/// </summary>
public sealed class OnlineChatService
{
    /// <summary>频道标识前缀。</summary>
    private const string ChannelIdPrefix = "chan:v1:";

    /// <summary>聊天存储。</summary>
    private readonly IOnlineChatStore _store;

    /// <summary>事件发布出口。</summary>
    private readonly IOnlineEventPublisher _eventPublisher;

    /// <summary>社交裁决入口（唯一，禁止绕开）。</summary>
    private readonly OnlineSocialDecisionService _decisionService;

    /// <summary>可配置项。</summary>
    private readonly OnlineChatOptions _options;

    /// <summary>发送限流器。</summary>
    private readonly OnlineChatRateLimiter _rateLimiter;

    /// <summary>频道成员资格探针（可空：未装配时 Party / Group 频道一律拒绝，fail closed）。</summary>
    private readonly IOnlineChannelMembershipProbe _membershipProbe;

    /// <summary>内容审核扩展点（可空 = 全部放行）。</summary>
    private readonly IOnlineChatModerationHook _moderationHook;

    /// <summary>
    /// 初始化 <see cref="OnlineChatService"/>。
    /// </summary>
    /// <param name="store">聊天存储。</param>
    /// <param name="eventPublisher">事件发布出口。</param>
    /// <param name="decisionService">社交裁决入口。</param>
    /// <param name="options">可配置项（可空 = 推荐默认值）。</param>
    /// <param name="membershipProbe">频道成员资格探针（可空）。</param>
    /// <param name="moderationHook">内容审核扩展点（可空 = 放行）。</param>
    public OnlineChatService(
        IOnlineChatStore store,
        IOnlineEventPublisher eventPublisher,
        OnlineSocialDecisionService decisionService,
        OnlineChatOptions options = null,
        IOnlineChannelMembershipProbe membershipProbe = null,
        IOnlineChatModerationHook moderationHook = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        _decisionService = decisionService ?? throw new ArgumentNullException(nameof(decisionService));
        _options = options ?? new OnlineChatOptions();
        _rateLimiter = new OnlineChatRateLimiter(_options.RateLimitMaxMessages, _options.RateLimitWindowSeconds);
        _membershipProbe = membershipProbe;
        _moderationHook = moderationHook;
    }

    /// <summary>
    /// 派生频道标识（确定性：同作用域 + 同类型 + 同绑定主体 → 同标识）。
    /// <para>
    /// 定向私聊以**规范化玩家对**（小标识在前）派生，这是「A→B 与 B→A 落在同一频道」的实现依据；
    /// 全局频道不绑定主体（同一 App 一个）。
    /// </para>
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="kind">频道类型。</param>
    /// <param name="leftPlayerId">私聊一侧玩家（仅 <see cref="OnlineChatChannelKind.Direct"/> 使用）。</param>
    /// <param name="rightPlayerId">私聊另一侧玩家（仅 <see cref="OnlineChatChannelKind.Direct"/> 使用）。</param>
    /// <param name="boundId">绑定主体标识（Party 的 PartyId / Group 的 GroupId）。</param>
    /// <returns>频道标识。</returns>
    public static string BuildChannelId(long tenantId, long appId, OnlineChatChannelKind kind, long leftPlayerId, long rightPlayerId, string boundId)
    {
        var scopePrefix = ChannelIdPrefix + tenantId + ":" + appId + ":";
        if (kind == OnlineChatChannelKind.Global)
        {
            return scopePrefix + "global";
        }

        if (kind == OnlineChatChannelKind.Direct)
        {
            var low = leftPlayerId < rightPlayerId ? leftPlayerId : rightPlayerId;
            var high = leftPlayerId < rightPlayerId ? rightPlayerId : leftPlayerId;
            return scopePrefix + "direct:" + low + ":" + high;
        }

        return scopePrefix + (kind == OnlineChatChannelKind.Party ? "party:" : "group:") + (boundId ?? string.Empty);
    }

    /// <summary>
    /// 打开（或复用）定向私聊频道。
    /// <para>
    /// 维护约束：打开**不做社交裁决**——频道只是容器，不裁决不泄露。历史记录是双方各自的对话资产，
    /// 即使后来任一方屏蔽了对方，也不该让本人看不到自己的聊天记录；拦截发生在发送与下发环节。
    /// </para>
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="otherPlayerId">对端玩家。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>生效的私聊频道。</returns>
    public Task<OnlineResult<OnlineChatChannel>> OpenDirectChannelAsync(OnlineScope scope, long otherPlayerId, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope<OnlineChatChannel>(scope);
        if (failure != null)
        {
            return Task.FromResult(failure);
        }

        if (otherPlayerId <= 0 || otherPlayerId == scope.PlayerId)
        {
            return Task.FromResult(OnlineResult<OnlineChatChannel>.Fail(OnlineErrorCode.ParameterInvalid, "私聊对端玩家无效"));
        }

        var channelId = BuildChannelId(scope.TenantId, scope.AppId, OnlineChatChannelKind.Direct, scope.PlayerId, otherPlayerId, null);
        return OpenChannelAsync(scope, OnlineChatChannelKind.Direct, channelId, string.Empty, scope.PlayerId, otherPlayerId, cancellationToken);
    }

    /// <summary>
    /// 打开（或复用）队伍 / 群组频道（成员资格由 <see cref="IOnlineChannelMembershipProbe"/> 取当前事实）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="kind">频道类型（只接受 <see cref="OnlineChatChannelKind.Party"/> 或 <see cref="OnlineChatChannelKind.Group"/>）。</param>
    /// <param name="boundId">绑定主体标识（PartyId 或 GroupId）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>生效的频道；非成员或未装配探针时拒绝。</returns>
    public Task<OnlineResult<OnlineChatChannel>> OpenBoundChannelAsync(OnlineScope scope, OnlineChatChannelKind kind, string boundId, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope<OnlineChatChannel>(scope);
        if (failure != null)
        {
            return Task.FromResult(failure);
        }

        if (kind != OnlineChatChannelKind.Party && kind != OnlineChatChannelKind.Group)
        {
            return Task.FromResult(OnlineResult<OnlineChatChannel>.Fail(OnlineErrorCode.ParameterInvalid, "该频道类型不接受绑定主体"));
        }

        if (string.IsNullOrEmpty(boundId))
        {
            return Task.FromResult(OnlineResult<OnlineChatChannel>.Fail(OnlineErrorCode.ParameterInvalid, "绑定主体标识不能为空"));
        }

        var channelId = BuildChannelId(scope.TenantId, scope.AppId, kind, 0, 0, boundId);
        return OpenChannelAsync(scope, kind, channelId, boundId, 0, 0, cancellationToken);
    }

    /// <summary>
    /// 打开（或复用）全局频道（App 级公共空间，同租户同 App 的玩家均可访问）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>生效的全局频道。</returns>
    public Task<OnlineResult<OnlineChatChannel>> OpenGlobalChannelAsync(OnlineScope scope, CancellationToken cancellationToken = default)
    {
        var failure = ValidateScope<OnlineChatChannel>(scope);
        if (failure != null)
        {
            return Task.FromResult(failure);
        }

        var channelId = BuildChannelId(scope.TenantId, scope.AppId, OnlineChatChannelKind.Global, 0, 0, null);
        return OpenChannelAsync(scope, OnlineChatChannelKind.Global, channelId, string.Empty, 0, 0, cancellationToken);
    }

    /// <summary>
    /// 发送消息（按 <see cref="OnlineChatService"/> 类注释的固定顺序执行七步链路）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="channelId">频道标识。</param>
    /// <param name="content">消息内容。</param>
    /// <param name="dedupeKey">发送方去重键（可空；非空时重发幂等返回既有消息）。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>已落库的消息。</returns>
    public async Task<OnlineResult<OnlineChatMessage>> SendAsync(OnlineScope scope, string channelId, string content, string dedupeKey = null, string correlationId = null, CancellationToken cancellationToken = default)
    {
        var authorized = await AuthorizeChannelAsync(scope, channelId, cancellationToken).ConfigureAwait(false);
        if (!authorized.IsSuccess)
        {
            return OnlineResult<OnlineChatMessage>.Fail(authorized.Code, authorized.Message);
        }

        var trimmed = content == null ? string.Empty : content.Trim();
        if (trimmed.Length == 0)
        {
            return OnlineResult<OnlineChatMessage>.Fail(OnlineErrorCode.ParameterInvalid, "消息内容不能为空");
        }

        if (trimmed.Length > _options.MaxContentLength)
        {
            return OnlineResult<OnlineChatMessage>.Fail(OnlineErrorCode.ParameterInvalid, "消息内容超出长度上限");
        }

        var channel = authorized.Data;

        if (!string.IsNullOrEmpty(dedupeKey))
        {
            // 重发短路：命中即返回既有消息，不再走裁决 / 频控 / 审核（VC-6.10「同一逻辑发送只落一条」）。
            // 顺序不可后移——移到最后则重放要先过频控，窗口期内重发会拿到 RateLimitExceeded，
            // 客户端据此判定「发送失败」而消息其实早已入库。
            var replayed = await _store.FindByDedupeKeyAsync(scope.TenantId, scope.AppId, channelId, dedupeKey, cancellationToken).ConfigureAwait(false);
            if (replayed != null)
            {
                return OnlineResult<OnlineChatMessage>.Ok(replayed);
            }
        }

        var decision = await EvaluateSendAsync(scope, channel, cancellationToken).ConfigureAwait(false);
        if (!decision.Allowed)
        {
            return OnlineResult<OnlineChatMessage>.Fail(decision.Code, decision.Reason);
        }

        if (!_rateLimiter.TryAcquire(scope.TenantId, scope.AppId, scope.PlayerId, channelId))
        {
            return OnlineResult<OnlineChatMessage>.Fail(OnlineErrorCode.RateLimitExceeded, "发送过于频繁");
        }

        var now = Now();
        var draft = new OnlineChatMessage
        {
            MessageId = "msg-" + Guid.NewGuid().ToString("N"),
            TenantId = scope.TenantId,
            AppId = scope.AppId,
            ChannelId = channelId,
            ChannelKind = channel.Kind,
            SenderId = scope.PlayerId,
            Content = trimmed,
            SentAtTime = now,
            Sequence = 0,
            State = OnlineChatMessageState.Normal,
            RecalledAtTime = 0,
            RecalledByPlayerId = 0,
            DedupeKey = dedupeKey ?? string.Empty,
        };

        var moderationFailure = await CheckModerationAsync(draft, correlationId, cancellationToken).ConfigureAwait(false);
        if (moderationFailure != null)
        {
            return moderationFailure;
        }

        var stored = await _store.AppendAsync(draft, cancellationToken).ConfigureAwait(false);
        if (string.Equals(stored.MessageId, draft.MessageId, StringComparison.Ordinal))
        {
            // 只有真的新增才发事件：并发重放会在存储临界区内被去重键拦下并返回既有消息，
            // 无条件发布会让「消息已存在」这件事对外表现为第二次 MessageSent。
            await _eventPublisher.PublishAsync(OnlineChatEvents.CreateMessageSent(stored, correlationId), cancellationToken).ConfigureAwait(false);
        }

        return OnlineResult<OnlineChatMessage>.Ok(stored);
    }

    /// <summary>
    /// 按稳定排序键分页读取频道历史（离线补拉：把游标置为本地最后一条消息的位置即可续拉）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="channelId">频道标识。</param>
    /// <param name="cursor">上一页返回的游标（首页传 null 或空）。</param>
    /// <param name="pageSize">每页条数（<c>0</c> 取配置上限；超过上限按上限截断）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>历史分页结果（升序；已撤回消息保留占位但内容清空）。</returns>
    public async Task<OnlineResult<OnlineChatHistoryPage>> GetHistoryAsync(OnlineScope scope, string channelId, string cursor = null, int pageSize = 0, CancellationToken cancellationToken = default)
    {
        var authorized = await AuthorizeChannelAsync(scope, channelId, cancellationToken).ConfigureAwait(false);
        if (!authorized.IsSuccess)
        {
            return OnlineResult<OnlineChatHistoryPage>.Fail(authorized.Code, authorized.Message);
        }

        var effectivePageSize = pageSize > 0 ? pageSize : _options.MaxMessagesPerPage;
        if (effectivePageSize > _options.MaxMessagesPerPage)
        {
            effectivePageSize = _options.MaxMessagesPerPage;
        }

        long afterSentAtTime;
        long afterSequence;
        if (!IsCursorValid(cursor, out afterSentAtTime, out afterSequence))
        {
            return OnlineResult<OnlineChatHistoryPage>.Fail(OnlineErrorCode.ParameterInvalid, "分页游标格式非法");
        }

        // 多取一条用于判定 HasMore，避免为「是否还有下一页」再查一次存储。
        var fetched = await _store.ReadAfterAsync(scope.TenantId, scope.AppId, new ChatReadCursor { ChannelId = channelId, AfterSentAtTime = afterSentAtTime, AfterSequence = afterSequence, Limit = effectivePageSize + 1 }, cancellationToken).ConfigureAwait(false);
        var hasMore = fetched.Count > effectivePageSize;
        var pageMessages = new List<OnlineChatMessage>();
        var lastMessageId = string.Empty;
        var lastSentAtTime = 0L;
        var lastSequence = 0L;
        foreach (var message in fetched)
        {
            if (pageMessages.Count >= effectivePageSize)
            {
                break;
            }

            pageMessages.Add(RedactRecalled(message));
            lastMessageId = message.MessageId;
            lastSentAtTime = message.SentAtTime;
            lastSequence = message.Sequence;
        }

        var page = new OnlineChatHistoryPage
        {
            Messages = pageMessages,
            PageCursor = new OnlinePageCursor(hasMore && lastMessageId.Length > 0 ? EncodeCursor(lastSentAtTime, lastSequence) : string.Empty, hasMore),
        };
        return OnlineResult<OnlineChatHistoryPage>.Ok(page);
    }

    /// <summary>
    /// 统计频道未读数（排除本人发送的与已撤回的消息）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="channelId">频道标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>未读条数。</returns>
    public async Task<OnlineResult<int>> GetUnreadCountAsync(OnlineScope scope, string channelId, CancellationToken cancellationToken = default)
    {
        var authorized = await AuthorizeChannelAsync(scope, channelId, cancellationToken).ConfigureAwait(false);
        if (!authorized.IsSuccess)
        {
            return OnlineResult<int>.Fail(authorized.Code, authorized.Message);
        }

        var mark = await _store.FindReadMarkAsync(scope.TenantId, scope.AppId, scope.PlayerId, channelId, cancellationToken).ConfigureAwait(false);
        var afterSentAtTime = mark == null ? 0 : mark.LastReadSentAtTime;
        var afterSequence = mark == null ? 0 : mark.LastReadSequence;
        var count = await _store.CountUnreadAsync(scope.TenantId, scope.AppId, new ChatReadCursor { ChannelId = channelId, AfterSentAtTime = afterSentAtTime, AfterSequence = afterSequence }, scope.PlayerId, cancellationToken).ConfigureAwait(false);
        return OnlineResult<int>.Ok(count);
    }

    /// <summary>
    /// 标记已读到指定消息（位点只进不退：目标位置早于既有位点时幂等返回既有位点）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="channelId">频道标识。</param>
    /// <param name="messageId">已读到的消息标识。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>生效的已读位点。</returns>
    public async Task<OnlineResult<OnlineChatReadMark>> MarkReadAsync(OnlineScope scope, string channelId, string messageId, string correlationId = null, CancellationToken cancellationToken = default)
    {
        var authorized = await AuthorizeChannelAsync(scope, channelId, cancellationToken).ConfigureAwait(false);
        if (!authorized.IsSuccess)
        {
            return OnlineResult<OnlineChatReadMark>.Fail(authorized.Code, authorized.Message);
        }

        var message = await _store.FindMessageAsync(scope.TenantId, scope.AppId, channelId, messageId, cancellationToken).ConfigureAwait(false);
        if (message == null)
        {
            return OnlineResult<OnlineChatReadMark>.Fail(OnlineErrorCode.ResourceNotFound, "消息不存在");
        }

        var mark = await _store.FindReadMarkAsync(scope.TenantId, scope.AppId, scope.PlayerId, channelId, cancellationToken).ConfigureAwait(false);
        if (mark != null && mark.IsNotAfterPosition(message.SentAtTime, message.Sequence))
        {
            // 位点只进不退：乱序到达的旧「已读」不得把位点推回去（否则红点会莫名其妙重新亮起）。
            return OnlineResult<OnlineChatReadMark>.Ok(mark);
        }

        var updated = new OnlineChatReadMark
        {
            TenantId = scope.TenantId,
            AppId = scope.AppId,
            PlayerId = scope.PlayerId,
            ChannelId = channelId,
            LastReadMessageId = message.MessageId ?? string.Empty,
            LastReadSentAtTime = message.SentAtTime,
            LastReadSequence = message.Sequence,
            UpdatedAtTime = Now(),
        };
        var stored = await _store.SaveReadMarkAsync(updated, cancellationToken).ConfigureAwait(false);
        if (stored == null)
        {
            // 上面的读-比-写与本次写入不在同一临界区：期间已被更晚的位点抢写（或并发重复标记）。
            // 收敛到既有位点，且**不发事件**——位点并没有被这次调用改动。
            var converged = await _store.FindReadMarkAsync(scope.TenantId, scope.AppId, scope.PlayerId, channelId, cancellationToken).ConfigureAwait(false);
            if (converged == null)
            {
                return OnlineResult<OnlineChatReadMark>.Fail(OnlineErrorCode.ResourceNotFound, "已读位点不存在");
            }

            return OnlineResult<OnlineChatReadMark>.Ok(converged);
        }

        await _eventPublisher.PublishAsync(OnlineChatEvents.CreateReadMarkUpdated(stored, correlationId), cancellationToken).ConfigureAwait(false);
        return OnlineResult<OnlineChatReadMark>.Ok(stored);
    }

    /// <summary>
    /// 撤回消息（只有发送者本人、且在撤回窗口内；重复撤回幂等）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="channelId">频道标识。</param>
    /// <param name="messageId">消息标识。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>撤回后的消息。</returns>
    public async Task<OnlineResult<OnlineChatMessage>> RecallAsync(OnlineScope scope, string channelId, string messageId, string correlationId = null, CancellationToken cancellationToken = default)
    {
        var authorized = await AuthorizeChannelAsync(scope, channelId, cancellationToken).ConfigureAwait(false);
        if (!authorized.IsSuccess)
        {
            return OnlineResult<OnlineChatMessage>.Fail(authorized.Code, authorized.Message);
        }

        var message = await _store.FindMessageAsync(scope.TenantId, scope.AppId, channelId, messageId, cancellationToken).ConfigureAwait(false);
        if (message == null || message.SenderId != scope.PlayerId)
        {
            // 反预言：非发送者看不到这条消息（也谈不上撤回）。
            return OnlineResult<OnlineChatMessage>.Fail(OnlineErrorCode.ResourceNotFound, "消息不存在");
        }

        if (message.State == OnlineChatMessageState.Recalled)
        {
            // 幂等：已撤回返回既有终态。
            return OnlineResult<OnlineChatMessage>.Ok(RedactRecalled(message));
        }

        var now = Now();
        if (_options.RecallWindowSeconds > 0 && now - message.SentAtTime > _options.RecallWindowSeconds * 1000L)
        {
            return OnlineResult<OnlineChatMessage>.Fail(OnlineErrorCode.StateOperationForbidden, "已超出撤回窗口");
        }

        var updated = await _store.UpdateMessageStateAsync(scope.TenantId, scope.AppId, new ChatMessageStateTransition { ChannelId = channelId, MessageId = messageId, ExpectedState = OnlineChatMessageState.Normal, NewState = OnlineChatMessageState.Recalled, NowUnixMilliseconds = now, RecalledByPlayerId = scope.PlayerId }, cancellationToken).ConfigureAwait(false);
        if (updated == null)
        {
            // CAS 失败 = 已被并发撤回；重读后按当前事实返回，保证收敛。
            var current = await _store.FindMessageAsync(scope.TenantId, scope.AppId, channelId, messageId, cancellationToken).ConfigureAwait(false);
            if (current == null)
            {
                return OnlineResult<OnlineChatMessage>.Fail(OnlineErrorCode.ResourceNotFound, "消息不存在");
            }

            return OnlineResult<OnlineChatMessage>.Ok(RedactRecalled(current));
        }

        await _eventPublisher.PublishAsync(OnlineChatEvents.CreateMessageRecalled(updated, correlationId), cancellationToken).ConfigureAwait(false);
        return OnlineResult<OnlineChatMessage>.Ok(RedactRecalled(updated));
    }

    /// <summary>
    /// 举报频道内的某条消息（vault:C7 S6.5 的举报入口：场景与证据由本方法补齐后交统一裁决域）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="channelId">频道标识。</param>
    /// <param name="messageId">被举报的消息标识。</param>
    /// <param name="reason">举报原因。</param>
    /// <param name="evidence">补充说明（可空；原因选「其他」时必填）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>已提交的举报案件。</returns>
    public async Task<OnlineResult<OnlineReportCase>> ReportAsync(OnlineScope scope, string channelId, string messageId, OnlineReportReason reason, string evidence = null, CancellationToken cancellationToken = default)
    {
        var authorized = await AuthorizeChannelAsync(scope, channelId, cancellationToken).ConfigureAwait(false);
        if (!authorized.IsSuccess)
        {
            return OnlineResult<OnlineReportCase>.Fail(authorized.Code, authorized.Message);
        }

        var message = await _store.FindMessageAsync(scope.TenantId, scope.AppId, channelId, messageId, cancellationToken).ConfigureAwait(false);
        if (message == null)
        {
            return OnlineResult<OnlineReportCase>.Fail(OnlineErrorCode.ResourceNotFound, "消息不存在");
        }

        if (message.SenderId == scope.PlayerId)
        {
            return OnlineResult<OnlineReportCase>.Fail(OnlineErrorCode.ParameterInvalid, "不能举报自己发送的消息");
        }

        return await _decisionService.SubmitReportAsync(scope, new OnlineReportSubmission { ReportedPlayerId = message.SenderId, Scene = OnlineReportScene.Chat, Reason = reason, MatchId = null, ChatMessageId = messageId, ChannelId = channelId, Evidence = evidence, CorrelationId = null }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 打开或复用频道的唯一实现点（确定性标识 + 幂等落库 + 成员资格校验）。
    /// </summary>
    /// <param name="scope">生效作用域。</param>
    /// <param name="kind">频道类型。</param>
    /// <param name="channelId">已派生的频道标识。</param>
    /// <param name="boundId">绑定主体标识。</param>
    /// <param name="firstParticipant">私聊参与方一（非私聊传 <c>0</c>）。</param>
    /// <param name="secondParticipant">私聊参与方二（非私聊传 <c>0</c>）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>生效的频道。</returns>
    private async Task<OnlineResult<OnlineChatChannel>> OpenChannelAsync(OnlineScope scope, OnlineChatChannelKind kind, string channelId, string boundId, long firstParticipant, long secondParticipant, CancellationToken cancellationToken)
    {
        var membership = await CheckMembershipAsync(scope, kind, boundId, cancellationToken).ConfigureAwait(false);
        if (membership != null)
        {
            return membership;
        }

        var existing = await _store.FindChannelAsync(scope.TenantId, scope.AppId, channelId, cancellationToken).ConfigureAwait(false);
        if (existing != null)
        {
            return OnlineResult<OnlineChatChannel>.Ok(existing);
        }

        var participants = new List<long>();
        if (kind == OnlineChatChannelKind.Direct)
        {
            participants.Add(firstParticipant < secondParticipant ? firstParticipant : secondParticipant);
            participants.Add(firstParticipant < secondParticipant ? secondParticipant : firstParticipant);
        }

        var prototype = new OnlineChatChannel
        {
            ChannelId = channelId,
            TenantId = scope.TenantId,
            AppId = scope.AppId,
            Kind = kind,
            BoundId = boundId ?? string.Empty,
            Participants = participants,
            CreatedAtTime = Now(),
        };
        var stored = await _store.SaveChannelIfAbsentAsync(prototype, cancellationToken).ConfigureAwait(false);
        return OnlineResult<OnlineChatChannel>.Ok(stored);
    }

    /// <summary>
    /// 校验频道存在与玩家成员资格（读取前置；非成员一律反预言拒绝）。
    /// </summary>
    /// <param name="scope">生效作用域。</param>
    /// <param name="channelId">频道标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>成功返回频道记录，失败返回应直接回传给调用方的错误结果。</returns>
    private async Task<OnlineResult<OnlineChatChannel>> AuthorizeChannelAsync(OnlineScope scope, string channelId, CancellationToken cancellationToken)
    {
        if (scope == null || scope.PlayerId <= 0)
        {
            return OnlineResult<OnlineChatChannel>.Fail(OnlineErrorCode.ParameterInvalid, "缺少玩家主体位");
        }

        if (string.IsNullOrEmpty(channelId))
        {
            return OnlineResult<OnlineChatChannel>.Fail(OnlineErrorCode.ParameterInvalid, "频道标识不能为空");
        }

        var channel = await _store.FindChannelAsync(scope.TenantId, scope.AppId, channelId, cancellationToken).ConfigureAwait(false);
        if (channel == null)
        {
            return OnlineResult<OnlineChatChannel>.Fail(OnlineErrorCode.ResourceNotFound, "频道不存在");
        }

        if (channel.Kind == OnlineChatChannelKind.Direct)
        {
            if (!channel.Contains(scope.PlayerId))
            {
                return OnlineResult<OnlineChatChannel>.Fail(OnlineErrorCode.ResourceNotFound, "频道不存在");
            }

            return OnlineResult<OnlineChatChannel>.Ok(channel);
        }

        if (channel.Kind == OnlineChatChannelKind.Global)
        {
            return OnlineResult<OnlineChatChannel>.Ok(channel);
        }

        var membership = await CheckMembershipAsync(scope, channel.Kind, channel.BoundId, cancellationToken).ConfigureAwait(false);
        if (membership != null)
        {
            return membership;
        }

        return OnlineResult<OnlineChatChannel>.Ok(channel);
    }

    /// <summary>
    /// 校验队伍 / 群组频道的成员资格（探针未装配时 fail closed）。
    /// </summary>
    /// <param name="scope">生效作用域。</param>
    /// <param name="kind">频道类型。</param>
    /// <param name="boundId">绑定主体标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>通过校验返回 null，否则返回应回传的错误结果。</returns>
    private async Task<OnlineResult<OnlineChatChannel>> CheckMembershipAsync(OnlineScope scope, OnlineChatChannelKind kind, string boundId, CancellationToken cancellationToken)
    {
        if (kind != OnlineChatChannelKind.Party && kind != OnlineChatChannelKind.Group)
        {
            return null;
        }

        if (_membershipProbe == null)
        {
            // 成员资格不可验证时必须拒绝：放行等于把频道内容广播给任意知道频道标识的人。
            return OnlineResult<OnlineChatChannel>.Fail(OnlineErrorCode.StateNotReady, "频道成员资格校验未装配");
        }

        var isMember = await _membershipProbe.IsChannelMemberAsync(scope.TenantId, scope.AppId, kind, boundId, scope.PlayerId, cancellationToken).ConfigureAwait(false);
        if (!isMember)
        {
            return OnlineResult<OnlineChatChannel>.Fail(OnlineErrorCode.ResourceNotFound, "频道不存在");
        }

        return null;
    }

    /// <summary>
    /// 执行发送前的社交裁决（私聊带对端做屏蔽双向判定，其余做禁言 / 封禁判定）。
    /// </summary>
    /// <param name="scope">生效作用域。</param>
    /// <param name="channel">频道记录。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>裁决结果。</returns>
    private async Task<OnlineSocialDecision> EvaluateSendAsync(OnlineScope scope, OnlineChatChannel channel, CancellationToken cancellationToken)
    {
        if (channel.Kind == OnlineChatChannelKind.Direct)
        {
            var otherPlayerId = channel.OtherOf(scope.PlayerId);
            if (otherPlayerId <= 0)
            {
                return OnlineSocialDecision.Deny(OnlineErrorCode.ResourceNotFound, "频道不存在");
            }

            return await _decisionService.EvaluateAsync(scope.TenantId, scope.AppId, scope.PlayerId, otherPlayerId, OnlineSocialInteractionPurpose.DirectMessage, cancellationToken).ConfigureAwait(false);
        }

        return await _decisionService.EvaluateSendAsync(scope, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 执行内容审核扩展点（未装配 = 放行；插件异常 = 捕获放行并发留痕事件，VC-6.15）。
    /// </summary>
    /// <param name="draft">待发送消息。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>放行返回 null，拒绝返回应回传的错误结果。</returns>
    private async Task<OnlineResult<OnlineChatMessage>> CheckModerationAsync(OnlineChatMessage draft, string correlationId, CancellationToken cancellationToken)
    {
        if (_moderationHook == null)
        {
            return null;
        }

        OnlineChatModerationVerdict verdict;
        try
        {
            verdict = await _moderationHook.CheckAsync(draft, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // 审核服务故障不得导致聊天不可用：放行 + 留痕（放行窗口可由该事件回扫）。
            await _eventPublisher.PublishAsync(OnlineChatEvents.CreateModerationFailed(draft.TenantId, draft.AppId, draft.SenderId, draft.ChannelId, exception.Message, correlationId), cancellationToken).ConfigureAwait(false);
            return null;
        }

        if (verdict == null || verdict.Allowed)
        {
            return null;
        }

        return OnlineResult<OnlineChatMessage>.Fail(OnlineErrorCode.RiskControlRejected, "消息未通过内容审核：" + verdict.Reason);
    }

    /// <summary>
    /// 抹去已撤回消息的内容（审计仍保留原文，下发面清空）。
    /// </summary>
    /// <param name="message">消息副本。</param>
    /// <returns>可下发形态的消息副本。</returns>
    private static OnlineChatMessage RedactRecalled(OnlineChatMessage message)
    {
        if (message.State != OnlineChatMessageState.Recalled)
        {
            return message;
        }

        var redacted = message.Copy();
        redacted.Content = string.Empty;
        return redacted;
    }

    /// <summary>
    /// 编码分页游标（不透明令牌：客户端只回传不解释）。
    /// </summary>
    /// <param name="sentAtTime">位置的发送时刻。</param>
    /// <param name="sequence">位置的频道内序号。</param>
    /// <returns>游标字符串。</returns>
    private static string EncodeCursor(long sentAtTime, long sequence)
    {
        return "v1:" + sentAtTime.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + sequence.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 解码分页游标（空游标视为从头读取）。
    /// </summary>
    /// <param name="cursor">游标字符串（可空）。</param>
    /// <param name="sentAtTime">解码出的发送时刻。</param>
    /// <param name="sequence">解码出的频道内序号。</param>
    /// <returns>格式合法返回 <c>true</c>。</returns>
    private static bool IsCursorValid(string cursor, out long sentAtTime, out long sequence)
    {
        sentAtTime = 0;
        sequence = 0;
        if (string.IsNullOrEmpty(cursor))
        {
            return true;
        }

        var parts = cursor.Split(':');
        if (parts.Length != 3 || parts[0] != "v1")
        {
            return false;
        }

        return long.TryParse(parts[1], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out sentAtTime)
               && long.TryParse(parts[2], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out sequence);
    }

    /// <summary>
    /// 校验作用域与玩家主体位（泛型形态）。
    /// </summary>
    /// <typeparam name="TData">接口成功负载类型。</typeparam>
    /// <param name="scope">生效作用域。</param>
    /// <returns>成功返回 null，失败返回错误结果。</returns>
    private static OnlineResult<TData> ValidateScope<TData>(OnlineScope scope)
    {
        if (scope == null)
        {
            return OnlineResult<TData>.Fail(OnlineErrorCode.ParameterInvalid, "缺少作用域");
        }

        if (scope.PlayerId <= 0)
        {
            return OnlineResult<TData>.Fail(OnlineErrorCode.ParameterInvalid, "聊天操作必须具备玩家主体位");
        }

        return null;
    }

    /// <summary>取当前 UTC 毫秒时刻。</summary>
    /// <returns>UTC 毫秒时间戳。</returns>
    private static long Now()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }
}
