// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
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
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GameFrameX.Online.Leaderboard;

/// <summary>
/// 排行榜存储契约（榜单定义 + 条目；InMemory 为单进程默认实现，生产持久化归 Server 仓运行时装配 X4）。
/// <para>
/// 维护约束（红线）：
/// (1) 作用域隔离——所有查询以 (TenantId, AppId) 为前置条件，跨作用域访问与不存在同构（null，反预言）；
/// (2) <see cref="ApplySubmissionAsync"/> 是唯一条目写入入口，**累计策略裁决与条目落档必须在同一临界区**，
/// 并发投递不得交错撕裂（形态对齐 C97 Ticket 存储 / C99 通知存储的临界区约定）；
/// (3) 出入参一律防御性拷贝——返回对象与内部状态无别名，调用方改写不影响存储。
/// </para>
/// </summary>
public interface IOnlineLeaderboardStore
{
    /// <summary>
    /// 创建榜单（同作用域同名榜单已存在时拒绝，返回 null，不覆盖）。
    /// </summary>
    /// <param name="leaderboard">榜单定义（作用域取其 TenantId / AppId）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>创建后的榜单副本；已存在返回 null。</returns>
    Task<OnlineLeaderboard> CreateAsync(OnlineLeaderboard leaderboard, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按作用域查找榜单定义。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="leaderboardId">榜单标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>榜单副本；不存在或跨作用域返回 null。</returns>
    Task<OnlineLeaderboard> FindAsync(long tenantId, long appId, string leaderboardId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 查找单玩家条目。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="leaderboardId">榜单标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>条目副本；未上榜或榜单不存在返回 null。</returns>
    Task<OnlineLeaderboardEntry> FindEntryAsync(long tenantId, long appId, string leaderboardId, long playerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 应用一笔分数提交（原子：累计策略裁决 + 条目落档在同一临界区）。
    /// </summary>
    /// <param name="leaderboard">目标榜单定义（调用方先经 <see cref="FindAsync"/> 取得）。</param>
    /// <param name="submission">分数提交。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>写入结果（聚合后条目 + 临界区内事实的前值）。</returns>
    Task<OnlineLeaderboardApplyResult> ApplySubmissionAsync(OnlineLeaderboard leaderboard, OnlineLeaderboardScoreSubmission submission, CancellationToken cancellationToken = default);

    /// <summary>
    /// 列出榜单全序条目（按 <see cref="OnlineLeaderboardOrdering"/> 排序的条目副本列表）。
    /// </summary>
    /// <param name="leaderboard">目标榜单定义。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>全序条目副本；空榜返回空列表。</returns>
    Task<List<OnlineLeaderboardEntry>> ListOrderedEntriesAsync(OnlineLeaderboard leaderboard, CancellationToken cancellationToken = default);
}
