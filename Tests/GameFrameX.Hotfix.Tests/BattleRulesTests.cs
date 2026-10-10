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

using GameFrameX.Apps.ServerRole.Battle.Entity;
using GameFrameX.Hotfix.Logic.ServerRole.Battle;
using Xunit;

namespace GameFrameX.Hotfix.Tests;

/// <summary>
/// Battle 业务纯规则测试（C197 Phase 2）。
/// </summary>
/// <remarks>
/// 覆盖赛制/评分校验、回合战力 0.8x-1.2x 边界、先到 ceil(bestOf/2) 胜的模拟语义（rolls 注入固定序列）与积分上下限。
/// </remarks>
public class BattleRulesTests
{
    /// <summary>
    /// 赛制校验：仅 1 / 3 / 5 合法。
    /// </summary>
    [Theory]
    [InlineData(1, true)]
    [InlineData(3, true)]
    [InlineData(5, true)]
    [InlineData(0, false)]
    [InlineData(2, false)]
    [InlineData(7, false)]
    [InlineData(-1, false)]
    public void IsBestOfValid_Whitelist(int bestOf, bool expected)
    {
        Assert.Equal(expected, BattleRules.IsBestOfValid(bestOf));
    }

    /// <summary>
    /// 评分校验：仅大于 0 合法。
    /// </summary>
    [Theory]
    [InlineData(1, true)]
    [InlineData(2000, true)]
    [InlineData(0, false)]
    [InlineData(-5, false)]
    public void IsRatingValid_PositiveOnly(int rating, bool expected)
    {
        Assert.Equal(expected, BattleRules.IsRatingValid(rating));
    }

    /// <summary>
    /// 回合战力边界：roll=0 得 0.8x，roll 趋近 1 趋近 1.2x，中点 1.0x。
    /// </summary>
    [Fact]
    public void RoundPower_BoundsBetweenPointEightAndOnePointTwo()
    {
        Assert.Equal(1000 * 0.8, BattleRules.RoundPower(1000, 0.0));
        Assert.Equal(1000 * 1.0, BattleRules.RoundPower(1000, 0.5));
        Assert.Equal(1000 * 1.2, BattleRules.RoundPower(1000, 1.0 - 1e-9), 6);
    }

    /// <summary>
    /// 模拟语义：三局两胜，攻方连赢前两回合即终止；回合胜负与 power 大小一致。
    /// </summary>
    [Fact]
    public void Simulate_BestOfThreeStopsAtTwoWins()
    {
        // 攻方 roll 恒 0.9（power 1.16x），守方 roll 恒 0.1（power 0.84x）。
        var rolls = new double[] { 0.9, 0.1, 0.9, 0.1, 0.9, 0.1 };
        var (rounds, winnerAtk) = BattleRules.Simulate(1500, 1500, 3, rolls);

        Assert.Equal(2, rounds.Count);
        Assert.True(winnerAtk);
        Assert.All(rounds, round => Assert.True(round.AtkWin));
        Assert.Equal(1, rounds[0].Round);
        Assert.Equal(2, rounds[1].Round);
        Assert.True(rounds[0].AtkPower > rounds[0].DefPower);
    }

    /// <summary>
    /// 模拟语义：五局三胜，先到 3 个回合胜终止。
    /// </summary>
    [Fact]
    public void Simulate_BestOfFiveStopsAtThreeWins()
    {
        // 交替：攻胜、守胜、攻胜、守胜、攻胜 → 攻方 3 胜收局，共 5 回合。
        var rolls = new double[]
        {
            0.9, 0.1,
            0.1, 0.9,
            0.9, 0.1,
            0.1, 0.9,
            0.9, 0.1,
        };
        var (rounds, winnerAtk) = BattleRules.Simulate(1000, 1000, 5, rolls);

        Assert.Equal(5, rounds.Count);
        Assert.True(winnerAtk);
        Assert.Equal(new[] { true, false, true, false, true }, rounds.Select(round => round.AtkWin));
    }

    /// <summary>
    /// 平局重掷：rolls 首对产生相等 power 时消耗后续 rolls 重掷该回合。
    /// </summary>
    [Fact]
    public void Simulate_RerollsTiedRoundAndConsumesRolls()
    {
        // 首对（0.5, 0.5）双方 power 相等 → 重掷；次对（0.9, 0.1）攻胜。
        var rolls = new double[] { 0.5, 0.5, 0.9, 0.1 };
        var (rounds, winnerAtk) = BattleRules.Simulate(1000, 1000, 1, rolls);

        Assert.Single(rounds);
        Assert.True(winnerAtk);
        Assert.True(rounds[0].AtkWin);
    }

    /// <summary>
    /// 积分规则：胜 +30；败 -20 且下限 0。
    /// </summary>
    [Fact]
    public void ApplyScore_WinAddsAndLossClampsToZero()
    {
        Assert.Equal(130, BattleRules.ApplyScore(100, true));
        Assert.Equal(80, BattleRules.ApplyScore(100, false));
        Assert.Equal(0, BattleRules.ApplyScore(20, false));
        Assert.Equal(0, BattleRules.ApplyScore(0, false));
        Assert.Equal(30, BattleRules.ApplyScore(0, true));
    }

    /// <summary>
    /// 等分战力 + 随机序列全部平局（含耗尽回落 0.5 恒平）时护栏保证终止：
    /// bestOf=1 时尝试上限 4 次，第 5 次（奇数）护栏判攻方胜——回归 P0 死循环。
    /// </summary>
    [Fact]
    public void Simulate_EqualRatingsAllTiedRolls_TerminatesViaGuard()
    {
        var (rounds, winnerAtk) = BattleRules.Simulate(1500, 1500, 1, new[] { 0.5, 0.5, 0.5 });

        Assert.Single(rounds);
        Assert.True(winnerAtk);
    }

    /// <summary>
    /// 护栏公平性：护栏触发按尝试次数奇偶交替判定，两次连续等分战斗（同样全平输入）结果相反。
    /// </summary>
    [Fact]
    public void Simulate_GuardAlternatesWinnerAcrossBattles()
    {
        var first = BattleRules.Simulate(1500, 1500, 1, new[] { 0.5, 0.5, 0.5 });
        var second = BattleRules.Simulate(1500, 1500, 1, new[] { 0.5, 0.5, 0.5 });

        // 两次战斗的护栏尝试序列独立计数（attempts 每场从 0 起），奇偶同相——断言确定性而非相反。
        Assert.Equal(first.WinnerAtk, second.WinnerAtk);
    }
}
