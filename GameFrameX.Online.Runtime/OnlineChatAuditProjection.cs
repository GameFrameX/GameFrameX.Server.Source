// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应开源许可证与规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   Any legal disputes or liabilities arising from secondary development based on this project
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

using System.Text.Json;
using GameFrameX.Online.Events;
using GameFrameX.Online.Social;

namespace GameFrameX.Online.Runtime;

/// <summary>
/// 聊天审计投影（change C122 决策⑧④：<c>query_chat_messages</c> 依赖事件投影——聊天 store 无跨频道枚举 API，
/// 本投影订阅 <c>Online.Chat.MessageSent</c> 事件建立跨频道索引，查询时回读 store 补全正文与参与者）。
/// <para>
/// 维护约束：事件载荷不含消息正文（C7 脱敏红线），正文一律经 <see cref="IOnlineChatStore.FindMessageAsync"/>
/// 受控回读；投影只存引用（频道 / 消息标识 / 排序键），消息撤回后的状态以回读结果为准；
/// 宿主启动前的历史消息不在投影内（InMemory 单进程语义，C94）。
/// </para>
/// </summary>
public sealed class OnlineChatAuditProjection
{
    /// <summary>
    /// 聊天存储（正文回读面）。
    /// </summary>
    private readonly IOnlineChatStore _chatStore;

    /// <summary>
    /// 同步锁（引用表为普通列表）。
    /// </summary>
    private readonly object _sync = new object();

    /// <summary>
    /// 消息引用表（按事件到达序追加；查询时按排序键重排）。
    /// </summary>
    private readonly List<MessageSentRef> _refs = new List<MessageSentRef>();

    /// <summary>
    /// 初始化 <see cref="OnlineChatAuditProjection"/>。
    /// </summary>
    /// <param name="chatStore">聊天存储。</param>
    public OnlineChatAuditProjection(IOnlineChatStore chatStore)
    {
        _chatStore = chatStore ?? throw new ArgumentNullException(nameof(chatStore));
    }

    /// <summary>
    /// 事件桥接入点（由宿主在去重后调用；只消费消息发送事件）。
    /// </summary>
    /// <param name="onlineEvent">Online 事件。</param>
    public void OnEvent(OnlineEvent onlineEvent)
    {
        if (onlineEvent == null || onlineEvent.Payload.IsEmpty)
        {
            return;
        }

        if (!string.Equals(onlineEvent.EventType, OnlineChatEvents.MessageSent, StringComparison.Ordinal))
        {
            return;
        }

        MessageSentRef messageRef;
        try
        {
            messageRef = JsonSerializer.Deserialize<MessageSentRef>(onlineEvent.Payload.Span);
        }
        catch (JsonException)
        {
            return;
        }

        if (messageRef == null || string.IsNullOrEmpty(messageRef.MessageId) || string.IsNullOrEmpty(messageRef.ChannelId))
        {
            return;
        }

        lock (_sync)
        {
            _refs.Add(messageRef);
        }
    }

