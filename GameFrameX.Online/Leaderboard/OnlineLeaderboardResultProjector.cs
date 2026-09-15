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
using System.Globalization;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Online.Contracts;
using GameFrameX.Online.Idempotency;
using GameFrameX.Online.Match;
using GameFrameX.Online.Scope;

namespace GameFrameX.Online.Leaderboard;

/// <summary>
/// 可信结果写榜投影器（vault:C8 S7.2：排行榜只接受服务端可信事件，客户端分数提交被拒绝）。
/// <para>
/// 维护约束（红线）：
/// (1) **载荷零信任**——结算结果只从 C98 <see cref="IOnlineMatchResultStore"/>（结算唯一落定点）按
/// (TenantId, AppId, MatchId) 读取，且核对调用方传入的 MatchResultId 与存储值一致；本类型不接受
/// 任何调用方直接携带分数 / 结果载荷的入口（伪造标识 → ResourceNotFound，篡改载荷 → RiskControlRejected，VC-7.1）；
/// (2) **写入幂等**——复用 C93 <see cref="OnlineIdempotencyService"/>，键确定性派生
/// <c>lb-{MatchResultId}-{LeaderboardId}-{PlayerId}</c>（对齐 C98 dispatcher「业务单号由结果标识派生」先例），
/// 同结果事件重复投递只计一次（VC-7.2）；幂等记录过期后的重投残留与 C95 资产域同款，见模块 review.md；
/// (3) 逐玩家失败隔离——防刷 / 限流 / 幂等冲突只影响该玩家，不阻塞同结果其余玩家（VC-7.4）。
/// </para>
/// </summary>
public sealed class OnlineLeaderboardResultProjector
{
    /// <summary>对局结算结果存储（可信事实唯一来源）。</summary>
    private readonly IOnlineMatchResultStore _matchResultStore;

    /// <summary>排行榜服务（榜单解析 + 可信写入）。</summary>
    private readonly OnlineLeaderboardService _leaderboardService;

    /// <summary>幂等服务（C93 组装）。</summary>
    private readonly OnlineIdempotencyService _idempotencyService;

    /// <summary>
    /// 初始化 <see cref="OnlineLeaderboardResultProjector"/>。
    /// </summary>
    /// <param name="matchResultStore">对局结算结果存储。</param>
    /// <param name="leaderboardService">排行榜服务。</param>
    /// <param name="idempotencyService">幂等服务。</param>
    public OnlineLeaderboardResultProjector(IOnlineMatchResultStore matchResultStore, OnlineLeaderboardService leaderboardService, OnlineIdempotencyService idempotencyService)
    {
        _matchResultStore = matchResultStore ?? throw new ArgumentNullException(nameof(matchResultStore));
        _leaderboardService = leaderboardService ?? throw new ArgumentNullException(nameof(leaderboardService));
        _idempotencyService = idempotencyService ?? throw new ArgumentNullException(nameof(idempotencyService));
    }

    /// <summary>
    /// 将一局已结算结果投影到目标榜单（幂等；逐玩家失败隔离）。
    /// </summary>
    /// <param name="scope">生效作用域（榜单与结果的 TenantId / AppId 以此为准）。</param>
    /// <param name="leaderboardId">目标榜单标识。</param>
    /// <param name="matchId">对局标识（据此从结算存储读取结果）。</param>
    /// <param name="expectedMatchResultId">调用方持有的结果标识（与存储值核对，防伪造载荷）。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒；传 0 取系统时钟，测试可注入）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>投影回执（首次落榜数 / 回放数 / 逐玩家失败明细）；失败返回对应错误码。</returns>
    public async Task<OnlineResult<OnlineLeaderboardProjectionOutcome>> ProjectAsync(OnlineScope scope, string leaderboardId, string matchId, string expectedMatchResultId, long nowUnixMilliseconds = 0, CancellationToken cancellationToken = default)
    {
        if (scope == null)
        {
            throw new ArgumentNullException(nameof(scope));
        }

        if (string.IsNullOrEmpty(leaderboardId))
        {
            return OnlineResult<OnlineLeaderboardProjectionOutcome>.Fail(OnlineErrorCode.ParameterInvalid, "榜单标识不能为空");
        }

        if (string.IsNullOrEmpty(matchId))
        {
            return OnlineResult<OnlineLeaderboardProjectionOutcome>.Fail(OnlineErrorCode.ParameterInvalid, "对局标识不能为空");
        }

        if (string.IsNullOrEmpty(expectedMatchResultId))
        {
            return OnlineResult<OnlineLeaderboardProjectionOutcome>.Fail(OnlineErrorCode.ParameterInvalid, "结果标识不能为空");
        }

        var boardResult = await _leaderboardService.ResolveBoardAsync(scope, leaderboardId, cancellationToken).ConfigureAwait(false);
        if (!boardResult.IsSuccess)
        {
            return OnlineResult<OnlineLeaderboardProjectionOutcome>.Fail(boardResult.Code, boardResult.Message);
        }

        var board = boardResult.Data;
        var stored = await _matchResultStore.FindByMatchAsync(scope.TenantId, scope.AppId, matchId, cancellationToken).ConfigureAwait(false);
        if (stored == null)
        {
            return OnlineResult<OnlineLeaderboardProjectionOutcome>.Fail(OnlineErrorCode.ResourceNotFound, "对局结果不存在或未结算");
        }

        if (!string.Equals(stored.MatchResultId, expectedMatchResultId, StringComparison.Ordinal))
        {
            return OnlineResult<OnlineLeaderboardProjectionOutcome>.Fail(OnlineErrorCode.RiskControlRejected, "结果标识与结算事实不一致");
        }

        var now = nowUnixMilliseconds > 0 ? nowUnixMilliseconds : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var outcome = new OnlineLeaderboardProjectionOutcome
        {
            MatchResultId = stored.MatchResultId,
            LeaderboardId = leaderboardId,
        };

        if (stored.Entries == null)
        {
            return OnlineResult<OnlineLeaderboardProjectionOutcome>.Ok(outcome);
        }

        foreach (var entry in stored.Entries)
        {
            if (entry == null)
            {
                continue;
            }

            await ProjectEntryAsync(board, stored, entry, now, outcome, cancellationToken).ConfigureAwait(false);
        }

        return OnlineResult<OnlineLeaderboardProjectionOutcome>.Ok(outcome);
    }

