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
/// 群组存储契约（vault:C7 S6.4）。
/// <para>
/// 维护约束（红线）：**群记录是聚合根**——「成员数上限」「角色唯一性」「同一被邀请人至多一条待答复邀请」
/// 这些不变量都以整条记录为粒度，因此成员集合、邀请集合、角色、Metadata 的**一切变更只能经
/// <see cref="ReplaceAsync"/>**（CAS 单点）。把成员变更拆成独立写入会让两个不变量同时失效：
/// 上限判定读到别的写入者尚未提交的成员数，角色调整与成员移除交错出「非成员持管理员角色」。
/// </para>
/// <para>
/// 存储实现负责防御性深拷贝（读写均不得与调用方共享可变引用），且 **CAS 失败时不得留下任何写入痕迹**。
/// </para>
/// </summary>
public interface IOnlineGroupStore
{
    /// <summary>
    /// 以群组标识为唯一键「不存在则创建」：键已存在时返回既有记录且不写入。
    /// <para>
    /// 该方法是并发建群的收敛点：同一标识的并发创建里只有第一条落库，其余全部拿到同一条既有记录，
    /// 调用方据此返回既有群组而非报错或建出第二个群。
    /// </para>
    /// </summary>
    /// <param name="group">待创建的群记录（初始 <c>Revision</c> 为 0）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>当前生效的群记录副本（既有记录或刚落库的入参）。</returns>
    Task<OnlineGroup> SaveIfAbsentAsync(OnlineGroup group, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按作用域与群组标识查找群记录。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="groupId">群组标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>群记录副本；不存在返回 null。</returns>
    Task<OnlineGroup> FindAsync(long tenantId, long appId, string groupId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 以 CAS 语义整体替换群记录（成员、邀请、角色、Metadata 等一切变更的唯一入口）。
    /// <para>
    /// 语义：期望版本号不匹配或记录不存在 → 返回 null 且不留任何写入痕迹（不部分应用、不推进版本号）；
    /// 成功 → 落库记录的 <c>Revision</c> 置为 <paramref name="expectedRevision"/> + 1，
    /// <c>UpdatedAtTime</c> 以入参为准（入参 &lt;= 0 时取当前时刻）。
    /// </para>
    /// </summary>
    /// <param name="group">替换后的群记录（其 GroupId/TenantId/AppId 定位目标行；Revision 由存储层推进）。</param>
    /// <param name="expectedRevision">期望的当前版本号（调用方读取快照时的 <c>Revision</c>）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>落库后的群记录副本；CAS 失败或记录不存在返回 null。</returns>
    Task<OnlineGroup> ReplaceAsync(OnlineGroup group, int expectedRevision, CancellationToken cancellationToken = default);

    /// <summary>
    /// 列出某玩家所属的全部群组（含已解散群组）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>群记录副本列表。</returns>
    Task<IReadOnlyList<OnlineGroup>> ListByPlayerAsync(long tenantId, long appId, long playerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 列出作用域内处于指定状态的全部群组（超期邀请扫描的输入）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="state">群组状态。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>群记录副本列表。</returns>
    Task<IReadOnlyList<OnlineGroup>> ListByStateAsync(long tenantId, long appId, OnlineGroupState state, CancellationToken cancellationToken = default);
}
