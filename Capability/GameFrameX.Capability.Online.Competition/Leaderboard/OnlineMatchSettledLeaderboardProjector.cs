// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the PRC and relevant international regulations.
//   使用本项目须严格遵守相关法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with relevant laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file at the root of the source code for the full license text.
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
//   CNB  仓库：https://www.cnb.cool/GameFrameX
//   CNB Repository: https://www.cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Events;
using GameFrameX.Capability.Online.Abstractions.Idempotency;
using GameFrameX.Capability.Online.Abstractions.Scope;
using GameFrameX.Capability.Online.Match;
using GameFrameX.Foundation.Idempotency;

namespace GameFrameX.Capability.Online.Competition.Leaderboard;

/// <summary>
/// Match.Settled → 排行榜投影订阅体（C245 Batch B）：消费对局结算完成事件，经
/// <see cref="OnlineLeaderboardResultProjector"/>（可信源 result store + MatchResultId 核对 + 幂等键）
/// 投影到预置榜单；失败进待重试表，由 tick job <c>leaderboard-projection-retry</c> 逐轮重投。
/// <para>
/// 维护约束（红线）：
/// (1) **双层去重**——<see cref="OnlineEventConsumer"/>（EventId 去重，Operations 审计桥先例）挡同事件重投，
/// 投影器幂等键（lb-{MatchResultId}-{LeaderboardId}-{PlayerId}）挡不同 EventId 同结果的重复投递；
/// (2) **异常不外抛**——订阅体异常吞掉记录（先例：Operations 审计桥「投影异常不得阻断业务事件分发」），
/// 失败只进待重试表经 <see cref="QueryStatus"/> 可观测；不可解析载荷无从重投，直接丢弃；
/// (3) **终态裁决**——重投时结果事实已不存在（store 清理）视作不可投影终态，移除待重试条目。
/// </para>
/// </summary>
public sealed class OnlineMatchSettledLeaderboardProjector
{
    /// <summary>可信结果写榜投影器（真实投影执行体）。</summary>
    private readonly OnlineLeaderboardResultProjector _projector;

    /// <summary>对局结算结果存储（终态裁决用）。</summary>
    private readonly IOnlineMatchResultStore _matchResultStore;

    /// <summary>目标榜单标识。</summary>
    private readonly string _leaderboardId;

    /// <summary>EventId 去重消费者（同事件重投短路）。</summary>
    private readonly OnlineEventConsumer _consumer;

    /// <summary>待重试表与并发锁。</summary>
    private readonly object _pendingLock = new();

    /// <summary>待重试条目（键 = MatchResultId，去重）。</summary>
    private readonly Dictionary<string, PendingProjection> _pending = new(StringComparer.Ordinal);

    /// <summary>
    /// 初始化 <see cref="OnlineMatchSettledLeaderboardProjector"/>。
    /// </summary>
    /// <param name="matchResultStore">对局结算结果存储（可信事实唯一来源）。</param>
    /// <param name="leaderboardService">排行榜服务。</param>
    /// <param name="idempotencyService">幂等服务。</param>
    /// <param name="leaderboardId">目标榜单标识。</param>
    public OnlineMatchSettledLeaderboardProjector(IOnlineMatchResultStore matchResultStore, OnlineLeaderboardService leaderboardService, OnlineIdempotencyService idempotencyService, string leaderboardId)
    {
        _matchResultStore = matchResultStore ?? throw new ArgumentNullException(nameof(matchResultStore));
        _projector = new OnlineLeaderboardResultProjector(matchResultStore, leaderboardService, idempotencyService);
        _leaderboardId = leaderboardId ?? throw new ArgumentNullException(nameof(leaderboardId));
        _consumer = new OnlineEventConsumer(new InMemoryEventDeduplicator());
    }

