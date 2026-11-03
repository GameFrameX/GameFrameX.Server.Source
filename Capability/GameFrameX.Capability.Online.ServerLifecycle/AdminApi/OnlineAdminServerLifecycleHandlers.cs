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

namespace GameFrameX.Capability.Online.ServerLifecycle.AdminApi;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Hosting.AdminApi;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Scope;
using GameFrameX.Capability.Online.ServerLifecycle.ServerMerge;

/// <summary>
/// 区服生命周期 admin handlers（开服登记 / 日历查询 / 合服计划 / 预演 / 执行）。
/// </summary>
public static class OnlineAdminServerLifecycleHandlers
{
    /// <summary>
    /// 注册全部区服生命周期 actions。
    /// </summary>
    /// <param name="registry">动作注册表。</param>
    /// <param name="calendar">开服日历服务。</param>
    /// <param name="mergeService">合服服务。</param>
    public static void RegisterServerLifecycleActions(AdminActionRegistry registry, OnlineServerLifecycleService calendar, OnlineServerMergeService mergeService)
    {
        if (registry == null)
        {
            throw new ArgumentNullException(nameof(registry));
        }

        registry.Register("register_server_open", OnlineAdminApiAdapters.Wrap((request, scope, cancellationToken) => RegisterServerOpenAsync(request, cancellationToken, calendar)), true);
        registry.Register("query_server_calendar", OnlineAdminApiAdapters.Wrap((request, scope, cancellationToken) => QueryServerCalendarAsync(request, cancellationToken, calendar)), false);
        registry.Register("plan_server_merge", OnlineAdminApiAdapters.Wrap((request, scope, cancellationToken) => PlanServerMergeAsync(request, cancellationToken, mergeService)), true);
        registry.Register("dry_run_server_merge", OnlineAdminApiAdapters.Wrap((request, scope, cancellationToken) => DryRunServerMergeAsync(request, cancellationToken, mergeService)), true);
        registry.Register("execute_server_merge", OnlineAdminApiAdapters.Wrap((request, scope, cancellationToken) => ExecuteServerMergeAsync(request, cancellationToken, mergeService)), true);
    }

    /// <summary>
    /// register_server_open：登记开服锚点（幂等；首次登记发布 ServerOpened 能力事件）。
    /// </summary>
    private static async Task<object> RegisterServerOpenAsync(AdminApiRequest request, CancellationToken cancellationToken, OnlineServerLifecycleService calendar)
    {
        var serverId = RequireInt64(request, "ServerId");
        var openedAt = request.ReadNullableInt64("OpenedAtUnixMilliseconds") ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var outcome = await calendar.TryRegisterOpenAsync(serverId, openedAt, cancellationToken).ConfigureAwait(false);
        return new ServerOpenResponse
        {
            ServerId = outcome.Anchor.ServerId,
            OpenedAtUnixMilliseconds = outcome.Anchor.OpenedAtUnixMilliseconds,
            FirstRegistration = outcome.Registered,
        };
    }

    /// <summary>
    /// query_server_calendar：开服锚点 + 当前开服天数。
    /// </summary>
    private static async Task<object> QueryServerCalendarAsync(AdminApiRequest request, CancellationToken cancellationToken, OnlineServerLifecycleService calendar)
    {
        var serverId = RequireInt64(request, "ServerId");
        var now = request.ReadNullableInt64("NowUnixMilliseconds") ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var anchor = await calendar.GetAnchorAsync(serverId, cancellationToken).ConfigureAwait(false);
        return new ServerCalendarResponse
        {
            ServerId = serverId,
            Registered = anchor != null,
            OpenedAtUnixMilliseconds = anchor == null ? 0 : anchor.OpenedAtUnixMilliseconds,
            MergedIntoServerId = anchor == null ? 0 : anchor.MergedIntoServerId,
            OpenServerDay = await calendar.GetOpenServerDayAsync(serverId, now, cancellationToken).ConfigureAwait(false),
        };
    }

    /// <summary>
    /// plan_server_merge：登记归并计划（幂等；仅计划不执行）。
    /// </summary>
    private static async Task<object> PlanServerMergeAsync(AdminApiRequest request, CancellationToken cancellationToken, OnlineServerMergeService mergeService)
    {
        var (targetServerId, sourceServerIds) = ReadMergeRequest(request);
        var outcome = await mergeService.PlanAsync(targetServerId, sourceServerIds, cancellationToken).ConfigureAwait(false);
        return new MergePlanResponse { PlanId = outcome.PlanId, TargetServerId = targetServerId, SourceServerIds = new List<long>(sourceServerIds) };
    }

    /// <summary>
    /// dry_run_server_merge：dry-run 预演（零写入；报告留存为执行前置）。
    /// </summary>
    private static async Task<object> DryRunServerMergeAsync(AdminApiRequest request, CancellationToken cancellationToken, OnlineServerMergeService mergeService)
    {
        var (targetServerId, sourceServerIds) = ReadMergeRequest(request);
        var report = await mergeService.DryRunAsync(targetServerId, sourceServerIds, cancellationToken).ConfigureAwait(false);
        return MergeReportResponse.From(report, alreadyExecuted: false);
    }

