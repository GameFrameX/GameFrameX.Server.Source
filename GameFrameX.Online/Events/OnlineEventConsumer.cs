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
/// Online 事件消费去重基座（vault:C2 S1.6：同一 <c>EventId</c> 投递两次只处理一次，第二次丢弃并记录，VC-1.13）。
/// <para>
/// 维护约束：消费处理入口必须先经 <see cref="TryConsume"/> 判定再去执行业务处理；
/// 重复事件被拦截时应保留审计痕迹（由调用方记录，本基座只做判定）。
/// </para>
/// </summary>
public sealed class OnlineEventConsumer
{
    /// <summary>
    /// Foundation 事件去重器。
    /// </summary>
    private readonly IEventDeduplicator _eventDeduplicator;

    /// <summary>
    /// 初始化 <see cref="OnlineEventConsumer"/>。
    /// </summary>
    /// <param name="eventDeduplicator">Foundation 事件去重器（保留期实现由装配方决定）。</param>
    public OnlineEventConsumer(IEventDeduplicator eventDeduplicator)
    {
        _eventDeduplicator = eventDeduplicator ?? throw new ArgumentNullException(nameof(eventDeduplicator));
    }

    /// <summary>
    /// 尝试消费一个事件：首见返回 <c>true</c> 并登记去重标识；重复返回 <c>false</c>（应跳过处理）。
    /// </summary>
    /// <param name="onlineEvent">待消费事件。</param>
    /// <returns>首次见到该 <c>EventId</c> 返回 <c>true</c>；重复见到返回 <c>false</c>。</returns>
    public bool TryConsume(OnlineEvent onlineEvent)
    {
        if (onlineEvent == null)
        {
            throw new ArgumentNullException(nameof(onlineEvent));
        }

        return _eventDeduplicator.TryConsume(onlineEvent.EventId);
    }
}
