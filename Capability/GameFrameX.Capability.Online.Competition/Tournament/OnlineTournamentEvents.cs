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

namespace GameFrameX.Capability.Online.Competition.Tournament;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using GameFrameX.Capability.Online.Abstractions.Events;

/// <summary>
/// 赛事域事件工厂（复用 <see cref="OnlineEvent"/> 信封，<c>Source = "online-tournament"</c>）。
/// <para>
/// 维护约束（红线）：
/// (1) **不再造第二套信封**——赛事事件与赛季 / 排行榜事件同用统一信封与投影消费口径，第六类事件接入
/// 因而不是「新增一套事件协议」而是「新增一组事件类型与负载」；
/// (2) 事件是**事实不是状态**——<c>Ended</c> 只陈述「赛事已结束、成绩已冻结 N 条」，条目明细不进事件
/// （明细经结果查询读取，事件只留计数与时刻作为审计锚点）；
/// (3) <c>Settled</c> 只陈述本次结算的发放 / 回放 / 失败计数，**不含失败玩家明细**（明细经结算回执返回运维）；
/// (4) <c>Registered</c> 是**玩家维度事件**（信封 <c>PlayerId</c> 非 0），负载只含该玩家自身的报名事实与
/// 报名时刻的资格判定输入，不含任何其他玩家数据。
/// </para>
/// </summary>
public static class OnlineTournamentEvents
{
    /// <summary>赛事创建（排期已登记）。</summary>
    public const string TournamentCreated = "Online.Tournament.Created";

    /// <summary>赛事开始（进入进行中，报名窗口关闭）。</summary>
    public const string TournamentStarted = "Online.Tournament.Started";

    /// <summary>玩家报名成功（玩家维度事件；重放报名不重发）。</summary>
    public const string TournamentRegistered = "Online.Tournament.Registered";

    /// <summary>赛事结束（成绩已从关联榜单冻结落档；**不重置榜单**）。</summary>
    public const string TournamentEnded = "Online.Tournament.Ended";

    /// <summary>赛事结算（奖励已按冻结成绩名次发放完毕）。</summary>
    public const string TournamentSettled = "Online.Tournament.Settled";

    /// <summary>事件来源标识。</summary>
    public const string Source = "online-tournament";

    /// <summary>
    /// 构造赛事创建事件。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="tournamentId">赛事标识。</param>
    /// <param name="leaderboardId">关联榜单标识。</param>
    /// <param name="startTime">排期开始时刻（UTC 毫秒）。</param>
    /// <param name="endTime">排期结束时刻（UTC 毫秒）。</param>
    /// <param name="minLeaderboardRank">报名资格的名次上限（0 表示不限制）。</param>
    /// <param name="rewardRuleCount">奖励规则条数。</param>
    /// <param name="state">创建后的状态名。</param>
    /// <param name="createdTime">创建时刻（UTC 毫秒）。</param>
    /// <returns>事件信封实例。</returns>
    public static OnlineEvent CreateTournamentCreated(long tenantId, long appId, string tournamentId, string leaderboardId, long startTime, long endTime, int minLeaderboardRank, int rewardRuleCount, string state, long createdTime)
    {
        var payload = new TournamentCreatedPayload
        {
            TournamentId = tournamentId ?? string.Empty,
            LeaderboardId = leaderboardId ?? string.Empty,
            StartTime = startTime,
            EndTime = endTime,
            MinLeaderboardRank = minLeaderboardRank,
            RewardRuleCount = rewardRuleCount,
            State = state,
        };
        var auditFields = new Dictionary<string, string>
        {
            { "TournamentId", tournamentId ?? string.Empty },
            { "LeaderboardId", leaderboardId ?? string.Empty },
            { "StartTime", startTime.ToString(CultureInfo.InvariantCulture) },
            { "EndTime", endTime.ToString(CultureInfo.InvariantCulture) },
            { "MinLeaderboardRank", payload.MinLeaderboardRank.ToString(CultureInfo.InvariantCulture) },
            { "RewardRuleCount", rewardRuleCount.ToString(CultureInfo.InvariantCulture) },
            { "State", state },
        };
        return new OnlineEvent
        {
            EventId = "evt-" + Guid.NewGuid().ToString("N"),
            EventType = TournamentCreated,
            OccurredTime = createdTime > 0 ? createdTime : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SchemaVersion = 1,
            TenantId = tenantId,
            AppId = appId,
            ServerId = 0,
            PlayerId = 0,
            Source = Source,
            CorrelationId = tournamentId ?? string.Empty,
            Payload = JsonSerializer.SerializeToUtf8Bytes(payload),
            PayloadAuditFields = auditFields,
        };
    }

