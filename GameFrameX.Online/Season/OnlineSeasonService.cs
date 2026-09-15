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
using GameFrameX.Online.Assets;
using GameFrameX.Online.Contracts;
using GameFrameX.Online.Events;
using GameFrameX.Online.Leaderboard;
using GameFrameX.Online.Scope;

namespace GameFrameX.Online.Season;

/// <summary>
/// 赛季生命周期服务（vault:C8 S7.3：开始/结束时间、分数重置、历史快照、赛季奖励、结算幂等与失败重试）。
/// <para>
/// 维护约束（红线）：
/// (1) **快照先行**——结束赛季时先持久化历史快照、再清空榜单，且清空是 CAS 语义（榜上条目与快照逐项一致
/// 才清）；期间有写入落地则重读重拍快照后重试，重试耗尽返回可重试错误且赛季保持进行中。**赛季结束不能直接
/// 删除历史数据**，也绝不静默丢弃一条已结算的真实成绩（VC-7.5）；
/// (2) **结算只读快照**——结算依据是冻结快照而非实时榜单（重置后榜单已空）；快照缺失一律拒绝结算，
/// 不回落、不降级（VC-7.6）；
/// (3) **结算幂等**——逐玩家经 C95 <see cref="OnlineGrantService"/> 发放，幂等键与业务单号确定性派生
/// <c>season-{SeasonId}-{PlayerId}</c>，重复触发命中回放返回首次结果，不重复发奖。「永不重复发奖」是
/// 幂等键稳定性带来的策略无关硬保证；「重试补齐**此前发放失败**的玩家」则依赖宿主把 Foundation 的失败
/// 重放策略装配为 <c>FailedReplayPolicy.ReExecute</c>——默认 <c>ReplayError</c> 会把失败原样回放，失败玩家
/// 需待保留期过期后重新占位方可补发（本服务不自行改写全局幂等策略，见方案复审 R6）；
/// (4) **逐玩家失败隔离**——单玩家发放失败只计入回执明细，不阻塞其余玩家；仅当无失败玩家才把赛季推进到
/// 结算完成态（部分发奖不得标记完成）；
/// (5) 跨 App / 跨租户读写与「赛季不存在」同构返回 ResourceNotFound（反预言，对齐 C94/C99/C102 先例）。
/// </para>
/// <para>
/// 运行时边界（X4）：<c>StartTime</c>/<c>EndTime</c> 只是排期元数据，到点驱动 <see cref="StartAsync"/> /
/// <see cref="EndAsync"/> 的调度器、以及持久化与跨实例协调，均归 Server 仓运行时装配。
/// </para>
/// </summary>
public sealed class OnlineSeasonService
{
    /// <summary>
    /// 重置流程的最大尝试次数（快照与清空之间持续有写入时重读重拍；超出即让调用方稍后重试，避免活锁）。
    /// </summary>
    private const int MaxResetAttempts = 3;

    /// <summary>
    /// 赛季奖励发放使用的区服位（恒为 0）：赛季与榜单同为 (TenantId, AppId) 作用域，**没有归属服**。
    /// 若改取调用方作用域的区服，同一赛季从不同区服触发结算会落到不同的幂等绑定键上，重试将不再回放首次结果
    /// 而重复发奖——故必须用与调用方无关的稳定值。
    /// </summary>
    private const long SeasonScopeServerId = 0;

    /// <summary>赛季存储。</summary>
    private readonly IOnlineSeasonStore _seasonStore;

    /// <summary>排行榜服务（榜单解析、赛季取快照读、重置清空 + 读缓存失效）。</summary>
    private readonly OnlineLeaderboardService _leaderboardService;

    /// <summary>统一资产入口（赛季奖励唯一发放通道，X5）。</summary>
    private readonly OnlineGrantService _grantService;

    /// <summary>事件发布器（可空；未接线时不发事件）。</summary>
    private readonly IOnlineEventPublisher _eventPublisher;

