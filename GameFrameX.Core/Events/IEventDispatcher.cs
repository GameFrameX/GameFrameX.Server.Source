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
/// 事件分发契约（<see cref="GameEventDispatcher"/> 实例核的注入面）：分发到目标 Actor 或全局监听器，
/// 并提供停机排空等待。
/// </summary>
/// <remarks>
/// The event-dispatch contract (the injection seam over the <see cref="GameEventDispatcher"/> instance
/// core): dispatches to the target actor or the global listeners, plus the shutdown-drain wait.
/// 组合根 / 宿主模块经构造参数注入本接口；静态便捷层 <see cref="EventDispatcher"/> 为过渡转发。
/// Compositions inject this interface through constructors; the static convenience layer
/// <see cref="EventDispatcher"/> forwards during the transition.
/// </remarks>
public interface IEventDispatcher
{
    /// <summary>
    /// 分发事件到指定的Actor或全局监听器。
    /// </summary>
    /// <param name="actorId">目标Actor的唯一标识符，如果为无效值则分发到全局监听器</param>
    /// <param name="eventArgs">事件携带的参数数据，绑定键取自其具体类型；无载荷事件须使用空标记类，禁止传null</param>
    /// <exception cref="ArgumentNullException"><paramref name="eventArgs"/> 为 null 时抛出</exception>
    void Dispatch(long actorId, GameEventArgs eventArgs);

    /// <summary>
    /// 等待所有在飞事件派发完成（停机排空用）。
    /// </summary>
    /// <param name="timeout">最长等待时限。</param>
    /// <returns>时限内全部派发完成返回 true；超时仍有在飞派发返回 false。</returns>
    Task<bool> WaitAllDispatchedAsync(TimeSpan timeout);
}
