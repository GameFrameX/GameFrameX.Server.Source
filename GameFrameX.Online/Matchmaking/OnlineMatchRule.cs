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
/// 匹配规则（vault:C5 S4.6：第一版匹配规则——模式/区域/队伍规模/等级段位范围/等待时间扩展）。
/// <para>
/// 维护约束（红线）：规则以配置驱动，**不得**在协调器里硬编码条件（vault:C5 风险表：规则硬编码会让
/// 后续扩展需重写）。本类是纯函数——不读写存储、不感知时钟以外的外部状态，
/// 因此「为什么这两张票据能成组」在测试中可精确复现。
/// </para>
/// </summary>
public sealed class OnlineMatchRule
{
    /// <summary>等待时间扩展阈值（秒）。</summary>
    private readonly int _waitExpansionThresholdSeconds;

    /// <summary>技能区间扩展间隔（秒）。</summary>
    private readonly int _expansionIntervalSeconds;

    /// <summary>单档扩展量。</summary>
    private readonly int _skillExpansionPerInterval;

    /// <summary>
    /// 初始化 <see cref="OnlineMatchRule"/>。
    /// </summary>
    /// <param name="options">匹配与限流可配置项。</param>
    public OnlineMatchRule(OnlineMatchmakerOptions options)
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        _waitExpansionThresholdSeconds = options.WaitExpansionThresholdSeconds;
        _expansionIntervalSeconds = options.ExpansionIntervalSeconds;
        _skillExpansionPerInterval = options.SkillExpansionPerInterval;
    }

    /// <summary>
    /// 计算票据当前实际生效的技能区间（含等待时间扩展，VC-4.5）。
    /// <para>
    /// 规则：等待时长未达阈值时不扩展；超过阈值后每满一个 <c>ExpansionIntervalSeconds</c> 放宽一档，
    /// 单档放宽 <c>SkillExpansionPerInterval</c>。扩展单调不减——等待越久区间只可能更宽。
    /// </para>
    /// </summary>
    /// <param name="ticket">票据。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒）。</param>
    /// <returns>生效技能区间（票据无区间时返回 null）。</returns>
    public OnlineMatchSkillRange EffectiveSkillRange(OnlineMatchTicket ticket, long nowUnixMilliseconds)
    {
        if (ticket == null || ticket.SkillRange == null)
        {
            return null;
        }

        var waitSeconds = (nowUnixMilliseconds - ticket.CreatedAtTime) / 1000;
        if (waitSeconds < _waitExpansionThresholdSeconds)
        {
            return ticket.SkillRange.Copy();
        }

        var overdueSeconds = waitSeconds - _waitExpansionThresholdSeconds;
        var steps = 1 + (int)(overdueSeconds / _expansionIntervalSeconds);
        return ticket.SkillRange.Widen(steps * _skillExpansionPerInterval);
    }

    /// <summary>
    /// 判定两张票据当前是否可同组（模式/区域/目标规模/延迟要求一致，且技能区间有交集）。
    /// </summary>
    /// <param name="anchor">基准票据。</param>
    /// <param name="candidate">候选票据。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒）。</param>
    /// <returns>可同组返回 <c>true</c>。</returns>
    public bool CanGroup(OnlineMatchTicket anchor, OnlineMatchTicket candidate, long nowUnixMilliseconds)
    {
        if (anchor == null || candidate == null || anchor.TicketId == candidate.TicketId)
        {
            return false;
        }

        if (anchor.State != OnlineMatchTicketState.Queued || candidate.State != OnlineMatchTicketState.Queued)
        {
            return false;
        }

        if (anchor.Mode != candidate.Mode || anchor.Region != candidate.Region)
        {
            return false;
        }

        if (anchor.TeamSize != candidate.TeamSize || anchor.TeamSize <= 0)
        {
            return false;
        }

        if (anchor.LatencyRequirement != candidate.LatencyRequirement)
        {
            return false;
        }

        var anchorRange = EffectiveSkillRange(anchor, nowUnixMilliseconds);
        var candidateRange = EffectiveSkillRange(candidate, nowUnixMilliseconds);
        if (anchorRange == null || candidateRange == null)
        {
            return false;
        }

        return anchorRange.Intersects(candidateRange);
    }

    /// <summary>
    /// 计算票据当前等待时长（秒）。
    /// </summary>
    /// <param name="ticket">票据。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒）。</param>
    /// <returns>等待秒数（不小于 0）。</returns>
    public static int WaitSeconds(OnlineMatchTicket ticket, long nowUnixMilliseconds)
    {
        if (ticket == null || nowUnixMilliseconds <= ticket.CreatedAtTime)
        {
            return 0;
        }

        return (int)((nowUnixMilliseconds - ticket.CreatedAtTime) / 1000);
    }

    /// <summary>
    /// 汇总一组票据的成员总数。
    /// </summary>
    /// <param name="tickets">票据集合。</param>
    /// <returns>去重后的玩家总数。</returns>
    public static int TotalPlayerCount(IReadOnlyList<OnlineMatchTicket> tickets)
    {
        var players = new HashSet<long>();
        foreach (var ticket in tickets)
        {
            foreach (var playerId in ticket.PlayerIds)
            {
                players.Add(playerId);
            }
        }

        return players.Count;
    }
}
