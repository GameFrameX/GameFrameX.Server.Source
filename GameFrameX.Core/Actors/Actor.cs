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

using System.Collections.Concurrent;
using GameFrameX.Core.Abstractions;
using GameFrameX.Core.Abstractions.Agent;
using GameFrameX.Core.Abstractions.Timer;
using GameFrameX.Core.Actors.Impl;
using GameFrameX.Core.Components;
using GameFrameX.Core.Timer;
using GameFrameX.Foundation.Logger;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Utility.Setting;

namespace GameFrameX.Core.Actors;

/// <summary>
/// Actor类,用于管理和协调组件的生命周期、消息传递等核心功能
/// </summary>
public sealed class Actor : IActor, IDisposable
{
    /// <summary>
    /// 组件映射字典,用于存储当前Actor下的所有组件实例
    /// </summary>
    private readonly ConcurrentDictionary<Type, BaseComponent> _componentsMap = new();

    /// <summary>
    /// Actor构造函数
    /// </summary>
    /// <param name="id">Actor的唯一标识符</param>
    /// <param name="type">Actor的类型标识</param>
    public Actor(long id, ushort type)
    {
        Id = id;
        Type = type;
        WorkerActor = new WorkerActor(id);

        if (type < GlobalConst.ActorTypeSeparator)
        {
            Tell(() => SetAutoRecycle(true));
        }
        else
        {
            Tell(() => ComponentRegister.ActiveComponentsAsync(this));
        }
    }

    /// <summary>
    /// 判断Actor是否准备好进行反激活操作
    /// </summary>
    internal bool ReadyToDeActive
    {
        get { return _componentsMap.Values.All(item => item.ReadyToInactive); }
    }

    /// <summary>
    /// Actor的唯一标识符
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// 订阅的定时器任务ID集合
    /// </summary>
    public HashSet<long> ScheduleIdSet { get; } = new();

    /// <summary>
    /// Actor的类型标识,用于区分不同种类的Actor
    /// </summary>
    public ushort Type { get; set; }

    /// <summary>
    /// 工作者Actor实例,负责具体的任务执行
    /// </summary>
    public IWorkerActor WorkerActor { get; init; }

    /// <summary>
    /// 标识Actor是否启用自动回收机制
    /// </summary>
    public bool AutoRecycle { get; private set; }

    /// <summary>
    /// 设置Actor的自动回收状态
    /// </summary>
    /// <param name="autoRecycle">是否启用自动回收,true表示启用,false表示禁用</param>
    public void SetAutoRecycle(bool autoRecycle)
    {
        Tell(() => { AutoRecycle = autoRecycle; });
    }

    /// <summary>
    /// 获取指定类型的组件代理实例
    /// </summary>
    /// <typeparam name="T">组件代理类型</typeparam>
    /// <param name="isNew">当组件不存在时是否创建新实例,默认为true</param>
    /// <returns>返回指定类型的组件代理实例</returns>
    public async Task<T> GetComponentAgentAsync<T>(bool isNew = true) where T : IComponentAgent
    {
        return (T)await GetComponentAgentAsync(typeof(T), isNew);
    }

    /// <summary>
    /// 获取所有已激活的组件代理实例
    /// </summary>
    /// <remarks>
    /// 遍历组件映射字典(_componentsMap),筛选出所有处于激活状态(IsActive=true)的组件,
    /// 并获取它们对应的代理实例。这个方法通常用于需要批量处理或遍历所有活跃组件的场景。
    /// </remarks>
    /// <returns>返回包含所有已激活组件代理实例的列表</returns>
    public List<IComponentAgent> GetActiveComponentAgents()
    {
        var agents = new List<IComponentAgent>();
        foreach (var kv in _componentsMap)
        {
            if (kv.Value.IsActive)
            {
                agents.Add(kv.Value.GetAgent());
            }
        }

        return agents;
    }

