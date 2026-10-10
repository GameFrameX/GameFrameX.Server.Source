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

using GameFrameX.Apps.ServerRole.Match.Entity;
using GameFrameX.Hotfix.Logic.ServerRole.Match;
using GameFrameX.Proto.Proto;
using Xunit;

namespace GameFrameX.Hotfix.Tests;

/// <summary>
/// Match 业务纯规则测试（C197 Phase 2）。
/// </summary>
/// <remarks>
/// 覆盖模式/评分校验、配对窗口公式、FIFO 配对与无配对路径。
/// </remarks>
public class MatchRulesTests
{
    /// <summary>
    /// 模式白名单：仅 1v1 / 2v2 合法。
    /// </summary>
    [Theory]
    [InlineData(MatchMode.OneVsOne, true)]
    [InlineData(MatchMode.TwoVsTwo, true)]
    [InlineData((MatchMode)0, false)]
    [InlineData((MatchMode)99, false)]
    public void IsModeDefined_Whitelist(MatchMode mode, bool expected)
    {
        Assert.Equal(expected, MatchRules.IsModeDefined(mode));
    }

    /// <summary>
    /// 评分校验：仅大于 0 合法。
    /// </summary>
    [Theory]
    [InlineData(1, true)]
    [InlineData(1500, true)]
    [InlineData(0, false)]
    [InlineData(-10, false)]
    public void IsRatingValid_PositiveOnly(int rating, bool expected)
    {
        Assert.Equal(expected, MatchRules.IsRatingValid(rating));
    }

    /// <summary>
    /// 配对窗口公式：100 + 等待秒 × 20。
    /// </summary>
    [Theory]
    [InlineData(0, 100)]
    [InlineData(1, 120)]
    [InlineData(5, 200)]
    [InlineData(10, 300)]
    public void MatchWindow_Formula(long waitSeconds, int expected)
    {
        Assert.Equal(expected, MatchRules.MatchWindow(waitSeconds));
    }

    /// <summary>
    /// FIFO 配对：队首与后续首个满足窗口者成对，剩余保持相对顺序；入参不被修改。
    /// </summary>
    [Fact]
    public void TryPair_FifoPairsHeadWithFirstCandidateInWindow()
    {
        var entries = new List<MatchPoolEntryState>
        {
            new MatchPoolEntryState { PlayerId = 1, Rating = 1000, EnqueueUnixTime = 100 },
            new MatchPoolEntryState { PlayerId = 2, Rating = 2000, EnqueueUnixTime = 100 },
            new MatchPoolEntryState { PlayerId = 3, Rating = 1080, EnqueueUnixTime = 102 },
            new MatchPoolEntryState { PlayerId = 4, Rating = 1005, EnqueueUnixTime = 103 },
        };

        // 队首已等待 2 秒 → 窗口 140：跳过 2000（差 1000），命中 1080（差 80）。
        var pair = MatchRules.TryPair(entries, 102);

        Assert.NotNull(pair);
        Assert.Equal(1, pair.First.PlayerId);
        Assert.Equal(3, pair.Second.PlayerId);
        Assert.Equal(new long[] { 2, 4 }, pair.Remaining.Select(entry => entry.PlayerId));
        Assert.Equal(4, entries.Count);
    }

    /// <summary>
    /// 无配对：不足两人、或窗口内无候选时返回 null。
    /// </summary>
    [Fact]
    public void TryPair_ReturnsNullWhenNoCandidate()
    {
        Assert.Null(MatchRules.TryPair(new List<MatchPoolEntryState>(), 100));

        var single = new List<MatchPoolEntryState> { new MatchPoolEntryState { PlayerId = 1, Rating = 1000, EnqueueUnixTime = 100 } };
        Assert.Null(MatchRules.TryPair(single, 100));

        // 刚入队（等待 0 秒 → 窗口 100），评分差 101 不满足。
        var far = new List<MatchPoolEntryState>
        {
            new MatchPoolEntryState { PlayerId = 1, Rating = 1000, EnqueueUnixTime = 100 },
            new MatchPoolEntryState { PlayerId = 2, Rating = 1101, EnqueueUnixTime = 100 },
        };
        Assert.Null(MatchRules.TryPair(far, 100));

        // 等待 1 秒后窗口扩大到 120，命中。
        Assert.NotNull(MatchRules.TryPair(far, 101));
    }

    /// <summary>
    /// 最近配对记录削顶：超过 50 条淘汰最旧，并按时间倒序可查到本人最近配对。
    /// </summary>
    [Fact]
    public void AppendRecentMatch_EvictsOldestAndFindLatest()
    {
        var recent = new List<MatchResultState>();
        for (var index = 1; index <= MatchRules.MaxRecentMatches + 5; index++)
        {
            MatchRules.AppendRecentMatch(recent, new MatchResultState { MatchId = index, PlayerA = index, PlayerB = index + 1000 });
        }

        Assert.Equal(MatchRules.MaxRecentMatches, recent.Count);
        Assert.Equal(6, recent[0].MatchId);

        var latest = MatchRules.FindLatestMatch(recent, MatchRules.MaxRecentMatches + 5 + 1000);
        Assert.NotNull(latest);
        Assert.Equal(MatchRules.MaxRecentMatches + 5, latest.MatchId);
        Assert.Null(MatchRules.FindLatestMatch(recent, 999999));
    }
}
