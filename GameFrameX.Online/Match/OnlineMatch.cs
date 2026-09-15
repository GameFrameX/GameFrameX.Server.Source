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

using System.Collections.Generic;
using GameFrameX.Online.Matchmaking;

namespace GameFrameX.Online.Match;

/// <summary>
/// 对局聚合根（vault:C6「Match Actor 持有对局运行状态」）。
/// <para>
/// 维护约束（红线）：本类型是**服务端权威状态**的唯一载体，只能由 <see cref="OnlineMatchActor"/> 改写——
/// 客户端提交的是 <see cref="OnlineMatchInput"/>（意图），胜负与奖励从不来自客户端（VC-5.2）。
/// </para>
/// <para>
/// <see cref="Version"/> 是乐观并发标记：每次成功改写自增，存储层按期望版本做 CAS
/// （同一对局被两个 Actor 同时处理时，后写者必然失败，避免串局，VC-5.13）。
/// <see cref="Events"/> 是有界事件日志，仅供重连增量补发读取，超出上限后最旧事件被丢弃。
/// </para>
/// </summary>
public sealed class OnlineMatch
{
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
    /// 获取或设置对局标识。
    /// </summary>
    public string MatchId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置来源分配标识（vault:C5 上游，创建后不再改写）。
    /// </summary>
    public string AssignmentId
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
    /// 获取或设置成组时冻结的规则快照（复盘「为什么这两个人匹配到一起」的依据）。
    /// </summary>
    public OnlineMatchRuleSnapshot RuleSnapshot
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置当前生命周期状态。
    /// </summary>
    public OnlineMatchState State
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置成员列表（含断线成员，断线不立即等于退出）。
    /// </summary>
    public List<OnlineMatchMember> Members
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置服务端权威序号（每次被接受的输入或服务器事件推进一次）。
    /// </summary>
    public long ServerSequence
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置玩法私有状态载荷（不透明字节，由 <see cref="IOnlineMatchGame"/> 解释）。
    /// </summary>
    public byte[] GameState
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置有界服务器事件日志（按序号升序，供重连增量补发）。
    /// </summary>
    public List<OnlineMatchServerEvent> Events
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置结算结果（未结算为 null；一经落定不可改写）。
    /// </summary>
    public OnlineMatchResult Result
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置创建时刻（UTC 毫秒）。
    /// </summary>
    public long CreatedTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置最近一次状态变更时刻（UTC 毫秒）。
    /// </summary>
    public long StateChangedTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置当前阶段截止时刻（UTC 毫秒；由 Actor 按阶段时限重算）。
    /// </summary>
    public long DeadlineTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置进入结束态的时刻（UTC 毫秒；未结束为 0）。
    /// </summary>
    public long EndedTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置乐观并发版本（每次成功改写自增）。
    /// </summary>
    public long Version
    {
        get;
        set;
    }

    /// <summary>
    /// 按玩家标识查找成员。
    /// </summary>
    /// <param name="playerId">玩家标识。</param>
    /// <returns>成员；非成员返回 null。</returns>
    public OnlineMatchMember FindMember(long playerId)
    {
        if (Members == null)
        {
            return null;
        }

        foreach (var member in Members)
        {
            if (member != null && member.PlayerId == playerId)
            {
                return member;
            }
        }

        return null;
    }

    /// <summary>
    /// 统计处于参与态（已加入 / 已准备 / 对局中）的成员数；断线成员不计入。
    /// </summary>
    /// <returns>参与态成员数。</returns>
    public int CountActiveMembers()
    {
        var count = 0;
        if (Members == null)
        {
            return count;
        }

        foreach (var member in Members)
        {
            if (member != null && (member.State == OnlineMatchMemberState.Joined || member.State == OnlineMatchMemberState.Ready || member.State == OnlineMatchMemberState.Playing))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// 复制对局（存储、快照与跨线程传递使用；成员、事件、载荷全量深拷贝）。
    /// </summary>
    /// <returns>对局副本。</returns>
    public OnlineMatch Copy()
    {
        var members = new List<OnlineMatchMember>();
        if (Members != null)
        {
            foreach (var member in Members)
            {
                members.Add(member == null ? null : member.Copy());
            }
        }

        var events = new List<OnlineMatchServerEvent>();
        if (Events != null)
        {
            foreach (var item in Events)
            {
                events.Add(item == null ? null : item.Copy());
            }
        }

        return new OnlineMatch
        {
            TenantId = TenantId,
            AppId = AppId,
            ServerId = ServerId,
            MatchId = MatchId,
            AssignmentId = AssignmentId,
            Mode = Mode,
            Region = Region,
            RuleSnapshot = RuleSnapshot == null ? null : RuleSnapshot.Copy(),
            State = State,
            Members = members,
            ServerSequence = ServerSequence,
            GameState = GameState == null ? null : (byte[])GameState.Clone(),
            Events = events,
            Result = Result == null ? null : Result.Copy(),
            CreatedTime = CreatedTime,
            StateChangedTime = StateChangedTime,
            DeadlineTime = DeadlineTime,
            EndedTime = EndedTime,
            Version = Version,
        };
    }
}
