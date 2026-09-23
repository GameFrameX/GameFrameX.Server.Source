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
/// Top N 查询载荷（<see cref="OnlineLeaderboardService.GetTopAsync"/> 除作用域外的全部查询维度；
/// keyset 游标由服务编码，客户端不透明）。
/// </summary>
public sealed class OnlineLeaderboardTopQuery
{
    /// <summary>
    /// 获取或设置榜单标识。
    /// </summary>
    /// <remarks>Gets or sets the leaderboard id.</remarks>
    public string LeaderboardId { get; init; }

    /// <summary>
    /// 获取或设置本页条目数（超出 <c>MaxPageSize</c> 按上限截断）。
    /// </summary>
    /// <remarks>Gets or sets the page size (clamped to MaxPageSize).</remarks>
    public int Count { get; init; }

    /// <summary>
    /// 获取或设置上一页返回的游标（首页传 null 或空串）。
    /// </summary>
    /// <remarks>Gets or sets the cursor returned by the previous page (null or empty for the first page).</remarks>
    public string Cursor { get; init; }

    /// <summary>
    /// 获取或设置当前时刻（UTC 毫秒；传 0 取系统时钟，缓存 TTL 判定用）。
    /// </summary>
    /// <remarks>Gets or sets the current time (UTC milliseconds; 0 = system clock; used for cache TTL checks).</remarks>
    public long NowUnixMilliseconds { get; init; }
}
