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
/// 处罚种类（vault:C7 S6.3：管理员施加的处罚，运行时强制由社交裁决统一承担）。
/// <para>
/// 维护约束：处罚种类的**语义边界决定裁决力度**，不得混淆——
/// <see cref="Mute"/> 只剥夺发言能力（不禁组队、不禁匹配、不阻断他人发来的消息）；
/// <see cref="Ban"/> 剥夺全部定向互动与登录后行为。新增种类必须同步裁决服务与 Admin 镜像。
/// </para>
/// </summary>
public enum OnlinePunishmentKind
{
    /// <summary>
    /// 禁言（只影响发言：定向私聊、队伍/群组/全局频道消息一律拒绝；不影响组队邀请与匹配）。
    /// </summary>
    Mute = 0,

    /// <summary>
    /// 封禁（拒绝全部定向互动：私聊、队伍邀请、匹配成组），映射错误码 <c>AccountBanned</c>。
    /// </summary>
    Ban = 1,
}
