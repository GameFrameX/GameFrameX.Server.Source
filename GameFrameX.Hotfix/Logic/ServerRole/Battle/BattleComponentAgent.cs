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

using GameFrameX.Apps.ServerRole.Battle.Component;
using GameFrameX.Apps.ServerRole.Battle.Entity;

namespace GameFrameX.Hotfix.Logic.ServerRole.Battle;

/// <summary>
/// 战斗服务器（Battle Role）业务组件代理：回合战斗模拟创建与记录查询。
/// </summary>
/// <remarks>
/// Battle Role 独立业务逻辑系统（C197 垂直切片）的热更侧落点。
/// 业务规则判定一律委托 <see cref="BattleRules"/>（纯函数，可单测）；
/// 本类只做状态编排：随机数注入模拟、战斗记录落库与双方积分结算。
/// </remarks>
public class BattleComponentAgent : StateComponentAgent<BattleComponent, BattleState>
{
    /// <summary>
    /// 创建战斗：请求者为攻方；校验评分与赛制后以随机数注入模拟整场，落库记录并结算双方积分。
    /// </summary>
    /// <param name="playerId">攻方玩家ID。</param>
    /// <param name="request">创建请求。</param>
    /// <param name="response">创建响应。</param>
    public Task OnCreateAsync(long playerId, ReqBattleCreate request, RespBattleCreate response)
    {
        if (!BattleRules.IsRatingValid(request.AtkRating) || !BattleRules.IsRatingValid(request.DefRating))
        {
            response.ErrorCode = (int)BattleErrorCode.RatingInvalid;
            return Task.CompletedTask;
        }

        if (!BattleRules.IsBestOfValid(request.BestOf))
        {
            response.ErrorCode = (int)BattleErrorCode.BestOfInvalid;
            return Task.CompletedTask;
        }

        // 随机配额取 bestOf×4×2（正常每回合消耗 2 个、平局重掷额外消耗）；
        // 即便全部平局，BattleRules.Simulate 的尝试次数护栏保证必然终止。
        var rolls = new double[request.BestOf * 4 * 2];
        for (var index = 0; index < rolls.Length; index++)
        {
            rolls[index] = RandomHelper.NextDouble();
        }

        var (rounds, winnerAtk) = BattleRules.Simulate(request.AtkRating, request.DefRating, request.BestOf, rolls);
        var record = new BattleRecordState
        {
            BattleId = State.NextBattleId++,
            AtkPlayerId = playerId,
            DefPlayerId = request.DefPlayerId,
            AtkRating = request.AtkRating,
            DefRating = request.DefRating,
            BestOf = request.BestOf,
            Rounds = rounds,
            WinnerPlayerId = winnerAtk ? playerId : request.DefPlayerId,
        };

        State.PlayerScores.TryGetValue(playerId, out var atkScore);
        State.PlayerScores.TryGetValue(request.DefPlayerId, out var defScore);
        var newAtkScore = BattleRules.ApplyScore(atkScore, winnerAtk);
        var newDefScore = BattleRules.ApplyScore(defScore, !winnerAtk);
        State.PlayerScores[playerId] = newAtkScore;
        State.PlayerScores[request.DefPlayerId] = newDefScore;

        record.ScoreDelta = (int)(newAtkScore - atkScore);
        State.Battles[record.BattleId] = record;

        response.BattleId = record.BattleId;
        response.WinnerPlayerId = record.WinnerPlayerId;
        response.ScoreDelta = record.ScoreDelta;
        FillRounds(record, response.Rounds);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 查询战斗记录：不存在返回 <see cref="BattleErrorCode.BattleNotFound"/>。
    /// </summary>
    /// <param name="playerId">玩家ID。</param>
    /// <param name="request">查询请求。</param>
    /// <param name="response">查询响应。</param>
    public Task OnQueryAsync(long playerId, ReqBattleQuery request, RespBattleQuery response)
    {
        if (!State.Battles.TryGetValue(request.BattleId, out var record))
        {
            response.ErrorCode = (int)BattleErrorCode.BattleNotFound;
            return Task.CompletedTask;
        }

        response.BattleId = record.BattleId;
        response.WinnerPlayerId = record.WinnerPlayerId;
        response.ScoreDelta = record.ScoreDelta;
        FillRounds(record, response.Rounds);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 战斗记录回合 → 协议载荷。
    /// </summary>
    /// <param name="record">战斗记录。</param>
    /// <param name="target">目标回合载荷列表。</param>
    private static void FillRounds(BattleRecordState record, List<BattleRoundInfo> target)
    {
        foreach (var round in record.Rounds)
        {
            target.Add(new BattleRoundInfo
            {
                Round = round.Round,
                AtkPower = round.AtkPower,
                DefPower = round.DefPower,
                AtkWin = round.AtkWin,
            });
        }
    }
}
