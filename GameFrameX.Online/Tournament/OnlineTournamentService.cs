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

namespace GameFrameX.Online.Tournament;

/// <summary>
/// 赛事生命周期服务（vault:C8 S7.4：创建、报名、资格条件、开始/结束时间、关联排行榜、结算、奖励、结果查询）。
/// <para>
/// 维护约束（红线）：
/// (1) **只读榜单**——赛事从关联榜单读取报名资格与结束成绩，但**从不写入、也从不重置**该榜：
/// 榜单的写入归 C102 可信链路、重置归 C103 赛季；同一榜单可被赛季与赛事共用，赛事若也重置会清空赛季数据；
/// (2) **成绩先行**——结束赛事时先把关联榜单的全序条目冻结为赛事成绩、再推进状态；冻结一经落档即与榜单后续
/// 变化无关（结果查询的审计依据）；
/// (3) **结算只读冻结成绩**——结算依据是冻结成绩而非实时榜单，成绩缺失一律拒绝结算，不回落、不降级；
/// (4) **结算幂等**——逐玩家经 C95 <see cref="OnlineGrantService"/> 发放，幂等键与业务单号确定性派生
/// <c>tournament-{TournamentId}-{PlayerId}</c>，重复触发命中回放返回首次结果，不重复发奖；
/// (5) **逐玩家失败隔离**——单玩家发放失败只计入回执明细，不阻塞其余玩家；仅当无失败玩家才把赛事推进到
/// 结算完成态（部分发奖不得标记完成）；
/// (6) 跨 App / 跨租户读写与「赛事不存在」同构返回 ResourceNotFound（反预言，对齐 C94/C99/C102/C103 先例）。
/// </para>
/// <para>
/// 运行时边界（X4）：<c>StartTime</c>/<c>EndTime</c> 只是排期元数据，到点驱动 <see cref="StartAsync"/> /
/// <see cref="EndAsync"/> 的调度器、以及持久化与跨实例协调，均归 Server 仓运行时装配。
/// </para>
/// </summary>
public sealed class OnlineTournamentService
{
    /// <summary>
    /// 赛事奖励发放使用的区服位（恒为 0）：赛事与榜单同为 (TenantId, AppId) 作用域，**没有归属服**。
    /// 若改取调用方作用域的区服，同一赛事从不同区服触发结算会落到不同的幂等绑定键上，重试将不再回放首次结果
    /// 而重复发奖（承 C103 R8 结论）。
    /// </summary>
    private const long TournamentScopeServerId = 0;

    /// <summary>赛事存储。</summary>
    private readonly IOnlineTournamentStore _tournamentStore;

    /// <summary>排行榜服务（榜单解析、报名资格读取、结束成绩读取）。</summary>
    private readonly OnlineLeaderboardService _leaderboardService;

    /// <summary>统一资产入口（赛事奖励唯一发放通道，X5）。</summary>
    private readonly OnlineGrantService _grantService;

    /// <summary>事件发布器（可空；未接线时不发事件）。</summary>
    private readonly IOnlineEventPublisher _eventPublisher;

    /// <summary>
    /// 初始化 <see cref="OnlineTournamentService"/>。
    /// </summary>
    /// <param name="tournamentStore">赛事存储。</param>
    /// <param name="leaderboardService">排行榜服务。</param>
    /// <param name="grantService">统一资产入口（赛事奖励发放）。</param>
    /// <param name="eventPublisher">事件发布器（可空；未接线时不发事件）。</param>
    public OnlineTournamentService(IOnlineTournamentStore tournamentStore, OnlineLeaderboardService leaderboardService, OnlineGrantService grantService, IOnlineEventPublisher eventPublisher = null)
    {
        _tournamentStore = tournamentStore ?? throw new ArgumentNullException(nameof(tournamentStore));
        _leaderboardService = leaderboardService ?? throw new ArgumentNullException(nameof(leaderboardService));
        _grantService = grantService ?? throw new ArgumentNullException(nameof(grantService));
        _eventPublisher = eventPublisher;
    }