    /// <summary>
    /// 投影单玩家条目：幂等判定 → 首次执行写入 → 落定幂等记录；失败逐玩家隔离。
    /// </summary>
    /// <param name="board">目标榜单。</param>
    /// <param name="result">可信结算结果。</param>
    /// <param name="entry">结果条目。</param>
    /// <param name="now">当前时刻（UTC 毫秒）。</param>
    /// <param name="outcome">累计回执。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>完成通知。</returns>
    private async Task ProjectEntryAsync(OnlineLeaderboard board, OnlineMatchResult result, OnlineMatchResultEntry entry, long now, OnlineLeaderboardProjectionOutcome outcome, CancellationToken cancellationToken)
    {
        var playerScope = new OnlineScope(result.TenantId, result.AppId, result.ServerId, entry.PlayerId);
        var idempotencyKey = BuildIdempotencyKey(result.MatchResultId, board.LeaderboardId, entry.PlayerId);
        var decision = await _idempotencyService.BeginAsync(playerScope, idempotencyKey, BuildCanonicalRequestText(result.MatchResultId, board.LeaderboardId, entry.PlayerId, entry.Score), true, cancellationToken).ConfigureAwait(false);

        if (decision.Kind == OnlineIdempotencyOutcomeKind.Replay)
        {
            outcome.ReplayCount++;
            return;
        }

        if (!decision.CanExecute)
        {
            outcome.FailedPlayers.Add(new OnlineLeaderboardProjectionFailure
            {
                PlayerId = entry.PlayerId,
                Code = decision.ErrorCode,
                Message = "幂等判定未放行：" + decision.Kind,
            });
            return;
        }

        var write = await _leaderboardService.ApplyTrustedScoreAsync(board, new OnlineLeaderboardScoreSubmission
        {
            PlayerId = entry.PlayerId,
            IncomingScore = entry.Score,
            SourceKind = OnlineLeaderboardScoreSource.MatchResult,
            SourceMatchResultId = result.MatchResultId,
            SubmittedTime = now,
        }, now, cancellationToken).ConfigureAwait(false);

        if (write.IsSuccess)
        {
            await _idempotencyService.CompleteAsync(playerScope, idempotencyKey, Encoding.UTF8.GetBytes(write.Entry.Score.ToString(CultureInfo.InvariantCulture)), true, cancellationToken).ConfigureAwait(false);
            outcome.AppliedCount++;
            return;
        }

        await _idempotencyService.FailAsync(playerScope, idempotencyKey, true, cancellationToken).ConfigureAwait(false);
        outcome.FailedPlayers.Add(new OnlineLeaderboardProjectionFailure
        {
            PlayerId = entry.PlayerId,
            Code = write.Code,
            Message = write.Message,
        });
    }

    /// <summary>
    /// 构造确定性幂等键（同结果同榜同玩家重投同值；对齐 C98 dispatcher 派生先例）。
    /// </summary>
    /// <param name="matchResultId">结算结果标识。</param>
    /// <param name="leaderboardId">榜单标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <returns>幂等键。</returns>
    private static string BuildIdempotencyKey(string matchResultId, string leaderboardId, long playerId)
    {
        return string.Concat("lb-", matchResultId, "-", leaderboardId, "-", playerId.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// 构造规范化请求文本（同一业务意图的重复投递必须产生相同文本，幂等摘要据此判定冲突）。
    /// </summary>
    /// <param name="matchResultId">结算结果标识。</param>
    /// <param name="leaderboardId">榜单标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="score">本笔分数。</param>
    /// <returns>规范化请求文本。</returns>
    private static string BuildCanonicalRequestText(string matchResultId, string leaderboardId, long playerId, long score)
    {
        return string.Concat(
            "leaderboard-score|",
            matchResultId,
            "|",
            leaderboardId,
            "|",
            playerId.ToString(CultureInfo.InvariantCulture),
            "|",
            score.ToString(CultureInfo.InvariantCulture));
    }
}
