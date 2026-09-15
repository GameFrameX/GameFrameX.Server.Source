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

namespace GameFrameX.Online.Audit;

/// <summary>
/// 统一审计域词表（vault:C9 S8.2：支付、奖励、邮件、兑换码、远程配置与处罚六域审计接入统一链路；
/// <see cref="Operation"/> 为 S8.1 受控操作审计落点——VC-8.3 的「审计查询」检索对象）。
/// <para>
/// 维护约束（红线）：域值是**线上原值**，检索过滤按本词表匹配；六域清单为 vault:C9 契约字面集合，
/// 新增或重命名属**跨仓契约变更**，须先改 vault:C9 契约与 Admin 侧映射，再改本词表；
/// 本仓不得单方面扩展。未知域在接入时被白名单拒绝（<see cref="IsKnown"/>，VC-8.3 审计完整性），
/// 防止各域私造域值使统一审计链路重新分裂为各域私有（C29 风险表）。
/// </para>
/// </summary>
public static class OnlineAuditDomain
{
    /// <summary>
    /// 支付域（Admin 既有支付审计：订单、退款、充值等）。
    /// </summary>
    public const string Payment = "Payment";

    /// <summary>
    /// 奖励域（Admin 既有奖励审计：发放、撤销、补发等）。
    /// </summary>
    public const string Reward = "Reward";

    /// <summary>
    /// 邮件域（Admin 既有邮件审计：系统邮件发送、附件发放等）。
    /// </summary>
    public const string Mail = "Mail";

    /// <summary>
    /// 兑换码域（Admin 既有兑换码审计：生成、作废、兑换等）。
    /// </summary>
    public const string RedeemCode = "RedeemCode";

    /// <summary>
    /// 远程配置域（Admin 既有远程配置审计：配置发布、回滚等；App 级操作，区服标识通常为 0）。
    /// </summary>
    public const string RemoteConfig = "RemoteConfig";

    /// <summary>
    /// 处罚域（Admin 既有处罚审计：封禁、禁言、解封等）。
    /// </summary>
    public const string Penalty = "Penalty";

    /// <summary>
    /// 受控操作域（vault:C9 S8.1 受控操作审计：踢下线、取消 Ticket、结束异常 Match、暂停恢复、冻结、
    /// 发放撤销资产、匹配隔离等经权限判定后的操作留痕——VC-8.3 检索对象）。
    /// </summary>
    public const string Operation = "Operation";

    /// <summary>
    /// 判定域值是否在白名单内（接入校验入口；大小写敏感——域值是线上原值，不做大小写归一）。
    /// </summary>
    /// <param name="domain">待判定域值。</param>
    /// <returns>已知域返回 <c>true</c>；<see langword="null"/>、空串或未知域返回 <c>false</c>。</returns>
    public static bool IsKnown(string domain)
    {
        return domain == Payment
               || domain == Reward
               || domain == Mail
               || domain == RedeemCode
               || domain == RemoteConfig
               || domain == Penalty
               || domain == Operation;
    }
}
