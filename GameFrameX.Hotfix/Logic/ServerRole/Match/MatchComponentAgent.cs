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

using GameFrameX.Apps.ServerRole.Match.Component;
using GameFrameX.Apps.ServerRole.Match.Entity;
using GameFrameX.Foundation.Utility;

namespace GameFrameX.Hotfix.Logic.ServerRole.Match;

/// <summary>
/// 匹配服务器（Match Role）业务组件代理：匹配池排队、FIFO 配对与状态查询。
/// </summary>
/// <remarks>
/// Match Role 独立业务逻辑系统（C197 垂直切片）的热更侧落点。
/// 业务规则判定一律委托 <see cref="MatchRules"/>（纯函数，可单测）；
/// 本类只做状态编排：入队查重、池懒创建、配对出池与最近配对记录维护。
/// </remarks>
public class MatchComponentAgent : StateComponentAgent<MatchComponent, MatchState>
{
    /// <summary>
    /// 加入匹配池：模式/评分校验后入队，随后立即尝试与队首配对；配对成功双方出池并记录最近配对。
    /// </summary>
    /// <param name="playerId">玩家ID。</param>
    /// <param name="request">入队请求。</param>
    /// <param name="response">入队响应。</param>
    public Task OnEnqueueAsync(long playerId, ReqMatchEnqueue request, RespMatchEnqueue response)
    {
        if (!MatchRules.IsModeDefined(request.Mode))
        {
            response.ErrorCode = (int)MatchErrorCode.ModeInvalid;
            return Task.CompletedTask;
        }

        if (!MatchRules.IsRatingValid(request.Rating))
        {
            response.ErrorCode = (int)MatchErrorCode.RatingInvalid;
            return Task.CompletedTask;
        }

        if (FindEntry(playerId, out _, out _))
        {
            response.ErrorCode = (int)MatchErrorCode.AlreadyQueuing;
            return Task.CompletedTask;
        }

        var now = TimerHelper.UnixTimeSeconds();
        if (!State.Pools.TryGetValue(request.Mode, out var pool))
        {
            pool = new List<MatchPoolEntryState>();
            State.Pools[request.Mode] = pool;
        }

        pool.Add(new MatchPoolEntryState { PlayerId = playerId, Rating = request.Rating, EnqueueUnixTime = now });

        var pair = MatchRules.TryPair(pool, now);
        if (pair == null)
        {
            response.MatchId = 0;
            return Task.CompletedTask;
        }

        State.Pools[request.Mode] = pair.Remaining;
        var matchId = State.NextMatchId++;
        MatchRules.AppendRecentMatch(State.RecentMatches, new MatchResultState
        {
            MatchId = matchId,
            Mode = request.Mode,
            PlayerA = pair.First.PlayerId,
            PlayerB = pair.Second.PlayerId,
            CreatedUnixTime = now,
        });
        response.MatchId = matchId;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 取消排队：仅排队中可取消（否则 <see cref="MatchErrorCode.NotQueuing"/>）。
    /// </summary>
    /// <param name="playerId">玩家ID。</param>
    /// <param name="request">取消请求。</param>
    /// <param name="response">取消响应。</param>
    public Task OnCancelAsync(long playerId, ReqMatchCancel request, RespMatchCancel response)
    {
        if (!FindEntry(playerId, out var pool, out var entry))
        {
            response.ErrorCode = (int)MatchErrorCode.NotQueuing;
            return Task.CompletedTask;
        }

        pool.Remove(entry);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 查询本人匹配状态：排队中返回 Queuing=true；否则从最近配对记录回填最近一次配对 MatchId。
    /// </summary>
    /// <param name="playerId">玩家ID。</param>
    /// <param name="request">状态查询请求。</param>
    /// <param name="response">状态查询响应。</param>
    public Task OnStatusAsync(long playerId, ReqMatchStatus request, RespMatchStatus response)
    {
        if (FindEntry(playerId, out _, out var entry))
        {
            response.Queuing = true;
            response.MatchId = 0;
            return Task.CompletedTask;
        }

        var latest = MatchRules.FindLatestMatch(State.RecentMatches, playerId);
        response.Queuing = false;
        response.MatchId = latest == null ? 0 : latest.MatchId;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 查找玩家当前排队条目。
    /// </summary>
    /// <param name="playerId">玩家ID。</param>
    /// <param name="pool">所在匹配池列表。</param>
    /// <param name="entry">排队条目。</param>
    /// <returns>排队中返回 true。</returns>
    private bool FindEntry(long playerId, out List<MatchPoolEntryState> pool, out MatchPoolEntryState entry)
    {
        foreach (var pair in State.Pools)
        {
            foreach (var item in pair.Value)
            {
                if (item.PlayerId == playerId)
                {
                    pool = pair.Value;
                    entry = item;
                    return true;
                }
            }
        }

        pool = null;
        entry = null;
        return false;
    }
}
