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
using GameFrameX.Core.Actors;
using GameFrameX.Core.Hotfix;
using GameFrameX.Core.Timer.Handler;
using GameFrameX.Core.Utility;
using GameFrameX.Foundation.Logger;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Utility;
using Quartz;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GameFrameX.Core.Timer;

/// <summary>
/// Quartz定时器
/// 提供基于Quartz的定时任务调度功能,支持热更新和非热更新两种模式
/// </summary>
public static class QuartzTimer
{
    /// <summary>
    /// 统计工具实例,用于记录定时器的使用情况
    /// </summary>
    private static readonly StatisticsTool StatisticsTool = new();

    /// <summary>
    /// 删除指定的定时任务
    /// </summary>
    /// <param name="id">要删除的定时任务ID</param>
    public static void Remove(long id)
    {
        if (id <= 0)
        {
            return;
        }

        Scheduler.DeleteJob(new JobKey(id.ToString()));
    }

    /// <summary>
    /// 批量删除定时任务
    /// </summary>
    /// <param name="set">要删除的定时任务ID集合</param>
    public static void Remove(IEnumerable<long> set)
    {
        foreach (var id in set)
        {
            Remove(id);
        }
    }

    /// <summary>
    /// 暂停指定的定时任务
    /// </summary>
    /// <param name="id">要暂停的定时任务ID</param>
    public static void Pause(long id)
    {
        if (id <= 0)
        {
            return;
        }

        Scheduler.PauseJob(new JobKey(id.ToString()));
    }

    /// <summary>
    /// 批量暂停定时任务
    /// </summary>
    /// <param name="set">要暂停的定时任务ID集合</param>
    public static void Pause(IEnumerable<long> set)
    {
        foreach (var id in set)
        {
            Pause(id);
        }
    }

    /// <summary>
    /// 恢复指定的定时任务
    /// </summary>
    /// <param name="id">要恢复的定时任务ID</param>
    public static void Resume(long id)
    {
        if (id <= 0)
        {
            return;
        }

        Scheduler.ResumeJob(new JobKey(id.ToString()));
    }

    /// <summary>
    /// 批量恢复定时任务
    /// </summary>
    /// <param name="set">要恢复的定时任务ID集合</param>
    public static void Resume(IEnumerable<long> set)
    {
        foreach (var id in set)
        {
            Resume(id);
        }
    }

    #region 热更定时器

    /// <summary>
    /// 每隔一段时间执行一次的定时任务
    /// </summary>
    /// <typeparam name="T">定时器处理器类型,必须实现ITimerHandler接口</typeparam>
    /// <param name="actorId">Actor的唯一标识,用于定位执行任务的Actor</param>
    /// <param name="delay">首次执行前的延迟时间</param>
    /// <param name="interval">每次执行之间的间隔时间</param>
    /// <param name="eventArgs">传递给定时器处理器的自定义参数</param>
    /// <param name="repeatCount">循环次数,设置为-1表示无限循环执行</param>
    /// <param name="isMissFire">是否允许错过执行</param>
    /// <returns>生成的定时任务ID,可用于后续管理该任务</returns>
    public static long Schedule<T>(long actorId, TimeSpan delay, TimeSpan interval, GameEventArgs eventArgs = null, int repeatCount = -1, bool isMissFire = true) where T : ITimerHandler
    {
        var nextId = NextId();
        var firstTimeOffset = DateTimeOffset.Now.Add(delay);
        var schedule = SimpleScheduleBuilder.Create().WithInterval(interval);
        if (repeatCount < 0)
        {
            schedule = schedule.RepeatForever();
        }
        else
        {
            schedule = schedule.WithRepeatCount(repeatCount);
        }

        if (isMissFire)
        {
            // 与旧版 WithMisfireHandlingInstructionIgnoreMisfires 行为一致
            // Matches the previous WithMisfireHandlingInstructionIgnoreMisfires behavior
            schedule = schedule.WithMisfireInstruction(SimpleTriggerMisfireInstruction.IgnoreMisfires);
        }

        var trigger = TriggerBuilder.Create(TimeProvider.System).StartAt(firstTimeOffset).WithSchedule(schedule).Build();
        Scheduler.ScheduleJob(GetJobDetail<T>(nextId, actorId, eventArgs), trigger);
        return nextId;
    }

