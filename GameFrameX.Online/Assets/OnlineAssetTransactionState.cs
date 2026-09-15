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
/// 资产交易状态（vault:C4 交易不变量「失败不能留下不可解释的半成品状态」的状态机承载）。
/// <para>
/// 维护约束（红线）：合法迁移只有 <see cref="Executing"/>→<see cref="Succeeded"/>、
/// <see cref="Executing"/>→<see cref="Failed"/>（应用前失败，零账本条目）、
/// <see cref="Executing"/>→<see cref="CompensationPending"/>（应用中断，已落条目待反转）、
/// <see cref="CompensationPending"/>→<see cref="Compensated"/>（反转条目已追加，净效应为 0）；
/// 终态不可再迁移；<see cref="CompensationPending"/> 是唯一的非终态中间态，
/// 重启恢复（<c>OnlineGrantService.RecoverAsync</c>）与对账巡检以它为扫描输入。
/// </para>
/// </summary>
public enum OnlineAssetTransactionState
{
    /// <summary>
    /// 执行中（幂等占位已取得，资产应用尚未落定）。
    /// </summary>
    Executing = 1,

    /// <summary>
    /// 已成功（账本条目已落账且余额/库存快照已同步更新）。
    /// </summary>
    Succeeded = 2,

    /// <summary>
    /// 已失败（应用前校验不通过或无已落条目的中断；净效应为 0，可换键重试）。
    /// </summary>
    Failed = 3,

    /// <summary>
    /// 待补偿（应用中途中断且已有账本条目；等待反转条目追加，非终态）。
    /// </summary>
    CompensationPending = 4,

    /// <summary>
    /// 已补偿（反转条目已追加，原交易净效应为 0；历史账本不改写）。
    /// </summary>
    Compensated = 5,
}
