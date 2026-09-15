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

using GameFrameX.Foundation.Idempotency;

namespace GameFrameX.Online.Events;

/// <summary>
/// Online 事件发布基座（vault:C2 S1.6：组装 Foundation <c>IEventPublisher</c> 通用原语，
/// Online 语义（作用域字段承载）由 <see cref="OnlineEventEnvelopeMapper"/> 完成）。
/// <para>维护约束：发布前必须完成信封校验（Foundation 发布器保证非法信封不被分发）；业务侧只允许经本基座发布 Online 事件，
/// 禁止绕过作用域承载直发裸信封。</para>
/// </summary>
public sealed class OnlineEventPublisher : IOnlineEventPublisher
{
    /// <summary>
    /// Foundation 事件发布器。
    /// </summary>
    private readonly IEventPublisher _eventPublisher;

    /// <summary>
    /// 初始化 <see cref="OnlineEventPublisher"/>。
    /// </summary>
    /// <param name="eventPublisher">Foundation 事件发布器（传输实现由装配方决定）。</param>
    public OnlineEventPublisher(IEventPublisher eventPublisher)
    {
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
    }

    /// <summary>
    /// 发布一个 Online 事件（映射为 Foundation 信封后分发）。
    /// </summary>
    /// <param name="onlineEvent">待发布事件。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>完成通知。</returns>
    public Task PublishAsync(OnlineEvent onlineEvent, CancellationToken cancellationToken = default)
    {
        if (onlineEvent == null)
        {
            throw new ArgumentNullException(nameof(onlineEvent));
        }

        var envelope = OnlineEventEnvelopeMapper.ToEnvelope(onlineEvent);
        return _eventPublisher.PublishAsync(envelope, cancellationToken);
    }
}
