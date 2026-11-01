// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规及开源许可证之规定。
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
//   Any legal disputes and liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:  https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using GameFrameX.Core.Abstractions.Events;
using GameFrameX.Core.Abstractions;
using GameFrameX.Core.Actors;
using GameFrameX.Core.Actors.Impl;
using GameFrameX.Core.Components;
using GameFrameX.Core.Hotfix;
using GameFrameX.Foundation.Extensions;
using GameFrameX.Foundation.Logger;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Foundation.Utility;
using GameFrameX.Utility;
using GameFrameX.Utility.Setting;

namespace GameFrameX.Core.Events;

/// <summary>
/// 事件分发类 - 负责处理游戏中所有事件的分发和处理
/// </summary>
public static class EventDispatcher
{
    /// <summary>
    /// 在飞派发计数：Dispatch 触发后、派发任务（Actor 路径与全局路径）完成前递增，
    /// 任务完成时递减；停机排空（<see cref="WaitAllDispatchedAsync"/>）以归零为完成判据。
    /// </summary>
    private static int _inFlightDispatchCount;
    /// <summary>
    /// 分发事件到指定的Actor或全局监听器
    /// </summary>
    /// <param name="actorId">目标Actor的唯一标识符，如果为无效值则分发到全局监听器</param>
    /// <param name="eventArgs">事件携带的参数数据，绑定键取自其具体类型；无载荷事件须使用空标记类，禁止传null</param>
    /// <exception cref="ArgumentNullException"><paramref name="eventArgs" /> 为 null 时抛出</exception>
    public static void Dispatch(long actorId, GameEventArgs eventArgs)
    {
        ArgumentNullException.ThrowIfNull(eventArgs);

        // 尝试获取目标Actor
        var actor = ActorManager.GetActor(actorId);
        if (actor != null)
        {
            // 派发完成信号：工作单元执行完、或入队被拒（Actor 已隔离 / 邮箱已停）时只计一次。
            // Dispatch completion signal: counted exactly once, whether the work unit ran or the enqueue was rejected.
            var dispatchCompleted = 0;
            void CompleteDispatch()
            {
                if (Interlocked.Exchange(ref dispatchCompleted, 1) == 0)
                {
                    Interlocked.Decrement(ref _inFlightDispatchCount);
                }
            }

            // 用入队（而非 Tell）取得完成信号：Tell 在隔离/邮箱停止时静默丢弃工作单元，
            // 在飞计数将永久滞留，停机排空每次都空等满超时；EnqueueAsync 仍经 Actor 邮箱 FIFO 串行执行。
            // Enqueue (instead of Tell) provides the completion signal: Tell silently drops the work unit
            // when the actor is quarantined or its mailbox stopped, leaking the in-flight counter; EnqueueAsync
            // still runs the work unit serially through the actor mailbox in FIFO order.
            Interlocked.Increment(ref _inFlightDispatchCount);
            var dispatchTask = actor.WorkerActor.EnqueueAsync(async () =>
            {
                try
                {
                    await DispatchToActorAsync(actor, actorId, eventArgs);
                }
                finally
                {
                    CompleteDispatch();
                }
            }, callChainId: WorkerActor.NextChainId(), discard: true, timeOut: GlobalSettings.CurrentSetting.ActorTimeOut);

            _ = dispatchTask.ContinueWith(_ => CompleteDispatch(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return;
        }

        // 目标Actor不在内存中：按监听器所属Actor分别投递串行执行，不在线程池上执行监听器
        DispatchGlobal(actorId, eventArgs);
    }

    /// <summary>
    /// 等待所有在飞事件派发完成（停机排空用，C367）。
    /// </summary>
    /// <remarks>
    /// Polls the in-flight dispatch counter until it reaches zero or the timeout elapses.
    /// 返回 false 表示超时仍有派发未完成，不抛异常——调用方记日志后放行停机。
    /// </remarks>
    /// <param name="timeout">最长等待时限。</param>
    /// <returns>时限内全部派发完成返回 true；超时仍有在飞派发返回 false。</returns>
    public static async Task<bool> WaitAllDispatchedAsync(TimeSpan timeout)
    {
        var deadlineUtcMilliseconds = TimerHelper.UnixTimeMilliseconds() + (long)timeout.TotalMilliseconds;
        while (Interlocked.CompareExchange(ref _inFlightDispatchCount, 0, 0) > 0)
        {
            if (TimerHelper.UnixTimeMilliseconds() >= deadlineUtcMilliseconds)
            {
                return false;
            }

            await Task.Delay(20);
        }

        return true;
    }

    /// <summary>
    /// 在目标 Actor 上下文内分发事件：取出该 Actor 类型注册的监听器并逐个调用其事件处理方法。
    /// </summary>
    /// <param name="actor">目标 Actor</param>
    /// <param name="actorId">目标Actor的唯一标识符</param>
    /// <param name="eventArgs">事件携带的参数数据，不为null</param>
    /// <returns>异步任务</returns>
    internal static async Task DispatchToActorAsync(IActor actor, long actorId, GameEventArgs eventArgs)
    {
        // 事件需要在本actor内执行，不可多线程执行，所以不能使用Task.WhenAll来处理
        // 获取该Actor类型下注册的所有事件监听器
        var listeners = HotfixManager.Resolver.FindListeners(actor.Type, eventArgs.GetType());
        if (listeners.IsNullOrEmpty())
        {
            // Localization: Core.EventDispatcher.DispatchNoListeners - EventDispatcher.Dispatch Actor {0} 类型：{1} 事件类型：{2} 没有找到任何监听者
            LogHelper.Warning(LocalizationService.GetString(Localization.Keys.Core.EventDispatcher.DispatchNoListeners, actorId, actor.Type, eventArgs.GetType().Name));
            return;
        }

        // 遍历所有监听器并执行事件处理
        foreach (var listener in listeners)
        {
            await DispatchListenerInActorAsync(actor, listener, actorId, eventArgs);
        }
    }

    /// <summary>
    /// 全局分发：目标 Actor 不在内存中时，把每个监听器投递到其所属全局 Actor 的消息队列中串行执行；
    /// 监听器属于单个玩家 Actor 类型（或所属全局 Actor 尚未创建）时记 Warning 后跳过，绝不以空代理调用监听器。
    /// </summary>
    /// <param name="actorId">原始目标Actor的唯一标识符（仅用于日志定位）</param>
    /// <param name="eventArgs">事件携带的参数数据，不为null</param>
    private static void DispatchGlobal(long actorId, GameEventArgs eventArgs)
    {
        // 事件需要在本actor内执行，不可多线程执行，所以不能使用Task.WhenAll来处理
        // 获取全局注册的事件监听器（聚合全部Actor类型）
        var listeners = HotfixManager.Resolver.FindListeners(eventArgs.GetType());
        if (listeners.IsNullOrEmpty())
        {
            // Localization: Core.EventDispatcher.DispatchNoListeners - EventDispatcher.Dispatch Actor {0} 类型：{1} 事件类型：{2} 没有找到任何监听者
            LogHelper.Warning(LocalizationService.GetString(Localization.Keys.Core.EventDispatcher.DispatchNoListeners, actorId, ActorIdGenerator.GetActorType(actorId), eventArgs.GetType().Name));
            return;
        }

        // 遍历所有监听器：按所属Actor类型分别投递（同一Actor上的监听器按投递顺序FIFO串行）
        foreach (var listener in listeners)
        {
            var componentType = HotfixManager.Resolver.GetComponentType(listener.AgentType);
            if (componentType == null)
            {
                // Localization: Core.EventDispatcher.DispatchOwnerActorNotLoaded - EventDispatcher.Dispatch Actor {0} 事件类型：{1} 监听器 {2} 所属 Actor 不在内存中，已跳过
                LogHelper.Warning(LocalizationService.GetString(Localization.Keys.Core.EventDispatcher.DispatchOwnerActorNotLoaded, actorId, eventArgs.GetType().Name, listener.GetType().FullName));
                continue;
            }

            var ownerActorType = ComponentRegister.GetActorType(componentType);
            Actor ownerActor = null;
            if (ownerActorType > GlobalConst.ActorTypeSeparator)
            {
                // 全局Actor在启动期以默认服务器ID创建，这里用同一ID反查
                ownerActor = ActorManager.GetActor(ActorIdGenerator.GetActorId(ownerActorType));
            }

            if (ownerActor == null)
            {
                // 监听器属于单个玩家Actor类型（目标Actor已回收）或全局Actor尚未创建：跳过，不再以空代理执行
                // Localization: Core.EventDispatcher.DispatchOwnerActorNotLoaded - EventDispatcher.Dispatch Actor {0} 事件类型：{1} 监听器 {2} 所属 Actor 不在内存中，已跳过
                LogHelper.Warning(LocalizationService.GetString(Localization.Keys.Core.EventDispatcher.DispatchOwnerActorNotLoaded, actorId, eventArgs.GetType().Name, listener.GetType().FullName));
                continue;
            }

            // 每个监听器投递计入在飞计数：仍经所属 Actor 邮箱 FIFO 串行执行，但用 EnqueueAsync 取得完成信号，
            // 停机排空可等待；入队被拒（隔离/邮箱停止）由任务终态统一回落计数。
            // Each listener dispatch counts as in-flight: still serialized through the owner actor's mailbox
            // in FIFO order, but EnqueueAsync provides the completion signal for shutdown drain; a rejected
            // enqueue (quarantined/stopped mailbox) settles the counter via the task's final state.
            Interlocked.Increment(ref _inFlightDispatchCount);
            var listenerDispatchTask = ownerActor.WorkerActor.EnqueueAsync(() => DispatchListenerInActorAsync(ownerActor, listener, actorId, eventArgs), callChainId: WorkerActor.NextChainId(), discard: true, timeOut: GlobalSettings.CurrentSetting.ActorTimeOut);
            _ = listenerDispatchTask.ContinueWith(_ => Interlocked.Decrement(ref _inFlightDispatchCount), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    /// <summary>
    /// 在 Actor 上下文内执行单个监听器：取出该监听器代理类型的组件代理并调用其事件处理方法，异常就地记录。
    /// </summary>
    /// <param name="actor">监听器所属 Actor</param>
    /// <param name="listener">事件监听器</param>
    /// <param name="actorId">原始目标Actor的唯一标识符（仅用于日志定位）</param>
    /// <param name="eventArgs">事件携带的参数数据，不为null</param>
    /// <returns>异步任务</returns>
    private static async Task DispatchListenerInActorAsync(IActor actor, IEventListener listener, long actorId, GameEventArgs eventArgs)
    {
        // 获取监听器对应的组件代理
        var componentAgent = await actor.GetComponentAgentAsync(listener.AgentType, false);

        if (componentAgent == null)
        {
            // 组件未注册到该 Actor（isNew:false 未命中）：显式跳过并告警，绝不以 null 代理调用监听器
            // Localization: Core.EventDispatcher.DispatchOwnerActorNotLoaded - EventDispatcher.Dispatch Actor {0} 事件类型：{1} 监听器 {2} 所属 Actor 不在内存中，已跳过
            LogHelper.Warning(LocalizationService.GetString(Localization.Keys.Core.EventDispatcher.DispatchOwnerActorNotLoaded, actorId, eventArgs.GetType().Name, listener.GetType().FullName));
            return;
        }
        try
        {
            // 调用监听器的事件处理方法
            await listener.HandleEventAsync(componentAgent, eventArgs);
        }
        catch (Exception exception)
        {
            // 捕获并记录事件处理过程中的异常
            // Localization: Core.EventDispatcher.DispatchError - EventDispatcher.Dispatch Actor {0} {1} 异常: {2}
            LogHelper.Error(LocalizationService.GetString(Localization.Keys.Core.EventDispatcher.DispatchError, actorId, eventArgs.GetType().Name, exception));
        }
    }
}