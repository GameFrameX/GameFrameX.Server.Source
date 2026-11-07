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

using GameFrameX.Apps.ServerRole.World.Entity;

namespace GameFrameX.Hotfix.Logic.ServerRole.World;

/// <summary>
/// 世界公告业务纯规则（可脱离 Actor 基础设施单测）。
/// </summary>
/// <remarks>
/// 承载与存储无关的公告规则：标题合法性、过期时间校验与过期判定。
/// Agent 只做状态编排，规则判定一律走本类（<c>MailCampaignFilter</c> 先例）。
/// </remarks>
internal static class WorldRules
{
    /// <summary>
    /// 标题长度上限（字符）。
    /// </summary>
    public const int MaxTitleLength = 64;


    /// <summary>
    /// 公告有效期上限（30 天，Unix 秒）。
    /// </summary>
    public const long MaxExpireSeconds = 30 * 24 * 60 * 60;
    /// <summary>
    /// 判定标题是否合法：非空白且不超过长度上限。
    /// </summary>
    /// <param name="title">标题。</param>
    /// <returns>合法返回 true。</returns>
    public static bool IsTitleValid(string title)
    {
        return !string.IsNullOrWhiteSpace(title) && title.Length <= MaxTitleLength;
    }

    /// <summary>
    /// 判定发布请求的过期时间是否合法：须严格晚于当前时间且距今不超过 <see cref="MaxExpireSeconds"/>（30 天）。
    /// </summary>
    /// <param name="expireUnixTime">过期 Unix 秒。</param>
    /// <param name="now">当前 Unix 秒。</param>
    /// <returns>合法返回 true。</returns>
    public static bool IsExpireValid(long expireUnixTime, long now)
    {
        return expireUnixTime > now && expireUnixTime - now <= MaxExpireSeconds;
    }

    /// <summary>
    /// 判定公告是否已过期：当前时间不早于过期时间。
    /// </summary>
    /// <param name="announcement">公告状态。</param>
    /// <param name="now">当前 Unix 秒。</param>
    /// <returns>已过期返回 true。</returns>
    public static bool IsExpired(WorldAnnouncementState announcement, long now)
    {
        return now >= announcement.ExpireUnixTime;
    }
}
