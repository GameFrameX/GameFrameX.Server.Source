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
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Online.Contracts;
using GameFrameX.Online.Events;

namespace GameFrameX.Online.Match;

/// <summary>
/// 结算服务（vault:C6 S5.7「结算事实先落定，发奖与结果分发解耦」）。
/// <para>
/// 维护约束（红线）：同一对局只结算一次。两层保证：
/// (1) <see cref="IOnlineMatchResultStore.CommitAsync"/> 首结果获胜——并发触发也只有一份结果能落地；
/// (2) 已存在结果时直接复用，不再进入 Actor 的结算阶段（避免二次生成 <c>MatchResultId</c>）。
/// 因此「两个客户端同时喊结算」或「结算被重试」都不会产生第二份结果（VC-5.8 / VC-5.9）。
/// </para>
/// <para>
/// 结算失败（玩法无法产出结果）时把对局转入 <see cref="OnlineMatchState.SettlementFailed"/>——
/// 这是确定态而非僵尸：它仍会流向 <see cref="OnlineMatchState.Closed"/> 并被 Tick 释放，
/// 但不会有任何奖励被分发。
/// </para>
/// </summary>
public sealed class OnlineMatchSettlementService
{
    /// <summary>对局运行时。</summary>
    private readonly OnlineMatchRuntime _runtime;

    /// <summary>结算结果存储（幂等边界）。</summary>
    private readonly IOnlineMatchResultStore _resultStore;

    /// <summary>事件发布器（可空）。</summary>
    private readonly IOnlineEventPublisher _eventPublisher;

    /// <summary>
    /// 初始化结算服务。
    /// </summary>
    /// <param name="runtime">对局运行时。</param>
    /// <param name="resultStore">结算结果存储。</param>
    /// <param name="eventPublisher">事件发布器（可空）。</param>
    public OnlineMatchSettlementService(OnlineMatchRuntime runtime, IOnlineMatchResultStore resultStore, IOnlineEventPublisher eventPublisher = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _resultStore = resultStore ?? throw new ArgumentNullException(nameof(resultStore));
        _eventPublisher = eventPublisher;
    }

    /// <summary>
    /// 对指定对局执行结算（幂等）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="matchId">对局标识。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>结算执行结果；失败返回对应错误码。</returns>
    public async Task<OnlineResult<OnlineMatchSettlementOutcome>> SettleAsync(long tenantId, long appId, string matchId, long nowUnixMilliseconds, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(matchId))
        {
            return OnlineResult<OnlineMatchSettlementOutcome>.Fail(OnlineErrorCode.ParameterInvalid, "对局标识不能为空");
        }

        var existing = await _resultStore.FindByMatchAsync(tenantId, appId, matchId, cancellationToken).ConfigureAwait(false);
        if (existing != null)
        {
            return OnlineResult<OnlineMatchSettlementOutcome>.Ok(new OnlineMatchSettlementOutcome
            {
                Result = existing,
                IsReplay = true,
                State = OnlineMatchState.Completed,
            });
        }

        var actor = await _runtime.ResolveActorAsync(tenantId, appId, matchId, cancellationToken).ConfigureAwait(false);
        if (actor == null)
        {
            return OnlineResult<OnlineMatchSettlementOutcome>.Fail(OnlineErrorCode.ResourceNotFound, "对局不存在");
        }

        var built = await actor.BuildResultAsync(nowUnixMilliseconds, cancellationToken).ConfigureAwait(false);
        if (!built.IsSuccess)
        {
            if (built.Code == OnlineErrorCode.StateNotReady)
            {
                return OnlineResult<OnlineMatchSettlementOutcome>.Fail(built.Code, built.Message);
            }

            await actor.MarkSettlementFailedAsync(nowUnixMilliseconds, cancellationToken).ConfigureAwait(false);
            return OnlineResult<OnlineMatchSettlementOutcome>.Fail(built.Code, built.Message);
        }

        var committed = await _resultStore.CommitAsync(built.Data, cancellationToken).ConfigureAwait(false);
        if (committed == null)
        {
            return OnlineResult<OnlineMatchSettlementOutcome>.Fail(OnlineErrorCode.ServiceBusy, "结算结果写入失败");
        }

        var isReplay = !string.Equals(committed.MatchResultId, built.Data.MatchResultId, StringComparison.Ordinal);
        if (!isReplay)
        {
            await actor.CompleteSettlementAsync(committed, nowUnixMilliseconds, cancellationToken).ConfigureAwait(false);
            await PublishAsync(OnlineMatchRuntimeEvents.CreateMatchSettled(committed), cancellationToken).ConfigureAwait(false);
        }

        return OnlineResult<OnlineMatchSettlementOutcome>.Ok(new OnlineMatchSettlementOutcome
        {
            Result = committed,
            IsReplay = isReplay,
            State = OnlineMatchState.Completed,
        });
    }

    /// <summary>
    /// 发布事件（发布器可空；发布失败不改变已落定的结算事实）。
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
