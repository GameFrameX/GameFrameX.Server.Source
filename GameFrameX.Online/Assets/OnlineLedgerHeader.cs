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
//   please see the LICENSE file in the root directory of the source code.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯其他合法权益等法律法规所禁止的行为！
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
// ==========================================================================================

namespace GameFrameX.Online.Assets;

/// <summary>
/// 账本头载荷（<see cref="OnlineAssetChangeBatch"/> 与 <see cref="OnlineLedgerEntry"/> 共享的追溯头字段：
/// 作用域（租户/App/玩家/归属服/发起服）+ 来源/操作/原因/业务单号/操作者——VC-3.14 全字段追溯的公共半边）。
/// <para>
/// 维护约束：头字段一经落账即为事实；新增追溯维度只改本载荷，不再加长实体构造器。
/// </para>
/// </summary>
public sealed class OnlineLedgerHeader
{
    /// <summary>
    /// 获取或设置租户标识。
    /// </summary>
    /// <remarks>Gets or sets the tenant id.</remarks>
    public long TenantId { get; init; }

    /// <summary>
    /// 获取或设置 App 标识。
    /// </summary>
    /// <remarks>Gets or sets the app id.</remarks>
    public long AppId { get; init; }

    /// <summary>
    /// 获取或设置玩家标识。
    /// </summary>
    /// <remarks>Gets or sets the player id.</remarks>
    public long PlayerId { get; init; }

    /// <summary>
    /// 获取或设置归属服标识（资产所有者的家服；跨服发奖的路由归属，VC-3.10）。
    /// </summary>
    /// <remarks>Gets or sets the home server id (the asset owner's home server; VC-3.10).</remarks>
    public long HomeServerId { get; init; }

    /// <summary>
    /// 获取或设置发起服标识（执行本次变更的服务器；归属服只表运行位置，不做数据隔离）。
    /// </summary>
    /// <remarks>Gets or sets the initiating server id (the server performing the change).</remarks>
    public long InitiatingServerId { get; init; }

    /// <summary>
    /// 获取或设置变更来源（六类业务来源或系统补偿；未登记来源结构性不可达）。
    /// </summary>
    /// <remarks>Gets or sets the change source.</remarks>
    public OnlineAssetChangeSource Source { get; init; }

    /// <summary>
    /// 获取或设置操作类型（发放/扣除/撤销/补发/人工调整）。
    /// </summary>
    /// <remarks>Gets or sets the grant operation.</remarks>
    public OnlineGrantOperation Operation { get; init; }

    /// <summary>
    /// 获取或设置变更原因（业务语义描述，审计追溯必填）。
    /// </summary>
    /// <remarks>Gets or sets the change reason (required for audit tracing).</remarks>
    public string Reason { get; init; }

    /// <summary>
    /// 获取或设置业务单号（支付订单号/兑换码流水/邮件附件流水/结算标识等来源侧单号）。
    /// </summary>
    /// <remarks>Gets or sets the business order id.</remarks>
    public string BusinessOrderId { get; init; }

    /// <summary>
    /// 获取或设置操作者（会话或管理员标识；系统来源为空字符串，人工资金动作必填）。
    /// </summary>
    /// <remarks>Gets or sets the operator id (empty for system sources).</remarks>
    public string OperatorId { get; init; }

    /// <summary>
    /// 从既有交易构造账本头（批次与账本条目的共同来源：交易已是头字段的权威事实）。
    /// </summary>
    /// <param name="transaction">资产交易。</param>
    /// <returns>账本头载荷。</returns>
    public static OnlineLedgerHeader FromTransaction(OnlineAssetTransaction transaction)
    {
        return new OnlineLedgerHeader
        {
            TenantId = transaction.TenantId,
            AppId = transaction.AppId,
            PlayerId = transaction.PlayerId,
            HomeServerId = transaction.HomeServerId,
            InitiatingServerId = transaction.InitiatingServerId,
            Source = transaction.Source,
            Operation = transaction.Operation,
            Reason = transaction.Reason,
            BusinessOrderId = transaction.BusinessOrderId,
            OperatorId = transaction.OperatorId,
        };
    }
}
