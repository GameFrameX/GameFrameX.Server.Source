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
//   shall be borne by the developer; the project organization and contributors assume no responsibility.
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
/// PostgreSQL 心跳写侧（C166 T8：与 <see cref="MongoEndpointRegistry"/> 逐字段对齐的平行实现）。
/// </summary>
/// <remarks>
/// The PostgreSQL heartbeat writer (C166 T8): a field-by-field parallel of
/// <see cref="MongoEndpointRegistry"/> over the <c>server_heartbeat</c> table
/// (D18 full-name contract; one row per live instance, primary key
/// <c>instance_id</c>). Start upserts the current state (Booting) immediately,
/// then a full-row upsert every heartbeat interval (5 s default) — always the
/// complete latest state, so after a database outage every field recovers with
/// the next successful write. Graceful exit writes the terminal Stopped state
/// (idempotent) instead of waiting for the TTL replacement: the
/// <see cref="PostgreSqlTtlCleanupJob"/> (or the optional pg_cron script) removes
/// stale rows, since PostgreSQL has no native TTL index; the Evicted timing of
/// the watcher is therefore relaxed to within one cleanup period (AC-4).
/// </remarks>
public sealed class PostgreSqlEndpointRegistry : IDisposable
{
    /// <summary>
    /// 缺省心跳间隔（5s，与 Mongo 版同值）。
    /// </summary>
    /// <remarks>
    /// The default heartbeat interval (5 s, same value as the Mongo implementation).
    /// </remarks>
    public static readonly TimeSpan DefaultHeartbeatInterval = TimeSpan.FromSeconds(5);

    /// <summary>
    /// server_heartbeat 表名（D18 全名约定；稳定契约）。
    /// </summary>
    /// <remarks>
    /// The server_heartbeat table name (D18 no-abbreviation rule; a stable contract).
    /// </remarks>
    public const string HeartbeatTableName = "server_heartbeat";

    /// <summary>
    /// TTL 保存时长（15s，与 Mongo TTL 索引同值；由清理 job 兑现）。
    /// </summary>
    /// <remarks>
    /// The TTL expire-after window (15 s, same value as the Mongo TTL index; enforced by the cleanup job instead of an index).
    /// </remarks>
    public const long HeartbeatTimeToLiveSeconds = 15;

