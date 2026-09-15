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
/// 定向社交互动的目的分类（vault:C7 关键约束：Block 必须在三处统一生效的**枚举载体**）。
/// <para>
/// 维护约束：本枚举是「同一份裁决覆盖三条通路」的落点——Chat 私聊、Party 邀请、Matchmaker 成组
/// 都携带本枚举调用 <see cref="IOnlineSocialGate.EvaluateAsync"/>。
/// 新增定向互动通路**必须**扩展本枚举并走同一裁决入口，禁止在通路内自建判定
/// （那正是 vault:C7 风险表首条要防的「行为不一致，出现绕过」）。
/// 三种目的的裁决力度差异由 <see cref="OnlineSocialDecisionService"/> 承载：
/// 禁言只拒绝 <see cref="DirectMessage"/>，封禁拒绝三者，屏蔽拒绝三者。
/// </para>
/// </summary>
public enum OnlineSocialInteractionPurpose
{
    /// <summary>
    /// 定向私聊消息（含 Direct 频道发送；受屏蔽、禁言、封禁三重裁决）。
    /// </summary>
    DirectMessage = 0,

    /// <summary>
    /// 队伍邀请（受屏蔽、封禁裁决；不受禁言影响——禁言不剥夺组队能力）。
    /// </summary>
    PartyInvite = 1,

    /// <summary>
    /// 匹配成组（受屏蔽、封禁裁决；不受禁言影响）。
    /// </summary>
    Matchmaking = 2,
}
