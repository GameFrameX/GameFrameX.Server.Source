// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the PRC and related international regulations.
//   使用本项目须严格遵守相关法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file at the root of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or to infringe upon the legal rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development,
//   本组织与贡献者概不承担任何责任。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://www.cnb.cool/GameFrameX
//   CNB Repository: https://www.cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Hosting.Core;
using GameFrameX.Capability.Online.Abstractions;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Events;
using GameFrameX.Capability.Online.Abstractions.Scope;
using GameFrameX.Capability.Online.Match;
using GameFrameX.Capability.Online.Matchmaking;
using Xunit;

namespace GameFrameX.Tests.Online;

/// <summary>
/// 对局能力撮合通知生命周期测试（C407 / GFX-1105）：订阅回调只入队——发布事件不得同步解析分配
///（旧实现在同步总线回调内 <c>GetAwaiter().GetResult()</c> 两处，存在 SynchronizationContext 的环境死锁）；
/// 解析与推送由 match-assignment-notification-tick 任务异步完成；停用收口 = 关闭入队 + 退订 + 清空待处理 +
/// 等待在途，返回后旧模块零通知（热更后旧桥接回调不再触发）。
/// <para>
/// 观测面：<see cref="OnlineMatchDomainService.RegistrationCount"/>（分配登记表条目数）——
/// 解析成功的唯一副作用；测试宿主内无在线会话，推送路径走离线静默跳过（等待方经状态轮询兜底）。
/// </para>
/// </summary>
[Collection(CapabilityRuntimeCollection.Name)]
public sealed class OnlineMatchCapabilityLifecycleTests
{
    /// <summary>
    /// 静默调度间隔（毫秒）：tick job 一律由用例显式驱动，后台循环在用例时长内不触发。
    /// </summary>
    private const int QuietSchedulerIntervalMilliseconds = 3600000;

    /// <summary>玩家一。</summary>
    private const long PlayerOne = 2101;

    /// <summary>玩家二。</summary>
    private const long PlayerTwo = 2102;

    /// <summary>玩家三。</summary>
    private const long PlayerThree = 2103;

    /// <summary>玩家四。</summary>
    private const long PlayerFour = 2104;

    /// <summary>批量上限回归的分配数（= 单轮上限 64 + 溢出 1）。</summary>
    private const int BatchedAssignmentCount = 65;

    /// <summary>
    /// 发布不同步解析：事件发布后登记表不变（旧实现同步解析立即登记——本断言对旧实现确定性红），
    /// 下一轮 tick 才解析登记（通知时点延后一个调度节拍）。
    /// </summary>
    [Fact]
    public async Task AssignmentEvent_PublishOnly_DoesNotResolveSynchronously_ResolvesOnNextTick()
    {
        await OnlineCapabilityTestHarness.WithRuntimeAsync(async runtime =>
        {
            await runtime.Host.StartAsync();
            var domain = DomainService(runtime);
            var assignment = await CommitAssignmentAsync(runtime, PlayerOne, PlayerTwo);
            Assert.Equal(0, domain.RegistrationCount);

            PublishAssignmentCreated(runtime, assignment);
            Assert.Equal(0, domain.RegistrationCount);

            await runtime.Host.Scheduler.TickAsync();
            Assert.Equal(1, domain.RegistrationCount);
        }, configure: QuietScheduler);
    }

    /// <summary>
    /// 真实撮合链路端到端：两张票据入队 → 单轮 tick 内协调器成组提交并发布分配事件 →
    /// 同轮 match-assignment-notification-tick 任务解析登记（拓扑序：online-matchmaking 先于 online-match 装配，
    /// 通知 job 恒在协调器 job 之后执行）。
    /// </summary>
    [Fact]
    public async Task RealMatchmakingFlow_AssignmentResolvedWithinSameTickRound()
    {
        await OnlineCapabilityTestHarness.WithRuntimeAsync(async runtime =>
        {
            await runtime.Host.StartAsync();
            var domain = DomainService(runtime);
            Assert.True((await domain.EnqueueAsync(PlayerScope(PlayerOne), 1, 1000)).IsSuccess);
            Assert.True((await domain.EnqueueAsync(PlayerScope(PlayerTwo), 1, 1050)).IsSuccess);
            await runtime.Host.Scheduler.TickAsync();

            // 计数断言先于任何状态查询：GetStatusAsync 自身会经 ResolveMatchedNumericAsync 登记分配，
            // 后置断言无法证伪「通知 job 未在当轮解析」的装配序回归。
            Assert.Equal(1, domain.RegistrationCount);

            var firstStatus = await domain.GetStatusAsync(PlayerScope(PlayerOne));
            var secondStatus = await domain.GetStatusAsync(PlayerScope(PlayerTwo));
            Assert.True(firstStatus.IsSuccess, firstStatus.Message);
            Assert.True(secondStatus.IsSuccess, secondStatus.Message);
            Assert.True(firstStatus.Data.Matched);
            Assert.True(secondStatus.Data.Matched);
        }, configure: QuietScheduler);
    }