    /// <summary>
    /// 创建赛事（同作用域同标识赛事已存在时拒绝；关联榜单必须已存在；奖励规则区间必须互不重叠）。
    /// </summary>
    /// <param name="scope">生效作用域（赛事归属取其 TenantId / AppId）。</param>
    /// <param name="request">创建请求。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒；传 0 取系统时钟，测试可注入）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>创建后的赛事定义（初始状态为 <see cref="OnlineTournamentState.Scheduled"/>）；失败返回对应错误码。</returns>
    public async Task<OnlineResult<OnlineTournament>> CreateAsync(OnlineScope scope, OnlineTournamentCreateRequest request, long nowUnixMilliseconds = 0, CancellationToken cancellationToken = default)
    {
        if (scope == null)
        {
            throw new ArgumentNullException(nameof(scope));
        }

        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (string.IsNullOrEmpty(request.TournamentId))
        {
            return OnlineResult<OnlineTournament>.Fail(OnlineErrorCode.ParameterInvalid, "赛事标识不能为空");
        }

        if (string.IsNullOrEmpty(request.LeaderboardId))
        {
            return OnlineResult<OnlineTournament>.Fail(OnlineErrorCode.ParameterInvalid, "关联榜单标识不能为空");
        }

        if (request.EndTime <= request.StartTime)
        {
            return OnlineResult<OnlineTournament>.Fail(OnlineErrorCode.ParameterInvalid, "赛事结束时刻必须晚于开始时刻");
        }

        if (!TryValidateRewardRules(request.RewardRules, out var ruleMessage))
        {
            return OnlineResult<OnlineTournament>.Fail(OnlineErrorCode.ParameterInvalid, ruleMessage);
        }

        if (!TryValidateEligibility(request.Eligibility, out var eligibilityMessage))
        {
            return OnlineResult<OnlineTournament>.Fail(OnlineErrorCode.ParameterInvalid, eligibilityMessage);
        }

        var boardResult = await _leaderboardService.ResolveBoardAsync(scope, request.LeaderboardId, cancellationToken).ConfigureAwait(false);
        if (!boardResult.IsSuccess)
        {
            return OnlineResult<OnlineTournament>.Fail(boardResult.Code, boardResult.Message);
        }

        var tournament = new OnlineTournament
        {
            TournamentId = request.TournamentId,
            TenantId = scope.TenantId,
            AppId = scope.AppId,
            LeaderboardId = request.LeaderboardId,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            Eligibility = request.Eligibility == null ? new OnlineTournamentEligibility() : request.Eligibility.Copy(),
            RewardRules = CopyRules(request.RewardRules),
            State = OnlineTournamentState.Scheduled,
            CreatedTime = nowUnixMilliseconds > 0 ? nowUnixMilliseconds : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };

        var created = await _tournamentStore.CreateAsync(tournament, cancellationToken).ConfigureAwait(false);
        if (created == null)
        {
            return OnlineResult<OnlineTournament>.Fail(OnlineErrorCode.ParameterInvalid, "赛事标识已存在");
        }

        await PublishAsync(OnlineTournamentEvents.CreateTournamentCreated(created), cancellationToken).ConfigureAwait(false);
        return OnlineResult<OnlineTournament>.Ok(created);
    }

    /// <summary>
    /// 开始赛事（<see cref="OnlineTournamentState.Scheduled"/> → <see cref="OnlineTournamentState.Active"/>；**开始即关闭报名窗口**）。
    /// </summary>
    /// <param name="scope">生效作用域（跨作用域与不存在同构拒绝）。</param>
    /// <param name="tournamentId">赛事标识。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒；传 0 取系统时钟，测试可注入）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>开始后的赛事定义；失败返回对应错误码。</returns>
    public async Task<OnlineResult<OnlineTournament>> StartAsync(OnlineScope scope, string tournamentId, long nowUnixMilliseconds = 0, CancellationToken cancellationToken = default)
    {
        var tournamentResult = await ResolveTournamentAsync(scope, tournamentId, cancellationToken).ConfigureAwait(false);
        if (!tournamentResult.IsSuccess)
        {
            return tournamentResult;
        }

        var tournament = tournamentResult.Data;
        if (!OnlineTournamentStateMachine.TryTransition(tournament.State, OnlineTournamentState.Active))
        {
            return OnlineResult<OnlineTournament>.Fail(OnlineErrorCode.StateOperationForbidden, "赛事当前状态不允许开始：" + tournament.State);
        }

        tournament.State = OnlineTournamentState.Active;
        tournament.StartedTime = nowUnixMilliseconds > 0 ? nowUnixMilliseconds : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _tournamentStore.SaveAsync(tournament, cancellationToken).ConfigureAwait(false);
        await PublishAsync(OnlineTournamentEvents.CreateTournamentStarted(tournament), cancellationToken).ConfigureAwait(false);
        return OnlineResult<OnlineTournament>.Ok(tournament);
    }

