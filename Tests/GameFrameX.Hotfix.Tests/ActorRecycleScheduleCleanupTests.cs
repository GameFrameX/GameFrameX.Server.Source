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
//   禁止利用本项目实施危害国家安全、破坏社会秩序、
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

using System.Reflection;
using GameFrameX.Core.Abstractions.Agent;
using GameFrameX.Core.Abstractions.Events;
using GameFrameX.Core.Abstractions.Hotfix;
using GameFrameX.Core.Actors;
using GameFrameX.Core.Components;
using GameFrameX.Core.Hotfix;
using GameFrameX.Core.Timer;
using GameFrameX.Core.Timer.Handler;
using GameFrameX.Network.Abstractions;
using GameFrameX.Network.HTTP;
using GameFrameX.Utility;
using GameFrameX.Utility.Setting;
using Xunit;

namespace GameFrameX.Hotfix.Tests;

/// <summary>
/// Actor 回收清理定时任务回归测试（C436 / GFX-1176 CR-H01）：Actor 出表后其名下 Quartz 任务必须被删除；
/// 定时任务触发时目标 Actor 不在内存中，不得重建 Actor，且任务必须自行删除（杜绝周期任务复活已回收 Actor）。
/// Regression tests for recycle-time timer cleanup (C436): a removed actor's Quartz jobs are deleted, and
/// a job firing for an actor that is not in memory never recreates the actor and deletes itself.
/// </summary>
[Collection(HotfixManagerStaticStateCollection.Name)]
public sealed class ActorRecycleScheduleCleanupTests : IDisposable
{
    /// <summary>
    /// 每服单例段探针 Actor 类型（未注册组件，构造为裸 Actor）。
    /// </summary>
    private const ushort GlobalProbeActorType = 201;

    /// <summary>
    /// 每实体多实例段探针 Actor 类型（0 &lt; 96 &lt; 128，业务程序集未占用）。
    /// </summary>
    private const ushort MultiProbeActorType = 96;

    /// <summary>
    /// 探针专用服务器ID段，避免与其他测试创建的 Actor 冲突。
    /// </summary>
    private const int ProbeServerIdStart = 9720;

    /// <summary>
    /// 初始化进程级设置并注入测试解析替身（热更程序集 = 测试程序集，供 Quartz 作业校验与处理器解析）。
    /// </summary>
    public ActorRecycleScheduleCleanupTests()
    {
        if (GlobalSettings.CurrentSetting == null)
        {
            GlobalSettings.SetCurrentSetting(new AppSetting { ServerId = 1 });
        }

        HotfixManager.SetResolverForTesting(new TimerProbeResolver());
    }

    /// <summary>
    /// 复原生产解析宿主。
    /// </summary>
    public void Dispose()
    {
        ProbeTimerHandler.ResetInvocationCount();
        HotfixManager.SetResolverForTesting(null);
    }

    /// <summary>
    /// 显式移除带定时任务的 Actor：await 返回时任务已从调度器删除且 ScheduleIdSet 已清空（改前任务永久遗留）。
    /// </summary>
    [Fact]
    public async Task RemoveAsync_ActorWithScheduledJob_DeletesJobAndClearsSet()
    {
        await QuartzTimer.StartAsync();
        var actorId = ActorIdGenerator.GetActorId(GlobalProbeActorType, ProbeServerIdStart);
        var actor = await ActorManager.GetOrNewAsync(actorId);
        var scheduleId = await QuartzTimer.DelayAsync<ProbeTimerHandler>(actorId, TimeSpan.FromHours(1));
        actor.ScheduleIdSet.Add(scheduleId);

        await ActorManager.RemoveAsync(actorId);

        Assert.Empty(actor.ScheduleIdSet);
        Assert.False(await QuartzTimer.RemoveAsync(scheduleId));
    }