    /// <summary>
    /// 建表与索引 DDL（幂等：CREATE TABLE / INDEX IF NOT EXISTS）。
    /// </summary>
    /// <remarks>
    /// The idempotent schema DDL for the heartbeat table (plus the last_heartbeat index used by the cleanup job scans).
    /// </remarks>
    internal const string EnsureHeartbeatSchemaSql = @"
CREATE TABLE IF NOT EXISTS server_heartbeat (
    instance_id text PRIMARY KEY,
    role text NOT NULL,
    advertise_endpoint text NOT NULL,
    status text NOT NULL,
    load integer NOT NULL,
    address_kind text NOT NULL,
    incarnation bigint NOT NULL,
    last_heartbeat timestamptz NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_server_heartbeat_last_heartbeat ON server_heartbeat (last_heartbeat);";

    /// <summary>
    /// 全行 upsert（ON CONFLICT (instance_id) DO UPDATE，语义等同 Mongo ReplaceOne upsert）。
    /// </summary>
    /// <remarks>
    /// The full-row upsert (ON CONFLICT (instance_id) DO UPDATE; semantically equal to the Mongo ReplaceOne upsert).
    /// </remarks>
    private const string UpsertHeartbeatSql = @"
INSERT INTO server_heartbeat (instance_id, role, advertise_endpoint, status, load, address_kind, incarnation, last_heartbeat)
VALUES ($1, $2, $3, $4, $5, $6, $7, $8)
ON CONFLICT (instance_id) DO UPDATE SET
    role = EXCLUDED.role,
    advertise_endpoint = EXCLUDED.advertise_endpoint,
    status = EXCLUDED.status,
    load = EXCLUDED.load,
    address_kind = EXCLUDED.address_kind,
    incarnation = EXCLUDED.incarnation,
    last_heartbeat = EXCLUDED.last_heartbeat;";

    /// <summary>
    /// 数据源（池化）。
    /// </summary>
    /// <remarks>
    /// The pooled data source.
    /// </remarks>
    private readonly NpgsqlDataSource _dataSource;

    /// <summary>
    /// 本进程实例身份。
    /// </summary>
    /// <remarks>
    /// This process's instance identity.
    /// </remarks>
    private readonly InstanceDescriptor _selfDescriptor;

    /// <summary>
    /// 心跳间隔。
    /// </summary>
    /// <remarks>
    /// The heartbeat interval.
    /// </remarks>
    private readonly TimeSpan _heartbeatInterval;

    /// <summary>
    /// 心跳循环取消令牌源。
    /// </summary>
    /// <remarks>
    /// The heartbeat loop cancellation token source.
    /// </remarks>
    private readonly CancellationTokenSource _loopCancellation = new();

    /// <summary>
    /// 心跳循环任务。
    /// </summary>
    /// <remarks>
    /// The heartbeat loop task.
    /// </remarks>
    private Task _loopTask;

    /// <summary>
    /// 是否已写终态 Stopped（幂等守卫）。
    /// </summary>
    /// <remarks>
    /// Whether the terminal Stopped state was already written (idempotent guard).
    /// </remarks>
    private int _stoppedWritten;

    /// <summary>
    /// 当前宣告的实例状态（Booting → MarkActive 后 Active）。
    /// </summary>
    /// <remarks>
    /// The status currently announced (Booting until MarkActiveAsync, Active afterwards).
    /// </remarks>
    private volatile int _currentStatus = (int)InstanceStatus.Booting;

    /// <summary>
    /// 初始化 PostgreSQL 心跳写侧。
    /// </summary>
    /// <remarks>
    /// Initializes the writer. Call <see cref="StartAsync"/> to begin heartbeating.
    /// </remarks>
    /// <param name="dataSource">控制库数据源 / The control-database data source</param>
    /// <param name="selfDescriptor">本进程实例身份 / This process's instance identity</param>
    /// <param name="heartbeatInterval">心跳间隔；缺省 5s / The heartbeat interval; defaults to 5 s</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="dataSource"/> 为 null 时抛出 / Thrown when <paramref name="dataSource"/> is null</exception>
    /// <exception cref="ArgumentNullException">当 <paramref name="selfDescriptor"/> 为 null 时抛出 / Thrown when <paramref name="selfDescriptor"/> is null</exception>
    public PostgreSqlEndpointRegistry(NpgsqlDataSource dataSource, InstanceDescriptor selfDescriptor, TimeSpan? heartbeatInterval = null)
    {
        ArgumentNullException.ThrowIfNull(dataSource, nameof(dataSource));
        ArgumentNullException.ThrowIfNull(selfDescriptor, nameof(selfDescriptor));

        _dataSource = dataSource;
        _heartbeatInterval = heartbeatInterval ?? DefaultHeartbeatInterval;
        _selfDescriptor = selfDescriptor;
    }

    /// <summary>
    /// 从环境变量构建本进程实例身份（与 Mongo 版共用同一 D12 引导逻辑，单一事实源）。
    /// </summary>
    /// <remarks>
    /// Builds this process's instance identity from the D12 bootstrap environment
    /// variables — delegated to <see cref="AdvertiseEndpointEnvironment.CreateSelfDescriptorFromEnvironment"/>
    /// (a pure environment function, storage-agnostic) so both adapters share one
    /// source of truth. Returns null when the advertise port is not configured;
    /// callers treat that as "no cross-process identity" and skip the write side.
    /// </remarks>
    /// <param name="roleName">承载的 Role 名 / The hosted role name</param>
    /// <returns>实例身份；未配置广播端口时为 null / The identity, or null when the advertise port is not configured</returns>
    public static InstanceDescriptor CreateSelfDescriptorFromEnvironment(string roleName)
    {
        return AdvertiseEndpointEnvironment.CreateSelfDescriptorFromEnvironment(roleName);
    }

    /// <summary>
    /// 幂等建表 / 建索引（供装配层与测试复用）。
    /// </summary>
    /// <remarks>
    /// Idempotently creates the heartbeat table and its index (shared with the runtime wiring and tests).
    /// </remarks>
    /// <param name="dataSource">数据源 / The data source</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>异步任务 / Async task</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="dataSource"/> 为 null 时抛出 / Thrown when <paramref name="dataSource"/> is null</exception>
    public static Task EnsureSchemaAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataSource, nameof(dataSource));
        return ExecuteNonQueryAsync(dataSource, EnsureHeartbeatSchemaSql, cancellationToken);
    }

    /// <summary>
    /// 启动心跳写侧：幂等建表 + 立即写入 Booting + 起后台心跳循环。
    /// </summary>
    /// <remarks>
    /// Starts the writer: creates the schema (idempotent), writes the current
    /// status (Booting) immediately so the topology can see this process within
    /// one poll round, then runs the background heartbeat loop. The owning
    /// startup flow must call <see cref="MarkActiveAsync"/> once truly ready.
    /// Also subscribes to <see cref="AppDomain.ProcessExit"/> as the best-effort
    /// Stopped safety net. Unlike Mongo (TTL index), expired-row cleanup is the
    /// <see cref="PostgreSqlTtlCleanupJob"/>'s responsibility — started by the
    /// discovery runtime alongside this registry.
    /// </remarks>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>异步任务 / Async task</returns>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSchemaAsync(_dataSource, cancellationToken).ConfigureAwait(false);
        await UpsertHeartbeatAsync((InstanceStatus)_currentStatus, CancellationToken.None).ConfigureAwait(false);
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        _loopTask = Task.Run(() => HeartbeatLoopAsync(_loopCancellation.Token));
    }

    /// <summary>
    /// 标记实例就绪：心跳状态由 Booting 切换为 Active 并立即写入。
    /// </summary>
    /// <remarks>
    /// Marks the instance as ready: flips the announced status to Active and writes it immediately.
    /// Repeat calls are harmless.
    /// </remarks>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>异步任务 / Async task</returns>
    public Task MarkActiveAsync(CancellationToken cancellationToken = default)
    {
        _currentStatus = (int)InstanceStatus.Active;
        return UpsertHeartbeatAsync(InstanceStatus.Active, cancellationToken);
    }

    /// <summary>
    /// 停止心跳写侧并写终态 Stopped（幂等）。
    /// </summary>
    /// <remarks>
    /// Stops the loop and writes the terminal Stopped state so watchers drop this
    /// instance immediately. Idempotent.
    /// </remarks>
    /// <returns>异步任务 / Async task</returns>
    public async Task StopAsync()
    {
        _loopCancellation.Cancel();
        if (_loopTask != null)
        {
            try
            {
                await _loopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        await WriteStoppedAsync().ConfigureAwait(false);
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
    }

    /// <summary>
    /// 释放资源（未写终态时尽力同步写 Stopped）。
    /// </summary>
    /// <remarks>
    /// Disposes and, when Stopped was not written yet, writes it synchronously as a best effort.
    /// </remarks>
    public void Dispose()
    {
        try
        {
            _loopCancellation.Cancel();
            WriteStoppedAsync().GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // 尽力而为：进程退出路径上 PostgreSQL 不可达时无法补写终态，交给 TTL 清理 job 兜底清除。
        }
        finally
        {
            _loopCancellation.Dispose();
        }
    }

    /// <summary>
    /// 心跳循环：固定间隔全行 upsert。
    /// </summary>
    /// <remarks>
    /// The heartbeat loop: a full-row upsert every interval; a database outage leaves the
    /// row stale (the watcher marks the instance Offline after the three-period threshold),
    /// and recovery is simply the next successful full upsert.
    /// </remarks>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>异步任务 / Async task</returns>
    private async Task HeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_heartbeatInterval, cancellationToken).ConfigureAwait(false);
                await UpsertHeartbeatAsync((InstanceStatus)_currentStatus, CancellationToken.None).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception exception)
            {
                LogHelper.Error(exception, "[PostgreSqlEndpointRegistry] heartbeat upsert failed for instance {instanceId}; will retry next interval", _selfDescriptor.InstanceId);
            }
        }
    }

    /// <summary>
    /// 进程退出钩子：尽力同步写 Stopped。
    /// </summary>
    /// <remarks>
    /// The ProcessExit hook: writes Stopped synchronously as a best effort.
    /// </remarks>
    /// <param name="sender">事件源 / The event source</param>
    /// <param name="eventArguments">事件参数 / The event arguments</param>
    private void OnProcessExit(object sender, EventArgs eventArguments)
    {
        try
        {
            WriteStoppedAsync().GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // 进程退出路径上 PostgreSQL 不可达时无法补写终态，交给 TTL 清理 job 兜底清除。
        }
    }

    /// <summary>
    /// 写终态 Stopped（幂等）。
    /// </summary>
    /// <remarks>
    /// Writes the terminal Stopped state (idempotent; the first writer wins).
    /// </remarks>
    /// <returns>异步任务 / Async task</returns>
    private Task WriteStoppedAsync()
    {
        return UpsertHeartbeatAsync(InstanceStatus.Stopped, CancellationToken.None);
    }

    /// <summary>
    /// 全行 upsert 本进程心跳（终态守卫与 Mongo 版一致：Stopped 只写一次、终态后不回退）。
    /// </summary>
    /// <remarks>
    /// Upserts the full heartbeat row with the same terminal-state guard as the Mongo
    /// implementation: Stopped is written once, and in-flight Booting/Active heartbeats
    /// never overwrite the terminal state.
    /// </remarks>
    /// <param name="status">本次写入状态 / The status to write</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>异步任务 / Async task</returns>
    private async Task UpsertHeartbeatAsync(InstanceStatus status, CancellationToken cancellationToken)
    {
        if (status == InstanceStatus.Stopped)
        {
            // 终态幂等：首个 Stopped 写入胜出，后续 Stopped 调用直接返回。
            if (Interlocked.Exchange(ref _stoppedWritten, 1) != 0)
            {
                return;
            }
        }
        else if (Volatile.Read(ref _stoppedWritten) != 0)
        {
            // 终态后不再回退。
            return;
        }

        await using var command = _dataSource.CreateCommand(UpsertHeartbeatSql);
        command.Parameters.AddWithValue(_selfDescriptor.InstanceId);
        command.Parameters.AddWithValue(_selfDescriptor.Role);
        command.Parameters.AddWithValue(_selfDescriptor.AdvertiseEndpoint);
        command.Parameters.AddWithValue(status.ToString());
        command.Parameters.AddWithValue(0); // 负载自报：固定 0，接真实指标源属后续 change。
        command.Parameters.AddWithValue(_selfDescriptor.AddressKind.ToString());
        command.Parameters.AddWithValue(_selfDescriptor.Incarnation);
        command.Parameters.AddWithValue(DateTime.UtcNow);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 执行无参 DDL / SQL。
    /// </summary>
    /// <remarks>
    /// Executes a parameter-less DDL statement.
    /// </remarks>
    internal static async Task ExecuteNonQueryAsync(NpgsqlDataSource dataSource, string sql, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}