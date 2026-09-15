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

namespace GameFrameX.Online.Party;

/// <summary>
/// 队伍存储契约（vault:C5 S4.3）。
/// <para>
/// 维护约束（红线）：存储实现负责**防御性深拷贝**——<c>Save</c> 入参与 <c>Find</c> 返回值都不得与
/// 内部状态共享引用（C95 交易存储先例：按引用保存会让「落库前的实例突变」提前污染已存状态，
/// 不符合持久化行语义）。按玩家反查队伍由实现维护索引，索引与队伍成员集合必须同源更新，
/// 不允许出现指向不存在队伍的悬挂索引。
/// </para>
/// </summary>
public interface IOnlinePartyStore
{
    /// <summary>
    /// 保存队伍（新增或覆盖）。
    /// </summary>
    /// <param name="party">队伍聚合。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>完成通知。</returns>
    Task SavePartyAsync(OnlineParty party, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按队伍标识查找队伍。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="partyId">队伍标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>队伍副本；不存在返回 null。</returns>
    Task<OnlineParty> FindPartyAsync(long tenantId, long appId, string partyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按玩家反查其所在队伍（跨作用域返回 null，避免反预言）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>队伍副本；不在任何队伍返回 null。</returns>
    Task<OnlineParty> FindPartyByPlayerAsync(long tenantId, long appId, long playerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 移除队伍（解散/取消的清理动作）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="partyId">队伍标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>完成通知。</returns>
    Task RemovePartyAsync(long tenantId, long appId, string partyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 列出作用域内全部队伍（空闲过期扫描用）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>队伍副本列表。</returns>
    Task<IReadOnlyList<OnlineParty>> ListPartiesAsync(long tenantId, long appId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 保存邀请。
    /// </summary>
    /// <param name="invite">邀请条目。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>完成通知。</returns>
    Task SaveInviteAsync(OnlinePartyInvite invite, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按邀请标识查找邀请。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="inviteId">邀请标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>邀请副本；不存在返回 null。</returns>
    Task<OnlinePartyInvite> FindInviteAsync(long tenantId, long appId, string inviteId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 查找被邀请人的待答复邀请。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="inviteeId">被邀请玩家标识。</param>
    /// <param name="partyId">队伍标识（null 表示不限队伍）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>待答复邀请副本；无则返回 null。</returns>
    Task<OnlinePartyInvite> FindPendingInviteAsync(long tenantId, long appId, long inviteeId, string partyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 列出作用域内全部邀请（邀请过期扫描用）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>邀请副本列表。</returns>
    Task<IReadOnlyList<OnlinePartyInvite>> ListInvitesAsync(long tenantId, long appId, CancellationToken cancellationToken = default);
}