    /// <summary>
    /// 构造赛事开始事件。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="tournamentId">赛事标识。</param>
    /// <param name="leaderboardId">关联榜单标识。</param>
    /// <param name="startTime">排期开始时刻（UTC 毫秒）。</param>
    /// <param name="endTime">排期结束时刻（UTC 毫秒）。</param>
    /// <param name="startedTime">开始时刻（UTC 毫秒）。</param>
    /// <returns>事件信封实例。</returns>
    public static OnlineEvent CreateTournamentStarted(long tenantId, long appId, string tournamentId, string leaderboardId, long startTime, long endTime, long startedTime)
    {
        var payload = new TournamentStartedPayload
        {
            TournamentId = tournamentId ?? string.Empty,
            LeaderboardId = leaderboardId ?? string.Empty,
            StartTime = startTime,
            EndTime = endTime,
        };
        var auditFields = new Dictionary<string, string>
        {
            { "TournamentId", tournamentId ?? string.Empty },
            { "LeaderboardId", leaderboardId ?? string.Empty },
            { "StartTime", startTime.ToString(CultureInfo.InvariantCulture) },
            { "EndTime", endTime.ToString(CultureInfo.InvariantCulture) },
        };
        return new OnlineEvent
        {
            EventId = "evt-" + Guid.NewGuid().ToString("N"),
            EventType = TournamentStarted,
            OccurredTime = startedTime > 0 ? startedTime : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SchemaVersion = 1,
            TenantId = tenantId,
            AppId = appId,
            ServerId = 0,
            PlayerId = 0,
            Source = Source,
            CorrelationId = tournamentId ?? string.Empty,
            Payload = JsonSerializer.SerializeToUtf8Bytes(payload),
            PayloadAuditFields = auditFields,
        };
    }

    /// <summary>
    /// 构造玩家报名事件（玩家维度；仅在**首次**落档报名时发布，重放报名不重发）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="tournamentId">赛事标识。</param>
    /// <param name="playerId">报名玩家标识。</param>
    /// <param name="registeredTime">报名时刻（UTC 毫秒）。</param>
    /// <param name="rankAtRegistration">报名时刻的名次（无资格门槛未查榜时为 0）。</param>
    /// <param name="scoreAtRegistration">报名时刻的分数（无资格门槛未查榜时为 0）。</param>
    /// <returns>事件信封实例。</returns>
    public static OnlineEvent CreateTournamentRegistered(long tenantId, long appId, string tournamentId, long playerId, long registeredTime, int rankAtRegistration, long scoreAtRegistration)
    {
        var payload = new TournamentRegisteredPayload
        {
            TournamentId = tournamentId ?? string.Empty,
            PlayerId = playerId,
            RegisteredTime = registeredTime,
            RankAtRegistration = rankAtRegistration,
            ScoreAtRegistration = scoreAtRegistration,
        };
        var auditFields = new Dictionary<string, string>
        {
            { "TournamentId", tournamentId ?? string.Empty },
            { "PlayerId", playerId.ToString(CultureInfo.InvariantCulture) },
            { "RegisteredTime", registeredTime.ToString(CultureInfo.InvariantCulture) },
            { "RankAtRegistration", rankAtRegistration.ToString(CultureInfo.InvariantCulture) },
            { "ScoreAtRegistration", scoreAtRegistration.ToString(CultureInfo.InvariantCulture) },
        };
        return new OnlineEvent
        {
            EventId = "evt-" + Guid.NewGuid().ToString("N"),
            EventType = TournamentRegistered,
            OccurredTime = registeredTime > 0 ? registeredTime : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SchemaVersion = 1,
            TenantId = tenantId,
            AppId = appId,
            ServerId = 0,
            PlayerId = playerId,
            Source = Source,
            CorrelationId = tournamentId ?? string.Empty,
            Payload = JsonSerializer.SerializeToUtf8Bytes(payload),
            PayloadAuditFields = auditFields,
        };
    }

