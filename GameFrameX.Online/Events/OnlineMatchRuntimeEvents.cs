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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using GameFrameX.Online.Match;

namespace GameFrameX.Online.Events;

/// <summary>
/// 对局域事件工厂（vault:C6 S5.8「只有可信的服务端结果事件才能触发 Wallet、Reward、Leaderboard 和 Notification」）。
/// <para>
/// 维护约束（红线）：事件是**事实不是状态**——消费端不得回写对局；载荷只放标识与状态名，
/// 不放玩法私有状态（沿用 C93 脱敏要求，玩法状态由快照/增量按序号补发，不经事件总线）。
/// <see cref="MatchSettled"/> 是全链路唯一的可信结果来源：排行榜（阶段 7）与通知（阶段 6）
/// 只消费它，绝不消费客户端上报的任何结果（VC-5.2 / VC-5.9）。
/// </para>
/// </summary>
public static class OnlineMatchRuntimeEvents
{
    /// <summary>对局生命周期状态变更。</summary>
    public const string MatchStateChanged = "Online.Match.StateChanged";

    /// <summary>对局结算完成（可信结果事件）。</summary>
    public const string MatchSettled = "Online.Match.Settled";

    /// <summary>事件来源标识（与匹配域的 online-matchmaker 区分）。</summary>
    public const string Source = "online-match";

    /// <summary>
    /// 构造对局生命周期状态变更事件。
    /// </summary>
    /// <param name="match">变更后的对局快照。</param>
    /// <param name="fromState">原状态。</param>
    /// <param name="toState">目标状态。</param>
    /// <returns>事件信封实例。</returns>
    public static OnlineEvent CreateMatchStateChanged(OnlineMatch match, OnlineMatchState fromState, OnlineMatchState toState)
    {
        var memberCount = match.Members == null ? 0 : match.Members.Count;
        var payload = new MatchStateChangedPayload
        {
            MatchId = match.MatchId ?? string.Empty,
            AssignmentId = match.AssignmentId ?? string.Empty,
            FromState = fromState.ToString(),
            ToState = toState.ToString(),
            ServerSequence = match.ServerSequence,
            MemberCount = memberCount,
        };
        var auditFields = new Dictionary<string, string>
        {
            { "MatchId", match.MatchId ?? string.Empty },
            { "AssignmentId", match.AssignmentId ?? string.Empty },
            { "FromState", fromState.ToString() },
            { "ToState", toState.ToString() },
            { "ServerSequence", match.ServerSequence.ToString(CultureInfo.InvariantCulture) },
            { "MemberCount", memberCount.ToString(CultureInfo.InvariantCulture) },
        };
        return new OnlineEvent
        {
            EventId = "evt-" + Guid.NewGuid().ToString("N"),
            EventType = MatchStateChanged,
            OccurredTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SchemaVersion = 1,
            TenantId = match.TenantId,
            AppId = match.AppId,
            ServerId = match.ServerId,
            PlayerId = 0,
            Source = Source,
            CorrelationId = string.Empty,
            Payload = JsonSerializer.SerializeToUtf8Bytes(payload),
            PayloadAuditFields = auditFields,
        };
    }

