// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and other rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Capability.Online.ServerLifecycle.ServerMerge;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Events;
using GameFrameX.Capability.Online.Abstractions.Keys;

/// <summary>
/// 合服归并服务（框架原语面：计划幂等登记 → dry-run 预演留存 → 参与者编排执行 → 幂等回放）。
/// </summary>
/// <remarks>
/// 红线：① 一切真实执行 dry-run 先行（<see cref="OnlineServerMergeOptions.RequireDryRunBeforeExecute"/>
/// 默认开启：无该计划 dry-run 报告时拒绝执行）；② 幂等可重入——同一计划重复执行返回既有执行报告回放
/// （<c>AlreadyExecuted=true</c>），参与者自身也必须幂等；③ 全局域零操作铁律——参与者上报的全域触碰计数
/// 非 0 时执行拒绝落定（<see cref="OnlineErrorCode.StateOperationForbidden"/>）。
/// </remarks>
public sealed class OnlineServerMergeService
{
    /// <summary>
    /// 内置参与者顺序（锚点标记 → 榜单封档 → 在途对局强制结算 → PlayerRoute 重指向 → 区服域行键重写），
    /// 玩法参与者追加其后。
    /// </summary>
    private readonly List<IServerMergeParticipant> _participants;

    /// <summary>
    /// 归并计划存储。
    /// </summary>
    private readonly IOnlineServerMergeStore _mergeStore;

    /// <summary>
    /// 合服参数。
    /// </summary>
    private readonly OnlineServerMergeOptions _options;

    /// <summary>
    /// 能力事件发布器（可空：离线工具场景静默跳过）。
    /// </summary>
    private readonly IOnlineEventPublisher _eventPublisher;

    /// <summary>
    /// 初始化服务。
    /// </summary>
    /// <param name="mergeStore">归并计划存储。</param>
    /// <param name="participants">参与者集合（含内置与玩法参与者，按传入顺序执行）。</param>
    /// <param name="options">合服参数。</param>
    /// <param name="eventPublisher">能力事件发布器（可空）。</param>
    public OnlineServerMergeService(IOnlineServerMergeStore mergeStore, IEnumerable<IServerMergeParticipant> participants, OnlineServerMergeOptions options = null, IOnlineEventPublisher eventPublisher = null)
    {
        _mergeStore = mergeStore ?? throw new ArgumentNullException(nameof(mergeStore));
        if (participants == null)
        {
            throw new ArgumentNullException(nameof(participants));
        }

        _participants = new List<IServerMergeParticipant>(participants);
        _options = options ?? new OnlineServerMergeOptions();
        _eventPublisher = eventPublisher;
    }

    /// <summary>
    /// 追加玩法侧合服参与者（内置五步之后、按追加顺序执行；Activate 期由能力模块聚合命名注册后调用）。
    /// </summary>
    /// <param name="participants">追加的参与者集合（null 或空集合 = 无操作）。</param>
    /// <remarks>
    /// 仅约定在宿主 Activate 完成前调用（合服执行开始前聚合完毕）；实例方法线程安全（内部加锁）。
    /// </remarks>
    public void AttachParticipants(IEnumerable<IServerMergeParticipant> participants)
    {
        if (participants == null)
        {
            return;
        }

        lock (_participants)
        {
            foreach (var participant in participants)
            {
                if (participant != null)
                {
                    _participants.Add(participant);
                }
            }
        }
    }

