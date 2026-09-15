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
/// 对局分配（vault:C5 S4.8 冻结字段集：AssignmentId / MatchId / PlayerIds / Mode / Region /
/// RuleSnapshot / CreatedAt）。阶段 5（C98）消费本类型创建 Match，不需要任何额外隐式约定。
/// <para>
/// 维护约束（红线）：assignment 是**不可变事实**——产生后不得改写。VC-4.2 要求「取消不产生 assignment
/// 或产生后客户端被拒绝入局」；由于 assignment 与票据终态在同一原子边界内落定，
/// 一旦产生即代表匹配已成立，取消只会作用于未成组的票据。
/// </para>
/// </summary>
public sealed class OnlineMatchAssignment
{
    /// <summary>
    /// 获取或设置分配标识。
    /// </summary>
    public string AssignmentId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置对局标识（阶段 5 以此创建 Match）。
    /// </summary>
    public string MatchId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置本分配的玩家集合（队伍票据为整队成员，VC-4.3）。
    /// </summary>
    public List<long> PlayerIds
    {
        get;
        set;
    }

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
    /// 获取或设置规则快照。
    /// </summary>
    public OnlineMatchRuleSnapshot RuleSnapshot
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置产生时刻（UTC 毫秒）。
    /// </summary>
    public long CreatedAtTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置本分配消费的票据标识集合（VC-4.12 唯一性追溯的依据）。
    /// </summary>
    public List<string> TicketIds
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置租户标识（作用域隔离键）。
    /// </summary>
    public long TenantId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置 App 标识（作用域隔离键）。
    /// </summary>
    public long AppId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置区服标识（作用域隔离键）。
    /// </summary>
    public long ServerId
    {
        get;
        set;
    }

    /// <summary>
    /// 复制分配（存储层防御性深拷贝使用）。
    /// </summary>
    /// <returns>分配副本。</returns>
    public OnlineMatchAssignment Copy()
    {
        var playerIds = new List<long>();
        if (PlayerIds != null)
        {
            playerIds.AddRange(PlayerIds);
        }

        var ticketIds = new List<string>();
        if (TicketIds != null)
        {
            ticketIds.AddRange(TicketIds);
        }

        return new OnlineMatchAssignment
        {
            AssignmentId = AssignmentId,
            MatchId = MatchId,
            PlayerIds = playerIds,
            Mode = Mode,
            Region = Region,
            RuleSnapshot = RuleSnapshot == null ? null : RuleSnapshot.Copy(),
            CreatedAtTime = CreatedAtTime,
            TicketIds = ticketIds,
            TenantId = TenantId,
            AppId = AppId,
            ServerId = ServerId,
        };
    }
}