    /// <summary>
    /// 根据代理类型获取组件代理实例
    /// </summary>
    /// <param name="agentType">代理类型</param>
    /// <param name="isNew">当组件不存在时是否创建新实例,默认为true</param>
    /// <returns>返回指定类型的组件代理实例</returns>
    public async Task<IComponentAgent> GetComponentAgentAsync(Type agentType, bool isNew = true)
    {
        var compType = agentType.BaseType.GetGenericArguments()[0];
        BaseComponent component;
        if (isNew)
        {
            component = _componentsMap.GetOrAdd(compType, GetOrAddFactory);
        }
        else if (!_componentsMap.TryGetValue(compType, out component))
        {
            return default;
        }

        var agent = component.GetAgent(agentType);
        if (!component.IsActive)
        {
            await SendAsyncWithoutCheck(() => ActivateComponentAsync(component, agent, compType));
        }

        return agent;
    }

    /// <summary>
    /// 激活指定组件并依次调用其代理的激活生命周期方法
    /// </summary>
    /// <param name="component">要激活的组件实例</param>
    /// <param name="agent">组件对应的代理实例</param>
    /// <param name="compType">组件类型,用于日志记录</param>
    /// <returns>表示异步操作的任务</returns>
    private async Task ActivateComponentAsync(BaseComponent component, IComponentAgent agent, Type compType)
    {
        try
        {
            await component.ActiveAsync();
        }
        catch (Exception e)
        {
            // Localization: Core.Actor.ActiveComponentFailed - Actor激活组件失败 actorId:{0} 组件类型:{1} 异常：\n{2}
            LogHelper.Fatal(e, LocalizationService.GetString(Localization.Keys.Core.Actor.ActiveComponentFailed, Id, compType.FullName, e));
            if (GlobalSettings.CurrentSetting.IsDebug)
            {
                throw;
            }
        }

        try
        {
            await agent.BeforeActivationAsync();
            await agent.ActiveAsync();
            await agent.AfterActivationAsync();
        }
        catch (Exception e)
        {
            // Localization: Core.Actor.ActiveComponentFailed - Actor激活组件失败 actorId:{0} 组件类型:{1} 异常：\n{2}
            LogHelper.Fatal(e, LocalizationService.GetString(Localization.Keys.Core.Actor.ActiveComponentFailed, Id, compType.FullName, e));
            if (GlobalSettings.CurrentSetting.IsDebug)
            {
                throw;
            }
        }
    }

    /// <summary>
    /// 处理跨天逻辑,遍历所有组件并执行跨天操作
    /// </summary>
    /// <param name="serverDay">开服天数</param>
    public async Task CrossDayAsync(int serverDay)
    {
        // Localization: Core.Actor.CrossDay - actor跨天 id:{0} type:{1}
        LogHelper.Debug(LocalizationService.GetString(Localization.Keys.Core.Actor.CrossDay, Id, Type));
        foreach (var comp in _componentsMap.Values)
        {
            var agent = comp.GetAgent();
            if (agent is ICrossDay crossDay)
            {
                // 使用try-catch缩小异常影响范围
                try
                {
                    await crossDay.OnCrossDayAsync(serverDay);
                }
                catch (Exception e)
                {
                    // Localization: Core.Actor.CrossDayFailed - {0}跨天失败 actorId:{1} actorType:{2} 异常：\n{3}
                    LogHelper.Error(e, LocalizationService.GetString(Localization.Keys.Core.Actor.CrossDayFailed, agent.GetType().FullName, Id, Type, e));
                }
            }
        }
    }

    private readonly ConcurrentDictionary<string, object> _data = new ConcurrentDictionary<string, object>();


    /// <summary>
    /// 设置Actor的数据
    /// </summary>
    /// <typeparam name="T">要存储的数据类型</typeparam>
    /// <param name="key">数据的键名</param>
    /// <param name="value">要存储的数据值</param>
    /// <remarks>
    /// 用于在Actor中存储任意类型的数据，通过键值对的方式进行管理。
    /// 如果键已存在，则会覆盖原有的值。
    /// </remarks>
    public void SetData<T>(string key, T value)
    {
        _data[key] = value;
    }

    /// <summary>
    /// 获取Actor中存储的数据
    /// </summary>
    /// <typeparam name="T">要获取的数据类型</typeparam>
    /// <param name="key">数据的键名</param>
    /// <returns>返回指定类型的数据值</returns>
    /// <remarks>
    /// 如果指定的键不存在或类型不匹配，可能会抛出异常。
    /// 使用前建议先确认数据是否存在。
    /// </remarks>
    public T GetData<T>(string key)
    {
        if (_data.TryGetValue(key, out var value))
        {
            return (T)value;
        }

        return default;
    }


