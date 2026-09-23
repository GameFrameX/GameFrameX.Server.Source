//  ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
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
/// 玩家附近排名窗口查询载荷（<see cref="OnlineLeaderboardService.GetAroundPlayerAsync"/> 除作用域外的全部查询维度；
/// 窗口含玩家本人，边界处截断，VC-7.3）。
/// </summary>
public sealed class OnlineLeaderboardAroundQuery
{
    /// <summary>
    /// 获取或设置榜单标识。
    /// </summary>
    /// <remarks>Gets or sets the leaderboard id.</remarks>
    public string LeaderboardId { get; init; }

    /// <summary>
    /// 获取或设置玩家标识（未上榜返回 ResourceNotFound）。
    /// </summary>
    /// <remarks>Gets or sets the player id (an unranked player yields ResourceNotFound).</remarks>
    public long PlayerId { get; init; }

    /// <summary>
    /// 获取或设置前向窗口条数（缺省 0 = 不含前向邻居；Before / After 均缺省时窗口仅含本人）。
    /// </summary>
    /// <remarks>Gets or sets the number of entries before the player (default 0 = no preceding neighbors; when both Before and After are left at 0 the window contains only the player).</remarks>
    public int Before { get; init; }

    /// <summary>
    /// 获取或设置后向窗口条数（缺省 0 = 不含后向邻居；Before / After 均缺省时窗口仅含本人）。
    /// </summary>
    /// <remarks>Gets or sets the number of entries after the player (default 0 = no following neighbors; when both Before and After are left at 0 the window contains only the player).</remarks>
    public int After { get; init; }

    /// <summary>
    /// 获取或设置当前时刻（UTC 毫秒；传 0 取系统时钟）。
    /// </summary>
    /// <remarks>Gets or sets the current time (UTC milliseconds; 0 = system clock).</remarks>
    public long NowUnixMilliseconds { get; init; }
}
