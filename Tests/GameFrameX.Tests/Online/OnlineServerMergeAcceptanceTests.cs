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

namespace GameFrameX.Tests.Online;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.Abstractions.Events;
using GameFrameX.Capability.Online.ServerLifecycle;
using GameFrameX.Capability.Online.ServerLifecycle.Cli;
using GameFrameX.Capability.Online.ServerLifecycle.ServerMerge;
using GameFrameX.DataBase;
using Xunit;

/// <summary>
/// N3 合服演练（acceptance 可重跑）：双服构造数据 → 开服锚点 → dry-run 报告（零写入）→ 真实执行 →
/// 路由重指向 / 锚点归并断言 → 重跑幂等回放 → 全局域零操作断言；并覆盖 CLI 入口 dry-run 出口与
/// Program.Main 双进程语义（登记 → 预演 → 跨进程执行）。
/// </summary>
[Collection("GameDbStaticStateCollection")]
public sealed class OnlineServerMergeAcceptanceTests
{
    [Fact]
    public async Task ServerMergeDrill_TwoServers_DryRunThenExecuteThenReplayAllGreen()
    {
        // —— 双服构造数据：开服锚点（服 1 早于服 2 开服）+ 玩家路由 + 榜单封档计数器。
        var calendarStore = new InMemoryOnlineServerCalendarStore();
        var publisher = new CollectingEventPublisher();
        var lifecycle = new OnlineServerLifecycleService(calendarStore, publisher);
        await lifecycle.TryRegisterOpenAsync(1, 864000000L);
        await lifecycle.TryRegisterOpenAsync(2, 2 * 864000000L);

        var routeRewriter = new InMemoryOnlinePlayerRouteRewriter();
        routeRewriter.RegisterRoute(101, 1);
        routeRewriter.RegisterRoute(102, 1);
        routeRewriter.RegisterRoute(201, 2);

        var sealTarget = new CountingSealTarget(5);
        var settler = new CountingMatchSettler(2);
        var mergeStore = new InMemoryOnlineServerMergeStore();
        var participants = new List<IServerMergeParticipant>
        {
            new OnlineServerAnchorMergeParticipant(calendarStore),
            new OnlineLeaderboardSealMergeParticipant(sealTarget),
            new OnlineInFlightMatchSettlementMergeParticipant(settler),
            new OnlinePlayerRouteMergeParticipant(routeRewriter),
            new OnlineServerRowKeyRewriteParticipant(new NullOnlineServerMergeRowRewriter(), 500),
        };
        var mergeService = new OnlineServerMergeService(mergeStore, participants, new OnlineServerMergeOptions(), publisher);

        // —— dry-run：报告先行且零写入。
        var dryRun = await mergeService.DryRunAsync(2, new long[] { 1 });
        Assert.True(dryRun.DryRun);
        Assert.Equal("merge-2-1", dryRun.PlanId);
        Assert.Equal(0, dryRun.GlobalDomainRowsTouched);
        Assert.Equal(1, StepCount(dryRun, "server-anchor:mark-merged"));
        Assert.Equal(5, StepCount(dryRun, "leaderboard:seal"));
        Assert.Equal(2, StepCount(dryRun, "match:force-settle"));
        Assert.Equal(2, StepCount(dryRun, "player-route:rewrite"));
        Assert.False(sealTarget.Sealed);
        Assert.False(settler.Settled);
        Assert.Equal(1, routeRewriter.FindRoute(101));

        // —— 真实执行。
        var executed = await mergeService.ExecuteAsync(2, new long[] { 1 });
        Assert.False(executed.AlreadyExecuted);
        Assert.Equal(0, executed.Report.GlobalDomainRowsTouched);
        Assert.True(sealTarget.Sealed);
        Assert.True(settler.Settled);
        Assert.Equal(2, routeRewriter.FindRoute(101));
        Assert.Equal(2, routeRewriter.FindRoute(102));
        Assert.Equal(2, routeRewriter.FindRoute(201));

        var sourceAnchor = await calendarStore.FindAsync(1);
        Assert.True(sourceAnchor.IsMerged);
        Assert.Equal(2, sourceAnchor.MergedIntoServerId);

        // 目标服锚点不受影响（开服时刻保留，合服不改写目标服日历）。
        var targetAnchor = await calendarStore.FindAsync(2);
        Assert.False(targetAnchor.IsMerged);
        Assert.Equal(2 * 864000000L, targetAnchor.OpenedAtUnixMilliseconds);

        // ServerOpened ×2（双服开服）+ ServerMerged ×1（合服完成）。
        Assert.Equal(2, publisher.Events.Count(e => e.EventType == OnlineServerLifecycleEvents.ServerOpened));
        Assert.Single(publisher.Events.Where(e => e.EventType == OnlineServerLifecycleEvents.ServerMerged));

        // —— 重跑：幂等回放，不再触发任何写入。
        var replay = await mergeService.ExecuteAsync(2, new long[] { 1 });
        Assert.True(replay.AlreadyExecuted);
        Assert.Equal(1, sealTarget.SealCalls);
        Assert.Equal(1, settler.SettleCalls);
    }

