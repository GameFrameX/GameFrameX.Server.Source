// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes and liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository:  https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:   https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:     https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.Reflection;
using GameFrameX.Core.Abstractions.Hotfix;
using GameFrameX.Core.Abstractions.Agent;
using GameFrameX.Core.Abstractions.Events;
using GameFrameX.Core.Actors;
using GameFrameX.Core.Components;
using GameFrameX.Core.Hotfix;
using GameFrameX.Core.Hotfix.Agent;
using GameFrameX.Core.Timer;
using GameFrameX.Core.Timer.Handler;
using GameFrameX.Foundation.Utility;
using GameFrameX.Utility.Setting;
using Quartz;
using Xunit;

namespace GameFrameX.Hotfix.Tests;

/// <summary>
/// 组件代理时间戳延迟调度语义回归：DelayAsync&lt;T&gt;(long) 的 time 按 Unix 毫秒时间戳解释。
/// </summary>
/// <remarks>
/// 旧实现把 Unix 时间戳当 Ticks 构造 DateTime，触发点落在 0001 年附近，misfire 立即触发并自删作业——
/// 定时任务永不按预期触达。以 GetJobDetail 存在性探测断言：+250ms 探测时作业必须仍在调度中（未触发未删除）。
/// </remarks>
public sealed class AgentDelayTimestampSchedulingTests
{
    /// <summary>调度目标偏移（毫秒）：足够远以覆盖断言窗口，足够近以约束用例时长。</summary>
    private const int ScheduleTargetOffsetMilliseconds = 1_500;

    /// <summary>断言探测等待（毫秒）：远小于调度目标，作业必须仍未触发。</summary>
    private const int ProbeWaitMilliseconds = 250;

    [Fact]
    public async Task DelayAsync_ByUnixMillisecondTimestamp_KeepsJobPendingUntilDue_AndRemovesAfterFiring()
    {
        await QuartzTimer.StartAsync();
        HotfixManager.SetResolverForTesting(new NullHotfixResolver());
        try
        {
            var agent = CreateProbeComponentAgent();
            var dueTimestamp = TimerHelper.UnixTimeMilliseconds() + ScheduleTargetOffsetMilliseconds;

            var scheduleId = await agent.DelayAsync<TimestampDelayTimerHandler>(dueTimestamp);
            Assert.True(scheduleId > 0);

            var scheduler = GetScheduler();
            try
            {
                // 到期前作业必须仍在调度中：旧实现按 Ticks 解释触发点落在 0001 年附近（早已过期），
                // 或经时区换算落在过去——两者都会提前离开"挂起"状态（确定性红）
                await Task.Delay(ProbeWaitMilliseconds);
                Assert.NotNull(await scheduler.GetJobDetail(new JobKey(scheduleId.ToString())));

                // 到期后作业触发并完成自删（一次性作业非持久化）：验证触发时刻确实由时间戳决定
                var removalDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
                while (DateTime.UtcNow < removalDeadline
                       && await scheduler.GetJobDetail(new JobKey(scheduleId.ToString())) != null)
                {
                    await Task.Delay(50);
                }

                Assert.Null(await scheduler.GetJobDetail(new JobKey(scheduleId.ToString())));
            }
            finally
            {
                await QuartzTimer.RemoveAsync(scheduleId);
            }
        }
        finally
        {
            HotfixManager.SetResolverForTesting(null);
        }
    }

    /// <summary>
    /// 构造挂在真实 Actor 上的探针组件代理（ActorLimit/WorkerActor 均不介入，仅承载 ActorId 与调度集合）。
    /// </summary>
    private static TimestampDelayComponentAgent CreateProbeComponentAgent()
    {
        if (GlobalSettings.CurrentSetting == null)
        {
            GlobalSettings.SetCurrentSetting(new AppSetting { ServerId = 1 });
        }

        // 类型取 200（全局段）：构造器的组件激活 Tell 对未注册类型仅记录 Fatal 日志后返回，无副作用
        var actor = new Actor(990_099, 200);
        var component = new TimestampDelayProbeComponent { Actor = actor, };
        var agent = new TimestampDelayComponentAgent();
        agent.SetOwner(component);
        return agent;
    }

    /// <summary>
    /// 取 QuartzTimer 私有调度器实例（GetJobDetail 存在性探测用）。
    /// </summary>
    private static IScheduler GetScheduler()
    {
        var schedulerField = typeof(QuartzTimer).GetField("_scheduler", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(schedulerField);
        return Assert.IsAssignableFrom<IScheduler>(schedulerField.GetValue(null));
    }

    /// <summary>探针组件（无任何状态语义，仅承载 Actor 归属）。</summary>
    private sealed class TimestampDelayProbeComponent : BaseComponent
    {
    }

    /// <summary>探针组件代理（仅使用 DelayAsync 调度面）。</summary>
    private sealed class TimestampDelayComponentAgent : BaseComponentAgent<TimestampDelayProbeComponent>
    {
    }

    /// <summary>探针定时处理器（触发前即被移除，处理体不被调用）。</summary>
    private sealed class TimestampDelayTimerHandler : ITimerHandler
    {
        /// <summary>探针空实现。</summary>
        public Task InnerHandleTimerAsync(IComponentAgent agent, GameEventArgs gameEventArgs)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// 测试用解析契约替身：HotfixAssembly 指向测试程序集，其余成员返回空值（本测试不让作业触发）。
    /// </summary>
    private sealed class NullHotfixResolver : IHotfixResolver
    {
        /// <summary>测试程序集。</summary>
        public Assembly HotfixAssembly
        {
            get { return typeof(NullHotfixResolver).Assembly; }
        }

        /// <summary>不在切换中。</summary>
        public bool InTransition
        {
            get { return false; }
        }

        /// <summary>无代理类型。</summary>
        public Type GetAgentType(Type componentType)
        {
            return null;
        }

        /// <summary>无组件类型。</summary>
        public Type GetComponentType(Type agentType)
        {
            return null;
        }

        /// <summary>无代理实例。</summary>
        public T GetAgent<T>(BaseComponent component, Type refAssemblyType) where T : class, IComponentAgent
        {
            return null;
        }

        /// <summary>无 RPC 处理器。</summary>
        public Network.Abstractions.IMessageHandler GetRpcHandler(Type requestType)
        {
            return null;
        }

        /// <summary>无消息处理器。</summary>
        public Network.Abstractions.IMessageHandler GetMessageHandler(int messageId)
        {
            return null;
        }

        /// <summary>无 HTTP 处理器。</summary>
        public Network.HTTP.BaseHttpHandler GetHttpHandler(string command)
        {
            return null;
        }

        /// <summary>无 HTTP 处理器列表。</summary>
        public List<Network.HTTP.BaseHttpHandler> GetListHttpHandler()
        {
            return new List<Network.HTTP.BaseHttpHandler>();
        }

        /// <summary>无监听者。</summary>
        public List<IEventListener> FindListeners(ushort actorType, Type eventArgsType)
        {
            return new List<IEventListener>();
        }

        /// <summary>无监听者。</summary>
        public List<IEventListener> FindListeners(Type eventArgsType)
        {
            return new List<IEventListener>();
        }

        /// <summary>无实例。</summary>
        public T GetInstance<T>(string typeName, Type refAssemblyType = null)
        {
            return default;
        }
    }
}
