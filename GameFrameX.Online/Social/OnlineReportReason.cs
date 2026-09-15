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
/// 举报原因分类（vault:C7 S6.3：玩家侧选择的举报理由，Admin 侧按原因决定处置力度）。
/// <para>
/// 维护约束：<see cref="Other"/> 不是兜底垃圾桶——选择 <see cref="Other"/> 时举报证据字段
/// （<see cref="OnlineReportCase.Evidence"/>）必须非空，否则 Admin 无法处置（VC-6.16 证据链要求）。
/// </para>
/// </summary>
public enum OnlineReportReason
{
    /// <summary>
    /// 骚扰与人身攻击。
    /// </summary>
    Harassment = 0,

    /// <summary>
    /// 辱骂与不当言论。
    /// </summary>
    Abuse = 1,

    /// <summary>
    /// 刷屏与广告。
    /// </summary>
    Spam = 2,

    /// <summary>
    /// 作弊与违规操作。
    /// </summary>
    Cheating = 3,

    /// <summary>
    /// 不良信息（含违法违禁内容）。
    /// </summary>
    Inappropriate = 4,

    /// <summary>
    /// 欺诈与诈骗。
    /// </summary>
    Fraud = 5,

    /// <summary>
    /// 其他（须在 <see cref="OnlineReportCase.Evidence"/> 中补充说明）。
    /// </summary>
    Other = 6,
}
