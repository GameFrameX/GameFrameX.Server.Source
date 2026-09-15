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
/// 举报发生场景（vault:C7 S6.3：举报案件的场景分类，决定需要携带哪些证据标识）。
/// <para>
/// 维护约束：场景不是自由文本而是有限枚举，Admin 侧按场景分派处置队列；
/// 新增场景必须同步 Admin 镜像与错误码表快照之外的分类表，不得在业务代码中按字符串比较场景。
/// </para>
/// </summary>
public enum OnlineReportScene
{
    /// <summary>
    /// 聊天场景（须携带频道标识 <c>ChannelId</c>，定向频道还须携带消息标识 <c>ChatMessageId</c>）。
    /// </summary>
    Chat = 0,

    /// <summary>
    /// 队伍场景（须携带队伍标识 <c>MatchId</c> 字段位的队伍上下文）。
    /// </summary>
    Party = 1,

    /// <summary>
    /// 群组场景。
    /// </summary>
    Group = 2,

    /// <summary>
    /// 匹配与对局场景（须携带对局标识 <c>MatchId</c>）。
    /// </summary>
    Match = 3,

    /// <summary>
    /// 个人资料场景（昵称、头像、签名等展示内容）。
    /// </summary>
    Profile = 4,

    /// <summary>
    /// 好友互动场景（好友申请留言等）。
    /// </summary>
    Friend = 5,
}
