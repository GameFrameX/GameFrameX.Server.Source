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
using System.Globalization;
using System.IO;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// 合服行键重写 CLI 运行器（自托管入口；AdminApi action 为另一入口，双入口共用同一合服服务原语）。
/// </summary>
/// <remarks>
/// 用法：<c>--target=&lt;serverId&gt; --sources=&lt;id1,id2,...&gt; [--execute]</c>（名字与值以等号分隔）——缺省 dry-run 预演
/// （零写入，输出 JSON 报告）；<c>--execute</c> 真实执行（受 dry-run 先行门禁约束，幂等可重入；
/// 计划存储持久化时跨进程有效，进程内存储时报告随进程丢失）。
/// </remarks>
public static class OnlineServerMergeCli
{
    /// <summary>
    /// 运行 CLI（对注入的合服服务执行 dry-run 或真实执行；报告 JSON 输出到 <paramref name="output"/>）。
    /// </summary>
    /// <param name="args">参数（--target/--sources/--execute）。</param>
    /// <param name="mergeService">合服服务（由 CLI 宿主按部署形态装配）。</param>
    /// <param name="output">报告输出流。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>进程退出码（0 = 成功；2 = 参数错误；3 = 业务拒绝）。</returns>
    public static async Task<int> RunAsync(string[] args, OnlineServerMergeService mergeService, TextWriter output, CancellationToken cancellationToken = default)
    {
        if (mergeService == null)
        {
            throw new ArgumentNullException(nameof(mergeService));
        }

        if (output == null)
        {
            throw new ArgumentNullException(nameof(output));
        }

        var options = await ParseArgumentsAsync(args, output).ConfigureAwait(false);
        if (options == null)
        {
            return 2;
        }

        if (options.TargetServerId <= 0 || options.SourceServerIds.Count == 0)
        {
            await output.WriteLineAsync("ERROR: --target and --sources are required. Usage: --target=<serverId> --sources=<id1,id2,...> [--execute]").ConfigureAwait(false);
            return 2;
        }

        OnlineServerMergeReport report;
        bool alreadyExecuted = false;
        try
        {
            if (options.Execute)
            {
                var outcome = await mergeService.ExecuteAsync(options.TargetServerId, options.SourceServerIds, cancellationToken).ConfigureAwait(false);
                report = outcome.Report;
                alreadyExecuted = outcome.AlreadyExecuted;
            }
            else
            {
                report = await mergeService.DryRunAsync(options.TargetServerId, options.SourceServerIds, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException || exception is OperationCanceledException || exception is OnlineServiceException)
        {
            await output.WriteLineAsync("ERROR: " + exception.Message).ConfigureAwait(false);
            return 3;
        }


        var document = JsonSerializer.Serialize(new CliReport
        {
            PlanId = report.PlanId,
            TargetServerId = report.TargetServerId,
            SourceServerIds = new List<long>(report.SourceServerIds),
            DryRun = report.DryRun,
            AlreadyExecuted = alreadyExecuted,
            RowKeySchemaVersion = report.RowKeySchemaVersion,
            GlobalDomainRowsTouched = report.GlobalDomainRowsTouched,
            Steps = ToStepViews(report.Steps),
        }, new JsonSerializerOptions { WriteIndented = true });
        await output.WriteLineAsync(document).ConfigureAwait(false);
        return 0;
    }

    /// <summary>
    /// 解析 CLI 参数（--target/--sources/--execute）；非法参数输出错误并返回 null。
    /// </summary>
    private static async Task<OnlineServerMergeCliOptions> ParseArgumentsAsync(string[] args, TextWriter output)
    {
        var options = new OnlineServerMergeCliOptions();
        foreach (var arg in args ?? Array.Empty<string>())
        {
            if (arg == "--execute")
            {
                options.Execute = true;
                continue;
            }

            var separatorIndex = arg.IndexOf('=');
            if (separatorIndex <= 0)
            {
                await output.WriteLineAsync("ERROR: unknown argument '" + arg + "'. Usage: --target=<serverId> --sources=<id1,id2,...> [--execute]").ConfigureAwait(false);
                return null;
            }

            var name = arg.Substring(0, separatorIndex);
            var value = arg.Substring(separatorIndex + 1);
            if (name == "--target")
            {
                if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var targetServerId))
                {
                    await output.WriteLineAsync("ERROR: invalid --target value '" + value + "'.").ConfigureAwait(false);
                    return null;
                }

                options.TargetServerId = targetServerId;
            }
            else if (name == "--sources")
            {
                foreach (var segment in value.Split(','))
                {
                    if (!long.TryParse(segment.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var sourceServerId))
                    {
                        await output.WriteLineAsync("ERROR: invalid --sources segment '" + segment + "'.").ConfigureAwait(false);
                        return null;
                    }

                    if (sourceServerId <= 0)
                    {
                        await output.WriteLineAsync("ERROR: invalid --sources segment '" + segment + "' (server ids must be positive).").ConfigureAwait(false);
                        return null;
                    }

                    options.SourceServerIds.Add(sourceServerId);
                }
            }
            else
            {
                await output.WriteLineAsync("ERROR: unknown argument '" + arg + "'. Usage: --target=<serverId> --sources=<id1,id2,...> [--execute]").ConfigureAwait(false);
                return null;
            }
        }

        return options;
    }

    /// <summary>
    /// CLI 参数解析结果（target/sources/execute）。
    /// </summary>
    private sealed class OnlineServerMergeCliOptions
    {
        public long TargetServerId { get; set; }

        public List<long> SourceServerIds { get; } = new List<long>();

        public bool Execute { get; set; }
    }

    /// <summary>
    /// 分步报告转 CLI 视图。
    /// </summary>
    private static List<CliStepView> ToStepViews(IReadOnlyList<OnlineServerMergeStepReport> steps)
    {
        var views = new List<CliStepView>();
        foreach (var step in steps)
        {
            var details = new Dictionary<string, long>();
            foreach (var pair in step.Details)
            {
                details[pair.Key] = pair.Value;
            }

            views.Add(new CliStepView { Step = step.Step, AffectedCount = step.AffectedCount, Details = details });
        }

        return views;
    }

    /// <summary>CLI 报告视图。</summary>
    private sealed class CliReport
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
        public List<CliStepView> Steps { get; set; }
    }

    /// <summary>CLI 分步报告视图。</summary>
    private sealed class CliStepView
    {
        /// <summary>步骤名。</summary>
        public string Step { get; set; }

        /// <summary>受影响计数。</summary>
        public long AffectedCount { get; set; }

        /// <summary>明细。</summary>
        public Dictionary<string, long> Details { get; set; }
    }
}
