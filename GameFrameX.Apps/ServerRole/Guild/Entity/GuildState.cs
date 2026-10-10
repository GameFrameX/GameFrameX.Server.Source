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

namespace GameFrameX.Apps.ServerRole.Guild.Entity;

/// <summary>
/// 公会服务器（Guild Role，公会管理，域号 680）服务端作用域状态。
/// </summary>
/// <remarks>
/// 承载全部公会档案、名称唯一索引：生命周期由 StateComponent 管理
/// （激活读取 / 消息完成回写 / 定时与停机兜底回存）。
/// </remarks>
public sealed class GuildState : BaseCacheState
{
    /// <summary>
    /// 全部公会。Key: 公会ID。
    /// </summary>
    public Dictionary<long, GuildStateItem> Guilds { get; set; } = new Dictionary<long, GuildStateItem>();

    /// <summary>
    /// 公会名称唯一索引。Key: 公会名称，Value: 公会ID。
    /// </summary>
    public Dictionary<string, long> NameIndex { get; set; } = new Dictionary<string, long>();

    /// <summary>
    /// 下一个公会ID（从 10001 递增）。
    /// </summary>
    public long NextGuildId { get; set; } = 10001;
}

/// <summary>
/// 单个公会状态。
/// </summary>
public sealed class GuildStateItem
{
    /// <summary>
    /// 公会ID。
    /// </summary>
    public long GuildId { get; set; }

    /// <summary>
    /// 公会名称（全局唯一）。
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// 会长玩家ID。
    /// </summary>
    public long LeaderId { get; set; }

    /// <summary>
    /// 成员列表（玩家ID）。
    /// </summary>
    public List<long> Members { get; set; } = new List<long>();

    /// <summary>
    /// 待审批申请列表（玩家ID）。
    /// </summary>
    public List<long> Applications { get; set; } = new List<long>();

    /// <summary>
    /// 公会等级。
    /// </summary>
    public int Level { get; set; }

    /// <summary>
    /// 公会经验（累计，升级阈值见 GuildRules）。
    /// </summary>
    public long Exp { get; set; }
}
