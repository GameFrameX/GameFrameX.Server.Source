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

/// <summary>
/// 聊天存储的单进程默认实现（持久化与多实例协调归 Server 仓运行时装配，X4）。
/// <para>
/// 维护约束：<see cref="AppendAsync"/> 的四步（去重 → 分配序号 → 落定发送时刻 → 写入）在同一临界区内完成，
/// 这是「翻页不重复、不漏项」的实现依据，拆开加锁即失效（见接口上的红线说明）。
/// 频道内序号与发送时刻各用一张按频道的字典维护，避免每次追加都去扫描全部消息。
/// </para>
/// <para>
/// 天花板（ponytail）：全局单锁 + 按频道线性扫描（历史分页与未读统计都是频道内线性，随单频道消息量增长）。
/// 单频道消息上万后需换按频道的二级索引与分段存储；当前量级下正确性优先。
/// 消息不做清理（<see cref="OnlineChatOptions.HistoryRetentionSeconds"/> 由运行时装配的调度消费）。
/// </para>
/// </summary>
public sealed class InMemoryOnlineChatStore : IOnlineChatStore
{
    /// <summary>并发保护锁。</summary>
    private readonly object _syncRoot = new object();

    /// <summary>频道表（键 = 作用域 + 频道标识）。</summary>
    private readonly Dictionary<string, OnlineChatChannel> _channelsById = new Dictionary<string, OnlineChatChannel>(StringComparer.Ordinal);

    /// <summary>消息表（键 = 作用域 + 频道标识 + 消息标识）。</summary>
    private readonly Dictionary<string, OnlineChatMessage> _messagesById = new Dictionary<string, OnlineChatMessage>(StringComparer.Ordinal);

    /// <summary>去重索引（键 = 作用域 + 频道标识 + 去重键 → 消息标识）。</summary>
    private readonly Dictionary<string, string> _messageIdByDedupeKey = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>频道内下一个可用序号（键 = 作用域 + 频道标识）。</summary>
    private readonly Dictionary<string, long> _nextSequenceByChannel = new Dictionary<string, long>(StringComparer.Ordinal);

    /// <summary>频道内最后一条消息的发送时刻（键 = 作用域 + 频道标识；用于落定单调不减的发送时刻）。</summary>
    private readonly Dictionary<string, long> _lastSentAtTimeByChannel = new Dictionary<string, long>(StringComparer.Ordinal);