    /// <summary>
    /// 登记归并计划（幂等：等价集合重复登记返回既有计划）。
    /// </summary>
    /// <param name="targetServerId">目标区服标识。</param>
    /// <param name="sourceServerIds">源区服标识集合。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>生效计划与计划标识。</returns>
    public async Task<(OnlineServerMergePlan Plan, string PlanId)> PlanAsync(long targetServerId, IReadOnlyList<long> sourceServerIds, CancellationToken cancellationToken = default)
    {
        var plan = OnlineServerMergePlan.Create(targetServerId, sourceServerIds, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        var outcome = await _mergeStore.TryPlanAsync(plan, cancellationToken).ConfigureAwait(false);
        return (outcome.Plan, outcome.Plan.BuildPlanId());
    }

    /// <summary>
    /// dry-run 预演（不落任何写入；报告留存为真实执行的前置凭据；计划未登记时自动登记）。
    /// </summary>
    /// <param name="targetServerId">目标区服标识。</param>
    /// <param name="sourceServerIds">源区服标识集合。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>dry-run 报告。</returns>
    public async Task<OnlineServerMergeReport> DryRunAsync(long targetServerId, IReadOnlyList<long> sourceServerIds, CancellationToken cancellationToken = default)
    {
        var (_, planId) = await PlanAsync(targetServerId, sourceServerIds, cancellationToken).ConfigureAwait(false);
        var plan = await _mergeStore.FindPlanAsync(planId, cancellationToken).ConfigureAwait(false);
        var report = await RunParticipantsAsync(plan, true, cancellationToken).ConfigureAwait(false);
        await _mergeStore.RecordDryRunReportAsync(planId, report, cancellationToken).ConfigureAwait(false);
        return report;
    }

    /// <summary>
    /// 真实执行归并（幂等：已执行计划返回既有报告回放且不再触发任何写入）。
    /// <para>
    /// 事件投递语义（at-least-once）：落定后发布 <c>online.server.merged</c> 并在存储记账发布状态；
    /// 发布失败时合服实际已完成，重试走幂等回放并补发未发布事件（<c>EventId</c> 为计划派生稳定值，
    /// 订阅方可据此去重）。
    /// </para>
    /// </summary>
    /// <param name="targetServerId">目标区服标识。</param>
    /// <param name="sourceServerIds">源区服标识集合。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>执行报告与是否为既有执行的幂等回放。</returns>
    public async Task<(OnlineServerMergeReport Report, bool AlreadyExecuted)> ExecuteAsync(long targetServerId, IReadOnlyList<long> sourceServerIds, CancellationToken cancellationToken = default)
    {
        var (_, planId) = await PlanAsync(targetServerId, sourceServerIds, cancellationToken).ConfigureAwait(false);
        var plan = await _mergeStore.FindPlanAsync(planId, cancellationToken).ConfigureAwait(false);

        var executed = await _mergeStore.FindExecutedReportAsync(planId, cancellationToken).ConfigureAwait(false);
        if (executed != null)
        {
            await PublishMergedEventIfNeededAsync(plan, planId, cancellationToken).ConfigureAwait(false);
            return (executed, true);
        }

        if (_options.RequireDryRunBeforeExecute)
        {
            var dryRun = await _mergeStore.FindDryRunReportAsync(planId, cancellationToken).ConfigureAwait(false);
            if (dryRun == null)
            {
                throw new OnlineServiceException(OnlineErrorCode.StateOperationForbidden, "Server merge plan '" + planId + "' has no dry-run report; run dry-run first.");
            }
        }

        var report = await RunParticipantsAsync(plan, false, cancellationToken).ConfigureAwait(false);
        await _mergeStore.MarkExecutedAsync(planId, report, cancellationToken).ConfigureAwait(false);

        await PublishMergedEventIfNeededAsync(plan, planId, cancellationToken).ConfigureAwait(false);

        return (report, false);
    }

    /// <summary>
    /// 补发未发布的合服完成事件并记账（无发布器或已发布 = 无操作；发布失败异常上抛，重试回放时再补发）。
    /// </summary>
    /// <param name="plan">归并计划。</param>
    /// <param name="planId">计划标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>完成通知。</returns>
    private async Task PublishMergedEventIfNeededAsync(OnlineServerMergePlan plan, string planId, CancellationToken cancellationToken)
    {
        if (_eventPublisher == null || await _mergeStore.IsMergedEventPublishedAsync(planId, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        await _eventPublisher.PublishAsync(OnlineServerLifecycleEvents.CreateServerMerged(plan.TargetServerId, plan.SourceServerIds, planId), cancellationToken).ConfigureAwait(false);
        await _mergeStore.MarkMergedEventPublishedAsync(planId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 编排全部参与者（dry-run 与真实执行共用；结束后校验全局域零操作铁律）。
    /// </summary>
    /// <param name="plan">归并计划。</param>
    /// <param name="dryRun">是否 dry-run 预演。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>聚合报告。</returns>
    private async Task<OnlineServerMergeReport> RunParticipantsAsync(OnlineServerMergePlan plan, bool dryRun, CancellationToken cancellationToken)
    {
        var context = new OnlineServerMergeContext(plan, dryRun, OnlineRowKey.RowKeySchemaVersion);
        var steps = new List<OnlineServerMergeStepReport>();
        IServerMergeParticipant[] participantsSnapshot;
        lock (_participants)
        {
            participantsSnapshot = _participants.ToArray();
        }

        foreach (var participant in participantsSnapshot)
        {
            steps.Add(await participant.MergeAsync(context, cancellationToken).ConfigureAwait(false));
        }

        if (context.GlobalDomainRowsTouched != 0)
        {
            throw new OnlineServiceException(OnlineErrorCode.StateOperationForbidden, "Server merge participant touched " + context.GlobalDomainRowsTouched + " global-domain rows; global domain requires zero merge operations (C462 row-key rule).");
        }

        return new OnlineServerMergeReport
        {
            PlanId = plan.BuildPlanId(),
            TargetServerId = plan.TargetServerId,
            SourceServerIds = new List<long>(plan.SourceServerIds),
            DryRun = dryRun,
            ReportedAtUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            RowKeySchemaVersion = OnlineRowKey.RowKeySchemaVersion,
            Steps = steps,
            GlobalDomainRowsTouched = 0,
        };
    }
}