    /// <summary>
    /// 构造赛事结束事件（成绩冻结先行已完成的落定点；关联榜单未被本流程修改）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="tournamentId">赛事标识。</param>
    /// <param name="leaderboardId">关联榜单标识。</param>
    /// <param name="standingsEntryCount">冻结成绩的条目数（仅计已报名参赛者）。</param>
    /// <param name="frozenTime">成绩冻结时刻（UTC 毫秒）。</param>
    /// <param name="endedTime">结束时刻（UTC 毫秒）。</param>
    /// <returns>事件信封实例。</returns>
    public static OnlineEvent CreateTournamentEnded(long tenantId, long appId, string tournamentId, string leaderboardId, int standingsEntryCount, long frozenTime, long endedTime)
    {
        var payload = new TournamentEndedPayload
        {
            TournamentId = tournamentId ?? string.Empty,
            LeaderboardId = leaderboardId ?? string.Empty,
            StandingsEntryCount = standingsEntryCount,
            FrozenTime = frozenTime,
        };
        var auditFields = new Dictionary<string, string>
        {
            { "TournamentId", tournamentId ?? string.Empty },
            { "LeaderboardId", leaderboardId ?? string.Empty },
            { "StandingsEntryCount", standingsEntryCount.ToString(CultureInfo.InvariantCulture) },
            { "FrozenTime", frozenTime.ToString(CultureInfo.InvariantCulture) },
        };
        return new OnlineEvent
        {
            EventId = "evt-" + Guid.NewGuid().ToString("N"),
            EventType = TournamentEnded,
            OccurredTime = endedTime > 0 ? endedTime : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SchemaVersion = 1,
            TenantId = tenantId,
            AppId = appId,
            ServerId = 0,
            PlayerId = 0,
            Source = Source,
            CorrelationId = tournamentId ?? string.Empty,
            Payload = JsonSerializer.SerializeToUtf8Bytes(payload),
            PayloadAuditFields = auditFields,
        };
    }

    /// <summary>
    /// 构造赛事结算事件（仅在全量发放成功、赛事推进到结算完成态时发布）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="tournamentId">赛事标识。</param>
    /// <param name="grantedCount">本次首次发放成功玩家数。</param>
    /// <param name="replayCount">本次命中幂等回放玩家数。</param>
    /// <param name="failedCount">本次发放失败玩家数（正常路径恒为 0）。</param>
    /// <param name="settledTime">结算时刻（UTC 毫秒）。</param>
    /// <returns>事件信封实例。</returns>
    public static OnlineEvent CreateTournamentSettled(long tenantId, long appId, string tournamentId, int grantedCount, int replayCount, int failedCount, long settledTime)
    {
        var payload = new TournamentSettledPayload
        {
            TournamentId = tournamentId ?? string.Empty,
            GrantedCount = grantedCount,
            ReplayCount = replayCount,
            FailedCount = failedCount,
        };
        var auditFields = new Dictionary<string, string>
        {
            { "TournamentId", tournamentId ?? string.Empty },
            { "GrantedCount", grantedCount.ToString(CultureInfo.InvariantCulture) },
            { "ReplayCount", replayCount.ToString(CultureInfo.InvariantCulture) },
            { "FailedCount", failedCount.ToString(CultureInfo.InvariantCulture) },
        };
        return new OnlineEvent
        {
            EventId = "evt-" + Guid.NewGuid().ToString("N"),
            EventType = TournamentSettled,
            OccurredTime = settledTime > 0 ? settledTime : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SchemaVersion = 1,
            TenantId = tenantId,
            AppId = appId,
            ServerId = 0,
            PlayerId = 0,
            Source = Source,
            CorrelationId = tournamentId ?? string.Empty,
            Payload = JsonSerializer.SerializeToUtf8Bytes(payload),
            PayloadAuditFields = auditFields,
        };
    }