    /// <summary>
    /// 延迟执行一次的定时任务
    /// </summary>
    /// <typeparam name="T">定时器处理器类型,必须实现ITimerHandler接口</typeparam>
    /// <param name="actorId">Actor的唯一标识,用于定位执行任务的Actor</param>
    /// <param name="delay">延迟执行的时间</param>
    /// <param name="eventArgs">传递给定时器处理器的自定义参数</param>
    /// <returns>生成的定时任务ID,可用于后续管理该任务</returns>
    public static long Delay<T>(long actorId, TimeSpan delay, GameEventArgs eventArgs = null) where T : ITimerHandler
    {
        var nextId = NextId();
        var firstTimeOffset = DateTimeOffset.Now.Add(delay);
        var trigger = TriggerBuilder.Create(TimeProvider.System).StartAt(firstTimeOffset).WithSchedule(SimpleScheduleBuilder.Create().WithMisfireInstruction(SimpleTriggerMisfireInstruction.NextWithRemainingCount)).Build();
        Scheduler.ScheduleJob(GetJobDetail<T>(nextId, actorId, eventArgs), trigger);
        return nextId;
    }

    /// <summary>
    /// 基于Cron表达式的定时任务
    /// </summary>
    /// <typeparam name="T">定时器处理器类型,必须实现ITimerHandler接口</typeparam>
    /// <param name="actorId">Actor的唯一标识,用于定位执行任务的Actor</param>
    /// <param name="cronExpression">标准的Cron表达式,用于配置复杂的执行时间规则</param>
    /// <param name="eventArgs">传递给定时器处理器的自定义参数</param>
    /// <returns>生成的定时任务ID,可用于后续管理该任务</returns>
    public static long WithCronExpression<T>(long actorId, string cronExpression, GameEventArgs eventArgs = null) where T : ITimerHandler
    {
        var nextId = NextId();
        var trigger = TriggerBuilder.Create(TimeProvider.System).StartNow().WithSchedule(CronScheduleBuilder.Create(cronExpression)).Build();
        Scheduler.ScheduleJob(GetJobDetail<T>(nextId, actorId, eventArgs), trigger);
        return nextId;
    }

    /// <summary>
    /// 每日定时执行的任务
    /// </summary>
    /// <typeparam name="T">定时器处理器类型,必须实现ITimerHandler接口</typeparam>
    /// <param name="actorId">Actor的唯一标识,用于定位执行任务的Actor</param>
    /// <param name="hour">指定执行的小时(0-23)</param>
    /// <param name="minute">指定执行的分钟(0-59)</param>
    /// <param name="eventArgs">传递给定时器处理器的自定义参数</param>
    /// <returns>生成的定时任务ID,可用于后续管理该任务</returns>
    /// <exception cref="ArgumentOutOfRangeException">当hour或minute参数超出有效范围时抛出</exception>
    public static long Daily<T>(long actorId, int hour, int minute, GameEventArgs eventArgs = null) where T : ITimerHandler
    {
        if (hour < 0 || hour >= 24 || minute < 0 || minute >= 60)
        {
            throw new ArgumentOutOfRangeException($"定时器参数错误 TimerHandler:{typeof(T).FullName} {nameof(hour)}:{hour} {nameof(minute)}:{minute}");
        }

        var nextId = NextId();
        var trigger = TriggerBuilder.Create(TimeProvider.System).StartNow().WithSchedule(CronScheduleBuilder.Create($"0 {minute} {hour} * * ?")).Build();
        Scheduler.ScheduleJob(GetJobDetail<T>(nextId, actorId, eventArgs), trigger);
        return nextId;
    }