    /// <summary>
    /// 构造对局结算完成事件（可信结果事件）。
    /// </summary>
    /// <param name="result">已落定的结算结果。</param>
    /// <param name="correlationId">关联标识（可空）。</param>
    /// <returns>事件信封实例。</returns>
    public static OnlineEvent CreateMatchSettled(OnlineMatchResult result, string correlationId = null)
    {
        var entries = new List<MatchSettledEntryPayload>();
        var winnerCount = 0;
        if (result.Entries != null)
        {
            foreach (var entry in result.Entries)
            {
                if (entry == null)
                {
                    continue;
                }

                if (entry.IsWinner)
                {
                    winnerCount++;
                }

                entries.Add(new MatchSettledEntryPayload
                {
                    PlayerId = entry.PlayerId,
                    Rank = entry.Rank,
                    IsWinner = entry.IsWinner,
                    Score = entry.Score,
                    RewardCount = entry.Rewards == null ? 0 : entry.Rewards.Count,
                });
            }
        }

        var payload = new MatchSettledPayload
        {
            MatchResultId = result.MatchResultId ?? string.Empty,
            MatchId = result.MatchId ?? string.Empty,
            Mode = result.Mode,
            Region = result.Region,
            Outcome = result.Outcome.ToString(),
            SettledTime = result.SettledTime,
            Entries = entries,
        };
        var auditFields = new Dictionary<string, string>
        {
            { "MatchResultId", result.MatchResultId ?? string.Empty },
            { "MatchId", result.MatchId ?? string.Empty },
            { "Mode", result.Mode.ToString(CultureInfo.InvariantCulture) },
            { "Region", result.Region.ToString(CultureInfo.InvariantCulture) },
            { "Outcome", result.Outcome.ToString() },
            { "EntryCount", entries.Count.ToString(CultureInfo.InvariantCulture) },
            { "WinnerCount", winnerCount.ToString(CultureInfo.InvariantCulture) },
        };
        return new OnlineEvent
        {
            EventId = "evt-" + Guid.NewGuid().ToString("N"),
            EventType = MatchSettled,
            OccurredTime = result.SettledTime > 0 ? result.SettledTime : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SchemaVersion = 1,
            TenantId = result.TenantId,
            AppId = result.AppId,
            ServerId = result.ServerId,
            PlayerId = 0,
            Source = Source,
            CorrelationId = correlationId ?? string.Empty,
            Payload = JsonSerializer.SerializeToUtf8Bytes(payload),
            PayloadAuditFields = auditFields,
        };
    }

    /// <summary>
    /// 对局状态变更事件负载（SchemaVersion=1）。
    /// </summary>
    private sealed class MatchStateChangedPayload
    {
        /// <summary>对局标识。</summary>
        public string MatchId
        {
            get;
            set;
        }

        /// <summary>来源分配标识。</summary>
        public string AssignmentId
        {
            get;
            set;
        }

        /// <summary>原状态名。</summary>
        public string FromState
        {
            get;
            set;
        }

        /// <summary>目标状态名。</summary>
        public string ToState
        {
            get;
            set;
        }

        /// <summary>变更时的服务器序号。</summary>
        public long ServerSequence
        {
            get;
            set;
        }

        /// <summary>成员数（含断线成员）。</summary>
        public int MemberCount
        {
            get;
            set;
        }
    }

    /// <summary>
    /// 对局结算完成事件负载（SchemaVersion=1）。
    /// </summary>
    private sealed class MatchSettledPayload
    {
        /// <summary>结果标识（幂等边界）。</summary>
        public string MatchResultId
        {
            get;
            set;
        }

        /// <summary>对局标识。</summary>
        public string MatchId
        {
            get;
            set;
        }

        /// <summary>玩法模式。</summary>
        public int Mode
        {
            get;
            set;
        }

        /// <summary>区域。</summary>
        public int Region
        {
            get;
            set;
        }

        /// <summary>结束方式名。</summary>
        public string Outcome
        {
            get;
            set;
        }

        /// <summary>结算时刻（UTC 毫秒）。</summary>
        public long SettledTime
        {
            get;
            set;
        }

        /// <summary>玩家条目集合。</summary>
        public List<MatchSettledEntryPayload> Entries
        {
            get;
            set;
        }
    }

    /// <summary>
    /// 结算结果单玩家条目负载（SchemaVersion=1）。
    /// </summary>
    private sealed class MatchSettledEntryPayload
    {
        /// <summary>玩家标识。</summary>
        public long PlayerId
        {
            get;
            set;
        }

        /// <summary>名次。</summary>
        public int Rank
        {
            get;
            set;
        }

        /// <summary>是否胜方。</summary>
        public bool IsWinner
        {
            get;
            set;
        }

        /// <summary>玩法得分。</summary>
        public long Score
        {
            get;
            set;
        }

        /// <summary>奖励条目数（奖励明细经资产域事件下发，此处只给计数）。</summary>
        public int RewardCount
        {
            get;
            set;
        }
    }
}
