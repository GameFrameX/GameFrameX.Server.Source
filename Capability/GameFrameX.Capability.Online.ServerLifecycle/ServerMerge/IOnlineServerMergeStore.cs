// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and other rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Capability.Online.ServerLifecycle.ServerMerge;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// 归并计划存储契约（计划幂等登记 + dry-run 报告留存 + 执行落定记录）。
/// </summary>
public interface IOnlineServerMergeStore
{
    /// <summary>
    /// 登记归并计划（幂等：同 PlanId 已存在时返回既有计划且 <c>planned=false</c>）。
    /// </summary>
    /// <param name="plan">归并计划。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>生效计划与是否本次首次登记。</returns>
    Task<(OnlineServerMergePlan Plan, bool Planned)> TryPlanAsync(OnlineServerMergePlan plan, CancellationToken cancellationToken = default);

    /// <summary>
    /// 查找归并计划。
    /// </summary>
    /// <param name="planId">计划标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>归并计划；不存在返回 null。</returns>
    Task<OnlineServerMergePlan> FindPlanAsync(string planId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 留存 dry-run 报告（真实执行的强制前置凭据）。
    /// </summary>
    /// <param name="planId">计划标识。</param>
    /// <param name="report">dry-run 报告。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>完成通知。</returns>
    Task RecordDryRunReportAsync(string planId, OnlineServerMergeReport report, CancellationToken cancellationToken = default);

    /// <summary>
    /// 查找最近一次 dry-run 报告。
    /// </summary>
    /// <param name="planId">计划标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>dry-run 报告；未预演回 null。</returns>
    Task<OnlineServerMergeReport> FindDryRunReportAsync(string planId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 查找执行落定报告（幂等回放凭据）。
    /// </summary>
    /// <param name="planId">计划标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>执行报告；未执行过返回 null。</returns>
    Task<OnlineServerMergeReport> FindExecutedReportAsync(string planId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 落定执行报告（标记计划已执行；重复落定同 PlanId = no-op）。
    /// </summary>
    /// <param name="planId">计划标识。</param>
    /// <param name="report">执行报告。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>完成通知。</returns>
    Task MarkExecutedAsync(string planId, OnlineServerMergeReport report, CancellationToken cancellationToken = default);
}
