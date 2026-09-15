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

using System.Threading;
using System.Threading.Tasks;

namespace GameFrameX.Online.Match;

/// <summary>
/// 对局结算结果存储（vault:C6 S5.7 幂等结算的持久化面）。
/// <para>
/// 维护约束（红线）：<see cref="CommitAsync"/> 是**唯一**允许落定结算结果的入口，
/// 且必须实现「同一对局首次结果胜出」语义——已存在结果时不得覆盖，直接返回既有结果。
/// VC-5.8「以同一 MatchResultId 重试结算 3 次只发一次奖」与
/// 「重复结算生效次数 = 0」都由本约束担保；调用方无需自行去重。
/// </para>
/// </summary>
public interface IOnlineMatchResultStore
{
    /// <summary>
    /// 按对局标识查找结算结果。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="matchId">对局标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>结果副本；未结算返回 null。</returns>
    Task<OnlineMatchResult> FindByMatchAsync(long tenantId, long appId, string matchId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 落定结算结果（首次胜出：已存在则返回既有结果，不覆盖）。
    /// </summary>
    /// <param name="result">待落定结果。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>落定后的结果副本（重试时即首次结果）。</returns>
    Task<OnlineMatchResult> CommitAsync(OnlineMatchResult result, CancellationToken cancellationToken = default);
}