    /// <summary>
    /// 赛事创建事件负载（SchemaVersion=1）。
    /// </summary>
    private sealed class TournamentCreatedPayload
    {
        /// <summary>赛事标识。</summary>
        public string TournamentId { get; set; }

        /// <summary>关联榜单标识。</summary>
        public string LeaderboardId { get; set; }

        /// <summary>排期开始时刻（UTC 毫秒）。</summary>
        public long StartTime { get; set; }

        /// <summary>排期结束时刻（UTC 毫秒）。</summary>
        public long EndTime { get; set; }

        /// <summary>报名资格的名次上限（0 表示不限制）。</summary>
        public int MinLeaderboardRank { get; set; }

        /// <summary>奖励规则条数。</summary>
        public int RewardRuleCount { get; set; }

        /// <summary>创建后的状态名。</summary>
        public string State { get; set; }
    }

    /// <summary>
    /// 赛事开始事件负载（SchemaVersion=1）。
    /// </summary>
    private sealed class TournamentStartedPayload
    {
        /// <summary>赛事标识。</summary>
        public string TournamentId { get; set; }

        /// <summary>关联榜单标识。</summary>
        public string LeaderboardId { get; set; }

        /// <summary>排期开始时刻（UTC 毫秒）。</summary>
        public long StartTime { get; set; }

        /// <summary>排期结束时刻（UTC 毫秒）。</summary>
        public long EndTime { get; set; }
    }

    /// <summary>
    /// 玩家报名事件负载（SchemaVersion=1；玩家维度，仅含该玩家自身事实）。
    /// </summary>
    private sealed class TournamentRegisteredPayload
    {
        /// <summary>赛事标识。</summary>
        public string TournamentId { get; set; }

        /// <summary>报名玩家标识（与信封 PlayerId 同值）。</summary>
        public long PlayerId { get; set; }

        /// <summary>报名时刻（UTC 毫秒）。</summary>
        public long RegisteredTime { get; set; }

        /// <summary>报名时刻的名次（无资格门槛未查榜时为 0）。</summary>
        public int RankAtRegistration { get; set; }

        /// <summary>报名时刻的分数（无资格门槛未查榜时为 0）。</summary>
        public long ScoreAtRegistration { get; set; }
    }

    /// <summary>
    /// 赛事结束事件负载（SchemaVersion=1）。
    /// </summary>
    private sealed class TournamentEndedPayload
    {
        /// <summary>赛事标识。</summary>
        public string TournamentId { get; set; }

        /// <summary>关联榜单标识。</summary>
        public string LeaderboardId { get; set; }

        /// <summary>冻结成绩的条目数（仅计已报名参赛者）。</summary>
        public int StandingsEntryCount { get; set; }

        /// <summary>成绩冻结时刻（UTC 毫秒）。</summary>
        public long FrozenTime { get; set; }
    }

    /// <summary>
    /// 赛事结算事件负载（SchemaVersion=1）。
    /// </summary>
    private sealed class TournamentSettledPayload
    {
        /// <summary>赛事标识。</summary>
        public string TournamentId { get; set; }

        /// <summary>本次首次发放成功玩家数。</summary>
        public int GrantedCount { get; set; }

        /// <summary>本次命中幂等回放玩家数。</summary>
        public int ReplayCount { get; set; }

        /// <summary>本次发放失败玩家数（正常路径恒为 0）。</summary>
        public int FailedCount { get; set; }
    }
}