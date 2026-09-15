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
using GameFrameX.Online.Leaderboard;

namespace GameFrameX.Online.Events;

/// <summary>
/// 排行域事件工厂（vault:C8 S7.1 / S7.2；C93 信封事件，<c>Source = "online-leaderboard"</c>）。
/// <para>
/// 维护约束（红线）：事件是**事实不是状态**——<see cref="ScoreUpdated"/> 只陈述「某玩家分数已由某结算结果
/// 变更为某值」，榜上名次不进事件（名次是查询时事实，快照口径随读变化）；载荷与审计字段都不含
/// 玩家私有数据。分数变更事件只可能由可信写入链路产生（分数写入唯一入口即投影器，VC-7.1 的事件半边）。
/// </para>
/// </summary>
public static class OnlineLeaderboardEvents
{
    /// <summary>榜单创建。</summary>
    public const string LeaderboardCreated = "Online.Leaderboard.Created";

    /// <summary>榜上分数更新（可信写入）。</summary>
    public const string ScoreUpdated = "Online.Leaderboard.ScoreUpdated";

    /// <summary>事件来源标识。</summary>
    public const string Source = "online-leaderboard";

    /// <summary>
    /// 构造榜单创建事件。
    /// </summary>
    /// <param name="leaderboard">创建后的榜单定义。</param>
    /// <returns>事件信封实例。</returns>
    public static OnlineEvent CreateLeaderboardCreated(OnlineLeaderboard leaderboard)
    {
        var payload = new LeaderboardCreatedPayload
        {
            LeaderboardId = leaderboard.LeaderboardId ?? string.Empty,
            SortOrder = leaderboard.SortOrder.ToString(),
            ScoreUpdatePolicy = leaderboard.ScoreUpdatePolicy.ToString(),
            CreatedTime = leaderboard.CreatedTime,
        };
        var auditFields = new Dictionary<string, string>
        {
            { "LeaderboardId", leaderboard.LeaderboardId ?? string.Empty },
            { "SortOrder", leaderboard.SortOrder.ToString() },
            { "ScoreUpdatePolicy", leaderboard.ScoreUpdatePolicy.ToString() },
            { "CreatedTime", leaderboard.CreatedTime.ToString(CultureInfo.InvariantCulture) },
        };
        return new OnlineEvent
        {
            EventId = "evt-" + Guid.NewGuid().ToString("N"),
            EventType = LeaderboardCreated,
            OccurredTime = leaderboard.CreatedTime > 0 ? leaderboard.CreatedTime : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SchemaVersion = 1,
            TenantId = leaderboard.TenantId,
            AppId = leaderboard.AppId,
            ServerId = 0,
            PlayerId = 0,
            Source = Source,
            CorrelationId = string.Empty,
            Payload = JsonSerializer.SerializeToUtf8Bytes(payload),
            PayloadAuditFields = auditFields,
        };
    }

    /// <summary>
    /// 构造榜上分数更新事件。
    /// </summary>
    /// <param name="leaderboard">目标榜单定义。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="oldScore">变更前分数。</param>
    /// <param name="newScore">变更后分数。</param>
    /// <param name="sourceMatchResultId">来源结算结果标识（追溯键，兼作 CorrelationId）。</param>
    /// <param name="updatedTime">变更时刻（UTC 毫秒）。</param>
    /// <returns>事件信封实例。</returns>
    public static OnlineEvent CreateScoreUpdated(OnlineLeaderboard leaderboard, long playerId, long oldScore, long newScore, string sourceMatchResultId, long updatedTime)
    {
        var payload = new ScoreUpdatedPayload
        {
            LeaderboardId = leaderboard.LeaderboardId ?? string.Empty,
            PlayerId = playerId,
            OldScore = oldScore,
            NewScore = newScore,
            SourceKind = OnlineLeaderboardScoreSource.MatchResult.ToString(),
            SourceMatchResultId = sourceMatchResultId ?? string.Empty,
            UpdatedTime = updatedTime,
        };
        var auditFields = new Dictionary<string, string>
        {
            { "LeaderboardId", leaderboard.LeaderboardId ?? string.Empty },
            { "PlayerId", playerId.ToString(CultureInfo.InvariantCulture) },
            { "OldScore", oldScore.ToString(CultureInfo.InvariantCulture) },
            { "NewScore", newScore.ToString(CultureInfo.InvariantCulture) },
            { "SourceKind", OnlineLeaderboardScoreSource.MatchResult.ToString() },
            { "SourceMatchResultId", sourceMatchResultId ?? string.Empty },
            { "UpdatedTime", updatedTime.ToString(CultureInfo.InvariantCulture) },
        };
        return new OnlineEvent
        {
            EventId = "evt-" + Guid.NewGuid().ToString("N"),
            EventType = ScoreUpdated,
            OccurredTime = updatedTime > 0 ? updatedTime : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            SchemaVersion = 1,
            TenantId = leaderboard.TenantId,
            AppId = leaderboard.AppId,
            ServerId = 0,
            PlayerId = playerId,
            Source = Source,
            CorrelationId = sourceMatchResultId ?? string.Empty,
            Payload = JsonSerializer.SerializeToUtf8Bytes(payload),
            PayloadAuditFields = auditFields,
        };
    }

    /// <summary>
    /// 榜单创建事件负载（SchemaVersion=1）。
    /// </summary>
    private sealed class LeaderboardCreatedPayload
    {
        /// <summary>榜单标识。</summary>
        public string LeaderboardId
        {
            get;
            set;
        }

        /// <summary>排序方向名。</summary>
        public string SortOrder
        {
            get;
            set;
        }

        /// <summary>累计策略名。</summary>
        public string ScoreUpdatePolicy
        {
            get;
            set;
        }

        /// <summary>创建时刻（UTC 毫秒）。</summary>
        public long CreatedTime
        {
            get;
            set;
        }
    }

    /// <summary>
    /// 榜上分数更新事件负载（SchemaVersion=1）。
    /// </summary>
    private sealed class ScoreUpdatedPayload
    {
        /// <summary>榜单标识。</summary>
        public string LeaderboardId
        {
            get;
            set;
        }

        /// <summary>玩家标识。</summary>
        public long PlayerId
        {
            get;
            set;
        }

        /// <summary>变更前分数。</summary>
        public long OldScore
        {
            get;
            set;
        }

        /// <summary>变更后分数。</summary>
        public long NewScore
        {
            get;
            set;
        }

        /// <summary>分数来源名。</summary>
        public string SourceKind
        {
            get;
            set;
        }

        /// <summary>来源结算结果标识。</summary>
        public string SourceMatchResultId
        {
            get;
            set;
        }

        /// <summary>变更时刻（UTC 毫秒）。</summary>
        public long UpdatedTime
        {
            get;
            set;
        }
    }
}
