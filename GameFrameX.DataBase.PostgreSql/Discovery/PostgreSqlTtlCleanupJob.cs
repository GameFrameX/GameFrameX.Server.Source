// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规及开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes and liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using Npgsql;

namespace GameFrameX.NetWork.RemoteMessaging.Discovery;

/// <summary>
/// PostgreSQL TTL 清理 job（C166 T8 AC-4：替代 Mongo TTL 索引的过期行清除）。
/// </summary>
/// <remarks>
/// The PostgreSQL TTL cleanup job (C166 T8, AC-4): replaces the Mongo TTL index.
/// PostgreSQL has no TTL index, so a background timer deletes expired rows on a
/// fixed period (5 s default): <c>server_heartbeat</c> rows whose
/// <c>last_heartbeat</c> is older than 15 s, and <c>player_route</c> rows whose
/// <c>last_seen_at</c> is older than 30 days. Timing semantics versus Mongo are
/// deliberately relaxed (documented): a row is removed within one cleanup period
/// after expiry instead of at the exact expire-after instant, so the watcher's
/// <c>Evicted</c> event may arrive up to one period late — liveness itself is
/// unaffected because the watcher's three-period staleness check (the primary
/// signal) never depends on row disappearance. Deployments preferring external
/// scheduling can skip this job and install the pg_cron script instead
/// (<c>PostgreSqlTtlCleanup.cron.sql</c>).
/// </remarks>
internal sealed class PostgreSqlTtlCleanupJob : IDisposable
{
    /// <summary>
    /// 缺省清理周期（5s）。
    /// </summary>
    /// <remarks>
    /// The default cleanup period (5 s).
    /// </remarks>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 过期行清理 SQL（两条 DELETE，与 Mongo TTL 窗口同值：心跳 15s / 玩家路由 30 天）。
    /// </summary>
    /// <remarks>
    /// The cleanup SQL (both DELETEs; the windows match the Mongo TTL indexes: 15 s heartbeats, 30-day player routes).
    /// </remarks>
    internal const string CleanupSql = @"
DELETE FROM server_heartbeat WHERE last_heartbeat < now() - interval '15 seconds';
DELETE FROM player_route WHERE last_seen_at < now() - interval '30 days';";

    /// <summary>
    /// 数据源（池化）。
    /// </summary>
    /// <remarks>
    /// The pooled data source.
    /// </remarks>
    private readonly NpgsqlDataSource _dataSource;

    /// <summary>
    /// 清理周期。
    /// </summary>
    /// <remarks>
    /// The cleanup period.
    /// </remarks>
    private readonly TimeSpan _interval;

    /// <summary>
    /// 后台计时器。
    /// </summary>
    /// <remarks>
    /// The background timer.
    /// </remarks>
    private Timer _timer;

    /// <summary>
    /// 串行化清理轮次（上一轮未完成时跳过本轮，避免堆积）。
    /// </summary>
    /// <remarks>
    /// Serializes cleanup rounds (a round is skipped while the previous one is still running).
    /// </remarks>
    private int _running;

    /// <summary>
    /// 初始化清理 job。
    /// </summary>
    /// <remarks>
    /// Initializes the job; call <see cref="Start"/> to begin (an immediate first
    /// pass, then the periodic loop). <paramref name="interval"/> is configurable
    /// for tests (shrunk periods).
    /// </remarks>
    /// <param name="dataSource">控制库数据源 / The control-database data source</param>
    /// <param name="interval">清理周期；缺省 5s / The cleanup period; defaults to 5 s</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="dataSource"/> 为 null 时抛出 / Thrown when <paramref name="dataSource"/> is null</exception>
    public PostgreSqlTtlCleanupJob(NpgsqlDataSource dataSource, TimeSpan? interval = null)
    {
        ArgumentNullException.ThrowIfNull(dataSource, nameof(dataSource));

        _dataSource = dataSource;
        _interval = interval ?? DefaultInterval;
    }

    /// <summary>
    /// 启动清理 job：立即执行一轮 + 固定周期循环。
    /// </summary>
    /// <remarks>
    /// Starts the job: one immediate pass, then a timer-driven loop. Failures are
    /// logged and retried on the next tick (a missed pass only delays expiry-based
    /// row removal, never liveness).
    /// </remarks>
    public void Start()
    {
        if (_timer != null)
        {
            return;
        }

        _ = RunOnceAsync();
        _timer = new Timer(static state => _ = ((PostgreSqlTtlCleanupJob)state).RunOnceAsync(), this, _interval, _interval);
    }

    /// <summary>
    /// 执行一轮清理（跳过重入）。
    /// </summary>
    /// <remarks>
    /// Runs one cleanup pass (re-entrant calls are skipped).
    /// </remarks>
    /// <returns>异步任务 / Async task</returns>
    private async Task RunOnceAsync()
    {
        if (Interlocked.Exchange(ref _running, 1) != 0)
        {
            return;
        }

        try
        {
            await PostgreSqlEndpointRegistry.ExecuteNonQueryAsync(_dataSource, CleanupSql, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            LogHelper.Error(exception, "[PostgreSqlTtlCleanupJob] TTL cleanup pass failed; will retry next interval");
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    /// <summary>
    /// 停止并释放清理 job。
    /// </summary>
    /// <remarks>
    /// Stops and disposes the job.
    /// </remarks>
    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }
}