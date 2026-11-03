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
//   or infringe upon the legal rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any disputes or liabilities arising from secondary development based on this project
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

namespace GameFrameX.Capability.Online.Persistence;

using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.ServerLifecycle.ServerMerge;
using GameFrameX.DataBase;

/// <summary>
/// 归并计划存储 GameDb 持久化实现（<see cref="OnlineServerMergePlanState"/> 单行整存整取：
/// 计划幂等登记 + dry-run 报告留存 + 执行落定 + 事件发布记账，跨进程 / 跨运行生命周期可回读——
/// CLI 一次进程 dry-run、另一次进程 <c>--execute</c> 的前置）。
/// <para>
/// 幂等语义：建行走 <c>TryAddUniqueAsync</c>（PlanId 全表唯一索引列，并发首插唯一胜出，落者回读胜者行）；
/// 报告落定 / 事件记账为单字段变更的 CAS 条件写（失配在最新行上重放本字段变更，不整行盲覆盖）。
/// </para>
/// </summary>
public sealed class GameDbOnlineServerMergeStore : IOnlineServerMergeStore
{
    /// <summary>
    /// 行 Id 共享序列名（与既有持久化行同口径，按类型隔离计数）。
    /// </summary>
    private const string RowIdSequence = "row-id";

    /// <summary>
    /// 报告 / 记账字段变更的 CAS 失配最大重试次数（在最新行上重放本字段变更）。
    /// </summary>
    private const int MarkMutationMaxAttempts = 3;

    /// <summary>
    /// 初始化存储（GameDb 须已由宿主初始化）。
    /// </summary>
    public GameDbOnlineServerMergeStore()
    {
    }

    /// <inheritdoc />
    public async Task<(OnlineServerMergePlan Plan, bool Planned)> TryPlanAsync(OnlineServerMergePlan plan, CancellationToken cancellationToken = default)
    {
        if (plan == null)
        {
            throw new ArgumentNullException(nameof(plan));
        }

        var planId = plan.BuildPlanId();
        var existing = await FindStateAsync(planId, cancellationToken).ConfigureAwait(false);
        if (existing != null)
        {
            return (DeserializePlan(existing), false);
        }

        var state = new OnlineServerMergePlanState
        {
            PlanId = planId,
            PlanJson = JsonSerializer.Serialize(plan),
        };
        state.Id = await GameDb.NextSequenceAsync<OnlineServerMergePlanState>(RowIdSequence, cancellationToken).ConfigureAwait(false);
        if (await GameDb.TryAddUniqueAsync(state, candidate => candidate.PlanId == planId, cancellationToken).ConfigureAwait(false))
        {
            return (plan, true);
        }

        // 唯一插入冲突（并发窗口落者）：回读胜者行，返回既有计划。
        var winner = await FindStateAsync(planId, cancellationToken).ConfigureAwait(false);
        if (winner == null)
        {
            throw new InvalidOperationException("Server merge plan unique-insert conflict but no existing row found: " + planId);
        }

        return (DeserializePlan(winner), false);
    }

    /// <inheritdoc />
    public async Task<OnlineServerMergePlan> FindPlanAsync(string planId, CancellationToken cancellationToken = default)
    {
        if (planId == null)
        {
            throw new ArgumentNullException(nameof(planId));
        }

        var state = await FindStateAsync(planId, cancellationToken).ConfigureAwait(false);
        return state == null ? null : DeserializePlan(state);
    }

