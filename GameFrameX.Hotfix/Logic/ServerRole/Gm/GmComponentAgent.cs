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

using GameFrameX.Apps.ServerRole.Gm.Component;
using GameFrameX.Apps.ServerRole.Gm.Entity;
using GameFrameX.Foundation.Utility;
using GameFrameX.Proto.Proto;

namespace GameFrameX.Hotfix.Logic.ServerRole.Gm;

/// <summary>
/// 违规处罚服务器（Gm Role）业务组件代理：处罚新增 / 撤销 / 查询。
/// </summary>
/// <remarks>
/// Gm Role 独立业务逻辑系统（C197 垂直切片）的热更侧落点。
/// 业务规则判定一律委托 <see cref="GmRules"/>（纯函数，可单测）；
/// 本类只做状态编排：处罚建档、Active→Revoked 状态迁移与过期惰性置 Expired 后过滤。
/// </remarks>
public class GmComponentAgent : StateComponentAgent<GmComponent, GmState>
{
    /// <summary>
    /// 新增处罚：类型与时长校验通过后建档（Active，含操作者审计）并返回到期时间。
    /// </summary>
    /// <param name="operatorPlayerId">操作者（GM）玩家ID。</param>
    /// <param name="request">新增处罚请求。</param>
    /// <param name="response">新增处罚响应。</param>
    public Task OnAddPenaltyAsync(long operatorPlayerId, ReqGmAddPenalty request, RespGmAddPenalty response)
    {
        if (!GmRules.IsTypeDefined(request.Type))
        {
            response.ErrorCode = (int)GmErrorCode.PenaltyTypeInvalid;
            return Task.CompletedTask;
        }

        if (!GmRules.IsDurationValid(request.DurationSeconds))
        {
            response.ErrorCode = (int)GmErrorCode.DurationInvalid;
            return Task.CompletedTask;
        }

        var now = TimerHelper.UnixTimeSeconds();
        var penalty = new GmPenaltyState
        {
            PenaltyId = State.NextPenaltyId++,
            PlayerId = request.PlayerId,
            Type = request.Type,
            Reason = request.Reason,
            OperatorPlayerId = operatorPlayerId,
            CreatedUnixTime = now,
            DurationSeconds = request.DurationSeconds,
        };
        State.Penalties[penalty.PenaltyId] = penalty;
        response.PenaltyId = penalty.PenaltyId;
        response.ExpireUnixTime = GmRules.ExpireAt(penalty.CreatedUnixTime, penalty.DurationSeconds);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 撤销处罚：仅可撤销生效中的处罚（已过期惰性置 Expired，已撤销/已过期均拒绝）。
    /// </summary>
    /// <param name="request">撤销处罚请求。</param>
    /// <param name="response">撤销处罚响应。</param>
    public Task OnRevokePenaltyAsync(ReqGmRevokePenalty request, RespGmRevokePenalty response)
    {
        if (!State.Penalties.TryGetValue(request.PenaltyId, out var penalty))
        {
            response.ErrorCode = (int)GmErrorCode.PenaltyNotFound;
            return Task.CompletedTask;
        }

        var now = TimerHelper.UnixTimeSeconds();
        if (penalty.Status == GmPenaltyStatus.Active && GmRules.IsExpired(penalty, now))
        {
            penalty.Status = GmPenaltyStatus.Expired;
        }

        if (penalty.Status != GmPenaltyStatus.Active)
        {
            response.ErrorCode = (int)GmErrorCode.NotActive;
            return Task.CompletedTask;
        }

        penalty.Status = GmPenaltyStatus.Revoked;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 查询玩家处罚：生效中（Active）的过期记录惰性置 Expired，仅返回未过期且未撤销的记录。
    /// </summary>
    /// <param name="request">查询处罚请求。</param>
    /// <param name="response">查询处罚响应。</param>
    public Task OnQueryPenaltiesAsync(ReqGmQueryPenalties request, RespGmQueryPenalties response)
    {
        var now = TimerHelper.UnixTimeSeconds();
        foreach (var penalty in State.Penalties.Values)
        {
            if (penalty.PlayerId != request.PlayerId)
            {
                continue;
            }

            if (penalty.Status == GmPenaltyStatus.Active && GmRules.IsExpired(penalty, now))
            {
                penalty.Status = GmPenaltyStatus.Expired;
            }

            if (penalty.Status != GmPenaltyStatus.Active)
            {
                continue;
            }

            response.Penalties.Add(new GmPenaltyInfo
            {
                PenaltyId = penalty.PenaltyId,
                Type = penalty.Type,
                Reason = penalty.Reason,
                CreatedUnixTime = penalty.CreatedUnixTime,
                ExpireUnixTime = GmRules.ExpireAt(penalty.CreatedUnixTime, penalty.DurationSeconds),
                Status = penalty.Status,
            });
        }

        return Task.CompletedTask;
    }
}