    /// <summary>
    /// 在每周指定的多个日期执行的定时任务
    /// </summary>
    /// <typeparam name="T">定时器处理器类型,必须实现ITimerHandler接口</typeparam>
    /// <param name="actorId">Actor的唯一标识,用于定位执行任务的Actor</param>
    /// <param name="hour">指定执行的小时(0-23)</param>
    /// <param name="minute">指定执行的分钟(0-59)</param>
    /// <param name="gameEventArgs">传递给定时器处理器的自定义参数</param>
    /// <param name="dayOfWeeks">指定要执行的星期几,可以指定多个</param>
    /// <returns>生成的定时任务ID,可用于后续管理该任务</returns>
    /// <exception cref="ArgumentNullException">当dayOfWeeks参数为空或长度为0时抛出</exception>
    public static long WithDayOfWeeks<T>(long actorId, int hour, int minute, GameEventArgs gameEventArgs, params DayOfWeek[] dayOfWeeks) where T : ITimerHandler
    {
        if (dayOfWeeks == null || dayOfWeeks.Length <= 0)
        {
            throw new ArgumentNullException($"定时每周执行 参数为空：{nameof(dayOfWeeks)} TimerHandler:{typeof(T).FullName} actorId:{actorId} actorType:{ActorIdGenerator.GetActorType(actorId)}");
        }

        var nextId = NextId();
        var daysOfWeek = string.Join(",", dayOfWeeks.Select(ToCronDayOfWeek));
        var trigger = TriggerBuilder.Create(TimeProvider.System).StartNow().WithSchedule(CronScheduleBuilder.Create($"0 {minute} {hour} ? * {daysOfWeek}")).Build();
        Scheduler.ScheduleJob(GetJobDetail<T>(nextId, actorId, gameEventArgs), trigger);
        return nextId;
    }

    /// <summary>
    /// 每周固定某天执行的定时任务
    /// </summary>
    /// <typeparam name="T">定时器处理器类型,必须实现ITimerHandler接口</typeparam>
    /// <param name="actorId">Actor的唯一标识,用于定位执行任务的Actor</param>
    /// <param name="dayOfWeek">指定执行的星期几</param>
    /// <param name="hour">指定执行的小时(0-23)</param>
    /// <param name="minute">指定执行的分钟(0-59)</param>
    /// <param name="gameEventArgs">传递给定时器处理器的自定义参数</param>
    /// <returns>生成的定时任务ID,可用于后续管理该任务</returns>
    public static long Weekly<T>(long actorId, DayOfWeek dayOfWeek, int hour, int minute, GameEventArgs gameEventArgs = null) where T : ITimerHandler
    {
        var nextId = NextId();
        var trigger = TriggerBuilder.Create(TimeProvider.System).StartNow().WithSchedule(CronScheduleBuilder.Create($"0 {minute} {hour} ? * {ToCronDayOfWeek(dayOfWeek)}")).Build();
        Scheduler.ScheduleJob(GetJobDetail<T>(nextId, actorId, gameEventArgs), trigger);
        return nextId;
    }

    /// <summary>
    /// 每月固定某天执行的定时任务
    /// </summary>
    /// <typeparam name="T">定时器处理器类型,必须实现ITimerHandler接口</typeparam>
    /// <param name="actorId">Actor的唯一标识,用于定位执行任务的Actor</param>
    /// <param name="dayOfMonth">指定执行的日期(1-31)</param>
    /// <param name="hour">指定执行的小时(0-23)</param>
    /// <param name="minute">指定执行的分钟(0-59)</param>
    /// <param name="gameEventArgs">传递给定时器处理器的自定义参数</param>
    /// <returns>生成的定时任务ID,可用于后续管理该任务</returns>
    /// <exception cref="ArgumentException">当dayOfMonth参数超出有效范围时抛出</exception>
    public static long Monthly<T>(long actorId, int dayOfMonth, int hour, int minute, GameEventArgs gameEventArgs = null) where T : ITimerHandler
    {
        if (dayOfMonth is < 0 or > 31)
        {
            throw new ArgumentException($"定时器参数错误 TimerHandler:{typeof(T).FullName} {nameof(dayOfMonth)}:{dayOfMonth} actorId:{actorId} actorType:{ActorIdGenerator.GetActorType(actorId)}");
        }

        var nextId = NextId();
        var trigger = TriggerBuilder.Create(TimeProvider.System).StartNow().WithSchedule(CronScheduleBuilder.Create($"0 {minute} {hour} {dayOfMonth} * ?")).Build();
        Scheduler.ScheduleJob(GetJobDetail<T>(nextId, actorId, gameEventArgs), trigger);
        return nextId;
    }

