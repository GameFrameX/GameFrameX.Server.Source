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

using GameFrameX.Capability.Online.Abstractions.Contracts;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GameFrameX.Capability.Online.Abstractions.Audit;

/// <summary>
/// 统一审计存储契约（统一审计链路的唯一持久化面；生产持久化实现归运行时装配）。
/// <para>
/// 维护约束（红线）：
/// ① 「EventId 判重 + 落档」必须在实现内**同一临界区**完成（<see cref="AppendAsync"/> 的原子语义，
/// 幂等半边的结构性保证——服务层先查后写的两步竞态会漏重复）；
/// ② 记录**只追加、不修改、不淘汰**（审计不可丢）；
/// ③ 出入参必须防御性拷贝（持久化行语义，对齐交易存储先例——调用方持有的引用不得别名到存储内部状态）；
/// ④ <see cref="QueryAsync"/> 的过滤与分页**下推到实现内完成**（不得回退为全量行集交由调用方过滤）：
/// 全序固定为 (OccurredTime 倒序, EventId 序数升序)，过滤为合取（<see cref="OnlineAuditStoreQuery.Domains"/>
/// 域集合内析取），时间窗为闭区间，游标为全序上「严格之后」的 keyset 语义——
/// 行集至多 <see cref="OnlineAuditStoreQuery.PageSize"/> 行且按全序排列。
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
    /// 按查询载荷检索审计页：作用域两键锚定 + 可选维度过滤 + keyset 游标 + 全序排序 + 页大小，
    /// 全部在实现内完成（SQL 实现对应 WHERE + ORDER BY + LIMIT；调用方不再消费全量行集）。
    /// <para>
    /// 维护约束：作用域外记录结构性不可见（跨作用域查询与「无数据」同构——空行集 + <see langword="false"/>，
    /// 反预言由作用域索引保证）；<paramref name="query"/> 的作用域 / 页大小 / 游标合法性由调用方
    /// （<c>OnlineAuditService.QueryAsync</c>）先行校验，实现只需防御性下限钳制。
    /// </para>
    /// </summary>
    /// <param name="query">存储层查询载荷（过滤维度 + 已解码游标 + 页大小）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>按全序排列、至多 PageSize 行的结果页（行集为防御性拷贝）。</returns>
    Task<OnlineAuditStorePage> QueryAsync(OnlineAuditStoreQuery query, CancellationToken cancellationToken = default);
}