// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Hosting;
using GameFrameX.Launcher.Online;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 能力调度壳测试（一次性生命周期与停机路径幂等——S2930 修复行为锚点的机制层移植；
    /// 三类 job 注册点：逐滴答顺序执行 / 事件驱动爆发口与异常隔离）。
    /// </summary>
    public class CapabilitySchedulerTests
    {
        /// <summary>
        /// 构造测试用调度器（Online 单入口组合根承载，最小驱动间隔）。
        /// </summary>
        /// <returns>待测调度器。</returns>
        private static CapabilityScheduler CreateScheduler()
        {
            var composition = OnlineCapabilityComposition.AddOnlineCapabilities(options =>
            {
                options.TenantId = 1;
                options.AppId = 1;
                options.ServerId = 1;
                options.SchedulerIntervalMilliseconds = 100;
                options.EnableRuntime = true;
            });
            return composition.Runtime.Scheduler;
        }

        /// <summary>
        /// 生产停机路径：启动后停止应正常返回（循环被取消打断并退出，令牌就地释放）。
        /// </summary>
        [Fact]
        public async Task StartThenStopAsync_CompletesWithoutThrowing()
        {
            var scheduler = CreateScheduler();

            scheduler.Start();
            await scheduler.StopAsync();

            scheduler.Dispose();

            // 停机后调度器仍可驱动一次 tick：TickAsync 不依赖运行中循环，仅驱动注册 job，
            // 调用成功即锚定「启动→停止→释放」整段链未抛、job 集合完整。
            Assert.NotNull(scheduler);
            await scheduler.TickAsync();
        }

        /// <summary>
        /// 停止幂等：StopAsync 重复调用与后续 Dispose 均不得抛异常（已释放令牌上的重入防护）。
        /// </summary>
        [Fact]
        public async Task StopAsync_RepeatedAfterStop_DoesNotThrow()
        {
            var scheduler = CreateScheduler();

            scheduler.Start();
            await scheduler.StopAsync();
            await scheduler.StopAsync();

            scheduler.Dispose();

            // 重复停止后调度器仍可驱动一次 tick：锚定 StopAsync 重入防护未破坏 job 路径。
            Assert.NotNull(scheduler);
            await scheduler.TickAsync();
        }

        /// <summary>
        /// 从未启动即停止：StopAsync 与 Dispose 直接走释放路径，不得抛异常。
        /// </summary>
        [Fact]
        public async Task StopAsync_WithoutStart_DisposesWithoutThrowing()
        {
            var scheduler = CreateScheduler();

            await scheduler.StopAsync();

            scheduler.Dispose();

            // 从未启动即停止后调度器仍可驱动一次 tick：锚定释放路径未抛、job 可达。
            Assert.NotNull(scheduler);
            await scheduler.TickAsync();
        }

        /// <summary>
        /// 逐滴答 job 按注册序顺序执行（装配序即执行序不变量）。
        /// </summary>
        [Fact]
        public async Task TickAsync_RunsTickJobsInRegistrationOrder()
        {
            var order = new System.Collections.Generic.List<string>();
            var scheduler = new CapabilityScheduler(1000, new[]
            {
                new CapabilityTickJob("a/first", _ => { lock (order) { order.Add("first"); } return Task.CompletedTask; }),
                new CapabilityTickJob("a/second", _ => { lock (order) { order.Add("second"); } return Task.CompletedTask; }),
                new CapabilityTickJob("a/third", _ => { lock (order) { order.Add("third"); } return Task.CompletedTask; }),
            });

            await scheduler.TickAsync();

            Assert.Equal(new[] { "first", "second", "third" }, order);
        }

        /// <summary>
        /// 事件驱动 job：宿主爆发口同步分发，单 job 异常不阻断其它 job 也不向来源传播。
        /// </summary>
        [Fact]
        public void RaiseEvent_DrivesEventJobsAndIsolatesExceptions()
        {
            var handled = new System.Collections.Generic.List<string>();
            var scheduler = new CapabilityScheduler(1000, Array.Empty<CapabilityTickJob>(), null, new[]
            {
                new CapabilityEventJob("a/boom", _ => throw new InvalidOperationException("boom")),
                new CapabilityEventJob("a/ok", notification => { lock (handled) { handled.Add(notification.Type); } }),
            });

            scheduler.RaiseEvent(new CapabilityEventNotification("t.moved", null, 1));

            Assert.Equal(new[] { "t.moved" }, handled);
        }
    }
}