    /// <summary>
    /// 停用退订：模块停用后再发布的分配事件被总线丢弃（旧实现停用为 no-op、订阅仍在——
    /// 发布即同步解析登记，本断言对旧实现确定性红），tick 后登记表不增。
    /// </summary>
    [Fact]
    public async Task DeactivateAsync_Unsubscribes_LaterAssignmentEventsAreIgnored()
    {
        await OnlineCapabilityTestHarness.WithRuntimeAsync(async runtime =>
        {
            await runtime.Host.StartAsync();
            var domain = DomainService(runtime);
            var module = MatchModule(runtime);
            var firstAssignment = await CommitAssignmentAsync(runtime, PlayerOne, PlayerTwo);
            PublishAssignmentCreated(runtime, firstAssignment);
            await runtime.Host.Scheduler.TickAsync();
            Assert.Equal(1, domain.RegistrationCount);

            var secondAssignment = await CommitAssignmentAsync(runtime, PlayerThree, PlayerFour);
            await module.DeactivateAsync(CancellationToken.None);
            PublishAssignmentCreated(runtime, secondAssignment);
            await runtime.Host.Scheduler.TickAsync();

            Assert.Equal(1, domain.RegistrationCount);
        }, configure: QuietScheduler);
    }

    /// <summary>
    /// 停用清空待处理：发布后（仅入队未处理）立即停用——队列清空、退订完成，
    /// 后续 tick 不再补处理（旧实现发布即同步解析——发布后登记表断言对旧实现确定性红）。
    /// </summary>
    [Fact]
    public async Task DeactivateAsync_ClearsPendingNotifications_PublishedBeforeDeactivate()
    {
        await OnlineCapabilityTestHarness.WithRuntimeAsync(async runtime =>
        {
            await runtime.Host.StartAsync();
            var domain = DomainService(runtime);
            var module = MatchModule(runtime);
            var assignment = await CommitAssignmentAsync(runtime, PlayerOne, PlayerTwo);

            PublishAssignmentCreated(runtime, assignment);
            Assert.Equal(0, domain.RegistrationCount);

            await module.DeactivateAsync(CancellationToken.None);
            await runtime.Host.Scheduler.TickAsync();

            Assert.Equal(0, domain.RegistrationCount);
        }, configure: QuietScheduler);
    }

    /// <summary>
    /// 停用幂等：连续两次停用均正常完成（句柄已释放 / 队列已清空为无操作），不抛异常；
    /// 且首次停用后晚到的分配事件回调被入队闸丢弃，二次停用未重开闸门——
    /// 后续 tick 不再产生任何分配登记（对「重复停用意外重开入队闸」确定性红）。
    /// </summary>
    [Fact]
    public async Task DeactivateAsync_RepeatedCalls_AreIdempotent()
    {
        await OnlineCapabilityTestHarness.WithRuntimeAsync(async runtime =>
        {
            await runtime.Host.StartAsync();
            var domain = DomainService(runtime);
            var module = MatchModule(runtime);
            var assignment = await CommitAssignmentAsync(runtime, PlayerOne, PlayerTwo);

            await module.DeactivateAsync(CancellationToken.None);
            PublishAssignmentCreated(runtime, assignment);
            await module.DeactivateAsync(CancellationToken.None);
            await runtime.Host.Scheduler.TickAsync();

            Assert.Equal(0, domain.RegistrationCount);
        }, configure: QuietScheduler);
    }

