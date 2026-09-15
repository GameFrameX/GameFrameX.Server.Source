// ==========================================================================================
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
//   Any disputes or liabilities arising from secondary development based on this project
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

namespace GameFrameX.Online.Audit;

/// <summary>
/// 统一审计存储契约（vault:C9 S8.2：统一审计链路的唯一持久化面；生产持久化实现归运行时装配，X4）。
/// <para>
/// 维护约束（红线）：
/// ① 「EventId 判重 + 落档」必须在实现内**同一临界区**完成（<see cref="AppendAsync"/> 的原子语义，
/// VC-8.4 幂等半边的结构性保证——服务层先查后写的两步竞态会漏重复）；
/// ② 记录**只追加、不修改、不淘汰**（审计不可丢）；
/// ③ 出入参必须防御性拷贝（持久化行语义，对齐 C95 交易存储先例——调用方持有的引用不得别名到存储内部状态）。
/// </para>
/// </summary>
public interface IOnlineAuditStore
{
    /// <summary>
    /// 追加一条审计记录（EventId 判重与落档在同一临界区内完成）。
    /// </summary>
    /// <param name="record">待落档记录（调用方已完成校验与脱敏）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>新落档返回 <see langword="true"/>；<c>EventId</c> 已存在（幂等重放，本次不落档、无副作用）返回 <see langword="false"/>。</returns>
    Task<bool> AppendAsync(OnlineAuditRecord record, CancellationToken cancellationToken = default);

    /// <summary>
    /// 列出租户 + App 作用域内的全部审计记录（区服/玩家/域等过滤由服务层承担——审计是跨区服检索域）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>该作用域内的记录快照（无数据返回空列表而非 <see langword="null"/>）。</returns>
    Task<IReadOnlyList<OnlineAuditRecord>> ListAsync(long tenantId, long appId, CancellationToken cancellationToken = default);
}
