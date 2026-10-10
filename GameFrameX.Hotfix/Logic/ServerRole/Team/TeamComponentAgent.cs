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

using GameFrameX.Apps.ServerRole.Team.Component;
using GameFrameX.Apps.ServerRole.Team.Entity;
using GameFrameX.Foundation.Utility;

namespace GameFrameX.Hotfix.Logic.ServerRole.Team;

/// <summary>
/// 队伍服务器（Team Role，队伍管理，域号 670）业务组件代理。
/// </summary>
/// <remarks>
/// Team Role 独立业务逻辑系统（C197 垂直切片）的热更侧落点。
/// 业务规则判定一律委托 <see cref="TeamRules"/>（纯函数，可单测）；
/// 本类只做状态编排：队伍取号创建、一人一队约束、队长转移与空队解散。
/// </remarks>
public class TeamComponentAgent : StateComponentAgent<TeamComponent, TeamState>
{
    /// <summary>
    /// 创建队伍：名称 / 容量校验，一人一队约束；创建者为队长。
    /// </summary>
    /// <param name="playerId">创建者玩家ID。</param>
    /// <param name="request">创建请求。</param>
    /// <param name="response">创建响应。</param>
    public Task OnCreateAsync(long playerId, ReqTeamCreate request, RespTeamCreate response)
    {
        if (!TeamRules.IsNameValid(request.Name))
        {
            response.ErrorCode = (int)TeamErrorCode.NameInvalid;
            return Task.CompletedTask;
        }

        if (!TeamRules.IsCapacityValid(request.MaxMemberCount))
        {
            response.ErrorCode = (int)TeamErrorCode.CapacityInvalid;
            return Task.CompletedTask;
        }

        if (TryGetTeamOf(playerId, out _))
        {
            response.ErrorCode = (int)TeamErrorCode.AlreadyInTeam;
            return Task.CompletedTask;
        }

        var team = new TeamStateItem
        {
            TeamId = State.NextTeamId++,
            Name = request.Name,
            LeaderId = playerId,
            Members = new List<long> { playerId },
            MaxMemberCount = request.MaxMemberCount,
            CreatedUnixTime = TimerHelper.UnixTimeSeconds(),
        };
        State.Teams[team.TeamId] = team;
        response.TeamId = team.TeamId;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 加入队伍：一人一队约束、队伍存在、满员拒绝。
    /// </summary>
    /// <param name="playerId">加入者玩家ID。</param>
    /// <param name="request">加入请求。</param>
    /// <param name="response">加入响应。</param>
    public Task OnJoinAsync(long playerId, ReqTeamJoin request, RespTeamJoin response)
    {
        if (TryGetTeamOf(playerId, out _))
        {
            response.ErrorCode = (int)TeamErrorCode.AlreadyInTeam;
            return Task.CompletedTask;
        }

        if (!State.Teams.TryGetValue(request.TeamId, out var team))
        {
            response.ErrorCode = (int)TeamErrorCode.TeamNotFound;
            return Task.CompletedTask;
        }

        if (team.Members.Count >= team.MaxMemberCount)
        {
            response.ErrorCode = (int)TeamErrorCode.TeamFull;
            return Task.CompletedTask;
        }

        team.Members.Add(playerId);
        response.MemberCount = team.Members.Count;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 离开队伍：移除成员；空队解散，否则队长离队时转移给最早加入的成员。
    /// </summary>
    /// <param name="playerId">离队玩家ID。</param>
    /// <param name="request">离队请求。</param>
    /// <param name="response">离队响应。</param>
    public Task OnLeaveAsync(long playerId, ReqTeamLeave request, RespTeamLeave response)
    {
        if (!TryGetTeamOf(playerId, out var team))
        {
            response.ErrorCode = (int)TeamErrorCode.NotMember;
            return Task.CompletedTask;
        }

        response.TeamId = team.TeamId;
        team.Members.RemoveAll(member => member == playerId);
        if (TeamRules.LeaveOutcome(team.Members))
        {
            State.Teams.Remove(team.TeamId);
            response.Disbanded = true;
            return Task.CompletedTask;
        }

        if (team.LeaderId == playerId)
        {
            team.LeaderId = TeamRules.ResolveNewLeader(team.Members);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 队长移除成员：仅队长可踢，不能踢自己，目标须在队。
    /// </summary>
    /// <param name="playerId">操作玩家ID。</param>
    /// <param name="request">移除请求。</param>
    /// <param name="response">移除响应。</param>
    public Task OnKickAsync(long playerId, ReqTeamKick request, RespTeamKick response)
    {
        if (!TryGetTeamOf(playerId, out var team))
        {
            response.ErrorCode = (int)TeamErrorCode.NotMember;
            return Task.CompletedTask;
        }

        if (team.LeaderId != playerId)
        {
            response.ErrorCode = (int)TeamErrorCode.NotLeader;
            return Task.CompletedTask;
        }

        if (request.MemberPlayerId == playerId)
        {
            response.ErrorCode = (int)TeamErrorCode.CannotSelf;
            return Task.CompletedTask;
        }

        if (team.Members.RemoveAll(member => member == request.MemberPlayerId) == 0)
        {
            response.ErrorCode = (int)TeamErrorCode.NotMember;
            return Task.CompletedTask;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 解散队伍：仅队长可解散。
    /// </summary>
    /// <param name="playerId">操作玩家ID。</param>
    /// <param name="request">解散请求。</param>
    /// <param name="response">解散响应。</param>
    public Task OnDisbandAsync(long playerId, ReqTeamDisband request, RespTeamDisband response)
    {
        if (!TryGetTeamOf(playerId, out var team))
        {
            response.ErrorCode = (int)TeamErrorCode.NotMember;
            return Task.CompletedTask;
        }

        if (team.LeaderId != playerId)
        {
            response.ErrorCode = (int)TeamErrorCode.NotLeader;
            return Task.CompletedTask;
        }

        State.Teams.Remove(team.TeamId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 拉取全部队伍列表。
    /// </summary>
    /// <param name="playerId">玩家ID（本业务不使用）。</param>
    /// <param name="request">列表请求。</param>
    /// <param name="response">列表响应。</param>
    public Task OnListAsync(long playerId, ReqTeamList request, RespTeamList response)
    {
        foreach (var team in State.Teams.Values)
        {
            response.Teams.Add(ToTeamInfo(team));
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 查询玩家所在队伍。
    /// </summary>
    /// <param name="playerId">玩家ID。</param>
    /// <param name="team">队伍状态。</param>
    /// <returns>在队返回 true。</returns>
    private bool TryGetTeamOf(long playerId, out TeamStateItem team)
    {
        foreach (var candidate in State.Teams.Values)
        {
            if (candidate.Members.Contains(playerId))
            {
                team = candidate;
                return true;
            }
        }

        team = null;
        return false;
    }

    /// <summary>
    /// 状态队伍 → 协议摘要载荷。
    /// </summary>
    /// <param name="team">队伍状态。</param>
    /// <returns>协议摘要载荷。</returns>
    private static TeamInfo ToTeamInfo(TeamStateItem team)
    {
        return new TeamInfo
        {
            TeamId = team.TeamId,
            Name = team.Name,
            LeaderId = team.LeaderId,
            MemberCount = team.Members.Count,
            MaxMemberCount = team.MaxMemberCount,
        };
    }
}
