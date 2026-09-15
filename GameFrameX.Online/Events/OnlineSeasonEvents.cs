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
using System.Globalization;
using System.Text.Json;
using GameFrameX.Online.Season;

namespace GameFrameX.Online.Events;

/// <summary>
/// 赛季域事件工厂（vault:C8 S7.3；C93 信封事件，<c>Source = "online-season"</c>）。
/// <para>
/// 维护约束（红线）：事件是**事实不是状态**——<c>Ended</c> 只陈述「赛季已结束、快照已落档 N 条、榜单已重置」，
/// 快照条目明细不进事件（快照本体经赛季快照回溯查询读取，事件只留计数与时刻作为审计锚点）；
/// <c>Settled</c> 只陈述本次结算的发放 / 回放 / 失败计数，**不含逐玩家明细也不含任何玩家私有数据**
/// （失败明细经结算回执返回给运维，不进事件总线）。
/// </para>
/// </summary>
public static class OnlineSeasonEvents
{
    /// <summary>赛季创建（排期已登记）。</summary>
    public const string SeasonCreated = "Online.Season.Created";

    /// <summary>赛季开始（进入进行中）。</summary>
    public const string SeasonStarted = "Online.Season.Started";

    /// <summary>赛季结束（快照已落档、榜单已重置）。</summary>
    public const string SeasonEnded = "Online.Season.Ended";

    /// <summary>赛季结算（奖励已按快照名次发放完毕）。</summary>
    public const string SeasonSettled = "Online.Season.Settled";

    /// <summary>事件来源标识。</summary>
    public const string Source = "online-season";

    /// <summary>
    /// 构造赛季创建事件。
    /// </summary>
    /// <param name="season">创建后的赛季定义。</param>
    /// <returns>事件信封实例。</returns>
    public static OnlineEvent CreateSeasonCreated(OnlineSeason season)
    {
        var rewardRuleCount = season.RewardRules == null ? 0 : season.RewardRules.Count;
        var payload = new SeasonCreatedPayload
        {
            SeasonId = season.SeasonId ?? string.Empty,
            LeaderboardId = season.LeaderboardId ?? string.Empty,
            StartTime = season.StartTime,
            EndTime = season.EndTime,
            RewardRuleCount = rewardRuleCount,
            State = season.State.ToString(),
        };
        var auditFields = new Dictionary<string, string>
        {
            { "SeasonId", season.SeasonId ?? string.Empty },
            { "LeaderboardId", season.LeaderboardId ?? string.Empty },
            { "StartTime", season.StartTime.ToString(CultureInfo.InvariantCulture) },
            { "EndTime", season.EndTime.ToString(CultureInfo.InvariantCulture) },
            { "RewardRuleCount", rewardRuleCount.ToString(CultureInfo.InvariantCulture) },
            { "State", season.State.ToString() },
        };
        return new OnlineEvent
        {
            EventId = "evt-" + Guid.NewGuid().ToString("N"),
            EventType = SeasonCreated,
            OccurredTime = season.CreatedTime > 0 ? season.CreatedTime : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SchemaVersion = 1,
            TenantId = season.TenantId,
            AppId = season.AppId,
            ServerId = 0,
            PlayerId = 0,
            Source = Source,
            CorrelationId = season.SeasonId ?? string.Empty,
            Payload = JsonSerializer.SerializeToUtf8Bytes(payload),
            PayloadAuditFields = auditFields,
        };
    }

    /// <summary>
    /// 构造赛季开始事件。
    /// </summary>
    /// <param name="season">开始后的赛季定义。</param>
    /// <returns>事件信封实例。</returns>
    public static OnlineEvent CreateSeasonStarted(OnlineSeason season)
    {
        var payload = new SeasonStartedPayload
        {
            SeasonId = season.SeasonId ?? string.Empty,
            LeaderboardId = season.LeaderboardId ?? string.Empty,
            StartTime = season.StartTime,
            EndTime = season.EndTime,
        };
        var auditFields = new Dictionary<string, string>
        {
            { "SeasonId", season.SeasonId ?? string.Empty },
            { "LeaderboardId", season.LeaderboardId ?? string.Empty },
            { "StartTime", season.StartTime.ToString(CultureInfo.InvariantCulture) },
            { "EndTime", season.EndTime.ToString(CultureInfo.InvariantCulture) },
        };
        return new OnlineEvent
        {
            EventId = "evt-" + Guid.NewGuid().ToString("N"),
            EventType = SeasonStarted,
            OccurredTime = season.StartTime > 0 ? season.StartTime : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SchemaVersion = 1,
            TenantId = season.TenantId,
            AppId = season.AppId,
            ServerId = 0,
            PlayerId = 0,
            Source = Source,
            CorrelationId = season.SeasonId ?? string.Empty,
            Payload = JsonSerializer.SerializeToUtf8Bytes(payload),
            PayloadAuditFields = auditFields,
        };
    }

