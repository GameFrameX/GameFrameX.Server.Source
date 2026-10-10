// ==========================================================================================
//  GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//  GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//  均受中华人民共和国及相关国际法律法规保护。
//  are protected by the laws of the People's Republic of China and relevant international regulations.
// 
//  使用本项目须严格遵守相应法律法规及开源许可证之规定。
//  Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
// 
//  本项目采用 MIT 许可证与 Apache License 2.0 双许可证分发，
//  This project is dual-licensed under the MIT License and Apache License 2.0,
//  完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//  please refer to the LICENSE file in the root directory of the source code for the full license text.
// 
//  禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//  It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//  侵犯他人合法权益等法律法规所禁止的行为！
//  or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//  因基于本项目二次开发所产生的一切法律纠纷与责任，
//  Any legal disputes and liabilities arising from secondary development based on this project
//  本项目组织与贡献者概不承担。
//  shall be borne solely by the developer; the project organization and contributors assume no responsibility.
// 
//  GitHub 仓库：https://github.com/GameFrameX
//  GitHub Repository: https://github.com/GameFrameX
//  Gitee  仓库：https://gitee.com/GameFrameX
//  Gitee Repository:  https://gitee.com/GameFrameX
//  官方文档：https://gameframex.doc.alianblank.com/
//  Official Documentation: https://gameframex.doc.alianblank.com/
// ==========================================================================================

using System.Collections.Generic;
using GameFrameX.Apps.ServerRole.Team.Entity;

namespace GameFrameX.Hotfix.Logic.ServerRole.Team;

/// <summary>
/// 队伍业务纯规则（可脱离 Actor 基础设施单测）。
/// </summary>
/// <remarks>
/// 承载与存储无关的队伍规则：名称 / 容量校验、离队后的队长转移与解散判定。
/// Agent 只做状态编排，规则判定一律走本类（<c>MailCampaignFilter</c> 先例）。
/// </remarks>
internal static class TeamRules
{
    /// <summary>
    /// 队伍名称长度上限（字符）。
    /// </summary>
    public const int MaxNameLength = 32;

    /// <summary>
    /// 成员数上限允许的最小值。
    /// </summary>
    public const int MinCapacity = 2;

    /// <summary>
    /// 成员数上限允许的最大值。
    /// </summary>
    public const int MaxCapacity = 10;

    /// <summary>
    /// 判定队伍名称是否合法：非空白且不超过长度上限。
    /// </summary>
    /// <param name="name">队伍名称。</param>
    /// <returns>合法返回 true。</returns>
    public static bool IsNameValid(string name)
    {
        return !string.IsNullOrWhiteSpace(name) && name.Length <= MaxNameLength;
    }

    /// <summary>
    /// 判定成员数上限是否合法：区间 [2, 10]。
    /// </summary>
    /// <param name="maxMemberCount">成员数上限。</param>
    /// <returns>合法返回 true。</returns>
    public static bool IsCapacityValid(int maxMemberCount)
    {
        return maxMemberCount >= MinCapacity && maxMemberCount <= MaxCapacity;
    }

    /// <summary>
    /// 解析离队后的新队长：取最早加入的成员（成员列表首位）；无剩余成员返回 0（队伍解散）。
    /// </summary>
    /// <param name="membersAfterLeave">离队后的成员列表（按加入顺序）。</param>
    /// <returns>新队长玩家ID；无成员返回 0。</returns>
    public static long ResolveNewLeader(List<long> membersAfterLeave)
    {
        return membersAfterLeave.Count > 0 ? membersAfterLeave[0] : 0;
    }

    /// <summary>
    /// 判定离队后队伍是否应解散：剩余成员为 0 即解散，否则队长转移。
    /// </summary>
    /// <param name="membersAfterLeave">离队后的成员列表。</param>
    /// <returns>应解散返回 true。</returns>
    public static bool LeaveOutcome(List<long> membersAfterLeave)
    {
        return membersAfterLeave.Count == 0;
    }
}