    /// <summary>
    /// 匹配能力缺席态：排除 online-matchmaking 后域服务不装配、订阅与通知 job 均不注册，
    /// 停用收口走无订阅路径正常完成，tick 轮照常执行。
    /// </summary>
    [Fact]
    public async Task DeactivateAsync_WithoutMatchmaking_CompletesWithoutSubscription()
    {
        await OnlineCapabilityTestHarness.WithRuntimeAsync(async runtime =>
        {
            await runtime.Host.StartAsync();
            Assert.Null(runtime.Host.Services.GetOptionalService<IOnlineMatchDomain>());
            var module = MatchModule(runtime);

            await module.DeactivateAsync(CancellationToken.None);
            await runtime.Host.Scheduler.TickAsync();
        }, configure: options =>
        {
            QuietScheduler(options);
            options.Filter = new CapabilityAssemblyFilter(null, new[] { "online-matchmaking" });
        });
    }

    /// <summary>
    /// 条目级容错：队列中不可解析条目（分配不存在 → ResourceNotFound 静默跳过）不阻断
    /// 同批其余通知；单轮 tick 后可解析条目全部登记且调度轮正常完成。
    /// </summary>
    [Fact]
    public async Task NotifyJob_SkipsUnresolvableEntry_ProcessesRemainderOfBatch()
    {
        await OnlineCapabilityTestHarness.WithRuntimeAsync(async runtime =>
        {
            await runtime.Host.StartAsync();
            var domain = DomainService(runtime);
            var firstAssignment = await CommitAssignmentAsync(runtime, PlayerOne, PlayerTwo);
            var secondAssignment = await CommitAssignmentAsync(runtime, PlayerThree, PlayerFour);

            PublishAssignmentCreated(runtime, firstAssignment);
            runtime.Host.EventBus.Publish<OnlineEvent>(
                OnlineMatchEvents.AssignmentCreated,
                OnlineMatchEvents.CreateAssignmentCreated(OnlineCapabilityTestHarness.TenantId, OnlineCapabilityTestHarness.AppId, OnlineCapabilityTestHarness.ServerId, "asg-does-not-exist", "match-does-not-exist", 1, 1, 2, PlayerOne, new List<string>()));
            PublishAssignmentCreated(runtime, secondAssignment);
            Assert.Equal(0, domain.RegistrationCount);

            await runtime.Host.Scheduler.TickAsync();

            Assert.Equal(2, domain.RegistrationCount);
        }, configure: QuietScheduler);
    }

    /// <summary>
    /// 单轮有限批次：65 条待处理通知一轮只消费 64 条（上限常量），剩余 1 条下一轮续处理——
    /// 坏消息风暴不得独占当轮调度。
    /// </summary>
    [Fact]
    public async Task NotifyJob_LimitsBatchPerRound_RemainingProcessedNextRound()
    {
        await OnlineCapabilityTestHarness.WithRuntimeAsync(async runtime =>
        {
            await runtime.Host.StartAsync();
            var domain = DomainService(runtime);
            for (var i = 0; i < BatchedAssignmentCount; i++)
            {
                var assignment = await CommitAssignmentAsync(runtime, 300000 + (i * 2), 300001 + (i * 2));
                PublishAssignmentCreated(runtime, assignment);
            }

            Assert.Equal(0, domain.RegistrationCount);
            await runtime.Host.Scheduler.TickAsync();
            Assert.Equal(64, domain.RegistrationCount);

            await runtime.Host.Scheduler.TickAsync();
            Assert.Equal(BatchedAssignmentCount, domain.RegistrationCount);
        }, configure: QuietScheduler);
    }

    /// <summary>
    /// 构造玩家作用域（三元组与族配置段一致）。
    /// </summary>
    /// <param name="playerId">玩家标识。</param>
    /// <returns>玩家作用域。</returns>
    private static OnlineScope PlayerScope(long playerId)
    {
        return new OnlineScope(OnlineCapabilityTestHarness.TenantId, OnlineCapabilityTestHarness.AppId, OnlineCapabilityTestHarness.ServerId, playerId);
    }