    /// <summary>
    /// 订阅体入口：解析 Match.Settled 载荷并投影到目标榜单；失败（榜单缺失 / 结果缺失 / 异常）记待重试，
    /// 异常不向订阅方抛（吞掉记录）。
    /// </summary>
    /// <param name="onlineEvent">Online 事件（应为 Match.Settled）。</param>
    /// <returns>完成通知。</returns>
    public async Task HandleEventAsync(OnlineEvent onlineEvent)
    {
        MatchSettledPayload payload;
        OnlineEvent sourceEvent = onlineEvent;
        try
        {
            if (onlineEvent == null || onlineEvent.EventType != OnlineMatchRuntimeEvents.MatchSettled)
            {
                return;
            }

            if (!_consumer.TryConsume(onlineEvent))
            {
                return;
            }

            payload = DeserializePayload(onlineEvent.Payload);
            if (payload == null || string.IsNullOrEmpty(payload.MatchResultId) || string.IsNullOrEmpty(payload.MatchId))
            {
                // 载荷不可解析 / 缺标识：无从重投，直接丢弃（不记待重试）。
                return;
            }
        }
        catch (Exception)
        {
            // 去重 / 解析段异常无从定位业务标识，吞掉不外抛、不记待重试。
            return;
        }

        try
        {
            // 榜单级作用域（PlayerId=0）：投影器内部逐玩家以结算事实重建 scope。
            var scope = new OnlineScope(sourceEvent.TenantId, sourceEvent.AppId, sourceEvent.ServerId, 0);
            var projected = await _projector.ProjectAsync(scope, _leaderboardId, payload.MatchId, payload.MatchResultId).ConfigureAwait(false);
            if (!projected.IsSuccess)
            {
                RecordPending(sourceEvent, payload);
            }
        }
        catch (Exception)
        {
            // 投影段异常同样记待重试（重投幂等键保证不双计）；不向订阅方抛。
            RecordPending(sourceEvent, payload);
        }
    }

    /// <summary>
    /// 重投待重试条目（幂等键保证不双计）：成功或结果事实已不存在（不可投影终态）移除；失败 Attempts++ 保留。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>完成通知。</returns>

    public async Task RetryPendingAsync(CancellationToken cancellationToken)
    {
        PendingProjection[] snapshot;
        lock (_pendingLock)
        {
            if (_pending.Count == 0)
            {
                return;
            }

            snapshot = new PendingProjection[_pending.Count];
            _pending.Values.CopyTo(snapshot, 0);
        }

        foreach (var entry in snapshot)
        {
            var stored = await _matchResultStore.FindByMatchAsync(entry.TenantId, entry.AppId, entry.MatchId, cancellationToken).ConfigureAwait(false);
            if (stored == null)
            {
                // 结果已被清理：不可投影终态，移除。
                RemovePending(entry.MatchResultId);
                continue;
            }

            var scope = new OnlineScope(entry.TenantId, entry.AppId, entry.ServerId, 0);
            var projected = await _projector.ProjectAsync(scope, _leaderboardId, entry.MatchId, entry.MatchResultId, 0, cancellationToken).ConfigureAwait(false);
            if (projected.IsSuccess)
            {
                RemovePending(entry.MatchResultId);
            }
            else
            {
                BumpPending(entry.MatchResultId);
            }
        }
    }

    /// <summary>
    /// 查询待重试条目快照（admin 可观测面；条目 = MatchResultId / MatchId / Attempts）。
    /// </summary>
    /// <returns>待重试条目副本。</returns>
    public IReadOnlyList<OnlineLeaderboardProjectionRetryStatus> QueryStatus()
    {
        lock (_pendingLock)
        {
            var items = new List<OnlineLeaderboardProjectionRetryStatus>(_pending.Count);
            foreach (var entry in _pending.Values)
            {
                items.Add(new OnlineLeaderboardProjectionRetryStatus
                {
                    MatchResultId = entry.MatchResultId,
                    MatchId = entry.MatchId,
                    Attempts = entry.Attempts,
                });
            }

            return items;
        }
    }