    /// <summary>
    /// 报名参赛（vault:C8 VC-7.8：资格条件判定 + 报名幂等）。
    /// <para>
    /// 系统级失败（赛事不存在 / 状态不允许报名 / 关联榜单不可读）走失败分支；**资格不满足是业务结论**，
    /// 以成功回执携带 <see cref="OnlineTournamentRegistrationOutcome.Rejection"/> 返回（对齐 C99
    /// <c>OnlineSocialDecision</c>「允许 + 拒绝码 + 原因」先例，且不占用冻结的 <c>OnlineErrorCode</c>）。
    /// 重复报名返回既有登记并置 <see cref="OnlineTournamentRegistrationOutcome.IsReplay"/>，不新增登记、不重发事件。
    /// </para>
    /// </summary>
    /// <param name="scope">生效作用域（跨作用域与不存在同构拒绝）。</param>
    /// <param name="tournamentId">赛事标识。</param>
    /// <param name="playerId">报名玩家标识。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒；传 0 取系统时钟，测试可注入）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>报名回执；系统级失败返回对应错误码。</returns>
    public async Task<OnlineResult<OnlineTournamentRegistrationOutcome>> RegisterAsync(OnlineScope scope, string tournamentId, long playerId, long nowUnixMilliseconds = 0, CancellationToken cancellationToken = default)
    {
        if (playerId <= 0)
        {
            return OnlineResult<OnlineTournamentRegistrationOutcome>.Fail(OnlineErrorCode.ParameterInvalid, "报名玩家标识非法");
        }

        var tournamentResult = await ResolveTournamentAsync(scope, tournamentId, cancellationToken).ConfigureAwait(false);
        if (!tournamentResult.IsSuccess)
        {
            return OnlineResult<OnlineTournamentRegistrationOutcome>.Fail(tournamentResult.Code, tournamentResult.Message);
        }

        var tournament = tournamentResult.Data;
        if (tournament.State != OnlineTournamentState.Scheduled)
        {
            return OnlineResult<OnlineTournamentRegistrationOutcome>.Fail(OnlineErrorCode.StateOperationForbidden, "赛事当前状态不允许报名：" + tournament.State);
        }

        var rank = 0;
        var score = 0L;
        var eligibility = tournament.Eligibility ?? new OnlineTournamentEligibility();
        if (eligibility.RequiresLeaderboardLookup())
        {
            var rankResult = await _leaderboardService.GetPlayerRankAsync(scope, tournament.LeaderboardId, playerId, nowUnixMilliseconds, cancellationToken).ConfigureAwait(false);
            if (rankResult.IsSuccess && rankResult.Data != null && rankResult.Data.Entry != null)
            {
                rank = rankResult.Data.Rank;
                score = rankResult.Data.Entry.Score;
            }
            else if (rankResult.Code != OnlineErrorCode.ResourceNotFound)
            {
                return OnlineResult<OnlineTournamentRegistrationOutcome>.Fail(rankResult.Code, rankResult.Message);
            }
        }

        var rejection = eligibility.Evaluate(rank, score);
        if (rejection != OnlineTournamentRegistrationRejection.None)
        {
            return OnlineResult<OnlineTournamentRegistrationOutcome>.Ok(new OnlineTournamentRegistrationOutcome
            {
                Accepted = false,
                IsReplay = false,
                Rejection = rejection,
                Registration = null,
            });
        }

        var registration = new OnlineTournamentRegistration
        {
            TournamentId = tournament.TournamentId,
            TenantId = tournament.TenantId,
            AppId = tournament.AppId,
            PlayerId = playerId,
            RegisteredTime = nowUnixMilliseconds > 0 ? nowUnixMilliseconds : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            RankAtRegistration = rank,
            ScoreAtRegistration = score,
        };

        var registered = await _tournamentStore.TryRegisterAsync(registration, cancellationToken).ConfigureAwait(false);
        var isReplay = !registered.IsNew;
        var stored = registered.Registration;

        if (!isReplay)
        {
            await PublishAsync(OnlineTournamentEvents.CreateTournamentRegistered(tournament, stored), cancellationToken).ConfigureAwait(false);
        }

        return OnlineResult<OnlineTournamentRegistrationOutcome>.Ok(new OnlineTournamentRegistrationOutcome
        {
            Accepted = true,
            IsReplay = isReplay,
            Rejection = OnlineTournamentRegistrationRejection.None,
            Registration = stored,
        });
    }