    /// <summary>
    /// 移除Actor中存储的数据
    /// </summary>
    /// <param name="key">要移除的数据键名</param>
    /// <returns>如果成功移除数据返回true，如果键不存在返回false</returns>
    /// <remarks>
    /// 从Actor的数据存储中移除指定键的数据。
    /// 如果键不存在，则不会产生任何效果。
    /// </remarks>
    public bool RemoveData(string key)
    {
        return _data.TryRemove(key, out _);
    }

    /// <summary>
    /// 清除Actor中存储的所有数据
    /// </summary>
    /// <remarks>
    /// 该方法会清空Actor中所有通过SetData方法存储的数据。
    /// 清除后所有数据将无法恢复，请谨慎使用。
    /// 通常在Actor被回收或重置时调用此方法。
    /// </remarks>
    public void ClearData()
    {
        _data.Clear();
    }

    /// <summary>
    /// Actor 回收时的处理方法
    /// </summary>
    /// <remarks>
    /// 当 Actor 被系统回收时调用此方法，固定两步：
    /// 1. 依次执行一次性回收回调（单个回调异常记日志后继续）；
    /// 2. 删除 <see cref="ScheduleIdSet"/> 中仍登记的全部 Quartz 定时任务并清空集合。
    /// 第 2 步保证"Actor 出表 ⇒ 名下定时任务全部删除"：遗留任务触发时找不到 Actor，不会再把已回收的 Actor
    /// 拉起。调度器未启动或已关闭时任务已不存在，只清空集合；删除异常记日志，不向回收流程外泄。
    /// Runs the once recycle callbacks, then deletes every Quartz job still registered in
    /// <see cref="ScheduleIdSet"/> and clears the set, so no timer outlives its actor.
    /// </remarks>
    /// <returns>表示异步操作的任务</returns>
    public async Task OnRecycleAsync()
    {
        try
        {
            while (_onceRecycleCallbacks.First != null)
            {
                var first = _onceRecycleCallbacks.First;
                _onceRecycleCallbacks.RemoveFirst();
                try
                {
                    first.Value?.Invoke();
                }
                catch (Exception ex)
                {
                    // 记录回调执行异常但继续执行其他回调
                    // Localization: Core.Actor.RecycleCallbackFailed - Actor回收回调执行异常 actorId:{0} actorType:{1} 异常：\n{2}
                    LogHelper.Error(ex, LocalizationService.GetString(Localization.Keys.Core.Actor.RecycleCallbackFailed, Id, Type, ex));
                }
            }
        }
        catch (Exception ex)
        {
            // 记录整体执行异常
            // Localization: Core.Actor.RecycleFailed - Actor回收过程异常 actorId:{0} actorType:{1} 异常：\n{2}
            LogHelper.Error(ex, LocalizationService.GetString(Localization.Keys.Core.Actor.RecycleFailed, Id, Type, ex));
        }
        finally
        {
            _onceRecycleCallbacks.Clear();
        }

        await RemoveScheduledJobsAsync();
    }

    /// <summary>
    /// 删除本 Actor 名下仍登记的全部定时任务并清空 <see cref="ScheduleIdSet"/>。
    /// </summary>
    /// <remarks>
    /// Deletes every job still registered in <see cref="ScheduleIdSet"/> and clears the set.
    /// </remarks>
    private async Task RemoveScheduledJobsAsync()
    {
        if (ScheduleIdSet.Count == 0)
        {
            return;
        }

        try
        {
            if (QuartzTimer.IsSchedulerRunning)
            {
                await QuartzTimer.RemoveAsync(ScheduleIdSet);
            }
        }
        catch (Exception ex)
        {
            // Localization: Core.Actor.RecycleFailed - Actor回收过程异常 actorId:{0} actorType:{1} 异常：\n{2}
            LogHelper.Error(ex, LocalizationService.GetString(Localization.Keys.Core.Actor.RecycleFailed, Id, Type, ex));
        }
        finally
        {
            ScheduleIdSet.Clear();
        }
    }

