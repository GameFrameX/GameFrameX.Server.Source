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
/// 举报案件存储契约（vault:C7 S6.3：举报证据链的持久化落点，VC-6.16）。
/// <para>
/// 维护约束：案件状态只能经 <see cref="ReplaceAsync"/> 的 CAS 语义改写——期望状态不匹配即整体失败并返回 null，
/// 绝不部分应用；「Admin 裁决 vs 玩家撤回」并发时由此收敛到同一临界区，谁先改到目标态谁生效。
/// 存储实现必须在 CAS 失败时不留任何写入痕迹，并负责防御性深拷贝。
/// </para>
/// </summary>
public interface IOnlineReportStore
{
    /// <summary>
    /// 以案件标识为唯一键「不存在则创建」：键已存在时返回既有记录且不写入。
    /// <para>
    /// 适用范围（避免误当成「重复举报去重」）：唯一键是**案件标识**，而
    /// <see cref="OnlineSocialDecisionService.SubmitReportAsync"/> 每次调用都新铸一个标识，
    /// 因此本方法**不提供跨调用的提交去重**——同一次双击会落两条案件。它守的是「同一标识的重复写入」：
    /// 按标识重放的写入（客户端重试带着服务端已返回的标识回来、或未来的持久化实现做 upsert）收敛到同一条，
    /// 不会凭空多出一条。
    /// </para>
    /// <para>
    /// 为什么不用「举报人 + 被举报人 + 证据」这类内容签名当去重键：撤回后重新举报、以及 Admin 驳回后
    /// 补充新证据再次举报（VC-6.16 证据链）都是**合法**流程，内容去重会把它们一并吞掉，
    /// 玩家视角就是「我明明举报了却查不到」。提交去重只能由调用方显式给出的请求键承担，
    /// 而 C27 范围未定义该键（Admin 命令面 S6.10 归后续变更）。
    /// </para>
    /// </summary>
    /// <param name="reportCase">待创建的案件（作用域已由调用方落定）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>当前生效的案件副本（既有记录或刚落库的入参）。</returns>
    Task<OnlineReportCase> SaveIfAbsentAsync(OnlineReportCase reportCase, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按案件标识查找。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="reportId">案件标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>案件副本；不存在返回 null。</returns>
    Task<OnlineReportCase> FindAsync(long tenantId, long appId, string reportId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 以 CAS 语义改写案件状态与处置结果。
    /// </summary>
    /// <param name="reportCase">改写后的案件（其标识与作用域须与既有记录一致）。</param>
    /// <param name="expectedState">期望的当前状态；不匹配即失败。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>更新后的案件副本；CAS 失败或记录不存在返回 null。</returns>
    Task<OnlineReportCase> ReplaceAsync(OnlineReportCase reportCase, OnlineReportState expectedState, CancellationToken cancellationToken = default);

    /// <summary>
    /// 列出某举报人提交的案件（玩家侧「我的举报」入口）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="reporterId">举报人。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>案件副本列表。</returns>
    Task<IReadOnlyList<OnlineReportCase>> ListByReporterAsync(long tenantId, long appId, long reporterId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 列出作用域内处于指定状态的案件（Admin 待办队列输入）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="state">案件状态。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>案件副本列表。</returns>
    Task<IReadOnlyList<OnlineReportCase>> ListByStateAsync(long tenantId, long appId, OnlineReportState state, CancellationToken cancellationToken = default);
}
