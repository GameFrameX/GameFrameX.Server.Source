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

namespace GameFrameX.Tests.Online;

using System;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.Persistence;
using GameFrameX.Capability.Online.ServerLifecycle.ServerMerge;
using GameFrameX.DataBase;
using GameFrameX.DataBase.Abstractions;
using GameFrameX.DataBase.Sqlite;
using Xunit;

/// <summary>
/// GameDb 合服计划存储持久化回归（PR #974 意见 #4 修复载体）：计划幂等登记 / dry-run 报告留存 /
/// 执行落定 / 事件发布记账在全新存储实例上可回读（跨运行生命周期语义）。
/// </summary>
[Collection("GameDbStaticStateCollection")]
public sealed class GameDbOnlineServerMergeStoreTests
{
    /// <summary>
    /// SQLite 内存库门面（静态门面串行集合保证独占）。
    /// </summary>
    private static async Task WithSqliteFacadeAsync(Func<Task> action, string name)
    {
        var opened = await GameDb.InitAsync<SqliteDbService>(new DbOptions
        {
            ConnectionString = "Data Source=:memory:",
            Name = name,
        });
        Assert.True(opened);
        try
        {
            await action();
        }
        finally
        {
            await GameDb.CloseAsync();
            GameDb.ResetForTesting();
        }
    }

    [Fact]
    public async Task PlanAndReports_SurviveFreshStoreInstance()
    {
        await WithSqliteFacadeAsync(async () =>
        {
            var plan = OnlineServerMergePlan.Create(3, new long[] { 1, 2 }, 1000);
            var firstOutcome = await new GameDbOnlineServerMergeStore().TryPlanAsync(plan);
            Assert.True(firstOutcome.Planned);

            // 等价集合重复登记：幂等返回既有计划。
            var equivalentPlan = OnlineServerMergePlan.Create(3, new long[] { 2, 1 }, 9999);
            var secondOutcome = await new GameDbOnlineServerMergeStore().TryPlanAsync(equivalentPlan);
            Assert.False(secondOutcome.Planned);
            Assert.Equal(firstOutcome.Plan.BuildPlanId(), secondOutcome.Plan.BuildPlanId());

            var planId = firstOutcome.Plan.BuildPlanId();
            var dryRunReport = new OnlineServerMergeReport
            {
                PlanId = planId,
                TargetServerId = 3,
                SourceServerIds = new System.Collections.Generic.List<long> { 1, 2 },
                DryRun = true,
                RowKeySchemaVersion = 1,
                Steps = new System.Collections.Generic.List<OnlineServerMergeStepReport> { new OnlineServerMergeStepReport("player-route:rewrite", 2) },
            };
            await new GameDbOnlineServerMergeStore().RecordDryRunReportAsync(planId, dryRunReport);

            // 全新存储实例（模拟另一进程）回读：计划与 dry-run 报告均在。
            var freshStore = new GameDbOnlineServerMergeStore();
            var loadedPlan = await freshStore.FindPlanAsync(planId);
            Assert.Equal(new long[] { 1, 2 }, loadedPlan.SourceServerIds);
            var loadedDryRun = await freshStore.FindDryRunReportAsync(planId);
            Assert.True(loadedDryRun.DryRun);
            Assert.Equal(2, loadedDryRun.Steps[0].AffectedCount);

            // 执行落定与事件发布记账：默认未发布；标记后新实例可见。
            Assert.False(await freshStore.IsMergedEventPublishedAsync(planId));
            var executedReport = new OnlineServerMergeReport { PlanId = planId, TargetServerId = 3, DryRun = false };
            await freshStore.MarkExecutedAsync(planId, executedReport);
            await freshStore.MarkMergedEventPublishedAsync(planId);

            var anotherFreshStore = new GameDbOnlineServerMergeStore();
            Assert.NotNull(await anotherFreshStore.FindExecutedReportAsync(planId));
            Assert.True(await anotherFreshStore.IsMergedEventPublishedAsync(planId));
        }, "c472-merge-store-persistence-tests");
    }

    [Fact]
    public async Task RecordDryRun_WithoutRegisteredPlan_Throws()
    {
        await WithSqliteFacadeAsync(async () =>
        {
            var store = new GameDbOnlineServerMergeStore();
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.RecordDryRunReportAsync("merge-9-1", new OnlineServerMergeReport { PlanId = "merge-9-1" }, CancellationToken.None));
        }, "c472-merge-store-unregistered-plan-tests");
    }
}