    /// <inheritdoc />
    public Task RecordDryRunReportAsync(string planId, OnlineServerMergeReport report, CancellationToken cancellationToken = default)
    {
        if (report == null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        return MutateStateAsync(planId, nameof(RecordDryRunReportAsync), state =>
        {
            state.DryRunReportJson = JsonSerializer.Serialize(report);
            return true;
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OnlineServerMergeReport> FindDryRunReportAsync(string planId, CancellationToken cancellationToken = default)
    {
        if (planId == null)
        {
            throw new ArgumentNullException(nameof(planId));
        }

        var state = await FindStateAsync(planId, cancellationToken).ConfigureAwait(false);
        return state != null && HasPayload(state.DryRunReportJson) ? DeserializeReport(state.DryRunReportJson) : null;
    }

    /// <inheritdoc />
    public async Task<OnlineServerMergeReport> FindExecutedReportAsync(string planId, CancellationToken cancellationToken = default)
    {
        if (planId == null)
        {
            throw new ArgumentNullException(nameof(planId));
        }

        var state = await FindStateAsync(planId, cancellationToken).ConfigureAwait(false);
        return state != null && HasPayload(state.ExecutedReportJson) ? DeserializeReport(state.ExecutedReportJson) : null;
    }

    /// <inheritdoc />
    public Task MarkExecutedAsync(string planId, OnlineServerMergeReport report, CancellationToken cancellationToken = default)
    {
        if (report == null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        return MutateStateAsync(planId, nameof(MarkExecutedAsync), state =>
        {
            state.ExecutedReportJson = JsonSerializer.Serialize(report);
            return true;
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> IsMergedEventPublishedAsync(string planId, CancellationToken cancellationToken = default)
    {
        if (planId == null)
        {
            throw new ArgumentNullException(nameof(planId));
        }

        var state = await FindStateAsync(planId, cancellationToken).ConfigureAwait(false);
        return state != null && state.MergedEventPublishedAtUnixMilliseconds > 0;
    }

    /// <inheritdoc />
    public Task MarkMergedEventPublishedAsync(string planId, CancellationToken cancellationToken = default)
    {
        return MutateStateAsync(
            planId,
            nameof(MarkMergedEventPublishedAsync),
            state =>
            {
                if (state.MergedEventPublishedAtUnixMilliseconds != 0)
                {
                    return false;
                }

                state.MergedEventPublishedAtUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                return true;
            },
            cancellationToken);
    }

    /// <summary>
    /// 读-改-写单行载荷（CAS 条件写 + 失配重读重试，避免并发操作者互相整行覆盖；语义 = 在最新行上重放本字段的变更）。
    /// <para>
    /// 变更回调返回 false = 幂等无变化，直接跳过（不产生冗余条件写）。
    /// </para>
    /// </summary>
    private static async Task MutateStateAsync(string planId, string operationName, Func<OnlineServerMergePlanState, bool> applyMutation, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < MarkMutationMaxAttempts; attempt++)
        {
            var state = await RequireStateAsync(planId, operationName, cancellationToken).ConfigureAwait(false);
            if (!applyMutation(state))
            {
                return;
            }

            var written = state.RowVersion == null
                ? await GameDb.UpdateIfRowVersionIsNullAsync(state.Id, state, cancellationToken).ConfigureAwait(false)
                : await GameDb.UpdateIfRowVersionAsync(state.Id, state.RowVersion.Value, state, cancellationToken).ConfigureAwait(false);
            if (written)
            {
                return;
            }
        }

        throw new InvalidOperationException("Server merge plan row " + planId + " lost too many row-version races during " + operationName + "; retry the operation.");
    }

    /// <summary>
    /// 按 PlanId 查行（不存在返回 null）。
    /// </summary>
    private static Task<OnlineServerMergePlanState> FindStateAsync(string planId, CancellationToken cancellationToken)
    {
        return GameDb.FindAsync<OnlineServerMergePlanState>(state => state.PlanId == planId, isCreateIfNotExists: false, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// 按 PlanId 取必需行（计划未登记即抛 <see cref="InvalidOperationException"/>——报告 / 记账均以已登记计划为前提）。
    /// </summary>
    private static async Task<OnlineServerMergePlanState> RequireStateAsync(string planId, string operationName, CancellationToken cancellationToken)
    {
        if (planId == null)
        {
            throw new ArgumentNullException(nameof(planId));
        }

        var state = await FindStateAsync(planId, cancellationToken).ConfigureAwait(false);
        if (state == null)
        {
            throw new InvalidOperationException("Server merge plan not registered: " + planId + " (operation: " + operationName + ").");
        }

        return state;
    }

    /// <summary>
    /// 载荷空串判定（空 = 对应报告未留存）。
    /// </summary>
    private static bool HasPayload(string json)
    {
        return !string.IsNullOrEmpty(json);
    }

    /// <summary>
    /// 计划载荷反序列化。
    /// </summary>
    private static OnlineServerMergePlan DeserializePlan(OnlineServerMergePlanState state)
    {
        return JsonSerializer.Deserialize<OnlineServerMergePlan>(state.PlanJson);
    }

    /// <summary>
    /// 报告载荷反序列化。
    /// </summary>
    private static OnlineServerMergeReport DeserializeReport(string json)
    {
        return JsonSerializer.Deserialize<OnlineServerMergeReport>(json);
    }
}
