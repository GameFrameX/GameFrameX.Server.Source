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
using GameFrameX.Online.Contracts;
using GameFrameX.Online.Events;
using GameFrameX.Online.Scope;

namespace GameFrameX.Online.Leaderboard;

/// <summary>
/// 排行榜服务（vault:C8 S7.1：创建 / Top N / 玩家附近排名 / 游标分页 / 读缓存；查询侧作用域反预言）。
/// <para>
/// 维护约束（红线）：
/// (1) **本服务没有任何接受「裸分数」的公开提交 API**——分数写入只经内部
/// <see cref="ApplyTrustedScoreAsync"/>（由可信写入链路 <see cref="OnlineLeaderboardResultProjector"/> 调用），
/// 客户端无入口可刷分（VC-7.1）；
/// (2) 跨 App / 跨租户读写与「榜单不存在」同构返回 ResourceNotFound（反预言，不泄露榜单存在性，VC-7.14）；
/// (3) Top N 读缓存 TTL + 写失效——任何成功写榜立即失效该榜缓存，缓存只影响读延迟不影响正确性。
/// </para>
/// </summary>
public sealed class OnlineLeaderboardService
{
    /// <summary>排行榜存储。</summary>
    private readonly IOnlineLeaderboardStore _store;

    /// <summary>组件选项。</summary>
    private readonly OnlineLeaderboardOptions _options;

    /// <summary>事件发布器（可空）。</summary>
    private readonly IOnlineEventPublisher _eventPublisher;

    /// <summary>提交限流器（防刷高频半边）。</summary>
    private readonly OnlineLeaderboardRateLimiter _rateLimiter;

    /// <summary>读缓存与缓存锁。</summary>
    private readonly object _cacheSyncRoot = new object();

    /// <summary>Top N 快照读缓存：键 = 榜单存储键。</summary>
    private readonly Dictionary<string, CacheSlot> _snapshotCache = new Dictionary<string, CacheSlot>();

    /// <summary>
    /// 初始化 <see cref="OnlineLeaderboardService"/>。
    /// </summary>
    /// <param name="store">排行榜存储。</param>
    /// <param name="options">组件选项（空则取默认值）。</param>
    /// <param name="eventPublisher">事件发布器（可空；未接线时不发事件）。</param>
    public OnlineLeaderboardService(IOnlineLeaderboardStore store, OnlineLeaderboardOptions options = null, IOnlineEventPublisher eventPublisher = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _options = options ?? new OnlineLeaderboardOptions();
        _eventPublisher = eventPublisher;
        _rateLimiter = new OnlineLeaderboardRateLimiter(_options.RateLimitMaxSubmissions, _options.RateLimitWindowSeconds);
    }

    /// <summary>
    /// 创建榜单（同作用域同名榜单已存在时拒绝；创建成功发布 <c>Online.Leaderboard.Created</c> 事件）。
    /// </summary>
    /// <param name="scope">生效作用域（鉴权上下文产物；榜单归属取其 TenantId / AppId）。</param>
    /// <param name="request">创建请求。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒；传 0 取系统时钟，测试可注入）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>创建后的榜单定义；失败返回对应错误码。</returns>
    public async Task<OnlineResult<OnlineLeaderboard>> CreateAsync(OnlineScope scope, OnlineLeaderboardCreateRequest request, long nowUnixMilliseconds = 0, CancellationToken cancellationToken = default)
    {
        if (scope == null)
        {
            throw new ArgumentNullException(nameof(scope));
        }

        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrEmpty(request.LeaderboardId))
        {
            return OnlineResult<OnlineLeaderboard>.Fail(OnlineErrorCode.ParameterInvalid, "榜单标识不能为空");
        }

        if (request.SortOrder != OnlineLeaderboardSortOrder.Descending && request.SortOrder != OnlineLeaderboardSortOrder.Ascending)
        {
            return OnlineResult<OnlineLeaderboard>.Fail(OnlineErrorCode.ParameterInvalid, "排序方向取值非法");
        }

