// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the PRC and relevant international regulations.
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
using System.Threading.Tasks;
using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Hosting.Core;
using GameFrameX.Capability.Online.Abstractions;
using Xunit;

namespace GameFrameX.Tests.Online;

/// <summary>
/// 机制事件总线退订契约测试（C407）：Subscribe 返回幂等退订句柄——释放后不再进入后续发布快照；
/// 重复释放安全；发布快照内已进入分发的回调允许当轮完成；既有语义（Activate 前拒绝发布、
/// 单订阅体异常记日志后继续分发）不回归。
/// </summary>
[Collection(CapabilityRuntimeCollection.Name)]
public sealed class CapabilityEventBusTests
{
    /// <summary>
    /// 静默调度间隔（毫秒）：本组用例不驱动任何 tick job，后台循环在用例时长内不触发。
    /// </summary>
    private const int QuietSchedulerIntervalMilliseconds = 3600000;

    /// <summary>
    /// Activate 开始前发布拒绝：未经宿主激活的总线抛 <see cref="InvalidOperationException"/>，
    /// 保证 Register 期发出的事件不会丢给后注册订阅者。
    /// </summary>
    [Fact]
    public void Publish_BeforeActivationBegun_Throws()
    {
        var bus = new CapabilityEventBus(new CollectingCapabilityAssemblyLog());
        bus.Subscribe<string>("tests.capabilityeventbus.preactivation", _ => { });

        Assert.Throws<InvalidOperationException>(() => bus.Publish("tests.capabilityeventbus.preactivation", "payload"));
    }

    /// <summary>
    /// 退订生效：句柄释放后同主题发布不再投递该订阅，其余订阅不受影响。
    /// </summary>
    [Fact]
    public async Task SubscribeDispose_StopsDelivery_ForThatSubscriptionOnly()
    {
        var deliveredFirst = 0;
        var deliveredSecond = 0;
        await OnlineCapabilityTestHarness.WithRuntimeAsync(async runtime =>
        {
            await runtime.Host.StartAsync();
            var bus = runtime.Host.EventBus;
            var first = bus.Subscribe<string>("tests.capabilityeventbus.dispose", _ => deliveredFirst++);
            bus.Subscribe<string>("tests.capabilityeventbus.dispose", _ => deliveredSecond++);

            bus.Publish("tests.capabilityeventbus.dispose", "one");
            first.Dispose();
            bus.Publish("tests.capabilityeventbus.dispose", "two");

            Assert.Equal(1, deliveredFirst);
            Assert.Equal(2, deliveredSecond);
        }, configure: QuietScheduler);
    }

    /// <summary>
    /// 退订幂等：同一句柄重复释放不得抛异常，发布仍不投递。
    /// </summary>
    [Fact]
    public async Task SubscriptionDispose_RepeatedCalls_AreIdempotent()
    {
        var delivered = 0;
        await OnlineCapabilityTestHarness.WithRuntimeAsync(async runtime =>
        {
            await runtime.Host.StartAsync();
            var bus = runtime.Host.EventBus;
            var subscription = bus.Subscribe<string>("tests.capabilityeventbus.idempotent", _ => delivered++);

            subscription.Dispose();
            subscription.Dispose();
            bus.Publish("tests.capabilityeventbus.idempotent", "one");

            Assert.Equal(0, delivered);
        }, configure: QuietScheduler);
    }

    /// <summary>
    /// 分发中退订的快照语义：订阅体在自身回调内释放自己——当轮快照内已进入分发的回调完成（恰一次），
    /// 后续发布不再投递（快照在锁内构建，释放后的条目不再进入新快照）。
    /// </summary>
    [Fact]
    public async Task Unsubscribe_FromInsideHandler_CompletesSnapshotRound_SkipsNextPublish()
    {
        var delivered = 0;
        await OnlineCapabilityTestHarness.WithRuntimeAsync(async runtime =>
        {
            await runtime.Host.StartAsync();
            var bus = runtime.Host.EventBus;
            IDisposable subscription = null;
            subscription = bus.Subscribe<string>("tests.capabilityeventbus.insidehandler", _ =>
            {
                delivered++;
                subscription.Dispose();
            });

            bus.Publish("tests.capabilityeventbus.insidehandler", "one");
            bus.Publish("tests.capabilityeventbus.insidehandler", "two");

            Assert.Equal(1, delivered);
        }, configure: QuietScheduler);
    }

    /// <summary>
    /// 订阅体异常隔离不回归：首个订阅体抛异常被总线捕获记入装配日志，当轮继续投递后续订阅。
    /// </summary>
    [Fact]
    public async Task SubscriberException_IsLogged_DispatchContinues()
    {
        var options = OnlineCapabilityTestHarness.CreateOptions();
        options.HostOptions.SchedulerIntervalMilliseconds = QuietSchedulerIntervalMilliseconds;
        var log = (CollectingCapabilityAssemblyLog)options.AssemblyLog;
        var runtime = CapabilityComposition.Compose(new OnlineCapabilityFamily(), options);
        try
        {
            await runtime.Host.StartAsync();
            var bus = runtime.Host.EventBus;
            var delivered = 0;
            bus.Subscribe<string>("tests.capabilityeventbus.faulted", _ => throw new InvalidOperationException("subscriber failed"));
            bus.Subscribe<string>("tests.capabilityeventbus.faulted", _ => delivered++);

            bus.Publish("tests.capabilityeventbus.faulted", "one");

            Assert.Equal(1, delivered);
            Assert.Contains(log.Errors, message => message.Contains("subscriber failed"));
        }
        finally
        {
            await runtime.StopAsync();
        }
    }

    /// <summary>
    /// 静默后台调度（本组用例只发布事件，不驱动 tick job）。
    /// </summary>
    /// <param name="options">组合选项。</param>
    private static void QuietScheduler(CapabilityCompositionOptions options)
    {
        options.HostOptions.SchedulerIntervalMilliseconds = QuietSchedulerIntervalMilliseconds;
    }
}