    /// <summary>
    /// 初始化 <see cref="OnlineSeasonService"/>。
    /// </summary>
    /// <param name="seasonStore">赛季存储。</param>
    /// <param name="leaderboardService">排行榜服务。</param>
    /// <param name="grantService">统一资产入口（赛季奖励发放）。</param>
    /// <param name="eventPublisher">事件发布器（可空；未接线时不发事件）。</param>
    public OnlineSeasonService(IOnlineSeasonStore seasonStore, OnlineLeaderboardService leaderboardService, OnlineGrantService grantService, IOnlineEventPublisher eventPublisher = null)
    {
        _seasonStore = seasonStore ?? throw new ArgumentNullException(nameof(seasonStore));
        _leaderboardService = leaderboardService ?? throw new ArgumentNullException(nameof(leaderboardService));
        _grantService = grantService ?? throw new ArgumentNullException(nameof(grantService));
        _eventPublisher = eventPublisher;
    }

    /// <summary>
    /// 创建赛季（同作用域同名赛季已存在时拒绝；关联榜单必须已存在；奖励规则区间必须互不重叠）。
    /// </summary>
    /// <param name="scope">生效作用域（赛季归属取其 TenantId / AppId）。</param>
    /// <param name="request">创建请求。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒；传 0 取系统时钟，测试可注入）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>创建后的赛季定义（初始状态为 <see cref="OnlineSeasonState.Scheduled"/>）；失败返回对应错误码。</returns>
    public async Task<OnlineResult<OnlineSeason>> CreateAsync(OnlineScope scope, OnlineSeasonCreateRequest request, long nowUnixMilliseconds = 0, CancellationToken cancellationToken = default)
    {
        if (scope == null)
        {
            throw new ArgumentNullException(nameof(scope));
        }

        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrEmpty(request.SeasonId))
        {
            return OnlineResult<OnlineSeason>.Fail(OnlineErrorCode.ParameterInvalid, "赛季标识不能为空");
        }

        if (string.IsNullOrEmpty(request.LeaderboardId))
        {
            return OnlineResult<OnlineSeason>.Fail(OnlineErrorCode.ParameterInvalid, "关联榜单标识不能为空");
        }

        if (request.EndTime <= request.StartTime)
        {
            return OnlineResult<OnlineSeason>.Fail(OnlineErrorCode.ParameterInvalid, "赛季结束时刻必须晚于开始时刻");
        }

        if (!TryValidateRewardRules(request.RewardRules, out var ruleMessage))
        {
            return OnlineResult<OnlineSeason>.Fail(OnlineErrorCode.ParameterInvalid, ruleMessage);
        }

        var boardResult = await _leaderboardService.ResolveBoardAsync(scope, request.LeaderboardId, cancellationToken).ConfigureAwait(false);
        if (!boardResult.IsSuccess)
        {
            return OnlineResult<OnlineSeason>.Fail(boardResult.Code, boardResult.Message);
        }

        var season = new OnlineSeason
        {
            SeasonId = request.SeasonId,
            TenantId = scope.TenantId,
            AppId = scope.AppId,
            LeaderboardId = request.LeaderboardId,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            RewardRules = CopyRules(request.RewardRules),
            State = OnlineSeasonState.Scheduled,
            CreatedTime = nowUnixMilliseconds > 0 ? nowUnixMilliseconds : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };

        var created = await _seasonStore.CreateAsync(season, cancellationToken).ConfigureAwait(false);
        if (created == null)
        {
            return OnlineResult<OnlineSeason>.Fail(OnlineErrorCode.ParameterInvalid, "赛季标识已存在");
        }

