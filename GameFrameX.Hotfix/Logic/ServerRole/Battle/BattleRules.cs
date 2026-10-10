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

using System;
using System.Collections.Generic;
using GameFrameX.Apps.ServerRole.Battle.Entity;

namespace GameFrameX.Hotfix.Logic.ServerRole.Battle;

/// <summary>
/// 战斗业务纯规则（可脱离 Actor 基础设施单测）。
/// </summary>
/// <remarks>
/// 承载与存储无关的战斗规则：赛制/评分校验、回合战力公式、先到 ceil(bestOf/2) 胜的整场模拟与积分变化。
/// 随机数以 rolls 序列注入（确定性可单测）；Agent 只做状态编排，规则判定一律走本类（<c>MailCampaignFilter</c> 先例）。
/// </remarks>
internal static class BattleRules
{
    /// <summary>
    /// 胜利积分增量。
    /// </summary>
    public const int WinScoreDelta = 30;

    /// <summary>
    /// 失败积分减量。
    /// </summary>
    public const int LoseScoreDelta = 20;

    /// <summary>
    /// 模拟尝试次数上限因子（bestOf × 本因子 = 总尝试次数硬上限，防等分战力平局死循环）。
    /// </summary>
    public const int MaxAttemptFactor = 4;

    /// <summary>
    /// 判定赛制是否合法：局数属于 {1, 3, 5}。
    /// </summary>
    /// <param name="bestOf">赛制局数。</param>
    /// <returns>合法返回 true。</returns>
    public static bool IsBestOfValid(int bestOf)
    {
        return bestOf == 1 || bestOf == 3 || bestOf == 5;
    }

    /// <summary>
    /// 判定评分是否合法（大于 0）。
    /// </summary>
    /// <param name="rating">评分。</param>
    /// <returns>合法返回 true。</returns>
    public static bool IsRatingValid(int rating)
    {
        return rating > 0;
    }

    /// <summary>
    /// 计算回合战力：rating × (0.8 + 0.4 × roll)，roll ∈ [0,1) → 战力 ∈ [0.8x, 1.2x)。
    /// </summary>
    /// <param name="rating">评分。</param>
    /// <param name="roll">随机数 [0,1)。</param>
    /// <returns>回合战力。</returns>
    public static double RoundPower(int rating, double roll)
    {
        return rating * (0.8 + 0.4 * roll);
    }

    /// <summary>
    /// 整场战斗模拟：逐回合比较战力，高者胜；战力相等重掷该回合（消耗 rolls 前进），先到 ceil(bestOf/2) 个回合胜者获胜。
    /// </summary>
    /// <remarks>
    /// 有界护栏：等分战力 + 随机序列全部平局（或耗尽回落 0.5 恒平）时，内层重掷循环可能不终止——
    /// 总尝试次数超过 <paramref name="bestOf"/> × <see cref="MaxAttemptFactor"/> 后，按尝试次数奇偶交替判定该回合胜负，
    /// 保证模拟必然终止（胜负分布对攻守双方公平交替）。
    /// </remarks>
    /// <param name="atkRating">攻方评分。</param>
    /// <param name="defRating">守方评分。</param>
    /// <param name="bestOf">赛制局数（调用前先校验合法性）。</param>
    /// <param name="rolls">注入的随机数序列（[0,1)）；平局重掷会额外消耗，序列耗尽后按 0.5 兜底。</param>
    /// <returns>回合结果列表与攻方是否获胜。</returns>
    public static (List<BattleRoundState> Rounds, bool WinnerAtk) Simulate(int atkRating, int defRating, int bestOf, IReadOnlyList<double> rolls)
    {
        var rounds = new List<BattleRoundState>();
        var atkWins = 0;
        var defWins = 0;
        var winsNeeded = (bestOf + 1) / 2;
        var rollIndex = 0;
        var attempts = 0;
        var maxAttempts = bestOf * MaxAttemptFactor;

        while (atkWins < winsNeeded && defWins < winsNeeded)
        {
            double atkPower;
            double defPower;
            while (true)
            {
                attempts++;
                if (attempts > maxAttempts)
                {
                    // 护栏触发：以尝试次数奇偶交替打破平局（攻/守交替受益），保证有界终止。
                    atkPower = attempts % 2 == 1 ? 1 : -1;
                    defPower = -atkPower;
                    break;
                }

                var atkRoll = rollIndex < rolls.Count ? rolls[rollIndex] : 0.5;
                rollIndex++;
                var defRoll = rollIndex < rolls.Count ? rolls[rollIndex] : 0.5;
                rollIndex++;
                atkPower = RoundPower(atkRating, atkRoll);
                defPower = RoundPower(defRating, defRoll);
                if (atkPower != defPower)
                {
                    break;
                }
            }

            var atkWin = atkPower > defPower;
            if (atkWin)
            {
                atkWins++;
            }
            else
            {
                defWins++;
            }

            rounds.Add(new BattleRoundState
            {
                Round = rounds.Count + 1,
                AtkPower = atkPower,
                DefPower = defPower,
                AtkWin = atkWin,
            });
        }

        return (rounds, atkWins > defWins);
    }

    /// <summary>
    /// 应用积分变化：胜 +30 / 败 -20，下限 0。
    /// </summary>
    /// <param name="oldScore">原积分。</param>
    /// <param name="isWin">是否胜利。</param>
    /// <returns>新积分。</returns>
    public static long ApplyScore(long oldScore, bool isWin)
    {
        return isWin ? oldScore + WinScoreDelta : Math.Max(0, oldScore - LoseScoreDelta);
    }
}
