// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
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

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Online.Events;

namespace GameFrameX.Online.GameEvents;

/// <summary>
/// 游戏事件存储内存实现（单进程默认；生产持久化与留存归档归 Server 仓运行时装配，X4）。
/// <para>
/// 维护约束（红线）：事件标识去重与追加在**同一临界区**完成（并发重投不会落两条）；
/// 时间窗为**闭区间**（[from, to]），供指标复算得到确定结果；列表按事件发生时刻升序返回。
/// </para>
/// </summary>
public sealed class InMemoryOnlineGameEventStore : IOnlineGameEventStore
{
    /// <summary>全局锁。</summary>
    private readonly object _syncRoot = new object();

    /// <summary>事件表：键 = EventId（幂等键）。</summary>
    private readonly Dictionary<string, OnlineEvent> _events = new Dictionary<string, OnlineEvent>();

    /// <summary>
    /// 初始化 <see cref="InMemoryOnlineGameEventStore"/>。
    /// </summary>
    public InMemoryOnlineGameEventStore()
    {
    }

    /// <summary>
    /// 在全局锁内的同一临界区完成事件标识去重与追加，重复投递只落一条。
    /// </summary>
    /// <remarks>
    /// Deduplicates by event id and appends within the same critical section under the global lock, so a duplicate delivery is stored only once.
    /// </remarks>
    /// <param name="onlineEvent">通过 L0 校验的事件信封 / The event envelope that passed L0 validation</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>本次是否新落档（EventId 已存在返回 false）/ Whether this call newly stored the event (false when the EventId already exists)</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="onlineEvent"/> 为 null 时抛出 / Thrown when <paramref name="onlineEvent"/> is null</exception>
    /// <exception cref="ArgumentException">当 <paramref name="onlineEvent"/> 的 EventId 为 null 或空时抛出 / Thrown when the EventId of <paramref name="onlineEvent"/> is null or empty</exception>
    public Task<bool> AppendAsync(OnlineEvent onlineEvent, CancellationToken cancellationToken = default)
    {
        if (onlineEvent == null)
        {
            throw new ArgumentNullException(nameof(onlineEvent));
        }

        var eventId = onlineEvent.EventId;
        if (string.IsNullOrEmpty(eventId))
        {
            throw new ArgumentException("事件标识不能为空（EventId 是存储幂等键）", nameof(onlineEvent));
        }

        lock (_syncRoot)
        {
            if (_events.ContainsKey(eventId))
            {
                return Task.FromResult(false);
            }

            _events[eventId] = onlineEvent;
            return Task.FromResult(true);
        }
    }

    /// <summary>
    /// 在全局锁内按事件标识（幂等键）查找内存事件表中的事件。
    /// </summary>
    /// <remarks>
    /// Looks up the in-memory event table by event id (the idempotency key) under the global lock.
    /// </remarks>
    /// <param name="eventId">事件标识 / The event id</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>事件信封；不存在返回 null / The event envelope; null when not found</returns>
    public Task<OnlineEvent> FindAsync(string eventId, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            return Task.FromResult(_events.TryGetValue(eventId ?? string.Empty, out var stored) ? stored : null);
        }
    }

    /// <summary>
    /// 在全局锁内按作用域与闭区间时间窗筛选事件，并按事件发生时刻升序排序返回。
    /// </summary>
    /// <remarks>
    /// Filters events by scope and inclusive time window under the global lock, returning them sorted ascending by occurred time.
    /// </remarks>
    /// <param name="tenantId">租户标识 / The tenant id</param>
    /// <param name="appId">App 标识 / The app id</param>
    /// <param name="fromTime">窗口起点（UTC 毫秒，含）/ Window start (UTC milliseconds, inclusive)</param>
    /// <param name="toTime">窗口终点（UTC 毫秒，含）/ Window end (UTC milliseconds, inclusive)</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>事件列表；无记录返回空列表 / The event list; an empty list when none match</returns>
    public Task<List<OnlineEvent>> ListAsync(long tenantId, long appId, long fromTime, long toTime, CancellationToken cancellationToken = default)
    {
        var matched = new List<OnlineEvent>();
        lock (_syncRoot)
        {
            foreach (var stored in _events.Values)
            {
                if (stored.TenantId != tenantId || stored.AppId != appId)
                {
                    continue;
                }

                if (stored.OccurredTime < fromTime || stored.OccurredTime > toTime)
                {
                    continue;
                }

                matched.Add(stored);
            }
        }

        matched.Sort(CompareByOccurredTime);
        return Task.FromResult(matched);
    }

    /// <summary>
    /// 按事件发生时刻升序比较（同一时刻以事件标识稳定消解，保证列表顺序可复算）。
    /// </summary>
    /// <param name="left">左侧事件。</param>
    /// <param name="right">右侧事件。</param>
    /// <returns>比较结果。</returns>
    private static int CompareByOccurredTime(OnlineEvent left, OnlineEvent right)
    {
        var compared = left.OccurredTime.CompareTo(right.OccurredTime);
        if (compared != 0)
        {
            return compared;
        }

        return string.CompareOrdinal(left.EventId ?? string.Empty, right.EventId ?? string.Empty);
    }
}