        await PublishAsync(OnlineSeasonEvents.CreateSeasonCreated(created), cancellationToken).ConfigureAwait(false);
        return OnlineResult<OnlineSeason>.Ok(created);
    }

    /// <summary>
    /// 开始赛季（<see cref="OnlineSeasonState.Scheduled"/> → <see cref="OnlineSeasonState.Active"/>）。
    /// </summary>
    /// <param name="scope">生效作用域（跨作用域与不存在同构拒绝）。</param>
    /// <param name="seasonId">赛季标识。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒；传 0 取系统时钟，测试可注入）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>开始后的赛季定义；失败返回对应错误码。</returns>
    public async Task<OnlineResult<OnlineSeason>> StartAsync(OnlineScope scope, string seasonId, long nowUnixMilliseconds = 0, CancellationToken cancellationToken = default)
    {
        var seasonResult = await ResolveSeasonAsync(scope, seasonId, cancellationToken).ConfigureAwait(false);
        if (!seasonResult.IsSuccess)
        {
            return seasonResult;
        }

        var season = seasonResult.Data;
        if (!OnlineSeasonStateMachine.TryTransition(season.State, OnlineSeasonState.Active))
        {
            return OnlineResult<OnlineSeason>.Fail(OnlineErrorCode.StateOperationForbidden, "赛季当前状态不允许开始：" + season.State);
        }

        season.State = OnlineSeasonState.Active;
        await _seasonStore.SaveAsync(season, cancellationToken).ConfigureAwait(false);
        await PublishAsync(OnlineSeasonEvents.CreateSeasonStarted(season), cancellationToken).ConfigureAwait(false);
        return OnlineResult<OnlineSeason>.Ok(season);
    }

    /// <summary>
    /// 结束赛季并重置榜单（vault:C8 S7.3 核心：先落历史快照，再清空榜单）。
    /// <para>
    /// 快照与清空之间若榜单有新写入落地，清空会被 CAS 拒绝并重读重拍快照后重试，保证快照覆盖的条目集合
    /// 与被清空的条目集合完全一致——既不丢历史，也不丢分。重试耗尽返回 <c>ServiceBusy</c>，榜单与赛季状态均
    /// 未被改变（期间落档的中间快照不匹配任何已清空的榜单，会被下次成功重试整体覆盖），由调用方稍后重试。
    /// </para>
    /// </summary>
    /// <param name="scope">生效作用域（跨作用域与不存在同构拒绝）。</param>
    /// <param name="seasonId">赛季标识。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒；传 0 取系统时钟，测试可注入）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>落档的历史快照（即 VC-7.5 的「快照导出」证据）；失败返回对应错误码。</returns>
    public async Task<OnlineResult<OnlineSeasonSnapshot>> EndAsync(OnlineScope scope, string seasonId, long nowUnixMilliseconds = 0, CancellationToken cancellationToken = default)
    {
        var seasonResult = await ResolveSeasonAsync(scope, seasonId, cancellationToken).ConfigureAwait(false);
        if (!seasonResult.IsSuccess)
        {
            return OnlineResult<OnlineSeasonSnapshot>.Fail(seasonResult.Code, seasonResult.Message);
        }

        var season = seasonResult.Data;
        if (!OnlineSeasonStateMachine.TryTransition(season.State, OnlineSeasonState.Ended))
        {
            return OnlineResult<OnlineSeasonSnapshot>.Fail(OnlineErrorCode.StateOperationForbidden, "赛季当前状态不允许结束：" + season.State);
        }

        var boardResult = await _leaderboardService.ResolveBoardAsync(scope, season.LeaderboardId, cancellationToken).ConfigureAwait(false);
        if (!boardResult.IsSuccess)
        {
            return OnlineResult<OnlineSeasonSnapshot>.Fail(boardResult.Code, boardResult.Message);
        }

        var now = nowUnixMilliseconds > 0 ? nowUnixMilliseconds : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        for (var attempt = 0; attempt < MaxResetAttempts; attempt++)
        {
            var entries = await _leaderboardService.ListOrderedEntriesForSeasonAsync(boardResult.Data, cancellationToken).ConfigureAwait(false);
            var snapshot = BuildSnapshot(season, entries, now);
            await _seasonStore.SaveSnapshotAsync(snapshot, cancellationToken).ConfigureAwait(false);

            if (!await _leaderboardService.TryResetForSeasonAsync(boardResult.Data, entries, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            season.State = OnlineSeasonState.Ended;
            season.EndedTime = now;
            await _seasonStore.SaveAsync(season, cancellationToken).ConfigureAwait(false);
            await PublishAsync(OnlineSeasonEvents.CreateSeasonEnded(season, snapshot), cancellationToken).ConfigureAwait(false);
            return OnlineResult<OnlineSeasonSnapshot>.Ok(snapshot);
        }

        return OnlineResult<OnlineSeasonSnapshot>.Fail(OnlineErrorCode.ServiceBusy, "重置期间榜单持续有写入，请稍后重试");
    }

    /// <summary>
    /// 查询赛季历史快照（重置后可回溯旧榜；快照是长期保留的冻结事实，不随新一届写入变化）。
    /// </summary>
    /// <param name="scope">生效作用域（跨作用域与不存在同构拒绝）。</param>
    /// <param name="seasonId">赛季标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>历史快照；失败返回对应错误码。</returns>
    public async Task<OnlineResult<OnlineSeasonSnapshot>> GetSnapshotAsync(OnlineScope scope, string seasonId, CancellationToken cancellationToken = default)
    {
        var seasonResult = await ResolveSeasonAsync(scope, seasonId, cancellationToken).ConfigureAwait(false);
        if (!seasonResult.IsSuccess)
        {
            return OnlineResult<OnlineSeasonSnapshot>.Fail(seasonResult.Code, seasonResult.Message);
        }

        var snapshot = await _seasonStore.FindSnapshotAsync(scope.TenantId, scope.AppId, seasonResult.Data.SeasonId, cancellationToken).ConfigureAwait(false);
        if (snapshot == null)
        {
            return OnlineResult<OnlineSeasonSnapshot>.Fail(OnlineErrorCode.ResourceNotFound, "赛季尚未结束，无历史快照");
        }

        return OnlineResult<OnlineSeasonSnapshot>.Ok(snapshot);
    }

    /// <summary>
    /// 结算赛季奖励（按冻结快照的名次发放；幂等，可安全重试）。
    /// <para>
    /// 重复触发已结算赛季时逐玩家命中幂等回放（返回首次结果，不重复发放）。只要仍有失败玩家，赛季就停留在
    /// <see cref="OnlineSeasonState.Ended"/>，由运维重试（部分发奖不得标记完成）；重试时已发放玩家必定回放、
    /// 不会被重复发放，此前失败的玩家能否补发取决于宿主的幂等失败重放策略（见类型级维护约束 (3)）。
    /// </para>
    /// </summary>
    /// <param name="scope">生效作用域（跨作用域与不存在同构拒绝）。</param>
    /// <param name="seasonId">赛季标识。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒；传 0 取系统时钟，测试可注入）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>逐玩家结算回执；失败返回对应错误码。</returns>
    public async Task<OnlineResult<OnlineSeasonSettlementOutcome>> SettleAsync(OnlineScope scope, string seasonId, long nowUnixMilliseconds = 0, CancellationToken cancellationToken = default)
    {
        var seasonResult = await ResolveSeasonAsync(scope, seasonId, cancellationToken).ConfigureAwait(false);
        if (!seasonResult.IsSuccess)
        {
            return OnlineResult<OnlineSeasonSettlementOutcome>.Fail(seasonResult.Code, seasonResult.Message);
        }

        var season = seasonResult.Data;
        var isReplay = season.State == OnlineSeasonState.Settled;
        if (!isReplay && !OnlineSeasonStateMachine.TryTransition(season.State, OnlineSeasonState.Settled))
        {
            return OnlineResult<OnlineSeasonSettlementOutcome>.Fail(OnlineErrorCode.StateNotReady, "赛季尚未结束，不能结算：" + season.State);
        }

        var snapshot = await _seasonStore.FindSnapshotAsync(scope.TenantId, scope.AppId, season.SeasonId, cancellationToken).ConfigureAwait(false);
        if (snapshot == null)
        {
            return OnlineResult<OnlineSeasonSettlementOutcome>.Fail(OnlineErrorCode.StateNotReady, "赛季快照缺失，拒绝结算");
        }

        var outcome = new OnlineSeasonSettlementOutcome
        {
            SeasonId = season.SeasonId,
            IsReplay = isReplay,
        };

        if (snapshot.Entries != null)
        {
            foreach (var view in snapshot.Entries)
            {
                if (view == null || view.Entry == null)
                {
                    continue;
                }

                var rule = FindRule(season.RewardRules, view.Rank);
                if (rule == null)
                {
                    continue;
                }

                await SettlePlayerAsync(season, view.Entry.PlayerId, rule.Rewards, outcome, cancellationToken).ConfigureAwait(false);
            }
        }

        var now = nowUnixMilliseconds > 0 ? nowUnixMilliseconds : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (!isReplay && outcome.FailedPlayers.Count == 0)
        {
            season.State = OnlineSeasonState.Settled;
            season.SettledTime = now;
            await _seasonStore.SaveAsync(season, cancellationToken).ConfigureAwait(false);
            await PublishAsync(OnlineSeasonEvents.CreateSeasonSettled(season, outcome), cancellationToken).ConfigureAwait(false);
        }

        return OnlineResult<OnlineSeasonSettlementOutcome>.Ok(outcome);
    }

    /// <summary>
    /// 结算单玩家：经统一资产入口发放（幂等键确定性派生，重试回放首次结果）；失败逐玩家隔离。
    /// </summary>
    /// <param name="season">赛季定义（提供作用域与结算标识）。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="rewards">奖励变更行。</param>
    /// <param name="outcome">累计回执。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>完成通知。</returns>
    private async Task SettlePlayerAsync(OnlineSeason season, long playerId, IReadOnlyList<OnlineAssetChangeLine> rewards, OnlineSeasonSettlementOutcome outcome, CancellationToken cancellationToken)
    {
        var businessOrderId = BuildBusinessOrderId(season.SeasonId, playerId);
        var playerScope = new OnlineScope(season.TenantId, season.AppId, SeasonScopeServerId, playerId);
        var request = new OnlineGrantRequest(
            playerScope,
            OnlineAssetChangeSource.MatchReward,
            OnlineGrantOperation.Grant,
            "赛季奖励结算",
            businessOrderId,
            "online-season",
            rewards,
            businessOrderId,
            0,
            season.SeasonId);

        var granted = await _grantService.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
        if (!granted.IsSuccess)
        {
            outcome.FailedPlayers.Add(new OnlineSeasonSettlementFailure
            {
                PlayerId = playerId,
                Code = granted.Code,
                Message = granted.Message,
            });
            return;
        }

        if (granted.Data != null && granted.Data.IsReplay)
        {
            outcome.ReplayCount++;
            return;
        }

        outcome.GrantedCount++;
    }

    /// <summary>
    /// 按作用域解析赛季定义（跨作用域与不存在同构返回失败 ResourceNotFound）。
    /// </summary>
    /// <param name="scope">生效作用域。</param>
    /// <param name="seasonId">赛季标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>赛季定义；不存在返回失败。</returns>
    private async Task<OnlineResult<OnlineSeason>> ResolveSeasonAsync(OnlineScope scope, string seasonId, CancellationToken cancellationToken)
    {
        if (scope == null)
        {
            throw new ArgumentNullException(nameof(scope));
        }

        if (string.IsNullOrEmpty(seasonId))
        {
            return OnlineResult<OnlineSeason>.Fail(OnlineErrorCode.ParameterInvalid, "赛季标识不能为空");
        }

        var season = await _seasonStore.FindAsync(scope.TenantId, scope.AppId, seasonId, cancellationToken).ConfigureAwait(false);
        if (season == null)
        {
            return OnlineResult<OnlineSeason>.Fail(OnlineErrorCode.ResourceNotFound, "赛季不存在");
        }

        return OnlineResult<OnlineSeason>.Ok(season);
    }

    /// <summary>
    /// 以榜单全序条目构造历史快照（名次按全序下标 1 起算，与 C102 查询口径同源）。
    /// </summary>
    /// <param name="season">赛季定义。</param>
    /// <param name="entries">重置前的全序条目。</param>
    /// <param name="snapshottedTime">快照生成时刻（UTC 毫秒）。</param>
    /// <returns>历史快照。</returns>
    private static OnlineSeasonSnapshot BuildSnapshot(OnlineSeason season, List<OnlineLeaderboardEntry> entries, long snapshottedTime)
    {
        var snapshot = new OnlineSeasonSnapshot
        {
            SeasonId = season.SeasonId,
            TenantId = season.TenantId,
            AppId = season.AppId,
            LeaderboardId = season.LeaderboardId,
            SnapshottedTime = snapshottedTime,
            Entries = new List<OnlineLeaderboardEntryView>(),
        };

        if (entries == null)
        {
            return snapshot;
        }

        for (var index = 0; index < entries.Count; index++)
        {
            snapshot.Entries.Add(new OnlineLeaderboardEntryView
            {
                Rank = index + 1,
                Entry = entries[index].Copy(),
            });
        }

        return snapshot;
    }

    /// <summary>
    /// 按冻结名次查找命中的奖励规则（第一条命中即返回；区间互不重叠由创建校验保证）。
    /// </summary>
    /// <param name="rules">奖励规则集合。</param>
    /// <param name="rank">快照冻结名次。</param>
    /// <returns>命中的规则；未命中返回 null。</returns>
    private static OnlineSeasonRewardRule FindRule(List<OnlineSeasonRewardRule> rules, int rank)
    {
        if (rules == null)
        {
            return null;
        }

        foreach (var rule in rules)
        {
            if (rule != null && rule.ContainsRank(rank) && rule.Rewards != null && rule.Rewards.Count > 0)
            {
                return rule;
            }
        }

        return null;
    }

    /// <summary>
    /// 校验奖励规则（非空、名次区间合法、奖励行非空、区间互不重叠）。
    /// </summary>
    /// <param name="rules">待校验规则集合。</param>
    /// <param name="message">校验失败原因。</param>
    /// <returns>通过返回 <c>true</c>。</returns>
    private static bool TryValidateRewardRules(List<OnlineSeasonRewardRule> rules, out string message)
    {
        message = null;
        if (rules == null || rules.Count == 0)
        {
            message = "赛季奖励规则不得为空";
            return false;
        }

        foreach (var rule in rules)
        {
            if (rule == null)
            {
                message = "赛季奖励规则不得为空项";
                return false;
            }

            if (rule.FromRank < 1 || rule.ToRank < rule.FromRank)
            {
                message = "赛季奖励规则名次区间非法";
                return false;
            }

            if (rule.Rewards == null || rule.Rewards.Count == 0)
            {
                message = "赛季奖励规则的奖励明细不得为空";
                return false;
            }
        }

        var ordered = new List<OnlineSeasonRewardRule>(rules);
        ordered.Sort((left, right) => left.FromRank.CompareTo(right.FromRank));
        for (var index = 1; index < ordered.Count; index++)
        {
            if (ordered[index].FromRank <= ordered[index - 1].ToRank)
            {
                message = "赛季奖励规则名次区间重叠（同一名次命中多条规则会重复发奖）";
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 深拷贝奖励规则集合（赛季定义与调用方请求解耦，创建后固化）。
    /// </summary>
    /// <param name="rules">待拷贝规则集合。</param>
    /// <returns>规则副本列表。</returns>
    private static List<OnlineSeasonRewardRule> CopyRules(List<OnlineSeasonRewardRule> rules)
    {
        var copies = new List<OnlineSeasonRewardRule>();
        if (rules == null)
        {
            return copies;
        }

        foreach (var rule in rules)
        {
            if (rule != null)
            {
                copies.Add(rule.Copy());
            }
        }

        return copies;
    }

    /// <summary>
    /// 构造玩家维度的业务单号 / 幂等键（确定性，重投同值——重试按同键回放首次发奖结果）。
    /// </summary>
    /// <param name="seasonId">赛季标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <returns>业务单号。</returns>
    private static string BuildBusinessOrderId(string seasonId, long playerId)
    {
        return string.Concat("season-", seasonId, "-", playerId.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// 发布事件（发布器可空；发布失败不改变已落定的赛季事实）。
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
}
