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
/// 通知来源分类（vault:C7 S6.9：好友、组队、匹配、结算、资产、邮件、公告、处罚与申诉接入通知域）。
/// <para>
/// 维护约束：分类表达「来源域归属」，不是权限判据、不是路由判据——通知域不按分类分派业务逻辑，
/// 载荷由来源域自行序列化（通知域视为不透明，见 <see cref="OnlineNotification.Payload"/>）；
/// 新增成员必须同步来源域接入说明，禁止把同一业务的不同阶段（如「邀请已发出」/「邀请被接受」）
/// 拆成两个分类——阶段差异应由载荷表达。
/// </para>
/// </summary>
public enum OnlineNotificationKind
{
    /// <summary>
    /// 组队邀请（来源：队伍域）。
    /// </summary>
    PartyInvite = 0,

    /// <summary>
    /// 匹配结果（来源：匹配域）。
    /// </summary>
    MatchResult = 1,

    /// <summary>
    /// 对局结算（来源：对局域）。
    /// </summary>
    MatchSettlement = 2,

    /// <summary>
    /// 资产变化（来源：资产域）。
    /// </summary>
    AssetChanged = 3,

    /// <summary>
    /// 邮件（来源：邮件域）。
    /// </summary>
    Mail = 4,

    /// <summary>
    /// 好友变更（来源：好友域）。
    /// </summary>
    FriendChanged = 5,

    /// <summary>
    /// Admin 公告（来源：运营后台）。
    /// </summary>
    Announcement = 6,

    /// <summary>
    /// 处罚（来源：Admin 处罚流程，VC-6.17 的运行时限时通知）。
    /// </summary>
    Punishment = 7,

    /// <summary>
    /// 申诉（来源：Admin 申诉流程）。
    /// </summary>
    Appeal = 8,
}
