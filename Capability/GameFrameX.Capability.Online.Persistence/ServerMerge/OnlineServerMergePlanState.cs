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
//   or infringe upon the legal rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any disputes or liabilities arising from secondary development based on this project
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

namespace GameFrameX.Capability.Online.Persistence;

using GameFrameX.DataBase;
using GameFrameX.Foundation.Orm.Attribute;

/// <summary>
/// 合服计划持久化行（键 = <c>PlanId</c> 唯一；计划与 dry-run / 执行报告以 JSON 载荷整存整取，
/// 行键为全局域自增 Id，不携带任何区服维度）。
/// <para>
/// 载荷语义：PlanJson 建行时写入不再变更（等价集合同 PlanId，幂等登记回读既有行）；
/// DryRunReportJson 为最近一次预演报告（真实执行的强制前置凭据）；
/// ExecutedReportJson 为执行落定报告（幂等回放凭据）；
/// MergedEventPublishedAtUnixMilliseconds 为合服完成事件发布记账（0 = 未发布，重试回放据此补发）。
/// </para>
/// </summary>
public sealed class OnlineServerMergePlanState : BaseCacheState
{
    /// <summary>
    /// 归并计划标识（<c>merge-{target}:{sources 升序}</c>；全表唯一，唯一插入谓词键）。
    /// </summary>
    [EntityIndex("ux_online_server_merge_plan_plan_id", Unique = true)]
    public string PlanId { get; set; } = string.Empty;

    /// <summary>
    /// 归并计划 JSON 载荷。
    /// </summary>
    public string PlanJson { get; set; } = string.Empty;

    /// <summary>
    /// 最近一次 dry-run 报告 JSON 载荷（空串 = 未预演）。
    /// </summary>
    public string DryRunReportJson { get; set; } = string.Empty;

    /// <summary>
    /// 执行落定报告 JSON 载荷（空串 = 未执行）。
    /// </summary>
    public string ExecutedReportJson { get; set; } = string.Empty;

    /// <summary>
    /// 合服完成事件发布时刻（UTC 毫秒；0 = 未发布）。
    /// </summary>
    public long MergedEventPublishedAtUnixMilliseconds { get; set; }
}
