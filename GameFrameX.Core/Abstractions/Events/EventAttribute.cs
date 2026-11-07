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
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
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

namespace GameFrameX.Core.Abstractions.Events;

/// <summary>
/// 事件监听绑定特性：声明监听器类所绑定的 <see cref="GameEventArgs" /> 子类类型。
/// </summary>
/// <remarks>
/// 绑定键为事件参数类型本身（CLR 类型全局唯一），替代历史裸 int 事件 ID：
/// 编译期即可校验类型存在，跨目录撞值导致的「静默死监听」在编译期不再可能。
/// 无业务载荷的事件以空标记类（继承 <see cref="GameEventArgs" /> 的无成员子类）表达。
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class EventAttribute : System.Attribute
{
    /// <summary>
    /// 使用指定的事件参数类型初始化 <see cref="EventAttribute" /> 类的新实例。
    /// </summary>
    /// <param name="eventArgsType">绑定的事件参数类型，必须是 <see cref="GameEventArgs" /> 的子类。</param>
    public EventAttribute(Type eventArgsType)
    {
        ArgumentNullException.ThrowIfNull(eventArgsType);
        EventArgsType = eventArgsType;
    }

    /// <summary>
    /// 绑定的事件参数类型（事件键）。
    /// </summary>
    public Type EventArgsType { get; }
}
