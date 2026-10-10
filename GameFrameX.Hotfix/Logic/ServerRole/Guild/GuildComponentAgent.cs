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

using GameFrameX.Apps.ServerRole.Guild.Component;
using GameFrameX.Apps.ServerRole.Guild.Entity;

namespace GameFrameX.Hotfix.Logic.ServerRole.Guild;

/// <summary>
/// 公会服务器（Guild Role，公会管理，域号 680）业务组件代理。
/// </summary>
/// <remarks>
/// Guild Role 独立业务逻辑系统（C197 垂直切片）的热更侧落点。
/// 业务规则判定一律委托 <see cref="GuildRules"/>（纯函数，可单测）；
/// 本类只做状态编排：公会取号创建与名称唯一索引、申请入列、会长审批（入会 + 经验 + 升级重算）、
/// 会长权限约束（不可离会 / 踢人不含会长 / 解散）、档案查询。
/// </remarks>
public class GuildComponentAgent : StateComponentAgent<GuildComponent, GuildState>
{
    /// <summary>
    /// 创建公会：名称校验与全局唯一，一人一公会约束；创建者为会长。
    /// </summary>
    /// <param name="playerId">创建者玩家ID。</param>
    /// <param name="request">创建请求。</param>
    /// <param name="response">创建响应。</param>
    public Task OnCreateAsync(long playerId, ReqGuildCreate request, RespGuildCreate response)
    {
        if (!GuildRules.IsGuildNameValid(request.Name))
        {
            response.ErrorCode = (int)GuildErrorCode.NameInvalid;
            return Task.CompletedTask;
        }

        if (State.NameIndex.ContainsKey(request.Name))
        {
            response.ErrorCode = (int)GuildErrorCode.GuildNameExists;
            return Task.CompletedTask;
        }

        if (TryGetGuildOf(playerId, out _))
        {
            response.ErrorCode = (int)GuildErrorCode.AlreadyInGuild;
            return Task.CompletedTask;
        }

        var guild = new GuildStateItem
        {
            GuildId = State.NextGuildId++,
            Name = request.Name,
            LeaderId = playerId,
            Members = new List<long> { playerId },
            Level = 1,
            Exp = 0,
        };
        State.Guilds[guild.GuildId] = guild;
        State.NameIndex[guild.Name] = guild.GuildId;
        response.GuildId = guild.GuildId;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 申请加入公会：一人一公会约束、公会存在；申请人进入待审批列表（重复申请幂等）。
    /// </summary>
    /// <param name="playerId">申请玩家ID。</param>
    /// <param name="request">申请请求。</param>
    /// <param name="response">申请响应。</param>
    public Task OnApplyAsync(long playerId, ReqGuildApply request, RespGuildApply response)
    {
        if (TryGetGuildOf(playerId, out _))
        {
            response.ErrorCode = (int)GuildErrorCode.AlreadyInGuild;
            return Task.CompletedTask;
        }

        if (!State.Guilds.TryGetValue(request.GuildId, out var guild))
        {
            response.ErrorCode = (int)GuildErrorCode.GuildNotFound;
            return Task.CompletedTask;
        }

        if (!guild.Applications.Contains(playerId))
        {
            guild.Applications.Add(playerId);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 会长审批通过入会申请：申请人入会、清申请、公会经验 +10 并按阈值重算等级。
    /// </summary>
    /// <param name="playerId">会长玩家ID。</param>
    /// <param name="request">审批请求。</param>
    /// <param name="response">审批响应。</param>
    public Task OnApproveAsync(long playerId, ReqGuildApprove request, RespGuildApprove response)
    {
        if (!TryGetGuildOf(playerId, out var guild))
        {
            response.ErrorCode = (int)GuildErrorCode.NotMember;
            return Task.CompletedTask;
        }

        if (guild.LeaderId != playerId)
        {
            response.ErrorCode = (int)GuildErrorCode.NotLeader;
            return Task.CompletedTask;
        }

        if (guild.Applications.RemoveAll(applicant => applicant == request.ApplicantPlayerId) == 0)
        {
            response.ErrorCode = (int)GuildErrorCode.NotApplicant;
            return Task.CompletedTask;
        }

        // 幂等护栏：重试/并发下申请人可能已在成员表（一人一公会的申请侧已拦截），入会去重防止成员重复与经验重复发放。
        if (!guild.Members.Contains(request.ApplicantPlayerId))
        {
            guild.Members.Add(request.ApplicantPlayerId);
            guild.Exp += GuildRules.ApplyExpGain;
            guild.Level = GuildRules.ExpToLevel(guild.Exp);
        }

        response.Level = guild.Level;
        response.Exp = guild.Exp;
        response.MemberCount = guild.Members.Count;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 离开公会：会长不可离会（须解散）；普通成员移除自己。
    /// </summary>
    /// <param name="playerId">离会玩家ID。</param>
    /// <param name="request">离会请求。</param>
    /// <param name="response">离会响应。</param>
    public Task OnLeaveAsync(long playerId, ReqGuildLeave request, RespGuildLeave response)
    {
        if (!TryGetGuildOf(playerId, out var guild))
        {
            response.ErrorCode = (int)GuildErrorCode.NotMember;
            return Task.CompletedTask;
        }

        if (guild.LeaderId == playerId)
        {
            response.ErrorCode = (int)GuildErrorCode.NotLeader;
            return Task.CompletedTask;
        }

        guild.Members.RemoveAll(member => member == playerId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 会长移除成员：仅会长可踢，且不能移除会长本人。
    /// </summary>
    /// <param name="playerId">操作玩家ID。</param>
    /// <param name="request">移除请求。</param>
    /// <param name="response">移除响应。</param>
    public Task OnKickAsync(long playerId, ReqGuildKick request, RespGuildKick response)
    {
        if (!TryGetGuildOf(playerId, out var guild))
        {
            response.ErrorCode = (int)GuildErrorCode.NotMember;
            return Task.CompletedTask;
        }

        if (guild.LeaderId != playerId)
        {
            response.ErrorCode = (int)GuildErrorCode.NotLeader;
            return Task.CompletedTask;
        }

        if (request.MemberPlayerId == guild.LeaderId)
        {
            response.ErrorCode = (int)GuildErrorCode.NotLeader;
            return Task.CompletedTask;
        }

        if (guild.Members.RemoveAll(member => member == request.MemberPlayerId) == 0)
        {
            response.ErrorCode = (int)GuildErrorCode.NotMember;
            return Task.CompletedTask;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 解散公会：仅会长可解散；同步清理名称唯一索引。
    /// </summary>
    /// <param name="playerId">操作玩家ID。</param>
    /// <param name="request">解散请求。</param>
    /// <param name="response">解散响应。</param>
    public Task OnDisbandAsync(long playerId, ReqGuildDisband request, RespGuildDisband response)
    {
        if (!TryGetGuildOf(playerId, out var guild))
        {
            response.ErrorCode = (int)GuildErrorCode.NotMember;
            return Task.CompletedTask;
        }

        if (guild.LeaderId != playerId)
        {
            response.ErrorCode = (int)GuildErrorCode.NotLeader;
            return Task.CompletedTask;
        }

        State.NameIndex.Remove(guild.Name);
        State.Guilds.Remove(guild.GuildId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 查询公会档案（含待审批申请列表）。
    /// </summary>
    /// <param name="request">查询请求。</param>
    /// <param name="response">查询响应。</param>
    public Task OnQueryAsync(ReqGuildQuery request, RespGuildQuery response)
    {
        if (!State.Guilds.TryGetValue(request.GuildId, out var guild))
        {
            response.ErrorCode = (int)GuildErrorCode.GuildNotFound;
            return Task.CompletedTask;
        }

        response.Name = guild.Name;
        response.LeaderId = guild.LeaderId;
        response.Level = guild.Level;
        response.Exp = guild.Exp;
        response.MemberCount = guild.Members.Count;
        response.Applications.AddRange(guild.Applications);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 查询玩家所在公会。
    /// </summary>
    /// <param name="playerId">玩家ID。</param>
    /// <param name="guild">公会状态。</param>
    /// <returns>在会返回 true。</returns>
    private bool TryGetGuildOf(long playerId, out GuildStateItem guild)
    {
        foreach (var candidate in State.Guilds.Values)
        {
            if (candidate.Members.Contains(playerId))
            {
                guild = candidate;
                return true;
            }
        }

        guild = null;
        return false;
    }
}