    /// <summary>已读位点表（键 = 作用域 + 玩家 + 频道标识）。</summary>
    private readonly Dictionary<string, OnlineChatReadMark> _readMarksByPlayerChannel = new Dictionary<string, OnlineChatReadMark>(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task<OnlineChatChannel> SaveChannelIfAbsentAsync(OnlineChatChannel channel, CancellationToken cancellationToken = default)
    {
        if (channel == null)
        {
            throw new ArgumentNullException(nameof(channel));
        }

        var key = BuildChannelKey(channel.TenantId, channel.AppId, channel.ChannelId);
        lock (_syncRoot)
        {
            OnlineChatChannel existing;
            if (_channelsById.TryGetValue(key, out existing))
            {
                return Task.FromResult(existing.Copy());
            }

            // 存档存副本而非调用方实例（调用方复用该实例不会回写库内记录）。
            var stored = channel.Copy();
            _channelsById[key] = stored;
            return Task.FromResult(stored.Copy());
        }
    }

    /// <inheritdoc />
    public Task<OnlineChatChannel> FindChannelAsync(long tenantId, long appId, string channelId, CancellationToken cancellationToken = default)
    {
        var key = BuildChannelKey(tenantId, appId, channelId);
        lock (_syncRoot)
        {
            OnlineChatChannel found;
            if (_channelsById.TryGetValue(key, out found))
            {
                return Task.FromResult(found.Copy());
            }

            return Task.FromResult<OnlineChatChannel>(null);
        }
    }

    /// <inheritdoc />
    public Task<OnlineChatMessage> AppendAsync(OnlineChatMessage message, CancellationToken cancellationToken = default)
    {
        if (message == null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        var channelKey = BuildChannelKey(message.TenantId, message.AppId, message.ChannelId);
        var messageKey = channelKey + ":" + (message.MessageId ?? string.Empty);
        lock (_syncRoot)
        {
            if (!string.IsNullOrEmpty(message.DedupeKey))
            {
                var dedupeKey = channelKey + ":d:" + message.DedupeKey;
                string dedupedMessageId;
                if (_messageIdByDedupeKey.TryGetValue(dedupeKey, out dedupedMessageId))
                {
                    OnlineChatMessage deduped;
                    if (_messagesById.TryGetValue(channelKey + ":" + dedupedMessageId, out deduped))
                    {
                        // 重发幂等：命中去重键即返回既有消息，不新增、不改写。
                        return Task.FromResult(deduped.Copy());
                    }
                }
            }

            long sequence;
            if (!_nextSequenceByChannel.TryGetValue(channelKey, out sequence) || sequence <= 0)
            {
                sequence = 1;
            }

            _nextSequenceByChannel[channelKey] = sequence + 1;

            long lastSentAtTime;
            if (_lastSentAtTimeByChannel.TryGetValue(channelKey, out lastSentAtTime) && message.SentAtTime < lastSentAtTime)
            {
                // 时钟回拨兜底：发送时刻在频道内单调不减，否则新消息会排到游标之前被永久跳过。
                message.SentAtTime = lastSentAtTime;
            }

            message.Sequence = sequence;
            _lastSentAtTimeByChannel[channelKey] = message.SentAtTime;
            // 防御性深拷贝：存档存副本而非调用方实例（序号与发送时刻已回填到副本上）。
            var stored = message.Copy();
            _messagesById[messageKey] = stored;
            if (!string.IsNullOrEmpty(message.DedupeKey))
            {
                _messageIdByDedupeKey[channelKey + ":d:" + message.DedupeKey] = message.MessageId ?? string.Empty;
            }

            return Task.FromResult(stored.Copy());
        }
    }

    /// <inheritdoc />
    public Task<OnlineChatMessage> FindByDedupeKeyAsync(long tenantId, long appId, string channelId, string dedupeKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(dedupeKey))
        {
            return Task.FromResult<OnlineChatMessage>(null);
        }

        var channelKey = BuildChannelKey(tenantId, appId, channelId);
        lock (_syncRoot)
        {
            string messageId;
            if (_messageIdByDedupeKey.TryGetValue(channelKey + ":d:" + dedupeKey, out messageId))
            {
                OnlineChatMessage found;
                if (_messagesById.TryGetValue(channelKey + ":" + messageId, out found))
                {
                    return Task.FromResult(found.Copy());
                }
            }

            return Task.FromResult<OnlineChatMessage>(null);
        }
    }

    /// <inheritdoc />
    public Task<OnlineChatMessage> FindMessageAsync(long tenantId, long appId, string channelId, string messageId, CancellationToken cancellationToken = default)
    {
        var key = BuildChannelKey(tenantId, appId, channelId) + ":" + (messageId ?? string.Empty);
        lock (_syncRoot)
        {
            OnlineChatMessage found;
            if (_messagesById.TryGetValue(key, out found))
            {
                return Task.FromResult(found.Copy());
            }

            return Task.FromResult<OnlineChatMessage>(null);
        }
    }

    /// <inheritdoc />
    public Task<OnlineChatMessage> UpdateMessageStateAsync(long tenantId, long appId, string channelId, string messageId, OnlineChatMessageState expectedState, OnlineChatMessageState newState, long nowUnixMilliseconds, long recalledByPlayerId, CancellationToken cancellationToken = default)
    {
        var key = BuildChannelKey(tenantId, appId, channelId) + ":" + (messageId ?? string.Empty);
        lock (_syncRoot)
        {
            OnlineChatMessage current;
            if (!_messagesById.TryGetValue(key, out current))
            {
                return Task.FromResult<OnlineChatMessage>(null);
            }

            if (current.State != expectedState)
            {
                // CAS 失败：不留任何写入痕迹。
                return Task.FromResult<OnlineChatMessage>(null);
            }

            current.State = newState;
            current.RecalledAtTime = nowUnixMilliseconds;
            current.RecalledByPlayerId = recalledByPlayerId;
            return Task.FromResult(current.Copy());
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OnlineChatMessage>> ReadAfterAsync(long tenantId, long appId, string channelId, long afterSentAtTime, long afterSequence, int limit, CancellationToken cancellationToken = default)
    {
        var channelKey = BuildChannelKey(tenantId, appId, channelId);
        var pageSize = limit > 0 ? limit : 1;
        var matched = new List<OnlineChatMessage>();
        lock (_syncRoot)
        {
            foreach (var pair in _messagesById)
            {
                var message = pair.Value;
                if (message.TenantId != tenantId || message.AppId != appId || !string.Equals(message.ChannelId, channelId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (IsAfterCursor(message, afterSentAtTime, afterSequence))
                {
                    matched.Add(message.Copy());
                }
            }
        }

        matched.Sort(CompareBySortKey);

        var result = new List<OnlineChatMessage>();
        foreach (var message in matched)
        {
            if (result.Count >= pageSize)
            {
                break;
            }

            result.Add(message);
        }

        return Task.FromResult<IReadOnlyList<OnlineChatMessage>>(result);
    }

    /// <inheritdoc />
    public Task<int> CountUnreadAsync(long tenantId, long appId, string channelId, long afterSentAtTime, long afterSequence, long readerPlayerId, CancellationToken cancellationToken = default)
    {
        var count = 0;
        lock (_syncRoot)
        {
            foreach (var pair in _messagesById)
            {
                var message = pair.Value;
                if (message.TenantId != tenantId || message.AppId != appId || !string.Equals(message.ChannelId, channelId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (message.SenderId == readerPlayerId)
                {
                    continue;
                }

                if (message.State != OnlineChatMessageState.Normal)
                {
                    // 已撤回的消息不再计入未读：让玩家为一个看不到内容的消息去点红点是无意义的。
                    continue;
                }

                if (IsAfterCursor(message, afterSentAtTime, afterSequence))
                {
                    count++;
                }
            }
        }

        return Task.FromResult(count);
    }

    /// <inheritdoc />
    public Task<OnlineChatReadMark> SaveReadMarkAsync(OnlineChatReadMark readMark, CancellationToken cancellationToken = default)
    {
        if (readMark == null)
        {
            throw new ArgumentNullException(nameof(readMark));
        }

        var key = BuildReadMarkKey(readMark.TenantId, readMark.AppId, readMark.PlayerId, readMark.ChannelId);
        lock (_syncRoot)
        {
            OnlineChatReadMark existing;
            if (_readMarksByPlayerChannel.TryGetValue(key, out existing) && existing.IsNotAfterPosition(readMark.LastReadSentAtTime, readMark.LastReadSequence))
            {
                // 位点只进不退：回落请求在临界区内被拒，不留下写入痕迹（调用方据 null 收敛到既有位点）。
                return Task.FromResult<OnlineChatReadMark>(null);
            }

            var stored = readMark.Copy();
            _readMarksByPlayerChannel[key] = stored;
            return Task.FromResult(stored.Copy());
        }
    }

    /// <inheritdoc />
    public Task<OnlineChatReadMark> FindReadMarkAsync(long tenantId, long appId, long playerId, string channelId, CancellationToken cancellationToken = default)
    {
        var key = BuildReadMarkKey(tenantId, appId, playerId, channelId);
        lock (_syncRoot)
        {
            OnlineChatReadMark found;
            if (_readMarksByPlayerChannel.TryGetValue(key, out found))
            {
                return Task.FromResult(found.Copy());
            }

            return Task.FromResult<OnlineChatReadMark>(null);
        }
    }

    /// <summary>
    /// 判定消息是否严格位于游标之后（稳定排序键 <c>(SentAtTime, Sequence)</c> 的字典序比较）。
    /// </summary>
    /// <param name="message">消息。</param>
    /// <param name="afterSentAtTime">游标位置的发送时刻。</param>
    /// <param name="afterSequence">游标位置的频道内序号。</param>
    /// <returns>位于游标之后返回 <c>true</c>。</returns>
    private static bool IsAfterCursor(OnlineChatMessage message, long afterSentAtTime, long afterSequence)
    {
        if (message.SentAtTime > afterSentAtTime)
        {
            return true;
        }

        if (message.SentAtTime < afterSentAtTime)
        {
            return false;
        }

        return message.Sequence > afterSequence;
    }

    /// <summary>
    /// 按稳定排序键比较两条消息（升序）。
    /// </summary>
    /// <param name="left">左侧消息。</param>
    /// <param name="right">右侧消息。</param>
    /// <returns>比较结果。</returns>
    private static int CompareBySortKey(OnlineChatMessage left, OnlineChatMessage right)
    {
        if (left.SentAtTime != right.SentAtTime)
        {
            return left.SentAtTime < right.SentAtTime ? -1 : 1;
        }

        if (left.Sequence != right.Sequence)
        {
            return left.Sequence < right.Sequence ? -1 : 1;
        }

        return 0;
    }

    /// <summary>
    /// 构造频道索引键（作用域 + 频道标识）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="channelId">频道标识。</param>
    /// <returns>索引键。</returns>
    private static string BuildChannelKey(long tenantId, long appId, string channelId)
    {
        return "chan:" + tenantId + ":" + appId + ":" + (channelId ?? string.Empty);
    }

    /// <summary>
    /// 构造已读位点索引键（作用域 + 玩家 + 频道标识）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="channelId">频道标识。</param>
    /// <returns>索引键。</returns>
    private static string BuildReadMarkKey(long tenantId, long appId, long playerId, string channelId)
    {
        return "rmk:" + tenantId + ":" + appId + ":" + playerId + ":" + (channelId ?? string.Empty);
    }
}
