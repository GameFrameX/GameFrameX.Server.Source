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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Events;
using GameFrameX.Capability.Online.Abstractions.Keys;
using GameFrameX.Capability.Online.ServerLifecycle;
using GameFrameX.Capability.Online.ServerLifecycle.ServerMerge;
using Xunit;

/// <summary>
/// 区服生命周期与合服原语回归：开服锚点幂等 / 开服天数边界 / ServerOpened 事件 /
/// dry-run 先行门禁 / 幂等回放 / 全局域零操作铁律。
/// </summary>
public sealed class OnlineServerLifecycleTests
{
    private const long DayMilliseconds = 86400000L;

    [Fact]
    public async Task TryRegisterOpen_Idempotent_ReturnsExistingAnchorAndKeepsFirstOpenTime()
    {
        var service = new OnlineServerLifecycleService(new InMemoryOnlineServerCalendarStore());
        var first = await service.TryRegisterOpenAsync(7, 1000);
        var second = await service.TryRegisterOpenAsync(7, 999999);

        Assert.True(first.Registered);
        Assert.False(second.Registered);
        Assert.Equal(1000, second.Anchor.OpenedAtUnixMilliseconds);
    }

    [Fact]
    public async Task GetOpenServerDay_Boundary_OpenDayIsDayOneAndNextDayFlipsAtExactly24Hours()
    {
        var service = new OnlineServerLifecycleService(new InMemoryOnlineServerCalendarStore());
        await service.TryRegisterOpenAsync(7, 1000);

        Assert.Equal(1, await service.GetOpenServerDayAsync(7, 1000));
        Assert.Equal(1, await service.GetOpenServerDayAsync(7, 1000 + DayMilliseconds - 1));
        Assert.Equal(2, await service.GetOpenServerDayAsync(7, 1000 + DayMilliseconds));
        Assert.Equal(10, await service.GetOpenServerDayAsync(7, 1000 + 9 * DayMilliseconds));
        Assert.Equal(0, await service.GetOpenServerDayAsync(8, 1000));
    }

    [Fact]
    public async Task TryRegisterOpen_FirstRegistration_PublishesServerOpenedOnce()
    {
        var publisher = new CollectingEventPublisher();
        var service = new OnlineServerLifecycleService(new InMemoryOnlineServerCalendarStore(), publisher);
        await service.TryRegisterOpenAsync(7, 1000);
        await service.TryRegisterOpenAsync(7, 2000);

        var opened = publisher.Events.Where(e => e.EventType == OnlineServerLifecycleEvents.ServerOpened).ToList();
        Assert.Single(opened);
        Assert.Equal(7, opened[0].ServerId);
        Assert.Equal("1000", opened[0].PayloadAuditFields["OpenedAtUnixMilliseconds"]);
    }

    [Fact]
    public async Task Plan_SameSourceSetDifferentOrder_CanonicalPlanIdIsIdempotent()
    {
        var mergeStore = new InMemoryOnlineServerMergeStore();
        var service = BuildMergeService(mergeStore);

        var first = await service.PlanAsync(3, new long[] { 1, 2 });
        var second = await service.PlanAsync(3, new long[] { 2, 1, 2 });

        Assert.Equal(first.PlanId, second.PlanId);
        Assert.Equal(new long[] { 1, 2 }, second.Plan.SourceServerIds);
    }

    [Fact]
    public async Task Plan_TargetInSources_IsRejected()
    {
        var service = BuildMergeService(new InMemoryOnlineServerMergeStore());
        await Assert.ThrowsAsync<ArgumentException>(() => service.PlanAsync(3, new long[] { 1, 3 }));
        await Assert.ThrowsAsync<ArgumentException>(() => service.PlanAsync(3, new long[] { 0 }));
        await Assert.ThrowsAsync<ArgumentException>(() => service.PlanAsync(3, new long[0]));
    }

    [Fact]
    public async Task Execute_WithoutDryRun_IsRejectedByDryRunFirstGate()
    {
        var service = BuildMergeService(new InMemoryOnlineServerMergeStore());

        var exception = await Assert.ThrowsAsync<OnlineServiceException>(() => service.ExecuteAsync(3, new long[] { 1 }));
        Assert.Equal(OnlineErrorCode.StateOperationForbidden, exception.Code);
    }

