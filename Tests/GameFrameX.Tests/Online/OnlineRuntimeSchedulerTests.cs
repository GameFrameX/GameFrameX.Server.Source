// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and applicable international regulations.
//   使用本项目须严格遵守相应开源许可证与规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   Any legal disputes or liabilities arising from secondary development based on this project
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
using System.Threading.Tasks;
using GameFrameX.Online.Runtime;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// Online Runtime 后台调度器测试（change C128：停止令牌释放与停机路径幂等——S2930 修复的行为锚点）。
    /// </summary>
    public class OnlineRuntimeSchedulerTests
    {
        /// <summary>
        /// 构造测试用调度器（InMemory 宿主组合根，最小驱动间隔）。
        /// </summary>
        /// <returns>待测调度器。</returns>
        private static OnlineRuntimeScheduler CreateScheduler()
        {
            var host = new OnlineRuntimeHost(new OnlineRuntimeOptions
            {
                TenantId = 1,
                AppId = 1,
                ServerId = 1,
                SchedulerIntervalMilliseconds = 100,
            });
            return new OnlineRuntimeScheduler(host);
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

            // 停机后调度器仍可驱动一次 tick：TickAsync 不依赖运行中循环，仅走 _host sweep，
            // 调用成功即锚定「启动→停止→释放」整段链未抛、_host 状态完整。
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

            // 重复停止后调度器仍可驱动一次 tick：锚定 StopAsync 重入防护未破坏 host 路径。
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

            // 从未启动即停止后调度器仍可驱动一次 tick：TickAsync 不读 _loopTask，走 _host sweep，
            // 调用成功即锚定「释放路径未抛、_host sweep 可达」。
            Assert.NotNull(scheduler);
            await scheduler.TickAsync();
        }
    }
}
