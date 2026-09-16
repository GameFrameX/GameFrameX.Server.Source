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
    /// <param name="channelKind">频道类型过滤（null 表示不限）。</param>
    /// <param name="participantPlayerId">参与者过滤（频道成员或发送者；null 表示不限）。</param>
    /// <param name="keyword">正文关键字过滤（null 或空表示不限）。</param>
    /// <param name="startTime">起始时刻（UTC 毫秒；null 表示不限）。</param>
    /// <param name="endTime">结束时刻（UTC 毫秒；null 表示不限）。</param>
    /// <param name="cursor">分页游标（<c>sentAtTime:sequence</c>；null 从头）。</param>
    /// <param name="pageSize">页大小（1～100）。</param>
    /// <returns>查询页（正文与参与者已回读补全）。</returns>
    public async Task<ChatAuditPage> QueryAsync(long tenantId, long appId, OnlineChatChannelKind? channelKind, long? participantPlayerId, string keyword, long? startTime, long? endTime, string cursor, int pageSize)
    {
        var effectivePageSize = Math.Max(1, Math.Min(100, pageSize <= 0 ? 20 : pageSize));
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

        var channelCache = new Dictionary<string, OnlineChatChannel>();
        var items = new List<ChatAuditItem>();
        var lastExamined = 0;
        for (var index = 0; index < ordered.Count; index++)
        {
            var reference = ordered[index];
            lastExamined = index;
            if (startTime.HasValue && reference.SentAtTime < startTime.Value)
            {
                continue;
            }

            if (endTime.HasValue && reference.SentAtTime > endTime.Value)
            {
                continue;
            }

            if (channelKind.HasValue)
            {
                if (!Enum.TryParse<OnlineChatChannelKind>(reference.ChannelKind, true, out var parsedKind) || parsedKind != channelKind.Value)
                {
                    continue;
                }
            }

            if (!channelCache.TryGetValue(reference.ChannelId, out var channel))
            {
                channel = await _chatStore.FindChannelAsync(tenantId, appId, reference.ChannelId).ConfigureAwait(false);
                channelCache[reference.ChannelId] = channel;
            }

            var participants = BuildParticipants(reference, channel);
            if (participantPlayerId.HasValue && !participants.Contains(participantPlayerId.Value))
            {
                continue;
            }

            var message = await _chatStore.FindMessageAsync(tenantId, appId, reference.ChannelId, reference.MessageId).ConfigureAwait(false);
            if (message == null)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(keyword) && (message.Content == null || message.Content.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0))
            {
                continue;
            }

            items.Add(new ChatAuditItem
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
            });
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
