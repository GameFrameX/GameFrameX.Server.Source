// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Capability.Online.Abstractions.Events;
using GameFrameX.Capability.Hosting.Abstractions;

/// <summary>
/// Online 事件发布基座（C294 起委托机制事件总线 <see cref="ICapabilityEventPublisher"/>，
/// 由族基建 <c>OnlineCapabilityFamily.RegisterInfrastructure</c> 经族上下文的事件发布口构造）。
/// <para>
/// 维护约束：主题 = 事件类型字符串本身（<c>&lt;族&gt;.&lt;域&gt;.&lt;事件&gt;</c> 小写形态，如
/// <c>online.match.settled</c>，由各域事件常量类保证）；载荷类型固定为 <see cref="OnlineEvent"/>，
/// 订阅方以相同主题 + 相同载荷类型经 Register 期 <c>Subscribe</c> 声明；宿主 Activate 开始前总线拒绝发布
/// （Register 期发出的事件后注册订阅者收不到）；单订阅体异常由机制记日志后继续分发，
/// 业务发布方不再感知订阅体异常。
/// </para>
/// </summary>
public sealed class OnlineEventPublisher : IOnlineEventPublisher
{
    /// <summary>
    /// 机制事件发布口（族上下文注入；实际实现为机制 <c>CapabilityEventBus</c>）。
    /// </summary>
    private readonly ICapabilityEventPublisher _eventPublisher;

    /// <summary>
    /// 初始化 <see cref="OnlineEventPublisher"/>。
    /// </summary>
    /// <param name="eventPublisher">机制事件发布口（不可为 null）。</param>
    public OnlineEventPublisher(ICapabilityEventPublisher eventPublisher)
    {
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
    }

    /// <summary>
    /// 发布一个 Online 事件（载荷 = <see cref="OnlineEvent"/>，主题 = 事件类型字符串；同步分发到该主题的全部订阅者）。
    /// </summary>
    /// <param name="onlineEvent">待发布事件。</param>
    /// <param name="cancellationToken">取消令牌（分发为同步语义，本参数仅占位契约）。</param>
    /// <returns>完成通知。</returns>
    public Task PublishAsync(OnlineEvent onlineEvent, CancellationToken cancellationToken = default)
    {
        if (onlineEvent == null)
        {
            throw new ArgumentNullException(nameof(onlineEvent));
        }

        if (string.IsNullOrWhiteSpace(onlineEvent.EventType))
        {
            throw new ArgumentException("Online event type (also the bus topic) must be a non-empty string.", nameof(onlineEvent));
        }

        _eventPublisher.Publish(onlineEvent.EventType, onlineEvent);
        return Task.CompletedTask;
    }
}
