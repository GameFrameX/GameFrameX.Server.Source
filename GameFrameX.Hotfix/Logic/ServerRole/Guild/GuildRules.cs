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

namespace GameFrameX.Hotfix.Logic.ServerRole.Guild;

/// <summary>
/// 公会业务纯规则（可脱离 Actor 基础设施单测）。
/// </summary>
/// <remarks>
/// 承载与存储无关的公会规则：名称校验、经验升级阈值、每次审批的经验增量。
/// Agent 只做状态编排，规则判定一律走本类（<c>MailCampaignFilter</c> 先例）。
/// </remarks>
internal static class GuildRules
{
    /// <summary>
    /// 公会名称长度下限（字符）。
    /// </summary>
    public const int MinNameLength = 2;

    /// <summary>
    /// 公会名称长度上限（字符）。
    /// </summary>
    public const int MaxNameLength = 16;

    /// <summary>
    /// 每级升级所需累计经验（阈值累计：等级 = 1 + 经验 / 100）。
    /// </summary>
    public const long ExpPerLevel = 100;

    /// <summary>
    /// 每次审批入会带来的公会经验增量。
    /// </summary>
    public const long ApplyExpGain = 10;

    /// <summary>
    /// 判定公会名称是否合法：非空白且长度在区间 [2, 16]。
    /// </summary>
    /// <param name="name">公会名称。</param>
    /// <returns>合法返回 true。</returns>
    public static bool IsGuildNameValid(string name)
    {
        return !string.IsNullOrWhiteSpace(name) && name.Length >= MinNameLength && name.Length <= MaxNameLength;
    }

    /// <summary>
    /// 按累计经验换算等级：等级 = 1 + 经验 / 100（每 100 经验升一级）。
    /// </summary>
    /// <param name="exp">累计经验。</param>
    /// <returns>公会等级。</returns>
    public static int ExpToLevel(long exp)
    {
        return (int)(1 + exp / ExpPerLevel);
    }
}