    /// <summary>
    /// execute_server_merge：真实执行归并（幂等；无 dry-run 报告时拒绝）。
    /// </summary>
    private static async Task<object> ExecuteServerMergeAsync(AdminApiRequest request, CancellationToken cancellationToken, OnlineServerMergeService mergeService)
    {
        var (targetServerId, sourceServerIds) = ReadMergeRequest(request);
        var outcome = await mergeService.ExecuteAsync(targetServerId, sourceServerIds, cancellationToken).ConfigureAwait(false);
        return MergeReportResponse.From(outcome.Report, outcome.AlreadyExecuted);
    }

    /// <summary>
    /// 读取合服请求参数（TargetServerId + SourceServerIds 逗号分隔串）。
    /// </summary>
    private static (long TargetServerId, IReadOnlyList<long> SourceServerIds) ReadMergeRequest(AdminApiRequest request)
    {
        var targetServerId = RequireInt64(request, "TargetServerId");
        var rawSources = OnlineAdminApiContract.RequireString(request, "SourceServerIds");
        var sources = new List<long>();
        foreach (var segment in rawSources.Split(','))
        {
            if (!long.TryParse(segment.Trim(), out var sourceServerId))
            {
                throw new OnlineServiceException(OnlineErrorCode.ParameterInvalid, "SourceServerIds contains an invalid server id: '" + segment + "'.");
            }

            sources.Add(sourceServerId);
        }

        return (targetServerId, sources);
    }

    /// <summary>开服登记响应。</summary>
    private sealed class ServerOpenResponse
    {
        /// <summary>区服标识。</summary>
        public long ServerId { get; set; }

        /// <summary>开服时刻（UTC 毫秒）。</summary>
        public long OpenedAtUnixMilliseconds { get; set; }

        /// <summary>是否本次首次登记。</summary>
        public bool FirstRegistration { get; set; }
    }


    /// <summary>
    /// 读取必填 int64 参数（缺失 / 非法时 ParameterInvalid）。
    /// </summary>
    private static long RequireInt64(AdminApiRequest request, string name)
    {
        if (request.TryReadInt64(name, out var value))
        {
            return value;
        }

        throw new OnlineServiceException(OnlineErrorCode.ParameterInvalid, "Required parameter missing or invalid: " + name + ".");
    }
    /// <summary>日历查询响应。</summary>
    private sealed class ServerCalendarResponse
    {
        /// <summary>区服标识。</summary>
        public long ServerId { get; set; }

        /// <summary>是否已登记锚点。</summary>
        public bool Registered { get; set; }

        /// <summary>开服时刻（UTC 毫秒）。</summary>
        public long OpenedAtUnixMilliseconds { get; set; }

        /// <summary>合服归入目标（0 = 未合入）。</summary>
        public long MergedIntoServerId { get; set; }

        /// <summary>当前开服天数（未登记 = 0）。</summary>
        public int OpenServerDay { get; set; }
    }

    /// <summary>合服计划响应。</summary>
    private sealed class MergePlanResponse
    {
        /// <summary>计划标识。</summary>
        public string PlanId { get; set; }

        /// <summary>目标区服标识。</summary>
        public long TargetServerId { get; set; }

        /// <summary>源区服标识集合。</summary>
        public List<long> SourceServerIds { get; set; }
    }

    /// <summary>合服报告响应（dry-run / 执行共用）。</summary>
    private sealed class MergeReportResponse
    {
        /// <summary>计划标识。</summary>
        public string PlanId { get; set; }

        /// <summary>目标区服标识。</summary>
        public long TargetServerId { get; set; }

        /// <summary>源区服标识集合。</summary>
        public List<long> SourceServerIds { get; set; }

        /// <summary>是否 dry-run 预演。</summary>
        public bool DryRun { get; set; }

        /// <summary>是否既有执行的幂等回放。</summary>
        public bool AlreadyExecuted { get; set; }

        /// <summary>行键模式版本快照。</summary>
        public int RowKeySchemaVersion { get; set; }

        /// <summary>全局域触碰行数（恒 0 断言面）。</summary>
        public long GlobalDomainRowsTouched { get; set; }

        /// <summary>分步报告。</summary>
        public List<MergeStepView> Steps { get; set; }

        /// <summary>
        /// 由域报告构造响应。
        /// </summary>
        public static MergeReportResponse From(OnlineServerMergeReport report, bool alreadyExecuted)
        {
            var steps = new List<MergeStepView>();
            foreach (var step in report.Steps)
            {
                var details = new Dictionary<string, long>();
                foreach (var pair in step.Details)
                {
                    details[pair.Key] = pair.Value;
                }

                steps.Add(new MergeStepView { Step = step.Step, AffectedCount = step.AffectedCount, Details = details });
            }

            return new MergeReportResponse
            {
                PlanId = report.PlanId,
                TargetServerId = report.TargetServerId,
                SourceServerIds = new List<long>(report.SourceServerIds),
                DryRun = report.DryRun,
                AlreadyExecuted = alreadyExecuted,
                RowKeySchemaVersion = report.RowKeySchemaVersion,
                GlobalDomainRowsTouched = report.GlobalDomainRowsTouched,
                Steps = steps,
            };
        }
    }

    /// <summary>分步报告视图。</summary>
    private sealed class MergeStepView
    {
        /// <summary>步骤名。</summary>
        public string Step { get; set; }

        /// <summary>受影响计数。</summary>
        public long AffectedCount { get; set; }

        /// <summary>明细。</summary>
        public Dictionary<string, long> Details { get; set; }
    }
}
