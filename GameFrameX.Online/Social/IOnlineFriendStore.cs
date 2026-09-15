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
/// 好友关系存储契约（vault:C7 S6.2）。
/// <para>
/// 维护约束（红线，VC-6.1 / VC-6.2 的落点）：**一对玩家（无向对）至多一条关系记录**是该存储的
/// 结构性不变量，不是调用方的纪律——<see cref="SaveIfAbsentAsync"/> 与
/// <see cref="UpdateStateAsync"/> 是该不变量的两个守卫点：
/// 并发「添加好友」只能在 <see cref="SaveIfAbsentAsync"/> 内收敛出一条记录（先到者生效，后到者拿到既有记录）；
/// 关系状态只能经 <see cref="UpdateStateAsync"/> 的 CAS 语义改写（期望状态不匹配即整体失败并返回 null，
/// 绝不部分应用），并发「接受 vs 拒绝」由此收敛到同一临界区——谁先改到目标态，谁的结果生效。
/// </para>
/// <para>
/// 存储实现负责防御性深拷贝，且**不得**在 CAS 失败时留下任何写入痕迹。
/// </para>
/// </summary>
public interface IOnlineFriendStore
{
    /// <summary>
    /// 以无向对为唯一键「不存在则创建」：键已存在时返回既有记录且不写入。
    /// <para>
    /// 该方法是「重复请求不错乱」的实现依据（VC-6.1）：并发连发的好友请求里只有第一条能创建记录，
    /// 其余全部拿到同一条既有记录，调用方据此返回既有状态而非报错或新建。
    /// </para>
    /// </summary>
    /// <param name="friendship">待创建的关系（其无向对与作用域已由调用方规范化）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>当前生效的关系记录副本（既有记录或刚落库的入参）。</returns>
    Task<OnlineFriendship> SaveIfAbsentAsync(OnlineFriendship friendship, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按无向对查找关系（两个方向的入参都命中同一条记录）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="leftPlayerId">一侧玩家标识（无需预先规范化）。</param>
    /// <param name="rightPlayerId">另一侧玩家标识（无需预先规范化）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>关系副本；不存在返回 null。</returns>
    Task<OnlineFriendship> FindAsync(long tenantId, long appId, long leftPlayerId, long rightPlayerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 以 CAS 语义改写关系状态（接受 / 拒绝 / 过期 / 删除 / 重新发起走此入口）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="friendshipId">关系标识。</param>
    /// <param name="expectedState">期望的当前状态；不匹配即失败。</param>
    /// <param name="newState">目标状态。</param>
    /// <param name="nowUnixMilliseconds">本次变更时刻（UTC 毫秒）。</param>
    /// <param name="responded">本次变更是否构成一次答复（答复时回填答复时刻）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>更新后的关系副本；CAS 失败或记录不存在返回 null。</returns>
    Task<OnlineFriendship> UpdateStateAsync(long tenantId, long appId, string friendshipId, OnlineFriendshipState expectedState, OnlineFriendshipState newState, long nowUnixMilliseconds, bool responded, CancellationToken cancellationToken = default);

    /// <summary>
    /// 以 CAS 语义把静止态关系重新发起为待答复请求（VC-6.2 状态机末段：「删除后可重新添加」）。
    /// <para>
    /// 为什么需要独立入口：重新发起同时改写**状态、方向与有效期**三项——方向必须跟随本次发起人翻转
    /// （否则「谁先发起」的审计语义与「仅被请求方可答复」的权限判据会一起错位），
    /// 有效期也必须重置。三项须在同一临界区内落到同一条记录，故不拆成「改状态 + 再改方向」两次写入。
    /// </para>
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="friendshipId">关系标识。</param>
    /// <param name="expectedState">期望的当前状态（静止态）；不匹配即失败。</param>
    /// <param name="requesterId">本次发起人（写入为新的方向事实）。</param>
    /// <param name="addresseeId">本次被请求方。</param>
    /// <param name="nowUnixMilliseconds">本次变更时刻（UTC 毫秒）。</param>
    /// <param name="expiresAtTime">重置后的请求失效时刻（UTC 毫秒）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>更新后的关系副本；CAS 失败或记录不存在返回 null。</returns>
    Task<OnlineFriendship> RenewRequestAsync(long tenantId, long appId, string friendshipId, OnlineFriendshipState expectedState, long requesterId, long addresseeId, long nowUnixMilliseconds, long expiresAtTime, CancellationToken cancellationToken = default);

    /// <summary>
    /// 列出某玩家的全部关系记录（任意状态；好友列表与待答复请求列表的输入）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>关系副本列表。</returns>
    Task<IReadOnlyList<OnlineFriendship>> ListByPlayerAsync(long tenantId, long appId, long playerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 列出作用域内处于指定状态的全部关系（超期扫描的输入）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="state">关系状态。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>关系副本列表。</returns>
    Task<IReadOnlyList<OnlineFriendship>> ListByStateAsync(long tenantId, long appId, OnlineFriendshipState state, CancellationToken cancellationToken = default);
}
