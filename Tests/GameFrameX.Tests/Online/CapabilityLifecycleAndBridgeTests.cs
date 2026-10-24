// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the PRC and relevant international regulations.
//   使用本项目须严格遵守相关法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please refer to the LICENSE file at the root of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe the legal rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project,
//   本组织与贡献者概不承担任何责任。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository:  https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:   https://gitee.com/GameFrameX
//   CNB  仓库：https://www.cnb.cool/GameFrameX
//   CNB Repository:     https://www.cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Docs:      https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Hosting.Core;
using GameFrameX.Foundation.Localization.Core;
using Xunit;
using LocalizationKeys = GameFrameX.Localization.Keys;

namespace GameFrameX.Tests.Online;

/// <summary>
/// 能力宿主生命周期与能力桥测试（设计 §6.4：激活中途失败逆序回滚并撤桥、停止期逆序停用并自动撤桥、
/// 撤桥后旧租约显式失败、同契约重复发布 fail-fast）。
/// </summary>
[Collection(CapabilityRuntimeCollection.Name)]
public sealed class CapabilityLifecycleAndBridgeTests
{
    /// <summary>回滚用例：槽位一发布的桥契约。</summary>
    public interface IRollbackFirstBridge
    {
        /// <summary>发布者标记。</summary>
        string Owner { get; }
    }

    /// <summary>回滚用例：槽位二发布的桥契约。</summary>
    public interface IRollbackSecondBridge
    {
        /// <summary>发布者标记。</summary>
        string Owner { get; }
    }

    /// <summary>回滚用例：激活失败的槽位三在抛出前发布的桥契约。</summary>
    public interface IRollbackThirdBridge
    {
        /// <summary>发布者标记。</summary>
        string Owner { get; }
    }

    /// <summary>撤桥用例：停止后旧租约显式失败的桥契约。</summary>
    public interface IRetractedLeaseBridge
    {
        /// <summary>发布者标记。</summary>
        string Owner { get; }
    }

    /// <summary>停止用例：槽位一发布的桥契约。</summary>
    public interface IStopFirstBridge
    {
        /// <summary>发布者标记。</summary>
        string Owner { get; }
    }

    /// <summary>停止用例：槽位二发布的桥契约。</summary>
    public interface IStopSecondBridge
    {
        /// <summary>发布者标记。</summary>
        string Owner { get; }
    }

    /// <summary>重复发布用例：两个槽位争抢发布的同一桥契约。</summary>
    public interface IDuplicatePublishBridge
    {
        /// <summary>发布者标记。</summary>
        string Owner { get; }
    }

    /// <summary>串行化用例：启动进行中被停止的模块发布的桥契约。</summary>
    public interface ISerializedStopBridge
    {
        /// <summary>发布者标记。</summary>
        string Owner { get; }
    }

    /// <summary>调度器启动失败回滚用例：已激活模块发布的桥契约。</summary>
    public interface ISchedulerRollbackBridge
    {
        /// <summary>发布者标记。</summary>
        string Owner { get; }
    }

    /// <summary>
    /// 桥契约测试实现（同时实现全部测试契约，按发布者标记区分实例）。
    /// </summary>
    private sealed class BridgeImplementation : IRollbackFirstBridge, IRollbackSecondBridge, IRollbackThirdBridge, IRetractedLeaseBridge, IStopFirstBridge, IStopSecondBridge, IDuplicatePublishBridge, ISerializedStopBridge, ISchedulerRollbackBridge
    {
        /// <summary>
        /// 初始化桥实现。
        /// </summary>
        /// <param name="owner">发布者标记。</param>
        public BridgeImplementation(string owner)
        {
            Owner = owner;
        }

        /// <inheritdoc cref="IRollbackFirstBridge.Owner" />
        public string Owner { get; }
    }