    /// <summary>
    /// 结束赛事并冻结成绩（vault:C8 S7.4 核心：报名即参赛，成绩在结束时从关联榜单一次性冻结）。
    /// <para>
    /// 冻结范围**限于已报名参赛者**：未报名的玩家即使在该共用榜单上名列前茅也不进入赛事成绩——
    /// 否则「报名」对奖励发放没有任何约束力。名次按 C102 全序（分数按榜向 → 更新时间 → 玩家标识）
    /// 在参赛者集合内重新编号 1..N，使赛事名次是赛事内部的事实。**本流程不修改关联榜单**（只读，P0-1）。
    /// </para>
    /// </summary>
    /// <param name="scope">生效作用域（跨作用域与不存在同构拒绝）。</param>
    /// <param name="tournamentId">赛事标识。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒；传 0 取系统时钟，测试可注入）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>冻结的赛事成绩（即「结果查询」的证据）；失败返回对应错误码。</returns>
    public async Task<OnlineResult<OnlineTournamentStandings>> EndAsync(OnlineScope scope, string tournamentId, long nowUnixMilliseconds = 0, CancellationToken cancellationToken = default)
    {
        var tournamentResult = await ResolveTournamentAsync(scope, tournamentId, cancellationToken).ConfigureAwait(false);
        if (!tournamentResult.IsSuccess)
        {
            return OnlineResult<OnlineTournamentStandings>.Fail(tournamentResult.Code, tournamentResult.Message);
        }

        var tournament = tournamentResult.Data;
        if (!OnlineTournamentStateMachine.TryTransition(tournament.State, OnlineTournamentState.Ended))
        {
            return OnlineResult<OnlineTournamentStandings>.Fail(OnlineErrorCode.StateOperationForbidden, "赛事当前状态不允许结束：" + tournament.State);
        }

        var boardResult = await _leaderboardService.ResolveBoardAsync(scope, tournament.LeaderboardId, cancellationToken).ConfigureAwait(false);
        if (!boardResult.IsSuccess)
        {
            return OnlineResult<OnlineTournamentStandings>.Fail(boardResult.Code, boardResult.Message);
        }

        var entries = await _leaderboardService.ListOrderedEntriesForSeasonAsync(boardResult.Data, cancellationToken).ConfigureAwait(false);
        var registrations = await _tournamentStore.ListRegistrationsAsync(tournament.TenantId, tournament.AppId, tournament.TournamentId, cancellationToken).ConfigureAwait(false);

        var now = nowUnixMilliseconds > 0 ? nowUnixMilliseconds : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var standings = BuildStandings(tournament, entries, registrations, now);
        await _tournamentStore.SaveStandingsAsync(standings, cancellationToken).ConfigureAwait(false);

        tournament.State = OnlineTournamentState.Ended;
        tournament.EndedTime = now;
        await _tournamentStore.SaveAsync(tournament, cancellationToken).ConfigureAwait(false);
        await PublishAsync(OnlineTournamentEvents.CreateTournamentEnded(tournament, standings), cancellationToken).ConfigureAwait(false);
        return OnlineResult<OnlineTournamentStandings>.Ok(standings);
    }

    /// <summary>
    /// 查询赛事冻结成绩（结果查询；冻结后不随关联榜单变化）。
    /// </summary>
    /// <param name="scope">生效作用域（跨作用域与不存在同构拒绝）。</param>
    /// <param name="tournamentId">赛事标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>冻结成绩；失败返回对应错误码。</returns>
    public async Task<OnlineResult<OnlineTournamentStandings>> GetStandingsAsync(OnlineScope scope, string tournamentId, CancellationToken cancellationToken = default)
    {
        var tournamentResult = await ResolveTournamentAsync(scope, tournamentId, cancellationToken).ConfigureAwait(false);
        if (!tournamentResult.IsSuccess)
        {
            return OnlineResult<OnlineTournamentStandings>.Fail(tournamentResult.Code, tournamentResult.Message);
        }

        var standings = await _tournamentStore.FindStandingsAsync(scope.TenantId, scope.AppId, tournamentResult.Data.TournamentId, cancellationToken).ConfigureAwait(false);
        if (standings == null)
        {
            return OnlineResult<OnlineTournamentStandings>.Fail(OnlineErrorCode.ResourceNotFound, "赛事尚未结束，无冻结成绩");
        }

        return OnlineResult<OnlineTournamentStandings>.Ok(standings);
    }

