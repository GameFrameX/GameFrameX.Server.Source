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
/// 资产域告警类型（vault:C4 交易不变量：负余额、负库存、重复订单和异常回滚必须告警）。
/// <para>
/// 维护约束：告警必须携带定位字段（交易标识/作用域/资产标识，见
/// <see cref="OnlineAssetAlertRecord"/>）；告警是事实通知不改变业务结果；
/// 新增告警类型须同步运维 Runbook（告警处置归运维共担项）。
/// </para>
/// </summary>
public enum OnlineAssetAlertKind
{
    /// <summary>
    /// 资产不足尝试（拟扣减后低于下限 0 的拒绝；负余额/负库存出现次数 = 0 的守护输入，VC-3.15）。
    /// </summary>
    NegativeBalanceAttempt = 1,

    /// <summary>
    /// 重复业务意图观察（幂等键命中首次结果的回放；回放本身是正确行为，告警供重复订单巡检，VC-3.1～3.4/3.15）。
    /// </summary>
    DuplicateRequestObserved = 2,

    /// <summary>
    /// 应用中断已补偿（交易执行中断后反转条目追加成功，净效应 0；异常回滚留痕，VC-3.6）。
    /// </summary>
    CompensatedAfterApplyFailure = 3,

    /// <summary>
    /// 补偿失败（反转条目追加失败，交易滞留 CompensationPending；需人工介入，SLO 告警，VC-3.11）。
    /// </summary>
    CompensationFailed = 4,

    /// <summary>
    /// 补偿队列 SLO 超时（跨服补偿滞留超过时限仍未投递成功；超时转人工，VC-3.11）。
    /// </summary>
    CompensationSloExceeded = 5,
}
