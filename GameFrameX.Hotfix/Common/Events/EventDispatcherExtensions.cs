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

using GameFrameX.Core.Abstractions.Agent;
using GameFrameX.Core.Abstractions.Events;
using GameFrameX.Foundation.Extensions;
using GameFrameX.Foundation.Logger;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Hotfix.Logic.Server;
using GameFrameX.Utility.Setting;

namespace GameFrameX.Hotfix.Common.Events;

public static class EventDispatcherExtensions
{
    /// <summary>
    /// 分发事件：服务器 agent 自处理；服务器段事件（<see cref="IServerScopeEvent" />）再遍历全部在线玩家 actor 逐个派发。
    /// </summary>
    /// <param name="agent">代理对象</param>
    /// <param name="eventArgs">事件参数，绑定键取自其具体类型；无载荷事件须使用空标记类，禁止传null</param>
    /// <remarks>
    /// 广播不变式：服务器段事件先由服务器 agent 自身处理，再遍历在线玩家逐 actor 派发（顺序与历史值段语义一致）；
    /// 作用域归属由事件参数类型实现 <see cref="IServerScopeEvent" /> 声明。
    /// </remarks>
    public static void Dispatch(this IComponentAgent agent, GameEventArgs eventArgs)
    {
        ArgumentNullException.ThrowIfNull(eventArgs);

        // 自己处理
        SelfHandle(agent, eventArgs);

        if (eventArgs is IServerScopeEvent && agent.OwnerType > GlobalConst.ActorTypeSeparator)
        {
            // 全局非玩家事件，抛给所有玩家
            agent.Tell(()
                           =>
                       {
                           return ServerComponentAgent.OnlineRoleForeach(role
                                                                             =>
                                                                         {
                                                                             role.Dispatch(eventArgs);
                                                                         });
                       });
        }
    }

    private static void SelfHandle(IComponentAgent agent, GameEventArgs eventArgs)
    {
        agent.Tell(async () =>
        {
            // 事件需要在本actor内执行，不可多线程执行，所以不能使用Task.WhenAll来处理
            var listeners = HotfixManager.FindListeners(agent.OwnerType, eventArgs.GetType());
            if (listeners.IsNullOrEmpty())
            {
                // Localization: Hotfix.Event.SelfHandleNoListeners - EventDispatcherExtensions.SelfHandle {0} 事件类型：{1} 没有找到任何监听者
                LogHelper.Warning(LocalizationService.GetString(Localization.Keys.Hotfix.Event.SelfHandleNoListeners, eventArgs.GetType().Name, eventArgs.GetType().Name));
                return;
            }

            foreach (var listener in listeners)
            {
                var componentAgent = await agent.GetComponentAgent(listener.AgentType, false);
                try
                {
                    await listener.HandleEvent(componentAgent, eventArgs);
                }
                catch (Exception exception)
                {
                    // Localization: Hotfix.Event.SelfHandleException - EventDispatcherExtensions.SelfHandle 处理事件 {0} 时发生异常: {1}
                    LogHelper.Error(LocalizationService.GetString(Localization.Keys.Hotfix.Event.SelfHandleException, eventArgs.GetType().Name, exception));
                }
            }
        });
    }
}