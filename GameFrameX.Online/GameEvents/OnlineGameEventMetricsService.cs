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

namespace GameFrameX.Online.GameEvents;

/// <summary>
/// 游戏事件指标复算服务（vault:C8 S7.5 / VC-7.10）：从原始事件**现场复算**窗口指标。
/// <para>
/// 维护约束（红线）：
/// (1) **只读原始事件、不维护累计计数器**——累计计数在丢包 / 重投 / 重启下必然与原始事件漂移，
/// 而复算的偏差恒为 0（同一窗口同一批事件 = 同一结果），这是本 change「可复算」的可验证含义；
/// (2) 分类由**登记表**归属推导（<see cref="OnlineGameEventSchema"/>），调用方不得另行指定——
/// 否则同一事件名在不同报表里落到不同分类；
/// (3) 窗口为**闭区间**且与存储查询口径一致（复用 <see cref="IOnlineGameEventStore.ListAsync"/>），
/// 边界事件不会被漏计或重复计；
/// (4) 脏事件不在事件存储内，其计数单独取自死信汇（正常应为 0）；
/// (5) 活跃玩家数是**去重**口径（窗口内发生过会话开始的玩家集合大小），不是会话开始的事件条数——
/// 同一玩家重连多次只计 1 人；比值类指标（如胜率）由 Win / Lose 计数派生，不在本层固化比值口径。
/// </para>
/// </summary>
public sealed class OnlineGameEventMetricsService
{
    /// <summary>事件存储（指标的唯一数据来源）。</summary>
    private readonly IOnlineGameEventStore _eventStore;

    /// <summary>死信汇（脏事件计数来源）。</summary>
    private readonly IOnlineGameEventDeadLetterSink _deadLetterSink;

    /// <summary>
    /// 初始化 <see cref="OnlineGameEventMetricsService"/>。
    /// </summary>
    /// <param name="eventStore">事件存储。</param>
    /// <param name="deadLetterSink">死信汇。</param>
    public OnlineGameEventMetricsService(IOnlineGameEventStore eventStore, IOnlineGameEventDeadLetterSink deadLetterSink)
    {
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _deadLetterSink = deadLetterSink ?? throw new ArgumentNullException(nameof(deadLetterSink));
    }

    /// <summary>
    /// 复算指定作用域与时间窗的事件指标。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="fromTime">窗口起点（UTC 毫秒，含）。</param>
    /// <param name="toTime">窗口终点（UTC 毫秒，含）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>指标快照。</returns>
    public async Task<OnlineGameEventMetrics> ComputeAsync(long tenantId, long appId, long fromTime, long toTime, CancellationToken cancellationToken = default)
    {
        var metrics = new OnlineGameEventMetrics
        {
            TenantId = tenantId,
            AppId = appId,
            FromTime = fromTime,
            ToTime = toTime,
        };

        var events = await _eventStore.ListAsync(tenantId, appId, new EventTimeRangeQuery { FromTime = fromTime, ToTime = toTime }, cancellationToken).ConfigureAwait(false);
        if (events != null)
        {
            var activePlayers = new HashSet<long>();
            foreach (var onlineEvent in events)
            {
                if (onlineEvent == null)
                {
                    continue;
                }

                metrics.TotalCount++;
                Accumulate(metrics.CountByName, onlineEvent.EventType ?? string.Empty);

                var descriptor = OnlineGameEventSchema.Find(onlineEvent.EventType);
                if (descriptor != null)
                {
                    Accumulate(metrics.CountByCategory, descriptor.Category.ToString());
                }

                if (onlineEvent.EventType == OnlineGameEventName.SessionStart && onlineEvent.PlayerId > 0)
                {
                    activePlayers.Add(onlineEvent.PlayerId);
                }
            }

            metrics.ActivePlayerCount = activePlayers.Count;
        }

        var deadLetters = await _deadLetterSink.ListAsync(tenantId, appId, new EventTimeRangeQuery { FromTime = fromTime, ToTime = toTime }, cancellationToken).ConfigureAwait(false);
        metrics.RejectedCount = deadLetters == null ? 0 : deadLetters.Count;
        return metrics;
    }

    /// <summary>
    /// 累加一个键的计数（键不存在时以 0 起算）。
    /// </summary>
    /// <param name="counters">计数表。</param>
    /// <param name="key">计数键。</param>
    private static void Accumulate(Dictionary<string, int> counters, string key)
    {
        counters.TryGetValue(key, out var current);
        counters[key] = current + 1;
    }
}
