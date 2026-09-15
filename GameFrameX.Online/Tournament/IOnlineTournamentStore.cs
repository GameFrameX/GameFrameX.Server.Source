// ==========================================================================================
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

namespace GameFrameX.Online.Tournament;

/// <summary>
/// 赛事存储契约（赛事定义 + 报名登记 + 冻结成绩；InMemory 为单进程默认实现，生产持久化归 Server 仓运行时装配 X4）。
/// <para>
/// 维护约束（红线）：
/// (1) 作用域隔离——所有查询以 (TenantId, AppId) 为前置条件，跨作用域访问与不存在同构（null，反预言）；
/// (2) 冻结成绩与赛事定义**分离存储**：成绩一经写入即为冻结事实，赛事状态推进（<see cref="SaveAsync"/>）
/// 不得改写成绩内容——「结果查询」的存储层保证；
/// (3) 报名的「判定重复 + 落档」必须在**同一临界区**完成（<see cref="TryRegisterAsync"/>），
/// 否则并发报名会为同一玩家落下两条登记，使参赛人数与发奖人数口径不一致；
/// (4) 出入参一律防御性拷贝——返回对象与内部状态无别名，调用方改写不影响存储。
/// </para>
/// </summary>
public interface IOnlineTournamentStore
{
    /// <summary>
    /// 创建赛事（同作用域同标识赛事已存在时拒绝，返回 null，不覆盖）。
    /// </summary>
    /// <param name="tournament">赛事定义（作用域取其 TenantId / AppId）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>创建后的赛事副本；已存在返回 null。</returns>
    Task<OnlineTournament> CreateAsync(OnlineTournament tournament, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按作用域查找赛事定义。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="tournamentId">赛事标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>赛事副本；不存在或跨作用域返回 null。</returns>
    Task<OnlineTournament> FindAsync(long tenantId, long appId, string tournamentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 保存赛事定义（状态推进落档；按作用域 + 赛事标识覆盖既有定义，赛事必须已存在）。
    /// </summary>
    /// <param name="tournament">赛事定义（含推进后的状态与时间戳）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>完成通知。</returns>
    Task SaveAsync(OnlineTournament tournament, CancellationToken cancellationToken = default);

    /// <summary>
    /// 尝试登记报名（唯一键 = (TournamentId, PlayerId)；判定重复与落档在同一临界区）。
    /// </summary>
    /// <param name="registration">报名登记。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>落档结果（<see cref="OnlineTournamentRegistrationResult.IsNew"/> 显式标出本次是否新登记；
    /// 非新登记时携带既有登记，调用方据此判定重放、不再发事件）。</returns>
    Task<OnlineTournamentRegistrationResult> TryRegisterAsync(OnlineTournamentRegistration registration, CancellationToken cancellationToken = default);

    /// <summary>
    /// 查找单个玩家的报名登记。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="tournamentId">赛事标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>登记副本；未报名或跨作用域返回 null。</returns>
    Task<OnlineTournamentRegistration> FindRegistrationAsync(long tenantId, long appId, string tournamentId, long playerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 列出赛事的全部报名登记（按报名时刻升序，供参赛名册查询与测试断言）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="tournamentId">赛事标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>登记副本列表；无报名返回空列表。</returns>
    Task<List<OnlineTournamentRegistration>> ListRegistrationsAsync(long tenantId, long appId, string tournamentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 保存冻结成绩（按作用域 + 赛事标识覆盖；赛事结束流程在推进状态**之前**调用——成绩先行）。
    /// </summary>
    /// <param name="standings">冻结成绩。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>完成通知。</returns>
    Task SaveStandingsAsync(OnlineTournamentStandings standings, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按作用域查找冻结成绩。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="tournamentId">赛事标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>成绩副本；不存在或跨作用域返回 null。</returns>
    Task<OnlineTournamentStandings> FindStandingsAsync(long tenantId, long appId, string tournamentId, CancellationToken cancellationToken = default);
}
