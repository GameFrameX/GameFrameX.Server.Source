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

/// <summary>
/// 内存实现归并计划存储（默认实现；部署侧可覆盖为持久化实现）。
/// </summary>
public sealed class InMemoryOnlineServerMergeStore : IOnlineServerMergeStore
{
    /// <summary>
    /// 存储守卫锁。
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    /// 计划表（键 = PlanId）。
    /// </summary>
    private readonly Dictionary<string, OnlineServerMergePlan> _plans = new();

    /// <summary>
    /// dry-run 报告表（键 = PlanId）。
    /// </summary>
    private readonly Dictionary<string, OnlineServerMergeReport> _dryRunReports = new();

    /// <summary>
    /// 执行报告表（键 = PlanId）。
    /// </summary>
    private readonly Dictionary<string, OnlineServerMergeReport> _executedReports = new();

    /// <summary>
    /// 合服完成事件已发布标记表（键 = PlanId；at-least-once 补发记账）。
    /// </summary>
    private readonly HashSet<string> _mergedEventPublishedPlanIds = new();

    /// <inheritdoc />
    public Task<(OnlineServerMergePlan Plan, bool Planned)> TryPlanAsync(OnlineServerMergePlan plan, CancellationToken cancellationToken = default)
    {
        if (plan == null)
        {
            throw new ArgumentNullException(nameof(plan));
        }

        lock (_lock)
        {
            var planId = plan.BuildPlanId();
            if (_plans.TryGetValue(planId, out var existing))
            {
                return Task.FromResult((existing, false));
            }

            _plans[planId] = plan;
            return Task.FromResult((plan, true));
        }
    }

    /// <inheritdoc />
    public Task<OnlineServerMergePlan> FindPlanAsync(string planId, CancellationToken cancellationToken = default)
    {
        if (planId == null)
        {
            throw new ArgumentNullException(nameof(planId));
        }

        lock (_lock)
        {
            _plans.TryGetValue(planId, out var plan);
            return Task.FromResult(plan);
        }
    }

    /// <inheritdoc />
    public Task RecordDryRunReportAsync(string planId, OnlineServerMergeReport report, CancellationToken cancellationToken = default)
    {
        if (planId == null)
        {
            throw new ArgumentNullException(nameof(planId));
        }

        if (report == null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        lock (_lock)
        {
            _dryRunReports[planId] = report;
            return Task.CompletedTask;
        }
    }

    /// <inheritdoc />
    public Task<OnlineServerMergeReport> FindDryRunReportAsync(string planId, CancellationToken cancellationToken = default)
    {
        if (planId == null)
        {
            throw new ArgumentNullException(nameof(planId));
        }

        lock (_lock)
        {
            _dryRunReports.TryGetValue(planId, out var report);
            return Task.FromResult(report);
        }
    }

    /// <inheritdoc />
    public Task<OnlineServerMergeReport> FindExecutedReportAsync(string planId, CancellationToken cancellationToken = default)
    {
        if (planId == null)
        {
            throw new ArgumentNullException(nameof(planId));
        }

        lock (_lock)
        {
            _executedReports.TryGetValue(planId, out var report);
            return Task.FromResult(report);
        }
    }

    /// <inheritdoc />
    public Task MarkExecutedAsync(string planId, OnlineServerMergeReport report, CancellationToken cancellationToken = default)
    {
        if (planId == null)
        {
            throw new ArgumentNullException(nameof(planId));
        }

        if (report == null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        lock (_lock)
        {
            _executedReports[planId] = report;
            return Task.CompletedTask;
        }
    }

    /// <inheritdoc />
    public Task<bool> IsMergedEventPublishedAsync(string planId, CancellationToken cancellationToken = default)
    {
        if (planId == null)
        {
            throw new ArgumentNullException(nameof(planId));
        }

        lock (_lock)
        {
            return Task.FromResult(_mergedEventPublishedPlanIds.Contains(planId));
        }
    }

    /// <inheritdoc />
    public Task MarkMergedEventPublishedAsync(string planId, CancellationToken cancellationToken = default)
    {
        if (planId == null)
        {
            throw new ArgumentNullException(nameof(planId));
        }

        lock (_lock)
        {
            _mergedEventPublishedPlanIds.Add(planId);
            return Task.CompletedTask;
        }
    }
}