    /// <summary>
    /// 将 System.DayOfWeek 转换为 Quartz Cron 的星期表达式
    /// </summary>
    /// <param name="dayOfWeek">星期几 / Day of week</param>
    /// <returns>Cron 星期表达式（MON-SUN）/ Cron day-of-week token (MON-SUN)</returns>
    private static string ToCronDayOfWeek(DayOfWeek dayOfWeek)
    {
        return dayOfWeek switch
        {
            DayOfWeek.Sunday => "SUN",
            DayOfWeek.Monday => "MON",
            DayOfWeek.Tuesday => "TUE",
            DayOfWeek.Wednesday => "WED",
            DayOfWeek.Thursday => "THU",
            DayOfWeek.Friday => "FRI",
            _ => "SAT",
        };
    }

    #endregion 热更定时器

    /*

    #region 非热更定时器

    /// <summary>
    /// 每隔一段时间执行一次
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="delay"></param>
    /// <param name="interval"></param>
    /// <param name="param"></param>
    /// <param name="repeatCount"> -1 表示永远 </param>
    /// <returns></returns>
    public static long Schedule<T>(TimeSpan delay, TimeSpan interval, Param param = null, int repeatCount = -1) where T : NotHotfixTimerHandler
    {
        var            nextId          = NextId();
        var            firstTimeOffset = DateTimeOffset.Now.Add(delay);
        TriggerBuilder builder;
        if (repeatCount < 0)
        {
            builder = TriggerBuilder.Create().StartAt(firstTimeOffset).WithSimpleSchedule(x => x.WithInterval(interval).RepeatForever());
        }
        else
        {
            builder = TriggerBuilder.Create().StartAt(firstTimeOffset).WithSimpleSchedule(x => x.WithInterval(interval).WithRepeatCount(repeatCount));
        }

        _scheduler.ScheduleJob(GetJobDetail<T>(nextId, param), builder.Build());
        return nextId;
    }

    /// <summary>
    /// 基于时间delay
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="delay"></param>
    /// <param name="param"></param>
    /// <returns></returns>
    public static long Delay<T>(TimeSpan delay, Param param = null) where T : NotHotfixTimerHandler
    {
        var nextId          = NextId();
        var firstTimeOffset = DateTimeOffset.Now.Add(delay);
        var trigger         = TriggerBuilder.Create().StartAt(firstTimeOffset).Build();
        _scheduler.ScheduleJob(GetJobDetail<T>(nextId, param), trigger);
        return nextId;
    }

    /// <summary>
    /// 基于cron表达式
    /// </summary>
    /// <param name="cronExpression"></param>
    /// <param name="param"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public static long WithCronExpression<T>(string cronExpression, Param param = null) where T : NotHotfixTimerHandler
    {
        var nextId  = NextId();
        var trigger = TriggerBuilder.Create().StartNow().WithCronSchedule(cronExpression).Build();
        _scheduler.ScheduleJob(GetJobDetail<T>(nextId, param), trigger);
        return nextId;
    }

    /// <summary>
    /// 每日
    /// </summary>
    /// <param name="hour"></param>
    /// <param name="minute"></param>
    /// <param name="param"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    public static long Daily<T>(int hour, int minute, Param param = null) where T : NotHotfixTimerHandler
    {
        if (hour < 0 || hour >= 24 || minute < 0 || minute >= 60)
        {
            throw new ArgumentOutOfRangeException($"定时器参数错误 TimerHandler:{typeof(T).FullName} {nameof(hour)}:{hour} {nameof(minute)}:{minute}");
        }

        var nextId  = NextId();
        var trigger = TriggerBuilder.Create().StartNow().WithSchedule(CronScheduleBuilder.DailyAtHourAndMinute(hour, minute)).Build();
        _scheduler.ScheduleJob(GetJobDetail<T>(nextId, param), trigger);
        return nextId;
    }

    /// <summary>
    /// 每周某些天
    /// </summary>
    /// <param name="hour"></param>
    /// <param name="minute"></param>
    /// <param name="param"></param>
    /// <param name="dayOfWeeks"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    /// <exception cref="ArgumentNullException"></exception>
    public static long WithDayOfWeeks<T>(int hour, int minute, Param param, params DayOfWeek[] dayOfWeeks) where T : NotHotfixTimerHandler
    {
        if (dayOfWeeks == null || dayOfWeeks.Length <= 0)
        {
            throw new ArgumentNullException($"定时每周执行 参数为空：{nameof(dayOfWeeks)} TimerHandler:{typeof(T).FullName}");
        }

        var nextId  = NextId();
        var trigger = TriggerBuilder.Create().StartNow().WithSchedule(CronScheduleBuilder.AtHourAndMinuteOnGivenDaysOfWeek(hour, minute, dayOfWeeks)).Build();
        _scheduler.ScheduleJob(GetJobDetail<T>(nextId, param), trigger);
        return nextId;
    }

    /// <summary>
    /// 每周某天
    /// </summary>
    /// <param name="dayOfWeek"></param>
    /// <param name="hour"></param>
    /// <param name="minute"></param>
    /// <param name="param"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public static long Weekly<T>(DayOfWeek dayOfWeek, int hour, int minute, Param param = null) where T : NotHotfixTimerHandler
    {
        var nextId  = NextId();
        var trigger = TriggerBuilder.Create().StartNow().WithSchedule(CronScheduleBuilder.WeeklyOnDayAndHourAndMinute(dayOfWeek, hour, minute)).Build();
        _scheduler.ScheduleJob(GetJobDetail<T>(nextId, param), trigger);
        return nextId;
    }

    /// <summary>
    /// 每月某天
    /// </summary>
    /// <param name="dayOfMonth"></param>
    /// <param name="hour"></param>
    /// <param name="minute"></param>
    /// <param name="param"></param>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    public static long Monthly<T>(int dayOfMonth, int hour, int minute, Param param = null) where T : NotHotfixTimerHandler
    {
        if (dayOfMonth is < 0 or > 31)
        {
            throw new ArgumentException($"定时器参数错误 TimerHandler:{typeof(T).FullName} {nameof(dayOfMonth)}:{dayOfMonth}");
        }

        var nextId  = NextId();
        var trigger = TriggerBuilder.Create().StartNow().WithSchedule(CronScheduleBuilder.MonthlyOnDayAndHourAndMinute(dayOfMonth, hour, minute)).Build();
        _scheduler.ScheduleJob(GetJobDetail<T>(nextId, param), trigger);
        return nextId;
    }

    #endregion 非热更定时器

    */

