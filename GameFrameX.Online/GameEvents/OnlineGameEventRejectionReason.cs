// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
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

namespace GameFrameX.Online.GameEvents;

/// <summary>
/// 游戏事件 L0 校验的拒绝码（vault:C8 S7.5 四类拒绝；**不是** <c>OnlineErrorCode</c>）。
/// <para>
/// 维护约束（红线）：拒绝码描述的是**脏事件本身**（投递方数据问题），不是业务判定结果，
/// 因而不占用 23 个已冻结的 <c>OnlineErrorCode</c> 成员（对齐 C99 <c>OnlineSocialDecision</c> /
/// C104 赛事报名拒绝码的同一取舍）。拒绝码**只增不改**。
/// </para>
/// </summary>
public enum OnlineGameEventRejectionReason
{
    /// <summary>未拒绝。</summary>
    None = 0,

    /// <summary>事件名未登记（不在 17 个标准事件名内）。</summary>
    UnknownEventName = 1,

    /// <summary>版本非法（小于 1 或超出登记表声明的 <c>CurrentVersion</c>）。</summary>
    UnsupportedVersion = 2,

    /// <summary>必需字段缺失（登记表声明的必需载荷字段未在载荷中提供）。</summary>
    MissingRequiredField = 3,

    /// <summary>作用域非法（TenantId 或 AppId 非正数——无法归属的事件无法被任何下游消费）。</summary>
    InvalidScope = 4,
}
