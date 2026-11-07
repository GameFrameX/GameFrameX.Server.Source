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


using GameFrameX.Apps.Server.Component;
using GameFrameX.Apps.Server.Entity;
using GameFrameX.Core.Abstractions.Attribute;
using GameFrameX.Core.Abstractions.Events;
using GameFrameX.Core.Events;
using GameFrameX.Core.Timer.Handler;
using GameFrameX.Foundation.Utility;
using GameFrameX.Foundation.Localization.Core;

namespace GameFrameX.Hotfix.Logic.Server;

public class ServerComponentAgent : StateComponentAgent<ServerComponent, ServerState>
{
    public override async Task<bool> ActiveAsync()
    {
        var isContinue = await base.ActiveAsync();
        if (isContinue)
        {
            // 跨天定时器
            await WithCronExpressionAsync<CrossDayTimeHandler>("0 0 0 * * ? *");
            if (State.FirstStartTime == default)
            {
                State.FirstStartTime = TimerHelper.UnixTimeSeconds();
                await OwnerComponent.WriteStateAsync();
            }
        }

        return isContinue;
    }

    private Task TestDelayTimerAsync()
    {
        // Localization: Hotfix.Server.TestDelayTimerExecuted - ServerCompAgent.TestDelayTimer.延时3秒执行.执行一次
        LogHelper.Debug(LocalizationService.GetString(Localization.Keys.Hotfix.Server.TestDelayTimerExecuted));
        return Task.CompletedTask;
    }

    private Task TestScheduleTimerAsync()
    {
        // Localization: Hotfix.Server.TestScheduleTimerExecuted - ServerCompAgent.TestSchedueTimer.延时1秒执行.每隔10秒执行
        LogHelper.Debug(LocalizationService.GetString(Localization.Keys.Hotfix.Server.TestScheduleTimerExecuted));
        //
        // var states = await GameDb.FindListAsync<LoginState>(m => m.Id != 0);
        // LOGGER.Debug(states);
        //
        // var s1 = await GameDb.CountAsync<LoginState>(m => m.Id == 563517512475926528);
        // LOGGER.Debug(s1);
        // var s = await GameDb.FindAsync<LoginState>(m => m.UserName != null);
        // LOGGER.Debug(s);
        // LoginState loginState = new LoginState()
        // {
        //     Id = TimeHelper.UnixTimeSeconds(), CreateId = TimeHelper.UnixTimeSeconds(),
        //     UserName = "Save"
        // };
        // loginState.AfterLoadFromDB(true);
        // var savedState = await GameDb.UpdateAsync<LoginState>(loginState);
        // LOGGER.Debug(savedState);
        // await Task.Delay(TimeSpan.FromSeconds(1));
        // var count = await GameDb.DeleteAsync(savedState);
        // LOGGER.Debug(count);
        return Task.CompletedTask;
    }


    /// <summary>
    /// 获取服务器开服启动时间
    /// </summary>
    /// <returns></returns>
    [Service]
    [ThreadSafe]
    public virtual long FirstStartTime()
    {
        return State.FirstStartTime;
    }

    [Service]
    [ThreadSafe]
    public virtual Task<int> GetWorldLevelAsync()
    {
        return Task.FromResult(State.WorldLevel);
    }

    /*******************演示代码**************************/
    [Service]
    [ThreadSafe]
    public virtual int DoSomething0()
    {
        return State.WorldLevel;
    }

    [Discard]
    [ThreadSafe]
    protected virtual Task DoSomething1Async()
    {
        return Task.CompletedTask;
    }

    [ThreadSafe]
    protected void DoSomething2()
    {
        // 演示占位方法，无具体业务逻辑；保留方法签名以供示例展示。
    }

    [Discard]
    [TimeOut(12000)]
    protected virtual Task DoSomething3Async()
    {
        return Task.CompletedTask;
    }

    private class DelayTimer : TimerHandler<ServerComponentAgent>
    {
        protected override Task HandleTimerAsync(ServerComponentAgent agent, GameEventArgs gameEventArgs)
        {
            return agent.TestDelayTimerAsync();
        }
    }

    private class ScheduleTimer : TimerHandler<ServerComponentAgent>
    {
        protected override Task HandleTimerAsync(ServerComponentAgent agent, GameEventArgs gameEventArgs)
        {
            return agent.TestScheduleTimerAsync();
        }
    }

    /// <summary>
    /// 跨天定时器调用
    /// </summary>
    private class CrossDayTimeHandler : TimerHandler<ServerComponentAgent>
    {
        protected override async Task HandleTimerAsync(ServerComponentAgent agent, GameEventArgs gameEventArgs)
        {
            // Localization: Hotfix.Server.CrossDayTimerExecuted - ServerCompAgent.CrossDayTimeHandler.跨天定时器执行{0}
            LogHelper.Debug(LocalizationService.GetString(Localization.Keys.Hotfix.Server.CrossDayTimerExecuted, TimerHelper.CurrentDateTimeWithUtcFormat()));
            var crossDays = TimerHelper.GetCrossDaysWithUtc(agent.State.FirstStartTime);

            await ActorManager.RoleCrossDayAsync(crossDays);
            await ActorManager.CrossDayAsync(crossDays, GlobalConst.ActorTypeServer);
        }
    }

    /*******************演示代码**************************/
}
