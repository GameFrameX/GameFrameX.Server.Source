// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
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

namespace GameFrameX.Online.Tournament;

/// <summary>
/// 赛事报名资格条件（vault:C8 S7.4「资格条件」的配置载体）。
/// <para>
/// 维护约束（红线）：
/// (1) 判定数据源是**关联榜单的实时名次与分数**——赛事开始前榜单即为报名依据，判定发生在报名时刻并当场冻结
/// 判定输入（名次 / 分数随报名登记留存），因此事后榜单变化不会让已通过的报名变得「本不该通过」；
/// (2) 两项条件均为**可选**：取零（<see cref="MinLeaderboardRank"/>）或 null（<see cref="MinLeaderboardScore"/>）
/// 表示该项不限；两项都不限即「无门槛赛事」，此时不读取榜单、任何玩家都可报名；
/// (3) 判定顺序固定为「先名次后分数」——未上榜时无从谈分数，先报 <see cref="OnlineTournamentRegistrationRejection.NotRanked"/>
/// 比报「分数不足」更贴近事实，也避免用一个恒假条件掩盖真正的拒绝原因。
/// </para>
/// </summary>
public sealed class OnlineTournamentEligibility
{
    /// <summary>
    /// 获取或设置名次上限（必须进入关联榜单前 N；0 表示不限）。
    /// </summary>
    public int MinLeaderboardRank
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置分数下限（关联榜单分数不得低于该值；null 表示不限）。
    /// </summary>
    public long? MinLeaderboardScore
    {
        get;
        set;
    }

    /// <summary>
    /// 判定报名资格（判定输入为关联榜单上的名次与分数）。
    /// </summary>
    /// <param name="rank">玩家在关联榜单上的名次（从 1 开始；未上榜传 0）。</param>
    /// <param name="score">玩家在关联榜单上的分数（未上榜传 0）。</param>
    /// <returns>通过返回 <see cref="OnlineTournamentRegistrationRejection.None"/>，否则返回具体拒绝原因。</returns>
    public OnlineTournamentRegistrationRejection Evaluate(int rank, long score)
    {
        if (MinLeaderboardRank > 0)
        {
            if (rank <= 0)
            {
                return OnlineTournamentRegistrationRejection.NotRanked;
            }

            if (rank > MinLeaderboardRank)
            {
                return OnlineTournamentRegistrationRejection.RankTooLow;
            }
        }

        if (MinLeaderboardScore.HasValue && score < MinLeaderboardScore.Value)
        {
            return OnlineTournamentRegistrationRejection.ScoreTooLow;
        }

        return OnlineTournamentRegistrationRejection.None;
    }

    /// <summary>
    /// 判定资格条件是否要求读取关联榜单（两项条件均不限时为 <c>false</c>，报名无需访问榜单）。
    /// </summary>
    /// <returns>需要读取榜单返回 <c>true</c>。</returns>
    public bool RequiresLeaderboardLookup()
    {
        return MinLeaderboardRank > 0 || MinLeaderboardScore.HasValue;
    }

    /// <summary>
    /// 复制资格条件（赛事定义与调用方请求解耦，创建后固化）。
    /// </summary>
    /// <returns>资格条件副本。</returns>
    public OnlineTournamentEligibility Copy()
    {
        return new OnlineTournamentEligibility
        {
            MinLeaderboardRank = MinLeaderboardRank,
            MinLeaderboardScore = MinLeaderboardScore,
        };
    }
}
