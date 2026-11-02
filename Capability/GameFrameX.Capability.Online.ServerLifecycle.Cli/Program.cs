// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and other rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Capability.Online.ServerLifecycle.Cli;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.Persistence;
using GameFrameX.Capability.Online.ServerLifecycle.ServerMerge;
using GameFrameX.DataBase;
using GameFrameX.DataBase.Abstractions;
using GameFrameX.DataBase.Sqlite;

/// <summary>
/// 合服行键重写 CLI 入口（自托管）：装配 ServerLifecycle 合服服务（缺省 dry-run 先行），
/// GameDb 绑定经 <c>SERVER_MERGE_DB_PROVIDER</c>（sqlite）+ <c>SERVER_MERGE_DB_CONNECTION_STRING</c> 环境变量注入；
/// 未配置数据库时以空端口运行（报告恒 0，仅供参数演练）。
/// </summary>
public static class Program
{
    /// <summary>
    /// CLI 入口（参数：<c>--target=&lt;serverId&gt; --sources=&lt;id1,id2,...&gt; [--execute]</c>；缺省 dry-run）。
    /// </summary>
    /// <param name="args">命令行参数。</param>
    /// <returns>进程退出码。</returns>
    public static async Task<int> Main(string[] args)
    {
        var provider = Environment.GetEnvironmentVariable("SERVER_MERGE_DB_PROVIDER");
        var connectionString = Environment.GetEnvironmentVariable("SERVER_MERGE_DB_CONNECTION_STRING");
        if (string.Equals(provider, "sqlite", StringComparison.OrdinalIgnoreCase))
        {
            var opened = await GameDb.InitAsync<SqliteDbService>(new DbOptions
            {
                ConnectionString = string.IsNullOrWhiteSpace(connectionString) ? "Data Source=server_merge_cli.db" : connectionString,
                Name = "server-merge-cli",
            }).ConfigureAwait(false);
            if (!opened)
            {
                Console.WriteLine("ERROR: GameDb sqlite initialization failed.");
                return 3;
            }
        }

        IOnlineServerMergeRowRewriter rowRewriter = string.Equals(provider, "sqlite", StringComparison.OrdinalIgnoreCase)
            ? new GameDbOnlineServerMergeRowRewriter()
            : new NullOnlineServerMergeRowRewriter();
        var participants = new List<IServerMergeParticipant>
        {
            new OnlineServerAnchorMergeParticipant(new InMemoryOnlineServerCalendarStore()),
            new OnlineLeaderboardSealMergeParticipant(new NullOnlineLeaderboardSealTarget()),
            new OnlineInFlightMatchSettlementMergeParticipant(new NullOnlineInFlightMatchSettler()),
            new OnlinePlayerRouteMergeParticipant(new NullOnlinePlayerRouteRewriter()),
            new OnlineServerRowKeyRewriteParticipant(rowRewriter, 500),
        };
        var mergeService = new OnlineServerMergeService(new InMemoryOnlineServerMergeStore(), participants, new OnlineServerMergeOptions());
        var exitCode = await OnlineServerMergeCli.RunAsync(args, mergeService, Console.Out).ConfigureAwait(false);

        if (string.Equals(provider, "sqlite", StringComparison.OrdinalIgnoreCase))
        {
            await GameDb.CloseAsync().ConfigureAwait(false);
        }

        return exitCode;
    }
}
