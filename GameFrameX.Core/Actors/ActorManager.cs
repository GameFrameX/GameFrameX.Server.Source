// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规及开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 MIT 许可证与 Apache License 2.0 双许可证分发，
//   This project is dual-licensed under the MIT License and Apache License 2.0,
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

using System.Collections.Concurrent;
using GameFrameX.Core.Abstractions;
using GameFrameX.Core.Abstractions.Agent;
using GameFrameX.Core.Actors.Impl;
using GameFrameX.Core.Components;
using GameFrameX.Core.Hotfix;
using GameFrameX.Core.Timer;
using GameFrameX.Core.Utility;
using GameFrameX.Foundation.Logger;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Utility;
using GameFrameX.Foundation.Extensions;
using GameFrameX.Foundation.Utility;
using GameFrameX.Utility.Setting;

namespace GameFrameX.Core.Actors;

/// <summary>
/// Actor管理器，提供Actor的生命周期管理、组件代理获取、定时回存和跨天处理等核心能力。
/// </summary>
/// <remarks>
/// Actor manager that provides core capabilities such as Actor lifecycle management, component agent retrieval, timed state persistence, and cross-day processing.
/// </remarks>
public static class ActorManager
{
    private const int WorkerCount = 10;

    private const int OnceSaveCount = 1000;

    private const int CrossDayGlobalWaitSeconds = 60;
    private const int CrossDayNotRoleWaitSeconds = 120;
    private static readonly ConcurrentDictionary<long, Actor> ActorMap = new();

    private static readonly ConcurrentDictionary<long, DateTime> ActiveTimeDic = new ConcurrentDictionary<long, DateTime>();

    private static readonly List<WorkerActor> WorkerActors = new();

    static ActorManager()
    {
        for (var i = 0; i < WorkerCount; i++)
        {
            WorkerActors.Add(new WorkerActor());
        }
    }

    /// <summary>
    /// 根据ActorId获取对应的IComponentAgent对象。
    /// </summary>
    /// <remarks>
    /// Gets the corresponding IComponentAgent object by ActorId.
    /// </remarks>
    /// <param name="actorId">Actor唯一标识 / Actor unique identifier</param>
    /// <param name="isNew">是否当获取为空时默认创建，默认值为true / Whether to create by default when not found, default is true</param>
    /// <typeparam name="T">组件代理类型 / Component agent type</typeparam>
    /// <returns>组件代理对象的异步任务 / Async task of the component agent object</returns>
    public static async Task<T> GetComponentAgent<T>(long actorId, bool isNew = true) where T : IComponentAgent
    {
        Actor actor;
        if (isNew)
        {
            actor = await GetOrNew(actorId);
            return await actor.GetComponentAgent<T>();
        }

        actor = Get(actorId);
        if (actor != null)
        {
            return await actor.GetComponentAgent<T>();
        }

        return await Task.FromResult<T>(default);
    }

    /// <summary>
    /// 是否存在指定的Actor。
    /// </summary>
    /// <remarks>
    /// Determines whether the specified Actor exists.
    /// </remarks>
    /// <param name="actorId">Actor唯一标识 / Actor unique identifier</param>
    /// <returns>如果存在则返回 <c>true</c>；否则返回 <c>false</c> / <c>true</c> if the Actor exists; otherwise <c>false</c></returns>
    public static bool HasActor(long actorId)
    {
        return ActorMap.ContainsKey(actorId);
    }

    /// <summary>
    /// 根据ActorId获取对应的Actor。
    /// </summary>
    /// <remarks>
    /// Gets the corresponding Actor by ActorId.
    /// </remarks>
    /// <param name="actorId">Actor唯一标识 / Actor unique identifier</param>
    /// <returns>Actor对象；如果不存在则返回 <c>null</c> / The Actor object; <c>null</c> if not found</returns>
    internal static Actor GetActor(long actorId)
    {
        ActorMap.TryGetValue(actorId, out var actor);
        return actor;
    }

    /// <summary>
    /// 根据ActorId和组件类型获取对应的IComponentAgent数据。
    /// </summary>
    /// <remarks>
    /// Gets the corresponding IComponentAgent data by ActorId and component type.
    /// </remarks>
    /// <param name="actorId">Actor唯一标识 / Actor unique identifier</param>
    /// <param name="agentType">组件代理的类型 / Type of the component agent</param>
    /// <param name="isNew">是否当获取为空时默认创建，默认值为true / Whether to create by default when not found, default is true</param>
    /// <returns>组件代理对象的异步任务 / Async task of the component agent object</returns>
    internal static async Task<IComponentAgent> GetComponentAgent(long actorId, Type agentType, bool isNew = true)
    {
        Actor actor;
        if (isNew)
        {
            actor = await GetOrNew(actorId);
            return await actor.GetComponentAgent(agentType);
        }

        actor = Get(actorId);
        if (actor != null)
        {
            return await actor.GetComponentAgent(agentType);
        }

        return await Task.FromResult<IComponentAgent>(default);
    }