    #region 调度

    /// <summary>
    /// Quartz调度器实例
    /// </summary>
    private static readonly Lazy<Task<IScheduler>> SchedulerInitializer = new(CreateSchedulerAsync);

    /// <summary>
    /// Quartz调度器实例
    /// </summary>
    private static IScheduler _scheduler;

    /// <summary>
    /// 已启动的 Quartz 调度器实例。
    /// </summary>
    private static IScheduler Scheduler
    {
        get
        {
            if (_scheduler == null)
            {
                throw new InvalidOperationException("QuartzTimer.Start must be awaited before using timer operations.");
            }

            return _scheduler;
        }
    }

    /// <summary>
    /// 初始化定时任务调度器
    /// </summary>
    /// <remarks>
    /// 设置日志提供程序并创建调度器实例
    /// </remarks>
    private static async Task<IScheduler> CreateSchedulerAsync()
    {
        // Quartz 4 移除了 Quartz.Logging 抽象与 StdSchedulerFactory：
        // 日志改经 Microsoft.Extensions.Logging 桥接到 LogHelper，调度器改由 QuartzSchedulerBuilder 构建，
        // 并把容器内日志与无法注入日志的站点统一指向 Quartz.Diagnostics.LogProvider
        // (Quartz 4 removed Quartz.Logging and StdSchedulerFactory; logging now bridges to LogHelper via
        // Microsoft.Extensions.Logging, the scheduler is built by QuartzSchedulerBuilder, and both the
        // container's loggers and non-injectable sites point at Quartz.Diagnostics.LogProvider).
        Quartz.Diagnostics.LogProvider.SetLogProvider(new QuartzLogBridge());
        var factory = QuartzSchedulerBuilder.Create(builder => builder.Services.AddSingleton<ILoggerFactory>(new QuartzLogBridge()))
            .Build();
        return await factory.GetScheduler();
    }