    /// <summary>
    /// 静默后台调度（用例显式驱动 tick 语义）。
    /// </summary>
    /// <param name="options">组合选项。</param>
    private static void QuietScheduler(CapabilityCompositionOptions options)
    {
        options.HostOptions.SchedulerIntervalMilliseconds = QuietSchedulerIntervalMilliseconds;
    }

    /// <summary>
    /// 取宿主内的对局能力模块实例。
    /// </summary>
    /// <param name="runtime">组合产物。</param>
    /// <returns>对局能力模块。</returns>
    private static OnlineMatchCapabilityModule MatchModule(CapabilityRuntime runtime)
    {
        return runtime.Host.Modules.OfType<OnlineMatchCapabilityModule>().Single();
    }

    /// <summary>
    /// 取共享面上注册的匹配域服务具体实例（登记表计数为本组观测面）。
    /// </summary>
    /// <param name="runtime">组合产物。</param>
    /// <returns>匹配域服务。</returns>
    private static OnlineMatchDomainService DomainService(CapabilityRuntime runtime)
    {
        return (OnlineMatchDomainService)runtime.Host.Services.GetService<IOnlineMatchDomain>();
    }

    /// <summary>
    /// 伪造一条已落档的撮合分配：两名玩家经域服务入队（票据 Queued），手工提交分配
    ///（与协调器 <c>CommitMatchAsync</c> 同入口、不经撮合成组），供用例直接控制
    /// 「分配已存在而未被解析」的前置态。
    /// </summary>
    /// <param name="runtime">组合产物。</param>
    /// <param name="firstPlayerId">玩家一。</param>
    /// <param name="secondPlayerId">玩家二。</param>
    /// <returns>已落档的分配。</returns>
    private static async Task<OnlineMatchAssignment> CommitAssignmentAsync(CapabilityRuntime runtime, long firstPlayerId, long secondPlayerId)
    {
        var domain = DomainService(runtime);
        var store = runtime.Host.Services.GetService<IOnlineMatchTicketStore>();
        var ticketIds = new List<string>();
        foreach (var playerId in new[] { firstPlayerId, secondPlayerId })
        {
            var enqueue = await domain.EnqueueAsync(PlayerScope(playerId), 1, 1000);
            Assert.True(enqueue.IsSuccess, enqueue.Message);
            var ticket = await store.FindLatestByPlayerAsync(OnlineCapabilityTestHarness.TenantId, OnlineCapabilityTestHarness.AppId, playerId);
            Assert.NotNull(ticket);
            ticketIds.Add(ticket.TicketId);
        }

        var assignment = new OnlineMatchAssignment
        {
            AssignmentId = "asg-" + Guid.NewGuid().ToString("N"),
            MatchId = "match-" + Guid.NewGuid().ToString("N"),
            PlayerIds = new List<long> { firstPlayerId, secondPlayerId },
            Mode = 1,
            Region = 1,
            CreatedAtTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            TicketIds = ticketIds,
            TenantId = OnlineCapabilityTestHarness.TenantId,
            AppId = OnlineCapabilityTestHarness.AppId,
            ServerId = OnlineCapabilityTestHarness.ServerId,
        };
        var committed = await store.CommitMatchAsync(assignment, ticketIds, OnlineMatchTicketState.Queued);
        Assert.NotNull(committed);
        return committed;
    }

    /// <summary>
    /// 以与协调器相同的载荷工厂发布分配事件（测试线程直接发布——不经调度器）。
    /// </summary>
    /// <param name="runtime">组合产物。</param>
    /// <param name="assignment">已落档的分配。</param>
    private static void PublishAssignmentCreated(CapabilityRuntime runtime, OnlineMatchAssignment assignment)
    {
        runtime.Host.EventBus.Publish<OnlineEvent>(
            OnlineMatchEvents.AssignmentCreated,
            OnlineMatchEvents.CreateAssignmentCreated(OnlineCapabilityTestHarness.TenantId, OnlineCapabilityTestHarness.AppId, OnlineCapabilityTestHarness.ServerId, assignment.AssignmentId, assignment.MatchId, assignment.Mode, assignment.Region, assignment.PlayerIds.Count, assignment.PlayerIds[0], assignment.TicketIds));
    }
}
