//  ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯公民合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
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

namespace GameFrameX.Capability.Online.Matchmaking;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.Abstractions.Events;

/// <summary>
/// 匹配事件发件箱契约（C374：Commit 成功后事件投递失败的补偿锚点）。
/// <para>
/// 维护约束：事件在 <see cref="IOnlineMatchTicketStore.CommitMatchAsync"/> 成功后、任何发布尝试之前
/// 写入发件箱；投递失败条目保留，由撮合循环每轮重投，形成至少一次（at-least-once）投递——
/// 重复投递由 <see cref="OnlineEvent.EventId"/> 作为消费端去重键兜底。
/// </para>
/// </summary>
public interface IOnlineMatchEventOutbox
{
    /// <summary>
    /// 追加一批待投递事件（按 <see cref="OnlineEvent.EventId"/> 幂等：重复标识跳过，不覆盖）。
    /// </summary>
    /// <param name="events">待投递事件（顺序即期望投递顺序）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>完成通知。</returns>
    Task AppendAsync(IReadOnlyList<OnlineEvent> events, CancellationToken cancellationToken = default);

    /// <summary>
    /// 列出作用域内全部待投递条目（按写入顺序）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>待投递条目列表；无则空列表。</returns>
    Task<IReadOnlyList<OnlineMatchEventOutboxEntry>> ListPendingAsync(long tenantId, long appId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 标记条目已投递（条目不存在时静默返回——重投竞态下对端可能已标记）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="eventId">事件标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>完成通知。</returns>
    Task MarkDeliveredAsync(long tenantId, long appId, string eventId, CancellationToken cancellationToken = default);
}