    /// <summary>
    /// 查询玩家报名登记（结果查询半边：报名事实与报名时刻冻结的资格判定输入）。
    /// </summary>
    /// <param name="scope">生效作用域（跨作用域与不存在同构拒绝）。</param>
    /// <param name="tournamentId">赛事标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>报名登记；未报名返回 ResourceNotFound。</returns>
    public async Task<OnlineResult<OnlineTournamentRegistration>> GetRegistrationAsync(OnlineScope scope, string tournamentId, long playerId, CancellationToken cancellationToken = default)
    {
        var tournamentResult = await ResolveTournamentAsync(scope, tournamentId, cancellationToken).ConfigureAwait(false);
        if (!tournamentResult.IsSuccess)
        {
            return OnlineResult<OnlineTournamentRegistration>.Fail(tournamentResult.Code, tournamentResult.Message);
        }

        var registration = await _tournamentStore.FindRegistrationAsync(scope.TenantId, scope.AppId, tournamentResult.Data.TournamentId, playerId, cancellationToken).ConfigureAwait(false);
        if (registration == null)
        {
            return OnlineResult<OnlineTournamentRegistration>.Fail(OnlineErrorCode.ResourceNotFound, "该玩家未报名本赛事");
        }

        return OnlineResult<OnlineTournamentRegistration>.Ok(registration);
    }

    /// <summary>
    /// 结算赛事奖励（按冻结成绩的名次发放；幂等，可安全重试）。
    /// <para>
    /// 重复触发已结算赛事时逐玩家命中幂等回放（返回首次结果，不重复发放）。只要仍有失败玩家，赛事就停留在
    /// <see cref="OnlineTournamentState.Ended"/>，由运维重试（部分发奖不得标记完成）；重试时已发放玩家必定回放、
    /// 不会被重复发放，此前失败的玩家能否补发取决于宿主的幂等失败重放策略
    /// （与 C103 同款装配依赖：默认 <c>ReplayError</c> 下失败键在保留期内原样回放）。
    /// </para>
    /// </summary>
    /// <param name="scope">生效作用域（跨作用域与不存在同构拒绝）。</param>
    /// <param name="tournamentId">赛事标识。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒；传 0 取系统时钟，测试可注入）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>逐玩家结算回执；失败返回对应错误码。</returns>
    public async Task<OnlineResult<OnlineTournamentSettlementOutcome>> SettleAsync(OnlineScope scope, string tournamentId, long nowUnixMilliseconds = 0, CancellationToken cancellationToken = default)
    {
        var tournamentResult = await ResolveTournamentAsync(scope, tournamentId, cancellationToken).ConfigureAwait(false);
        if (!tournamentResult.IsSuccess)
        {
            return OnlineResult<OnlineTournamentSettlementOutcome>.Fail(tournamentResult.Code, tournamentResult.Message);
        }

        var tournament = tournamentResult.Data;
        var isReplay = tournament.State == OnlineTournamentState.Settled;
        if (!isReplay && !OnlineTournamentStateMachine.TryTransition(tournament.State, OnlineTournamentState.Settled))
        {
            return OnlineResult<OnlineTournamentSettlementOutcome>.Fail(OnlineErrorCode.StateNotReady, "赛事尚未结束，不能结算：" + tournament.State);
        }

        var standings = await _tournamentStore.FindStandingsAsync(scope.TenantId, scope.AppId, tournament.TournamentId, cancellationToken).ConfigureAwait(false);
        if (standings == null)
        {
            return OnlineResult<OnlineTournamentSettlementOutcome>.Fail(OnlineErrorCode.StateNotReady, "赛事冻结成绩缺失，拒绝结算");
        }

        var outcome = new OnlineTournamentSettlementOutcome
        {
            TournamentId = tournament.TournamentId,
            IsReplay = isReplay,
        };

        await SettleStandingsEntriesAsync(tournament, standings, outcome, cancellationToken).ConfigureAwait(false);

        if (!isReplay && outcome.FailedPlayers.Count == 0)
        {
            tournament.State = OnlineTournamentState.Settled;
            tournament.SettledTime = nowUnixMilliseconds > 0 ? nowUnixMilliseconds : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await _tournamentStore.SaveAsync(tournament, cancellationToken).ConfigureAwait(false);
            await PublishAsync(OnlineTournamentEvents.CreateTournamentSettled(tournament, outcome), cancellationToken).ConfigureAwait(false);
        }

        return OnlineResult<OnlineTournamentSettlementOutcome>.Ok(outcome);
    }

