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

namespace GameFrameX.Online.Tournament;

/// <summary>
/// 赛事报名拒绝原因（vault:C8 VC-7.8「被拒绝，原因明确」的机读载体）。
/// <para>
/// 维护约束（红线）：资格不满足是**业务判定而非系统错误**——服务以成功回执携带本枚举返回，
/// 不占用 <c>OnlineErrorCode</c>（该枚举 23 成员冻结，且「资格不足」不属于任何既有段位的语义）。
/// 新增原因时必须同步 <see cref="OnlineTournamentEligibility.Evaluate"/> 与 VC-7.8 用例，
/// 保证每条拒绝路径都有可断言的稳定取值。
/// </para>
/// </summary>
public enum OnlineTournamentRegistrationRejection
{
    /// <summary>
    /// 未被拒绝（通过资格判定）。
    /// </summary>
    None = 0,

    /// <summary>
    /// 未上榜：资格要求关联榜单名次，但玩家在关联榜单上没有条目（无任何可信成绩）。
    /// </summary>
    NotRanked = 1,

    /// <summary>
    /// 名次不足：玩家在关联榜单的名次低于资格要求的上限。
    /// </summary>
    RankTooLow = 2,

    /// <summary>
    /// 分数不足：玩家在关联榜单的分数低于资格要求的下限。
    /// </summary>
    ScoreTooLow = 3,
}
