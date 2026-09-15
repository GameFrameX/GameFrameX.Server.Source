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
using System.Threading;
using System.Threading.Tasks;

namespace GameFrameX.Online.Leaderboard;

/// <summary>
/// 排行榜内存存储（单进程默认实现；生产持久化 / 多实例分片归 Server 仓运行时装配，X4）。
/// <para>
/// 维护约束（红线）：榜单定义与条目同锁（单锁粒度：本实现以整体锁保证「策略裁决 + 落档」原子性与
/// 查询快照一致性；热点写入的分片 / 批量优化属 vault L6 吞吐项，归运行时装配，不在此实现）。
/// 出入参防御性拷贝，存储内对象与外界无别名。
/// </para>
/// </summary>
public sealed class InMemoryOnlineLeaderboardStore : IOnlineLeaderboardStore
{
    /// <summary>全局读写锁（榜单与条目共用，保证快照一致性）。</summary>
    private readonly object _syncRoot = new object();

    /// <summary>榜单定义表：键 = (TenantId, AppId, LeaderboardId)。</summary>
    private readonly Dictionary<string, OnlineLeaderboard> _boards = new Dictionary<string, OnlineLeaderboard>();

    /// <summary>条目表：键 = 榜单键 → (PlayerId → 条目)。</summary>
    private readonly Dictionary<string, Dictionary<long, OnlineLeaderboardEntry>> _entries = new Dictionary<string, Dictionary<long, OnlineLeaderboardEntry>>();

    /// <summary>
    /// 初始化 <see cref="InMemoryOnlineLeaderboardStore"/>。
    /// </summary>
    public InMemoryOnlineLeaderboardStore()
    {
    }

    /// <inheritdoc />
    public Task<OnlineLeaderboard> CreateAsync(OnlineLeaderboard leaderboard, CancellationToken cancellationToken = default)
    {
        if (leaderboard == null)
        {
            throw new ArgumentNullException(nameof(leaderboard));
        }

        lock (_syncRoot)
        {
            var key = BuildKey(leaderboard.TenantId, leaderboard.AppId, leaderboard.LeaderboardId);
            if (_boards.ContainsKey(key))
            {
                return Task.FromResult<OnlineLeaderboard>(null);
            }

            _boards[key] = leaderboard.Copy();
            _entries[key] = new Dictionary<long, OnlineLeaderboardEntry>();
            return Task.FromResult(leaderboard.Copy());
        }
    }