    /// <summary>
    /// 根据ActorId获取对应Actor中所有激活状态的组件代理对象。
    /// </summary>
    /// <remarks>
    /// Returns all activated component agent objects in the specified Actor.
    /// Returns an empty list if the ActorId does not exist.
    /// The activation state of components is maintained internally by the Actor.
    /// </remarks>
    /// <param name="actorId">要查询的Actor唯一标识 / Actor unique identifier to query</param>
    /// <returns>该Actor下所有处于激活状态的组件代理对象列表；如果Actor不存在则返回空列表 / List of all activated component agents in the Actor; empty list if the Actor does not exist</returns>
    public static List<IComponentAgent> GetActiveComponentAgents(long actorId)
    {
        var result = new List<IComponentAgent>();
        var actor = GetActor(actorId);
        if (actor.IsNull())
        {
            return result;
        }

        return actor.GetActiveComponentAgents();
    }

    /// <summary>
    /// 根据组件类型获取对应的IComponentAgent数据。
    /// </summary>
    /// <remarks>
    /// Gets the corresponding IComponentAgent data by component type.
    /// </remarks>
    /// <typeparam name="T">组件代理类型 / Component agent type</typeparam>
    /// <param name="isNew">是否当获取为空时默认创建，默认值为true / Whether to create by default when not found, default is true</param>
    /// <returns>组件代理对象的异步任务 / Async task of the component agent object</returns>
    public static Task<T> GetComponentAgent<T>(bool isNew = true) where T : IComponentAgent
    {
        var compType = HotfixManager.GetComponentType(typeof(T));
        var actorType = ComponentRegister.GetActorType(compType);
        var actorId = ActorIdGenerator.GetActorId(actorType);
        return GetComponentAgent<T>(actorId, isNew);
    }

    /// <summary>
    /// 根据actorId获取对应的actor实例，不存在则新生成一个Actor对象。
    /// </summary>
    /// <remarks>
    /// Gets the corresponding Actor instance by actorId, or creates a new one if it does not exist.
    /// </remarks>
    /// <param name="actorId">Actor唯一标识 / Actor unique identifier</param>
    /// <returns>Actor对象的异步任务 / Async task of the Actor object</returns>
    internal static async Task<Actor> GetOrNew(long actorId)
    {
        var actorType = ActorIdGenerator.GetActorType(actorId);
        if (actorType < GlobalConst.ActorTypeSeparator)
        {
            var now = TimerHelper.GetNowWithUtc();
            if (ActiveTimeDic.TryGetValue(actorId, out var activeTime)
                && (now - activeTime).TotalMinutes < 10
                && ActorMap.TryGetValue(actorId, out var actor))
            {
                ActiveTimeDic[actorId] = now;
                return actor;
            }

            return await GetLifeActor(actorId).SendAsync(() =>
            {
                ActiveTimeDic[actorId] = now;
                return ActorMap.GetOrAdd(actorId, k => new Actor(k, ActorIdGenerator.GetActorType(k)));
            });
        }

        return ActorMap.GetOrAdd(actorId, k => new Actor(k, ActorIdGenerator.GetActorType(k)));
    }

    /// <summary>
    /// 根据actorId获取对应的actor实例，不存在则返回空
    /// </summary>
    /// <param name="actorId">actorId</param>
    /// <returns>Actor对象任务</returns>
    private static Actor Get(long actorId)
    {
        var actorType = ActorIdGenerator.GetActorType(actorId);
        Actor valueActor;
        if (actorType < GlobalConst.ActorTypeSeparator)
        {
            var now = TimerHelper.GetNowWithUtc();
            if (ActiveTimeDic.TryGetValue(actorId, out var activeTime)
                && (now - activeTime).TotalMinutes < 10
                && ActorMap.TryGetValue(actorId, out var actor))
            {
                ActiveTimeDic[actorId] = now;
                return actor;
            }

            ActorMap.TryGetValue(actorId, out valueActor);
            return valueActor;
        }

        ActorMap.TryGetValue(actorId, out valueActor);
        return valueActor;
    }

