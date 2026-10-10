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

using GameFrameX.Apps.ServerRole.Gm.Entity;
using GameFrameX.Proto.Proto;

namespace GameFrameX.Hotfix.Logic.ServerRole.Gm;

/// <summary>
/// 违规处罚业务纯规则（可脱离 Actor 基础设施单测）。
/// </summary>
/// <remarks>
/// 承载与存储无关的处罚规则：处罚类型合法性、时长区间校验、到期时间计算与过期判定。
/// Agent 只做状态编排，规则判定一律走本类（<c>MailCampaignFilter</c> 先例）。
/// </remarks>
internal static class GmRules
{
    /// <summary>
    /// 处罚时长上限（秒，365 天）。
    /// </summary>
    public const long MaxDurationSeconds = 365 * 24 * 60 * 60;

    /// <summary>
    /// 判定处罚类型是否合法（仅 Ban / Mute 有定义）。
    /// </summary>
    /// <param name="type">处罚类型。</param>
    /// <returns>合法返回 true。</returns>
    public static bool IsTypeDefined(GmPenaltyType type)
    {
        return type == GmPenaltyType.Ban || type == GmPenaltyType.Mute;
    }

    /// <summary>
    /// 判定处罚时长是否合法（开区间下界、闭区间上界：(0, 365 天]）。
    /// </summary>
    /// <param name="durationSeconds">处罚时长（秒）。</param>
    /// <returns>合法返回 true。</returns>
    public static bool IsDurationValid(long durationSeconds)
    {
        return durationSeconds > 0 && durationSeconds <= MaxDurationSeconds;
    }

    /// <summary>
    /// 计算处罚到期 Unix 秒（创建时间 + 时长）。
    /// </summary>
    /// <param name="createdUnixTime">创建 Unix 秒。</param>
    /// <param name="durationSeconds">处罚时长（秒）。</param>
    /// <returns>到期 Unix 秒。</returns>
    public static long ExpireAt(long createdUnixTime, long durationSeconds)
    {
        return createdUnixTime + durationSeconds;
    }

    /// <summary>
    /// 判定处罚是否已过期（当前时间不早于到期时间视为过期）。
    /// </summary>
    /// <param name="penalty">处罚状态。</param>
    /// <param name="now">当前 Unix 秒。</param>
    /// <returns>过期返回 true。</returns>
    public static bool IsExpired(GmPenaltyState penalty, long now)
    {
        return now >= ExpireAt(penalty.CreatedUnixTime, penalty.DurationSeconds);
    }
}