    /// <summary>
    /// 周期任务指向不在内存中的 Actor：多次到期后 Actor 仍不存在、处理器从未执行，且任务已被删除
    /// （改前默认 isNew:true 会重建 Actor）。
    /// </summary>
    [Fact]
    public async Task TimerFires_TargetActorNotInMemory_DoesNotResurrectAndRemovesJob()
    {
        await QuartzTimer.StartAsync();
        var actorId = ActorIdGenerator.GetActorId(MultiProbeActorType, ProbeServerIdStart + 1);
        Assert.False(ActorManager.HasActor(actorId));

        var scheduleId = await QuartzTimer.ScheduleAsync<ProbeTimerHandler>(actorId, TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(100));
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            var jobExists = true;
            while (DateTime.UtcNow < deadline)
            {
                // ResumeAsync 对未暂停作业无副作用，返回值即作业存在性。
                jobExists = await QuartzTimer.ResumeAsync(scheduleId);
                if (!jobExists)
                {
                    break;
                }

                await Task.Delay(50);
            }

            // 再等待多个周期，确认没有任何触发重建 Actor。
            await Task.Delay(300);

            Assert.False(jobExists);
            Assert.False(ActorManager.HasActor(actorId));
            Assert.Equal(0, ProbeTimerHandler.InvocationCount);
        }
        finally
        {
            await QuartzTimer.RemoveAsync(scheduleId);
            await ActorManager.RemoveAsync(actorId);
        }
    }

    /// <summary>
    /// 探针组件：仅提供代理类型的泛型形态，不被注册。
    /// </summary>
    private sealed class TimerProbeComponent : BaseComponent
    {
    }

    /// <summary>
    /// 探针代理基类：使 <c>BaseType.GetGenericArguments()[0] == TimerProbeComponent</c>。
    /// </summary>
    private abstract class TimerProbeAgentBase<TComponent>
    {
    }

    /// <summary>
    /// 探针代理类型壳（不实例化）。
    /// </summary>
    private sealed class TimerProbeAgent : TimerProbeAgentBase<TimerProbeComponent>
    {
    }

    /// <summary>
    /// 探针处理器基类：使 <c>BaseType.GenericTypeArguments[0] == TimerProbeAgent</c>（TimerJobHelper 据此反查代理）。
    /// </summary>
    private abstract class ProbeTimerHandlerBase<TAgent> : ITimerHandler
    {
        public abstract Task InnerHandleTimerAsync(IComponentAgent agent, GameEventArgs gameEventArgs);
    }

    /// <summary>
    /// 探针处理器：统计执行次数（目标 Actor 不在内存时不得执行）。
    /// </summary>
    private sealed class ProbeTimerHandler : ProbeTimerHandlerBase<TimerProbeAgent>
    {
        private static int _invocationCount;

        public static int InvocationCount
        {
            get { return Volatile.Read(ref _invocationCount); }
        }

        public static void ResetInvocationCount()
        {
            Volatile.Write(ref _invocationCount, 0);
        }

        public override Task InnerHandleTimerAsync(IComponentAgent agent, GameEventArgs gameEventArgs)
        {
            Interlocked.Increment(ref _invocationCount);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// 解析替身：热更程序集指向测试程序集；按类型全名解析探针处理器；代理解析返回 null。
    /// </summary>
    private sealed class TimerProbeResolver : IHotfixResolver
    {
        public Assembly HotfixAssembly
        {
            get { return typeof(TimerProbeResolver).Assembly; }
        }

        public bool InTransition
        {
            get { return false; }
        }

        public Type GetAgentType(Type componentType)
        {
            return null;
        }

        public Type GetComponentType(Type agentType)
        {
            return null;
        }

        public T GetAgent<T>(BaseComponent component, Type refAssemblyType) where T : class, IComponentAgent
        {
            return null;
        }

        public IMessageHandler GetRpcHandler(Type requestType)
        {
            return null;
        }

        public IMessageHandler GetMessageHandler(int messageId)
        {
            return null;
        }

        public BaseHttpHandler GetHttpHandler(string command)
        {
            return null;
        }

        public List<BaseHttpHandler> GetListHttpHandler()
        {
            return new List<BaseHttpHandler>();
        }

        public List<IEventListener> FindListeners(ushort actorType, Type eventArgsType)
        {
            return new List<IEventListener>();
        }

        public List<IEventListener> FindListeners(Type eventArgsType)
        {
            return new List<IEventListener>();
        }

        public T GetInstance<T>(string typeName, Type refAssemblyType = null)
        {
            if (typeName == typeof(ProbeTimerHandler).FullName)
            {
                return (T)(object)new ProbeTimerHandler();
            }

            return default;
        }
    }
}
