// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the PRC and relevant international regulations.
//   使用本项目须严格遵守相关法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with relevant laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file at the root of the source code for the full license text.
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
//   CNB  仓库：https://www.cnb.cool/GameFrameX
//   CNB Repository: https://www.cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Hosting.AdminApi;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Scope;
using GameFrameX.Capability.Online.Competition.Leaderboard;

namespace GameFrameX.Capability.Online.Competition.AdminApi;

/// <summary>
/// 排行榜域 admin 命令（query_leaderboard——只读榜单查询：单元信息 + Top 条目 + 可选玩家附近窗口）。
/// <para>
/// 维护约束：只读幂等包裹 false（无状态变更，与 query_match_settlement 同族）；跨 App / 跨租户与
/// 榜单不存在同构 ResourceNotFound（排行榜服务反预言红线）；玩家附近窗口为尽力投影（玩家未上榜时省略）。
/// </para>
/// </summary>
public static class OnlineAdminLeaderboardHandlers
{
    /// <summary>
    /// 注册排行榜查询 action（只读）。
    /// </summary>
    /// <param name="registry">动作注册表。</param>
    /// <param name="leaderboards">排行榜服务（Register 期构造）。</param>
    public static void RegisterLeaderboardQuery(AdminActionRegistry registry, OnlineLeaderboardService leaderboards)
    {
        registry.Register("query_leaderboard", OnlineAdminApiAdapters.Wrap((request, scope, cancellationToken) => QueryLeaderboardAsync(request, scope, cancellationToken, leaderboards)), false);
    }

    /// <summary>
    /// query_leaderboard：榜单单元信息 + Top N 条目（Rank/PlayerId/Score）+ 可选 AroundPlayer 窗口。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <param name="scope">作用域。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="leaderboards">排行榜服务。</param>
    /// <returns>查询响应。</returns>
    private static async Task<object> QueryLeaderboardAsync(AdminApiRequest request, OnlineScope scope, CancellationToken cancellationToken, OnlineLeaderboardService leaderboards)
    {
        var leaderboardId = OnlineAdminApiContract.RequireString(request, "LeaderboardId");
        var count = request.ReadNullableInt32("Count") ?? 10;
        var playerId = request.ReadNullableInt64("PlayerId") ?? 0;

        var boardResult = await leaderboards.ResolveBoardAsync(scope, leaderboardId, cancellationToken).ConfigureAwait(false);
        if (!boardResult.IsSuccess)
        {
            throw new OnlineServiceException(boardResult.Code, boardResult.Message);
        }

        var board = boardResult.Data;
        var response = new LeaderboardQueryResponse
        {
            LeaderboardId = board.LeaderboardId,
            SortOrder = board.SortOrder.ToString(),
            ScoreUpdatePolicy = board.ScoreUpdatePolicy.ToString(),
            Top = new List<LeaderboardEntryResponse>(),
        };

        var topResult = await leaderboards.GetTopAsync(scope, new OnlineLeaderboardTopQuery { LeaderboardId = leaderboardId, Count = count }, cancellationToken).ConfigureAwait(false);
        if (topResult.IsSuccess)
        {
            foreach (var view in topResult.Data.Entries)
            {
                response.Top.Add(new LeaderboardEntryResponse
                {
                    Rank = view.Rank,
                    PlayerId = view.Entry.PlayerId,
                    Score = view.Entry.Score,
                });
            }
        }

        if (playerId > 0)
        {
            // 附近窗口为尽力投影：玩家未上榜 / 榜单异常时省略，不阻断整体查询。
            var aroundResult = await leaderboards.GetAroundPlayerAsync(scope, new OnlineLeaderboardAroundQuery { LeaderboardId = leaderboardId, PlayerId = playerId, Before = 2, After = 2 }, cancellationToken).ConfigureAwait(false);
            if (aroundResult.IsSuccess)
            {
                response.AroundPlayer = new LeaderboardAroundResponse
                {
                    PlayerId = aroundResult.Data.PlayerId,
                    Rank = aroundResult.Data.Rank,
                    TotalCount = (int)aroundResult.Data.TotalCount,
                    Around = new List<LeaderboardEntryResponse>(),
                };
                foreach (var view in aroundResult.Data.Around)
                {
                    response.AroundPlayer.Around.Add(new LeaderboardEntryResponse
                    {
                        Rank = view.Rank,
                        PlayerId = view.Entry.PlayerId,
                        Score = view.Entry.Score,
                    });
                }
            }
        }

        return response;
    }

    /// <summary>
    /// 排行榜查询响应（JSON 投影）。
    /// </summary>
    public sealed class LeaderboardQueryResponse
    {
        /// <summary>获取或设置榜单标识。</summary>
        public string LeaderboardId { get; set; }

        /// <summary>获取或设置排序方向名（Descending/Ascending）。</summary>
        public string SortOrder { get; set; }

        /// <summary>获取或设置累计策略名（Best/Sum/Latest）。</summary>
        public string ScoreUpdatePolicy { get; set; }

        /// <summary>获取或设置 Top 条目。</summary>
        public List<LeaderboardEntryResponse> Top { get; set; } = new List<LeaderboardEntryResponse>();

        /// <summary>获取或设置玩家附近窗口（未请求或玩家未上榜为 null）。</summary>
        public LeaderboardAroundResponse AroundPlayer { get; set; }
    }

    /// <summary>
    /// 榜单条目响应（JSON 投影）。
    /// </summary>
    public sealed class LeaderboardEntryResponse
    {
        /// <summary>获取或设置名次。</summary>
        public int Rank { get; set; }

        /// <summary>获取或设置玩家标识。</summary>
        public long PlayerId { get; set; }

        /// <summary>获取或设置分数。</summary>
        public long Score { get; set; }
    }

    /// <summary>
    /// 玩家附近窗口响应（JSON 投影）。
    /// </summary>
    public sealed class LeaderboardAroundResponse
    {
        /// <summary>获取或设置玩家标识。</summary>
        public long PlayerId { get; set; }

        /// <summary>获取或设置玩家名次。</summary>
        public int Rank { get; set; }

        /// <summary>获取或设置榜单总条数。</summary>
        public int TotalCount { get; set; }

        /// <summary>获取或设置窗口条目（含玩家本人）。</summary>
        public List<LeaderboardEntryResponse> Around { get; set; } = new List<LeaderboardEntryResponse>();
    }
}