    [Fact]
    public async Task DryRun_ThenExecute_SecondExecuteReplaysWithoutDoubleWrite()
    {
        var calendarStore = new InMemoryOnlineServerCalendarStore();
        await calendarStore.TryRegisterAsync(new OnlineServerAnchor { ServerId = 1, OpenedAtUnixMilliseconds = 1000 });
        await calendarStore.TryRegisterAsync(new OnlineServerAnchor { ServerId = 2, OpenedAtUnixMilliseconds = 2000 });
        var routeRewriter = new InMemoryOnlinePlayerRouteRewriter();
        routeRewriter.RegisterRoute(101, 1);
        routeRewriter.RegisterRoute(102, 1);
        routeRewriter.RegisterRoute(201, 2);
        routeRewriter.RegisterRoute(301, 9);
        var mergeStore = new InMemoryOnlineServerMergeStore();
        var service = BuildMergeService(mergeStore, calendarStore, routeRewriter);

        var dryRun = await service.DryRunAsync(3, new long[] { 1, 2 });
        Assert.True(dryRun.DryRun);
        Assert.Equal(0, dryRun.GlobalDomainRowsTouched);
        Assert.Equal(3, dryRun.Steps.First(step => step.Step == "player-route:rewrite").AffectedCount);
        Assert.Equal(0, routeRewriter.FindRoute(101) != 3 ? 0 : 1); // dry-run 零写入：路由仍指向源服
        Assert.Equal(1, routeRewriter.FindRoute(101));

        var executed = await service.ExecuteAsync(3, new long[] { 1, 2 });
        Assert.False(executed.AlreadyExecuted);
        Assert.Equal(3, executed.Report.Steps.First(step => step.Step == "player-route:rewrite").AffectedCount);
        Assert.Equal(3, routeRewriter.FindRoute(101));
        var anchorOne = await calendarStore.FindAsync(1);
        Assert.Equal(3, routeRewriter.FindRoute(201));
        Assert.Equal(9, routeRewriter.FindRoute(301));

        Assert.True(anchorOne.IsMerged);
        Assert.Equal(3, anchorOne.MergedIntoServerId);

        var replay = await service.ExecuteAsync(3, new long[] { 2, 1 });
        Assert.True(replay.AlreadyExecuted);
        Assert.Equal(executed.Report.PlanId, replay.Report.PlanId);
    }

    [Fact]
    public async Task Execute_TwiceWithParticipantCounts_AnchorMarkMergedIsIdempotent()
    {
        var calendarStore = new InMemoryOnlineServerCalendarStore();
        await calendarStore.TryRegisterAsync(new OnlineServerAnchor { ServerId = 1, OpenedAtUnixMilliseconds = 1 });
        var service = BuildMergeService(new InMemoryOnlineServerMergeStore(), calendarStore);

        await service.DryRunAsync(3, new long[] { 1 });
        var first = await service.ExecuteAsync(3, new long[] { 1 });
        var anchorStep = first.Report.Steps.Single(step => step.Step == "server-anchor:mark-merged");
        Assert.Equal(1, anchorStep.AffectedCount);

        // 幂等回放：不重新执行参与者。
        var replay = await service.ExecuteAsync(3, new long[] { 1 });
        Assert.True(replay.AlreadyExecuted);
    }

    [Fact]
    public async Task Execute_ParticipantTouchedGlobalDomain_IsRefused()
    {
        var mergeStore = new InMemoryOnlineServerMergeStore();
        var participants = new List<IServerMergeParticipant> { new GlobalDomainViolatingParticipant() };
        var service = new OnlineServerMergeService(mergeStore, participants);
        // 铁律断言面：dry-run 阶段即拒绝触碰全局域（预演也不允许改写全局域行），报告不落定。
        var dryRunException = await Assert.ThrowsAsync<OnlineServiceException>(() => service.DryRunAsync(3, new long[] { 1 }));
        Assert.Equal(OnlineErrorCode.StateOperationForbidden, dryRunException.Code);
        Assert.Null(await mergeStore.FindDryRunReportAsync("merge-3-1"));
        Assert.Null(await mergeStore.FindExecutedReportAsync("merge-3-1"));
    }

    [Fact]
    public async Task MergeReport_CarriesRowKeySchemaVersionSnapshot()
    {
        var service = BuildMergeService(new InMemoryOnlineServerMergeStore());
        var report = await service.DryRunAsync(3, new long[] { 1 });
        Assert.Equal(OnlineRowKey.RowKeySchemaVersion, report.RowKeySchemaVersion);
    }

    internal static OnlineServerMergeService BuildMergeService(IOnlineServerMergeStore mergeStore, IOnlineServerCalendarStore calendarStore = null, IOnlinePlayerRouteRewriter routeRewriter = null)
    {
        calendarStore ??= new InMemoryOnlineServerCalendarStore();
        routeRewriter ??= new NullOnlinePlayerRouteRewriter();
        var participants = new List<IServerMergeParticipant>
        {
            new OnlineServerAnchorMergeParticipant(calendarStore),
            new OnlineLeaderboardSealMergeParticipant(new NullOnlineLeaderboardSealTarget()),
            new OnlineInFlightMatchSettlementMergeParticipant(new NullOnlineInFlightMatchSettler()),
            new OnlinePlayerRouteMergeParticipant(routeRewriter),
            new OnlineServerRowKeyRewriteParticipant(new NullOnlineServerMergeRowRewriter(), 500),
        };
        return new OnlineServerMergeService(mergeStore, participants);
    }

    /// <summary>
    /// 触碰全局域的违规参与者（铁律回归载体：合服服务必须拒绝落定）。
    /// </summary>
    private sealed class GlobalDomainViolatingParticipant : IServerMergeParticipant
    {
        public string Name
        {
            get { return "global-domain-violator"; }
        }

        public Task<OnlineServerMergeStepReport> MergeAsync(OnlineServerMergeContext context, CancellationToken cancellationToken)
        {
            context.RecordGlobalDomainRowsTouched(42);
            return Task.FromResult(new OnlineServerMergeStepReport("violator:touch-global-domain", 42));
        }
    }

    /// <summary>
    /// 事件收集发布器。
    /// </summary>
    private sealed class CollectingEventPublisher : IOnlineEventPublisher
    {
        public List<OnlineEvent> Events { get; } = new();

        public Task PublishAsync(OnlineEvent onlineEvent, CancellationToken cancellationToken = default)
        {
            Events.Add(onlineEvent);
            return Task.CompletedTask;
        }
    }
}
