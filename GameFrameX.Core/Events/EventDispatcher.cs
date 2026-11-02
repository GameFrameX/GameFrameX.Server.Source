// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository:  https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:    https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:      https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using GameFrameX.Core.Abstractions.Events;

namespace GameFrameX.Core.Events;

/// <summary>
/// 事件分发静态便捷层（过渡保留：转发 <see cref="GameEventDispatcher.ProcessDefault"/>，已标记过时并指向注入面）。
/// </summary>
/// <remarks>
/// The static convenience layer for event dispatching (transitional: forwards to
/// <see cref="GameEventDispatcher.ProcessDefault"/> and is marked obsolete, pointing at the
/// injection seam <see cref="IEventDispatcher"/>). 组合根 / 宿主模块应注入 <see cref="IEventDispatcher"/>；
/// 后续收敛版本迁移调用点后移除本层。
/// Compositions inject <see cref="IEventDispatcher"/>; the layer is removed once later
/// convergence versions migrate the call sites.
/// </remarks>
public static class EventDispatcher
{
    /// <summary>
    /// 分发事件到指定的Actor或全局监听器（静态便捷层：转发 <see cref="GameEventDispatcher.Dispatch"/>）。
    /// </summary>
    /// <param name="actorId">目标Actor的唯一标识符，如果为无效值则分发到全局监听器</param>
    /// <param name="eventArgs">事件携带的参数数据，绑定键取自其具体类型；无载荷事件须使用空标记类，禁止传null</param>
    /// <exception cref="ArgumentNullException"><paramref name="eventArgs"/> 为 null 时抛出</exception>
    [Obsolete("静态便捷层过渡保留：请改用注入的 IEventDispatcher 实例（进程缺省 GameEventDispatcher.ProcessDefault）。Static convenience layer kept for transition; use the injected IEventDispatcher instance (process default GameEventDispatcher.ProcessDefault) instead.", false)]
    public static void Dispatch(long actorId, GameEventArgs eventArgs)
    {
        GameEventDispatcher.ProcessDefault.Dispatch(actorId, eventArgs);
    }

    /// <summary>
    /// 等待所有在飞事件派发完成（停机排空用；静态便捷层：转发 <see cref="GameEventDispatcher.WaitAllDispatchedAsync"/>）。
    /// </summary>
    /// <param name="timeout">最长等待时限。</param>
    /// <returns>时限内全部派发完成返回 true；超时仍有在飞派发返回 false。</returns>
    [Obsolete("静态便捷层过渡保留：请改用注入的 IEventDispatcher 实例（进程缺省 GameEventDispatcher.ProcessDefault）。Static convenience layer kept for transition; use the injected IEventDispatcher instance (process default GameEventDispatcher.ProcessDefault) instead.", false)]
    public static Task<bool> WaitAllDispatchedAsync(TimeSpan timeout)
    {
        return GameEventDispatcher.ProcessDefault.WaitAllDispatchedAsync(timeout);
    }

    /// <summary>
    /// 在目标 Actor 上下文内分发事件（静态便捷层：转发 <see cref="GameEventDispatcher"/> 同名内部路径，仓内扩展入口共用）。
    /// </summary>
    /// <param name="actor">目标 Actor</param>
    /// <param name="actorId">目标Actor的唯一标识符</param>
    /// <param name="eventArgs">事件携带的参数数据，不为null</param>
    /// <returns>异步任务</returns>
    internal static Task DispatchToActorAsync(GameFrameX.Core.Abstractions.IActor actor, long actorId, GameEventArgs eventArgs)
    {
        return GameEventDispatcher.DispatchToActorAsync(actor, actorId, eventArgs);
    }
}
