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

    /// <summary>
    /// 在全局锁内创建榜单：同作用域同标识已存在时拒绝返回 null，否则落档定义副本并初始化空条目表。
    /// </summary>
    /// <remarks>
    /// Creates the leaderboard under the global lock: refuses with null when the same id already exists in the scope, otherwise stores a defensive copy of the definition and initializes an empty entry table.
    /// </remarks>
    /// <param name="leaderboard">榜单定义（作用域取自其 TenantId 与 AppId）/ The leaderboard definition (scope taken from its TenantId and AppId)</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>创建后的榜单副本；已存在返回 null / A copy of the created leaderboard; null when it already exists</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="leaderboard"/> 为 null 时抛出 / Thrown when <paramref name="leaderboard"/> is null</exception>
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

    /// <summary>
    /// 在全局锁内按作用域与榜单标识查找榜单定义，返回防御性副本。
    /// </summary>
    /// <remarks>
    /// Finds the leaderboard definition by scope and leaderboard id under the global lock, returning a defensive copy.
    /// </remarks>
    /// <param name="tenantId">租户标识 / The tenant id</param>
    /// <param name="appId">App 标识 / The app id</param>
    /// <param name="leaderboardId">榜单标识 / The leaderboard id</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>榜单副本；不存在或跨作用域返回 null / A copy of the leaderboard; null when not found or cross-scope</returns>
    public Task<OnlineLeaderboard> FindAsync(long tenantId, long appId, string leaderboardId, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            return Task.FromResult(TryFindBoard(tenantId, appId, leaderboardId, out var board) ? board.Copy() : null);
        }
    }

    /// <summary>
    /// 在全局锁内查找指定玩家的榜上条目，返回防御性副本。
    /// </summary>
    /// <remarks>
    /// Finds a single player's entry on the board under the global lock, returning a defensive copy.
    /// </remarks>
    /// <param name="tenantId">租户标识 / The tenant id</param>
    /// <param name="appId">App 标识 / The app id</param>
    /// <param name="leaderboardId">榜单标识 / The leaderboard id</param>
    /// <param name="playerId">玩家标识 / The player id</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>条目副本；未上榜或榜单不存在返回 null / A copy of the entry; null when the player is unranked or the board does not exist</returns>
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

    /// <summary>
    /// 在全局锁内的同一临界区按累计策略裁决分数并落档条目，不存在则新建条目。
    /// </summary>
    /// <remarks>
    /// Resolves the score by the accumulation policy and archives the entry within the same critical section under the global lock, creating the entry when absent.
    /// </remarks>
    /// <param name="leaderboard">目标榜单定义（调用方先经 <see cref="FindAsync"/> 取得）/ The target leaderboard definition (obtained by the caller via <see cref="FindAsync"/> first)</param>
    /// <param name="submission">分数提交 / The score submission</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>写入结果（聚合后条目副本与临界区内事实的前值）/ The apply result (the merged entry copy and the pre-update facts observed inside the critical section)</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="leaderboard"/> 或 <paramref name="submission"/> 为 null 时抛出 / Thrown when <paramref name="leaderboard"/> or <paramref name="submission"/> is null</exception>
    /// <exception cref="InvalidOperationException">当目标榜单未创建或已失效（未经 <see cref="FindAsync"/> 解析）时抛出 / Thrown when the target leaderboard is not created or has expired (not resolved via <see cref="FindAsync"/>)</exception>
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

    /// <summary>
    /// 在全局锁内拷贝全榜条目，再按榜单排序规则排出全序返回。
    /// </summary>
    /// <remarks>
    /// Copies every entry of the board under the global lock, then sorts the copies into the full order defined by the board's sort rule.
    /// </remarks>
    /// <param name="leaderboard">目标榜单定义 / The target leaderboard definition</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>全序条目副本；空榜返回空列表 / The ordered entry copies; an empty list for an empty board</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="leaderboard"/> 为 null 时抛出 / Thrown when <paramref name="leaderboard"/> is null</exception>
    /// <exception cref="InvalidOperationException">当目标榜单未创建或已失效（未经 <see cref="FindAsync"/> 解析）时抛出 / Thrown when the target leaderboard is not created or has expired (not resolved via <see cref="FindAsync"/>)</exception>
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
    /// 在全局锁内将榜上条目与期望快照做 CAS 比对，完全一致才清空整榜，否则拒绝且不改变状态。
    /// </summary>
    /// <remarks>
    /// Compares the board entries against the expected snapshot as a CAS check under the global lock, clearing the whole board only on an exact match and otherwise refusing without any state change.
    /// </remarks>
    /// <param name="leaderboard">目标榜单定义 / The target leaderboard definition</param>
    /// <param name="expectedEntries">调用方持有的全序条目快照 / The ordered entry snapshot held by the caller</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>清空成功返回 true；榜上条目已变化而拒绝清空返回 false / True when the board is cleared; false when the entries changed and the reset is refused</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="leaderboard"/> 或 <paramref name="expectedEntries"/> 为 null 时抛出 / Thrown when <paramref name="leaderboard"/> or <paramref name="expectedEntries"/> is null</exception>
    /// <exception cref="InvalidOperationException">当目标榜单未创建或已失效（未经 <see cref="FindAsync"/> 解析）时抛出 / Thrown when the target leaderboard is not created or has expired (not resolved via <see cref="FindAsync"/>)</exception>
    public Task<bool> TryResetAsync(OnlineLeaderboard leaderboard, IReadOnlyList<OnlineLeaderboardEntry> expectedEntries, CancellationToken cancellationToken = default)
    {
        if (leaderboard == null)
        {
            throw new ArgumentNullException(nameof(leaderboard));
        }

        if (expectedEntries == null)
        {
            throw new ArgumentNullException(nameof(expectedEntries));
        }

        lock (_syncRoot)
        {
            var key = BuildKey(leaderboard.TenantId, leaderboard.AppId, leaderboard.LeaderboardId);
            if (!_entries.TryGetValue(key, out var boardEntries))
            {
                throw new InvalidOperationException("榜单不存在或已失效，重置前必须先经 FindAsync 解析");
            }

            if (!MatchesExpectedOrdered(leaderboard, boardEntries, expectedEntries))
            {
                return Task.FromResult(false);
            }

            boardEntries.Clear();
            return Task.FromResult(true);
        }
    }

    /// <summary>
    /// 在临界区内比对榜上条目与调用方持有的快照是否逐项一致（CAS 判据）。
    /// </summary>
    /// <param name="leaderboard">榜单定义（取排序方向重建全序）。</param>
    /// <param name="boardEntries">临界区内的条目表。</param>
    /// <param name="expectedEntries">调用方持有的全序条目快照。</param>
    /// <returns>完全一致返回 <c>true</c>。</returns>
    private static bool MatchesExpectedOrdered(OnlineLeaderboard leaderboard, Dictionary<long, OnlineLeaderboardEntry> boardEntries, IReadOnlyList<OnlineLeaderboardEntry> expectedEntries)
    {
        if (boardEntries.Count != expectedEntries.Count)
        {
            return false;
        }

        var current = new List<OnlineLeaderboardEntry>(boardEntries.Values);
        OnlineLeaderboardOrdering.Sort(leaderboard.SortOrder, current);
        for (var index = 0; index < current.Count; index++)
        {
            if (!MatchesExpectedEntry(current[index], expectedEntries[index]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 比对单个条目是否与快照一致（玩家、分数、更新时间、提交计数四要素全等）。
    /// </summary>
    /// <param name="current">临界区内的条目。</param>
    /// <param name="expected">快照中的条目。</param>
    /// <returns>一致返回 <c>true</c>。</returns>
    private static bool MatchesExpectedEntry(OnlineLeaderboardEntry current, OnlineLeaderboardEntry expected)
    {
        return expected != null
            && current.PlayerId == expected.PlayerId
            && current.Score == expected.Score
            && current.LastUpdateTime == expected.LastUpdateTime
            && current.SubmissionCount == expected.SubmissionCount;
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