    /// <summary>
    /// 按冻结成绩逐条目结算奖励（跳过无效视图与名次未命中规则的条目；发放经 <see cref="SettlePlayerAsync"/> 幂等执行）。
    /// </summary>
    /// <param name="tournament">赛事定义（提供作用域与奖励规则）。</param>
    /// <param name="standings">结算依据的冻结成绩。</param>
    /// <param name="outcome">累计回执。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>完成通知。</returns>
    private async Task SettleStandingsEntriesAsync(OnlineTournament tournament, OnlineTournamentStandings standings, OnlineTournamentSettlementOutcome outcome, CancellationToken cancellationToken)
    {
        if (standings.Entries == null)
        {
            return;
        }

        foreach (var view in standings.Entries)
        {
            if (view == null || view.Entry == null)
            {
                continue;
            }

            var rule = FindRule(tournament.RewardRules, view.Rank);
            if (rule == null)
            {
                continue;
            }

            await SettlePlayerAsync(tournament, view.Entry.PlayerId, rule.Rewards, outcome, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 结算单玩家：经统一资产入口发放（幂等键确定性派生，重试回放首次结果）；失败逐玩家隔离。
    /// </summary>
    /// <param name="tournament">赛事定义（提供作用域与结算标识）。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="rewards">奖励变更行。</param>
    /// <param name="outcome">累计回执。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>完成通知。</returns>
    private async Task SettlePlayerAsync(OnlineTournament tournament, long playerId, IReadOnlyList<OnlineAssetChangeLine> rewards, OnlineTournamentSettlementOutcome outcome, CancellationToken cancellationToken)
    {
        var businessOrderId = BuildBusinessOrderId(tournament.TournamentId, playerId);
        var playerScope = new OnlineScope(tournament.TenantId, tournament.AppId, TournamentScopeServerId, playerId);
        var request = new OnlineGrantRequest(
            playerScope,
            OnlineAssetChangeSource.MatchReward,
            OnlineGrantOperation.Grant,
            "赛事奖励结算",
            businessOrderId,
            rewards,
            businessOrderId)
        {
            OperatorId = "online-tournament",
            CorrelationId = tournament.TournamentId,
        };

        var granted = await _grantService.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
        if (!granted.IsSuccess)
        {
            outcome.FailedPlayers.Add(new OnlineTournamentSettlementFailure
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
    /// 按作用域解析赛事定义（跨作用域与不存在同构返回失败 ResourceNotFound）。
    /// </summary>
    /// <param name="scope">生效作用域。</param>
    /// <param name="tournamentId">赛事标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>赛事定义；不存在返回失败。</returns>
    private async Task<OnlineResult<OnlineTournament>> ResolveTournamentAsync(OnlineScope scope, string tournamentId, CancellationToken cancellationToken)
    {
        if (scope == null)
        {
            throw new ArgumentNullException(nameof(scope));
        }

        if (string.IsNullOrEmpty(tournamentId))
        {
            return OnlineResult<OnlineTournament>.Fail(OnlineErrorCode.ParameterInvalid, "赛事标识不能为空");
        }

        var tournament = await _tournamentStore.FindAsync(scope.TenantId, scope.AppId, tournamentId, cancellationToken).ConfigureAwait(false);
        if (tournament == null)
        {
            return OnlineResult<OnlineTournament>.Fail(OnlineErrorCode.ResourceNotFound, "赛事不存在");
        }

        return OnlineResult<OnlineTournament>.Ok(tournament);
    }

    /// <summary>
    /// 以榜单全序条目构造赛事冻结成绩（**仅纳入已报名参赛者**，名次在参赛者集合内按 C102 全序重新编号 1..N）。
    /// </summary>
    /// <param name="tournament">赛事定义。</param>
    /// <param name="entries">结束时读取的榜单全序条目。</param>
    /// <param name="registrations">赛事报名登记（参赛者集合）。</param>
    /// <param name="frozenTime">冻结时刻（UTC 毫秒）。</param>
    /// <returns>冻结成绩。</returns>
    private static OnlineTournamentStandings BuildStandings(OnlineTournament tournament, List<OnlineLeaderboardEntry> entries, List<OnlineTournamentRegistration> registrations, long frozenTime)
    {
        var standings = new OnlineTournamentStandings
        {
            TournamentId = tournament.TournamentId,
            TenantId = tournament.TenantId,
            AppId = tournament.AppId,
            LeaderboardId = tournament.LeaderboardId,
            FrozenTime = frozenTime,
            Entries = new List<OnlineLeaderboardEntryView>(),
        };

        if (entries == null)
        {
            return standings;
        }

        var registeredPlayerIds = new HashSet<long>();
        if (registrations != null)
        {
            foreach (var registration in registrations)
            {
                if (registration != null)
                {
                    registeredPlayerIds.Add(registration.PlayerId);
                }
            }
        }

        foreach (var entry in entries)
        {
            if (entry == null || !registeredPlayerIds.Contains(entry.PlayerId))
            {
                continue;
            }

            standings.Entries.Add(new OnlineLeaderboardEntryView
            {
                Rank = standings.Entries.Count + 1,
                Entry = entry.Copy(),
            });
        }

        return standings;
    }

    /// <summary>
    /// 按冻结名次查找命中的奖励规则（第一条命中即返回；区间互不重叠由创建校验保证）。
    /// </summary>
    /// <param name="rules">奖励规则集合。</param>
    /// <param name="rank">冻结名次。</param>
    /// <returns>命中的规则；未命中返回 null。</returns>
    private static OnlineTournamentRewardRule FindRule(List<OnlineTournamentRewardRule> rules, int rank)
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
    private static bool TryValidateRewardRules(List<OnlineTournamentRewardRule> rules, out string message)
    {
        message = null;
        if (rules == null || rules.Count == 0)
        {
            message = "赛事奖励规则不得为空";
            return false;
        }

        foreach (var rule in rules)
        {
            if (rule == null)
            {
                message = "赛事奖励规则不得为空项";
                return false;
            }

            if (rule.FromRank < 1 || rule.ToRank < rule.FromRank)
            {
                message = "赛事奖励规则名次区间非法";
                return false;
            }

            if (rule.Rewards == null || rule.Rewards.Count == 0)
            {
                message = "赛事奖励规则的奖励明细不得为空";
                return false;
            }
        }

        var ordered = new List<OnlineTournamentRewardRule>(rules);
        ordered.Sort((left, right) => left.FromRank.CompareTo(right.FromRank));
        for (var index = 1; index < ordered.Count; index++)
        {
            if (ordered[index].FromRank <= ordered[index - 1].ToRank)
            {
                message = "赛事奖励规则名次区间重叠（同一名次命中多条规则会重复发奖）";
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 校验报名资格条件（名次上限非负、分数下限不得为负）。
    /// </summary>
    /// <param name="eligibility">待校验资格条件（可空，表示无门槛）。</param>
    /// <param name="message">校验失败原因。</param>
    /// <returns>通过返回 <c>true</c>。</returns>
    private static bool TryValidateEligibility(OnlineTournamentEligibility eligibility, out string message)
    {
        message = null;
        if (eligibility == null)
        {
            return true;
        }

        if (eligibility.MinLeaderboardRank < 0)
        {
            message = "赛事资格条件的名次上限不得为负";
            return false;
        }

        if (eligibility.MinLeaderboardScore.HasValue && eligibility.MinLeaderboardScore.Value < 0)
        {
            message = "赛事资格条件的分数下限不得为负";
            return false;
        }

        return true;
    }

    /// <summary>
    /// 深拷贝奖励规则集合（赛事定义与调用方请求解耦，创建后固化）。
    /// </summary>
    /// <param name="rules">待拷贝规则集合。</param>
    /// <returns>规则副本列表。</returns>
    private static List<OnlineTournamentRewardRule> CopyRules(List<OnlineTournamentRewardRule> rules)
    {
        var copies = new List<OnlineTournamentRewardRule>();
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
    /// <param name="tournamentId">赛事标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <returns>业务单号。</returns>
    private static string BuildBusinessOrderId(string tournamentId, long playerId)
    {
        return string.Concat("tournament-", tournamentId, "-", playerId.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// 发布事件（发布器可空；发布失败不改变已落定的赛事事实）。
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