    /// <inheritdoc />
    public Task<OnlineLeaderboard> FindAsync(long tenantId, long appId, string leaderboardId, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            return Task.FromResult(TryFindBoard(tenantId, appId, leaderboardId, out var board) ? board.Copy() : null);
        }
    }

    /// <inheritdoc />
    public Task<OnlineLeaderboardEntry> FindEntryAsync(long tenantId, long appId, string leaderboardId, long playerId, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            if (!TryFindBoard(tenantId, appId, leaderboardId, out _) || !_entries.TryGetValue(BuildKey(tenantId, appId, leaderboardId), out var boardEntries) || !boardEntries.TryGetValue(playerId, out var entry))
            {
                return Task.FromResult<OnlineLeaderboardEntry>(null);
            }

            return Task.FromResult(entry.Copy());
        }
    }

    /// <inheritdoc />
    public Task<OnlineLeaderboardApplyResult> ApplySubmissionAsync(OnlineLeaderboard leaderboard, OnlineLeaderboardScoreSubmission submission, CancellationToken cancellationToken = default)
    {
        if (leaderboard == null)
        {
            throw new ArgumentNullException(nameof(leaderboard));
        }

        if (submission == null)
        {
            throw new ArgumentNullException(nameof(submission));
        }

        lock (_syncRoot)
        {
            var key = BuildKey(leaderboard.TenantId, leaderboard.AppId, leaderboard.LeaderboardId);
            if (!_boards.ContainsKey(key) || !_entries.TryGetValue(key, out var boardEntries))
            {
                throw new InvalidOperationException("榜单不存在或已失效，写入前必须先经 FindAsync 解析");
            }

            if (!boardEntries.TryGetValue(submission.PlayerId, out var entry))
            {
                entry = new OnlineLeaderboardEntry { PlayerId = submission.PlayerId };
                boardEntries[submission.PlayerId] = entry;
            }

            var oldScore = entry.Score;
            var isNewEntry = entry.SubmissionCount == 0;
            entry.Score = ResolveScore(leaderboard, entry, submission.IncomingScore);
            entry.SourceKind = submission.SourceKind;
            entry.SourceMatchResultId = submission.SourceMatchResultId;
            entry.LastUpdateTime = submission.SubmittedTime;
            entry.SubmissionCount++;

            return Task.FromResult(new OnlineLeaderboardApplyResult
            {
                Entry = entry.Copy(),
                OldScore = oldScore,
                IsNewEntry = isNewEntry,
            });
        }
    }

    /// <inheritdoc />
    public Task<List<OnlineLeaderboardEntry>> ListOrderedEntriesAsync(OnlineLeaderboard leaderboard, CancellationToken cancellationToken = default)
    {
        if (leaderboard == null)
        {
            throw new ArgumentNullException(nameof(leaderboard));
        }

        List<OnlineLeaderboardEntry> copies;
        lock (_syncRoot)
        {
            var key = BuildKey(leaderboard.TenantId, leaderboard.AppId, leaderboard.LeaderboardId);
            if (!_entries.TryGetValue(key, out var boardEntries))
            {
                throw new InvalidOperationException("榜单不存在或已失效，查询前必须先经 FindAsync 解析");
            }

            copies = new List<OnlineLeaderboardEntry>(boardEntries.Count);
            foreach (var entry in boardEntries.Values)
            {
                copies.Add(entry.Copy());
            }
        }

        OnlineLeaderboardOrdering.Sort(leaderboard.SortOrder, copies);
        return Task.FromResult(copies);
    }

    /// <summary>
    /// 在锁内解析榜单定义（调用方已持锁）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="leaderboardId">榜单标识。</param>
    /// <param name="board">解析到的榜单定义。</param>
    /// <returns>存在返回 <c>true</c>。</returns>
    private bool TryFindBoard(long tenantId, long appId, string leaderboardId, out OnlineLeaderboard board)
    {
        board = null;
        if (string.IsNullOrEmpty(leaderboardId))
        {
            return false;
        }

        if (!_boards.TryGetValue(BuildKey(tenantId, appId, leaderboardId), out var stored))
        {
            return false;
        }

        board = stored;
        return true;
    }

    /// <summary>
    /// 在临界区内按累计策略裁决新分数（首笔直接生效；Best 以榜向判优，Sum 累加，Latest 覆盖）。
    /// </summary>
    /// <param name="leaderboard">榜单定义。</param>
    /// <param name="entry">临界区内当前条目。</param>
    /// <param name="incomingScore">本笔分数。</param>
    /// <returns>裁决后的分数。</returns>
    private static long ResolveScore(OnlineLeaderboard leaderboard, OnlineLeaderboardEntry entry, long incomingScore)
    {
        if (entry.SubmissionCount == 0)
        {
            return incomingScore;
        }

        switch (leaderboard.ScoreUpdatePolicy)
        {
            case OnlineLeaderboardScoreUpdatePolicy.Sum:
                return entry.Score + incomingScore;
            case OnlineLeaderboardScoreUpdatePolicy.Latest:
                return incomingScore;
            case OnlineLeaderboardScoreUpdatePolicy.Best:
            default:
                return OnlineLeaderboardOrdering.IsBetter(leaderboard.SortOrder, incomingScore, entry.Score) ? incomingScore : entry.Score;
        }
    }

    /// <summary>
    /// 构造存储键（作用域 + 榜单标识；跨作用域访问因键不同而自然隔离）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="leaderboardId">榜单标识。</param>
    /// <returns>存储键。</returns>
    private static string BuildKey(long tenantId, long appId, string leaderboardId)
    {
        return tenantId.ToString(CultureInfo.InvariantCulture) + ":" + appId.ToString(CultureInfo.InvariantCulture) + ":" + leaderboardId;
    }
}