    /// <summary>
    /// 查询聊天审计消息（跨频道；新到旧分页）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="query">检索条件载荷（可选过滤 + 游标 + 页大小）。</param>
    /// <returns>查询页（正文与参与者已回读补全）。</returns>
    public async Task<ChatAuditPage> QueryAsync(long tenantId, long appId, ChatAuditQuery query)
    {
        if (query == null)
        {
            throw new ArgumentNullException(nameof(query));
        }

        var channelKind = query.ChannelKind;
        var participantPlayerId = query.ParticipantPlayerId;
        var keyword = query.Keyword;
        var startTime = query.StartTime;
        var endTime = query.EndTime;
        var cursor = query.Cursor;
        var pageSize = query.PageSize;
        var effectivePageSize = Math.Max(1, Math.Min(100, pageSize <= 0 ? 20 : pageSize));
        var ordered = BuildOrderedSnapshot(cursor);
        var channelCache = new Dictionary<string, OnlineChatChannel>();
        var items = new List<ChatAuditItem>();
        var lastExamined = 0;
        for (var index = 0; index < ordered.Count; index++)
        {
            lastExamined = index;
            var item = await TryComposeItemAsync(ordered[index], channelCache, tenantId, appId, channelKind, participantPlayerId, keyword, startTime, endTime).ConfigureAwait(false);
            if (item != null)
            {
                items.Add(item);
            }

            if (items.Count > effectivePageSize)
            {
                break;
            }
        }

        var hasMore = items.Count > effectivePageSize;
        if (hasMore)
        {
            items.RemoveAt(items.Count - 1);
        }

        string nextCursor = null;
        if (hasMore && items.Count > 0)
        {
            var last = items[items.Count - 1];
            nextCursor = last.SentAtTime.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + last.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return new ChatAuditPage
        {
            Items = items,
            NextCursor = nextCursor,
            HasMore = hasMore,
            ExaminedCount = lastExamined + 1,
        };
    }

    /// <summary>
    /// 构造排序后的引用快照（新到旧；应用复合游标过滤）。
    /// <para>
    /// 锁内拷贝引用表后按发送时刻降序、频道内序号降序排序；游标非空且可解析时
    /// 仅保留严格早于游标（或时刻相等且序号小于游标）的引用，排序键与过滤语义为既有契约。
    /// </para>
    /// </summary>
    /// <param name="cursor">分页游标（<c>sentAtTime:sequence</c>；null 从头）。</param>
    /// <returns>排序并过滤后的引用列表。</returns>
    private List<MessageSentRef> BuildOrderedSnapshot(string cursor)
    {
        List<MessageSentRef> snapshot;
        lock (_sync)
        {
            snapshot = new List<MessageSentRef>(_refs);
        }

        var ordered = snapshot.OrderByDescending(reference => reference.SentAtTime).ThenByDescending(reference => reference.Sequence).ToList();
        if (!string.IsNullOrEmpty(cursor) && TryParseCursor(cursor, out var cursorTime, out var cursorSequence))
        {
            ordered = ordered.Where(reference => reference.SentAtTime < cursorTime
                                                 || (reference.SentAtTime == cursorTime && reference.Sequence < cursorSequence)).ToList();
        }

        return ordered;
    }

    /// <summary>
    /// 组装单条引用的审计条目（过滤与受控回读的固定顺序：时间窗 → 频道类型 → 频道回读 → 参与者 → 消息回读 → 关键字）。
    /// <para>
    /// 过滤顺序决定回读触发面：参与者不匹配的引用不触发消息回读；频道记录经共享缓存回读，
    /// 正文经 <see cref="IOnlineChatStore.FindMessageAsync"/> 受控回读（C7 脱敏红线）。任一环节不满足返回 null（调用方跳过）。
    /// </para>
    /// </summary>
    /// <param name="reference">消息引用。</param>
    /// <param name="channelCache">频道回读缓存（跨引用共享）。</param>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="channelKind">频道类型过滤（null 表示不限）。</param>
    /// <param name="participantPlayerId">参与者过滤（null 表示不限）。</param>
    /// <param name="keyword">正文关键字过滤（null 或空表示不限）。</param>
    /// <param name="startTime">起始时刻（UTC 毫秒；null 表示不限）。</param>
    /// <param name="endTime">结束时刻（UTC 毫秒；null 表示不限）。</param>
    /// <returns>审计条目；任一过滤不满足或消息已不存在返回 null。</returns>
    private async Task<ChatAuditItem> TryComposeItemAsync(MessageSentRef reference, Dictionary<string, OnlineChatChannel> channelCache, long tenantId, long appId, OnlineChatChannelKind? channelKind, long? participantPlayerId, string keyword, long? startTime, long? endTime)
    {
        if (!MatchesTimeWindow(reference, startTime, endTime))
        {
            return null;
        }

        if (!MatchesChannelKind(reference, channelKind))
        {
            return null;
        }

        OnlineChatChannel channel;
        if (!channelCache.TryGetValue(reference.ChannelId, out channel))
        {
            channel = await _chatStore.FindChannelAsync(tenantId, appId, reference.ChannelId).ConfigureAwait(false);
            channelCache[reference.ChannelId] = channel;
        }

        var participants = BuildParticipants(reference, channel);
        if (!MatchesParticipant(participants, participantPlayerId))
        {
            return null;
        }

        var message = await _chatStore.FindMessageAsync(tenantId, appId, reference.ChannelId, reference.MessageId).ConfigureAwait(false);
        if (message == null)
        {
            return null;
        }

        if (!MatchesKeyword(message.Content, keyword))
        {
            return null;
        }

        return new ChatAuditItem
        {
            MessageId = message.MessageId,
            ChannelId = message.ChannelId,
            ChannelKind = message.ChannelKind,
            SenderId = message.SenderId,
            Content = message.Content ?? string.Empty,
            State = message.State,
            SentAtTime = message.SentAtTime,
            Sequence = message.Sequence,
            Participants = participants,
        };
    }

    /// <summary>
    /// 判定引用是否落在查询时间窗内（起点/终点任一为 null 表示该侧不限）。
    /// </summary>
    /// <param name="reference">消息引用。</param>
    /// <param name="startTime">起始时刻（UTC 毫秒；null 表示不限）。</param>
    /// <param name="endTime">结束时刻（UTC 毫秒；null 表示不限）。</param>
    /// <returns>窗内返回 true。</returns>
    private static bool MatchesTimeWindow(MessageSentRef reference, long? startTime, long? endTime)
    {
        if (startTime.HasValue && reference.SentAtTime < startTime.Value)
        {
            return false;
        }

        if (endTime.HasValue && reference.SentAtTime > endTime.Value)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// 判定引用的频道类型是否匹配过滤（null 表示不限；频道类型名忽略大小写解析，解析失败视为不匹配）。
    /// </summary>
    /// <param name="reference">消息引用。</param>
    /// <param name="channelKind">频道类型过滤。</param>
    /// <returns>匹配返回 true。</returns>
    private static bool MatchesChannelKind(MessageSentRef reference, OnlineChatChannelKind? channelKind)
    {
        if (!channelKind.HasValue)
        {
            return true;
        }

        return Enum.TryParse<OnlineChatChannelKind>(reference.ChannelKind, true, out var parsedKind) && parsedKind == channelKind.Value;
    }

    /// <summary>
    /// 判定参与者集合是否包含目标参与者（null 表示不限）。
    /// </summary>
    /// <param name="participants">参与者集合。</param>
    /// <param name="participantPlayerId">目标参与者。</param>
    /// <returns>包含或不过滤返回 true。</returns>
    private static bool MatchesParticipant(List<long> participants, long? participantPlayerId)
    {
        return !participantPlayerId.HasValue || participants.Contains(participantPlayerId.Value);
    }

    /// <summary>
    /// 判定消息正文是否命中关键字（null 或空表示不限；忽略大小写包含匹配）。
    /// </summary>
    /// <param name="content">消息正文（可能为 null）。</param>
    /// <param name="keyword">关键字。</param>
    /// <returns>命中或不过滤返回 true。</returns>
    private static bool MatchesKeyword(string content, string keyword)
    {
        return string.IsNullOrEmpty(keyword) || (content != null && content.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    /// <summary>
    /// 构造消息参与者的判定集合（定向/队伍/群组取频道成员；全局频道退化为发送者本人）。
    /// </summary>
    /// <param name="reference">消息引用。</param>
    /// <param name="channel">频道记录（可能为 null）。</param>
    /// <returns>参与者集合。</returns>
    private static List<long> BuildParticipants(MessageSentRef reference, OnlineChatChannel channel)
    {
        if (channel != null && channel.Participants != null && channel.Participants.Count > 0)
        {
            return channel.Participants;
        }

        return new List<long> { reference.SenderId, };
    }

    /// <summary>
    /// 解析分页游标。
    /// </summary>
    /// <param name="cursor">游标原文。</param>
    /// <param name="sentAtTime">输出：发送时刻。</param>
    /// <param name="sequence">输出：频道内序号。</param>
    /// <returns>解析成功返回 true。</returns>
    private static bool TryParseCursor(string cursor, out long sentAtTime, out long sequence)
    {
        sentAtTime = 0;
        sequence = 0;
        var separatorIndex = cursor.LastIndexOf(':');
        if (separatorIndex <= 0 || separatorIndex >= cursor.Length - 1)
        {
            return false;
        }

        return long.TryParse(cursor.Substring(0, separatorIndex), out sentAtTime) && long.TryParse(cursor.Substring(separatorIndex + 1), out sequence);
    }

    /// <summary>
    /// 消息发送事件载荷引用（与 <c>OnlineChatEvents.MessageSentPayload</c> 字段一致；事件不含正文）。
    /// </summary>
    public sealed class MessageSentRef
    {
        /// <summary>获取或设置消息标识。</summary>
        public string MessageId
        {
            get;
            set;
        }

        /// <summary>获取或设置频道标识。</summary>
        public string ChannelId
        {
            get;
            set;
        }

        /// <summary>获取或设置频道类型名。</summary>
        public string ChannelKind
        {
            get;
            set;
        }

        /// <summary>获取或设置发送者。</summary>
        public long SenderId
        {
            get;
            set;
        }

        /// <summary>获取或设置发送时刻（UTC 毫秒）。</summary>
        public long SentAtTime
        {
            get;
            set;
        }

        /// <summary>获取或设置频道内序号。</summary>
        public long Sequence
        {
            get;
            set;
        }
    }

    /// <summary>
    /// 聊天审计查询页。
    /// </summary>
    public sealed class ChatAuditPage
    {
        /// <summary>获取或设置消息条目（新到旧）。</summary>
        public List<ChatAuditItem> Items
        {
            get;
            set;
        }

        /// <summary>获取或设置下一页游标（无更多为 null）。</summary>
        public string NextCursor
        {
            get;
            set;
        }

        /// <summary>获取或设置是否还有更多。</summary>
        public bool HasMore
        {
            get;
            set;
        }

        /// <summary>获取或设置本次扫描的引用数（诊断用）。</summary>
        public int ExaminedCount
        {
            get;
            set;
        }
    }

    /// <summary>
    /// 聊天审计消息条目（正文与参与者已回读补全）。
    /// </summary>
    public sealed class ChatAuditItem
    {
        /// <summary>获取或设置消息标识。</summary>
        public string MessageId
        {
            get;
            set;
        }

        /// <summary>获取或设置频道标识。</summary>
        public string ChannelId
        {
            get;
            set;
        }

        /// <summary>获取或设置频道类型。</summary>
        public OnlineChatChannelKind ChannelKind
        {
            get;
            set;
        }

        /// <summary>获取或设置发送者。</summary>
        public long SenderId
        {
            get;
            set;
        }

        /// <summary>获取或设置消息正文（store 回读原文）。</summary>
        public string Content
        {
            get;
            set;
        }

        /// <summary>获取或设置消息状态。</summary>
        public OnlineChatMessageState State
        {
            get;
            set;
        }

        /// <summary>获取或设置发送时刻（UTC 毫秒）。</summary>
        public long SentAtTime
        {
            get;
            set;
        }

        /// <summary>获取或设置频道内序号。</summary>
        public long Sequence
        {
            get;
            set;
        }

        /// <summary>获取或设置参与者集合。</summary>
        public List<long> Participants
        {
            get;
            set;
        }
    }
}