    /// <summary>
    /// 等待所有Actor完成当前正在执行的操作。
    /// </summary>
    /// <remarks>
    /// Waits for all Actors to finish their currently executing operations.
    /// </remarks>
    /// <returns>表示所有Actor操作完成的异步任务 / Async task representing completion of all Actor operations</returns>
    public static Task AllFinish()
    {
        var tasks = new List<Task>();
        foreach (var actor in ActorMap.Values)
        {
            tasks.Add(actor.SendAsync(() => true));
        }

        return Task.WhenAll(tasks);
    }

    /// <summary>
    /// 根据ActorId 获取玩家
    /// </summary>
    /// <param name="actorId">ActorId</param>
    /// <returns>WorkerActor对象</returns>
    private static WorkerActor GetLifeActor(long actorId)
    {
        return WorkerActors[(int)(actorId % WorkerCount)];
    }

    /// <summary>
    /// 检查并回收空闲的Actor。
    /// </summary>
    /// <remarks>
    /// Checks and recycles idle Actors based on the configured recycle time.
    /// </remarks>
    /// <returns>表示回收操作的异步任务 / Async task representing the recycle operation</returns>
    public static Task CheckIdle()
    {
        foreach (var actor in ActorMap.Values)
        {
            if (!actor.AutoRecycle)
            {
                continue;
            }

            async Task Func()
            {
                if (actor.AutoRecycle && (TimerHelper.GetNowWithUtc() - ActiveTimeDic[actor.Id]).TotalMinutes > GlobalSettings.CurrentSetting.ActorRecycleTime)
                {
                    async Task<bool> Work()
                    {
                        if (ActiveTimeDic.TryGetValue(actor.Id, out var activeTime) && (TimerHelper.GetNowWithUtc() - ActiveTimeDic[actor.Id]).TotalMinutes > GlobalSettings.CurrentSetting.ActorRecycleTime)
                        {
                            // 防止定时回存失败时State被直接移除
                            if (actor.ReadyToDeActive)
                            {
                                await actor.Inactive();
                                await actor.OnRecycle();
                                ActorMap.TryRemove(actor.Id, out _);
                                LogHelper.Debug("ActorManager.CheckIdle, Actor recycled, actorId: {actorId}, actorType: {actorType}, message: {message}", actor.Id, actor.Type, LocalizationService.GetString(Localization.Keys.Core.Actor.Recycled, actor.Id, actor.Type));
                            }
                            else
                            {
                                // 不能存就久一点再判断
                                ActiveTimeDic[actor.Id] = TimerHelper.GetNowWithUtc();
                            }
                        }

                        return true;
                    }

                    await GetLifeActor(actor.Id).SendAsync(Work);
                }
            }

            actor.Tell(Func);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 保存所有Actor的状态数据。
    /// </summary>
    /// <remarks>
    /// Saves the state data of all Actors.
    /// </remarks>
    /// <returns>表示保存操作的异步任务 / Async task representing the save operation</returns>
    public static async Task SaveAll()
    {
        try
        {
            var begin = DateTime.Now;
            var taskList = new List<Task>();
            foreach (var actor in ActorMap.Values)
            {
                async Task Save()
                {
                    await actor.SaveAllState();
                }

                taskList.Add(actor.SendAsync((Func<Task>)Save, checkLock: true));
            }

            await Task.WhenAll(taskList);
            LogHelper.Info("ActorManager.SaveAll, Save all actor state time: {saveAllStateTime} ms, message: {message}", (DateTime.Now - begin).TotalMilliseconds, LocalizationService.GetString(Localization.Keys.Core.ActorManager.SaveAllStateTime, (DateTime.Now - begin).TotalMilliseconds));
        }
        catch (Exception e)
        {
            LogHelper.Error("ActorManager.SaveAll, Save all actor state, message: {message}, error: {exception}", LocalizationService.GetString(Localization.Keys.Core.ActorManager.SaveAllStateError, e), e);
            throw;
        }
    }

    /// <summary>
    /// 定时回存所有Actor的状态数据。
    /// </summary>
    /// <remarks>
    /// Periodically saves the state data of all Actors in batches.
    /// </remarks>
    /// <returns>表示定时回存操作的异步任务 / Async task representing the timed save operation</returns>
    public static async Task TimerSave()
    {
        try
        {
            await TimerSaveInBatches(ActorMap.Values, SaveActorStateAsync, static () => GlobalTimer.IsWorking, static () => Task.Delay(1000));
        }
        catch (Exception exception)
        {
            LogHelper.Error("ActorManager.TimerSave, Timer save all actor state error, message: {message}, error: {exception}", LocalizationService.GetString(Localization.Keys.Core.ActorManager.TimerSaveStateError), exception);
        }
    }

    private static Task SaveActorStateAsync(Actor actor)
    {
        async Task Work()
        {
            await actor.SaveAllState();
        }

        return actor.SendAsync((Func<Task>)Work, checkLock: true);
    }


    private static async Task TimerSaveInBatches<T>(IEnumerable<T> items, Func<T, Task> saveAsync, Func<bool> isWorking, Func<Task> delayAsync)
    {
        var taskList = new List<Task>(2048);
        foreach (var item in items)
        {
            // 如果定时回存过程中关服，停止继续投递新任务，但必须等待已经投递的保存任务完成。
            if (!isWorking())
            {
                if (taskList.Count > 0)
                {
                    await Task.WhenAll(taskList);
                }

                return;
            }

            taskList.Add(saveAsync(item));
            if (taskList.Count < OnceSaveCount)
            {
                continue;
            }

            await Task.WhenAll(taskList);
            taskList.Clear();
            if (isWorking())
            {
                await delayAsync();
            }
        }

        if (taskList.Count > 0)
        {
            await Task.WhenAll(taskList);
        }
    }

    /// <summary>
    /// 角色跨天处理。
    /// </summary>
    /// <remarks>
    /// Processes cross-day logic for all role-type Actors.
    /// </remarks>
    /// <param name="openServerDay">开服天数 / Number of days since server opened</param>
    /// <returns>表示跨天操作的异步任务 / Async task representing the cross-day operation</returns>
    public static Task RoleCrossDay(int openServerDay)
    {
        foreach (var actor in ActorMap.Values)
        {
            if (actor.Type < GlobalConst.ActorTypeSeparator)
            {
                actor.Tell(() => actor.CrossDay(openServerDay));
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 全局跨天处理，驱动Actor优先执行，随后依次处理全局组件和非玩家组件。
    /// </summary>
    /// <remarks>
    /// Processes global cross-day logic, with the driver Actor executing first, followed by global and non-player components.
    /// </remarks>
    /// <param name="openServerDay">开服天数 / Number of days since server opened</param>
    /// <param name="driverActorType">驱动Actor类型 / Driver Actor type</param>
    /// <returns>表示跨天操作的异步任务 / Async task representing the cross-day operation</returns>
    public static async Task CrossDay(int openServerDay, ushort driverActorType)
    {
        // 驱动actor优先执行跨天
        var id = ActorIdGenerator.GetActorId(driverActorType);
        var driverActor = ActorMap[id];
        await driverActor.CrossDay(openServerDay);

        var begin = DateTime.Now;
        var a = 0;
        var b = 0;
        foreach (var actor in ActorMap.Values)
        {
            if (actor.Type > GlobalConst.ActorTypeSeparator && actor.Type != driverActorType)
            {
                b++;

                async Task Work()
                {
                    LogHelper.Info("ActorManager.CrossDay, CrossDay actor, actorId: {actorId}, actorType: {actorType}, message: {message}", actor.Id, actor.Type, LocalizationService.GetString(Localization.Keys.Core.ActorManager.GlobalActorCrossDay, actor.Type));
                    await actor.CrossDay(openServerDay);
                    Interlocked.Increment(ref a);
                }

                actor.Tell(Work);
            }
        }

        while (a < b)
        {
            if ((DateTime.Now - begin).TotalSeconds > CrossDayGlobalWaitSeconds)
            {
                LogHelper.Warning("ActorManager.CrossDay, GlobalCompCrossDayTimeout, timeout: {timeout}, message: {message}", CrossDayGlobalWaitSeconds, LocalizationService.GetString(Localization.Keys.Core.ActorManager.GlobalCompCrossDayTimeout, CrossDayGlobalWaitSeconds));
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10));
        }

        var globalCost = (DateTime.Now - begin).TotalMilliseconds;
        LogHelper.Info("ActorManager.CrossDay, GlobalCompCrossDayComplete, cost: {cost}, message: {message}", globalCost, LocalizationService.GetString(Localization.Keys.Core.ActorManager.GlobalCompCrossDayComplete, globalCost));
        a = 0;
        b = 0;
        foreach (var actor in ActorMap.Values)
        {
            if (actor.Type > GlobalConst.ActorTypeSeparator && actor.Type != driverActorType)
            {
                b++;

                async Task Work()
                {
                    await actor.CrossDay(openServerDay);
                    Interlocked.Increment(ref a);
                }

                actor.Tell(Work);
            }
        }

        while (a < b)
        {
            if ((DateTime.Now - begin).TotalSeconds > CrossDayNotRoleWaitSeconds)
            {
                LogHelper.Warning("ActorManager.CrossDay, NonPlayerCompCrossDayTimeout, timeout: {timeout}, message: {message}", CrossDayNotRoleWaitSeconds, LocalizationService.GetString(Localization.Keys.Core.ActorManager.NonPlayerCompCrossDayTimeout, CrossDayNotRoleWaitSeconds));
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10));
        }

        var otherCost = (DateTime.Now - begin).TotalMilliseconds - globalCost;
        LogHelper.Info("ActorManager.CrossDay, NonPlayerCompCrossDayComplete, cost: {cost}, message: {message}", otherCost, LocalizationService.GetString(Localization.Keys.Core.ActorManager.NonPlayerCompCrossDayComplete, otherCost));
    }

    /// <summary>
    /// 删除所有Actor并使其进入非激活状态。
    /// </summary>
    /// <remarks>
    /// Removes all Actors and deactivates them.
    /// </remarks>
    /// <returns>表示删除操作的异步任务 / Async task representing the removal operation</returns>
    public static async Task RemoveAll()
    {
        var tasks = new List<Task>();
        foreach (var actor in ActorMap.Values)
        {
            tasks.Add(actor.Inactive());
        }

        await Task.WhenAll(tasks);
    }

    /// <summary>
    /// 删除指定Actor并使其进入非激活状态。
    /// </summary>
    /// <remarks>
    /// Removes the specified Actor and deactivates it.
    /// </remarks>
    /// <param name="actorId">Actor唯一标识 / Actor unique identifier</param>
    /// <returns>表示删除操作的异步任务 / Async task representing the removal operation</returns>
    public static Task Remove(long actorId)
    {
        if (ActorMap.Remove(actorId, out var actor))
        {
            actor.Tell(actor.Inactive);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 遍历所有Actor并执行指定的回调操作。
    /// </summary>
    /// <remarks>
    /// Iterates over all Actors and executes the specified callback action.
    /// </remarks>
    /// <param name="action">对每个Actor执行的回调操作 / Callback action to execute for each Actor</param>
    public static void ActorForEach(Action<IActor> action)
    {
        foreach (var actor in ActorMap.Values)
        {
            try
            {
                action.Invoke(actor);
            }
            catch (Exception exception)
            {
                LogHelper.Error("ActorManager.ActorForEach, error, actorId: {actorId}, actorType: {actorType}, exception: {exception}", actor.Id, actor.Type, exception);
            }
        }
    }

    /// <summary>
    /// 遍历所有匹配指定组件代理类型的Actor，并执行异步回调操作。
    /// </summary>
    /// <remarks>
    /// Iterates over all Actors matching the specified component agent type and executes the async callback.
    /// </remarks>
    /// <param name="func">对每个匹配Actor执行的异步回调操作 / Async callback to execute for each matching Actor</param>
    /// <typeparam name="T">组件代理类型 / Component agent type</typeparam>
    public static void ActorForEach<T>(Func<T, Task> func) where T : IComponentAgent
    {
        var agentType = typeof(T);
        var compType = HotfixManager.GetComponentType(agentType);
        var actorType = ComponentRegister.GetActorType(compType);
        foreach (var actor in ActorMap.Values)
        {
            if (actor.Type != actorType)
            {
                continue;
            }

            async Task Work()
            {
                var comp = await actor.GetComponentAgent<T>();
                await func(comp);
            }

            actor.Tell(Work);
        }
    }

    /// <summary>
    /// 遍历所有匹配指定组件代理类型的Actor，并执行同步回调操作。
    /// </summary>
    /// <remarks>
    /// Iterates over all Actors matching the specified component agent type and executes the synchronous callback.
    /// </remarks>
    /// <param name="action">对每个匹配Actor执行的同步回调操作 / Synchronous callback to execute for each matching Actor</param>
    /// <typeparam name="T">组件代理类型 / Component agent type</typeparam>
    public static void ActorForEach<T>(Action<T> action) where T : IComponentAgent
    {
        var agentType = typeof(T);
        var compType = HotfixManager.GetComponentType(agentType);
        var actorType = ComponentRegister.GetActorType(compType);
        foreach (var actor in ActorMap.Values)
        {
            if (actor.Type == actorType)
            {
                async Task Work()
                {
                    var comp = await actor.GetComponentAgent<T>();
                    action(comp);
                }

                actor.Tell(Work);
            }
        }
    }

    /// <summary>
    /// 清除所有Actor的组件代理缓存。
    /// </summary>
    /// <remarks>
    /// Clears the component agent cache of all Actors.
    /// </remarks>
    public static void ClearAgent()
    {
        foreach (var actor in ActorMap.Values)
        {
            actor.Tell(actor.ClearAgent);
        }
    }
}