    [Fact]
    public async Task ServerMergeCli_DryRunMode_PrintsJsonReport()
    {
        var calendarStore = new InMemoryOnlineServerCalendarStore();
        // 锚点前置校验：dry-run 也要求源服 / 目标服锚点已登记（未登记即预演失败）。
        await calendarStore.TryRegisterAsync(new OnlineServerAnchor { ServerId = 1, OpenedAtUnixMilliseconds = 864000000L });
        await calendarStore.TryRegisterAsync(new OnlineServerAnchor { ServerId = 3, OpenedAtUnixMilliseconds = 864000000L });
        var routeRewriter = new InMemoryOnlinePlayerRouteRewriter();
        routeRewriter.RegisterRoute(101, 1);
        var mergeService = new OnlineServerMergeService(
            new InMemoryOnlineServerMergeStore(),
            new List<IServerMergeParticipant>
            {
                new OnlineServerAnchorMergeParticipant(calendarStore),
                new OnlineLeaderboardSealMergeParticipant(new NullOnlineLeaderboardSealTarget()),
                new OnlineInFlightMatchSettlementMergeParticipant(new NullOnlineInFlightMatchSettler()),
                new OnlinePlayerRouteMergeParticipant(routeRewriter),
                new OnlineServerRowKeyRewriteParticipant(new NullOnlineServerMergeRowRewriter(), 500),
            });

        using (var output = new StringWriter())
        {
            var exitCode = await OnlineServerMergeCli.RunAsync(new[] { "--target=3", "--sources=1" }, mergeService, output);

            Assert.Equal(0, exitCode);
            var report = output.ToString();
            Assert.Contains("\"PlanId\": \"merge-3-1\"", report);
            Assert.Contains("\"DryRun\": true", report);
            Assert.Contains("\"GlobalDomainRowsTouched\": 0", report);
            Assert.Equal(1, routeRewriter.FindRoute(101)); // CLI 缺省 dry-run：零写入。
        }
    }

    [Fact]
    public async Task ServerMergeCli_ExecuteWithoutPriorDryRun_ExitCodeThree()
    {
        var mergeService = new OnlineServerMergeService(new InMemoryOnlineServerMergeStore(), new List<IServerMergeParticipant>());
        using (var output = new StringWriter())
        {
            var exitCode = await OnlineServerMergeCli.RunAsync(new[] { "--target=3", "--sources=1,2", "--execute" }, mergeService, output);

            Assert.Equal(3, exitCode);
            Assert.Contains("dry-run", output.ToString());
        }
    }

