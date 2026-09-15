//  ==========================================================================================
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

namespace GameFrameX.Online.Social;

/// <summary>
/// 聊天存储契约（vault:C7 S6.5 / S6.6：消息追加、游标分页、已读位点、离线补拉，VC-6.10/6.11）。
/// <para>
/// 维护约束（红线，分页稳定性的唯一实现点）：<see cref="AppendAsync"/> 必须在**同一临界区内**
/// 完成「去重键查重 → 分配频道内序号 → 落单调不减的发送时刻 → 写入」。这四步拆开都会破坏
/// 「翻页不重复、不漏项」：序号分配与写入分离会让并发追加拿到同一个序号，
/// 发送时刻回填分离会让同毫秒消息排到游标之前被永久跳过。
/// </para>
/// <para>
/// 维护约束：<see cref="ReadAfterAsync"/> 的排序键固定为 <c>(SentAtTime, Sequence)</c> 升序，
/// 实现不得改用其他顺序——游标由服务层按同一键编码，换序即失效。
/// </para>
/// <para>
/// 存储实现负责防御性深拷贝，且不得在去重命中或 CAS 失败时留下任何写入痕迹。
/// </para>
/// </summary>
public interface IOnlineChatStore
{
    /// <summary>
    /// 以频道标识为唯一键「不存在则创建」频道记录：键已存在时返回既有记录且不写入。
    /// </summary>
    /// <param name="channel">待创建的频道（标识已由调用方确定性派生）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>当前生效的频道记录副本（既有记录或刚落库的入参）。</returns>
    Task<OnlineChatChannel> SaveChannelIfAbsentAsync(OnlineChatChannel channel, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按频道标识查找频道记录。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="channelId">频道标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>频道记录副本；不存在返回 null。</returns>
    Task<OnlineChatChannel> FindChannelAsync(long tenantId, long appId, string channelId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 追加一条消息（分配频道内序号并落定发送时刻；去重键命中时返回既有消息且不写入）。
    /// </summary>
    /// <param name="message">待追加的消息（序号与发送时刻由本方法落定，入参中的值会被覆盖）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>生效的消息副本（既有消息或刚落库的入参）。</returns>
    Task<OnlineChatMessage> AppendAsync(OnlineChatMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按发送方去重键查找消息（频道内唯一，见 <see cref="OnlineChatMessage.DedupeKey"/>）。
    /// <para>
    /// 用途：服务层在走完成员资格与内容校验后、进入裁决 / 频控 / 审核之前先查一次去重键——
    /// 重发（同一去重键）必须原样返回既有消息，**不得**再消耗频控配额、也不该因窗口期内超限或此刻的
    /// 禁言/审核结论而被拒，否则「重发幂等」在客户端看来是「发送失败」（消息其实已入库）。
    /// 本查询只是让重放走捷径；真正的并发唯一约束仍在 <see cref="AppendAsync"/> 的临界区内。
    /// </para>
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="channelId">频道标识。</param>
    /// <param name="dedupeKey">发送方去重键（空串表示无去重键，直接返回 null）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>命中的消息副本；无去重键或未命中返回 null。</returns>
    Task<OnlineChatMessage> FindByDedupeKeyAsync(long tenantId, long appId, string channelId, string dedupeKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按消息标识查找。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="channelId">频道标识。</param>
    /// <param name="messageId">消息标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>消息副本；不存在返回 null。</returns>
    Task<OnlineChatMessage> FindMessageAsync(long tenantId, long appId, string channelId, string messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 以 CAS 语义改写消息状态（当前只有撤回一种迁移）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="channelId">频道标识。</param>
    /// <param name="messageId">消息标识。</param>
    /// <param name="expectedState">期望的当前状态；不匹配即失败。</param>
    /// <param name="newState">目标状态。</param>
    /// <param name="nowUnixMilliseconds">本次变更时刻（UTC 毫秒）。</param>
    /// <param name="recalledByPlayerId">撤回人（非撤回迁移传 <c>0</c>）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>更新后的消息副本；CAS 失败或记录不存在返回 null。</returns>
    Task<OnlineChatMessage> UpdateMessageStateAsync(long tenantId, long appId, string channelId, string messageId, OnlineChatMessageState expectedState, OnlineChatMessageState newState, long nowUnixMilliseconds, long recalledByPlayerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按稳定排序键升序读取游标之后的消息页（离线补拉与历史分页共用）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="channelId">频道标识。</param>
    /// <param name="afterSentAtTime">游标位置的发送时刻（UTC 毫秒；从头读传 <c>0</c>）。</param>
    /// <param name="afterSequence">游标位置的频道内序号（从头读传 <c>0</c>）。</param>
    /// <param name="limit">最多返回条数（必须为正）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>严格位于游标之后、按 <c>(SentAtTime, Sequence)</c> 升序排列的消息副本列表。</returns>
    Task<IReadOnlyList<OnlineChatMessage>> ReadAfterAsync(long tenantId, long appId, string channelId, long afterSentAtTime, long afterSequence, int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// 统计游标之后的未读条数（排除本人发送的与已撤回的消息）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="channelId">频道标识。</param>
    /// <param name="afterSentAtTime">已读位点的发送时刻（UTC 毫秒）。</param>
    /// <param name="afterSequence">已读位点的频道内序号。</param>
    /// <param name="readerPlayerId">读取者（本人发的消息不计未读）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>未读条数。</returns>
    Task<int> CountUnreadAsync(long tenantId, long appId, string channelId, long afterSentAtTime, long afterSequence, long readerPlayerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 落定已读位点（整体替换：位点是只进不退的最终位置，不做增量合并）。
    /// <para>
    /// 维护约束（「只进不退」的唯一守卫点）：**在写入临界区内**比对既有位点，入参位置不晚于既有位点时
    /// 整体拒绝并返回 null、不留任何写入痕迹（判定口径见
    /// <see cref="OnlineChatReadMark.IsNotAfterPosition"/>）。该判据不能只放在服务层的读-比-写里：
    /// 那是两次独立临界区，两个乱序到达的「已读」会各自读到旧位点后依次写入，后写者把位点推回去，
    /// 表现为红点重新亮起。
    /// </para>
    /// </summary>
    /// <param name="readMark">已读位点（作用域、玩家与频道已由调用方落定）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>生效的位点副本；位点回落（CAS 语义不满足）返回 null。</returns>
    Task<OnlineChatReadMark> SaveReadMarkAsync(OnlineChatReadMark readMark, CancellationToken cancellationToken = default);

    /// <summary>
    /// 查找已读位点。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="channelId">频道标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>已读位点副本；从未标记过返回 null。</returns>
    Task<OnlineChatReadMark> FindReadMarkAsync(long tenantId, long appId, long playerId, string channelId, CancellationToken cancellationToken = default);
}