    /// <summary>
    /// 启动定时任务调度器
    /// </summary>
    /// <returns>表示异步操作的任务</returns>
    /// <remarks>
    /// 启动调度器,使其开始处理已安排的任务
    /// </remarks>
    public static async Task Start()
    {
        _scheduler = await SchedulerInitializer.Value;
        if (_scheduler.Status != SchedulerStatus.Running)
        {
            await _scheduler.Start();
        }
    }

    /// <summary>
    /// 停止定时任务调度器
    /// </summary>
    /// <returns>表示异步操作的任务</returns>
    /// <remarks>
    /// 关闭调度器,停止所有正在运行的任务,并阻止新任务的触发
    /// </remarks>
    public static Task Stop()
    {
        if (!SchedulerInitializer.IsValueCreated && _scheduler == null)
        {
            return Task.CompletedTask;
        }

        return StopAsync();
    }

    private static async Task StopAsync()
    {
        var scheduler = _scheduler ?? await SchedulerInitializer.Value;
        if (scheduler.Status is not (SchedulerStatus.Shutdown or SchedulerStatus.ShuttingDown))
        {
            await scheduler.Shutdown();
        }
    }

    /// <summary>
    /// 任务ID生成器的初始值,基于当前时间的Ticks
    /// </summary>
    private static long _id = DateTime.UtcNow.Ticks;

    /// <summary>
    /// 生成下一个任务ID
    /// </summary>
    /// <returns>新的任务ID</returns>
    private static long NextId()
    {
        return Interlocked.Increment(ref _id);
    }

