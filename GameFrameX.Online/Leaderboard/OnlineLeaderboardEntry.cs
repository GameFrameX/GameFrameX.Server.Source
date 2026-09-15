// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
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

namespace GameFrameX.Online.Leaderboard;

/// <summary>
/// 排行榜条目（单玩家在单榜单上的聚合状态；名次不在条目上，查询时按全序计算）。
/// <para>
/// 维护约束（红线）：分数只经可信写入链路产生（<see cref="OnlineLeaderboardScoreSource"/> 即来源白名单），
/// <see cref="SourceMatchResultId"/> 使每笔分数可追溯到结算结果——争议判定的审计依据；
/// 条目一经产生不从榜单移除（第一版无移除语义，赛季重置归 C103）。
/// </para>
/// </summary>
public sealed class OnlineLeaderboardEntry
{
    /// <summary>
    /// 获取或设置玩家标识。
    /// </summary>
    public long PlayerId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置榜上分数（按榜单累计策略聚合后的当前值）。
    /// </summary>
    public long Score
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置最近一次写入的分数来源。
    /// </summary>
    public OnlineLeaderboardScoreSource SourceKind
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置最近一次写入的来源结算结果标识（追溯键）。
    /// </summary>
    public string SourceMatchResultId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置最近一次写入时刻（UTC 毫秒；并列分数的次级排序键，先写入者靠前）。
    /// </summary>
    public long LastUpdateTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置累计提交次数（含未改变分数的提交，如 Best 策略下的非更优分数）。
    /// </summary>
    public int SubmissionCount
    {
        get;
        set;
    }

    /// <summary>
    /// 复制条目（存储出入参防御性拷贝）。
    /// </summary>
    /// <returns>条目副本。</returns>
    public OnlineLeaderboardEntry Copy()
    {
        return new OnlineLeaderboardEntry
        {
            PlayerId = PlayerId,
            Score = Score,
            SourceKind = SourceKind,
            SourceMatchResultId = SourceMatchResultId,
            LastUpdateTime = LastUpdateTime,
            SubmissionCount = SubmissionCount,
        };
    }
}
