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
/// 统一资产入口操作类型（vault:C4 S3.4：发放/扣除/撤销/补发/人工调整五操作共用同一交易管道）。
/// <para>
/// 维护约束：所有操作共用幂等与账本语义，操作类型只影响审计语义与部分校验
/// （<see cref="Revoke"/> 与 <see cref="Adjust"/> 必须携带操作者——人工资金动作留痕红线）；
/// 方向语义由变更行的带符号数额表达（发放为正、扣除为负），操作枚举本身不隐含方向，
/// 避免混合批次（如对局结算同时发奖与扣入场费）无法表达。
/// </para>
/// </summary>
public enum OnlineGrantOperation
{
    /// <summary>
    /// 发放（向玩家增加资产；数额为正）。
    /// </summary>
    Grant = 1,

    /// <summary>
    /// 扣除（从玩家减少资产；数额为负，余额下限 0 校验）。
    /// </summary>
    Deduct = 2,

    /// <summary>
    /// 撤销（撤销既有发放；操作者必填，审计红线）。
    /// </summary>
    Revoke = 3,

    /// <summary>
    /// 补发（对失败/中断的发放重新提交；复用幂等键保证不重复生效）。
    /// </summary>
    Reissue = 4,

    /// <summary>
    /// 人工调整（Admin 运营调账；操作者必填，正负数额均可）。
    /// </summary>
    Adjust = 5,
}