    private readonly LinkedList<Action> _onceRecycleCallbacks = new LinkedList<Action>();

    /// <summary>
    /// 添加一个在Actor回收时执行一次的回调事件
    /// </summary>
    /// <param name="action">要执行的回调方法</param>
    /// <remarks>
    /// 该回调事件只会在Actor被回收时触发一次，之后会自动移除。
    /// 通常用于:
    /// - 执行一次性的清理操作
    /// - 触发状态变更通知
    /// - 记录回收日志等场景
    /// </remarks>
    public void AddOnceRecycleCallback(Action action)
    {
        _onceRecycleCallbacks.AddLast(action);
    }

    /// <summary>
    /// 反激活所有组件,使其进入非活动状态
    /// </summary>
    /// <remarks>
    /// 每个组件单独隔离：某个组件反激活抛异常时记 Fatal 后继续反激活其余组件，本方法不向外抛出。
    /// 组件自身保证失败时 <see cref="BaseComponent.IsActive"/> 仍落为 false，因此单个组件故障不会让
    /// Actor 永远无法被空闲回收，也不会让后续组件的状态登记残留。
    /// Each component is isolated: a failing component is logged as Fatal and the rest still
    /// deactivate; this method never throws.
    /// </remarks>
    public async Task InactiveAsync()
    {
        foreach (var item in _componentsMap.Values)
        {
            try
            {
                await item.InactiveAsync();
            }
            catch (Exception e)
            {
                // Localization: Core.Actor.InactiveComponentFailed - Actor反激活组件失败 actorId:{0} 组件类型:{1} 异常：\n{2}
                LogHelper.Fatal(e, LocalizationService.GetString(Localization.Keys.Core.Actor.InactiveComponentFailed, Id, item.GetType().FullName, e));
            }
        }
    }

    /// <summary>
    /// 清理所有组件的缓存代理实例
    /// </summary>
    public void ClearAgent()
    {
        foreach (var comp in _componentsMap.Values)
        {
            comp.ClearCacheAgent();
        }
    }

    /// <summary>
    /// 创建或获取指定类型的组件实例
    /// </summary>
    /// <param name="type">组件类型</param>
    /// <returns>返回基础组件实例</returns>
    private BaseComponent GetOrAddFactory(Type type)
    {
        return ComponentRegister.CreateComponent(this, type);
    }

    /// <summary>
    /// 保存所有组件的当前状态
    /// </summary>
    internal async Task SaveAllStateAsync()
    {
        foreach (var item in _componentsMap)
        {
            await item.Value.WriteStateAsync();
        }
    }

    /// <summary>
    /// 重写ToString方法,返回Actor的标识信息
    /// </summary>
    /// <returns>返回包含类型和ID的字符串表示</returns>
    public override string ToString()
    {
        return $"{base.ToString()}_{Type}_{Id}";
    }

    #region actor 入队

    /// <summary>
    /// 发送无返回值的工作指令到Actor队列
    /// </summary>
    /// <param name="work">要执行的工作内容</param>
    /// <param name="timeOut">执行执行超时时间（毫秒）,默认为-1,将采用配置时间ActorTimeOut</param>
    /// <param name="cancellationToken">取消操作的令牌</param>
    public void Tell(Action work, int timeOut = -1, CancellationToken cancellationToken = default)
    {
        if (timeOut <= 0)
        {
            timeOut = GlobalSettings.CurrentSetting.ActorTimeOut;
        }

        WorkerActor.Tell(work, timeOut, cancellationToken);
    }

    /// <summary>
    /// 发送异步工作指令到Actor队列
    /// </summary>
    /// <param name="work">要执行的异步工作内容</param>
    /// <param name="timeOut">执行执行超时时间（毫秒）,默认为-1,将采用配置时间ActorTimeOut</param>
    /// <param name="cancellationToken">取消操作的令牌</param>
    public void Tell(Func<Task> work, int timeOut = -1, CancellationToken cancellationToken = default)
    {
        if (timeOut <= 0)
        {
            timeOut = GlobalSettings.CurrentSetting.ActorTimeOut;
        }

        WorkerActor.Tell(work, timeOut, cancellationToken);
    }

