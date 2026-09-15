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
/// 匹配票据（vault:C5 S4.5 冻结字段集：TicketId / PartyId / PlayerIds / AppId / Mode / Region /
/// SkillRange / TeamSize / LatencyRequirement / CustomProperties / CreatedAt / ExpiresAt / Status）。
/// <para>
/// 维护约束（红线）：<see cref="PartyId"/> 非空时 <see cref="PlayerIds"/> 必须是**整队**成员——
/// 队伍完整性（VC-4.3）要求一张票据要么整队成组、要么整队留队，禁止部分成员先走。
/// 票据一经离开 <see cref="OnlineMatchTicketState.Queued"/> 即冻结，任何字段不再改写
/// （终态是事实，不是可编辑状态）。
/// </para>
/// </summary>
public sealed class OnlineMatchTicket
{
    /// <summary>
    /// 获取或设置票据标识。
    /// </summary>
    public string TicketId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置来源队伍标识（单人排队为空字符串）。
    /// </summary>
    public string PartyId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置票据携带的玩家集合（队伍票据为整队成员）。
    /// </summary>
    public List<long> PlayerIds
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
    /// 获取或设置技术水平区间。
    /// </summary>
    public OnlineMatchSkillRange SkillRange
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置目标对局规模（该票据所在组的总人数）。
    /// </summary>
    public int TeamSize
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置延迟要求（毫秒；0 表示不限）。
    /// </summary>
    public int LatencyRequirement
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置自定义匹配属性（玩法自定义条件，不参与基础成组）。
    /// </summary>
    public Dictionary<string, string> CustomProperties
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置入队时刻（UTC 毫秒；成组按此 FIFO 优先）。
    /// </summary>
    public long CreatedAtTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置过期时刻（UTC 毫秒；到点转 <see cref="OnlineMatchTicketState.Expired"/>）。
    /// </summary>
    public long ExpiresAtTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置票据状态。
    /// </summary>
    public OnlineMatchTicketState State
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置失败原因码（成功匹配为 <see cref="OnlineMatchFailureReason.None"/>）。
    /// </summary>
    public OnlineMatchFailureReason FailureReason
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置产出的对局分配标识（未匹配为空字符串）。
    /// </summary>
    public string AssignmentId
    {
        get;
        set;
    }

    /// <summary>
    /// 复制票据（存储层防御性深拷贝使用；玩家集合、技能区间、自定义属性逐项复制）。
    /// </summary>
    /// <returns>票据副本。</returns>
    public OnlineMatchTicket Copy()
    {
        var playerIds = new List<long>();
        if (PlayerIds != null)
        {
            playerIds.AddRange(PlayerIds);
        }

        var properties = new Dictionary<string, string>();
        if (CustomProperties != null)
        {
            foreach (var pair in CustomProperties)
            {
                properties[pair.Key] = pair.Value;
            }
        }

        return new OnlineMatchTicket
        {
            TicketId = TicketId,
            PartyId = PartyId,
            PlayerIds = playerIds,
            TenantId = TenantId,
            AppId = AppId,
            ServerId = ServerId,
            Mode = Mode,
            Region = Region,
            SkillRange = SkillRange == null ? null : SkillRange.Copy(),
            TeamSize = TeamSize,
            LatencyRequirement = LatencyRequirement,
            CustomProperties = properties,
            CreatedAtTime = CreatedAtTime,
            ExpiresAtTime = ExpiresAtTime,
            State = State,
            FailureReason = FailureReason,
            AssignmentId = AssignmentId,
        };
    }
}
