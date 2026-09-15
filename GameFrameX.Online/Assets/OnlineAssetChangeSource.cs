// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯其他合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others that are prohibited by laws and regulations!
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
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Online.Assets;

/// <summary>
/// 资产变更来源（vault:C4 S3.1/S3.6：六类业务来源 + 系统补偿来源；账本红线——来源未登记的资产变更结构性不可达）。
/// <para>
/// 维护约束（资产红线）：任何账本条目必须携带本枚举之一；六类业务来源（支付确认、兑换码、邮件附件、
/// 对局/赛季奖励、Admin 人工、活动任务）是 vault:C4 冻结契约的封闭集合，新增来源须回 vault 契约评审；
/// <see cref="SystemCompensation"/> 仅供统一入口的补偿路径追加反转条目使用，业务调用方禁止指定。
/// 既有系统（Bag/Mail/Reward/Payment/RedeemCode）迁移后只能以本枚举提交统一交易，不得绕过账本（X5）。
/// </para>
/// </summary>
public enum OnlineAssetChangeSource
{
    /// <summary>
    /// 支付确认后的权益发放（业务单号 = 支付订单号；支付「已确认」不等于资产到账，须由本入口生成可审计交易）。
    /// </summary>
    PaymentConfirm = 1,

    /// <summary>
    /// 兑换码兑换（业务单号 = 兑换码批次/流水号；同码重复兑换由幂等键拦截）。
    /// </summary>
    RedeemCode = 2,

    /// <summary>
    /// 邮件附件领取（业务单号 = 邮件附件领取流水号；重复领取只生效一次）。
    /// </summary>
    MailAttachment = 3,

    /// <summary>
    /// 对局奖励与赛季奖励（业务单号 = MatchResultId 等结算标识；结算重试不重复发奖）。
    /// </summary>
    MatchReward = 4,

    /// <summary>
    /// Admin 人工发放或撤销（操作者必填；权限与审批由 Admin 侧承接，本仓只落审计字段）。
    /// </summary>
    AdminOperation = 5,

    /// <summary>
    /// 活动和任务奖励（业务单号 = 活动任务发放流水号）。
    /// </summary>
    ActivityTask = 6,

    /// <summary>
    /// 系统补偿（统一入口补偿路径专属：交易执行中断后的反转条目来源；业务调用方禁止指定）。
    /// </summary>
    SystemCompensation = 7,
}
