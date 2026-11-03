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
using GameFrameX.Capability.Online.ServerLifecycle;
using GameFrameX.Capability.Online.ServerLifecycle.ServerMerge;
using GameFrameX.DataBase;
using GameFrameX.DataBase.Abstractions;
using GameFrameX.DataBase.Sqlite;

/// <summary>
/// 合服行键重写 CLI 入口（自托管）：装配 ServerLifecycle 合服服务（缺省 dry-run 先行），
/// GameDb 绑定经 <c>SERVER_MERGE_DB_PROVIDER</c>（sqlite）+ <c>SERVER_MERGE_DB_CONNECTION_STRING</c> 环境变量注入；
/// sqlite 模式下归并计划存储走 GameDb 持久化——一次进程 dry-run 留存报告，另一次进程 <c>--execute</c>
/// 可凭该报告通过执行门禁（跨进程可回读）。未配置数据库时以空端口 + 进程内存储运行（仅参数演练，
/// <c>--execute</c> 因报告随进程丢失而恒被门禁拒绝）；配置了不支持的 provider 值直接报错退出（不静默回退）。
/// </summary>
public static class Program
{
    /// <summary>
    /// CLI 入口（参数：<c>--target=&lt;serverId&gt; --sources=&lt;id1,id2,...&gt; [--execute]</c>；缺省 dry-run；
    /// <c>--register-open=&lt;id1,id2,...&gt;</c> 先登记开服锚点（sqlite 模式持久化，幂等保留首登开服时刻）后继续，
    /// 单独使用时登记完即退出）。
    /// </summary>
    /// <param name="args">命令行参数。</param>
    /// <returns>进程退出码。</returns>
    public static async Task<int> Main(string[] args)
    {
        var provider = Environment.GetEnvironmentVariable("SERVER_MERGE_DB_PROVIDER");
        var connectionString = Environment.GetEnvironmentVariable("SERVER_MERGE_DB_CONNECTION_STRING");
        var isSqliteProvider = string.Equals(provider, "sqlite", StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(provider) && !isSqliteProvider)
        {
            Console.WriteLine("ERROR: unsupported SERVER_MERGE_DB_PROVIDER '" + provider + "'. Supported: sqlite.");
            return 2;
        }

        if (isSqliteProvider)
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

        try
        {
            IOnlineServerCalendarStore calendarStore = isSqliteProvider
                ? new GameDbOnlineServerCalendarStore()
                : new InMemoryOnlineServerCalendarStore();
            string[] mergeArguments;
            try
            {
                mergeArguments = await RegisterOpenAnchorsAsync(args, calendarStore, isSqliteProvider).ConfigureAwait(false);
            }
            catch (ArgumentException exception)
            {
                Console.WriteLine("ERROR: " + exception.Message);
                return 2;
            }
            catch (Exception exception) when (exception is InvalidOperationException)
            {
                Console.WriteLine("ERROR: " + exception.Message);
                return 3;
            }

            if (mergeArguments == null)
            {
                return 0;
            }

            IOnlineServerMergeRowRewriter rowRewriter = isSqliteProvider
                ? new GameDbOnlineServerMergeRowRewriter()
                : new NullOnlineServerMergeRowRewriter();
            IOnlineServerMergeStore mergeStore = isSqliteProvider
                ? new GameDbOnlineServerMergeStore()
                : new InMemoryOnlineServerMergeStore();
            var participants = new List<IServerMergeParticipant>
            {
                new OnlineServerAnchorMergeParticipant(calendarStore),
                new OnlineLeaderboardSealMergeParticipant(new NullOnlineLeaderboardSealTarget()),
                new OnlineInFlightMatchSettlementMergeParticipant(new NullOnlineInFlightMatchSettler()),
                new OnlinePlayerRouteMergeParticipant(new NullOnlinePlayerRouteRewriter()),
                new OnlineServerRowKeyRewriteParticipant(rowRewriter, 500),
            };
            var mergeService = new OnlineServerMergeService(mergeStore, participants, new OnlineServerMergeOptions());
            return await OnlineServerMergeCli.RunAsync(mergeArguments, mergeService, Console.Out).ConfigureAwait(false);
        }
        finally
        {
            if (isSqliteProvider)
            {
                await GameDb.CloseAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// 处理 <c>--register-open=&lt;id1,id2,...&gt;</c>：按当前时刻登记开服锚点（幂等）。
    /// </summary>
    /// <param name="args">原始命令行参数。</param>
    /// <param name="calendarStore">开服锚点存储。</param>
    /// <param name="persisted">锚点是否持久化（sqlite 模式 true；进程内存储的登记不跨进程）。</param>
    /// <returns>剔除 register-open 后的合服参数；本次调用只登记不开融合服时返回 null（调用方直接退出 0）。</returns>
    /// <exception cref="ArgumentException">register-open 的 id 段非法（非正整数）时抛出（按参数错误 exit 2 收口）。</exception>
    /// <exception cref="InvalidOperationException">锚点登记被拒绝时抛出（按业务拒绝 exit 3 收口）。</exception>
    private static async Task<string[]> RegisterOpenAnchorsAsync(string[] args, IOnlineServerCalendarStore calendarStore, bool persisted)
    {
        var mergeArguments = new List<string>();
        var registerOpenSegments = new List<string>();
        foreach (var argument in args ?? Array.Empty<string>())
        {
            if (argument != null && argument.StartsWith("--register-open=", StringComparison.Ordinal))
            {
                registerOpenSegments.AddRange(argument.Substring("--register-open=".Length).Split(','));
            }
            else
            {
                mergeArguments.Add(argument);
            }
        }

        if (registerOpenSegments.Count == 0)
        {
            return mergeArguments.ToArray();
        }

        var registerOpenServerIds = new List<long>();
        foreach (var segment in registerOpenSegments)
        {
            if (!long.TryParse(segment.Trim(), out var serverId) || serverId <= 0)
            {
                throw new ArgumentException("invalid --register-open segment '" + segment + "'. Expected: --register-open=<id1,id2,...> with positive integer ids.");
            }

            registerOpenServerIds.Add(serverId);
        }

        var openedAtUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        foreach (var serverId in registerOpenServerIds)
        {
            await calendarStore.TryRegisterAsync(new OnlineServerAnchor { ServerId = serverId, OpenedAtUnixMilliseconds = openedAtUnixMilliseconds }).ConfigureAwait(false);
            Console.WriteLine("Registered open anchor for server " + serverId + (persisted ? "." : " (in-memory store, not persisted across runs)."));
        }

        return mergeArguments.Count == 0 ? null : mergeArguments.ToArray();
    }
}
