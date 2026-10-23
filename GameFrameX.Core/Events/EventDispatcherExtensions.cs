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

namespace GameFrameX.Core.Events;

/// <summary>
/// 组件代理侧事件派发扩展：提供以组件代理为入口的事件派发写法。
/// </summary>
public static class EventDispatcherExtensions
{
    /// <summary>
    /// 在代理所属 Actor 内派发事件：只触发该 Actor 类型注册的监听器，监听器收到该 Actor 上的组件代理。
    /// </summary>
    /// <param name="agent">代理对象</param>
    /// <param name="eventArgs">事件参数，绑定键取自其具体类型；无载荷事件须使用空标记类，禁止传null</param>
    /// <remarks>
    /// 与静态 <see cref="EventDispatcher.Dispatch(long, GameEventArgs)" /> 共用同一监听器循环（Actor 内派发路径），日志键也统一为 <c>Core.EventDispatcher.*</c>；
    /// 直接以代理所属 Actor 入队而不经 <see cref="EventDispatcher.Dispatch(long, GameEventArgs)" /> 的 ActorMap 查找——已回收出表的 Actor 经 ActorMap 会落入全局监听器分支，偏离本语义；
    /// 不做作用域广播（服务器段广播分支已退役）：需要遍历在线玩家时，以会话层 <c>PlayerSessionManager</c> 为在线事实源自行派发。
    /// </remarks>
    public static void Dispatch(this IComponentAgent agent, GameEventArgs eventArgs)
    {
        ArgumentNullException.ThrowIfNull(eventArgs);

        var actor = agent.Owner.Actor;
        actor.Tell(() => EventDispatcher.DispatchToActorAsync(actor, agent.ActorId, eventArgs));
    }
}