    /// <summary>
    /// 反序列化结算事件载荷（字段名与 MatchSettledPayload 线缆形态一致）。
    /// </summary>
    /// <param name="payload">UTF8 JSON 载荷。</param>
    /// <returns>载荷；不可解析返回 null。</returns>
    private static MatchSettledPayload DeserializePayload(ReadOnlyMemory<byte> payload)
    {
        if (payload.IsEmpty)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<MatchSettledPayload>(payload.Span);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 记录待重试条目（按 MatchResultId 去重；重复失败 Attempts 不衰减）。
    /// </summary>
    /// <param name="onlineEvent">来源事件（作用域三元组）。</param>
    /// <param name="payload">已解析载荷。</param>
    private void RecordPending(OnlineEvent onlineEvent, MatchSettledPayload payload)
    {
        lock (_pendingLock)
        {
            if (_pending.TryGetValue(payload.MatchResultId, out var existing))
            {
                existing.Attempts++;
                return;
            }

            _pending[payload.MatchResultId] = new PendingProjection
            {
                MatchResultId = payload.MatchResultId,
                MatchId = payload.MatchId,
                TenantId = onlineEvent.TenantId,
                AppId = onlineEvent.AppId,
                ServerId = onlineEvent.ServerId,
                Attempts = 1,
            };
        }
    }

    /// <summary>
    /// 移除待重试条目。
    /// </summary>
    /// <param name="matchResultId">结果标识。</param>
    private void RemovePending(string matchResultId)
    {
        lock (_pendingLock)
        {
            _pending.Remove(matchResultId);
        }
    }

    /// <summary>
    /// 重投失败：Attempts++ 保留。
    /// </summary>
    /// <param name="matchResultId">结果标识。</param>
    private void BumpPending(string matchResultId)
    {
        lock (_pendingLock)
        {
            if (_pending.TryGetValue(matchResultId, out var existing))
            {
                existing.Attempts++;
            }
        }
    }

    /// <summary>
    /// 反序列化结算事件载荷（字段名与 MatchSettledPayload 线缆形态一致）。
    /// </summary>
    /// <param name="payload">UTF8 JSON 载荷。</param>
    /// <returns>载荷；不可解析返回 null。</returns>
    private static MatchSettledPayload DeserializePayload(byte[] payload)
    {
        if (payload == null || payload.Length == 0)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<MatchSettledPayload>(payload);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 结算事件负载（私有 DTO，字段名与 <c>OnlineMatchRuntimeEvents</c> 的 MatchSettledPayload 一致）。
    /// </summary>
    private sealed class MatchSettledPayload
    {
        /// <summary>结果标识（幂等边界）。</summary>
        public string MatchResultId { get; set; }

        /// <summary>对局标识。</summary>
        public string MatchId { get; set; }

        /// <summary>玩法模式。</summary>
        public int Mode { get; set; }

        /// <summary>区域。</summary>
        public int Region { get; set; }

        /// <summary>结束方式名。</summary>
        public string Outcome { get; set; }

        /// <summary>结算时刻（UTC 毫秒）。</summary>
        public long SettledTime { get; set; }

        /// <summary>玩家条目集合。</summary>
        public List<MatchSettledEntryPayload> Entries { get; set; }
    }

    /// <summary>
    /// 结算事件单玩家条目负载。
    /// </summary>
    private sealed class MatchSettledEntryPayload
    {
        /// <summary>玩家标识。</summary>
        public long PlayerId { get; set; }

        /// <summary>名次。</summary>
        public int Rank { get; set; }

        /// <summary>是否胜方。</summary>
        public bool IsWinner { get; set; }

        /// <summary>玩法得分。</summary>
        public long Score { get; set; }

        /// <summary>奖励条目数。</summary>
        public int RewardCount { get; set; }
    }

    /// <summary>
    /// 待重试条目（进程内易失，重启失效与 D3 边界一致）。
    /// </summary>
    private sealed class PendingProjection
    {
        /// <summary>结果标识。</summary>
        public string MatchResultId { get; set; }

        /// <summary>对局标识。</summary>
        public string MatchId { get; set; }

        /// <summary>租户标识。</summary>
        public long TenantId { get; set; }

        /// <summary>App 标识。</summary>
        public long AppId { get; set; }

        /// <summary>区服标识。</summary>
        public long ServerId { get; set; }

        /// <summary>累计投递尝试次数（首发失败 = 1）。</summary>
        public int Attempts { get; set; }
    }
}

/// <summary>
/// 排行榜投影待重试条目状态（QueryStatus 投影形态）。
/// </summary>
public sealed class OnlineLeaderboardProjectionRetryStatus
{
    /// <summary>获取结算结果标识。</summary>
    public string MatchResultId { get; set; }

    /// <summary>获取对局标识。</summary>
    public string MatchId { get; set; }

    /// <summary>获取累计投递尝试次数。</summary>
    public int Attempts { get; set; }
}