    /// <summary>
    /// 发送无返回值的异步工作指令
    /// </summary>
    /// <param name="work">要执行的工作内容</param>
    /// <returns>返回表示异步操作的Task</returns>
    public Task SendAsync(Action work)
    {
        return WorkerActor.SendAsync(work);
    }

    /// <summary>
    /// 发送带超时的异步工作指令
    /// </summary>
    /// <param name="work">要执行的工作内容</param>
    /// <param name="timeOut">执行超时时间（毫秒）,默认为-1,将采用配置时间ActorTimeOut</param>
    /// <param name="cancellationToken">取消操作的令牌</param>
    /// <returns>返回表示异步操作的Task</returns>
    public Task SendAsync(Action work, int timeOut = -1, CancellationToken cancellationToken = default)
    {
        if (timeOut <= 0)
        {
            timeOut = GlobalSettings.CurrentSetting.ActorTimeOut;
        }

        return WorkerActor.SendAsync(work, timeOut, cancellationToken);
    }

    /// <summary>
    /// 发送带返回值的异步工作指令
    /// </summary>
    /// <typeparam name="T">返回值类型</typeparam>
    /// <param name="work">要执行的工作内容</param>
    /// <param name="timeOut">执行超时时间（毫秒）,默认为-1,将采用配置时间ActorTimeOut</param>
    /// <param name="cancellationToken">取消操作的令牌</param>
    /// <returns>返回指定类型的异步操作结果</returns>
    public Task<T> SendAsync<T>(Func<T> work, int timeOut = -1, CancellationToken cancellationToken = default)
    {
        if (timeOut <= 0)
        {
            timeOut = GlobalSettings.CurrentSetting.ActorTimeOut;
        }

        return WorkerActor.SendAsync(work, timeOut, cancellationToken);
    }

    /// <summary>
    /// 发送带锁检查的异步工作指令
    /// </summary>
    /// <param name="work">要执行的异步工作内容</param>
    /// <param name="timeOut">执行超时时间（毫秒）,默认为-1,将采用配置时间ActorTimeOut</param>
    /// <param name="checkLock">是否检查锁,默认为true</param>
    /// <param name="cancellationToken">取消操作的令牌</param>
    /// <returns>返回表示异步操作的Task</returns>
    public Task SendAsync(Func<Task> work, int timeOut = -1, bool checkLock = true, CancellationToken cancellationToken = default)
    {
        if (timeOut <= 0)
        {
            timeOut = GlobalSettings.CurrentSetting.ActorTimeOut;
        }

        return WorkerActor.SendAsync(work, timeOut, checkLock, cancellationToken);
    }

    /// <summary>
    /// 发送不检查锁的异步工作指令
    /// </summary>
    /// <param name="work">要执行的异步工作内容</param>
    /// <param name="timeout">执行超时时间（毫秒）,默认为-1,将采用配置时间ActorTimeOut</param>
    /// <param name="cancellationToken">取消操作的令牌</param>
    /// <returns>返回表示异步操作的Task</returns>
    public Task SendAsyncWithoutCheck(Func<Task> work, int timeout = -1, CancellationToken cancellationToken = default)
    {
        if (timeout <= 0)
        {
            timeout = GlobalSettings.CurrentSetting.ActorTimeOut;
        }

        return WorkerActor.SendAsync(work, timeout, false, cancellationToken);
    }

    /// <summary>
    /// 发送带返回值的异步工作指令
    /// </summary>
    /// <typeparam name="T">返回值类型</typeparam>
    /// <param name="work">要执行的异步工作内容</param>
    /// <param name="timeOut">执行超时时间（毫秒）,默认为-1,将采用配置时间ActorTimeOut</param>
    /// <param name="cancellationToken">取消操作的令牌</param>
    /// <returns>返回指定类型的异步操作结果</returns>
    public Task<T> SendAsync<T>(Func<Task<T>> work, int timeOut = -1, CancellationToken cancellationToken = default)
    {
        if (timeOut <= 0)
        {
            timeOut = GlobalSettings.CurrentSetting.ActorTimeOut;
        }

        return WorkerActor.SendAsync(work, timeOut, cancellationToken);
    }

    #endregion

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        ClearAgent();
        ClearData();
    }
}