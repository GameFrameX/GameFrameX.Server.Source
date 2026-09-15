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
/// 群组成员角色（vault:C7 S6.4：Owner &gt; Admin &gt; Member 三级）。
/// <para>
/// 维护约束：角色是权限判据的**唯一**来源，不额外落权限位掩码——同一判据存两处必然产生不一致。
/// 数值越小权限越高（<see cref="Owner"/> = 0），因此「操作者能否处置目标」等价于数值比较
/// （大者受小者管辖），不需要另立权限等级表。
/// </para>
/// </summary>
public enum OnlineGroupRole
{
    /// <summary>
    /// 群主（建群者；群内唯一）。不可被踢出、不可自行退出（须先解散），仅群主可调整成员角色与解散群组。
    /// </summary>
    Owner = 0,

    /// <summary>
    /// 管理员（可写入 Metadata、可撤销邀请、可踢出普通成员；不能处置同级管理员或群主）。
    /// </summary>
    Admin = 1,

    /// <summary>
    /// 普通成员（默认角色；只可查看成员列表、邀请他人、自行退群）。
    /// </summary>
    Member = 2,
}
