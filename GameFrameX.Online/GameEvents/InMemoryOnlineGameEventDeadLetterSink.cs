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
/// 死信汇内存实现（单进程默认；生产持久化归 Server 仓运行时装配，X4）。
/// <para>
/// 维护约束（红线）：保留上限内的**最新**记录（超限丢弃最旧）——死信是排查素材而非账本，
/// 无上限增长会拖垮进程；上限与淘汰策略由本实现的容量参数显式声明，不做隐式截断。
/// </para>
/// </summary>
public sealed class InMemoryOnlineGameEventDeadLetterSink : IOnlineGameEventDeadLetterSink
{
    /// <summary>默认保留条数上限。</summary>
    public const int DefaultCapacity = 10000;

    /// <summary>全局锁。</summary>
    private readonly object _syncRoot = new object();

    /// <summary>死信列表（按写入顺序；超上限时从头部淘汰）。</summary>
    private readonly LinkedList<OnlineGameEventDeadLetter> _deadLetters = new LinkedList<OnlineGameEventDeadLetter>();

    /// <summary>保留条数上限。</summary>
    private readonly int _capacity;

    /// <summary>
    /// 初始化 <see cref="InMemoryOnlineGameEventDeadLetterSink"/>。
    /// </summary>
    /// <param name="capacity">保留条数上限（小于 1 时取 <see cref="DefaultCapacity"/>）。</param>
    public InMemoryOnlineGameEventDeadLetterSink(int capacity = DefaultCapacity)
    {
        _capacity = capacity < 1 ? DefaultCapacity : capacity;
    }

    /// <summary>
    /// 在全局锁内写入一条死信，超出保留上限时淘汰最旧记录。
    /// </summary>
    /// <remarks>
    /// Writes a dead letter under the global lock, evicting the oldest records when the retention capacity is exceeded.
    /// </remarks>
    /// <param name="onlineEvent">被拒绝的事件信封 / The rejected event envelope</param>
    /// <param name="reason">拒绝码 / The rejection reason code</param>
    /// <param name="message">拒绝原因 / The rejection message</param>
    /// <param name="rejectedTime">拒收时刻（UTC 毫秒）/ Rejection time (UTC milliseconds)</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>落档后的死信记录 / The archived dead letter record</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="onlineEvent"/> 为 null 时抛出 / Thrown when <paramref name="onlineEvent"/> is null</exception>
    public Task<OnlineGameEventDeadLetter> WriteAsync(OnlineEvent onlineEvent, OnlineGameEventRejectionReason reason, string message, long rejectedTime, CancellationToken cancellationToken = default)
    {
        if (onlineEvent == null)
        {
            throw new ArgumentNullException(nameof(onlineEvent));
        }

        var deadLetter = new OnlineGameEventDeadLetter
        {
            Event = onlineEvent,
            Reason = reason,
            Message = message,
            RejectedTime = rejectedTime,
        };

        lock (_syncRoot)
        {
            _deadLetters.AddLast(deadLetter);
            while (_deadLetters.Count > _capacity)
            {
                _deadLetters.RemoveFirst();
            }
        }

        return Task.FromResult(deadLetter);
    }

    /// <summary>
    /// 在全局锁内按作用域与闭区间时间窗筛选内存中的死信。
    /// </summary>
    /// <remarks>
    /// Filters the in-memory dead letters by scope and inclusive time window under the global lock.
    /// </remarks>
    /// <param name="tenantId">租户标识 / The tenant id</param>
    /// <param name="appId">App 标识 / The app id</param>
    /// <param name="fromTime">窗口起点（UTC 毫秒，含）/ Window start (UTC milliseconds, inclusive)</param>
    /// <param name="toTime">窗口终点（UTC 毫秒，含）/ Window end (UTC milliseconds, inclusive)</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>匹配的死信列表（按写入即拒收顺序升序）；无记录返回空列表 / The matched dead letters in write (rejection) order; an empty list when none match</returns>
    public Task<List<OnlineGameEventDeadLetter>> ListAsync(long tenantId, long appId, long fromTime, long toTime, CancellationToken cancellationToken = default)
    {
        var matched = new List<OnlineGameEventDeadLetter>();
        lock (_syncRoot)
        {
            foreach (var deadLetter in _deadLetters)
            {
                if (deadLetter.RejectedTime < fromTime || deadLetter.RejectedTime > toTime)
                {
                    continue;
                }

                var onlineEvent = deadLetter.Event;
                if (onlineEvent == null || onlineEvent.TenantId != tenantId || onlineEvent.AppId != appId)
                {
                    continue;
                }

                matched.Add(deadLetter);
            }
        }

        return Task.FromResult(matched);
    }
}
