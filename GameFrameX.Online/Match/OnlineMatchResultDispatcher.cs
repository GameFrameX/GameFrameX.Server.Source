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
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Online.Assets;
using GameFrameX.Online.Contracts;
using GameFrameX.Online.Scope;

namespace GameFrameX.Online.Match;

/// <summary>
/// 结算结果分发器（vault:C6 S5.8「可信结果 → 资产发放」）。
/// <para>
/// 维护约束（红线）：奖励发放的幂等边界是 <see cref="OnlineMatchResult.MatchResultId"/>——
/// 业务单号与幂等键都由它派生（<c>{MatchResultId}-{PlayerId}</c>），
/// 因此重复投递同一份结算结果不会二次发奖；而不同玩家的键不同，所以逐玩家独立判定，
/// 一个玩家的失败或重放不影响其他玩家。分发**只消费服务端产出的结果对象**，
/// 没有任何客户端可提交奖励结果的入口（VC-5.9）。
/// </para>
/// <para>
/// 本次只覆盖资产侧（排行榜与通知由下游阶段消费 <c>Online.Match.Settled</c> 事件自行处理）。
/// </para>
/// </summary>
public sealed class OnlineMatchResultDispatcher
{
    /// <summary>发奖入口（资产域唯一写入通道）。</summary>
    private readonly OnlineGrantService _grantService;

    /// <summary>
    /// 初始化分发器。
    /// </summary>
    /// <param name="grantService">发奖入口。</param>
    public OnlineMatchResultDispatcher(OnlineGrantService grantService)
    {
        _grantService = grantService ?? throw new ArgumentNullException(nameof(grantService));
    }

    /// <summary>
    /// 按结算结果逐玩家发放奖励。
    /// </summary>
    /// <param name="result">已落定的结算结果。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>分发回执；入参非法返回对应错误码。</returns>
    public async Task<OnlineResult<OnlineMatchDispatchOutcome>> DispatchAsync(OnlineMatchResult result, CancellationToken cancellationToken = default)
    {
        if (result == null || string.IsNullOrEmpty(result.MatchResultId))
        {
            return OnlineResult<OnlineMatchDispatchOutcome>.Fail(OnlineErrorCode.ParameterInvalid, "结算结果或结果标识缺失");
        }

        var outcome = new OnlineMatchDispatchOutcome
        {
            MatchResultId = result.MatchResultId,
            FailedPlayerIds = new List<long>(),
        };

        if (result.Entries == null)
        {
            return OnlineResult<OnlineMatchDispatchOutcome>.Ok(outcome);
        }

        foreach (var entry in result.Entries)
        {
            if (entry == null || entry.Rewards == null || entry.Rewards.Count == 0)
            {
                continue;
            }

            var businessOrderId = BuildBusinessOrderId(result.MatchResultId, entry.PlayerId);
            var scope = new OnlineScope(result.TenantId, result.AppId, result.ServerId, entry.PlayerId);
            var request = new OnlineGrantRequest(
                scope,
                OnlineAssetChangeSource.MatchReward,
                OnlineGrantOperation.Grant,
                "对局结算奖励",
                businessOrderId,
                "online-match",
                entry.Rewards,
                businessOrderId,
                0,
                result.MatchResultId);

            var granted = await _grantService.ExecuteAsync(request, cancellationToken).ConfigureAwait(false);
            if (!granted.IsSuccess)
            {
                outcome.FailedPlayerIds.Add(entry.PlayerId);
                continue;
            }

            outcome.SucceededCount++;
            if (granted.Data != null && granted.Data.IsReplay)
            {
                outcome.ReplayCount++;
            }
        }

        return OnlineResult<OnlineMatchDispatchOutcome>.Ok(outcome);
    }

    /// <summary>
    /// 构造玩家维度的业务单号 / 幂等键（确定性，重投同值）。
    /// </summary>
    /// <param name="matchResultId">结算结果标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <returns>业务单号。</returns>
    private static string BuildBusinessOrderId(string matchResultId, long playerId)
    {
        return string.Concat(matchResultId, "-", playerId.ToString(CultureInfo.InvariantCulture));
    }
}