    /// <summary>
    /// 构造赛季结束事件（快照先行已完成的落定点）。
    /// </summary>
    /// <param name="season">结束后的赛季定义（<c>EndedTime</c> 已落档）。</param>
    /// <param name="snapshot">落档的历史快照。</param>
    /// <returns>事件信封实例。</returns>
    public static OnlineEvent CreateSeasonEnded(OnlineSeason season, OnlineSeasonSnapshot snapshot)
    {
        var entryCount = snapshot.Entries == null ? 0 : snapshot.Entries.Count;
        var payload = new SeasonEndedPayload
        {
            SeasonId = season.SeasonId ?? string.Empty,
            LeaderboardId = season.LeaderboardId ?? string.Empty,
            SnapshotEntryCount = entryCount,
            SnapshottedTime = snapshot.SnapshottedTime,
        };
        var auditFields = new Dictionary<string, string>
        {
            { "SeasonId", season.SeasonId ?? string.Empty },
            { "LeaderboardId", season.LeaderboardId ?? string.Empty },
            { "SnapshotEntryCount", entryCount.ToString(CultureInfo.InvariantCulture) },
            { "SnapshottedTime", snapshot.SnapshottedTime.ToString(CultureInfo.InvariantCulture) },
        };
        return new OnlineEvent
        {
            EventId = "evt-" + Guid.NewGuid().ToString("N"),
            EventType = SeasonEnded,
            OccurredTime = season.EndedTime > 0 ? season.EndedTime : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SchemaVersion = 1,
            TenantId = season.TenantId,
            AppId = season.AppId,
            ServerId = 0,
            PlayerId = 0,
            Source = Source,
            CorrelationId = season.SeasonId ?? string.Empty,
            Payload = JsonSerializer.SerializeToUtf8Bytes(payload),
            PayloadAuditFields = auditFields,
        };
    }

    /// <summary>
    /// 构造赛季结算事件（仅在全量发放成功、赛季推进到结算完成态时发布）。
    /// </summary>
    /// <param name="season">结算后的赛季定义（<c>SettledTime</c> 已落档）。</param>
    /// <param name="outcome">逐玩家结算回执。</param>
    /// <returns>事件信封实例。</returns>
    public static OnlineEvent CreateSeasonSettled(OnlineSeason season, OnlineSeasonSettlementOutcome outcome)
    {
        var failedCount = outcome.FailedPlayers == null ? 0 : outcome.FailedPlayers.Count;
        var payload = new SeasonSettledPayload
        {
            SeasonId = season.SeasonId ?? string.Empty,
            GrantedCount = outcome.GrantedCount,
            ReplayCount = outcome.ReplayCount,
            FailedCount = failedCount,
        };
        var auditFields = new Dictionary<string, string>
        {
            { "SeasonId", season.SeasonId ?? string.Empty },
            { "GrantedCount", outcome.GrantedCount.ToString(CultureInfo.InvariantCulture) },
            { "ReplayCount", outcome.ReplayCount.ToString(CultureInfo.InvariantCulture) },
            { "FailedCount", failedCount.ToString(CultureInfo.InvariantCulture) },
        };
        return new OnlineEvent
        {
            EventId = "evt-" + Guid.NewGuid().ToString("N"),
            EventType = SeasonSettled,
            OccurredTime = season.SettledTime > 0 ? season.SettledTime : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SchemaVersion = 1,
            TenantId = season.TenantId,
            AppId = season.AppId,
            ServerId = 0,
            PlayerId = 0,
            Source = Source,
            CorrelationId = season.SeasonId ?? string.Empty,
            Payload = JsonSerializer.SerializeToUtf8Bytes(payload),
            PayloadAuditFields = auditFields,
        };
    }

    /// <summary>
    /// 赛季创建事件负载（SchemaVersion=1）。
    /// </summary>
    private sealed class SeasonCreatedPayload
    {
        /// <summary>赛季标识。</summary>
        public string SeasonId
        {
            get;
            set;
        }

        /// <summary>关联榜单标识。</summary>
        public string LeaderboardId
        {
            get;
            set;
        }

        /// <summary>排期开始时刻（UTC 毫秒）。</summary>
        public long StartTime
        {
            get;
            set;
        }

        /// <summary>排期结束时刻（UTC 毫秒）。</summary>
        public long EndTime
        {
            get;
            set;
        }

        /// <summary>奖励规则条数。</summary>
        public int RewardRuleCount
        {
            get;
            set;
        }

        /// <summary>创建后的状态名。</summary>
        public string State
        {
            get;
            set;
        }
    }

    /// <summary>
    /// 赛季开始事件负载（SchemaVersion=1）。
    /// </summary>
    private sealed class SeasonStartedPayload
    {
        /// <summary>赛季标识。</summary>
        public string SeasonId
        {
            get;
            set;
        }

        /// <summary>关联榜单标识。</summary>
        public string LeaderboardId
        {
            get;
            set;
        }

        /// <summary>排期开始时刻（UTC 毫秒）。</summary>
        public long StartTime
        {
            get;
            set;
        }

        /// <summary>排期结束时刻（UTC 毫秒）。</summary>
        public long EndTime
        {
            get;
            set;
        }
    }

    /// <summary>
    /// 赛季结束事件负载（SchemaVersion=1）。
    /// </summary>
    private sealed class SeasonEndedPayload
    {
        /// <summary>赛季标识。</summary>
        public string SeasonId
        {
            get;
            set;
        }

        /// <summary>关联榜单标识。</summary>
        public string LeaderboardId
        {
            get;
            set;
        }

        /// <summary>落档快照的条目数。</summary>
        public int SnapshotEntryCount
        {
            get;
            set;
        }

        /// <summary>快照生成时刻（UTC 毫秒）。</summary>
        public long SnapshottedTime
        {
            get;
            set;
        }
    }

    /// <summary>
    /// 赛季结算事件负载（SchemaVersion=1）。
    /// </summary>
    private sealed class SeasonSettledPayload
    {
        /// <summary>赛季标识。</summary>
        public string SeasonId
        {
            get;
            set;
        }

        /// <summary>本次首次发放成功玩家数。</summary>
        public int GrantedCount
        {
            get;
            set;
        }

        /// <summary>本次命中幂等回放玩家数。</summary>
        public int ReplayCount
        {
            get;
            set;
        }

        /// <summary>本次发放失败玩家数（正常路径恒为 0）。</summary>
        public int FailedCount
        {
            get;
            set;
        }
    }
}
