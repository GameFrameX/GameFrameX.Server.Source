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

namespace GameFrameX.Online.Matchmaking;

/// <summary>
/// 匹配规则快照（vault:C5 S4.8：assignment 必须携带 RuleSnapshot，使阶段 5 可无歧义复现判定）。
/// <para>
/// 维护约束：快照记录的是**本次成组实际生效**的规则，不是当前配置——等待时间扩展会让有效技能区间
/// 与入队时不同，快照必须反映扩展后的值（<see cref="SkillRangeExpanded"/> 标记是否发生扩展），
/// 否则阶段 5 复盘「为什么这两个人匹配到一起」时得到错误答案。
/// </para>
/// </summary>
public sealed class OnlineMatchRuleSnapshot
{
    /// <summary>
    /// 获取或设置玩法模式。
    /// </summary>
    public int Mode
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置区域。
    /// </summary>
    public int Region
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置目标对局规模。
    /// </summary>
    public int TeamSize
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置成组后实际生效的技能区间下界。
    /// </summary>
    public int SkillMin
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置成组后实际生效的技能区间上界。
    /// </summary>
    public int SkillMax
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置参与成组票据的最长等待时长（秒）。
    /// </summary>
    public int WaitSeconds
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置是否发生了等待时间扩展。
    /// </summary>
    public bool SkillRangeExpanded
    {
        get;
        set;
    }

    /// <summary>
    /// 复制快照。
    /// </summary>
    /// <returns>快照副本。</returns>
    public OnlineMatchRuleSnapshot Copy()
    {
        return new OnlineMatchRuleSnapshot
        {
            Mode = Mode,
            Region = Region,
            TeamSize = TeamSize,
            SkillMin = SkillMin,
            SkillMax = SkillMax,
            WaitSeconds = WaitSeconds,
            SkillRangeExpanded = SkillRangeExpanded,
        };
    }
}