        if (request.ScoreUpdatePolicy != OnlineLeaderboardScoreUpdatePolicy.Best && request.ScoreUpdatePolicy != OnlineLeaderboardScoreUpdatePolicy.Sum && request.ScoreUpdatePolicy != OnlineLeaderboardScoreUpdatePolicy.Latest)
        {
            return OnlineResult<OnlineLeaderboard>.Fail(OnlineErrorCode.ParameterInvalid, "累计策略取值非法");
        }

        if (request.MaxScorePerSubmission < 0)
        {
            return OnlineResult<OnlineLeaderboard>.Fail(OnlineErrorCode.ParameterInvalid, "单次分数上限不得为负");
        }

        var board = new OnlineLeaderboard
        {
            LeaderboardId = request.LeaderboardId,
            TenantId = scope.TenantId,
            AppId = scope.AppId,
            SortOrder = request.SortOrder,
            ScoreUpdatePolicy = request.ScoreUpdatePolicy,
            MaxScorePerSubmission = request.MaxScorePerSubmission,
            CreatedTime = nowUnixMilliseconds > 0 ? nowUnixMilliseconds : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };

        var created = await _store.CreateAsync(board, cancellationToken).ConfigureAwait(false);
        if (created == null)
        {
            return OnlineResult<OnlineLeaderboard>.Fail(OnlineErrorCode.ParameterInvalid, "榜单标识已存在");
        }

