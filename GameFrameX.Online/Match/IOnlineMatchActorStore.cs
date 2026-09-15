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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GameFrameX.Online.Match;

/// <summary>
/// 对局 Actor 存储（vault:C6 S5.1 Actor 所有权与清理策略的持久化面）。
/// <para>
/// 维护约束（红线）：<see cref="UpdateAsync"/> 是**唯一**允许改写对局状态的入口，且必须提供
/// 「期望版本」——版本不匹配即整体失败并返回 null，绝不部分应用。这是「同一对局不被两个 Actor
/// 同时推进」（VC-5.13 无串局）的实现依据：谁先写到新版本谁生效，另一方的 CAS 必然失败。
/// </para>
/// <para>
/// 存储实现负责防御性深拷贝，且**不得**在 CAS 失败时留下任何写入痕迹。
/// 本阶段部署在同一进程内，<see cref="DeleteAsync"/> 即 Actor 释放（VC-5.11 无僵尸 Match）。
/// </para>
/// </summary>
public interface IOnlineMatchActorStore
{
    /// <summary>
    /// 保存对局（新增；已存在同标识对局即失败）。
    /// </summary>
    /// <param name="match">对局。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>落库后的对局副本；已存在返回 null。</returns>
    Task<OnlineMatch> CreateAsync(OnlineMatch match, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按标识查找对局。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="matchId">对局标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>对局副本；不存在返回 null。</returns>
    Task<OnlineMatch> FindAsync(long tenantId, long appId, string matchId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 以期望版本递增更新对局（CAS 语义）。
    /// </summary>
    /// <param name="match">对局新状态（<see cref="OnlineMatch.Version"/> 为期望版本）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>更新后的对局副本；版本不匹配返回 null。</returns>
    Task<OnlineMatch> UpdateAsync(OnlineMatch match, CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除对局（Actor 释放；仅 <see cref="OnlineMatchState.Closed"/> 允许）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="matchId">对局标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>删除成功返回 <c>true</c>。</returns>
    Task<bool> DeleteAsync(long tenantId, long appId, string matchId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 列出作用域内全部对局（Tick 扫描与可观测性用）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>对局副本列表。</returns>
    Task<IReadOnlyList<OnlineMatch>> ListAsync(long tenantId, long appId, CancellationToken cancellationToken = default);
}
