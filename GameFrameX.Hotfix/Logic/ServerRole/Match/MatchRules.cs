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
using System.Linq;
using GameFrameX.Apps.ServerRole.Match.Entity;

namespace GameFrameX.Hotfix.Logic.ServerRole.Match;

/// <summary>
/// 匹配业务纯规则（可脱离 Actor 基础设施单测）。
/// </summary>
/// <remarks>
/// 承载与存储无关的匹配规则：模式/评分校验、配对窗口公式、池内 FIFO 配对、最近配对记录削顶。
/// Agent 只做状态编排，规则判定一律走本类（<c>MailCampaignFilter</c> 先例）。
/// </remarks>
internal static class MatchRules
{
    /// <summary>
    /// 配对窗口基础宽度（评分差）。
    /// </summary>
    public const int BaseWindowRating = 100;

    /// <summary>
    /// 每等待一秒扩大的配对窗口（评分差）。
    /// </summary>
    public const int WindowPerSecond = 20;

    /// <summary>
    /// 最近配对结果保留条数上限。
    /// </summary>
    public const int MaxRecentMatches = 50;

    /// <summary>
    /// 判定匹配模式是否已定义（白名单 1v1 / 2v2）。
    /// </summary>
    /// <param name="mode">匹配模式。</param>
    /// <returns>已定义返回 true。</returns>
    public static bool IsModeDefined(MatchMode mode)
    {
        return mode == MatchMode.OneVsOne || mode == MatchMode.TwoVsTwo;
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
    /// 计算配对窗口：100 + 等待秒数 × 20（评分差上限）。
    /// </summary>
    /// <param name="waitSeconds">先入队一方已等待秒数。</param>
    /// <returns>配对窗口（评分差）。</returns>
    public static int MatchWindow(long waitSeconds)
    {
        return (int)(BaseWindowRating + waitSeconds * WindowPerSecond);
    }

    /// <summary>
    /// 池内 FIFO 配对：取队首元素，与后续首个评分差不超过其等待时间对应窗口的元素成对。
    /// </summary>
    /// <param name="entries">池内排队条目（入队顺序）。本方法不修改入参。</param>
    /// <param name="now">当前 Unix 秒（纯函数注入）。</param>
    /// <returns>配对成功返回结果（成对双方 + 剩余条目）；无可配对返回 null。</returns>
    public static MatchPairResult TryPair(List<MatchPoolEntryState> entries, long now)
    {
        if (entries.Count < 2)
        {
            return null;
        }

        var head = entries[0];
        var waitSeconds = Math.Max(0, now - head.EnqueueUnixTime);
        var window = MatchWindow(waitSeconds);
        for (var index = 1; index < entries.Count; index++)
        {
            var candidate = entries[index];
            if (Math.Abs(candidate.Rating - head.Rating) <= window)
            {
                var remaining = new List<MatchPoolEntryState>(entries);
                remaining.RemoveAt(index);
                remaining.RemoveAt(0);
                return new MatchPairResult { First = head, Second = candidate, Remaining = remaining };
            }
        }

        return null;
    }

    /// <summary>
    /// 追加最近配对记录并维持上限：超过保留上限时淘汰最旧记录。
    /// </summary>
    /// <param name="recent">最近配对记录列表。</param>
    /// <param name="result">新配对记录。</param>
    public static void AppendRecentMatch(List<MatchResultState> recent, MatchResultState result)
    {
        recent.Add(result);
        while (recent.Count > MaxRecentMatches)
        {
            recent.RemoveAt(0);
        }
    }

    /// <summary>
    /// 从最近配对记录中查找玩家的最近一次配对（后发生的优先）。
    /// </summary>
    /// <param name="recent">最近配对记录列表。</param>
    /// <param name="playerId">玩家ID。</param>
    /// <returns>最近一次配对记录；无则返回 null。</returns>
    public static MatchResultState FindLatestMatch(List<MatchResultState> recent, long playerId)
    {
        for (var index = recent.Count - 1; index >= 0; index--)
        {
            var result = recent[index];
            if (result.PlayerA == playerId || result.PlayerB == playerId)
            {
                return result;
            }
        }

        return null;
    }
}

/// <summary>
/// FIFO 配对结果：成对双方与池内剩余条目。
/// </summary>
public sealed class MatchPairResult
{
    /// <summary>
    /// 先入队一方。
    /// </summary>
    public MatchPoolEntryState First { get; set; }

    /// <summary>
    /// 后入队一方（触发配对的请求者）。
    /// </summary>
    public MatchPoolEntryState Second { get; set; }

    /// <summary>
    /// 配对后池内剩余条目（保持原相对顺序）。
    /// </summary>
    public List<MatchPoolEntryState> Remaining { get; set; }
}
