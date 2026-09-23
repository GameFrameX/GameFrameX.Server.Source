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
/// 社交关系图谱存储契约（vault:C7 S6.3：屏蔽 / 静音 / 处罚三类记录的存储落点）。
/// <para>
/// 维护约束（红线，VC-6.3 / VC-6.4 / VC-6.5 的落点）：
/// ① <see cref="IsBlockedEitherWayAsync"/> 是「屏蔽双向生效」的**唯一原子判据**——
/// 它必须在同一临界区内同时检查两个方向。拆成两次单向查询由调用方拼装，会让并发屏蔽在两次查询之间
/// 落库，产生「A 刚屏蔽 B，B→A 的邀请却被放行」的窗口；
/// ② <see cref="ListActivePunishmentsAsync"/> 的生效过滤（未撤销 + 已到生效时刻 + 未失效）
/// 必须在同一临界区内完成，调用方不得拿到全量处罚后自行过滤——那会把
/// 「处罚是否生效」的判断散落到各通路，正是 vault:C7 风险表首条要防的行为不一致；
/// ③ 屏蔽与静音是**方向性记录**（归属玩家拥有解除权），但屏蔽的**裁决**按双向生效，二者不可混淆。
/// </para>
/// <para>
/// 存储实现负责防御性深拷贝，且不得在 CAS 失败时留下任何写入痕迹。
/// </para>
/// </summary>
public interface IOnlineSocialGraphStore
{
    /// <summary>
    /// 以（归属玩家，被屏蔽玩家）为唯一键「不存在则创建」屏蔽记录：键已存在时返回既有记录且不写入。
    /// </summary>
    /// <param name="entry">待创建的屏蔽记录（作用域与方向已由调用方落定）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>当前生效的屏蔽记录副本（既有记录或刚落库的入参）。</returns>
    Task<OnlineBlockEntry> SaveBlockIfAbsentAsync(OnlineBlockEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按方向键查找屏蔽记录。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="ownerId">屏蔽发起人。</param>
    /// <param name="blockedPlayerId">被屏蔽玩家。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>屏蔽记录副本；不存在返回 null。</returns>
    Task<OnlineBlockEntry> FindBlockAsync(long tenantId, long appId, long ownerId, long blockedPlayerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 解除屏蔽（仅归属玩家本人可解除；解除不存在的记录视为幂等成功）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="ownerId">屏蔽发起人。</param>
    /// <param name="blockedPlayerId">被屏蔽玩家。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>实际删除了记录返回 <c>true</c>；原本不存在返回 <c>false</c>。</returns>
    Task<bool> RemoveBlockAsync(long tenantId, long appId, long ownerId, long blockedPlayerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 判定两名玩家之间**任一方向**是否存在屏蔽（裁决的唯一原子判据）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="leftPlayerId">一侧玩家标识。</param>
    /// <param name="rightPlayerId">另一侧玩家标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>任一方向存在屏蔽返回 <c>true</c>。</returns>
    Task<bool> IsBlockedEitherWayAsync(long tenantId, long appId, long leftPlayerId, long rightPlayerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 列出某玩家发起的全部屏蔽（展示与解除界面输入）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="ownerId">屏蔽发起人。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>屏蔽记录副本列表。</returns>
    Task<IReadOnlyList<OnlineBlockEntry>> ListBlocksAsync(long tenantId, long appId, long ownerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 以（归属玩家，被静音玩家）为唯一键「不存在则创建」静音记录：键已存在时返回既有记录且不写入。
    /// </summary>
    /// <param name="entry">待创建的静音记录。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>当前生效的静音记录副本（既有记录或刚落库的入参）。</returns>
    Task<OnlineMuteEntry> SaveMuteIfAbsentAsync(OnlineMuteEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按方向键查找静音记录。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="ownerId">静音发起人。</param>
    /// <param name="mutedPlayerId">被静音玩家。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>静音记录副本；不存在返回 null。</returns>
    Task<OnlineMuteEntry> FindMuteAsync(long tenantId, long appId, long ownerId, long mutedPlayerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 解除静音。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="ownerId">静音发起人。</param>
    /// <param name="mutedPlayerId">被静音玩家。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>实际删除了记录返回 <c>true</c>；原本不存在返回 <c>false</c>。</returns>
    Task<bool> RemoveMuteAsync(long tenantId, long appId, long ownerId, long mutedPlayerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 列出某玩家发起的全部静音。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="ownerId">静音发起人。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>静音记录副本列表。</returns>
    Task<IReadOnlyList<OnlineMuteEntry>> ListMutesAsync(long tenantId, long appId, long ownerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 以处罚标识为唯一键「不存在则创建」处罚记录。
    /// <para>
    /// 适用范围（避免误当成「重复处罚去重」）：唯一键是**处罚标识**，而
    /// <see cref="OnlinePunishmentService.ApplyAsync"/> 每次调用都新铸一个标识，
    /// 因此本方法**不提供跨调用的施加去重**——同一条 Admin 命令重放会落两条处罚记录。它守的是
    /// 「同一标识的重复写入」（重放带着已返回的标识回来时收敛到同一条）。
    /// </para>
    /// <para>
    /// 为什么不用 <c>AdminCaseId</c> 当去重键：同一个 Admin 案件可以合法地产生多次处罚——
    /// 先禁言后升级封禁、或期限处罚升级为永久处罚，用案件号去重会把升级动作静默吞掉，
    /// 玩家实际未被封禁而 Admin 界面显示已处置。施加去重同样只能由调用方显式给出的请求键承担，
    /// C27 范围未定义（处罚命令面 S6.10 归后续变更）。
    /// </para>
    /// </summary>
    /// <param name="punishment">待创建的处罚。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>当前生效的处罚副本（既有记录或刚落库的入参）。</returns>
    Task<OnlinePunishment> SavePunishmentIfAbsentAsync(OnlinePunishment punishment, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按处罚标识查找。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="punishmentId">处罚标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>处罚副本；不存在返回 null。</returns>
    Task<OnlinePunishment> FindPunishmentAsync(long tenantId, long appId, string punishmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 以 CAS 语义整体替换处罚记录（施加后的补写与撤销都走此入口）。
    /// </summary>
    /// <param name="punishment">替换后的处罚（其标识与作用域须与既有记录一致）。</param>
    /// <param name="expectedRevision">期望的当前版本号；不匹配即失败。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>更新后的处罚副本；CAS 失败或记录不存在返回 null。</returns>
    Task<OnlinePunishment> ReplacePunishmentAsync(OnlinePunishment punishment, int expectedRevision, CancellationToken cancellationToken = default);

    /// <summary>
    /// 列出某玩家在给定时刻**确实生效**的全部处罚（裁决服务的唯一处罚输入）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="query">生效处罚查询载荷（玩家定位 + 判定时刻；预留过滤维度扩展位）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>生效中的处罚副本列表（已撤销、未到生效时刻、已失效的均不返回）。</returns>
    Task<IReadOnlyList<OnlinePunishment>> ListActivePunishmentsAsync(long tenantId, long appId, ActivePunishmentQuery query, CancellationToken cancellationToken = default);

    /// <summary>
    /// 列出某玩家的全部处罚记录（含已撤销与已失效；Admin 复查与玩家申诉的输入）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="playerId">被处罚玩家。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>处罚副本列表。</returns>
    Task<IReadOnlyList<OnlinePunishment>> ListPunishmentsByPlayerAsync(long tenantId, long appId, long playerId, CancellationToken cancellationToken = default);
}