    /// <summary>
    /// 定时器任务执行助手类
    /// 负责执行定时任务并处理异常情况
    /// </summary>
    private sealed class TimerJobHelper : IJob
    {
        /// <summary>
        /// 执行定时任务
        /// </summary>
        /// <param name="context">任务执行上下文 / Job execution context</param>
        /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
        /// <returns>表示异步操作的任务 / A task representing the asynchronous operation</returns>
        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var handlerType = context.JobDetail.JobDataMap.GetString(TimerKey);
            try
            {
                if (context.JobDetail.JobDataMap.TryGetValue(ParamKey, out var param))
                {
                    var handler = HotfixManager.GetInstance<ITimerHandler>(handlerType);
                    if (handler != null)
                    {
                        var actorId = context.JobDetail.JobDataMap.GetLong(ActorIdKey);
                        var baseType = handler.GetType().BaseType;
                        if (baseType != null && baseType.GenericTypeArguments.Length > 0)
                        {
                            var agentType = baseType.GenericTypeArguments[0];
                            var componentAgent = await ActorManager.GetComponentAgent(actorId, agentType);
                            if (componentAgent != null)
                            {
                                GameEventArgs eventArgs = param as GameEventArgs;
                                componentAgent.Tell(() => handler.InnerHandleTimer(componentAgent, eventArgs));
                                return;
                            }
                        }
                    }
                }

                LogHelper.Error(LocalizationService.GetString(Localization.Keys.Core.Timer.InvalidHandlerType, handlerType));
            }
            catch (Exception e)
            {
                LogHelper.Error(e.ToString());
            }
        }
    }

    /// <summary>
    /// 任务参数的键名
    /// </summary>
    public const string ParamKey = "param";

    /// <summary>
    /// Actor ID的键名
    /// </summary>
    private const string ActorIdKey = "actor_id";

    /// <summary>
    /// 定时器类型的键名
    /// </summary>
    private const string TimerKey = "timer";

    /// <summary>
    /// 创建热更新定时任务的作业详情
    /// </summary>
    /// <typeparam name="T">定时器处理器类型</typeparam>
    /// <param name="id">任务ID</param>
    /// <param name="actorId">Actor ID</param>
    /// <param name="eventArgs">任务参数</param>
    /// <returns>作业详情对象</returns>
    /// <exception cref="Exception">当定时器不在热更新程序集中时抛出</exception>
    private static IJobDetail GetJobDetail<T>(long id, long actorId, GameEventArgs eventArgs) where T : ITimerHandler
    {
        var handlerType = typeof(T);
        StatisticsTool.Count(handlerType.FullName);
        if (handlerType.Assembly != HotfixManager.HotfixAssembly)
        {
            throw new Exception("定时器代码需要在热更项目里");
        }

        var job = JobBuilder.Create<TimerJobHelper>().WithIdentity(id + string.Empty).Build();
        if (eventArgs == null)
        {
            eventArgs = GameEmptyEventArgs.EmptyEventArgs;
        }

        job.JobDataMap.Add(ParamKey, eventArgs);
        job.JobDataMap.Add(ActorIdKey, actorId);
        job.JobDataMap.Add(TimerKey, handlerType.FullName);
        return job;
    }

    /// <summary>
    /// 创建非热更新定时任务的作业详情
    /// </summary>
    /// <typeparam name="T">定时器处理器类型</typeparam>
    /// <param name="id">任务ID</param>
    /// <param name="gameEventArgs">任务参数</param>
    /// <returns>作业详情对象</returns>
    private static IJobDetail GetJobDetail<T>(long id, GameEventArgs gameEventArgs) where T : NotHotfixTimerHandler
    {
        StatisticsTool.Count(typeof(T).FullName);
        var job = JobBuilder.Create<T>().WithIdentity(id + string.Empty).Build();
        if (gameEventArgs == null)
        {
            gameEventArgs = GameEmptyEventArgs.EmptyEventArgs;
        }

        job.JobDataMap.Add(ParamKey, gameEventArgs);
        return job;
    }

    /// <summary>
    /// Quartz 日志桥接工厂（Quartz 4 使用 Microsoft.Extensions.Logging 记录日志）。
    /// 将 Warning 及以上级别路由到 LogHelper，低于 Warning 的级别忽略，保持原有日志行为。
    /// Quartz log bridge factory (Quartz 4 logs via Microsoft.Extensions.Logging).
    /// Routes Warning and above to LogHelper and ignores lower levels, preserving the previous behavior.
    /// </summary>
    private sealed class QuartzLogBridge : ILoggerFactory
    {
        /// <summary>
        /// 创建日志桥接实例
        /// </summary>
        /// <param name="categoryName">日志类别名称 / Log category name</param>
        /// <returns>日志桥接实例 / Log bridge instance</returns>
        public ILogger CreateLogger(string categoryName)
        {
            return new BridgeLogger();
        }

        /// <summary>
        /// 添加日志提供程序（桥接场景无需处理）
        /// </summary>
        /// <param name="provider">日志提供程序 / Log provider</param>
        public void AddProvider(ILoggerProvider provider)
        {
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
        }

        /// <summary>
        /// 日志桥接，将 MEL 级别映射到 LogHelper
        /// </summary>
        private sealed class BridgeLogger : ILogger
        {
            /// <summary>
            /// 开始日志作用域（不支持）
            /// </summary>
            /// <typeparam name="TState">状态类型 / State type</typeparam>
            /// <param name="state">作用域状态 / Scope state</param>
            /// <returns>始终返回 null / Always returns null</returns>
            public IDisposable BeginScope<TState>(TState state) where TState : notnull
            {
                return null;
            }

            /// <summary>
            /// 判断日志级别是否启用（Warning 及以上）
            /// </summary>
            /// <typeparam name="TState">状态类型 / State type</typeparam>
            /// <param name="logLevel">日志级别 / Log level</param>
            /// <returns>Warning 及以上返回 true / true for Warning and above</returns>
            public bool IsEnabled(LogLevel logLevel)
            {
                return logLevel >= LogLevel.Warning;
            }

            /// <summary>
            /// 写入日志，按级别路由到 LogHelper
            /// </summary>
            /// <typeparam name="TState">状态类型 / State type</typeparam>
            /// <param name="logLevel">日志级别 / Log level</param>
            /// <param name="eventId">事件 ID / Event ID</param>
            /// <param name="state">日志状态 / Log state</param>
            /// <param name="exception">异常信息 / Exception information</param>
            /// <param name="formatter">格式化函数 / Formatter function</param>
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            {
                if (!IsEnabled(logLevel))
                {
                    return;
                }

                var message = formatter(state, exception);
                if (logLevel == LogLevel.Warning)
                {
                    LogHelper.Warning(message);
                    return;
                }

                if (exception != null)
                {
                    LogHelper.Error($"{message}{Environment.NewLine}{exception}");
                }
                else
                {
                    LogHelper.Error(message);
                }
            }
        }
    }

    #endregion 调度
}