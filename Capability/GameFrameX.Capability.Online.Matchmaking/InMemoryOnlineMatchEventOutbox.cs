//  ==========================================================================================
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
//   侵犯公民合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
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

namespace GameFrameX.Capability.Online.Matchmaking;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.Abstractions.Events;

/// <summary>
/// 匹配事件发件箱内存实现（C374；单锁串行，条目以 (TenantId, AppId, EventId) 为主键幂等追加）。
/// <para>
/// 维护约束：条目一旦投递成功即标记 Delivered 且不再出现在 <see cref="ListPendingAsync"/> 结果中；
/// 追加按 EventId 幂等（重复标识跳过不覆盖），保证重放写入不产生重复投递义务。
/// 与 <see cref="InMemoryOnlineMatchTicketStore"/> 同为进程内实现——进程重启即清空，
/// 持久化落地后可在同一契约接缝升级为与 Commit 同事务写入。
/// </para>
/// </summary>
public sealed class InMemoryOnlineMatchEventOutbox : IOnlineMatchEventOutbox
{
    /// <summary>并发保护锁（追加 / 查询 / 标记共用一个临界区）。</summary>
    private readonly object _syncRoot = new object();

    /// <summary>发件箱条目表（键 = TenantId:AppId:EventId）。</summary>
    private readonly Dictionary<string, OnlineMatchEventOutboxEntry> _entries = new Dictionary<string, OnlineMatchEventOutboxEntry>();

    /// <summary>追加序号（同刻写入的稳定排序兜底）。</summary>
    private long _sequence;

    /// <summary>
    /// 幂等追加一批事件：按 <see cref="OnlineEvent.EventId"/> 去重（已存在即跳过），新条目按追加顺序登记为待投递。
    /// </summary>
    /// <param name="events">待投递事件。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>完成通知。</returns>
    public Task AppendAsync(IReadOnlyList<OnlineEvent> events, CancellationToken cancellationToken = default)
    {
        if (events == null || events.Count == 0)
        {
            return Task.CompletedTask;
        }

        lock (_syncRoot)
        {
            foreach (var onlineEvent in events)
            {
                if (onlineEvent == null || string.IsNullOrEmpty(onlineEvent.EventId))
                {
                    continue;
                }

                var key = BuildKey(onlineEvent.TenantId, onlineEvent.AppId, onlineEvent.EventId);
                if (_entries.ContainsKey(key))
                {
                    continue;
                }

                _entries[key] = new OnlineMatchEventOutboxEntry
                {
                    EventId = onlineEvent.EventId,
                    TenantId = onlineEvent.TenantId,
                    AppId = onlineEvent.AppId,
                    Event = onlineEvent,
                    Delivered = false,
                    SequenceNumber = Interlocked.Increment(ref _sequence),
                };
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 列出作用域内全部待投递条目（按登记顺序；不含已投递条目）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>待投递条目列表。</returns>
    public Task<IReadOnlyList<OnlineMatchEventOutboxEntry>> ListPendingAsync(long tenantId, long appId, CancellationToken cancellationToken = default)
    {
        var pending = new List<OnlineMatchEventOutboxEntry>();

        lock (_syncRoot)
        {
            foreach (var entry in _entries.Values)
            {
                if (entry.TenantId == tenantId && entry.AppId == appId && !entry.Delivered)
                {
                    pending.Add(entry);
                }
            }

            pending.Sort((left, right) => left.SequenceNumber.CompareTo(right.SequenceNumber));
        }

        return Task.FromResult<IReadOnlyList<OnlineMatchEventOutboxEntry>>(pending);
    }

    /// <summary>
    /// 标记条目已投递；条目不存在时静默返回（重投竞态下对端可能已标记/清理）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="eventId">事件标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>完成通知。</returns>
    public Task MarkDeliveredAsync(long tenantId, long appId, string eventId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(eventId))
        {
            return Task.CompletedTask;
        }

        lock (_syncRoot)
        {
            var key = BuildKey(tenantId, appId, eventId);
            if (_entries.TryGetValue(key, out var entry))
            {
                entry.Delivered = true;
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 构造条目主键（TenantId:AppId:EventId）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="eventId">事件标识。</param>
    /// <returns>条目主键。</returns>
    private static string BuildKey(long tenantId, long appId, string eventId)
    {
        return tenantId.ToString() + ":" + appId.ToString() + ":" + eventId;
    }
}