    [Fact]
    public async Task ProgramMain_UnsupportedProvider_ExitCodeTwo()
    {
        var previousProvider = Environment.GetEnvironmentVariable("SERVER_MERGE_DB_PROVIDER");
        var previousConnection = Environment.GetEnvironmentVariable("SERVER_MERGE_DB_CONNECTION_STRING");
        try
        {
            Environment.SetEnvironmentVariable("SERVER_MERGE_DB_PROVIDER", "postgres");
            Environment.SetEnvironmentVariable("SERVER_MERGE_DB_CONNECTION_STRING", null);

            var exitCode = await Program.Main(new[] { "--target=3", "--sources=1" });

            Assert.Equal(2, exitCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SERVER_MERGE_DB_PROVIDER", previousProvider);
            Environment.SetEnvironmentVariable("SERVER_MERGE_DB_CONNECTION_STRING", previousConnection);
        }
    }

    [Fact]
    public async Task ProgramMain_SqliteMode_DryRunThenExecuteAcrossSeparateRuns()
    {
        // 审查验收（PR #974 意见 #4）：一次运行登记锚点 + dry-run 留存报告，另一次全新装配 --execute 成功——
        // 计划 / 报告 / 锚点均持久化，dry-run 先行门禁凭跨运行可回读的报告放行。
        var databaseFilePath = Path.Combine(Path.GetTempPath(), "c472-server-merge-cli-" + Guid.NewGuid().ToString("N") + ".db");
        var previousProvider = Environment.GetEnvironmentVariable("SERVER_MERGE_DB_PROVIDER");
        var previousConnection = Environment.GetEnvironmentVariable("SERVER_MERGE_DB_CONNECTION_STRING");
        try
        {
            Environment.SetEnvironmentVariable("SERVER_MERGE_DB_PROVIDER", "sqlite");
            Environment.SetEnvironmentVariable("SERVER_MERGE_DB_CONNECTION_STRING", "Data Source=" + databaseFilePath);

            var dryRunExitCode = await Program.Main(new[] { "--register-open=1,3", "--target=3", "--sources=1" });
            Assert.Equal(0, dryRunExitCode);

            var executeExitCode = await Program.Main(new[] { "--target=3", "--sources=1", "--execute" });
            Assert.Equal(0, executeExitCode);

            // 幂等：执行后的再次 dry-run 报告源服行数为 0（归属列已重写 / 锚点已标记）。
            var replayExitCode = await Program.Main(new[] { "--target=3", "--sources=1" });
            Assert.Equal(0, replayExitCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SERVER_MERGE_DB_PROVIDER", previousProvider);
            Environment.SetEnvironmentVariable("SERVER_MERGE_DB_CONNECTION_STRING", previousConnection);
            GameDb.ResetForTesting();
            try
            {
                File.Delete(databaseFilePath);
            }
            catch (IOException)
            {
                // 临时库清理失败不影响断言结论。
            }
        }
    }

    private static long StepCount(OnlineServerMergeReport report, string step)
    {
        return report.Steps.Single(candidate => candidate.Step == step).AffectedCount;
    }

    /// <summary>
    /// 计数榜单封档端口（演练夹具：封档条目数固定，记录调用次数验证幂等）。
    /// </summary>
    private sealed class CountingSealTarget : IOnlineLeaderboardSealTarget
    {
        private readonly long _entryCount;

        public CountingSealTarget(long entryCount)
        {
            _entryCount = entryCount;
        }

        public int SealCalls { get; private set; }

        public bool Sealed { get; private set; }

        public Task<long> SealAsync(IReadOnlyList<long> sourceServerIds, long targetServerId, bool dryRun, System.Threading.CancellationToken cancellationToken)
        {
            if (!dryRun)
            {
                SealCalls++;
                Sealed = true;
            }

            return Task.FromResult(_entryCount);
        }
    }

    /// <summary>
    /// 计数在途对局强制结算端口（演练夹具）。
    /// </summary>
    private sealed class CountingMatchSettler : IOnlineInFlightMatchSettler
    {
        private readonly long _matchCount;

        public CountingMatchSettler(long matchCount)
        {
            _matchCount = matchCount;
        }

        public int SettleCalls { get; private set; }

        public bool Settled { get; private set; }

        public Task<long> ForceSettleAsync(IReadOnlyList<long> sourceServerIds, long targetServerId, bool dryRun, System.Threading.CancellationToken cancellationToken)
        {
            if (!dryRun)
            {
                SettleCalls++;
                Settled = true;
            }

            return Task.FromResult(_matchCount);
        }
    }

    /// <summary>
    /// 事件收集发布器。
    /// </summary>
    private sealed class CollectingEventPublisher : IOnlineEventPublisher
    {
        public List<OnlineEvent> Events { get; } = new();

        public Task PublishAsync(OnlineEvent onlineEvent, System.Threading.CancellationToken cancellationToken = default)
        {
            Events.Add(onlineEvent);
            return Task.CompletedTask;
        }
    }
}
