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

namespace GameFrameX.Online.Leaderboard;

/// <summary>
/// 排行榜榜单定义（vault:C8 S7.1：第一版个人榜；作用域 = (TenantId, AppId)，App 内跨区服聚合）。
/// <para>
/// 维护约束（红线）：榜单归属租户与 App 双键隔离——跨 App / 跨租户读写一律查不到（ResourceNotFound 反预言，
/// 对齐 C94 存储 / C99 频道先例，不泄露榜单存在性，VC-7.14）；<see cref="ServerId"/> 不参与榜单隔离，
/// 队伍榜 / 公会榜 / 复杂跨服排名延后（vault Out of Scope）。排序方向与累计策略创建后固化，运行期不可变更；
/// 赛季维度的生命周期（重置 / 快照）归 C103，本类型不承载赛季字段。
/// </para>
/// </summary>
public sealed class OnlineLeaderboard
{
    /// <summary>
    /// 获取或设置榜单标识（App 内唯一，创建方提供）。
    /// </summary>
    public string LeaderboardId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置租户标识（作用域隔离键）。
    /// </summary>
    public long TenantId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置 App 标识（作用域隔离键）。
    /// </summary>
    public long AppId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置排序方向（创建后固化）。
    /// </summary>
    public OnlineLeaderboardSortOrder SortOrder
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置分数累计策略（创建后固化）。
    /// </summary>
    public OnlineLeaderboardScoreUpdatePolicy ScoreUpdatePolicy
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置单次提交分数合理上限（防刷校验上界；0 = 使用 <see cref="OnlineLeaderboardOptions.DefaultMaxScorePerSubmission"/> 全局默认值）。
    /// </summary>
    public long MaxScorePerSubmission
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置创建时刻（UTC 毫秒）。
    /// </summary>
    public long CreatedTime
    {
        get;
        set;
    }

    /// <summary>
    /// 复制榜单定义（存储出入参防御性拷贝）。
    /// </summary>
    /// <returns>榜单定义副本。</returns>
    public OnlineLeaderboard Copy()
    {
        return new OnlineLeaderboard
        {
            LeaderboardId = LeaderboardId,
            TenantId = TenantId,
            AppId = AppId,
            SortOrder = SortOrder,
            ScoreUpdatePolicy = ScoreUpdatePolicy,
            MaxScorePerSubmission = MaxScorePerSubmission,
            CreatedTime = CreatedTime,
        };
    }
}