        await PublishAsync(OnlineLeaderboardEvents.CreateLeaderboardCreated(created), cancellationToken).ConfigureAwait(false);
        return OnlineResult<OnlineLeaderboard>.Ok(created);
    }

    /// <summary>
    /// 查询 Top N（带 keyset 游标分页；游标由本服务编码，客户端不透明）。
    /// </summary>
    /// <param name="scope">生效作用域（跨 App / 跨租户与不存在同构拒绝）。</param>
    /// <param name="leaderboardId">榜单标识。</param>
    /// <param name="count">本页条目数（超出 <c>MaxPageSize</c> 按上限截断）。</param>
    /// <param name="cursor">上一页返回的游标（首页传 null 或空串）。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒；传 0 取系统时钟，缓存 TTL 判定用）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>本页条目与后继游标；失败返回对应错误码。</returns>
    public async Task<OnlineResult<OnlineLeaderboardPage>> GetTopAsync(OnlineScope scope, string leaderboardId, int count, string cursor = null, long nowUnixMilliseconds = 0, CancellationToken cancellationToken = default)
    {
        if (count <= 0)
        {
            return OnlineResult<OnlineLeaderboardPage>.Fail(OnlineErrorCode.ParameterInvalid, "条目数必须为正");
        }

        var boardResult = await ResolveBoardAsync(scope, leaderboardId, cancellationToken).ConfigureAwait(false);
        if (!boardResult.IsSuccess)
        {
            return OnlineResult<OnlineLeaderboardPage>.Fail(boardResult.Code, boardResult.Message);
        }

        var board = boardResult.Data;
        OnlineLeaderboardEntry cursorKey = null;
        if (!string.IsNullOrEmpty(cursor))
        {
            cursorKey = DecodeCursor(cursor);
            if (cursorKey == null)
            {
                return OnlineResult<OnlineLeaderboardPage>.Fail(OnlineErrorCode.ParameterInvalid, "分页游标非法");
            }
        }

        var snapshot = await GetSnapshotAsync(board, nowUnixMilliseconds, cancellationToken).ConfigureAwait(false);
        var pageSize = _options.MaxPageSize > 0 ? Math.Min(count, _options.MaxPageSize) : count;
        var startIndex = cursorKey == null ? 0 : FindIndexAfter(snapshot, board.SortOrder, cursorKey);
        var page = new OnlineLeaderboardPage
        {
            Entries = new List<OnlineLeaderboardEntryView>(),
            TotalCount = snapshot.Count,
        };

        var index = startIndex;
        while (index < snapshot.Count && page.Entries.Count < pageSize)
        {
            page.Entries.Add(new OnlineLeaderboardEntryView
            {
                Rank = index + 1,
                Entry = snapshot[index].Copy(),
            });
            index++;
        }

        var hasMore = index < snapshot.Count;
        page.Cursor = hasMore && page.Entries.Count > 0
            ? new OnlinePageCursor(EncodeCursor(page.Entries[page.Entries.Count - 1].Entry), true)
            : new OnlinePageCursor(string.Empty, false);
        return OnlineResult<OnlineLeaderboardPage>.Ok(page);
    }

    /// <summary>
    /// 查询玩家名次（未上榜返回 ResourceNotFound）。
    /// </summary>
    /// <param name="scope">生效作用域。</param>
    /// <param name="leaderboardId">榜单标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒；传 0 取系统时钟）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>条目与名次视图；失败返回对应错误码。</returns>
    public async Task<OnlineResult<OnlineLeaderboardEntryView>> GetPlayerRankAsync(OnlineScope scope, string leaderboardId, long playerId, long nowUnixMilliseconds = 0, CancellationToken cancellationToken = default)
    {
        var boardResult = await ResolveBoardAsync(scope, leaderboardId, cancellationToken).ConfigureAwait(false);
        if (!boardResult.IsSuccess)
        {
            return OnlineResult<OnlineLeaderboardEntryView>.Fail(boardResult.Code, boardResult.Message);
        }

        var snapshot = await GetSnapshotAsync(boardResult.Data, nowUnixMilliseconds, cancellationToken).ConfigureAwait(false);
        var index = snapshot.FindIndex(entry => entry.PlayerId == playerId);
        if (index < 0)
        {
            return OnlineResult<OnlineLeaderboardEntryView>.Fail(OnlineErrorCode.ResourceNotFound, "玩家未上榜");
        }

        return OnlineResult<OnlineLeaderboardEntryView>.Ok(new OnlineLeaderboardEntryView
        {
            Rank = index + 1,
            Entry = snapshot[index].Copy(),
        });
    }

    /// <summary>
    /// 查询玩家附近排名（窗口含玩家本人，边界处截断；VC-7.3）。
    /// </summary>
    /// <param name="scope">生效作用域。</param>
    /// <param name="leaderboardId">榜单标识。</param>
    /// <param name="playerId">玩家标识（未上榜返回 ResourceNotFound）。</param>
    /// <param name="before">前向窗口条数（默认 2）。</param>
    /// <param name="after">后向窗口条数（默认 2）。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒；传 0 取系统时钟）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>附近排名窗口；失败返回对应错误码。</returns>
    public async Task<OnlineResult<OnlineLeaderboardAroundView>> GetAroundPlayerAsync(OnlineScope scope, string leaderboardId, long playerId, int before = 2, int after = 2, long nowUnixMilliseconds = 0, CancellationToken cancellationToken = default)
    {
        if (before < 0 || after < 0)
        {
            return OnlineResult<OnlineLeaderboardAroundView>.Fail(OnlineErrorCode.ParameterInvalid, "附近排名窗口不得为负");
        }

        var boardResult = await ResolveBoardAsync(scope, leaderboardId, cancellationToken).ConfigureAwait(false);
        if (!boardResult.IsSuccess)
        {
            return OnlineResult<OnlineLeaderboardAroundView>.Fail(boardResult.Code, boardResult.Message);
        }

        var snapshot = await GetSnapshotAsync(boardResult.Data, nowUnixMilliseconds, cancellationToken).ConfigureAwait(false);
        var index = snapshot.FindIndex(entry => entry.PlayerId == playerId);
        if (index < 0)
        {
            return OnlineResult<OnlineLeaderboardAroundView>.Fail(OnlineErrorCode.ResourceNotFound, "玩家未上榜");
        }

        var view = new OnlineLeaderboardAroundView
        {
            PlayerId = playerId,
            Rank = index + 1,
            TotalCount = snapshot.Count,
            Around = new List<OnlineLeaderboardEntryView>(),
        };

        var startIndex = Math.Max(0, index - before);
        var endIndex = Math.Min(snapshot.Count - 1, index + after);
        for (var windowIndex = startIndex; windowIndex <= endIndex; windowIndex++)
        {
            view.Around.Add(new OnlineLeaderboardEntryView
            {
                Rank = windowIndex + 1,
                Entry = snapshot[windowIndex].Copy(),
            });
        }

        return OnlineResult<OnlineLeaderboardAroundView>.Ok(view);
    }

    /// <summary>
    /// 内部：按作用域解析榜单定义（跨作用域与不存在同构返回失败 ResourceNotFound）。
    /// </summary>
    /// <param name="scope">生效作用域。</param>
    /// <param name="leaderboardId">榜单标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>榜单定义；不存在返回失败。</returns>
    internal async Task<OnlineResult<OnlineLeaderboard>> ResolveBoardAsync(OnlineScope scope, string leaderboardId, CancellationToken cancellationToken = default)
    {
        if (scope == null)
        {
            throw new ArgumentNullException(nameof(scope));
        }

        if (string.IsNullOrEmpty(leaderboardId))
        {
            return OnlineResult<OnlineLeaderboard>.Fail(OnlineErrorCode.ParameterInvalid, "榜单标识不能为空");
        }

        var board = await _store.FindAsync(scope.TenantId, scope.AppId, leaderboardId, cancellationToken).ConfigureAwait(false);
        if (board == null)
        {
            return OnlineResult<OnlineLeaderboard>.Fail(OnlineErrorCode.ResourceNotFound, "排行榜不存在");
        }

        return OnlineResult<OnlineLeaderboard>.Ok(board);
    }

    /// <summary>
    /// 内部：可信分数写入（防刷上限 + 限流 + 原子落榜 + 缓存失效 + 事件；幂等由投影器承载，本方法不重复判定）。
    /// </summary>
    /// <param name="leaderboard">目标榜单（调用方已解析）。</param>
    /// <param name="submission">分数提交。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒；传 0 取系统时钟）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>写入回执（成功含条目与旧分数，失败含错误码）。</returns>
    internal async Task<OnlineLeaderboardWriteOutcome> ApplyTrustedScoreAsync(OnlineLeaderboard leaderboard, OnlineLeaderboardScoreSubmission submission, long nowUnixMilliseconds = 0, CancellationToken cancellationToken = default)
    {
        if (leaderboard == null)
        {
            throw new ArgumentNullException(nameof(leaderboard));
        }

        if (submission == null)
        {
            throw new ArgumentNullException(nameof(submission));
        }

        if (string.IsNullOrEmpty(submission.SourceMatchResultId))
        {
            return Failed(OnlineErrorCode.ParameterInvalid, "分数来源结算结果标识缺失");
        }

        var maxScore = leaderboard.MaxScorePerSubmission > 0 ? leaderboard.MaxScorePerSubmission : _options.DefaultMaxScorePerSubmission;
        if (maxScore > 0 && Math.Abs(submission.IncomingScore) > maxScore)
        {
            return Failed(OnlineErrorCode.RiskControlRejected, "分数超出合理范围，已风控拒绝");
        }

        var now = nowUnixMilliseconds > 0 ? nowUnixMilliseconds : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (!_rateLimiter.TryAcquire(leaderboard.TenantId, leaderboard.AppId, submission.PlayerId, now))
        {
            return Failed(OnlineErrorCode.RateLimitExceeded, "提交过于频繁，已限流");
        }

        var applied = await _store.ApplySubmissionAsync(leaderboard, submission, cancellationToken).ConfigureAwait(false);
        InvalidateCache(leaderboard);
        await PublishAsync(OnlineLeaderboardEvents.CreateScoreUpdated(leaderboard, submission.PlayerId, applied.OldScore, applied.Entry.Score, submission.SourceMatchResultId, applied.Entry.LastUpdateTime), cancellationToken).ConfigureAwait(false);

        return new OnlineLeaderboardWriteOutcome
        {
            IsSuccess = true,
            Entry = applied.Entry,
            OldScore = applied.OldScore,
        };
    }

    /// <summary>
    /// 内部：读取榜单全序条目（赛季结束取快照专用；**绕过读缓存直读存储**——快照必须是重置前的最新事实，
    /// 取到缓存旧值会漏掉刚刚落榜的成绩）。
    /// </summary>
    /// <param name="leaderboard">目标榜单（调用方已解析）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>全序条目副本。</returns>
    internal Task<List<OnlineLeaderboardEntry>> ListOrderedEntriesForSeasonAsync(OnlineLeaderboard leaderboard, CancellationToken cancellationToken = default)
    {
        if (leaderboard == null)
        {
            throw new ArgumentNullException(nameof(leaderboard));
        }

        return _store.ListOrderedEntriesAsync(leaderboard, cancellationToken);
    }

    /// <summary>
    /// 内部：赛季重置清空榜单条目（CAS；成功后**立即失效读缓存**——否则重置后 TTL 内仍会返回旧榜）。
    /// </summary>
    /// <param name="leaderboard">目标榜单（调用方已解析）。</param>
    /// <param name="expectedEntries">调用方持有的全序条目快照（CAS 判据）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>清空成功返回 <c>true</c>；榜上条目已变化返回 <c>false</c>。</returns>
    internal async Task<bool> TryResetForSeasonAsync(OnlineLeaderboard leaderboard, IReadOnlyList<OnlineLeaderboardEntry> expectedEntries, CancellationToken cancellationToken = default)
    {
        if (leaderboard == null)
        {
            throw new ArgumentNullException(nameof(leaderboard));
        }

        var reset = await _store.TryResetAsync(leaderboard, expectedEntries, cancellationToken).ConfigureAwait(false);
        if (reset)
        {
            InvalidateCache(leaderboard);
        }

        return reset;
    }

    /// <summary>
    /// 构造失败回执。
    /// </summary>
    /// <param name="code">错误码。</param>
    /// <param name="message">原因。</param>
    /// <returns>失败回执。</returns>
    private static OnlineLeaderboardWriteOutcome Failed(OnlineErrorCode code, string message)
    {
        return new OnlineLeaderboardWriteOutcome
        {
            IsSuccess = false,
            Code = code,
            Message = message,
        };
    }

    /// <summary>
    /// 读取榜单全序快照（缓存命中直接返回；未启用缓存或过期时从存储重建并回填缓存）。
    /// </summary>
    /// <param name="leaderboard">榜单定义。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒；缓存 TTL 判定）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>全序快照（服务私有，调用方不得改写）。</returns>
    private async Task<List<OnlineLeaderboardEntry>> GetSnapshotAsync(OnlineLeaderboard leaderboard, long nowUnixMilliseconds, CancellationToken cancellationToken)
    {
        var now = nowUnixMilliseconds > 0 ? nowUnixMilliseconds : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var cacheKey = leaderboard.TenantId.ToString(CultureInfo.InvariantCulture) + ":" + leaderboard.AppId.ToString(CultureInfo.InvariantCulture) + ":" + leaderboard.LeaderboardId;
        if (_options.CacheEnabled)
        {
            lock (_cacheSyncRoot)
            {
                if (_snapshotCache.TryGetValue(cacheKey, out var slot) && slot.ExpiresAtTime > now)
                {
                    return slot.Snapshot;
                }
            }
        }

        var snapshot = await _store.ListOrderedEntriesAsync(leaderboard, cancellationToken).ConfigureAwait(false);
        if (_options.CacheEnabled && _options.CacheTtlSeconds > 0)
        {
            lock (_cacheSyncRoot)
            {
                _snapshotCache[cacheKey] = new CacheSlot
                {
                    Snapshot = snapshot,
                    ExpiresAtTime = now + _options.CacheTtlSeconds * 1000L,
                };
            }
        }

        return snapshot;
    }

    /// <summary>
    /// 失效榜单读缓存（任何成功写榜后调用）。
    /// </summary>
    /// <param name="leaderboard">榜单定义。</param>
    private void InvalidateCache(OnlineLeaderboard leaderboard)
    {
        var cacheKey = leaderboard.TenantId.ToString(CultureInfo.InvariantCulture) + ":" + leaderboard.AppId.ToString(CultureInfo.InvariantCulture) + ":" + leaderboard.LeaderboardId;
        lock (_cacheSyncRoot)
        {
            _snapshotCache.Remove(cacheKey);
        }
    }

    /// <summary>
    /// 编码 keyset 游标（稳定排序键三元组；客户端视为不透明令牌）。
    /// </summary>
    /// <param name="entry">游标锚点条目。</param>
    /// <returns>游标字符串。</returns>
    private static string EncodeCursor(OnlineLeaderboardEntry entry)
    {
        return string.Concat(
            entry.Score.ToString(CultureInfo.InvariantCulture),
            ":",
            entry.LastUpdateTime.ToString(CultureInfo.InvariantCulture),
            ":",
            entry.PlayerId.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// 解码 keyset 游标为伪条目（仅稳定排序键三字段有效）。
    /// </summary>
    /// <param name="cursor">游标字符串。</param>
    /// <returns>伪条目；无法解析返回 null。</returns>
    private static OnlineLeaderboardEntry DecodeCursor(string cursor)
    {
        var parts = cursor.Split(':');
        if (parts.Length != 3)
        {
            return null;
        }

        if (!long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var score)
            || !long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var time)
            || !long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var playerId))
        {
            return null;
        }

        return new OnlineLeaderboardEntry { PlayerId = playerId, Score = score, LastUpdateTime = time };
    }

    /// <summary>
    /// 在全序快照中定位「严格晚于游标键」的首个下标（keyset 语义：翻页期间新增不重不漏）。
    /// </summary>
    /// <param name="snapshot">全序快照。</param>
    /// <param name="sortOrder">排序方向。</param>
    /// <param name="cursorKey">游标伪条目。</param>
    /// <returns>起始下标；游标键晚于全部条目时为快照长度。</returns>
    private static int FindIndexAfter(List<OnlineLeaderboardEntry> snapshot, OnlineLeaderboardSortOrder sortOrder, OnlineLeaderboardEntry cursorKey)
    {
        for (var index = 0; index < snapshot.Count; index++)
        {
            if (OnlineLeaderboardOrdering.Compare(sortOrder, snapshot[index], cursorKey) > 0)
            {
                return index;
            }
        }

        return snapshot.Count;
    }

    /// <summary>
    /// 发布事件（发布器可空；发布失败不改变已落定的写入事实）。
    /// </summary>
    /// <param name="onlineEvent">待发布事件。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>完成通知。</returns>
    private async Task PublishAsync(OnlineEvent onlineEvent, CancellationToken cancellationToken)
    {
        if (_eventPublisher == null)
        {
            return;
        }

        await _eventPublisher.PublishAsync(onlineEvent, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 读缓存槽（榜单全序快照 + 绝对过期时刻）。
    /// </summary>
    private sealed class CacheSlot
    {
        /// <summary>获取或设置全序条目快照。</summary>
        public List<OnlineLeaderboardEntry> Snapshot
        {
            get;
            set;
        }

        /// <summary>获取或设置过期时刻（UTC 毫秒）。</summary>
        public long ExpiresAtTime
        {
            get;
            set;
        }
    }
}
