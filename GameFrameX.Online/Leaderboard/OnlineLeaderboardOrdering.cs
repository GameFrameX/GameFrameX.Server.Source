// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
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

using System;
using System.Collections.Generic;

namespace GameFrameX.Online.Leaderboard;

/// <summary>
/// 排行榜全序比较器（唯一名次口径：分数按榜向 → 更新时间升序 → 玩家标识升序）。
/// <para>
/// 维护约束（红线）：这是 Top N / 附近排名 / 分页 / keyset 游标共用的**唯一**排序事实来源，
/// 禁止在服务或存储里另写一份比较逻辑；次级键保证同分玩家也有确定全序（先写入者靠前，再按玩家标识消解），
/// 名次无并列空洞、结果可复算（VC-7.3）。修改比较语义 = 修改全部查询口径，须回 vault 评审。
/// </para>
/// </summary>
public static class OnlineLeaderboardOrdering
{
    /// <summary>
    /// 按榜单方向比较两个条目的名次先后。
    /// </summary>
    /// <param name="sortOrder">榜单排序方向。</param>
    /// <param name="left">左条目。</param>
    /// <param name="right">右条目。</param>
    /// <returns>左条目名次靠前返回负数；靠后返回正数；同一玩家返回 0。</returns>
    public static int Compare(OnlineLeaderboardSortOrder sortOrder, OnlineLeaderboardEntry left, OnlineLeaderboardEntry right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        if (left == null)
        {
            return 1;
        }

        if (right == null)
        {
            return -1;
        }

        var scoreComparison = sortOrder == OnlineLeaderboardSortOrder.Ascending
            ? left.Score.CompareTo(right.Score)
            : right.Score.CompareTo(left.Score);
        if (scoreComparison != 0)
        {
            return scoreComparison;
        }

        var timeComparison = left.LastUpdateTime.CompareTo(right.LastUpdateTime);
        if (timeComparison != 0)
        {
            return timeComparison;
        }

        return left.PlayerId.CompareTo(right.PlayerId);
    }

    /// <summary>
    /// 判定候选分数是否优于既有分数（Best 策略裁决口径，与名次方向同源）。
    /// </summary>
    /// <param name="sortOrder">榜单排序方向。</param>
    /// <param name="candidateScore">候选分数。</param>
    /// <param name="currentScore">当前分数。</param>
    /// <returns>候选更优返回 <c>true</c>；持平或更差返回 <c>false</c>。</returns>
    public static bool IsBetter(OnlineLeaderboardSortOrder sortOrder, long candidateScore, long currentScore)
    {
        return sortOrder == OnlineLeaderboardSortOrder.Ascending
            ? candidateScore < currentScore
            : candidateScore > currentScore;
    }

    /// <summary>
    /// 以榜单方向对条目列表做全序排序（原位排序；调用方保证传入副本）。
    /// </summary>
    /// <param name="sortOrder">榜单排序方向。</param>
    /// <param name="entries">待排序列表。</param>
    public static void Sort(OnlineLeaderboardSortOrder sortOrder, List<OnlineLeaderboardEntry> entries)
    {
        if (entries == null)
        {
            throw new ArgumentNullException(nameof(entries));
        }

        entries.Sort((left, right) => Compare(sortOrder, left, right));
    }
}