    /// <summary>
    /// 构造机制组合选项（合法作用域三元组 + 白名单圈定参与面）。
    /// </summary>
    /// <param name="log">装配日志收集器。</param>
    /// <param name="enabledIds">白名单。</param>
    /// <returns>组合选项。</returns>
    private static CapabilityCompositionOptions CreateOptions(CollectingCapabilityAssemblyLog log, string[] enabledIds)
    {
        return new CapabilityCompositionOptions(default, log)
        {
            HostOptions = new CapabilityHostOptions { TenantId = 1, AppId = 1, ServerId = 1, },
            Filter = new CapabilityAssemblyFilter(enabledIds),
        };
    }

    /// <summary>
    /// 以机制测试族执行一次真实组合。
    /// </summary>
    /// <param name="options">组合选项。</param>
    /// <returns>组合产物。</returns>
    private static CapabilityRuntime Compose(CapabilityCompositionOptions options)
    {
        return CapabilityComposition.Compose(new MechanismTestFamily(), options);
    }

    /// <summary>
    /// 激活中途失败：First ← Second ← Third 链式依赖，Third 在发布自己的桥后抛出——
    /// 失败的 Third 自身先 Deactivate 一次，已激活的 Second、First 再按逆序 Deactivate，三个模块发布的桥全部撤销
    /// （TryAcquire 为 false），StartAsync 重抛原异常，并记一条激活失败回滚 Error。
    /// </summary>
    [Fact]
    public async Task StartAsync_ActivationFailsMidway_RollsBackInReverseAndRetractsBridges()
    {
        MechanismScenario.Reset();
        CapabilityRuntime runtime = null;
        try
        {
            var deactivated = new List<string>();
            var activated = new List<string>();
            MechanismScenario.First.Activate = (context, cancellationToken) =>
            {
                activated.Add("mech-first");
                context.Bridges.Publish<IRollbackFirstBridge>(new BridgeImplementation("mech-first"));
                return Task.CompletedTask;
            };
            MechanismScenario.First.Deactivate = cancellationToken => deactivated.Add("mech-first");
            MechanismScenario.Second.Requires = new[] { "mech-first", };
            MechanismScenario.Second.Activate = (context, cancellationToken) =>
            {
                activated.Add("mech-second");
                context.Bridges.Publish<IRollbackSecondBridge>(new BridgeImplementation("mech-second"));
                return Task.CompletedTask;
            };
            MechanismScenario.Second.Deactivate = cancellationToken => deactivated.Add("mech-second");
            MechanismScenario.Third.Requires = new[] { "mech-second", };
            MechanismScenario.Third.Activate = (context, cancellationToken) =>
            {
                activated.Add("mech-third");
                context.Bridges.Publish<IRollbackThirdBridge>(new BridgeImplementation("mech-third"));
                throw new InvalidOperationException("mech-third activation failed");
            };
            MechanismScenario.Third.Deactivate = cancellationToken => deactivated.Add("mech-third");
            var log = new CollectingCapabilityAssemblyLog();

            runtime = Compose(CreateOptions(log, new[] { "mech-first", "mech-second", "mech-third", }));
            Assert.Equal(new[] { "mech-first", "mech-second", "mech-third", }, runtime.Report.EnabledIdsInOrder);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.StartAsync());

            Assert.Equal("mech-third activation failed", exception.Message);
            Assert.Equal(new[] { "mech-first", "mech-second", "mech-third", }, activated);
            Assert.Equal(new[] { "mech-third", "mech-second", "mech-first", }, deactivated);
            Assert.False(CapabilityBridge<IRollbackFirstBridge>.TryAcquire(out _));
            Assert.False(CapabilityBridge<IRollbackSecondBridge>.TryAcquire(out _));
            Assert.False(CapabilityBridge<IRollbackThirdBridge>.TryAcquire(out _));
            Assert.Contains(string.Format(LocalizationService.GetString(LocalizationKeys.Capability.Hosting.ActivationFailed), "mech-third", 2), log.Errors);
        }
        finally
        {
            if (runtime != null)
            {
                await runtime.StopAsync();
            }

            MechanismScenario.Reset();
        }
    }

    /// <summary>
    /// 撤桥后旧租约显式失败：启动后取得的租约可正常访问契约；宿主停止后同一租约访问
    /// <see cref="CapabilityBridgeLease{TContract}.Contract"/> 抛 <see cref="CapabilityBridgeUnavailableException"/>
    /// （不回旧数据、不降级），重新 TryAcquire 返回 false。
    /// </summary>
    [Fact]
    public async Task Lease_AfterStopAsync_ContractThrowsBridgeUnavailable()
    {
        MechanismScenario.Reset();
        CapabilityRuntime runtime = null;
        try
        {
            var implementation = new BridgeImplementation("mech-first");
            MechanismScenario.First.Activate = (context, cancellationToken) =>
            {
                context.Bridges.Publish<IRetractedLeaseBridge>(implementation);
                return Task.CompletedTask;
            };

            runtime = Compose(CreateOptions(new CollectingCapabilityAssemblyLog(), new[] { "mech-first", }));
            Assert.False(CapabilityBridge<IRetractedLeaseBridge>.TryAcquire(out _));

            await runtime.StartAsync();

            Assert.True(CapabilityBridge<IRetractedLeaseBridge>.TryAcquire(out var lease));
            Assert.Same(implementation, lease.Contract);

            await runtime.StopAsync();

            var exception = Assert.Throws<CapabilityBridgeUnavailableException>(() => lease.Contract);
            Assert.Equal(typeof(IRetractedLeaseBridge).FullName, exception.ContractTypeName);
            Assert.False(CapabilityBridge<IRetractedLeaseBridge>.TryAcquire(out var afterStop));
            Assert.Null(afterStop);
        }
        finally
        {
            if (runtime != null)
            {
                await runtime.StopAsync();
            }

            MechanismScenario.Reset();
        }
    }

    /// <summary>
    /// 停止期编排：逆拓扑序 Deactivate，机制自动撤销全部模块发布的桥（模块与组合根无撤销入口）。
    /// </summary>
    [Fact]
    public async Task StopAsync_DeactivatesInReverseOrderAndRetractsAllBridges()
    {
        MechanismScenario.Reset();
        CapabilityRuntime runtime = null;
        try
        {
            var deactivated = new List<string>();
            MechanismScenario.First.Activate = (context, cancellationToken) =>
            {
                context.Bridges.Publish<IStopFirstBridge>(new BridgeImplementation("mech-first"));
                return Task.CompletedTask;
            };
            MechanismScenario.First.Deactivate = cancellationToken => deactivated.Add("mech-first");
            MechanismScenario.Second.Requires = new[] { "mech-first", };
            MechanismScenario.Second.Activate = (context, cancellationToken) =>
            {
                context.Bridges.Publish<IStopSecondBridge>(new BridgeImplementation("mech-second"));
                return Task.CompletedTask;
            };
            MechanismScenario.Second.Deactivate = cancellationToken => deactivated.Add("mech-second");

            runtime = Compose(CreateOptions(new CollectingCapabilityAssemblyLog(), new[] { "mech-first", "mech-second", }));
            await runtime.StartAsync();

            Assert.True(CapabilityBridge<IStopFirstBridge>.TryAcquire(out var firstLease));
            Assert.Equal("mech-first", firstLease.Contract.Owner);
            Assert.True(CapabilityBridge<IStopSecondBridge>.TryAcquire(out var secondLease));
            Assert.Equal("mech-second", secondLease.Contract.Owner);
            Assert.Empty(deactivated);

            await runtime.StopAsync();

            Assert.Equal(new[] { "mech-second", "mech-first", }, deactivated);
            Assert.False(CapabilityBridge<IStopFirstBridge>.TryAcquire(out _));
            Assert.False(CapabilityBridge<IStopSecondBridge>.TryAcquire(out _));
            Assert.Throws<CapabilityBridgeUnavailableException>(() => firstLease.Contract);
            Assert.Throws<CapabilityBridgeUnavailableException>(() => secondLease.Contract);
        }
        finally
        {
            if (runtime != null)
            {
                await runtime.StopAsync();
            }

            MechanismScenario.Reset();
        }
    }

    /// <summary>
    /// 同契约重复发布 fail-fast：两个槽位在 Activate 中发布同一契约，StartAsync 抛 InvalidOperationException
    /// （消息含契约类型与前后两个发布者）；失败的后发布者自身先 Deactivate，先发布者随激活回滚 Deactivate，其桥一并撤销。
    /// </summary>
    [Fact]
    public async Task StartAsync_SameContractPublishedTwice_FailsFast()
    {
        MechanismScenario.Reset();
        CapabilityRuntime runtime = null;
        try
        {
            var deactivated = new List<string>();
            MechanismScenario.First.Activate = (context, cancellationToken) =>
            {
                context.Bridges.Publish<IDuplicatePublishBridge>(new BridgeImplementation("mech-first"));
                return Task.CompletedTask;
            };
            MechanismScenario.First.Deactivate = cancellationToken => deactivated.Add("mech-first");
            MechanismScenario.Second.Requires = new[] { "mech-first", };
            MechanismScenario.Second.Activate = (context, cancellationToken) =>
            {
                context.Bridges.Publish<IDuplicatePublishBridge>(new BridgeImplementation("mech-second"));
                return Task.CompletedTask;
            };
            MechanismScenario.Second.Deactivate = cancellationToken => deactivated.Add("mech-second");

            runtime = Compose(CreateOptions(new CollectingCapabilityAssemblyLog(), new[] { "mech-first", "mech-second", }));

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.StartAsync());

            Assert.Equal(string.Format(LocalizationService.GetString(LocalizationKeys.Capability.Hosting.BridgeDuplicatePublish), typeof(IDuplicatePublishBridge).FullName, "mech-first", "mech-second"), exception.Message);
            Assert.Equal(new[] { "mech-second", "mech-first", }, deactivated);
            Assert.False(CapabilityBridge<IDuplicatePublishBridge>.TryAcquire(out _));
        }
        finally
        {
            if (runtime != null)
            {
                await runtime.StopAsync();
            }

            MechanismScenario.Reset();
        }
    }

    /// <summary>
    /// 激活失败且失败模块自身的 Deactivate 也抛出：停用异常只记 DeactivationFailed Error，
    /// StartAsync 重抛的仍是原激活异常，已激活模块照常逆序回滚。
    /// </summary>
    [Fact]
    public async Task StartAsync_FailedModuleDeactivateThrows_OriginalExceptionPreserved()
    {
        MechanismScenario.Reset();
        CapabilityRuntime runtime = null;
        try
        {
            var deactivated = new List<string>();
            MechanismScenario.First.Deactivate = cancellationToken => deactivated.Add("mech-first");
            MechanismScenario.Second.Requires = new[] { "mech-first", };
            MechanismScenario.Second.Activate = (context, cancellationToken) => throw new InvalidOperationException("mech-second activation failed");
            MechanismScenario.Second.Deactivate = cancellationToken =>
            {
                deactivated.Add("mech-second");
                throw new InvalidOperationException("mech-second deactivation failed");
            };
            var log = new CollectingCapabilityAssemblyLog();

            runtime = Compose(CreateOptions(log, new[] { "mech-first", "mech-second", }));

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.StartAsync());

            Assert.Equal("mech-second activation failed", exception.Message);
            Assert.Equal(new[] { "mech-second", "mech-first", }, deactivated);
            Assert.Contains(string.Format(LocalizationService.GetString(LocalizationKeys.Capability.Hosting.DeactivationFailed), "mech-second", "mech-second deactivation failed"), log.Errors);
        }
        finally
        {
            if (runtime != null)
            {
                await runtime.StopAsync();
            }

            MechanismScenario.Reset();
        }
    }

    /// <summary>
    /// 启动与停止串行化：激活进行中调用 StopAsync，停止等待启动结束后才执行——
    /// 激活未放行前停止不完成；放行后模块先完成激活、再被 Deactivate，激活期发布的桥最终全部撤销（无泄漏桥）。
    /// </summary>
    [Fact]
    public async Task StopAsync_DuringStartAsync_WaitsForStartThenStops()
    {
        MechanismScenario.Reset();
        CapabilityRuntime runtime = null;
        try
        {
            var events = new List<string>();
            var activationEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseActivation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            MechanismScenario.First.Activate = async (context, cancellationToken) =>
            {
                context.Bridges.Publish<ISerializedStopBridge>(new BridgeImplementation("mech-first"));
                activationEntered.SetResult(true);
                await releaseActivation.Task;
                lock (events)
                {
                    events.Add("activated");
                }
            };
            MechanismScenario.First.Deactivate = cancellationToken =>
            {
                lock (events)
                {
                    events.Add("deactivated");
                }
            };

            runtime = Compose(CreateOptions(new CollectingCapabilityAssemblyLog(), new[] { "mech-first", }));
            var startTask = runtime.StartAsync();
            await activationEntered.Task;

            var stopTask = runtime.StopAsync();
            await Task.Delay(200);

            Assert.False(stopTask.IsCompleted);
            Assert.Empty(events);

            releaseActivation.SetResult(true);
            await startTask;
            await stopTask;

            Assert.Equal(new[] { "activated", "deactivated", }, events);
            Assert.False(CapabilityBridge<ISerializedStopBridge>.TryAcquire(out _));
        }
        finally
        {
            if (runtime != null)
            {
                await runtime.StopAsync();
            }

            MechanismScenario.Reset();
        }
    }

    /// <summary>
    /// 调度器启动失败纳入回滚：全部模块激活成功后调度器启动抛出（调度器已被停止）——
    /// 已激活模块逆序 Deactivate、桥全部撤销、记一条调度器启动失败回滚 Error，StartAsync 重抛原异常。
    /// </summary>
    [Fact]
    public async Task StartAsync_SchedulerStartFails_RollsBackActivatedModules()
    {
        MechanismScenario.Reset();
        CapabilityRuntime runtime = null;
        try
        {
            var deactivated = new List<string>();
            MechanismScenario.First.Activate = (context, cancellationToken) =>
            {
                context.Bridges.Publish<ISchedulerRollbackBridge>(new BridgeImplementation("mech-first"));
                return Task.CompletedTask;
            };
            MechanismScenario.First.Deactivate = cancellationToken => deactivated.Add("mech-first");
            MechanismScenario.Second.Requires = new[] { "mech-first", };
            MechanismScenario.Second.Deactivate = cancellationToken => deactivated.Add("mech-second");
            var log = new CollectingCapabilityAssemblyLog();

            runtime = Compose(CreateOptions(log, new[] { "mech-first", "mech-second", }));
            await runtime.Host.Scheduler.StopAsync();

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.StartAsync());

            Assert.Equal(LocalizationService.GetString(LocalizationKeys.Capability.Hosting.SchedulerRestartAfterStop), exception.Message);
            Assert.Equal(new[] { "mech-second", "mech-first", }, deactivated);
            Assert.False(CapabilityBridge<ISchedulerRollbackBridge>.TryAcquire(out _));
            Assert.Contains(string.Format(LocalizationService.GetString(LocalizationKeys.Capability.Hosting.SchedulerStartFailed), 2), log.Errors);
        }
        finally
        {
            if (runtime != null)
            {
                await runtime.StopAsync();
            }

            MechanismScenario.Reset();
        }
    }
}
